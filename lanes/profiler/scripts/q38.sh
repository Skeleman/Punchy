#!/bin/bash
# Job 34 (supervisor, 11-05), rerun on main after the player-build hotfix (PR #160). the new shadow defaults (PR #158: cheap far caster + cached far shadows, near 25 m) vs the
# old default (-cheapfarcaster 0 -cachedfarshadows 0) on main da5a22c. Profile build, uncapped, v2. Spots: the fishing
# village (-2883,-2121: the only one on v2 now), the swamp spot used as "fv1" in job 33 (-3304,-1644), the forest
# stop, and the desert (Sonoran) HumanVillage (3045,-827). Variants: new, old, and no camera shadows (-benchnoshadows,
# for the shadow passes' GPU ms = variant - noshadows). 2 rounds, order rotated. Round 1 new/old also captured
# (-profilestop) for the shadow passes' CPU markers. Then one BenchTune sweep session at the swamp spot to keep
# counting ~0.5 s frames and hangs after PR #159.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=c19e799
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q38 shadow defaults new vs old (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q38logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
spot() { case $1 in
  fishing) echo "-island v2 -goto -2883,-2121 -benchstops herewarm,here -benchlong here 20";;
  swamp) echo "-island v2 -goto -3304,-1644 -benchstops herewarm,here -benchlong here 20";;
  forest) echo "-island v2 -benchstops forest";;
  desert) echo "-island v2 -goto 3045,-827 -benchstops herewarm,here -benchlong here 20";; esac; }
stopname() { [ $1 = forest ] && echo forest || echo here; }
variant() { case $1 in new) echo "";; old) echo "-cheapfarcaster 0 -cachedfarshadows 0";; noshadows) echo "-benchnoshadows";; esac; }
for RD in 1 2; do
  case $RD in 1) ORDER="new old noshadows";; *) ORDER="noshadows old new";; esac
  for SP in fishing swamp forest desert; do
    for V in $ORDER; do
      T=prof38-$SP-$V-$RD; ST=$(stopname $SP); X=""
      if [ $RD = 1 ] && [ $V != noshadows ]; then rm -f "$R/q-$T.raw"; X="-profilestop $ST $W/q-$T.raw"; fi
      run Profile-${K}p $T $(spot $SP) $(variant $V) -fpscap 0 -hitchms 300 $X
      cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
      echo "$SP $V $RD | $(grep -h "BenchRoute: $ST: avg" $L/$T.log | sed 's/BenchRoute: [a-z]*: //; s/, frames.*main/, main/; s/, surface.*//' | cut -c1-200) | hitches $(grep -h 'BenchRoute: hitch' $L/$T.log | awk '{print $3}' | tr '\n' ' ')"
    done
  done
done
# Shadow markers (CPU) from the round 1 captures.
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
for f in "$R"/q-prof38-*.raw; do
  [ -f "$f" ] || continue; T=$(basename $f .raw); T=${T#q-}; strays >/dev/null
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$(cygpath -w $f)" -out "$W/$T.txt" -logFile "$W/analyze.log" )
  echo "== $T"; grep "frames used" "$R/$T.txt"; grep -i "shadow" "$R/$T.txt" | cut -c1-200 | head -12
  rm -f "$f"
done
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null
# Stall count: one Bench-build sweep session at the swamp spot (where the 0.5 s frames were most frequent).
build $K Build Bench-${K} || { echo "bench build failed"; exit 1; }
SW="new:;old:cheap=0,cached=0;o1-50-c2:shadows=0.5,cascades=2;nofloras:flora=0;cheap:cheap=1,cached=0;cached:cheap=0,cached=1;still:still=1;all:cheap=1,still=1,cached=1"
for RD in 1 2; do
  T=q38-sweep-$RD
  run Bench-${K} $T -island v2 -goto -3304,-1644 -benchstops here -benchlong here 160 -fpscap 0 -hitchms 300 -benchtune panel=0 -benchtunesweep "$SW"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  echo "$T: 0.5 s frames $(grep -c 'BenchRoute: hitch 5[0-2][0-9] ms' $L/$T.log), done $(grep -c 'BenchRoute: done' $L/$T.log), hitches $(grep -h 'BenchRoute: hitch' $L/$T.log | awk '{print $3}' | tr '\n' ' ')"
  grep -h "BenchTune: sweep" $L/$T.log | sed 's/; shadows x.*//'
done
log "q38 done"
