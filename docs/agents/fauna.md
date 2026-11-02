# Fauna: creatures, the player's body, folk and workers

Read before changing creatures, behaviour, poses, the player's body, settlements' residents or workers.

- `Fauna/` — `LimbType` (segments plus seven key poses), `CreatureSpecies` (body plus slots; can be a recoloured
  variant of another species, with weighted coats). ECS entities; `CreatureBrainSystem` (behaviour) and
  `CreatureRenderSystem` (posing, one instanced draw) are Burst jobs. `FaunaDirector` spawns per biome density.
  Behaviour: each species' Activities (active by day/night, grazing, thirst, playfulness, laziness, what it hunts)
  weight what it does next: graze, drink at the water's edge, rest, sleep, play chase with its own kind, hunt;
  prey flee predators. Animals see each other through a per-frame snapshot grid. Activities set posture weights
  (lie, head down, play bow, curl) that `CreatureAnimator` applies procedurally, so no extra poses are authored.
- **The player's body is a creature.** `FaunaDirector.PlayerSpecies` (a `CreatureSpecies`, currently `Human`)
  is spawned as a creature tagged `PlayerAvatar`; `PlayerAvatarSystem` copies the player's position, heading and
  speed into it instead of a brain, so any species can be played and moves with its own gait. Setting the field
  to empty falls back to the player's placeholder mesh. Humanoid folk (Human, Dwarf, Elf, Goblin, Ogre) share the
  Human limb parts with different slot scales and thicknesses, plus their own torso or leg poses where the gait
  differs (goblin crouch, ogre sway). Settlements are landmarks, one per culture (HumanVillage, DwarfHold,
  ElfGrove, GoblinCamp, OgreCamp): mostly residents of that folk, in its own buildings (flora forms Hut,
  StoneHouse, Tower, Tent, turned to face the square). Residents get a Home and Leash so they stay in town.
  Set pieces use placed props (LandmarkProp.Count/Angle/Clearance: exactly N round a circle, everything else kept
  clear): an inn and smithy in human villages, a smithy in dwarf holds, pens and stalls in camps, a forum and
  fountain at the heart of elf groves. Residents with a Leash and HomeRing/HomeAngle live at such a spot (penned
  animals); nothing with a home heads out of its range, even fleeing.
- **Individuals:** CreatureState.Individual is fixed at spawn and drives CreatureBlob.Trait (posture lean, arm
  swing, bounce, torso twist, head tilt, pace), hair colour (segments marked Hair, from the species' HairColors)
  and style options (slots with the same StyleGroup: each individual shows one; list a slot twice to make it
  commoner). The humanoids' slot 3 is a placeholder kept so later slot indices don't shift.
- **Workers:** a LandmarkResident with a Task (Hammer, Chop, Gesture, Tend, Watch) lives at its HomeRing/HomeAngle
  spot facing FaceRing/FaceAngle, works by day in CreatureMode.Work (arm poses in CreatureAnimator.TaskPose),
  takes breaks within 7 m, and dozes at its post at night. LandmarkResident.Size overrides size (HumanChild is a
  small, playful Human variant).

## Animation conventions

- A part's segments point along their local +Z. A slot's `Direction` is a rest rotation in degrees (Unity Euler,
  ZXY): `(90, 0, 0)` points down (legs), `(0, 180, 0)` backwards (tails), `(0, 90, 0)` out to the right.
- Joint angles are relative to the previous segment. **Positive pitch swings a leg backwards and droops a tail.**
  Mirrored slots flip yaw and roll.
- A gait cycle is three keys: stance sweeps linearly reach → push (foot planted), swing eases push → reach plus
  `lift × sin`. Gaits come from slot phases (walk 0/¼/½/¾, trot = diagonal pairs, hop = pairs together).
- Stride and standing height are measured from the key poses, so feet don't slide; change poses rather than
  speeds to fix sliding.
- Body sockets are in body units: x and y across the radius, z from −1 (rump) to 1 (chest). Child sockets are
  metres from the **end** of the parent's last segment, in that segment's frame.

## Gotchas

- Two-legged species set **Upright** so the body stays level on slopes (the legs take up the ground); others tilt
  with it. No body tilts to follow the ground while airborne.
- Species have a **Habitat**: Land, Swims (fish, eels, dolphins) or Floats (ducks).
  - Swimmers keep to water at least 0.7 m deep. They swim `clamp(depth × 0.45, 0.25, SwimDepth)` under the
    surface and leap out `Leaps` times a minute. `CreatureState.Air` runs 0..1 through a leap.
  - Floaters ride on the surface.
  - The director spawns water species only at water spots, and land species only on dry ones.
  - Aquatic bodies are legless, so `LeglessStride` sets their gait. The animator treats `Position.y` as the
    body's centre, with no ground tilt.
- Swimmers in rivers drift downstream at `PlayerMotor.RiverCurrent`, doubled in rapids. The drift is capped
  below swim speed, so the player can always reach the bank.
- Creature parts are drawn solid now (the capsule mesh was once wound inside-out, which let hidden parts show
  through), so a part meant to be seen must poke out of whatever it's attached to.
- Joint axes depend on which way a part points: for a part pointing up (an upright torso, neck or head), a yaw
  leans it sideways and a roll (z) twists it about its length.
- Joint angles accumulate along chains: a snake or tail ripple needs small per-joint angles (about 10–15°).
- **Climbing** reads the stick against the camera (`PlayerMovementSystem.StickOnSurface`): forward up, back down, right along the surface; tests hold the camera with `PlayerInputSystem.TestCamera`. Ambient sounds are a per-biome set (`AmbientSounds`, keyed by biome NameHash; empty allowed), and the global call budget is 0.6/s.

- Right face button (B / Circle, the crouch action): a roll on the ground (PlayerMotion.Roll, replicated as IdleAction.Roll in the Pose byte), a slow glide-descent on the wing, a sink in the water. A fish sprint (Sprint) is 2.8x and holds depth. B still cancels in menus. `-actiontest roll|fly|fish:<Body>` logs each.
