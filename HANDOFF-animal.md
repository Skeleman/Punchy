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
- **Z-07:** the elf (Home) and dormouse (Climb) spins are fixed (97 spin lines to 14 at the same spots). Left: Play-mode
  circling (ducks, prairie dogs, rabbits, small birds, target 8-20 m, under 0.3 m/s, 270 deg in 1.2-2.9 s). Likely
  the chase itself; look at Play stage 1 steering if Zach still sees it.
- **Z-20:** cat and dog carries look right (arms open under the animal, front-on and side-on). Open: a large animal
  (MudPig and Deer ran or weren't picked up; find one that can be), and a remote carrier from a client (the client's
  `-watch Human` followed a villager: use `-body Dog` on the client and stand it next to the host).
- **Z-22:** woodcutter, smith and miner close up (`-watch Human:Chop -watchfrom 0.5,2.5` or `-camlook`).
  DwarfMiningCamp isn't a `-place` name (it stayed at DwarfHold).
- **Z-28:** breach arcs side-on look clean. Open: a jump over a rock or ledge and a route past boulders from above
  (Glacier Grotto and Dunes CaveEntrance had none in view). One side-on series showed the body lying in a flat curl
  on the snow after landing (shots side-18..20): check it.
- **P3, needing a design note first:** Z-50 gallop, Z-51 several voices, Z-52 NPC species reactions (ownKind is
  started in Z-25), Z-53 chases.

## Slot list (next)
1. Build animal-behaviours + world-features + wip/poses (the merged branches compiled together in slot 5 as
   tmp a38b87b).
2. Z-22 close-ups and the remaining Z-20 and Z-28 shots above; merge wip/poses if Z-22 passes.
3. Ask the profiler for the worm marks' cost with 32 slots (BENCH item 17 re-run).

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
