namespace BatteryHelper.Core;

/// <summary>Subtract only a graphics domain belonging to the same CPU package.</summary>
public static class CpuPowerAccounting
{
    public const string Coverage = "CPU 封装扣除核显（仍保留封装内其他组件）";

    public static PowerReading ExcludeIntegratedGraphics(PowerReading package, PowerReading graphics, DateTimeOffset now)
    {
        package = package.Fresh(now); graphics = graphics.Fresh(now);
        var source = $"{package.Source} − {graphics.Source}";
        PowerReading Missing(string reason) => PowerReading.Missing(source, Coverage, reason, now);
        if (package.Status != ReadingStatus.Available || package.Watts is not { } total || !double.IsFinite(total) || total < 0)
            return Missing($"封装功率不可用，无法计算：{package.Reason ?? "缺少有效读数。"}");
        if (graphics.Status != ReadingStatus.Available || graphics.Watts is not { } gpu || !double.IsFinite(gpu) || gpu < 0)
            return Missing($"同一封装的核显功率不可用，无法扣除：{graphics.Reason ?? "缺少有效读数。"}");
        if ((package.SampledAt - graphics.SampledAt).Duration() > TimeSpan.FromMilliseconds(250))
            return Missing("封装与核显采样时间不匹配，等待同步读数。");
        if (gpu > total)
            return Missing("核显读数大于封装读数，等待有效的同步采样；未将差值伪装为零。");
        return PowerReading.Measured(total - gpu, source, Coverage,
            package.SampledAt < graphics.SampledAt ? package.SampledAt : graphics.SampledAt);
    }
}
