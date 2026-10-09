using System.Runtime.InteropServices;
using BatteryHelper.Core;
using Windows.Devices.Power;
using Windows.System.Power;

namespace BatteryHelper.Sensors;

public sealed record BatterySample(BatteryPresence Presence, SupplyState Supply, bool? ExternalPower,
    double? Percent, PowerReading Power, string? Reason = null);

public sealed class WindowsBatteryProvider : IDisposable
{
    private Battery? _battery;
    public event Action? Changed;

    public WindowsBatteryProvider()
    {
        try {
            _battery = Battery.AggregateBattery;
            _battery.ReportUpdated += ReportUpdated;
        } catch { /* Read() reports the actual error; constructor must not prevent other sensors. */ }
    }

    private void ReportUpdated(Battery sender, object args) => Changed?.Invoke();

    public BatterySample Read(DateTimeOffset now)
    {
        try {
            var report = (_battery ?? Battery.AggregateBattery).GetReport();
            var external = GetSystemPowerStatus(out var native) && native.ACLineStatus != 255
                ? native.ACLineStatus == 1 : (bool?)null;
            // WinRT explicitly distinguishes NotPresent from a failed query.
            var presence = report.Status == BatteryStatus.NotPresent ? BatteryPresence.Absent : BatteryPresence.Present;
            var supply = BatteryRules.Classify(presence, report.ChargeRateInMilliwatts,
                report.Status == BatteryStatus.Charging, report.Status == BatteryStatus.Discharging, external);
            double? percent = report.RemainingCapacityInMilliwattHours is { } remaining &&
                report.FullChargeCapacityInMilliwattHours is > 0 and var full
                ? Math.Clamp(100d * remaining / full, 0, 100) : null;
            var watts = BatteryRules.ToWatts(report.ChargeRateInMilliwatts);
            var power = presence == BatteryPresence.Absent
                ? PowerReading.Missing("Windows BatteryReport", "电池充入／放出功率", "设备没有电池。", now)
                : watts is { } value
                    ? PowerReading.Measured(value, "Windows BatteryReport", "电池充入／放出功率", now)
                    : PowerReading.Missing("Windows BatteryReport", "电池充入／放出功率", "电池驱动没有提供充放电速率。", now);
            return new(presence, supply, external, percent, power);
        } catch (Exception error) {
            var reason = $"电池查询失败（{error.GetType().Name}），无法确认是否存在电池。";
            return new(BatteryPresence.Unknown, SupplyState.Unknown, null, null,
                PowerReading.Missing("Windows BatteryReport", "电池", reason, now), reason);
        }
    }

    public void Dispose() { if (_battery is not null) _battery.ReportUpdated -= ReportUpdated; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
