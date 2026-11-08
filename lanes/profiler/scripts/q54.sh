#!/bin/bash
# Job 26 (refactor 3.5b): static flora variant A/B on refactor/flora-static 5febffdb, Profile build, uncapped.
# Stops: bigtown (job 25's big settlement), spot = dense forest (under the OakGrove canopy), place = Tors (-benchplace).
# 1920x1080 window at render scale 1.333 (1440p pixels: the laptop desktop clamps bigger windows).
# A = static variant (default), B = -nostaticflora; order A B B A. Then one A and one B with -benchnoshadows: the shadow
# pass = GPU with shadows minus GPU without. Round 1's A takes screenshots of each stop.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5febffd; OUT=Profile-${K}f
STOPS="-benchstops bigtown,spot -benchspot 1740,3355,180"; Q=q54
# "q54.sh tors": the Tors stop alone (the first run left "place" out of -benchstops, so it never ran)
[ "$1" = tors ] && { STOPS="-benchstops place -benchplace Tors"; Q=q54t; }
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q static flora A/B (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q54logs"; mkdir -p "$L" "$R/$Q-shots"
rm -f "$R"/$Q-*.csv
go() { # name round extra...
  local name=$1 r=$2; shift 2
  BENCHW=1920 BENCHH=1080 runcsv $OUT $Q-$name $K s133 $r $STOPS -fpscap 0 -benchflora -benchrenderscale 1.333 "$@"
  local log="$R/q-$Q-$name-cs133-$r.log"
  grep -q 'Crash!!!\|Received signal' "$log" && { echo "CRASH in $log"; grep -A15 'Crash!!!\|Received signal' "$log" | head -25; exit 1; }
  cp "$log" "$L/"
}
go static 1 -benchshots "$W/$Q-shots"
go anim 1 -nostaticflora
go anim 2 -nostaticflora
go static 2
go static-noshadow 1 -benchnoshadows
go anim-noshadow 1 -nostaticflora -benchnoshadows
for f in static anim static-noshadow anim-noshadow; do echo "== $f"; table "$R/$Q-$f.csv"; done
grep -h '\[flora\] static' "$L"/q-$Q-static-cs133-1.log | head -1 | cut -c1-200
grep -h 'BenchRoute flora' "$L"/q-$Q-static-cs133-1.log | cut -c1-200
grep -h 'BenchRoute: place' "$L"/q-$Q-static-cs133-1.log | cut -c1-200
ls "$R/$Q-shots"
echo $Q done
