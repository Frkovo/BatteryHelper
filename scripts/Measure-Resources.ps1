param([int]$Seconds=60,[string]$Output='artifacts\validation\installed-resources.csv')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDestination=[IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
$taskService=Get-CimInstance Win32_Service -Filter "Name='BatteryHelperPower'"
$taskUi=Get-Process -Name BatteryHelper -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $env:ProgramFiles 'BatteryHelper\BatteryHelper.exe')}
$taskIds=@($taskService.ProcessId)+@($taskUi.Id)
$taskPrevious=@{}
$taskTimer=[Diagnostics.Stopwatch]::StartNew()
while($taskTimer.Elapsed.TotalSeconds -lt $Seconds) {
    $taskCounters=Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "Name LIKE 'BatteryHelper%'"
    foreach($taskCounter in $taskCounters) {
        if($taskCounter.IDProcess -notin $taskIds){continue}
        $taskId=$taskCounter.IDProcess
        $taskCpu=$null
        if($taskPrevious.ContainsKey($taskId)) {
            $taskOld=$taskPrevious[$taskId]
            $taskElapsed=[double]$taskCounter.Timestamp_Sys100NS-[double]$taskOld.Timestamp_Sys100NS
            if($taskElapsed -gt 0){$taskCpu=100*([double]$taskCounter.PercentProcessorTime-[double]$taskOld.PercentProcessorTime)/$taskElapsed/[Environment]::ProcessorCount}
        }
        [pscustomobject]@{TimeUtc=[DateTimeOffset]::UtcNow.ToString('O');Process=$taskCounter.Name;ProcessId=$taskId;CpuPercent=if($null -ne $taskCpu){[Math]::Round($taskCpu,4)}else{''};WorkingSetMiB=[Math]::Round([double]$taskCounter.WorkingSet/1MB,2);PrivateWorkingSetMiB=[Math]::Round([double]$taskCounter.WorkingSetPrivate/1MB,2);Handles=$taskCounter.HandleCount} | Export-Csv -LiteralPath $taskDestination -NoTypeInformation -Append -Encoding UTF8
        $taskPrevious[$taskId]=$taskCounter
    }
    Start-Sleep -Seconds 3
}
