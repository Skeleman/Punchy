#!/bin/bash
# Job 45g follow-up: RenderDoc pairs at fall 74 and the rapids, to split what's left of rows 5 and 6 (job 45g: fall +1.2 over
# off, rapids +1.0 over row 5). One frame per arm, 1920x1080, -gfx High, noon, Clear, -body default.
#   fall:   new (b/water-rows-fixes 0318b5ee), old (water/deform 9b1cacc1), off (new with -waterfield 0)
#   rapids: new, old, row5 (2856a634)
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
ON=Profile-0318b5e; OO=Profile-9b1cacc; O5=Profile-2856a63; Q=q92
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q water rows RenderDoc (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
keepawake
L="$R/${Q}logs"; RD="${R:?}/rd92"; mkdir -p "$L" "$RD"
AT="-goto -2167,3,3691 -benchstops here -benchweather Clear -benchhour 12"
FALL="-shotcam -2150.7,30.1,3675.1,-2180.2,12.1,3648.1"
RAPIDS="-shotcam -2169,13.1,3682,-2184,3.1,3656"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
rdgame() { # out name args...
  local o=$1 n=$2; shift 2
  fixed $o || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 -body ${BENCHBODY:-default} \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop here 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
rm -f "$RD"/*.rdc
rdgame $ON fall-new $AT $FALL; rdgame $OO fall-old $AT $FALL; rdgame $ON fall-off -waterfield 0 $AT $FALL
rdgame $ON rapids-new $AT $RAPIDS; rdgame $OO rapids-old $AT $RAPIDS; rdgame $O5 rapids-row5 $AT $RAPIDS
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
grep -c '^== ' "$R/$Q-passes.txt"
echo $Q done
