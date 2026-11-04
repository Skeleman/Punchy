#!/bin/bash
# Queue item 13, part b: the biggest cave (route "cave" stop) is ~0.9 ms CPU slower on 913d1b2 than 27bf8c1: one profile
# each there, uncapped, v2.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q13b cave profiles (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build 27bf8c1 BuildDev Profile-27bf8c1 || exit 1
keepawake
log "q13b dialogs: $(dialogs)"
for K in 27bf8c1 913d1b2; do profile Profile-$K prof13-$K-cave cave -island v2 -fpscap 0; done
profdiff "$R/prof13-27bf8c1-cave.txt" "$R/prof13-913d1b2-cave.txt" | head -30
for K in 27bf8c1 913d1b2; do echo "== $K"; grep -v SPIKE "$R/prof13-$K-cave.txt" | grep -E "PlayerMovementSystem|ReachJob|ThirdPersonCamera.LateUpdate|TraceSystem$|CaveSystem|KitCollision|FloraSystem" | cut -f1-8; done
log "q13b done"
