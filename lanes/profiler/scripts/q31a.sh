#!/bin/bash
# Job 31 smoke: Profile build of main 69ca891 with the runner's new -benchnoshadows/-benchnopost/-benchnocopies/
# -benchnoshader; does each switch log, and do draw/setpass counters read with -profiler-enable?
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=69ca891
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q31a gpu split smoke (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K BuildDev Profile-${K}g || { echo "build failed"; exit 1; }
keepawake
L="$R/q31logs"; mkdir -p "$L"
T=q31a-smoke
run Profile-${K}g $T -island v2 -goto -3304,-1644 -benchstops herewarm,here -benchlong here 10 -fpscap 0 -benchgpupasses -profiler-enable -benchnoshadows -benchnopost -benchnocopies -benchnoshader Water < /dev/null
cp "$R/q-$T.log" "$L/$T.log"
grep -h "BenchRoute: off\|BenchRoute: gpu\|error CS\|Exception" "$L/$T.log" | cut -c1-300 | head
grep -h "BenchRoute: here: avg" "$L/$T.log" | grep -o "gpu [0-9.]* ms\|draw calls.*k\|main thread [0-9.]* ms"
log "q31a done"
