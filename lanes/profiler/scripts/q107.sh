#!/bin/bash
# R-18: a RenderDoc frame of the NaN bloom dot (job 56's hilltop at Ultra, pixel 1060,224 on main cc835cf6 and far-lite
# alike, not at High) and its pixel history (rdpixel.py): which draw first writes a non-finite value there.
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
O=Profile-cc835cf; Q=q107
S=$HOME/Projects/Punchy-coordination/lanes/profiler/scripts
RDC="/c/Program Files/RenderDoc/renderdoccmd.exe"; QRD="/c/Program Files/RenderDoc/qrenderdoc.exe"
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q R-18 NaN dot capture (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"' EXIT
keepawake
RD="${R:?}/rd107"; mkdir -p "$RD"; rm -f "$RD"/*.rdc
fixed $O || exit 1; cool
( cd "$P/Builds/Bench" && timeout 900 "$RDC" capture -w -d "$(cygpath -w "$P/Builds/Bench")" -c "$(cygpath -w "$RD/hilltop-ultra")" \
    "$(cygpath -w "$P/Builds/Bench/Punchy.exe")" -logFile "$W/q-$Q-rd.log" -benchroute -weatherseed 4 -time 12 -body default \
    $(screen Bench 1920 1080) -gfx Ultra -fpscap 0 -benchstops spot -benchspot -3460,2425,270 -benchhour 18 -benchweather Clear \
    -benchshots "$W/$Q-shots" -renderdocstop spot 2 >/dev/null 2>&1 )
echo "capture rc=$?"; strays >/dev/null
grep -h 'RenderDoc capture' "$R/q-$Q-rd.log" | head -2
CAPS=$(ls "$RD"/*.rdc | xargs -n1 cygpath -w | tr '\n' ';')
RD_CAPS="$CAPS" RD_OUT="$(cygpath -w "$R/$Q-pixel.txt")" RD_X=1060 RD_Y=224 timeout 1800 "$QRD" --python "$(cygpath -w "$S/rdpixel.py")" >/dev/null 2>&1
echo "qrenderdoc rc=$?"; strays >/dev/null
cat "$R/$Q-pixel.txt" | head -80
# Job 65a: which Ultra setting turns it on. Ultra is view 1.7, detail 2, shadows 2; High 1.3, 1.5, 1.4. One game each with
# one setting back at its High value (and plain Ultra and High as checks); a shot at the spot's end, then whether the
# halo is at (1060, 224): its centre pixel's neighbourhood far brighter than 12 px away.
halo() { python -c "
from PIL import Image
im=Image.open(r'$1').convert('L'); px=im.load(); best=0
for y in range(214,235):
    for x in range(1050,1071):
        ring=max(px[x+12,y],px[x-12,y],px[x,y+12],px[x,y-12]); best=max(best,px[x,y]-ring)
print('HALO' if best>110 else 'clean', best)"; }
for arm in ultra high view13 detail15 shadows14; do
  case $arm in ultra) set -- -gfx Ultra;; high) set -- -gfx High;; view13) set -- -gfx Ultra -gfxset view=1.3;;
    detail15) set -- -gfx Ultra -gfxset detail=1.5;; shadows14) set -- -gfx Ultra -gfxset shadows=1.4;; esac
  BENCHW=1920 BENCHH=1080 run $O $Q-$arm -fpscap 0 "$@" -benchstops spot -benchspot -3460,2425,270 -benchhour 18 -benchweather Clear     -benchshots "$W/$Q-shots/$arm"
  echo "$arm: $(halo "$R/$Q-shots/$arm/spot.png" 2>&1)"
done
echo $Q done
