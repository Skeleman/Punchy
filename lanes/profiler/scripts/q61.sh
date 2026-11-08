#!/bin/bash
# Job 33: what the overnight prototypes cost. Profile builds, 1920x1080, render scale 1, -gfx High, -weatherseed 4,
# uncapped, noon, a fresh game per run.
#   swells: main 46012623 (A) against water/swells 098ddffd (B: swells, breakers, whitecaps, shore contact, Snell's
#           window) and B with -noswells (N). Order A B N N B A at:
#             beach-clear / beach-storm: -benchplace Beach (facing out to sea), -benchweather Clear / Storm;
#             lake: job 29's lake shore view.
#   glitter, lava, pose: see each case below. river-flow follows when its branch is pushed.
# "q61.sh swells|glitter|lava|pose|clumps|bluemoon".
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
KM=4601262; OM=Profile-${KM}m; KW=098ddff; OW=Profile-${KW}m; Q=q61
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q prototypes $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $KM BuildDev $OM || { echo "build $OM failed"; exit 1; }
COMMON="-gfx High -fpscap 0"
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
go() { # set variant round build commit extra...
  local set=$1 v=$2 r=$3 out=$4 c=$5; shift 5
  BENCHW=1920 BENCHH=1080 runcsv $out $Q-$set-$v $c s100 $r $COMMON "$@"
  check "$R/q-$Q-$set-$v-cs100-$r.log"
}
case $1 in
swells)
  build $KW BuildDev $OW || { echo "build $OW failed"; exit 1; }
  keepawake
  for set in beach-clear beach-storm lake; do
    case $set in
      beach-clear) A="-benchstops place -benchplace Beach -benchweather Clear" ;;
      beach-storm) A="-benchstops place -benchplace Beach -benchweather Storm" ;;
      lake) A="-benchstops spot -benchspot -1624,-488,0" ;;
    esac
    rm -f "$R"/$Q-$set-*.csv "$L"/q-$Q-$set-*.log
    go $set main 1 $OM $KM $A -benchshots "$W/$Q-shots/$set-main"
    go $set swells 1 $OW $KW $A -benchshots "$W/$Q-shots/$set-swells"
    go $set noswells 1 $OW $KW $A -noswells -benchshots "$W/$Q-shots/$set-noswells"
    go $set noswells 2 $OW $KW $A -noswells
    go $set swells 2 $OW $KW $A
    go $set main 2 $OM $KM $A
    for f in "$R"/$Q-$set-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  done
  grep -h 'weather .* forced\|Weather: forced' "$L"/q-$Q-beach-*-1.log | sort | uniq -c | cut -c1-160 ;;
glitter)   # water/glitter-grid 17c1b431 against main at job 29's lake shore view, A B B A
  KG=17c1b43; OG=Profile-${KG}m
  build $KG BuildDev $OG || { echo "build $OG failed"; exit 1; }
  keepawake
  A="-benchstops spot -benchspot -1624,-488,0"
  rm -f "$R"/$Q-glitter-*.csv "$L"/q-$Q-glitter-*.log
  go glitter main 1 $OM $KM $A -benchshots "$W/$Q-shots/glitter-main"
  go glitter grid 1 $OG $KG $A -benchshots "$W/$Q-shots/glitter-grid"
  go glitter grid 2 $OG $KG $A
  go glitter main 2 $OM $KM $A
  for f in "$R"/$Q-glitter-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done ;;
lava)      # water/lava-plates 53582b9b against main, looking down into the VolcanoCone crater, A B B A
  KL=53582b9; OL=Profile-${KL}m
  build $KL BuildDev $OL || { echo "build $OL failed"; exit 1; }
  keepawake
  A="-benchstops place -benchplace VolcanoCone:centre -benchview place,${PITCH:-55}"
  rm -f "$R"/$Q-lava-*.csv "$L"/q-$Q-lava-*.log
  go lava main 1 $OM $KM $A -benchshots "$W/$Q-shots/lava-main"
  go lava plates 1 $OL $KL $A -benchshots "$W/$Q-shots/lava-plates"
  go lava plates 2 $OL $KL $A
  go lava main 2 $OM $KM $A
  for f in "$R"/$Q-lava-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'BenchRoute: place: avg' "$L"/q-$Q-lava-*-1.log | sed 's/ fps.*, at / ... at /' | cut -c1-120 ;;
pose)      # refactor/pose-by-size dcd0318d against main: -perflog's pose job ms and posed/reused counts (CPU), in
           # job 24's shot-list sessions (40 s fill, then 5 lines of 3 s): the meadow at -wildcap 2000 and the
           # mountains (rocs, if one shows: the log's species lines say), A B B A each.
  KP=dcd0318; OP=Profile-${KP}m
  build $KP BuildDev $OP || { echo "build $OP failed"; exit 1; }
  keepawake
  mkdir -p "$R/$Q-poseshots"
  for spot in meadow mountains; do
    { echo "-place $spot -time 12 -wait 40 -perflog -shot fill"; for i in 1 2 3 4 5; do echo "-wait 3 -perflog -shot s$i"; done; } > "$R/$Q-list-$spot.txt"
  done
  one() { # spot build commit tag round
    local tag=$Q-pose-$1-$4-$5 cap=""
    [ $1 = meadow ] && cap="-wildcap 2000"
    fixed $2 || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 600 ./Punchy.exe -logFile "$W/q-$tag.log" $(screen Bench 1920 1080) \
        -shotlist "$W/$Q-list-$1.txt" -shotdir "$W/$Q-poseshots/$tag" -quit -perflog -gfx High -fpscap 0 -weatherseed 4 $cap >/dev/null 2>&1 )
    [ $? -eq 124 ] && echo "$tag: TIMED OUT"
    strays >/dev/null; check "$R/q-$tag.log"
    echo "== $tag"; grep -o 'pose job median[^|]*' "$R/q-$tag.log" | tail -5
  }
  for spot in meadow mountains; do
    one $spot $OM $KM main 1; one $spot $OP $KP size 1; one $spot $OP $KP size 2; one $spot $OM $KM main 2
  done
  grep -ohi 'roc\b[^,]*\|sky serpent[^,]*\|SkySerpent[^,]*' "$L"/q-$Q-pose-mountains-*.log | sort | uniq -c | head ;;
clumps)    # grass/clumps d6fee299 (5-blade tufts, half the roots) against -grassclumps 0 on the same build, the Meadow place
           # looking across the field, C O O C; then one RenderDoc capture of each (rdpasses.py top draws: the grass).
  KC=d6fee29; OC=Profile-${KC}m
  RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
  S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
  build $KC BuildDev $OC || { echo "build $OC failed"; exit 1; }
  keepawake
  A="-benchstops place -benchplace Meadow -benchview place,12"
  rm -f "$R"/$Q-clumps-*.csv "$L"/q-$Q-clumps-*.log
  go clumps clumps 1 $OC $KC $A -benchshots "$W/$Q-shots/clumps-clumps"
  go clumps old 1 $OC $KC $A -grassclumps 0 -benchshots "$W/$Q-shots/clumps-old"
  go clumps old 2 $OC $KC $A -grassclumps 0
  go clumps clumps 2 $OC $KC $A
  for f in "$R"/$Q-clumps-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  mkdir -p "$R/rd61c"; rm -f "$R/rd61c"/*.rdc
  for v in clumps old; do
    x=; [ $v = old ] && x="-grassclumps 0"
    fixed $OC || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$WW\\rd61c\\$v" \
        "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rdc-$v.log" -benchroute -weatherseed 4 -time 12 \
        $(screen Bench 1920 1080) $COMMON $A $x -renderdocstop place 1 >/dev/null 2>&1 )
    strays >/dev/null; check "$R/q-$Q-rdc-$v.log"
  done
  CAPS=$(ls "$R/rd61c"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
  RD_CAPS="$CAPS" RD_OUT="$WW\\$Q-clumps-passes.txt" RD_TOPDRAWS=25 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
  echo "qrenderdoc rc=$?"; strays >/dev/null ;;
bluemoon)  # night/blue-moon 7be628b2 against main: the Summit at midnight, Clear forced, -bluemoondays 0, the camera on the
           # blue moon (-benchview bluemoon, re-aimed once the hour is set), M B B M.
  KB=7be628b; OB=Profile-${KB}m
  build $KB BuildDev $OB || { echo "build $OB failed"; exit 1; }
  build $KM BuildDev ${OM}2 || { echo "build ${OM}2 failed"; exit 1; }   # main again with the runner's -benchview bluemoon
  keepawake
  A="-benchstops place -benchplace Summit -benchview bluemoon -benchhour 0 -benchweather Clear -bluemoondays 0"
  rm -f "$R"/$Q-bluemoon-*.csv "$L"/q-$Q-bluemoon-*.log
  go bluemoon main 1 ${OM}2 $KM $A -benchshots "$W/$Q-shots/bluemoon-main"
  go bluemoon blue 1 $OB $KB $A -benchshots "$W/$Q-shots/bluemoon-blue"
  go bluemoon blue 2 $OB $KB $A
  go bluemoon main 2 ${OM}2 $KM $A
  for f in "$R"/$Q-bluemoon-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
  grep -h 'view on the blue moon\|weather Clear forced' "$L"/q-$Q-bluemoon-*-1.log | sort | uniq -c | cut -c1-160 ;;
*) echo "usage: q61.sh swells|glitter|lava|pose|clumps|bluemoon"; exit 2 ;;
esac
echo $Q $1 done
