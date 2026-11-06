#!/bin/bash
# q50 part 3: is it the capture? Profile build of 2049f8c, the run stop with no profiler capture (runner hitch log at
# 100 ms), 3 sessions; then the Bench build of 2049f8c, same, 3 sessions.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2049f8c
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q50c no-capture check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q50logs"; mkdir -p "$L"
for BD in Profile-${K}p Bench-${K}; do
  for RD in 1 2 3; do
    T=q50c-$BD-$RD
    run $BD $T -island v2 -benchstops run -fpscap 60 -hitchms 100
    cp "$R/q-$T.log" "$L/$T.log"
    echo "$T | $(grep -h 'BenchRoute: run: avg' $L/$T.log | grep -o 'avg [0-9.]* fps\|worst [0-9.]* ms' | tr '\n' ' ') | hitches over 100 ms: $(grep -h 'BenchRoute: hitch' $L/$T.log | awk '{print $3}' | tr '\n' ' ') | crash $(grep -c 'Crash!!!' $L/$T.log)"
  done
done
log "q50c done"
