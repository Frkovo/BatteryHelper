using BatteryHelper.Core;
using LibreHardwareMonitor.Hardware;

namespace BatteryHelper.Sensors;

public sealed class HardwarePowerProvider : IDisposable
{
    private Computer? _computer;
    private readonly IntelTelemetryProvider _intel = new();
    private string? _openError;

    public (PowerReading Cpu, PowerReading Gpu, PowerReading Input) Read(DateTimeOffset now)
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
        var cpu = PowerReading.Missing("LibreHardwareMonitor", "CPU 封装（范围可能包含核显）", defaultReason, now);
        var gpu = PowerReading.Missing("LibreHardwareMonitor", "GPU", defaultReason, now);
        var input = PowerReading.Missing("整机输入传感器", "整机输入", "设备未提供明确覆盖整机的输入功率传感器。", now);
        if (_computer is null) return (cpu, gpu, input);

        foreach (var hardware in _computer.Hardware) {
            try { hardware.Update(); }
            catch { continue; }
            var power = hardware.Sensors.Where(s => s.SensorType == SensorType.Power && s.Value is not null).ToArray();
            if (hardware.HardwareType == HardwareType.Cpu) {
                var package = power.FirstOrDefault(s => s.Name == "CPU Package") ??
                    power.FirstOrDefault(s => s.Name == "Package");
                if (package?.Value is { } value)
                    cpu = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {package.Name}", "CPU 封装（范围可能包含核显）", now);
            } else if (hardware.HardwareType is HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.GpuAmd) {
                // Pick one board/package sensor, never add overlapping GPU core and board readings.
                var selected = power.FirstOrDefault(s => s.Name == "GPU Package") ??
                    power.FirstOrDefault(s => s.Name == "GPU Power") ??
                    power.FirstOrDefault(s => s.Name == "GPU Total") ??
                    power.FirstOrDefault(s => s.Name == "GPU Core");
                if (selected?.Value is { } value && gpu.Status != ReadingStatus.Available)
                    gpu = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {selected.Name}", $"GPU · {selected.Name}", now);
            } else if (hardware.HardwareType == HardwareType.Psu) {
                // Only a known PSU input channel qualifies; output rails and CPU Platform are not charger input.
                var total = power.FirstOrDefault(s => s.Name is "Total Input" or "Total Input Power" or "Input Power");
                if (total?.Value is { } value)
                    input = PowerReading.Measured(value, $"LibreHardwareMonitor · {hardware.Name} · {total.Name}", "电源整机输入", now);
            }
        }
        if (gpu.Status != ReadingStatus.Available) gpu = _intel.ReadIntegrated(now);
        return (cpu, gpu, input);
    }

    public void Dispose()
    {
        _intel.Dispose();
        _computer?.Close(); _computer = null;
    }
}
