# Handoff: profiler (laptop), refreshed 2026-10-30 after slot 4

Read COORDINATION.md first. This lane benches and profiles builds when the supervisor hands it the Unity slot. Only
one Unity runs at a time on the laptop. When the slot is done: post BENCH-REPORTS (newest first), send one message
saying "Unity closed", and send no ack-only messages.

## Machine and paths
- Laptop: i7-7700HQ, Optimus GTX, Unity 6000.5.8f1, D3D11. C: has about 16 GB free; ask before deleting builds.
- Main checkout `C:\Users\zache\Projects\Punchy`: builds happen there in detached HEAD, then it returns to
  `b/weather-mapjob` (lib.sh HOME_BRANCH). Untracked `.vsconfig` and `Assets/Resources.meta` aren't ours; leave them.
- Runner: worktree `C:\Users\zache\Projects\Punchy-wt`, branch `b/bench-runner` (9a4c308), in `Tools/bench/`:
  - `bench.sh` (smoke|ab|baseline|profile|profdiff|bisect|mp);
  - `lib.sh` (build, run, runcsv, profile, compare, table, fixed, dialogs, keepawake);
  - `BenchmarkRoute.cs.txt` and `BProfileTools.cs`, copied into the project only for a build.
  - Results go to `Tools/bench/results/` (gitignored); the lock is `results/.lock`.
- Scratchpad scripts (temporary; re-create from these patterns if they're gone): slot.sh to slot4.sh, one script per
  slot that takes the lock, starts keepawake, builds, runs ABBA and prints tables. Also swim.sh, item13.sh, mp2.sh, and
  play.sh (ReleaseBuild for Builds/Play).

## Archived builds worth keeping (Builds/)
- Bench-bc89606: world-features before the cave-chunk, worm and blocked work; the A side of many checks.
- Bench-256e6a9 (cave-chunk), Bench-d60e085 (unmasked worm marks), Bench-2de3029 (worm mask), Bench-b70dbfa (wake
  mask), Bench-0c637a9 (gfx sweep), Bench-3d1893d / Bench-793f103 (swimmer wakes, item 6).
- Profile-3481353 / Profile-74d8148 (the Brain.Blocked markers on bc89606 / 3d874dc), Profile-256e6a9.
- Local-only branches in the main checkout: bench/worm-merge (d60e085), bench/worm-mask (2de3029), bench/wake-mask
  (b70dbfa).
- Profiler-only marker branches on origin (never merge): wip/brain-blocked*, wip/brain-sample*.

## Open threads
- Gfx default: Low recommended (slot 4 report). Next probe if wanted: `-gfxset view=0.8,detail=0.75,shadows=0.8` on
  glide and townwalk.
- Worm mask: +0.4 ms against a 0.3 bar, with about ±0.2 noise between sessions; the call is the supervisor's and the
  animal lane's.
- Wakes: re-check with a camera over open water (the slot 4 frame was inside a canopy).
- Item 15 cave-chunk: busiest chunk frame only −16%. A per-chunk timing log would show whether the mouth chunk is the
  one that's slow.

## Flags that work (solo bench)
- Route: `-benchroute -benchstops a,b -fpscap N -benchshots <dir>`, `-benchspot x,y,z,yaw` (spot stop),
  `-benchwait s`, and `-profilestop <stop> <raw>` (dev builds).
- Placing and spawning: `-place <name|Glacier|clearlake|tree:Species|cave:Name>`, `-goto x,z`, and `-spawn Species:N`
  (6–45 m around the player).
- Camera: `-watch Species -watchfrom up,off` and `-camlook x,y,z,tx,ty,tz`.
- Graphics, per run and never saved: `-gfx Low|Medium|High|Ultra` and `-gfxset view=..,detail=..,shadows=..`.
- Analyzer columns: key, medIncl, meanIncl, medSelf, meanSelf, calls/frame, maxCallMs, maxFrameMs. For an
  IJobParallelFor (BuildChunkMeshJob), maxFrame is the one-chunk reading.

## Gotchas (each one has cost a run)
- **Variable names in scripts that source lib.sh:** never assign P, R, W, WW, B, BASE, UNITY, SMI, HOME_BRANCH, REPORT,
  HEADER or NOISE. In slot 4, `W=<commit>` sent every log to a relative folder and lost a pass. Use WM, K, G and so on.
- A running bash script: don't sed -i it (that makes a new inode). For a same-length change (a commit hash), overwrite
  the bytes in place (python open 'r+b'), and only in a block bash hasn't parsed yet.
- Fixed views: `-camlook` on the command line holds through a `here` stop. `-watch` moves the camera and follows any
  animal of that species. The Glacier has native SnowWorms, so "no spawn" isn't "no worms".
- One townwalk capture of Brain.* can swing 2× depending on where the animals stand: take 2 per build, ABBA.
- A `-place CaveNetwork` look doesn't frame the big cave's mouth. `-benchspot -2822,-648,90` stands in the slot at the
  mouth, facing in.
- Check dialogs before each run (`dialogs` in lib.sh; the mintty "Unity ..." titles are just the agents' terminals).
  Never click one.
- No Defender, firewall, power, registry or ExecutionPolicy changes. Kill only by exact PID.
- Never pass -screen-* to a real Punchy build. Bench builds use the PunchyBench identity.
- Compare same-island only: check the `at x,z` end positions. Take screenshots at every gate. Grep logs for "Mesh
  bounds" (NaN bounds) and Exception.

## Baseline feel (0c637a9, Medium, cap 60, 1% low)
townwalk ~41, glide ~31 (the limit), caveapproach ~35, forest ~51. Glide at cap 60 has sat at about 30 since v0.8.
