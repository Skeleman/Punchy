#!/bin/bash
# Zach 11-11: q84 again right after a fresh boot of the laptop, to check nothing was slowly worsening the numbers.
# Zach 11-11: fix 1, the field shading the chunk water over its whole square with no ring ripples or foam (b/fix-waterfield-step
# 17ec2216), vs the old look (-waterfieldripples 1). Earlier: displacement only (a9db11d7: where the field fully covers, no ring
# ripples are worked out and the field adds no foam) vs the old ripples and foam (-waterfieldripples 1), both at cuts 4 and
# 30 Hz (-waterfieldcuts 4 -waterfieldhz 30, the settings Zach found look the same), and the field off. 3 rounds. Profile build, 1920x1080, -gfx High, uncapped, -weatherseed 4, noon, Clear, a fresh game per run.
# Job 45's spot and swim: -goto 488,-1144 (the lake), then swim east 40 m at 4 m/s (-benchline swim mode). Field on vs
# -waterfield 0, ABAB x2 (4 rounds). Shots on round 1. Whole-frame GPU/CPU ms from the bench; the water pass ms from one
# RenderDoc capture per arm.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=17ec221; O=Profile-${K}; Q=q86
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q field displacement only $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="${R:?}/rd86"; mkdir -p "$L" "$RD" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999 -benchweather Clear -benchhour 12"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # name round args...
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx High -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'WaterField:' "$f" | tail -1 | cut -c13-120) | $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-80)"
}
rdgame() { # name args...: one RenderDoc capture
  local n=$1; shift
  fixed $O || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 -body ${BENCHBODY:-default} \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop line 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
SET="-waterfieldcuts 4 -waterfieldhz 30"
arm() { case $1 in ripples) echo "$SET -waterfieldripples 1";; disp) echo "$SET";; off) echo "-waterfield 0";; esac; }
rev() { echo $* | tr ' ' '
' | tac | tr '
' ' '; }
# "q83.sh rd": the RenderDoc part only (the first run's captures launched without -body, so Zach's horse).
if [ "$1" != rd ]; then
rm -f "$R"/$Q-*.csv
for r in 1 2; do
  V="ripples disp off"; [ $r = 2 ] && V=$(rev $V)
  for a in $V; do game $a $r $(arm $a) $SWIM; done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
rm -f "${RD:?}"/*.rdc
for a in ripples disp off; do rdgame $a $(arm $a) $SWIM; done
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '
' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
grep -c '^== ' "$R/$Q-passes.txt"
echo $Q done
