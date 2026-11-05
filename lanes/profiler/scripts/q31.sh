#!/bin/bash
# Job 31: GPU split at v2 fishing village 1 (day) and the v3 HumanVillage at 22:00. The GPU recorders read nothing on
# this D3D11 laptop, so each pass is measured by switching it off (runner: -benchnoshadows, -benchnopost,
# -benchnocopies, -benchnolights, -benchnoshader <names>) and taking the stop's gpu ms against a plain run (plain at
# the start and the end, for drift). Profile build of main 69ca891, uncapped, herewarm + here 20 s, -profiler-enable
# for the draw / setpass counters.
B=/c/Users/zache/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=69ca891
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "q31 gpu split (since $(date +%T))" > "$R/.lock/job"; trap 'rm -rf "$R/.lock"' EXIT
rm -rf "$P/Builds/Profile-${K}g"
build $K BuildDev Profile-${K}g || { echo "build failed"; exit 1; }
keepawake
L="$R/q31logs"; mkdir -p "$L"
VARS=("plain|" "noshadows|-benchnoshadows" "nopost|-benchnopost" "nocopies|-benchnocopies" "nolights|-benchnolights"
      "nowater|-benchnoshader Water" "noflora|-benchnoshader Flora" "nodetail|-benchnoshader Grass,Pebbles"
      "noterrain|-benchnoshader Terrain" "nocreatures|-benchnocreatures" "nosky|-benchnoshader Sky"
      "noeffects|-benchnoshader Particle,Ambient,Fireflies,Butterflies,Flame,Plume,Haze,Precipitation,Drips,Gas"
      "nostructures|-benchnoshader Colossus,FarShadow,Cave" "plain2|")
for SPOT in "fv1|-island v2 -goto -3304,-1644" "night|-island v3 -goto 3045,-827 -benchhour 22"; do
  SN=${SPOT%%|*}; SA=${SPOT#*|}
  for V in "${VARS[@]}"; do
    VN=${V%%|*}; VA=${V#*|}
    T=q31-$SN-$VN
    run Profile-${K}g $T $SA -benchstops herewarm,here -benchlong here 20 -fpscap 0 -benchgpupasses -profiler-enable $VA < /dev/null
    cp "$R/q-$T.log" "$L/$T.log"
    grep -q "Crash!!!\|Received signal" "$L/$T.log" && { grep -A15 "Crash!!!\|Received signal" "$L/$T.log" | head -30; exit 1; }
    H=$(grep -h "BenchRoute: here: avg" "$L/$T.log")
    echo "$SN $VN | $(echo "$H" | grep -o 'avg [0-9.]* fps') | $(echo "$H" | grep -o 'gpu [0-9.]* ms') | $(echo "$H" | grep -o 'main thread [0-9.]* ms, render thread [0-9.]* ms') | $(echo "$H" | grep -o 'draw calls avg [0-9]*, batches [0-9]*, setpass [0-9]*, shadow casters [0-9]*, rendered tris [0-9]*k') | $(echo "$H" | grep -o 'moving lights max [0-9]* avg [0-9.]*') | $(grep -h 'BenchRoute: off' $L/$T.log | sort -u | cut -c15-90 | tr '\n' ';')"
  done
done
log "q31 done"
