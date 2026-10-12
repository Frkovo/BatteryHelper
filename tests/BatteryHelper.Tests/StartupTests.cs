using System.Xml.Linq;
using BatteryHelper.Core;
using Xunit;

namespace BatteryHelper.Tests;

public sealed class StartupTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    [Fact] public void StartupRunsOnBatteriesWithoutIdleNetworkOrTimeLimit()
    {
        var doc = XDocument.Parse(StartupTaskDefinition.Create(@"C:\Program Files\BatteryHelper\BatteryHelper.exe", Sid));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string Value(string name) => doc.Descendants(ns + name).Single().Value;
        Assert.Equal("false", Value("DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value("StopIfGoingOnBatteries"));
        Assert.Equal("false", Value("RunOnlyIfIdle")); Assert.Equal("false", Value("RunOnlyIfNetworkAvailable"));
        Assert.Equal("PT0S", Value("ExecutionTimeLimit")); Assert.Equal("InteractiveToken", Value("LogonType"));
        Assert.Equal("LeastPrivilege", Value("RunLevel")); Assert.Equal("--supervise", Value("Arguments"));
        Assert.Equal("PT5S", Value("Delay")); Assert.Equal(Sid, doc.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
    }
    [Fact] public void ExecutablePathIsPreservedAndXmlEscaped()
    {
        const string path = @"C:\Test & Tools\BatteryHelper.exe";
        var xml = StartupTaskDefinition.Create(path, Sid); Assert.Contains("&amp;", xml);
        Assert.Equal(path, XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "Command").Value);
    }
}
