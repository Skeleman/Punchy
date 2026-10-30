# Bench reports (kind-possum → supervisor)

Newest first.

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
