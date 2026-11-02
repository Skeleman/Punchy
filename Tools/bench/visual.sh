#!/bin/bash
# D3D11 vs D3D12 look check on one archived build: day (town creatures, cave, lake, river), night (town windows and fires,
# night grove), the Electric spires at night; each run's exit code; then 2 quits per cap under D3D11.
B=$(cd "$(dirname "$0")" && pwd); . "$B/lib.sh"
out=${1:-Bench-09c9b55}
fixed $out || exit 1
go() { # api tag args...
  local api=$1 tag=$2; shift 2; local flag=""; [ $api = d3d11 ] && flag=-force-d3d11
  mkdir -p "$R/vis-$tag-$api"; cool
  ( cd "$P/Builds/Bench" && timeout 600 ./Punchy.exe $flag -logFile "$W/q-vis-$tag-$api.log" -benchroute -weatherseed 4 -time 12 -fpscap 0 -benchshots "$W/vis-$tag-$api" "$@" $(screen Bench 1280 720) >/dev/null 2>&1 )
  echo "$tag $api exit=$? exceptions=$(grep -c Exception $R/q-vis-$tag-$api.log) $(grep -oE 'BenchRoute: [a-z]+: avg [0-9.]+ fps, 1% low [0-9.]+' $R/q-vis-$tag-$api.log | sed 's/BenchRoute: //' | tr '\n' ';')"
  strays >/dev/null
}
for api in d3d11 d3d12; do
  go $api day -benchstops town,cave,lake,river
  go $api night -benchstops town,nightgrove -benchhour 21
  go $api spire -benchstops spot -benchspot -960,-3960,0 -benchhour 23 -moonphase 0.5 -bluemoondays 0
done
for cap in 60 0 30; do for i in 1 2; do
  ( cd "$P/Builds/Bench" && timeout 300 ./Punchy.exe -force-d3d11 -logFile "$W/q-quit-$cap-$i.log" -benchroute -benchstops town -fpscap $cap $(screen Bench 1280 720) >/dev/null 2>&1 )
  echo "quit d3d11 cap $cap #$i exit=$?"
done; done
echo "== VISUAL done"
