# Audit: profiler (kind-possum), first pass

Read-only, on rc/v0.9.0 4d7e986 (2026-10-29). Pattern searches plus focused reads, not a line-by-line pass over all
2.4 MB. Ranked by impact within each part. Owning lane in brackets. Part 2 (netcode, streaming and caches) is below.

## 1. BrainJob and PoseJob never overlap [animal]
- `CreatureBrainSystem.cs:107-111` snapshots every creature on the main thread, then schedules BrainJob (writes
  CreatureState).
- `CreatureRenderSystem.cs:434-436` snapshots CreatureState again with ToComponentDataArray, which completes BrainJob
  on the main thread; PoseJob is scheduled after that and completed at `:617`.
- So brain and posing run back to back with the main thread waiting on both. Both grow with creature count, so a spawn
  burst spikes both in the same frame: likely the "PoseJob and BrainJob double on spike frames" question. The sync
  point, not two separate costs. Confirm with a capture before acting on it.
- Fix: pose from last frame's state, or build the render snapshot in a job that depends on BrainJob, so posing overlaps
  the next frame's brain.

## 2. One 200-byte CreatureState; behaviour in one 5,500-line job [animal]
- `CreatureData.cs:424`: 43 fields (position, gait, sway, mode, task, held item...). Brain, posing, net write and audio
  each read or copy all of it.
- Split hot movement / behaviour (mode, task) / presentation (sway, look): smaller per-frame copies, change filtering
  becomes possible, clearer read/write sets per system.
- BrainJob switches over 33 modes in one job (300 KB file). Per-mode components or tags with smaller jobs is the usual
  ECS shape. A maintainability gain more than speed (brain ~0.8 ms on workers), but it's the riskiest file to change.

## 3. Content names hardcoded in code [animal]
- `FaunaDirector.cs:452, 543, 575, 836-839, 988, 1049, 1103`: species found by name ("Human", "Hermit", "Pigeon").
- `CreatureAudioSystem.cs:95-96`: `name == "Cat"`, `"Dog" || "MarshDog" || "Puppy"`.
- A rename or a new asset breaks these silently (a new dog breed won't bark).
- Fix: a field on the asset (e.g. a VoiceFamily) and a default-player / hermit-role setting on the director or biome.
  The rest of the content pipeline (biomes, landmarks, species, limbs baked to blobs, live reload) is sound.

## 4. Entity queries created per frame in multiplayer [world/net]
- `WorldSync.cs:190-360`: Update/LateUpdate create and dispose up to ~8 queries a frame when hosting or joined.
  `NetMenu.cs:423` (Count<T>) does the same every frame.
- Same class as the old AmbientLife.Near (now cached, `AmbientLife.cs:174`).
- Fix: create them once per world replacement, as FaunaDirector does. Small main-thread cost, MP only, which is the
  least-benched path.

## 5. Smaller
- `FaunaDirector.cs:589`: inline query never disposed. Runs on avatar change only: a small leak, not per frame. [animal]
- `TerrainStreamingSystem.cs:444`: `new[] { Entity, WaterEntity }` allocates per loaded chunk per run; `:448-452` hides
  far chunks by adding/removing DisableRendering (a structural change each time). An enableable component or switching
  MaterialMeshInfo avoids both. [world]
- 26 MonoBehaviours vs 17 ECS systems. Flora, weather, caves, ground detail and FaunaDirector do world-sized work in
  MonoBehaviours, but push the heavy parts into Burst jobs, so fine for speed. The cost: each sets up its own queries,
  and they can't use ECS job dependencies, so they Complete() by hand.

## Done well
- TerrainSource as the single source of truth for the ground.
- Terrain streaming: per-frame vertex budget, async MeshDataArray builds over several frames, entity names editor-only.
- Flora: GPU culling and indirect draws.
- Brain OnUpdate Burst-compiled; temp arrays disposed through job dependencies; spatial grid for neighbours.
- Content in data assets, with live reload.

# Part 2: netcode, streaming and cache budgets

## N1. Every ghost has the same priority and is sent every tick [world/net]
- `CreatureGhost.prefab` and `NetPlayer.prefab`: Importance 1, MaxSendRate 0 (every tick), OptimizationMode Dynamic.
  `WorldState.prefab`: Importance 100. No ClientServerTickRate is set anywhere, so the netcode defaults apply.
- Under a bandwidth limit, other players' avatars compete equally with every animal near them, and a sleeping deer
  costs a slot like a charging one.
- Fix, in order of value:
  - NetPlayer importance well above creatures (e.g. 50), so players never starve behind animals.
  - Distance importance (GhostDistanceData + GhostDistanceImportance scaling): near animals first.
  - A MaxSendRate on CreatureGhost (e.g. 20-30 Hz; it's interpolated anyway), or two prefabs: critters lower still.
- Bench before and after with `bench.sh mp` (the host's numbers with a client joined) once it has run end to end.

## N2. The relevancy pass is a server sync point and scales ghosts x players [world/net]
- `CreatureGhosts.cs:116-158`: OrderLast in SimulationSystemGroup, a main-thread `SystemAPI.Query` over every creature
  ghost for every player, every tick (Burst, so fast per item).
- It reads LocalTransform, which CreatureNetWriteSystem's job writes, so the main thread waits for BrainJob and
  WriteJob each tick. The server's version of finding 1.
- Fix: an IJobEntity writing into the relevancy set's parallel writer, scheduled after WriteJob; or only rebuild every
  few ticks (relevancy changes slowly at 200 m).
- Small: connections whose player hasn't been placed get every ghost (`:147`), so a joiner's first ticks are the most
  expensive ones.

## N3. Player position is client-authoritative [world/net]
- `NetPlayer.cs:185-200` (NetPlayerApplySystem): the server sets each NetPlayer to whatever position its owner sends,
  with no speed or teleport check. Fine for friends' co-op, but anything server-owned that trusts player positions
  (relevancy, FaunaDirector spawns around players, interactions) can be steered by one client.
- Fix if it matters: a max-speed clamp per tick (flight and glide speeds are known from PlayerBody), teleports only
  through an RPC the server checks.

## N4. CreatureNet bandwidth is reasonable
- `CreatureGhosts.cs:22-45`: about 25 fields, quantized, delta-compressed; posture floats at 1/100. Species as int and
  Holder as int could be smaller, but delta compression already makes unchanged fields nearly free. Not worth changing
  before N1.

## S1. Flora re-gathers and re-uploads a whole layer when any tile changes [world]
- `FloraSystem.cs:378-397, 481-532`: unloading or placing any tile marks the layer Dirty; Upload then copies every tile
  of the layer into one array, runs CountModelsJob synchronously (`.Run()`, main thread) and re-uploads the whole
  instance buffer with SetData.
- The cost scales with all plants in range, not with what changed, and at glide speed tiles change every few frames.
  A likely contributor to glide's 1% low (30 at cap 60, the number to watch). Confirm with `bench.sh profile <c> glide`.
- Fix: give each tile a fixed slot range in the GPU buffer (a ring over the tile grid), upload only new tiles with
  SetData(offset), and keep model counts per tile so the totals are a subtraction, not a recount.

## S2. Terrain streaming: budgeted well, one forced wait [world]
- `TerrainStreamingSystem.cs:48-55, 229-242`: 14,000 vertices a batch (a quarter at speed, nearest-ahead first),
  unload at view radius + 1.5 chunks (hysteresis), meshes pooled. Good.
- `:133, 327`: a batch still running after MaxBuildFrames (4) is completed on the main thread. At glide speed, with
  flora uploads (S1) in the same frames, that's a stall. Log how often it happens before tuning; if often, skip the
  forced wait and let the next batch start later.
- `:444` allocation and `:448-452` structural toggles: see part 1, item 5.

## S3. Caves and ground detail: bounded [world]
- `CaveSystem.cs:1242-1290`: wanted list rebuilt only when the camera changes chunk; unload at 1.2x load radius
  (hysteresis); chunks still building are parked until their jobs end rather than waited on. Good. The sort lambda
  captures (one small allocation per chunk crossing): fine.
- `GroundDetailSystem.cs:152, 182-191`: tiles unloaded beyond radius + 1.5 tiles. Good.

## S4. GPU buffers only grow [world]
- `FloraSystem.cs:523-529`: instance buffers grow to the next power of two and never shrink. A high-water mark, so
  memory after a dense forest stays at forest size. Fine on desktop; note for any lower-memory target.

## Not found
- No global memory budget across terrain, caves, flora and detail: each is bounded by its own radius, which is fine
  for a fixed view distance. A view-distance setting would need one.
