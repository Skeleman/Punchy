# Shared parts of bench.sh. B is this folder; it holds BenchmarkRoute.cs.txt (+ .meta.txt) and BProfileTools.cs, which
# are copied into the project only for the length of a build.
P=${PUNCHY:-/c/Users/zache/Projects/Punchy}
R=$B/results; BASE=$B/baselines
W=$(cygpath -m "$R"); WW=$(cygpath -w "$R")
UNITY=${UNITY:-"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe"}
SMI=/c/Windows/System32/nvidia-smi.exe
HOME_BRANCH=${HOME_BRANCH:-b/weather-mapjob}   # what the main checkout is left on after a build
NOISE=${NOISE:-5}                               # % a 1% low or average may drop before it counts as worse

log() { echo "[$(date +%T)] $*" | tee -a "$R/job.log"; }
report() { echo "$*" >> "$REPORT"; echo "$*"; }
fail() { report "FAILED: $*"; exit 1; }
REPORT=$R/report-$(date +%m%d-%H%M%S).txt
wait_s() { for i in $(seq 1 $1); do timeout 1 tail -f /dev/null 2>/dev/null; done; }
short() { git -C "$P" rev-parse --short=7 "$1" 2>/dev/null || echo "$1"; }
cool() { for i in $(seq 1 60); do t=$($SMI --query-gpu=temperature.gpu --format=csv,noheader 2>/dev/null || echo 0); [ "$t" -lt 65 ] && break; wait_s 5; done; }

keepawake() { # the helper ends by itself when no bench script or game is left
  # 'keepawake[.]ps1' can't match this check's own command line (or a shell's that contains it), only the helper's
  powershell -NoProfile -Command "if (-not (Get-CimInstance Win32_Process | Where-Object { \$_.Name -eq 'powershell.exe' -and \$_.CommandLine -match '-File .*keepawake[.]ps1' })) { exit 1 }" >/dev/null 2>&1 ||
    (nohup powershell -NoProfile -File "$(cygpath -w $B/keepawake.ps1)" >/dev/null 2>&1 &)
}

strays() { # batchmode Unity, bench games and orphaned bee_backend only; never an interactive editor or a game the user started
  powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { (\$_.Name -eq 'Unity.exe' -and \$_.CommandLine -match 'batchmode') -or (\$_.Name -eq 'Punchy.exe' -and \$_.CommandLine -match 'benchroute|benchclient') -or (\$_.Name -eq 'bee_backend.exe' -and -not (Get-CimInstance Win32_Process | Where-Object { \$_.Name -eq 'Unity.exe' -and \$_.CommandLine -notmatch 'batchmode' })) } | ForEach-Object { Write-Output ('killing stray ' + \$_.Name + ' ' + \$_.ProcessId); Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }"
}

dialogs() { # windows that can stall a run (Defender, firewall): listed in the report, never clicked
  powershell -NoProfile -Command "Get-Process | Where-Object { \$_.MainWindowTitle -match 'Security|Defender|Firewall|Unity' } | ForEach-Object { \$_.ProcessName + ': ' + \$_.MainWindowTitle }" 2>/dev/null | sort | uniq -c | tr '\n' ';'
}

build() { # commit Build|BuildDev outdir: archived per commit (A/Bs copy it into the fixed path to run)
  local c=$1 m=$2 out=$3
  [ -f "$P/Builds/$out/Punchy.exe" ] && [ -f "$P/Builds/$out/.bench2-$c" ] && return 0
  strays >/dev/null
  git -C "$P" fetch -q origin 2>/dev/null
  ( cd "$P" && git checkout -q --detach $c && [ "$(git rev-parse --short=7 HEAD)" = "$c" ] ) || { log "checkout $c failed"; return 1; }
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  cp "$B/BenchmarkRoute.cs.txt" "$P/Assets/Game/Scripts/Debugging/BenchmarkRoute.cs"
  cp "$B/BenchmarkRoute.cs.meta.txt" "$P/Assets/Game/Scripts/Debugging/BenchmarkRoute.cs.meta"
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  log "build $out ($c $m)"
  ( cd "$P" && timeout 3000 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -buildTarget Win64 -executeMethod BProfileTools.$m -out Builds/$out/Punchy.exe -logFile "$W/build-$out.log" )
  local rc=$?
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  ( cd "$P" && git checkout -- ProjectSettings 2>/dev/null
    rm -f Assets/Game/Scripts/Debugging/BenchmarkRoute.cs Assets/Game/Scripts/Debugging/BenchmarkRoute.cs.meta \
          Assets/Game/Scripts/Editor/BProfileTools.cs Assets/Game/Scripts/Editor/BProfileTools.cs.meta
    git checkout -q $HOME_BRANCH )
  strays >/dev/null
  local res=$(grep -E 'BProfile build|error CS' "$R/build-$out.log" | head -3 | tr '\n' ' ')
  [ $rc -eq 124 ] && res="TIMED OUT $res"
  log "  rc=$rc $res"
  echo "$res" | grep -q Succeeded && touch "$P/Builds/$out/.bench2-$c" && return 0
  return 1
}

screen() { # outdir w h: the test window, only for a build with the PunchyBench identity (BProfileTools.BenchIdentity).
            # Unity saves -screen-* flags as player prefs, so on the real game's build they would end up in Zach's settings
            # (F-01); an older build runs at whatever its prefs hold, and the log says so.
  if tail -n 1 "$P/Builds/$1/Punchy_Data/app.info" 2>/dev/null | grep -qx PunchyBench; then echo "-screen-fullscreen 0 -screen-width $2 -screen-height $3"
  else log "  $1 isn't a PunchyBench build: no -screen-* flags, so it runs at its saved window size (not comparable)" >&2; fi
}

fixed() { # outdir: copy an archived build into the one path games run from (rule 14)
  [ "$(cat $P/Builds/Bench/.from 2>/dev/null)" = "$1" ] && return 0
  rm -rf "$P/Builds/Bench" && cp -r "$P/Builds/$1" "$P/Builds/Bench" && echo "$1" > "$P/Builds/Bench/.from"
}

run() { # outdir tag args...: one bench game; its log is results/q-<tag>.log
  local out=$1 tag=$2; shift 2
  [ -f "$P/Builds/$out/Punchy.exe" ] || { log "$tag: no build $out"; return 1; }
  fixed $out || { log "$tag: copy to Builds/Bench failed"; return 1; }
  cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$W/q-$tag.log" -benchroute -weatherseed 4 -time 12 $(screen Bench 1280 720) "$@" >/dev/null 2>&1 )
  [ $? -eq 124 ] && log "$tag: TIMED OUT (dialogs: $(dialogs))"
  strays >/dev/null
  return 0
}

parse() { # log commit cap round: CSV rows from a game's per-stop lines
  awk -v c="$2" -v cap="$3" -v r="$4" -v ex="$(grep -c Exception "$1" 2>/dev/null)" '
    function num(re,   m) { return match($0, re) ? substr($0, RSTART, RLENGTH) : "" }
    function val(re,   s) { s = num(re); sub(/.* /, "", s); return s }   # the number after the label
    /BenchRoute: [a-z]+: avg/ {
      stop = $0; sub(/.*BenchRoute: /, "", stop); sub(/:.*/, "", stop)
      at = val("at -?[0-9]+,-?[0-9]+"); split(at, xz, ",")
      slow = 0; w = $0; sub(/.*slow frames:/, "", w); n = split(w, a, " "); for (i = 1; i <= n; i++) { split(a[i], s, "/"); slow += s[2] }
      printf "%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%d,%s,%s,%s,%s\n", c, cap, r, stop,
        val("avg [0-9.]+"), val("1% low [0-9.]+"), val("median [0-9.]+"), val("worst [0-9.]+"),
        val("cpu [0-9.]+"), val("gpu [0-9.]+"), slow, xz[1], xz[2], val("players [0-9]+"), ex }' "$1"
}

HEADER=commit,cap,round,stop,avg,low1,median,worst,cpu,gpu,slow,x,z,players,exceptions
runcsv() { # outdir csvname commit cap round args...: run, then append its rows to results/<csvname>.csv
  local out=$1 name=$2 c=$3 cap=$4 r=$5; shift 5
  local tag=$name-c$cap-$r
  [ -f "$R/$name.csv" ] || echo $HEADER > "$R/$name.csv"
  run $out $tag "$@"
  local rows=$(parse "$R/q-$tag.log" $c $cap $r)
  [ -z "$rows" ] && log "$tag: no stop lines (see q-$tag.log)"
  [ -n "$rows" ] && echo "$rows" >> "$R/$name.csv"
}

table() { # csv: per stop and cap, the rounds' mean of the main numbers
  awk -F, 'NR > 1 { k = $4 " cap " $2; n[k]++; a[k] += $5; l[k] += $6; c[k] += $9; g[k] += $10; s[k] += $11; ex[k] += $15 }
    END { for (k in n) printf "  %-22s avg %5.1f  1%% low %5.1f  cpu %5.1f  gpu %5.1f  slow %4.1f  exc %d\n", k, a[k]/n[k], l[k]/n[k], c[k]/n[k], g[k]/n[k], s[k]/n[k], ex[k] }' "$1" | sort | tee -a "$REPORT"
}

compare() { # base.csv new.csv label: silent (one line) unless a number got worse beyond noise in every round, a stop moved
            # (another island: not comparable), the J-006 floor (1% low 30 at cap 60) is missed, or exceptions appear
  local out
  out=$(awk -F, -v noise=$NOISE -v label="$3" '
    FNR == 1 { file++; next }
    { k = $4 "|" $2 }
    file == 1 { bn[k]++; ba[k] += $5; bl[k] += $6; bg[k] += $10; bx[k] = $12; bz[k] = $13
                if (!(k in blmin) || $6 < blmin[k]) blmin[k] = $6; if (!(k in blmax) || $6 > blmax[k]) blmax[k] = $6; next }
    { nn[k]++; na[k] += $5; nl[k] += $6; ng[k] += $10; ne[k] += $15; nx[k] = $12; nz[k] = $13
      if (!(k in nlmax) || $6 > nlmax[k]) nlmax[k] = $6; if (!(k in namax) || $5 > namax[k]) namax[k] = $5; keys[k] = 1 }
    END {
      bad = 0
      for (k in keys) {
        split(k, p, "|"); name = p[1] " cap " p[2]
        if (!(k in bn)) { printf "NEW   %s: avg %.1f, 1%% low %.1f (no base)\n", name, na[k]/nn[k], nl[k]/nn[k]; continue }
        BA = ba[k]/bn[k]; BL = bl[k]/bn[k]; NA = na[k]/nn[k]; NL = nl[k]/nn[k]
        # the base rounds own spread widens the band, so a noisy stop needs a bigger drop
        spread = BL > 0 ? 100 * (blmax[k] - blmin[k]) / BL : 0
        band = noise > 1.5 * spread ? noise : 1.5 * spread
        line = sprintf("%s: avg %.1f->%.1f, 1%% low %.1f->%.1f, gpu %.1f->%.1f", name, BA, NA, BL, NL, bg[k]/bn[k], ng[k]/nn[k])
        dx = nx[k] - bx[k]; dz = nz[k] - bz[k]
        if (bx[k] != "" && nx[k] != "" && dx*dx + dz*dz > 900) { printf "MOVED %s (ended %s,%s, base %s,%s: another island, not comparable)\n", line, nx[k], nz[k], bx[k], bz[k]; bad++; continue }
        if (nlmax[k] < BL * (1 - band/100)) { printf "WORSE %s (1%% low down %.0f%%, band %.0f%%)\n", line, 100*(1-NL/BL), band; bad++ }
        else if (namax[k] < BA * (1 - noise/100)) { printf "WORSE %s (avg down %.0f%%)\n", line, 100*(1-NA/BA); bad++ }
        if (p[2] == 60 && NL < 30) { printf "J-006 %s (1%% low under 30 at cap 60)\n", line; bad++ }
        if (ne[k] > 0) { printf "EXC   %s: %d exceptions\n", name, ne[k]; bad++ }
        summary = summary sprintf(" %s %.1f->%.1f;", p[1] "@" p[2], BL, NL)
      }
      # a stop the base has but the new runs lack (crash, timeout, no stop lines) is a failure, never a pass
      for (k in bn) if (!(k in keys)) { split(k, p, "|"); printf "MISS  %s cap %s: no rows in the new runs\n", p[1], p[2]; bad++ }
      if (length(keys) == 0 && length(bn) == 0) { print "MISS  no rows on either side"; bad++ }
      if (!bad) printf "OK %s: no change beyond noise (1%% lows:%s)\n", label, summary
    }' "$1" "$2")
  echo "$out" | sort >> "$REPORT"
  echo "$out" | grep -q "^OK" && { echo "$out"; return 0; }
  echo "$out" | sort
  echo "NOT CLEAN: $3 (full rows: $2; report: $REPORT)"
  return 1
}

profile() { # outdir tag stop args...: a .raw capture of one stop, analysed by BProfileTools.Analyze into results/<tag>.txt
  local out=$1 tag=$2 stop=$3; shift 3
  rm -f "$R/q-$tag.raw"
  run $out $tag -benchstops $stop -profilestop $stop "$W/q-$tag.raw" "$@"
  [ -f "$R/q-$tag.raw" ] || { log "$tag: no capture"; return 1; }
  strays >/dev/null
  cp "$P/Library/LastSceneManagerSetup.txt" "$R/LastSceneManagerSetup.bak" 2>/dev/null
  cp "$B/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs"
  ( cd "$P" && timeout 1500 "$UNITY" -batchmode -projectPath "$(cygpath -m $P)" -executeMethod BProfileTools.Analyze -raws "$WW\\q-$tag.raw" -out "$WW\\$tag.txt" -logFile "$W/analyze.log" )
  cp "$R/LastSceneManagerSetup.bak" "$P/Library/LastSceneManagerSetup.txt" 2>/dev/null
  rm -f "$P/Assets/Game/Scripts/Editor/BProfileTools.cs" "$P/Assets/Game/Scripts/Editor/BProfileTools.cs.meta"
  strays >/dev/null; rm -f "$R/q-$tag.raw"
  [ -f "$R/$tag.txt" ]
}

profdiff() { # a.txt b.txt: markers by growth in mean self time and on slow frames, and the slow-frame counts
  report "profile diff: $(basename $1) -> $(basename $2)"
  for f in "$1" "$2"; do report "  $(basename $f): $(grep -E '^frames used' $f | head -1); $(grep -oE 'SPIKES \(frames > 33.3 ms\): [0-9]+ of [0-9]+' $f | head -1)"; done
  awk -F'\t' '
    FNR == 1 { file++ }
    /^key\t/ { table = 1; next } /^SPIKES/ { table = 0 }
    /^SPIKE / { k = $1; sub(/^SPIKE /, "", k); spike[file, k] = $2; keys[k] = 1; next }
    table && NF >= 6 { mean[file, $1] = $5; keys[$1] = 1 }
    END { for (k in keys) {
            # waiting and idle time only mirrors the real work, and the bare thread rows are totals
            if (k ~ /Idle|WaitForSignal|WaitForJobGroupID|WaitOnSwapChain|PresentFrame|\|$/) continue
            dm = mean[2, k] - mean[1, k]; ds = spike[2, k] - spike[1, k]
            score = dm + ds / 10   # slow-frame growth counts for less: those frames are only a few percent of the run
            if (score > 0.05 || score < -0.05) printf "%8.2f %8.2f %8.2f  %s (mean %.2f->%.2f, slow frames %.2f->%.2f)\n", score, dm, ds, k, mean[1, k], mean[2, k], spike[1, k], spike[2, k] } }' "$1" "$2" |
    sort -k1,1nr > "$R/.profdiff"
  report "  grew most (dMean ms, dSlow-frame ms):"
  head -10 "$R/.profdiff" | cut -c10- | sed 's/^/   /' | tee -a "$REPORT"
  report "  shrank most:"
  awk '$1 < 0' "$R/.profdiff" | sort -k1,1n | head -5 | cut -c10- | sed 's/^/   /' | tee -a "$REPORT"
}

bisect() { # good bad stop metric: first-parent bisect, 2 rounds a step at cap 0; a step is bad when its mean is nearer bad's
  local good=$1 bad=$2 stop=$3 metric=$4 col
  case $metric in avg) col=5;; low1) col=6;; cpu) col=9;; *) col=10;; esac
  measure() { # commit: the stop's mean of the metric over 2 rounds
    build $1 Build Bench-$1 >&2 || { echo fail; return; }
    rm -f "$R/bis-$1.csv"
    for r in 1 2; do runcsv Bench-$1 bis-$1 $1 0 $r -benchstops $stop -fpscap 0 >&2; done
    awk -F, -v c=$col -v s=$stop 'NR > 1 && $4 == s { t += $c; n++ } END { if (n) printf "%.2f", t / n; else print "fail" }' "$R/bis-$1.csv"
  }
  local list=($(git -C "$P" rev-list --first-parent --reverse $good..$bad | cut -c1-7))
  [ ${#list[@]} -gt 0 ] || fail "no first-parent commits between $good and $bad"
  local gv=$(measure $good) bv=$(measure $bad)
  report "bisect $stop $metric: good $good = $gv, bad $bad = $bv, ${#list[@]} first-parent commits"
  [ "$gv" = fail ] || [ "$bv" = fail ] && fail "couldn't measure an end"
  local lo=0 hi=$((${#list[@]} - 1))   # list[hi] is bad; everything before list[lo] is good
  while [ $lo -lt $hi ]; do
    local mid=$(( (lo + hi) / 2 )) m=${list[$(( (lo + hi) / 2 ))]}
    local v=$(measure $m)
    [ "$v" = fail ] && { report "  $m: build or run failed; stopping with the range $(short ${list[$lo]})..$(short ${list[$hi]})"; break; }
    if awk -v v=$v -v g=$gv -v b=$bv 'BEGIN { exit !((v - g) * (v - g) > (v - b) * (v - b)) }'; then hi=$mid; report "  $m = $v: bad"
    else lo=$((mid + 1)); report "  $m = $v: good"; fi
  done
  local first=${list[$hi]}
  report "first bad (first-parent): $(git -C "$P" log -1 --format='%h %s' $first | cut -c1-100)"
  local parents=$(git -C "$P" log -1 --format=%p $first | wc -w)
  if [ $parents -gt 1 ]; then
    # first-parent from the merge's first parent would only find the merge again: walk the merged branch instead
    local m1=$(git -C "$P" rev-parse --short=7 $first^1) m2=$(git -C "$P" rev-parse --short=7 $first^2)
    report "  it's a merge; its merged commits, for the next step (bench.sh bisect $(git -C "$P" merge-base $m1 $m2 | cut -c1-7) $m2 $stop $metric does it over them):"
    git -C "$P" log --oneline --no-merges $first^1..$first | head -20 | sed 's/^/    /' | tee -a "$REPORT"
  fi
}

mpsmoke() { # outdir commit: the host's numbers alone vs with a client on this machine (low-res, capped, auto-walking)
  local out=$1 c=$2
  fixed $out || fail "copy to Builds/Bench failed"
  rm -f "$R/mp-$c-solo.csv" "$R/mp-$c-joined.csv"
  for r in 1 2; do
    runcsv $out mp-$c-solo solo 60 $r -host -benchwait 45 -benchstops town,townwalk -fpscap 60
    # joined: the client is the same exe (one firewall rule), capped so its own cost stays out of the way. A join gives up
    # after 15 s (NetMenu.JoinTimeout), so it starts only once the host's world is up; no host world in 2 min, no client.
    local hostlog="$R/q-mp-$c-joined-c60-$r.log"; rm -f "$hostlog"
    ( for i in $(seq 1 120); do
        if grep -q "BenchRoute: world ready" "$hostlog" 2>/dev/null; then
          cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$W/q-mp-$c-client-$r.log" -benchclient -join 127.0.0.1 -autowalk -fpscap 20 -nohud $(screen Bench 480 270) >/dev/null 2>&1
          exit 0
        fi
        wait_s 1
      done ) &
    runcsv $out mp-$c-joined joined 60 $r -host -benchwait 45 -benchstops town,townwalk -fpscap 60
    strays >/dev/null; wait
  done
  local joined=$(awk -F, 'NR > 1 && $14 < 2 { bad++ } END { print bad + 0 }' "$R/mp-$c-joined.csv")
  [ "$joined" -gt 0 ] && report "MP: the client didn't show up on the host in $joined stop(s): check q-mp-$c-client-*.log (dialogs: $(dialogs))"
  NOISE=${MPNOISE:-15} compare "$R/mp-$c-solo.csv" "$R/mp-$c-joined.csv" "MP $c host joined vs solo (band ${MPNOISE:-15}%)"
}
