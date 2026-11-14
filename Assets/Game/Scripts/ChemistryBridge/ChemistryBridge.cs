using System.Diagnostics;
using System.IO;
using Chem;
using Chem.Lockstep;
using Punchy.Net;
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
    /// overlay: the core's open world (ChemistryBridge.Lockstep.cs), 1 m cells on Punchy's own grid (cell (x, y) is
    /// floor(x), floor(z)), filled chunk by chunk from TerrainSource as players reach new ground and ticked at the rules'
    /// tick rate. H casts Fireball (Shift+H Fire Bolt), J Frost (Shift+J Sleet Storm) and L Create Water (Shift+L Wave) at
    /// the ground under the screen centre, found by marching a ray against TerrainSource (the terrain has no colliders).
    /// The view is a 256 x 256 window of the world round the player (4 x 4 chunks, re-centred as the player walks: the
    /// world itself stays), read cell by cell from whatever chunks are filled there into two maps that the grass, terrain
    /// and water shaders read (ChemistryOverlay.hlsl: burning, burnt, ice, frost; water on land, steam, smoke), and
    /// ChemistryEffects draws flames on burning cells and puffs of steam and smoke from the gas. -nochemistry turns it
    /// off. On a pad the wizard's hands cast (D16, ChemistryBridge.Hands.cs): LT / LB the left hand's thrust / throw, RT /
    /// RB the right's, hold to charge.
    /// </summary>
    public partial class ChemistryBridge : MonoBehaviour
    {
        // The view's window, in cells (a whole number of chunks).
        public const int Size = 256;
        const int WindowChunks = Size / Chunks.Size;
        // How long a frame may spend ticking (ms); a tick that doesn't fit carries on next frame (World.StepSome).
        const double TickBudgetMs = 2.0;
        // Behind the batches by more than CatchUpTicks (a slow start, a joiner's first seconds), a frame may tick for
        // CatchUpBudgetMs: at 10 fps the 2 ms alone runs fewer ticks than the host stamps, and casts land seconds late.
        const int CatchUpTicks = 6;
        const double CatchUpBudgetMs = 8.0;
        const float AimRange = 300f, AimStep = 0.5f;

        // Cell kinds (a filled chunk's ground, as the view reads it); KindNone where nothing is filled.
        const byte KindDirt = 0, KindGrass = 1, KindWater = 2, KindRock = 3, KindNone = 255;

        static readonly int MapId = Shader.PropertyToID("_PunchyChemMap");
        static readonly int Map2Id = Shader.PropertyToID("_PunchyChemMap2");
        static readonly int RectId = Shader.PropertyToID("_PunchyChemRect");
        static readonly int OnId = Shader.PropertyToID("_PunchyChemOn");

        Chem.Rules rules;
        Chem.World viewWorld;     // the world the view was last set up for (a snapshot or a reset replaces it)
        int2 corner;              // world XZ (m) of the window's cell (0, 0), on a chunk's corner
        int viewLayer;            // the layer the window shows (the player's)
        byte[] seeded = new byte[Size * Size];   // each window cell's ground kind (KindNone: not filled)
        readonly bool[] known = new bool[WindowChunks * WindowChunks];   // the window's chunks whose ground is in seeded
        Texture2D map, map2;
        Color32[] texels, texels2;
        float[] heights;          // the ground (or water surface, or cave floor) at each cell's centre, for the flames and puffs
        float[] baseHeights;      // as sampled, before any height edit
        readonly ChemistryEffects effects = new ChemistryEffects();
        // Last map's state per cell (bit 0 frozen, 1 burning, 2 steam) and water, to count what changed.
        byte[] lastState;
        int[] lastWater;
        // What the chemistry did since the view was last set up, as the map saw it change (README: "reactions").
        int melted, doused, boiled, steamed, condensed;
        int shownTick = -1;
        float nextReport;
        double tickMsThisSecond, mapMsThisSecond, fxMsThisSecond;
        int framesThisSecond;
        readonly Stopwatch watch = new Stopwatch();

        // -chem-demo: a Fireball on the nearest grass a few metres from the player, once, DemoDelay s after the view is
        // first up with the player's chunk filled.
        const float DemoDelay = 8f;
        readonly bool demo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-chem-demo") >= 0;
        bool demoDone;
        float demoAt = float.MaxValue;

        // The view's height sampling in flight (a Burst job, picked up a frame or more later).
        JobHandle seedJob;
        NativeArray<float> seedHeights;
        bool seeding;
        int2 seedCorner;
        int seedLayer;
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
                return;
            }
            StartHands();
            HookLockstep();
            StartScript();
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            StopHands();
            if (seeding)
            {
                seedJob.Complete();
                seeding = false;
            }
            if (seedHeights.IsCreated)
                seedHeights.Dispose();
            effects.Dispose();
            DropSession();
            DisposeLand();
        }

        void OnDestroy()
        {
            GameBootstrap.WorldsReplacing -= OnWorldsReplacing;
            if (map != null)
                Destroy(map);
            if (map2 != null)
                Destroy(map2);
            if (disc != null)
                Destroy(disc);
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
            if (dropSession)
                DropSession();
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain)
                || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var role = CurrentRole();
            if (role == null)
                return;
            if (session != null && role != sessionRole)
                DropSession();
            if (role != LockstepRole.Client)
                NoteTerrain(terrain);
            if (session == null)
            {
                if (role == LockstepRole.Client)
                    StartClient();
                else if (TerrainSettled)
                    StartSession(role.Value);
                else
                    return;
            }
            Script(terrain, player.Position);

            // The session first, every frame, whatever the view is doing: only the host's clock paces the ticks.
            watch.Restart();
            var budget = Behind > CatchUpTicks ? CatchUpBudgetMs : TickBudgetMs;
            session.Update(Time.deltaTime, () => watch.Elapsed.TotalMilliseconds > budget);
            tickMsThisSecond += watch.Elapsed.TotalMilliseconds;

            if (seeding)
            {
                if (seedJob.IsCompleted)
                    FinishSeed();
                else
                    return;
            }
            if (role == LockstepRole.Client && !session.Synced)
                return;
            // The window follows the player (and its layer): only the view moves, the world keeps every filled chunk.
            var layer = LayerOf(terrain, player.Position);
            var at = Chunks.ChunkOf((int)math.floor(player.Position.x)) - corner.x / Chunks.Size;
            var atY = Chunks.ChunkOf((int)math.floor(player.Position.z)) - corner.y / Chunks.Size;
            if (heights == null || layer != viewLayer || !terrain.Equals(seededSource) || at < 1 || at > 2 || atY < 1 || atY > 2)
            {
                StartSeed(terrain, player.Position, layer);
                return;
            }
            if (world != viewWorld)
                NewView();

            HandleInput(terrain);
            Hands(terrain, player.Position);
            if (demo && !demoDone && demoAt == float.MaxValue && world.IsFilled(ChunkKey.OfCell(viewLayer, (int)math.floor(player.Position.x), (int)math.floor(player.Position.z))))
                demoAt = Time.unscaledTime + DemoDelay;
            if (demo && !demoDone && Time.unscaledTime >= demoAt)
                Demo(player.Position);

            if (world.Tick != shownTick && !world.TickInProgress)
            {
                watch.Restart();
                UpdateMap();
                mapMsThisSecond += watch.Elapsed.TotalMilliseconds;
                shownTick = world.Tick;
            }
            PublishLand();
            watch.Restart();
            effects.Frame(corner, Size, heights, rules.Sim.Gas.MaxAmount, Camera.main);
            fxMsThisSecond += watch.Elapsed.TotalMilliseconds;
            framesThisSecond++;
            if (Time.unscaledTime >= nextReport)
                Report();
        }

        // ------------------------------------------------------------------------------------------------ the window

        /// <summary>Samples the heights under a window round a point (the player's chunk among its middle four).</summary>
        void StartSeed(TerrainSource terrain, float3 round, int layer)
        {
            ForgetLand();   // (the window's edits come off the ground; the new window lays its own)
            viewWorld = null;
            seedCorner = new int2(Chunks.ChunkOf((int)math.floor(round.x)) - WindowChunks / 2, Chunks.ChunkOf((int)math.floor(round.z)) - WindowChunks / 2) * Chunks.Size;
            seedLayer = layer;
            seedSourcePending = terrain;
            if (!seedHeights.IsCreated)
                seedHeights = new NativeArray<float>(Size * Size, Allocator.Persistent);
            seedJob = new HeightJob { Source = terrain, Corner = seedCorner, Cave = layer != SurfaceLayer, From = round.y + 1f, Heights = seedHeights }.Schedule(Size * Size, 256);
            seeding = true;
        }

        void FinishSeed()
        {
            seedJob.Complete();
            seeding = false;
            seededSource = seedSourcePending;
            corner = seedCorner;
            viewLayer = seedLayer;
            baseHeights = seedHeights.ToArray();
            heights = (float[])baseHeights.Clone();
            Debug.Log($"[Chemistry] view: the window at ({corner.x}, {corner.y}) to ({corner.x + Size}, {corner.y + Size}) in layer {viewLayer}");
        }

        // A world or a window the view hasn't seen (a new window, a snapshot loaded, a reset): what it shows starts again.
        void NewView()
        {
            viewWorld = world;
            NewHandsWorld();
            lastState = new byte[Size * Size];
            lastWater = new int[Size * Size];
            melted = doused = boiled = steamed = condensed = 0;
            effects.Burning.Clear();
            effects.Steam.Clear();
            effects.Smoke.Clear();
            effects.Melted.Clear();
            effects.Doused.Clear();
            System.Array.Copy(baseHeights, heights, heights.Length);
            System.Array.Fill(seeded, KindNone);
            System.Array.Clear(known, 0, known.Length);
            ResetLand();
            shownTick = -1;
            EnsureMap();
            System.Array.Clear(texels, 0, texels.Length);
            System.Array.Clear(texels2, 0, texels2.Length);
            Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, 1f / Size, 1f / Size));
            // (The overlay is the surface's: in a cave the surface above mustn't show the cave's fires.)
            Shader.SetGlobalFloat(OnId, viewLayer == SurfaceLayer ? 1f : 0f);
            var drawn = new System.Collections.Generic.List<(int, int)>();
            for (int by = 0; by < WindowChunks; by++)
                for (int bx = 0; bx < WindowChunks; bx++)
                    drawn.Add(WindowKey(bx, by).Chunk);
            world.SetDrawn(drawn);
        }

        ChunkKey WindowKey(int bx, int by) => new ChunkKey(viewLayer, corner.x / Chunks.Size + bx, corner.y / Chunks.Size + by);

        /// <summary>The heights on the window's 1 m grid (at cell centres): the ground or water surface, or a cave's floor.</summary>
        [BurstCompile]
        struct HeightJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int2 Corner;
            public bool Cave;
            public float From;
            [WriteOnly] public NativeArray<float> Heights;

            public void Execute(int i)
            {
                var xz = new float2(Corner.x + i % Size + 0.5f, Corner.y + i / Size + 0.5f);
                if (Cave)
                {
                    Heights[i] = CaveFloor(Source, xz, From, out var floor) ? floor : From - 1f;
                    return;
                }
                var ground = Source.SampleGround(xz);
                Heights[i] = ground.HasWater && !ground.CaveWater && ground.WaterSurface > ground.Height + 0.1f ? ground.WaterSurface : ground.Height;
            }
        }

        // ------------------------------------------------------------------------------------------------ casting

        void HandleInput(TerrainSource terrain)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            bool fire = keyboard.hKey.wasPressedThisFrame, frost = keyboard.jKey.wasPressedThisFrame, water = keyboard.lKey.wasPressedThisFrame;
            if (!fire && !frost && !water)
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
            else if (frost)
                Cast(shift ? "sleet-storm" : "frost", hit, cam.transform.forward);
            else
                Cast(shift ? "wave" : "create-water", hit, cam.transform.forward);
        }

        /// <summary>The window cell of a kind nearest a point at least 6 m from it, or -1.</summary>
        int Nearest(float3 to, byte kind)
        {
            int px = (int)math.floor(to.x) - corner.x, py = (int)math.floor(to.z) - corner.y;
            int best = -1, bestD = int.MaxValue;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int d = (x - px) * (x - px) + (y - py) * (y - py);
                    if (seeded[y * Size + x] == kind && d >= 36 && d < bestD)
                    {
                        bestD = d;
                        best = y * Size + x;
                    }
                }
            return best;
        }

        /// <summary>-chem-demo: casts a Fireball on the grass cell nearest the player that is at least 6 m away.</summary>
        void Demo(float3 playerPos)
        {
            demoDone = true;
            CastOnNearest("fireball", KindGrass, playerPos);
        }

        bool CastOnNearest(string spell, byte kind, float3 playerPos)
        {
            var best = Nearest(playerPos, kind);
            if (best < 0)
            {
                Debug.Log($"[Chemistry] {spell}: no {KindName(kind)} in the window");
                return false;
            }
            var at = new float3(corner.x + best % Size + 0.5f, playerPos.y, corner.y + best / Size + 0.5f);
            Debug.Log($"[Chemistry] {spell} on the nearest {KindName(kind)}");
            Cast(spell, at, Vector3.forward);
            return true;
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
        /// <summary>Whether the view is built and the world ticking.</summary>
        public bool Ready => world != null && !seeding && heights != null && viewWorld == world;
        public int Burning => burningCount;
        public int Burnt => burntCount;
        public int Ice => iceCount;
        public int Frost => frostCount;
        public int LandWater => landWaterCount;
        public int SteamCells => effects.Steam.Count;
        /// <summary>Reactions seen since the view was last set up: ice melted, fires doused, water boiled, new steam, condensed.</summary>
        public (int melted, int doused, int boiled, int steamed, int condensed) Reactions => (melted, doused, boiled, steamed, condensed);
        public int Puffs => effects.PuffCount;
        /// <summary>The world point at a window cell's centre, on the ground (or the water surface).</summary>
        public Vector3 CellCentre(int x, int y) => new Vector3(corner.x + x + 0.5f, heights != null ? heights[y * Size + x] : 0f, corner.y + y + 0.5f);
        public int2 Corner => corner;
        public int ViewLayer => viewLayer;
        public bool SeededGrass(int x, int y) => seeded[y * Size + x] == KindGrass;
        public bool SeededWater(int x, int y) => seeded[y * Size + x] == KindWater;
        public int Tick => session?.NetTick ?? 0;
        /// <summary>The chunks filled so far (every layer).</summary>
        public int FilledChunks => world?.Filled.Count ?? 0;

        /// <summary>Casts a spell at a world point (the keys use the ground under the screen centre), facing a way.</summary>
        public void CastAt(string spell, Vector3 at, Vector3 facing)
        {
            if (Ready)
                Cast(spell, at, facing);
        }

        /// <summary>
        /// Whether a world cell has chemistry: a cast where its chunk isn't filled yet (the first tick or two after a teleport
        /// into new ground) is refused here, and asked again by whoever cast it.
        /// </summary>
        bool Filled(int x, int y, string what)
        {
            if (world.At(x, ChunkKey.Row(viewLayer, y)) != null)
                return true;
            Debug.Log($"[Chemistry] {what} at cell ({x}, {y}) in layer {viewLayer}: refused, no chemistry there yet (its chunk isn't filled)");
            return false;
        }

        // A cast is a request on the session's casts channel: the host stamps it, and every peer runs it on that tick.
        void Cast(string spell, float3 at, Vector3 facing)
        {
            int x = (int)math.floor(at.x), y = (int)math.floor(at.z);
            if (!Filled(x, y, spell))
                return;
            var s = rules.Spell(spell);
            int charge = 1;
            if (s.Shape == "line")
            {
                // A line goes out from its caster: start it a few cells short of the aimed cell, along the camera's
                // heading, so it runs through that cell. Charged to 3 (a line half again as long per level).
                var heading = math.normalizesafe(new float2(facing.x, facing.z), new float2(0, 1));
                int aimX = (int)math.round(heading.x * 10f), aimY = (int)math.round(heading.y * 10f);
                charge = 3;
                int fromX = x - (int)math.round(heading.x * 6f), fromY = y - (int)math.round(heading.y * 6f);
                session.Request(casts, new CastCommand(spell, fromX, ChunkKey.Row(viewLayer, fromY), Chem.World.DirOf(aimX, aimY), charge, aimX, aimY, Me));
            }
            else
            {
                // Create Water charged once more (5 x 5 instead of 3 x 3), enough to douse a stretch of the fire front.
                if (spell == "create-water")
                    charge = 2;
                session.Request(casts, new CastCommand(spell, x, ChunkKey.Row(viewLayer, y), 0, charge, 0, 0, Me));
            }
            Debug.Log($"[Chemistry] cast {spell} at ({at.x:F1}, {at.y:F1}, {at.z:F1}), cell ({x}, {y}) in layer {viewLayer}, ground {GroundName(x, y)}: asked for (tick {session.NetTick})");
        }

        static string KindName(byte k) => k == KindGrass ? "grass" : k == KindWater ? "water" : k == KindRock ? "rock" : k == KindNone ? "none" : "dirt";

        /// <summary>A world cell's ground as its chunk was filled (in the view's layer): its terrain, and water if any.</summary>
        string GroundName(int x, int y)
        {
            var g = world?.GroundOf(ChunkKey.OfCell(viewLayer, x, y));
            if (g == null)
                return "none";
            int i = ((y & (Chunks.Size - 1)) << Chunks.Shift) | (x & (Chunks.Size - 1));
            return g.WaterAt(i) > 0 ? "water" : g.TerrainAt(i);
        }

        static byte KindOf(ChunkGround g, int i)
        {
            if (g.WaterAt(i) > 0)
                return KindWater;
            var t = g.TerrainAt(i);
            return t == "grass" ? KindGrass : t == "rock" ? KindRock : KindDirt;
        }

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
            map2 = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "ChemistryMap2",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texels2 = new Color32[Size * Size];
            Shader.SetGlobalTexture(Map2Id, map2);
        }

        int burningCount, burntCount, iceCount, frostCount, landWaterCount;

        /// <summary>
        /// Renders the window's cells into the maps (ChemistryOverlay.hlsl): r burning, g burnt, b ice, a frost; and r water
        /// on land, g steam, b smoke. Each of the window's chunks that is filled is read where it lies, active or not (an
        /// inactive chunk keeps its look); one not filled yet is blank. Also lists the cells the effects draw from, and
        /// counts what changed since last time.
        /// </summary>
        void UpdateMap()
        {
            int burning = 0, burnt = 0, ice = 0, frost = 0, landWater = 0;
            var steamId = rules.Sim.WaterBoils;
            int maxGas = math.max(1, rules.Sim.Gas.MaxAmount);
            effects.Burning.Clear();
            effects.Steam.Clear();
            effects.Smoke.Clear();
            bool surface = viewLayer == SurfaceLayer;
            for (int by = 0; by < WindowChunks; by++)
                for (int bx = 0; bx < WindowChunks; bx++)
                {
                    int b = by * WindowChunks + bx;
                    if (!known[b])
                    {
                        var g = world.GroundOf(WindowKey(bx, by));
                        if (g == null)
                            continue;
                        known[b] = true;
                        for (int i = 0; i < Chunks.Area; i++)
                        {
                            int w = (by * Chunks.Size + (i >> Chunks.Shift)) * Size + bx * Chunks.Size + (i & (Chunks.Size - 1));
                            seeded[w] = KindOf(g, i);
                            SeedLand(w, g.HeightAt(i));
                        }
                    }
                    for (int ly = 0; ly < Chunks.Size; ly++)
                    {
                        int y = by * Chunks.Size + ly;
                        int row = ChunkKey.Row(viewLayer, corner.y + y);
                        for (int lx = 0; lx < Chunks.Size; lx++)
                        {
                            int x = bx * Chunks.Size + lx;
                            int i = y * Size + x;
                            var c = world.At(corner.x + x, row);
                            if (surface)
                                NoteLand(x, y, i, c.Height);
                            byte r = 0, g = 0, bl = 0, a = 0;
                            byte wr = 0, wg = 0, wb = 0;
                            if (c.Fire > 0)
                            {
                                r = (byte)math.min(255, 110 + c.Fire * 50);
                                burning++;
                                effects.Burning.Add(new ChemistryEffects.Spot { Index = i, Amount = c.Fire });
                            }
                            if (seeded[i] == KindGrass && c.T != "grass")
                            {
                                g = 255;
                                if (c.Fire <= 0)
                                    burnt++;
                            }
                            if (c.Frozen)
                            {
                                bl = 255;
                                ice++;
                            }
                            if (c.Cover == "frost")
                            {
                                a = 255;
                                frost++;
                            }
                            // Water standing on land (a pond's own water is Punchy's): over half from the first unit, so
                            // the shaders' soft edge (at 0.5) falls round every wet cell, deeper water a little more.
                            if (seeded[i] != KindWater && c.Water > 0 && !c.Frozen)
                            {
                                wr = (byte)math.min(255, 150 + c.Water * 12);
                                landWater++;
                            }
                            bool steam = false;
                            if (c.Gas != null && c.GasAmt > 0)
                            {
                                var amount = (byte)math.min(255, c.GasAmt * 255 / maxGas);
                                if (c.Gas == steamId)
                                {
                                    steam = true;
                                    wg = amount;
                                    effects.Steam.Add(new ChemistryEffects.Spot { Index = i, Amount = c.GasAmt });
                                }
                                else if (c.Gas == "smoke")
                                {
                                    wb = amount;
                                    effects.Smoke.Add(new ChemistryEffects.Spot { Index = i, Amount = c.GasAmt });
                                }
                            }
                            texels[i] = new Color32(r, g, bl, a);
                            texels2[i] = new Color32(wr, wg, wb, 0);

                            // What changed since the last map (the reactions in reactions.json, as their results show).
                            var was = lastState[i];
                            bool wasFrozen = (was & 1) != 0, wasBurning = (was & 2) != 0, wasSteam = (was & 4) != 0;
                            if (wasFrozen && !c.Frozen)
                            {
                                melted++;   // fire-ice
                                effects.Melted.Add(new ChemistryEffects.Spot { Index = i, Amount = 1 });
                            }
                            if (wasBurning && c.Fire <= 0 && (c.Water > 0 || steam || c.DampUntil > world.Tick))
                            {
                                doused++;   // water-fire
                                effects.Doused.Add(new ChemistryEffects.Spot { Index = i, Amount = 1 });
                            }
                            if (steam && !wasSteam)
                            {
                                steamed++;
                                if (c.Water < lastWater[i])
                                    boiled++;   // fire-water
                            }
                            if (wasSteam && !steam && c.Water > lastWater[i])
                                condensed++;   // cold-steam
                            lastState[i] = (byte)((c.Frozen ? 1 : 0) | (c.Fire > 0 ? 2 : 0) | (steam ? 4 : 0));
                            lastWater[i] = c.Water;
                        }
                    }
                }
            map.SetPixels32(texels);
            map.Apply(false);
            map2.SetPixels32(texels2);
            map2.Apply(false);
            burningCount = burning;
            burntCount = burnt;
            iceCount = ice;
            frostCount = frost;
            landWaterCount = landWater;
        }

        void Report()
        {
            nextReport = Time.unscaledTime + 1f;
            if (burningCount > 0 || burntCount > 0 || iceCount > 0 || frostCount > 0 || landWaterCount > 0 || effects.Steam.Count > 0 || movedCells > 0)
                Debug.Log($"[Chemistry] tick {session.NetTick}: {burningCount} burning, {burntCount} burnt, {iceCount} ice, {frostCount} frost, " +
                          $"{landWaterCount} water on land, {effects.Steam.Count} steam, {effects.Smoke.Count} smoke, {movedCells} moved; {effects.PuffCount} puffs; " +
                          $"so far {melted} melted, {doused} doused, {boiled} boiled, {steamed} steamed, {condensed} condensed; " +
                          $"behind {Behind}; bridge {((tickMsThisSecond + mapMsThisSecond + fxMsThisSecond + landMsThisSecond) / math.max(1, framesThisSecond)):F2} ms a frame " +
                          $"(ticking {tickMsThisSecond:F1} ms, map {mapMsThisSecond:F1} ms, effects {fxMsThisSecond:F1} ms, ground {landMsThisSecond:F1} ms this second, {framesThisSecond} frames)");
            tickMsThisSecond = mapMsThisSecond = fxMsThisSecond = landMsThisSecond = 0;
            framesThisSecond = 0;
        }
    }
}
