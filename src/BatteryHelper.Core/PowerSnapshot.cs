namespace BatteryHelper.Core;

public enum BatteryPresence { Unknown, Present, Absent }
public enum SupplyState { Unknown, Discharging, Charging, ExternalPowerIdle, NoBattery }
public enum ReadingStatus { Available, Unavailable, Stale, Error }

public sealed record PowerReading(double? Watts, string Source, string Coverage,
    DateTimeOffset SampledAt, ReadingStatus Status, string? Reason = null)
{
    public static PowerReading Missing(string source, string coverage, string reason,
        DateTimeOffset? now = null) => new(null, source, coverage, now ?? DateTimeOffset.UtcNow,
            ReadingStatus.Unavailable, reason);

    public static PowerReading Measured(double watts, string source, string coverage, DateTimeOffset now) =>
        double.IsFinite(watts) && watts >= 0
            ? new(watts, source, coverage, now, ReadingStatus.Available)
            : Missing(source, coverage, "传感器返回无效功率。", now);

    public PowerReading Fresh(DateTimeOffset now) =>
        Status == ReadingStatus.Available && now - SampledAt > TimeSpan.FromSeconds(5)
            ? this with { Watts = null, Status = ReadingStatus.Stale, Reason = "超过 5 秒未更新。" }
            : this;
}

public sealed record PowerSnapshot(DateTimeOffset SampledAt, BatteryPresence Battery,
    SupplyState Supply, bool? ExternalPower, double? BatteryPercent,
    PowerReading BatteryPower, PowerReading InputPower, PowerReading CpuPower,
    PowerReading GpuPower, string? StateReason = null)
{
    public static PowerSnapshot Empty(string reason) => new(DateTimeOffset.UtcNow,
        BatteryPresence.Unknown, SupplyState.Unknown, null, null,
        PowerReading.Missing("Windows 电池接口", "电池", reason),
        PowerReading.Missing("整机输入", "整机输入", reason),
        PowerReading.Missing("CPU", "CPU 封装", reason),
        PowerReading.Missing("GPU", "GPU", reason), reason);

    public PowerSnapshot Fresh(DateTimeOffset now) => this with {
        Supply = now - SampledAt > TimeSpan.FromSeconds(5) ? SupplyState.Unknown : Supply,
        BatteryPercent = now - SampledAt > TimeSpan.FromSeconds(5) ? null : BatteryPercent,
        StateReason = now - SampledAt > TimeSpan.FromSeconds(5) ? "采集连接已超过 5 秒未更新。" : StateReason,
        BatteryPower = BatteryPower.Fresh(now), InputPower = InputPower.Fresh(now),
        CpuPower = CpuPower.Fresh(now), GpuPower = GpuPower.Fresh(now)
    };
}

public static class Protocol
{
    public const string PipeName = "BatteryHelper.Power.v1";
    public const int MaxMessageLength = 65536;
}
