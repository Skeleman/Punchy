#!/bin/bash
# Job 42: fix the Summit stall's swapchain wait (job 41). b/fix-swapchain-stall c9672fcc (main 33fe6457 + "-vsync N" and
# "-maxqueued N" from script), Profile build, job 41's setup (Summit facing out, pitched up 10, 1920x1080, -gfx High,
# -weatherseed 4, noon, uncapped, -lowres half, plain weather), D3D11 default. Arms:
#   A: no flags (control)  B: -maxqueued 1  C: -maxqueued 3  D: -vsync 1  E: -force-d3d11-no-singlethreaded
# Each round runs one fresh game per arm, in shuffled order, up to 24 rounds. A stall is a frame of 30 s or more (the
# game's " s frame at " line), or a log that stays quiet 120 s: then the game is killed by PID, the first time in each arm
# after cdb confirms its stack (freezestacks.ps1). freezestall.ps1's own cdb attach is off while results/.nocdb exists,
# since an attach ends a stall. Pass is 0 stalls in 24 games, so an arm drops out at its 3rd stall (it can't pass, and 3 is
# enough to compare its rate); A keeps going. Stops when every arm but A has 24 games or has dropped.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
K=c9672fc; O=Profile-${K}m4; Q=q72
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q swapchain stall fix (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock" "$R/.nocdb"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build $K BuildDev $O || { echo "build $O failed"; exit 1; }
keepawake
touch "$R/.nocdb"
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; ST="$R/$Q-stalls.txt"; mkdir -p "$L"
SUMMIT="-benchstops place -benchplace Summit -benchview place,-10"
COMMON="-gfx High -fpscap 0 -lowres half"
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
flags() { case $1 in A) ;; B) echo -maxqueued 1 ;; C) echo -maxqueued 3 ;; D) echo -vsync 1 ;; E) echo -force-d3d11-no-singlethreaded ;; esac; }
gamepid() { timeout 20 powershell -NoProfile -Command "(Get-Process Punchy -ErrorAction SilentlyContinue | Where-Object { \$_.Path -like '*\\Builds\\Bench\\Punchy.exe' } | Select-Object -First 1).Id" 2>/dev/null | tr -d '\r'; }
watchdog() { # tag arm: while the game runs, kill it by PID once its log has been quiet 120 s (cdb first, once per arm)
  local lg="$R/q-$1.log" a=$2 last= since=$(date +%s) sz pid n
  while :; do
    sleep 5
    pid=$(gamepid); [ -n "$pid" ] && [ -f "$lg" ] || { since=$(date +%s); continue; }
    sz=$(stat -c %s "$lg" 2>/dev/null || echo 0)
    [ "$sz" = "$last" ] || since=$(date +%s); last=$sz
    [ $(( $(date +%s) - since )) -ge 120 ] || continue
    if [ ! -f "$R/$Q-stack-$a" ]; then
      touch "$R/$Q-stack-$a"
      timeout 600 powershell -NoProfile -File "$(cygpath -w "$S/freezestacks.ps1")" -Id $pid >/dev/null 2>&1
      n=$(stat -c %s "$lg" 2>/dev/null || echo 0)
      echo "$(date +%T) $1: quiet 120 s, cdb stack to freezestacks-$pid.txt; log $([ "$n" = "$sz" ] && echo 'still quiet' || echo 'moved after the attach'); killed pid $pid" >> "$ST"
      [ "$n" = "$sz" ] || echo "$1 resumed" >> "$R/$Q-resumed.txt"
    else
      echo "$(date +%T) $1: quiet 120 s, killed pid $pid" >> "$ST"
    fi
    timeout 20 powershell -NoProfile -Command "Stop-Process -Id $pid -Force" >/dev/null 2>&1
    echo "$1" >> "$R/$Q-killed.txt"
    return
  done
}
stalls() { # tag: this game's stalls (frames of 30 s or more, plus a kill unless the attach let the stalled frame end)
  local lg="$R/q-$1.log" s k=0
  s=$(grep -h ' s frame at ' "$lg" 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($(i+1)=="s" && $(i+2)=="frame" && $i+0>=30) c++} END{print c+0}')
  grep -qx "$1" "$R/$Q-killed.txt" 2>/dev/null && ! grep -qx "$1 resumed" "$R/$Q-resumed.txt" 2>/dev/null && k=1
  echo $((s + k))
}
rm -f "$R"/$Q-*.csv "$L"/q-$Q-*.log "$ST" "$R"/$Q-stack-* "$R/$Q-killed.txt" "$R/$Q-resumed.txt"
declare -A NS NG; for a in A B C D E; do NS[$a]=0; NG[$a]=0; done
for r in $(seq 1 24); do
  live=; for a in B C D E; do [ ${NS[$a]} -lt 3 ] && live="$live $a"; done
  [ -z "$live" ] && { echo "every arm but A has dropped out (3 stalls) after round $((r - 1))"; break; }
  for a in $(shuf -e A $live); do
    n=$Q-$a-g$r; tag=$n-cs100-1
    watchdog $tag $a & wd=$!
    RUNTIMEOUT=1500 BENCHW=1920 BENCHH=1080 runcsv $O $n $K s100 1 $COMMON $SUMMIT $(flags $a)
    kill $wd 2>/dev/null; wait $wd 2>/dev/null
    lg="$R/q-$tag.log"; check "$lg"
    s=$(stalls $tag); NS[$a]=$((NS[$a] + s)); NG[$a]=$((NG[$a] + 1))
    echo "$(date +%T) r$r $a: stalls $s (arm ${NS[$a]} in ${NG[$a]}) | $(grep -hE 'Version: +Direct3D' "$lg" | head -1 | tr -s ' ') | $(grep -h 'FrameRateCap: vSync' "$lg" | head -1 | cut -c1-80) | $(grep -h ' s frame at ' "$lg" | head -2 | cut -c1-90 | tr '\n' ' ')$(grep -qx "$tag" "$R/$Q-killed.txt" 2>/dev/null && echo KILLED)"
  done
done
echo "== stalls (frames of 30 s or more, or 120 s quiet and killed) per arm"
for a in A B C D E; do echo "$a $(flags $a): ${NS[$a]} stalls in ${NG[$a]} games"; done
echo "== watchdog"; cat "$ST" 2>/dev/null
echo "== fps at the Summit, games without a stall only"
for a in A B C D E; do
  for f in "$R"/$Q-$a-g*.csv; do
    t=$(basename $f .csv); [ "$(stalls $t-cs100-1)" = 0 ] && tail -n +2 "$f"
  done | awk -F, -v a=$a '{n++; f+=$5; l+=$6; c+=$9; g+=$10} END{if(n) printf "%s: %d games, avg %.1f fps, 1%% low %.1f, cpu %.2f ms, gpu %.2f ms\n", a, n, f/n, l/n, c/n, g/n}'
done
echo $Q done
