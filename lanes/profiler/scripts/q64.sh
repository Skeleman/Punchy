#!/bin/bash
# Job 34: the cloud march's empty-air strides (sky/cost 79ffa7c9 against main 4669aabc).
# Job 32's setup: Profile builds, the Summit place facing out, pitched up 10 (-benchview place,-10), 1920x1080,
# -gfx High, -weatherseed 4, uncapped, noon, a fresh game per run, weather forced over the stop by the runner.
#   a:      on the branch build, -skyoff 16 (old even steps) against -skyoff 0 (new strides), ABBA, in plain, Clear,
#           Overcast and Storm; -benchnosky once (the baseline); main plain twice (same as old steps?).
#   parts:  plain weather, branch build: -skyoff 1 (no march), 2 (no far clouds), 4 (no sky colour), 8 (no weather
#           volumes), order 1 2 4 8 8 4 2 1.
#   sunset: -benchhour 17.6 (sun about 6 deg up; it sets at 18): old against new ABBA with shots, then a shot each
#           facing west (yaw 270, pitch -5), and a -skyoff 32 (steps per pixel) shot of each at noon and sunset.
# "q64.sh a|parts|sunset"; no argument runs all three.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KM=4669aab; OM=Profile-${KM}m4; KN=79ffa7c; ON=Profile-${KN}m4; Q=q64
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q sky strides $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
build $KN BuildDev $ON || { echo "build $ON failed"; exit 1; }
keepawake
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
SUMMIT="-benchstops place -benchplace Summit"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
go() { # set variant build commit extra...: round 1 or 2 by whether round 1's log exists; shots on round 1
  local set=$1 v=$2 out=$3 c=$4; shift 4
  local r=1; [ -f "$L/q-$Q-$set-$v-cs100-1.log" ] && r=2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$set-$v"
  RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv $out $Q-$set-$v $c s100 $r $COMMON $SUMMIT -benchview ${VIEW:-place,-10} "$@" $shot
  check "$R/q-$Q-$set-$v-cs100-$r.log"
  grep -h ' s frame at ' "$R/q-$Q-$set-$v-cs100-$r.log" | head -3
}
wx() { case $1 in plain) ;; *) echo -benchweather $1 ;; esac; }
tables() { for f in "$R"/$Q-$1-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part a; then
  rm -f "$R"/$Q-a-*.csv "$L"/q-$Q-a-*.log
  for w in plain Clear Overcast Storm; do
    go a $w-old $ON $KN $(wx $w) -skyoff 16
    go a $w-new $ON $KN $(wx $w) -skyoff 0
    go a $w-new $ON $KN $(wx $w) -skyoff 0
    go a $w-old $ON $KN $(wx $w) -skyoff 16
  done
  go a nosky $ON $KN -benchnosky
  go a plain-main $OM $KM
  go a plain-main $OM $KM
  tables a
  grep -h 'weather .* forced\|BenchRoute: place: avg' "$L"/q-$Q-a-*-1.log | sed 's/ fps.*, at / ... at /' | cut -c1-200
fi
if part parts; then
  rm -f "$R"/$Q-parts-*.csv "$L"/q-$Q-parts-*.log
  for n in 1 2 4 8 8 4 2 1; do go parts off$n $ON $KN -skyoff $n; done
  tables parts
fi
if part sunset; then
  rm -f "$R"/$Q-sun-*.csv "$L"/q-$Q-sun-*.log
  for v in old new new old; do
    [ $v = old ] && s=16 || s=0
    go sun $v $ON $KN -benchhour 17.6 -skyoff $s
  done
  VIEW=270,-5 go sun west-old $ON $KN -benchhour 17.6 -skyoff 16
  VIEW=270,-5 go sun west-new $ON $KN -benchhour 17.6 -skyoff 0
  VIEW=270,-5 go sun west-main $OM $KM -benchhour 17.6
  go sun steps-noon $ON $KN -skyoff 32
  go sun steps-noon-old $ON $KN -skyoff 48
  go sun steps-sunset $ON $KN -benchhour 17.6 -skyoff 32
  tables sun
fi
echo $Q done
