#!/bin/bash
# Job 78 (audit 11-13, #311): night town light cost on main 3b192717 (the supervisor's shared main for jobs 78-80, 87).
# The town of job 76's far-lights view (2988.8,-920; the R-25 town), at midnight, Clear: from the square (yaw 0) and from
# 200 m south of it looking north (2988.8,-1120, yaw 0). Medium and Ultra, 1920x1080, uncapped, -lightlog.
# Per view and preset: 3 measured games (GPU, main), the first with the Profiler on (LightBudget.Resolve and the glow
# work on the CPU, via BProfileTools.Analyze); then one RenderDoc frame each (the glow pass's GPU ms, rdpasses.py).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
Q=q120; K=3b19271; O=Profile-$K
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 78 night town lights (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
[ -f "$P/Builds/$O/.bench2-$K" ] || "$S/shelf.sh" get $K "$P" $O || true
# (A shelf build made before 11-14 19:30 overlaid main's runner, without -benchline/-renderdocstop: rebuild it here.)
[ -d "$P/Builds/$O" ] && { python "$S/runnercheck.py" "$(cygpath -w "$P/Builds/$O")" || rm -rf "${P:?}/Builds/$O"; }
build $K BuildDev $O || { echo "build failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="${R:?}/rd120"; mkdir -p "$L" "$RD" "$R/$Q-shots"; rm -f "$RD"/*.rdc "$R"/$Q-*.csv
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
view() { case $1 in
  square) echo "-goto 2988.8,-920 -benchstops spot -benchspot 2988.8,-920,0";;
  out200) echo "-goto 2988.8,-1120 -benchstops spot -benchspot 2988.8,-1120,0";; esac; }
COMMON="-benchhour 0 -benchweather Clear -lightlog"
for g in Medium Ultra; do for v in square out200; do for r in 1 2 3; do
  n=${g,,}-$v; tag=$Q-$n-cs100-$r; pf=(); shot=()
  [ $r = 1 ] && { pf=(-profiler-enable -profiler-log-file "$W/q-$tag.raw" -profiler-maxusedmemory 1073741824); shot=(-benchshots "$W/$Q-shots/$n"); }
  BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx $g -fpscap 0 $(view $v) $COMMON "${pf[@]}" "${shot[@]}"
  f="$R/q-$tag.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: spot: avg' "$f" | grep -o '1% low [0-9.]*\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms' | tr '\n' ' ') | $(grep -h '\[lights\]' "$f" | tail -1 | cut -c1-140)"
done; done; done
for g in Medium Ultra; do for v in square out200; do
  n=${g,,}-$v; fixed $O || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -body ${BENCHBODY:-default} \
      $(screen Bench 1920 1080) -gfx $g -fpscap 0 $(view $v) $COMMON -renderdocstop spot 1 >/dev/null 2>&1 )
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
done; done
CAPS=$(ls "$RD"/*.rdc 2>/dev/null | xargs -n1 cygpath -w | tr '\n' ';')
[ -n "$CAPS" ] && RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
for t in $(ls "$R"/q-$Q-*-cs100-1.raw 2>/dev/null | xargs -n1 basename | sed 's/^q-//; s/\.raw$//'); do
  ( cd "$P" && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze \
      -raws "$WW\\q-$t.raw" -out "$WW\\$t.txt" -keep LightBudget -logFile "$W/analyze-$t.log" )
  [ -f "$R/$t.txt" ] && rm -f "$R/q-$t.raw"
  echo "== $t"; grep -i "LightBudget\|Glow\|Light" "$R/$t.txt" | head -12
done
python "$B/report.py" "$R/$Q"
echo $Q done
