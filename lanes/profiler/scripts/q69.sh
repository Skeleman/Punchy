#!/bin/bash
# Job 39: depth priming on the laptop (main 5558f89a, R-3 #220): -depthpriming forced vs off. Profile build, 1920x1080,
# -weatherseed 4, noon (-benchhour 12), Clear forced over each stop, uncapped, a fresh game per run.
# One -benchspot per game, so two games per run: the big town (bigtown stop), the dense forest (spot 1740,3355,180)
# and the Summit (place); then the lake (spot -1624,-488,0).
#   a:  -gfx High then -gfx Low: ABBA forced/off, both games each time; shots on round 1.
#   rd: one RenderDoc capture per spot, preset and mode (16); rdpasses.py: the prepass and opaque pass ms.
# "q69.sh a|rd"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5558f89; O=Profile-${K}m4; Q=q69
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q depth priming $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
pgrep -f freezesample.sh >/dev/null 2>&1 || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd69"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
WX="-benchweather Clear -benchhour 12 -fpscap 0"
G1="-benchstops bigtown,spot,place -benchspot 1740,3355,180 -benchplace Summit"
G2="-benchstops spot -benchspot -1624,-488,0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
run1() { # set preset mode args...: round 1 or 2 by whether round 1's log exists; shots on round 1
  local set=$1 g=$2 m=$3; shift 3
  local n=$Q-$set-$g-$m r=1; [ -f "$L/q-$n-cs100-1.log" ] && r=2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$set-$g-$m"
  RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv $O $n $K s100 $r -gfx $g $WX -depthpriming $m "$@" $shot
  check "$R/q-$n-cs100-$r.log"
  grep -h 'depthpriming\| s frame at ' "$R/q-$n-cs100-$r.log" | sort -u | head -3 | cut -c1-200
}
if part a; then
  rm -f "$R"/$Q-a?-*.csv "$L"/q-$Q-a?-*.log
  for g in High Low; do
    for m in forced off off forced; do run1 a1 $g $m $G1; run1 a2 $g $m $G2; done
  done
  for f in "$R"/$Q-a?-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
if part rd; then
  rm -f "$RD"/*.rdc
  for g in High Low; do for m in forced off; do
    for s in bigtown:"$G1" spot:"$G1" place:"$G1" lake:"$G2"; do
      st=${s%%:*}; a=${s#*:}; rs=$st; [ $st = lake ] && rs=spot
      fixed $O || exit 1; cool
      ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\rd69\$g-$m-$st" \
          "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$g-$m-$st.log" -benchroute -weatherseed 4 -time 12 \
          $(screen Bench 1920 1080) -gfx $g $WX -depthpriming $m $a -renderdocstop $rs 1 >/dev/null 2>&1 )
      [ $? -eq 124 ] && echo "rd $g $m $st: TIMED OUT"
      strays >/dev/null; check "$R/q-$Q-rd-$g-$m-$st.log"
      grep -h 'RenderDoc capture' "$R/q-$Q-rd-$g-$m-$st.log" | head -1
    done
  done; done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\$Q-passes.txt" RD_MAXDEPTH=5 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  grep -i 'capture\|\.rdc\|prepass\|opaque\|frame' "$R/$Q-passes.txt" | head -120
fi
echo $Q done
