#!/bin/bash
# Queue item 16: memory recheck on main f405348 (PR #95 W-73 bake blob fix, PR #96). (1) Editor, v2 then v3: play 1 timed
# with Burst safety checks / jobs debugger / leak detection off, then 10 enter/exit cycles with leak detection on;
# Total Used per cycle; the editor's settings put back at exit. (2) Player (Bench build + NetCycle helper): 3 host/solo
# cycles on v2 and v3, memory after each.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=f405348
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q16 memory recheck (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/NetCycle.cs.txt" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs"
build $K Build Bench-${K}n; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/NetCycle.cs" "$P/Assets/Game/Scripts/Debugging/NetCycle.cs.meta"
[ $rc = 0 ] || { echo "build Bench-${K}n failed"; exit 1; }
keepawake
log "q16 dialogs: $(dialogs)"
editor() { # island
  local ISL=$1 OUTD="$R/q16-editor-$1"
  rm -rf "$OUTD"; mkdir -p "$OUTD"
  strays >/dev/null
  ( cd "$P" && git checkout -q --detach $K && [ "$(git rev-parse --short=7 HEAD)" = "$K" ] ) || { log "checkout $K failed"; return 1; }
  cp "$S/MemCheck.cs.txt" "$P/Assets/Game/Scripts/Editor/MemCheck.cs"
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  log "q16 editor $ISL"
  ( cd "$P" && timeout 3300 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod MemCheck.Run -island $ISL -fasttiming -nosnap \
      -memout "$(cygpath -m $OUTD)" -logFile "$(cygpath -m $OUTD)/editor.log" )
  log "  editor $ISL rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  ( cd "$P" && git checkout -- ProjectSettings 2>/dev/null; rm -f Assets/Game/Scripts/Editor/MemCheck.cs Assets/Game/Scripts/Editor/MemCheck.cs.meta; git checkout -q $HOME_BRANCH )
  strays >/dev/null
  echo "== editor $ISL"
  grep -E "settings|bake log|leak detection|^== |TIMED|done" "$OUTD/memcheck.txt" | sed -E 's/Profiler total allocated.*//' | cut -c1-200
  grep -o "Total Used Memory [0-9]* MB" "$OUTD/memcheck.txt" | awk '{print $4}' | tr '\n' ' '; echo
  grep -i "Leak Detected\|leak(s) from callstack\|not been disposed" "$OUTD/editor.log" | sort | uniq -c | cut -c1-200
  grep -c "error CS" "$OUTD/editor.log" | sed 's/^/compile errors: /'
}
editor v2
editor v3
# (2) player
fixed Bench-${K}n || exit 1
for ISL in v2 v3; do
  OUTD="$R/q16-player-$ISL"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) -island $ISL -netcycle 3 >/dev/null 2>&1 )
  log "q16 player $ISL rc=$? dialogs: $(dialogs)"
  strays >/dev/null
  echo "== player $ISL: exc $(grep -c Exception $OUTD/game.log)"
  grep -h "NETCYCLE" "$OUTD/game.log" | cut -c1-230
  grep -h "\[island\].*ready at\|shared with" "$OUTD/game.log" | cut -c1-140
done
log "q16 done"
