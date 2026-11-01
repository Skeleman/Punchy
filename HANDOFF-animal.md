# Handoff: animal lane (laptop, session zache-db)

Refreshed 2026-10-31 (after animal slot 9). The supervisor is kind-possum, the "Unity build analytics supervisor setup" session.
Worktree `Projects/Punchy-animal`, branch `animal-behaviours` (c810406, pushed). Reports go newest-first in
REPORTS-animal.md, with one message to the supervisor per finished batch. Never open Unity without the supervisor's
slot.

## Branches (all pushed)
- **animal-behaviours c810406 (slot 9):** c0ac39c plus wip/net-interact (6d6d569: interactions go to the server as
  `InteractRpc`, not on NetPlayerCommand). Built clean as a4a5378 (with world-features b39522b; tmp branch
  tmp/animal-slot9-net, local only).
- **animal-behaviours c0ac39c (slot 8):** 77c03e9 plus wip/remote-carry (20bb420: the host logs each joined player's
  interaction request; the client's `-interact` log adds ghost id and distance). It built clean in 4c2f633 (with
  world-features b39522b and scene seed 3).
- **Local only, not pushed:** 866dc99 in Punchy-animal (tmp/animal-slot8-seed3) is the merged tree with World.Seed 3
  in World.unity, for building a human mining camp.
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
- **Z-20:** cat and dog carries look right in solo. Large animal: DesertTortoise (0.35 m, flee 0) is picked up, but
  being wild it squirms free in 2-5 s. Dogs are mostly over the 0.65 m limit.
  **Remote carrier FIXED (slot 9):** requests went on NetPlayerCommand, which the server samples only at its own
  tick. A slow host (5-9 fps, shared clock behind real time) saw them seconds late. They now go as `InteractRpc`
  (Pet, PickUp, PutDown, Startle). Host shot s9b-Cat h-046 shows the joined player carrying a cat. v0.10.0 has the
  old path.
- **Z-22:** woodcutter, smiths (slot 6) and miners (slot 8, seed 3 build) all pass by sight. Our seed (1337) has no
  mining camp. Seed 3 has a human camp at (209,-3878) with its mine at (316,-3955): use
  `-goto 209,-3878 -time 10`, then `-watch Human:Mine` per shot-list line. Miners reach the face (stage 101)
  about 2 minutes in.
- **Z-28 (slot 6):** flat curl fixed (wip/z28-air merged).
- **Z-28 (earlier):** breach arcs side-on look clean. Open: a jump over a rock or ledge and a route past boulders from above
  (Glacier Grotto and Dunes CaveEntrance had none in view). One side-on series showed the body lying in a flat curl
  on the snow after landing (shots side-18..20): check it.
- **P3, needing a design note first:** Z-50 gallop, Z-51 several voices, Z-52 NPC species reactions (ownKind is
  started in Z-25), Z-53 chases.

- **Bench paw prints:** fixed and merged (slot 7).

## Slot list (next)
1. Z-07 leftovers (Songbird/Dormouse Wander) if Zach still sees them.
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
- MP test: host `-host -body Human -shotlist` (`-spawn Cat:2 -wait 25`, then `-watch Cat -wait 0.5` lines), client
  `-join 127.0.0.1 -body Human -interact Cat:carry`. The host must pass `-host`, or both run solo. Spawned cats are
  wild and squirm free in 2-5 s, so use 0.5 s host shots.
- A one-shot client-to-server action belongs in an RPC (or an InputEvent), not a plain IInputComponentData field:
  the server reads input only at its own tick.
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
- Burst's Debug.Log formats only G/D/X (no `F1`). On the host, `-watch Human` doesn't find a remote player's body.
- Keep shell calls batched, and send only one message per batch.
