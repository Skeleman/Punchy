#!/bin/bash
# q49's 0.5 s present waits (DXGI.WaitOnSwapChain) (supervisor, 11-06). Usage: q50.sh <tag> [commit]. First run 'open'
# on 2049f8c; then a bisect: 781fa5a (before #174/#175) and ba97b20 (#174 merged, before #175).
# Profile build of 2049f8c (q49's), the run and salt walk-up captures (-fpscap 60, last 2000 frames) x3 each through
# BigFrame (frames over 100 ms, 4 longest trees). One player at a time; each run waits for 6 GB free (RAM is tight).
# Logs which Unity editors are open before each run.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=${2:-2049f8c}
S=/c/users/zache/Projects/punchy-coordination/lanes/profiler/scripts
PH=${1:?tag}
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q50 present waits, editor $PH (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
memgb() { powershell -NoProfile -Command "[math]::Floor((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB)" | tr -d '\r'; }
memwait() { for i in $(seq 1 60); do f=$(memgb); [ "${f:-0}" -ge 6 ] && return 0; log "free memory ${f} GB, waiting"; sleep 60; done; echo "never 6 GB free"; exit 1; }
editors() { tasklist //FI "IMAGENAME eq Unity.exe" //FO CSV //NH 2>/dev/null | grep -c '"Unity.exe"'; }
OUTD=${OUTD:-Profile-${K}p}
build $K BuildDev $OUTD || { echo "profile build failed"; exit 1; }
keepawake
L="$R/q50logs"; mkdir -p "$L"
crashcheck() {
  grep -q "Crash!!!\|Received signal" "$1" || return 0
  if grep -q "BenchRoute: done" "$1" && grep -q "SurfaceGlow.FarScanJob" "$1"; then echo "known quit crash (SurfaceGlow.FarScanJob, fixed in #175) in $(basename $1)"; return 0; fi
  echo "CRASH in $(basename $1): stopping"; grep -A12 "OUTPUTTING STACK TRACE" "$1" | cut -c1-220; exit 1
}
for RD in $(seq 1 ${RDS:-3}); do
  for SP in run salt; do
    T=prof50-$PH-$SP-$RD; rm -f "$R/q-$T.raw"
    memwait
    log "$T: free $(memgb) GB, Unity.exe processes $(editors)"
    if [ $SP = run ]; then ST=run; A="-island v2 -benchstops run"; else ST=hereclose; A="-island v3 -goto 3918,-1125 -close 3702,-917,134 -benchstops hereclose"; fi
    run $OUTD $T $A -fpscap 60 -profilestop $ST "$W/q-$T.raw"
    cp "$R/q-$T.log" "$L/$T.log"; crashcheck "$L/$T.log"
    [ -f "$R/q-$T.raw" ] || { echo "no capture $T"; continue; }
    memwait; strays >/dev/null
    cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
    cp "$S/BigFrame.cs.txt" "$P/Assets/Game/Scripts/Editor/BigFrame.cs"
    ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BigFrame.Dump -raw "$WW\\q-$T.raw" -out "$W/$T-big.txt" -minms 3 -frames 4 -listms 100 -logFile "$W/bigframe.log" )
    rm -f "$P/Assets/Game/Scripts/Editor/BigFrame.cs" "$P/Assets/Game/Scripts/Editor/BigFrame.cs.meta"
    cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
    strays >/dev/null; rm -f "$R/q-$T.raw"
    cp "$R/$T-big.txt" "$L/" 2>/dev/null
    echo "$T: $(sed -n 2p $R/$T-big.txt | cut -c1-200) | swapchain waits over 100 ms in trees: $(grep -c 'DXGI.WaitOnSwapChain  [1-9][0-9][0-9]' $R/$T-big.txt)"
  done
done
log "q50 $PH done"
