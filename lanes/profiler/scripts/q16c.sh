#!/bin/bash
# Item 16, part c: is the 2nd-host crash new with W-73? Main before #95 (3a4acb8) + NetCycle, v2, -netcycle 3, 3 runs.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=3a4acb8
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q16c pre-W-73 host cycles (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K Build Bench-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
fixed Bench-${K}n || exit 1
for N in 1 2 3; do
  OUTD="$R/q16c-v2-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island v2 -netcycle 3 >/dev/null 2>&1 )
  echo "== run $N rc=$? dialogs: $(dialogs)"
  strays >/dev/null
  grep -h "NETCYCLE" "$OUTD/game.log" | cut -c1-110
  grep -h -A2 "OUTPUTTING STACK TRACE" "$OUTD/game.log" | cut -c1-120
done
