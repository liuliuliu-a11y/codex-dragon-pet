$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$watcher = [IO.Path]::GetFullPath((Join-Path $projectRoot 'bin/CodexPetWatcher.exe'))
foreach ($process in Get-Process -Name CodexPetWatcher -ErrorAction SilentlyContinue) {
    if ($process.Path -eq $watcher) { Stop-Process -Id $process.Id }
}
$entryPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Dragon Pet Link.lnk'
if (Test-Path -LiteralPath $entryPath) {
    $shellObject = New-Object -ComObject WScript.Shell
    $entry = $shellObject.CreateShortcut($entryPath)
    if ($entry.TargetPath -eq $watcher) { Remove-Item -LiteralPath $entryPath }
}
Write-Output 'Automatic Codex linking is disabled for this installation. The pet and its settings were kept.'
