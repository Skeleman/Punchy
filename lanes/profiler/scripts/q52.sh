#!/bin/bash
# Job 24 (refactor 1.1): population sweep. Profile build of main 01cb543 (-wildcap, PR #196), -place meadow -time 12,
# -gfx Low, uncapped, 1920x1080 window. Per N in 180 500 1000 2000 and two brain modes:
#   near = -nobrainlod (everything thinks every frame); def = today's brain LOD (80 m, every 4th frame: about half the
#   ring already skips at N=500); far = -brainlod 20,4 (most of the ring thinks on its turn only).
# A session: a 70 s fill line, then 7 lines of 3 s (-perflog prints perf / perf2 per line: ~21 s sampled), -gclog.
# Round 1 of def is captured from launch (the editor keeps the last 2000 frames = the sample) and read by SysMs; round 2 runs
# without the profiler, in reverse order.  "q52.sh dry" = one uncaptured N=500 def session.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=01cb543; S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q52 population sweep (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev Profile-$K || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q52logs"; mkdir -p "$L" "$R/q52shots"
# -perflog and -brainlod are shot-line flags (read per line), so every line carries them.
list() { { echo "-place meadow -time 12 -wait 70 -perflog $1 -shot fill"; for i in 1 2 3 4 5 6 7; do echo "-wait 3 -perflog $1 -shot s$i"; done; } > "$R/q52list-$2.txt"; }
list "" def; list "-brainlod 20,4" far; list "-nobrainlod" near
sysms() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] && cp "$R/$1-sys.txt" "$L/"
}
one() { # n mode round capture(0/1)
  local tag=q52-$1-$2-$3 extra=""
  mkdir -p "$R/q52shots/$tag"
  [ $4 = 1 ] && { rm -f "$R/q-$tag.raw"; extra="$extra -profiler-enable -profiler-maxusedmemory 268435456 -profiler-log-file $W/q-$tag.raw"; }
  fixed Profile-$K || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 600 ./Punchy.exe -logFile "$W/q-$tag.log" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 \
      -shotlist "$W/q52list-$2.txt" -shotdir "$W/q52shots/$tag" -quit -perflog -gclog -gfx Low -fpscap 0 -wildcap $1 $extra >/dev/null 2>&1 )
  [ $? -eq 124 ] && log "$tag: TIMED OUT"
  strays >/dev/null
  cp "$R/q-$tag.log" "$L/"
  grep -q 'Crash!!!\|Received signal' "$R/q-$tag.log" && { echo "CRASH in $tag"; grep -A15 'Crash!!!\|Received signal' "$R/q-$tag.log" | head -25; exit 1; }
  echo "$tag: $(grep -c "perf2 's" "$R/q-$tag.log") samples, $(grep -o 'FaunaDirector: -wildcap[^,]*' "$R/q-$tag.log" | head -1)"
  [ $4 = 1 ] && sysms $tag
}
if [ "$1" = dry ]; then one 500 def 0 0; grep -E "perf2? 's1'|ReaderTimes|readers" "$R/q-q52-500-def-0.log" | head -5 | cut -c1-1500; exit 0; fi
git -C "$P" checkout -q --detach $K   # SysMs opens this project: keep it on the build's commit, not a reimport away
ORDER="180-near 180-def 180-far 500-near 500-def 500-far 1000-near 1000-def 1000-far 2000-near 2000-def 2000-far"
for x in $ORDER; do c=0; [ ${x#*-} = def ] && c=1; one ${x%-*} ${x#*-} 1 $c; done
for x in $(echo $ORDER | tr ' ' '\n' | tac); do one ${x%-*} ${x#*-} 2 0; done
echo q52 done
