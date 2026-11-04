#!/bin/bash
# #99 follow-up: job 23's fix build 84208ff crashed on the first switch in 7/7 runs. Does main (0ec7a8d, includes 84208ff)?
# Bench build + NetCycle, -island v2 -netcycle 3, 3 runs.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=0ec7a8d
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q23b #99 on main (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K Build Bench-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
for N in 1 2 3; do
  fixed Bench-${K}n || exit 1
  OUTD="$R/q23/main-v2-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$(cygpath -m $OUTD)/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island v2 -netcycle 3 >/dev/null 2>&1 )
  rc=$?; strays >/dev/null
  echo "== main-v2-$N rc=$rc, hosting $(grep -c 'NETCYCLE cycle [0-9] hosting' $OUTD/game.log)/3, back to solo $(grep -c 'NETCYCLE cycle [0-9] back to solo' $OUTD/game.log)/3, done $(grep -c 'NETCYCLE done' $OUTD/game.log), crash $(grep -c 'OUTPUTTING STACK TRACE' $OUTD/game.log), dialogs: $(dialogs)"
done
log "q23b done"
