#!/bin/bash
# Job 22: cave FPS at the bat folk caves (#81) on main 0ec7a8d, uncapped. Spots: v2 bat folk / hut cave floor (530,45,958)
# and the surface above it (530,958); v3 bat folk chamber (-2674,23,-607) and the surface above (-2674,-607).
# (1) Bench build, 3 runs per spot (-benchstops here): frame, cpu, gpu, main/render thread ms (runner efbcd3a).
# (2) Profile build, 1 run per spot with -benchgpupasses and -benchstops herewarm,here: GPU passes, draw calls,
# batches, setpass, shadow casters, rendered tris (render counters only read in dev builds).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=0ec7a8d
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q22 bat folk cave GPU (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }
build $K BuildDev Profile-$K || { echo "build Profile-$K failed"; exit 1; }
keepawake
log "q22 dialogs: $(dialogs)"
mkdir -p "$R/q22logs"
spot() { case $1 in v2cave) echo "-island v2 -goto 530,45,958";; v2surf) echo "-island v2 -goto 530,958";;
  v3cave) echo "-island v3 -goto -2674,23,-607";; v3surf) echo "-island v3 -goto -2674,-607";; esac; }
for RD in 1 2 3; do for S in v2cave v2surf v3cave v3surf; do
  run Bench-$K q22-$S-$RD $(spot $S) -benchstops here -fpscap 0; cp "$R/q-q22-$S-$RD.log" "$R/q22logs/$S-$RD.log"
done; done
for S in v2cave v2surf v3cave v3surf; do
  run Profile-$K q22-gpu-$S $(spot $S) -benchstops herewarm,here -benchgpupasses -fpscap 0; cp "$R/q-q22-gpu-$S.log" "$R/q22logs/$S-gpu.log"
done
for f in "$R"/q22logs/*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f)"
  grep -h -e "BenchRoute: here:" -e "gpu: here (" $f | sed 's/, per 5 s.*//' | cut -c1-900
done
log "q22 done"
