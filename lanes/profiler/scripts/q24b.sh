#!/bin/bash
# Job 24b: what makes the frame every 4.0 s on main 810955f (56 ms solo, 150-260 ms host/client in q21d/q24; none in
# job 22 on 0ec7a8d). Profile build of 810955f, Profiler capture of the skeletal lizard walk (hereclose), uncapped; the
# editor keeps the last 2000 frames (~20 s, ~5 of those frames). BigFrame dumps the worst frames' call trees.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=810955f
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q24b 4 s hitch capture (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "build failed"; exit 1; }
keepawake
T=prof24b-lizard; rm -f "$R/q-$T.raw"
run Profile-${K}p $T -island v3 -goto -731,-2685 -close -515,-2477,226 -benchstops hereclose -profilestop hereclose "$W/q-$T.raw" -fpscap 0
cp "$R/q-$T.log" "$R/q24logs/$T.log"
grep -h "BenchRoute: hereclose\|BenchRoute: hitch" "$R/q-$T.log" | cut -c1-200 | head -20
[ -f "$R/q-$T.raw" ] || { echo "no capture"; exit 1; }
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$T.raw" -out "$W/$T-big.txt" -minms 3 -frames 6 -listms 40 -logFile "$W/bigframe.log" )
rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null
cut -c1-200 "$R/$T-big.txt" | head -150
log "q24b done"
