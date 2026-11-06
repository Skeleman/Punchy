#!/bin/bash
# Millfield big frame, Bench timing probe (supervisor, 11-06): the frame shows only in release builds (2 of 3 Bench,
# 0 of 5 Profile sessions), always at (3253,10,-857) on the v2 townwalk. Bench build of 781fa5a with the runner's new
# -benchprobe (stopwatch on every second-level player loop system; on a frame over 150 ms it logs the slowest systems,
# GC and where SurfaceGlow's far scan stood; it also logs every far scan landing with its texture upload size).
# Out dir Bench-781fa5a-probe so it rebuilds with the probe. 4 sessions of the townwalk stop, uncapped.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=781fa5a
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q48 Millfield Bench probe (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K}-probe2 || { echo "build failed"; grep -m5 "error CS" "$W/build-Bench-${K}-probe2.log"; exit 1; }
keepawake
L="$R/q48blogs"; mkdir -p "$L"
crashcheck() {
  grep -q "Crash!!!\|Received signal" "$1" || return 0
  if grep -q "BenchRoute: done" "$1" && grep -q "SurfaceGlow.FarScanJob" "$1"; then echo "known quit crash (SurfaceGlow.FarScanJob) in $(basename $1)"; return 1; fi
  echo "CRASH in $(basename $1): stopping"; grep -A12 "OUTPUTTING STACK TRACE" "$1" | cut -c1-220; exit 1
}
for RD in 1 2 3 4; do
  T=q48b-townwalk-$RD
  run Bench-${K}-probe2 $T -island v2 -benchstops townwalk -fpscap 0 -hitchms 100 -benchprobe 150
  cp "$R/q-$T.log" "$L/$T.log"; crashcheck "$L/$T.log"
  echo "== $T: $(grep -h 'BenchRoute: townwalk: avg' $L/$T.log | sed 's/, cpu.*//; s/BenchRoute: //')"
  grep -h "BenchProbe: on\|BenchProbe: watching" $L/$T.log | head -2
  grep -h "BenchRoute: hitch" $L/$T.log | cut -c1-120
  grep -h "BenchProbe: slow frame" $L/$T.log | cut -c1-700
  echo "far scan landings: $(grep -c 'BenchProbe: far scan landed' $L/$T.log); first: $(grep -h 'BenchProbe: far scan landed' $L/$T.log | head -1 | cut -c1-200)"
done
log "q48 done"
