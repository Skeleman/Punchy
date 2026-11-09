#!/bin/bash
# Job 37: R-2 on the laptop (main acd0cad5, #218) against 13cdbcdc (before R-2). Profile builds, 1920x1080, -gfx High,
# -weatherseed 4, uncapped, a fresh game per run, ABBA.
#   fires: job 27's fire test: firesbase (job 25's start spot, noon) and fires (the same with -benchfires 500 flames).
#   ember: the Ember Forest burning at night (job 27: -benchview 120,5 -benchhour 22), acd0cad5 default vs -worldglow 0.
#   bake:  the world glow bake time ("SurfaceGlow: world glow baked in N ms") from every acd0cad5 game.
# "q67.sh fires|ember"; no argument runs both, then the bake times.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KN=acd0cad; ON=Profile-${KN}m4; KO=13cdbcd; OO=Profile-${KO}m4; Q=q67
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-2 $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KO BuildDev $OO || { echo "build $OO failed"; exit 1; }
build $KN BuildDev $ON || { echo "build $ON failed"; exit 1; }
keepawake
pgrep -f freezesample.sh >/dev/null 2>&1 || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
run1() { # set variant build commit args...: round 1 or 2 by whether round 1's log exists; shots on round 1
  local set=$1 v=$2 out=$3 c=$4; shift 4
  local r=1; [ -f "$L/q-$Q-$set-$v-cs100-1.log" ] && r=2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$set-$v"
  RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv $out $Q-$set-$v $c s100 $r $COMMON "$@" $shot
  check "$R/q-$Q-$set-$v-cs100-$r.log"
  grep -h ' s frame at \|BenchRoute flames' "$R/q-$Q-$set-$v-cs100-$r.log" | sort -u | head -4 | cut -c1-200
}
tables() { for f in "$R"/$Q-$1-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part fires; then
  rm -f "$R"/$Q-fires-*.csv "$L"/q-$Q-fires-*.log
  F="-benchstops firesbase,fires -benchfires 500"
  run1 fires before $OO $KO $F; run1 fires r2 $ON $KN $F; run1 fires r2 $ON $KN $F; run1 fires before $OO $KO $F
  tables fires
fi
if part ember; then
  rm -f "$R"/$Q-ember-*.csv "$L"/q-$Q-ember-*.log
  E="-benchstops place -benchplace EmberForest: -benchview 120,5 -benchhour 22"
  run1 ember glow $ON $KN $E; run1 ember noglow $ON $KN $E -worldglow 0
  run1 ember noglow $ON $KN $E -worldglow 0; run1 ember glow $ON $KN $E
  tables ember
fi
echo "== world glow bake (acd0cad5 games)"
grep -h 'world glow baked in' "$L"/q-$Q-*-r2-*.log "$L"/q-$Q-ember-*.log 2>/dev/null | cut -c1-200
echo $Q done
