#!/bin/bash
# While bench games run: every 5 s, the game's CPU seconds, threads and working set, the GPU's load, and how far the
# game's log has grown, to results/freezesample.csv. Read-only (it starts and stops nothing). A stall reads as a log
# that stops growing; then CPU seconds still rising = the game busy on the CPU, GPU at 99% = waiting on the GPU,
# neither = blocked on something else. Ends when no bench lock and no game for 10 minutes.
R=$HOME/Projects/Punchy-wt/Tools/bench/results; O=$R/freezesample.csv; idle=0
SMI="/c/Windows/System32/nvidia-smi.exe"; [ -x "$SMI" ] || SMI=nvidia-smi
[ -f "$O" ] || echo "time,pid,cpu_s,threads,ws_mb,gpu_util,gpu_mhz,log,log_kb" > "$O"
while [ $idle -lt 120 ]; do
  p=$(timeout 20 powershell -NoProfile -Command "\$p=Get-Process Punchy -ErrorAction SilentlyContinue | Select-Object -First 1; if(\$p){'{0},{1:F1},{2},{3:F0}' -f \$p.Id,\$p.TotalProcessorTime.TotalSeconds,\$p.Threads.Count,(\$p.WorkingSet64/1MB)}" 2>/dev/null | tr -d '\r')
  if [ -n "$p" ]; then
    idle=0
    g=$(timeout 10 "$SMI" --query-gpu=utilization.gpu,clocks.gr --format=csv,noheader,nounits 2>/dev/null | tr -d ' \r')
    l=$(ls -t "$R"/q-*.log 2>/dev/null | head -1)
    echo "$(date +%T),$p,$g,$(basename "$l"),$(( $(stat -c %s "$l" 2>/dev/null || echo 0) / 1024 ))" >> "$O"
  else
    [ -d "$R/.lock" ] && idle=0 || idle=$((idle + 1))
  fi
  sleep 5
done
