namespace BatteryHelper.Core;

/// <summary>Energy/time deltas. First sample, reset, unsupported units and invalid intervals never produce a made-up zero.</summary>
public sealed class EnergyCounter
{
    private double? _lastEnergy;
    private double? _lastTime;

    public void Reset() { _lastEnergy = null; _lastTime = null; }

    public double? Sample(double joules, double seconds, double? wrapJoules = null)
    {
        if (!double.IsFinite(joules) || joules < 0 || !double.IsFinite(seconds)) {
            Reset(); return null;
        }
        var previousEnergy = _lastEnergy;
        var previousTime = _lastTime;
        _lastEnergy = joules; _lastTime = seconds;
        if (previousEnergy is null || previousTime is null) return null;
        var elapsed = seconds - previousTime.Value;
        if (elapsed <= 0 || elapsed > 5) return null;
        var energy = joules - previousEnergy.Value;
        if (energy < 0) {
            // A counter wrap is accepted only at the documented boundary, never an arbitrary decrease.
            if (wrapJoules is not { } wrap || previousEnergy < wrap * .9 || joules > wrap * .1) return null;
            energy += wrap;
        }
        var watts = energy / elapsed;
        return double.IsFinite(watts) && watts >= 0 ? watts : null;
    }
}
