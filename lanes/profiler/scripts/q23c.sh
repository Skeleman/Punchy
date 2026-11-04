#!/bin/bash
# #99 release blocker: player-client 38a8f4c (P-10 row 0a, teardown reordered: worlds' jobs, then the scene, then dispose).
# Bench build + NetCycle, v2, 3 runs x -netcycle 5 (15 re-hosts) with the default order and 3 x 5 with -hostorder old,
# interleaved. A crash ends its run, so the attempts are counted.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=38a8f4c
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q23c #99 38a8f4c (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K Build Bench-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
fixed Bench-${K}n || exit 1
for N in 1 2 3; do for MODE in default old; do
  OUTD="$R/q23/c-$MODE-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"
  EXTRA=""; [ $MODE = old ] && EXTRA="-hostorder old"
  cool
  ( cd "$P/Builds/Bench" && timeout 1200 ./Punchy.exe -logFile "$(cygpath -m $OUTD)/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island v2 -netcycle 5 $EXTRA >/dev/null 2>&1 )
  rc=$?; strays >/dev/null
  H=$(grep -c 'NETCYCLE cycle [0-9] hosting' $OUTD/game.log); BS=$(grep -c 'NETCYCLE cycle [0-9] back to solo' $OUTD/game.log); C=$(grep -c 'OUTPUTTING STACK TRACE' $OUTD/game.log)
  echo "== $MODE-$N rc=$rc, hosting $H/5, back to solo $BS/5, done $(grep -c 'NETCYCLE done' $OUTD/game.log), crash $C, exc $(grep -c Exception $OUTD/game.log), dialogs: $(dialogs)"
  grep -h -i "hostorder\|teardown" $OUTD/game.log | head -2 | cut -c1-160
  [ "$C" != 0 ] && grep -h -A6 "OUTPUTTING STACK TRACE" $OUTD/game.log | cut -c1-160
done; done
log "q23c done"
