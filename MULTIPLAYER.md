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
- 2026-10-27 — B — Agreed with A: a creature ghost's species is a **stable network index**, not its position in
  `FaunaDirector`'s list (`PlayerSpecies` sits at index 0 today and shifts the others). The table is the species
  from the biome Fauna and landmark Residents lists only, deduplicated and sorted by asset name (ordinal).
  GUIDs aren't available in builds. Building the table logs an error and refuses to go networked if two species
  share a name. The seed check's settings hash includes this table, so a client with different species is
  refused on connect. Player species get their own table in M3 (A).
- 2026-10-27 — A — **M0 merged to `multiplayer`.**
  - Packages: `com.unity.netcode` 6.5.0 and `com.unity.multiplayer.playmode` 2.0.2. The netcode project-settings
    assets and `VirtualProjectsConfig.json` are committed.
  - `Net/GameBootstrap.cs`: a custom `ClientServerBootstrap` that creates ServerWorld and ClientWorld, then makes
    **ClientWorld the default injection world**. The stock bootstrap would have made it ServerWorld, which breaks
    every MonoBehaviour.
  - `NetWorlds.Client` / `NetWorlds.Server` find each world explicitly. `Server` is null on a pure client.
  - Added `[WorldSystemFilter(LocalSimulation | ClientSimulation)]` to `TerrainStreamingSystem`,
    `PlayerInputSystem`, `PlayerMovementSystem`, `PlayerAvatarSystem` and **`CreatureBrainSystem` (B's file, a
    one-line edit)**. For now, creatures live in the client world exactly as before. In M4, B moves FaunaDirector
    and the brain to the server, so change that filter then.
  - The subscene (TerrainSource, player) loads into both worlds, and the server has its own idle copy of the
    player for now. Presentation-group systems only exist in the client world.
  - Tested in host play: same look, creatures spawn, movement and river current work, 161 fps. The console shows
    occasional "Server Tick Batching" warnings during editor hitches, which are harmless. Keep **Run In
    Background** on, or the server falls behind while the editor isn't focused.
  - Next for A: M1 (connection, player ghost with owner prediction, camera on the local player). **B:** M2 and M4
    are unblocked.
- 2026-10-27 — A — **M1 merged to `multiplayer`.**
  - **Decision: player movement is client-authoritative**, not server-predicted. Tree colliders (`FloraObstacle`)
    and cave walls only exist around the local player, so server-side prediction would mispredict all the time.
    For co-op exploration, trusting each client with its own position is fine. (The plan above said "predicted";
    this replaces that.)
  - **How it works:**
    - Each machine still moves its own baked `PlayerTag` player as in single player.
    - The server spawns one `NetPlayer` ghost per connection: interpolated, owned, with AutoCommandTarget. The
      prefab is `Assets/Game/Prefabs/NetPlayer.prefab`, spawned from `NetPlayerSpawner` in the World subscene.
    - The owner writes its pose into `NetPlayerCommand` (an `IInputComponentData`) in `GhostInputSystemGroup`. The
      server copies it onto the ghost's transform, and other clients interpolate it.
    - The owner hides its own ghost, and remote ghosts stay hidden until their first pose arrives. Code is in
      `Net/NetPlayer.cs`.
  - **Server-side player positions** (for B's spawning) are `NetPlayer` + `LocalTransform` in `NetWorlds.Server`.
    The server's baked `PlayerTag` entity is an idle leftover; ignore it.
  - **Connecting:**
    - Port 7979. By default a machine hosts: it listens on any IPv4 and its own client joins over loopback.
    - `-join <ip>` on the command line makes it a client only.
    - The **F2** panel (`Net/NetMenu.cs`, on the "Net" object in SampleScene) shows the LAN address and player
      count, and joins by IP. `GameBootstrap.Join` and `Host` dispose the worlds, recreate them and reload the
      scene.
    - Clients only go in game once the World subscene has loaded, because the ghost prefabs live there.
  - **Tested:**
    - The editor hosted and a Windows build joined with `-join 127.0.0.1`. Each saw the other.
    - The build hosted and the editor joined through `GameBootstrap.Join`: the scene reloaded, the editor's own
      avatar came back, and it saw the host.
    - Remote players are still the old capsule body until M3. Weather visibly differs between machines (M2).
  - **Gotchas:**
    - A MonoBehaviour authoring class must be in a file named after it, or the subscene import worker silently
      skips it ("referenced script is missing").
    - A second instance on the same PC can't host on 7979 (bind error). Use `-join`.
  - Next for A: M3 (avatars), using the separate player species table B asked for.
- 2026-10-27 — A — **M3 merged to `multiplayer`.**
  - **Playable species table:** `NetAvatars.PlayableSpecies` on the "Net" object in SampleScene, in network order
    (append only). It holds Human, Elf, Dwarf, Goblin, Ogre, HumanChild, Wolf, RedFox, Deer, BlackBear, Monkey and
    MountainGoat. A player's body travels as an index into this list. **It's separate from FaunaDirector's creature
    list**, as agreed.
  - **What replicates:** the owner sends species, Individual (look traits), coat and grounded in `NetPlayerCommand`.
    The server copies them to `NetPlayerLook` (`[GhostField]`s). Each client spawns a `RemoteAvatar` creature
    puppet per remote player, with that player's species, coat and individual.
  - **Driving and rendering:** `PlayerAvatarSystem` drives remote puppets from the interpolated ghost, the same way
    as the local avatar. The capsule only shows for a player whose body isn't in the table.
  - **Each machine keeps one look:** a random Individual saved in PlayerPrefs (`-newlook` re-rolls it). Before
    this, every player got the same look, because FaunaDirector's RNG is fixed-seed and the avatar is its first
    spawn.
  - **F2 panel:** a `Body: < Human >` picker switches your body live. FaunaDirector rebuilds, and the remote bodies
    it destroys are recreated.
  - **Test flags:** `-body <Name>` (starting body) and `-autowalk` (walk in a circle without input).
  - **B's file:** `FaunaDirector.wildQuery` now also excludes `RemoteAvatar`, a one-line change. Players' bodies
    aren't wildlife, so they're never counted or despawned.
  - **Note:** puppets read about half their true speed in `CreatureState.Speed`, local and remote alike. The brain's
    `Advance` moves puppets by their own speed before the avatar system snaps them, so the avatar system only
    measures the remainder. The leg phase still adds up correctly, so it looks right. It's pre-existing and
    consistent, so I left it alone.
  - **Tested** (editor host, Windows build joining):
    - A Wolf, a Human with a different look, and a Goblin all appeared with the right body, coat and individual.
    - Walking animates.
    - A live switch to Ogre replicated.
- 2026-10-27 — A — **M4 merged to `multiplayer`** (done by A with Zach's OK, because B's machine is much weaker).
  Animals are server-authoritative ghosts.
  - **Where animals live:** `FaunaDirector` now runs the animals in the *simulation world*: the server world when
    hosting, the local world without netcode, and none on a client that joined someone else. It spawns around
    **every** player (`NetPlayer` positions on a server) and despawns only what is far from all of them. Counts are
    kept per player. Residents appear when any player is near.
  - **Spawning:** on a server it instantiates the creature ghost (`Assets/Game/Prefabs/CreatureGhost.prefab`,
    spawned from `CreatureGhostSpawner` in the World subscene) and adds `CreatureState` and `CreatureBody`.
  - **Brain:** `CreatureBrainSystem` runs in `ServerSimulation | LocalSimulation` only, and skips
    `RemoteAvatar` as well as `PlayerAvatar`. Animals react to the **nearest** player.
  - **Replication** (`Net/World/CreatureGhosts.cs`):
    - The server's `CreatureNetWriteSystem` copies `CreatureState` → `LocalTransform` (position, heading) and
      `CreatureNet` (species, individual, coat, scale, mode, task, stage, speed, look, lie, head-down, bow, curl,
      air).
    - On clients, `FaunaDirector.GiveReplicasBodies` adds `CreatureBody`, `CreatureState` and `CreatureReplica` to
      each arriving ghost.
    - `CreatureReplicaSystem` rebuilds `CreatureState` every frame. The gait phase is advanced by the replicated
      speed, using the brain's stride maths. The animator and renderer are unchanged.
  - **Species table:** `FaunaDirector`'s species list is **sorted by asset name** (it logs an error on duplicates),
    and the player's species is no longer in it. The player's body has its own blob, so changing body never
    rebuilds or despawns the animals.
  - **Fixed an M3 bug:** remote player bodies were also advanced by the brain (double gait phase, half speed).
  - **`ProjectSettings`:** `runInBackground: 1`. Netcode needs it, or a tabbed-out player stalls and disconnects.
  - **Tested:**
    - The editor hosted: 29–78 animals were simulated on the server, and the host client received and rendered
      them all. A duck floated correctly.
    - A build hosted and the editor joined: the editor had 34 replicated animals (a snake and a squirrel looked
      right), the host's body appeared, and the editor spawned no animals of its own.
  - **Not done yet** (left for M6): relevancy (every client gets every animal the server has, at most about 300),
    and fear marks and other client-side extras only work through replicated `Mode`.
  - **For B:** M2 is still yours (seed check, WorldClock, weather). `DayNightCycle.Night` feeds `FaunaClock` in both
    worlds through FaunaDirector, so once the clock replicates, animals sleep in sync with what clients see.
- 2026-10-27 — A — **M2 merged to `multiplayer`** (done by A; B's plan followed). Everyone shares the host's sky.
  - **World state ghost:** `Net/World/WorldSync.cs` (on the "Net" object) publishes a `WorldState` ghost from the
    host: time of day, day type, wind angle, pressure time and offset, cloud drift, plus a `WeatherCellState`
    buffer of the host's 14 or fewer weather cells. The prefab is `Assets/Game/Prefabs/WorldState.prefab`, spawned
    once from `WorldStateSpawner` in the World subscene.
  - **Joined clients:** `WeatherSystem.Remote = true`. No cells spawn locally, there are no day-type changes and no
    debug weather keys, and wind doesn't veer. WorldSync copies in the host's state every frame, before
    DayNightCycle and WeatherSystem run.
  - **Lightning:** the host's strikes raise `WeatherSystem.Struck`, which goes out as a `LightningRpc` to everyone.
    Joined clients play it through `WeatherSystem.Strike`.
  - **Weather/ changes (B's area, small):**
    - `WeatherField`: `CellState`, `ExportCells`, `ImportCells`, and `PressureTime` / `PressureOffset` accessors.
    - `WeatherSystem`: `Remote`, `Struck`, `Strike`, and `DayTypeIndex` / `WindAngle` / `CloudOffset` accessors.
  - **World check:** on going in game, each client sends a `WorldCheckRpc` with a fingerprint of its world
    (seed, size, sea level, biome and landmark lists, sorted creature species, playable species). The host
    disconnects anyone whose fingerprint differs and logs both values. F2 then says "Not connected: … refused …".
    `WorldSync.TestSalt` is a test hook to force a mismatch.
  - **Tested:**
    - The editor hosted with a forced storm, and a Windows build joined. The build's HUD read "Stormy / Storm /
      09:36" against the host's "Stormy / 09:35", and rain fell on both (window screenshot).
    - A salted client was refused with the log line.
  - **Note:** the time-of-day field isn't interpolated (it wraps at 24 h), so it steps at the snapshot rate.
    That's invisible.
- 2026-10-27 — A — **M5 merged to `multiplayer`.** Online play through Unity Relay works; the project's Unity Cloud
  link was already set up and Relay is enabled.
  - **Package:** `com.unity.services.multiplayer` 2.4.1, which brings in Relay, Authentication and Core.
  - **Code:** `Net/RelaySessions.cs` signs in anonymously, creates or joins Relay allocations (up to 8 players) and
    keeps the join code and a status line. `GameBootstrap.HostRelay` gives the server two drivers: an in-process
    IPC driver for the host's own client and a Relay UDP driver for everyone else. The host's client gets an
    explicit IPC driver. `GameBootstrap.JoinRelay` gives a client a Relay UDP driver.
  - **F2 panel:**
    - "Host online (get a join code)" shows the code with a Copy button.
    - "Code: [____] Join online" joins a game by its code.
    - Errors (unknown code, Relay not enabled, offline) appear in plain words.
  - **Launch flags:** `-hostonline` and `-joincode <CODE>`.
  - **Tested:** the editor hosted online (code JPJFPB) and a Windows build joined with `-joincode` as an autowalking
    Dwarf. The host saw it move with the right body.
  - **Known:** Transport warns that a Relay driver should bind to `0.0.0.0:0`. We listen on port 7979 so the IPC
    link has a fixed port. It works; tidy this up in M6.
