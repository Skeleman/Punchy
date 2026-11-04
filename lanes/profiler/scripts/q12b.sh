#!/bin/bash
# Queue item 12, part b: the cave spot again on the cave floor (-goto x,y,z; "-goto 530,958" lands on the surface above
# batfolk cave 26, chamber 525,45,958), uncapped, 2 rounds flipped; profiles there; shots of each spot into folders that
# exist (CaptureScreenshot silently writes nothing into a missing folder).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
MAIN=27bf8c1; PRE=b28ee7b; NEW=52b4af5
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q12b cave floor spot (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
log "q12b dialogs: $(dialogs)"
rm -f "$R/q12b.csv"
for RD in 1 2; do
  if [ $RD = 1 ]; then ORDER="$MAIN $PRE $NEW"; else ORDER="$NEW $PRE $MAIN"; fi
  for K in $ORDER; do
    SD="$R/shots-q12/$K-cave"; mkdir -p "$SD"; SX=""; [ $RD = 1 ] && SX="-benchshots $(cygpath -m "$SD")"
    runcsv Bench-$K q12b $K-cavefloor 0 $RD -benchstops here -goto 530,45,958 -fpscap 0 $SX
    cp "$R/q-q12b-c0-$RD.log" "$R/q12b-$K-$RD.log"
  done
done
# Shots of the wharf and the town stop on the new build (looks only).
SD="$R/shots-q12/$NEW-wharf"; mkdir -p "$SD"
run Bench-$NEW q12b-shotwharf -benchstops here -place prop:FishWharf -fpscap 0 -benchshots "$(cygpath -m "$SD")"
SD="$R/shots-q12/$NEW-route"; mkdir -p "$SD"
run Bench-$NEW q12b-shottown -benchstops town -fpscap 0 -benchshots "$(cygpath -m "$SD")"
awk -F, 'NR > 1 { k = $1 " " $4; n[k]++; a[k] += $5; l[k] += $6; m[k] += $7; c[k] += $9; g[k] += $10; e[k] += $15 }
  END { for (k in n) printf "  %-26s avg %5.1f  1%% low %5.1f  med %5.1f ms  cpu %5.1f  gpu %5.1f  exc %d\n", k, a[k]/n[k], l[k]/n[k], m[k]/n[k], c[k]/n[k], g[k]/n[k], e[k] }' "$R/q12b.csv" | sort | tee "$R/q12b-table.txt"
grep -h "BenchRoute: here" "$R"/q12b-*-1.log | grep -o "surface hidden [0-9.]*, cave chunks [0-9]*\|at -\?[0-9]*,-\?[0-9]*" | paste - -
for K in $MAIN $PRE $NEW; do profile Profile-$K prof12-$K-cavefloor here -fpscap 0 -goto 530,45,958; done
echo "== cavefloor $MAIN -> $NEW"; profdiff "$R/prof12-$MAIN-cavefloor.txt" "$R/prof12-$NEW-cavefloor.txt" | head -25
echo "== cavefloor $PRE -> $NEW"; profdiff "$R/prof12-$PRE-cavefloor.txt" "$R/prof12-$NEW-cavefloor.txt" | head -25
find "$R/shots-q12" -type f
log "q12b done"
