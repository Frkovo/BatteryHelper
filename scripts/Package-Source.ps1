param([string]$Output='artifacts\dist\BatteryHelper-Source-1.0.1.zip')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDestination=[IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
New-Item -ItemType Directory -Force (Split-Path -Parent $taskDestination) | Out-Null
Add-Type -AssemblyName System.IO.Compression
$taskArchive=[IO.Compression.ZipArchive]::new([IO.File]::Create($taskDestination),[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($taskPath in @('src','tests','scripts','installer','patches','docs','licenses')) {
        $taskFolder=Join-Path $taskRoot $taskPath
        if(-not(Test-Path -LiteralPath $taskFolder)){continue}
        foreach($taskFile in Get-ChildItem -LiteralPath $taskFolder -Recurse -File) {
            $taskRelative=$taskFile.FullName.Substring($taskRoot.Length+1)
            if($taskRelative -match '[\\/](bin|obj)[\\/]'){continue}
            $taskEntry=$taskArchive.CreateEntry($taskRelative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
            $taskStream=$taskEntry.Open(); $taskSource=[IO.File]::OpenRead($taskFile.FullName)
            try{$taskSource.CopyTo($taskStream)}finally{$taskSource.Dispose();$taskStream.Dispose()}
        }
    }
    foreach($taskPath in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','dependencies.json','Directory.Build.props','BatteryHelper.slnx','.gitignore','.gitattributes','.vendor\lhm-source.zip','.vendor\lhm\THIRD-PARTY-NOTICES.txt')) {
        $taskFile=Join-Path $taskRoot $taskPath
        if(-not(Test-Path -LiteralPath $taskFile)){continue}
        $taskRelative=if($taskPath.StartsWith('.vendor')){'upstream/'+[IO.Path]::GetFileName($taskPath)}else{$taskPath}
        $taskEntry=$taskArchive.CreateEntry($taskRelative,[IO.Compression.CompressionLevel]::Optimal)
        $taskStream=$taskEntry.Open(); $taskSource=[IO.File]::OpenRead($taskFile)
        try{$taskSource.CopyTo($taskStream)}finally{$taskSource.Dispose();$taskStream.Dispose()}
    }
    # Include reproducible acceptance evidence, without account/ACL configuration or debug transcripts.
    $taskEvidencePaths=@(
        'BatteryHelper.trx','embedded-left.json','embedded-recreated.json','embedded-hit-test.json',
        'installed-left.json','installed-right.json','position-persistence.json',
        'recovery-summary.json','resource-summary.json','uninstall-summary.json',
        'installed-resources-final.csv','service-recovery.jsonl','idle-resume-samples.jsonl',
        'installed-samples.jsonl','final-status.json','soak\summary.json','soak\samples.jsonl',
        'installed-visible-1.0.1.png','installed-render-1.0.1.json','recreated-visible-1.0.1.png','recreated-render-1.0.1.json'
    )
    foreach($taskEvidence in $taskEvidencePaths){
        $taskRelative='artifacts\validation\'+$taskEvidence
        $taskFile=Join-Path $taskRoot $taskRelative
        if(-not(Test-Path -LiteralPath $taskFile)){continue}
        $taskEntry=$taskArchive.CreateEntry($taskRelative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        $taskStream=$taskEntry.Open(); $taskSource=[IO.File]::OpenRead($taskFile)
        try{$taskSource.CopyTo($taskStream)}finally{$taskSource.Dispose();$taskStream.Dispose()}
    }
}finally{$taskArchive.Dispose()}
Write-Output $taskDestination
