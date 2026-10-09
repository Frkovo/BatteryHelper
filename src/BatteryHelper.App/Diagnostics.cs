using System.IO;
using System.Text.Json;
using System.Security.Principal;
using System.Windows.Interop;
using BatteryHelper.Core;
using Forms = System.Windows.Forms;

namespace BatteryHelper.App;

internal static class Diagnostics
{
    // Debug-only pixel check of the application's own visible client rectangle.
    // The normal background sampler does not take screenshots.
    public static void CaptureHost(TaskbarHost host)
    {
        if (!host.Visible || !Native.GetWindowRect(host.Handle, out var rect)) return;
        var center = new System.Drawing.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        if (Native.WindowFromPoint(center) != host.Handle) return;
        using var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
        Directory.CreateDirectory(AppSettings.DirectoryPath);
        bitmap.Save(Path.Combine(AppSettings.DirectoryPath, "taskbar-capture.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
    public static void RuntimeReport(TaskbarHost host, AppSettings settings, PowerSnapshot snapshot, string output)
    {
        Native.GetWindowRect(host.Handle, out var rect);
        var parent = Native.GetParent(host.Handle);
        var state = new {
            TimeUtc = DateTimeOffset.UtcNow, Administrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
            settings.Corner, settings.Visible, settings.HideOnFullscreen, settings.AutoStart,
            WindowVisible = host.Visible, Embedded = parent != IntPtr.Zero && Native.ClassName(parent) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd",
            ParentClass = Native.ClassName(parent), ParentHandle = parent.ToInt64(),
            ChildStyle = (Native.GetWindowLongPtr(host.Handle, -16).ToInt64() & 0x40000000) != 0,
            HitTestOwnWindow = host.Handle != IntPtr.Zero && Native.WindowFromPoint(new((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)) == host.Handle,
            host.Generation, host.Reason,
            Rendering = host.RenderState,
            Bounds = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            PrimaryText = host.View.PrimaryText, SecondaryText = host.View.SecondaryText,
            Sample = snapshot
        };
        File.WriteAllText(output, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void PlacementReport(string[] args)
    {
        var output = args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault() ?? "placement.json";
        var screens = Forms.Screen.AllScreens.Select(screen => new {
            screen.DeviceName, screen.Primary,
            Bounds = new PixelRect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Right, screen.Bounds.Bottom),
            WorkArea = new PixelRect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Right, screen.WorkingArea.Bottom),
            Left = TaskbarPlacement.Calculate(screen, Corner.Left, 248, 44),
            Right = TaskbarPlacement.Calculate(screen, Corner.Right, 248, 44),
            ForegroundIsFullscreen = Native.IsFullscreen(screen.Bounds)
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(screens, new JsonSerializerOptions { WriteIndented = true }));
    }
}
