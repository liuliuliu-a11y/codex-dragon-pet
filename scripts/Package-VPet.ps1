param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,
      [Parameter(Mandatory=$true)][string]$StageDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [string]$Version='0.8.1', [string]$DotnetRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $DotnetRoot) {
    $sdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($sdk) { $DotnetRoot = Split-Path -Parent $sdk.Source }
    if (-not $DotnetRoot -or -not (Test-Path -LiteralPath (Join-Path $DotnetRoot 'LICENSE.txt'))) { throw 'Specify -DotnetRoot with the .NET SDK directory used for this runtime.' }
}
if (Test-Path -LiteralPath $StageDirectory) { throw 'Use a new package stage directory.' }
$stage = [IO.Path]::GetFullPath($StageDirectory)
New-Item -ItemType Directory -Path (Join-Path $stage 'bin'),(Join-Path $stage 'scripts'),$OutputDirectory -Force | Out-Null
foreach ($child in Get-ChildItem -LiteralPath $RuntimeDirectory) {
    if ($child.Name -eq 'test-evidence' -or $child.Name -match '(?i)Tests\.' -or $child.Extension -eq '.pdb') { continue }
    Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $stage 'bin') -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $stage 'bin/CodexVPet.exe') -Destination (Join-Path $stage 'bin/CodexDragonPet.exe')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /optimize+ /platform:x64 ('/out:' + (Join-Path $stage 'bin/CodexPetWatcher.exe')) /r:System.dll /r:System.Core.dll (Join-Path $project 'src/CodexWatcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Watcher build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Update-VPet.ps1') -Destination (Join-Path $stage 'scripts')
foreach ($name in @('Start-Chat.ps1','Start-WithCodex.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'scripts') }
foreach ($name in @('start-chat.cmd','start-with-codex.cmd')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $project 'docs/WHITE-DRAGON.md') -Destination (Join-Path $stage 'README.md')
Copy-Item -LiteralPath (Join-Path $project 'docs/LOCAL-COMPANION.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'DEPENDENCIES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'docs') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $project 'preview-v0.2.0.png') -Destination $stage
& (Join-Path $PSScriptRoot 'Collect-RuntimeLicenses.ps1') -Destination $stage -DotnetRoot $DotnetRoot
'@echo off', 'start "" "%~dp0bin\CodexDragonPet.exe"' | Set-Content -LiteralPath (Join-Path $stage 'start.cmd') -Encoding ASCII
'@echo off', 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-VPet.ps1"' | Set-Content -LiteralPath (Join-Path $stage 'update.cmd') -Encoding ASCII
if (Get-ChildItem -LiteralPath (Join-Path $stage 'bin/assets/vpet') -Directory | Where-Object Name -eq 'vup') { throw 'Reference character assets must not be included in white-dragon release' }
foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
    $relative = $file.FullName.Substring($stage.TrimEnd('\').Length + 1)
    if ($file.Name -match '(?i)auth\.json|settings\.json|companion-link\.json|vpet-status|\.jsonl$|Tests\.|\.pdb$' -or $relative -match '(?i)(^|[\\/])(profile|sessions|test-evidence|verification|tmp)[\\/]') { throw ('Unintended private/test file in release: ' + $relative) }
}
$archive = Join-Path $OutputDirectory ('codex-white-dragon-' + $Version + '-win-x64.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
Write-Output $archive
