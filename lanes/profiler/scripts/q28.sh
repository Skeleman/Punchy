#!/bin/bash
# Job 28: re-run of job 27 on main 6f139d0 (NetMenu fix #125, W-94 salt ball/hazard houses/ruin bricks, A-66 fish folk/pygmies/Decide).
# Part A, Bench build, uncapped, runner hitch log at 33 ms (-hitchms 33): the route stops (town, townwalk, forest,
# cave, caveexit, lake, river, run, glide, dash) on v2; the fish wharf (-place prop:FishWharf, 30 s); every colossus
# placed on v3, walked up to (hereclose); a host + external client at the v2 cave where the client leaves and the host
# keeps measuring.
# Part B, Profile build: a capture per stop (-profilestop, -fpscap 60 so 2000 frames hold the stop), the wharf, the
# lizard walk, the host across a client join/leave (-fpscap 20), and startup from launch (-fpscap 20, TESTING.md).
# Each capture: BigFrame (frames over 33 ms listed, the 12 longest dumped) and BProfileTools (spike-frame markers).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=6f139d0
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q28 recheck (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q28logs"; mkdir -p "$L"
log "q28 dialogs: $(dialogs)"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
H="-hitchms 33 -fpscap 0"
ROUTE="town townwalk forest cave caveexit lake river run glide dash"
# ---- Part A (Bench, uncapped, hitch log at 33 ms)
run Bench-${K} q28-route -island v2 -benchstops $(echo $ROUTE | tr ' ' ,) $H; cp "$R/q-q28-route.log" "$L/route.log"; crashed "$L/route.log"
run Bench-${K} q28-wharf -island v2 -place prop:FishWharf -benchstops here -benchlong here 30 $H; cp "$R/q-q28-wharf.log" "$L/wharf.log"; crashed "$L/wharf.log"
grep -h "fishing village [0-9] at" "$L/route.log" | sed 's/.*village \([0-9]\) at \([-0-9]*\),\([-0-9]*\).*/v2 \1 \2,\3/' > "$L/villages.txt"
run Bench-${K} q28-probe -island v3 -benchstops here $H; cp "$R/q-q28-probe.log" "$L/probe.log"
grep -h "fishing village [0-9] at" "$L/probe.log" | sed 's/.*village \([0-9]\) at \([-0-9]*\),\([-0-9]*\).*/v3 \1 \2,\3/' >> "$L/villages.txt"
cat "$L/villages.txt"
while read IS N XZ; do
  run Bench-${K} q28-fv-$IS-$N -island $IS -goto $XZ -benchstops here -benchlong here 30 $H; cp "$R/q-q28-fv-$IS-$N.log" "$L/fv-$IS-$N.log"; crashed "$L/fv-$IS-$N.log"
done < "$L/villages.txt"
start() { echo "$1" | awk -F, '{ y = $3 * 3.14159265 / 180; printf "%.0f,%.0f\n", $1 + 300 * sin(y), $2 + 300 * cos(y) }'; }
SC=$(grep -o "StoneColossi: SaltCrystal placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*), facing [-0-9.]*" "$L/probe.log" | sed 's/.*(\([-0-9.]*\), [-0-9.]*, \([-0-9.]*\)), facing \([-0-9.]*\)/\1,\2,\3/')
echo "salt crystal $SC"
run Bench-${K} q28-salt -island v3 -goto $(start $SC) -close $SC -benchstops hereclose $H; cp "$R/q-q28-salt.log" "$L/salt.log"; crashed "$L/salt.log"
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
# ---- Part B (Profile captures, -fpscap 60, last 2000 frames)
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
cap() { # tag stop args...
  local T=prof28-$1 ST=$2; shift 2; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T "$@" -profilestop $ST "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  [ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
}
cap townwalk townwalk -island v2 -benchstops townwalk -fpscap 60
cap glide glide -island v2 -benchstops glide -fpscap 60
F1=$(head -1 "$L/villages.txt" | cut -d' ' -f3); cap fv here -island v2 -goto $F1 -benchstops here -benchlong here 30 -fpscap 60
cap salt hereclose -island v3 -goto $(start $SC) -close $SC -benchstops hereclose -fpscap 60
# ---- Summary
echo "==== Part A"
for f in "$L"/route.log "$L"/wharf.log "$L"/fv-*.log "$L"/salt.log "$L"/joinleave-*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f), hitches $(grep -c 'BenchRoute: hitch' $f)"
  grep -h "BenchRoute: [a-z]*: avg" $f | sed 's/, gpu.*//' | cut -c1-160
  grep -h -B1 "BenchRoute: hitch" $f | cut -c1-110 | grep -v "^--"
done
echo "==== Part B"
for f in "$R"/prof28-*-big.txt; do
  T=$(basename $f -big.txt); echo "=== $T"; sed -n 2p $f | cut -c1-400; grep "frames used" "$R/$T.txt"; grep "^SPIKE" "$R/$T.txt" | head -8 | cut -c1-160
done
log "q28 done"
