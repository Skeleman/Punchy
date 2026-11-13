using System.Diagnostics;
using System.IO;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using EcsWorld = Unity.Entities.World;

namespace Punchy.Chemistry
{
    /// <summary>
    /// Experimental bridge to the spell-chemistry core (the com.anweather.chemistry-core package; see README.md here).
    /// Punchy's ground is a function of position with no stored layer that can change, so the chemistry runs as an
    /// overlay: a 256 x 256 cell world (1 m cells) in a window centred on the player, seeded from TerrainSource on the
    /// 1 m grid (grass, water, rock, dirt) and ticked at the rules' tick rate. Cell (x, y) is Punchy's (x, z) plus the
    /// window's corner. H casts Fireball (Shift+H Fire Bolt) and J casts Frost (Shift+J Sleet Storm) at the ground
    /// under the screen centre, found by marching a ray against TerrainSource (the terrain has no colliders). What the
    /// cells do (burning, burnt, ice, frost) goes into a 256 x 256 map that the grass, terrain and water shaders read
    /// (ChemistryOverlay.hlsl). Local and cosmetic only: no network, no saving. -nochemistry turns it off.
    /// </summary>
    public class ChemistryBridge : MonoBehaviour
    {
        public const int Size = 256;
        // Past this distance from the window's centre (m) the window is rebuilt round the player (what burned is lost).
        const float RecentreDistance = 96f;
        // How long a frame may spend ticking (ms); a tick that doesn't fit carries on next frame (World.StepSome).
        const double TickBudgetMs = 2.0;
        const float AimRange = 300f, AimStep = 0.5f;

        // Seeded cell kinds.
        const byte KindDirt = 0, KindGrass = 1, KindWater = 2, KindRock = 3;

        static readonly int MapId = Shader.PropertyToID("_PunchyChemMap");
        static readonly int RectId = Shader.PropertyToID("_PunchyChemRect");
        static readonly int OnId = Shader.PropertyToID("_PunchyChemOn");

        Chem.Rules rules;
        Chem.World world;
        int2 corner;              // world XZ (m) of cell (0, 0)
        byte[] seeded;            // the kind each cell was seeded as
        Texture2D map;
        Color32[] texels;
        int shownTick = -1;
        float tickClock;
        float nextReport;
        double tickMsThisSecond, mapMsThisSecond;
        int framesThisSecond;
        readonly Stopwatch watch = new Stopwatch();

        // -chem-demo: a Fireball on the nearest grass a few metres from the player, once, DemoDelay s after the
        // window's last seed (the first seed is from a stand-in terrain, so it waits for the real one).
        const float DemoDelay = 8f;
        readonly bool demo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-chem-demo") >= 0;
        bool demoDone;
        float demoAt = float.MaxValue;

        // The seed sampling in flight (a Burst job, picked up a frame or more later).
        JobHandle seedJob;
        NativeArray<byte> seedKinds;
        bool seeding;
        int2 seedCorner;
        TerrainSource seededSource, seedSourcePending;

        EntityQuery playerQuery, terrainQuery;
        EcsWorld queryWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nochemistry") >= 0 || FindAnyObjectByType<ChemistryBridge>() != null)
                return;
            var go = new GameObject("ChemistryBridge");
            DontDestroyOnLoad(go);
            go.AddComponent<ChemistryBridge>();
        }

        void Awake()
        {
            Instance = this;
            var dir = Path.Combine(Application.streamingAssetsPath, "ChemistryRules");
            try
            {
                rules = Chem.Rules.Load(dir);
                Debug.Log($"[Chemistry] rules loaded from {dir}: {rules.Sim.TicksPerSecond} ticks a second");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Chemistry] couldn't load the rules from {dir}, bridge off: {e.Message}");
                enabled = false;
            }
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (seeding)
            {
                seedJob.Complete();
                seeding = false;
            }
            if (seedKinds.IsCreated)
                seedKinds.Dispose();
        }

        void OnDestroy()
        {
            if (map != null)
                Destroy(map);
        }

        bool EnsureQueries()
        {
            var ecs = EcsWorld.DefaultGameObjectInjectionWorld;
            if (ecs == null || !ecs.IsCreated)
                return false;
            if (queryWorld != ecs)
            {
                var em = ecs.EntityManager;
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                queryWorld = ecs;
            }
            return true;
        }

        void Update()
        {
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain)
                || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var playerXZ = player.Position.xz;

            if (seeding)
            {
                if (seedJob.IsCompleted)
                    FinishSeed();
                else
                    return;
            }
            // (Also when the terrain itself changes: at start-up the first TerrainSource is a stand-in whose surfaces
            // aren't baked yet, so a window seeded from it has the wrong grass.)
            if (world == null || !terrain.Equals(seededSource) || math.distance(playerXZ, (float2)(corner + Size / 2)) > RecentreDistance)
            {
                StartSeed(terrain, playerXZ);
                return;
            }

            HandleInput(terrain, playerXZ);
            if (demo && !demoDone && Time.unscaledTime >= demoAt)
                Demo(player.Position);

            watch.Restart();
            tickClock += Time.deltaTime;
            var tickLength = 1f / Mathf.Max(1, rules.Sim.TicksPerSecond);
            // (After a long hitch, catch up at most a second of ticks.)
            tickClock = Mathf.Min(tickClock, 1f);
            while (tickClock >= tickLength || world.TickInProgress)
            {
                if (!world.StepSome(() => watch.Elapsed.TotalMilliseconds > TickBudgetMs))
                    break;
                tickClock -= tickLength;
                if (watch.Elapsed.TotalMilliseconds > TickBudgetMs)
                    break;
            }
            tickMsThisSecond += watch.Elapsed.TotalMilliseconds;

            if (world.Tick != shownTick && !world.TickInProgress)
            {
                watch.Restart();
                UpdateMap();
                mapMsThisSecond += watch.Elapsed.TotalMilliseconds;
                shownTick = world.Tick;
            }
            framesThisSecond++;
            if (Time.unscaledTime >= nextReport)
                Report();
        }

        // ------------------------------------------------------------------------------------------------ seeding

        void StartSeed(TerrainSource terrain, float2 playerXZ)
        {
            seedCorner = (int2)math.floor(playerXZ) - Size / 2;
            seedSourcePending = terrain;
            if (!seedKinds.IsCreated)
                seedKinds = new NativeArray<byte>(Size * Size, Allocator.Persistent);
            seedJob = new SeedJob { Source = terrain, Corner = seedCorner, Kinds = seedKinds }.Schedule(Size * Size, 256);
            seeding = true;
        }

        void FinishSeed()
        {
            seedJob.Complete();
            seeding = false;
            corner = seedCorner;
            seededSource = seedSourcePending;
            demoAt = Time.unscaledTime + DemoDelay;
            world = new Chem.World(rules, Size, Size, 1, "dirt");
            seeded = seedKinds.ToArray();
            int grass = 0, water = 0, rock = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var c = world.At(x, y);
                    switch (seeded[y * Size + x])
                    {
                        case KindGrass:
                            c.T = "grass";
                            grass++;
                            break;
                        case KindWater:
                            // A pool two height steps down, filled to just under the bank, so the water stays put
                            // (cells all start level: the overlay doesn't model Punchy's slopes).
                            c.Height = -2;
                            c.Water = 6;
                            water++;
                            break;
                        case KindRock:
                            c.T = "rock";
                            rock++;
                            break;
                    }
                }
            shownTick = -1;
            tickClock = 0f;
            EnsureMap();
            Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, 1f / Size, 1f / Size));
            Shader.SetGlobalFloat(OnId, 1f);
            Debug.Log($"[Chemistry] window at ({corner.x}, {corner.y}) to ({corner.x + Size}, {corner.y + Size}): {grass} grass, {water} water, {rock} rock cells");
        }

        /// <summary>Samples the ground on the window's 1 m grid (at cell centres) and decides each cell's kind.</summary>
        [BurstCompile]
        struct SeedJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int2 Corner;
            [WriteOnly] public NativeArray<byte> Kinds;

            public void Execute(int i)
            {
                var xz = new float2(Corner.x + i % Size + 0.5f, Corner.y + i / Size + 0.5f);
                var ground = Source.SampleGround(xz);
                if (ground.HasWater && !ground.CaveWater && ground.WaterSurface > ground.Height + 0.1f)
                {
                    Kinds[i] = KindWater;
                    return;
                }
                var normal = Source.SampleNormal(xz, 1f);
                var surface = Source.SampleSurface(xz, ground, normal);
                // As GroundDetailSystem decides where blades grow.
                var grass = surface.Grass * math.saturate(1f - surface.Snow * 3f) * math.smoothstep(0.78f, 0.9f, normal.y);
                Kinds[i] = grass > 0.1f ? KindGrass : normal.y < 0.7f ? KindRock : KindDirt;
            }
        }

        // ------------------------------------------------------------------------------------------------ casting

        void HandleInput(TerrainSource terrain, float2 playerXZ)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            bool fire = keyboard.hKey.wasPressedThisFrame, frost = keyboard.jKey.wasPressedThisFrame;
            if (!fire && !frost)
                return;
            bool shift = keyboard.shiftKey.isPressed;
            var cam = Camera.main;
            if (cam == null || !Aim(terrain, cam.transform.position, cam.transform.forward, out var hit))
            {
                Debug.Log("[Chemistry] nothing to aim at under the screen centre");
                return;
            }
            if (fire)
                Cast(shift ? "fire-bolt" : "fireball", hit, cam.transform.forward);
            else
                Cast(shift ? "sleet-storm" : "frost", hit, cam.transform.forward);
        }

        /// <summary>-chem-demo: casts a Fireball on the grass cell nearest the player that is at least 6 m away.</summary>
        void Demo(float3 playerPos)
        {
            demoDone = true;
            int px = (int)math.floor(playerPos.x) - corner.x, py = (int)math.floor(playerPos.z) - corner.y;
            int best = -1, bestD = int.MaxValue;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int d = (x - px) * (x - px) + (y - py) * (y - py);
                    if (seeded[y * Size + x] == KindGrass && d >= 36 && d < bestD)
                    {
                        bestD = d;
                        best = y * Size + x;
                    }
                }
            if (best < 0)
            {
                Debug.Log("[Chemistry] -chem-demo: no grass in the window");
                return;
            }
            var at = new float3(corner.x + best % Size + 0.5f, playerPos.y, corner.y + best / Size + 0.5f);
            Debug.Log("[Chemistry] -chem-demo: fireball on the nearest grass");
            Cast("fireball", at, Vector3.forward);
        }

        /// <summary>Marches a ray against TerrainSource (ground or water surface, whichever is higher).</summary>
        static bool Aim(TerrainSource terrain, Vector3 from, Vector3 direction, out float3 hit)
        {
            var dir = math.normalizesafe((float3)direction, new float3(0, 0, 1));
            var p = (float3)from;
            for (float d = 0f; d < AimRange; d += AimStep)
            {
                var q = p + dir * d;
                var ground = terrain.SampleGround(q.xz);
                var top = ground.HasWater && !ground.CaveWater ? math.max(ground.Height, ground.WaterSurface) : ground.Height;
                if (q.y <= top)
                {
                    hit = new float3(q.x, top, q.z);
                    return true;
                }
            }
            hit = default;
            return false;
        }

        /// <summary>The running bridge, for test scripts (null when off).</summary>
        public static ChemistryBridge Instance { get; private set; }
        /// <summary>Whether the window is built and ticking.</summary>
        public bool Ready => world != null && !seeding;
        public int Burning => burningCount;
        public int Burnt => burntCount;
        public int Ice => iceCount;
        public int Frost => frostCount;
        public int Tick => world?.Tick ?? 0;

        /// <summary>Casts a spell at a world point (the keys use the ground under the screen centre), facing a way.</summary>
        public void CastAt(string spell, Vector3 at, Vector3 facing)
        {
            if (Ready)
                Cast(spell, at, facing);
        }

        void Cast(string spell, float3 at, Vector3 facing)
        {
            int x = (int)math.floor(at.x) - corner.x, y = (int)math.floor(at.z) - corner.y;
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                Debug.Log($"[Chemistry] {spell} at ({at.x:F1}, {at.z:F1}) is outside the chemistry window");
                return;
            }
            var s = rules.Spell(spell);
            int charge = 1;
            if (s.Shape == "line")
            {
                // A line goes out from its caster: start it a few cells short of the aimed cell, along the camera's
                // heading, so it runs through that cell. Charged to 3 (a line half again as long per level).
                var heading = math.normalizesafe(new float2(facing.x, facing.z), new float2(0, 1));
                int aimX = (int)math.round(heading.x * 10f), aimY = (int)math.round(heading.y * 10f);
                charge = 3;
                int fromX = math.clamp(x - (int)math.round(heading.x * 6f), 0, Size - 1);
                int fromY = math.clamp(y - (int)math.round(heading.y * 6f), 0, Size - 1);
                world.Cast(spell, fromX, fromY, Chem.World.DirOf(aimX, aimY), charge, aimX, aimY);
            }
            else
            {
                world.Cast(spell, x, y, 0, charge);
            }
            var cell = world.At(x, y);
            Debug.Log($"[Chemistry] cast {spell} at ({at.x:F1}, {at.y:F1}, {at.z:F1}), cell ({x}, {y}) seeded {KindName(seeded[y * Size + x])}: now {cell.T}, fire {cell.Fire}, water {cell.Water}, frozen {cell.Frozen}. Log: {LastLog()}");
            shownTick = -1;   // show the cast at once
            UpdateMap();
            shownTick = world.Tick;
        }

        string LastLog()
        {
            var log = world.Log;
            int from = math.max(0, log.Count - 3);
            var text = "";
            for (int i = from; i < log.Count; i++)
                text += (text.Length > 0 ? " | " : "") + log[i].Text;
            return text;
        }

        static string KindName(byte k) => k == KindGrass ? "grass" : k == KindWater ? "water" : k == KindRock ? "rock" : "dirt";

        // ------------------------------------------------------------------------------------------------ the map

        void EnsureMap()
        {
            if (map != null)
                return;
            map = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "ChemistryMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texels = new Color32[Size * Size];
            Shader.SetGlobalTexture(MapId, map);
        }

        int burningCount, burntCount, iceCount, frostCount;

        /// <summary>Renders the cells into the map: r burning, g burnt, b ice, a frost (ChemistryOverlay.hlsl).</summary>
        void UpdateMap()
        {
            int burning = 0, burnt = 0, ice = 0, frost = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    var c = world.At(x, y);
                    byte r = 0, g = 0, b = 0, a = 0;
                    if (c.Fire > 0)
                    {
                        r = (byte)math.min(255, 110 + c.Fire * 50);
                        burning++;
                    }
                    if (seeded[i] == KindGrass && c.T != "grass")
                    {
                        g = 255;
                        if (c.Fire <= 0)
                            burnt++;
                    }
                    if (c.Frozen)
                    {
                        b = 255;
                        ice++;
                    }
                    if (c.Cover == "frost")
                    {
                        a = 255;
                        frost++;
                    }
                    texels[i] = new Color32(r, g, b, a);
                }
            map.SetPixels32(texels);
            map.Apply(false);
            burningCount = burning;
            burntCount = burnt;
            iceCount = ice;
            frostCount = frost;
        }

        void Report()
        {
            nextReport = Time.unscaledTime + 1f;
            if (burningCount > 0 || burntCount > 0 || iceCount > 0 || frostCount > 0)
                Debug.Log($"[Chemistry] tick {world.Tick}: {burningCount} burning, {burntCount} burnt, {iceCount} ice, {frostCount} frost; " +
                          $"bridge {((tickMsThisSecond + mapMsThisSecond) / math.max(1, framesThisSecond)):F2} ms a frame " +
                          $"(ticking {tickMsThisSecond:F1} ms, map {mapMsThisSecond:F1} ms this second, {framesThisSecond} frames)");
            tickMsThisSecond = mapMsThisSecond = 0;
            framesThisSecond = 0;
        }
    }
}
