#!/bin/bash
# Flora breakdown at the v3 cave approach (supervisor, 11-06; q41 showed flora 2.0 ms GPU there). Profile build of
# adb11e0 with the runner's new -benchnoprime / -benchrenderscale switches (out dir ...p2 so it rebuilds), uncapped.
# Each pass is split by switching it off with and without flora (-benchnoshader Flora):
#   shadows (-benchnoshadows), depth prepass (-benchnoprime: the PC renderer forces depth priming), and pixels
#   (-benchrenderscale 0.5: a quarter of the pixels, same vertices; flora's cost shrinking with it = pixel bound).
# 2 rounds, order reversed. Round 1 plain and noflora also captured through SysMs for triangles/instanced draws/SetPass.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=adb11e0
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q44 flora pass breakdown (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}p2 || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q44logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
F="-benchnoshader Flora"
variant() { case $1 in
  plain) echo "";; noflora) echo "$F";;
  noshadows) echo "-benchnoshadows";; noflora-noshadows) echo "$F -benchnoshadows";;
  noprime) echo "-benchnoprime";; noflora-noprime) echo "$F -benchnoprime";;
  half) echo "-benchrenderscale 0.5";; noflora-half) echo "$F -benchrenderscale 0.5";; esac; }
VS="plain noflora noshadows noflora-noshadows noprime noflora-noprime half noflora-half"
RVS=$(echo $VS | tr ' ' '\n' | tac | tr '\n' ' ')
sysms() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/SysMs.cs.txt" "$P/Assets/Game/Scripts/Editor/SysMs.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod SysMs.Dump -raw "$WW\\q-$1.raw" -out "$W/$1-sys.txt" -logFile "$W/sysms.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/SysMs.cs" "$P/Assets/Game/Scripts/Editor/SysMs.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  [ -f "$R/$1-sys.txt" ] && cp "$R/$1-sys.txt" "$L/" && sed -n '/== counters/,/== main-thread/p' "$R/$1-sys.txt"
}
for RD in 1 2; do
  [ $RD = 1 ] && ORDER="$VS" || ORDER="$RVS"
  for V in $ORDER; do
    T=prof44-$V-$RD; X=""
    if [ $RD = 1 ] && { [ $V = plain ] || [ $V = noflora ]; }; then rm -f "$R/q-$T.raw"; X="-profilestop caveapproach $W/q-$T.raw"; fi
    run Profile-${K}p2 $T -island v3 -benchstops caveapproach $(variant $V) -fpscap 0 -hitchms 50 $X
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "$V $RD | $(grep -h "BenchRoute: caveapproach: avg" $L/$T.log | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms\|render thread [0-9.]* ms' | tr '\n' ' ') | $(grep -h 'BenchRoute: off\|BenchRoute: render scale\|BenchRoute: depth priming' $L/$T.log | sort -u | cut -c1-80 | tr '\n' ' ')"
    [ -n "$X" ] && { [ -f "$R/q-$T.raw" ] && sysms $T || echo "no capture $T"; }
  done
done
log "q44 done"
