#!/bin/bash
# q50 part 4: the Bench build of 2049f8c, run stop UNCAPPED (-fpscap 0), 3 sessions, against part 3 (capped at 60).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2049f8c
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q50d no-capture check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q50logs"; mkdir -p "$L"
for BD in Bench-${K}; do
  for RD in 1 2 3; do
    T=q50d-$BD-$RD
    run $BD $T -island v2 -benchstops run -fpscap 0 -hitchms 100
    cp "$R/q-$T.log" "$L/$T.log"
    echo "$T | $(grep -h 'BenchRoute: run: avg' $L/$T.log | grep -o 'avg [0-9.]* fps\|worst [0-9.]* ms' | tr '\n' ' ') | hitches over 100 ms: $(grep -h 'BenchRoute: hitch' $L/$T.log | awk '{print $3}' | tr '\n' ' ') | crash $(grep -c 'Crash!!!' $L/$T.log)"
  done
done
log "q50d done"
