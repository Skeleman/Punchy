#!/bin/bash
# Job 33's two void cases, third try. "q63.sh scout": one water/lava-plates session, a shot list of candidate views
# into the crater round the MagmaSerpent (-211,-2129) by -goto and -view yaw,pitch (yaw 0 faces north, pitch positive
# looks down), to pick the lava framing before timing it. Beach swells and lava timing follow in q61 once the view is
# picked (runner m4: "-benchview place+180,8").
B=$HOME/Projects/Punchy-wt/Tools/bench
. "$B/lib.sh"
Q=q63
mkdir "$R/.lock" 2>/dev/null || { echo "lock held: $(cat $R/.lock/job)"; exit 2; }
echo $$ > "$R/.lock/pid"; echo "$Q $1 (since $(date +%T))" > "$R/.lock/job"
trap 'rm -rf "$R/.lock"; git -C "$P" checkout -q $HOME_BRANCH 2>/dev/null' EXIT
case $1 in
scout)
  OL=Profile-53582b9m2
  # (Second pass: the first showed lava only from the serpent's own spot looking steeply down, a channel running
  # north-east to south-west: c0p70. Variants round it.)
  { echo "-goto -211,-2129 -time 12 -weatherseed 4 -view 0,70 -wait 40 -shot c0p70"
    for c in "-211,-2129 0,85 c0p85" "-221,-2129 0,70 w10p70" "-221,-2119 0,70 w10n10p70" "-211,-2129 225,60 c0y225p60"              "-211,-2129 45,60 c0y45p60" "-216,-2134 225,75 sw7p75" "-206,-2124 45,75 ne7p75" "-231,-2149 45,45 sw28y45p45"; do
      read -r at v name <<< "$c"; echo "-goto $at -view $v -wait 8 -shot $name"
    done; } > "$R/$Q-scout.txt"
  rm -rf "$R/$Q-scout"; mkdir -p "$R/$Q-scout"; fixed $OL || exit 1; cool
  ( cd "$P/Builds/Bench" && timeout 900 ./Punchy.exe -logFile "$W/q-$Q-scout.log" $(screen Bench 1920 1080) -gfx High \
      -fpscap 0 -weatherseed 4 -shotlist "$W/$Q-scout.txt" -shotdir "$W/$Q-scout" -quit >/dev/null 2>&1 )
  echo "rc=$?"; strays >/dev/null
  ls "$R/$Q-scout"; grep -h 'shot list\|goto\|Crash' "$R/q-$Q-scout.log" | cut -c1-160 | head -20 ;;
*) echo "usage: q63.sh scout"; exit 2 ;;
esac
echo $Q $1 done
