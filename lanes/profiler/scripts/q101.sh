#!/bin/bash
# Job 50: two-player crash and desync check (correctness only, no timing). origin/main at start, one Dev build with the
# profiler's TwoPlayerRoute overlay (TwoPlayerRoute.cs.txt; never committed to the game). Two instances on the laptop,
# 1280x720 windowed, -fpscap 30 each, -weatherseed 4, -body default.
#   host:    -host, walks the route once the client is in: rapids below fall 74, the lake (swim), a town at 23:00, the bat
#            roost, the storm spot in Storm; then holds at the storm for the rejoin and a last stop.
#   client:  -join 127.0.0.1, keeps beside the host; quits during the storm stop (the leave), then a second client joins
#            (the rejoin) and quits after 2 minutes (the final leave).
# Both log a TPCHECK line every 10 s and take a shot 15 s into each stop. Not automated: talking to a folk (A-93).
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
Q=q101
git -C "$P" fetch -q origin
K=$(git -C "$P" rev-parse --short=7 origin/main); O=Profile-${K}tp
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 50 two players on $K (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; rm -f "$P/Assets/Game/Scripts/Debugging/TwoPlayerRoute.cs" "$P/Assets/Game/Scripts/Debugging/TwoPlayerRoute.cs.meta"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
echo "main is $K"
# The overlay goes in after build() checks out the commit: build() copies its own files, so add ours through a wrapper.
build_tp() {
  ( cd "$P" && git checkout -q --detach $K ) || return 1
  cp "$S/TwoPlayerRoute.cs.txt" "$P/Assets/Game/Scripts/Debugging/TwoPlayerRoute.cs"
  build $K BuildDev $O; local rc=$?
  rm -f "$P/Assets/Game/Scripts/Debugging/TwoPlayerRoute.cs" "$P/Assets/Game/Scripts/Debugging/TwoPlayerRoute.cs.meta"
  return $rc
}
build_tp || { echo "build failed"; exit 1; }
grep -q 'TwoPlayerRoute' "$R/build-$O.log" && grep 'error CS' "$R/build-$O.log" | head -5
keepawake
fixed $O || exit 1
L="$R/${Q}logs"; SH="$R/$Q-shots"; rm -rf "${L:?}" "${SH:?}"; mkdir -p "$L" "$SH"
COMMON="-weatherseed 4 -time 12 -body default $(screen Bench 1280 720) -fpscap 30 -tpshots $(cygpath -m "$SH")"
STOPS="rapids@-2167,3691@12@Clear;swim@488,-1144@12@Clear;town@3066,-913@23@Clear;roost@-1149,1219@12@Clear;storm@1489,-897@12@Storm;rejoin@1489,-897@12@Storm;last@1489,-897@12@Storm"
HOLD=60
cool
launch() { # name args...: one instance in the background; prints its PID
  local n=$1; shift
  ( cd "$P/Builds/Bench" && exec timeout 1500 ./Punchy.exe -logFile "$(cygpath -m "$L/$n.log")" $COMMON "$@" >/dev/null 2>&1 ) &
  echo $!
}
waitlog() { # file pattern seconds
  local i; for i in $(seq 1 $3); do grep -q "$2" "$1" 2>/dev/null && return 0; sleep 1; done; return 1; }
HP=$(launch host -host -tproute host -tpstops "$STOPS" -tphold $HOLD -tpwait 300 -tpquitafter 900)
waitlog "$L/host.log" "TPCHECK host: in the world" 240 || echo "host never reached the world"
sleep 20; log "$Q host in the world; starting client 1"
# Client 1 leaves 20 s into the storm stop: the route starts ~10 s after it joins, five stops of HOLD before the storm.
C1=$(launch client1 -join 127.0.0.1 -tproute client -tpquitafter $((10 + 5 * HOLD + 20 + 30)))
wait $C1; log "$Q client 1 exited (rc $?)"
sleep 5; log "$Q starting client 2 (the rejoin)"
C2=$(launch client2 -join 127.0.0.1 -tproute client -tpquitafter 120)
wait $C2; log "$Q client 2 exited (rc $?)"
waitlog "$L/host.log" "TPCHECK host: route done" 400 || echo "host route not done"
sleep 15
pid=$(gamepid); [ -n "$pid" ] && timeout 20 powershell -NoProfile -Command "Stop-Process -Id $pid -Force" >/dev/null 2>&1
wait $HP 2>/dev/null
strays >/dev/null
for f in "$L"/*.log; do
  echo "== $(basename $f): $(grep -c 'TPCHECK' $f) checks, exceptions $(grep -c 'Exception' $f), crash $(grep -c 'Crash!!!\|Received signal' $f)"
  grep -h 'WorldSync:\|Net status\|disconnect\|Disconnect' "$f" | head -6 | cut -c1-200
  grep -h 'TPCHECK.*stop \|TPCHECK.*leaving\|TPCHECK.*route\|TPCHECK.*client in' "$f" | head -4 | cut -c1-200
done
python "$S/tpcompare.py" "$L" > "$R/$Q-compare.txt"; head -80 "$R/$Q-compare.txt"
echo $Q done
