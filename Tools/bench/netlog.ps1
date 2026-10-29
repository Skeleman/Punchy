# Machine-wide adapter byte counters every 10 s while Punchy runs (bandwidth for the MP test; the laptop is otherwise idle).
param([string]$Out)
"time,rxBytes,txBytes" | Out-File -Encoding utf8 $Out
do {
  $s = Get-NetAdapterStatistics | Measure-Object -Property ReceivedBytes, SentBytes -Sum
  "{0},{1},{2}" -f (Get-Date -Format HH:mm:ss), $s[0].Sum, $s[1].Sum | Out-File -Encoding utf8 -Append $Out
  Start-Sleep -Seconds 10
} while (Get-Process Punchy -ErrorAction SilentlyContinue)
