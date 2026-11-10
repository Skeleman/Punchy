#!/bin/bash
# Job 44: W-129 streaming spikes in a player build. Read only. Main at the start, Profile build, 1920x1080, -gfx High,
# noon, Clear, uncapped, -weatherseed 4, island v3 (the default), each run a fresh launch, other editors closed.
#   walk:  the shrub walk, z=1324, x -1236 to 164 (1400 m) at 30 m/s on the ground (-benchline, Punchy b/bench-stress
#          2018d2fd), 3 times.
#   glide: z=-1200, x -3000 to 3000 (6 km) at 60 m/s, 40 m over the ground or the sea, 2 times.
# Each run twice, alternating: plain (its numbers; -hitchms 50 logs every frame over 50 ms with the player's position),
# then with the Profiler recording the measured part (-profilestop line), analysed by BProfileTools.Analyze -over 50:
# for every main-thread frame over 50 ms, the top samples by self and inclusive time.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=$(git -C "$P" rev-parse --short=7 origin/main); O=Profile-${K}; Q=q74
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q W-129 walk/glide $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
echo "main $K"
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L"
WX="-benchstops line -benchweather Clear -benchhour 12 -fpscap 0 -gfx High -hitchms 50"
WALK="-benchline -1236,1324,164,1324,30,0.1"
GLIDE="-benchline -3000,-1200,3000,-1200,60,40"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
freeram() { powershell -NoProfile -c "[math]::Floor((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB)" | tr -d '\r'; }
summary() { # log: the stop line, the over-50/over-100 counts and the worst hitch with its position
  local f=$1
  echo "  $(grep -h 'BenchRoute: line: avg' "$f" | cut -c1-160)"
  echo "  over 50: $(grep -c 'BenchRoute: hitch' "$f"), over 100: $(grep -E 'BenchRoute: hitch ([0-9]{4,}|[1-9][0-9]{2}) ms' "$f" | wc -l)," \
       "worst: $(grep -h 'BenchRoute: hitch' "$f" | sort -t' ' -k3,3nr | head -1 | cut -c1-120)"
  grep -h ' s frame at ' "$f" | cut -c1-120 | sed 's/^/  STALL /'
}
game() { # name args...: one plain run
  local n=$1; shift
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 1 $WX "$@"
  check "$R/q-$Q-$n-cs100-1.log"
  echo "$(date +%T) $n"; summary "$R/q-$Q-$n-cs100-1.log"
}
prof() { # name args...: one run with the Profiler on the measured part, then the -over analysis into results/q74-<name>.txt
  local n=$1 tag=$Q-$1; shift
  rm -f "$R/q-$tag.raw"
  RUNTIMEOUT=900 BENCHW=1920 BENCHH=1080 run $O $tag $WX "$@" -profilestop line "$W/q-$tag.raw"
  check "$R/q-$tag.log"
  echo "$(date +%T) $n (profiled)"; summary "$R/q-$tag.log"
  [ -f "$R/q-$tag.raw" ] || { echo "  no capture"; return; }
  echo "  raw $(du -m "$R/q-$tag.raw" | cut -f1) MB"
  local i; for i in $(seq 1 30); do [ "$(freeram)" -ge 6 ] && break; sleep 20; done
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 2400 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze \
      -raws "$WW\\q-$tag.raw" -out "$WW\\$tag.txt" -over 50 -logFile "$W/analyze-$tag.log" )
  echo "  analyze rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  strays >/dev/null
  [ -f "$R/$tag.txt" ] && { rm -f "$R/q-$tag.raw"; echo "  $(grep -c '^OVER' "$R/$tag.txt") frames over 50 ms in the capture"; }
}
rm -f "$R"/$Q-*.csv
for r in 1 2 3; do game walk$r $WALK; prof walk$r-p $WALK; done
for r in 1 2; do game glide$r $GLIDE; prof glide$r-p $GLIDE; done
echo $Q done
