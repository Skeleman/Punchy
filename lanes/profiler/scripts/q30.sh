#!/bin/bash
# Job 30: creature system refactor audit (read only). Profile build of origin/animal-behaviours tip 115678a, v2,
# uncapped, 30 s standing, last 2000 frames: a meadow (-place meadow), the HumanVillage (town stop spot), fishing
# villages 1 and 3 (job 28 spots). BProfileTools (all markers, -keep Brain) and BigFrame (longest frames).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=115678a
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q30 creature audit captures (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q30logs"; mkdir -p "$L"
for spec in "meadow -place meadow" "village -goto 3045,-827" "fv1 -goto -3304,-1644" "fv3 -goto -2883,-2121"; do
  set -- $spec; TAG=$1; shift
  T=prof30-$TAG; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T -island v2 "$@" -benchstops here -benchlong here 30 -fpscap 0 -profilestop here "$W/q-$T.raw" < /dev/null
  cp "$R/q-$T.log" "$L/$T.log"
  grep -q "Crash!!!\|Received signal" "$L/$T.log" && { grep -A15 "Crash!!!\|Received signal" "$L/$T.log" | head -30; exit 1; }
  echo "== $T $(grep -h 'BenchRoute: here: avg' $L/$T.log | sed 's/, gpu.*at /, at /;s/, players.*//' | cut -c1-200)"
  grep -h "Net status" "$L/$T.log" | tail -1 | grep -o "animals [0-9]*"
done
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
for f in "$R"/q-prof30-*.raw; do
  T=$(basename $f .raw); T=${T#q-}
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$(cygpath -w $f)" -out "$W/$T-big.txt" -minms 1 -frames 3 -listms 33 -logFile "$W/bigframe.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$(cygpath -w $f)" -out "$W/$T.txt" -keep Brain -logFile "$W/analyze.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  echo "== $T"; grep "frames used" "$R/$T.txt"
done
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null; rm -f "$R"/q-prof30-*.raw
log "q30 done"
