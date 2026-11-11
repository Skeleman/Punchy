#!/bin/bash
# Zach 11-11: the wave field's cheaper forms. One Profile build of Punchy b/water-rows-fixes d2c748a6 (job 45g's
# branch + -waterfieldnormals and the -waterfieldtess prototype). Job 45c's lake swim (-goto 488,-1144, east 40 m at
# 4 m/s), 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, -body default, 4 rounds rotated. Shots on round 1
# (to compare the wake's look). Arms:
#   cuts4 (the default: CPU cut 4), cuts2 (-waterfieldcuts 2), tess (-waterfieldtess 1: CPU cut 2, the GPU divides up
#   to 4x more by screen size), normals (-waterfieldnormals 1: no sheet, the field only shades the chunk water), off.
# Supervisor 11-11 (rule 23, R-9 row 7): the same arms again at -gfx Medium; both presets reported (Medium decides).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=d2c748a; O=Profile-${K}; Q=q94
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q wave field forms (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
grep -h 'WaterTess\|tessellation' "$R/build-$O.log" | grep -i 'error' | head -10
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999 -benchweather Clear -benchhour 12"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
arm() { case $1 in cuts4) ;; cuts2) echo "-waterfieldcuts 2";; tess) echo "-waterfieldtess 1";; normals) echo "-waterfieldnormals 1";; off) echo "-waterfield 0";; esac; }
game() { # gfx name round args...
  local g=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx $g -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms\|1% low [0-9.]*' | tr '\n' ' ')| $(grep -h 'WaterField:' "$f" | tail -1 | cut -c13-90) $(grep -h 'WaterField: tessellation' "$f" | head -1) $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rot() { local r=$1; shift; local a=("$@") n=$#; local i; for ((i = 0; i < n; i++)); do echo -n "${a[$(((i + r - 1) % n))]} "; done; }
rm -f "$R"/$Q-*.csv
V="cuts4 cuts2 tess normals off"
for g in High Medium; do
  for r in 1 2 3 4; do
    for a in $(rot $r $V); do game $g ${g,,}-$a $r $(arm $a) $SWIM; done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
