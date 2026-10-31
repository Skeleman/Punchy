# Handoff: animal lane (laptop, session zache-db)

Refreshed 2026-10-31 (after animal slot 7). The supervisor is kind-possum, the "Unity build analytics supervisor setup" session.
Worktree `Projects/Punchy-animal`, branch `animal-behaviours` (77c03e9, pushed). Reports go newest-first in
REPORTS-animal.md, with one message to the supervisor per finished batch. Never open Unity without the supervisor's
slot.

## Branches (all pushed)
- **animal-behaviours 77c03e9 (slot 7):** 9b7459b plus wip/z07-play2 (960df87: Play spins 29 to 6) and wip/trace-seat
  (6283358: no paw prints on bench seats). Merged with world-features b870008 it is the a66c520 tree (built clean).
- **animal-behaviours 9b7459b:** 4f2f0a4 plus wip/z28-air (f116dd4) and wip/poses (9b7459b), merged in slot 6.
- **animal-behaviours 4f2f0a4 (slot 5):** d0388e2 plus spin-check, z07-spin, carry, worm-trail (feeder) and wake-trail (feeder),
  merged in slot 5.
- **world-features 247c690:** 0c637a9 plus worm-trail-shader, wake-trail-shader and worm-arc (Z-28 with
  world-laptop/blocked-top, which compiles clean). wip/gfx-creatures was already in (0c637a9).
- **Worm marks pair, merged:** wip/worm-trail 61ad6b6 / wip/worm-trail-shader 407f998: 32 slots each side. The feeder's
  Range reads the `_PunchyBurrowRange` global (GraphicsSettings publishes DetailRange(BurrowRange); 150 m before it's
  set), so animal-behaviours doesn't need GraphicsSettings to compile; the shader gates on `_PunchyBurrowRange`. Cost
  with 32 slots not re-measured yet (profiler).
- **wip/z07-spin d337490 (merged):** no turning within 10 cm of the target, and Home mode isn't leash-clamped.
- **wip/poses 2755204 (merged slot 6):** Z-22 chop, smith and dwarf smith close-ups pass.
- **wip/z28-air ba7ff69 (merged slot 6):** worms fly straight; no flat coil.
- **wip/z07-play 1257001:** superseded; its bow hold is in wip/z07-play2.
- **wip/brain-sample da5da2c:** profiler item 13 work. Not for merge.

## Open rows
- **Z-07:** Play circling fixed (slot 7: the outward-cancel lerp and the blocked coin toss). Left: Songbird and
  Dormouse Wander spins (focus ~900 m) and Dormouse Climb at 0.15 m; low priority.
- **Z-20:** cat and dog carries look right (arms open under the animal, front-on and side-on). Open: a large animal
  (MudPig and Deer ran or weren't picked up; find one that can be), and a remote carrier: carrying a client's player body isn't a feature (`-interact` skips player
  bodies; slot 7). Needs a design call, or test a client carrying an animal while the host watches.
- **Z-22:** woodcutter and smiths pass (slot 6). Miner close up still open (`-watch Human:Chop -watchfrom 0.5,2.5` or `-camlook`).
  DwarfMiningCamp isn't a `-place` name (it stayed at DwarfHold).
- **Z-28 (slot 6):** flat curl fixed (wip/z28-air merged).
- **Z-28 (earlier):** breach arcs side-on look clean. Open: a jump over a rock or ledge and a route past boulders from above
  (Glacier Grotto and Dunes CaveEntrance had none in view). One side-on series showed the body lying in a flat curl
  on the snow after landing (shots side-18..20): check it.
- **P3, needing a design note first:** Z-50 gallop, Z-51 several voices, Z-52 NPC species reactions (ownKind is
  started in Z-25), Z-53 chases.

- **Bench paw prints:** fixed and merged (slot 7).

## Slot list (next)
1. Z-20 remote carrier: a client (`-join`, Human body) with `-interact Cat:carry` near a `-spawn Cat:2` host; host
   shots of the client carrying. Large animal: find a pettable one of 0.5 m or less that holds still.
2. Miners: no mining camp on this seed, so Z-22 miners need another seed or a hand-placed camp.
3. Z-07 leftovers (Songbird/Dormouse Wander) if Zach still sees them.
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
- In shot lists, `-camlook` on a line with `-spawn` didn't take; frame from the player (stand it where you want to look).
- Keep shell calls batched, and send only one message per batch.
