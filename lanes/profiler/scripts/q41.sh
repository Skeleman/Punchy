#!/bin/bash
# Systems audit finding 11 (supervisor, 11-06): one v3 sweep, uncapped, at 3 spots: a forest with a colossus nearby
# (150 m from the Ember Forest CinderSnail, else the salt crystal), a fishing village, and a cave mouth (route stop
# caveapproach: walks from 400 m out to 100 m from the biggest cave). Main adb11e0 (builds from q40).
# CPU: Profile build capture of each stop (-profilestop), 2 rounds, through SysMs (ms per system, GC per frame, render
# counters). GPU: Bench build, each system's shaders switched off in turn (-benchnoshader ...), gpu ms against plain,
# 2 rounds with the order reversed.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=adb11e0
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q41 v3 per-system sweep (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q41logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
PROBE="$R/q40logs/probe.log"
[ -f "$PROBE" ] || { run Bench-${K} q41-probe -island v3 -benchstops here -fpscap 0; PROBE="$R/q-q41-probe.log"; }
grep -o "StoneColossi: [A-Za-z]* placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*)" "$PROBE" | sort -u
col() { grep -o "StoneColossi: $1 placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*)" "$PROBE" | head -1 | sed 's/.*(\([-0-9.]*\), [-0-9.]*, \([-0-9.]*\))/\1 \2/'; }
C=$(col CinderSnail); CN=CinderSnail; [ -z "$C" ] && { C=$(col SaltCrystal); CN=SaltCrystal; }
FOREST=$(echo $C | awk '{ printf "%.0f,%.0f", $1 + 150, $2 }')
VILLAGE=$(grep -h "fishing village 1 at" "$PROBE" | head -1 | sed 's/.*village 1 at \([-0-9]*\),\([-0-9]*\).*/\1,\2/')
echo "forest: 150 m east of $CN ($C) -> $FOREST; village: fishing village 1 at $VILLAGE; cave: caveapproach"
[ -n "$FOREST" ] && [ -n "$VILLAGE" ] || { echo "spots not found"; exit 1; }
spot() { case $1 in
  forest) echo "-island v3 -goto $FOREST -benchstops herewarm,here -benchlong here 20";;
  village) echo "-island v3 -goto $VILLAGE -benchstops herewarm,here -benchlong here 20";;
  cave) echo "-island v3 -benchstops caveapproach";; esac; }
stopname() { [ $1 = cave ] && echo caveapproach || echo here; }
H="-fpscap 0 -hitchms 50"
# ---- CPU: Profile captures
sysms() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] || { echo "SysMs failed for $1:"; grep -m5 "error CS\|Exception" "$W/sysms.log"; }
}
for RD in 1 2; do
  for SP in forest village cave; do
    T=prof41-$SP-$RD; ST=$(stopname $SP); rm -f "$R/q-$T.raw"
    run Profile-${K}p $T $(spot $SP) $H -profilestop $ST "$W/q-$T.raw"
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "$SP cpu $RD | $(grep -h "BenchRoute: $ST: avg" $L/$T.log | sed 's/BenchRoute: [a-z]*: //' | cut -c1-260)"
    [ -f "$R/q-$T.raw" ] && sysms $T || echo "no capture $T"
    [ -f "$R/$T-sys.txt" ] && cp "$R/$T-sys.txt" "$L/" && cat "$R/$T-sys.txt"
  done
done
# ---- GPU: Bench ablations
variant() { case $1 in
  plain) echo "";; noshadows) echo "-benchnoshadows";; nowater) echo "-benchnoshader Water";;
  noflora) echo "-benchnoshader Flora";; nocreatures) echo "-benchnocreatures";;
  nocolossi) echo "-benchnoshader Colossus,FarShadow";;
  noambient) echo "-benchnoshader Particle,Ambient,Fireflies,Butterflies,Critter";; esac; }
VS="plain noshadows nowater noflora nocreatures nocolossi noambient"
RVS=$(echo $VS | tr ' ' '\n' | tac | tr '\n' ' ')
for RD in 1 2; do
  [ $RD = 1 ] && ORDER="$VS" || ORDER="$RVS"
  for SP in forest village cave; do
    for V in $ORDER; do
      T=q41-$SP-$V-$RD; ST=$(stopname $SP)
      run Bench-${K} $T $(spot $SP) $(variant $V) $H
      cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
      echo "$SP $V $RD | $(grep -h "BenchRoute: $ST: avg" $L/$T.log | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms' | tr '\n' ' ') | $(grep -h 'BenchRoute: off\|noshader' $L/$T.log | head -2 | cut -c1-100 | tr '\n' ' ') | hitches $(grep -c 'BenchRoute: hitch' $L/$T.log)"
    done
  done
done
log "q41 done"
