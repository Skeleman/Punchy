#!/bin/bash
# Audit finding 11 follow-ups (supervisor, 11-06), main adb11e0 (same builds as q41), v3, uncapped.
# A: the hidden water cost at v3 fishing village 1 (-2151,3731): plain, -benchnoshader Water, -benchnocopies (no opaque
#    colour / depth texture copies), both; Bench build, 3 rounds, order rotated.
# B: GC.Alloc callers (top 10 by bytes): Profile captures of the 3 q41 spots through GcCallers (nearest profiler sample
#    above each allocation), then one deep-profiling capture at the village (BuildDeep + -deepprofiling, 10 s) for the
#    real call sites.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=adb11e0
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q42 water copies + GC callers (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K} || { echo "build failed"; exit 1; }
build $K BuildDev Profile-${K}p || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q42logs"; mkdir -p "$L"
crashed() { grep -q "Crash!!!\|Received signal" "$1" && { echo "CRASH in $(basename $1): stopping"; grep -A15 "Crash!!!\|Received signal" "$1" | head -30; exit 1; }; }
H="-fpscap 0 -hitchms 50"
VILLAGE="-island v3 -goto -2151,3731 -benchstops herewarm,here -benchlong here 20"
# ---- A
variant() { case $1 in plain) echo "";; nowater) echo "-benchnoshader Water";; nocopies) echo "-benchnocopies";; both) echo "-benchnoshader Water -benchnocopies";; esac; }
for RD in 1 2 3; do
  case $RD in 1) ORDER="plain nowater nocopies both";; 2) ORDER="both nocopies nowater plain";; *) ORDER="nocopies plain both nowater";; esac
  for V in $ORDER; do
    T=q42-water-$V-$RD
    run Bench-${K} $T $VILLAGE $(variant $V) $H
    cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
    echo "water $V $RD | $(grep -h "BenchRoute: here: avg" $L/$T.log | grep -o 'avg [0-9.]* fps\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms\|render thread [0-9.]* ms' | tr '\n' ' ') | $(grep -h 'BenchRoute: off' $L/$T.log | sort -u | cut -c1-90 | tr '\n' ' ')"
  done
done
# ---- B
gc() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/GcCallers.cs.txt" "$P/Assets/Game/Scripts/Editor/GcCallers.cs"
  ( cd "$P" && timeout 2400 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod GcCallers.Dump -raw "$WW\\q-$1.raw" -out "$W/$1-gc.txt" -top 10 -logFile "$W/gccallers.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/GcCallers.cs" "$P/Assets/Game/Scripts/Editor/GcCallers.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
  if [ -f "$R/$1-gc.txt" ]; then cp "$R/$1-gc.txt" "$L/"; cat "$R/$1-gc.txt" | cut -c1-300; else echo "GcCallers failed for $1:"; grep -m5 "error CS\|Exception" "$W/gccallers.log"; fi
}
spot() { case $1 in
  forest) echo "-island v3 -goto 2349,-2575 -benchstops herewarm,here -benchlong here 20";;
  village) echo "$VILLAGE";;
  cave) echo "-island v3 -benchstops caveapproach";; esac; }
for SP in forest village cave; do
  T=prof42-$SP; ST=here; [ $SP = cave ] && ST=caveapproach; rm -f "$R/q-$T.raw"
  run Profile-${K}p $T $(spot $SP) $H -profilestop $ST "$W/q-$T.raw"
  cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
  echo "== $T"; [ -f "$R/q-$T.raw" ] && gc $T || echo "no capture $T"
done
build $K BuildDeep Deep-${K}d || { echo "deep build failed"; log "q42 done (no deep)"; exit 0; }
T=deep42-village; rm -f "$R/q-$T.raw"
run Deep-${K}d $T -island v3 -goto -2151,3731 -benchstops herewarm,here -benchlong here 10 -deepprofiling $H -profilestop here "$W/q-$T.raw"
cp "$R/q-$T.log" "$L/$T.log"; crashed "$L/$T.log"
echo "== $T: raw $(ls -la $R/q-$T.raw 2>/dev/null | awk '{print $5}')"; [ -f "$R/q-$T.raw" ] && gc $T || echo "no capture $T"
log "q42 done"
