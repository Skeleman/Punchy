#!/bin/bash
# Job 35: can the depth-normal prepass go? render/ssao-source 75df5e08 (main 4669aabc + -ssao normals|depth|after|off).
# Job 31's spots: the bigtown stop and the dense forest (-benchspot 1740,3355,180), both in one game per run.
# Profile build, 1920x1080, -gfx High, -weatherseed 4, uncapped, noon, a fresh game per run.
#   a:  GPU ms per mode, order normals depth after off off after depth normals; shots of each mode on round 1.
#   rd: one RenderDoc capture per mode at the forest; rdpasses.py: which passes remain and their GPU share.
# "q65.sh a|rd"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=75df5e0; O=Profile-${K}m4; Q=q65
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q ssao source $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
L="$R/${Q}logs"; RD="$R/rd65"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
STOPS="-benchstops bigtown,spot -benchspot 1740,3355,180"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
if part a; then
  rm -f "$R"/$Q-a-*.csv "$L"/q-$Q-a-*.log
  for m in normals depth after off off after depth normals; do
    r=1; [ -f "$L/q-$Q-a-$m-cs100-1.log" ] && r=2
    shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$m"
    BENCHW=1920 BENCHH=1080 runcsv $O $Q-a-$m $K s100 $r $COMMON $STOPS -ssao $m $shot
    check "$R/q-$Q-a-$m-cs100-$r.log"
    grep -h -i 'ssao' "$R/q-$Q-a-$m-cs100-$r.log" | sort -u | head -2 | cut -c1-200
  done
  for f in "$R"/$Q-a-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
fi
if part rd; then
  rm -f "$RD"/*.rdc
  for m in normals depth after off; do
    fixed $O || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd65\\$m" \
        "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$m.log" -benchroute -weatherseed 4 -time 12 \
        $(screen Bench 1920 1080) $COMMON -benchstops spot -benchspot 1740,3355,180 -ssao $m -renderdocstop spot 1 >/dev/null 2>&1 )
    [ $? -eq 124 ] && echo "rd $m: TIMED OUT"
    strays >/dev/null; check "$R/q-$Q-rd-$m.log"
    grep -h 'RenderDoc capture' "$R/q-$Q-rd-$m.log"
  done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-passes.txt" RD_MAXDEPTH=5 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  head -200 "$R/$Q-passes.txt"
fi
echo $Q done
