# Animal lane: key files (under Assets/Game/Scripts unless noted)

- **Brain:** Fauna/CreatureBrainSystem.cs (activities, Play, flee, hunt; worms: `Tunnel()`, arc depth `deep`,
  `Obstructed`, the leap check). CreatureData.cs (CRLF), CreatureSpecies.cs (CRLF).
- **Worms and wakes:** CreatureBurrowSystem.cs, MarkMask.cs, Shaders/TerrainSurface.shader `BurrowMarks`;
  CreatureWakeSystem.cs, Water.shader `SwimmerWakes`.
- **Poses:** CreatureAnimator.cs: `Evaluate` (part chain, serpent wave ~line 290), `ActionPose` (emotes, Carry),
  `TaskPose`/`SwingBody`, pitch and height posture block ~line 110.
- **Player:** Player/PlayerMovementSystem.cs (`Floor`, `SwimOrFlop`, `Fly`, `ClimbTrunk`, `TrunkCentre`);
  Fauna/PlayerAvatarSystem.cs `Pose()` (sit, lie, bow); FaunaDirector.cs builds PlayerBody (Size, JumpFactor).
- **Interactions over the network:** `InteractRpc` (Pet, PickUp, PutDown, Startle), handled the frame it arrives.
- **Audio:** Audio/SoundSynth.cs (append new SoundIds only), CreatureAudioSystem.cs (calls, Countryside, frog
  chorus), GameAudio.cs (shared; one low-pass per voice).
- **Talk:** FolkTalkSystem.cs, StreamingAssets/dialogue.json.
- **Debug:** Debugging/DebugControls.cs (-watch, -watchfrom, -spawn, -place tree:/meadow, -emote, -spincheck,
  -footdrift, -interact).
- **Line endings:** LF: DebugControls, CreatureBrainSystem, CreatureBurrowSystem, CreatureWakeSystem,
  CreatureBreathSystem, HeldPoseSystem, MarkMask, reports. CRLF: CreatureAnimator, CreatureData, CreatureSpecies,
  TraceSystem, SoundSynth, GameAudio, CreatureAudioSystem, PlayerMovementSystem, PlayerAvatarSystem,
  AmbienceSystem and both shaders.
