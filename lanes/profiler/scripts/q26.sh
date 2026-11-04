#!/bin/bash
# Job 26 (self-fix, b/fix-netstatus cad0ffc): NetMenu's 10 s "Net status" line sampled the ground under every animal
# on the main thread (34-83 ms on the client in q25). Before/after: Profile builds of main ccdad75 and the fix, solo at
# the v2 bat folk cave (~190 animals), here stop 60 s at -fpscap 60 (so the editor's last 2000 frames hold 3 status
# lines), profiled; BProfileTools per marker (NetMenu.Update max call) and frame stats. 2 runs each, alternating.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
MAINK=ccdad75; FIX=cad0ffc
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q26 NetMenu status fix before/after (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $MAINK BuildDev Profile-${MAINK}p || { echo "build main failed"; exit 1; }
build $FIX BuildDev Profile-${FIX}p || { echo "build fix failed"; exit 1; }
keepawake
mkdir -p "$R/q26logs"
for RD in 1 2; do
  case $RD in 1) ORDER="$MAINK $FIX";; *) ORDER="$FIX $MAINK";; esac
  for K in $ORDER; do
    T=prof26-$K-$RD; rm -f "$R/q-$T.raw"
    run Profile-${K}p $T -island v2 -goto 530,45,958 -benchstops here -benchlong here 60 -fpscap 60 -profilestop here "$W/q-$T.raw"
    cp "$R/q-$T.log" "$R/q26logs/$T.log"
    grep -h "BenchRoute: here:\|Net status" "$R/q-$T.log" | cut -c1-240 | tail -4
    if grep -q "Crash!!!\|Received signal" "$R/q-$T.log"; then echo "CRASH in $T: stopping"; grep -A15 "Crash!!!\|Received signal" "$R/q-$T.log" | head -30; exit 1; fi
    echo "$T exc $(grep -c Exception "$R/q-$T.log")"
  done
done
strays >/dev/null
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
for f in "$R"/q-prof26-*.raw; do
  T=$(basename $f .raw); T=${T#q-}
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$(cygpath -w $f)" -out "$W/$T.txt" -logFile "$W/analyze.log" )
  echo "== $T"; grep "frames used" "$R/$T.txt"; grep "NetMenu" "$R/$T.txt" | cut -c1-200
done
rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null; rm -f "$R"/q-prof26-*.raw
log "q26 done"
