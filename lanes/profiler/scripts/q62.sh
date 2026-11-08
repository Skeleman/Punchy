#!/bin/bash
# The freeze (job 32 interim and addendum): is it main's code, the bench runner, or the laptop? At job 29's lake spot
# (-1624,-488), where main froze 2 of 2 overnight, a fresh game per run, Profile builds, 1920x1080, -gfx High,
# -weatherseed 4, uncapped, noon:
#   m:     main 46012623 built 04:18 with the runner of then (froze at the Summit and the lake overnight)
#   m2:    main 46012623 built 10:11 with today's runner (no freeze in 8 Summit sessions since 10:15)
#   j30:   job 30's 346cf4d9 with the same runner as m (ran the lake spot cleanly under RenderDoc at 03:15)
#   route: the bench route's spot stop; goto: no route, "-goto -1624,-488" and a 90 s shot list (the game's own start)
# Order: m-route m2-route j30-route m-goto m2-goto, then reversed. A freeze shows as a gap between the game's own
# "Net status" lines (every ~10 s), so it reads the same with or without the route; plus the runner's long-frame
# lines where the runner has them.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
Q=q62
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q freeze A/B (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
keepawake
L="$R/${Q}logs"; mkdir -p "$L"
out() { case $1 in m) echo Profile-4601262m ;; m2) echo Profile-4601262m2 ;; j30) echo Profile-346cf4dl ;; esac; }
gaps() { # log: the longest gaps between the game's "Net status" lines (wall clock), and the runner's own lines
  python - "$1" <<'PY'
import re, sys
t = []
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    m = re.search(r"Net status \(solo\) (\d+):(\d+):(\d+\.\d+)", line)
    if m:
        t.append(int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)))
g = sorted(((b - a, a) for a, b in zip(t, t[1:])), reverse=True)[:3]
print("  status lines %d, longest gaps %s" % (len(t), ", ".join("%.0f s at %02d:%02d:%02d" % (d, a // 3600, a % 3600 // 60, a % 60) for d, a in g)))
PY
  grep -h ' s frame at \|BenchRoute: spot: avg' "$1" | sed 's/, gfx.*//' | cut -c1-170 | sed 's/^/  /'
}
one() { # build-key mode round
  local b=$1 mode=$2 r=$3 tag=$Q-$1-$2-$3
  if [ $mode = route ]; then
    RUNTIMEOUT=1500 BENCHW=1920 BENCHH=1080 run $(out $b) $tag -gfx High -fpscap 0 -benchstops spot -benchspot -1624,-488,0
  else
    echo "-wait 90 -shot a" > "$R/$Q-list.txt"
    fixed $(out $b) || exit 1; cool
    ( cd "$P/Builds/Bench" && timeout 1500 ./Punchy.exe -logFile "$W/q-$tag.log" $(screen Bench 1920 1080) -goto -1624,-488 \
        -time 12 -weatherseed 4 -gfx High -fpscap 0 -shotlist "$W/$Q-list.txt" -shotdir "$W/$Q-shots/$tag" -quit >/dev/null 2>&1 )
    [ $? -eq 124 ] && echo "$tag: TIMED OUT"
    strays >/dev/null
  fi
  grep -q 'Crash!!!\|Received signal' "$R/q-$tag.log" && echo "CRASH in q-$tag.log"
  cp "$R/q-$tag.log" "$L/"
  echo "== $tag ($(date +%T))"; gaps "$R/q-$tag.log"
}
mkdir -p "$R/$Q-shots"
for r in 1 2; do
  ORDER="m:route m2:route j30:route m:goto m2:goto"
  [ $r = 2 ] && ORDER="m2:goto m:goto j30:route m2:route m:route"
  for v in $ORDER; do one ${v%%:*} ${v#*:} $r; done
done
echo $Q done
