#!/bin/bash
# Queue item 11, walking: the player walks through the biggest cave (v2 and v1), push-out sliding it along the walls,
# with -perflog per leg (last 3 s: frame mean/max, main, GPU, chunks, probe, IsRock) and walk lines (where it got to).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=762c16a
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q11c cave walk (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
fixed Bench-$K || exit 1
walk() { # island x y z
  local ISL=$1 X=$2 Y=$3 Z=$4 EX=""; [ $ISL = v1 ] && EX="-island v1"
  OUTD="$R/q11c-$ISL"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  { echo "-gocave #1 -wait 15 -perflog -shot start"
    echo "-walk $(awk "BEGIN{print $X + 60}"),$Y,$Z -wait 12 -perflog -shot east"
    echo "-walk $X,$Y,$(awk "BEGIN{print $Z + 60}") -wait 12 -perflog -shot north"
    echo "-walk $(awk "BEGIN{print $X - 60}"),$Y,$Z -wait 12 -perflog -shot west"
    echo "-walk $X,$Y,$(awk "BEGIN{print $Z - 60}") -wait 12 -perflog -shot south"; } > "$OUTD/list.txt"
  cool
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 60 $(screen Bench 1280 720) \
      -shotdir "$WO" -shotlist "$WO/list.txt" -quit $EX >/dev/null 2>&1 )
  strays >/dev/null
  log "q11c $ISL: exc $(grep -c Exception $OUTD/game.log)"
  grep -h "DebugControls: perf" "$OUTD/game.log" | cut -c1-330
  grep -h "DebugControls: walk at" "$OUTD/game.log" | awk 'NR % 8 == 0' | cut -c1-160 | tail -12
}
walk v2 -1653.3 29.7 2380.4
walk v1 -2676.9 13.6 -696.9
log "q11c done"
