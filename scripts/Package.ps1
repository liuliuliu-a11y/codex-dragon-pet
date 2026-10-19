param([string]$Version = '0.2.0', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'dist' }
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$stage = Join-Path $destination ('codex-dragon-pet-' + $Version)
New-Item -ItemType Directory -Path (Join-Path $stage 'bin/assets') -Force | Out-Null
foreach ($file in @('CodexDragonPet.exe','CodexDragonPet.exe.config','CodexPetWatcher.exe','CodexPetWatcher.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('bin/' + $file)) -Destination (Join-Path $stage 'bin') -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/dragon-sprites-clean.png') -Destination (Join-Path $stage 'bin/assets') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/tray.ico') -Destination (Join-Path $stage 'bin/assets') -Force
foreach ($file in @('start.cmd','start-with-codex.cmd','README.md','DEPENDENCIES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'preview-v0.2.0.png') -Destination $stage -Force
New-Item -ItemType Directory -Path (Join-Path $stage 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/Start-WithCodex.ps1') -Destination (Join-Path $stage 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/Install.ps1'),(Join-Path $projectRoot 'scripts/Disable-CodexLink.ps1') -Destination (Join-Path $stage 'scripts') -Force
$archive = Join-Path $destination ('codex-dragon-pet-' + $Version + '.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
$checksum = Get-FileHash -LiteralPath $archive -Algorithm SHA256
($checksum.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
Write-Output $archive
