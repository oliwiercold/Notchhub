# Prints one JSON object to standard output — that's the whole contract of a "command" data source.
# Read-only: queries WMI/CIM, changes nothing.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'SilentlyContinue'

$os = Get-CimInstance Win32_OperatingSystem
$boot = $os.LastBootUpTime
$up = (Get-Date) - $boot
$totalGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$freeGb = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
$usedGb = [math]::Round($totalGb - $freeGb, 1)

$bat = Get-CimInstance Win32_Battery | Select-Object -First 1
$hasBattery = $null -ne $bat

$uptimeText = if ($up.Days -gt 0) { '{0} d {1} h' -f $up.Days, $up.Hours } else { '{0} h {1} min' -f $up.Hours, $up.Minutes }

[ordered]@{
    computer       = $env:COMPUTERNAME
    user           = $env:USERNAME
    os             = $os.Caption
    uptimeText     = $uptimeText
    uptimeHours    = [math]::Round($up.TotalHours, 1)
    bootTime       = $boot.ToString('yyyy-MM-dd HH:mm')
    memoryUsedGb   = $usedGb
    memoryTotalGb  = $totalGb
    memoryPercent  = [math]::Round(100 * $usedGb / [math]::Max($totalGb, 0.1))
    memoryFraction = [math]::Round($usedGb / [math]::Max($totalGb, 0.1), 3)
    hasBattery     = $hasBattery
    batteryPercent = if ($hasBattery) { [int]$bat.EstimatedChargeRemaining } else { $null }
    processes      = (Get-Process).Count
} | ConvertTo-Json -Compress
