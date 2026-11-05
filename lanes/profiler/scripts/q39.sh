#!/bin/bash
# Supervisor 11-05: compile check of b/fix-player-build (76eea0f) as a player (release) build.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q39 player build check (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build 76eea0f Build Bench-76eea0f && echo PASS || { echo FAIL; grep -m3 "error CS" "$R/build-Bench-76eea0f.log"; }
