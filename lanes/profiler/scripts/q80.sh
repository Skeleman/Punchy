#!/bin/bash
# Jobs 45d and 45f: R-9 rows 5 and 6 on the laptop. Read only. Profile builds, 1920x1080, -gfx High, uncapped,
# -weatherseed 4, noon, Clear, a fresh game per run, 4 rounds alternating (ABBA x2). The camera is pinned with the game's
# -shotcam and measured by the bench's "here" stop (the player left where -goto put it). Shots on round 1.
#   d: water/deform 2856a634 (row 5), fall 74 from 40 m: field on vs -waterfield 0.
#   f: water/deform 9b1cacc1 (row 6) vs 2856a634 (row 5), the rapids below fall 74 from 30 m, and the same 6 m from the
#      grid line x = -2176 (the double-read band).
# Then one RenderDoc frame per arm and view. "q80.sh d|f"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K5=2856a63; K6=9b1cacc; O5=Profile-${K5}; O6=Profile-${K6}; Q=q80
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-9 rows 5-6 $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
build $K5 BuildDev $O5 || { echo "build $O5 failed"; exit 1; }
part f && { build $K6 BuildDev $O6 || { echo "build $O6 failed"; exit 1; }; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="${R:?}/rd80"; mkdir -p "$L" "$RD" "$R/$Q-shots"
AT="-goto -2167,3,3691 -benchstops here -benchweather Clear -benchhour 12"
FALL="-shotcam -2150.7,30.1,3675.1,-2180.2,12.1,3648.1"
RAPIDS="-shotcam -2169,13.1,3682,-2184,3.1,3656"
BAND="-shotcam -2170,13.1,3682,-2185,3.1,3656"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'DebugControls: shotcam' "$f" | head -1 | cut -c1-80) | $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-80)"
}
rdgame() { # out name args...
  local o=$1 n=$2; shift 2
  fixed $o || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop here 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
if part d; then
  rm -f "$R"/$Q-d-*.csv
  for r in 1 2 3 4; do
    list="on off"; [ $((r % 2)) = 0 ] && list="off on"
    for a in $list; do x=; [ $a = off ] && x="-waterfield 0"; game $O5 d-$a $r $x $AT $FALL; done
  done
  for f in "$R"/$Q-d-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
if part f; then
  rm -f "$R"/$Q-f-*.csv
  for v in rapids band; do
    cam=$RAPIDS; [ $v = band ] && cam=$BAND
    for r in 1 2 3 4; do
      list="row6 row5"; [ $((r % 2)) = 0 ] && list="row5 row6"
      for a in $list; do o=$O6; [ $a = row5 ] && o=$O5; game $o f-$v-$a $r $AT $cam; done
    done
  done
  for f in "$R"/$Q-f-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
rm -f "${RD:?}"/*.rdc
part d && { rdgame $O5 d-on $AT $FALL; rdgame $O5 d-off -waterfield 0 $AT $FALL; }
part f && { rdgame $O6 f-rapids-row6 $AT $RAPIDS; rdgame $O5 f-rapids-row5 $AT $RAPIDS; }
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
grep -c '^== ' "$R/$Q-passes.txt"
echo $Q done
