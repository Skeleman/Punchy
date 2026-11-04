#!/bin/bash
# Job 22c (#81, Zach saw the cave freeze in a build: the world-features Dev build of 2026-11-03 12:44, ~bab5e33, before
# W-75's hitch fixes and #102). Builds: main 0ec7a8d and world-features head 3aaeb59 (Bench + Profile), bab5e33 (Bench). walking (6 m/s) and fast (18 m/s) from 250 m out on the surface,
# through the mouth and the passages, to the bat folk chamber, v2 (530,45,958) and v3 (-2674,23,-607). Runner with the
# hereentry/hereentryfast stops and a per-frame hitch log (> 50 ms, with time, position and phase).
# (1) Bench build, uncapped, 2 runs per island: here, hereentry, hereentryfast. (2) Profile build captures of each entry
# (walk at -fpscap 25, fast at -fpscap 60, so the capture fits 2000 frames) with BigFrame dumps of the worst frames
# (markers >= 5 ms) and the BProfileTools analysis.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
MAIN=0ec7a8d; HEAD=3aaeb59; ZACH=bab5e33
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q22c cave entry hitches (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
for K in $MAIN $HEAD $ZACH; do build $K Build Bench-${K}r || echo "build Bench-${K}r failed"; done
for K in $MAIN $HEAD; do build $K BuildDev Profile-${K}r || echo "build Profile-${K}r failed"; done
keepawake
log "q22c dialogs: $(dialogs)"
mkdir -p "$R/q22logs"
spot() { case $1 in v2) echo "-island v2 -goto 530,45,958";; v3) echo "-island v3 -goto -2674,23,-607";; esac; }
for RD in 1 2; do for I in v2 v3; do
  case $RD in 1) ORDER="$ZACH $MAIN $HEAD";; *) ORDER="$HEAD $MAIN $ZACH";; esac
  for K in $ORDER; do
    run Bench-${K}r q22c-$K-$I-$RD $(spot $I) -benchstops here,hereentry,hereentryfast -fpscap 0; cp "$R/q-q22c-$K-$I-$RD.log" "$R/q22logs/entry-$K-$I-$RD.log"
  done
done; done
analyze() { # tag
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\q-$1.raw" -out "$WW\$1-big.txt" -minms 5 -frames 12 -listms 50 -logFile "$W/bigframe.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\q-$1.raw" -out "$WW\$1.txt" -logFile "$W/analyze.log" )
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  strays >/dev/null; rm -f "$R/q-$1.raw"
}
for K in $MAIN $HEAD; do for I in v2 v3; do
  for ST in hereentry hereentryfast; do
    CAP=25; [ $ST = hereentryfast ] && CAP=60
    T=prof22c-$K-$I-$ST; rm -f "$R/q-$T.raw"
    run Profile-${K}r $T $(spot $I) -benchstops here,$ST -profilestop $ST "$W/q-$T.raw" -fpscap $CAP
    cp "$R/q-$T.log" "$R/q22logs/$T.log"
    [ -f "$R/q-$T.raw" ] && analyze $T || echo "no capture $T"
  done
done; done
for f in "$R"/q22logs/entry-*.log "$R"/q22logs/prof22c-*.log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f)"
  grep -h -e "BenchRoute: entry path" -e "BenchRoute: here" -e "BenchRoute: hitch" $f | sed 's/, per 5 s worst ms\/slow frames:/ | 5s:/' | cut -c1-420 | head -60
done
for f in "$R"/prof22c-*-big.txt; do echo "== $(basename $f)"; cut -c1-180 $f | head -200; done
log "q22c done"
