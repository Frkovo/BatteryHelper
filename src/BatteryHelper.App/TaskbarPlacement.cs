using System.Collections.Concurrent;
using BatteryHelper.Core;
using Forms = System.Windows.Forms;

namespace BatteryHelper.App;

internal readonly record struct PlacementResult(PixelRect Rect, bool TaskbarHidden, long ParentHandle, bool Embedded, string? Reason);

internal static class TaskbarPlacement
{
    private sealed record Occupancy(PixelRect[] Rectangles, DateTimeOffset CapturedAt);
    private static readonly ConcurrentDictionary<long, Occupancy> Cache = new();
    private static readonly ConcurrentDictionary<long, long> Queries = new();
    private static readonly OccupancyWorker Worker = new();
    private static long _generation;
    public static object State => new { Epoch = _generation, PendingQueries = Queries.Count, CachedTaskbars = Cache.Count, Worker.Restarts };
    public static void Invalidate() { Interlocked.Increment(ref _generation); Cache.Clear(); Queries.Clear(); Worker.Restart(); }
    public static void Stop() => Worker.Dispose();

    private static void RefreshOccupancy(IntPtr taskbar)
    {
        var key = taskbar.ToInt64();
        var generation = Interlocked.Read(ref _generation);
        if (!Queries.TryAdd(key, generation)) return;
        // A stalled UIA provider is terminated in its owned helper process, not left
        // holding an in-process query gate forever after resume.
        _ = Task.Run(async () => {
            try {
                var occupied = await Worker.Query(taskbar);
                if (generation == Interlocked.Read(ref _generation)) Cache[key] = new(occupied, DateTimeOffset.UtcNow);
            } finally { ((ICollection<KeyValuePair<long, long>>)Queries).Remove(new(key, generation)); }
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
