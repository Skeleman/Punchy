# Every thread's stack in a stalled game (freezestall.ps1 starts it once per game, in its own process): cdb attaches
# non-invasively (-pv: the threads are suspended while it reads them, then it detaches; nothing is changed) and prints
# the thread CPU times and each thread's stack to results\freezestacks-<pid>.txt. Symbols from Microsoft's and Unity's
# symbol servers, cached in ~\Tools\symcache. 15 minutes at most (the first symbol download is large).
# cdb is copied out of WinDbg's Store package (it won't run from WindowsApps) into ~\Tools\cdb (Zach OK'd it, 11-09).
param([int]$Id)
$cdb = Join-Path $HOME 'Tools\cdb\cdb.exe'
if (-not (Test-Path $cdb)) { exit }
$cache = Join-Path $HOME 'Tools\symcache'
$out = Join-Path $HOME "Projects\Punchy-wt\Tools\bench\results\freezestacks-$Id.txt"
$sym = "srv*$cache*https://msdl.microsoft.com/download/symbols;srv*$cache*https://symbolserver.unity3d.com/"
"== $(Get-Date -Format 'HH:mm:ss') pid $Id" | Set-Content $out
$c = Start-Process -FilePath $cdb -ArgumentList '-y', "`"$sym`"", '-pv', '-p', $Id, '-c', '"!runaway 3;~*kc 40;qd"' `
  -RedirectStandardOutput "$out.tmp" -WindowStyle Hidden -PassThru
$c | Wait-Process -Timeout 900 -ErrorAction SilentlyContinue
if (-not $c.HasExited) { Stop-Process -Id $c.Id -Force; "cdb timed out after 900 s" | Add-Content $out }
Get-Content "$out.tmp" | Where-Object { $_ -notmatch 'NatVis script' } | Add-Content $out
Remove-Item "$out.tmp"
"== done $(Get-Date -Format 'HH:mm:ss')" | Add-Content $out
