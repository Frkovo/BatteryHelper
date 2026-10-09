param([ValidateSet('Install','Uninstall')][string]$Action = 'Install', [Parameter(Mandatory)][string]$ApplicationDirectory)
$ErrorActionPreference = 'Stop'
$taskApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
$taskServiceName = 'BatteryHelperPower'
$taskExecutable = Join-Path $taskApplicationDirectory 'service\BatteryHelper.Service.exe'
$taskConfigDirectory = Join-Path $env:ProgramData 'BatteryHelper'
$taskConfigPath = Join-Path $taskConfigDirectory 'service.json'
trap {
    try {
        New-Item -ItemType Directory -Path $taskConfigDirectory -Force | Out-Null
        ($_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace) | Set-Content -LiteralPath (Join-Path $taskConfigDirectory 'setup-error.log') -Encoding UTF8
    } catch { }
    exit 1
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator rights are required to configure the power service.' }

if ($Action -eq 'Uninstall') {
    if (Test-Path -LiteralPath $taskConfigPath) {
        $taskOwnerSid = (Get-Content -LiteralPath $taskConfigPath -Raw | ConvertFrom-Json).userSid
        $taskRunKey = [Microsoft.Win32.Registry]::Users.OpenSubKey("$taskOwnerSid\Software\Microsoft\Windows\CurrentVersion\Run", $true)
        if ($taskRunKey) {
            try {
                $taskStartupValue = [string]$taskRunKey.GetValue('BatteryHelper')
                $taskUiExecutable = Join-Path $taskApplicationDirectory 'BatteryHelper.exe'
                if ($taskStartupValue.StartsWith(('"'+$taskUiExecutable+'"'), [StringComparison]::OrdinalIgnoreCase)) { $taskRunKey.DeleteValue('BatteryHelper', $false) }
            } finally { $taskRunKey.Dispose() }
        }
    }
    $taskExistingService = Get-Service -Name $taskServiceName -ErrorAction SilentlyContinue
    if ($taskExistingService) {
        Stop-Service -Name $taskServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $taskServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the power service.' }
    }
    # Only touch this application's exact configuration file, never remove ProgramData recursively.
    if (Test-Path -LiteralPath $taskConfigPath) { Remove-Item -LiteralPath $taskConfigPath -Force }
    if ((Test-Path -LiteralPath $taskConfigDirectory) -and @(Get-ChildItem -LiteralPath $taskConfigDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $taskConfigDirectory -Force }
    exit 0
}
if (-not (Test-Path -LiteralPath $taskExecutable)) { throw 'Published service executable not found.' }
$taskDriver = Join-Path $taskApplicationDirectory 'drivers\PawnIO_setup.exe'
$taskDriverHive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $taskDriverKey = $taskDriverHive.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
    $taskDriverInstalled = $null -ne $taskDriverKey
    if ($taskDriverKey) { $taskDriverKey.Dispose() }
} finally { $taskDriverHive.Dispose() }
if (-not $taskDriverInstalled) {
    if ((Get-AuthenticodeSignature -LiteralPath $taskDriver).Status -ne 'Valid') { throw 'PawnIO installer signature validation failed.' }
    $taskDriverInstaller = Start-Process -FilePath $taskDriver -ArgumentList '-install','-silent' -WindowStyle Hidden -Wait -PassThru
    if ($taskDriverInstaller.ExitCode -ne 0) { throw 'PawnIO installation failed.' }
}
New-Item -ItemType Directory -Path $taskConfigDirectory -Force | Out-Null
# Resolve the desktop user rather than an alternate administrator used for UAC.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BatteryHelperSessionUser {
    [DllImport("wtsapi32.dll", CharSet=CharSet.Unicode)] private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int info, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
    public static string Query(int session, int info) {
        IntPtr buffer; int bytes;
        if(!WTSQuerySessionInformation(IntPtr.Zero, session, info, out buffer, out bytes)) throw new InvalidOperationException("Cannot identify the installing desktop user.");
        try { return Marshal.PtrToStringUni(buffer); } finally { WTSFreeMemory(buffer); }
    }
}
'@
$taskSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
$taskUserName = [BatteryHelperSessionUser]::Query($taskSession, 5)
$taskUserDomain = [BatteryHelperSessionUser]::Query($taskSession, 7)
if (-not $taskUserName) { throw 'Install from the intended user desktop session.' }
$taskSid = ([Security.Principal.NTAccount]::new($taskUserDomain, $taskUserName).Translate([Security.Principal.SecurityIdentifier])).Value
@{ userSid = $taskSid } | ConvertTo-Json | Set-Content -LiteralPath $taskConfigPath -Encoding UTF8
# LocalSystem and administrators manage this file; local users may read it but cannot grant themselves pipe access.
$taskAcl = [Security.AccessControl.DirectorySecurity]::new()
$taskAcl.SetAccessRuleProtection($true, $false)
$taskInheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
foreach ($taskPrincipal in @('S-1-5-18','S-1-5-32-544')) {
    $taskRule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskPrincipal), 'FullControl', $taskInheritance, 'None', 'Allow')
    $taskAcl.AddAccessRule($taskRule)
}
$taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskSid), 'ReadAndExecute', $taskInheritance, 'None', 'Allow'))
Set-Acl -LiteralPath $taskConfigDirectory -AclObject $taskAcl
$taskExistingService = Get-Service -Name $taskServiceName -ErrorAction SilentlyContinue
if ($taskExistingService) {
    Stop-Service -Name $taskServiceName -Force
    $taskServiceInstance = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'"
    $taskChange = Invoke-CimMethod -InputObject $taskServiceInstance -MethodName Change -Arguments @{ PathName=('"'+$taskExecutable+'" --service'); StartMode='Automatic'; StartName='LocalSystem' }
    if ($taskChange.ReturnValue -ne 0) { throw "Could not update the power service (code $($taskChange.ReturnValue))." }
} else {
    New-Service -Name $taskServiceName -DisplayName 'BatteryHelper Power Sampler' -BinaryPathName ('"'+$taskExecutable+'" --service') -StartupType Automatic -Description 'Read-only local power telemetry; sampling pauses when no BatteryHelper client is connected.' | Out-Null
}
& sc.exe failure $taskServiceName reset= 86400 actions= restart/3000/restart/10000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure service recovery.' }
& sc.exe failureflag $taskServiceName 1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not enable service recovery.' }
Start-Service -Name $taskServiceName
if ((Get-Service -Name $taskServiceName).Status -ne 'Running') { throw 'Power service did not start.' }
if (Test-Path -LiteralPath (Join-Path $taskConfigDirectory 'setup-error.log')) { Remove-Item -LiteralPath (Join-Path $taskConfigDirectory 'setup-error.log') -Force }
