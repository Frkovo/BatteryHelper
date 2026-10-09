using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using BatteryHelper.Core;
using BatteryHelper.Sensors;

namespace BatteryHelper.Service;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--service")) { ServiceBase.Run(new PowerWindowsService()); return 0; }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try {
            if (args.Contains("--probe")) return await Probe(args, stop.Token);
            if (args.Contains("--watch-pipe")) return await WatchPipe(args, stop.Token);
            if (args.Contains("--console")) { using var server = new PowerServer(); await server.Run(stop.Token); return 0; }
            Console.WriteLine("BatteryHelper.Service --probe [--seconds N] [--out file] | --watch-pipe | --console | --service");
            return 0;
        } catch (OperationCanceledException) { return 0; }
        catch (Exception error) { Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"); return 1; }
    }

    private static int Seconds(string[] args) => int.TryParse(Option(args, "--seconds"), out var seconds) ? Math.Clamp(seconds, 1, 86400) : 5;
    private static string? Option(string[] args, string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }

    private static async Task<int> Probe(string[] args, CancellationToken token)
    {
        using var output = Option(args, "--out") is { } path ? new StreamWriter(path, false) : null;
        using var sampler = new PowerSampler();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < Seconds(args)) {
            var sample = sampler.Read();
            var json = JsonSerializer.Serialize(sample);
            if (output is not null) { await output.WriteLineAsync(json); await output.FlushAsync(token); }
            Console.WriteLine(PowerPresentation.From(sample).PrimaryText + " | " + PowerPresentation.From(sample).SecondaryText);
            if (args.Contains("--details")) Console.WriteLine(json);
            await Task.Delay(1000, token);
        }
        return 0;
    }

    private static async Task<int> WatchPipe(string[] args, CancellationToken token)
    {
        using var output = Option(args, "--out") is { } path ? new StreamWriter(path, false) : null;
        using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, token);
        using var reader = new StreamReader(pipe);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < Seconds(args)) {
            var line = await reader.ReadLineAsync(token);
            if (line is null || line.Length > Protocol.MaxMessageLength) break;
            var sample = JsonSerializer.Deserialize<PowerSnapshot>(line);
            if (sample is null) continue;
            if (output is not null) { await output.WriteLineAsync(line); await output.FlushAsync(token); }
            Console.WriteLine(PowerPresentation.From(sample).PrimaryText + " | " + PowerPresentation.From(sample).SecondaryText);
        }
        return 0;
    }
}

internal sealed class PowerWindowsService : ServiceBase
{
    private CancellationTokenSource? _stop;
    private Task? _task;
    private PowerServer? _server;
    public PowerWindowsService() { ServiceName = "BatteryHelperPower"; CanHandlePowerEvent = true; }
    protected override void OnStart(string[] args)
    {
        _stop = new(); _server = new();
        _task = Task.Run(() => _server.Run(_stop.Token));
        _task.ContinueWith(t => { Environment.Exit(1); }, TaskContinuationOptions.OnlyOnFaulted);
    }
    protected override void OnStop() { _stop?.Cancel(); try { _task?.Wait(5000); } catch { } _server?.Dispose(); _stop?.Dispose(); }
    protected override bool OnPowerEvent(PowerBroadcastStatus status) { _server?.Reset(); return true; }
}

internal sealed class PowerServer : IDisposable
{
    private readonly SampleHub _hub = new();
    private readonly List<Task> _clients = [];

    public async Task Run(CancellationToken token)
    {
        var sampling = _hub.Run(token);
        try {
            while (!token.IsCancellationRequested) {
                var pipe = CreatePipe();
                try { await pipe.WaitForConnectionAsync(token); }
                catch { pipe.Dispose(); throw; }
                _clients.RemoveAll(t => t.IsCompleted);
                _clients.Add(Serve(pipe, token));
            }
        } finally { await Task.WhenAll(_clients.Prepend(sampling)); }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        var sid = WindowsIdentity.GetCurrent().User!;
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BatteryHelper", "service.json");
        if (File.Exists(config)) {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            sid = new SecurityIdentifier(document.RootElement.GetProperty("userSid").GetString()!);
        } else if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)) {
            throw new InvalidOperationException("Missing authorized-user service configuration.");
        }
        security.AddAccessRule(new(sid, PipeAccessRights.Read | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(Protocol.PipeName, PipeDirection.Out, 8,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    private async Task Serve(NamedPipeServerStream pipe, CancellationToken token)
    {
        _hub.AddClient();
        try {
            using (pipe) using (var writer = new StreamWriter(pipe) { AutoFlush = true }) {
                long version = -1;
                while (!token.IsCancellationRequested && pipe.IsConnected) {
                    var current = await _hub.Next(version, token);
                    version = current.Version;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(current.Sample).AsMemory(), token);
                }
            }
        } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { _hub.RemoveClient(); }
    }

    public void Reset() => _hub.Reset();
    public void Dispose() => _hub.Dispose();
}

internal sealed class SampleHub : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private TaskCompletionSource _updated = NewSignal();
    private PowerSnapshot _sample = PowerSnapshot.Empty("正在连接硬件采集服务。");
    private long _version;
    private int _clients;
    private bool _reset;
    private PowerSampler? _sampler;
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Wake() { try { if (_wake.CurrentCount == 0) _wake.Release(); } catch (SemaphoreFullException) { } }
    public void AddClient() { Interlocked.Increment(ref _clients); Wake(); }
    public void RemoveClient() { Interlocked.Decrement(ref _clients); Wake(); }
    public void Reset() { lock (_gate) _reset = true; Wake(); }

    public async Task<(long Version, PowerSnapshot Sample)> Next(long version, CancellationToken token)
    {
        while (true) {
            Task signal;
            lock (_gate) {
                if (version != _version) return (_version, _sample);
                signal = _updated.Task;
            }
            await signal.WaitAsync(token);
        }
    }

    public async Task Run(CancellationToken token)
    {
        try {
            while (!token.IsCancellationRequested) {
                bool reset;
                lock (_gate) { reset = _reset; _reset = false; }
                if (reset || Volatile.Read(ref _clients) == 0) {
                    _sampler?.Dispose(); _sampler = null;
                    Publish(PowerSnapshot.Empty(reset ? "正在恢复硬件采集。" : "采集已暂停。"));
                }
                if (Volatile.Read(ref _clients) > 0) {
                    try {
                        if (_sampler is null) { _sampler = new(); _sampler.Changed += Wake; }
                        Publish(_sampler.Read());
                    } catch (Exception error) {
                        Publish(PowerSnapshot.Empty($"采集暂时不可用（{error.GetType().Name}），正在重试。"));
                        _sampler?.Dispose(); _sampler = null;
                    }
                }
                await _wake.WaitAsync(1000, token);
            }
        } catch (OperationCanceledException) { }
        finally { _sampler?.Dispose(); _sampler = null; }
    }

    private void Publish(PowerSnapshot sample)
    {
        lock (_gate) { _sample = sample; _version++; _updated.TrySetResult(); _updated = NewSignal(); }
    }
    public void Dispose() { _sampler?.Dispose(); }
}
