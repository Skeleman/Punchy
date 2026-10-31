# Handoff: animal lane (laptop, session zache-db)

Refreshed 2026-10-30 night (after animal slot 5). The supervisor is kind-possum, the "Unity build analytics supervisor setup" session.
Worktree `Projects/Punchy-animal`, branch `animal-behaviours` (4f2f0a4, pushed). Reports go newest-first in
REPORTS-animal.md, with one message to the supervisor per finished batch. Never open Unity without the supervisor's
slot.

## Branches (all pushed)
- **animal-behaviours 4f2f0a4:** d0388e2 plus spin-check, z07-spin, carry, worm-trail (feeder) and wake-trail (feeder),
  merged in slot 5.
- **world-features 247c690:** 0c637a9 plus worm-trail-shader, wake-trail-shader and worm-arc (Z-28 with
  world-laptop/blocked-top, which compiles clean). wip/gfx-creatures was already in (0c637a9).
- **Worm marks pair, merged:** wip/worm-trail 61ad6b6 / wip/worm-trail-shader 407f998: 32 slots each side. The feeder's
  Range reads the `_PunchyBurrowRange` global (GraphicsSettings publishes DetailRange(BurrowRange); 150 m before it's
  set), so animal-behaviours doesn't need GraphicsSettings to compile; the shader gates on `_PunchyBurrowRange`. Cost
  with 32 slots not re-measured yet (profiler).
- **wip/z07-spin d337490 (merged):** no turning within 10 cm of the target, and Home mode isn't leash-clamped.
- **wip/poses 2755204 (not merged):** compiles; Z-21 sprint looked fine side-on (carrying a dog); Z-22 shots too far
  and occluded to judge.
- **wip/brain-sample da5da2c:** profiler item 13 work. Not for merge.

## Open rows
- **Z-07 (slot 6):** Play spins traced to the bow and the blocked side-step coin toss: wip/z07-play, unbuilt.
- **Z-07 (earlier):** the elf (Home) and dormouse (Climb) spins are fixed (97 spin lines to 14 at the same spots). Left: Play-mode
  circling (ducks, prairie dogs, rabbits, small birds, target 8-20 m, under 0.3 m/s, 270 deg in 1.2-2.9 s). Likely
  the chase itself; look at Play stage 1 steering if Zach still sees it.
- **Z-20:** cat and dog carries look right (arms open under the animal, front-on and side-on). Open: a large animal
  (MudPig and Deer ran or weren't picked up; find one that can be), and a remote carrier from a client (the client's
  `-watch Human` followed a villager: use `-body Dog` on the client and stand it next to the host).
- **Z-22:** woodcutter, smith and miner close up (`-watch Human:Chop -watchfrom 0.5,2.5` or `-camlook`).
  DwarfMiningCamp isn't a `-place` name (it stayed at DwarfHold).
- **Z-28 (slot 6):** flat curl = turning in the air; fix wip/z28-air.
- **Z-28 (earlier):** breach arcs side-on look clean. Open: a jump over a rock or ledge and a route past boulders from above
  (Glacier Grotto and Dunes CaveEntrance had none in view). One side-on series showed the body lying in a flat curl
  on the snow after landing (shots side-18..20): check it.
- **P3, needing a design note first:** Z-50 gallop, Z-51 several voices, Z-52 NPC species reactions (ownKind is
  started in Z-25), Z-53 chases.

## Slot list (next)
0. Check pass b's output (scratchpad slot6b.out; results/animal-slot/s6-workb, s6-host/s6-client, s6-wormb), which
   was still running at the slot-6 handback, before anything else.
1. Judge Z-22 from s6-workb (chop, smith, dwarf smith at 1.7,3). If it passes, merge wip/poses into animal-behaviours.
2. Build tmp f0aacd5 (tmp/animal-slot6 + wip/z28-air + wip/z07-play): -spincheck shot list spin6 (PrairieDogTown,
   Lake, OakGrove), then the worm6b list. Merge z28-air and z07-play if the coil and the Play spins are gone.
3. Z-20 remote carrier: the host carries a Dog; the client joins 45 s later as -body Cat (-camlook or -watch Dog).
4. Miners: no mining camp is placed on this seed, so Z-22 miners need another seed or a hand-placed camp.
## Key files
- **Worms:** Fauna/CreatureBrainSystem.cs `Tunnel()` (arc depth `deep`, `Obstructed`, the leap check);
  CreatureBurrowSystem.cs; MarkMask.cs; Shaders/TerrainSurface.shader `BurrowMarks`.
- **Wakes:** CreatureWakeSystem.cs; Water.shader `SwimmerWakes`.
- **Poses:** CreatureAnimator.cs: `Evaluate` (part chain and serpent wave at about line 290), `ActionPose` (emotes,
  Carry), `TaskPose`/`SwingBody`, the pitch and height posture block at about line 110.
- **Player:** Player/PlayerMovementSystem.cs (`Floor`, `SwimOrFlop`, `Fly`, `ClimbTrunk`, `TrunkCentre`);
  Fauna/PlayerAvatarSystem.cs `Pose()` (sit, lie, bow); FaunaDirector.cs builds PlayerBody (Size, JumpFactor).
- **Audio:** Audio/SoundSynth.cs (append new SoundIds only), CreatureAudioSystem.cs (calls, Countryside, frog
  chorus), GameAudio.cs (shared; one low-pass per voice).
- **Talk:** FolkTalkSystem.cs, StreamingAssets/dialogue.json (Ghost culture).
- **Debug:** Debugging/DebugControls.cs (flags: -watch, -watchfrom, -spawn, -place tree:/meadow, -emote, -spincheck,
  -footdrift).

## Gotchas
- Line endings: LF for DebugControls, CreatureBrainSystem, CreatureBurrowSystem, CreatureWakeSystem,
  CreatureBreathSystem, HeldPoseSystem, MarkMask and the reports; CRLF for CreatureAnimator, CreatureData, TraceSystem,
  SoundSynth, GameAudio, CreatureAudioSystem, PlayerMovementSystem, PlayerAvatarSystem, AmbienceSystem and both
  shaders. Edit with Python byte-level replacements, not sed.
- `-watch Human` picks your own body: play as `-body Dog`. A lone palm: `-place Dunes -place tree:Palm`.
- The default camera sits behind the player. For side shots, use `-goto x,z` plus `-camlook` (an open meadow is about
  x -255, z 27, ground 35.4).
- HLSL `&&` doesn't short-circuit. Unity fixes a global array's length the first time it's set in a session (an
  editor keeps the old size until restart).
- CreatureState fields are local (ghosts copy fields one by one), so adding one isn't a network change. SoundIds are
  stored by number: append only.
- Worm arcs start deep, so obstacles under about 20 m ahead are crossed underground.
- Keep shell calls batched, and send only one message per batch.
