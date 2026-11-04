#!/bin/bash
# Queue item 11: slow caves on the laptop, main 762c16a. (1) cave + caveapproach at cap 60, v2 and -island v1, ABBA vs
# v0.11.0 (4eea658, v1 only); (2) one dev profile per island at the cave stop with GPU passes; (3) a -perflog pass in the
# biggest cave per island: normal, without walls drawn, and with the old (managed) camera probe and no room cull.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=762c16a
OLD=4eea658
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q11 slow caves (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }
build $K BuildDev Profile-$K || { echo "build Profile-$K failed"; exit 1; }
keepawake
log "q11 dialogs: $(dialogs)"
rm -f "$R/q11.csv"; mkdir -p "$R/shots-q11"; SH=$(cygpath -m "$R/shots-q11")
STOPS="-benchstops cave,caveapproach -fpscap 60"
for RD in 1 2; do
  if [ $RD = 1 ]; then ORDER="v2 v1 old"; else ORDER="old v1 v2"; fi
  for S in $ORDER; do
    SHOTS=""; [ $RD = 1 ] && SHOTS="-benchshots $SH/$S"
    case $S in
      v2) runcsv Bench-$K q11 $K-v2 60 $RD $STOPS $SHOTS ;;
      v1) runcsv Bench-$K q11 $K-v1 60 $RD $STOPS -island v1 $SHOTS ;;
      old) runcsv Bench-$OLD q11 $OLD 60 $RD $STOPS $SHOTS ;;
    esac
  done
done
awk -F, 'NR > 1 { k = $1 " " $4; n[k]++; a[k] += $5; l[k] += $6; c[k] += $9; g[k] += $10 }
  END { for (k in n) printf "  %-26s avg %5.1f  1%% low %5.1f  cpu %5.1f  gpu %5.1f\n", k, a[k]/n[k], l[k]/n[k], c[k]/n[k], g[k]/n[k] }' "$R/q11.csv" | sort | tee "$R/q11-table.txt"
# Profiles, one per island, at the cave stop (cap 60 as the bench; GPU passes on).
profile Profile-$K prof-$K-cave-v2 cave -fpscap 60 -benchgpupasses
profile Profile-$K prof-$K-cave-v1 cave -fpscap 60 -benchgpupasses -island v1
echo "== cave v1 -> v2"; profdiff "$R/prof-$K-cave-v1.txt" "$R/prof-$K-cave-v2.txt" | head -30
# -perflog in the biggest cave (uncapped, to price things): normal, no walls, old probe + no room cull.
fixed Bench-$K || exit 1
for ISL in v2 v1; do
  EX=""; [ $ISL = v1 ] && EX="-island v1"
  OUTD="$R/q11-perf-$ISL"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  { echo "-gocave #1 -wait 25 -perflog -shot big"
    echo "-wait 8 -perflog -nocavewalls -shot nowalls"
    echo "-wait 8 -perflog -oldcave -shot old"
    echo "-wait 8 -perflog -shot again"; } > "$OUTD/list.txt"
  cool
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) \
      -shotdir "$WO" -shotlist "$WO/list.txt" -quit $EX >/dev/null 2>&1 )
  strays >/dev/null
  log "q11 perf $ISL: exc $(grep -c Exception $OUTD/game.log)"
  grep -h "perf\b\|^perf\|\[perf\]" "$OUTD/game.log" | cut -c1-400
done
for f in "$R"/q-q11-*.log; do echo "$(basename $f) exc $(grep -c Exception $f) nan $(grep -c 'Mesh bounds' $f) $(grep -m1 -o 'world ready.*' $f | cut -c1-50)"; done
log "q11 done"
