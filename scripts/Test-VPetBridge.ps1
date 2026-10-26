param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,
      [Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$profile = Join-Path $evidence 'profile'
$sessions = Join-Path $evidence 'sessions'
New-Item -ItemType Directory -Path $profile,$sessions -Force | Out-Null
$log = Join-Path $sessions 'rollout-00000000-0000-0000-0000-000000000908.jsonl'
function Write-Event([string]$payload) {
    $record = '{"timestamp":"' + [DateTime]::UtcNow.ToString('o') + '","type":"event_msg","payload":' + $payload + '}'
    Add-Content -LiteralPath $log -Value $record -Encoding UTF8
}
function Wait-Check([scriptblock]$predicate, [string]$label, [int]$seconds=30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        if ($script:hostProcess.HasExited) { throw ('Host exited during ' + $label) }
        try { if (& $predicate) { Write-Output ('PASS ' + $label); return } } catch [IO.IOException] { } catch [System.ArgumentException] { }
        Start-Sleep -Milliseconds 400
    }
    throw ('Timed out: ' + $label)
}
function Snapshot { Get-Content -LiteralPath (Join-Path $profile 'vpet-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Live { Get-Content -LiteralPath (Join-Path $evidence 'live.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
Write-Event '{"type":"task_started","turn_id":"bridge-1"}'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $RuntimeDirectory 'CodexVPet.exe'
$start.UseShellExecute = $false
$start.ArgumentList.Add('--verify'); $start.ArgumentList.Add($evidence)
$start.Environment['CODEX_WHITE_DRAGON_DATA_DIR'] = $profile
$start.Environment['CODEX_HOME'] = $evidence
$start.Environment['CODEX_PET_DATA_DIR'] = Join-Path $evidence 'backend-settings'
$script:hostProcess = [Diagnostics.Process]::Start($start)
try {
    Wait-Check { (Test-Path -LiteralPath (Join-Path $profile 'vpet-status.json')) -and (Snapshot).running } 'real backend and host start on isolated work'
    Write-Event '{"type":"agent_reasoning","text":"SECRET TEST THOUGHT"}'
    Write-Event '{"type":"agent_message","message":"正在测试公开进展"}'
    Write-Event '{"type":"task_complete","turn_id":"bridge-1","last_agent_message":"First result"}'
    Wait-Check { (Snapshot).completionSerial -eq 1 -and -not (Snapshot).running } 'first task completion crosses real bridge'
    Wait-Check { (Test-Path -LiteralPath (Join-Path $evidence 'live.json')) -and (Live).notificationCount -eq 1 } 'avatar consumes first completion once'
    $child = @(Get-CimInstance Win32_Process -Filter "Name='CodexStateBridge.exe'" | Where-Object ParentProcessId -eq $script:hostProcess.Id)
    if ($child.Count -ne 1) { throw 'Expected one owned bridge process' }
    $oldBridge = $child[0].ProcessId; Stop-Process -Id $oldBridge -Force
    Wait-Check { @(Get-CimInstance Win32_Process -Filter "Name='CodexStateBridge.exe'" | Where-Object { $_.ParentProcessId -eq $script:hostProcess.Id -and $_.ProcessId -ne $oldBridge }).Count -eq 1 } 'bridge restarts automatically after controlled termination' 40
    Wait-Check { (Snapshot).completionSerial -eq 0 -and (([DateTime]::UtcNow - [DateTime](Snapshot).updatedAt).TotalSeconds -lt 5) } 'new bridge epoch restores fresh status'
    Wait-Check { (Live).observedCompletion -eq 0 -and (Live).notificationCount -eq 1 } 'restart does not replay the old completion'
    Write-Event '{"type":"task_started","turn_id":"bridge-2"}'
    Wait-Check { (Snapshot).running } 'second task enters working state'
    Write-Event '{"type":"task_complete","turn_id":"bridge-2","last_agent_message":"Second result"}'
    Wait-Check { (Live).notificationCount -eq 2 } 'second completion notifies once after bridge restart'
    $snapshotText = Get-Content -LiteralPath (Join-Path $profile 'vpet-status.json') -Raw
    if ($snapshotText -match 'SECRET TEST THOUGHT') { throw 'Non-public content leaked to host' }
    @{passed=$true;host=$script:hostProcess.Id;bridgeRestarted=$true;notifications=2;privateContentWithheld=$true} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'bridge-integration.json') -Encoding UTF8
    Write-Output 'PASS public-status boundary remains intact'
} finally {
    if (-not $script:hostProcess.HasExited) {
        if (-not $script:hostProcess.CloseMainWindow() -or -not $script:hostProcess.WaitForExit(5000)) { $script:hostProcess.Kill(); $script:hostProcess.WaitForExit(5000) | Out-Null }
    }
    $script:hostProcess.Dispose()
}
