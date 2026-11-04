#!/bin/bash
# Item 16, part b: does the v2 player crash (Burst statics init) on the 2nd host again? Same build, v2, -netcycle 3, twice.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q16b v2 host crash repro (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
fixed Bench-f405348n || exit 1
for N in 1 2; do
  OUTD="$R/q16b-v2-$N"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island v2 -netcycle 3 >/dev/null 2>&1 )
  echo "== run $N rc=$? dialogs: $(dialogs)"
  strays >/dev/null
  grep -h "NETCYCLE" "$OUTD/game.log" | cut -c1-120
  grep -h -A3 "OUTPUTTING STACK TRACE" "$OUTD/game.log" | cut -c1-120
done
