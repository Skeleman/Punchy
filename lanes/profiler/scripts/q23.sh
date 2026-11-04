#!/bin/bash
# #99 check (Zach asked): P-8's host-twice crash fix 84208ff vs its parent be6d005, Bench builds with the NetCycle helper
# (job 16's repro: -netcycle 3 = host, solo, host, solo, host, solo). Fix: 5 runs v2 + 2 runs v3. Parent: 3 runs v2.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
FIX=84208ff; PAR=be6d005
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q23 #99 host-twice check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $FIX Build Bench-${FIX}n; r1=$?
build $PAR Build Bench-${PAR}n; r2=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $r1 = 0 ] && [ $r2 = 0 ] || { echo "build failed ($r1 $r2)"; exit 1; }
keepawake
cyc() { # build island tag
  fixed $1 || return 1
  local OUTD="$R/q23/$3"; rm -rf "$OUTD"; mkdir -p "$OUTD"
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$(cygpath -m $OUTD)/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island $2 -netcycle 3 >/dev/null 2>&1 )
  local rc=$?
  strays >/dev/null
  echo "== $3 rc=$rc, cycles back to solo $(grep -c 'NETCYCLE cycle [0-9] back to solo' $OUTD/game.log)/3, done $(grep -c 'NETCYCLE done' $OUTD/game.log), exc $(grep -c Exception $OUTD/game.log), crash $(grep -c 'OUTPUTTING STACK TRACE\|Crash!!!' $OUTD/game.log), dialogs: $(dialogs)"
  grep -h "NETCYCLE" "$OUTD/game.log" | cut -c1-110
  grep -h -A4 "OUTPUTTING STACK TRACE" "$OUTD/game.log" | cut -c1-140
}
for N in 1 2 3 4 5; do cyc Bench-${FIX}n v2 fix-v2-$N; [ $N -le 3 ] && cyc Bench-${PAR}n v2 parent-v2-$N; done
for N in 1 2; do cyc Bench-${FIX}n v3 fix-v3-$N; done
log "q23 done"
