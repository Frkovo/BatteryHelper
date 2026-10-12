using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using BatteryHelper.Core;
using Microsoft.Win32;

namespace BatteryHelper.App;

internal static class Lifecycle
{
    public static string? DiagnosticPath { get; set; }
    private const string ExitEvent = "Local\\BatteryHelper.IntentionalExit.v1";
    public static EventWaitHandle OpenExit() => new(false, EventResetMode.ManualReset, ExitEvent);
    public static void RequestExit() { using var signal = OpenExit(); signal.Set(); }
    public static void ResetExit() { using var signal = OpenExit(); signal.Reset(); }
    public static void Log(string message)
    {
        try {
            Directory.CreateDirectory(AppSettings.DirectoryPath);
            var path = Path.Combine(AppSettings.DirectoryPath, "lifecycle.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}\n");
        } catch (Exception error) {
            if (DiagnosticPath is not null) {
                try { File.AppendAllText(DiagnosticPath + ".log-errors", error.GetType().Name + ": " + error.Message + "\n"); } catch { }
            }
        }
    }
    public static int Supervise(string[] args)
    {
        using var instance = new Mutex(true, "Local\\BatteryHelper.Supervisor", out var first);
        if (!first) return 0;
        using var exit = OpenExit(); exit.Reset();
        var executable = Environment.ProcessPath!;
        var diagnostics = args.SkipWhile(a => a != "--diagnostics").Skip(1).FirstOrDefault();
        DiagnosticPath = diagnostics;
        if (diagnostics is not null) File.WriteAllText(diagnostics + ".supervisor.json", System.Text.Json.JsonSerializer.Serialize(new {
            ProcessId = Environment.ProcessId, DataDirectory = AppSettings.DirectoryPath,
            LocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA"), Directory = Environment.CurrentDirectory,
            LogTail = File.Exists(Path.Combine(AppSettings.DirectoryPath, "lifecycle.log")) ? File.ReadLines(Path.Combine(AppSettings.DirectoryPath, "lifecycle.log")).TakeLast(6).ToArray() : []
        }));
        try {
            Log("supervisor started");
            while (AppSettings.Load().AutoStart && !exit.WaitOne(0)) {
                var processId = Native.ControlProcessId();
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--background"); start.ArgumentList.Add("--managed");
                if (diagnostics is not null) { start.ArgumentList.Add("--diagnostics"); start.ArgumentList.Add(diagnostics + ".ui.json"); }
                var attached = processId > 0 && processId != Environment.ProcessId;
                using var process = attached ? Process.GetProcessById(processId) : Process.Start(start);
                if (process is null) return 1;
                // A just-created process may not expose its main module yet. Its path
                // is already trusted through ProcessStartInfo; validate only adoption.
                if (attached && !string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return 1;
                var unresponsive = 0;
                while (!process.HasExited && !exit.WaitOne(1000)) {
                    if (!AppSettings.Load().AutoStart) return 0;
                    var control = Native.ControlWindow();
                    if (control != IntPtr.Zero && !Native.Responds(control)) {
                        if (++unresponsive >= 5) { Log("UI unresponsive; restarting owned process"); process.Kill(); break; }
                    } else unresponsive = 0;
                }
                if (exit.WaitOne(0) || !AppSettings.Load().AutoStart) return 0;
                Log("UI exited unexpectedly; restarting");
                if (exit.WaitOne(3000)) return 0;
            }
            return 0;
        } catch (Exception error) { Log($"supervisor failure: {error.GetType().Name}: {error.Message}"); return 1; }
        finally { Log("supervisor stopped"); }
    }
}

internal static class AutoStart
{
    public static void Set(bool enabled)
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try {
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            var name = StartupTaskDefinition.Name(sid);
            if (enabled) folder.RegisterTask(name, StartupTaskDefinition.Create(Environment.ProcessPath!, sid), 6, sid, null, 3, null);
            else {
                try { folder.GetTask(name); folder.DeleteTask(name, 0); }
                catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x80070002)) { }
            }
            // Migrate the old Run entry only after the task was successfully configured.
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            run?.DeleteValue("BatteryHelper", false);
            Lifecycle.Log(enabled ? "startup task configured" : "startup task removed");
        } finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler); }
    }
    public static void StartSupervisor()
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try { scheduler.Connect(); scheduler.GetFolder("\\").GetTask(StartupTaskDefinition.Name(sid)).Run(null); }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler); }
    }
}
