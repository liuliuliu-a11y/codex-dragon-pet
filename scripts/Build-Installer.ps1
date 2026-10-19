param([string]$Version = '0.2.1', [string]$CompilerPath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use the format major.minor.patch.' }
if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 7/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 7, or provide -CompilerPath pointing to ISCC.exe.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'dist' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$buildOutput = Join-Path $projectRoot ('verification/installer-build-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $projectRoot 'build.ps1') -Test -UiTest -OutputDirectory $buildOutput
$stage = Join-Path $output ('installer-payload-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'Package.ps1') -Version $Version -OutputDirectory $output -StageDirectory $stage -BinaryDirectory $buildOutput | Out-Null
& $CompilerPath ('/DAppVersion=' + $Version) ('/DPayloadDir=' + $stage) ('/DOutputDir=' + $output) (Join-Path $projectRoot 'installer/CodexDragonPet.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setup = Join-Path $output ('CodexDragonPet-Setup-' + $Version + '.exe')
if (-not (Test-Path -LiteralPath $setup)) { throw 'Installer output is missing.' }
$archive = Join-Path $output ('codex-dragon-pet-' + $Version + '.zip')
$manifest = foreach ($file in @($setup,$archive)) {
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file)
}
$manifest | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Output $setup
