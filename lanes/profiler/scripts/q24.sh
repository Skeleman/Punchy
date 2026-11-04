#!/bin/bash
# Job 24: fps and worst frame walking up to the new colossi on v3 (main 810955f, PR #116, #40): the Ashlands skeletal
# lizard and the PoisonSwamp carnivorous plant. Runner stop "hereclose" (-close x,z,yaw): 10 s still at 300 m out along
# the colossus's facing, then a 6 m/s walk to 20 m from it; every frame over 50 ms is logged with how far out it was.
# Control: the same walk aimed at a point 800 m to the side (no colossus within 400 m). 2 rounds, rotated, uncapped.
# Uses the Bench-810955fd build from q21d (the DungeonCheck helper in it only runs with -dungeoncheck).
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=810955f
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q24 colossus approach (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
build $K Build Bench-${K}d || { echo "build failed"; exit 1; }
keepawake
mkdir -p "$R/q24logs"
log "q24 dialogs: $(dialogs)"
run Bench-${K}d q24-probe -island v3 -benchstops here -fpscap 0
cp "$R/q-q24-probe.log" "$R/q24logs/probe.log"
grep -h "StoneColossi" "$R/q24logs/probe.log"
# All colossus positions, for the control check.
ALL=$(grep -o "StoneColossi: [A-Za-z]* placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*)" "$R/q24logs/probe.log" | sed 's/.*(\(.*\))/\1/' | awk -F', ' '{printf "%s,%s ", $1, $3}')
spec() { # kind -> "x,z,yaw" or empty
  grep -o "StoneColossi: $1 placed at ([-0-9.]*, [-0-9.]*, [-0-9.]*), facing [-0-9.]*" "$R/q24logs/probe.log" | head -1 |
    sed 's/.*(\([-0-9.]*\), [-0-9.]*, \([-0-9.]*\)), facing \([-0-9.]*\)/\1,\2,\3/'
}
control() { # "x,z,yaw" -> the same walk 800 m to one side, the side whose target is 400 m+ from every colossus
  echo "$1" | awk -F, -v all="$ALL" '{
    y = $3 * 3.14159265 / 180; px = cos(y); pz = -sin(y); n = split(all, a, " ")
    for (s = 1; s >= -1; s -= 2) { cx = $1 + s * 800 * px; cz = $2 + s * 800 * pz; ok = 1
      for (i = 1; i <= n; i++) { split(a[i], q, ","); if ((q[1]-cx)^2 + (q[2]-cz)^2 < 400^2) ok = 0 }
      if (ok) { printf "%.0f,%.0f,%s\n", cx, cz, $3; exit } } }'
}
start() { echo "$1" | awk -F, '{ y = $3 * 3.14159265 / 180; printf "%.0f,%.0f\n", $1 + 300 * sin(y), $2 + 300 * cos(y) }'; }
declare -A WALK
for C in SkeletalLizard CarnivorousPlant; do
  S1=$(spec $C); [ -n "$S1" ] || { echo "$C: not placed on v3 (see probe)"; continue; }
  WALK[$C]=$S1; CT=$(control "$S1"); [ -n "$CT" ] && WALK[$C-control]=$CT || echo "$C: no control spot clear of colossi"
done
for k in "${!WALK[@]}"; do echo "walk $k: -close ${WALK[$k]}, start $(start ${WALK[$k]})"; done
for RD in 1 2; do
  case $RD in 1) ORDER="SkeletalLizard SkeletalLizard-control CarnivorousPlant CarnivorousPlant-control";;
              *) ORDER="CarnivorousPlant-control CarnivorousPlant SkeletalLizard-control SkeletalLizard";; esac
  for k in $ORDER; do
    [ -n "${WALK[$k]}" ] || continue
    T=q24-$k-$RD
    run Bench-${K}d $T -island v3 -goto $(start ${WALK[$k]}) -close ${WALK[$k]} -benchstops hereclose -fpscap 0
    cp "$R/q-$T.log" "$R/q24logs/$k-$RD.log"
    if grep -q "Crash!!!\|Received signal" "$R/q-$T.log"; then echo "CRASH in $T: stopping"; grep -A15 "Crash!!!\|Received signal" "$R/q-$T.log" | head -30; exit 1; fi
  done
done
for f in "$R"/q24logs/*-[12].log; do
  echo "== $(basename $f .log): exc $(grep -c Exception $f)"
  grep -h -e "BenchRoute: approach" -e "BenchRoute: hereclose" -e "BenchRoute: hitch" $f | sed 's/, per 5 s worst ms\/slow frames:/ | 5s:/' | cut -c1-460 | head -60
done
log "q24 done"
