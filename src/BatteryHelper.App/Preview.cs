using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatteryHelper.Core;

namespace BatteryHelper.App;

internal static class Preview
{
    public static void Render(string[] args)
    {
        var output = args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault() ?? "preview";
        Directory.CreateDirectory(output);
        var application = new System.Windows.Application();
        var source = args.SkipWhile(a => a != "--snapshot").Skip(1).FirstOrDefault();
        var sample = source is not null ? JsonSerializer.Deserialize<PowerSnapshot>(File.ReadLines(source).Last())! : PowerSnapshot.Empty("预览：传感器尚未连接。");
        // Freeze display time to the capture timestamp without inventing sensor values.
        sample = sample with { SampledAt = DateTimeOffset.UtcNow,
            BatteryPower = sample.BatteryPower with { SampledAt = DateTimeOffset.UtcNow },
            InputPower = sample.InputPower with { SampledAt = DateTimeOffset.UtcNow },
            CpuPower = sample.CpuPower with { SampledAt = DateTimeOffset.UtcNow },
            GpuPower = sample.GpuPower with { SampledAt = DateTimeOffset.UtcNow } };
        foreach (var dark in new[] { true, false }) {
            Theme.Apply(application, dark);
            var overlay = new PowerView(); overlay.Update(sample);
            var details = new DetailsWindow(new AppSettings(), _ => { }); details.Update(sample);
            foreach (var scale in new[] { 1d, 1.5, 2d }) {
                SaveView(overlay, Path.Combine(output, $"taskbar-{(dark ? "dark" : "light")}-{scale * 100:F0}.png"), scale);
            }
            Save(details, Path.Combine(output, $"details-{(dark ? "dark" : "light")}.png"), 1);
            details.SelectSettings();
            Save(details, Path.Combine(output, $"settings-{(dark ? "dark" : "light")}.png"), 1);
            details.Close();
        }
    }
    private static void SaveView(FrameworkElement view, string path, double scale)
    {
        view.Measure(new(view.Width, view.Height)); view.Arrange(new(0, 0, view.Width, view.Height)); view.UpdateLayout();
        var target = new RenderTargetBitmap((int)Math.Ceiling(view.Width * scale), (int)Math.Ceiling(view.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Save(Window window, string path, double scale)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new(window.Width, window.Height)); root.Arrange(new(0, 0, window.Width, window.Height)); root.UpdateLayout();
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, window.Width, window.Height));
            context.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                new Rect(root.Margin.Left, root.Margin.Top, window.Width - root.Margin.Left - root.Margin.Right,
                    window.Height - root.Margin.Top - root.Margin.Bottom));
        }
        var target = new RenderTargetBitmap((int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(window.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
