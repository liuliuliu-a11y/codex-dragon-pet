param([string]$TargetDirectory, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
$sourceBin = Join-Path $packageRoot 'bin'
foreach ($name in @('CodexDragonPet.exe','CodexPetWatcher.exe','CodexDragonPet.exe.config','CodexPetWatcher.exe.config','assets/layered/body.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceBin $name))) { throw 'Extract the complete release ZIP before running update.cmd.' }
}
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Dragon Pet Link.lnk'
if (-not $TargetDirectory -and (Test-Path -LiteralPath $startupLink)) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startupLink)
    if ([IO.Path]::GetFileName($shortcut.TargetPath) -eq 'CodexPetWatcher.exe') {
        $TargetDirectory = Split-Path -Parent (Split-Path -Parent $shortcut.TargetPath)
    }
}
if (-not $TargetDirectory) { $TargetDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/CodexDragonPet' }
$targetRoot = [IO.Path]::GetFullPath($TargetDirectory)
$targetBin = Join-Path $targetRoot 'bin'
$targetExe = Join-Path $targetBin 'CodexDragonPet.exe'
$targetWatcher = Join-Path $targetBin 'CodexPetWatcher.exe'
if (-not (Test-Path -LiteralPath $targetExe)) { throw 'Existing installation not found. Run start.cmd for a portable launch, or supply -TargetDirectory to this script.' }
if ([IO.Path]::GetFullPath($sourceBin) -eq [IO.Path]::GetFullPath($targetBin)) { throw 'Extract the release to a separate folder before updating.' }
$version = (Get-Item -LiteralPath (Join-Path $sourceBin 'CodexDragonPet.exe')).VersionInfo.ProductVersion
function Stop-TargetProcesses {
    foreach ($process in @(Get-Process -Name CodexPetWatcher,CodexDragonPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe -or $_.Path -eq $targetWatcher } | Sort-Object @{Expression={if($_.ProcessName -eq 'CodexPetWatcher'){0}else{1}}})) {
        if ($process.ProcessName -eq 'CodexDragonPet' -and $process.CloseMainWindow()) {
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }
        } else { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }
    }
}
$linked = @(Get-Process -Name CodexPetWatcher -ErrorAction SilentlyContinue | Where-Object Path -eq $targetWatcher).Count -gt 0
$backup = Join-Path $targetRoot ('backup/update-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Stop-TargetProcesses
try {
    Copy-Item -LiteralPath $targetBin -Destination $backup -Recurse
} catch {
    if (-not $NoLaunch) {
        Start-Process -FilePath $targetExe -WindowStyle Hidden
        if ($linked) { Start-Process -FilePath $targetWatcher -WindowStyle Hidden }
    }
    throw
}
try {
    # Copy only release-owned runtime files; user settings live outside this directory.
    foreach ($name in @('CodexDragonPet.exe','CodexDragonPet.exe.config','CodexPetWatcher.exe','CodexPetWatcher.exe.config')) {
        Copy-Item -LiteralPath (Join-Path $sourceBin $name) -Destination $targetBin -Force
    }
    Copy-Item -LiteralPath (Join-Path $sourceBin 'assets') -Destination $targetBin -Recurse -Force
    foreach ($source in Get-ChildItem -LiteralPath $sourceBin -Recurse -File) {
        # Test executables and other development files are never installed.
        $relative = $source.FullName.Substring([IO.Path]::GetFullPath($sourceBin).TrimEnd('\').Length + 1)
        if ($relative -notmatch '^assets[\\/]' -and $source.Name -notin @('CodexDragonPet.exe','CodexDragonPet.exe.config','CodexPetWatcher.exe','CodexPetWatcher.exe.config')) { continue }
        if ((Get-FileHash -LiteralPath $source.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $targetBin $relative)).Hash) { throw "Runtime verification failed: $relative" }
    }
    if ((Get-Item -LiteralPath $targetExe).VersionInfo.ProductVersion -ne $version) { throw 'Installed version differs from the package.' }
} catch {
    $problem = $_
    # Restore each saved child without deleting user files or expanding wildcard paths.
    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $backup 'bin')) { Copy-Item -LiteralPath $item.FullName -Destination $targetBin -Recurse -Force }
    if (-not $NoLaunch) {
        Start-Process -FilePath $targetExe -WindowStyle Hidden
        if ($linked) { Start-Process -FilePath $targetWatcher -WindowStyle Hidden }
    }
    throw $problem
}
if (-not $NoLaunch) {
    Start-Process -FilePath $targetExe -WindowStyle Hidden
    if ($linked) { Start-Process -FilePath $targetWatcher -WindowStyle Hidden }
}
Write-Output "Updated to $version. Settings and startup shortcut preserved. Backup: $backup"
