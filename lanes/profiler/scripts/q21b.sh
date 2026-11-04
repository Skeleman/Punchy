#!/bin/bash
# Job 21b (v3 joins timed out in 21: main still runs the v3 audit reports, a ~40 s frame): dungeon host/client check on main e31291e (PR #104, #24), island v3, two processes on this laptop: a host
# (-host) and a client (-join 127.0.0.1) from Builds/Bench, both with -dungeontest (cave stand-in) and the profiler-only
# DungeonCheck helper (posts/creatures/replicas per world every 10 s). Scenario 1: both -place temple; scenario 2: both on
# the hut cave floor (-goto 530,45,958). BenchRoute "here" measures frame ms in place after -benchwait 50 (so ~60-80 s
# after world ready). Both processes share the laptop, so frame ms are two games at once.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=e31291e
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q21b dungeon host/client v2 (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
keepawake
fixed Bench-${K}d || exit 1
log "q21 dialogs: $(dialogs)"
mkdir -p "$R/q21blogs"
COMMON="-benchroute -weatherseed 4 -time 12 $(screen Bench 1280 720) -island v2 -dungeontest -dungeoncheck -benchstops here -benchwait 50 -fpscap 0"
for SC in temple cave; do
  case $SC in temple) WHERE="-place temple";; cave) WHERE="-goto 530,45,958";; esac
  cool
  HL="$R/q21blogs/$SC-host.log"; CL="$R/q21blogs/$SC-client.log"; rm -f "$HL" "$CL"
  ( cd "$P/Builds/Bench" && timeout 420 ./Punchy.exe -logFile "$(cygpath -m $HL)" $COMMON $WHERE -host >/dev/null 2>&1 ) &
  HP=$!
  for i in $(seq 1 120); do grep -q "BenchRoute: world ready" "$HL" 2>/dev/null && break; sleep 1; done
  log "q21 $SC host world ready after ${i}s; starting the client"
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$(cygpath -m $CL)" $COMMON $WHERE -join 127.0.0.1 >/dev/null 2>&1 ) &
  CP=$!
  wait $CP; log "  client rc=$?"
  wait $HP; log "  host rc=$? dialogs: $(dialogs)"
  strays >/dev/null
  for f in "$HL" "$CL"; do
    echo "== $SC $(basename $f .log): exc $(grep -c Exception $f)"
    grep -h "\[dungeon\]\|DUNGEONCHECK\|BenchRoute: here\|BenchRoute: world ready\|GameBootstrap\|landmarks, hash\|connected\|Connected" $f | cut -c1-330 | head -60
  done
done
log "q21b done"
