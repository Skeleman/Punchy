#!/bin/bash
# Job 29: #132 fix (A-68 row 1, 3d66652c) before/after. Before 12c9318 (first parent of merge 69ca891), after 69ca891
# (that merge brings only 3d66652c). Bench, uncapped, 30 s standing at the job 28 fishing village spots (q28logs/
# villages.txt: 4 on v2, 3 on v3), 2 rounds alternating build order. Then Profile captures (-fpscap 60, -keep Brain)
# at v2 villages 1 and 3 for each build: BrainJob, Brain.Advance, the CreatureSnapshotSystem wait.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
BEF=12c9318; AFT=69ca891
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q29 fishing village brain before/after (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
for K in $BEF $AFT; do
  build $K Build Bench-$K || { echo "build $K failed"; exit 1; }
  build $K BuildDev Profile-${K}p || { echo "profile build $K failed"; exit 1; }
done
keepawake
L="$R/q29logs"; mkdir -p "$L"
cp "$R/q28logs/villages.txt" "$L/villages.txt"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
for RD in 1 2; do
  case $RD in 1) ORDER="$BEF $AFT";; *) ORDER="$AFT $BEF";; esac
  while read IS N XZ; do
    for K in $ORDER; do
      T=q29-$K-$IS-$N-$RD
      run Bench-$K $T -island $IS -goto $XZ -benchstops here -benchlong here 30 -hitchms 33 -fpscap 0 < /dev/null
      cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
      echo "$K $IS $N $RD $(grep -h 'BenchRoute: here: avg' $L/$T.log | sed 's/BenchRoute: here: //;s/, gpu.*//' | cut -c1-120) | hitches $(grep -c 'BenchRoute: hitch' $L/$T.log) | exc $(grep -c Exception $L/$T.log) | $(grep -h 'BenchRoute: here: avg' $L/$T.log | grep -o 'at [-0-9]*,[-0-9]*')"
    done
  done < "$L/villages.txt"
done
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
for N in 1 3; do
  XZ=$(awk -v n=$N '$1=="v2" && $2==n {print $3}' "$L/villages.txt")
  for K in $BEF $AFT; do
    T=prof29-$K-v2-$N; rm -f "$R/q-$T.raw"
    run Profile-${K}p $T -island v2 -goto $XZ -benchstops here -benchlong here 30 -fpscap 60 -profilestop here "$W/q-$T.raw" < /dev/null
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  done
done
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
for f in "$R"/q-prof29-*.raw; do
  T=$(basename $f .raw); T=${T#q-}
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$(cygpath -w $f)" -out "$W/$T.txt" -keep Brain -logFile "$W/analyze.log" )
  echo "== $T"; grep "frames used\|SPIKES" "$R/$T.txt"
  grep -P "^Workers\|CreatureBrainSystem:BrainJob|^Workers\|Brain\.(Advance|Tick)\t|^Main\|Default World Punchy.Fauna.CreatureSnapshotSystem\t" "$R/$T.txt" | cut -c1-160
done
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null; rm -f "$R"/q-prof29-*.raw
log "q29 done"
