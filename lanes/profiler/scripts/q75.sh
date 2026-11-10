#!/bin/bash
# Job 45: R-9 water field and R-10 lights on the laptop. Read only. Profile builds, 1920x1080, -gfx High, uncapped,
# -weatherseed 4, Clear, a fresh game per run, round 1 in list order, round 2 reversed (ABBA), and so on.
#   r9:   water/deform (fd50ad58): -goto 488,-1144 (the lake; the player swims there), then swim straight east 40 m at
#         4 m/s (-benchline swim mode, Punchy b/bench-stress fa26214d), field on vs -waterfield 0, noon, 4 rounds (ABAB x2).
#         Shots on round 1. Whole-frame GPU ms from the bench; the water pass ms from one RenderDoc capture per arm.
#   r10:  light/forwardplus (0a4b3b50) at 2988.8,-920 facing north, 22:00: default (48 lights at High), -lightbudget 0,
#         -lightbudget 64, -townglow 0; and origin/main (06b2a72b) default as today's baseline. 4 rounds.
# "q75.sh r9|r10|rd"; no argument runs r9, r10, then rd.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KW=fd50ad5; KL=0a4b3b5; KM=06b2a72; Q=q75
OW=Profile-${KW}; OL=Profile-${KL}; OM=Profile-${KM}
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-9 / R-10 $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
for b in "$KW $OW" "$KL $OL" "$KM $OM"; do set -- $b; build $1 BuildDev $2 || { echo "build $2 failed"; exit 1; }; done
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd75"; mkdir -p "$L" "$RD" "$R/$Q-shots"
SWIM="-goto 488,-1144 -benchstops line -benchline 488,-1144,528,-1144,4,-999"
LAKE="-benchweather Clear -benchhour 12"
TOWN="-benchweather Clear -benchhour 22 -benchstops spot -benchspot 2988.8,-920,0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
rev() { echo $* | tr ' ' '\n' | tac | tr '\n' ' '; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 "$@" $shot
  check "$R/q-$Q-$n-cs100-$r.log"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg\|swimming from\| s frame at ' "$R/q-$Q-$n-cs100-$r.log" | cut -c1-140 | tr '\n' ' ')"
}
tables() { for f in "$R"/$Q-$1-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
arm9() { case $1 in on) echo $OW;; off) echo "$OW -waterfield 0";; esac; }
arm10() { case $1 in def) echo $OL;; lb0) echo "$OL -lightbudget 0";; lb64) echo "$OL -lightbudget 64";; tg0) echo "$OL -townglow 0";; main) echo $OM;; esac; }
if part r9; then
  rm -f "$R"/$Q-r9-*.csv
  for r in 1 2 3 4; do
    list="on off"; [ $((r % 2)) = 0 ] && list="off on"
    for a in $list; do game $(arm9 $a | cut -d' ' -f1) r9-$a $r $(arm9 $a | cut -s -d' ' -f2-) $LAKE $SWIM; done
  done
  tables r9
fi
if part r10; then
  rm -f "$R"/$Q-r10-*.csv
  V="def lb0 lb64 tg0 main"
  for r in 1 2 3 4; do
    list=$V; [ $((r % 2)) = 0 ] && list=$(rev $V)
    for a in $list; do game $(arm10 $a | cut -d' ' -f1) r10-$a $r $(arm10 $a | cut -s -d' ' -f2-) $TOWN; done
  done
  tables r10
fi
rdgame() { # out name stop args...: one RenderDoc capture at -gfx High
  local o=$1 n=$2 st=$3; shift 3
  fixed $o || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop $st 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
if part rd; then
  rm -f "$RD"/*.rdc
  rdgame $OW r9-on line $LAKE $SWIM
  rdgame $OW r9-off line -waterfield 0 $LAKE $SWIM
  for a in def lb0 lb64 tg0 main; do rdgame $(arm10 $a | cut -d' ' -f1) r10-$a spot $(arm10 $a | cut -s -d' ' -f2-) $TOWN; done
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 5400 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  grep -c '^== ' "$R/$Q-passes.txt"
fi
echo $Q done
