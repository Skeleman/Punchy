# Logs each Unity editor's RAM every N seconds to a CSV, for spotting trends (see editor-ram.ps1).
# Usage: powershell -ExecutionPolicy Bypass -File tools/editor-ram-log.ps1 [-Out logs/editor-ram.csv] [-Every 120] [-Hours 12]
# Read-only apart from the CSV; changes nothing else.
param([string]$Out = "logs/editor-ram.csv", [int]$Every = 120, [double]$Hours = 12)
$dir = Split-Path $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
if (-not (Test-Path $Out)) { "time,pid,project,workingGB,privateGB,freeGB" | Out-File $Out -Encoding ascii }
$end = (Get-Date).AddHours($Hours)
while ((Get-Date) -lt $end) {
    $t = Get-Date -Format "yyyy-MM-dd HH:mm"
    $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | ForEach-Object {
        if ($_.CommandLine -match '-projectpath\s+"?([^"]+?)"?(\s+-|$)') {
            $proj = Split-Path ($Matches[1] -replace '\\', '/') -Leaf
            $ws = [math]::Round($_.WorkingSetSize / 1GB, 2)
            $pv = [math]::Round($_.PrivatePageCount / 1GB, 2)
            "$t,$($_.ProcessId),$proj,$ws,$pv,$free" | Out-File $Out -Append -Encoding ascii
        }
    }
    Start-Sleep -Seconds $Every
}
