#!/bin/bash
# Job 47a: what tree LOD reach can the laptop afford? Read only. Main 33db029c plus the bench-only -lodscale flag
# (Punchy b/bench-lod 9bae987e: multiplies a flora class's reach, its mesh changes, floor and impostor hand-off; shadows
# as tuned). Profile build, 1920x1080, uncapped, -weatherseed 4, noon, Clear, -gfx High then -gfx Medium, a fresh game per
# run, round 1 in list order, round 2 reversed (ABBA). Arms: tree reach x0.5, x0.75, x1 (today), x1.5, x2.
# Spots (W-131's): the walk start (-1236,1324) facing east along the walk line; the hill on that line (-216,1324, ground
# ~110 m, the walk's highest point) facing east; and the 300 m flight along the line, 60 s at 40 m/s (-benchline, frames
# over 33 ms logged). Shots on round 1 at the hilltop. -profiler-enable so the draw counters are valid in the player.
# "q77.sh High|Medium"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=9bae987; O=Profile-${K}s; Q=q77   # the q78 build: same commit, runner b/bench-stress 34abe6ac (adds -benchnossao only)
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q 47a trees $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
WX="-benchweather Clear -benchhour 12 -fpscap 0 -profiler-enable"
spot() { case $1 in walk) echo "-benchstops spot -benchspot -1236,1324,90";; hill) echo "-benchstops spot -benchspot -216,1324,90";;
  fly) echo "-benchstops line -benchline -1236,1324,1164,1324,40,300 -hitchms 33";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
rev() { echo $* | tr ' ' '\n' | tac | tr '\n' ' '; }
game() { # name round args...
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && [[ $n == *hill* ]] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r $WX "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"
  check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms\|draw calls avg [0-9]*\|rendered tris [0-9]*k' | tr '\n' ' ')" \
       "over33 $(grep -c 'BenchRoute: hitch' "$f") $(grep -h ' s frame at ' "$f" | cut -c1-90 | tr '\n' ' ')"
}
GFX=${1:-"High Medium"}
for g in $GFX; do
  rm -f "$R"/$Q-$g-*.csv
  V="0.5 0.75 1 1.5 2"
  for s in walk hill fly; do
    for r in 1 2; do
      list=$V; [ $r = 2 ] && list=$(rev $V)
      for x in $list; do game $g-$s-x$x $r -gfx $g -lodscale tree=$x $(spot $s); done
    done
  done
  for f in "$R"/$Q-$g-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
done
echo $Q done
