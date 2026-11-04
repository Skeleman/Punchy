#!/bin/bash
# Queue item 14: memory check on main (5d0956a). (1) Bench build (runner 3f76120: cave stop settles until caves built).
# (2) Batchmode editor with MemCheck.cs: idle, play 1 after the bake, after exit (snapshot), 10 more play cycles,
# after (snapshot); top 10 types/objects; growth. (3) Player RAM on -island v2 and v3 (town + cave, uncapped).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5d0956a
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q14 memory check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }
keepawake
log "q14 dialogs: $(dialogs)"
# (2) editor
OUTD="$R/q14"; rm -rf "$OUTD"; mkdir -p "$OUTD"
strays >/dev/null
( cd "$P" && git checkout -q --detach $K && [ "$(git rev-parse --short=7 HEAD)" = "$K" ] ) || { log "checkout $K failed"; exit 1; }
cp "$S/MemCheck.cs.txt" "$P/Assets/Game/Scripts/Editor/MemCheck.cs"
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
log "q14 editor run"
( cd "$P" && timeout 3300 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod MemCheck.Run -memout "$(cygpath -m $OUTD)" -logFile "$(cygpath -m $OUTD)/editor.log" )
log "  editor rc=$?"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
( cd "$P" && git checkout -- ProjectSettings 2>/dev/null; rm -f Assets/Game/Scripts/Editor/MemCheck.cs Assets/Game/Scripts/Editor/MemCheck.cs.meta; git checkout -q $HOME_BRANCH )
strays >/dev/null
cat "$OUTD/memcheck.txt"
grep -c "Exception" "$OUTD/editor.log" | sed 's/^/editor exceptions: /'
grep -m5 "error CS" "$OUTD/editor.log"
# (3) player RAM
rm -f "$R/q14.csv"
for ISL in v2 v3; do
  runcsv Bench-$K q14 $K-$ISL 0 1 -island $ISL -benchstops town,cave -fpscap 0
  cp "$R/q-q14-c0-1.log" "$R/q14-$ISL.log"
  echo "== player $ISL: $(grep -m1 -o 'world ready.*' $R/q14-$ISL.log | cut -c1-60); exc $(grep -c Exception $R/q14-$ISL.log)"
  grep -o "\[island\] reports.*" "$R/q14-$ISL.log" | head -2 | cut -c1-120
  grep -o "BenchRoute: cave: settled.*" "$R/q14-$ISL.log"
  grep -h "BenchRoute: [a-z]*: avg" "$R/q14-$ISL.log" | sed -E 's/.*BenchRoute: ([a-z]+): avg ([0-9.]+) fps.*(gfx [^,]*), (system [0-9]+ MB).*/  \1 avg \2 fps, \3, \4/'
done
log "q14 done"
