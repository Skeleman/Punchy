#!/bin/bash
# Job 27 (standing order: find the remaining frames over 33 ms) on main 1699574 (W-92 merged).
# Part A, Bench build, uncapped, runner hitch log at 33 ms (-hitchms 33): the route stops (town, townwalk, forest,
# cave, caveexit, lake, river, run, glide, dash) on v2; the fish wharf (-place prop:FishWharf, 30 s); every colossus
# placed on v3, walked up to (hereclose); a host + external client at the v2 cave where the client leaves and the host
# keeps measuring.
# Part B, Profile build: a capture per stop (-profilestop, -fpscap 60 so 2000 frames hold the stop), the wharf, the
# lizard walk, the host across a client join/leave (-fpscap 20), and startup from launch (-fpscap 20, TESTING.md).
# Each capture: BigFrame (frames over 33 ms listed, the 12 longest dumped) and BProfileTools (spike-frame markers).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=1699574
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q27 big frame hunt (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q27logs"; mkdir -p "$L"
log "q27 dialogs: $(dialogs)"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
H="-hitchms 33 -fpscap 0"
ROUTE="town townwalk forest cave caveexit lake river run glide dash"
# ---- Part A
run Bench-${K} q27-route -island v2 -benchstops $(echo $ROUTE | tr ' ' ,) $H; cp "$R/q-q27-route.log" "$L/route.log"; crashed "$L/route.log"
run Bench-${K} q27-wharf -island v2 -place prop:FishWharf -benchstops here -benchlong here 30 $H; cp "$R/q-q27-wharf.log" "$L/wharf.log"; crashed "$L/wharf.log"
run Bench-${K} q27-probe -island v3 -benchstops here $H; cp "$R/q-q27-probe.log" "$L/probe.log"
grep -h "StoneColossi\|Dunes\|Salt" "$L/probe.log" | grep -i "placed\|colossus" | cut -c1-200
start() { echo "$1" | awk -F, '{ y = $3 * 3.14159265 / 180; printf "%.0f,%.0f\n", $1 + 300 * sin(y), $2 + 300 * cos(y) }'; }
grep -o "StoneColossi: [A-Za-z]* placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*), facing [-0-9.]*" "$L/probe.log" |
  sed 's/StoneColossi: \([A-Za-z]*\) placed at (\([-0-9.]*\), [-0-9.]*, \([-0-9.]*\)), facing \([-0-9.]*\)/\1 \2,\3,\4/' > "$L/colossi.txt"
LZ=
while read NAME C; do
  [ "$NAME" = SkeletalLizard ] && LZ=$C
  run Bench-${K} q27-walk-$NAME -island v3 -goto $(start $C) -close $C -benchstops hereclose $H
  cp "$R/q-q27-walk-$NAME.log" "$L/walk-$NAME.log"; crashed "$L/walk-$NAME.log"
done < "$L/colossi.txt"
joinleave() { # outdir tag extra-host-args: host + client at the v2 cave; the client leaves at the end of its route, the host measures on
  local HL="$L/$2-host.log" CL="$L/$2-client.log"; rm -f "$HL" "$CL"
  local C="-benchroute -weatherseed 4 -time 12 $(screen Bench 1280 720) -island v2 -benchstops here -goto 530,45,958"
  fixed $1 || return 1; cool
  ( cd "$P/Builds/Bench" && timeout 480 ./Punchy.exe -logFile "$(cygpath -m $HL)" $C -benchwait 40 -benchlong here 130 $3 -host >/dev/null 2>&1 ) &
  local HP=$!
  for i in $(seq 1 120); do grep -q "BenchRoute: world ready" "$HL" 2>/dev/null && break; sleep 1; done
  sleep 20
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$(cygpath -m $CL)" $C -benchwait 10 -benchlong here 30 -hitchms 33 -fpscap 0 -join 127.0.0.1 >/dev/null 2>&1 ) &
  local CP=$!
  wait $CP; wait $HP; strays >/dev/null
  crashed "$HL"; crashed "$CL"
}
joinleave Bench-${K} joinleave "$H"
# ---- Part B
analyze() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$1.raw" -out "$W/$1-big.txt" -minms 3 -frames 12 -listms 33 -logFile "$W/bigframe.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\\q-$1.raw" -out "$W/$1.txt" -logFile "$W/analyze.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
}
cap() { # tag stop args...
  local T=prof27-$1 ST=$2; shift 2; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T "$@" -profilestop $ST "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  [ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
}
for ST in $ROUTE; do cap $ST $ST -island v2 -benchstops $ST -fpscap 60; done
cap wharf here -island v2 -place prop:FishWharf -benchstops here -benchlong here 30 -fpscap 60
[ -n "$LZ" ] && cap lizard hereclose -island v3 -goto $(start $LZ) -close $LZ -benchstops hereclose -fpscap 60
rm -f "$R/q-prof27-joinleave.raw"
joinleave Profile-${K}p prof27-joinleave "-fpscap 20 -profilestop here $W/q-prof27-joinleave.raw"
[ -f "$R/q-prof27-joinleave.raw" ] && analyze prof27-joinleave
# Startup: capture from launch (TESTING.md), -fpscap 20 so the editor's 2000 frames reach back to the island-ready frame.
T=prof27-startup; rm -f "$R/q-$T.raw"
run Profile-${K}p $T -island v2 -benchstops here -fpscap 20 -profiler-enable -profiler-maxusedmemory 268435456 -profiler-log-file "$W/q-$T.raw"
cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
[ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
# ---- Summary
echo "==== Part A: frames over 33 ms per log (count, worst), and each stop's line"
for f in "$L"/route.log "$L"/wharf.log "$L"/walk-*.log "$L"/joinleave-*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f), hitches $(grep -c 'BenchRoute: hitch' $f)"
  grep -h "BenchRoute: [a-z]*: avg" $f | sed 's/, gfx.*//' | cut -c1-200
  grep -h "BenchRoute: hitch" $f | awk '{print $3, $NF}' | sort | uniq -c | sort -rn | head -8
  grep -h "onnected\|disconnected" $f | head -3
done
echo "==== Part B"
for f in "$R"/prof27-*-big.txt; do
  T=$(basename $f -big.txt); echo "=== $T"; sed -n 2p $f | cut -c1-400; grep "SPIKES\|frames used" "$R/$T.txt"; grep "^SPIKE" "$R/$T.txt" | head -8 | cut -c1-160
done
log "q27 done"
