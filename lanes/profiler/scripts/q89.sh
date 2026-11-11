#!/bin/bash
# Zach 11-11: the wave field's savings applied to all of R-9's rows, measured formally (jobs 45c, 45d, 45f again).
#   old:  water/deform 9b1cacc1 (rows 4-6 as pushed: cuts 8, 60 Hz, no sleep or culling, rings and foam on the field)
#   new:  Punchy b/water-rows-fixes 0318b5ee (= 9b1cacc1 + sleep, per-group culling, the 30 Hz step fix, fix 1, cuts 4 and
#         30 Hz by default, row 6's shore foam second read only on rivers, its flow-anchor hand-over band 2-5 m)
#   off:  new with -waterfield 0
#   row5: 2856a634 (rapids view only, row 6's reference as in job 45f)
# Profile builds, 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, -body default, a fresh game per run, 4 rounds
# with the order rotated. Shots on round 1.
#   swim:   job 45c's lake swim (-goto 488,-1144, swim east 40 m at 4 m/s). Pass: new <= +0.3 ms GPU over off.
#   fall:   job 45d's fall 74 view. Pass: new <= +0.3 ms GPU over off.
#   rapids: job 45f's rapids view. Pass: new <= +0.2 ms GPU over row5.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KO=9b1cacc; KN=0318b5e; K5=2856a63; Q=q89
OO=Profile-${KO}; ON=Profile-${KN}; O5=Profile-${K5}
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q water rows with the savings (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
for b in "$KO $OO" "$KN $ON" "$K5 $O5"; do set -- $b; build $1 BuildDev $2 || { echo "build $2 failed"; exit 1; }; done
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999"
AT="-goto -2167,3,3691 -benchstops here"
view() { case $1 in swim) echo "$SWIM";; fall) echo "$AT -shotcam -2150.7,30.1,3675.1,-2180.2,12.1,3648.1";;
  rapids) echo "$AT -shotcam -2169,13.1,3682,-2184,3.1,3656";; esac; }
arm() { case $1 in old) echo $OO;; new) echo $ON;; off) echo "$ON -waterfield 0";; row5) echo $O5;; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 -benchweather Clear -benchhour 12 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ')| $(grep -h 'WaterField:' "$f" | tail -1 | cut -c13-110) $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rot() { # round list...: the list rotated by the round
  local r=$1; shift; local a=("$@") n=$#; local i
  for ((i = 0; i < n; i++)); do echo -n "${a[$(((i + r - 1) % n))]} "; done
}
rm -f "$R"/$Q-*.csv
for v in swim fall rapids; do
  V="old new off"; [ $v = rapids ] && V="old new row5"
  for r in 1 2 3 4; do
    list=$(rot $r $V); [ $((r % 2)) = 0 ] && list=$(echo $list | tr ' ' '\n' | tac | tr '\n' ' ')
    for a in $list; do set -- $(arm $a); o=$1; shift; game $o $v-$a $r "$@" $(view $v); done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
