#!/bin/bash
# A bench job from a short spec, instead of a hand-copied q-script (Zach 11-11: script the repeated steps).
#   job.sh <spec>       the spec is a bash file defining:
#     Q=q96                       the job's tag (results/<Q>-*.csv, <Q>logs/, <Q>-shots/)
#     TITLE="..."                 for the lock and the log
#     BUILDS="a1cb189 60edfac"    commits to build (Profile-<commit>, or OUT_<commit>=<outdir> to reuse another name)
#     VIEWS="town north"          and view() { case $1 in town) echo "...flags...";; esac; }
#     ARMS="r10 main"             and arm() { case $1 in r10) echo "Profile-a1cb189 -flag x";; esac; }  (outdir, flags)
#     ROUNDS=3 GFX="High"         rounds (arm order rotated by one each round, reversed every other) and presets
#     REF=main BAR=0.3            report.py's reference arm and pass line (optional)
#     CYCLE=""                    optional -benchcycle spec: then each arm is one game holding every cycle arm
#     before() / after()          optional hooks (RenderDoc captures, extra games) run after the builds / at the end
# Every game: 1920x1080, -fpscap 0, -weatherseed 4, -body default (lib.sh run), shots on round 1, a crash stops the job.
SPEC=$1
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
ROUNDS=3; GFX=High; REF=""; BAR=""; CYCLE=""; TITLE=""
before() { :; }; after() { :; }
. "$SPEC"
# DRY=1 job.sh <spec>: print the builds and games in order (no lock, builds or games), to check a spec.
if [ -n "${DRY:-}" ]; then
  build() { echo "build $1 -> $3"; }; runcsv() { echo "game $(env | grep '^PUNCHY_' | tr '
' ' ')$*"; }; check() { :; }; keepawake() { :; }; R=$(mktemp -d)
fi
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q $TITLE (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
for c in $BUILDS; do
  o=$(eval echo "\${OUT_$c:-Profile-$c}")
  build $c BuildDev $o || { echo "build $o failed"; exit 1; }
done
keepawake
[ -z "${DRY:-}" ] && ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; mkdir -p "$L" "$R/$Q-shots"
check() { [ -n "${DRY:-}" ] && return; grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # gfx view arm round
  local g=$1 v=$2 a=$3 r=$4 spec o n shot=""
  spec=$(arm $a); o=${spec%% *}; [ "$o" = "$spec" ] && spec="" || spec=${spec#* }
  # (Words "@VAR=value" in an arm are environment variables for that game, e.g. @PUNCHY_FARSKIRT=0.)
  local envs=() flags=() w
  for w in $spec; do [ "${w:0:1}" = "@" ] && envs+=("${w:1}") || flags+=("$w"); done
  spec="${flags[*]}"
  n=$v-$a; [ "$GFX" != "${GFX% *}" ] && n=${g,,}-$n
  [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  local cyc=(); [ -n "$CYCLE" ] && cyc=(-benchcycle "$CYCLE")
  ( for e in "${envs[@]}"; do export "$e"; done
    BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx $g -fpscap 0 $(view $v) $spec "${cyc[@]}" $shot )
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*[^:]*: avg' "$f" 2>/dev/null | sed 's/.*BenchRoute: //' | grep -o '^[^:]*\|1% low [0-9.]*\|median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" 2>/dev/null | cut -c1-70 | head -2 | tr '\n' ' ')"
}
rot() { local r=$1; shift; local a=("$@") n=$# i out=(); for ((i = 0; i < n; i++)); do out+=("${a[$(((i + r - 1) % n))]}"); done
        [ $((r % 2)) = 0 ] && for ((i = n - 1; i >= 0; i--)); do echo -n "${out[$i]} "; done || echo -n "${out[*]}"; }
before
rm -f "$R"/$Q-*.csv
for g in $GFX; do
  for v in $VIEWS; do
    for r in $(seq 1 $ROUNDS); do
      for a in $(rot $r $ARMS); do game $g $v $a $r; done
    done
  done
done
after
echo
[ -z "${DRY:-}" ] && python "$B/report.py" "$R/$Q" ${REF:+--ref $REF} ${BAR:+--bar $BAR}
echo $Q done
