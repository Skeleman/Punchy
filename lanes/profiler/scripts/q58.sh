#!/bin/bash
# Job 30 (refactor 3.4): water layers skipped where unused, A/B, and the underwater frame without water.
# A = refactor/water-costs a21cb739 (the job 29 build), B = 346cf4d9 (skips zero-weight layers). Profile builds,
# 1920x1080, render scale 1, -gfx High, -weatherseed 4, uncapped, a fresh game per run.
#   A. lake shore and under (job 29's spots), A B B A.
#   B. river (the route's river stop) and the sea (-benchplace Beach) in one game, A B B A.
#   C. B only, the under view with water off: plus terrain off, plus flora off, plus terrain, flora and grass off
#      (order w wt wf wall wall wf wt w); then 12 m east of the sea-folk pillar, plain and water off (P W W P).
# "q58.sh a|b|c" runs one part; no argument runs all three.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=a21cb73; KB=346cf4d; OA=Profile-${KA}i; OB=Profile-${KB}j; Q=q58
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q water layer skip A/B (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KA BuildDev $OA || { echo "build A failed"; exit 1; }
build $KB BuildDev $OB || { echo "build B failed"; exit 1; }
keepawake
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
LAKE="-benchstops spot -benchspot -1624,-488,0"
MOVED="-benchstops spot -benchspot -1612,-488,0"
go() { # set variant round build commit extra...
  local set=$1 v=$2 r=$3 out=$4 c=$5; shift 5
  BENCHW=1920 BENCHH=1080 runcsv $out $Q-$set-$v $c s100 $r -gfx High -fpscap 0 "$@"
  local log="$R/q-$Q-$set-$v-cs100-$r.log"
  grep -q 'Crash!!!\|Received signal' "$log" && { echo "CRASH in $log"; grep -A15 'Crash!!!\|Received signal' "$log" | head -25; exit 1; }
  cp "$log" "$L/"
}
abba() { # set args...
  local set=$1; shift
  rm -f "$R"/$Q-$set-*.csv
  go $set a 1 $OA $KA "$@" -benchshots "$W/$Q-shots/$set-a"
  go $set b 1 $OB $KB "$@" -benchshots "$W/$Q-shots/$set-b"
  go $set b 2 $OB $KB "$@"
  go $set a 2 $OA $KA "$@"
  for f in "$R"/$Q-$set-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
}
if part a; then
  abba shore $LAKE
  abba under $LAKE -benchpitch -35
fi
part b && abba rivsea -benchstops river,place -benchplace Beach
if part c; then
  rm -f "$R"/$Q-c-*.csv "$L"/q-$Q-c-*.log
  shaders() { case $1 in w) echo Water ;; wt) echo Water,Terrain ;; wf) echo Water,Flora ;; wall) echo Water,Terrain,Flora,Grass ;; esac; }
  for v in w wt wf wall wall wf wt w; do
    r=1; [ -f "$L/q-$Q-c-$v-cs100-1.log" ] && r=2
    shot=; [ $r = 1 ] && shot="-benchshot $W/$Q-shots/c-$v.png"
    go c $v $r $OB $KB $LAKE -benchpitch -35 -benchnoshader $(shaders $v) $shot
  done
  go c moved 1 $OB $KB $MOVED -benchpitch -35 -benchshot "$W/$Q-shots/c-moved.png"
  go c moved-w 1 $OB $KB $MOVED -benchpitch -35 -benchnoshader Water -benchshot "$W/$Q-shots/c-moved-w.png"
  go c moved-w 2 $OB $KB $MOVED -benchpitch -35 -benchnoshader Water
  go c moved 2 $OB $KB $MOVED -benchpitch -35
  for f in "$R"/$Q-c-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'off shaders' "$L"/q-$Q-c-*-1.log | cut -c1-250 | sort -u
fi
# "q58.sh sky": what's left of the under view with water, terrain, flora and grass off (27 ms GPU): the sky shader,
# post and shadows, on B. Order: w ws wall wallsky wallsky wall ws w, then w-nopost and w-noshadows once each.
if [ "$ONLY" = sky ]; then
  rm -f "$R"/$Q-sky-*.csv "$L"/q-$Q-sky-*.log
  shaders() { case $1 in w) echo Water ;; ws) echo Water,Sky ;; wall) echo Water,Terrain,Flora,Grass ;; wallsky) echo Water,Terrain,Flora,Grass,Sky ;; esac; }
  for v in w ws wall wallsky wallsky wall ws w; do
    r=1; [ -f "$L/q-$Q-sky-$v-cs100-1.log" ] && r=2
    shot=; [ $r = 1 ] && shot="-benchshot $W/$Q-shots/sky-$v.png"
    go sky $v $r $OB $KB $LAKE -benchpitch -35 -benchnoshader $(shaders $v) $shot
  done
  go sky w-nopost 1 $OB $KB $LAKE -benchpitch -35 -benchnoshader Water -benchnopost
  go sky w-noshadows 1 $OB $KB $LAKE -benchpitch -35 -benchnoshader Water -benchnoshadows
  for f in "$R"/$Q-sky-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'off shaders' "$L"/q-$Q-sky-*-1.log | cut -c1-250 | sort -u
fi
grep -h 'BenchRoute: place:\|BenchRoute: river:' "$L"/q-$Q-rivsea-a-cs100-1.log | sed 's/ fps.*, at / ... at /' | cut -c1-160
echo $Q done
