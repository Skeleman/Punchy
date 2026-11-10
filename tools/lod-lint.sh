#!/usr/bin/env bash
# Rule 20 check: lists fixed-metre visual ranges a branch ADDS or CHANGES versus origin/main.
# Usage: tools/lod-lint.sh <branch-or-ref> [base]   (run from any Punchy worktree)
# Every hit must either use DetailPolicy.ScreenSize or say in a comment why it is gameplay/simulation
# (senses, AI, collision, streaming). The supervisor runs this on every branch before merging.
ref="${1:?branch or ref}"; base="${2:-origin/main}"
git diff -U0 "$base...$ref" -- 'Assets/*.cs' 'Assets/*.shader' 'Assets/*.hlsl' 'Assets/*.compute' |
  grep -E '^\+[^+]' |
  grep -E '(Range|Distance|Radius|Fade[A-Za-z]*|Far|Near|Cull[A-Za-z]*|Lod[A-Za-z]*)[A-Za-z0-9_]*\s*=\s*[0-9]+(\.[0-9]+)?f?\b|(distance|length)\([^)]*\)\s*[<>]=?\s*[0-9]+(\.[0-9]+)?f?\b|[<>]=?\s*[0-9]{2,}(\.[0-9]+)?f\b' |
  grep -v -E 'ScreenSize|DetailPolicy|// *(gameplay|sim|streaming|collision|AI|senses)' |
  sed 's/^+//' | cut -c1-200
exit 0
