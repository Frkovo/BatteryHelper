using System.Globalization;

namespace BatteryHelper.Core;

public sealed record PowerPresentation(string Label, string Value, string Icon,
    string CpuValue, string GpuValue, string Description)
{
    public string PrimaryText => $"{Label} {Value}";
    public string SecondaryText => $"CPU {CpuValue}  ·  GPU {GpuValue}";

    public static PowerPresentation From(PowerSnapshot raw, DateTimeOffset? time = null)
    {
        var sample = raw.Fresh(time ?? DateTimeOffset.UtcNow);
        var inputAvailable = sample.InputPower.Status == ReadingStatus.Available;
        var (label, reading, icon) = sample.Supply switch {
            SupplyState.Discharging => ("放电", sample.BatteryPower, "battery"),
            SupplyState.Charging when inputAvailable => ("输入", sample.InputPower, "plug"),
            SupplyState.Charging => ("充电", sample.BatteryPower, "bolt"),
            SupplyState.ExternalPowerIdle when inputAvailable => ("输入", sample.InputPower, "plug"),
            SupplyState.ExternalPowerIdle => ("未充电", (PowerReading?)null, "battery"),
            SupplyState.NoBattery => ("总功率", sample.InputPower, "plug"),
            _ => ("状态未知", sample.BatteryPower with { Watts = null, Status = ReadingStatus.Unavailable }, "battery")
        };
        var value = reading is null ? "" : Format(reading);
        var reason = reading?.Reason ?? sample.StateReason ?? "";
        return new(label, value, icon, Format(sample.CpuPower), Format(sample.GpuPower), reason);
    }

    public static string Format(PowerReading reading) =>
        reading.Status == ReadingStatus.Available && reading.Watts is { } watts && double.IsFinite(watts)
            ? $"{watts.ToString("F1", CultureInfo.InvariantCulture)} W" : "— W";
}

public static class BatteryRules
{
    public static double? ToWatts(int? milliwatts) => milliwatts is { } rate ? Math.Abs((double)rate) / 1000 : null;

    public static SupplyState Classify(BatteryPresence presence, int? milliwatts,
        bool charging, bool discharging, bool? externalPower)
    {
        if (presence == BatteryPresence.Unknown) return SupplyState.Unknown;
        if (presence == BatteryPresence.Absent) return SupplyState.NoBattery;
        if (milliwatts < 0 || discharging) return SupplyState.Discharging;
        if (milliwatts > 0 || charging) return SupplyState.Charging;
        if (externalPower == true) return SupplyState.ExternalPowerIdle;
        return SupplyState.Unknown;
    }
}
