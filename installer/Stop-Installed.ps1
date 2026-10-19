param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference = 'Stop'
$targetRoot = [IO.Path]::GetFullPath($InstallRoot)
$petPath = Join-Path $targetRoot 'bin/CodexDragonPet.exe'
$watcherPath = Join-Path $targetRoot 'bin/CodexPetWatcher.exe'
$currentSession = (Get-Process -Id $PID).SessionId

# Stop only this installation, leaving Codex and other copies untouched.
foreach ($name in @('CodexPetWatcher','CodexDragonPet')) {
    foreach ($process in Get-Process -Name $name -ErrorAction SilentlyContinue) {
        if ($process.SessionId -ne $currentSession) { continue }
        $expected = if ($name -eq 'CodexPetWatcher') { $watcherPath } else { $petPath }
        if ($process.Path -ne $expected) { continue }
        if ($name -eq 'CodexDragonPet') {
            $null = $process.CloseMainWindow()
            $null = $process.WaitForExit(3000)
        }
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -ErrorAction Stop
            if (-not $process.WaitForExit(3000)) { throw 'The installed companion did not exit.' }
        }
    }
}

# Recreate selected shortcuts on upgrade; do not remove another copy's links.
$shellObject = New-Object -ComObject WScript.Shell
$ownedLinks = @(
    @{Folder='Startup';Name='Codex Dragon Pet Link.lnk';Target=$watcherPath},
    @{Folder='Desktop';Name='Codex Dragon Pet.lnk';Target=$petPath}
)
foreach ($item in $ownedLinks) {
    $linkPath = Join-Path ([Environment]::GetFolderPath($item.Folder)) $item.Name
    if (Test-Path -LiteralPath $linkPath) {
        $link = $shellObject.CreateShortcut($linkPath)
        if ($link.TargetPath -eq $item.Target) { Remove-Item -LiteralPath $linkPath }
    }
}
