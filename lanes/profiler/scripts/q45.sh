#!/bin/bash
# Creature render breakdown at the v3 forest spot (supervisor, 11-06; q41: 0.85 ms GPU, 0.8-1.5 ms CPU). Profile build
# adb11e0p2 (q44's, with -benchnoprime / -benchrenderscale), 150 m east of the CinderSnail (2349,-2575), uncapped.
# Creatures off (-benchnocreatures: neither posed nor drawn) against plain, alone and with camera shadows off and at
# render scale 0.5, 2 rounds, order reversed. Every run with -renderpasslog (creatures, kept, segments, per-pass CPU).
# Round 1 plain and nocreatures captured through SysMs for counters.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=adb11e0
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q45 creature render breakdown (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p2 || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q45logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
C="-benchnocreatures"
variant() { case $1 in
  plain) echo "";; nocreatures) echo "$C";;
  noshadows) echo "-benchnoshadows";; nocreatures-noshadows) echo "$C -benchnoshadows";;
  half) echo "-benchrenderscale 0.5";; nocreatures-half) echo "$C -benchrenderscale 0.5";; esac; }
VS="plain nocreatures noshadows nocreatures-noshadows half nocreatures-half"
RVS=$(echo $VS | tr ' ' '\n' | tac | tr '\n' ' ')
sysms() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] && cp "$R/$1-sys.txt" "$L/" && sed -n '/== systems/,/== main-thread/p' "$R/$1-sys.txt"
}
for RD in 1 2; do
  [ $RD = 1 ] && ORDER="$VS" || ORDER="$RVS"
  for V in $ORDER; do
    T=prof45-$V-$RD; X=""
    if [ $RD = 1 ] && { [ $V = plain ] || [ $V = nocreatures ]; }; then rm -f "$R/q-$T.raw"; X="-profilestop here $W/q-$T.raw"; fi
    run Profile-${K}p2 $T -island v3 -goto 2349,-2575 -benchstops herewarm,here -benchlong here 20 $(variant $V) -renderpasslog -fpscap 0 -hitchms 50 $X
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "$V $RD | $(grep -h "BenchRoute: here: avg" $L/$T.log | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms\|render thread [0-9.]* ms' | tr '\n' ' ')"
    grep -h "CreatureRenderSystem passes" $L/$T.log | tail -2 | cut -c1-400
    [ -n "$X" ] && { [ -f "$R/q-$T.raw" ] && sysms $T || echo "no capture $T"; }
  done
done
log "q45 done"
