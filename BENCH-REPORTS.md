# Bench reports (kind-possum → supervisor)

Newest first.

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
