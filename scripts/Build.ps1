param([switch]$Installer, [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1')
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
& $taskDotnet test 'tests\BatteryHelper.Tests\BatteryHelper.Tests.csproj' -c Release --logger 'trx;LogFileName=BatteryHelper.trx' --results-directory 'artifacts\validation' -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
foreach ($taskTarget in @(@{Name='Service';Output='service'},@{Name='App';Output='app'})) {
    & $taskDotnet publish ("src\BatteryHelper."+$taskTarget.Name+"\BatteryHelper."+$taskTarget.Name+".csproj") -c Release -r win-x64 --self-contained true -p:PlatformTarget=x64 -o ("artifacts\publish\"+$taskTarget.Output) -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Publishing $($taskTarget.Name) failed." }
}
if ($Installer) {
    if (-not $InnoCompiler) { $InnoCompiler = Join-Path $taskRoot '.tools\inno\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Inno Setup compiler not found. Pass -InnoCompiler with the path to ISCC.exe.' }
    & $InnoCompiler 'installer\BatteryHelper.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
