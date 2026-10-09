param([switch]$SkipSdk)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskLock = Get-Content (Join-Path $taskRoot 'dependencies.json') -Raw | ConvertFrom-Json
$taskToolDirectory = Join-Path $taskRoot '.tools'
$taskVendorDirectory = Join-Path $taskRoot '.vendor'
New-Item -ItemType Directory -Force $taskToolDirectory,$taskVendorDirectory | Out-Null
if (-not $SkipSdk -and -not (Test-Path (Join-Path $taskToolDirectory 'dotnet\dotnet.exe'))) {
    $taskMetadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $taskSdk = $taskMetadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $taskLock.dotnetSdk | Select-Object -First 1
    if (-not $taskSdk) { throw 'Pinned SDK was not found in official release metadata.' }
    $taskAsset = $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    $taskZip = Join-Path $taskToolDirectory 'dotnet-sdk.zip'
    Invoke-WebRequest $taskAsset.url -OutFile $taskZip
    if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash -ne $taskAsset.hash) { throw 'SDK checksum mismatch.' }
    Expand-Archive -LiteralPath $taskZip -DestinationPath (Join-Path $taskToolDirectory 'dotnet') -Force
}
$taskCommit = $taskLock.libreHardwareMonitor.commit
$taskLhmDirectory = Join-Path $taskVendorDirectory 'lhm'
if (-not (Test-Path (Join-Path $taskLhmDirectory 'LibreHardwareMonitorLib\LibreHardwareMonitorLib.csproj'))) {
    $taskSourceZip = Join-Path $taskVendorDirectory 'lhm-source.zip'
    $taskBundledSource = Join-Path $taskRoot 'upstream\lhm-source.zip'
    if (Test-Path -LiteralPath $taskBundledSource) { Copy-Item -LiteralPath $taskBundledSource -Destination $taskSourceZip }
    else { Invoke-WebRequest "https://codeload.github.com/LibreHardwareMonitor/LibreHardwareMonitor/zip/$taskCommit" -OutFile $taskSourceZip }
    $taskExtraction = Join-Path $taskVendorDirectory 'extracted'
    Expand-Archive -LiteralPath $taskSourceZip -DestinationPath $taskExtraction -Force
    Move-Item -LiteralPath (Join-Path $taskExtraction "LibreHardwareMonitor-$taskCommit") -Destination $taskLhmDirectory
}
$taskProjectPath = Join-Path $taskLhmDirectory 'LibreHardwareMonitorLib\LibreHardwareMonitorLib.csproj'
[xml]$taskProject = Get-Content -LiteralPath $taskProjectPath -Raw
$taskProperties = $taskProject.Project.PropertyGroup[0]
$taskProperties.TargetFrameworks = 'net10.0'
$taskProperties.GeneratePackageOnBuild = 'false'
foreach ($taskName in @('PlatformTarget','CsWin32PlatformTarget')) {
    $taskNode = $taskProperties.SelectSingleNode($taskName)
    if (-not $taskNode) { $taskNode = $taskProject.CreateElement($taskName); $taskProperties.AppendChild($taskNode) | Out-Null }
    $taskNode.InnerText = 'x64'
}
# .NET 10 already provides these two APIs. Remove only redundant upstream package references.
foreach ($taskReference in @($taskProject.SelectNodes('//PackageReference'))) {
    if ($taskReference.Include -in @('Microsoft.Win32.Registry','System.Threading.AccessControl')) { $taskReference.ParentNode.RemoveChild($taskReference) | Out-Null }
}
$taskProject.Save($taskProjectPath)
Copy-Item -LiteralPath (Join-Path $taskRoot 'patches\IntelMsr.cs') -Destination (Join-Path $taskLhmDirectory 'LibreHardwareMonitorLib\PawnIo\IntelMsr.cs') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'patches\IntelCpu.cs') -Destination (Join-Path $taskLhmDirectory 'LibreHardwareMonitorLib\Hardware\Cpu\IntelCpu.cs') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'patches\IntelIntegratedGpu.cs') -Destination (Join-Path $taskLhmDirectory 'LibreHardwareMonitorLib\Hardware\Gpu\IntelIntegratedGpu.cs') -Force
Write-Output "Prepared LibreHardwareMonitor $taskCommit (.NET 10 x64, validated MSR reads)."
