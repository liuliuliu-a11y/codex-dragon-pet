param([switch]$PetOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$petExecutable = Join-Path $projectRoot 'bin/CodexDragonPet.exe'
if (-not (Test-Path -LiteralPath $petExecutable)) {
    & (Join-Path $projectRoot 'build.ps1')
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Pet build failed.' }
}
$codexRequested = $false
if (-not $PetOnly) {
    $codexWindow = Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $codexWindow) {
        $app = Get-StartApps | Where-Object { $_.AppID -like 'OpenAI.Codex_*!*' } | Select-Object -First 1
        if ($app) {
            Start-Process -FilePath 'explorer.exe' -ArgumentList ('shell:AppsFolder\' + $app.AppID) -WindowStyle Normal | Out-Null
            $codexRequested = $true
        } else {
            Write-Warning 'Codex desktop app was not found. Open Codex manually; the pet will still start.'
        }
    } else { $codexRequested = $true }
}
Start-Process -FilePath $petExecutable -WindowStyle Normal | Out-Null
[pscustomobject]@{ CodexStartedOrAlreadyOpen=$codexRequested; PetStartRequested=$true }
