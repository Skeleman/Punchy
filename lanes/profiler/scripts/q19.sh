#!/bin/bash
# Job 19: startup streaming tail on world-features 4e3b63c, v3 and v2, uncapped. (1) Bench build, 3 cold starts per island,
# player left where it spawns (-benchstops here; the town stop teleports and restreams): island ready, BenchRoute world
# ready, "[stream] world ready", chunks. Plus 1 run per island with the town stop, to size the teleport effect on jobs
# 17/18. (2) Profile build with the StreamTrace helper (-streamtrace), 2 runs per island: per-second and summary lines.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=4e3b63c
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q19 streaming tail (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }
cp "$S/StreamTrace.cs.txt" "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs"
build $K BuildDev Profile-${K}s; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs" "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs.meta"
[ $rc = 0 ] || { echo "build Profile-${K}s failed"; exit 1; }
keepawake
log "q19 dialogs: $(dialogs)"
mkdir -p "$R/q19logs"
for RD in 1 2 3; do for ISL in v3 v2; do
  run Bench-$K q19-$ISL-$RD -island $ISL -benchstops here -fpscap 0; cp "$R/q-q19-$ISL-$RD.log" "$R/q19logs/$ISL-here-$RD.log"
done; done
for ISL in v3 v2; do run Bench-$K q19-$ISL-town -island $ISL -benchstops town -fpscap 0; cp "$R/q-q19-$ISL-town.log" "$R/q19logs/$ISL-town.log"; done
for RD in 1 2; do for ISL in v3 v2; do
  run Profile-${K}s q19-trace-$ISL-$RD -island $ISL -benchstops here -fpscap 0 -streamtrace; cp "$R/q-q19-trace-$ISL-$RD.log" "$R/q19logs/$ISL-trace-$RD.log"
done; done
for f in "$R"/q19logs/*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f); island $(grep -m1 -o 'v[23] ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / route $(grep -m1 -o 'BenchRoute: world ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / $(grep -m1 -o '\[stream\] world ready at [0-9.]* s since start, [0-9]* chunks' $f)"
  grep -h "STREAMTRACE summary\|STREAMTRACE totals\|STREAMTRACE start" $f | cut -c1-1500
done
log "q19 done"
