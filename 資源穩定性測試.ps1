param(
    [double]$DurationHours = 3,
    [int]$DurationMinutes = 0,
    [int]$IntervalSeconds = 60
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GuiResourceReader {
    [DllImport("user32.dll")]
    public static extern int GetGuiResources(IntPtr process, int flag);
}
'@

$dataDir = Join-Path $env:LOCALAPPDATA 'CodexUsageWidget'
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$csvPath = Join-Path $dataDir "stability-$stamp.csv"
$summaryPath = Join-Path $dataDir "stability-$stamp-summary.json"
$latestPath = Join-Path $dataDir 'stability-latest.txt'
$duration = if ($DurationMinutes -gt 0) {
    [TimeSpan]::FromMinutes($DurationMinutes)
} else {
    [TimeSpan]::FromHours([Math]::Max(0.01, $DurationHours))
}
$deadline = [DateTime]::Now.Add($duration)
$previousCpu = $null
$samples = [System.Collections.Generic.List[object]]::new()

while ([DateTime]::Now -lt $deadline) {
    $process = Get-Process -Name 'CodexUsageWidget' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process) {
        $cpuDelta = if ($null -eq $previousCpu) { 0 } else { [Math]::Max(0, $process.CPU - $previousCpu) }
        $previousCpu = $process.CPU
        $sample = [pscustomobject]@{
            Timestamp = [DateTimeOffset]::Now.ToString('o')
            ProcessId = $process.Id
            Responding = $process.Responding
            WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 3)
            PrivateMemoryMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 3)
            CpuDeltaSeconds = [Math]::Round($cpuDelta, 4)
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
            GdiObjects = [GuiResourceReader]::GetGuiResources($process.Handle, 0)
            UserObjects = [GuiResourceReader]::GetGuiResources($process.Handle, 1)
        }
        $samples.Add($sample)
        $sample | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8 -Append
    } else {
        [pscustomobject]@{
            Timestamp = [DateTimeOffset]::Now.ToString('o')
            ProcessId = ''
            Responding = $false
            WorkingSetMB = ''
            PrivateMemoryMB = ''
            CpuDeltaSeconds = ''
            Handles = ''
            Threads = ''
            GdiObjects = ''
            UserObjects = ''
        } | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8 -Append
    }
    Start-Sleep -Seconds ([Math]::Max(1, $IntervalSeconds))
}

$first = $samples | Select-Object -First 1
$last = $samples | Select-Object -Last 1
$summary = [ordered]@{
    started_at = if ($first) { $first.Timestamp } else { $null }
    completed_at = [DateTimeOffset]::Now.ToString('o')
    requested_duration_minutes = [Math]::Round($duration.TotalMinutes, 2)
    samples = $samples.Count
    process_remained_running = ($samples.Count -gt 0 -and $last.Responding)
    working_set_start_mb = if ($first) { $first.WorkingSetMB } else { $null }
    working_set_end_mb = if ($last) { $last.WorkingSetMB } else { $null }
    working_set_max_mb = if ($samples.Count) { ($samples | Measure-Object WorkingSetMB -Maximum).Maximum } else { $null }
    private_memory_start_mb = if ($first) { $first.PrivateMemoryMB } else { $null }
    private_memory_end_mb = if ($last) { $last.PrivateMemoryMB } else { $null }
    handles_start = if ($first) { $first.Handles } else { $null }
    handles_end = if ($last) { $last.Handles } else { $null }
    handles_max = if ($samples.Count) { ($samples | Measure-Object Handles -Maximum).Maximum } else { $null }
    gdi_start = if ($first) { $first.GdiObjects } else { $null }
    gdi_end = if ($last) { $last.GdiObjects } else { $null }
    user_start = if ($first) { $first.UserObjects } else { $null }
    user_end = if ($last) { $last.UserObjects } else { $null }
    cpu_total_sampled_seconds = if ($samples.Count) { [Math]::Round(($samples | Measure-Object CpuDeltaSeconds -Sum).Sum, 4) } else { $null }
    csv_path = $csvPath
}
$summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
@($csvPath, $summaryPath) | Set-Content -LiteralPath $latestPath -Encoding UTF8

