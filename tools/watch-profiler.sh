#!/usr/bin/env bash
# Waits for the profiler to push a report, without any model calls: plain git fetches on a timer.
#   bash tools/watch-profiler.sh [max hours, default 12] [poll minutes, default 5]
# Watches origin/coordination's REPORTS-profiler.md and BENCH-QUEUE.md. When either changes, it prints the new
# commits and the lines added to the report, then exits, so an agent that started it in the background is woken
# with the news. It also ends on its own at the time limit, or if the shell that started it goes away (rule 18).
# Only `git fetch` runs: the working tree and local branch are never touched.
cd "$(dirname "$0")/.." || exit 3
hours=${1:-12}; every=$(( ${2:-5} * 60 ))
parent=$PPID
end=$((SECONDS + hours * 3600))
files="REPORTS-profiler.md BENCH-QUEUE.md"
timeout 120 git fetch -q origin coordination 2>/dev/null
start=$(git rev-parse origin/coordination)
blobs() { git rev-parse "$1:REPORTS-profiler.md" "$1:BENCH-QUEUE.md" 2>/dev/null | tr '\n' ' '; }
was=$(blobs "$start")
echo "watching origin/coordination from ${start:0:8}, every $((every / 60)) min, up to ${hours} h"
while :; do
  sleep "$every"
  if ! kill -0 "$parent" 2>/dev/null; then echo "watch: parent gone"; exit 2; fi
  if [ $SECONDS -ge $end ]; then echo "watch: no profiler update within ${hours} h"; exit 1; fi
  timeout 120 git fetch -q origin coordination 2>/dev/null || continue
  now=$(git rev-parse origin/coordination)
  [ "$now" = "$start" ] && continue
  if [ "$(blobs "$now")" != "$was" ]; then
    echo "watch: profiler files changed (${start:0:8} -> ${now:0:8})"
    git log --format='  %h %an: %s' "$start..$now" -- $files
    echo "--- lines added to REPORTS-profiler.md (first 60) ---"
    git diff "$start" "$now" -- REPORTS-profiler.md | grep '^+[^+]' | cut -c2- | head -60
    echo "--- BENCH-QUEUE.md changes ---"
    git diff --stat "$start" "$now" -- BENCH-QUEUE.md
    exit 0
  fi
done
