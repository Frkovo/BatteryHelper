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
        if (args.Contains("--occupancy-worker")) { OccupancyWorker.Run(); return; }
        if (args.Contains("--supervise")) { Environment.ExitCode = Lifecycle.Supervise(args); return; }
        if (args.Contains("--render-preview")) { Preview.Render(args); return; }
        if (args.Contains("--inspect-placement")) { Diagnostics.PlacementReport(args); return; }
        if (args.Contains("--quit")) {
            Lifecycle.RequestExit(); Native.PostMessage(new IntPtr(0xffff), Native.ExitMessage, IntPtr.Zero, IntPtr.Zero);
            var wait = Stopwatch.StartNew();
            while (Native.ControlWindow() != IntPtr.Zero && wait.Elapsed.TotalSeconds < 5) Thread.Sleep(100);
            Thread.Sleep(1200); return;
        }
        if (args.Contains("--simulate-resume")) { Native.PostMessage(Native.ControlWindow(), Native.ResumeMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--status")) { Native.PostMessage(Native.ControlWindow(), Native.StatusMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--recreate-host")) { Native.PostMessage(new IntPtr(0xffff), Native.RecreateHostMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--capture-host")) { Native.PostMessage(new IntPtr(0xffff), Native.CaptureHostMessage, IntPtr.Zero, IntPtr.Zero); return; }
        if (args.Contains("--set-corner")) {
            var right = args.SkipWhile(a => a != "--set-corner").Skip(1).FirstOrDefault() == "right";
            Native.PostMessage(new IntPtr(0xffff), Native.CornerMessage, new IntPtr(right ? 1 : 0), IntPtr.Zero); return;
        }
        if (args.Contains("--configure-startup")) {
            var enabled = args.SkipWhile(a => a != "--configure-startup").Skip(1).FirstOrDefault() == "on";
            var settings = AppSettings.Load() with { AutoStart = enabled };
            settings.Save(); AutoStart.Set(enabled);
            if (enabled) AutoStart.StartSupervisor();
            return;
        }
        using var instance = new Mutex(true, "Local\\BatteryHelper.App", out var first);
        if (!first) { if (!args.Contains("--background")) Native.PostMessage(new IntPtr(0xffff), Native.ShowDetailsMessage, IntPtr.Zero, IntPtr.Zero); return; }
        try { var application = new PowerApplication(); application.Run(); }
        catch (Exception error) { Lifecycle.Log($"fatal UI failure: {error.GetType().Name}: {error.Message}"); Environment.ExitCode = 1; }
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
    private CancellationTokenSource? _connectionStop;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastTick = Environment.TickCount64;
    private long _lastRecovery;
    private int _recoveryRetries;
    private int _recoveries;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Lifecycle.DiagnosticPath = e.Args.SkipWhile(a => a != "--diagnostics").Skip(1).FirstOrDefault();
        if (e.Args.Contains("--managed")) {
            using var exit = Lifecycle.OpenExit();
            if (exit.WaitOne(0)) { Shutdown(); return; }
        } else Lifecycle.ResetExit();
        Lifecycle.Log("UI started");
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
                if ((uint)message == Native.ExitMessage) { Dispatcher.BeginInvoke(Quit); handled = true; }
                if ((uint)message == Native.ResumeMessage) { Recover("simulated resume"); handled = true; }
                if ((uint)message == Native.StatusMessage) { WriteStatus(); handled = true; }
                if ((uint)message == Native.CornerMessage) { SaveSettings(_settings with { Corner = wParam == IntPtr.Zero ? Corner.Left : Corner.Right }); handled = true; }
                if ((uint)message == Native.RecreateHostMessage) { _host.Recreate(); UpdatePosition(); handled = true; }
                if ((uint)message == Native.CaptureHostMessage) { Diagnostics.CaptureHost(_host, _diagnosticsPath is null ? null : _diagnosticsPath + ".png"); handled = true; }
                if ((uint)message == Native.TaskbarCreatedMessage) {
                    Dispatcher.BeginInvoke(() => {
                        Recover("Explorer taskbar recreated");
                    });
                }
                if (message == 0x218) {
                    if (wParam.ToInt64() is 6 or 7 or 18) Dispatcher.BeginInvoke(() => Recover("power resume"));
                    if (wParam.ToInt64() == 4) Dispatcher.BeginInvoke(Suspend);
                }
                if (message is 0x7e or 0x2e0 or 0x1a) Dispatcher.BeginInvoke(() => { TaskbarPlacement.Invalidate(); UpdatePosition(); });
                return IntPtr.Zero;
        });
        CreateTray();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        InitializeStartup(e.Args.Contains("--managed"));
        _recoveryTimer.Tick += (_, _) => { UpdatePosition(); if (--_recoveryRetries <= 0) _recoveryTimer.Stop(); };
        _timer.Tick += (_, _) => {
            var tick = Environment.TickCount64;
            if (tick - _lastTick > 10000) Recover("timer resumed after suspension");
            _lastTick = tick;
            Theme.Apply(this);
            _host.Update(_snapshot);
            _details?.Update(_snapshot);
            if (++_positionTick % 2 == 0) UpdatePosition();
            if (_diagnosticsPath is not null) Diagnostics.RuntimeReport(_host, _settings, _snapshot, _diagnosticsPath, new { Recoveries = _recoveries, Connection = _connection?.Status.ToString() });
        };
        _timer.Start(); UpdatePosition();
        StartConnection();
        if (e.Args.Contains("--details")) ShowDetails();
    }

    private async void InitializeStartup(bool managed)
    {
        if (managed) return; // The scheduler already owns this launch; preserve its active definition.
        try {
            await Task.Run(() => AutoStart.Set(_settings.AutoStart));
            if (_settings.AutoStart && !_stop.IsCancellationRequested) await Task.Run(AutoStart.StartSupervisor);
        } catch (Exception error) { Lifecycle.Log($"startup configuration failure: {error.GetType().Name}: {error.Message}"); }
    }
    private void Quit() { Lifecycle.RequestExit(); Shutdown(); }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_stop.IsCancellationRequested) return;
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => Recover("system resume"));
        else if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(Suspend);
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (!_stop.IsCancellationRequested && e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Dispatcher.BeginInvoke(() => Recover("session available"));
    }
    private void Suspend()
    {
        _connectionStop?.Cancel(); SetSnapshot(PowerSnapshot.Empty("正在等待系统恢复。")); Lifecycle.Log("system suspended");
    }
    private void Recover(string reason)
    {
        var now = Environment.TickCount64;
        if (_stop.IsCancellationRequested || now - _lastRecovery < 1500) return;
        _lastRecovery = now; _lastTick = now; _recoveries++;
        Lifecycle.Log($"recovery {_recoveries}: {reason}");
        SetSnapshot(PowerSnapshot.Empty("正在恢复显示与采集连接。"));
        TaskbarPlacement.Invalidate(); _host?.Recreate(); CreateTray(); StartConnection();
        UpdatePosition(); _recoveryRetries = 20; _recoveryTimer.Start();
    }
    private void StartConnection()
    {
        var previous = _connectionStop; var previousTask = _connection;
        previous?.Cancel();
        if (previous is not null) {
            if (previousTask is null) previous.Dispose();
            else _ = previousTask.ContinueWith(_ => previous.Dispose());
        }
        _connectionStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _connection = ReadPower(_connectionStop.Token);
        _ = _connection.ContinueWith(t => {
            Lifecycle.Log($"power connection fault: {t.Exception?.GetBaseException().GetType().Name}");
            if (!_stop.IsCancellationRequested) Dispatcher.BeginInvoke(() => Recover("power connection fault"));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
    private void CreateTray()
    {
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Icon?.Dispose(); _tray.Dispose(); }
        _tray = new Forms.NotifyIcon { Icon = IconFactory.Create(), Text = "BatteryHelper", Visible = true };
        _tray.MouseClick += (_, mouse) => { if (mouse.Button == Forms.MouseButtons.Left) ShowDetails(); };
        RebuildMenu();
    }
    private void WriteStatus()
    {
        if (_host is null) return;
        Directory.CreateDirectory(AppSettings.DirectoryPath);
        Diagnostics.RuntimeReport(_host, _settings, _snapshot, Path.Combine(AppSettings.DirectoryPath, "status.json"), new { Recoveries = _recoveries, Connection = _connection?.Status.ToString() });
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
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(6));
                    string? line;
                    try { line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Power pipe has stopped delivering samples."); }
                    if (line is null) break;
                    if (line.Length > Protocol.MaxMessageLength) throw new IOException("Invalid power message length.");
                    if (JsonSerializer.Deserialize<PowerSnapshot>(line) is { } sample)
                        await Dispatcher.InvokeAsync(() => { if (!token.IsCancellationRequested) SetSnapshot(sample); });
                }
            } catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or JsonException or ObjectDisposedException) { }
            catch (OperationCanceledException) { break; }
            if (token.IsCancellationRequested) break;
            var now = DateTimeOffset.UtcNow;
            var fallback = battery.Read(now);
            var unavailable = PowerSnapshot.Empty("采集服务未连接。安装采集组件后可读取 CPU／GPU。");
            var local = unavailable with { Battery = fallback.Presence, Supply = fallback.Supply,
                ExternalPower = fallback.ExternalPower, BatteryPercent = fallback.Percent,
                BatteryPower = fallback.Power, StateReason = fallback.Reason };
            await Dispatcher.InvokeAsync(() => { if (!token.IsCancellationRequested) SetSnapshot(local); });
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

    private async void SaveSettings(AppSettings settings)
    {
        await _settingsGate.WaitAsync();
        try {
            if (settings.AutoStart != _settings.AutoStart) await Task.Run(() => AutoStart.Set(settings.AutoStart));
            settings.Save(); _settings = settings;
            _details?.SetSettings(settings); RebuildMenu(); UpdatePosition();
            if (settings.AutoStart) await Task.Run(AutoStart.StartSupervisor);
        } catch (Exception error) {
            System.Windows.MessageBox.Show($"设置未能保存：{error.Message}", "BatteryHelper", MessageBoxButton.OK, MessageBoxImage.Information);
            _details?.SetSettings(_settings);
        } finally { _settingsGate.Release(); }
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
        menu.Items.Add("退出", null, (_, _) => Quit());
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
        SystemEvents.PowerModeChanged -= OnPowerModeChanged; SystemEvents.SessionSwitch -= OnSessionSwitch;
        _stop.Cancel(); _connectionStop?.Cancel(); _timer.Stop(); _recoveryTimer.Stop(); TaskbarPlacement.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _details?.Close(); _host?.Dispose(); _control?.Dispose(); _stop.Dispose();
        Lifecycle.Log("UI exited"); base.OnExit(e);
    }
}
