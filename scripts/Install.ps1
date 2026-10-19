param([switch]$NoAutoLink)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$pet = Join-Path $projectRoot 'bin/CodexDragonPet.exe'
$watcher = Join-Path $projectRoot 'bin/CodexPetWatcher.exe'
if (-not (Test-Path -LiteralPath $pet) -or -not (Test-Path -LiteralPath $watcher)) { & (Join-Path $projectRoot 'build.ps1') }
$shellObject = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$link = $shellObject.CreateShortcut((Join-Path $desktop 'Codex Dragon Pet.lnk'))
$link.TargetPath = $pet; $link.WorkingDirectory = (Split-Path $pet)
$link.IconLocation = (Join-Path $projectRoot 'bin/assets/tray.ico') + ',0'; $link.Save()
if (-not $NoAutoLink) {
    $startup = [Environment]::GetFolderPath('Startup')
    $entry = $shellObject.CreateShortcut((Join-Path $startup 'Codex Dragon Pet Link.lnk'))
    $entry.TargetPath = $watcher; $entry.WorkingDirectory = (Split-Path $watcher)
    $entry.Description = 'Start the dragon companion when the Codex desktop host opens.'
    $entry.IconLocation = (Join-Path $projectRoot 'bin/assets/tray.ico') + ',0'; $entry.Save()
    Start-Process -FilePath $watcher -WindowStyle Hidden | Out-Null
}
Start-Process -FilePath $pet -WindowStyle Normal | Out-Null
[pscustomobject]@{Installed=$true;AutoLinkEnabled=(-not $NoAutoLink);ProjectRoot=$projectRoot}
