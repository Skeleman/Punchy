#!/bin/bash
# Job 64 (Zach 11-12): where start-up time and start-up hitches go, and whether tiered culling (#270) would help.
# origin/main at start, one Profile build, 1920x1080, High, uncapped, noon, Clear, -body default. Per start (spawn, town,
# forest): one warm-up launch (discarded; makes sure the island cache holds this build's island), three cache-hit
# launches and one -freshisland launch. Every game: the bench's "here" stop measuring 60 s after its 10 s settle (the
# settled reference). The first hit and the fresh launch of each start also record the Profiler from process start
# (-profiler-log-file), analysed by BProfileTools.Analyze: every main-thread frame over 35 ms with its top samples,
# and the whole frame list (-frames) for the time line and the settle time.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
Q=q104
git -C "$P" fetch -q origin
K=$(git -C "$P" rev-parse --short=7 origin/main); O=Profile-$K
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 64 start-up on $K (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
echo "main is $K"
build $K BuildDev $O || { echo "build failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L"
start() { case $1 in spawn) echo "";; town) echo "-place HumanVillage";; forest) echo "-place OakGrove";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
marks() { # log: the start-up time line from the game's own lines (seconds since process start)
  local f=$1
  echo "island $(grep -h '\[island\] cache' "$f" | head -1 | grep -o 'hit\|miss\|stale' | head -1) ready $(grep -h '\[island\].*ready at' "$f" | head -1 | grep -o 'ready at [0-9.]* s' | grep -o '[0-9.]*') s;" \
       "stream world ready $(grep -h '\[stream\] world ready' "$f" | head -1 | grep -o 'at [0-9.]* s' | grep -o '[0-9.]*') s;" \
       "player $(grep -h 'BenchRoute: world ready' "$f" | head -1 | grep -o 'at [0-9.]* s' | grep -o '[0-9.]*') s;" \
       "$(grep -h 'BenchRoute: here: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms\|1% low [0-9.]* fps' | tr '\n' ' ')"
}
game() { # start name round [profile] [extra...]
  local s=$1 n=$2 r=$3 prof=$4; shift 4
  local tag=$Q-$s-$n-cs100-$r pf=()
  [ "$prof" = 1 ] && pf=(-profiler-enable -profiler-log-file "$W/q-$tag.raw" -profiler-maxusedmemory 1073741824)
  BENCHW=1920 BENCHH=1080 RUNTIMEOUT=300 runcsv $O $Q-$s-$n $K s100 $r -gfx High -fpscap 0 -benchhour 12 -benchweather Clear \
    -benchstops here -benchlong here 60 $(start $s) "$@" "${pf[@]}"
  local f="$R/q-$tag.log"; check "$f"
  echo "$(date +%T) $s $n r$r: $(marks "$f")"
}
analyze() { # tag: Analyze -over 35 and -frames on q-<tag>.raw (the editor reads it; one Unity at a time)
  local tag=$1
  [ -f "$R/q-$tag.raw" ] || { echo "  $tag: no capture"; return; }
  echo "  $tag raw $(du -m "$R/q-$tag.raw" | cut -f1) MB"
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze \
      -raws "$WW\\q-$tag.raw" -out "$WW\\$tag.txt" -over 35 -frames "$WW\\$tag-frames.csv" -logFile "$W/analyze-$tag.log" )
  echo "  analyze $tag rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  strays >/dev/null
  [ -f "$R/$tag.txt" ] && { rm -f "$R/q-$tag.raw"; echo "  $(grep -c '^OVER' "$R/$tag.txt") frames over 35 ms"; }
}
rm -f "$R"/$Q-*.csv
for s in spawn town forest; do
  game $s warm 1 0
  game $s hit 1 1
  game $s hit 2 0
  game $s hit 3 0
  game $s fresh 1 1 -freshisland
done
git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null
for s in spawn town forest; do analyze $Q-$s-hit-cs100-1; analyze $Q-$s-fresh-cs100-1; done
python "$S/startup.py" "$R" $Q > "$R/$Q-startup.txt"; cat "$R/$Q-startup.txt"
echo $Q done
