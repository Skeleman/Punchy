# Handoff: animal lane (laptop, session zache-db)

Refreshed 2026-10-30 evening. The supervisor is kind-possum, the "Unity build analytics supervisor setup" session.
Worktree `Projects/Punchy-animal`, branch `animal-behaviours` (d0388e2, pushed). Reports go newest-first in
REPORTS-animal.md, with one message to the supervisor per finished batch. Never open Unity without the supervisor's
slot.

## Branches (all pushed)
- **animal-behaviours d0388e2:** includes v010-fixes (Z-02 player side, Z-03, Z-04, Z-05, Z-06, Z-08, Z-14, Z-23..Z-26),
  trunk-lean, worm-look and trunk-hug. Compiled and partly checked in slot 3.
- **wip/gfx-creatures 6f6dd94:** creature render ranges × GraphicsSettings.DetailRange. Compiled. It sits on the world
  gfx branch, so the world lane merges it into world-features.
- **Feeder/shader pairs.** Each pair must land together; the new feeder against the old shader misreads the slots.
  - Worms: wip/worm-trail bd5e706 (CreatureBurrowSystem: bumps, craters, MarkMask) with wip/worm-trail-shader 5198212
    (TerrainSurface BurrowMarks, 64 slots, NearBurrowMark).
    - First cost check was +0.5 ms against a 0.3 bar; the mask was added afterwards. The re-run is profiler BENCH
      item 17.
    - Still to wire: the feeder's Range should read GraphicsSettings.DetailRange(BurrowRange), and the world lane's
      shader gate `_PunchyBurrowRange`.
  - Wakes: wip/wake-trail 138874c (CreatureWakeSystem ripples, MarkMask) with wip/wake-trail-shader 4f597bf
    (Water.shader SwimmerWakes rings, 48 slots, 45 m gate, NearRipple). Seen rendering in slot 3; cost also in item 17.
- **wip/worm-arc 9213d8e:** Z-28 on top of worm-trail, plus a merge of world-laptop/blocked-top (it now carries
  world-features history: merge it into world-features, not animal-behaviours). Uncompiled, including zache-73's
  Blocked(..., out top) and RockAt. Tell zache-73 whether it builds clean.
- **wip/spin-check 7d03e22:** `-spincheck [deg,s]`, default 270° in 3 s, logs spinning creatures; -watch line shows yaw
  and turn rate. Uncompiled.
- **wip/poses 2755204:** Z-21 (sprint foot keeps run angles) and Z-22 (SwingLift/SwingBody: wind-up, torso into the
  swing). Uncompiled.
- **wip/carry 95d50e8:** Z-20. CreatureState.CarryWidth is local, set in HeldPoseSystem; the Carry arms open by it;
  the animal is placed between the posed hand tips. Uncompiled.
- **wip/brain-sample da5da2c:** profiler item 13 work, up to date with an older animal-behaviours. Not for merge.

## Open rows
- **Z-07** (spinning: a mouse near Start, an elf at the ElfGrove fountain, 2-3 s each): run `-spincheck` for a few
  minutes at Start (as a Dog body) and at `-place ElfGrove` at a few hours. Fix from the logged mode, stage, target and
  focus.
- **Z-20, Z-21, Z-22, Z-28:** compile and shoot (see the slot list).
- **P3, needing a design note first:** Z-50 gallop, Z-51 several voices, Z-52 NPC species reactions (ownKind is
  started in Z-25), Z-53 chases.

## Slot list (next)
1. Build one test branch: world-features + animal-behaviours + spin-check + poses + carry + worm-arc. Build with the
   scratchpad build.sh/run.sh (lib.sh `build <commit> Build <out>` under results/.lock; run from Builds/Bench with
   -shotlist and a timeout).
2. Z-07: `-spincheck` at Start and at ElfGrove.
3. Z-21: a human sprinting, side-on. Z-22: a woodcutter, smith and miner (`-place HumanVillage`/`DwarfHold` by day).
4. Z-20: carry a cat, a dog and a large animal, side-on and front-on, plus a remote carrier seen from a client
   (`-host` / `-join`). Check which way the shoulders open (flip `part.Side * open` if the arms close) and where the
   animal sits along the forearms.
5. Z-28: a breach side-on (`-watchfrom`), a jump over a rock or ledge, and a route past boulders from above. Tell
   zache-73 the compile result for blocked-top.
6. Merge what passes, write the report, message "Unity closed".

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
