#!/bin/bash
# b/fix-stall-hang after: the q35 sweep on the same game commit (b19e37d) with the runner capturing frame timings once per frame (was up to 3x). Count ~0.5 s hitches and hangs vs q35.
# shadows; cached far shadows not in yet) on main a38f9ca. Bench build, uncapped, v2. BenchTune's in-session sweep
# (-benchtunesweep): every set in one session, so weather, light and camera match between shots; 4 s settle + 8 s
# measured each. Fishing village 1 and the forest stop, 2 rounds each (round 2 in reverse order, for drift).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=b19e37d
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q37 shadow options sweep (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
rm -rf "$P/Builds/Bench-${K}f"
build $K Build Bench-${K}f || { echo "build failed"; exit 1; }
keepawake
L="$R/q37logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
SETS=("default:" "o1-50-c2:shadows=0.5,cascades=2" "nofloras:flora=0" "cheap:cheap=1" "still:still=1" "cached:cached=1"
      "cheapstill:cheap=1,still=1" "cheapcached:cheap=1,cached=1" "all:cheap=1,still=1,cached=1" "all-n15:cheap=1,still=1,cached=1,near=15")
for SPOT in fv1 forest; do
  case $SPOT in fv1) A="-island v2 -goto -3304,-1644 -benchstops here -benchlong here 200"; ST=here;; *) A="-island v2 -benchstops forest -benchlong forest 200"; ST=forest;; esac
  for RD in 1 2; do
    if [ $RD = 1 ]; then SW=$(IFS=';'; echo "${SETS[*]}"); else SW=$(for ((j=${#SETS[@]}-1; j>=0; j--)); do printf '%s;' "${SETS[$j]}"; done); SW=${SW%;}; fi
    T=q37-$SPOT-$RD; SD="$R/q37shots/$SPOT-$RD"; mkdir -p "$SD"
    run Bench-${K}f $T $A -fpscap 0 -benchtune panel=0 -benchtunesweep "$SW" -benchshots "$(cygpath -m $SD)"
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "== $T: exc $(grep -c Exception $L/$T.log)"; grep -h "BenchTune: sweep" $L/$T.log | cut -c1-260
  done
done
for f in $L/*.log; do echo "$(basename $f): 0.5 s hitches $(grep -c "BenchRoute: hitch 5[0-2][0-9] ms" $f), done $(grep -c "BenchRoute: done" $f)"; done
log "q37 done"
