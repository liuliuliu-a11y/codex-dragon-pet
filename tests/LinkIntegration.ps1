param(
    [Parameter(Mandatory=$true)][string]$BinaryDirectory,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$StartupShortcut
)
$ErrorActionPreference = 'Stop'
$bin = [IO.Path]::GetFullPath($BinaryDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$petPath = Join-Path $bin 'CodexDragonPet.exe'
$watcherPath = Join-Path $bin 'CodexPetWatcher.exe'
foreach ($path in @($petPath,$watcherPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw ('Missing binary: ' + $path) }
}
$session = (Get-Process -Id $PID).SessionId
foreach ($process in Get-Process -Name CodexDragonPet,CodexPetWatcher -ErrorAction SilentlyContinue) {
    if ($process.SessionId -eq $session -and $process.Path -notin @($petPath,$watcherPath)) {
        throw 'Exit the other companion installation before this integration test.'
    }
}
function OwnProcesses([string]$Name,[string]$Path) {
    @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session -and $_.Path -eq $Path })
}
function StopOwned {
    foreach ($process in @(OwnProcesses 'CodexPetWatcher' $watcherPath) + @(OwnProcesses 'CodexDragonPet' $petPath)) {
        Stop-Process -Id $process.Id
        if (-not $process.WaitForExit(3000)) { throw 'A test companion process did not exit.' }
    }
}
function Await([scriptblock]$Condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds(6)
    do { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}
$oldData = $env:CODEX_PET_DATA_DIR; $oldCodex = $env:CODEX_HOME
$fixture = $null; $checks = [ordered]@{}
try {
    StopOwned
    $env:CODEX_PET_DATA_DIR = Join-Path $evidence 'pet-data'
    $env:CODEX_HOME = Join-Path $evidence 'empty-codex'
    New-Item -ItemType Directory -Path (Join-Path $env:CODEX_HOME 'sessions') -Force | Out-Null
    if ($StartupShortcut) {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($StartupShortcut)
        if ($shortcut.TargetPath -ne $watcherPath) { throw 'Startup shortcut points to a different installation.' }
        Start-Process -FilePath $StartupShortcut | Out-Null
    } else { Start-Process -FilePath $watcherPath -WindowStyle Hidden | Out-Null }
    $checks.helperStarted = Await { (OwnProcesses 'CodexPetWatcher' $watcherPath).Count -eq 1 }
    if (@(Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session -and $_.MainWindowHandle -ne 0 }).Count) {
        $checks.existingCodexStartsPet = Await { (OwnProcesses 'CodexDragonPet' $petPath).Count -eq 1 }
    }
    foreach ($process in OwnProcesses 'CodexDragonPet' $petPath) { Stop-Process -Id $process.Id; $null = $process.WaitForExit(3000) }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $hostPath = Join-Path $evidence 'Codex.exe'
    & $compiler /nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+$hostPath) (Join-Path $PSScriptRoot 'LinkHost.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Host fixture compilation failed.' }
    $fixture = Start-Process -FilePath $hostPath -ArgumentList ('"'+$evidence+'"') -WindowStyle Normal -PassThru
    if (-not (Await { Test-Path -LiteralPath (Join-Path $evidence 'first.txt') })) { throw 'First host window did not appear.' }
    $checks.firstWindowStartsPet = Await { (OwnProcesses 'CodexDragonPet' $petPath).Count -eq 1 }
    foreach ($process in OwnProcesses 'CodexDragonPet' $petPath) { Stop-Process -Id $process.Id; $null = $process.WaitForExit(3000) }
    'second' | Set-Content -LiteralPath (Join-Path $evidence 'command.txt') -Encoding ASCII
    if (-not (Await { Test-Path -LiteralPath (Join-Path $evidence 'second.txt') })) { throw 'Second host window did not appear.' }
    $firstMark = (Get-Content -LiteralPath (Join-Path $evidence 'first.txt') -Raw).Split(':')
    $secondMark = (Get-Content -LiteralPath (Join-Path $evidence 'second.txt') -Raw).Split(':')
    $checks.sameProcessNewWindow = $firstMark[0] -eq $secondMark[0] -and $firstMark[1] -ne $secondMark[1]
    $checks.secondWindowRestartsPet = Await { (OwnProcesses 'CodexDragonPet' $petPath).Count -eq 1 }
    Start-Process -FilePath $watcherPath -WindowStyle Hidden | Out-Null
    Start-Sleep -Milliseconds 300
    $checks.singleHelper = (OwnProcesses 'CodexPetWatcher' $watcherPath).Count -eq 1
    $checks.singlePet = (OwnProcesses 'CodexDragonPet' $petPath).Count -eq 1
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    $checks | ConvertTo-Json
    if (@($checks.Values | Where-Object { -not $_ }).Count) { throw 'Native Codex link integration failed.' }
} finally {
    StopOwned
    if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $null = $fixture.WaitForExit(3000) }
    $env:CODEX_PET_DATA_DIR = $oldData; $env:CODEX_HOME = $oldCodex
}
