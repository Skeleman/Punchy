#!/bin/bash
# D3D12 look check of one commit: day (town, cave, lake, river), 21:00 (town, night grove), the spires at night; API and
# flags warning in the log; screenshots in results/lk-<c>-*.
B=$(cd "$(dirname "$0")" && pwd); . "$B/lib.sh"
c=${1:-a78354c}
git -C $P fetch -q origin
build $c Build Bench-$c || { echo "build failed"; grep -m5 "error CS" $R/build-Bench-$c.log; exit 1; }
fixed Bench-$c
look() { local n=$1; shift; mkdir -p "$R/lk-$c-$n"; cool
  ( cd $P/Builds/Bench && timeout 600 ./Punchy.exe -logFile "$W/q-lk-$c-$n.log" -benchroute -weatherseed 4 -time 12 -fpscap 0 -benchshots "$W/lk-$c-$n" "$@" $(screen Bench 1280 720) >/dev/null 2>&1 )
  echo "look $n exit=$? api=$(grep -m1 -oE 'Direct3D 1[12]' $R/q-lk-$c-$n.log) invalid=$(grep -c 'Invalid combination' $R/q-lk-$c-$n.log) exc=$(grep -c Exception $R/q-lk-$c-$n.log) $(grep -oE 'BenchRoute: [a-z]+: avg [0-9.]+ fps, 1% low [0-9.]+' $R/q-lk-$c-$n.log | sed 's/BenchRoute: //' | tr '\n' ';')"
  strays >/dev/null; }
look day -benchstops town,cave,lake,river
look night -benchstops town,nightgrove -benchhour 21
look spire -benchstops spot -benchspot -960,-3960,0 -benchhour 23 -moonphase 0.5 -bluemoondays 0
echo "== LOOK done"
