# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-10-31: slot 6 (part 1): merged build compiles, Z-22 still not judged, worm coil and Z-07 Play spins found
- **Build:** tmp/animal-slot6 f5b6597 (animal-behaviours 4f2f0a4 + world-features 3976d17 + wip/poses 2755204):
  compiled clean. Smoke against 3976d17: nothing worse beyond noise, 0 exceptions. The J-006 glide floor flag
  predates this build (1% low went 26.4 to 29.6).
- **Z-22:** `-watchfrom 0.6,2.5` sits too low. Only legs and shadows are in frame (the shadows show a raise and
  strike). DwarfHold smith: the arm raises, then strikes. Too small to pass. wip/poses NOT merged.
  - No miners exist on this seed: DwarfMiningCamp and HumanMiningCamp aren't among the placed landmarks.
- **Z-20 large:** Monkey (0.38 m) is held on open forearms and looks right. Marmot is held too.
  - Both squirm free in under 5 s.
  - Turkey and RedFox fled (CanCarry needs a pettable animal of 0.5 m or less).
  - Remote carrier: the client's join timed out at the handshake (host at 3-10 fps while loading), so it fell back
    to solo. Retry with the client 45 s after the host.
- **Z-28:** found the flat curl. `Tunnel()` kept turning in the air towards a close target, so the worm orbited and
  coiled flat in mid-air (Tors torsside-23 and Glacier).
  - Fix: wip/z28-air ba7ff69 on animal-behaviours. There is no turning while Air > 0, so the arc also stays straight
    over the obstacle it was aimed to clear.
  - Not yet seen in a build.
  - Boulder routes from above (watchpitch 70): too close to read.
- **Z-07 Play (from -spincheck at PrairieDogTown, a lake and OakGrove; 20 lines):** two causes.
  - Stage 0 (target 0.3-0.65 m): the bow turns to track a partner running round it.
  - Stage 1 (target 8 or 20 m): blocked ahead, a coin toss of side every frame while Play re-aims at the obstacle.
  - Fix: wip/z07-play (holds heading through the bow; veers to one side). Compiled into tmp f0aacd5 only if pass c
    ran.
- **Pass b was still running at handback:** build 1e7fb47 (tmp + z28-air), then higher work close-ups
  (`-watchfrom 1.7,3`), the net retry and worm shots, into results/animal-slot/s6-workb, s6-host, s6-client and
  s6-wormb. Its script releases the lock and leaves the main checkout on b/weather-mapjob when it exits. Pass c
  (f0aacd5, spin re-check) has not started.

## 2026-10-30: slot 5: Z-07 fixed, worm and wake pairs, carry and worm arcs merged
- **Build:** world-features + animal-behaviours + spin-check + poses + carry + worm-arc + both pairs compiled clean,
  blocked-top included (zache-73: Blocked(..., out top) and RockAt build). Rebuilt with the Z-07 fix: clean.
- **Worm marks:** capped at 32 slots on both sides; the feeder reads `_PunchyBurrowRange` (DetailRange(BurrowRange)
  from GraphicsSettings), the shader gates on it. Marks and craters render from above. Cost not re-measured.
- **Z-07:** `-spincheck` at Start (Dog body) and ElfGrove 9/15/20 h logged 97 spins: elves in Home stage 0 and dormice
  in Climb stage 0, at 0.00 m from their target at full turn rate. The way to a target under 10 cm away is noise; and
  an elf's door beyond its leash was clamped to a spot it stood on. Fix (wip/z07-spin d337490): no turning within
  10 cm, no leash clamp in Home mode. Re-run: 14 lines, none of them elves or dormice; the rest is Play-mode
  circling (open).
- **Z-20:** cat and dog carried with the arms open under them, front-on and side-on, sprinting too. A cat squirms
  free after about 2 s (by design). Large animal and remote carrier not seen (open).
- **Z-21/Z-22:** sprint side-on looks fine; worker swings too small in frame to judge. wip/poses not merged.
- **Z-28:** clean breach arcs side-on; obstacle jumps and boulder routes not seen.
- **Merged and pushed:** animal-behaviours 4f2f0a4 (spin-check, z07-spin, carry, worm-trail, wake-trail);
  world-features 247c690 (worm-trail-shader, wake-trail-shader, worm-arc). Same push.
- Unity closed, no Punchy.exe left, lock released, main checkout back on b/weather-mapjob.

## 2026-10-30: Z-20 carried animals follow the arms: wip/carry 95d50e8 (code only)
- **Real fix, local on every machine, no network change.**
  - `CreatureState.CarryWidth` is local. HeldPoseSystem sets it on every carrier body from the held creature's
    half-width.
  - The Carry pose opens the arms by it: shoulders out up to 0.7 rad, elbows 1.25 to 0.83 rad.
  - HeldPoseSystem poses the carrier and puts the animal between the posed hand tips, so it moves with the arms.
  - Bodies without two arms keep the carry table.
- **To check in the slot:** the opening's direction, the sit along the forearms, and small, medium and large animals
  side-on and front-on, plus a remote carrier from a client.

## 2026-10-30: Z-07 spin readout, Z-21 and Z-22 poses (code only), Z-20 needs a design call
- **Z-07 readout, wip/spin-check 494ada9 (debug only):** `-spincheck` logs any creature that turns a whole circle
  within 5 s while under 0.3 m/s, with what it's doing and where. `-watch` lines now give yaw and turn rate.
- **Z-21, wip/poses 2755204:** a sprint amplified every leg joint, the ankle included, so the foot folded against the
  shin at push-off. Now only the hip and knee get the longer stride; the foot keeps its run angles, eased 35% toward
  rest.
- **Z-22, wip/poses 2755204:** chopping, hammering and mining now have a slow raise with a hold at the top (the
  wind-up), then a fast strike.
  - The torso leans back and twists away while raising, folds forward into the blow, and the head drops.
  - The hammer's movement is under half the axe's.
  - Legs are untouched, so the feet stay planted.
- **Z-20: needs a design call.** HeldPoseSystem puts the animal at per-body carry-table hand spots, but the carrier's
  Carry arm pose is fixed. To fit the arms, the carrier's state needs the held animal's half-width. The options are
  that, or fixed arm poses in three size classes.
- All need a slot to compile and shoot.

## 2026-10-30: Unity slot 15:35-16:10: v0.10 fixes compile and merge, Z-06 confirmed, Z-07 not reproduced
- **Compile PASS** on world-features 7f519c0 plus animal-behaviours, wip/v010-fixes, wip/gfx-creatures and both
  wake-trail halves (test build 761dae5). No compile or shader errors, and no exceptions in any run.
  - So gfx-creatures compiles against the real GraphicsSettings.
- **Z-06: PASS.** The player's Bow emote as a horse, a deer and a dog, side-on in a meadow: the chest dips, the front
  legs bend, and the hind feet stay planted.
- **Z-07: not reproduced.**
  - HumanVillage at 19:30, a Human watched for 75 s: it walked home, then the watch picked another Human walking to a
    seat 113 m off.
  - Four spawned squirrels in an OakGrove for 50 s: they climbed, with no spin.
  - Still open. A faster repro needs a yaw-rate readout in the -watch log line, or Zach's spot and time.
- **Z-27 wakes: renders.** Spawned ducks on a lake show the spreading rings round each one. The mask path compiled.
- **Merged:** wip/v010-fixes into animal-behaviours at d0388e2, pushed. That's Z-02 (player side), Z-03, Z-04, Z-05,
  Z-06, Z-08, Z-14 and Z-23 to Z-26.
- **Not merged:**
  - wip/gfx-creatures 6f6dd94: it sits on the world lane's gfx branch, so it belongs in world-features. It merges
    clean there, and compiled in this slot.
  - wip/wake-trail plus wip/wake-trail-shader: they wait for the profiler's cost check (item 17).
  - The worm trail is waiting on item 17 too.
- **Not done:** Z-20, Z-21 and Z-22 pose shots; those rows aren't coded yet.
- **Unity closed** at about 16:10. No Unity or Punchy processes, the lock is clear, and the main checkout is as found.

## 2026-10-30: v0.10 feedback Z-14 and P2 (wip/v010-fixes, wip/wake-trail), code only
All code-checked only, none compiled; they need a slot.
- **Z-14, 2ab65e4:** the wolves' howl is softer, distant and once a night per pack.
  - Volume 1.0 to 0.5.
  - Synth: breath layer halved (the static), gentler vibrato, and a baked 1.2 s echo tail (`SoundSynth.Distant`).
  - GameAudio (shared file): every voice now has a distance low-pass, open to 40 m and down to 1.8 kHz by 200 m.
  - Each pack howls in one half-hour window a night, 21:00-03:00, keyed by its home (`HowlTime`). Before, a big pack
    could pass the chorus round all night.
- **Z-23, 1904241:** each body jumps its own height. `CreatureSpecies.PlayerJump`, else from size
  (sqrt, 0.45-1.4) with climbers 1.25x.
- **Z-24, ce4d3d7:** new `SoundId.Rattle` (appended). The rattlesnake had no calls at all; it now rattles by
  day, and a player rattlesnake rattles with every emote.
- **Z-25, 7e9ce49:** a ghost player can talk to ghosts, and they don't vanish from it.
  - Animals don't flee a player of their own species.
  - Ghosts talk only to a ghost player.
  - New Ghost culture in dialogue.json.
- **Z-26, 15e972c:**
  - The moo now rises and opens from a hum; it used to fall, which read as a groan.
  - The croak is rebuilt as clean pulse trains, "rib-bit", in place of the noisy vibrato that sounded like a burp.
  - New `SoundId.FrogChorus`: a night hum looped from among frogs within 40 m, louder the more there are.
- **Z-27, wip/wake-trail 34e2bdb plus world half wip/wake-trail-shader 40cd4b7** (Water.shader, for zache-73 to
  review). Swimmers drop ripples every 0.5 m or 0.5 s. Each spreads as a ring from where it was made over 3 s, so the
  wake is left in the water. 48 slots, 5 m reach. As with the worm trail, the two halves must land together.
- **Still to do:** Z-20 (held animals following the arms), Z-21 (foot against the shin in the long run stride) and
  Z-22 (putting the body into chopping and hammering). These are pose work and best tuned with shots in my slot.
- **Fingerprint:** no network-table changes. The new SoundIds are appended; the rattlesnake asset gains a call.

## 2026-10-30: v0.10 feedback P1 (wip/v010-fixes) and creature graphics ranges (wip/gfx-creatures), code only
All code-checked only, so none of it has compiled yet. They need a slot build.
- **Z-02 (player side), f378a43:** in a cave, a flight step that would enter rock was cancelled whole, so flying at a
  wall the bird hung in mid-air. That is the likely player-side cause. It now slides along level if that's clear,
  else sinks until it's down. Not reproduced; the cave side is zache-73's.
- **Z-03, 30c809c:** "swimming" now needs the player down at swimming height, so a jump over a river is neither
  slowed nor carried by the current.
- **Z-04, 30c809c:** the player fish's leap left it just under the surface line the swim branch tests, so the next
  frame zeroed its rise. It now stays airborne while rising.
- **Z-05, 30c809c:** player bodies (Puppets) skip the timed-call clock, so they vocalise only by emoting.
- **Z-06, 30c809c:** an animal body's Bow emote (a play bow, which reads as drinking) tipped about the middle, so the
  hind feet lifted. G-12 had fixed only the lowered head (HeadDown).
  - Now the bow also tips about the hips.
  - Hoofed bodies asked to sit lie down with their legs folded instead.
- **Z-07: needs a repro in a slot.** Reading the code found no clear spin in the sit stages or fidgets.
  - Next slot: watch hearth and seat sitters and a squirrel.
  - Suspects: an unreachable sit spot, where it orbits the target until the 60 s give-up, then picks it again; or the
    target sitting under it.
- **Z-08, f96056c:** breath now comes from each body's own mouth.
  - `CreatureData.MeasureMouth` measures the end of the head in the idle pose at bake.
  - Players' bodies now breathe in `CreatureBreathSystem` too.
  - AmbienceSystem (world lane's file, 2 lines) skips its fixed human-height player breath while players have bodies.
    That is the likely cause of the chicken player's breath above its head.
- **Graphics settings, wip/gfx-creatures 6f6dd94** (off 9cf4e26): the ranges below are now
  `GraphicsSettings.DetailRange(old value)`, so nothing changes at 1.0.
  - CreatureRenderSystem: foot detail 40 (passed into PoseJob, since Burst can't read the setting), torch flame 150,
    lantern 60, colonies 45, cave fish 35, swarm drawing 40.
  - TraceSystem: 40.
  - Swarm anger and timings stay fixed. BehindCull is left alone.
  - The burrow feeder is wired when wip/worm-trail lands with it.
  - "F5 graphics / quit" added to the F1 key line.
- **Fingerprint:** no network table changes. CreatureBlob gains Mouth/HasMouth (baked locally).

## 2026-10-30: Unity slot 08:20-09:05: climbing passes, worm trail of bumps (Zach's request)
- **Compile PASS:** worm-look, trunk-lean and the world worm shader, together.
- **Monkey climbs: PASS on oak and palm,** with client shots.
  - Oak: hands and feet are on the bark low down, and it hugs the drawn trunk at its leaning top.
  - Palm: it stays on the bark through the bend and at the crown. Find one with `-place Dunes -place tree:Palm`;
    no palm stands alone near the start.
- **Hold logs:** every hold is on the east side, the side it walks in from, 0.0 m from the logged spot. The far-side
  climbing seen in the last slot is gone.
- **Snow worm band fix: PASS.** The body reads as one even pale colour.
- **Merged into animal-behaviours and pushed:** fade08c (wip/trunk-lean) and d83b1ee (wip/worm-look). The tree is
  identical to the tested build.
- **NPC lean:** not looked at. It's still a straight post for animals; your call after these shots.
- **NEW, from Zach mid-slot.** The worm's trail turned with it (he wants separate bumps left in the ground), it broke
  out a few metres from its crater, it vanished before it was all back under, and its marks went too soon.
  - **wip/worm-trail 93c79bc, the animal half:**
    - `CreatureBurrowSystem` leaves a bump every 2 radii, kept 30 s, plus the mound over the head. Nothing carries a
      heading any more.
    - Craters are kept 40 s and placed where the body crosses the ground. They used to go where the arc began, under
      the ground and metres back.
    - The arc in `Tunnel` starts and ends 0.8 of the body's length deep, so the worm only levels out once it's all
      under. About 3 s of it is still above the ground.
    - Thrown sand and snow lands and lies 25-40 s as clods (`Trace.Rests`). Water splashes are unchanged.
  - **wip/worm-trail-shader 3338e66, the world half:** one commit on world-features for zache-73 to review.
    `BurrowMarks` reads 64 slots of (x, age, z, ±radius), draws each bump as a soft hump that sinks away late in its
    life, and drops the furrow. Craters are as in ad98690.
  - **Slot check: PASS** (build of both halves; shots in `results/animal-slot/trail`).
    - A row of separate bumps stays where it was raised.
    - The worm comes up through its crater and dives back into it. Its body goes all the way in, and clods lie round
      the hole.
    - Craters and trails are still there a minute on.
  - **Shader cost (for the profiler, items 15/16):** while any mark is within 250 m of the camera, every
    TerrainSurface fragment loops over up to 64 marks.
    - Each mark costs a float4 load and a squared-distance test against 3 radii, and nearly all are rejected there.
      Only a fragment inside a mark does about 15 more ALU (an exp, plus a length and a second exp for a crater).
    - In the worst case, with a worm in view and its full trail, that's 64 iterations per terrain fragment. With no
      worms near, the loop is skipped whole (`_PunchyBurrowerCount` is 0).
    - It used to be 8 slots with a 10-radius reach. If it shows up, the cheap fixes are 32 slots or a per-chunk
      bounds test.
  - **Not merged:** the feeder and the shader must land together. The new feeder against the old 8-slot shader
    would misread the slots.
- **Unity closed** at 08:58 (bench timeout). There are no Unity or Punchy processes, the lock is clear, and the main
  checkout is as found.

## 2026-10-30: climbers follow a leaning trunk: wip/trunk-lean 26ddffe (code-checked only)
- **DONE, code only.** The player now grabs and climbs round the trunk's centre at its own height, not a straight post
  at the foot. That covers Broadleaf lean and wiggle, Conifer lean and Palm bend.
- **Flora change, minimal and additive:**
  - `FloraModel.TrunkLeanMid/TrunkLeanTop` hold the lean at half and full height. They're filled from a new
    `Grower.TrunkAxis`, which Broadleaf, Conifer and Palm set from the centreline they already build.
  - `TrunkAxis` is separate from `TrunkPath` because `TrunkPath` switches on brackets and bark faces, which draw
    from the seed.
  - `FloraObstacle.LeanMid/LeanTop` are turned and scaled the way `Flora.hlsl` draws the tree, with a new
    `FloraObstacle.CentreAt`.
  - No new random draws; meshes and placement are untouched, so the world and fingerprint are unchanged.
- **Player side:** `PlayerMotion.TrunkLeanMid/Top`, `PlayerMovementSystem.TrunkCentre`, GrabTrunk and ClimbTrunk.
  The `-autowalk` climb test pushes at the centre too. Nothing on the wire.
- Told zache-73 the files and lines. It trial-merges clean with world-features and with world-laptop/f40.
- Not covered: NPC climbers. Their tree spot comes from `FloraGenerator.Shape`, which grows no mesh, and CreatureState
  is replicated, so carrying a lean there means a FloraShape field filled while baking the blob. That's a follow-up
  if you want it.
- Next slot (with wip/worm-look): `-body Monkey -place tree:Oak -autowalk`, with client shots as before and a palm
  (`-place tree:<Palm species>`). Pass = it hugs the trunk as drawn high up and doesn't drift off a leaning trunk.
  The new hold log (on worm-look) will also show which side it takes.
- NEXT: idle until the slot.

## 2026-10-30: trunk-hug merged (2272733); worm band, far-side climb and a still camera (wip/worm-look 85a1962)
- **DONE: wip/trunk-hug merged into animal-behaviours at 2272733, pushed.**
- **1. Dark band on the worm: the body's colouring, not shading.** Both worms reuse the `SnakeBody` limb, where every
  other segment takes the coat's Accent colour (a snake's bands). The snow worm's accent was dark blue-grey (0.55,
  0.6, 0.7 on a base of 0.82, 0.85, 0.9), and the sand worm's was dark brown. That gives hard-edged rings square to
  the body (worm-026/027/028); the edges are segment joins. I ruled out the shader's darkening: it's only a 12 cm
  contact band at the creature's own height, plus downward faces. Fix (c751907): each accent is now a shade off its
  base, so the segments read as rings. Colours only; nothing on the wire.
- **2. Far-side climb: the collider isn't offset at the base, but the drawn trunk drifts away from it higher up.**
  - The collider (`FloraSystem` obstacles) and the tree's base come from the same `FloraPlacement.Place` position.
  - The drawn trunk leans and wiggles above the base. Broadleaf: a lean of up to 7% of height × t² (up to about 0.7 m
    at the top of a 10 m oak) plus a wiggle of about 0.6 × trunk radius per section. Conifer: up to 3%, linear.
    Palm: a bend of 8–25% of height.
  - The climber follows a straight cylinder around the base, so on a leaning tree it hangs off the drawn trunk higher
    up. That also accounts for part of the gate's gap.
  - It doesn't explain which side the player climbs: Net status put it about 0.3 m+ on the far side of the base.
    The climb test now logs each new hold: the trunk's position against the logged spot, its radius, and which side
    the player is on. The next slot will settle it.
  - Proposed fix, needs your go because it touches the Flora files (world lane): FloraModel keeps the trunk's
    centre offset at half and full height (the generators already have `TrunkPath`), FloraObstacle carries it turned
    and scaled, and ClimbTrunk (plus the NPC tree spot) puts the climber round the trunk's centre at its own height.
    Local data only; no network change.
- **3. Still camera for the joint look: `-watchfrom H,D` (85a1962, debug-only).** Used with `-watch`, the camera
  stands D m to the side of the animal's heading and H m above the ground there. It turns to keep the animal in view
  (for a buried worm, the ground over it), stays put across the shot list's lines, and moves only when the animal
  gets more than 2D away. For the next slot: `-place Glacier -spawn SnowWorm:1 -watch SnowWorm -watchfrom 10,20` on
  the first line, then `-watch SnowWorm -watchfrom 10,20 -wait 1` per shot. Pass = the furrow and mound show from
  above, and the crater stays where the worm broke out.
- Code-checked only. The next slot compiles wip/worm-look; it can merge after that.
- NEXT: idle until the slot (after the world lane and the profiler's item 14), or your go on the trunk-lean fix.

## 2026-10-30 06:09: Unity slot (05:38-06:08): worms and trunk-hug checked in builds; Unity closed
Built through the runner's `build()` on the main checkout, then put it back (b/weather-mapjob, clean). Test commit
`t/animal-slot` (local only) = wip/trunk-hug + the world lane's worm shader 89ae558, cherry-picked. Shots and contact
sheets: `Punchy-wt/Tools/bench/results/animal-slot/<run>/` (worm, climb-host/-client, climb-*-1 = the first build, npc).
1. **Compile: PASS.** 5192485, then eeb373e: 0 errors, no warnings in the files I touched. Includes 149e4b0.
2. **Snow worm: PASS.** `-place Glacier -spawn SnowWorm:2 -watch SnowWorm`, 121 shots, 0 exceptions, no NaN-bounds
   lines. Two breaches about 16 s apart: it rises about 8 m in an arc and dives back. Snow puffs and a small crater
   show where it breaks out (worm-026).
3. **Joint look (for the world lane), partial:**
   - The breach crater shows at the break-out point (worm-026).
   - The big round holes in worm-009/013/015/060/090 are probably the Glacier: Grotto cave's entrance, not worm
     marks; the run started at that cave.
   - Not captured: the mound and furrow under the snow. `-watch` follows the buried worm, so the camera keeps going
     under the ground (worm-005/024/029).
   - Two oddities: a hard-edged dark band on the worm's body in mid-arc (worm-027/028), and the body's segments
     looking apart under the ground (worm-029, seen only from below).
   - A proper look needs a fixed camera over flat snow away from caves.
4. **wip/trunk-hug: PASS on contact, one test-hook bug found and fixed.**
   - **Hook bug, first build:** the test player stayed at the top of the trunk on its far side all run. The hook
     steered at the trunk spot it logged, which is a few tenths of a metre off the collider. With the feet on the
     bark, that was enough sideways push to walk it round the trunk, and from there heading home climbed. Fixed in
     80f2c22 (debug-only): up a trunk it pushes straight at, or straight away from, the trunk it holds.
   - **Second build:** Net status alternates between the top and the foot every 10 s, so it climbs and comes down
     again. The client's shots (climb-00/09/36) show the player Monkey with its paws and feet on the bark, where
     the gate had a half-metre gap.
   - Still open: it climbs on the far side of the trunk from where it starts (x ≈ -268 against a logged trunk at
     -267.2). That fits a trunk collider offset from the logged spot, but I haven't confirmed it; a log line of
     TrunkBase would. Whether it pops at the foot can't be judged from 2 s shots; that needs a short burst at about
     0.2 s.
   - **NPC climbers:** solo `-spawn Monkey:4 -watch Monkey` caught one going up (npc-53..56) and one coming down
     (npc-23..26). Feet at the trunk's edge, no visible gap, although depth is ambiguous from that angle.
- wip/trunk-hug is 6d231af, f6fb2dd, 80f2c22. Ready to merge into animal-behaviours on your go; the far-side question
  and the foot-pop burst can follow.
- **Unity closed:** no Unity, Punchy or bee_backend processes, and the bench lock is clear.
- NEXT: idle.

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
