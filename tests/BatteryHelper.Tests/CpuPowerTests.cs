using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryHelper.Core;
using Xunit;

namespace BatteryHelper.Tests;

public sealed class CpuPowerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static PowerReading Power(double value) => PowerReading.Measured(value, "RAPL", "domain", Now);
    [Theory]
    [InlineData(30, 8, 22)] [InlineData(30, 0, 30)] [InlineData(8, 8, 0)] [InlineData(0, 0, 0)]
    public void RemovesOnlyIntegratedDomainAndPreservesValidZero(double package, double graphics, double expected)
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(Power(package), Power(graphics), Now);
        Assert.Equal(ReadingStatus.Available, cpu.Status); Assert.Equal(expected, cpu.Watts);
        Assert.Equal(CpuPowerAccounting.Coverage, cpu.Coverage);
    }
    [Fact] public void MissingIntegratedDomainDoesNotLeakOriginalPackage()
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(Power(30), PowerReading.Missing("PP1", "graphics", "unsupported", Now), Now);
        Assert.Null(cpu.Watts); Assert.Contains("核显", cpu.Reason);
    }
    [Fact] public void GraphicsGreaterThanPackageDoesNotProduceFakeZero()
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(Power(5), Power(8), Now);
        Assert.Null(cpu.Watts); Assert.Equal(ReadingStatus.Unavailable, cpu.Status);
    }
    [Fact] public void StaleGraphicsCannotBeSubtracted()
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(Power(30), Power(8) with { SampledAt = Now.AddSeconds(-6) }, Now);
        Assert.Null(cpu.Watts); Assert.Contains("5 秒", cpu.Reason);
    }
    [Fact] public void DifferentSampleTimesAreRejected()
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(Power(30), Power(8) with { SampledAt = Now.AddSeconds(-1) }, Now);
        Assert.Null(cpu.Watts); Assert.Contains("同步", cpu.Reason);
    }
    [Fact] public void MissingPackageIsNotInferredFromGraphics()
    {
        var cpu = CpuPowerAccounting.ExcludeIntegratedGraphics(PowerReading.Missing("PKG", "package", "failed", Now), Power(8), Now);
        Assert.Null(cpu.Watts); Assert.Contains("封装", cpu.Reason);
    }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)]
    public void InvalidRawDomainIsRejected(double watts) =>
        Assert.Null(CpuPowerAccounting.ExcludeIntegratedGraphics(Power(30), Power(8) with { Watts = watts }, Now).Watts);
    [Fact] public void OptionalBreakdownPreservesOldProtocolAndStalesRawValues()
    {
        var snapshot = PowerSnapshot.Empty("test") with { SampledAt = Now, CpuPackagePower = Power(30), IntegratedGpuPower = Power(8) };
        var restored = JsonSerializer.Deserialize<PowerSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Equal(30, restored.CpuPackagePower!.Watts); Assert.Equal(8, restored.IntegratedGpuPower!.Watts);
        Assert.Equal(ReadingStatus.Stale, restored.Fresh(Now.AddSeconds(6)).IntegratedGpuPower!.Status);
        var old = JsonNode.Parse(JsonSerializer.Serialize(snapshot))!.AsObject();
        old.Remove("CpuPackagePower"); old.Remove("IntegratedGpuPower");
        Assert.Null(JsonSerializer.Deserialize<PowerSnapshot>(old.ToJsonString())!.IntegratedGpuPower);
    }
}
