#!/bin/bash
# Job 22b: render counters and GPU passes read nothing in 22 without the Profiler recording. Profile-0ec7a8d with a capture
# of the measured "here" stop (after a herewarm stop in the same place) plus -benchgpupasses, at the v2 cave, v2 surface,
# v3 cave; BProfileTools analysis (main / render / workers).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=0ec7a8d
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q22b cave GPU captures (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
spot() { case $1 in v2cave) echo "-island v2 -goto 530,45,958";; v2surf) echo "-island v2 -goto 530,958";; v3cave) echo "-island v3 -goto -2674,23,-607";; esac; }
for S in v2cave v2surf v3cave; do
  rm -f "$R/q-prof22-$S.raw"
  run Profile-$K prof22-$S $(spot $S) -benchstops herewarm,here -profilestop here "$W/q-prof22-$S.raw" -benchgpupasses -fpscap 0
  cp "$R/q-prof22-$S.log" "$R/q22logs/$S-prof.log"
  [ -f "$R/q-prof22-$S.raw" ] || { echo "no capture $S"; continue; }
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\q-prof22-$S.raw" -out "$WW\prof22-$S.txt" -logFile "$W/analyze.log" )
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  strays >/dev/null; rm -f "$R/q-prof22-$S.raw"
  echo "== $S"; grep -h -e "BenchRoute: here:" -e "gpu: here (" "$R/q22logs/$S-prof.log" | sed 's/, per 5 s.*//' | cut -c1-900
  grep -E "^frames used" "$R/prof22-$S.txt" | head -1
  awk -F'\t' '/^key\t/{t=1;next} t && ($1 ~ /^Main\|/ || $1 ~ /^Render\|/) {print}' "$R/prof22-$S.txt" | sort -t$'\t' -k5 -gr | head -25 | cut -c1-160
done
log "q22b done"
