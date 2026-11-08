#!/bin/bash
# Job 31 (refactor 3.5c): close-up shadow range 25 / 50 / 75 m (-shadownear), static vs animated (-nostaticflora), on
# refactor/flora-static 5fa346f3 (rebuilt: the runner's bigtown stop now takes -benchview). Profile build, 1920x1080,
# render scale 1, -gfx High, -weatherseed 4, uncapped, noon. Spots: bigtown and job 25's dense forest (spot).
#   runs:  2 rounds, the second in reverse order; each round also has one -benchnoshadows run per variant (the URP
#          shadow pass = GPU minus GPU without shadows; the range doesn't matter with shadows off).
#   caps:  one Profiler capture per range and variant at each spot (shadow casters, URP shadow CPU ms; SysMs).
#   yaws:  bigtown at 17:00, static, 25 m, shots at yaw 0/90/180/270 (to find a view along a street).
#   shots: bigtown at 17:00, static, a shot per range at each of the 4 yaws (pick the street view from them).
# "q59.sh runs|caps|yaws|shots".
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5fa346f; OUT=Profile-${K}k; Q=q59; S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q shadow range $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $OUT || { echo "profile build failed"; exit 1; }
keepawake
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
STOPS="-benchstops bigtown,spot -benchspot 1740,3355,180"
COMMON="-gfx High -fpscap 0"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
go() { # variant round extra...
  local v=$1 r=$2; shift 2
  BENCHW=1920 BENCHH=1080 runcsv $OUT $Q-$v $K s100 $r $STOPS $COMMON "$@"
  check "$R/q-$Q-$v-cs100-$r.log"
}
flag() { case $1 in anim*) echo -nostaticflora ;; esac; }
sysms() { # tag
  strays >/dev/null
  git -C "$P" checkout -q --detach $K
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  git -C "$P" checkout -q $HOME_BRANCH
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] && cp "$R/$1-sys.txt" "$L/"
}
case $1 in
runs)
  rm -f "$R"/$Q-*.csv "$L"/q-$Q-*.log
  ORDER="static-25 anim-25 static-50 anim-50 static-75 anim-75 static-noshadow anim-noshadow"
  for r in 1 2; do
    [ $r = 2 ] && ORDER=$(echo $ORDER | tr ' ' '\n' | tac | tr '\n' ' ')
    for v in $ORDER; do
      case $v in *noshadow) go $v $r $(flag $v) -benchnoshadows ;; *) go $v $r $(flag $v) -shadownear ${v#*-} ;; esac
    done
  done
  for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done ;;
caps)
  for stop in bigtown spot; do
    for v in static-25 anim-25 static-50 anim-50 static-75 anim-75; do
      tag=q59b-$v-$stop; rm -f "$R/q-$tag.raw"
      BENCHW=1920 BENCHH=1080 run $OUT $tag $STOPS $COMMON $(flag $v) -shadownear ${v#*-} -profilestop $stop "$W/q-$tag.raw"
      check "$R/q-$tag.log"; sysms $tag
      echo "== $tag: $(grep -E 'frame ms mean' "$R/$tag-sys.txt")"
      grep -iE 'shadow' "$R/$tag-sys.txt" | tr '\t' ' ' | head -6
    done
  done ;;
yaws)
  for y in 0 90 180 270; do
    BENCHW=1920 BENCHH=1080 run $OUT $Q-yaw$y -benchstops bigtown $COMMON -benchhour 17 -benchview $y,8 -benchshots "$W/$Q-shots/yaw$y"
    check "$R/q-$Q-yaw$y.log"
  done ;;
shots)   # every yaw and range (the yaw step's shots were lost: -benchshots needs its folder made first)
  for y in 0 90 180 270; do
    for n in 25 50 75; do
      mkdir -p "$R/$Q-shots/y$y-near$n"
      BENCHW=1920 BENCHH=1080 run $OUT $Q-shot-y$y-$n -benchstops bigtown $COMMON -benchhour 17 -benchview $y,8 -shadownear $n -benchshots "$W/$Q-shots/y$y-near$n"
      check "$R/q-$Q-shot-y$y-$n.log"
    done
  done
  ls -R "$R/$Q-shots" | head -30 ;;
esac
echo $Q $1 done
