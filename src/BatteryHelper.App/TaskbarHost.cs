using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using BatteryHelper.Core;
using Forms = System.Windows.Forms;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BatteryHelper.App;

// The display is always a child of Explorer's taskbar. A separate hidden WPF
// control window receives broadcasts and recreates this child after Explorer restarts.
internal sealed class TaskbarHost : IDisposable
{
    private PowerCanvas? _canvas;
    private PowerPresentation _display = PowerPresentation.From(PowerSnapshot.Empty("正在连接采集服务。"));
    public PowerView View { get; } = new();
    public IntPtr Handle => _canvas is { IsDisposed: false, IsHandleCreated: true } ? _canvas.Handle : IntPtr.Zero;
    public long ParentHandle { get; private set; }
    public int Generation { get; private set; }
    public string? Reason { get; private set; }
    public bool Visible => Handle != IntPtr.Zero && Native.IsWindowVisible(Handle);
    public object RenderState => new { Renderer = "GDI layered child", Width = _canvas?.ClientSize.Width, Height = _canvas?.ClientSize.Height, PaintCount = _canvas?.PaintCount };
    public void Update(PowerSnapshot snapshot) { View.Update(snapshot); _display = PowerPresentation.From(snapshot); _canvas?.SetDisplay(_display); }

    public void Place(PlacementResult placement, bool hidden)
    {
        if (!placement.Embedded || placement.ParentHandle == 0) { Hide(); Reason = placement.Reason; return; }
        var parent = new IntPtr(placement.ParentHandle);
        if (!Native.IsWindow(parent)) { Hide(); Reason = "正在等待任务栏恢复。"; return; }
        if (Handle == IntPtr.Zero || !Native.IsWindow(Handle) || Native.GetParent(Handle) != parent) {
            DisposeCanvas();
            var oldContext = Native.SetThreadDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(parent));
            try {
                _canvas = new PowerCanvas(parent, placement.Rect.Width, placement.Rect.Height);
                _canvas.Click += (_, _) => View.RequestDetails();
                _canvas.SetDisplay(_display);
                _ = _canvas.Handle;
                ParentHandle = placement.ParentHandle; Generation++;
            } catch (Exception error) {
                DisposeCanvas(); Reason = $"任务栏挂载失败（{error.GetType().Name}），将自动重试。"; return;
            } finally { if (oldContext != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(oldContext); }
        }
        if (hidden) { Hide(); Reason = "任务栏隐藏、全屏或显示已关闭。"; return; }
        var origin = new Point(placement.Rect.Left, placement.Rect.Top);
        if (!Native.ScreenToClient(parent, ref origin) || !Native.SetWindowPos(Handle, IntPtr.Zero,
            origin.X, origin.Y, placement.Rect.Width, placement.Rect.Height, 0x10 | 0x40)) {
            Hide(); Reason = "任务栏位置变化，正在重新挂载。"; return;
        }
        _canvas!.Invalidate(); Reason = null;
    }
    public void Recreate() => DisposeCanvas();
    private void Hide() { if (Handle != IntPtr.Zero && Native.IsWindow(Handle)) Native.ShowWindow(Handle, 0); }
    private void DisposeCanvas() { _canvas?.Dispose(); _canvas = null; ParentHandle = 0; }
    public void Dispose() => DisposeCanvas();
}

// Windows 11's taskbar compositor does not present a plain cross-process child
// reliably. WS_EX_LAYERED gives our embedded GDI surface its own composition layer.
internal sealed class PowerCanvas : Forms.Control
{
    private readonly IntPtr _parent;
    private PowerPresentation _display = PowerPresentation.From(PowerSnapshot.Empty("正在连接采集服务。"));
    public int PaintCount { get; private set; }
    public PowerCanvas(IntPtr parent, int width, int height)
    {
        _parent = parent; Size = new(width, height); Cursor = Forms.Cursors.Hand;
        SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override Forms.CreateParams CreateParams {
        get {
            var parameters = base.CreateParams;
            parameters.Parent = _parent; parameters.Caption = "BatteryHelper.TaskbarPower";
            parameters.Style = 0x40000000 | 0x04000000;
            parameters.ExStyle = 0x08000000 | 0x00080000; // WS_EX_NOACTIVATE | WS_EX_LAYERED
            return parameters;
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.SetLayeredWindowAttributes(Handle, 0, 255, 2))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    public void SetDisplay(PowerPresentation display) { _display = display; AccessibleName = $"{display.PrimaryText}，{display.SecondaryText}"; Invalidate(); }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        PaintCount++;
        var resources = System.Windows.Application.Current.Resources;
        Color ReadColor(string key) {
            var color = ((SolidColorBrush)resources[key]).Color;
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        var graphics = e.Graphics; var foreground = ReadColor("TextBrush");
        graphics.Clear(ReadColor("BackgroundBrush"));
        var scale = Native.GetDpiForWindow(Handle) / 96f;
        graphics.ScaleTransform(scale > 0 ? scale : 1, scale > 0 ? scale : 1);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var textBrush = new SolidBrush(foreground);
        using var secondaryBrush = new SolidBrush(ReadColor("MutedBrush"));
        using var pen = new Pen(foreground, 1.4f);
        using var labelFont = new Font("Microsoft YaHei UI", 12, GraphicsUnit.Pixel);
        using var valueFont = new Font("Consolas", 15, GraphicsUnit.Pixel);
        using var secondaryFont = new Font("Consolas", 10.5f, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap;
        if (_display.Icon == "bolt") {
            graphics.DrawPolygon(pen, [new PointF(19, 6), new(12, 15), new(18, 15), new(15, 23), new(25, 12), new(19, 12)]);
        } else if (_display.Icon == "plug") {
            graphics.DrawLine(pen, 15, 6, 15, 11); graphics.DrawLine(pen, 22, 6, 22, 11);
            graphics.DrawRectangle(pen, 12, 11, 13, 9); graphics.DrawLine(pen, 18.5f, 20, 18.5f, 24);
        } else {
            graphics.DrawRectangle(pen, 11, 9, 15, 10); graphics.DrawRectangle(pen, 26, 12, 3, 4);
            graphics.DrawLine(pen, 15, 12, 15, 16);
        }
        graphics.DrawString(_display.Label, labelFont, textBrush, new PointF(36, 5), format);
        var labelWidth = graphics.MeasureString(_display.Label, labelFont, new PointF(0, 0), format).Width;
        graphics.DrawString(_display.Value, valueFont, textBrush, new PointF(44 + labelWidth, 4), format);
        graphics.DrawString(_display.SecondaryText, secondaryFont, secondaryBrush, new PointF(10, 28), format);
    }
}
