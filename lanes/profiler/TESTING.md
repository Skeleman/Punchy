# Profiler lane: flags, gotchas, baselines

Read `lanes/shared/checks.md` first.

## Flags that work (solo bench)
- Route: `-benchroute -benchstops a,b -fpscap N -benchshots <dir>`, `-benchspot x,y,z,yaw` (spot stop),
  `-benchwait s`, `-benchday <day type>` (any weather at every stop), `-profilestop <stop> <raw>` (dev builds).
- Placing: `-place <name|Glacier|clearlake|tree:Species|cave:Name>`, `-place prop:<Species>[@x,z][#n]` (logs real
  prop coordinates), `-place cavestream[#n]`, `-goto x,z`, `-spawn Species:N` (6-45 m around the player).
- Camera: `-watch Species -watchfrom up,off`, `-camlook x,y,z,tx,ty,tz`.
- Graphics, per run and never saved: `-gfx Low|Medium|High|Ultra`, `-gfxset view=..,detail=..,shadows=..`.
- UI checks (once #4/#5 merge): `-menu <Tab>`, `-forcepad xbox|playstation`.
- MP: host + client pair; client framing via the client's `-shotlist`; remote carry with the client's
  `-interact Cat:carry` next to a host `-spawn Cat:2` (see REPORTS-animal slot 9).
- Analyzer columns: key, medIncl, meanIncl, medSelf, meanSelf, calls/frame, maxCallMs, maxFrameMs. For an
  IJobParallelFor (BuildChunkMeshJob), maxFrame is the one-chunk reading.

## Gotchas (each one has cost a run)
- **Script variable names that collide with lib.sh:** never assign P, R, W, WW, B, BASE, UNITY, SMI, HOME_BRANCH,
  REPORT, HEADER, NOISE, or `t` (lib.sh `cool` sets a global t). Use WM, K, G and so on.
- Don't `sed -i` a running bash script (new inode). For a same-length change, overwrite bytes in place
  (python 'r+b'), only in a block bash hasn't parsed yet.
- Check `dialogs` before every timed run and read the full line (it was cut off in logs once). Never click a
  dialog. A Windows Security Alert means Zach; results with ~500 ms worst frames are void.
- Launch only from `Builds/Bench` / `Builds/Play`; a new exe path raises a firewall prompt.
- No Defender, firewall, power, registry or ExecutionPolicy changes. Kill only by exact PID.
- Never pass `-screen-*` to a real Punchy build. Bench builds use the PunchyBench identity.
- Compare same-island only: check the `at x,z` end positions. Screenshots at every gate. Grep logs for
  "Mesh bounds" (NaN bounds) and Exception.
- `-camlook` holds through a `here` stop; `-watch` follows any animal of that species. The Glacier has native
  SnowWorms, so "no spawn" isn't "no worms".
- One townwalk capture of Brain.* can swing 2x with where animals stand: take 2 per build, ABBA.
- `-goto x,z` lands on the ground surface (above any cave); `-goto x,y,z` lands on the floor under that point.
- `-benchshots <dir>` writes nothing if the folder is missing: `mkdir -p` it first.
- runcsv's log is `q-<csv>-c<cap>-<round>.log`, overwritten by every run in a round: copy it after each run if needed.
- Editor memory: `scripts/MemCheck.cs.txt` (batchmode, play cycles, snapshots; `-cycles N -leakstack` names native leaks).
  Process.WorkingSet64 reads 0 under Unity's Mono: use the System Used Memory counter.
- Host/leave cycles in a player: copy `scripts/NetCycle.cs.txt` to Assets/Game/Scripts/Debugging/NetCycle.cs for one
  Bench build (see q16.sh), then `-netcycle N`. Switching Burst safety checks off in the editor recompiles Burst on the
  next play: don't time that play.
- `-place CaveNetwork` doesn't frame the big cave's mouth; `-benchspot -2822,-648,90` stands at the mouth, facing in.
- Gate shots are 1280x720 (the bench window); frame-rate numbers are measured at that size.

## MP framing (learned 2026-11-02, item 5)
- Host spawns at (0,65,0) facing +z; a local client spawns about 3 m away, near (0.5,65,1.5). An `-actiontest roll`
  on the host jogs it along +z (about 20 m per 3.5 s slot).
- On the client, `-camrel 0,4,-3,0,0,14` (behind and above, down the path) frames the host's first rolls.
  `-watch <Species>` doesn't pick up a remote avatar; `-goto` doesn't move a joined client.
- Shots are saved at the screen's size (1920x1080), not the window's: crop by that size.
- Time the shots against the host's own shot (its mtime) to find the frames during a roll.

## Baselines (Medium, cap 60, 1% low)
- v0.11.0 (4eea658): townwalk 35.0, glide 29.6, glide uncapped 37.1, MP joined townwalk 25.9.
- 0c637a9: townwalk ~41, glide ~31, caveapproach ~35, forest ~51. Glide at cap 60 has sat near 30 since v0.8.
