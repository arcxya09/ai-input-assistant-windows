$ErrorActionPreference='Stop'
$root=Join-Path (Resolve-Path artifacts) 'obsidian-fixture'
New-Item $root -ItemType Directory -Force | Out-Null
$setup=Join-Path $root 'setup.exe'
Invoke-WebRequest 'https://github.com/obsidianmd/obsidian-releases/releases/download/v1.13.7/Obsidian-1.13.7.exe' -OutFile $setup
if((Get-FileHash $setup -Algorithm SHA256).Hash.ToLower() -ne 'f233dc24896b3f2d5f9e4b01111181a561d0760b2105f0a474024c5f3143a9bc'){throw 'Obsidian fixture checksum mismatch'}
$extract=Join-Path $root 'extract'
& 7z x $setup "-o$extract" -y | Out-Null
if($LASTEXITCODE -ne 0){throw 'Cannot extract Obsidian installer'}
$archive=Get-ChildItem $extract -Recurse -Filter 'app-64.7z' | Select-Object -First 1
if(!$archive){throw 'Obsidian x64 archive missing'}
$app=Join-Path $root 'app'
& 7z x $archive.FullName "-o$app" -y | Out-Null
if($LASTEXITCODE -ne 0){throw 'Cannot extract Obsidian application'}
$env:OBSIDIAN_TEST_EXE=Join-Path $app 'Obsidian.exe'
if(!(Test-Path $env:OBSIDIAN_TEST_EXE)){throw 'Obsidian executable missing'}
Write-Host 'Verified official Obsidian 1.13.7 desktop fixture; excluded from release packages.'
