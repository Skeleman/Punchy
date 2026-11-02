#!/bin/bash
# Release smoke test: solo free play on the clean build at Builds/Play (rule 14), default cap / uncapped / 30; logs and screenshots in results/ (or $SMOKEOUT).
S=${SMOKEOUT:-$(cd "$(dirname "$0")" && pwd)/results}; mkdir -p $S
W=$(cygpath -m $S); D="$(cygpath -w $(dirname "$0")/drive.ps1)"
G=${PLAYDIR:-/c/Users/zache/Projects/Punchy/Builds/Play}
drv() { powershell -NoProfile -File "$D" "$@" >/dev/null 2>&1; }
w() { timeout $1 tail -f /dev/null 2>/dev/null; }
for cap in default 0 30; do
  extra=""; [ $cap != default ] && extra="-fpscap $cap"
  cd $G; ./Punchy.exe -logFile "$W/smoke-$cap.log" -place HumanVillage -time 12 $extra >/dev/null 2>&1 &
  w 45
  drv key 87 -ms 8000; drv look 600; drv key 16 -ms 100; drv key 87 -ms 10000; drv key 32 -ms 100; drv key 87 -ms 6000
  drv look -600; drv key 16 -ms 100; drv key 87 -ms 15000; drv wshot "$(cygpath -w $S/smoke-$cap.png)"
  drv key 112 -ms 100; w 2; drv wshot "$(cygpath -w $S/smoke-$cap-f1.png)"; drv key 112 -ms 100
  drv key 87 -ms 20000; w 30
  powershell -NoProfile -Command "Get-Process Punchy -ErrorAction SilentlyContinue | ForEach-Object { \$_.CloseMainWindow() | Out-Null }"; w 10
  powershell -NoProfile -Command "Stop-Process -Name Punchy -Force -ErrorAction SilentlyContinue"; w 3
  L=$S/smoke-$cap.log
  echo "== cap $cap: lines $(wc -l < $L), exceptions $(grep -c 'Exception' $L), errors $(grep -ciE '^error|error:' $L), batch warnings $(grep -c 'Server Tick Batching' $L), batch debug $(grep -c 'tick batching has occured' $L), crash $(grep -ciE 'crash|fatal' $L)"
  grep -E "Exception" $L | sort | uniq -c | sort -rn | head -5 | cut -c1-200
done
