#!/bin/bash
# Refactor roadmap 1.2, pixels: the laptop's logical desktop is 1920x1080, so a 2560x1440 window silently stays at 1080p
# (q51's "2560" rows are extra 1080p rounds). 1440p's pixel count here = 1080p at render scale 1.333 (post and UI excepted).
# Profile build (GPU timer), scale 1, 1.333, 1.333, 1. Ends with a shot size check.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=4fea789
STOPS=bigtown,forest,spot,firesbase,fires,lake
SPOT="-benchspot 1740,3355,180"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q51b render scale (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
rm -f "$R/q51b-s100.csv" "$R/q51b-s133.csv"
BENCHW=1920 BENCHH=1080 runcsv Profile-${K}s q51b-s100 ${K}s100 0 1 -benchstops $STOPS $SPOT -fpscap 0
BENCHW=1920 BENCHH=1080 runcsv Profile-${K}s q51b-s133 ${K}s133 0 1 -benchstops $STOPS $SPOT -fpscap 0 -benchrenderscale 1.333
BENCHW=1920 BENCHH=1080 runcsv Profile-${K}s q51b-s133 ${K}s133 0 2 -benchstops $STOPS $SPOT -fpscap 0 -benchrenderscale 1.333
BENCHW=1920 BENCHH=1080 runcsv Profile-${K}s q51b-s100 ${K}s100 0 2 -benchstops $STOPS $SPOT -fpscap 0
for f in s100 s133; do echo "== $f"; cut -d, -f3,4,5,6,9,10,11,15 "$R/q51b-$f.csv"; done
grep -ciE 'crash!!!|received signal' "$R"/q-q51b-*.log
