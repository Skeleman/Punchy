#!/bin/bash
# Job 28d: every settlement, incl. the harsh-biome ones with W-94 hazard shelters. -place prop: no longer fires from the
# command line on 6f139d0 (the flora lookup is built on workers since 6c441fab, after the args are read; q28c stood at
# 0,0), so -warp town N (the F6 list, nearest first). Bench-6f139d0, uncapped, hitch log at 33 ms, 30 s at each.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=6f139d0
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q28d settlements (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q28logs"
for IS in v2 v3; do
  for N in 1 2 3 4 5 6 7 8; do
    T=q28d-$IS-$N
    run Bench-$K $T -island $IS -warp town $N -benchstops here -benchlong here 30 -hitchms 33 -fpscap 0
    cp "$R/q-$T.log" "$L/town-$IS-$N.log"; f="$L/town-$IS-$N.log"
    grep -q "Crash!!!\|Received signal" "$f" && { grep -A15 "Crash!!!\|Received signal" "$f" | head -30; exit 1; }
    AT=$(grep -h "BenchRoute: here: avg" "$f" | grep -o "at [-0-9]*,[-0-9]*" | tail -1)
    NEAR=$(grep -h "^FolkJobs: [A-Za-z]* at" "$f" | awk -v p="${AT#at }" 'BEGIN{split(p,q,",")} {gsub(/[(),]/," "); d=($4-q[1])^2+($5-q[2])^2; if (best==""||d<best){best=d; n=$2" ("$4","$5")"}} END{print n}')
    SH=$(grep -ho "room [0-9]* [A-Za-z]*ShelterHouse" "$f" | awk '{print $3}' | sort | uniq -c | tr '\n' ' ')
    echo "== $IS town $N $AT near $NEAR | shelters: $SH| exc $(grep -c Exception $f) hitches $(grep -c 'BenchRoute: hitch' $f)"
    grep -h "BenchRoute: here: avg" "$f" | sed 's/, gpu.*//' | cut -c1-150
    grep -h "BenchRoute: hitch" "$f" | cut -c1-90 | head -3
  done
done
log "q28d done"
