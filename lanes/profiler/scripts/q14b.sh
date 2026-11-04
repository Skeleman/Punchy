#!/bin/bash
# Item 14, part b: 2 play cycles with native leak detection + stack traces, to name the Persistent allocations left
# after each play (q14: 132 at shutdown after 11 plays, ~22 MB a cycle).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=5d0956a
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q14b leak stacks (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
OUTD="$R/q14b"; rm -rf "$OUTD"; mkdir -p "$OUTD"
strays >/dev/null
( cd "$P" && git checkout -q --detach $K ) || exit 1
cp "$S/MemCheck.cs.txt" "$P/Assets/Game/Scripts/Editor/MemCheck.cs"
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
( cd "$P" && timeout 1800 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod MemCheck.Run -cycles 2 -leakstack -memout "$(cygpath -m $OUTD)" -logFile "$(cygpath -m $OUTD)/editor.log" )
log "q14b editor rc=$?"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
( cd "$P" && git checkout -- ProjectSettings 2>/dev/null; rm -f Assets/Game/Scripts/Editor/MemCheck.cs Assets/Game/Scripts/Editor/MemCheck.cs.meta; git checkout -q $HOME_BRANCH )
strays >/dev/null
grep "Total Used" "$OUTD/memcheck.txt" | grep -o "Total Used Memory [0-9]* MB"
grep -n -i "leak" "$OUTD/editor.log" | cut -c1-200 | head -20
