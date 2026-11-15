using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>One fire creatures know about: where it burns, how far its flames reach and how much heat it gives.</summary>
    public struct FireSpot
    {
        public float3 Position;
        /// <summary>The flames' reach (m): a walker's body stays out of this circle.</summary>
        public float Radius;
        /// <summary>How hot it burns, a torch about 0.3, a campfire about 1, a forest fire or lava 1.5 and up.</summary>
        public float Heat;
        public byte Flags;
    }

    /// <summary>
    /// A-121: every fire the brain steers round, in 16 m cells so a creature looks only at the few by it (rule 24's
    /// per-area lists, not a scan of every flame per brain). Landmark flames (camp, hut, town and hold fires: they
    /// stay pits when unlit, so they always count) and, at night near a player, the burning roots and litter of a
    /// burning forest. Built by FireSpotSystem; the containers belong to it and the brain only reads them.
    /// </summary>
    public struct FireSpots : IComponentData
    {
        /// <summary>Lit only at night (a camp's fire): heat then, a cold pit by day.</summary>
        public const byte NightOnly = 1;
        /// <summary>On the ground, where walkers go: an obstacle (a beacon on its deck is heat only).</summary>
        public const byte Ground = 2;
        /// <summary>A burning forest's flame, there only while it burns.</summary>
        public const byte Forest = 4;
        public const float CellSize = 16f;
        /// <summary>How much more than its own radius each spot is listed round, so a padded test (a body's radius)
        /// still finds it from the next cell.</summary>
        public const float ListedPad = 6f;

        [ReadOnly] public NativeArray<FireSpot> Spots;
        [ReadOnly] public NativeParallelMultiHashMap<int2, int> Cells;
        public bool On;

        public static int2 CellOf(float2 xz) => (int2)math.floor(xz / CellSize);

        /// <summary>
        /// The ground fire whose circle, widened by pad (at most ListedPad), holds xz: the one it is deepest in. False
        /// when there is none (or the list is off).
        /// </summary>
        public bool Near(float2 xz, float pad, out FireSpot fire)
        {
            fire = default;
            if (!On || !Cells.IsCreated || !Cells.TryGetFirstValue(CellOf(xz), out var i, out var it))
                return false;
            var found = false;
            var deepest = 0f;
            do
            {
                var f = Spots[i];
                if ((f.Flags & Ground) == 0)
                    continue;
                var reach = f.Radius + math.min(pad, ListedPad);
                var depth = reach - math.distance(f.Position.xz, xz);
                if (depth > deepest)
                {
                    deepest = depth;
                    fire = f;
                    found = true;
                }
            } while (Cells.TryGetNextValue(out i, ref it));
            return found;
        }
    }

    /// <summary>
    /// A-121: keeps the FireSpots list. Landmark fires are listed once per baked world (they never move), burning
    /// forest flames every two seconds at night round each player (FloraSystem only streams trees near players), and
    /// the list is rebuilt only when what it holds changed. Runs before the brain, in the brain's worlds.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CreatureBrainSystem))]
    public partial class FireSpotSystem : SystemBase
    {
        /// <summary>Debug switch for before/after checks: off, the brain steers round no fires (the list is still kept).</summary>
        public static bool On = true;
        /// <summary>How many spots the last build listed: [all, landmark, forest] (for probes).</summary>
        public static int3 LastCounts;
        const float ForestRange = 60f, ForestEvery = 2f;

        NativeArray<FireSpot> spots;
        NativeParallelMultiHashMap<int2, int> cells;
        readonly List<FireSpot> fixedSpots = new List<FireSpot>();
        readonly List<FireSpot> forestSpots = new List<FireSpot>();
        readonly List<FireSpot> gathered = new List<FireSpot>();
        BlobAssetReference<LandmarkSetBlob> fixedFrom;
        BiomePalette fixedPalette;
        BiomePalette burningFrom;
        readonly HashSet<FloraSpecies> burningSpecies = new HashSet<FloraSpecies>();
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> trees = new List<(float3, float3, float, float, int)>();
        FloraSystem flora;
        double nextForest;
        bool wasOn = true;
        EntityQuery paletteQuery;
        World paletteWorld;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
            spots = new NativeArray<FireSpot>(0, Allocator.Persistent);
            cells = new NativeParallelMultiHashMap<int2, int>(0, Allocator.Persistent);
            EntityManager.CreateSingleton(new FireSpots { Spots = spots, Cells = cells, On = false });
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteDependencyBeforeRW<CreatureState>();
            if (spots.IsCreated)
                spots.Dispose();
            if (cells.IsCreated)
                cells.Dispose();
        }

        protected override void OnUpdate()
        {
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            var changed = wasOn != On;
            wasOn = On;
            var palette = Palette();
            if (palette != null && (terrain.Landmarks != fixedFrom || palette != fixedPalette))
            {
                fixedFrom = terrain.Landmarks;
                fixedPalette = palette;
                ListLandmarkFires(terrain, palette);
                changed = true;
            }
            if (SystemAPI.Time.ElapsedTime >= nextForest)
            {
                nextForest = SystemAPI.Time.ElapsedTime + ForestEvery;
                var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
                gathered.Clear();
                if (palette != null && math.saturate((night - 0.2f) / 0.3f) > 0.01f)
                    GatherForest(terrain, palette);
                if (!Same(gathered, forestSpots))
                {
                    forestSpots.Clear();
                    forestSpots.AddRange(gathered);
                    changed = true;
                }
            }
            if (changed)
                Rebuild();
        }

        /// <summary>The landmark types, from the palette the terrain baked (in the default world: managed systems all
        /// read it there).</summary>
        BiomePalette Palette()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                world = World;
            if (paletteWorld != world)
            {
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                paletteWorld = world;
            }
            if (paletteQuery.CalculateEntityCount() != 1)
                return null;
            return world.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
        }

        /// <summary>Every landmark's Flame effect (camps, huts, villages, holds, mining and goblin camps), where
        /// LandmarkEffectSystem draws it, sized from the flames' spread and height.</summary>
        void ListLandmarkFires(in TerrainSource terrain, BiomePalette palette)
        {
            fixedSpots.Clear();
            if (!terrain.Landmarks.IsCreated || palette.Landmarks == null)
                return;
            ref var set = ref terrain.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                if (inst.Type < 0 || inst.Type >= palette.Landmarks.Length || palette.Landmarks[inst.Type] == null)
                    continue;
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                foreach (var effect in palette.Landmarks[inst.Type].Effects)
                {
                    if (effect == null || effect.Kind != LandmarkEffectKind.Flame)
                        continue;
                    var at = LandmarkSampling.ToWorld(inst, effect.Offset, radius);
                    var ground = terrain.SampleGround(at);
                    var y = ground.HasWater ? math.max(ground.Height, ground.WaterSurface) : ground.Height;
                    fixedSpots.Add(new FireSpot
                    {
                        Position = new float3(at.x, y, at.y),
                        Radius = math.max(effect.Width * inst.Scale * 0.5f + effect.Size * inst.Scale * 0.4f, 0.6f),
                        Heat = effect.Size * inst.Scale / 0.8f,
                        Flags = (byte)(FireSpots.Ground | (effect.NightOnly ? FireSpots.NightOnly : 0))
                    });
                }
            }
        }

        /// <summary>
        /// The burning forest's ground flames round each player, the same ones LandmarkEffectSystem draws (its seed
        /// rules, copied): burning roots at some trees' feet and a patch of burning litter a few metres off others.
        /// The crown flames are up in the trees, out of the way.
        /// </summary>
        void GatherForest(in TerrainSource terrain, BiomePalette palette)
        {
            if (burningFrom != palette)
            {
                burningFrom = palette;
                burningSpecies.Clear();
                if (palette.Biomes != null)
                    foreach (var biome in palette.Biomes)
                        if (biome != null && biome.HasTag(BiomeTags.Burning))
                            foreach (var f in biome.Flora)
                                if (f.Species != null)
                                    burningSpecies.Add(f.Species);
            }
            if (burningSpecies.Count == 0)
                return;
            if (flora == null)
                flora = Object.FindAnyObjectByType<FloraSystem>();
            if (flora == null)
                return;
            trees.Clear();
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAny<NetPlayer, PlayerTag>())
                flora.TreesNear(t.ValueRO.Position.xz, ForestRange, burningSpecies, trees);
            var seen = new HashSet<int>();
            foreach (var t in trees)
            {
                if (!seen.Add(t.seed))
                    continue;   // (two players' ranges overlap)
                var h = (uint)t.seed;
                var extra = Scramble(h ^ 0xA5A5A5A5u);
                if ((extra & 3u) == 0u)
                {
                    var size = 0.9f + (extra >> 8 & 0xFF) / 255f * 0.7f;
                    gathered.Add(new FireSpot { Position = t.foot, Radius = 0.4f + size * 0.3f, Heat = 1.5f, Flags = FireSpots.Ground | FireSpots.Forest });
                }
                if ((extra & 12u) == 0u)
                {
                    var a = (extra >> 16 & 0xFF) / 255f * math.PI * 2f;
                    var at = t.foot + new float3(math.cos(a), 0f, math.sin(a)) * (1.5f + (extra >> 24 & 0xF) * 0.2f);
                    gathered.Add(new FireSpot { Position = at, Radius = 0.5f, Heat = 0.8f, Flags = FireSpots.Ground | FireSpots.Forest });
                }
            }
        }

        /// <summary>LandmarkEffectSystem's hash, so the flames listed are the ones drawn.</summary>
        static uint Scramble(uint h)
        {
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return h;
        }

        static bool Same(List<FireSpot> a, List<FireSpot> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (math.any(a[i].Position != b[i].Position))
                    return false;
            return true;
        }

        /// <summary>New containers for the brain (it is done with the old ones first: it writes CreatureState).</summary>
        void Rebuild()
        {
            EntityManager.CompleteDependencyBeforeRW<CreatureState>();
            if (spots.IsCreated)
                spots.Dispose();
            if (cells.IsCreated)
                cells.Dispose();
            var count = fixedSpots.Count + forestSpots.Count;
            spots = new NativeArray<FireSpot>(count, Allocator.Persistent);
            cells = new NativeParallelMultiHashMap<int2, int>(math.max(count * 4, 1), Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                var f = i < fixedSpots.Count ? fixedSpots[i] : forestSpots[i - fixedSpots.Count];
                spots[i] = f;
                var reach = f.Radius + FireSpots.ListedPad;
                var lo = FireSpots.CellOf(f.Position.xz - reach);
                var hi = FireSpots.CellOf(f.Position.xz + reach);
                for (int x = lo.x; x <= hi.x; x++)
                    for (int z = lo.y; z <= hi.y; z++)
                        cells.Add(new int2(x, z), i);
            }
            LastCounts = new int3(count, fixedSpots.Count, forestSpots.Count);
            // (Off, the list is still kept, for probes: only the brain stops reading it.)
            SystemAPI.SetSingleton(new FireSpots { Spots = spots, Cells = cells, On = On && count > 0 });
        }
    }
}
