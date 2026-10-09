param([int]$Seconds=1800, [string]$OutputDirectory='artifacts\validation\soak')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$taskOutput=[IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskWatcher=Join-Path $taskRoot 'artifacts\publish\service\BatteryHelper.Service.exe'
$taskSamples=Join-Path $taskOutput 'samples.jsonl'
$taskOut=Join-Path $taskOutput 'watcher.log'
$taskError=Join-Path $taskOutput 'watcher-error.log'
$taskWatchArgs='--watch-pipe --seconds '+$Seconds+' --out "'+$taskSamples+'"'
$taskProcess=Start-Process -FilePath $taskWatcher -ArgumentList $taskWatchArgs -WindowStyle Hidden -RedirectStandardOutput $taskOut -RedirectStandardError $taskError -PassThru
$taskTimer=[Diagnostics.Stopwatch]::StartNew()
$taskProfile=Join-Path $taskOutput 'resources.csv'
$taskLastCpu=@{}
$taskLastTime=0.0
while(-not $taskProcess.HasExited -and $taskTimer.Elapsed.TotalSeconds -lt ($Seconds+15)) {
    $taskElapsed=$taskTimer.Elapsed.TotalSeconds
    foreach($taskName in @('BatteryHelper.Service','BatteryHelper')) {
        foreach($taskTarget in @(Get-Process -Name $taskName -ErrorAction SilentlyContinue)) {
            if($taskTarget.Id -eq $taskProcess.Id){continue}
            try {
                $taskCpu=if($null -ne $taskTarget.TotalProcessorTime){$taskTarget.TotalProcessorTime.TotalSeconds}else{$null}
                $taskCpuPercent=if($null -ne $taskCpu -and $taskLastCpu.ContainsKey($taskTarget.Id) -and $taskElapsed -gt $taskLastTime){100*($taskCpu-$taskLastCpu[$taskTarget.Id])/($taskElapsed-$taskLastTime)/[Environment]::ProcessorCount}else{$null}
                [pscustomobject]@{TimeUtc=[DateTimeOffset]::UtcNow.ToString('O');ElapsedSeconds=[Math]::Round($taskElapsed,1);Process=$taskName;WorkingSetMiB=[Math]::Round($taskTarget.WorkingSet64/1MB,2);PrivateMiB=[Math]::Round($taskTarget.PrivateMemorySize64/1MB,2);CpuPercent=if($null -ne $taskCpuPercent){[Math]::Round($taskCpuPercent,3)}else{''};Handles=$taskTarget.HandleCount} | Export-Csv -LiteralPath $taskProfile -NoTypeInformation -Append -Encoding UTF8
                if($null -ne $taskCpu){$taskLastCpu[$taskTarget.Id]=$taskCpu}
            } catch { }
        }
    }
    $taskLastTime=$taskElapsed
    $taskProcess.WaitForExit(5000) | Out-Null
}
if(-not $taskProcess.HasExited){$taskProcess.Kill();throw 'Soak watcher exceeded its deadline.'}
$taskLines=@(Get-Content -LiteralPath $taskSamples)
$taskRows=$taskLines | ForEach-Object { $_ | ConvertFrom-Json }
$taskSummary=[pscustomobject]@{DurationSeconds=[Math]::Round($taskTimer.Elapsed.TotalSeconds,1);ExitCode=$taskProcess.ExitCode;Samples=$taskLines.Count;CpuAvailable=@($taskRows | Where-Object {$_.CpuPower.Status -eq 0}).Count;GpuAvailable=@($taskRows | Where-Object {$_.GpuPower.Status -eq 0}).Count;BatteryAvailable=@($taskRows | Where-Object {$_.BatteryPower.Status -eq 0}).Count}
$taskSummary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'summary.json') -Encoding UTF8
$taskSummary | ConvertTo-Json -Compress
