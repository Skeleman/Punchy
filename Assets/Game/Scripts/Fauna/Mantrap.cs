using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The carnivorous plant (#41): rooted, never walks. A thin stalk of six jointed segments, four big leaves and a head of
    /// two black coconut-shell halves that close as a mouth. About ogre height (2.6 m). The head follows the nearest living
    /// thing within 8 m (player, animal, resident); anything within 1 m of the stalk gets a lunge (wind-up, fast strike,
    /// snap), then a short cooldown. A snap shoves the player (a short push and hop) or sends an animal off fleeing: there
    /// is no health system, so nothing else. Procedural meshes only (no imported assets).
    /// Placement comes from the world seed and the terrain alone (a seeded survey of FoggyMarsh, PoisonSwamp and
    /// FungalForest cells, clear of settlements and landmarks), so every machine in a game builds the same plants; the head
    /// is simulated locally, from the local player and the animals this machine can see.
    /// Test flags: "-nomantrap" (per shot line), "-mantraptest" (a 60 s scripted run), "-mantrapshot side|lunge".
    /// </summary>
    public class Mantrap : MonoBehaviour
    {
        public static bool Hidden;
        public static bool Placed { get; private set; }
        /// <summary>"-mantraptest": a scripted run (see RunTest), started once.</summary>
        public static bool TestRequested;
        /// <summary>"-mantrapshot lunge": the strike is held at its peak for HoldTime so a shot can catch it.</summary>
        public static bool HoldStrike;
        /// <summary>How long "-mantrapshot lunge" holds the strike at its peak (A-10c: was 8 s).</summary>
        public const float HoldTime = 0.3f;
        /// <summary>Set the frame a held strike starts, so the shot list can take its shot during the hold.</summary>
        public static bool StrikeHeld;
        /// <summary>"-mantrapstill": the player is put 0.8 m from plant 0 and put back there between bites (a hit check).</summary>
        public static bool StillRequested;
        /// <summary>Thin fog for the plant shots (set by "-mantrapshot").</summary>
        public static bool ThinFog;
        public static readonly List<Vector3> Spots = new List<Vector3>();

        const int Segments = 6;
        const float SegmentLength = 0.4f;
        const float TrackRange = 8f;
        const float BiteRange = 1f;
        const float Reach = 2f;           // a bite hits what is within this of the head tip...
        const float MaxFromStalk = 2.4f;  // ...and never anything farther than this from the stalk
        const float Cooldown = 1.2f;
        const float Step = 90f;
        const float MinSeparation = 45f;

        static readonly string[] BiomeNames = { "FoggyMarsh", "PoisonSwamp", "FungalForest" };
        static readonly int[] BiomeCaps = { 10, 8, 8 };

        static readonly Color Green = new Color(0.20f, 0.42f, 0.16f);
        static readonly Color DarkGreen = new Color(0.12f, 0.30f, 0.12f);
        static readonly Color Shell = new Color(0.06f, 0.055f, 0.05f);
        static readonly Color Flesh = new Color(0.55f, 0.10f, 0.14f);
        static readonly Color Tooth = new Color(0.86f, 0.82f, 0.66f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            // Going solo after losing the host reloads the scene, which destroys this object: build it again then.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindAnyObjectByType<Mantrap>() != null)
                return;
            Placed = false;
            new GameObject("Mantraps").AddComponent<Mantrap>();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        /// <summary>True when <see cref="source"/> belongs to the live default world (fetched again after a world swap).</summary>
        bool CurrentSource()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w != null && w.IsCreated && w == sourceWorld)
                return true;
            sourceWorld = null;
            if (w == null || !w.IsCreated)
                return false;
            using var terrainQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!terrainQuery.TryGetSingleton(out source))
                return false;
            sourceWorld = w;
            return true;
        }

        sealed class Plant
        {
            public Transform Root, Aim, Head, Upper, Lower;
            public Transform[] Seg = new Transform[Segments];
            public Transform[] Leaf = new Transform[4];
            public Vector3 Position;
            public float Scale, Phase;
            public float Yaw, Phi, Jaw;                 // current head turn (world yaw, deg), bend (deg), jaw open 0..1
            public int Stage;                           // 0 none, 1 wind-up, 2 strike, 3 snap, 4 recover
            public float StageTime, Wait, Hold, Flare;
            public int Target = -1;
            public int Lunges, Tracked;
            public Vector3 StrikeAt;
            public int StrikeKind;
            public Entity StrikeEntity;
        }

        TerrainSource source;
        // The world the cached source came from. Its blobs are freed with that world (joining, or the host leaving).
        World sourceWorld;
        bool havePlace;
        float nextTry;
        readonly List<Plant> plants = new List<Plant>();
        static Mesh[] segMeshes;
        static Mesh baseMesh, leafMesh, upperMesh, lowerMesh;
        static Material material;

        // Targets this machine can see: the local player first, then animals.
        World world;
        EntityQuery playerQuery, creatureQuery;
        EntityManager simManager;
        World queriedWorld;
        readonly List<float3> tPos = new List<float3>();
        readonly List<Entity> tEntity = new List<Entity>();
        readonly List<int> tKind = new List<int>();
        float nextRefresh;
        Entity playerEntity;

        // Shoves in progress (the player: pushed over 0.25 s).
        float3 shoveDir;
        float shoveLeft;

        // Totals and the scripted test.
        int totalLunges, lungesPlayer, lungesAnimal, trackFrames;
        float testStart = -1f;
        int testStep;
        Entity testDeer;
        float lastLog, stillStart = -1f;

        void Update()
        {
            if (!Placed)
            {
                if (!havePlace && Time.realtimeSinceStartup >= nextTry)
                {
                    nextTry = Time.realtimeSinceStartup + 1f;
                    TryPlace();
                }
                return;
            }
            if (!CurrentSource())
                return;
            var shown = !Hidden;
            foreach (var p in plants)
                if (p.Root.gameObject.activeSelf != shown)
                    p.Root.gameObject.SetActive(shown);
            if (!shown)
                return;
            Refresh();
            var dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (shoveLeft > 0f && world != null && world.IsCreated && playerEntity != Entity.Null && world.EntityManager.Exists(playerEntity))
            {
                var step = Mathf.Min(dt, shoveLeft);
                shoveLeft -= step;
                var t = world.EntityManager.GetComponentData<LocalTransform>(playerEntity);
                t.Position += shoveDir * (step * 6f);
                world.EntityManager.SetComponentData(playerEntity, t);
            }
            foreach (var p in plants)
                Tick(p, dt);
            if (TestRequested)
                RunTest();
            if (StillRequested)
                RunStill();
        }

        void LateUpdate()
        {
            if (ThinFog && RenderSettings.fogDensity > 0.0003f)
                RenderSettings.fogDensity = 0.0003f;
        }

        /// <summary>"-mantrapstill": 18 s with the player 0.8 m from plant 0, put back between bites; then a summary.</summary>
        void RunStill()
        {
            if (Spots.Count == 0 || playerEntity == Entity.Null || plants.Count == 0)
                return;
            var plant = plants[0];
            if (stillStart < 0f)
            {
                stillStart = Time.time;
                Debug.Log("Mantrap: still test starts, player 0.8 m from plant 0");
            }
            if (plant.Stage == 0 || stillStart == Time.time)
                MovePlayer(plant.Position + new Vector3(0.8f, 0f, 0f));
            if (Time.time - stillStart > 18f)
            {
                StillRequested = false;
                Debug.Log($"Mantrap: still test summary: plant 0 lunged {plant.Lunges}, player hit {lungesPlayer}");
            }
        }

        // ---------------------------------------------------------------- targets

        void Refresh()
        {
            if (Time.time < nextRefresh)
                return;
            nextRefresh = Time.time + 0.1f;
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return;
            if (world != w)
            {
                world = w;
                playerQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            }
            tPos.Clear(); tEntity.Clear(); tKind.Clear();
            if (playerQuery.CalculateEntityCount() == 1)
            {
                playerEntity = playerQuery.GetSingletonEntity();
                tPos.Add(w.EntityManager.GetComponentData<LocalTransform>(playerEntity).Position);
                tEntity.Add(playerEntity);
                tKind.Add(0);
            }
            var director = FindAnyObjectByType<FaunaDirector>();
            var sim = director != null ? director.SimWorld : null;
            if (sim == null || !sim.IsCreated)
                return;
            if (queriedWorld != sim)
            {
                queriedWorld = sim;
                simManager = sim.EntityManager;
                creatureQuery = simManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>() },
                    None = new[] { ComponentType.ReadOnly<PlayerAvatar>() }
                });
            }
            // (The creatures as the shared snapshot last had them, a frame behind at most, A-70 row 3; it includes
            // players' bodies, which this query leaves out.)
            var timed = ReaderTimes.Start();
            CreatureSnapshotSystem.Get(sim, creatureQuery, out var states, out var bodies, out var entities);
            // Only those anywhere near a plant matter: picked out in Burst (A-70 row 5), in creature order.
            var roots = new NativeArray<float2>(plants.Count, Allocator.TempJob);
            for (int k = 0; k < plants.Count; k++)
                roots[k] = new float2(plants[k].Position.x, plants[k].Position.z);
            var picked = new NativeList<int>(16, Allocator.TempJob);
            new NearJob { States = states, Roots = roots, RangeSq = TrackRange * TrackRange * 1.5f, Picked = picked }.Run();
            for (int n = 0; n < picked.Length; n++)
            {
                var i = picked[n];
                if (simManager.HasComponent<PlayerAvatar>(entities[i]))
                    continue;
                tPos.Add(states[i].Position);
                tEntity.Add(entities[i]);
                tKind.Add(1);
            }
            roots.Dispose();
            picked.Dispose();
            CreatureSnapshotSystem.Release(sim, states, bodies, entities);
            ReaderTimes.Stop(ReaderTimes.Mantrap, timed);
        }

        /// <summary>A-70 row 5: the creatures within reach of any plant (indices, in order), for Refresh.</summary>
        [BurstCompile]
        struct NearJob : IJob
        {
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<float2> Roots;
            public float RangeSq;
            public NativeList<int> Picked;

            public void Execute()
            {
                for (int i = 0; i < States.Length; i++)
                    for (int k = 0; k < Roots.Length; k++)
                        if (math.distancesq(Roots[k], States[i].Position.xz) < RangeSq)
                        {
                            Picked.Add(i);
                            break;
                        }
            }
        }

        // ---------------------------------------------------------------- behaviour

        void Tick(Plant p, float dt)
        {
            var clock = Time.time + p.Phase;
            var basePoint = p.Position + Vector3.up * 1.2f * p.Scale;
            // Nearest living thing within range.
            var best = -1;
            var bestD = TrackRange;
            for (int i = 0; i < tPos.Count; i++)
            {
                var d = Vector3.Distance(basePoint, tPos[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            p.Target = best;
            var wantYaw = p.Yaw;
            var wantPhi = 10f + 4f * Mathf.Sin(clock * 0.7f);
            var wantJaw = 0.12f + 0.08f * Mathf.Sin(clock * 0.5f);
            var turnSpeed = 90f;
            var bendSpeed = 60f;
            var jawSpeed = 3f;
            Vector3 tp = Vector3.zero;
            float horiz = 99f;
            if (best >= 0)
            {
                tp = tPos[best];
                var dx = tp.x - p.Position.x;
                var dz = tp.z - p.Position.z;
                horiz = Mathf.Sqrt(dx * dx + dz * dz);
                wantYaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                wantPhi = Mathf.Clamp(Mathf.Atan2(horiz, 1.9f) * Mathf.Rad2Deg * 1.15f, 14f, 70f);
                wantJaw = horiz < 4f ? 0.55f : 0.25f;
                p.Tracked++;
                trackFrames++;
            }
            else
            {
                wantYaw = p.Yaw + Mathf.Sin(clock * 0.31f) * 25f * dt;
                turnSpeed = 20f;
            }

            switch (p.Stage)
            {
                case 0:
                    p.Wait -= dt;
                    if (best >= 0 && horiz < BiteRange && p.Wait <= 0f && Mathf.Abs(tp.y - p.Position.y) < 3f)
                    {
                        p.Stage = 1; p.StageTime = 0f; p.StrikeAt = tp; p.StrikeKind = tKind[best]; p.StrikeEntity = tEntity[best];
                    }
                    break;
                case 1:   // wind-up: rear back, jaws wide
                    p.StageTime += dt;
                    wantPhi = -8f; wantJaw = 1f; bendSpeed = 140f; jawSpeed = 8f; turnSpeed = 360f;
                    TrackStrike(p);
                    wantYaw = YawTo(p, p.StrikeAt);
                    p.Flare = Mathf.MoveTowards(p.Flare, 1f, dt * 8f);
                    if (p.StageTime >= 0.22f) { p.Stage = 2; p.StageTime = 0f; }
                    break;
                case 2:   // strike: fast fold down on the spot, jaws still open
                    p.StageTime += dt;
                    wantPhi = 105f; wantJaw = 1f; bendSpeed = 700f; jawSpeed = 8f; turnSpeed = 720f;
                    TrackStrike(p);
                    wantYaw = YawTo(p, p.StrikeAt);
                    if (p.Phi >= 100f || p.StageTime > 0.3f)
                    {
                        if (HoldStrike && p.Hold < HoldTime)
                        {
                            if (p.Hold == 0f) StrikeHeld = true;
                            p.Hold += dt;
                            break;
                        }
                        if (HoldStrike && p.Hold > 0f)
                            Debug.Log($"Mantrap: strike held {p.Hold:F2} s at phi {p.Phi:F0}");
                        p.Stage = 3; p.StageTime = 0f;
                        Snap(p);
                    }
                    break;
                case 3:   // snap shut
                    p.StageTime += dt;
                    wantPhi = 105f; wantJaw = 0f; bendSpeed = 120f; jawSpeed = 16f;
                    wantYaw = YawTo(p, p.StrikeAt);
                    if (p.StageTime >= 0.22f) { p.Stage = 4; p.StageTime = 0f; }
                    break;
                case 4:   // recover
                    p.StageTime += dt;
                    wantPhi = 20f; wantJaw = 0.1f; bendSpeed = 90f; jawSpeed = 4f; turnSpeed = 90f;
                    p.Flare = Mathf.MoveTowards(p.Flare, 0f, dt * 2f);
                    if (p.StageTime >= 0.6f) { p.Stage = 0; p.Wait = Cooldown; p.Hold = 0f; }
                    break;
            }

            p.Yaw = Mathf.MoveTowardsAngle(p.Yaw, wantYaw, turnSpeed * dt);
            p.Phi = Mathf.MoveTowards(p.Phi, wantPhi, bendSpeed * dt);
            p.Jaw = Mathf.MoveTowards(p.Jaw, wantJaw, jawSpeed * dt);
            Pose(p, clock);
        }

        /// <summary>The head keeps aiming at the target it chose while it winds up and strikes.</summary>
        void TrackStrike(Plant p)
        {
            for (int i = 0; i < tEntity.Count; i++)
                if (tKind[i] == p.StrikeKind && tEntity[i] == p.StrikeEntity) { p.StrikeAt = tPos[i]; return; }
        }

        static float YawTo(Plant p, Vector3 at) => Mathf.Atan2(at.x - p.Position.x, at.z - p.Position.z) * Mathf.Rad2Deg;

        /// <summary>The bite: whatever is still within reach of the head gets shoved.</summary>
        void Snap(Plant p)
        {
            p.Lunges++;
            totalLunges++;
            var tip = p.Head.position + p.Head.forward * 0.3f * p.Scale;
            var from = new float3(p.Position.x, 0f, p.Position.z);
            var hitKind = -1;
            if (p.StrikeKind == 0 && playerEntity != Entity.Null && world != null && world.IsCreated && world.EntityManager.Exists(playerEntity))
            {
                var pos = world.EntityManager.GetComponentData<LocalTransform>(playerEntity).Position;
                if (InReach(p, tip, pos))
                {
                    var away = new float3(pos.x - from.x, 0f, pos.z - from.z);
                    shoveDir = math.lengthsq(away) > 1e-4f ? math.normalize(away) : new float3(0f, 0f, 1f);
                    shoveLeft = 0.25f;
                    if (world.EntityManager.HasComponent<PlayerMotion>(playerEntity))
                    {
                        var m = world.EntityManager.GetComponentData<PlayerMotion>(playerEntity);
                        m.VerticalVelocity = 4.5f;
                        m.Grounded = false;
                        world.EntityManager.SetComponentData(playerEntity, m);
                    }
                    hitKind = 0;
                    lungesPlayer++;
                }
            }
            else if (p.StrikeKind == 1 && simManager.Exists(p.StrikeEntity) && simManager.HasComponent<CreatureState>(p.StrikeEntity))
            {
                var s = simManager.GetComponentData<CreatureState>(p.StrikeEntity);
                if (InReach(p, tip, s.Position))
                {
                    var away = new float3(s.Position.x - from.x, 0f, s.Position.z - from.z);
                    away = math.lengthsq(away) > 1e-4f ? math.normalize(away) : new float3(0f, 0f, 1f);
                    s.Position += away * 1.2f;
                    s.Yaw = math.atan2(away.x, away.z);
                    s.Focus = new float2(p.Position.x, p.Position.z);
                    s.Mode = CreatureMode.Flee;
                    s.ModeTime = 0f;
                    s.StageTime = 0f;
                    s.Stage = 0;
                    simManager.SetComponentData(p.StrikeEntity, s);
                    hitKind = 1;
                    lungesAnimal++;
                }
            }
            Debug.Log($"Mantrap: lunge {totalLunges} at ({p.Position.x:F0}, {p.Position.z:F0}) on {(p.StrikeKind == 0 ? "player" : "animal")}, " +
                      $"head tip {Vector3.Distance(new Vector3(tip.x, 0f, tip.z), new Vector3(p.StrikeAt.x, 0f, p.StrikeAt.z)):F2} m from it, hit {(hitKind == 0 ? "player shoved" : hitKind == 1 ? "animal sent fleeing" : "none (out of reach)")}");
        }

        /// <summary>Judged against the head tip (about 2 m), and never past 2.4 m from the stalk.</summary>
        static bool InReach(Plant p, Vector3 tip, float3 at)
        {
            var fromStalk = math.distance(at.xz, new float2(p.Position.x, p.Position.z));
            var fromTip = math.distance(at.xz, new float2(tip.x, tip.z));
            return fromStalk < MaxFromStalk && fromTip < Reach;
        }

        // ---------------------------------------------------------------- posing

        void Pose(Plant p, float clock)
        {
            p.Aim.localRotation = Quaternion.Euler(0f, p.Yaw, 0f);
            var per = p.Phi / Segments;
            for (int i = 0; i < Segments; i++)
            {
                var sway = Mathf.Sin(clock * 0.9f - i * 0.5f) * 2.2f;
                var roll = Mathf.Sin(clock * 0.6f + i * 0.4f) * 1.6f;
                p.Seg[i].localRotation = Quaternion.Euler(per + (i == 0 ? sway : sway * 0.4f), 0f, roll);
            }
            // Mouth: the halves hinge at the back, 60 degrees each when fully open.
            p.Upper.localRotation = Quaternion.Euler(-62f * p.Jaw, 0f, 0f);
            p.Lower.localRotation = Quaternion.Euler(62f * p.Jaw, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var wave = Mathf.Sin(clock * 1.3f + i * 1.7f) * 7f;
                p.Leaf[i].localRotation = Quaternion.Euler(0f, i * 90f + 25f + Mathf.Sin(clock * 0.4f + i) * 6f, 0f) *
                                          Quaternion.Euler(58f - p.Flare * 24f + wave, 0f, 0f);
            }
        }

        // ---------------------------------------------------------------- placing

        void TryPlace()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return;
            var em = w.EntityManager;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return;
            sourceWorld = w;
            var palette = em.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var which = new NativeArray<int>(palette.Biomes.Length, Allocator.TempJob);
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                which[b] = -1;
                for (int k = 0; k < BiomeNames.Length; k++)
                    if (palette.Biomes[b] != null && palette.Biomes[b].name == BiomeNames[k])
                        which[b] = k;
            }
            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            var spots = new NativeArray<float2>(n * n, Allocator.TempJob);
            var kinds = new NativeArray<int>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Which = which, Origin = -half, Step = Step, Size = n, Seed = source.World.Seed, Spot = spots, Kind = kinds }
                .Schedule(n * n, 64).Complete();

            var perBiome = new List<(float key, float2 spot)>[BiomeNames.Length];
            for (int k = 0; k < perBiome.Length; k++) perBiome[k] = new List<(float, float2)>();
            var rejectedTown = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] < 0)
                    continue;
                if (InSettlement(spots[i]) || OnLandmark(spots[i]))
                {
                    rejectedTown++;
                    continue;
                }
                var rng = Unity.Mathematics.Random.CreateFromIndex((uint)i * 2654435761u ^ (uint)source.World.Seed ^ 0x3A47F1u);
                perBiome[kinds[i]].Add((rng.NextFloat(), spots[i]));
            }
            var counts = new int[BiomeNames.Length];
            var chosen = new List<float2>();
            var chosenBiome = new List<int>();
            for (int k = 0; k < perBiome.Length; k++)
            {
                perBiome[k].Sort((a, b) => a.key.CompareTo(b.key));
                foreach (var c in perBiome[k])
                {
                    if (counts[k] >= BiomeCaps[k])
                        break;
                    var apart = true;
                    foreach (var o in chosen)
                        if (math.distance(o, c.spot) < MinSeparation) { apart = false; break; }
                    if (!apart)
                        continue;
                    chosen.Add(c.spot);
                    chosenBiome.Add(k);
                    counts[k]++;
                }
            }
            which.Dispose(); spots.Dispose(); kinds.Dispose();
            BuildMeshes();
            for (int i = 0; i < chosen.Count; i++)
                BuildPlant(chosen[i], i);
            havePlace = true;
            Placed = true;
            Debug.Log($"Mantrap: placed {chosen.Count}: {BiomeNames[0]} {counts[0]}, {BiomeNames[1]} {counts[1]}, {BiomeNames[2]} {counts[2]} " +
                      $"(candidates {perBiome[0].Count}/{perBiome[1].Count}/{perBiome[2].Count}, {rejectedTown} rejected for a settlement or landmark)");
            foreach (var s in Spots)
                Debug.Log($"Mantrap: plant at ({s.x:F0}, {s.y:F1}, {s.z:F0})");
        }

        bool InSettlement(float2 xz)
        {
            if (!source.Settlements.IsCreated)
                return false;
            ref var set = ref source.Settlements.Value;
            for (int t = 0; t < set.Settlements.Length; t++)
                if (math.distance(set.Settlements[t].Centre, xz) < set.Settlements[t].Radius + 25f)
                    return true;
            return false;
        }

        bool OnLandmark(float2 xz)
        {
            if (!source.Landmarks.IsCreated)
                return false;
            ref var set = ref source.Landmarks.Value;
            var range = LandmarkSampling.CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
                if (LandmarkSampling.Hit(ref set, set.Items[k], xz, out _))
                    return true;
            return false;
        }

        [BurstCompile]
        struct SurveyJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<int> Which;
            public float Origin, Step;
            public int Size;
            public uint Seed;
            [WriteOnly] public NativeArray<float2> Spot;
            [WriteOnly] public NativeArray<int> Kind;

            public void Execute(int i)
            {
                var rng = Unity.Mathematics.Random.CreateFromIndex((uint)i * 1664525u ^ Seed ^ 0x7B1D2Cu);
                var xz = new float2(Origin + i % Size * Step, Origin + i / Size * Step) + rng.NextFloat2(Step * 0.1f, Step * 0.9f);
                Spot[i] = xz;
                Kind[i] = -1;
                var g = Source.SampleGround(xz);
                if (g.WaterDepth > -0.2f)
                    return;
                var b = g.Biomes.Dominant();
                if (b < 0 || b >= Which.Length || Which[b] < 0)
                    return;
                var h = g.Height;
                var slope = math.max(math.abs(Source.SampleHeight(xz + new float2(1.5f, 0f)) - h), math.abs(Source.SampleHeight(xz + new float2(0f, 1.5f)) - h)) / 1.5f;
                if (slope > 0.5f)
                    return;
                Kind[i] = Which[b];
            }
        }

        // ---------------------------------------------------------------- building

        static void BuildMeshes()
        {
            if (segMeshes != null)
                return;
            material = Resources.Load<Material>("Colossus");
            segMeshes = new Mesh[Segments];
            for (int i = 0; i < Segments; i++)
            {
                var b = new DunesColossus.MeshBuilder();
                var r0 = Mathf.Lerp(0.085f, 0.05f, i / (float)Segments);
                var r1 = Mathf.Lerp(0.085f, 0.05f, (i + 1) / (float)Segments);
                b.Limb(Vector3.zero, new Vector3(0f, SegmentLength, 0f), r0, r1, i % 2 == 0 ? Green : DarkGreen, 6);
                segMeshes[i] = b.ToMesh("MantrapStalk" + i);
            }
            var bb = new DunesColossus.MeshBuilder();
            bb.Ellipsoid(new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.2f, 0.3f), Quaternion.identity, DarkGreen);
            for (int k = 0; k < 5; k++)
            {
                var a = k * 72f * Mathf.Deg2Rad;
                bb.Limb(new Vector3(Mathf.Sin(a) * 0.1f, 0.05f, Mathf.Cos(a) * 0.1f), new Vector3(Mathf.Sin(a) * 0.5f, -0.02f, Mathf.Cos(a) * 0.5f), 0.07f, 0.02f, DarkGreen, 5);
            }
            baseMesh = bb.ToMesh("MantrapBase");

            // A leaf points along +Z, drooping at the tip: stem, broad blade, a smaller curled end.
            var lb = new DunesColossus.MeshBuilder();
            lb.Limb(Vector3.zero, new Vector3(0f, 0.04f, 0.45f), 0.04f, 0.03f, Green, 5);
            lb.Ellipsoid(new Vector3(0f, 0.06f, 0.95f), new Vector3(0.27f, 0.025f, 0.55f), Quaternion.Euler(-6f, 0f, 0f), Green, 12, 6);
            lb.Ellipsoid(new Vector3(0f, -0.06f, 1.42f), new Vector3(0.17f, 0.02f, 0.3f), Quaternion.Euler(24f, 0f, 0f), DarkGreen, 10, 5);
            lb.Limb(new Vector3(0f, 0.07f, 0.4f), new Vector3(0f, 0.065f, 1.45f), 0.012f, 0.008f, DarkGreen, 4);
            leafMesh = lb.ToMesh("MantrapLeaf");

            // The head: shell halves as bowls, hinged at the back of the sphere (z = 0), mouth along +Z. The upper half's bowl
            // opens down, the lower half's up; a red lining inside and teeth round the front of each rim.
            var centre = new Vector3(0f, 0f, 0.3f);
            var radii = new Vector3(0.27f, 0.25f, 0.3f);
            var ub = new DunesColossus.MeshBuilder();
            ub.Dome(centre, radii, Quaternion.identity, Shell);
            ub.Dome(centre, radii * 0.93f, Quaternion.identity, Flesh, true);
            var lwb = new DunesColossus.MeshBuilder();
            lwb.Dome(centre, radii, Quaternion.Euler(180f, 0f, 0f), Shell);
            lwb.Dome(centre, radii * 0.93f, Quaternion.Euler(180f, 0f, 0f), Flesh, true);
            for (int k = 0; k < 7; k++)
            {
                var a = Mathf.Lerp(-72f, 72f, k / 6f) * Mathf.Deg2Rad;
                var rim = centre + new Vector3(Mathf.Sin(a) * radii.x * 0.93f, 0f, Mathf.Cos(a) * radii.z * 0.93f);
                ub.Limb(rim, rim + new Vector3(0f, -0.09f, 0f), 0.02f, 0.004f, Tooth, 4);
                var a2 = Mathf.Lerp(-72f, 72f, (k + 0.5f) / 6f) * Mathf.Deg2Rad;
                if (k < 6)
                {
                    var rim2 = centre + new Vector3(Mathf.Sin(a2) * radii.x * 0.93f, 0f, Mathf.Cos(a2) * radii.z * 0.93f);
                    lwb.Limb(rim2, rim2 + new Vector3(0f, 0.09f, 0f), 0.02f, 0.004f, Tooth, 4);
                }
            }
            upperMesh = ub.ToMesh("MantrapUpper");
            lowerMesh = lwb.ToMesh("MantrapLower");
        }

        static Transform Part(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            return go.transform;
        }

        void BuildPlant(float2 xz, int index)
        {
            var ground = source.SampleHeight(xz);
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)index * 7919u ^ (uint)source.World.Seed ^ 0x51F3u);
            var p = new Plant { Position = new Vector3(xz.x, ground, xz.y), Scale = rng.NextFloat(0.92f, 1.12f), Phase = rng.NextFloat(0f, 20f) };
            p.Yaw = rng.NextFloat(0f, 360f);
            p.Phi = 10f;
            p.Wait = 0.5f;
            p.Root = Part("Mantrap" + index, transform, baseMesh);
            p.Root.position = p.Position;
            p.Root.localScale = Vector3.one * p.Scale;
            p.Aim = Part("Aim", p.Root, null);
            var parent = p.Aim;
            for (int i = 0; i < Segments; i++)
            {
                p.Seg[i] = Part("Stalk" + i, parent, segMeshes[i]);
                p.Seg[i].localPosition = i == 0 ? new Vector3(0f, 0.1f, 0f) : new Vector3(0f, SegmentLength, 0f);
                parent = p.Seg[i];
            }
            // Four leaves sprout from the lower stalk's sides, at different heights.
            for (int i = 0; i < 4; i++)
            {
                p.Leaf[i] = Part("Leaf" + i, p.Seg[Mathf.Min(i, 3)], leafMesh);
                p.Leaf[i].localPosition = new Vector3(0f, SegmentLength * (0.35f + 0.18f * i), 0f);
                p.Leaf[i].localScale = Vector3.one * (1.15f - 0.08f * i);
            }
            p.Head = Part("Head", p.Seg[Segments - 1], null);
            p.Head.localPosition = new Vector3(0f, SegmentLength, 0f);
            p.Head.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the mouth looks along the stalk's end
            p.Upper = Part("Upper", p.Head, upperMesh);
            p.Lower = Part("Lower", p.Head, lowerMesh);
            plants.Add(p);
            Spots.Add(p.Position);
            Pose(p, 0f);
        }

        // ---------------------------------------------------------------- test and shots

        /// <summary>The nearest plant to a point (for shot framing); null when none.</summary>
        public static bool Nearest(Vector3 to, out Vector3 at)
        {
            at = default;
            var best = float.MaxValue;
            foreach (var s in Spots)
            {
                var d = (s - to).sqrMagnitude;
                if (d < best) { best = d; at = s; }
            }
            return best < float.MaxValue;
        }

        /// <summary>Plant by index in placement order.</summary>
        public static Vector3 Spot(int index) => Spots[Mathf.Clamp(index, 0, Spots.Count - 1)];

        void MovePlayer(Vector3 to)
        {
            if (world == null || !world.IsCreated || playerEntity == Entity.Null)
                return;
            var em = world.EntityManager;
            var t = em.GetComponentData<LocalTransform>(playerEntity);
            t.Position = new float3(to.x, source.SampleHeight(new float2(to.x, to.z)) + 0.1f, to.z);
            em.SetComponentData(playerEntity, t);
        }

        /// <summary>
        /// "-mantraptest": 60 s on the first plant. 0 s the player 6 m off (tracked, no lunge); 6 s a Deer set walking past the
        /// stalk 0.5 m from it (tracked, lunged at); 30 s the player steps to 0.6 m (lunged at); 60 s summary.
        /// </summary>
        void RunTest()
        {
            if (Spots.Count == 0 || playerEntity == Entity.Null)
                return;
            if (testStart < 0f)
            {
                testStart = Time.time;
                Debug.Log("Mantrap: test starts on plant 0");
            }
            var t = Time.time - testStart;
            var plant = plants[0];
            var at = plant.Position;
            if (testStep == 0)
            {
                testStep = 1;
                MovePlayer(at + new Vector3(0f, 0f, -6f));
            }
            else if (testStep == 1 && t > 6f)
            {
                testStep = 2;
                var director = FindAnyObjectByType<FaunaDirector>();
                var idx = director != null ? director.SpeciesIndex("Deer") : -1;
                if (idx >= 0 && director.CanSpawn)
                {
                    var start = at + new Vector3(-7f, 0f, 0.5f);
                    testDeer = director.Spawn(idx, new float3(start.x, source.SampleHeight(new float2(start.x, start.z)), start.z), math.radians(90f));
                    var sim = director.SimWorld.EntityManager;
                    var s = sim.GetComponentData<CreatureState>(testDeer);
                    s.Mode = CreatureMode.Puppet;
                    s.Speed = 1.4f;
                    s.Yaw = math.radians(90f);
                    sim.SetComponentData(testDeer, s);
                    Debug.Log($"Mantrap: test deer set walking east past the plant, start ({start.x:F1}, {start.z:F1})");
                }
                else
                    Debug.Log("Mantrap: test could not spawn a Deer");
            }
            else if (testStep == 2 && t > 30f)
            {
                testStep = 3;
                MovePlayer(at + new Vector3(0.6f, 0f, 0f));
                Debug.Log("Mantrap: test player stepped in to 0.6 m");
            }
            else if (testStep == 3 && t > 60f)
            {
                testStep = 4;
                TestRequested = false;
                Debug.Log($"Mantrap: test summary over {t:F0} s: plant 0 lunged {plant.Lunges}, all plants {totalLunges} (player {lungesPlayer}, animal {lungesAnimal}), tracked frames {trackFrames} (plant 0: {plant.Tracked})");
            }
        }
    }
}
