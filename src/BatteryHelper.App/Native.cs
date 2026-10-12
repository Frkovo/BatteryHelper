using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryHelper.App;

internal static class Native
{
    public static readonly uint ShowDetailsMessage = RegisterWindowMessage("BatteryHelper.ShowDetails.v1");
    public static readonly uint ExitMessage = RegisterWindowMessage("BatteryHelper.Exit.v1");
    public static readonly uint CornerMessage = RegisterWindowMessage("BatteryHelper.SetCorner.v1");
    public static readonly uint RecreateHostMessage = RegisterWindowMessage("BatteryHelper.RecreateHost.v1");
    public static readonly uint CaptureHostMessage = RegisterWindowMessage("BatteryHelper.CaptureHost.v1");
    public static readonly uint ResumeMessage = RegisterWindowMessage("BatteryHelper.TestResume.v1");
    public static readonly uint StatusMessage = RegisterWindowMessage("BatteryHelper.Status.v1");
    public static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    public static IntPtr ControlWindow()
    {
        var result = IntPtr.Zero;
        EnumWindows((window, _) => {
            var caption = new StringBuilder(128); GetWindowText(window, caption, caption.Capacity);
            if (caption.ToString() == "BatteryHelper.Control") { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static int ControlProcessId() { var window = ControlWindow(); if (window == IntPtr.Zero) return 0; GetWindowThreadProcessId(window, out var pid); return (int)pid; }
    public static bool Responds(IntPtr window) => SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _) != IntPtr.Zero;
    public delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point point, uint flags);
    public static string ClassName(IntPtr hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }

    public static bool IsFullscreen(Rectangle monitor)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground) || !GetWindowRect(foreground, out var rect)) return false;
        if (ClassName(foreground) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return rect.Left <= monitor.Left && rect.Top <= monitor.Top && rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
    }
}
