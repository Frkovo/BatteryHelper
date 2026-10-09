using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BatteryHelper.Core;
using BatteryHelper.Sensors;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BatteryHelper.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--render-preview")) { Preview.Render(args); return; }
        if (args.Contains("--inspect-placement")) { Diagnostics.PlacementReport(args); return; }
        if (args.Contains("--quit")) { Native.PostMessage(new IntPtr(0xffff), Native.ExitMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--recreate-host")) { Native.PostMessage(new IntPtr(0xffff), Native.RecreateHostMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--capture-host")) { Native.PostMessage(new IntPtr(0xffff), Native.CaptureHostMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--set-corner")) {
            var right = args.SkipWhile(a => a != "--set-corner").Skip(1).FirstOrDefault() == "right";
            Native.PostMessage(new IntPtr(0xffff), Native.CornerMessage, new IntPtr(right ? 1 : 0), IntPtr.Zero); return;
        }
        if (args.Contains("--configure-startup")) {
            var enabled = args.SkipWhile(a => a != "--configure-startup").Skip(1).FirstOrDefault() == "on";
            var settings = AppSettings.Load() with { AutoStart = enabled };
            settings.Save(); AutoStart.Set(enabled); return;
        }
        using var instance = new Mutex(true, "Local\\BatteryHelper.App", out var first);
        if (!first) { if (!args.Contains("--background")) Native.PostMessage(new IntPtr(0xffff), Native.ShowDetailsMessage, IntPtr.Zero, IntPtr.Zero); return; }
        var application = new PowerApplication();
        application.Run();
    }
}

internal sealed class PowerApplication : System.Windows.Application
{
    private AppSettings _settings = AppSettings.Load();
    private PowerSnapshot _snapshot = PowerSnapshot.Empty("正在连接采集服务。");
    private TaskbarHost? _host;
    private HwndSource? _control;
    private DetailsWindow? _details;
    private Forms.NotifyIcon? _tray;
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Task? _connection;
    private int _positionTick;
    private string? _diagnosticsPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _diagnosticsPath = e.Args.SkipWhile(a => a != "--diagnostics").Skip(1).FirstOrDefault();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, error) => {
            try { Directory.CreateDirectory(AppSettings.DirectoryPath); File.AppendAllText(Path.Combine(AppSettings.DirectoryPath, "errors.log"), $"{DateTimeOffset.Now:O} {error.Exception.GetType().Name}: {error.Exception.Message}\n"); } catch { }
            error.Handled = true;
        };
        Theme.Apply(this);
        _host = new TaskbarHost();
        _host.View.DetailsRequested += ShowDetails;
        _control = new HwndSource(new HwndSourceParameters("BatteryHelper.Control") {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000, Width = 0, Height = 0
        });
        _control.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) => {
                if ((uint)message == Native.ShowDetailsMessage) { ShowDetails(); handled = true; }
                if ((uint)message == Native.ExitMessage) { Dispatcher.BeginInvoke(Shutdown); handled = true; }
                if ((uint)message == Native.CornerMessage) { SaveSettings(_settings with { Corner = wParam == IntPtr.Zero ? Corner.Left : Corner.Right }); handled = true; }
                if ((uint)message == Native.RecreateHostMessage) { _host.Recreate(); UpdatePosition(); handled = true; }
                if ((uint)message == Native.CaptureHostMessage) { Diagnostics.CaptureHost(_host); handled = true; }
                if ((uint)message == Native.TaskbarCreatedMessage) {
                    Dispatcher.BeginInvoke(() => {
                        TaskbarPlacement.Invalidate(); _host.Recreate(); UpdatePosition();
                        if (_tray is not null) { _tray.Visible = false; _tray.Visible = true; }
                    });
                }
                if (message is 0x7e or 0x2e0 or 0x1a) Dispatcher.BeginInvoke(UpdatePosition);
                return IntPtr.Zero;
        });
        _tray = new Forms.NotifyIcon { Icon = IconFactory.Create(), Text = "BatteryHelper", Visible = true };
        _tray.MouseClick += (_, mouse) => { if (mouse.Button == Forms.MouseButtons.Left) ShowDetails(); };
        RebuildMenu();
        try { AutoStart.Set(_settings.AutoStart); } catch { }
        _timer.Tick += (_, _) => {
            Theme.Apply(this);
            _host.Update(_snapshot);
            _details?.Update(_snapshot);
            if (++_positionTick % 2 == 0) UpdatePosition();
            if (_diagnosticsPath is not null) Diagnostics.RuntimeReport(_host, _settings, _snapshot, _diagnosticsPath);
        };
        _timer.Start(); UpdatePosition();
        _connection = ReadPower(_stop.Token);
        if (e.Args.Contains("--details")) ShowDetails();
    }

    private async Task ReadPower(CancellationToken token)
    {
        using var battery = new WindowsBatteryProvider();
        while (!token.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1200, token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                while (pipe.IsConnected && !token.IsCancellationRequested) {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null) break;
                    if (line.Length > Protocol.MaxMessageLength) throw new IOException("Invalid power message length.");
                    if (JsonSerializer.Deserialize<PowerSnapshot>(line) is { } sample)
                        await Dispatcher.InvokeAsync(() => SetSnapshot(sample));
                }
            } catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or JsonException) { }
            catch (OperationCanceledException) { break; }
            if (token.IsCancellationRequested) break;
            var now = DateTimeOffset.UtcNow;
            var fallback = battery.Read(now);
            var unavailable = PowerSnapshot.Empty("采集服务未连接。安装采集组件后可读取 CPU／GPU。");
            var local = unavailable with { Battery = fallback.Presence, Supply = fallback.Supply,
                ExternalPower = fallback.ExternalPower, BatteryPercent = fallback.Percent,
                BatteryPower = fallback.Power, StateReason = fallback.Reason };
            await Dispatcher.InvokeAsync(() => SetSnapshot(local));
            try { await Task.Delay(1000, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    private void SetSnapshot(PowerSnapshot snapshot)
    {
        _snapshot = snapshot; _host?.Update(snapshot); _details?.Update(snapshot);
        var presentation = PowerPresentation.From(snapshot);
        if (_tray is not null) _tray.Text = $"BatteryHelper · {presentation.PrimaryText}\n{presentation.SecondaryText}"[..Math.Min(63, $"BatteryHelper · {presentation.PrimaryText}\n{presentation.SecondaryText}".Length)];
    }

    private void ShowDetails()
    {
        if (_details is null) {
            _details = new DetailsWindow(_settings, SaveSettings);
            _details.Closed += (_, _) => _details = null;
        }
        _details.Update(_snapshot); _details.Show();
        if (_details.WindowState == WindowState.Minimized) _details.WindowState = WindowState.Normal;
        _details.Activate();
    }

    private void SaveSettings(AppSettings settings)
    {
        try {
            if (settings.AutoStart != _settings.AutoStart) AutoStart.Set(settings.AutoStart);
            settings.Save(); _settings = settings;
            _details?.SetSettings(settings); RebuildMenu(); UpdatePosition();
        } catch (Exception error) {
            System.Windows.MessageBox.Show($"设置未能保存：{error.Message}", "BatteryHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            _details?.SetSettings(_settings);
        }
    }

    private void RebuildMenu()
    {
        if (_tray is null) return;
        var old = _tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("功率详情与设置", null, (_, _) => ShowDetails());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("左下角", null, (_, _) => SaveSettings(_settings with { Corner = Corner.Left })) { Checked = _settings.Corner == Corner.Left });
        menu.Items.Add(new Forms.ToolStripMenuItem("右下角", null, (_, _) => SaveSettings(_settings with { Corner = Corner.Right })) { Checked = _settings.Corner == Corner.Right });
        menu.Items.Add(new Forms.ToolStripMenuItem(_settings.Visible ? "隐藏任务栏功率" : "显示任务栏功率", null, (_, _) => SaveSettings(_settings with { Visible = !_settings.Visible })));
        menu.Items.Add(new Forms.ToolStripMenuItem("登录时自动启动", null, (_, _) => SaveSettings(_settings with { AutoStart = !_settings.AutoStart })) { Checked = _settings.AutoStart });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu; old?.Dispose();
    }

    private void UpdatePosition()
    {
        if (_host is null) return;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.MonitorDevice) ?? Forms.Screen.PrimaryScreen!;
        var result = TaskbarPlacement.Calculate(screen, _settings.Corner, _host.View.Width, _host.View.Height);
        var hide = !_settings.Visible || result.TaskbarHidden || (_settings.HideOnFullscreen && Native.IsFullscreen(screen.Bounds));
        _host.Place(result, hide);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stop.Cancel(); _timer.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _details?.Close(); _host?.Dispose(); _control?.Dispose(); _stop.Dispose();
        base.OnExit(e);
    }
}

internal static class AutoStart
{
    public static void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) run.SetValue("BatteryHelper", $"\"{Environment.ProcessPath}\" --background");
        else run.DeleteValue("BatteryHelper", false);
    }
}
