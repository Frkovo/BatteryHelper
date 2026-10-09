using System.Text.Json;
using BatteryHelper.Core;
using Xunit;

namespace BatteryHelper.Tests;

public sealed class PowerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static PowerReading Power(double watts) => PowerReading.Measured(watts, "test", "test", Now);
    private static PowerSnapshot Snapshot(SupplyState supply) => new(Now,
        supply == SupplyState.NoBattery ? BatteryPresence.Absent : BatteryPresence.Present,
        supply, supply != SupplyState.Discharging, 50, Power(31.281),
        PowerReading.Missing("input", "input", "unavailable", Now), Power(12.4), Power(6.8));

    [Theory]
    [InlineData(31281, 31.281)] [InlineData(-34900, 34.9)] [InlineData(0, 0)]
    public void BatteryRateUsesMagnitudeAndMilliwatts(int input, double expected) => Assert.Equal(expected, BatteryRules.ToWatts(input));
    [Fact] public void MissingBatteryRateIsNotZero() => Assert.Null(BatteryRules.ToWatts(null));
    [Fact] public void UnknownBatteryQueryIsNotNoBattery() => Assert.Equal(SupplyState.Unknown, BatteryRules.Classify(BatteryPresence.Unknown, null, false, false, true));
    [Fact] public void ExplicitNoBatteryIsSupported() => Assert.Equal(SupplyState.NoBattery, BatteryRules.Classify(BatteryPresence.Absent, null, false, false, true));
    [Fact] public void BatteryDischargeOverridesExternalSupply() => Assert.Equal(SupplyState.Discharging, BatteryRules.Classify(BatteryPresence.Present, -20000, false, true, true));
    [Fact] public void ExternalPowerZeroRateIsIdle() => Assert.Equal(SupplyState.ExternalPowerIdle, BatteryRules.Classify(BatteryPresence.Present, 0, false, false, true));
    [Fact] public void ChargingFallbackChangesLabelAndValue()
    {
        var display = PowerPresentation.From(Snapshot(SupplyState.Charging), Now);
        Assert.Equal("充电", display.Label); Assert.Equal("31.3 W", display.Value);
    }
    [Fact] public void ValidInputHasPriorityWhenCharging()
    {
        var display = PowerPresentation.From(Snapshot(SupplyState.Charging) with { InputPower = Power(65.2) }, Now);
        Assert.Equal("输入", display.Label); Assert.Equal("65.2 W", display.Value);
    }
    [Fact] public void NoBatteryPreservesCpuGpuAndDoesNotInventTotal()
    {
        var display = PowerPresentation.From(Snapshot(SupplyState.NoBattery), Now);
        Assert.Equal("总功率", display.Label); Assert.Equal("— W", display.Value);
        Assert.Equal("12.4 W", display.CpuValue); Assert.Equal("6.8 W", display.GpuValue);
    }
    [Fact] public void IdleShowsStatusWithoutPretendingInputZero()
    {
        var display = PowerPresentation.From(Snapshot(SupplyState.ExternalPowerIdle), Now);
        Assert.Equal("未充电", display.Label); Assert.Equal("", display.Value);
    }
    [Fact] public void ZeroSensorReadingIsValid() => Assert.Equal("0.0 W", PowerPresentation.Format(Power(0)));
    [Fact] public void OldSampleClearsStateAndAllReadings()
    {
        var stale = Snapshot(SupplyState.Charging).Fresh(Now.AddSeconds(6));
        Assert.Equal(SupplyState.Unknown, stale.Supply); Assert.Null(stale.BatteryPercent);
        Assert.Equal(ReadingStatus.Stale, stale.CpuPower.Status); Assert.Null(stale.CpuPower.Watts);
    }
    [Fact] public void ExactlyFiveSecondsIsStillFresh() => Assert.Equal(ReadingStatus.Available, Power(1).Fresh(Now.AddSeconds(5)).Status);
    [Fact] public void InvalidValuesNeverAppearAsMeasured() { Assert.Null(Power(double.NaN).Watts); Assert.Null(Power(-1).Watts); Assert.Null(Power(double.PositiveInfinity).Watts); }
    [Fact] public void PowerSnapshotRoundTripsThroughPipeJson() { var snapshot = Snapshot(SupplyState.Discharging); Assert.Equal(snapshot, JsonSerializer.Deserialize<PowerSnapshot>(JsonSerializer.Serialize(snapshot))); }
    [Fact] public void DefaultsAreLeftAutostartAndFullscreenHide() { var defaults = new AppSettings(); Assert.Equal(Corner.Left, defaults.Corner); Assert.True(defaults.AutoStart); Assert.True(defaults.HideOnFullscreen); }
    [Fact] public void FirstEnergySampleDoesNotInventZero() => Assert.Null(new EnergyCounter().Sample(20, 1));
    [Fact] public void EnergyDifferenceIsPower() { var counter = new EnergyCounter(); counter.Sample(20, 1); Assert.Equal(12, counter.Sample(44, 3)); }
    [Fact] public void EnergyResetDoesNotProduceSpike() { var counter = new EnergyCounter(); counter.Sample(20, 1); Assert.Null(counter.Sample(1, 2)); Assert.Equal(4, counter.Sample(5, 3)); }
    [Fact] public void DocumentedWrapIsHandled() { var counter = new EnergyCounter(); counter.Sample(98, 1, 100); Assert.Equal(5, counter.Sample(3, 2, 100)); }
    [Fact] public void ArbitraryDecreaseIsNotWrap() { var counter = new EnergyCounter(); counter.Sample(70, 1, 100); Assert.Null(counter.Sample(3, 2, 100)); }
    [Fact] public void ResumeAndTimeResetReestablishBaseline() { var counter = new EnergyCounter(); counter.Sample(20, 10); Assert.Null(counter.Sample(21, 8)); Assert.Equal(2, counter.Sample(23, 9)); Assert.Null(counter.Sample(24, 20)); }
}

public sealed class PlacementTests
{
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void CornersAvoidOccupiedTaskbarAtEveryScale(double scale)
    {
        int S(int value) => (int)(value * scale);
        var monitor = new PixelRect(0, 0, S(1920), S(1080));
        var bar = new PixelRect(0, S(1032), S(1920), S(1080));
        PixelRect[] occupied = [new(0, S(1032), S(200), S(1080)), new(S(750), S(1032), S(1200), S(1080)), new(S(1600), S(1032), S(1920), S(1080))];
        foreach (var corner in new[] { Corner.Left, Corner.Right }) {
            var position = Placement.FindTaskbarSlot(monitor, bar, occupied, true, corner, S(248), S(44), S(8))!.Value;
            Assert.True(position.Top >= bar.Top); Assert.True(position.Bottom <= bar.Bottom);
            Assert.DoesNotContain(occupied, position.Intersects);
        }
    }
    [Fact] public void CrowdedTaskbarDoesNotCreateFloatingWindow()
    {
        var bar = new PixelRect(0, 1032, 1920, 1080);
        Assert.Null(Placement.FindTaskbarSlot(new(0, 0, 1920, 1080), bar, [bar], true, Corner.Left, 248, 44, 8));
    }
    [Fact] public void UnreliableBoundariesDoNotCoverTaskbar()
    {
        Assert.Null(Placement.FindTaskbarSlot(new(0, 0, 1920, 1080), new(0, 1032, 1920, 1080), [], false, Corner.Right, 248, 44, 8));
    }
    [Fact] public void SecondaryMonitorWithNegativeOriginStaysOnMonitor()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var position = Placement.FindTaskbarSlot(monitor, new(-1920, 1032, 0, 1080), [new(-400, 1032, 0, 1080)], true, Corner.Left, 248, 44, 8)!.Value;
        Assert.Equal(-1912, position.Left); Assert.True(position.Right <= 0);
    }
    [Fact] public void RightCornerDoesNotUseOnlyAvailableGapOnLeft()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var bar = new PixelRect(0, 1032, 1920, 1080);
        Assert.Null(Placement.FindTaskbarSlot(monitor, bar, [new(600, 1032, 1920, 1080)], true, Corner.Right, 248, 44, 8));
    }
}
