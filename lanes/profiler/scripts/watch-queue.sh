#!/bin/bash
# Waits, without an agent, for new profiler work: polls the coordination branch every POLL seconds (git ls-remote: one
# cheap request, no API quota) and the open lane:profiler issues every ISSUE_POLL seconds (gh), and exits with a short
# summary as soon as either changes. Run it in the background from an agent session: the session is woken when it
# exits, so waiting costs nothing.
#   watch-queue.sh [paths...]   paths on the coordination branch that count (default BENCH-QUEUE.md lanes/profiler)
#   POLL=60 ISSUE_POLL=300 MAX_HOURS=12 IGNORE="REPORTS-profiler|profiler:" watch-queue.sh (IGNORE: own commit subjects)
#   SELF_SESSION=<the session's Claude-Session URL>: commits carrying it are the watcher's own session's, also ignored
# Exit 0: something changed (printed). Exit 3: MAX_HOURS passed with nothing new (start it again).
C=$HOME/Projects/Punchy-coordination
BRANCH=coordination
POLL=${POLL:-60}; ISSUE_POLL=${ISSUE_POLL:-300}; MAX_HOURS=${MAX_HOURS:-12}
IGNORE=${IGNORE:-REPORTS-profiler|profiler:}
PATHS=("$@"); [ ${#PATHS[@]} -eq 0 ] && PATHS=(BENCH-QUEUE.md lanes/profiler)

issues() { timeout 60 gh issue list -R Skeleman/Punchy --label lane:profiler --state open --json number,title \
             --jq '.[] | "#\(.number) \(.title)"' 2>/dev/null | sort; }

base=$(git -C "$C" rev-parse "origin/$BRANCH")
seen_issues=$(issues)
start=$(date +%s); last_issue_check=$start
echo "watching $BRANCH from ${base:0:8} (${PATHS[*]}), every ${POLL}s; issues every ${ISSUE_POLL}s; since $(date +%T)"
while :; do
  now=$(date +%s)
  [ $((now - start)) -ge $((MAX_HOURS * 3600)) ] && { echo "nothing new after ${MAX_HOURS} h"; exit 3; }
  head=$(timeout 60 git -C "$C" ls-remote origin "refs/heads/$BRANCH" 2>/dev/null | cut -f1)
  if [ -n "$head" ] && [ "$head" != "$base" ]; then
    timeout 120 git -C "$C" fetch -q origin "$BRANCH"
    # Only others' commits count: the profiler's own (subjects matching IGNORE) would wake it for its own reports.
    commits=$(git -C "$C" log --format='%h %s' "$base..$head" -- "${PATHS[@]}" | grep -Ev "^[0-9a-f]+ ($IGNORE)")
    if [ -n "$SELF_SESSION" ] && [ -n "$commits" ]; then
      commits=$(echo "$commits" | while read -r h rest; do
        git -C "$C" show -s --format=%B "$h" | grep -qF "$SELF_SESSION" || echo "$h $rest"; done)
    fi
    if [ -n "$commits" ]; then
      echo "== $BRANCH ${base:0:8}..${head:0:8} at $(date +%T)"
      echo "$commits"
      for h in $(echo "$commits" | cut -d' ' -f1); do git -C "$C" show --name-only --format= "$h" -- "${PATHS[@]}"; done | sort -u
      exit 0
    fi
    base=$head   # other lanes' commits: keep waiting
  fi
  if [ $((now - last_issue_check)) -ge "$ISSUE_POLL" ]; then
    last_issue_check=$now
    cur=$(issues)
    new=$(comm -13 <(echo "$seen_issues") <(echo "$cur"))
    [ -n "$new" ] && { echo "== new lane:profiler issues at $(date +%T)"; echo "$new"; exit 0; }
    seen_issues=$cur
  fi
  sleep "$POLL"
done
