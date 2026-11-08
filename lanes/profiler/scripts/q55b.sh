#!/bin/bash
# Job 27 follow-up: LandmarkEffectSystem main-thread ms (FireSystem.Draw runs in it) from one Profiler capture per build and
# scene (-profilestop: the stop's measured part only), read by SysMs in a batch editor on that build's commit.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KA=01cb543; KB=9657300; S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q55b fire captures (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
keepawake
L="$R/q55logs"
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
cap() { # out commit tag stop args...
  local out=$1 c=$2 tag=q55b-$3-$4 stop=$4; shift 4
  rm -f "$R/q-$tag.raw"
  BENCHW=1920 BENCHH=1080 run $out $tag "$@" -fpscap 0 -profilestop $stop "$W/q-$tag.raw"
  grep -q 'Crash!!!\|Received signal' "$R/q-$tag.log" && { echo "CRASH in q-$tag.log"; exit 1; }
  cp "$R/q-$tag.log" "$L/"; sysms $tag $c
  echo "== $tag: $(grep -E 'frame ms mean' "$R/$tag-sys.txt")"
  grep -E 'LandmarkEffectSystem' "$R/$tag-sys.txt" | tr '\t' ' '
}
EMBER="-benchstops place -benchplace EmberForest: -benchview 120,5 -benchhour 22"
cap Profile-${KA}g $KA a place $EMBER
cap Profile-${KB}g $KB b place $EMBER
cap Profile-${KA}g $KA a fires -benchstops fires
cap Profile-${KB}g $KB b fires -benchstops fires
echo q55b done
