#!/bin/bash
# Job 32 (refactor 3.4): what the sky costs by weather; Snell's window and the underwater sky skip.
# Profile builds, 1920x1080, render scale 1, -gfx High, -weatherseed 4, uncapped, noon, a fresh game per run.
#   a:  main 46012623 at the Summit place, facing out (its own yaw), camera pitched up (-benchview place,-10): plain,
#       -benchweather Clear / Overcast / Storm (forced over the stop by the runner, held), -benchnoshader Sky.
#       Order P C O S N N S O C P.
#   ard: one RenderDoc capture each of plain, Clear, Overcast (rdpasses.py: the skybox draw's GPU time).
#   b:  water/snells-window 31b890be against main, job 29's lake spot under water and 12 m east of the pillar, A B B A.
#   grass (addendum): main at the Meadow place (its own yaw, pitch 12), -grassdensity 1 / 2 / 4 and grass off
#       (-benchnoshader Grass), order 1 2 4 N N 4 2 1; then a RenderDoc capture at density 1 and 4 (top draws).
#       Withdrawn (Zach: clumps, not density), so it runs only as "q60.sh grass".
# "q60.sh a|ard|b|grass"; no argument runs a, ard and b.
# Rerun 2026-11-08: the first try froze for 6-13+ min on the first frame at the Summit (7 of 13 sessions; 1-frame
# stops or timeouts), so main is the m2 build (its runner caps settling at 0.25 s a frame and logs frames over 5 s)
# and Summit sessions get 1800 s.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KM=4601262; OM=Profile-${KM}m2; KS=31b890b; OS=Profile-${KS}m; Q=q60
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q sky by weather $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
build $KS BuildDev $OS || { echo "build $OS failed"; exit 1; }
keepawake
L="$R/${Q}logs"; RD="$R/rd60"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
SUMMIT="-benchstops place -benchplace Summit -benchview place,-10"
LAKE="-benchstops spot -benchspot -1624,-488,0 -benchpitch -35"
MOVED="-benchstops spot -benchspot -1612,-488,0 -benchpitch -35"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
go() { # set variant round build commit extra...
  local set=$1 v=$2 r=$3 out=$4 c=$5; shift 5
  BENCHW=1920 BENCHH=1080 runcsv $out $Q-$set-$v $c s100 $r $COMMON "$@"
  check "$R/q-$Q-$set-$v-cs100-$r.log"
}
wx() { case $1 in clear) echo -benchweather Clear ;; overcast) echo -benchweather Overcast ;; storm) echo -benchweather Storm ;; nosky) echo -benchnoshader Sky ;; esac; }
if part a; then
  rm -f "$R"/$Q-sky-*.csv "$L"/q-$Q-sky-*.log
  for v in plain clear overcast storm nosky nosky storm overcast clear plain; do
    r=1; [ -f "$L/q-$Q-sky-$v-cs100-1.log" ] && r=2
    shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/sky-$v"
    RUNTIMEOUT=1800 go sky $v $r $OM $KM $SUMMIT $(wx $v) $shot
  done
  for f in "$R"/$Q-sky-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'weather .* forced\|off shaders\|BenchRoute: place: avg' "$L"/q-$Q-sky-*-1.log | sed 's/ fps.*, at / ... at /' | cut -c1-200
fi
if part ard; then
  rm -f "$RD"/*.rdc
  for v in plain clear overcast; do
    fixed $OM || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd60\\$v" \
        "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$v.log" -benchroute -weatherseed 4 -time 12 \
        $(screen Bench 1920 1080) $COMMON $SUMMIT $(wx $v) -renderdocstop place 1 >/dev/null 2>&1 )
    strays >/dev/null; check "$R/q-$Q-rd-$v.log"
    grep -h 'RenderDoc capture' "$R/q-$Q-rd-$v.log"
  done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-passes.txt" RD_MAXDEPTH=5 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  grep -i 'sky\|== \|(frame)' "$R/$Q-passes.txt" | head -40
fi
if part b; then
  for spot in under moved; do
    rm -f "$R"/$Q-$spot-*.csv
    [ $spot = under ] && A="$LAKE" || A="$MOVED"
    go $spot main 1 $OM $KM $A -benchshots "$W/$Q-shots/$spot-main"
    go $spot snell 1 $OS $KS $A -benchshots "$W/$Q-shots/$spot-snell"
    go $spot snell 2 $OS $KS $A
    go $spot main 2 $OM $KM $A
    for f in "$R"/$Q-$spot-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  done
fi
if [ "$ONLY" = grass ]; then   # the addendum was withdrawn (clumps instead): only when asked
  rm -f "$R"/$Q-grass-*.csv "$L"/q-$Q-grass-*.log
  MEADOW="-benchstops place -benchplace Meadow -benchview place,12"
  gr() { case $1 in d1) echo -grassdensity 1 ;; d2) echo -grassdensity 2 ;; d4) echo -grassdensity 4 ;; off) echo -benchnoshader Grass ;; esac; }
  for v in d1 d2 d4 off off d4 d2 d1; do
    r=1; [ -f "$L/q-$Q-grass-$v-cs100-1.log" ] && r=2
    shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/grass-$v"
    go grass $v $r $OM $KM $MEADOW $(gr $v) $shot
  done
  for f in "$R"/$Q-grass-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'off shaders\|GroundDetail\|BenchRoute: place: avg' "$L"/q-$Q-grass-*-1.log | sed 's/ fps.*, at / ... at /' | cut -c1-200 | sort -u
  mkdir -p "$R/rd60g"; rm -f "$R/rd60g"/*.rdc
  for v in d1 d4; do
    fixed $OM || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd60g\\$v" \
        "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rdg-$v.log" -benchroute -weatherseed 4 -time 12 \
        $(screen Bench 1920 1080) $COMMON $MEADOW $(gr $v) -renderdocstop place 1 >/dev/null 2>&1 )
    strays >/dev/null; check "$R/q-$Q-rdg-$v.log"
  done
  CAPS=$(ls "$R/rd60g"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-grass-passes.txt" RD_TOPDRAWS=25 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
fi
echo $Q done
