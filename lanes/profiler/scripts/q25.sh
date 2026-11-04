#!/bin/bash
# Job 25 (optional, supervisor): what makes the 800+ ms burst at the dungeon guard spot (both processes, q21d) and the
# 1.65 s host frame when the client disconnects. Main 0b39e9b, Profile build with DungeonCheck, v2, the q21d "guard"
# scenario (both players 3 m from the chest at 528,40,961). Host: here stop 120 s at -fpscap 20, profiled, so the
# editor's last 2000 frames (100 s) hold both the burst (~141 s) and the disconnect (~170 s). Client: here 60 s at
# -fpscap 30, profiled (its burst was ~93 s). BigFrame dumps of the longest frames of each.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=0b39e9b
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q25 guard burst / disconnect captures (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
cp "$S/DungeonCheck.cs.txt" "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs"
build $K BuildDev Profile-${K}d; rc=$?
rm -f "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs" "$P/Assets/Game/Scripts/Debugging/DungeonCheck.cs.meta"
[ $rc = 0 ] || { echo "build failed"; exit 1; }
keepawake
fixed Profile-${K}d || exit 1
log "q25 dialogs: $(dialogs)"
mkdir -p "$R/q25logs"
COMMON="-benchroute -weatherseed 4 -time 12 $(screen Bench 1280 720) -island v2 -dungeontest -dungeoncheck -benchstops here -goto 531,41,961"
HL="$R/q25logs/host.log"; CL="$R/q25logs/client.log"; rm -f "$HL" "$CL" "$R/q25-host.raw" "$R/q25-client.raw"
cool
( cd "$P/Builds/Bench" && timeout 480 ./Punchy.exe -logFile "$(cygpath -m $HL)" $COMMON -benchwait 50 -benchlong here 120 -fpscap 20 -profilestop here "$W/q25-host.raw" -host >/dev/null 2>&1 ) &
HP=$!
for i in $(seq 1 120); do grep -q "BenchRoute: world ready" "$HL" 2>/dev/null && break; sleep 1; done
sleep 30; log "q25 host world ready after ${i}s (+30 s); starting the client"
( cd "$P/Builds/Bench" && timeout 400 ./Punchy.exe -logFile "$(cygpath -m $CL)" $COMMON -benchwait 20 -benchlong here 60 -fpscap 30 -profilestop here "$W/q25-client.raw" -join 127.0.0.1 >/dev/null 2>&1 ) &
CP=$!
wait $CP; crc=$?; log "  client rc=$crc"
wait $HP; hrc=$?; log "  host rc=$hrc dialogs: $(dialogs)"
strays >/dev/null
for f in "$HL" "$CL"; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f)"
  grep -h "BenchRoute: here\|BenchRoute: hitch\|onnected\|guards its\|Crash!!!" $f | awk '!/hitch/ || $3>150' | cut -c1-260 | head -40
done
for rc in $crc $hrc; do { [ $rc = 139 ] || [ $rc = 134 ]; } && { echo "CRASH (rc $rc): stopping"; grep -h -A12 "Crash!!!\|Received signal" "$HL" "$CL" | head -40; exit 1; }; done
cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
for T in q25-host q25-client; do
  [ -f "$R/$T.raw" ] || { echo "no capture $T"; continue; }
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\$T.raw" -out "$W/$T-big.txt" -minms 5 -frames 8 -listms 300 -logFile "$W/bigframe.log" )
  echo "== $T-big.txt"; cut -c1-200 "$R/$T-big.txt" | head -220
done
rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
strays >/dev/null
log "q25 done"
