#!/bin/bash
# Job 78's Profiler analysis only (q120.sh's first run skipped it: BProfileTools wasn't in the project).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
Q=q120
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 78 analysis (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
for t in $(ls "$R"/q-$Q-*-cs100-1.raw 2>/dev/null | xargs -n1 basename | sed 's/^q-//; s/\.raw$//'); do
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze \
      -raws "$WW\q-$t.raw" -out "$(cygpath -w "$R/$t.txt")" -keep LightBudget -logFile "$W/analyze-$t.log" )
  echo "analyze $t rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"; strays >/dev/null
  [ -f "$R/$t.txt" ] && rm -f "$R/q-$t.raw"
done
echo q120a done
