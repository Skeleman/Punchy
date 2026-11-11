#!/bin/bash
# Job 49: R-12 cloud warp and rain (#251). Read only. Three arms: origin/main 60edfacb plain (main), the same build with
# -skyoff 1024 (the cloud maps read straight, i.e. warp off: warpoff), and 739cdff7 (before R-12: before).
# Profile builds (rebuilt with b/bench-stress a0e26c92 for -benchweatherat), 1920x1080, -gfx High, uncapped,
# -weatherseed 4, a fresh game per run, 4 rounds (order rotated each round). Measured by the bench's "here" stop (20 s,
# after its 10 s settle), camera pinned by -shotcam where the card gives one:
#   stormnight / stormnoon: (a) -goto 1489,-897, shotcam 1489,88,-897 -> 1889,98,-897, Storm forced at (5489,-897), 22:00 / 12:00
#   shadow: (c) shotcam 1489,30,-897 -> 1889,25,-600, Storm forced at (1700,-1900), 12:00
#   rain:   Rain forced over the player at -goto 1489,-897, noon, eye level (stand-in for the lone-oak spot: its
#           coordinates (w133-flora.txt) aren't in the repo or the coordination files)
# Shots on round 1. Pass: <= +0.1 ms GPU at every spot vs before.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=60edfac; KB=739cdff; Q=q87
OA=Profile-${KA}w; OB=Profile-${KB}w
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 49 cloud warp and rain (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KA BuildDev $OA || { echo "build $OA failed"; exit 1; }
build $KB BuildDev $OB || { echo "build $OB failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
spot() { case $1 in
  stormnight) echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 22";;
  stormnoon)  echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 12";;
  shadow)     echo "-goto 1489,-897 -shotcam 1489,30,-897,1889,25,-600 -benchweather Storm -benchweatherat 1700,-1900 -benchhour 12";;
  rain)       echo "-goto 1489,-897 -benchweather Rain -benchhour 12";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 -benchstops here "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: here: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') | $(grep -h 'BenchRoute: weather' "$f" | tail -1 | cut -c13-90) $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rm -f "$R"/$Q-*.csv
for s in stormnight stormnoon shadow rain; do
  for r in 1 2 3 4; do
    case $r in 1) list="main warpoff before";; 2) list="before warpoff main";; 3) list="warpoff main before";; 4) list="before main warpoff";; esac
    for a in $list; do
      case $a in main) game $OA $s-$a $r $(spot $s);; warpoff) game $OA $s-$a $r $(spot $s) -skyoff 1024;; before) game $OB $s-$a $r $(spot $s);; esac
    done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
