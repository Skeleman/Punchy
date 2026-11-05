#!/bin/bash
# Job 33 (Zach, 11-05): shots and metrics for shadow option 1 (sun shadow distance / cascades) and option 2 (flora
# shadow cast distance, W-102) on main b15876e, through the runner's BenchTune (-benchtune k=v,...). Bench build,
# uncapped, v2: fishing village 1 (-goto) and the route's forest stop; herewarm + here 20 s; a screenshot per stop.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=b15876e
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q33 shadow options shots (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
rm -rf "$P/Builds/Bench-${K}t"
build $K Build Bench-${K}t || { echo "build failed"; exit 1; }
keepawake
L="$R/q33logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
VARS=("default|panel=0"
      "o1-75-c3|shadows=0.75,cascades=3,panel=0"
      "o1-50-c2|shadows=0.5,cascades=2,panel=0"
      "o1-50-c4|shadows=0.5,cascades=4,panel=0"
      "o2-full|trees=1000,shrubs=1000,panel=0"
      "o2-t120|trees=120,shrubs=60,panel=0"
      "o2-t30|trees=30,shrubs=20,panel=0"
      "nofloras|flora=0,panel=0")
for SPOT in fv1 forest; do
  case $SPOT in fv1) A="-island v2 -goto -3304,-1644 -benchstops herewarm,here"; ST=here;; *) A="-island v2 -benchstops forest"; ST=forest;; esac
  for V in "${VARS[@]}"; do
    N=${V%%|*}; X=${V#*|}; T=q33-$SPOT-$N
    SD="$R/q33shots/$SPOT-$N"; mkdir -p "$SD"
    run Bench-${K}t $T $A -fpscap 0 -benchtune "$X" -benchshots "$(cygpath -m $SD)"
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "$SPOT $N | $(grep -h "BenchRoute: $ST: avg" $L/$T.log | sed 's/BenchRoute: [a-z]*: //; s/, 1% low [^,]*//; s/, median.*gpu/, gpu/; s/, frames.*main/, main/; s/, surface.*//') | $(grep -h "BenchTune: shadows" $L/$T.log | tail -1 | cut -c1-140)"
  done
done
ls -R "$R/q33shots" | head -40
log "q33 done"
