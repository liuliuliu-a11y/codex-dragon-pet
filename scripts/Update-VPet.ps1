param([string]$TargetDirectory, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$sourceBin = Join-Path $package 'bin'
foreach ($name in @('CodexDragonPet.exe','CodexVPet.dll','CodexStateBridge.exe','assets/vpet/vup.lps')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceBin $name))) { throw 'Please extract the complete white-dragon ZIP first.' }
}
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Dragon Pet Link.lnk'
if (-not $TargetDirectory -and (Test-Path -LiteralPath $startupLink)) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startupLink)
    if ([IO.Path]::GetFileName($shortcut.TargetPath) -eq 'CodexPetWatcher.exe') { $TargetDirectory = Split-Path -Parent (Split-Path -Parent $shortcut.TargetPath) }
}
if (-not $TargetDirectory) { $TargetDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/CodexDragonPet' }
$target = [IO.Path]::GetFullPath($TargetDirectory)
$targetBin = Join-Path $target 'bin'
if ([IO.Path]::GetFullPath($sourceBin) -eq [IO.Path]::GetFullPath($targetBin)) { throw 'Extract to a new folder before updating.' }
$watcherWasRunning = $false
$petWasRunning = $false
foreach ($process in @(Get-Process -Name CodexVPet,CodexDragonPet,CodexStateBridge,CodexPetWatcher -ErrorAction SilentlyContinue | Sort-Object @{Expression={if($_.ProcessName -eq 'CodexPetWatcher'){0}else{1}}})) {
    if ($process.HasExited) { continue }
    if (-not $process.Path -or [IO.Path]::GetDirectoryName($process.Path).TrimEnd('\') -ne $targetBin.TrimEnd('\')) { continue }
    if ($process.ProcessName -eq 'CodexPetWatcher') { $watcherWasRunning = $true }
    if ($process.ProcessName -in @('CodexVPet','CodexDragonPet')) { $petWasRunning = $true }
    if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }
}
$backup = Join-Path $target ('backup/white-dragon-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$backupComplete = $false
try {
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    if (Test-Path -LiteralPath $targetBin) { Copy-Item -LiteralPath $targetBin -Destination $backup -Recurse }
    $backupComplete = $true
    New-Item -ItemType Directory -Path $targetBin -Force | Out-Null
    foreach ($child in Get-ChildItem -LiteralPath $sourceBin) { Copy-Item -LiteralPath $child.FullName -Destination $targetBin -Recurse -Force }
    foreach ($file in Get-ChildItem -LiteralPath $sourceBin -File -Recurse) {
        $relative = $file.FullName.Substring([IO.Path]::GetFullPath($sourceBin).TrimEnd('\').Length + 1)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $targetBin $relative)).Hash) { throw ('Installed file differs: ' + $relative) }
    }
} catch {
    $failure = $_
    if ($backupComplete -and (Test-Path -LiteralPath (Join-Path $backup 'bin'))) {
        foreach ($child in Get-ChildItem -LiteralPath (Join-Path $backup 'bin')) { Copy-Item -LiteralPath $child.FullName -Destination $targetBin -Recurse -Force }
    }
    if ($petWasRunning -and (Test-Path -LiteralPath (Join-Path $targetBin 'CodexDragonPet.exe'))) { Start-Process -FilePath (Join-Path $targetBin 'CodexDragonPet.exe') }
    if ($watcherWasRunning -and (Test-Path -LiteralPath (Join-Path $targetBin 'CodexPetWatcher.exe'))) { Start-Process -FilePath (Join-Path $targetBin 'CodexPetWatcher.exe') -WindowStyle Hidden }
    throw $failure
}
if (-not $NoLaunch) {
    Start-Process -FilePath (Join-Path $targetBin 'CodexDragonPet.exe')
    if ($watcherWasRunning) { Start-Process -FilePath (Join-Path $targetBin 'CodexPetWatcher.exe') -WindowStyle Hidden }
}
Write-Output ('White dragon updated. Settings preserved. Backup: ' + $backup)
