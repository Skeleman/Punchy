#!/bin/bash
# Job 33 part 2 (Zach, 11-05): shadow options 1 and 2 against W-107's far-tree switches (cheap far caster, still far
# shadows; cached far shadows not in yet) on main a38f9ca. Bench build, uncapped, v2. BenchTune's in-session sweep
# (-benchtunesweep): every set in one session, so weather, light and camera match between shots; 4 s settle + 8 s
# measured each. Fishing village 1 and the forest stop, 2 rounds each (round 2 in reverse order, for drift).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=a38f9ca
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q34 shadow options sweep (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
rm -rf "$P/Builds/Bench-${K}t"
build $K Build Bench-${K}t || { echo "build failed"; exit 1; }
keepawake
L="$R/q34logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
SETS=("default:" "high:shadows=1.5,cascades=4" "o1-75-c3:shadows=0.75,cascades=3" "o1-50-c2:shadows=0.5,cascades=2"
      "o2-t30:trees=30,shrubs=20" "nofloras:flora=0" "cheap:cheap=1" "still:still=1" "cheapstill:cheap=1,still=1"
      "cheapstill-n15:cheap=1,still=1,near=15" "cheapstill-o1:cheap=1,still=1,shadows=0.75,cascades=3")
for SPOT in fv1 forest; do
  case $SPOT in fv1) A="-island v2 -goto -3304,-1644 -benchstops here -benchlong here 200"; ST=here;; *) A="-island v2 -benchstops forest -benchlong forest 200"; ST=forest;; esac
  for RD in 1 2; do
    if [ $RD = 1 ]; then SW=$(IFS=';'; echo "${SETS[*]}"); else SW=$(for ((j=${#SETS[@]}-1; j>=0; j--)); do printf '%s;' "${SETS[$j]}"; done); SW=${SW%;}; fi
    T=q34-$SPOT-$RD; SD="$R/q34shots/$SPOT-$RD"; mkdir -p "$SD"
    run Bench-${K}t $T $A -fpscap 0 -benchtune panel=0 -benchtunesweep "$SW" -benchshots "$(cygpath -m $SD)"
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "== $T: exc $(grep -c Exception $L/$T.log)"; grep -h "BenchTune: sweep" $L/$T.log | cut -c1-260
  done
done
log "q34 done"
