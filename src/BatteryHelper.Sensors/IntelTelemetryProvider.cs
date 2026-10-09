using System.Runtime.InteropServices;
using BatteryHelper.Core;
using BatteryHelper.Sensors.Interop;

namespace BatteryHelper.Sensors;

public sealed class IntelTelemetryProvider : IDisposable
{
    private readonly Dictionary<int, EnergyCounter> _counters = [];
    private IntelGcl.ctl_device_adapter_handle_t[]? _handles;
    private string _reason = "Intel 驱动未提供 GPU 能量遥测。";

    public PowerReading ReadIntegrated(DateTimeOffset now)
    {
        try {
            _handles ??= IntelGcl.GetDeviceHandles();
            for (var index = 0; index < _handles.Length; index++) {
                var properties = new IntelGcl.ctl_device_adapter_properties_t {
                    Size = (uint)Marshal.SizeOf<IntelGcl.ctl_device_adapter_properties_t>(), Version = 2,
                    name = "", reserved = new byte[IntelGcl.CTL_MAX_RESERVED_SIZE]
                };
                if (IntelGcl.ctlGetDeviceProperties(_handles[index], ref properties) != 0 ||
                    properties.device_type != IntelGcl.ctl_device_type_t.CTL_DEVICE_TYPE_GRAPHICS ||
                    (properties.graphics_adapter_properties & 1) == 0) continue;
                var telemetry = new IntelGcl.ctl_power_telemetry_t {
                    Size = (uint)Marshal.SizeOf<IntelGcl.ctl_power_telemetry_t>(), Version = 1,
                    psu = new IntelGcl.ctl_psu_info_t[IntelGcl.CTL_PSU_COUNT],
                    fanSpeed = new IntelGcl.ctl_oc_telemetry_item_t[IntelGcl.CTL_FAN_COUNT]
                };
                var code = IntelGcl.ctlPowerTelemetryGet(_handles[index], ref telemetry);
                if (code != 0) { _reason = $"Intel 遥测接口不可用（0x{code:X}）。"; Reset(index); continue; }
                var energy = ReadNumber(telemetry.gpuEnergyCounter, IntelGcl.ctl_units_t.CTL_UNITS_ENERGY_JOULES);
                var time = ReadNumber(telemetry.timeStamp, IntelGcl.ctl_units_t.CTL_UNITS_TIME_SECONDS);
                if (energy is null || time is null) { _reason = "Intel 驱动没有暴露受支持的 GPU 能量／时间计数器。"; Reset(index); continue; }
                if (!_counters.TryGetValue(index, out var counter)) _counters[index] = counter = new();
                if (counter.Sample(energy.Value, time.Value) is { } watts)
                    return PowerReading.Measured(watts, $"Intel IGCL · {properties.name}", "集成 GPU 能量计数器", now);
                _reason = "GPU 能量计数器正在建立采样基线。";
            }
        } catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or SEHException) {
            _reason = $"Intel 遥测组件不可用（{error.GetType().Name}）。";
        }
        return PowerReading.Missing("Intel IGCL", "集成 GPU", _reason, now);
    }

    private void Reset(int index) { if (_counters.TryGetValue(index, out var counter)) counter.Reset(); }

    internal static double? ReadNumber(IntelGcl.ctl_oc_telemetry_item_t item, IntelGcl.ctl_units_t units)
    {
        if (!item.bSupported || item.units != units) return null;
        double value = item.type switch {
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_DOUBLE => item.value.datadouble,
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_FLOAT => item.value.datafloat,
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_UINT64 => item.value.datau64,
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_INT64 => item.value.data64,
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_UINT32 => item.value.datau32,
            IntelGcl.ctl_data_type_t.CTL_DATA_TYPE_INT32 => item.value.data32,
            _ => double.NaN
        };
        return double.IsFinite(value) ? value : null;
    }

    public void Dispose() { _handles = null; _counters.Clear(); IntelGcl.Cleanup(); }
}
