#!/bin/bash
# Finding 11 follow-up 2 (supervisor, 11-06): where the v3 fishing village water's ~1.5 ms goes. Profile build of
# adb11e0, v3 fishing village 1 (-2151,3731), uncapped, 20 s captured; plain vs -benchnoshader Water, 2 rounds
# (ABBA). Each capture through BProfileTools.Analyze (marker stats by thread) for a plain - water-off diff.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=adb11e0
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q43 water wait markers (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q43logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
for X in plain-1 nowater-1 nowater-2 plain-2; do
  T=prof43-$X; V=""; case $X in nowater*) V="-benchnoshader Water";; esac; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T -island v3 -goto -2151,3731 -benchstops herewarm,here -benchlong here 20 $V -fpscap 0 -hitchms 50 -profilestop here "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  echo "$X | $(grep -h "BenchRoute: here: avg" $L/$T.log | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms\|render thread [0-9.]* ms' | tr '\n' ' ')"
done
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
for f in "$R"/q-prof43-*.raw; do
  [ -f "$f" ] || continue; T=$(basename $f .raw); T=${T#q-}; strays >/dev/null
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$(cygpath -w $f)" -out "$W/$T.txt" -logFile "$W/analyze.log" )
  cp "$R/$T.txt" "$L/" 2>/dev/null; echo "== $T: $(grep 'frames used' $R/$T.txt)"; rm -f "$f"
done
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null
log "q43 done"
