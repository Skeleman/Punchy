#!/bin/bash
# Job 28c: the W-94 hazard shelter houses (#69). They are flora species, so -place prop:<species> finds them and logs
# the eight nearest. Bench-6f139d0, v2 and v3, uncapped, hitch log at 33 ms, 30 s standing at the nearest of each.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=6f139d0
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q28c hazard houses (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q28logs"
for IS in v2 v3; do
  for SP in AshShelterHouse SandShelterHouse SnowShelterHouse StormShelterHouse; do
    T=q28c-$IS-$SP
    run Bench-$K $T -island $IS -place prop:$SP -benchstops here -benchlong here 30 -hitchms 33 -fpscap 0
    cp "$R/q-$T.log" "$L/haz-$IS-$SP.log"
    grep -q "Crash!!!\|Received signal" "$L/haz-$IS-$SP.log" && { grep -A15 "Crash!!!\|Received signal" "$L/haz-$IS-$SP.log" | head -30; exit 1; }
    echo "== $IS $SP exc $(grep -c Exception $L/haz-$IS-$SP.log) hitches $(grep -c 'BenchRoute: hitch' $L/haz-$IS-$SP.log)"
    grep -h -i "$SP" "$L/haz-$IS-$SP.log" | cut -c1-220 | head -4
    grep -h "BenchRoute: here: avg" "$L/haz-$IS-$SP.log" | sed 's/, gpu.*at /, at /;s/, players.*//' | cut -c1-200
    grep -h "BenchRoute: hitch" "$L/haz-$IS-$SP.log" | cut -c1-100 | head -3
  done
done
log "q28c done"
