#!/bin/bash
# Job 18: W-75 (world-features 096ec00) and A-51 (animal-behaviours b527dc1) vs main 9d34275; A-51 also vs its base
# f405348 (Bench-f405348n from job 16). Uncapped, island v2 (default), 3 rounds rotated: q13 route + hut cave floor.
# World ready: BenchRoute's line on every build, W-75's "[stream] world ready" with the load boost on and off
# (PUNCHY_LOADBOOST=0). Dev profiles at townwalk and the hut cave on main, W-75, A-51.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
MAIN=9d34275; W75=096ec00; A51=b527dc1; ABASE=f405348
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q18 W-75 + A-51 bench (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
for K in $MAIN $W75 $A51; do
  build $K Build Bench-$K || { echo "build Bench-$K failed"; exit 1; }
  build $K BuildDev Profile-$K || { echo "build Profile-$K failed"; exit 1; }
done
[ -f "$P/Builds/Bench-${ABASE}n/Punchy.exe" ] || { echo "no Bench-${ABASE}n"; exit 1; }
keepawake
log "q18 dialogs: $(dialogs)"
out() { [ $1 = $ABASE ] && echo Bench-${ABASE}n || echo Bench-$1; }
rm -f "$R/q18.csv"; mkdir -p "$R/q18logs"
for RD in 1 2 3; do
  case $RD in 1) ORDER="$MAIN $W75 $A51 $ABASE";; 2) ORDER="$A51 $ABASE $MAIN $W75";; 3) ORDER="$W75 $MAIN $ABASE $A51";; esac
  for K in $ORDER; do
    runcsv $(out $K) q18 $K-route 0 $RD -benchstops town,townwalk,cave,caveapproach,glide -fpscap 0
    cp "$R/q-q18-c0-$RD.log" "$R/q18logs/$K-route-$RD.log"
    runcsv $(out $K) q18 $K-hutcave 0 $RD -benchstops here -goto 530,45,958 -fpscap 0
    cp "$R/q-q18-c0-$RD.log" "$R/q18logs/$K-hutcave-$RD.log"
  done
done
# W-75 with the load boost off (world ready only), 3 runs
export PUNCHY_LOADBOOST=0
for RD in 1 2 3; do run Bench-$W75 q18-boostoff-$RD -benchstops town -fpscap 0; cp "$R/q-q18-boostoff-$RD.log" "$R/q18logs/$W75-boostoff-$RD.log"; done
unset PUNCHY_LOADBOOST
for K in $MAIN $W75 $A51; do
  profile Profile-$K prof18-$K-townwalk townwalk -fpscap 0
  profile Profile-$K prof18-$K-hutcave here -fpscap 0 -goto 530,45,958
done
echo "== bench (mean of 3)"
awk -F, 'NR > 1 { k = $1 " " $4; n[k]++; a[k] += $5; l[k] += $6; m[k] += $7; w[k] += $8; c[k] += $9; g[k] += $10; e[k] += $15 }
  END { for (k in n) printf "  %-24s avg %6.1f  1%%low %5.1f  med %5.2f ms  worst %6.1f ms  cpu %5.2f  gpu %5.2f  exc %d\n", k, a[k]/n[k], l[k]/n[k], m[k]/n[k], w[k]/n[k], c[k]/n[k], g[k]/n[k], e[k] }' "$R/q18.csv" | sort | tee "$R/q18-table.txt"
echo "== world ready (BenchRoute / [stream])"
for f in "$R"/q18logs/*-route-*.log "$R"/q18logs/*-boostoff-*.log; do
  echo "  $(basename $f .log): $(grep -m1 -o 'BenchRoute: world ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / $(grep -m1 -o '\[stream\] world ready at [0-9.]* s' $f | grep -o '[0-9.]* s') / island $(grep -m1 -o 'v2 ready at [0-9.]* s' $f | grep -o '[0-9.]* s')"
done
echo "== markers (mean self ms, main thread)"
for S in townwalk hutcave; do for K in $MAIN $W75 $A51; do
  printf "  %-9s %s:" $S $K
  awk -F'\t' '/^frames used/ { split($0,a,"mean "); split(a[2],b," "); printf " frame %s", b[1] }
    /^key\t/{t=1;next} t && $1 ~ /^Main\|/ && $1 ~ /CreatureRenderSystem$|InteractionInputSystem$|TerrainStreamingSystem$|GroundDetailSystem.Update|FloraSystem.LateUpdate|AmbientParticleSystem.Update|CritterSystem.Update|CreatureRenderSystem:PoseJob|ReachJob|Brain.Tick|Brain.Advance|DebugControls.OnGUI|GUIStyle.GetDimensions|WaitForJobGroupID/ {
      k=$1; sub(/^Main\|/,"",k); sub(/Assembly-CSharp.dll!Punchy\.[A-Za-z]*::/,"",k); sub(/ \[Invoke\]/,"",k); sub(/Default World Punchy\.[A-Za-z]*\./,"",k); sub(/UnityEngine.IMGUIModule.dll!UnityEngine::/,"",k); printf " | %s %.3f", k, $5 }
    END { print "" }' "$R/prof18-$K-$S.txt"
done; done
for f in "$R"/q18logs/*.log; do x=$(grep -c Exception $f); [ "$x" != 0 ] && echo "  EXC $x $(basename $f)"; done
log "q18 done"
