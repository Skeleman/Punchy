using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Two still stone giants (#40, after the Dunes colossus): a kneeling titan, head bowed over a planted sword, in the
    /// biggest cold region (Tundra, Glacier or Mountains), snow lying on everything that faces up; and a half-buried head,
    /// tilted, crown broken, in the biggest green region (Grassland or Temperate), moss on its brow and rubble round it.
    /// Built in code from the colossus mesh builder and placed like DunesColossus, from the terrain and world seed only
    /// (a seeded pick among the most inland dry cells of the region, kept 250 m off settlements), so they add no landmark
    /// data and move nothing else. "-nocolossus" hides them with the Dunes colossus.
    /// </summary>
    /// <remarks>
    /// A-81 row 1 (design note): each colossus is a <see cref="ColossusDefinition"/> asset in Resources/Colossi (biomes,
    /// salt, keep radius, far-shadow cast, rig, flora keep-out, collision parts) plus its builder, a partial file of this
    /// class (StoneColossi.&lt;Kind&gt;.cs) that registers itself with <see cref="Register"/>. This core only loads the
    /// definitions, picks each one's spot and calls its builder. A 14th colossus is: one word appended to <see cref="Kind"/>
    /// (assets store the number, so never reorder it), a new StoneColossi.&lt;Kind&gt;.cs with its builder, and a new
    /// definition asset with its own new salt. Nothing else moves, as each spot draws only from its own salt. If it walks,
    /// it also needs a Gait line in <see cref="ColossusWalker"/>.
    /// </remarks>
    public partial class StoneColossi : MonoBehaviour
    {
        /// <summary>
        /// W-86 added a basalt smith hunched over his anvil (Ashlands, Volcano or EmberForest). W-78 added a moss-backed giant
        /// tortoise (FungalForest) and a crystal-spined serpent (ElectricWastelands). W-88 (#40, Zach's designs) put a giant
        /// carnivorous plant in the PoisonSwamp and a skeletal lizard shedding ash in the Ashlands in the last two slots; the
        /// drowned knight and the smith are kept whole for later and come back with "-colossus DrownedKnight" / "-colossus AshSmith".
        /// </summary>
        // W-89 (#40, Zach's designs): a golem of floating rocks held together by arcs takes the ElectricWastelands slot
        // (the crystal serpent comes back with "-colossus CrystalSerpent"), and a seventh slot holds a walking saguaro in
        // the Sonoran.
        public enum Kind { KneelingTitan, HalfBuriedHead, GiantTortoise, CrystalSerpent, DrownedKnight, AshSmith, CarnivorousPlant, SkeletalLizard, RockGolem, WalkingSaguaro, SaltCrystal, MesaTortoise, GlassHeron, LanternStag, CinderSnail, MireWalker, SnowWorm, WalkingOak, Puffball, CharredTreant, IceMammoth, SkyWhale, HermitCrab, MagmaSerpent }
        // W-92 (#40, Zach's SaltFlat design): an eighth slot holds a giant rounded salt crystal rolling in slow circles.
        // W-106 row 2 (#40): a ninth slot holds a rock tortoise in the MesaCanyonlands that reads as a small mesa.
        // A-76 (#40): a tenth holds a glass heron wading the GlassDesert, an eleventh a lantern stag in the SparklingForest.
        // A-78 (#40): a twelfth holds a cinder snail grazing the EmberForest, the biggest biome that had none.
        // A-79 (#40): a thirteenth holds a mire walker creeping through the FoggyMarsh on its prop roots.
        // A-87 row 4 (#40): a hermit crab carrying an islet on its shell scuttles the Ocean shallows, the last biome without one.
        /// <summary>What stands in each slot (one per primary <see cref="ColossusDefinition"/>, by its Order). A "-colossus"
        /// flag swaps a kept kind into the slot of the kind it replaces. Sized when the colossi are placed.</summary>
        public static Kind[] Placed = new Kind[0];

        /// <summary>Where each slot's colossus stands (NaN until placed), for probes and shot lists.</summary>
        public static float3[] Positions = new float3[0];

        /// <summary>A-72 hook: the seeded spot each slot was placed on (NaN until placed). <see cref="Positions"/> follows
        /// a colossus that walks (<see cref="ColossusWalker"/>); this keeps the spot its loop is centred on.</summary>
        public static float3[] Spots = new float3[0];

        /// <summary>A-72 hook: the root a slot's colossus was built under (null until placed), for <see cref="ColossusWalker"/>.</summary>
        public Transform Root(int slot) => slot < roots.Length ? roots[slot] : null;

        /// <summary>The body rig a slot's colossus gets while it walks (from its definition).</summary>
        public static ColossusRigType RigOf(int slot) => slot < defs.Length ? defs[slot].Rig : ColossusRigType.None;

        /// <summary>Builds one colossus at <paramref name="at"/>; <paramref name="seed"/> is the world seed xor its salt.</summary>
        public delegate Transform Builder(in TerrainSource source, float3 at, float yaw, uint seed);
        static Dictionary<Kind, Builder> builders;

        /// <summary>Called once by each builder's partial file (from a static field) to hand its builder over.</summary>
        static bool Register(Kind kind, Builder build)
        {
            builders ??= new Dictionary<Kind, Builder>();
            builders[kind] = build;
            return true;
        }

        /// <summary>The definition standing in each slot (a kept one when a "-colossus" flag swapped it in).</summary>
        static ColossusDefinition[] defs = new ColossusDefinition[0];

        const float Step = 150f;
        /// <summary>
        /// W-89 (#40): the most relief (highest minus lowest ground, m) a colossus spot may have across its keep circle.
        /// A spot steeper than this is redrawn (up to eight times, then the flattest draw wins), so no colossus stands
        /// on a hillside with its feet hanging over the drop. A spot already flat enough keeps its first draw.
        /// </summary>
        const float MaxRelief = 6f;

        static readonly Color Stone = new Color(0.50f, 0.50f, 0.49f);
        static readonly Color Dark = new Color(0.36f, 0.36f, 0.36f);
        static readonly Color Hollow = new Color(0.10f, 0.10f, 0.11f);
        static readonly Color Rust = new Color(0.40f, 0.30f, 0.24f);
        static readonly Color Snow = new Color(0.90f, 0.92f, 0.95f);
        static readonly Color Moss = new Color(0.30f, 0.42f, 0.18f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindAnyObjectByType<StoneColossi>() != null)
                return;
            new GameObject("StoneColossi").AddComponent<StoneColossi>();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        Transform[] roots = new Transform[0];
        bool placed;
        float nextTry;

        void Update()
        {
            if (building)
                BuildNext();
            if (!placed)
            {
                if (surveyPending)
                    FinishSurvey();
                else if (Time.realtimeSinceStartup >= nextTry)
                {
                    nextTry = Time.realtimeSinceStartup + 1f;
                    TryPlace();
                }
                return;
            }
            foreach (var r in roots)
                if (r != null && r.gameObject.activeSelf == DunesColossus.Hidden)
                    r.gameObject.SetActive(!DunesColossus.Hidden);
            GlowLanterns();
            SwayPlant();
            SwayWorm();
            SwayOak();
            SwayPuffball();
            SwayTreant();
            SwayMammoth();
            SwaySkyWhale();
            SwayMagmaSerpent();
        }

        void OnDestroy()
        {
            DropSurvey();
        }

        void TryPlace()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!terrainQuery.TryGetSingleton(out TerrainSource source) || paletteQuery.CalculateEntityCount() != 1 || !source.Settlements.IsCreated)
                return;
            // #40 (W-59d): on island v2, wait for the launch bake. The editor's subscene holds a v1 bake (settlements
            // and all) for a few frames before IslandV2System swaps the v2 island in, and placing against that put the
            // editor's colossi on other spots (the head 26 m under the v2 ground). The player only ever saw v2.
            if (IslandV2.Requested() && !source.Island.IsCreated)
                return;

            // A-83 row 3: the biome survey runs on workers and the colossi are placed once it is done, a frame or
            // more later (FinishSurvey), not in this frame.
            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            surveyGrid = new NativeArray<int>(n * n, Allocator.Persistent);
            surveying = new SurveyJob { Source = source, Origin = -half, Step = Step, Size = n, Biome = surveyGrid }.Schedule(n * n, 64);
            surveySource = source;
            surveyWorld = world;
            surveyFrame = Time.frameCount;
            surveyPending = true;
            // The job reads the TerrainSource blobs, which quitting, leaving play mode or a world swap frees before
            // OnDestroy: finish it first, while they are intact (as CaveSystem does since 156e7df).
            Application.quitting += DropSurvey;
            Punchy.Net.GameBootstrap.WorldsReplacing += DropSurvey;
        }

        // A-83 row 3: the placement survey in flight, and the world and terrain it reads.
        JobHandle surveying;
        NativeArray<int> surveyGrid;
        TerrainSource surveySource;
        Unity.Entities.World surveyWorld;
        bool surveyPending;
        int surveyFrame;

        /// <summary>Once the survey is done, places the colossi from it. If the terrain is gone or changed while it
        /// runs, the survey is finished and dropped at once and placing tries again.</summary>
        void FinishSurvey()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || world != surveyWorld)
            {
                DropSurvey();
                return;
            }
            var em = world.EntityManager;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!terrainQuery.TryGetSingleton(out TerrainSource source) || !source.Equals(surveySource))
            {
                DropSurvey();
                return;
            }
            if (!surveying.IsCompleted)
                return;
            surveying.Complete();
            var biomes = surveyGrid.ToArray();
            DropSurvey();
            using var paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (paletteQuery.CalculateEntityCount() != 1)
                return;
            Debug.Log($"StoneColossi: survey took {Time.frameCount - surveyFrame} frames");
            Place(source, em.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity()), biomes);
        }

        /// <summary>Waits for a survey still in flight and frees its grid (finished, stale, quitting, world swap or
        /// teardown).</summary>
        void DropSurvey()
        {
            Application.quitting -= DropSurvey;
            Punchy.Net.GameBootstrap.WorldsReplacing -= DropSurvey;
            if (!surveyPending)
                return;
            surveying.Complete();
            surveyGrid.Dispose();
            surveyPending = false;
            surveyWorld = null;
        }

        // A-84 row 1: the build in progress. Place picks every slot; BuildNext builds one slot a frame and makes it
        // solid on the next, so the 14 builds (JIT, meshes, particle setup, collision sampling, and the garbage
        // collection they set off) no longer land in one 450 ms frame. Each slot still draws only from its own salt,
        // so the spots are the same.
        bool building;
        bool solidNext;
        int nextSlot;
        TerrainSource buildSource;
        Unity.Entities.World buildWorld;
        readonly System.Diagnostics.Stopwatch buildWatch = new System.Diagnostics.Stopwatch();
        /// <summary>A-85 row 1: the second half of the colossus just built, if its builder split itself in two
        /// (BuildNext runs it on the next frame, before the solid frame). Cleared when placing starts over.</summary>
        static System.Action BuildRest;

        void Place(TerrainSource source, BiomePalette palette, int[] biomes)
        {
            placed = true;
            BuildRest = null;
            Lanterns.Clear();
            LoadDefinitions();
            int slots = defs.Length;
            foreach (var r in roots)
                if (r != null)
                    Destroy(r.gameObject);
            roots = new Transform[slots];
            Placed = new Kind[slots];
            Positions = new float3[slots];
            Spots = new float3[slots];
            for (int slot = 0; slot < slots; slot++)
            {
                Placed[slot] = defs[slot].Kind;
                Positions[slot] = Spots[slot] = new float3(float.NaN);
            }
            pickSpot = new float2[slots];
            pickYaw = new float[slots];
            pickCells = new int[slots];
            buildWatch.Restart();
            for (int slot = 0; slot < slots; slot++)
                PickSlot(slot, source, palette, biomes);
            Debug.Log($"[colossi] picked {slots} slots in {buildWatch.Elapsed.TotalMilliseconds:F1} ms on frame {Time.frameCount}");
            buildSource = source;
            buildWorld = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            nextSlot = 0;
            solidNext = false;
            building = slots > 0;
        }

        /// <summary>
        /// A-84 row 1: builds the next picked slot's colossus on one frame and makes it solid on the next (the two
        /// halves cost about the same for the big ones). If the world or its terrain changed
        /// since <see cref="Place"/>, the build stops and placing starts over (Place clears what was built).
        /// </summary>
        void BuildNext()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || world != buildWorld || !terrainStill(world))
            {
                building = false;
                placed = false;
                BuildRest = null;
                return;
            }
            buildWatch.Restart();
            // A-85 row 1: a builder too big for one frame leaves its second half in BuildRest, run on a frame of
            // its own between the build and the solid frame.
            if (BuildRest != null)
            {
                var rest = BuildRest;
                BuildRest = null;
                rest();
                Debug.Log($"[colossi] slot {nextSlot - 1} ({defs[nextSlot - 1].Kind}) finished in {buildWatch.Elapsed.TotalMilliseconds:F1} ms on frame {Time.frameCount}");
                return;
            }
            var slot = solidNext ? nextSlot - 1 : nextSlot++;
            if (solidNext)
                SolidSlot(slot, buildSource);
            else
                BuildSlot(slot, buildSource);
            Debug.Log($"[colossi] slot {slot} ({defs[slot].Kind}) {(solidNext ? "made solid" : "built")} in {buildWatch.Elapsed.TotalMilliseconds:F1} ms on frame {Time.frameCount}");
            solidNext = !solidNext;
            if (!solidNext && nextSlot >= defs.Length)
                building = false;

            bool terrainStill(Unity.Entities.World w)
            {
                using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                return q.TryGetSingleton(out TerrainSource now) && now.Equals(buildSource);
            }
        }

        // A-84 row 1: each slot's pick, made in Place (so the flora keep-outs and their one replant come at once, as
        // before), and built later by BuildNext.
        float2[] pickSpot = new float2[0];
        float[] pickYaw = new float[0];
        int[] pickCells = new int[0];

        /// <summary>Picks a slot's spot and sets its keep-out (false when there is no room or no builder).</summary>
        bool PickSlot(int slot, in TerrainSource source, BiomePalette palette, int[] biomes)
        {
            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            var def = defs[slot];
            ColossusCollision.Clear(slot);
            var wanted = new HashSet<int>();
            for (int b = 0; b < palette.Biomes.Length; b++)
                if (palette.Biomes[b] != null)
                    foreach (var name in def.Biomes)
                        if (palette.Biomes[b].name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase))
                            wanted.Add(b);
            // A-85: a kind that shares its biome with an earlier one (KeepApart > 0) keeps clear of the colossi already placed.
            var others = new List<float2>();
            for (int s = 0; s < slot && def.KeepApart > 0f; s++)
                if (!math.any(math.isnan(Spots[s])))
                    others.Add(Spots[s].xz);
            float2 spot;
            float yaw;
            int cells;
            // A-88 row 3: a kind that lives on a landmark (the magma serpent in a volcano's crater) stands on the middle of
            // one of that landmark's instances, picked by its salt; with none in this world it falls back to its biomes.
            if (!string.IsNullOrEmpty(def.Landmark) && PickLandmark(source, palette, def, out spot, out yaw))
                cells = 0;
            else if (!Pick(source, biomes, n, half, wanted, def.Salt, def.KeepRadius * 0.7f, def.KeepApart, others, out spot, out yaw, out cells))
            {
                Debug.Log($"StoneColossi: no room for a {def.Kind} in this world, none placed");
                return false;
            }
            if (builders == null || !builders.ContainsKey(def.Kind))
            {
                Debug.LogWarning($"StoneColossi: no builder registered for {def.Kind}, none placed");
                return false;
            }
            pickSpot[slot] = spot;
            pickYaw[slot] = yaw;
            pickCells[slot] = cells;
            var ground = source.SampleHeight(spot);
            Positions[slot] = new float3(spot.x, ground, spot.y);
            Spots[slot] = Positions[slot];
            // Keep flora out of a circle round it and re-place the streamed tiles, so no tree grows through it (#40).
            // The head's circle is wide enough (W-59d) to leave a clear sight line from about 40 m away.
            if (def.FloraKeepOut)
            {
                FloraPlacement.SetKeepOut(slot, new float4(spot.x, spot.y, def.KeepRadius, 1f));
                FloraSystem.Replant = true;
            }
            return true;
        }

        /// <summary>A-88 row 3: picks one instance of the definition's landmark (by its salt) and stands on its middle.</summary>
        static bool PickLandmark(in TerrainSource source, BiomePalette palette, ColossusDefinition def, out float2 spot, out float yaw)
        {
            spot = default;
            yaw = 0f;
            if (!source.Landmarks.IsCreated || palette.Landmarks == null)
                return false;
            ref var set = ref source.Landmarks.Value;
            var found = new List<int>();
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var t = set.Instances[i].Type;
                if (t >= 0 && t < palette.Landmarks.Length && palette.Landmarks[t] != null && palette.Landmarks[t].name == def.Landmark)
                    found.Add(i);
            }
            if (found.Count == 0)
                return false;
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)source.World.Seed ^ def.Salt);
            spot = set.Instances[found[rng.NextInt(found.Count)]].Position;
            yaw = rng.NextFloat(360f);
            return true;
        }

        /// <summary>Builds a picked slot's colossus.</summary>
        void BuildSlot(int slot, in TerrainSource source)
        {
            if (math.any(math.isnan(Spots[slot])))
                return;
            var def = defs[slot];
            roots[slot] = builders[def.Kind](source, Positions[slot], pickYaw[slot], (uint)source.World.Seed ^ def.Salt);
        }

        /// <summary>Makes a built slot's colossus solid, the frame after its build.</summary>
        void SolidSlot(int slot, in TerrainSource source)
        {
            if (math.any(math.isnan(Spots[slot])) || roots[slot] == null)
                return;
            var def = defs[slot];
            var spot = pickSpot[slot];
            // W-101 row 4 (#40): solid to the player and the creatures. A part named in the definition can follow
            // the colossus as it moves (the salt ball's roll) or be left out (crumbs, the heron's shard litter).
            var follow = string.IsNullOrEmpty(def.CollisionFollow) ? null : roots[slot].Find(def.CollisionFollow);
            var leave = string.IsNullOrEmpty(def.CollisionLeave) ? new string[0] : def.CollisionLeave.Split(',');
            ColossusCollision.Register(slot, roots[slot], def.Kind.ToString(), follow, def.CollisionFollowRadius, leave);
            // R-6 row 2: far levels by the size-on-screen rule, built on workers from the meshes just made.
            ColossusLod.Attach(roots[slot], def.Kind.ToString(), GlowMaterial);
            Debug.Log($"StoneColossi: {def.Kind} placed at ({spot.x:F0}, {Positions[slot].y:F1}, {spot.y:F0}), facing {pickYaw[slot]:F0} degrees, region {pickCells[slot]} cells, relief {Relief(source, spot, def.KeepRadius * 0.7f):F1} m");
        }

        /// <summary>
        /// Loads the colossus definitions: each primary takes a slot by its Order, and "-colossus &lt;Kind&gt;" swaps a
        /// kept kind into the slot of the kind it replaces (so it stands on that slot's spot with that slot's salt).
        /// </summary>
        static void LoadDefinitions()
        {
            var all = Resources.LoadAll<ColossusDefinition>("Colossi");
            var primaries = new List<ColossusDefinition>();
            foreach (var d in all)
                if (d.Primary)
                    primaries.Add(d);
            primaries.Sort((a, b) => a.Order.CompareTo(b.Order));
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-colossus" && System.Enum.TryParse<Kind>(args[i + 1], true, out var swap))
                    foreach (var d in all)
                        if (!d.Primary && d.Kind == swap)
                            for (int s = 0; s < primaries.Count; s++)
                                if (primaries[s].Kind == d.Replaces)
                                    primaries[s] = d;
            defs = primaries.ToArray();
        }

        /// <summary>The biggest 4-connected region of <paramref name="wanted"/> cells, then a seeded pick among its most inland cells clear of settlements.</summary>
        static bool Pick(in TerrainSource source, int[] grid, int n, float half, HashSet<int> wanted, uint salt, float radius, float keepApart, List<float2> others, out float2 spot, out float yaw, out int cells)
        {
            spot = default; yaw = 0f; cells = 0;
            var region = new int[n * n];
            var sizes = new List<int> { 0 };
            var queue = new Queue<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (!wanted.Contains(grid[i]) || region[i] != 0)
                    continue;
                var id = sizes.Count;
                sizes.Add(0);
                region[i] = id;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    sizes[id]++;
                    int x = c % n, z = c / n;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n)
                            continue;
                        var j = nz * n + nx;
                        if (wanted.Contains(grid[j]) && region[j] == 0) { region[j] = id; queue.Enqueue(j); }
                    }
                }
            }
            var biggest = 0;
            for (int id = 1; id < sizes.Count; id++)
                if (sizes[id] > sizes[biggest]) biggest = id;
            if (biggest == 0)
                return false;
            cells = sizes[biggest];

            ref var towns = ref source.Settlements.Value;
            int At(int x, int z) => x < 0 || z < 0 || x >= n || z >= n ? 0 : region[z * n + x];
            var scores = new int[n * n];
            var best = -1;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (At(x, z) != biggest)
                    continue;
                var p = new float2(-half + x * Step, -half + z * Step);
                var clear = true;
                for (int s = 0; s < towns.Settlements.Length && clear; s++)
                    clear = math.distance(p, towns.Settlements[s].Centre) > towns.Settlements[s].Radius + 250f;
                if (!clear)
                {
                    scores[z * n + x] = -1;
                    continue;
                }
                var score = 0;
                for (int r = 1; r <= 12; r++, score++)
                {
                    var d = (int)math.round(r * 0.707f);
                    if (At(x + r, z) != biggest || At(x - r, z) != biggest || At(x, z + r) != biggest || At(x, z - r) != biggest ||
                        At(x + d, z + d) != biggest || At(x - d, z + d) != biggest || At(x + d, z - d) != biggest || At(x - d, z - d) != biggest)
                        break;
                }
                scores[z * n + x] = score;
                best = math.max(best, score);
            }
            if (best < 0)
                return false;
            var candidates = new List<int>();
            for (int i = 0; i < scores.Length; i++)
                if (At(i % n, i / n) == biggest && scores[i] >= best - 1)
                    candidates.Add(i);
            // A-85 (KeepApart): drop the cells near an earlier colossus, reaching further from the inland core until some
            // remain (none: the plain candidates stand). 0 for the kinds placed before it, so their spots never move.
            for (int floor = best - 1; keepApart > 0f && others.Count > 0 && floor >= 0; floor--)
            {
                var apart = new List<int>();
                for (int i = 0; i < scores.Length; i++)
                {
                    if (At(i % n, i / n) != biggest || scores[i] < floor)
                        continue;
                    var p = new float2(-half + i % n * Step, -half + i / n * Step);
                    var clear = true;
                    foreach (var o in others)
                        clear &= math.distance(p, o) > keepApart;
                    if (clear)
                        apart.Add(i);
                }
                if (apart.Count > 0)
                {
                    candidates = apart;
                    break;
                }
            }
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)source.World.Seed ^ salt);
            // The first draw is the pre-W-89 spot; only a steep one draws again (MaxRelief).
            var flattest = float.MaxValue;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var pick = candidates[rng.NextInt(candidates.Count)];
                var at = new float2(-half + pick % n * Step, -half + pick / n * Step) + rng.NextFloat2(-Step * 0.3f, Step * 0.3f);
                var relief = Relief(source, at, radius);
                if (relief < flattest) { flattest = relief; spot = at; }
                if (relief <= MaxRelief)
                    break;
            }
            yaw = rng.NextFloat(0f, 360f);
            return true;
        }

        /// <summary>Highest minus lowest ground at the centre and twelve points round a circle of <paramref name="radius"/>.</summary>
        static float Relief(in TerrainSource source, float2 at, float radius)
        {
            float lo = source.SampleHeight(at), hi = lo;
            for (int k = 0; k < 12; k++)
            {
                var a = k * math.PI / 6f;
                var h = source.SampleHeight(at + new float2(math.cos(a), math.sin(a)) * radius);
                lo = math.min(lo, h); hi = math.max(hi, h);
            }
            return hi - lo;
        }

        [BurstCompile]
        struct SurveyJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Step;
            public int Size;
            [WriteOnly] public NativeArray<int> Biome;

            public void Execute(int i)
            {
                var g = Source.SampleGround(new float2(Origin + i % Size * Step, Origin + i / Size * Step));
                Biome[i] = g.WaterDepth < -1f ? g.Biomes.Dominant() : -1;
            }
        }

        // ---------------------------------------------------------------- building


        /// <summary>Lanterns' flames are drawn within this far of the camera (m); their light is LightBudget's to decide
        /// (refactor 3.1: it was cut at this distance too). The charred treant's embers and the puffball's pores skip
        /// their whole glow past it, flames and light together.</summary>
        const float LanternFlameRange = 450f;

        /// <summary>
        /// A-76 row 2: each frame the stag's lanterns burn as small standard-fire flames and throw a warm light round them
        /// (a creature light request to LightBudget, which lights the ground and trees too), faint by day and brightening into the night
        /// (DayNightCycle.Night). Cosmetic and local.
        /// </summary>
        void GlowLanterns()
        {
            if (Lanterns.Count == 0 || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var night = cycle != null ? cycle.Night : 0f;
            var eye = cam.transform.position;
            for (int k = 0; k < Lanterns.Count; k++)
            {
                var point = Lanterns[k];
                if (point == null || !point.gameObject.activeInHierarchy)
                    continue;
                var flicker = 0.9f + 0.1f * Mathf.Sin(Time.time * 7f + k * 1.7f);
                // A-87 row 4: a point's scale sizes its flame and light (1 for a lantern; the hermit crab's lighthouse is a beacon).
                var scale = point.localScale.x;
                if ((point.position - eye).sqrMagnitude <= LanternFlameRange * LanternFlameRange)
                    Punchy.Ambience.FireSystem.AddFlame(point.position, (0.3f + 0.25f * night) * scale, 0x57A60 + k, Punchy.Ambience.FireSystem.Wood);
                var light = Punchy.Ambience.FireSystem.Light(Punchy.Ambience.FireSystem.Wood, flicker * (0.35f + 1.4f * night) * scale);
                light.w *= 0.6f + 0.8f * night;
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Creature, 0x57A60 + k, point.position, light);
            }
        }

        /// <summary>Slow, soft puffs rising from a box round <paramref name="local"/> (ash, dust, spores), in the ambience puff material.</summary>
        static void Puffs(Transform parent, string name, Vector3 local, Vector3 box, Color a, Color b, float sizeMin, float sizeMax, float rate, float rise, float life)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(rise, rise * 1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.5f;
            noise.frequency = 0.15f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            if (ambience != null && ambience.PuffMaterial != null)
                renderer.sharedMaterial = ambience.PuffMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Simulate(life, true, true);
            ps.Play();
        }

        /// <summary>Snow or moss on every face turned up past <paramref name="from"/> (full at <paramref name="full"/>).</summary>
        static void Weather(Mesh mesh, Color cover, float from, float full)
        {
            var normals = mesh.normals;
            var colors = mesh.colors;
            for (int i = 0; i < colors.Length; i++)
            {
                var t = Mathf.InverseLerp(from, full, normals[i].y);
                if (t > 0f)
                {
                    var glow = colors[i].a;   // (A-89: keep the glow mark; cover is opaque)
                    colors[i] = Color.Lerp(colors[i], cover * (0.94f + 0.06f * (i % 3)), Mathf.Min(1f, t * 1.6f));
                    colors[i].a = Mathf.Lerp(glow, 1f, Mathf.Min(1f, t * 1.6f));
                }
            }
            mesh.SetColors(colors);
        }

        static void AddPart(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GlowMaterial;
        }

        static Material glowMaterial;

        /// <summary>A-89: the Colossus material with vertex glow on (alpha below 1 lights up, see MeshBuilder.Glowing).
        /// A copy, so the ruins and driftwood that share Colossus keep their alpha for other things.</summary>
        internal static Material GlowMaterial
        {
            get
            {
                if (glowMaterial != null)
                    return glowMaterial;
                var colossus = Resources.Load<Material>("Colossus");
                if (colossus == null)
                    return null;
                glowMaterial = new Material(colossus) { name = "ColossusGlow" };
                glowMaterial.SetFloat("_VertexGlow", 1f);
                return glowMaterial;
            }
        }
    }
}
