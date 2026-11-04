#!/bin/bash
# Job 28b: is the q28 glide burst (64 frames over 33 ms, 15-30 s into the glide stop, x 650-885) new on 6f139d0?
# Bench glide stop alone, v2, uncapped, 6f139d0 vs 1699574, 3 runs each, alternating; per 5 s worst/slow and hitch spots.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q28b glide A/B (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
L="$R/q28logs"
for RD in 1 2 3; do
  case $RD in 2) ORDER="1699574 6f139d0";; *) ORDER="6f139d0 1699574";; esac
  for K in $ORDER; do
    T=q28b-glide-$K-$RD
    run Bench-$K $T -island v2 -benchstops glide -hitchms 33 -fpscap 0
    cp "$R/q-$T.log" "$L/glide-$K-$RD.log"
    grep -q "Crash!!!\|Received signal" "$L/glide-$K-$RD.log" && { grep -A15 "Crash!!!\|Received signal" "$L/glide-$K-$RD.log" | head -30; exit 1; }
    echo "== $K $RD hitches $(grep -c 'BenchRoute: hitch' $L/glide-$K-$RD.log) exc $(grep -c Exception $L/glide-$K-$RD.log)"
    grep -h "BenchRoute: glide" "$L/glide-$K-$RD.log" | sed 's/, gpu.*per 5 s/ | per 5 s/' | cut -c1-260
    grep -h "BenchRoute: hitch" "$L/glide-$K-$RD.log" | awk '{print $8}' | cut -d, -f1 | sort -n | sed -n '1p;$p' | tr '\n' ' '; echo
  done
done
log "q28b done"
