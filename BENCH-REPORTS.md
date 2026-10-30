# Bench reports (kind-possum → supervisor)

Newest first.

## 2026-10-30: slot (items 15, 16, worm marks): cave-chunk passes (small), Blocked cache passes (big), worm marks over the bar
0 exceptions and 0 NaN-bounds lines in every run. No dialogs. BProfileTools (b/bench-runner 9a4c308) now adds maxCallMs and
maxFrameMs columns: the longest single call, and the most one frame spent on a marker.

### Item 16: Brain.Blocked, blocked-cache 3d874dc vs bc89606: PASS, about 30x cheaper
Marker builds 3481353 (bc89606) and 74d8148 (3d874dc), townwalk cap 60, 2 dev captures each, ABBA. All four ended at
366,-406. Workers + main:
| | bc89606 r1 | bc89606 r2 | 3d874dc r1 | 3d874dc r2 |
|---|---|---|---|---|
| animals (Net status mean) | 100 | 97 | 98 | 106 |
| Brain.Blocked calls/frame | 39.4 | 39.0 | 38.9 | 44.7 |
| Brain.Blocked µs/call | 17.7 | 30.9 | **0.98** | **0.69** |
| Brain.Blocked ms/frame | 0.70 | 1.21 | 0.04 | 0.03 |
| BrainJob inclusive ms | 2.18 | 2.59 | **1.47** | **1.67** |
| worst Blocked frame ms (maxFrame) | 4.9 | 6.1 | 0.2 | 0.3 |
- Behaviour is the same: the call rate, animals and end spot all match. Town look (`town` stop, same frame):
  results/shots-town-{3481353,74d8148}/town.png. The buildings, props, paths and fences are identical; only the
  animals moved. The old cost swung 18–31 µs between captures, as in item 14; the new one is under 1 µs either way.

### Item 15: cave-chunk 256e6a9 vs bc89606, caveapproach: PASS (compiles, cut edge identical), but nowhere near 5x at frame level
- A/B, cap 0, ABBA, 2 rounds, same end -2773,-726: avg 97.5 → 98.0, **1% low 52.5 → 59.6**, GPU 6.5 → 6.3 ms,
  worst frame 27/30 → 25/21 ms.
- Dev captures (3481353 = bc89606 plus the Brain marker; no terrain change), 2000 frames, 0 spikes in both:
  | BuildChunkMeshJob | bc89606 | 256e6a9 |
  |---|---|---|
  | mean ms/frame (workers) | 0.700 | 0.661 |
  | busiest frame, ms (maxFrame) | 36.8 | **30.9** (−16%) |
  | longest single batch, ms (maxCall) | 15.6 | 14.4 |
  The job is an IJobParallelFor, so a call is one worker's batch; the busiest frame is the closest reading of the mouth
  chunk's total. It's 16% lower, not 5x. Either the mouth chunk isn't the busiest chunk on this walk, or the cut's
  skipped samples weren't most of its cost. A per-chunk timing log would settle which.
- Cut edge: a mouth spot `-benchspot -2822,-648,90` (in the slot at the mouth, facing in) and one above it
  (-2812,-660,60). Shots are identical on both builds: results/shots-mouth2-{bc89606,256e6a9}-*/spot.png. Both
  builds show a few flat grey shards at the cut edge (bottom right of the slot shot). They're already in bc89606,
  so they're not from this change; worth a look by the world lane. The `-place CaveNetwork` and caveapproach end
  shots don't frame the mouth, but they also match.

### Worm marks (92a9602 shader + 18f1b3b feeder, local merge d60e085): +0.5 ms GPU, over the 0.3 ms bar
The Glacier has native snow worms, so I fixed the camera instead of using -watch:
`-place Glacier -camlook 900,200,1525,869,186,1500 -benchwait 40 -benchstops here`, cap 0, a fresh player each run, 2 rounds
in ABBA order. Same frame in every shot (results/shots-wormcam-*). The `on` shots show the spawned worm's trail of bumps
across the snow; the natives-only shots show no marks in view.
| | avg | 1% low | GPU ms (r1, r2) |
|---|---|---|---|
| bc89606, no SnowWorm species (no marks; upper bound) | 115.2 | 52.0 | 5.5 (5.4, 5.5) |
| d60e085, natives only | 110.1 | 50.5 | 5.9 (6.1, 5.7) |
| d60e085, -spawn SnowWorm:1 (trail in view) | 107.8 | 51.0 | **6.4** (6.3, 6.5) |
- Spawned against natives-only, same build and frame: **+0.5 ms GPU** (+0.2 and +0.8 by round). Against bc89606:
  +0.9 ms, but that's an upper bound, since animal-behaviours also adds creatures, traces and the worms' bodies.
  The spawned run also draws the worm's own body and clods, but they're a few small meshes. Most of the gap should be
  the mark loop.
- Verdict: over the 0.3 ms bar. The 32-slot cap or the per-chunk bounds test is worth doing. The same command lines
  re-run the check: Bench-d60e085 is archived, and the merge is on the local branch bench/worm-merge.
- Caveat: there's no readout of _PunchyBurrowerCount, so how many marks each run fed is unknown. Natives within 150 m
  could have fed some marks in the natives-only runs too.


## 2026-10-30: slot (item 14, close-up ducks, F-40 smoke): Blocked is the town cost; ducks and F-40 pass
0 exceptions and 0 NaN-bounds lines in every run below. No dialogs.

### Item 14: Brain.Sample split with Brain.Blocked (91f8957 on 6cfedaf vs 32ec6ff on 7bd0768): item 13's +30% is Blocked noise, not Sample
One dev capture per stop, cap 60, workers + main summed per frame. Townwalk ended at 366,-406 in both, with 96/98
animals. Forest is the OakGrove stop: it ended at -2363,-400 in both, with 67 animals each.
| | townwalk 91f8957 | townwalk 32ec6ff | forest 91f8957 | forest 32ec6ff |
|---|---|---|---|---|
| Brain.Moving calls (movers) | 61.8 | 60.5 | 47.1 | 42.1 |
| Brain.Sample µs/call | 17.8 | 9.5 | 1.89 | 1.66 |
| Brain.Blocked µs/call (calls/frame) | 37.8 (45.7) | 19.7 (35.8) | 0.10 (29.9) | 0.11 (26.6) |
| Sample minus Blocked, µs/call | 2.75 | 3.00 | ≈1.85 | ≈1.62 |
| Blocked share of Sample time | 85% | 68% | ~2% | ~2% |
| BrainJob inclusive ms | 3.16 | 2.39 | 0.77 | 0.72 |
- **Sample's own work is flat** (2.75 vs 3.00 µs in town; forest flat or lower on 7bd0768). All the variance is
  `FloraPlacement.Blocked`. It costs 20–38 µs a call near town plots and props, and 0.1 µs away from them.
- **This run reversed item 13.** The old Sample is now the dearer one: 17.8 vs 9.5 µs, where item 13 had 8.7 vs 11.2.
  Both captures ended at the same place with the same animal count. So a single townwalk capture can swing Blocked
  2× on where the movers happen to stand. Item 13's +30% was this noise (and the layout), not Sample's code.
- **Target:** in town, Blocked is 68–85% of Sample and about 0.7–1.7 ms of BrainJob a frame. That matches the world
  lane's suspect: it re-runs Place() for nearby plots and props on every call. A baked or per-cell cached
  blocked answer is the fix to try (CLAUDE.md: per-region work belongs in a baked table).
- The forest control isn't fully identical. The grove behind matches, but one near prop differs: a standing stone
  in 91f8957 and an oak in 32ec6ff (results/shots-i14-*-forest/forest.png). It's a different island, but there are no
  plots, so Blocked costs the same 0.1 µs in both.
- The marker branches (wip/brain-blocked*) remain profiler-only.

### Close-up ducks (item 6 worst case): 793f103 vs 3d1893d, cleared
`-benchspot -1087,33,-1600,270 -goto -1087,-1600 -spawn Duck:8 -benchwait 12`, cap 0, ABBA plus a third round (BA).
"spawned 8 Duck" is in every log. Shots of the same frame each time; B's r2 shot shows a duck about 10 m out, with
wake rings.
| | avg | 1% low | GPU ms | worst ms |
|---|---|---|---|---|
| 3d1893d (3 runs) | 91.0 | 68.1 | 10.1 | 18 / 22 / 14 |
| 793f103 (3 runs) | 92.8 | 49.9 (65.9 without r2) | 9.6 | 21 / **728** / 21 |
- B's r2 had a single 728 ms frame in the stop's last 5 s window: one slow frame, no exception, and GC the same as
  the others (19). It didn't recur in r3 (worst 21 ms), so I'm treating it as an outside stall, not the wake loop
  (which would cost every frame). Without it the 1% lows are within noise, and avg and GPU are flat or better.
  **Swimmer wakes are cleared.**

### F-40 smoke: world-features tip bc89606 (trodden mask + early-out + worm marks) vs 6464ce7: PASS
bc89606 compiled (Bench-bc89606 is archived for item 15's A side). Stops: `-place OgreCamp` measured by `here`, plus
townwalk; cap 0, ABBA, 2 rounds. Same end x,z: -365,-4132 and 366,-406.
| | 6464ce7 | bc89606 |
|---|---|---|
| OgreCamp avg / 1% low / GPU | 111.1 / 69.9 / 4.9 | 112.2 / 72.8 / 4.8 |
| townwalk avg / 1% low / GPU | 86.9 / 50.4 / 7.2 | 86.4 / 49.0 / 7.2 |
| worst ms (camp, town) | 18–20, 27–30 | 18–19, 28–29 |
- Within noise. The camp shots match frame for frame; the stalls are too far from this viewpoint to show the
  trodden patch. A look check of the patch belongs to the world lane's stall shots.


## 2026-10-30: item 6, swimmer wakes (world-features 793f103 vs 3d1893d) at a duck lake: no cost found
- **Duck lake:** `-place Grassland` then `-place clearlake` → shore (-1087, 33, -1600), facing west (270) over a
  clear lake 1.8 m deep (Temperate biome, ducks listed). `-watch Duck` found a duck on it 35 m out, floating, with
  wake rings: results/lake2-793f103/g-duck.png. FoggyMarsh's "clear lake" landed on a glacier, FungalForest's is a
  1.6 m pond, Temperate's was the sea shore. Bench stop: `-benchspot -1087,33,-1600,270`.
- **A/B (spot, cap 0, ABBA, 2 rounds, same island: same end x,z and same frame):** avg 91.8 → 96.5, 1% low 56.2 → 59.0,
  GPU 9.7 → 9.4 ms, worst 24/22 → 19/31 ms, 0 exceptions. No change beyond noise.
- Caveat: neither end-of-stop shot shows a duck, so the swimmer loop (cost only within 12 m of a swimmer) may not have
  had a duck near the visible water for much of the stop. For a worst case, frame a duck up close (`-watch Duck`, or
  `-spawn Duck` at the shore) and re-bench.

## 2026-10-30: item 13, Brain.Advance split (wip/brain-sample 7bd0768 vs its e4010f2 twin 6cfedaf): dearer sampling, not more movers
Townwalk cap 60, one dev capture each (~1775 frames, 1 spike frame each), 96 animals in both (Net status mean).
Workers + main summed, per frame:
| | 6cfedaf (e4010f2) | 7bd0768 | Δ |
|---|---|---|---|
| Brain.Moving calls (movers) | 56.8 | 55.8 | −2% |
| Brain.Advance calls | 111.6 | 105.9 | −5% |
| Brain.Sample ms | 0.97 | 1.19 | +0.22 |
| Brain.Sample µs per Advance call | 8.7 | 11.2 | **+30%** |
| Brain.Phase ms | 0.116 | 0.115 | 0 |
| Brain.Advance self ms (Accelerate + heading) | 0.025 | 0.025 | 0 |
| BrainJob inclusive ms | 2.00 | 2.35 | +0.35 |
- The growth is each creature's terrain sampling getting dearer (Sample per call +30%), not more movers and not
  Accelerate. Most of the remaining +0.13 is likely the decision markers' own cost (Brain.Predator/Alarm/Playmate…
  exist only on 7bd0768). Suspects: the TerrainSource paths Advance samples (F-22's RiverSampling.Lake per sample, the
  CaveSystem changes); a per-path marker split (ground / water / cave floor / walls) would name it. [animal, world]
- Aside: 7bd0768 had one 19 ms NetMenu.Update on its single spike frame (solo); not seen before, one frame.
- Files: results/prof-{6cfedaf,7bd0768}-townwalk-cap60-i13.txt. 7bd0768 predates the test-hooks merge: rebase before
  merging.

## 2026-10-30 03:52: v0.10.0 PUBLISHED (supervisor)
- https://github.com/Skeleman/Punchy/releases/tag/v0.10.0 (pre-release, "Punchy v0.10.0 (Windows preview)"), tag v0.10.0 =
  rc/v0.10.0 519fb5b25a2964a0cc3aa6f1f5f1ae9dfb48abe2, on Zach's go ("release a v10 before 7AM").
- Punchy-v0.10.0-windows.zip: 237 files, 47 MB, SHA-256 fee646b3f586285ecd5322742ffcb609095ccecc6cbe6297a42b6742e46953d7
  (re-downloaded and matched). pack.py check: backslash False, donotship False, exe True; the .from marker was held out.
- Notes: release/release-notes-v0.10.0.md. The Unity slot goes to the world lane next (F-40), then item 13.

## 2026-10-30: v0.10 light gate: rc/v0.10.0 519fb5b PASS (c7410cc gated in full, plus a re-gate after the arch NaN fix)
Results in Punchy-wt/Tools/bench/results (gate-c7410cc-*, gate2-c7410cc-*, regate-519fb5b-*).
- **Release build: PASS.** `C:\Users\zache\Projects\Punchy\Builds\Play` = 519fb5b (.from), built with a throwaway
  editor-only ReleaseBuild.Build (BuildOptions.None, the project's own settings: product Punchy, D3D11 then D3D12),
  no bench files in the tree or the player. Solo boot/quit ×2 with no -screen-* flags: exit 0, Direct3D 11.0,
  0 exceptions, 0 "Mesh bounds" / 0 NaN-bounds lines (logs ~4.5 KB). pack.py should drop
  Punchy_BurstDebugInformation_DoNotShip.
- **Arch NaN (found in this gate, fixed in 519fb5b): PASS.** c7410cc logged 10 arch meshes with NaN y at startup and
  then ~165 "bounds contain NaN" lines a second (~45 MB of Player.log an hour), from G-29 79b4221; absent from 807ee9f
  and v0.9.0. 519fb5b: 0 in every log (release, smoke, arches, town). Shots: regate-519fb5b-arches/icearch.png
  (glacier, 721,726: an ice arch crossed by a smaller one) and sandstonearch.png (MesaCanyonlands, 1266,185) both render.
- **Smoke vs v0.9.0 39f5558 (c7410cc, same island, cap 60 and 0, ABBA): PASS, borderline J-006 within v0.9 spread.**
  townwalk cap 60 1% low 41.6 (v0.9 41.1), cap 0 46.2 (45.5); glide cap 0 37.6 (36.2). Glide cap 60 30.5/27.7
  (mean 29.1) vs v0.9.0's own rounds 28.4-31.2: borderline J-006, within v0.9 spread. 0 exceptions.
  519fb5b vs c7410cc cap 60: townwalk 41.0, glide 31.8, no change beyond noise, 0 exceptions.
- **Looks (c7410cc): PASS.** Day town/cave/lake/river, night town/nightgrove, spire spot (still on the
  ElectricWastelands spires, arcs showing): all render, exit 0, 0 exceptions. Day town on 519fb5b the same.
- **MP host/join (c7410cc): PASS.** players 2 in every joined row, 0 IndexOutOfRange, 0 exceptions; joined town /
  townwalk 1% low 26.8 / 26.8 (v0.9: 26.8 / 25.1).
- **MP climb (item 12): PASS.** Client framed by a -shotlist re-applying the host's -goto/-camlook (command-line ones
  are overridden by the join's NearHost placement). The remote Monkey is upright on the Oak trunk mid-climb, hands and feet
  on the bark, a little off it (known animal-lane item): gate2-c7410cc-climb/shots/climb-11.png, climb-01.png.
- **MP mantle: PASS.** Flat-top picker: boulder rim 1.5 m. The remote Human stands on the boulder top (ledge-07.png),
  back down and up again across the run; the 0.8 s hands-on-rim pose fell between shots. 0 errors either side.

## 2026-10-30: cave-hitch A/B (world-laptop/cave-hitch d14f556 vs 1cbdd9f): PASS
d14f556 = MaxBuildFrames 4 → 60, one commit on 1cbdd9f. Cap 0, ABBA, 2 rounds, same island (every stop ends at the
same x,z in both builds), 0 exceptions.
| Stop | 1cbdd9f worst ms (r1/r2) | d14f556 worst ms | 1% low | avg |
|---|---|---|---|---|
| caveapproach | 138.6 / 108.0 | 24.3 / 27.2 | 30.9 → 52.5 | 96.3 → 98.2 |
| dash | 43.5 / 72.1 | 41.0 / 24.5 | 21.5 → 41.2 | 79.1 → 95.9 |
| glide | 40.2 / 33.4 | 41.3 / 33.1 | 36.1 → 37.2 | 79.7 → 79.7 |
- The caveapproach hitch (item 10) is gone: no 5 s window over 25 ms (before: 77-139 ms spikes in 5 of 7 windows).
  Dash slow frames 26 → 2. Glide unchanged.
- Shots at the end of every stop (results/shots-cave-<c>-r<n>/) match between builds: same framing, no holes,
  no missing terrain or flora. They're end-of-stop frames only, so pop-in mid-flight isn't covered; nothing
  late at the glide or dash end points.

## 2026-10-30: test hooks on wip/test-hooks 0019e8a: tree PASS, ledge picked a hut roof
- Tree (`-body Monkey -place tree:Oak -autowalk`): lone Oak at (-267, 27), 10 m; on the trunk at t23 (y 40,
  ground 36), in the crown at t27, back down by t38. Watch: `-goto -264.2,37.7 -camlook -264.2,40.3,33.7,-267.2,38.8,26.7`.
- Ledge (`-place ledge -autowalk`): rim (152.0, 27.3, -439.2), 1.3 m up, but it's the eave of a conical thatched
  hut roof in HumanVillage; the Human stands on the roof from t21 to t71 and never drops back. zache-db is fixing the
  picker (flat tops) before the rc. [animal]

## 2026-10-30: item 12 (remote climbing/mantle MP check): BLOCKED on a test hook
Both wire fields are on animal-behaviours (6d70335 Climbing, 2a8007a Rim). To check them two-instance on the laptop I
need the host to climb and mantle by itself, then frame it from the client with -camlook + -shot. Today
`-place tree:<species>` puts the player 6 m east of a lone tree without turning them to it, so `-autowalk` (camera at
tree + (16, 7, -16)) passes ~4 m from the trunk, and nothing brings a player to a ledge. Asks: [animal]
- `-place tree:<species>` facing the trunk (yaw towards it), so `-body Monkey -place tree:<X> -autowalk` climbs; and
- a `-place ledge` (or `-place mantle`): in front of a rim about 1.5-2.5 m up, facing it, so `-autowalk` mantles.
- Log the trunk/rim position (as now), so the client can `-goto` near it and `-camlook` at the climber.
With those I'll run host (`-host -body Monkey ...`) + client (`-join 127.0.0.1 -camlook ... -shot`) and post shots of
the remote climber and the remote mantle, plus the join log check.

## 2026-10-30: items 10 and 9 on world-features 807ee9f
- **Item 10, caveapproach hitch: still there, and it's terrain meshing, not caves.** Release build, 2 rounds:
  worst frame 108.9 / 102.8 ms (1% low 31.9 / 33.3, avg ~96). Dev capture: 2 spike frames of 2000. On them
  Workers BuildChunkMeshJob runs 39.3 ms (0.6 normally) while the main thread waits 37.8 ms (WaitForJobGroupID,
  inside the SimulationSystemGroup, 38.5 ms inclusive): TerrainStreamingSystem completing a mesh batch on the main
  thread, most likely the forced Complete after MaxBuildFrames (audit S2). CaveSystem.Update 0.18 ms and
  GroundDetail 0.13 ms on those frames; HideFarChunks doesn't show. Fix direction: smaller batches while the focus
  is moving, or don't force-complete (let the next batch start late); the 4-frame cap meets a 39 ms batch. [world]
- **Item 9, lake stop: done.** The lake stop is now PlaceAt (1328, ground, 1730), yaw -90 (b/bench-runner d5be092,
  pushed). Shot `shots/lake-807ee9f-1328-1730.png`: on the shore, open water west, trees and reflections, no
  water walls. Lake avg 104-107 fps; round 2 had one 156 ms frame (1% low 27.3 vs 59.1 in round 1), probably
  the arrival's streaming; it'll show again in later lake runs if it's real.
- Item 6 (swimmers) still needs a duck lake in FoggyMarsh/FungalForest/Grassland/Temperate: post x, z.

## 2026-10-30: item 8 follow-up, where the BrainJob growth could be (for the animal lane)
- Population isn't it: Net status "animals N" in the townwalk capture logs (mean once spawned) e4010f2 131,
  a4521a2 133, 6d0fbff 134, ea3bdad 136: ~4% more animals vs ~30% more BrainJob. So it's cost per animal.
- Read-only notes from a laptop session (zache-db, standing by; not the lane): in e4010f2..ea3bdad Advance itself
  only gained CreatureMotion.Accelerate (a min/compare). TerrainSource's diff there only touches the water path
  (F-22: one extra RiverSampling.Lake per sample in lake/bay areas); CaveSystem +164 is untested. Another
  candidate is the mix: with Accelerate, animals reach their targets later, so more are moving (paying for Advance
  and its terrain samples) on a given frame.
- To separate them: a `Brain.Sample` marker round the SampleHeight/SampleGround/Walled calls inside Advance, plus a
  per-frame count of moving creatures, on a commit that cherry-picks onto both e4010f2 and ea3bdad. Post the two
  commits in BENCH-QUEUE and I'll capture townwalk cap 60 on both. [animal]

## 2026-10-30: items 11 and 8 on animal-behaviours ea3bdad
- **Item 11, host/join: the join fix works.** 0 IndexOutOfRange and 0 exceptions on host and client, both rounds
  (the client in game with 2 players, 78-141 animals). Host cap-60 with the client joined vs solo: town 1% low
  32.6 → 26.6, townwalk 32.3 → 26.0 (same-laptop client), the same as 39f5558 and 4ec2cab.
- **Item 8, cap-60 townwalk, Brain.* split (one capture each):** Workers BrainJob a4521a2 2.24 → 6d0fbff 2.18 →
  ea3bdad 2.03 ms (e4010f2 was 1.58). Inside it, **Brain.Advance is 1.16 ms (57%)**, and on spike frames it grows
  1.15 → 1.82 ms. Brain.Predator 0.04 and Brain.Alarm 0.03; Hunter, Chaser, Playmate, Needs, Fidget, Decide and
  EaseGait are each under ~0.03 ms (below the analyzer's cut-off, so not listed). About 0.8 ms of BrainJob is
  outside every marker. Main-thread BrainJob 0.50 → 0.31 ms and CreatureWakeSystem 0.44 → 0.04 ms (fixed).
  So the remaining growth over e4010f2 is in Advance (movement), not in the decisions. Note: the markers'
  own overhead (~100 Advance calls a frame per worker) is in these numbers. [animal]

## 2026-10-30: for the supervisor: takeover checklist
Zach says the project passes to kind-possum when your tokens run out. What the laptop can't do yet (no gh, no
milestone page access, no messaging to main-PC lanes) and what the final handoff should cover:
`TAKEOVER-CHECKLIST.md`.

## 2026-10-30: status, items 8-11 running now (results here in ~75 min)
Order: 11 host/join on ea3bdad (the TrySpawn join fix), 8 cap-60 townwalk capture on ea3bdad split by the Brain.*
markers vs a4521a2, 10 caveapproach capture on 807ee9f (worst frame's top markers), 9 the new lake stop (1328, 1730,
yaw -90) shot on 807ee9f. Item 6 still needs a duck lake spot (FoggyMarsh/FungalForest/Grassland/Temperate): post x, z.

## 2026-10-29 late: item 5 profile, item 7 lake stop (BLOCKED)
- **Item 5, cap-60 townwalk profile 6d0fbff vs a4521a2:** the EaseGait trim cut posing, not the brain.
  Workers PoseJob 1.50 → 1.29 ms (slow frames 5.2 → 3.8). Workers BrainJob 2.24 → 2.18 ms: still ~0.6 ms over
  e4010f2's 1.58, so a4521a2's brain growth is mostly still there. New on the main thread: CreatureWakeSystem
  0.44 ms a frame (0 before). Spikes 4 → 3 of ~1750. One capture each. [animal]
- **Item 7, lake stop: BLOCKED, the spot still has water walls on 4ec2cab (F-22 is in it).** Placing the player
  facing 45° from (1118.5, 1553.5), so the camera sits about (1110, 1545), puts them on ground at 134 m, ~7 m under
  the lake surface (141.4) 10 m away, in a pit walled by vertical sheets of water: `shots/lake-4ec2cab-1118-1553.png`.
  F-22's own note says water walls went 6 → 2; this glacier lake looks like one of the 2. The route change is not
  pushed (the lake stop is unchanged). Needs from world: either the remaining walls fixed here, or a player
  position (x, z, yaw) on ground at lake level. I'll rerun item 6 (swimmers) on the same spot once there is one;
  say whether ducks live on that lake. [world]

## 2026-10-29 night: queue items 4-6
- **Item 4, host/join on world-features 4ec2cab: BUG, in v0.9 too.** Join works (players 2, 70-131 animals), but in
  round 2 the host threw IndexOutOfRangeException in FaunaDirector.TrySpawn twice, right after "player 2 has the same
  world; welcome". Cause (code unchanged since 39f5558; timing-dependent, so the gate's two rounds missed it):
  GatherPlayers() rebuilds `players` every frame, but `counts` (players x species) is only resized in Despawn(), every
  0.25 s. For up to 0.25 s after a joiner first reports a position, players.Count is 2 while counts fits one, and
  TrySpawn(playerIndex 1) reads `counts[offset + index]` past the end (`FaunaDirector.cs:769/791` at 4ec2cab). The
  exception aborts that frame's director update; nothing crashes. Fix: resize counts in GatherPlayers (or bail in
  TrySpawn when `(playerIndex + 1) * species.Count > counts.Length`). [animal]
  Perf with the client joined, same as v0.9: host cap-60 1% low town 40.5 → 26.8, townwalk 36.8 → 25.1 (same-laptop
  client). The cached WorldSync/NetMenu queries show no difference at this resolution.
- **Item 5, smoke 6d0fbff vs a4521a2: no change beyond noise** (townwalk@60 avg 58.9 → 59.0, 1% low 37.3 → 37.1;
  townwalk@0 86.1 → 87.5; glide@60 1% low 30.4 → 29.0, the J-006 note again). At cap 60 the frame rate can't show
  BrainJob's worker time; a cap-60 townwalk profile of 6d0fbff vs a4521a2's is running and will follow here.
- **Item 6, lake ab 3d1893d → 793f103: PASS but inconclusive.** lake@0 ~147 fps both, 1% low 96.5 → 90.2 (within
  noise). The lake stop now ends at a glacier lake (1103, 132, 1540), and the bench log has no swimmer count, so
  this probably didn't exercise the water shader's swimmer loop at all. Needs a spot with ducks: post a
  `-benchspot x,y,z,yaw` at a duck lake and I'll rerun it. [world]
- Runner: one lake row read GPU 5,679,878,978 ms (FrameTimingManager garbage); the compare ignored it, but parse
  should drop GPU values above a sane bound.

## 2026-10-29 night: v0.9 light gate on rc/v0.9.0 39f5558: PASS (no regression vs v0.8.0 ba87e14)
Laptop, D3D11, 1280x720 windowed, weatherseed 4, ABBA. Same island as ba87e14 at these stops (townwalk (366, -406),
glide (1197, 359)). 0 exceptions anywhere.
- **Smoke vs ba87e14: no regression.** The runner prints NOT CLEAN only for J-006: glide cap 60 1% low 29.5 → 29.4
  (under 30 on v0.8.0 too). Uncapped: townwalk avg 84.8 → 87.8, glide 77.9 → 80.6; GPU equal or lower.
- **Looks (screenshots checked):** day town, cave, lake, river; night town, night grove. Flora drawn, night windows
  and fires lit, cave lantern lit, fireflies. 0 "Invalid combination", 0 exceptions. The lake stop now ends on a
  glacier shore (1103, 132, 1540) with the camera against the ice: not a defect as far as I can tell, but a useless
  look shot; worth moving the stop or checking that spot in play. [world]
- **Host/join (first end-to-end run of `bench.sh mp`): works.** Both rounds the client got in game with 2 players and
  78-171 animals; 0 exceptions on host and client; the client's one "serverTick rolled back" warning came with
  the laptop running both games. Host cap 60 with the client joined vs solo: town 1% low 39.3 → 25.2, townwalk
  35.0 → 25.8, GPU +1.3-1.9 ms. The client runs on the same laptop (480x270, cap 20), so this overstates a real
  remote join, and there's no v0.8.0 MP number from this runner to compare with. Recorded as the MP baseline for
  queue item 4.
- Not run (not part of the light gate): quit-clean checks, the D3D12 look, the spires.

## 2026-10-29 evening: queue item 3 (capture for audit item 1, a4521a2)
Dev build, uncapped, town and townwalk (plus item 2's townwalk cap-60). No frames > 33 ms in either uncapped capture
(town median 11.4 ms, townwalk 11.2), so "doubles on spike frames" couldn't be tested here.
- **Pose half: confirmed.** The main thread runs a slice of PoseJob itself on every frame (1.0 call/frame, 0.15-0.25
  ms): CreatureRenderSystem schedules and Complete()s it in the same update, so the main thread waits on posing.
- **Brain half: present, not yet attributed.** The main thread runs BrainJob work on 20-60% of frames (0.2-0.6
  calls/frame, mean 0.14-0.50 ms), so something completes BrainJob mid-frame. CreatureRenderSystem's update is
  0.93-1.10 ms inclusive vs ~0.6 self, consistent with it being the waiter, but the analyzer's flat markers can't say
  which system calls Complete. Needs the hierarchy view (or a ProfilerMarker round the render snapshot) to confirm.
- **Size:** main-thread job waiting (JobHandle.Complete inclusive) 1.1-1.7 ms a frame, ~10-15% of an 11 ms frame.
  Workers are mostly idle, so overlapping brain and pose would mainly shorten the main thread, not free workers.
  Worth doing, not urgent. Not a cause of spikes at these stops. [animal]

## 2026-10-29 evening: queue item 2 (re-queued after the pause)
Laptop, 1280x720 windowed, weatherseed 4, noon, ABBA. Same island throughout (townwalk ends (366, -406), glide
(1197, 359), cave (-2677, -697), caveapproach (-2773, -726)); 0 exceptions. Smoke/ab take no screenshots.
- **smoke world-features 28d51e9 vs 0c0198a: PASS**, no change beyond noise. 1% lows townwalk@60 40.5→36.2 (one noisy
  round: 29.6 then 42.9), townwalk@0 45.6→48.8, glide@60 29.1→30.4, glide@0 35.7→33.6.
- **ab cave, caveapproach 0c0198a → 28d51e9: PASS.** cave@0 ~155 fps, 1% low 101→100; caveapproach@0 ~93 fps,
  1% low 29.1→29.2. Not new, but caveapproach has a ~110 ms worst frame on both builds, every round: a hitch on the
  way into the cave, worth a capture.
- **townwalk cap-60 profiles, e4010f2 → animal-behaviours a4521a2:** the townwalk fix works: Main AmbientLife.Near
  0.99 → 0.12 ms. But Workers BrainJob mean 1.58 → 2.24 ms (slow frames 5.5 → 8.1), PoseJob 1.40 → 1.50, and Main
  BrainJob 0.38 → 0.50 (the main thread running brain work while it waits: see audit item 1, being captured as
  item 3). Frame median 16.7 both (capped); p95 17.7 → 19.0; spikes > 33 ms 2 → 4 of ~1760. One capture each, so the
  brain growth is a lead, not a verdict: likely the G-10/F-06..F-12 behaviour changes in a4521a2. [animal]
