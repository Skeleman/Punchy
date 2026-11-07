#!/bin/bash
# Refactor roadmap 1.2: stress stops on main, CPU-bound or GPU-bound at 1080p and 1440p, uncapped.
# bigtown = most buildings on the island; forest = OakGrove; firesbase/fires = same spot without and with 500 flames;
# lake = looking over the water. Bench build for frame and main-thread numbers (2 rounds, order reversed);
# Profile build for GPU numbers, because the Bench GPU timer under-reads.
# "q51.sh dry" runs one 1080p Bench session with shots, to check every stop shows what it should.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=4fea789
STOPS=bigtown,forest,spot,firesbase,fires,lake
SPOT="-benchspot 1740,3355,180"   # spot = under the OakGrove canopy (forest only looks at the grove from a meadow)
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q51 stress stops (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K}s || { echo "bench build failed"; exit 1; }
keepawake
if [ "$1" = dry ]; then
  mkdir -p "$R/q51-dry"
  BENCHW=1920 BENCHH=1080 run Bench-${K}s q51-dry -benchstops $STOPS $SPOT -fpscap 0 -benchshots "$W/q51-dry"
  grep -E 'BenchRoute stress|BenchRoute: [a-z]+: avg|Exception' "$R/q-q51-dry.log" | cut -c1-260
  ls "$R"/q51-dry* 2>/dev/null | head; exit 0
fi
build $K BuildDev Profile-${K}s || { echo "profile build failed"; exit 1; }
rm -f "$R/q51-bench.csv" "$R/q51-prof.csv"
go() { # outdir csv w h round
  BENCHW=$3 BENCHH=$4 runcsv $1 $2 $K$3 0 $5 -benchstops $STOPS $SPOT -fpscap 0
  crash=$(grep -c 'Crash!!!\|Received signal' "$R/q-$2-c0-$5.log"); [ "$crash" != 0 ] && { echo "CRASH in q-$2-c0-$5.log"; exit 1; }
}
for rd in 1 2; do
  if [ $rd = 1 ]; then A="1920 1080"; C="2560 1440"; else A="2560 1440"; C="1920 1080"; fi
  go Bench-${K}s q51-bench$(echo $A | cut -d' ' -f1) $A $rd; go Bench-${K}s q51-bench$(echo $C | cut -d' ' -f1) $C $rd
  go Profile-${K}s q51-prof$(echo $A | cut -d' ' -f1) $A $rd; go Profile-${K}s q51-prof$(echo $C | cut -d' ' -f1) $C $rd
done
for f in bench1920 bench2560 prof1920 prof2560; do echo "== $f"; table "$R/q51-$f.csv"; done
grep -h 'BenchRoute stress' "$R"/q-q51-bench1920-c0-1.log | cut -c1-200
