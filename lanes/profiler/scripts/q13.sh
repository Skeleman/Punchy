#!/bin/bash
# Queue item 13: merged main 913d1b2 (box collision #89, P-1 #90, W-68 v3 flag #91) vs 27bf8c1. -island v2, uncapped,
# 2 rounds flipped: route town,townwalk,cave,caveapproach,glide; hut cave floor (-goto 530,45,958); FishWharf steps.
# Profiles of 913d1b2 at townwalk, wharf and hut cave (27bf8c1's are results/prof12-27bf8c1-*.txt, same build).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
OLD=27bf8c1; NEW=913d1b2
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q13 merged main check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $NEW Build Bench-$NEW || { echo "BUILD FAILED Bench-$NEW"; exit 1; }
build $NEW BuildDev Profile-$NEW || { echo "BUILD FAILED Profile-$NEW"; exit 1; }
build $OLD Build Bench-$OLD || exit 1
keepawake
log "q13 dialogs: $(dialogs)"
rm -f "$R/q13.csv"
for RD in 1 2; do
  if [ $RD = 1 ]; then ORDER="$OLD $NEW"; else ORDER="$NEW $OLD"; fi
  for K in $ORDER; do
    for S in route cave wharf; do
      SX=""; if [ $RD = 1 ]; then SD="$R/shots-q13/$K-$S"; mkdir -p "$SD"; SX="-benchshots $(cygpath -m "$SD")"; fi
      case $S in
        route) runcsv Bench-$K q13 $K-route 0 $RD -island v2 -benchstops town,townwalk,cave,caveapproach,glide -fpscap 0 $SX ;;
        cave)  runcsv Bench-$K q13 $K-cavefloor 0 $RD -island v2 -benchstops here -goto 530,45,958 -fpscap 0 $SX ;;
        wharf) runcsv Bench-$K q13 $K-wharf 0 $RD -island v2 -benchstops here -place prop:FishWharf -fpscap 0 $SX ;;
      esac
      cp "$R/q-q13-c0-$RD.log" "$R/q13-$K-$S-$RD.log"
    done
  done
done
awk -F, 'NR > 1 { k = $1 " " $4; n[k]++; a[k] += $5; l[k] += $6; c[k] += $9; g[k] += $10; e[k] += $15; at[k] = at[k] " " $12 "," $13 }
  END { for (k in n) printf "  %-26s avg %5.1f  1%% low %5.1f  cpu %5.1f  gpu %5.1f  exc %d  at%s\n", k, a[k]/n[k], l[k]/n[k], c[k]/n[k], g[k]/n[k], e[k], at[k] }' "$R/q13.csv" | sort | tee "$R/q13-table.txt"
grep -h "BenchRoute: here" "$R"/q13-*-cave-1.log | grep -o "surface hidden [0-9.]*"
profile Profile-$NEW prof13-$NEW-townwalk townwalk -island v2 -fpscap 0
profile Profile-$NEW prof13-$NEW-wharf here -island v2 -fpscap 0 -place prop:FishWharf
profile Profile-$NEW prof13-$NEW-cavefloor here -island v2 -fpscap 0 -goto 530,45,958
for S in townwalk wharf cavefloor; do
  echo "== $S $OLD -> $NEW"; profdiff "$R/prof12-$OLD-$S.txt" "$R/prof13-$NEW-$S.txt" | head -16
  grep -v SPIKE "$R/prof13-$NEW-$S.txt" | grep -E "PlayerMovementSystem|ReachJob|ThirdPersonCamera.LateUpdate|TraceSystem$" | cut -f1-8
done
for f in "$R"/q13-*.log "$R"/q-prof13-*.log; do echo "$(basename $f) exc $(grep -c Exception $f) nan $(grep -c 'Mesh bounds' $f) $(grep -m1 -o 'world ready.*' $f | cut -c1-50)"; done
find "$R/shots-q13" -type f
log "q13 done"
