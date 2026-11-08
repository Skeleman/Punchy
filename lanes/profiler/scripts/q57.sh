#!/bin/bash
# Job 29 (refactor 3.4): where the lake's GPU time goes, on refactor/water-costs a21cb739. Profile build, 1920x1080,
# render scale 1, -gfx High, uncapped. Spots: job 25's lake (-1624,-488, noon) from the shore and under the water
# (-benchpitch -35), a fresh game per run.
# Variants: plain, nowater (-benchnoshader Water), nossr (-waterssr 0), nodetail (-waterdetail 0), both (the two
# together), in the order P W S D X X D S W P. Then once each: shore -benchnopost and -benchnoshadows (what fills the
# frame), and under -benchoff Underwater (the underwater fog and tint never come on).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=a21cb73; OUT=Profile-${K}i; Q=q57
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q lake GPU split (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "profile build failed"; exit 1; }
keepawake
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
rm -f "$R"/$Q-*.csv "$L"/q-$Q-*.log
LAKE="-benchstops spot -benchspot -1624,-488,0"
go() { # spot variant round extra...
  local spot=$1 v=$2 r=$3; shift 3
  local pitch=; [ $spot = under ] && pitch="-benchpitch -35"
  BENCHW=1920 BENCHH=1080 runcsv $OUT $Q-$spot-$v $K s100 $r $LAKE $pitch -gfx High -fpscap 0 "$@"
  local log="$R/q-$Q-$spot-$v-cs100-$r.log"
  grep -q 'Crash!!!\|Received signal' "$log" && { echo "CRASH in $log"; grep -A15 'Crash!!!\|Received signal' "$log" | head -25; exit 1; }
  cp "$log" "$L/"
}
flag() { case $1 in nowater) echo -benchnoshader Water ;; nossr) echo -waterssr 0 ;; nodetail) echo -waterdetail 0 ;;
                    both) echo -waterssr 0 -waterdetail 0 ;; esac; }
for spot in shore under; do
  go $spot plain 1 -benchshot "$W/$Q-shots/$spot-plain.png"
  for v in nowater nossr nodetail both both nodetail nossr nowater plain; do
    r=1; [ -f "$L/q-$Q-$spot-$v-cs100-1.log" ] && r=2
    go $spot $v $r $(flag $v)
  done
done
go shore nopost 1 -benchnopost
go shore noshadows 1 -benchnoshadows
go under nofog 1 -benchoff Underwater -benchshot "$W/$Q-shots/under-nofog.png"
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
grep -h 'BenchRoute: spot' "$L"/q-$Q-*.log | sed 's/.*main thread/main thread/' | cut -c1-80 | sort | uniq -c | head -3
grep -h 'BenchRoute: off\|Underwater: in' "$L"/q-$Q-*-1.log | sort | uniq -c | cut -c1-200
echo $Q done
