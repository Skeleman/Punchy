# One snapshot of a stalled game (freezesample.sh calls it when the game's log hasn't grown for 3 samples): its threads
# by state and wait reason, the threads that used CPU since the last snapshot, its I/O counts, Defender's CPU and the
# disk's load. Read-only.
$p = Get-Process Punchy -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { exit }
$now = Get-Date -Format 'HH:mm:ss'
"== $now pid $($p.Id) cpu $([math]::Round($p.TotalProcessorTime.TotalSeconds,1)) s, threads $($p.Threads.Count), ws $([math]::Round($p.WorkingSet64/1MB)) MB, responding $($p.Responding)"
$p.Threads | Group-Object { "$($_.ThreadState)/$(if ($_.ThreadState -eq 'Wait') { $_.WaitReason } else { '-' })" } | Sort-Object Count -Descending | ForEach-Object { "  $($_.Count) x $($_.Name)" }
$state = Join-Path $env:TEMP 'freezestall-threads.csv'
$prev = @{}; if (Test-Path $state) { Import-Csv $state | ForEach-Object { $prev[$_.Id] = [double]$_.Cpu } }
# The main thread is the oldest (start addresses are all ntdll's thread stub, so they name nothing).
$main = ($p.Threads | Sort-Object StartTime | Select-Object -First 1).Id
$rows = $p.Threads | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Cpu = $_.TotalProcessorTime.TotalMilliseconds; State = $_.ThreadState; Wait = $(if ($_.ThreadState -eq 'Wait') { $_.WaitReason } else { '' }); Prio = $_.CurrentPriority; Main = ($_.Id -eq $main) } }
$m0 = $rows | Where-Object Main; "  main thread $($m0.Id): $($m0.State) $($m0.Wait), prio $($m0.Prio)"
$rows | Select-Object Id, Cpu | Export-Csv $state -NoTypeInformation
$rows | ForEach-Object { $_ | Add-Member Delta ($_.Cpu - $(if ($prev.ContainsKey("$($_.Id)")) { $prev["$($_.Id)"] } else { $_.Cpu })) -PassThru } |
  Sort-Object Delta -Descending | Select-Object -First 5 | ForEach-Object { "  thread $($_.Id)$(if ($_.Main) { ' (main)' }): +$([math]::Round($_.Delta)) ms cpu, $($_.State) $($_.Wait), prio $($_.Prio)" }
$w = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)"
"  io: read $([math]::Round($w.ReadTransferCount/1MB)) MB in $($w.ReadOperationCount) ops, write $([math]::Round($w.WriteTransferCount/1MB)) MB, other $($w.OtherOperationCount) ops"
$d = Get-Process MsMpEng -ErrorAction SilentlyContinue; if ($d) { "  MsMpEng cpu $([math]::Round($d.TotalProcessorTime.TotalSeconds,1)) s" }
try { $c = Get-Counter '\PhysicalDisk(_Total)\% Disk Time','\PhysicalDisk(_Total)\Disk Bytes/sec','\Processor(_Total)\% Processor Time' -ErrorAction Stop
  "  disk $([math]::Round($c.CounterSamples[0].CookedValue))% busy, $([math]::Round($c.CounterSamples[1].CookedValue/1MB,1)) MB/s; all CPUs $([math]::Round($c.CounterSamples[2].CookedValue))%" } catch { "  counters unavailable" }
