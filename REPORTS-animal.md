# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-10-30: NPC climbers on wip/trunk-hug too (f6fb2dd)
- **DONE, code-checked only:** `CreatureBrainSystem.Climb` now puts an NPC climber's feet on the bark while it's up the
  trunk, the same as the player's. An NPC has no collision radius, so at the foot (and out on a branch) it keeps the
  old stand-off of 0.8 × stand height instead; that stops its chest poking into the trunk while it stands facing it.
  It eases between the two over its first stand height going up and its last one coming down, and as it pitches
  upright. Clients see it through the ghost position, as before.
- My slot plan, after F-40 and item 13: compile, snow-worm check, joint look with the world lane, then the trunk-hug
  shots: the player Monkey with the client watching, plus any NPC monkey or squirrel climbing near by. Pass = paws and
  feet on the bark, no pop at the foot. If no NPC climbs in view I'll find a monkey group by hand (there's no hook to
  make one climb).
- NEXT: idle until the slot.

## 2026-10-30 (after v0.10.0): worms merged into animal-behaviours (149e4b0); monkey trunk gap on wip/trunk-hug (6d231af)
- **DONE: wip/worms (incl. crater fix 7cb0674) merged into animal-behaviours at 149e4b0, pushed.** The conflicts in
  CreatureData.cs and CreatureSpecies.cs were the two new fields next to each other (EmoteReaction, Tunnels); I kept
  both. Also put TurnTowards' doc line back above TurnTowards (the worm branch had left it above Tunnel's). Not
  compiled yet (no slot). Still waiting for a slot: snow-worm check, joint look with world (crater fade by `1 - b.y`),
  fingerprint go for v0.11.
- **Monkey off the trunk: diagnosed, fix on `wip/trunk-hug` 6d231af (off 149e4b0), code-checked only.** In climb-11
  the body is about 1 m out from the trunk's centre, leaving a gap of about half a metre to the bark. It's an outward
  gap on the approach side, not a sideways one; it only looks sideways because the client's camera views the climb
  from the side. Causes:
  1. `ClimbTrunk` held the feet 0.6 × 0.35 m out from the bark, and the animator hangs the body a leg's length out
     from the feet.
  2. The player's body is a `Puppet`, so while climbing its front paws tucked up like "sitting up tall" instead of
     holding on the way a climbing NPC's do.
  Fix: the feet go onto the bark, eased in over about 0.2 s. When it lets go at the foot, the body steps back to the
  collision radius, so collision doesn't shove it out. A puppet keeps its front paws out while climbing. Other players
  see the same, since it's their transform and the same pose. Upright folk aren't affected (the fold only applies to
  non-upright legs).
- To check (next slot, any order): `-body Monkey -place tree:<X> -autowalk` with the same client `-camlook` shots.
  Pass: the paws and feet touch the bark, there's no visible gap, and there's no pop when it steps off at the foot.
- Seen while reading, not changed: NPC climbers (`CreatureBrainSystem.Climb`) hold their feet 0.8 × stand height off
  the bark. By the same reading that's an even bigger gap. Worth an NPC monkey or squirrel in the same shot.
- wip/brain-sample 7bd0768: agreed. A merge would keep the hooks (the two-dot diff only shows 3025dbf missing from it),
  but I'll merge animal-behaviours into it after item 13's capture so the hash being measured doesn't change.
- NEXT: idle until a slot or the next task.

## 2026-10-30 02:05: test hooks merged into animal-behaviours (3025dbf); worm craters on wip/worms (7cb0674)
- Profiler run of 0019e8a: tree PASS. Ledge: the drop-back worked (Net status alternates top/start every 10 s), but
  the pick was a thatch eave. Fixed in b7d82cd: flat tops only (level within 15 cm, 1.2 m in, ±0.5 m), a sloping one
  only as a fallback; the log says "flat top" or "sloping top". Merged; the rc build compiles it.
- wip/worms 7cb0674: craters fixed at break-out and dive-in, 6 s, age in y (world fades by `1 - b.y`). Not merged.
- Post-v0.10 note: the climbing monkey hangs slightly off the trunk's side.
- NEXT: idle until the supervisor's next task.

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
