#!/bin/bash
# Job 36: clouds on the low-resolution buffer (sky/lowres ccd74cc1 against main 13cdbcdc). The branch also carries
# sky/cost's strides and fewer far sun samples (main has neither), so main vs -skyfull prices those, and -skyfull vs
# half / quarter prices the buffer. Job 34's setup: Profile builds, the Summit facing out, pitched up 10, 1920x1080,
# -gfx High, -weatherseed 4, uncapped, noon, a fresh game per run, weather forced over the stop.
#   size:    plain: main, -skyfull, -lowres half, -lowres quarter, default (High's own size), ABBA.
#   weather: Clear, Overcast, Storm: main against default, ABBA.
#   look:    shots, no timing worth quoting (x1): a tree line against the sky (the dense forest spot, looking up 15)
#            and the horizon (the Summit, level), each main / full / half / quarter; a Storm view the same four.
# "q66.sh size|weather|look"; no argument runs all three. A frame-stall sampler (freezesample.sh) runs alongside.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KM=13cdbcd; OM=Profile-${KM}m4; KN=ccd74cc; ON=Profile-${KN}m4; Q=q66
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q sky low-res $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
build $KN BuildDev $ON || { echo "build $ON failed"; exit 1; }
keepawake
pgrep -f freezesample.sh >/dev/null 2>&1 || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
# variant -> build and flags
vb() { case $1 in main) echo "$OM $KM" ;; *) echo "$ON $KN" ;; esac; }
vf() { case $1 in full) echo -skyfull ;; half) echo -lowres half ;; quarter) echo -lowres quarter ;; esac; }
run1() { # set variant args...: round 1 or 2 by whether round 1's log exists; shots on round 1
  local set=$1 v=$2; shift 2
  local r=1; [ -f "$L/q-$Q-$set-$v-cs100-1.log" ] && r=2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$set-$v"
  local b=$(vb $v); set -- $COMMON "$@" $(vf $v) $shot
  RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv ${b% *} $Q-$set-$v ${b#* } s100 $r "$@"
  check "$R/q-$Q-$set-$v-cs100-$r.log"
  grep -h ' s frame at \|LowRes\|lowres' "$R/q-$Q-$set-$v-cs100-$r.log" | sort -u | head -4 | cut -c1-200
}
SUMMIT="-benchstops place -benchplace Summit"
tables() { for f in "$R"/$Q-$1-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part size; then
  rm -f "$R"/$Q-size-*.csv "$L"/q-$Q-size-*.log
  for v in main full half quarter default default quarter half full main; do run1 size $v $SUMMIT -benchview place,-10; done
  tables size
fi
if part weather; then
  rm -f "$R"/$Q-wx-*.csv "$L"/q-$Q-wx-*.log
  for w in Clear Overcast Storm; do
    for v in main default default main; do run1 wx-$w $v $SUMMIT -benchview place,-10 -benchweather $w; done
  done
  tables wx
fi
if part look; then
  rm -f "$R"/$Q-trees-*.csv "$R"/$Q-horizon-*.csv "$R"/$Q-storm-*.csv
  for v in main full half quarter; do
    run1 trees $v -benchstops spot -benchspot 1740,3355,180 -benchpitch -15
    run1 horizon $v $SUMMIT -benchview place,0
    run1 storm $v $SUMMIT -benchview place,-10 -benchweather Storm
  done
  tables trees; tables horizon; tables storm
fi
echo $Q done
