#!/bin/bash
# The build shelf (Zach 11-14): the main PC builds Profile-<commit> and parks it as Profile-<commit>.zip on the draft
# GitHub release `bench-builds` (drafts are visible only to repo writers; never publish it). The laptop downloads
# instead of building, so its time goes to games.
#   shelf.sh get <commit7> <project> [outdir]   laptop: unzip into <project>/Builds if on the shelf; exit 1 if not
#   shelf.sh put <commit7>...                   main PC: build in D:/Unity/Punchy-release, zip, upload
#   shelf.sh ls | rm <commit7>...               list / prune
# A shelf build is the same as build() in b/bench-stress Tools/bench/lib.sh (the runner, not main's) (BProfileTools.BuildDev, same Unity 6000.5.8f1) and
# carries the .bench2-<commit> marker, so build() then skips it.
REL=bench-builds; REPO=Skeleman/Punchy
cmd=$1; shift
case $cmd in
get)
  c=$1; P=$2; o=${3:-Profile-$1}
  [ -f "$P/Builds/$o/Punchy.exe" ] && exit 0
  t=$(mktemp -d)
  gh release download $REL -R $REPO -p "Profile-$c.zip" -D "$t" 2>/dev/null || { rm -rf "$t"; exit 1; }
  mkdir -p "$P/Builds" && rm -rf "$P/Builds/$o.part" && mkdir "$P/Builds/$o.part" &&
    tar -xf "$t/Profile-$c.zip" -C "$P/Builds/$o.part" && rm -rf "$P/Builds/$o" &&
    mv "$P/Builds/$o.part/Profile-$c" "$P/Builds/$o" && rm -rf "$P/Builds/$o.part"
  ok=$?; rm -rf "$t"
  [ $ok -eq 0 ] && [ -f "$P/Builds/$o/.bench2-$c" ] && echo "shelf: $o from the shelf" && exit 0
  echo "shelf: Profile-$c.zip didn't unpack; building instead"; rm -rf "$P/Builds/$o"; exit 1;;
put)
  export PUNCHY=/d/Unity/Punchy-release UNITY=/c/Unity/6000.5.8f1/Editor/Unity.exe HOME_BRANCH=${HOME_BRANCH:-rc/v0.14.0}
  T=/d/Unity/PunchyBench/shelf; mkdir -p $T/zips
  git -C $PUNCHY fetch -q origin
  rm -rf $T/tools && mkdir -p $T/tools && git -C $PUNCHY archive origin/b/bench-stress Tools/bench | tar -x -C $T/tools
  B=$T/tools/Tools/bench; mkdir -p $B/results; . "$B/lib.sh"
  for c in "$@"; do
    [ -f D:/Unity/PunchyBench/HOLD ] && { log "HOLD: stopping before $c"; exit 3; }
    gh release view $REL -R $REPO --json assets -q '.assets[].name' | grep -qx "Profile-$c.zip" && { log "$c already shelved"; continue; }
    build $c BuildDev Profile-$c || { log "shelf: build $c failed (log $B/results/build-Profile-$c.log)"; continue; }
    ( cd $PUNCHY/Builds && rm -f $T/zips/Profile-$c.zip && tar -a -cf "$(cygpath -w $T/zips/Profile-$c.zip)" Profile-$c ) &&
      gh release upload $REL -R $REPO --clobber $T/zips/Profile-$c.zip && rm -f $T/zips/Profile-$c.zip &&
      log "shelf: Profile-$c.zip uploaded"
  done;;
ls) gh release view $REL -R $REPO --json assets -q '.assets[] | "\(.name) \(.size/1048576|floor) MB \(.updatedAt)"';;
rm) for c in "$@"; do gh release delete-asset $REL "Profile-$c.zip" -R $REPO -y; done;;
*) sed -n 2,9p "$0"; exit 2;;
esac
