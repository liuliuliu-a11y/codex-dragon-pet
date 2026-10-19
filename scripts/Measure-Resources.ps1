param(
    [Parameter(Mandatory=$true)][int]$PetProcessId,
    [int]$Seconds = 25,
    [int]$WarmupSeconds = 5,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$target = Get-Process -Id $PetProcessId
if ($WarmupSeconds -gt 0) { Start-Sleep -Seconds $WarmupSeconds }
$engineCounters = @()
$memoryCounters = @()
try {
    $gpuCategory = [System.Diagnostics.PerformanceCounterCategory]::new('GPU Engine')
    foreach ($instance in $gpuCategory.GetInstanceNames()) {
        if ($instance -like "pid_${PetProcessId}_*") {
            $counter = [System.Diagnostics.PerformanceCounter]::new('GPU Engine','Utilization Percentage',$instance,$true)
            $null = $counter.NextValue()
            $engineCounters += $counter
        }
    }
    $gpuMemoryCategory = [System.Diagnostics.PerformanceCounterCategory]::new('GPU Process Memory')
    foreach ($instance in $gpuMemoryCategory.GetInstanceNames()) {
        if ($instance -like "pid_${PetProcessId}_*") {
            foreach ($name in @('Dedicated Usage','Shared Usage')) {
                $counter = [System.Diagnostics.PerformanceCounter]::new('GPU Process Memory',$name,$instance,$true)
                $null = $counter.NextValue()
                $memoryCounters += $counter
            }
        }
    }
} catch { }
$samples = @()
$previousCpu = $target.TotalProcessorTime.TotalSeconds
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$previousTime = $watch.Elapsed.TotalSeconds
try {
    for ($sampleIndex=0; $sampleIndex -lt $Seconds; $sampleIndex++) {
        Start-Sleep -Milliseconds 1000
        $target.Refresh()
        if ($target.HasExited) { throw 'The target exited during profiling.' }
        $now = $watch.Elapsed.TotalSeconds
        $cpuNow = $target.TotalProcessorTime.TotalSeconds
        $cpu = 100 * ($cpuNow - $previousCpu) / ($now - $previousTime) / [Environment]::ProcessorCount
        $previousCpu=$cpuNow; $previousTime=$now
        $gpuValues = @()
        foreach ($counter in $engineCounters) {
            try { $gpuValues += [Math]::Max(0,[double]$counter.NextValue()) } catch { }
        }
        $dedicated = @(); $shared = @()
        foreach ($counter in $memoryCounters) {
            try {
                $usage = [double]$counter.NextValue()/1MB
                if ($counter.CounterName -eq 'Dedicated Usage') { $dedicated += $usage } else { $shared += $usage }
            } catch { }
        }
        $samples += [pscustomobject]@{
            at = [DateTime]::UtcNow.ToString('o')
            cpuPercent = [Math]::Round($cpu,4)
            workingSetMB = [Math]::Round($target.WorkingSet64/1MB,2)
            privateMB = [Math]::Round($target.PrivateMemorySize64/1MB,2)
            gpuPercent = $(if ($gpuValues.Count) { [Math]::Round(($gpuValues | Measure-Object -Maximum).Maximum,4) } else { $null })
            dedicatedGpuMB = $(if ($dedicated.Count) { [Math]::Round(($dedicated | Measure-Object -Sum).Sum,2) } else { $null })
            sharedGpuMB = $(if ($shared.Count) { [Math]::Round(($shared | Measure-Object -Sum).Sum,2) } else { $null })
        }
    }
} finally {
    foreach ($counter in @($engineCounters)+@($memoryCounters)) { $counter.Dispose() }
    $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $validGpu = @($samples | Where-Object { $null -ne $_.gpuPercent })
    $result = [ordered]@{
        processId=$PetProcessId; sampleCount=$samples.Count; logicalProcessors=[Environment]::ProcessorCount
        cpuMeanPercent=($samples | Measure-Object cpuPercent -Average).Average
        cpuMaxPercent=($samples | Measure-Object cpuPercent -Maximum).Maximum
        workingSetMeanMB=($samples | Measure-Object workingSetMB -Average).Average
        workingSetPeakMB=($samples | Measure-Object workingSetMB -Maximum).Maximum
        privateMeanMB=($samples | Measure-Object privateMB -Average).Average
        gpuCounterAvailable=($validGpu.Count -gt 0)
        gpuMeanPercent=$(if ($validGpu.Count) { ($validGpu | Measure-Object gpuPercent -Average).Average } else { $null })
        gpuMaxPercent=$(if ($validGpu.Count) { ($validGpu | Measure-Object gpuPercent -Maximum).Maximum } else { $null })
        note='CPU is normalized over logical processors. GPU is the busiest reported engine for this PID; desktop compositor cost is not included. Null GPU means unavailable, not zero. Working set is not forcibly trimmed.'
        samples=$samples
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    $saved = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    if ($saved.sampleCount -ne $samples.Count) { throw 'Resource result write verification failed.' }
    $saved | Select-Object sampleCount,cpuMeanPercent,workingSetMeanMB,privateMeanMB,gpuCounterAvailable,gpuMeanPercent | ConvertTo-Json -Compress
}
