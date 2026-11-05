#!/usr/bin/env bash
# Restart a lane session after a usage limit or crash.
# Usage: tools/resume-worker.sh <lane> [account-config-dir]
#   lane: world | animal | player | profiler
#   account-config-dir: optional CLAUDE_CONFIG_DIR for a second account
set -euo pipefail
lane="${1:?usage: resume-worker.sh <lane> [account-config-dir]}"
coord="$(cd "$(dirname "$0")/.." && pwd)"
[ -f "$coord/HANDOFF-$lane.md" ] || [ -f "$coord/REPORTS-$lane.md" ] || { echo "no HANDOFF/REPORTS for lane '$lane'"; exit 1; }
[ -n "${2:-}" ] && export CLAUDE_CONFIG_DIR="$2"
prompt="You are the $lane lane of Punchy. Read $coord/COORDINATION.md, then $coord/HANDOFF-$lane.md and the last entries of $coord/REPORTS-$lane.md. Check git status in your tree; anything marked UNVERIFIED gets verified first. Then continue your card from the next open row. Run the handoff steps (COORDINATION rule 16) after each row."
exec claude "$prompt"
