#!/bin/bash
# Punchy bench runner: one command per job, blocking until done, ending in a short report it writes itself.
#
#   bench.sh smoke <commit> [<base commit>]   lane-batch smoke: townwalk + glide, cap 60 and 0, 2 rounds; compared with
#                                             the base commit's results (or baselines/current.csv); silent unless worse
#   bench.sh ab <a> <b> [stops]               both commits, rounds interleaved, compared (b against a)
#   bench.sh baseline <commit>                run the smoke matrix and keep it as baselines/current.csv
#   bench.sh profile <commit> <stop> [cap]    dev build, a .raw capture of one stop, analysed to results/prof-<c>-<stop>.txt
#   bench.sh profdiff <a.txt> <b.txt>         the markers that grew most from a to b (mean, and on slow frames), spike counts
#   bench.sh bisect <good> <bad> <stop> [gpu|cpu|low1|avg]
#                                             first-parent bisect on one stop (cap 0, 2 rounds per step) to the first bad
#                                             commit; at a merge it lists the merged commits for the next step
#   bench.sh mp <commit>                      host solo vs host with a local client joined (-join 127.0.0.1, -autowalk)
#
# Rules it keeps: one job at a time (a lock), stray batchmode Unity and bench games killed before each build, a timeout
# on every step, runs from one fixed exe path (rule 14: Builds/Bench), keep-awake while it runs, no dialogs touched.
# Results: results/<tag>.csv per run, results/report-*.txt per job; the last line printed is the verdict.
set -u
B=$(cd "$(dirname "$0")" && pwd)
. "$B/lib.sh"

job=${1:-}; shift || true
mkdir -p "$R"
case "$job" in smoke|baseline|ab|profile|bisect|mp)
  # One job at a time (two queues kill each other's builds): mkdir is atomic; a lock left by a dead job is taken over.
  if ! mkdir "$R/.lock" 2>/dev/null; then
    pid=$(cat "$R/.lock/pid" 2>/dev/null)
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then echo "another bench job is running: $(cat $R/.lock/job)"; exit 2; fi
  fi
  echo $$ > "$R/.lock/pid"; echo "$job $* (since $(date +%T))" > "$R/.lock/job"
  trap 'rm -rf "$R/.lock"' EXIT
  keepawake ;;
esac

case "$job" in
  smoke)
    c=$(short $1); base=${2:+$(short $2)}
    build $c Build Bench-$c || fail "build $c failed"
    [ -n "$base" ] && { build $base Build Bench-$base || fail "build $base failed"; }
    for r in 1 2; do for cap in 60 0; do
      runcsv Bench-$c smoke-$c "$c" $cap $r -benchstops townwalk,glide -fpscap $cap
      [ -n "$base" ] && runcsv Bench-$base smoke-$base "$base" $cap $r -benchstops townwalk,glide -fpscap $cap
    done; done
    if [ -n "$base" ]; then compare "$R/smoke-$base.csv" "$R/smoke-$c.csv" "$c vs $base"
    elif [ -f "$BASE/current.csv" ]; then compare "$BASE/current.csv" "$R/smoke-$c.csv" "$c vs baseline $(cat $BASE/current.commit 2>/dev/null)"
    else report "smoke $c: no baseline to compare with"; table "$R/smoke-$c.csv"; fi
    ;;
  baseline)
    c=$(short $1)
    build $c Build Bench-$c || fail "build $c failed"
    for r in 1 2; do for cap in 60 0; do runcsv Bench-$c base-$c "$c" $cap $r -benchstops townwalk,glide -fpscap $cap; done; done
    mkdir -p "$BASE"; cp "$R/base-$c.csv" "$BASE/current.csv"; echo $c > "$BASE/current.commit"
    report "baseline is now $c"; table "$BASE/current.csv"
    ;;
  ab)
    a=$(short $1); b=$(short $2); stops=${3:-town,townwalk,glide}
    build $a Build Bench-$a || fail "build $a failed"; build $b Build Bench-$b || fail "build $b failed"
    for r in 1 2; do
      runcsv Bench-$a ab-$a "$a" 0 $r -benchstops $stops -fpscap 0
      runcsv Bench-$b ab-$b "$b" 0 $r -benchstops $stops -fpscap 0
    done
    compare "$R/ab-$a.csv" "$R/ab-$b.csv" "$b vs $a"
    ;;
  profile)
    c=$(short $1); stop=$2; cap=${3:-0}
    build $c BuildDev Profile-$c || fail "dev build $c failed"
    profile Profile-$c prof-$c-$stop $stop -fpscap $cap
    report "profile $c $stop: $(grep -E '^frames used' $R/prof-$c-$stop.txt 2>/dev/null), $(grep -oE 'SPIKES \(frames > 33.3 ms\): [0-9]+ of [0-9]+' $R/prof-$c-$stop.txt 2>/dev/null)"
    ;;
  profdiff)
    profdiff "$1" "$2"
    ;;
  bisect)
    bisect "$(short $1)" "$(short $2)" "$3" "${4:-gpu}"
    ;;
  mp)
    c=$(short $1)
    build $c Build Bench-$c || fail "build $c failed"
    mpsmoke Bench-$c $c
    ;;
  *)
    sed -n '2,20p' "$0"; exit 1 ;;
esac
