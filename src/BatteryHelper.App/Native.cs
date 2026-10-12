using System.Drawing;
using System.Drawing.Imaging;
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
    [StructLayout(LayoutKind.Sequential)] private struct SurfaceSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, IntPtr position,
        ref SurfaceSize size, IntPtr sourceDc, ref Point source, uint color, ref Blend blend, uint flags);
    public readonly record struct AlphaSurface(int TransparentPixels, int PartialAlphaPixels, int OpaquePixels, Point HitPoint);
    public static AlphaSurface PresentAlpha(IntPtr window, Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        byte[] pixels;
        try {
            pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * bitmap.Width * 4, bitmap.Width * 4);
        } finally { bitmap.UnlockBits(data); }
        var clear = 0; var partial = 0; var opaque = 0; var hit = Point.Empty;
        for (var index = 3; index < pixels.Length; index += 4) {
            if (pixels[index] == 0) clear++;
            else if (pixels[index] < 255) partial++;
            else { if (opaque == 0) hit = new((index / 4) % bitmap.Width, (index / 4) / bitmap.Width); opaque++; }
        }
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        IntPtr surface = IntPtr.Zero, previous = IntPtr.Zero;
        try {
            var header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32 };
            surface = CreateDIBSection(dc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (surface == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(pixels, 0, bits, pixels.Length); previous = SelectObject(dc, surface);
            var size = new SurfaceSize { Width = bitmap.Width, Height = bitmap.Height }; var origin = Point.Empty;
            var blend = new Blend { Alpha = 255, Format = 1 }; // AC_SRC_OVER + premultiplied per-pixel alpha.
            if (!UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new(clear, partial, opaque, hit);
        } finally {
            if (previous != IntPtr.Zero) SelectObject(dc, previous);
            if (surface != IntPtr.Zero) DeleteObject(surface);
            DeleteDC(dc);
        }
    }
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
