#!/bin/bash
# Queue item 12: box collision cost (#79, W-67). main 27bf8c1 vs world-features 52b4af5, plus 52b4af5's parent b28ee7b
# (world-features isn't based on 27bf8c1, so b28ee7b -> 52b4af5 isolates the collision). Island v2 (the default).
# (1) route town,townwalk,cave,caveapproach,glide; the cave spot (-goto 530,958, here); a wharf (-place prop:FishWharf,
# here). Cap 0 , 2 rounds, order flipped. (2) dev profiles at townwalk and at the wharf on each build.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
MAIN=27bf8c1; PRE=b28ee7b; NEW=52b4af5
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q12 box collision cost (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $NEW Build Bench-$NEW || { echo "BUILD FAILED Bench-$NEW (stop: report it)"; exit 1; }
for K in $MAIN $PRE; do build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }; done
for K in $NEW $PRE $MAIN; do build $K BuildDev Profile-$K || { echo "build Profile-$K failed"; exit 1; }; done
keepawake
log "q12 dialogs: $(dialogs)"
rm -f "$R/q12.csv"; mkdir -p "$R/shots-q12"; SH=$(cygpath -m "$R/shots-q12")
for RD in 1 2; do
  if [ $RD = 1 ]; then ORDER="$MAIN $PRE $NEW"; else ORDER="$NEW $PRE $MAIN"; fi
  for K in $ORDER; do
    S1=""; S2=""; S3=""
    [ $RD = 1 ] && { S1="-benchshots $SH/$K-route"; S2="-benchshots $SH/$K-goto"; S3="-benchshots $SH/$K-wharf"; }
    runcsv Bench-$K q12 $K-route 0  $RD -benchstops town,townwalk,cave,caveapproach,glide -fpscap 0  $S1
    runcsv Bench-$K q12 $K-goto 0  $RD -benchstops here -goto 530,958 -fpscap 0  $S2
    runcsv Bench-$K q12 $K-wharf 0  $RD -benchstops here -place prop:FishWharf -fpscap 0  $S3
  done
done
awk -F, 'NR > 1 { k = $1 " " $4; n[k]++; a[k] += $5; l[k] += $6; m[k] += $7; c[k] += $9; g[k] += $10; e[k] += $15; at[k] = at[k] " " $12 "," $13 }
  END { for (k in n) printf "  %-26s avg %5.1f  1%% low %5.1f  med %5.1f ms  cpu %5.1f  gpu %5.1f  exc %d  at%s\n", k, a[k]/n[k], l[k]/n[k], m[k]/n[k], c[k]/n[k], g[k]/n[k], e[k], at[k] }' "$R/q12.csv" | sort | tee "$R/q12-table.txt"
for K in $MAIN $PRE $NEW; do
  profile Profile-$K prof12-$K-townwalk townwalk -fpscap 0 
  profile Profile-$K prof12-$K-wharf here -fpscap 0  -place prop:FishWharf
done
for S in townwalk wharf; do
  echo "== $S $MAIN -> $NEW"; profdiff "$R/prof12-$MAIN-$S.txt" "$R/prof12-$NEW-$S.txt" | head -25
  echo "== $S $PRE -> $NEW"; profdiff "$R/prof12-$PRE-$S.txt" "$R/prof12-$NEW-$S.txt" | head -25
done
for f in "$R"/q-q12-*.log "$R"/q-prof12-*.log; do echo "$(basename $f) exc $(grep -c Exception $f) nan $(grep -c 'Mesh bounds' $f) $(grep -m1 -o 'world ready.*' $f | cut -c1-50)"; done
log "q12 done"
