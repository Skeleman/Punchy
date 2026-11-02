Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Awake { [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f); }
'@
# ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED, held while a bench script or bench game runs (released when this process exits)
[Awake]::SetThreadExecutionState([uint32]2147483651) | Out-Null
while ((Get-Process Punchy -ErrorAction SilentlyContinue) -or (Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'bash.exe' -and $_.CommandLine -match 'florabatch|benchrun|bench.sh' })) { Start-Sleep -Seconds 30 }
