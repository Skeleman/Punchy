#!/bin/bash
# Job 17: W-76 parallel island bake, world-features b262269 vs its parent 096ec00 (W-75; Bench-096ec00 from job 18).
# Island v2 and v3, 3 cold starts each, rotated. Per log: [bake] stages, "baked in" (generate/places) + hashes,
# island ready, BenchRoute world ready, W-75 "[stream] world ready" (startup streaming, folded in per Zach). Uncapped.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
NEW=b262269; OLD=096ec00
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q17 W-76 bake bench (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $NEW Build Bench-$NEW || { echo "build Bench-$NEW failed"; exit 1; }
[ -f "$P/Builds/Bench-$OLD/Punchy.exe" ] || { echo "no Bench-$OLD"; exit 1; }
keepawake
log "q17 dialogs: $(dialogs)"
mkdir -p "$R/q17logs"
for RD in 1 2 3; do for ISL in v2 v3; do
  case $RD in 2) ORDER="$NEW $OLD";; *) ORDER="$OLD $NEW";; esac
  for K in $ORDER; do
    run Bench-$K q17-$K-$ISL-$RD -island $ISL -benchstops town -fpscap 0
    cp "$R/q-q17-$K-$ISL-$RD.log" "$R/q17logs/$K-$ISL-$RD.log"
  done
done; done
for ISL in v2 v3; do for K in $OLD $NEW; do for RD in 1 2 3; do
  f="$R/q17logs/$K-$ISL-$RD.log"
  echo "== $K $ISL $RD: exc $(grep -c Exception $f)"
  grep -h "\[bake\] stages ms" $f | cut -c1-400
  grep -h "baked in" $f | grep -o "baked in.*" | cut -c1-400
  echo "   island $(grep -m1 -o 'v[23] ready at [0-9.]* s' $f) / route $(grep -m1 -o 'BenchRoute: world ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / stream $(grep -m1 -o '\[stream\] world ready at [0-9.]* s' $f | grep -o '[0-9.]* s')"
done; done; done
log "q17 done"
