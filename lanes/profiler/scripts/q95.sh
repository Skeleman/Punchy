#!/bin/bash
# Follow-ups to jobs 49b and 45h (Zach 11-11: "attempt fixes"). Profile builds, 1920x1080, -gfx High, uncapped,
# -weatherseed 4, -body default, 3 rounds rotated; shots on round 1.
#   R-12: Punchy b/r12-trims c43b5eaf: plain, -skyoff 1024 (no warp), -skyoff 8192 (the march keeps the last warp on steps
#         800 m outside the cloud layer: a trial fix). Job 49's storm night and noon.
#   R-10: Punchy b/r10-trims a1cb1899 (R-10), b/r10-nocluster a378b7e6 (R-10 without the cluster variant in creatures,
#         plants, grass, pebbles and temple: prices the variant), main 60edfacb. Job 45e's town view (yaw 201) at 22:00.
#         Then one RenderDoc frame each of R-10 and main at the town, to name the draws that grew.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
Q=q95
OS=Profile-c43b5ea; OL=Profile-a1cb189; ON=Profile-a378b7e; OM=Profile-60edfacw
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-12 band / R-10 variant (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
build c43b5ea BuildDev $OS || { echo "build $OS failed"; exit 1; }
build a378b7e BuildDev $ON || { echo "build $ON failed"; exit 1; }
keepawake
ps -ef | grep -q '[f]reezesample.sh' || (nohup bash "$S/freezesample.sh" >/dev/null 2>&1 &)
L="$R/${Q}logs"; RD="${R:?}/rd95"; mkdir -p "$L" "$RD" "$R/$Q-shots"
spot() { case $1 in
  stormnight) echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 22 -benchstops here";;
  stormnoon)  echo "-goto 1489,-897 -shotcam 1489,88,-897,1889,98,-897 -benchweather Storm -benchweatherat 5489,-897 -benchhour 12 -benchstops here";;
  town)       echo "-benchweather Clear -benchhour 22 -benchstops spot -benchspot 2988.8,-920,201";; esac; }
check() { grep -q 'Crash!!!\|Received signal' "$1" && { echo "CRASH in $1"; grep -A15 'Crash!!!\|Received signal' "$1" | head -25; exit 1; }; cp "$1" "$L/"; }
game() { # out name round args...
  local o=$1 n=$2 r=$3; shift 3
  local shot=; [ $r = 1 ] && shot="-benchshots $W/$Q-shots/$n"
  BENCHW=1920 BENCHH=1080 runcsv $o $Q-$n ${o#Profile-} s100 $r -gfx High -fpscap 0 "$@" $shot
  local f="$R/q-$Q-$n-cs100-$r.log"; check "$f"
  echo "$(date +%T) $n r$r: $(grep -h 'BenchRoute: [a-z]*: avg' "$f" | grep -o 'median [0-9.]* ms\|gpu [0-9.]* ms\|cpu [0-9.]* ms' | tr '\n' ' ') $(grep -h ' s frame at ' "$f" | cut -c1-70)"
}
rot() { local r=$1; shift; local a=("$@") n=$#; local i; for ((i = 0; i < n; i++)); do echo -n "${a[$(((i + r - 1) % n))]} "; done; }
rm -f "$R"/$Q-*.csv
for s in stormnight stormnoon; do
  for r in 1 2 3; do
    for a in $(rot $r plain w1024 w8192); do
      case $a in plain) game $OS $s-$a $r $(spot $s);; *) game $OS $s-$a $r $(spot $s) -skyoff ${a#w};; esac
    done
  done
done
for r in 1 2 3; do
  for a in $(rot $r r10 nocluster main); do
    case $a in r10) o=$OL;; nocluster) o=$ON;; main) o=$OM;; esac
    game $o town-$a $r $(spot town)
  done
done
for f in "$R"/$Q-*.csv; do echo "== $(basename $f .csv)"; table "$f"; done
rdgame() { # out name args...
  local o=$1 n=$2; shift 2
  fixed $o || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/$n")" \
      "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd-$n.log" -benchroute -weatherseed 4 -time 22 -body ${BENCHBODY:-default} \
      $(screen Bench 1920 1080) -gfx High -fpscap 0 "$@" -renderdocstop spot 1 >/dev/null 2>&1 )
  [ $? -eq 124 ] && echo "rd $n: TIMED OUT"
  strays >/dev/null; check "$R/q-$Q-rd-$n.log"
  echo "rd $n: $(grep -h 'RenderDoc capture' "$R/q-$Q-rd-$n.log" | head -1)"
}
rm -f "$RD"/*.rdc
rdgame $OL town-r10 $(spot town); rdgame $OM town-main $(spot town); rdgame $ON town-nocluster $(spot town)
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-passes.txt")" RD_MAXDEPTH=6 RD_TOPDRAWS=25 timeout 3600 "$QRD" --python "$(cygpath -w "$S/rdpasses.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
echo $Q done
