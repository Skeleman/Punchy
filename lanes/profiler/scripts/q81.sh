#!/bin/bash
# Zach 11-10 (in the profiler session): what the wave field's savings buy. Punchy b/fix-waterfield-step d7f0f975 = water/deform
# 24459300 (R-9 row 4, job 45c's build) + idle sleep + per-group source culling, and launch flags for the finest cut and the
# step rate. One Profile build; every arm is a launch flag. 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, a
# fresh game per run, round 1 in list order, round 2 reversed.
#   swim (job 45c's: -goto 488,-1144, swim east 40 m at 4 m/s; one swimmer, the field never idle, so sleep can't act):
#     base (all four off: 45c's behaviour), each of cull / cuts4 / hz30 alone, the three pairs, all three together; and
#     nostep (-waterfieldstep 0: the sheet drawn flat, no compute) and off (-waterfield 0) as references.
#   idle (job 43's lake shore, -benchspot -1624,-488,270: water in view, nothing in it):
#     base, sleep alone, cull alone, sleep + cull, off.
# The game logs every 5 s how many steps were dispatched or skipped and how many sources pressed.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=d7f0f97; O=Profile-${K}; Q=q81
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q wave field savings (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999"
IDLE="-benchstops spot -benchspot -1624,-488,270"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
# An arm is a "+"-joined list of the changes it has on; the rest are off (base has none).
arm() {
  case $1 in nostep) echo "-waterfieldsleep 0 -waterfieldstep 0"; return;; off) echo "-waterfield 0"; return;; esac
  local f="-waterfieldsleep 0 -waterfieldcull 0"
  [[ +$1+ == *+sleep+* ]] && f=${f/-waterfieldsleep 0/}
  [[ +$1+ == *+cull+* ]] && f=${f/-waterfieldcull 0/}
  [[ +$1+ == *+cuts4+* ]] && f="$f -waterfieldcuts 4"
  [[ +$1+ == *+hz30+* ]] && f="$f -waterfieldhz 30"
  echo $f
}
rev() { echo $* | tr ' ' '\n' | tac | tr '\n' ' '; }
game() { # name round args...
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx High -fpscap 0 -benchweather Clear -benchhour 12 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ')| $(grep -h 'WaterField:' "$f" | tail -1 | cut -c1-200) $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rm -f "$R"/$Q-*.csv
for r in 1 2; do
  V="base cull cuts4 hz30 cull+cuts4 cull+hz30 cuts4+hz30 cull+cuts4+hz30 nostep off"; [ $r = 2 ] && V=$(rev $V)
  for a in $V; do game swim-$a $r $(arm $a) $SWIM; done
done
for r in 1 2; do
  V="base sleep cull sleep+cull off"; [ $r = 2 ] && V=$(rev $V)
  for a in $V; do game idle-$a $r $(arm $a) $IDLE; done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
