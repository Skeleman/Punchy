#!/bin/bash
# Queue item 11, expanded (4cf8925): caves should beat open ground, and what surface work still runs underground.
# (1) dev profile at caveapproach (open ground) per island, to set against q11's cave profiles; (2) -perflog in the big
# v2 cave with and without the surface extras that have launch flags (ambient life, mantraps, colossus, sand puffs),
# with -renderpasslog for CreatureRenderSystem's passes.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=762c16a
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q11b underground leftovers (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
log "q11b dialogs: $(dialogs)"
profile Profile-$K prof-$K-approach-v2 caveapproach -fpscap 60 -benchgpupasses
profile Profile-$K prof-$K-approach-v1 caveapproach -fpscap 60 -benchgpupasses -island v1
# v0.11.0 at the same cave stop (smaller caves, old collision and camera), to see what grew.
profile Profile-4eea658 prof-4eea658-cave cave -fpscap 60 -benchgpupasses
echo "== v0.11.0 cave -> main v1 cave"; profdiff "$R/prof-4eea658-cave.txt" "$R/prof-$K-cave-v1.txt" | head -40
echo "== v2 caveapproach -> cave"; profdiff "$R/prof-$K-approach-v2.txt" "$R/prof-$K-cave-v2.txt" | head -40
echo "== v1 caveapproach -> cave"; profdiff "$R/prof-$K-approach-v1.txt" "$R/prof-$K-cave-v1.txt" | head -40
fixed Bench-$K || exit 1
for V in plain extrasoff nopushout v1; do
  EX=""; LX=""
  [ $V = extrasoff ] && EX="-noambientlife -nomantrap -nocolossus -nosandpuffs"
  [ $V = nopushout ] && { EX="-nopushout"; LX="-nopushout"; }
  [ $V = v1 ] && EX="-island v1"
  OUTD="$R/q11b-perf-$V"; rm -rf "$OUTD"; mkdir -p "$OUTD"; WO=$(cygpath -m "$OUTD")
  { echo "-gocave #1 -wait 25 -perflog $LX -shot big"
    echo "-wait 8 -perflog $LX -shot again"
    echo "-wait 8 -perflog -nocavewalls $LX -shot nowalls"; } > "$OUTD/list.txt"
  cool
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$WO/game.log" -weatherseed 4 -time 12 -fpscap 0 $(screen Bench 1280 720) \
      -shotdir "$WO" -shotlist "$WO/list.txt" -quit -renderpasslog $EX >/dev/null 2>&1 )
  strays >/dev/null
  log "q11b perf $V: exc $(grep -c Exception $OUTD/game.log)"
  grep -h "perf" "$OUTD/game.log" | grep -v "^\s*$" | cut -c1-400 | tail -8
done
log "q11b done"
