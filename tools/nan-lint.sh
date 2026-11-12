#!/usr/bin/env bash
# Rule 24 hint list (R-18, #282): shader lines that can make a NaN pixel, which bloom spreads into a white circle.
# Usage: tools/nan-lint.sh [ref]   (default origin/main; run from any Punchy worktree)
# A hint, not a gate: every hit is a place to look, and many are fine (a value that can't be 0 or negative). Fix real
# ones at the source (SafeNormalize / normalizesafe, max(d, 1e-5), saturate or max(x, 0) before pow and sqrt, and
# derivatives taken before any branch), or say why in a comment with "nan-safe" on the line to silence it.
# Flags:
#   normalize   normalize() of a difference or a cross() with no guard (max, 1e-, a ?: test) in the same line
#   divide      a division by length/distance/dot/sqrt/abs or by a .w (can be 0)
#   pow/sqrt    pow() or sqrt() whose first argument is not saturate/abs/max/a literal (negative -> NaN)
#   deriv       ddx/ddy/fwidth inside an if block (undefined in divergent flow; shows up as sparkles at edges)
ref="${1:-origin/main}"
git ls-tree -r --name-only "$ref" -- Assets |
  grep -E '\.(shader|hlsl|cginc|compute)$' | grep -v -E '^Assets/(Temp|_Recovery)/' |
  while IFS= read -r f; do
    git show "$ref:$f" 2>/dev/null | tr -d '\r' | awk -v F="$f" '
      function hit(kind) { line = $0; gsub(/^[ \t]+/, "", line); printf "%s:%d: %-9s %s\n", F, NR, kind, substr(line, 1, 150) }
      {
        code = $0; sub(/\/\/.*/, "", code)
        if (code ~ /^[ \t]*$/ || $0 ~ /nan-safe/) { track(); next }
        if (code ~ /[^A-Za-z_]normalize[ \t]*\(/ && code !~ /(SafeNormalize|normalizesafe|max\(|NormalizeNormalPerPixel|1e-|\?)/ &&
            (code ~ /normalize[ \t]*\([^;]*[A-Za-z0-9_)\]][ \t]*-[ \t]*[A-Za-z_(]/ || code ~ /normalize[ \t]*\([ \t]*cross/)) hit("normalize")
        if (code ~ /\/[ \t]*\(?[ \t]*(length|distance|dot|sqrt|abs)[ \t]*\(/ || code ~ /\/[ \t]*[A-Za-z_][A-Za-z0-9_]*\.w([^A-Za-z0-9_]|$)/) {
          if (code !~ /(max\(|1e-|> *0 *\?)/) hit("divide")
        }
        s = code
        while (match(s, /(^|[^A-Za-z_])(pow|sqrt)[ \t]*\([ \t]*/)) {
          rest = substr(s, RSTART + RLENGTH)
          if (rest !~ /^(saturate|abs|max|[0-9.]|half\(|float\()/) { hit(substr(s, RSTART, RLENGTH) ~ /pow/ ? "pow" : "sqrt"); break }
          s = rest
        }
        if (inIf > 0 && code ~ /(ddx|ddy|fwidth)(_fine|_coarse)?[ \t]*\(/) hit("deriv")
        track()
      }
      # Brace depth, and the depths at which an if block opened (single-line ifs count for their own line only).
      function track(   i, c, n) {
        if (code ~ /(^|[^A-Za-z_])if[ \t]*\(/) pendingIf = 1
        n = length(code)
        for (i = 1; i <= n; i++) {
          c = substr(code, i, 1)
          if (c == "{") { depth++; if (pendingIf) { ifAt[++inIf] = depth; pendingIf = 0 } }
          else if (c == "}") { if (inIf > 0 && ifAt[inIf] == depth) inIf--; depth-- }
          else if (c == ";" && pendingIf) pendingIf = 0
        }
      }'
  done
exit 0
