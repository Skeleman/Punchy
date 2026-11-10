#!/bin/bash
# Zach 11-10 (in the profiler session): SSAO on vs off, in a cave and on the surface: a shot pair and the cost. Read only.
# Main 33db029c (+ b/bench-lod 9bae987e, which only adds -lodscale), Profile build with the runner's -benchnossao (Punchy
# b/bench-stress 34abe6ac). 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, a fresh game per run, ABBA x2
# (4 rounds). Stops: the bench's cave stop (the biggest cave, the camera inside) and the town stop. Shots on round 1.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=9bae987; O=Profile-${K}s; Q=q78
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q SSAO on/off (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # name round args...
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx High -fpscap 0 -benchweather Clear -benchhour 12 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: off SSAO' "$f" | cut -c1-120) | $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-80)"
}
rm -f "$R"/$Q-*.csv
for st in cave town; do
  for r in 1 2 3 4; do
    list="on off"; [ $((r % 2)) = 0 ] && list="off on"
    for a in $list; do
      x=; [ $a = off ] && x=-benchnossao
      game $st-$a $r -benchstops $st $x
    done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
