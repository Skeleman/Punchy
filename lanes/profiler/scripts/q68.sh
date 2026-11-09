#!/bin/bash
# Job 38: why isn't quarter-resolution sky cheaper than half? Main acd0cad5 Profile build (shared with q67), job 34's
# setup (the Summit facing out, pitched up 10, 1920x1080, -gfx High, -weatherseed 4, uncapped, noon), plain weather.
#   a:  at -lowres half and quarter: -skyoff 0 / 1 (no march) / 2 (no far clouds) / 4 (no sky colour) / 8 (no weather
#       volumes), and -benchnosky. Round 1 in that order, round 2 reversed; shots on round 1.
#   rd: one RenderDoc capture at quarter and one at half (for contrast); rdpasses.py to depth 6 with the top draws.
# "q68.sh a|rd"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=acd0cad; O=Profile-${K}m4; Q=q68
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q quarter sky $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
pgrep -f freezesample.sh >/dev/null 2>&1 || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd68"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
SUMMIT="-benchstops place -benchplace Summit -benchview place,-10"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
vf() { case ${1#*-} in nosky) echo -benchnosky ;; *) echo -skyoff ${1#*-off} ;; esac; }
if part a; then
  rm -f "$R"/$Q-a-*.csv "$L"/q-$Q-a-*.log
  V="half-off0 quarter-off0 half-off1 quarter-off1 half-off2 quarter-off2 half-off4 quarter-off4 half-off8 quarter-off8 half-nosky quarter-nosky"
  for r in 1 2; do
    list=$V; [ $r = 2 ] && list=$(echo $V | tr ' ' '\n' | tac | tr '\n' ' ')
    for v in $list; do
      shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$v"
      RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv $O $Q-a-$v $K s100 $r $COMMON $SUMMIT -lowres ${v%%-*} $(vf $v) $shot
      check "$R/q-$Q-a-$v-cs100-$r.log"
      grep -h ' s frame at ' "$R/q-$Q-a-$v-cs100-$r.log" | head -2
    done
  done
  for f in "$R"/$Q-a-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
if part rd; then
  rm -f "$RD"/*.rdc
  for s in quarter half; do
    fixed $O || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd68\\$s" \
        "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$s.log" -benchroute -weatherseed 4 -time 12 \
        $(screen Bench 1920 1080) $COMMON $SUMMIT -lowres $s -renderdocstop place 1 >/dev/null 2>&1 )
    [ $? -eq 124 ] && echo "rd $s: TIMED OUT"
    strays >/dev/null; check "$R/q-$Q-rd-$s.log"
    grep -h 'RenderDoc capture' "$R/q-$Q-rd-$s.log"
  done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-passes.txt" RD_MAXDEPTH=6 RD_TOPDRAWS=25 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  head -250 "$R/$Q-passes.txt"
fi
echo $Q done
