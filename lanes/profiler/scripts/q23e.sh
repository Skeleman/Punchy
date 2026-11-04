#!/bin/bash
# Job 23e: #99 retest on main 2dae74b (player-client 46abd95: new worlds made 2 frames after the old ones go). Release
# Bench build + NetCycle, -island v2 -fpscap 0 -netcycle 3. Run A (default gap): runs until 15 re-hosts or the first crash.
# Only if A is clean, run B (-switchgap 0, the old same-frame switch) the same way. Stops at the first crash (Zach's rule).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2dae74b
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q23e #99 2dae74b (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K Build Bench-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
fixed Bench-${K}n || exit 1
series() { # label extra-args...: returns 1 on a crash
  local L=$1; shift; local total=0 N=0
  while [ $total -lt 15 ] && [ $N -lt 8 ]; do
    N=$((N+1)); local OUTD="$R/q23/e-$L-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"
    cool
    ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$(cygpath -m $OUTD)/game.log" -weatherseed 4 -time 12 $(screen Bench 1280 720) -island v2 -fpscap 0 -netcycle 3 "$@" >/dev/null 2>&1 )
    local rc=$?; strays >/dev/null
    local H=$(grep -c 'NETCYCLE cycle [0-9] hosting' $OUTD/game.log) C=$(grep -c 'OUTPUTTING STACK TRACE' $OUTD/game.log)
    local G=$(grep -c 'GameBootstrap: making the new worlds (2 frames after the old ones went)' $OUTD/game.log)
    total=$((total+H))
    echo "== $L run $N rc=$rc: re-hosts $H (total $total), back to solo $(grep -c 'NETCYCLE cycle [0-9] back to solo' $OUTD/game.log), crash $([ $C != 0 ] && echo yes || echo no), 'making the new worlds (2 frames...)' lines $G, exc $(grep -c Exception $OUTD/game.log), dialogs: $(dialogs)"
    grep -h -m2 "GameBootstrap: making the new worlds" $OUTD/game.log | cut -c1-160
    if [ $C != 0 ]; then grep -h -A8 "OUTPUTTING STACK TRACE" $OUTD/game.log | cut -c1-160; echo "first crash in $L: stopping"; return 1; fi
  done
  echo "== $L clean: $total re-hosts in $N runs"; return 0
}
series A && series B -switchgap 0
log "q23e done"
