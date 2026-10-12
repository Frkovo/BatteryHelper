using System.Xml.Linq;

namespace BatteryHelper.Core;

public static class StartupTaskDefinition
{
    public static string Name(string sid) => $"BatteryHelper-{sid}";
    public static string Create(string executable, string sid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object? content = null) => new(ns + name, content);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "BatteryHelper 登录启动及常驻恢复（普通用户权限）")),
            E("Triggers", E("LogonTrigger", new[] { E("Enabled", "true"), E("UserId", sid), E("Delay", "PT5S") })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "User"),
                E("UserId", sid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new[] {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"),
                E("StopIfGoingOnBatteries", "false"), E("AllowHardTerminate", "true"),
                E("StartWhenAvailable", "true"), E("RunOnlyIfNetworkAvailable", "false"),
                E("IdleSettings", new[] { E("StopOnIdleEnd", "false"), E("RestartOnIdle", "false") }),
                E("AllowStartOnDemand", "true"), E("Enabled", "true"), E("Hidden", "false"),
                E("RunOnlyIfIdle", "false"), E("WakeToRun", "false"), E("ExecutionTimeLimit", "PT0S"),
                E("Priority", "7"), E("RestartOnFailure", new[] { E("Interval", "PT1M"), E("Count", "3") }) }),
            new XElement(ns + "Actions", new XAttribute("Context", "User"),
                E("Exec", new[] { E("Command", executable), E("Arguments", "--supervise"), E("WorkingDirectory", Path.GetDirectoryName(executable)) })))
            .ToString();
    }
}
