using System.Windows.Automation;
using System.Collections.Concurrent;
using BatteryHelper.Core;
using Forms = System.Windows.Forms;

namespace BatteryHelper.App;

internal readonly record struct PlacementResult(PixelRect Rect, bool TaskbarHidden, long ParentHandle, bool Embedded, string? Reason);

internal static class TaskbarPlacement
{
    private sealed record Occupancy(PixelRect[] Rectangles, DateTimeOffset CapturedAt);
    private static readonly ConcurrentDictionary<long, Occupancy> Cache = new();
    private static readonly ConcurrentDictionary<long, byte> Queries = new();
    public static void Invalidate() => Cache.Clear();

    private static void RefreshOccupancy(IntPtr taskbar)
    {
        var key = taskbar.ToInt64();
        if (!Queries.TryAdd(key, 0)) return;
        // UI Automation can stall while Explorer is unresponsive. Keep it off the UI thread
        // and allow only one query at a time; an expired result hides the child safely.
        _ = Task.Run(() => {
            try {
                var root = AutomationElement.FromHandle(taskbar);
                var condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));
                var children = root.FindAll(TreeScope.Descendants, condition);
                var occupied = new List<PixelRect>();
                foreach (AutomationElement child in children) {
                    var rect = child.Current.BoundingRectangle;
                    if (child.Current.IsOffscreen || rect.IsEmpty || rect.Width < 1 || rect.Height < 1) continue;
                    occupied.Add(new((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top), (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)));
                }
                Cache[taskbar.ToInt64()] = new(occupied.ToArray(), DateTimeOffset.UtcNow);
            } catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) {
                Cache.TryRemove(taskbar.ToInt64(), out _);
            } finally { Queries.TryRemove(key, out _); }
        });
    }

    public static PlacementResult Calculate(Forms.Screen screen, Corner corner, double widthDip, double heightDip)
    {
        var monitor = ToRect(screen.Bounds);
        var monitorHandle = Native.MonitorFromPoint(new(screen.Bounds.Left + 20, screen.Bounds.Top + 20), 2);
        var scale = Native.GetDpiForMonitor(monitorHandle, 0, out var dpi, out _) == 0 ? dpi / 96d : 1;
        var width = (int)Math.Round(widthDip * scale); var height = (int)Math.Round(heightDip * scale);
        var margin = (int)Math.Round(8 * scale);
        IntPtr taskbar = IntPtr.Zero;
        PixelRect? taskbarRect = null;
        Native.EnumWindows((window, _) => {
            if (Native.ClassName(window) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return true;
            if (Native.GetWindowRect(window, out var rect)) {
                var candidate = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (candidate.Intersects(monitor)) { taskbar = window; taskbarRect = candidate; }
            }
            return true;
        }, IntPtr.Zero);
        var hidden = taskbar != IntPtr.Zero && (!Native.IsWindowVisible(taskbar) ||
            taskbarRect is { } t && (Math.Min(t.Bottom, monitor.Bottom) - Math.Max(t.Top, monitor.Top) <= 3 ||
                Math.Min(t.Right, monitor.Right) - Math.Max(t.Left, monitor.Left) <= 3));
        PixelRect[] occupied = [];
        if (taskbar != IntPtr.Zero && !hidden) {
            Cache.TryGetValue(taskbar.ToInt64(), out var cached);
            var age = cached is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - cached.CapturedAt;
            if (age.TotalSeconds > 3) RefreshOccupancy(taskbar);
            if (age.TotalSeconds <= 5) occupied = cached!.Rectangles;
        }
        var slot = Placement.FindTaskbarSlot(monitor, taskbarRect, occupied, occupied.Length > 0, corner, width, height, margin);
        return new(slot ?? default, hidden, taskbar.ToInt64(), slot.HasValue,
            slot.HasValue ? null : "任务栏当前没有足够的安全空位，功率可在托盘详情中查看。");
    }
    private static PixelRect ToRect(System.Drawing.Rectangle rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
