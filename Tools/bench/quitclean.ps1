# Launch a player, let it run, close its window like a player would, and report the exit code (0 = clean quit).
param([string]$Exe, [string]$Log, [int]$Seconds = 45, [string]$Extra = "")
$p = Start-Process -FilePath $Exe -ArgumentList "-logFile `"$Log`" -place HumanVillage -time 12 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 $Extra" -PassThru
Start-Sleep -Seconds $Seconds
$p.CloseMainWindow() | Out-Null
if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force; "exit=killed (no exit in 30 s)" } else { "exit=$($p.ExitCode)" }
