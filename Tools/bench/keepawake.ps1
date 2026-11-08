Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Awake { [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f); }
'@
# ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED, held while a bench script or bench game runs (released when this process exits)
[Awake]::SetThreadExecutionState([uint32]2147483651) | Out-Null
# Also while a queue script holds the bench lock (results\.lock): the q-scripts' names never matched below, so the
# helper quit at the first gap between games and the display went off 15 min later, freezing the game's frame (the
# 2026-11-08 multi-minute frames, all while nobody was at the laptop).
$lock = Join-Path $PSScriptRoot 'results\.lock'
while ((Test-Path $lock) -or (Get-Process Punchy -ErrorAction SilentlyContinue) -or (Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'bash.exe' -and $_.CommandLine -match 'florabatch|benchrun|bench.sh' })) { Start-Sleep -Seconds 30 }
