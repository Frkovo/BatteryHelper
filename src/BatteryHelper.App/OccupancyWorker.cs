using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using BatteryHelper.Core;

namespace BatteryHelper.App;

internal sealed class OccupancyWorker : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processLock = new();
    private Process? _process;
    public int Restarts { get; private set; }
    public async Task<PixelRect[]> Query(IntPtr taskbar)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(6))) return [];
        try {
            Process process;
            lock (_processLock) {
                if (_process is null || _process.HasExited) {
                    _process?.Dispose();
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    start.ArgumentList.Add("--occupancy-worker");
                    _process = Process.Start(start)!;
                }
                process = _process;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await process.StandardInput.WriteLineAsync(taskbar.ToInt64().ToString().AsMemory(), deadline.Token).WaitAsync(deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token).WaitAsync(deadline.Token);
            // Anonymous process pipes can ignore cancellation inside an OS read.
            // Bound the await independently; terminating the helper releases that read.
            var line = await process.StandardOutput.ReadLineAsync(deadline.Token).AsTask().WaitAsync(deadline.Token);
            if (line is null || line.Length > 65536) throw new IOException("Invalid occupancy response.");
            return JsonSerializer.Deserialize<PixelRect[]>(line) ?? [];
        } catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception) {
            Lifecycle.Log($"taskbar query restarted: {error.GetType().Name}"); Restart(); return [];
        } finally { _gate.Release(); }
    }
    public void Restart()
    {
        lock (_processLock) {
            if (_process is not null) {
                try { if (!_process.HasExited) _process.Kill(); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                _process.Dispose(); _process = null; Restarts++;
            }
        }
    }
    public void Dispose() => Restart();
    public static void Run() => Task.Run(() => {
        // All UIA calls stay on a worker process MTA thread, with no live UI of its own.
        while (Console.ReadLine() is { } line) {
            var rectangles = new List<PixelRect>();
            try {
                if (!long.TryParse(line, out var handle)) throw new InvalidOperationException();
                var window = new IntPtr(handle);
                if (!Native.IsWindow(window) || Native.ClassName(window) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) throw new InvalidOperationException();
                var root = AutomationElement.FromHandle(window);
                var condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));
                var children = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement child in children) {
                    var rect = child.Current.BoundingRectangle;
                    if (child.Current.IsOffscreen || rect.IsEmpty || rect.Width < 1 || rect.Height < 1) continue;
                    rectangles.Add(new((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top), (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)));
                }
            } catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            Console.WriteLine(JsonSerializer.Serialize(rectangles)); Console.Out.Flush();
        }
    }).GetAwaiter().GetResult();
}
