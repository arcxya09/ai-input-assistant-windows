param([string]$Configuration="Release")
$ErrorActionPreference="Stop"
$version=([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
function Run-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" } }
New-Item artifacts -ItemType Directory -Force | Out-Null
Run-Dotnet run --project tests/AiInput.Tests/AiInput.Tests.csproj -c $Configuration
Run-Dotnet run --project tests/AiInput.Tests/AiInput.Tests.csproj -c $Configuration --no-build -- --github-update-smoke
Run-Dotnet publish src/AiInput.App/AiInput.App.csproj -c $Configuration -r win-x64 --self-contained true -o artifacts/app
Run-Dotnet publish src/AiInput.ContextHost/AiInput.ContextHost.csproj -c $Configuration -r win-x64 --self-contained true -o artifacts/app/ContextHost
Run-Dotnet publish src/AiInput.UpdateHost/AiInput.UpdateHost.csproj -c $Configuration -r win-x64 --self-contained true -o artifacts/app/Updater
Run-Dotnet publish tests/AiInput.Windows.Tests/AiInput.Windows.Tests.csproj -c $Configuration -r win-x64 --self-contained true -o artifacts/tests
& artifacts/tests/AiInput.Windows.Tests.exe (Resolve-Path artifacts/app).Path
if ($LASTEXITCODE -ne 0) { throw "Windows integration tests failed" }
Copy-Item README.md,THIRD-PARTY-NOTICES.md artifacts/app/
Copy-Item docs/user-guide.md artifacts/app/使用说明.md
$required=@("AiInputAssistant.exe","AiInputAssistant.dll","AiInputAssistant.pri","ContextHost/AiInput.ContextHost.exe","Updater/AiInput.UpdateHost.exe","Assets/App.ico")
foreach($item in $required){ if(!(Test-Path "artifacts/app/$item")){throw "Missing $item"} }
$smoke=Join-Path (Resolve-Path artifacts) "smoke.txt"
$process=Start-Process artifacts/app/AiInputAssistant.exe -ArgumentList @("--smoke-ui", $smoke) -PassThru
if(!$process.WaitForExit(30000)){Stop-Process -Id $process.Id -Force;throw "UI startup timeout"}
if($process.ExitCode -ne 0 -or !(Test-Path $smoke)){
    if(Test-Path $smoke){Get-Content $smoke}
    $events=Join-Path $env:LOCALAPPDATA 'AiInputAssistant/logs/events.log'
    if(Test-Path $events){Get-Content $events -Tail 8}
    throw "UI startup failed: $($process.ExitCode)"
}
if(!(Get-Content $smoke -Raw).StartsWith("PASS")){throw (Get-Content $smoke -Raw)}
Compress-Archive -Path artifacts/app/* -DestinationPath "artifacts/AiInputAssistant-$version-win-x64.zip" -Force
$compiler="C:/Program Files (x86)/Inno Setup 6/ISCC.exe"
if(!(Test-Path $compiler)){throw "Inno Setup 6 is required to build the installer"}
& $compiler "/DAppVersion=$version" installer/setup.iss
if($LASTEXITCODE -ne 0){throw "Installer compilation failed"}
& ./tests/Test-UpdateInstall.ps1 -Version $version
Get-FileHash artifacts/*.zip,artifacts/*.exe -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts/SHA256SUMS.txt
