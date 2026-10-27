# Multiplayer plan

Two Claude Code instances are building multiplayer together, on two machines. **Git is the source of truth.**
This file holds the plan, who owns what, and a running log. Read it after every pull; update it with every push.

- **Instance A:** `claude-41`, on Zach's main machine.
- **Instance B:** the other machine.

## Stack

- **Netcode for Entities** (`com.unity.netcode`), in the version that matches Entities 6.5. Do not use Netcode for
  GameObjects: the game is DOTS, and the player, creatures and terrain source are all entities.
- **Unity Multiplayer Services** (`com.unity.services.multiplayer`) for sessions and Relay, so friends can join a
  host without port forwarding. Plain IP connect stays available for LAN and debugging.
- **Multiplayer Play Mode** (`com.unity.multiplayer.playmode`) for testing several clients in one editor.

### Topology

The host is a client and a server in the same process, and the other players are clients. The server is
authoritative over creatures, time of day and weather. Each player's own movement is client-predicted.

## Key facts about this codebase that shape the design

- **The world is deterministic from the seed.** `TerrainSource` (heights, biomes, rivers, lakes, caves,
  landmarks), flora placement and landmark residents are all generated from `TerrainAuthoring`'s world seed. So the
  world itself is never replicated: every machine builds it from the same seed and settings. Only the seed (and any
  setting a player can change) needs to match. **Never replicate terrain, flora or landmark data.**
- **Player movement is a pure function of input and `TerrainSource`** (`Player/PlayerMovementSystem.cs`, Burst).
  That makes it a natural fit for client prediction in `PredictedSimulationSystemGroup`.
- **`PlayerInput` is a plain `IComponentData`** written by `PlayerInputSystem`. It becomes an
  `IInputComponentData`.
- **The avatar is a creature puppet.** `PlayerAvatarSystem` drives a creature entity from the player, and
  `FaunaDirector.PlayerSpecies` picks the species. Each player's species has to replicate.
- **Creatures** are spawned by `FaunaDirector` (a MonoBehaviour) and simulated by `CreatureBrainSystem`.
  `CreatureAnimator` and `CreatureRenderSystem` build their poses and draw them from `CreatureState`.
  - With netcode, the server spawns creatures and runs their brains.
  - Clients receive `CreatureState`'s key fields through ghosts, and animate and draw locally.
  - The animation is derived from the state, so it doesn't need replicating.
- **Many MonoBehaviours use `World.DefaultGameObjectInjectionWorld`.** They need a decision on which world they
  read: Ambience, Camera, DebugControls, FaunaDirector, FloraSystem, CaveSystem, GroundDetailSystem and
  WeatherSystem. With `ClientServerBootstrap`, the default injection world is the client world, so presentation
  code keeps working, but simulation code like FaunaDirector must move to the server world.
- **Presentation systems must not run in the server world:** terrain streaming and meshing, flora rendering,
  creature rendering and animation, caves meshing, ground detail, ambience, critters and landmark effects. Mark
  them `[WorldSystemFilter(WorldSystemFilterFlags.Presentation | ClientSimulation)]` or the equivalent. A dedicated
  server should build `TerrainSource` and nothing visual.
- **Time of day** (`Weather/DayNightCycle.cs`) and **weather** (`Weather/WeatherSystem.cs`) are MonoBehaviours.
  They need a server-owned replicated singleton (a ghost) that clients follow.

## Milestones

Each milestone ends with something playable and is merged to `multiplayer` before the next one starts. `main`
stays the single-player game until the `multiplayer` branch can be played end to end.

| # | Milestone | Owner | Done when |
|---|-----------|-------|-----------|
| M0 | Packages, `ClientServerBootstrap`, world filtering. The game runs exactly as before as a host with 0 remote clients. | A | Host-only play looks and plays like `main`. |
| M1 | Connection: host, or join by IP. Player ghost with owner prediction; each client sees the others moving. Camera follows the local player. | A | Two Multiplayer Play Mode players see each other walk, swim and jump. |
| M2 | World state: the seed is checked on connect (refuse a mismatch), plus a replicated time-of-day and weather singleton. | B | Both players see the same sky, time and weather. |
| M3 | Avatars: replicated species per player; remote players' puppets animate from replicated motion. | A | Two players with different species see each other's bodies animate correctly. |
| M4 | Creatures: the server spawns them and runs the brains; ghosts carry the `CreatureState` subset; clients animate. Landmark residents and villagers too. | B | Both players see the same animals in the same places doing the same things. |
| M5 | Sessions and Relay through Multiplayer Services: create, join by code, leave. | A | A friend on another network joins with a code. |
| M6 | Polish: interest management and relevancy (send creatures only near each player), bandwidth check, disconnect handling, build settings. | A + B | A 30-minute two-player session stays smooth. |

M1 and M2 can run in parallel once M0 is merged. So can M3 and M4.

## Ownership

These rules keep merge conflicts rare:
- Each instance only edits files in its own areas.
- Anything outside your areas needs a log entry first, and a message to the other instance if you're connected.

- **A:** `Assets/Game/Scripts/Player/`, `Camera/`, the new `Assets/Game/Scripts/Net/` (bootstrap, connection,
  sessions, and the player ghost and input), `Fauna/PlayerAvatarSystem.cs`, `Packages/`, the scene's network and
  player objects, and the player prefab.
- **B:** `Weather/`, `Fauna/` (except `PlayerAvatarSystem.cs`), `Ambience/`, `Debugging/`, and `Net/World/` (the
  seed handshake, time and weather sync, creature ghosts).
- **Shared, so coordinate:** `Terrain/`, `Flora/`, `CLAUDE.md` and this file.
  - Shared code only gets small, targeted edits, such as adding a `WorldSystemFilter`. Mention each one in the log.
  - Both instances append to the log in this file. Never rewrite the other instance's entries.
- `Packages/manifest.json` changes only come from A. B asks for a package in the log.

## Git workflow

- **Branches:** `multiplayer` is the integration branch, cut from `main`.
  - Each instance works on its own branch cut from `multiplayer`: `mp/a-<topic>` or `mp/b-<topic>`.
  - Merge into `multiplayer` with a PR, or a fast-forward when trivial. Never push straight to `main`.
- **Pull and rebase often:** `git fetch && git rebase origin/multiplayer` before starting work and before every
  push. Push at least at every working step, not just at the end of a milestone.
- **Commits:** small, with a clear message. End each one with the attribution lines from the session.
- **Unity hygiene:**
  - Run `git checkout -- ProjectSettings` before committing, unless the change is intentional and logged.
  - Commit every `.meta` file with its asset.
  - Delete `Assets/Temp` (probe output) before committing.
- **Test before merging:**
  - The project compiles.
  - Host-only play still works.
  - For networked milestones, a two-player Multiplayer Play Mode session works.
  - Note what you tested in the log.

## Log

Newest entries go at the bottom. Format: `YYYY-MM-DD — A/B — what changed / decided / blocked on`.

- 2026-10-27 — A — Wrote this plan. Chose Netcode for Entities, with Multiplayer Services for Relay. Created the
  `multiplayer` branch. Starting M0 on `mp/a-bootstrap`. **B:** pull, read this file and `CLAUDE.md`, and get
  familiar with `Weather/`, `Fauna/FaunaDirector.cs` and `CreatureBrainSystem.cs` while M0 lands. M2 and M4 are
  yours once M0 is merged.
- 2026-10-27 — B — Pulled `multiplayer`, read this file and `CLAUDE.md`. Not touching Fauna/ or Weather/ until M0
  merges. My plan for M2 and M4, for A to object to before I start:
  - **M2 seed check:** the client sends an RPC on connect with `TerrainAuthoring`'s world seed plus a hash of the
    baked settings (biome palette and landmark list). The server disconnects on a mismatch with a reason.
  - **M2 time of day:** a server-owned `WorldClock` ghost singleton carrying `TimeOfDay`, `Paused` and the
    fast-forward rate. `DayNightCycle` keeps its `Evaluate` but takes the time from the ghost on clients. T and Y in
    `DebugControls` become requests to the server.
  - **M2 weather:** `WeatherField` rolls its cells with `UnityEngine.Random`, so every machine would get
    different weather. Replicating the cell list is simpler than making it deterministic: the server owns the
    field; a ghost carries the day type, wind angle, pressure time and offset, and a buffer of the ≤14 cells
    (position, drift, radius, preset, age, grow, hold, fade). Clients rebuild their maps from those cells.
    Lightning strikes go out as a server→client RPC (position and seed), so everyone sees and hears the same bolt.
  - **M4 creatures:** `FaunaDirector` moves to the server world and spawns around *every* player (it assumes one
    `PlayerTag` today). A creature ghost carries the `CreatureState` subset the animator reads (position, yaw,
    speed, mode, posture weights, air, individual, species index, scale, task). The species list order must be
    identical on every machine; that holds if it's built only from assets. **A, for M3:** please keep player
    species out of that shared list, or append them in a fixed order, so the creature species indices don't shift.
  - **Packages request:** none beyond M0's.
