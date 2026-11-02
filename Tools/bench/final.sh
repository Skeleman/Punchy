#!/bin/bash
# Final v0.8.0 gate on one commit: a clean player build at Builds/Play (rule 14) and a bench build; quits at 3 caps on the
# clean build (exit codes, API); looks on the bench build's default API; town/townwalk/glide at caps 0 and 60, 2 rounds;
# the smoke test on the clean build.
B=$(cd "$(dirname "$0")" && pwd); . "$B/lib.sh"
S=$(dirname "$B"); c=${1:-4e3e4c1}
git -C $P fetch -q origin
build $c Build Bench-$c || { echo "bench build failed"; grep -m5 "error CS" $R/build-Bench-$c.log; exit 1; }
strays >/dev/null; ( cd $P && git checkout -q --detach $c ) || exit 1
cp "$B/BProfileTools.cs" $P/Assets/Game/Scripts/Editor/BProfileTools.cs
cp $P/Library/LastSceneManagerSetup.txt $R/LastSceneManagerSetup.bak
rm -rf $P/Builds/Play
( cd $P && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -buildTarget Win64 -executeMethod BProfileTools.Build -out Builds/Play/Punchy.exe -logFile "$W/build-Play-$c.log" )
cp $R/LastSceneManagerSetup.bak $P/Library/LastSceneManagerSetup.txt
( cd $P && git checkout -- ProjectSettings 2>/dev/null; rm -f Assets/Game/Scripts/Editor/BProfileTools.cs Assets/Game/Scripts/Editor/BProfileTools.cs.meta; git checkout -q $HOME_BRANCH )
strays >/dev/null
echo "clean build: $(grep -E 'BProfile build|error CS' $R/build-Play-$c.log | head -2 | tr '\n' ' ')"
[ -f $P/Builds/Play/Punchy.exe ] || exit 1; echo $c > $P/Builds/Play/.from
for cap in 60 0 30; do for i in 1 2; do
  r=$(powershell -NoProfile -File "$(cygpath -w $B/quitclean.ps1)" -Exe "$(cygpath -w $P/Builds/Play/Punchy.exe)" -Log "$(cygpath -w $R/q-fin-quit-$cap-$i.log)" -Extra "-fpscap $cap")
  echo "quit (clean build) cap $cap #$i: $r api=$(grep -m1 -oE 'Direct3D 1[12][^\r]*' $R/q-fin-quit-$cap-$i.log) invalid=$(grep -c 'Invalid combination' $R/q-fin-quit-$cap-$i.log) exc=$(grep -c Exception $R/q-fin-quit-$cap-$i.log)"
done; done
fixed Bench-$c
look() { local n=$1; shift; mkdir -p "$R/fin-$n"; cool
  ( cd $P/Builds/Bench && timeout 600 ./Punchy.exe -logFile "$W/q-fin-$n.log" -benchroute -weatherseed 4 -time 12 -fpscap 0 -benchshots "$W/fin-$n" "$@" $(screen Bench 1280 720) >/dev/null 2>&1 )
  echo "look $n exit=$? api=$(grep -m1 -oE 'Direct3D 1[12]' $R/q-fin-$n.log) invalid=$(grep -c 'Invalid combination' $R/q-fin-$n.log) exc=$(grep -c Exception $R/q-fin-$n.log)"
  strays >/dev/null; }
look day -benchstops town,cave,lake,river
look night -benchstops town,nightgrove -benchhour 21
look spire -benchstops spot -benchspot -960,-3960,0 -benchhour 23 -moonphase 0.5 -bluemoondays 0
rm -f $R/fin-$c.csv
for r in 1 2; do for cap in 60 0; do runcsv Bench-$c fin-$c $c $cap $r -benchstops town,townwalk,glide -fpscap $cap; done; done
echo "== perf $c"; table $R/fin-$c.csv
echo "J-006 (cap 60, 1% low under 30): $(awk -F, 'NR>1 && $2==60 && $6<30 { print $4 " r" $3 " " $6 }' $R/fin-$c.csv | tr '\n' ';')"
rm -f $R/smoke-*.log
SMOKEOUT=$R PLAYDIR=$P/Builds/Play timeout 1500 bash $B/smoke.sh 2>&1 | grep -v Segmentation
echo "smoke APIs: $(grep -h -m1 -oE 'Direct3D 1[12]' $R/smoke-*.log | sort | uniq -c | tr '\n' ' ')"
echo "Play size $(du -sm $P/Builds/Play | cut -f1) MB"
cd $P && git status --short
echo "== FINAL done"
