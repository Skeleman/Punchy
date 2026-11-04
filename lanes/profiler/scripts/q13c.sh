#!/bin/bash
# Item 13, part c: are 913d1b2's cave mesh jobs a late first bake (gone after 30 s more) or churn (still there)?
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q13c cave wait profile (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
log "q13c dialogs: $(dialogs)"
for K in 913d1b2 27bf8c1; do profile Profile-$K prof13-$K-cavewait cave -island v2 -fpscap 0 -benchwait 30
  echo "== $K wait30: $(grep -E '^frames used' $R/prof13-$K-cavewait.txt | head -1)"
  grep -v SPIKE "$R/prof13-$K-cavewait.txt" | grep -E "CaveSystem:(Column|Field|Mesh)Job" | cut -f1,5,6,7,8; done
grep -h "BenchRoute: cave:" "$R/q-prof13-913d1b2-cave.log" "$R/q-prof13-913d1b2-cavewait.log" | grep -o "cave chunks [0-9]* ([0-9]* meshed, [0-9]* buried)"
log "q13c done"
