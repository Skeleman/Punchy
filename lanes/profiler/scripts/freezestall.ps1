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
# Thread names (Unity names its threads: main, gfx, job workers, audio...): GetThreadDescription, read-only.
Add-Type -Namespace FS -Name T -MemberDefinition @"
[DllImport("kernel32.dll")] public static extern System.IntPtr OpenThread(int a, bool i, int id);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[DllImport("kernel32.dll")] public static extern int GetThreadDescription(System.IntPtr h, out System.IntPtr d);
[DllImport("kernel32.dll")] public static extern System.IntPtr LocalFree(System.IntPtr p);
[DllImport("ntdll.dll")] public static extern int NtQueryInformationThread(System.IntPtr h, int c, out System.IntPtr v, int l, System.IntPtr r);
"@ -ErrorAction SilentlyContinue
# Where a thread began (Win32StartAddress, the function passed to CreateThread; .NET's StartAddress is always ntdll's
# stub), as module+offset from the process's module list.
$mods = $p.Modules | ForEach-Object { [pscustomobject]@{ Name = $_.ModuleName; Lo = [int64]$_.BaseAddress; Hi = [int64]$_.BaseAddress + $_.ModuleMemorySize } }
function TStart($id) { try { $h = [FS.T]::OpenThread(0x840, $false, [int]$id); if ($h -eq [IntPtr]::Zero) { return '?' }
  $v = [IntPtr]::Zero; $r = [FS.T]::NtQueryInformationThread($h, 9, [ref]$v, [IntPtr]::Size, [IntPtr]::Zero); [void][FS.T]::CloseHandle($h)
  if ($r -ne 0) { return '?' }; $a = $v.ToInt64(); $m = $mods | Where-Object { $a -ge $_.Lo -and $a -lt $_.Hi } | Select-Object -First 1
  if ($m) { "{0}+0x{1:x}" -f $m.Name, ($a - $m.Lo) } else { "0x{0:x}" -f $a } } catch { '?' } }
function TName($id) { try { $h = [FS.T]::OpenThread(0x800, $false, [int]$id); if ($h -eq [IntPtr]::Zero) { return '?' }
  $d = [IntPtr]::Zero; $n = ''; if ([FS.T]::GetThreadDescription($h, [ref]$d) -ge 0 -and $d -ne [IntPtr]::Zero) { $n = [Runtime.InteropServices.Marshal]::PtrToStringUni($d); [void][FS.T]::LocalFree($d) }
  [void][FS.T]::CloseHandle($h); if ($n) { $n } else { '-' } } catch { '?' } }
$m0 = $rows | Where-Object Main; "  main thread $($m0.Id) [$(TName $m0.Id)]: $($m0.State) $($m0.Wait), prio $($m0.Prio), from $(TStart $m0.Id)"
$rows | Select-Object Id, Cpu | Export-Csv $state -NoTypeInformation
$rows | ForEach-Object { $_ | Add-Member Delta ($_.Cpu - $(if ($prev.ContainsKey("$($_.Id)")) { $prev["$($_.Id)"] } else { $_.Cpu })) -PassThru } |
  Sort-Object Delta -Descending | Select-Object -First 5 | ForEach-Object { "  thread $($_.Id) [$(TName $_.Id)]$(if ($_.Main) { ' (main)' }): +$([math]::Round($_.Delta)) ms cpu, $($_.State) $($_.Wait), prio $($_.Prio), from $(TStart $_.Id)" }
# Every named thread's state, grouped by name with digits dropped (Job.Worker 0..n as one), e.g. "Job.Worker # x7 Wait/UserRequest".
"  named: " + (($p.Threads | ForEach-Object { $n = TName $_.Id; if ($n -ne '-' -and $n -ne '?') { "$($n -replace '\d+', '#') $($_.ThreadState)/$(if ($_.ThreadState -eq 'Wait') { $_.WaitReason } else { '-' })" } } |
  Group-Object | ForEach-Object { if ($_.Count -gt 1) { "$($_.Name) x$($_.Count)" } else { $_.Name } }) -join '; ')
# Stacks: on a game's second snapshot (stalled 45 s or more), once per game, freezestacks.ps1 in its own process.
$seen = Join-Path $env:TEMP "freezestall-seen-$($p.Id).txt"
$n = if (Test-Path $seen) { [int](Get-Content $seen) + 1 } else { 1 }; Set-Content $seen $n
if ($n -eq 2) {
  Start-Process powershell -ArgumentList '-NoProfile', '-File', "`"$(Join-Path $PSScriptRoot 'freezestacks.ps1')`"", '-Id', $p.Id -WindowStyle Hidden
  "  stacks: cdb attached (non-invasive), to freezestacks-$($p.Id).txt"
}
$w = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)"
"  io: read $([math]::Round($w.ReadTransferCount/1MB)) MB in $($w.ReadOperationCount) ops, write $([math]::Round($w.WriteTransferCount/1MB)) MB, other $($w.OtherOperationCount) ops"
$d = Get-Process MsMpEng -ErrorAction SilentlyContinue; if ($d) { "  MsMpEng cpu $([math]::Round($d.TotalProcessorTime.TotalSeconds,1)) s" }
try { $c = Get-Counter '\PhysicalDisk(_Total)\% Disk Time','\PhysicalDisk(_Total)\Disk Bytes/sec','\Processor(_Total)\% Processor Time' -ErrorAction Stop
  "  disk $([math]::Round($c.CounterSamples[0].CookedValue))% busy, $([math]::Round($c.CounterSamples[1].CookedValue/1MB,1)) MB/s; all CPUs $([math]::Round($c.CounterSamples[2].CookedValue))%" } catch { "  counters unavailable" }
