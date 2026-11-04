#!/bin/bash
# Job 24c: the far shadow fix (W-90, PR #120: strips laid in a Burst job, applied a frame later) on main ccdad75. The q24
# colossus walks (hereclose, 300 m -> 20 m at 6 m/s, v3, uncapped) with far shadows on vs -nofarshadow, 2 runs each,
# rotated; then a Profile-build capture of the lizard walk (shadows on) with BigFrame and BProfileTools, for the worst
# frame and StoneColossi.Update per frame / max call.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=ccdad75
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q24c far shadow fix check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
mkdir -p "$R/q24clogs"
log "q24c dialogs: $(dialogs)"
run Bench-${K} q24c-probe -island v3 -benchstops here -fpscap 0
cp "$R/q-q24c-probe.log" "$R/q24clogs/probe.log"
grep -h "StoneColossi" "$R/q24clogs/probe.log"
spec() { grep -o "StoneColossi: $1 placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*), facing [-0-9.]*" "$R/q24clogs/probe.log" | head -1 |
           sed 's/.*(\([-0-9.]*\), [-0-9.]*, \([-0-9.]*\)), facing \([-0-9.]*\)/\1,\2,\3/'; }
start() { echo "$1" | awk -F, '{ y = $3 * 3.14159265 / 180; printf "%.0f,%.0f\n", $1 + 300 * sin(y), $2 + 300 * cos(y) }'; }
LZ=$(spec SkeletalLizard); PL=$(spec CarnivorousPlant)
echo "lizard -close $LZ, plant -close $PL"
for RD in 1 2; do
  case $RD in 1) ORDER="lizard lizard-off plant plant-off";; *) ORDER="plant-off plant lizard-off lizard";; esac
  for k in $ORDER; do
    case $k in lizard*) C=$LZ;; *) C=$PL;; esac
    [ -n "$C" ] || continue
    OFF=; case $k in *-off) OFF=-nofarshadow;; esac
    T=q24c-$k-$RD
    run Bench-${K} $T -island v3 -goto $(start $C) -close $C -benchstops hereclose -fpscap 0 $OFF
    cp "$R/q-$T.log" "$R/q24clogs/$k-$RD.log"
    if grep -q "Crash!!!\|Received signal" "$R/q-$T.log"; then echo "CRASH in $T: stopping"; grep -A15 "Crash!!!\|Received signal" "$R/q-$T.log" | head -30; exit 1; fi
  done
done
for f in "$R"/q24clogs/*-[12].log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f), hitches $(grep -c 'BenchRoute: hitch' $f)"
  grep -h -e "BenchRoute: hereclose" $f | sed 's/, per 5 s worst ms\/slow frames:/ | 5s:/' | cut -c1-420
  grep -h "BenchRoute: hitch" $f | cut -c1-110 | head -5
done
T=prof24c-lizard; rm -f "$R/q-$T.raw"
run Profile-${K}p $T -island v3 -goto $(start $LZ) -close $LZ -benchstops hereclose -profilestop hereclose "$W/q-$T.raw" -fpscap 0
cp "$R/q-$T.log" "$R/q24clogs/$T.log"
grep -h "BenchRoute: hereclose" "$R/q-$T.log" | cut -c1-200
[ -f "$R/q-$T.raw" ] || { echo "no capture"; exit 1; }
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$T.raw" -out "$W/$T-big.txt" -minms 3 -frames 4 -listms 30 -logFile "$W/bigframe.log" )
rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\\q-$T.raw" -out "$W/$T.txt" -logFile "$W/analyze.log" )
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null
cut -c1-200 "$R/$T-big.txt" | head -80
echo "== StoneColossi / FarShadow markers"; grep -i "StoneColossi\|FarShadow\|Lay" "$R/$T.txt" | cut -c1-200 | head -10
head -3 "$R/$T.txt" | cut -c1-200; grep "frames used" "$R/$T.txt"
log "q24c done"
