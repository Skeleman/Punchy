#!/usr/bin/env bash
# Waits for the profiler to push a report, without any model calls: plain git fetches on a timer.
#   bash tools/watch-profiler.sh [max hours, default 12] [poll minutes, default 5]
# Watches origin/coordination's REPORTS-profiler.md. Only the profiler writes it; BENCH-QUEUE.md is left out so the
# watching agent's own queue pushes don't wake it. When the report changes, it prints the new commits and the lines
# added, then exits, so an agent that started it in the background is woken with the news. It also ends on its own
# at the time limit, or if the shell that started it goes away (rule 18).
# Only `git fetch` runs: the working tree and local branch are never touched.
cd "$(dirname "$0")/.." || exit 3
hours=${1:-12}; every=$(( ${2:-5} * 60 ))
parent=$PPID
end=$((SECONDS + hours * 3600))
report=REPORTS-profiler.md
timeout 120 git fetch -q origin coordination 2>/dev/null
start=$(git rev-parse origin/coordination)
was=$(git rev-parse "$start:$report" 2>/dev/null)
echo "watching origin/coordination from ${start:0:8}, every $((every / 60)) min, up to ${hours} h"
while :; do
  sleep "$every"
  if ! kill -0 "$parent" 2>/dev/null; then echo "watch: parent gone"; exit 2; fi
  if [ $SECONDS -ge $end ]; then echo "watch: no profiler report within ${hours} h"; exit 1; fi
  timeout 120 git fetch -q origin coordination 2>/dev/null || continue
  now=$(git rev-parse origin/coordination)
  [ "$now" = "$start" ] && continue
  if [ "$(git rev-parse "$now:$report" 2>/dev/null)" != "$was" ]; then
    echo "watch: profiler report changed (${start:0:8} -> ${now:0:8})"
    git log --format='  %h %an: %s' "$start..$now" -- "$report"
    echo "--- lines added to $report (first 60) ---"
    git diff "$start" "$now" -- "$report" | grep '^+[^+]' | cut -c2- | head -60
    exit 0
  fi
done
