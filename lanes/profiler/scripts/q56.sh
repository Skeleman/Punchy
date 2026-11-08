#!/bin/bash
# Job 28 (refactor 3.5): static flora for bigger cities, on refactor/flora-static 5fa346f3 (draws grouped by material,
# -nofloradetail). Profile build, 1920x1080, render scale 1, uncapped.
#   ab:    bigtown + Tors: static (default), anim (-nostaticflora), nodetail (-nofloradetail), order S A N N A S; then
#          static and anim once with -benchnoshadows (the shadow pass = GPU with shadows minus without).
#   jump:  townjump (800 m out, jumped into the biggest town's middle), a fresh game each run, S A A S.
#   enter: townenter (walked in from 400 m out) then towncross (100 m through the middle, -benchframes per frame for
#          the far shadow cache redraws), a fresh game each run, S A A S.
# "q56.sh ab|jump|enter" runs one part; no argument runs all three.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5fa346f; OUT=Profile-${K}h; Q=q56
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q static flora for bigger cities (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "profile build failed"; exit 1; }
keepawake
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
go() { # set variant round extra...
  local set=$1 v=$2 r=$3; shift 3
  BENCHW=1920 BENCHH=1080 runcsv $OUT $Q-$set-$v $K s100 $r -fpscap 0 "$@"
  local log="$R/q-$Q-$set-$v-cs100-$r.log"
  grep -q 'Crash!!!\|Received signal' "$log" && { echo "CRASH in $log"; grep -A15 'Crash!!!\|Received signal' "$log" | head -25; exit 1; }
  cp "$log" "$L/"
}
flag() { case $1 in anim) echo -nostaticflora ;; nodetail) echo -nofloradetail ;; esac; }
part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
ONLY=$1
if part ab; then
  rm -f "$R"/$Q-ab-*.csv "$L"/q-$Q-ab-*.log
  AB="-benchstops bigtown,place -benchplace Tors"
  go ab static 1 $AB -benchflora -benchshots "$W/$Q-shots"
  for v in anim nodetail nodetail anim static; do
    r=1; [ -f "$L/q-$Q-ab-$v-cs100-1.log" ] && r=2
    go ab $v $r $AB $(flag $v)
  done
  go ab static-noshadow 1 $AB -benchnoshadows
  go ab anim-noshadow 1 $AB -benchnoshadows -nostaticflora
  for f in "$R"/$Q-ab-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
if part jump; then
  rm -f "$R"/$Q-jump-*.csv
  for vr in static:1 anim:1 anim:2 static:2; do
    v=${vr%:*}; r=${vr#*:}
    go jump $v $r -benchstops townjump -benchframes "$W/${Q}frames/jump-$v-$r" $(flag $v)
  done
  grep -h 'townjump: jumped\|walking in\|BenchRoute: townjump\|hitch' "$L"/q-$Q-jump-*.log | cut -c1-220
fi
if part enter; then
  rm -f "$R"/$Q-enter-*.csv
  for vr in static:1 anim:1 anim:2 static:2; do
    v=${vr%:*}; r=${vr#*:}
    go enter $v $r -benchstops townenter,towncross -benchframes "$W/${Q}frames/enter-$v-$r" $(flag $v)
  done
  for f in "$R"/$Q-enter-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
echo $Q done
