#!/bin/bash
# Job 48: A-99 creature extras by screen size (merged as #250). Read only. origin/main be98cde1 (before) vs 739cdff7 (after).
# Profile builds, 1920x1080, -gfx High, uncapped, -weatherseed 4, Clear, a fresh game per run, 4 rounds alternating
# (ABBA x2). Measured by the bench's "here" stop (20 s, after its 10 s settle) where the game's flags put the player:
#   hive:   -goto -1500,-995 (noon)
#   night:  -place HumanVillage, 23:00 (torches and lanterns)
#   tundra: -place Tundra (noon; traces and breath)
#   roost:  -goto -1149,1219 (noon; the bat roost)
# Shots on round 1. Pass: frame ms within noise of the before arm at every spot.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=be98cde; KB=739cdff; Q=q85
OA=Profile-${KA}; OB=Profile-${KB}
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 48 creature extras (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KA BuildDev $OA || { echo "build $OA failed"; exit 1; }
build $KB BuildDev $OB || { echo "build $OB failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
spot() { case $1 in hive) echo "-goto -1500,-995 -benchhour 12";; night) echo "-place HumanVillage -benchhour 23";;
  tundra) echo "-place Tundra -benchhour 12";; roost) echo "-goto -1149,1219 -benchhour 12";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 -benchweather Clear -benchstops here "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: here: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms\| at -\?[0-9]*,-\?[0-9]*' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rm -f "$R"/$Q-*.csv
for s in hive night tundra roost; do
  for r in 1 2 3 4; do
    list="before after"; [ $((r % 2)) = 0 ] && list="after before"
    for a in $list; do o=$OA; [ $a = after ] && o=$OB; game $o $s-$a $r $(spot $s); done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
