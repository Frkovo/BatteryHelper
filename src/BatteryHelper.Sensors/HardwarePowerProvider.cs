using BatteryHelper.Core;
using LibreHardwareMonitor.Hardware;

namespace BatteryHelper.Sensors;

public sealed class HardwarePowerProvider : IDisposable
{
    private Computer? _computer;
    private readonly IntelTelemetryProvider _intel = new();
    private string? _openError;

    public (PowerReading Cpu, PowerReading Gpu, PowerReading Input, PowerReading Package, PowerReading? Integrated) Read(DateTimeOffset now)
    {
        if (_computer is null) {
            try {
                _computer = new Computer {
                    IsCpuEnabled = true, IsGpuEnabled = true, IsPsuEnabled = true,
                    IsMotherboardEnabled = false, IsMemoryEnabled = false, IsStorageEnabled = false,
                    IsNetworkEnabled = false, IsControllerEnabled = false, IsPowerMonitorEnabled = false
                };
                _computer.Open(); _openError = null;
            } catch (Exception error) {
                _openError = $"硬件采集初始化失败（{error.GetType().Name}）。";
                _computer?.Close(); _computer = null;
            }
        }
        var defaultReason = _openError ?? "未找到可用的功率传感器，请检查 PawnIO 驱动及设备支持情况。";
        var cpu = PowerReading.Missing("LibreHardwareMonitor", "CPU", defaultReason, now);
        var packageReading = PowerReading.Missing("LibreHardwareMonitor", "原始 CPU 封装", defaultReason, now);
        PowerReading? integrated = null;
        var gpu = PowerReading.Missing("LibreHardwareMonitor", "GPU", defaultReason, now);
        var input = PowerReading.Missing("整机输入传感器", "整机输入", "设备未提供明确覆盖整机的输入功率传感器。", now);
        if (_computer is null) return (cpu, gpu, input, packageReading, integrated);
        var hasIntelIntegratedGpu = _computer.Hardware.Any(h => h.Identifier.ToString().StartsWith("/gpu-intel-integrated/", StringComparison.Ordinal));
        var selectedGpuIsIntegrated = false;

        foreach (var hardware in _computer.Hardware) {
            try { hardware.Update(); }
            catch { continue; }
            var power = hardware.Sensors.Where(s => s.SensorType == SensorType.Power && s.Value is not null).ToArray();
            if (hardware.HardwareType == HardwareType.Cpu) {
                var isIntel = hardware.Identifier.ToString().StartsWith("/intelcpu/", StringComparison.Ordinal);
                var package = power.FirstOrDefault(s => s.Name == "CPU Package") ??
                    power.FirstOrDefault(s => s.Name == "Package");
                packageReading = PowerReading.Missing($"LibreHardwareMonitor · {hardware.Name} · CPU Package", "原始 CPU 封装", defaultReason, now);
                if (package?.Value is { } value)
                    packageReading = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {package.Name}", "原始 CPU 封装", now);
                // This counter is on the CPU hardware object, not whichever GPU happens to be displayed.
                // PP1 (0x641) and PKG (0x611) are refreshed together by IntelCpu.Update().
                var graphics = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name == "CPU Graphics");
                integrated = null;
                if (isIntel && (graphics is not null || hasIntelIntegratedGpu)) {
                    var source = $"LibreHardwareMonitor · {hardware.Name} · CPU Graphics (RAPL PP1)";
                    integrated = graphics?.Value is { } graphicsWatts
                        ? PowerReading.Measured(graphicsWatts, source, "同一 CPU 封装的核显域（RAPL PP1）", now)
                        : PowerReading.Missing(source, "同一 CPU 封装的核显域（RAPL PP1）", "核显域尚未提供有效的同步功率读数。", now);
                    cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(packageReading, integrated, now);
                } else {
                    cpu = packageReading with { Coverage = isIntel
                        ? "CPU 封装（未检测到可用核显域）"
                        : "CPU 封装（此设备的核显包含关系未确认，未进行推测性扣除）" };
                }
            } else if (hardware.HardwareType is HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.GpuAmd) {
                // Pick one board/package sensor, never add overlapping GPU core and board readings.
                var selected = power.FirstOrDefault(s => s.Name == "GPU Package") ??
                    power.FirstOrDefault(s => s.Name == "GPU Power") ??
                    power.FirstOrDefault(s => s.Name == "GPU Total") ??
                    power.FirstOrDefault(s => s.Name == "GPU Core");
                if (selected?.Value is { } value && gpu.Status != ReadingStatus.Available) {
                    gpu = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {selected.Name}", $"GPU · {selected.Name}", now);
                    selectedGpuIsIntegrated = hardware.Identifier.ToString().StartsWith("/gpu-intel-integrated/", StringComparison.Ordinal);
                }
            } else if (hardware.HardwareType == HardwareType.Psu) {
                // Only a known PSU input channel qualifies; output rails and CPU Platform are not charger input.
                var total = power.FirstOrDefault(s => s.Name is "Total Input" or "Total Input Power" or "Input Power");
                if (total?.Value is { } value)
                    input = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {total.Name}", "电源整机输入", now);
            }
        }
        if (integrated is { Status: ReadingStatus.Available } && (selectedGpuIsIntegrated ||
            (hasIntelIntegratedGpu && gpu.Status != ReadingStatus.Available)))
            gpu = integrated with { Coverage = "核显功率（与 CPU 扣除项相同的 RAPL PP1 读数）" };
        if (gpu.Status != ReadingStatus.Available) gpu = _intel.ReadIntegrated(now);
        return (cpu, gpu, input, packageReading, integrated);
    }

    public void Dispose()
    {
        _intel.Dispose();
        _computer?.Close(); _computer = null;
    }
}
