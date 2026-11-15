#!/bin/bash
# Job 80 (audit 11-13, #311, #270): streaming spikes on main 3b192717 (shelf build), Medium, 1920x1080, uncapped, noon.
# Routes (each one game with the Profiler on, analysed with -over 8: every main frame over 8 ms and its top samples;
# then two plain games for the frame numbers):
#  cave:   -benchstops hereentry: from 250 m out on the surface to the mouth of wet cave c13 (1572,8,-2635, W-141's)
#  tunnel: out of the W-138 north portal (-1596,1164): a 100 m walk north at 4 m/s on the surface road (the bench can't
#          walk inside a bore; the portal's chunks in sight load as it comes out)
#  jump:   -benchstops townjump: 800 m out from the biggest town, jumped into its middle halfway (#288's flora boost
#          fires on a camera move over 200 m)
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
Q=q122; K=3b19271; O=Profile-$K
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q job 80 streaming spikes (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
[ -f "$P/Builds/$O/.bench2-$K" ] || "$S/shelf.sh" get $K "$P" $O || true
[ -d "$P/Builds/$O" ] && { python "$S/runnercheck.py" "$(cygpath -w "$P/Builds/$O")" || rm -rf "${P:?}/Builds/$O"; }
build $K BuildDev $O || { echo "build failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"; rm -f "$R"/$Q-*.csv
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
view() { case $1 in
  cave)   echo "-goto 1572,8,-2635 -benchstops hereentry";;
  tunnel) echo "-goto -1596,1164 -benchstops line -benchline -1596,1164,-1590,1264,4,0.1";;
  jump)   echo "-benchstops townjump";; esac; }
for v in cave tunnel jump; do for r in 1 2 3; do
  n=medium-$v; tag=$Q-$n-cs100-$r; pf=(); shot=()
  [ $r = 1 ] && { pf=(-profiler-enable -profiler-log-file "$W/q-$tag.raw" -profiler-maxusedmemory 1073741824); shot=(-benchshots "$W/$Q-shots/$n"); }
  BENCHW=1920 BENCHH=1080 runcsv $O $Q-$n $K s100 $r -gfx Medium -fpscap 0 $(view $v) -benchhour 12 -benchweather Clear -hitchms 8 "${pf[@]}" "${shot[@]}"
  f="$R/q-$tag.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o '1% low [0-9.]*\|median [0-9.]* ms\|gpu [0-9.]* ms\|main thread [0-9.]* ms' | tr '\n' ' ') frames>8ms $(grep -c 'BenchRoute: hitch' "$f")"
done; done
for v in cave tunnel jump; do
  t=$Q-medium-$v-cs100-1; [ -f "$R/q-$t.raw" ] || continue
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze \
      -raws "$(cygpath -w "$R/q-$t.raw")" -out "$(cygpath -w "$R/$t.txt")" -over 8 -frames "$(cygpath -w "$R/$t-frames.csv")" -logFile "$W/analyze-$t.log" )
  echo "analyze $t rc=$?"
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"; strays >/dev/null
  [ -f "$R/$t.txt" ] && { rm -f "$R/q-$t.raw"; echo "  $(grep -c '^OVER' "$R/$t.txt") frames over 8 ms"; }
done
python "$B/report.py" "$R/$Q"
echo $Q done
