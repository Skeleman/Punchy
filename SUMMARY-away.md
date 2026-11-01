# While Zach is away (2026-10-30 17:00, about 24 h)

Kept by the supervisor (kind-possum), newest first. The full detail is in REPORTS-*.md, BENCH-REPORTS.md and
FEEDBACK-v0.10.md (Z-IDs). No release goes out without Zach's go.

## 2026-10-30 ~22:30: first task-session slot (animal)
- A fresh task session ran the animal slot from HANDOFF-animal.md: about 163k tokens for the whole slot (the old lane
  paid ~50k per call at its size).
- **Merged:** animal-behaviours 4f2f0a4, world-features 247c690.
  - Worm trail with 32 marks and its range on the graphics Detail slider; the swimmer ripples; the worm arc and rock
    avoidance (Z-28); carrying (Z-20, checked with a cat and a dog); creature graphics ranges; RockAt.
  - **Z-07 spinning fixed:** animals turned at full speed within 10 cm of their target; an elf's door past its leash
    pointed it at its own spot. The spin log dropped from 97 lines to 14.
- **Still open:**
  - Z-22 worker swings: too small in frame, so poses aren't merged.
  - Spins while ducks, rabbits and prairie dogs play.
  - Carrying a large animal, and a remote carrier.
  - Z-28 jumps over rocks; one worm lies curled after landing.
- **Graphics default:** the profiler measured Low as the only preset with frame-rate headroom on this laptop (Medium
  gliding sits at ~30, as in v0.10). Left for Zach to decide; an in-between setting is queued for a test.

## 2026-10-30 21:25: usage limit; setup change
- All four sessions hit the account's 5-hour usage limit at 17:21 and stalled until it reset at 20:30 (about 3 h lost).
- Token telemetry: the world and animal lanes' contexts (~800k and ~530k tokens, re-read on every call) were ~75% of
  the spend. Dashboard: https://claude.ai/artifact/XrDf6foLq56Umx9HziqiwA
- **Decision:** world and animal write HANDOFF-<lane>.md and pause. Their queued code items run as short, fresh task
  sessions started from the handoff, one at a time. The profiler finishes slot 4 (worm/wake cost, gfx preset sweep).
- **For Zach on return:** restart the world and animal lane sessions from their handoff files (cheap), or keep the
  task-session setup.

## 2026-10-30 17:05: starting point
- **Merged so far (v0.11):**
  - World: graphics settings (F5 panel, presets, Quit), cave-mouth hitch, Blocked fast path, F-40 trodden ground,
    G-37 rocks, worm marks, the Z P1 fixes, creature graphics ranges.
  - Animal: climbing (trunk-hug, trunk-lean), worm colours, the Z P1 fixes, the wolf howl, the rattle, the moo and
    croak, jump heights, ghosts.
- **In code, waiting on a slot:**
  - World P2 (Z-30, 33, 36-42, Z-12 skirt, Z-38 caps).
  - Animal Z-20 carry, Z-21/22 poses, Z-28 worm arc + rocks, Z-07 spin check.
  - The worm trail and wakes, after the profiler's cost pass.
- **Unity now:** the profiler (item 17 worm/wake cost, the graphics preset sweep). Next the world lane, then the animal lane.

## 2026-10-31 00:55 — world task slot (world-features 3976d17)
- Merged world-laptop/z-p2 (Z-30 grain, Z-31..Z-42, Z-12 skirt, Z-38 caps). Clean build; no exceptions in 35 look runs.
- Passed on sight: Z-38 brown caps, Z-42 ice slab/crag (crag reads a bit like a cairn), dwarf roofs/stone houses day and night.
- Not judged (shots missed): Z-30 before/after, Z-31..37, Z-39..41 close-ups, Z-12 skirt, Z-09/10/11 and cave-mouth shards. Causes: `-place tree:` can't find props, stale falls/cave coordinates, no rain outside storm stops. Next slot in HANDOFF-world.md.
- Smoke flagged glide 1% low 36→23 from ~500 ms spikes; ABBA rerun showed no change beyond noise. Decision: keep the merge; the profiler re-checks glide spikes in its next slot.
- Note: synthetic key presses went to another window. Task sessions are told not to send keys outside the Punchy window.

## 2026-10-31 03:00 — profiler slot 5 on 3976d17 (BENCH-REPORTS e36f31e)
- In-between gfx (view 0.8, detail 0.75, shadows 0.8) holds J-006 on glide. Glide 1% low: in-between 34.0, Medium 28.9, Low 57.4. Townwalk: 51.9, 41.6, 57.5.
- Worm marks at 32 slots: +0.2 ms GPU, inside the noise.
- The ~500 ms glide spikes in the world smoke didn't come back: worst frame 43 ms over 10 runs. They hit the older build too and weren't GC or shader compiles, so likely an outside stall. Not a z-p2 regression.
- For Zach: the default preset is still his call. Options: keep Medium, make the in-between a "Balanced" default (passes glide, less visible downgrade), or use Low (the profiler's pick).
- Next: animal task slot.

## 2026-10-31 06:00 — animal slot 6 (two task sessions)
- Merged into animal-behaviours: wip/z28-air f116dd4 (Z-28: the worm flies straight out of the ground; the coil after landing is gone) and wip/poses 9b7459b (Z-22: the woodcutter, smith and dwarf smith visibly swing). f0aacd5 (both plus z07-play) built clean; the smoke showed only noise. The exact merged tree gets its own build in the next slot.
- Not merged: wip/z07-play. Spin lines went 20 → 15 but Play circling barely changed (13 → 12).
- Still open: Z-07 Play circling, the Z-20 remote carry (the host never picked up a dog), and miners (no mining camp on this seed).
- Token note: a task session that hands back while its own scripts are still running wakes again at each step (about 190k per wake). From now on, task sessions finish their runs before handing back.

## 2026-10-31 08:00 — world slot 2 (world-features b870008)
- Merged world-laptop/z30-more: the grain rewrite 632f479 now shows clear plank streaks, plus more timber marked (69f3185, not yet seen up close). Built clean; no exceptions in 7 runs. animal-behaviours 9b7459b + b870008 also compile together (tmp, not merged).
- Tooling: `-place prop:<Species>` logs real prop coordinates; `-benchday` (b/bench-runner db3c1f2) allows any weather at any stop; `-caveshards` probe.
- Passed by sight: Z-33 banners, Z-36 ripples, Z-39 ember pine, Z-40 forge, Z-41 ridge, Z-12 skirt, Z-09 (aurora behind clouds), Z-37 fireflies (weak).
- Open: Z-10 fog not judged; Z-11 rain (light, no wiggle); cave shards not seen. New animal bug: paw prints land on the bench seat when a dog walks under it.
- Next: the profiler checks the cost of the new grain on b870008.

## 2026-10-31 08:15 — profiler slot 6 (b870008 vs 3976d17, BENCH-REPORTS 309d31f)
- The new wood grain costs nothing measurable: Medium ABBA within noise (glide 1% low 31.1→31.0), town GPU 9.5 vs 9.8 ms. No exceptions, no NaN, no 500 ms spikes.
- In-between gfx on b870008: glide 35.6, still passes. Medium glide is still about 31, below J-006.
- Next: animal slot 7 (Z-07 Play circling, the bench paw prints, the Z-20 remote carry, a build of the animal-behaviours head).

## 2026-10-31 10:00 — animal slot 7 (animal-behaviours 77c03e9)
- Merged wip/z07-play2 960df87. Z-07 cause: a run past its range blended "away from the chaser" with "back to the middle", which cancelled out, and blocked runs tossed a coin for a side every frame. Spin lines over 3 runs: 38 → 24, Play spins 29 → 6.
- Merged wip/trace-seat 6283358: paw prints stay on the ground and no longer cover a bench seat (checked by sight).
- Merged with b870008, the tree built clean (a66c520); the smoke on the near-identical c477c0f showed only noise.
- Left: Z-20 remote carry needs a design call (the host never targets a client's body); songbird and dormouse Wander spins.
- Next: world slot 3 (Z-10 fog, Z-11 rain, cave shards, close-up of the newly marked timber).

## 2026-10-31 12:00 — world slot 3 (world-features b39522b)
- Merged; built clean (c7bb5f3, the same tree), 0 exceptions in 6 look runs. Each fix checked by sight before and after.
- Z-31/Z-32: stone house walls are flat blocks; the banding at night and in rain is gone. Z-34: the lines across cave streams were streak foam, now off underground. Z-35: falls run at the asset's 2.5 s fall time with spray at the lip.
- Passed: Z-09, Z-10 (marsh only), Z-11 heavy rain, Z-30 where seen close. No cave-mouth shards anywhere.
- Left: a cave stream with no pool (design call), hall and inn timber close-ups, a non-marsh river in fog.
- Decision: next slot builds a v0.11 *candidate* (world b39522b + animal 77c03e9) and runs the release gate, so Zach can decide on return. No tag, no publish.

## 2026-10-31 16:30 — v0.11 CANDIDATE ready for Zach's decision (GATE-v0.11-candidate.md, RELEASE-NOTES-v0.11-draft.md)
- cand/v0.11 21f1193 = world b39522b + animal 77c03e9 (clean merge). No tag, no release.
- Gate vs v0.10.0: Release build, boot and quit pass (0 exceptions, 0 NaN). Townwalk 1% low 37.8 vs 39.2. Glide at cap 60 29.7 vs 29.6 (borderline J-006, same as v0.10). Looks pass at 9 stops. MP pass (joined townwalk 22.8 vs 19.5).
- Flag: glide uncapped shows 50–80 ms spikes in 3 of 4 candidate runs (none in 10 runs of the base builds); ABBA 1% low 31.3 vs 35.6. Doesn't block the release, in the gate's view; the profiler is profiling it now.
- Zip (local only): Punchy/Builds/Zip-v0.11-cand, 49,468,639 bytes, SHA-256 847f35c1cb4319fa8966122a98442eb957b2237b31577480c0cc237774ad7337.

## On your return: decisions for Zach
1. Release v0.11 from cand/v0.11? The profiler (slot 7, BENCH-REPORTS a51577b) couldn't reproduce the glide spikes: two dev captures and a release ABBA vs a66c520 are level (35.3/36.3 vs 35.6/36.6, worst frame 38–40 ms on both), with no 50 ms+ windows. Likely an outside load during the gate. Nothing blocks it.
2. Default graphics preset: keep Medium (glide ~30, below J-006), a "Balanced" in-between (view 0.8, detail 0.75, shadows 0.8: glide 34–36, passes), or Low (the profiler's pick, ~57)?
3. Setup: restart the world and animal lanes from their handoffs, or keep the one-at-a-time task sessions? Since 21:25 they've merged about 10 Z-batches without a usage-limit hit; they cost roughly 150–300k tokens per slot.
4. Design calls: Z-20 remote carry (the host never targets a client's body); the cave stream with no pool; the P3 rows Z-50–Z-53.

## 2026-10-31 18:40 — work resumed
- No sign of Zach by 18:40, so I'm keeping work going (his "keep working"). Started animal slot 8, about 2 h: Z-20 remote carry, using the client-carries-cat plan, and Z-22 miners on a seed with a mining camp. Zach: if you want the laptop, say so and the slot stops at a clean point.
