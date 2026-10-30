param([string]$PetExecutable)
$ErrorActionPreference = 'Stop'
if (-not $PetExecutable) {
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Dragon Pet Link.lnk'
    if (Test-Path -LiteralPath $startup) {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startup)
        $PetExecutable = Join-Path (Split-Path -Parent $shortcut.TargetPath) 'CodexDragonPet.exe'
    }
    if (-not $PetExecutable -or -not (Test-Path -LiteralPath $PetExecutable)) {
        $PetExecutable = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin/CodexDragonPet.exe'
    }
}
if (-not (Test-Path -LiteralPath $PetExecutable)) { throw 'Please install the complete companion-enabled desktop pet first.' }
$version = (Get-Item -LiteralPath $PetExecutable).VersionInfo
$number = [version]('{0}.{1}.{2}.{3}' -f $version.FileMajorPart,$version.FileMinorPart,$version.FileBuildPart,$version.FilePrivatePart)
if ($number -lt [version]'0.7.1.0') { throw 'The installed desktop pet does not yet include this chat entry. Update the local companion build first.' }
Start-Process -FilePath $PetExecutable -ArgumentList '--chat' -WindowStyle Normal
