# Multiplayer

Read before changing anything players see, input, network tables or world state.

- Built on Netcode for Entities. The plan, ownership and log are in `MULTIPLAYER.md`, shared with a second Claude
  instance; read it after every pull.
- **Playing alone there is no networking:** one local world (`GameBootstrap.Solo`), as before multiplayer.
  - Hosting or joining (F2, or the flags below) disposes it and creates the netcode worlds, ServerWorld and/or
    ClientWorld. `GameBootstrap.PlaySolo()` goes back.
  - A lost host, a join that never connects, or a failed Relay join all drop back to solo where you stand.
  - `World.DefaultGameObjectInjectionWorld` is the local world or the **client** world, so MonoBehaviours always
    see the local player's view. Server-side code finds its world through `NetWorlds.Server`, which is the local
    world when solo. Use `world.IsServer()` when it matters.
  - Code must work in all three: local, client and server.
  - A MonoBehaviour that keeps a copy of `TerrainSource` must fetch it again when the default world changes: its blobs
    are freed with the old world (an NRE in `HasBiomes` otherwise, see `DunesColossus.CurrentSource`). `PlaySolo()`
    also reloads the scene, and `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` runs only once: hook `sceneLoaded`.
- Systems that touch input, cameras, meshes or rendering need
  `[WorldSystemFilter(LocalSimulation | ClientSimulation)]` or a presentation group, so they never run on the
  server.
- **Players:**
  - Movement is client-authoritative. Each machine moves its own baked `PlayerTag` player.
  - A `NetPlayer` ghost per connection carries the pose and look (`NetPlayerCommand` → `NetPlayerLook`).
  - Other players' bodies are `RemoteAvatar` puppets spawned by `NetAvatars`. `NetAvatars.PlayableSpecies` is the
    network's body table: append only, never reorder.
- **Animals:**
  - They live in the *simulation world*: the server when hosting, the local world without netcode, none on a
    joined client. The brain only runs there.
  - Clients get creature ghosts, which `FaunaDirector` gives bodies, and `CreatureReplicaSystem` rebuilds
    `CreatureState` from them.
  - FaunaDirector's species table is sorted by asset name, so an index means the same animal everywhere.
  - Each connection only receives animals within 200 m of its player (`CreatureRelevancySystem`).
- **Sky:** the host's time and weather go out on a `WorldState` ghost. Joined clients run
  `WeatherSystem.Remote = true` and copy that state in (`WorldSync`). Clients whose world fingerprint differs
  are disconnected.
- **Connecting:**
  - Default is solo. `-host` hosts on LAN (UDP 7979) from the start, and so does Multiplayer Play Mode's Client
    play type for virtual players.
  - `-join <ip>` joins over LAN; `-hostonline` / `-joincode <CODE>` go through Unity Relay.
  - F2 opens the panel for all of these and the body picker.
  - Test flags: `-body <Name>`, `-autowalk`, `-newlook`, `-time <h>`, `-place <name>`. A timestamped "Net status"
    line is logged every 10 s.
- Netcode needs **Run In Background** (set in Player Settings, and for play mode in the editor), or a tabbed-out
  player stalls.
- MonoBehaviour authoring classes must live in a file named after the class, or subscene baking silently skips
  them.
- Reimporting an asset or recompiling in play mode rebakes the subscene and resets the game. Test with builds:
  File → Build Profiles → Windows → Build to `Builds/Windows/Punchy.exe` (or a throwaway `BuildPipeline.BuildPlayer`
  script run through `unity command script run`; none is kept in the repo), then run `Builds/Windows/Punchy.exe -join 127.0.0.1` against a `-host` build or an editor that is hosting (F2).

### Keeping new features multiplayer-ready

Multiplayer is a proof of concept for now, but every new feature should fit it. For each one, decide which kind of
state it is:
- **Derived from the seed:** terrain, biomes, flora, landmarks, critter spots. Nothing to sync, as long as it
  doesn't depend on per-machine randomness (`UnityEngine.Random`, `_Time`, frame order). For ambient motion use
  `_PunchyClock` (SharedClock.hlsl), not `_Time`.
- **Shared world state that changes:** time, weather, anything the world remembers. It belongs to the host. Send it
  on `WorldState` (small fields) or its own ghost, or as an RPC for one-off events like lightning. Joined clients
  copy it and don't simulate it.
- **Simulated things others must see the same:** animals, and in future NPCs, items or projectiles. They run only
  in the simulation world (`NetWorlds.Server`) as ghosts, with relevancy so far-away ones aren't sent.
- **The local player's own state:** pose, body, held items. It's client-authoritative, carried on the `NetPlayer`
  ghost or its command.
- **Cosmetics around a player:** breath, footprints, grass bending, lanterns. Apply them to *every* player,
  including `NetPlayer` ghosts that aren't `GhostOwnerIsLocal`, by working them out from positions each machine
  already has. Nothing should only react to `PlayerTag`.

When a feature changes a network table (ghost fields, `PlayableSpecies`, species or biome lists), all players need
the same build. The world fingerprint in `WorldSync` refuses mismatched joiners.

## Learned in October 2026 (from the lane handoffs)

- A one-shot client-to-server action (pick up, pet, put down) belongs in an RPC or an InputEvent, not a plain
  `IInputComponentData` field: the server reads input only on its own tick, so a one-frame press gets lost on a
  slow host (see `InteractRpc`).
- `CreatureState` fields are local (ghosts copy fields one by one), so adding one isn't a network change.
  `SoundId`s are stored by number: append only.
- After adding fields to an RPC, a player build can keep the old layout in its Burst code (`Library/BurstCache`
  stale): the receiver logs "RpcSystem failed to deserialize ... bits read did not match expected" and drops the
  connection (InvalidRpc). `--burst-disable-compilation` on the player confirms it; delete `Library/BurstCache` and rebuild.
