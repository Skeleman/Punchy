#!/bin/bash
# Job 28 follow-up: SetPass calls, draws and tris per variant (the Profile build's render counters read invalid), from one
# Profiler capture per variant and stop (-profilestop: the stop's measured part only), read by SysMs on the build's commit.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5fa346f; OUT=Profile-${K}h; S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q56b flora captures (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
keepawake
L="$R/q56logs"
sysms() { # tag commit
  strays >/dev/null
  git -C "$P" checkout -q --detach $2
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] && cp "$R/$1-sys.txt" "$L/"
}
cap() { # variant stop args...
  local v=$1 stop=$2 tag=q56b-$1-$2; shift 2
  rm -f "$R/q-$tag.raw"
  BENCHW=1920 BENCHH=1080 run $OUT $tag "$@" -fpscap 0 -profilestop $stop "$W/q-$tag.raw"
  grep -q 'Crash!!!\|Received signal' "$R/q-$tag.log" && { echo "CRASH in q-$tag.log"; exit 1; }
  cp "$R/q-$tag.log" "$L/"; sysms $tag $K
  echo "== $tag: $(grep -E 'frame ms mean' "$R/$tag-sys.txt")"
  grep -iE 'setpass|draw call|triangles|batches' "$R/$tag-sys.txt" | tr '	' ' ' | head -6
}
for stop in bigtown place; do
  STOPS="-benchstops $stop -benchplace Tors"
  cap static $stop $STOPS
  cap anim $stop $STOPS -nostaticflora
done
echo q56b done
