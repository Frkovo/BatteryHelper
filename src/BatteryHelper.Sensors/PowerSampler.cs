using BatteryHelper.Core;

namespace BatteryHelper.Sensors;

public sealed class PowerSampler : IDisposable
{
    private readonly WindowsBatteryProvider _battery = new();
    private readonly HardwarePowerProvider _hardware = new();
    public event Action? Changed { add => _battery.Changed += value; remove => _battery.Changed -= value; }

    public PowerSnapshot Read()
    {
        var now = DateTimeOffset.UtcNow;
        var battery = _battery.Read(now);
        var (cpu, gpu, input) = _hardware.Read(now);
        return new(now, battery.Presence, battery.Supply, battery.ExternalPower, battery.Percent,
            battery.Power, input, cpu, gpu, battery.Reason);
    }

    public void Dispose() { _battery.Dispose(); _hardware.Dispose(); }
}
