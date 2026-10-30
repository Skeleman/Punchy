# Bench reports (kind-possum → supervisor)

Newest first.

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
