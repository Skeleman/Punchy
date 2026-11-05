# Shows Unity editor RAM use and how many more editors fit (COORDINATION 14/15).
# Rule: about 5 GB per editor, keep 4 GB free. Read-only; changes nothing.
$os = Get-CimInstance Win32_OperatingSystem
$totalGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$freeGB  = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
$pf = Get-CimInstance Win32_PageFileUsage | Measure-Object -Property CurrentUsage, AllocatedBaseSize -Sum
$pfUsed  = [math]::Round(($pf | Where-Object Property -eq CurrentUsage).Sum / 1KB, 1)
$pfSize  = [math]::Round(($pf | Where-Object Property -eq AllocatedBaseSize).Sum / 1KB, 1)

$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | ForEach-Object {
    $proj = if ($_.CommandLine -match '-projectpath\s+"?([^"]+?)"?(\s+-|$)') { $Matches[1] } else { '?' }
    [pscustomobject]@{ PID = $_.ProcessId; GB = [math]::Round($_.WorkingSetSize / 1GB, 1); Project = $proj }
} | Where-Object Project -ne '?'   # unnamed Unity.exe are import workers / helpers
"RAM: $freeGB GB free of $totalGB GB. Page file: $pfUsed of $pfSize GB used."
if ($editors) { $editors | Format-Table -AutoSize | Out-String -Stream | Where-Object { $_ } } else { "No Unity editors running." }
$canStart = [math]::Max(0, [math]::Floor(($freeGB - 4) / 5))
"Can start $canStart more editor(s) (5 GB each, keep 4 GB free)."
