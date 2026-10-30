# Audit: profiler (kind-possum), first pass

Read-only, on rc/v0.9.0 4d7e986 (2026-10-29). Pattern searches plus focused reads, not a line-by-line pass over all
2.4 MB. Not covered yet: netcode relevancy/importance/send rates, streaming and cache budgets/eviction.
Ranked by impact. Owning lane in brackets.

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
