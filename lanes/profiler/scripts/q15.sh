#!/bin/bash
# Queue item 15: Burst and jobs audit on main 3a4acb8. Dev profiles (uncapped, v2) at every q13 stop; the analysis
# (worker vs main busy time, top main-thread systems) is done from results/prof15-*.txt afterwards.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=3a4acb8
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q15 burst audit profiles (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-$K || { echo "build Profile-$K failed"; exit 1; }
keepawake
log "q15 dialogs: $(dialogs)"
for S in town townwalk cave caveapproach glide; do profile Profile-$K prof15-$S $S -island v2 -fpscap 0; done
profile Profile-$K prof15-cavefloor here -island v2 -fpscap 0 -goto 530,45,958
profile Profile-$K prof15-wharf here -island v2 -fpscap 0 -place prop:FishWharf
for f in "$R"/q-prof15-*.log; do echo "$(basename $f) exc $(grep -c Exception $f) $(grep -o 'BenchRoute: cave: settled.*' $f)"; done
for f in "$R"/prof15-*.txt; do echo "$(basename $f): $(grep -E '^frames used' $f | head -1)"; done
log "q15 done"
