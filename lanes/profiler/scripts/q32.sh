#!/bin/bash
# Job 32 on main 7492c31. A (#102): the island-ready frame, captured from launch in a Profile build, 3 runs on each
# island (v2, v3; the island seed is fixed in code, so the runs vary -weatherseed), BigFrame call tree of the longest
# frame; then one deep-profiling capture (v2) for the calls inside the unmarked bake. B (W-102 baseline): draw / setpass /
# batches / shadow casters from the capture's Render counters at fishing village 1 and the forest stop, plain / flora
# off / shadows off.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=7492c31
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q32 startup frame + draw counts (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
build $K BuildDeep Profile-${K}d || { echo "deep build failed"; exit 1; }
keepawake
L="$R/q32logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
editor() { # Tool.Method args...: one batchmode editor call with a profiler-only script copied in and removed after
  local tool=${1%%.*}; strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/$tool.cs.txt" "$P/Assets/Game/Scripts/Editor/$tool.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod "$@" -logFile "$W/editor-$tool.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/$tool.cs" "$P/Assets/Game/Scripts/Editor/$tool.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null
}
# ---- B first (cheap)
for SPOT in fv1 forest; do
  case $SPOT in fv1) A="-island v2 -goto -3304,-1644 -benchstops herewarm,here -benchlong here 10"; ST=here;; *) A="-island v2 -benchstops forest"; ST=forest;; esac
  for V in plain noflora noshadows; do
    case $V in plain) X=;; noflora) X="-benchnoshader Flora";; *) X=-benchnoshadows;; esac
    T=prof32-$SPOT-$V; rm -f "$R/q-$T.raw"
    run Profile-${K}p $T $A $X -fpscap 0 -profilestop $ST "$W/q-$T.raw"
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    [ -f "$R/q-$T.raw" ] && editor Counters.Dump -raw "$W\q-$T.raw" -out "$W/$T-counters.txt" || echo "no capture $T"
    echo "== $T: $(grep -h "BenchRoute: $ST: avg" $L/$T.log | sed 's/, 1% low.*gpu/, gpu/; s/, frames.*//')"; cat "$R/$T-counters.txt" 2>/dev/null
    rm -f "$R/q-$T.raw"
  done
done
# ---- A
for ISL in v2 v3; do for RD in 1 2 3; do
  T=prof32-start-$ISL-$RD; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T -island $ISL -weatherseed $RD -benchstops here -fpscap 20 -profiler-enable -profiler-maxusedmemory 268435456 -profiler-log-file "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  [ -f "$R/q-$T.raw" ] && editor BigFrame.Dump -raw "$W\q-$T.raw" -out "$W/$T-big.txt" -minms 20 -frames 2 -listms 300 || echo "no capture $T"
  echo "== $T: $(grep -h "world ready" $L/$T.log)"; sed -n 2p "$R/$T-big.txt" | cut -c1-300; sed -n 3,40p "$R/$T-big.txt"
  rm -f "$R/q-$T.raw"
done; done
T=prof32-deep-v2; rm -f "$R/q-$T.raw"
run Profile-${K}d $T -island v2 -benchstops here -fpscap 20 -deepprofiling -profiler-enable -profiler-maxusedmemory 1073741824 -profiler-log-file "$W/q-$T.raw"
cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
ls -la "$R/q-$T.raw" 2>&1
[ -f "$R/q-$T.raw" ] && editor BigFrame.Dump -raw "$W\q-$T.raw" -out "$W/$T-big.txt" -minms 50 -frames 1 -listms 1000 || echo "no capture $T"
echo "== $T: $(grep -h "world ready" $L/$T.log)"; head -150 "$R/$T-big.txt"
log "q32 done"
