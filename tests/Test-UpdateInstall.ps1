param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference='Stop'
$testRoot=Join-Path $env:TEMP ('AiInput-update-install-'+[guid]::NewGuid().ToString('N'))
$target=Join-Path $testRoot 'installed app'
$stage=Join-Path $testRoot 'download stage'
New-Item $target,$stage -ItemType Directory -Force | Out-Null
# An inert old app proves that the helper waits for its owning process before replacing files.
Set-Content (Join-Path $target 'AiInputAssistant.exe') 'old application placeholder'
$source=(Resolve-Path "artifacts/AiInputAssistant-$Version-Setup-x64.exe").Path
$installer=Join-Path $stage (Split-Path $source -Leaf)
Copy-Item $source $installer
$helper=Join-Path $stage 'AiInput.UpdateHost.exe'
Copy-Item artifacts/app/Updater/AiInput.UpdateHost.exe $helper
$root=Join-Path $env:LOCALAPPDATA 'AiInputAssistant'
New-Item $root -ItemType Directory -Force | Out-Null
$settings=Join-Path $root 'settings.json'
$key=Join-Path $root 'key.bin'
Set-Content $settings '{"Schema":1,"AutoUpdate":false,"FontSize":19}'
[IO.File]::WriteAllBytes($key,[byte[]](1,2,3,4,5))
$settingsHash=(Get-FileHash $settings).Hash
$keyHash=(Get-FileHash $key).Hash
$parent=Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 120' -PassThru -WindowStyle Hidden
$worker=$null
try {
    $plan=@{
        Installer=$installer;Version=$Version;Size=(Get-Item $installer).Length
        Sha256=(Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
        AppDirectory=$target;ParentId=$parent.Id;ParentStartTicks=$parent.StartTime.ToUniversalTime().Ticks;Restart=$false
    }
    $planPath=Join-Path $stage 'install.json'
    $plan|ConvertTo-Json|Set-Content $planPath
    $worker=Start-Process $helper -ArgumentList ('"'+$planPath+'"') -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(!(Test-Path ($planPath+'.ready'))) {
        if($worker.HasExited){throw 'Update helper exited before readiness handshake'}
        if([DateTime]::UtcNow -gt $deadline){throw 'Update helper readiness timed out'}
        Start-Sleep -Milliseconds 100
    }
    if((Get-Content (Join-Path $target 'AiInputAssistant.exe') -Raw).Trim() -ne 'old application placeholder'){throw 'Installer ran before parent exited'}
    Write-Host 'PASS update helper verifies package and waits for parent exit'
    Stop-Process -Id $parent.Id -Force
    if(!$worker.WaitForExit(180000)){throw 'Update installation timed out'}
    if($worker.ExitCode -ne 0){throw "Update helper failed: $($worker.ExitCode)"}
    $installed=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'AiInputAssistant.exe'))
    if("$($installed.FileMajorPart).$($installed.FileMinorPart).$($installed.FileBuildPart)" -ne $Version){throw 'Wrong installed version'}
    if(!(Test-Path (Join-Path $target 'Updater/AiInput.UpdateHost.exe'))){throw 'Updater missing from installed files'}
    Write-Host 'PASS verified installer upgrades in place to expected version'
    if((Get-FileHash $settings).Hash -ne $settingsHash -or (Get-FileHash $key).Hash -ne $keyHash){throw 'Update changed user settings or key'}
    Write-Host 'PASS update preserves user settings and API key bytes'
    if((Get-Content (Join-Path $root 'update-result.txt') -Raw) -ne 'UpdateInstalled'){throw 'Missing successful installation receipt'}
    Write-Host 'PASS update writes successful installation receipt'
} finally {
    if(!$parent.HasExited){Stop-Process -Id $parent.Id -Force}
    if($worker -and !$worker.HasExited){Stop-Process -Id $worker.Id -Force}
    # The CI machine is disposable; avoid deleting files owned by a still-running setup process.
}
