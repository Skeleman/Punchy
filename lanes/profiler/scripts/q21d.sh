#!/bin/bash
# Job 21d: 21c again on main 810955f (after PR #116), but the client measures its own fps (here, 60 s) while the host is
# still running (host here stop held for 240 s). v2, uncapped, both processes on this laptop. DungeonCheck now also logs
# DUNGEONPOSTS: creatures at NearWater/NearChest sockets per world (on the client these are replicas), whether they
# drink / face a player, and every player in the world. Scenarios: temple, cave, then "guard": both players 3 m from a
# chest a guard stands at (taken from the cave host's log), so a guard should turn to face them.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=810955f
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q21d dungeon host/client, client fps in session (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/DungeonCheck.cs.txt" "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs"
build $K Build Bench-${K}d; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs" "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs.meta"
[ $rc = 0 ] || { echo "build Bench-${K}d failed"; exit 1; }
keepawake
fixed Bench-${K}d || exit 1
log "q21d dialogs: $(dialogs)"
mkdir -p "$R/q21dlogs"
COMMON="-benchroute -weatherseed 4 -time 12 $(screen Bench 1280 720) -island v2 -dungeontest -dungeoncheck -benchstops here -fpscap 0"
for SC in temple cave guard; do
  case $SC in
    temple) WHERE="-place temple";;
    cave) WHERE="-goto 530,45,958";;
    guard) G=$(grep -o "chest(-\?[0-9]*,-\?[0-9]*,-\?[0-9]*):" "$R/q21dlogs/cave-host.log" | head -1 | tr -d 'chest():')
           [ -n "$G" ] || { echo "no guard at a chest in the cave host log; skipping guard"; continue; }
           IFS=, read GX GY GZ <<< "$G"; WHERE="-goto $((GX+3)),$((GY+1)),$GZ"; echo "guard spot: chest $G, players at $WHERE";;
  esac
  cool
  HL="$R/q21dlogs/$SC-host.log"; CL="$R/q21dlogs/$SC-client.log"; rm -f "$HL" "$CL"
  ( cd "$P/Builds/Bench" && timeout 480 ./Punchy.exe -logFile "$(cygpath -m $HL)" $COMMON $WHERE -benchwait 50 -benchlong here 240 -host >/dev/null 2>&1 ) &
  HP=$!
  for i in $(seq 1 120); do grep -q "BenchRoute: world ready" "$HL" 2>/dev/null && break; sleep 1; done
  sleep 30; log "q21d $SC host world ready after ${i}s (+30 s); starting the client"
  ( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$(cygpath -m $CL)" $COMMON $WHERE -benchwait 20 -benchlong here 60 -join 127.0.0.1 >/dev/null 2>&1 ) &
  CP=$!
  wait $CP; crc=$?; log "  client rc=$crc"
  wait $HP; hrc=$?; log "  host rc=$hrc dialogs: $(dialogs)"
  strays >/dev/null
  for f in "$HL" "$CL"; do
    echo "== $SC $(basename $f .log): exc $(grep -c Exception $f)"
    grep -h "\[dungeon\] .*\(drinks\|guards\)\|DUNGEONCHECK\|DUNGEONPOSTS\|BenchRoute: here\|BenchRoute: world ready\|GameBootstrap\|landmarks, hash\|onnected\|hash 0x\|hash [0-9A-F]\{8\} (" $f | cut -c1-420 | head -90
  done
  for rc in $crc $hrc; do [ $rc = 139 ] || [ $rc = 134 ] && { echo "CRASH in $SC (rc $rc): stopping"; grep -h -A12 "Crash!!!\|Received signal" "$HL" "$CL" | head -40; exit 1; }; done
done
log "q21d done"
