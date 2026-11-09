#!/bin/bash
# Job 41: what the Summit stall waits on (jobs 37-39). Main 0e964452, Profile build, job 38's Summit setup (facing out,
# pitched up 10, 1920x1080, -gfx High, -weatherseed 4, noon, uncapped), -lowres half, plain weather. 24 fresh games,
# 12 on the default API (D3D11 on this build: ProjectSettings lists D3D11 then D3D12) and 12 with -force-d3d12,
# alternating, round 2 starting on the other API. freezesample.sh snapshots every stall (freezestall.ps1: each thread's
# name, state, wait reason and start module); the stall list comes from the games' " s frame at " lines.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=0e96445; O=Profile-${K}m4; Q=q71
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q Summit stall $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L"
SUMMIT="-benchstops place -benchplace Summit -benchview place,-10"
COMMON="-gfx High -fpscap 0 -lowres half"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
rm -f "$R"/$Q-*.csv "$L"/q-$Q-*.log
for r in 1 2; do
  for i in 1 2 3 4 5 6; do
    a="d3d11 d3d12"; [ $r = 2 ] && a="d3d12 d3d11"
    for api in $a; do
      f=; [ $api = d3d12 ] && f=-force-d3d12
      n=$Q-$api-g$i
      RUNTIMEOUT=2400 BENCHW=1920 BENCHH=1080 runcsv $O $n $K s100 $r $COMMON $SUMMIT $f
      lg="$R/q-$n-cs100-$r.log"; check "$lg"
      echo "$(date +%T) $api r$r g$i: $(grep -h 'Version:  Direct3D' "$lg" | head -1 | tr -s ' ') $(grep -h ' s frame at ' "$lg" | head -2 | tr '\n' ' ')"
    done
  done
done
echo "== stalls (frames over 30 s) per API"
for api in d3d11 d3d12; do
  n=$(ls "$L"/q-$Q-$api-*.log | wc -l)
  s=$(grep -l ' s frame at ' "$L"/q-$Q-$api-*.log 2>/dev/null | xargs -r grep -h ' s frame at ' | awk '{for(i=1;i<=NF;i++) if($(i+1)=="s" && $(i+2)=="frame" && $i+0>=30) c++} END{print c+0}')
  echo "$api: $s stalls in $n games"
done
grep -h ' s frame at ' "$L"/q-$Q-*.log | cut -c1-160
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
echo $Q done
