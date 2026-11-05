#!/bin/bash
# b/fix-stall-hang investigation (supervisor, 11-05): the recurring 510-523 ms frames (and the hangs that followed
# them) seen in the BenchTune sweep sessions at fishing village 1. Profile build of main b19e37d, same sweep as q35,
# -profilestop here so every frame of the stop is captured; BigFrame lists frames over 400 ms and dumps the 6 longest
# call trees. Two sessions; stops at the first that shows a 0.5 s frame.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=b19e37d
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q36 0.5 s hitch capture (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q36logs"; mkdir -p "$L"
SW="default:;o1-50-c2:shadows=0.5,cascades=2;nofloras:flora=0;cheap:cheap=1;still:still=1;cached:cached=1;cheapstill:cheap=1,still=1;cheapcached:cheap=1,cached=1;all:cheap=1,still=1,cached=1;all-n15:cheap=1,still=1,cached=1,near=15"
for RD in 1 2; do
  T=prof36-fv1-$RD; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T -island v2 -goto -3304,-1644 -benchstops here -benchlong here 200 -fpscap 0 -hitchms 300 \
      -benchtune panel=0 -benchtunesweep "$SW" -profilestop here "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"
  echo "== $T: exc $(grep -c Exception $L/$T.log), raw $(ls -la $R/q-$T.raw 2>/dev/null | awk '{print $5}')"
  grep -h "BenchRoute: hitch\|BenchRoute: here:" $L/$T.log | cut -c1-200
  if [ -f "$R/q-$T.raw" ]; then
    strays >/dev/null
    cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
    cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
    ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$T.raw" -out "$W/$T-big.txt" -minms 5 -frames 6 -listms 300 -logFile "$W/bigframe.log" )
    rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
    cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
    strays >/dev/null
    sed -n 1,2p "$R/$T-big.txt" | cut -c1-400
    grep -q "=\([4-9][0-9][0-9]\|[0-9]\{4,\}\)" <(sed -n 2p "$R/$T-big.txt") && { echo "0.5 s frame caught in $T"; rm -f "$R/q-$T.raw"; break; }
    rm -f "$R/q-$T.raw"
  fi
done
log "q36 done"
