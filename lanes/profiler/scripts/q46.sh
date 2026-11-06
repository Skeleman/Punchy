#!/bin/bash
# FaunaDirector spike recheck (supervisor, 11-06): PR #172 time-sliced the comfort grid refresh. On main 781fa5a
# (#172 + #173), q40's slow spots again: Bench build, uncapped, frames over 33 ms: the v2 route stops townwalk, run,
# glide (2 sessions), and the host + client join/leave at the v2 cave (2 sessions). Profile captures (-fpscap 60) of
# run, the v2 fishing village and the salt walk-up through BigFrame + BProfileTools (spike markers: FaunaDirector).
# Compared with job 28 (6f139d0) and q40 (adb11e0).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=781fa5a
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q46 FaunaDirector recheck (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q46logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
H="-hitchms 33 -fpscap 0"
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
for RD in 1 2; do
  run Bench-${K} q46-route-$RD -island v2 -benchstops townwalk,run,glide $H; cp "$R/q-q46-route-$RD.log" "$L/route-$RD.log"; crashed "$L/route-$RD.log"
  joinleave Bench-${K} joinleave-$RD "$H"
done
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
  local T=prof46-$1 ST=$2; shift 2; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T "$@" -profilestop $ST "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  [ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
}
cap() { # tag stop args...
  local T=prof46-$1 ST=$2; shift 2; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T "$@" -profilestop $ST "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  [ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
}
cap run run -island v2 -benchstops run -fpscap 60
cap fv here -island v2 -goto -2883,-2121 -benchstops here -benchlong here 30 -fpscap 60
cap salt hereclose -island v3 -goto 3918,-1125 -close 3702,-917,134 -benchstops hereclose -fpscap 60
echo "==== Bench"
for f in "$L"/route-*.log "$L"/joinleave-*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f), hitches $(grep -c 'BenchRoute: hitch' $f), by stop: $(grep -h 'BenchRoute: hitch' $f | awk '{print $NF}' | sort | uniq -c | tr '
' ' ')"
  grep -h "BenchRoute: [a-z]*: avg" $f | sed 's/, gpu.*//' | cut -c1-160
done
echo "==== Profile"
for f in "$R"/prof46-*-big.txt; do
  T=$(basename $f -big.txt); echo "=== $T"; sed -n 2p $f | cut -c1-400; grep "frames used" "$R/$T.txt"; grep "^SPIKE" "$R/$T.txt" | head -8 | cut -c1-160
  grep -h "Main|FaunaDirector.Update\|Main|FaunaDirector.Update	" "$R/$T.txt" | grep -v SPIKE | head -2 | cut -c1-160
done
log "q46 done"
