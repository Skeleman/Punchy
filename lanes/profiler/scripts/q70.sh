#!/bin/bash
# Job 40: R-4 sky cost on the laptop (sky/look 5880293c vs main 5558f89a). Job 34/38's setup: Profile builds, the
# Summit facing out, pitched up 10, 1920x1080, -gfx High, -weatherseed 4, noon, uncapped, a fresh game per run.
#   a:  plain weather (as job 38) at -lowres half and quarter: main, R-4, R-4 -skyoff 896 (main's cloud look on R-4
#       code), R-4 -benchnosky. Round 1 in that order, round 2 reversed; shots on round 1.
#   wx: -benchweather Overcast and Rain at half, main vs R-4, ABBA.
#   rd: one RenderDoc capture of R-4 at half (DrawSkybox against job 38's 3.68 ms).
# "q70.sh a|wx|rd"; no argument runs all three.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KM=5558f89; OM=Profile-${KM}m4; KN=5880293; ON=Profile-${KN}m4; Q=q70
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-4 sky $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
build $KN BuildDev $ON || { echo "build $ON failed"; exit 1; }
keepawake
pgrep -f freezesample.sh >/dev/null 2>&1 || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd70"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { [ -z "$ONLY" ] || [ "$ONLY" = "$1" ]; }
SUMMIT="-benchstops place -benchplace Summit -benchview place,-10"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
# variant (main|r4|r4old|nosky) -> build and flags
vb() { case $1 in main) echo "$OM $KM" ;; *) echo "$ON $KN" ;; esac; }
vf() { case $1 in r4old) echo -skyoff 896 ;; nosky) echo -benchnosky ;; esac; }
run1() { # set variant args...: round 1 or 2 by whether round 1's log exists; shots on round 1
  local set=$1 v=$2; shift 2
  local n=$Q-$set-$v r=1; [ -f "$L/q-$n-cs100-1.log" ] && r=2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$set-$v"
  local b=$(vb $v)
  RUNTIMEOUT=1800 BENCHW=1920 BENCHH=1080 runcsv ${b% *} $n ${b#* } s100 $r $COMMON $SUMMIT "$@" $(vf $v) $shot
  check "$R/q-$n-cs100-$r.log"
  grep -h ' s frame at \|skyoff\|Weather: forced' "$R/q-$n-cs100-$r.log" | sort -u | head -3 | cut -c1-200
}
tables() { for f in "$R"/$Q-$1*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part a; then
  rm -f "$R"/$Q-half-*.csv "$R"/$Q-quarter-*.csv "$L"/q-$Q-half-*.log "$L"/q-$Q-quarter-*.log
  V="main r4 r4old nosky"
  for s in half quarter; do
    for v in $V $(echo $V | tr ' ' '\n' | tac); do run1 $s $v -lowres $s; done
  done
  tables half; tables quarter
fi
if part wx; then
  rm -f "$R"/$Q-wx*.csv "$L"/q-$Q-wx*.log
  for w in Overcast Rain; do
    for v in main r4 r4 main; do run1 wx$w $v -lowres half -benchweather $w; done
  done
  tables wx
fi
if part rd; then
  rm -f "$RD"/*.rdc
  fixed $ON || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\rd70\r4-half" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) $COMMON $SUMMIT -lowres half -renderdocstop place 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd.log"
  grep -h 'RenderDoc capture' "$R/q-$Q-rd.log"
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\$Q-passes.txt" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  head -80 "$R/$Q-passes.txt"
fi
echo $Q done
