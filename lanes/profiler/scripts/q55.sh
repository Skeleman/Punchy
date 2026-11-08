#!/bin/bash
# Job 27 (refactor 3.2): fire instance buffer A/B. A = main 01cb543 (e5c88fb4 game code), B = refactor/fire-budgets 9657300
# (fire-instances 03783ca9 + the Ember Forest near/far hand-off fix). Profile builds, 1920x1080, scale 1, uncapped.
# Sets: ember = the Ember Forest burning at night (-benchplace EmberForest: -benchhour 22 -benchview 120,5);
#       fires = job 25's start spot without and with 500 -benchfires flames (noon).
# Order A B B A per set; then each build once with -benchoff LandmarkEffectSystem (it draws every flame, and the forest
# fire's own effects): GPU with minus GPU without = the flame (and landmark effect) passes.
# The route logs "BenchRoute flames": flames a frame (read before they are drawn) and FireSystem.Dropped (B only).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=01cb543; KB=9657300; A=Profile-${KA}g; BB=Profile-${KB}g
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q55 fire instances A/B (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KA BuildDev $A || { echo "build A failed"; exit 1; }
build $KB BuildDev $BB || { echo "build B failed"; exit 1; }
keepawake
L="$R/q55logs"; mkdir -p "$L" "$R/q55-shots-a" "$R/q55-shots-b"
rm -f "$R"/q55-*.csv
args() { case $1 in
  ember) echo "-benchstops place -benchplace EmberForest: -benchview 120,5 -benchhour 22" ;;
  fires) echo "-benchstops firesbase,fires" ;;
esac; }
go() { # build commit tag set round extra...
  local out=$1 c=$2 tag=$3 set=$4 r=$5; shift 5
  BENCHW=1920 BENCHH=1080 runcsv $out q55-$tag-$set $c s100 $r $(args $set) -fpscap 0 "$@"
  local log="$R/q-q55-$tag-$set-cs100-$r.log"
  grep -q 'Crash!!!\|Received signal' "$log" && { echo "CRASH in $log"; grep -A15 'Crash!!!\|Received signal' "$log" | head -25; exit 1; }
  cp "$log" "$L/"
}
for set in ember fires; do
  go $A $KA a $set 1 -benchshots "$W/q55-shots-a"
  go $BB $KB b $set 1 -benchshots "$W/q55-shots-b"
  go $BB $KB b $set 2
  go $A $KA a $set 2
  go $A $KA a-off $set 1 -benchoff LandmarkEffectSystem
  go $BB $KB b-off $set 1 -benchoff LandmarkEffectSystem
done
for f in "$R"/q55-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
grep -h 'BenchRoute flames\|BenchRoute: off' "$L"/q-q55-*-cs100-1.log | sort | uniq -c | cut -c1-200
ls "$R/q55-shots-a" "$R/q55-shots-b"
echo q55 done
