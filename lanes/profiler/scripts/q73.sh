#!/bin/bash
# Job 43: water, SSAO, glow slots and shadow distance on the laptop (11-09 audit, sections 3, 4, 5, 8). Read only. Main
# 2644b295, Profile build, 1920x1080, -weatherseed 4, noon, Clear, uncapped, -gfx High then Medium, a fresh game per run.
# Round 1 in list order, round 2 reversed.
#   cal:    the Beach place (the game's own finder: out from the centre to where the ground meets the sea, facing out) at
#           camera pitches -10..60, with shots, to pick the 10% / 50% / 90% water views. Writes nothing; I pick the
#           pitches into results/q73-pitches.txt ("w10 <pitch>", "w50 <pitch>", "w90 <pitch>").
#   water:  those three views x -waterquality High / Medium / Low x -gfx High / Medium.
#   shadow: the dense forest (spot 1740,3355 facing 180) and the big town at noon, -gfxset shadows=1.0 / 1.6 / 2.0 over
#           the preset (sun shadow distance 50 / 80 / 100 m: 50 m x Shadows, clamped at 2, so 120 m can't be set; 1.0 casts
#           3 cascades, 1.6 and 2.0 four), plus the preset's own (High 1.4 = 70 m, Medium 1.0 = 50 m).
#   rd:     RenderDoc at -gfx High: each water view at each water quality (9), forest and town at each shadow setting
#           (8; the preset ones give SSAO's passes too); rdpasses.py to depth 6 with the top draws.
# SSAO and glow slots have no run-time switch on main, so they get no on/off runs (the card says to skip, not add code).
# "q73.sh cal|water|shadow|rd|rdpass"; no argument runs water, shadow and rd.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=2644b29; O=Profile-${K}m4; Q=q73
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q water/ssao/shadows $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
[ "$1" = rdpass ] || { build $K BuildDev $O || { echo "build $O failed"; exit 1; }; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="$R/rd73"; mkdir -p "$L" "$RD" "$R/$Q-shots"
ONLY=$1; part() { if [ -z "$ONLY" ]; then [ "$1" != cal ]; else [ "$ONLY" = "$1" ]; fi; }
WX="-benchweather Clear -benchhour 12 -fpscap 0"
BEACH="-benchstops place -benchplace Beach"
FOREST="-benchstops spot -benchspot 1740,3355,180"
TOWN="-benchstops bigtown"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
pitch() { awk -v w=$1 '$1 == w { print $2 }' "$R/$Q-pitches.txt" 2>/dev/null; }
rev() { echo $* | tr ' ' '\n' | tac | tr '\n' ' '; }
game() { # name round args...: one game, its numbers into results/<name>.csv; shots on round 1
  local n=$1 r=$2; shift 2
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  RUNTIMEOUT=1500 BENCHW=1920 BENCHH=1080 runcsv $O $n $K s100 $r $WX "$@" $shot
  check "$R/q-$n-cs100-$r.log"
  echo "$(date +%T) $n r$r: $(grep -h '^Graphics: \|BenchRoute: place .* at \| s frame at ' "$R/q-$n-cs100-$r.log" | sort -u | head -3 | cut -c1-110 | tr '\n' ' ')"
}
tables() { for f in "$R"/$Q-$1-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done; }
if part cal; then
  for p in -10 0 10 20 35 60; do game $Q-cal-p$p 1 -gfx High $BEACH -benchview place,$p; done
  grep -h 'BenchRoute: view\|place' "$L"/q-$Q-cal-*.log | grep -i 'beach\|view' | sort -u | head -8
fi
if part water; then
  [ -n "$(pitch w90)" ] || { echo "no $Q-pitches.txt: run cal first"; exit 1; }
  rm -f "$R"/$Q-water-*.csv
  V=; for g in High Medium; do for w in w10 w50 w90; do for q in High Medium Low; do V="$V $g-$w-$q"; done; done; done
  for r in 1 2; do
    list=$V; [ $r = 2 ] && list=$(rev $V)
    for v in $list; do
      IFS=- read g w q <<< "$v"
      game $Q-water-$v $r -gfx $g $BEACH -benchview place,$(pitch $w) -waterquality $q
    done
  done
  tables water
fi
sh() { case $1 in def) ;; *) echo -gfxset shadows=$1 ;; esac; }
if part shadow; then
  rm -f "$R"/$Q-shadow-*.csv
  V=; for g in High Medium; do for s in forest town; do for d in def 1.0 1.6 2.0; do V="$V $g-$s-$d"; done; done; done
  for r in 1 2; do
    list=$V; [ $r = 2 ] && list=$(rev $V)
    for v in $list; do
      IFS=- read g s d <<< "$v"
      a=$FOREST; [ $s = town ] && a=$TOWN
      game $Q-shadow-$v $r -gfx $g $a $(sh $d)
    done
  done
  tables shadow
fi
rdgame() { # name stop args...: one RenderDoc capture at -gfx High
  local n=$1 st=$2; shift 2
  fixed $O || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 1800 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 12 \
      $(screen Bench 1920 1080) -gfx High $WX "$@" -renderdocstop $st 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
if part rd; then
  [ -n "$(pitch w90)" ] || { echo "no $Q-pitches.txt: run cal first"; exit 1; }
  rm -f "$RD"/*.rdc
  for w in w10 w50 w90; do for q in High Medium Low; do
    rdgame water-$w-$q place $BEACH -benchview place,$(pitch $w) -waterquality $q
  done; done
  for s in forest town; do for d in def 1.0 1.6 2.0; do
    a=$FOREST; st=spot; [ $s = town ] && { a=$TOWN; st=bigtown; }
    rdgame shadow-$s-$d $st $a $(sh $d)
  done; done
fi
if part rd || [ "$ONLY" = rdpass ]; then
  CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=15 timeout 5400 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null
  grep -c '^== ' "$R/$Q-passes.txt"
fi
echo $Q done
