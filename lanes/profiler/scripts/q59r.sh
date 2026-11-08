#!/bin/bash
# Job 31 addendum: GPU time per pass from RenderDoc frame captures. The game is launched under renderdoccmd capture
# and the runner triggers the captures itself (-renderdocstop <stop> <n>, RenderDoc's in-app API), so no key presses.
# Then rdpasses.py, run in qrenderdoc's own Python, sums each event's GPU duration under Unity's pass markers.
#   bigtown and the dense forest: refactor/flora-static 5fa346f3, -shadownear 25, static on, 3 captures each.
#   lake: refactor/water-costs 346cf4d9 (job 30's new build), job 29's spot under water and from the shore, 1 each.
# Profile builds (rebuilt with the runner's -renderdocstop), 1920x1080, -gfx High, -weatherseed 4, uncapped, noon.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5fa346f; OUT=Profile-${K}l; KW=346cf4d; OUTW=Profile-${KW}l; Q=q59r
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q RenderDoc captures (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "build $OUT failed"; exit 1; }
build $KW BuildDev $OUTW || { echo "build $OUTW failed"; exit 1; }
keepawake
L="$R/${Q}logs"; RD="$R/rd"; mkdir -p "$L" "$RD"
cap() { # build tag args...: one game under RenderDoc; its captures are rd/<tag>_frame<N>.rdc
  local out=$1 tag=$2; shift 2
  fixed $out || { echo "copy $out failed"; exit 1; }
  cool
  rm -f "$RD/$tag"_*.rdc
  ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd\\$tag" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$tag.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "$tag: TIMED OUT"
  strays >/dev/null
  grep -q 'Crash!!!\|Received signal' "$R/q-$tag.log" && { echo "CRASH in q-$tag.log"; exit 1; }
  cp "$R/q-$tag.log" "$L/"
  grep -h 'RenderDoc capture\|BenchRoute: [a-z]*: avg' "$R/q-$tag.log" | cut -c1-160
  ls "$RD/$tag"_*.rdc 2>/dev/null
}
cap $OUT town -benchstops bigtown -shadownear 25 -renderdocstop bigtown 3
cap $OUT forest -benchstops spot -benchspot 1740,3355,180 -shadownear 25 -renderdocstop spot 3
cap $OUTW under -benchstops spot -benchspot -1624,-488,0 -benchpitch -35 -renderdocstop spot 1
cap $OUTW shore -benchstops spot -benchspot -1624,-488,0 -renderdocstop spot 1
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-passes.txt" RD_MAXDEPTH=5 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"
strays >/dev/null
cat "$R/$Q-passes.txt" | head -150
echo $Q done
