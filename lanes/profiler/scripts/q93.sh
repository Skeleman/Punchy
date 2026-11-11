#!/bin/bash
# Zach 11-11: the cloud shadow map prototype. One Profile build of Punchy b/cloud-shadow-map 88ee4c89 (origin/main
# 393a9c20 + WeatherSystem baking the clouds' shadow density round the camera every 2 frames, read once per pixel by
# PunchyCloudShadow): on (default), off (-cloudshadowmap 0: per pixel, as main), every frame (-cloudshadowmapevery 1).
# Spots: job 49's ground under the storm's shadow and the storm at noon, and the hive spot at noon on the bench's
# Partly cloudy day (cumulus shadows over the forest). 3 rounds rotated, 1920x1080, -gfx High, uncapped, -weatherseed 4,
# -body default. Shots on round 1 of every arm, to compare the shadows' look on vs off.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=88ee4c8; Q=q93
OA=Profile-${KA}
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q cloud shadow map (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KA BuildDev $OA || { echo "build $OA failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
spot() { case $1 in
  stormnight) echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 22";;
  stormnoon)  echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 12";;
  shadow)     echo "-goto 1489,-897 -shotcam 1489,30,-897,1889,25,-600 -benchweather Storm -benchweatherat 1700,-1900 -benchhour 12";;
  rain)       echo "-goto 1489,-897 -benchweather Rain -benchhour 12";;
  hive)       echo "-goto -1500,-995 -benchhour 12";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 -benchstops here "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: here: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') | $(grep -h 'BenchRoute: weather' "$f" | tail -1 | cut -c13-90) $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rm -f "$R"/$Q-*.csv
V="on off every1"
rot() { local r=$1; shift; local a=("$@") n=$#; local i; for ((i = 0; i < n; i++)); do echo -n "${a[$(((i + r - 1) % n))]} "; done; }
for s in shadow stormnoon hive; do
  for r in 1 2 3; do
    for a in $(rot $r $V); do
      case $a in on) game $OA $s-$a $r $(spot $s);; off) game $OA $s-$a $r $(spot $s) -cloudshadowmap 0;;
        every1) game $OA $s-$a $r $(spot $s) -cloudshadowmapevery 1;; esac
    done
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
