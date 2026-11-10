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
- Upright runners hold the elbow at the HumanArm run keys (85-95°): the per-folk arm swing and the sprint pump scale
  the shoulder only (A-41; scaled, the elbow folded shut at a sprint). Hip extension at a run is the RunPush key.
- Body sockets are in body units: x and y across the radius, z from −1 (rump) to 1 (chest). Child sockets are
  metres from the **end** of the parent's last segment, in that segment's frame.

## Gotchas

- **Lying down** (A-92, #231): a four-legged body's legs fold ahead of it coming down (fold `1-(1-Lie)^2`), and the
  hip swing that lifts them clear (A-42) runs from Lie 0.01 at full weight, refined to the least clearing angle; foot
  IK and sink fade with the fold. The player's body and AI hold still until Lie/Sit is under 0.25 (stand first).
  `LyingTests` measures every species' leg clearance at Lie 0.3-1, flat and on a 1-in-3 slope.
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
- **Mantrap** (`Mantrap.cs`, #41): a rooted carnivorous plant, not an ECS creature: a MonoBehaviour that builds procedural plants from a seeded survey (FoggyMarsh, PoisonSwamp, FungalForest; clear of settlements and landmarks), so every peer gets the same ones, and simulates the head locally from the local player and `FaunaDirector.SimWorld` animals. A snap shoves the player or sets an animal fleeing (no damage system). Flags: `-nomantrap`, `-mantraptest`, `-mantrapshot side|lunge`.
- **Personality** (`CreaturePersonality`, #50): values worked out from the replicated `CreatureState.Individual` (trait slots 60+), never stored or sent, so host and client agree. Only Aggression (species range `Aggression`) drives behaviour: with `HuntAggression` > 0 only individuals at or above it hunt, and prey ignores the rest (dogs: 0.6, hunt hares). `-personalitylog <Species>`.
- **Weight** (#16): each species has `Weight` in kg at mid Size (scales with size cubed; variants with 0 use base x VariantScale^3). A carrier lifts up to `Interactions.CarryCapacity` (40 kg, stand-in for a Strength stat), so children can be carried; heavier loads squirm free sooner.
- **Match species by name, not by object** (A-20, #7): in a built player a Resources species (the Domestics Dog) points at Resources copies of its prey, which are different objects from the palette's. `FaunaDirector.Lists` compares names; a reference `Contains` left every built dog with no prey.
- **Folk talks** (A-26): two townsfolk stop, face each other and take turns talking (Idle stages `ChatTalk` 70 / `ChatListen` 71, so clients see gestures and the bubble from the Stage byte; Partner and `ChatRole` are host-only, local `CreatureState` fields). Per-folk cooldown 90-200 s, at most 3 talks within 60 m of a home. `FolkChat:` log line per talk (host/solo). `-chattest` forces them (15-30 s cooldown); `-chatshot` on a shot line frames the nearest talking pair 4 m side-on. A fully synced version would need the partner sent.
- Cave collision (A-30): PlayerMovementSystem.PushOutOfSolids pushes knee/middle/head spheres (radius 0.35 x body size)
  out of SolidDistance and slides bodies back off cave floors steeper than ~50°. A landmark with a solid body joins
  by adding its distance to SolidDistance with math.min. CaveDistance can overstate up to 1.8x, so trace at half steps.
- A-32: the head-undo in PushOutOfSolids only counts rock overhead (surface normal y < -0.3); counting any rock
  wedged small bodies on steep floors. `-nohud` hides talk bubbles unless `-chatshot` sets FolkTalkLabels.BubblesWithoutHud.
- A-90: creature breath is shaped by body size alone (CreatureBreathSystem.ShapeFor, StandHeight + BodyLength against
  a person's 1.18 m); a player's body keeps the old breath. `-breathlog` logs the per-species table and live puff count,
  `-breathsize old` brings back the one-size breath to compare. Shot-list lines reset flags, so repeat them per line.
- A-94: past 4x a person ShapeFor keeps growing breath (one curve, up to 8x more: bigger, slower, longer-lived, seen
  further). Colossi breathe through StoneColossi.ColdBreath (size measured off their meshes) and AmbienceSystem's puffs,
  so only in the cold and near the camera. Fliers breathe each exhale out in one burst (ExhaleFor), so no dotted trail.
