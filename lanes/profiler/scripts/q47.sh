#!/bin/bash
# Millfield townwalk big frame (supervisor, 11-06): q40 331 ms and q46 418 ms at the same spot (3253,10,-857), not every
# session. Profile build of 781fa5a, v2 route stop townwalk, uncapped, whole stop captured (-profilestop townwalk);
# BigFrame lists frames over 100 ms and dumps the 4 longest call trees. Up to 4 sessions, stopping at the first that
# catches a frame over 200 ms. The known quit crash (SurfaceGlow.FarScanJob after "BenchRoute: done", q46) is logged and
# the capture still used; any other crash stops the script.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=781fa5a
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q47 Millfield townwalk capture (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q47logs"; mkdir -p "$L"
crashcheck() { # log: 0 ok, 1 known quit crash, exits on any other crash
  grep -q "Crash!!!\|Received signal" "$1" || return 0
  if grep -q "BenchRoute: done" "$1" && grep -q "SurfaceGlow.FarScanJob" "$1"; then echo "known quit crash (SurfaceGlow.FarScanJob) in $(basename $1)"; return 1; fi
  echo "CRASH in $(basename $1): stopping"; grep -A3 "OUTPUTTING STACK TRACE" "$1" | head -12; grep "lib_burst_generated) \[\|(Assembly-CSharp)\|at Punchy" "$1" | head -12 | cut -c1-220; exit 1
}
for RD in 1 2 3 4; do
  T=prof47-townwalk-$RD; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T -island v2 -benchstops townwalk -fpscap 0 -hitchms 50 -profilestop townwalk "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashcheck "$L/$T.log"
  echo "== $T: $(grep -h 'BenchRoute: townwalk: avg' $L/$T.log | sed 's/, cpu.*//; s/BenchRoute: //')"
  grep -h "BenchRoute: hitch [0-9]\{3,\}" $L/$T.log | cut -c1-120
  [ -f "$R/q-$T.raw" ] || { echo "no capture"; continue; }
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$T.raw" -out "$W/$T-big.txt" -minms 2 -frames 4 -listms 100 -logFile "$W/bigframe.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$T.raw"
  cp "$R/$T-big.txt" "$L/" 2>/dev/null
  sed -n 1,2p "$R/$T-big.txt" | cut -c1-300
  grep -q "=\([2-9][0-9][0-9]\|[0-9]\{4,\}\)" <(sed -n 2p "$R/$T-big.txt") && { echo "big frame caught in $T"; break; }
done
log "q47 done"
