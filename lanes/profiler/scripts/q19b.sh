#!/bin/bash
# Job 19b: profile the startup from launch (dev build, Unity's -profiler-enable/-profiler-log-file), v2 then v3, player left
# in place; dump the longest main-thread frames' call trees (BigFrame.cs.txt) and the usual analysis.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=4e3b63c
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q19b startup profile (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
for ISL in v2 v3; do
  rm -f "$R/q19-start-$ISL.raw"
  run Profile-${K}s q19-start-$ISL -island $ISL -benchstops here -fpscap 0 -profiler-enable -profiler-maxusedmemory 268435456 -profiler-log-file "$W/q19-start-$ISL.raw"
  ls -la "$R"/q19-start-$ISL.raw* 2>&1 | head -3
  cp "$R/q-q19-start-$ISL.log" "$R/q19logs/$ISL-start.log"
  [ -f "$R/q19-start-$ISL.raw" ] || { echo "no capture for $ISL"; continue; }
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\q19-start-$ISL.raw" -out "$WW\q19-bigframe-$ISL.txt" -minms 100 -frames 3 -logFile "$W/bigframe.log" )
  log "bigframe $ISL rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  strays >/dev/null
  echo "== $ISL"; cut -c1-200 "$R/q19-bigframe-$ISL.txt" | head -150
done
log "q19b done"
