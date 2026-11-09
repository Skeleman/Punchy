#!/bin/bash
# While bench games run: every 5 s, the game's CPU seconds, threads and working set, the GPU's load, and how far the
# game's log has grown, to results/freezesample.csv. Read-only (it starts and stops nothing). A stall reads as a log
# that stops growing; then CPU seconds still rising = the game busy on the CPU, GPU at 99% = waiting on the GPU,
# neither = blocked on something else. Ends when no bench lock and no game for 10 minutes.
# When the log hasn't grown for 3 samples (15 s), freezestall.ps1 adds a snapshot to results/freezestall.txt (threads
# by state and wait reason, which threads used CPU, I/O, Defender, disk), at most every 30 s.
# (2026-11-09: the first stall it saw, 728 s at 08:45, had the game idle: 0.03 s CPU per sample, GPU 0%.)
R=$HOME/Projects/Punchy-wt/Tools/bench/results; O=$R/freezesample.csv; idle=0; same=0; last=; lastdump=0
PS1="$(cygpath -w "$(dirname "$0")/freezestall.ps1")"
SMI="/c/Windows/System32/nvidia-smi.exe"; [ -x "$SMI" ] || SMI=nvidia-smi
[ -f "$O" ] || echo "time,pid,cpu_s,threads,ws_mb,gpu_util,gpu_mhz,log,log_kb" > "$O"
while [ $idle -lt 120 ]; do
  p=$(timeout 20 powershell -NoProfile -Command "\$p=Get-Process Punchy -ErrorAction SilentlyContinue | Select-Object -First 1; if(\$p){'{0},{1:F1},{2},{3:F0}' -f \$p.Id,\$p.TotalProcessorTime.TotalSeconds,\$p.Threads.Count,(\$p.WorkingSet64/1MB)}" 2>/dev/null | tr -d '\r')
  if [ -n "$p" ]; then
    idle=0
    g=$(timeout 10 "$SMI" --query-gpu=utilization.gpu,clocks.gr --format=csv,noheader,nounits 2>/dev/null | tr -d ' \r')
    l=$(ls -t "$R"/q-*.log 2>/dev/null | head -1)
    sz=$(stat -c %s "$l" 2>/dev/null || echo 0)
    echo "$(date +%T),$p,$g,$(basename "$l"),$(( sz / 1024 ))" >> "$O"
    [ "$l:$sz" = "$last" ] && same=$((same + 1)) || same=0; last="$l:$sz"
    if [ $same -ge 3 ] && [ $(( $(date +%s) - lastdump )) -ge 30 ]; then
      { echo "-- $(basename "$l"), log still at $(( sz / 1024 )) KB"; timeout 60 powershell -NoProfile -File "$PS1" 2>&1 | tr -d '\r'; } >> "$R/freezestall.txt"
      lastdump=$(date +%s)
    fi
  else
    [ -d "$R/.lock" ] && idle=0 || idle=$((idle + 1))
  fi
  sleep 5
done
