#!/bin/bash
# Job 115 bisect: which main merge between 9a1694e (good, 11-14 20:44) and 814a4da (bad, 11-17) brought the ~0.5 s
# present-wait frames? Over main's first-parent line (140 commits). Each step: one job.sh spec with the midpoint's
# build and 2 games at the town spot 2988.8,-920 (q159: good builds 0-1 frames over 450 ms per game, bad ones 10-12).
# A step is bad at >= 6 per game on average, good at <= 2; else 2 more games decide (>= 4 bad). Writes results/b115.log.
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts; R=$HOME/Projects/Punchy-wt/Tools/bench/results; P=$HOME/Projects/Punchy
L=$R/b115.log
mapfile -t line < <(git -C "$P" rev-list --first-parent --reverse 814a4da ^9a1694e | cut -c1-7)   # oldest first; last = 814a4da
lo=-1; hi=$((${#line[@]} - 1))   # line[lo] good (-1 = 9a1694e), line[hi] bad
echo "$(date +%T) bisect over ${#line[@]} commits" >> $L
while [ $((hi - lo)) -gt 1 ]; do
  m=$(( (lo + hi) / 2 )); c=${line[$m]}; step=b115-$c
  cat > $S/$step.job <<J
Q=$step; TITLE="Job 115 bisect $c"
BUILDS="$c"; VIEWS="spot"
view() { echo "-goto 2988.8,-920 -benchstops spot -benchspot 2988.8,-920,0 -benchhour 12 -benchweather Clear -hitchms 200"; }
ARMS="X"; arm() { echo Profile-$c; }
ROUNDS=\${B115ROUNDS:-2}; GFX="High"
J
  n=0; g=0
  for rounds in 2 4; do
    B115ROUNDS=$rounds bash $S/job.sh $S/$step.job > $R/$step.out 2>&1 || { echo "$(date +%T) $c: job failed: $(tail -2 $R/$step.out | tr '\n' ' ')" >> $L; exit 1; }
    n=0; g=0
    for f in $R/q-$step-spot-X-cs100-*.log; do g=$((g + 1)); n=$((n + $(grep -a 'BenchRoute: hitch' $f | awk '$3>450' | wc -l))); done
    [ $g -gt 0 ] || { echo "$(date +%T) $c: no games" >> $L; exit 1; }
    [ $((n)) -ge $((6 * g)) ] && break; [ $((n)) -le $((2 * g)) ] && break
  done
  if [ $n -ge $((4 * g)) ]; then hi=$m; v=bad; else lo=$m; v=good; fi
  echo "$(date +%T) $c $(git -C "$P" log -1 --format=%s $c | cut -c1-70): $n frames over 450 ms in $g games: $v ($((hi - lo - 1)) left)" >> $L
done
echo "$(date +%T) FIRST BAD: ${line[$hi]} $(git -C "$P" log -1 --format=%s ${line[$hi]}); last good: $([ $lo -ge 0 ] && echo ${line[$lo]} || echo 9a1694e)" >> $L
