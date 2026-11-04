#!/bin/bash
# Job 23d: #99 on a Dev (development) build of player-client 38a8f4c: do Dev builds avoid the crash? NetCycle, v2,
# 3 runs x -netcycle 5, default order only; stops at the first crash (Zach, 2026-11-04).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=38a8f4c
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q23d #99 38a8f4c Dev build (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K BuildDev Profile-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
fixed Profile-${K}n || exit 1
for N in 1 2 3; do for MODE in default; do
  OUTD="$R/q23/d-dev-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"
  EXTRA=""; [ $MODE = old ] && EXTRA="-hostorder old"
  cool
  ( cd "$P/Builds/Bench" && timeout 1200 ./Punchy.exe -logFile "$(cygpath -m $OUTD)/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island v2 -netcycle 5 $EXTRA >/dev/null 2>&1 )
  rc=$?; strays >/dev/null
  H=$(grep -c 'NETCYCLE cycle [0-9] hosting' $OUTD/game.log); BS=$(grep -c 'NETCYCLE cycle [0-9] back to solo' $OUTD/game.log); C=$(grep -c 'OUTPUTTING STACK TRACE' $OUTD/game.log)
  echo "== $MODE-$N rc=$rc, hosting $H/5, back to solo $BS/5, done $(grep -c 'NETCYCLE done' $OUTD/game.log), crash $C, exc $(grep -c Exception $OUTD/game.log), dialogs: $(dialogs)"
  grep -h -i "hostorder\|teardown" $OUTD/game.log | head -2 | cut -c1-160
  if [ "$C" != 0 ]; then grep -h -A6 "OUTPUTTING STACK TRACE" $OUTD/game.log | cut -c1-160; echo "first crash: stopping (Zach: one crash is enough)"; break 2; fi
done; done
log "q23d done"
