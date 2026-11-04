#!/bin/bash
# Job 27b: SaltCrystalRoll.Update cost (W-93 owns the file: measure only). Profile-1699574p, v3, the q27 hereclose walk to
# the salt crystal (300 m out along its facing, 6 m/s to 20 m), -fpscap 60, last 2000 frames; BigFrame and BProfileTools
# with -keep SaltCrystal so the marker is listed however small.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=1699574
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q27b salt crystal roll (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
C=$(grep "^SaltCrystal " "$R/q27logs/colossi.txt" | cut -d' ' -f2)
start() { echo "$1" | awk -F, '{ y = $3 * 3.14159265 / 180; printf "%.0f,%.0f\n", $1 + 300 * sin(y), $2 + 300 * cos(y) }'; }
T=prof27b-salt; rm -f "$R/q-$T.raw"
run Profile-${K}p $T -island v3 -goto $(start $C) -close $C -benchstops hereclose -fpscap 60 -profilestop hereclose "$W/q-$T.raw"
cp "$R/q-$T.log" "$R/q27logs/$T.log"
grep -h "BenchRoute: hereclose\|SaltCrystal" "$R/q-$T.log" | cut -c1-200 | head -8
grep -q "Crash!!!\|Received signal" "$R/q-$T.log" && { grep -A15 "Crash!!!\|Received signal" "$R/q-$T.log" | head -30; exit 1; }
[ -f "$R/q-$T.raw" ] || { echo "no capture"; exit 1; }
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\q-$T.raw" -out "$W/$T-big.txt" -minms 3 -frames 6 -listms 33 -logFile "$W/bigframe.log" )
rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\q-$T.raw" -out "$W/$T.txt" -keep SaltCrystal -logFile "$W/analyze.log" )
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null; rm -f "$R/q-$T.raw"
sed -n 2p "$R/$T-big.txt" | cut -c1-300; grep "frames used\|SPIKES" "$R/$T.txt"; grep -i "SaltCrystal" "$R/$T.txt" | cut -c1-200
log "q27b done"
