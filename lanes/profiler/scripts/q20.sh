#!/bin/bash
# Job 20: startup hitch fix (#102) world-features cfae8be vs 4e3b63c. (1) Bench builds, 3 cold starts per island, rotated,
# -benchstops here (no teleport): island ready, BenchRoute world ready, [stream] world ready, [flora] build, hashes, audit lines.
# (2) Profile-cfae8bes (+ StreamTrace helper): one -streamtrace run per island (batches), then a capture from launch at
# -fpscap 20 per island and BigFrame dumps (4e3b63c's are job 19's). (3) Bench-cfae8be -host once per island: per-world
# [flora] build ms and hashes.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
NEW=cfae8be; OLD=4e3b63c
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q20 startup hitch re-bench (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $NEW Build Bench-$NEW || { echo "build Bench-$NEW failed"; exit 1; }
cp "$S/StreamTrace.cs.txt" "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs"
build $NEW BuildDev Profile-${NEW}s; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs" "$P/Assets/Game/Scripts/Debugging/StreamTrace.cs.meta"
[ $rc = 0 ] || { echo "build Profile-${NEW}s failed"; exit 1; }
keepawake
log "q20 dialogs: $(dialogs)"
mkdir -p "$R/q20logs"
for RD in 1 2 3; do for ISL in v2 v3; do
  case $RD in 2) ORDER="$NEW $OLD";; *) ORDER="$OLD $NEW";; esac
  for K in $ORDER; do run Bench-$K q20-$K-$ISL-$RD -island $ISL -benchstops here -fpscap 0; cp "$R/q-q20-$K-$ISL-$RD.log" "$R/q20logs/$K-$ISL-$RD.log"; done
done; done
for ISL in v2 v3; do
  run Profile-${NEW}s q20-trace-$ISL -island $ISL -benchstops here -fpscap 0 -streamtrace; cp "$R/q-q20-trace-$ISL.log" "$R/q20logs/$NEW-$ISL-trace.log"
  run Bench-$NEW q20-host-$ISL -island $ISL -benchstops here -fpscap 0 -host; cp "$R/q-q20-host-$ISL.log" "$R/q20logs/$NEW-$ISL-host.log"
  log "host $ISL dialogs: $(dialogs)"
done
for ISL in v2 v3; do
  rm -f "$R/q20-start-$ISL.raw"
  run Profile-${NEW}s q20-start-$ISL -island $ISL -benchstops here -fpscap 20 -profiler-enable -profiler-maxusedmemory 268435456 -profiler-log-file "$W/q20-start-$ISL.raw"
  cp "$R/q-q20-start-$ISL.log" "$R/q20logs/$NEW-$ISL-start.log"
  [ -f "$R/q20-start-$ISL.raw" ] || { echo "no capture for $ISL"; continue; }
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\q20-start-$ISL.raw" -out "$WW\q20-bigframe-$ISL.txt" -minms 100 -frames 3 -logFile "$W/bigframe.log" )
  log "bigframe $ISL rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  strays >/dev/null; rm -f "$R/q20-start-$ISL.raw"
done
for f in "$R"/q20logs/*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f); island $(grep -m1 -o 'v[23] ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / route $(grep -m1 -o 'BenchRoute: world ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / $(grep -m1 -o '\[stream\] world ready at [0-9.]* s since start, [0-9]* chunks' $f)"
  grep -h -o "[0-9]* landmarks, hash [0-9A-F]*\|shared with [A-Za-z]*\|\[flora\] build .*\|\[hydro audit\].\{0,60\}\|biome report.\{0,60\}\|STREAMTRACE summary.*\|STREAMTRACE batches.\{0,400\}" $f | cut -c1-500
done
for ISL in v2 v3; do echo "== bigframe $ISL"; cut -c1-200 "$R/q20-bigframe-$ISL.txt" | head -90; done
log "q20 done"
