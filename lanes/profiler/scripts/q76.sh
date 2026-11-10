#!/bin/bash
# Job 45b: R-10 row 4c (light/forwardplus 8263a9b5, far lights) on the laptop. Read only. Profile builds, 1920x1080, -gfx High,
# uncapped, -weatherseed 4, Clear, 22:00, a fresh game per run; round 1 in list order, round 2 reversed (ABBA).
# Spot 2988.8,-920 facing north (job 45's view) and facing the Ember Forest (yaw 201: towards 2349,-2575, ~1.8 km).
# Arms: 8263a9b5 default (48 lights at High), -lightbudget 0, -lightbudget 64, -townglow 0; the branch's base 18df736a
# (so R-10 is measured against its own base); main ef393625 (06b2a72b and later don't build a player). Shots on round 1.
# RenderDoc at both views for default, base and main.
# "q76.sh bench|rd"; no argument runs both.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KL=8263a9b; KB=18df736; KM=ef39362; Q=q76
OL=Profile-${KL}; OB=Profile-${KB}; OM=Profile-${KM}
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-10 4c $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
for b in "$KL $OL" "$KB $OB" "$KM $OM"; do set -- $b; build $1 BuildDev $2 || { echo "build $2 failed"; exit 1; }; done
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd76"; mkdir -p "$L" "$RD" "$R/$Q-shots"
NIGHT="-benchweather Clear -benchhour 22 -benchstops spot"
view() { case $1 in north) echo "-benchspot 2988.8,-920,0";; ember) echo "-benchspot 2988.8,-920,201";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
rev() { echo $* | tr ' ' '\n' | tac | tr '\n' ' '; }
arm() { case $1 in def) echo $OL;; lb0) echo "$OL -lightbudget 0";; lb64) echo "$OL -lightbudget 64";; tg0) echo "$OL -townglow 0";; base) echo $OB;; main) echo $OM;; esac; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 "$@" $shot
  check "$R/q-$Q-$n-cs100-$r.log"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg\| s frame at ' "$R/q-$Q-$n-cs100-$r.log" | cut -c1-140 | tr '\n' ' ')"
}
tables() { for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part bench; then
  rm -f "$R"/$Q-*.csv
  V="def lb0 lb64 tg0 base main"
  for v in north ember; do
    for r in 1 2; do
      list=$V; [ $r = 2 ] && list=$(rev $V)
      for a in $list; do game $(arm $a | cut -d' ' -f1) $v-$a $r $(arm $a | cut -s -d' ' -f2-) $NIGHT $(view $v); done
    done
  done
  tables
fi
rdgame() { # out name args...: one RenderDoc capture at -gfx High
  local o=$1 n=$2; shift 2
  fixed $o || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop spot 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
if part rd; then
  rm -f "$RD"/*.rdc
  for v in north ember; do for a in def base main; do rdgame $(arm $a) $v-$a $NIGHT $(view $v); done; done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 5400 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  grep -c '^== ' "$R/$Q-passes.txt"
fi
echo $Q done
