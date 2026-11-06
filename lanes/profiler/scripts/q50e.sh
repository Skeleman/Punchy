#!/bin/bash
# q50 part 5, after Zach's reboot (11-06 13:29): Bench build of 2049f8c, run stop, 3 sessions capped at 60 then 2 uncapped.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2049f8c
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q50e post-reboot check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q50logs"; mkdir -p "$L"
for CAP in 60 60 60 0 0; do
  RD=$((RD+1)); T=q50e-cap$CAP-$RD
  run Bench-$K $T -island v2 -benchstops run -fpscap $CAP -hitchms 100
  cp "$R/q-$T.log" "$L/$T.log"
  echo "$T | $(grep -h 'BenchRoute: run: avg' $L/$T.log | grep -o 'avg [0-9.]* fps\|worst [0-9.]* ms' | tr '\n' ' ') | hitches over 100 ms: $(grep -h 'BenchRoute: hitch' $L/$T.log | awk '{print $3}' | tr '\n' ' ') | crash $(grep -c 'Crash!!!' $L/$T.log)"
done
log "q50e done"
