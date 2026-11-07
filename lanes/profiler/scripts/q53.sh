#!/bin/bash
# Job 25 (refactor 1.2): stress scenes on the job 24 baseline (main 01cb543 = e5c88fb4 game code), Profile build, uncapped.
# Scenes: bigtown (the settlement with the most buildings), spot = dense forest (under the OakGrove canopy), firesbase /
# fires (the same spot without and with 500 -benchfires flames), lake = the W-120 sea folk lake (-1624,-488) at noon, and
# under = the same with the camera pitched to -35 (under the surface).
# 1080p window at render scale 1 and 1.333 (= 1440p pixels: the laptop desktop clamps bigger windows), ABBA over 2 rounds.
# -benchflora logs the flora instances the GPU cull kept, plants vs rocks/kit buildings. Then one SysMs capture per scene
# (-profilestop, scale 1). "q53.sh dry" = one session per set with screenshots.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=01cb543; OUT=Profile-${K}f; S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
FOREST="-benchspot 1740,3355,180"; LAKE="-benchspot -1624,-488,0"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q53 stress scenes (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q53logs"; mkdir -p "$L"
args() { # set: the route flags for one set of scenes
  case $1 in
    town)  echo "-benchstops bigtown,spot,firesbase,fires $FOREST" ;;
    lake)  echo "-benchstops spot $LAKE" ;;
    under) echo "-benchstops spot $LAKE -benchpitch -35" ;;
  esac
}
crashed() { grep -q 'Crash!!!\|Received signal' "$R/q-$1.log" && { echo "CRASH in q-$1.log"; grep -A15 'Crash!!!\|Received signal' "$R/q-$1.log" | head -25; exit 1; }; }
if [ "$1" = dry ]; then
  for set in town lake under; do
    mkdir -p "$R/q53-dry-$set"
    BENCHW=1920 BENCHH=1080 run $OUT q53-dry-$set $(args $set) -fpscap 0 -benchflora -benchshots "$W/q53-dry-$set"; crashed q53-dry-$set
    grep -E 'BenchRoute stress|BenchRoute flora|BenchRoute: [a-z]+: avg|Exception' "$R/q-q53-dry-$set.log" | cut -c1-400
    ls -la "$R/q53-dry-$set"
  done
  exit 0
fi
rm -f "$R"/q53-*.csv
go() { # set scale(100|133) round
  local rs=""; [ $2 = 133 ] && rs="-benchrenderscale 1.333"
  BENCHW=1920 BENCHH=1080 runcsv $OUT q53-$1 $K s$2 $3 $(args $1) -fpscap 0 -benchflora $rs
  crashed q53-$1-cs$2-$3; cp "$R/q-q53-$1-cs$2-$3.log" "$L/"
}
for rd in 1 2; do
  if [ $rd = 1 ]; then A=100; C=133; else A=133; C=100; fi
  for set in town lake under; do go $set $A $rd; go $set $C $rd; done
done
for set in town lake under; do echo "== $set"; table "$R/q53-$set.csv"; done
# Captures: one stop each, scale 1; SysMs reads them in a batch editor on the build's commit.
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
git -C "$P" checkout -q --detach $K   # SysMs opens this project: keep it on the build's commit
cap() { # set stop
  local tag=q53-cap-$1-$2; rm -f "$R/q-$tag.raw"
  local a="$(args $1)"; [ $1 = town ] && a="-benchstops $2 $FOREST"   # just the captured stop
  BENCHW=1920 BENCHH=1080 run $OUT $tag $a -fpscap 0 -profilestop $2 "$W/q-$tag.raw"; crashed $tag
  cp "$R/q-$tag.log" "$L/"; sysms $tag
}
cap town bigtown; cap town spot; cap town fires; cap lake spot; cap under spot
echo q53 done
