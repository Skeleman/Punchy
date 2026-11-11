#!/bin/bash
# Job 45e: R-10 after merging main. Read only. light/forwardplus 1f6c966d (merge of origin/main 60edfacb, default light
# budget) vs origin/main 60edfacb. Profile builds, 1920x1080, -gfx High, uncapped, -weatherseed 4, Clear, 22:00, a fresh
# game per run, 4 rounds ABBA. Job 45b's spot 2988.8,-920 facing north and facing the Ember Forest (yaw 201), measured by
# the bench's "spot" stop (20 s). Shots on round 1. Pass: branch <= +0.5 ms GPU over main at both views.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KL=1f6c966; KM=60edfac; Q=q88
OL=Profile-${KL}; OM=Profile-${KM}w
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 45e R-10 merge (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KL BuildDev $OL || { echo "build $OL failed"; exit 1; }
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
NIGHT="-benchweather Clear -benchhour 22 -benchstops spot"
view() { case $1 in north) echo "-benchspot 2988.8,-920,0";; ember) echo "-benchspot 2988.8,-920,201";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: spot: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rm -f "$R"/$Q-*.csv
for v in north ember; do
  for r in 1 2 3 4; do
    list="branch main"; [ $((r % 2)) = 0 ] && list="main branch"
    for a in $list; do o=$OL; [ $a = main ] && o=$OM; game $o $v-$a $r $NIGHT $(view $v); done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
