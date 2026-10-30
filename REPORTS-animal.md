# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-10-30 02:30: worms review (post-v0.10): what wip/worms and wip/world-worms each need before merging
Code read only. The contract matches on both sides (`_PunchyBurrowers[8]` = x, yaw, z, ±radius; `_PunchyBurrowerCount`
via SetGlobalInteger). Each half is inert without the other (count 0 → shader branch skipped; no shader → no marks),
so either can land first. Trial merges: wip/world-worms into world-features clean; the two worm branches touch
different files.
- **Why the crater "barely shows" is mostly the animal side's fault:** the feeder sends w < 0 only while
  `Air > 0`, at the worm's *current* position. So the crater rides along the 4 s, ~30 m breach arc under the worm's
  own body and is gone the moment it dives. There's no fixed mark where it broke out or where it went back in.
- **wip/worms needs:**
  1. Merge animal-behaviours: 2 trivial conflicts (CreatureData.cs, CreatureSpecies.cs: Tunnels next to the new
     EmoteReaction; keep both).
  2. Feeder fix: when a worm's Air goes 0 → >0, record a crater at that spot, and another where it dives back in.
     Keep each about 6 s and send its age 0..1 in y, which the shader ignores for craters today, so this is
     compatible. The 8 slots are shared, nearest first. It's worked out per machine from Air, which is on the ghost
     (CreatureGhosts), so clients see it too; a late joiner misses old craters (cosmetic).
  3. Check in play: the snow worm (never seen yet); whether the mound should sit at the head rather than the body's
     middle (Position is the body centre, and these are long, size-16-20 bodies).
  4. Supervisor's go (rule 11): two new species move the species table and the world fingerprint (incompatible
     with v0.10). Plus a smoke bench; the feeder is a plain loop over the creatures, no sampling, so it should be
     cheap.
- **wip/world-worms needs:**
  1. Strengthen the marks (its own note). Mound and furrow tilt peak at about 0.25-0.3 and darken at ≤ 0.28.
  2. Once (2) above lands, fade the crater by age (`b.y` when `b.w < 0`) and draw it at the fixed point.
  3. Optional: the furrow fades over 2-8 radii. "About 4 body lengths" in its comment is really 4 diameters, and
     these bodies are much longer than that.
  4. Check one fixed shot with the animal half merged in.
- Order I'd suggest after v0.10: animal feeder fix → world strengthens and fades against it → one joint look → merge
  each into its own lane branch.

## 2026-10-30 02:10: task 2, Brain.Advance split: DONE code-only (not merged)
- `wip/brain-sample` 7bd0768 (off animal-behaviours) and its twin `wip/brain-sample-e4010f2` 6cfedaf (never merge).
- Inside Brain.Advance: Brain.Sample (ground/water/cave/wall sampling), Brain.Phase (gait step), Brain.Moving (empty
  mark per moving creature: callsPerFrame = movers). Advance self = the rest.
- Reading: more Moving calls with Sample/call flat = more movers (Accelerate); Sample/call up = dearer sampling.
- NEXT: waiting on the profiler's run of wip/test-hooks 0019e8a; then merge it to animal-behaviours.

## 2026-10-30 01:55: task 1, test hooks: DONE code-only, wip/test-hooks 0019e8a (not merged)
- `-place tree:<species>` now faces the trunk. New `-place ledge`: to the nearest settlement, then (once props' tops
  load) on level ground 1.5 m in front of the nearest rim the body can mantle (about 1-1.8 m for a human), facing it.
- `-autowalk` with either: 12 s at the trunk or over the rim (jumping near a rim), 8 s back, repeating every 20 s.
- Both log `DebugControls: climb test: ...` with the trunk/rim position and ready `-goto`/`-camlook` client flags.
- Not compiled or run by me (no editor slot); the profiler builds and runs `-body Monkey -place tree:Oak -autowalk`
  and `-place ledge -autowalk`. Merge to animal-behaviours after both pass.
- NEXT: task 2 (Brain.Sample marker plus a moving count), code-only.
