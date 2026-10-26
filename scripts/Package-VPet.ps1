param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,
      [Parameter(Mandatory=$true)][string]$StageDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [string]$Version='0.7.0')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (Test-Path -LiteralPath $StageDirectory) { throw 'Use a new package stage directory.' }
$stage = [IO.Path]::GetFullPath($StageDirectory)
New-Item -ItemType Directory -Path (Join-Path $stage 'bin'),(Join-Path $stage 'scripts'),$OutputDirectory -Force | Out-Null
foreach ($child in Get-ChildItem -LiteralPath $RuntimeDirectory) {
    if ($child.Name -in @('BridgeTests.exe','test-evidence') -or $child.Extension -eq '.pdb') { continue }
    Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $stage 'bin') -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $stage 'bin/CodexVPet.exe') -Destination (Join-Path $stage 'bin/CodexDragonPet.exe')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /optimize+ /platform:x64 ('/out:' + (Join-Path $stage 'bin/CodexPetWatcher.exe')) /r:System.dll /r:System.Core.dll (Join-Path $project 'src/CodexWatcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Watcher build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Update-VPet.ps1') -Destination (Join-Path $stage 'scripts')
Copy-Item -LiteralPath (Join-Path $project 'docs/WHITE-DRAGON.md') -Destination (Join-Path $stage 'README.md')
'@echo off', 'start "" "%~dp0bin\CodexDragonPet.exe"' | Set-Content -LiteralPath (Join-Path $stage 'start.cmd') -Encoding ASCII
'@echo off', 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-VPet.ps1"' | Set-Content -LiteralPath (Join-Path $stage 'update.cmd') -Encoding ASCII
if (Get-ChildItem -LiteralPath (Join-Path $stage 'bin/assets/vpet') -Directory | Where-Object Name -eq 'vup') { throw 'Reference character assets must not be included in white-dragon release' }
if (Get-ChildItem -LiteralPath $stage -File -Recurse | Where-Object { $_.Name -match 'auth\.json|settings\.json|\.jsonl$|Tests\.exe$' }) { throw 'Unintended private/test files in release' }
$archive = Join-Path $OutputDirectory ('codex-white-dragon-' + $Version + '-win-x64.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
Write-Output $archive
