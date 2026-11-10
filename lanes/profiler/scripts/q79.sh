#!/bin/bash
# Job 45c: R-9 row 4 water field re-run (water/deform 24459300: the fine sheet cut by screen size only near the player).
# Read only. Profile build, 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, a fresh game per run.
# Job 45's spot and swim: -goto 488,-1144 (the lake), then swim east 40 m at 4 m/s (-benchline swim mode). Field on vs
# -waterfield 0, ABAB x2 (4 rounds). Shots on round 1. Whole-frame GPU/CPU ms from the bench; the water pass ms from one
# RenderDoc capture per arm.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2445930; O=Profile-${K}; Q=q79
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-9 row 4 $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="${R:?}/rd79"; mkdir -p "$L" "$RD" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999 -benchweather Clear -benchhour 12"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # name round args...
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx High -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'swimming from' "$f" | cut -c13-60) | $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-80)"
}
rdgame() { # name args...: one RenderDoc capture
  local n=$1; shift
  fixed $O || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop line 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
rm -f "$R"/$Q-*.csv
for r in 1 2 3 4; do
  list="on off"; [ $((r % 2)) = 0 ] && list="off on"
  for a in $list; do
    x=; [ $a = off ] && x="-waterfield 0"
    game $a $r $x $SWIM
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
rm -f "${RD:?}"/*.rdc
rdgame on $SWIM
rdgame off -waterfield 0 $SWIM
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
grep -c '^== ' "$R/$Q-passes.txt"
echo $Q done
