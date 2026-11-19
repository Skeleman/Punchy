using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;
using Policy = Punchy.Settings.DetailPolicy;

namespace Punchy.Ambience
{
    /// <summary>W-142 (#262): leaves and petals afloat on wooded and wet rivers, circling in eddies and falling from riverside trees.</summary>
    public partial class LandmarkEffectSystem
    {
        const float RiverLeafMaxRange = 120f;
        const float FloatLeafSize = 0.16f;
        const float FloatSegmentShare = 0.55f;
        const float EddyTurn = 0.42f;
        const float RiversideTreeShare = 0.4f;
        const float WaterFlowSpeed = 1.6f;
        const float RiverLeafRefresh = 5f;
        static readonly Color[] Petals = { new Color(0.96f, 0.76f, 0.84f), new Color(0.96f, 0.95f, 0.88f), new Color(0.97f, 0.86f, 0.36f) };

        public static int LastFloatLeaves, LastEddyLeaves, LastRiversideTrees;

        readonly object riverLeafOwner = new object();
        readonly List<Kit.Plume> riverLeaves = new List<Kit.Plume>();
        readonly HashSet<Punchy.Flora.FloraSpecies> riversideSpecies = new HashSet<Punchy.Flora.FloraSpecies>();
        readonly Dictionary<Punchy.Flora.FloraSpecies, (Color a, Color b)> riversideFoliage = new Dictionary<Punchy.Flora.FloraSpecies, (Color, Color)>();
        readonly List<(float3 foot, float3 top, float height, float crown, int seed)> riversideTrees = new List<(float3, float3, float, float, int)>();
        List<Color>[] leafTints;
        bool[] petalBiome;
        BiomePalette riverLeafFrom;
        int2 riverLeafCellAt = new int2(int.MinValue);
        float riverLeafRangeAt, nextRiverLeaves;

        void RiverLeafTables(BiomePalette palette)
        {
            riverLeafFrom = palette;
            riversideSpecies.Clear();
            riversideFoliage.Clear();
            leafTints = new List<Color>[palette.Biomes.Length];
            petalBiome = new bool[palette.Biomes.Length];
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                var biome = palette.Biomes[b];
                if (biome == null || !(biome.HasTag(BiomeTags.Forest) || biome.Family == BiomeFamily.Wet))
                    continue;
                petalBiome[b] = biome.Family == BiomeFamily.Wet || biome.HasTag(BiomeTags.Glade);
                var tints = new List<Color>();
                foreach (var f in biome.Flora)
                {
                    var s = f.Species;
                    if (s == null || (s.Form != Punchy.Flora.FloraForm.BroadleafTree && s.Form != Punchy.Flora.FloraForm.HiveTree))
                        continue;
                    riversideSpecies.Add(s);
                    riversideFoliage[s] = (s.Foliage, s.FoliageVariation);
                    tints.Add(s.Foliage);
                    tints.Add(s.FoliageVariation);
                }
                if (tints.Count > 0 || petalBiome[b])
                    leafTints[b] = tints;
            }
        }

        void RiverLeaves(in TerrainSource source, BiomePalette palette, Vector3 eye)
        {
            if (!source.Rivers.IsCreated)
                return;
            if (riverLeafFrom != palette)
            {
                RiverLeafTables(palette);
                riverLeafCellAt = new int2(int.MinValue);
            }
            ref var map = ref source.Rivers.Value;
            var gone = Kit.Leaves.Effect != null ? Kit.Leaves.Effect.GonePixels : 2f;
            var range = math.min(Policy.ExtraReach(FloatLeafSize, gone), RiverLeafMaxRange) + 8f;
            var at = ((float3)eye).xz;
            var cell = (int2)math.floor((at - map.Origin) / map.CellSize);
            if (math.all(cell == riverLeafCellAt) && math.abs(range - riverLeafRangeAt) < map.CellSize && Time.unscaledTime < nextRiverLeaves)
                return;
            riverLeafCellAt = cell;
            riverLeafRangeAt = range;
            nextRiverLeaves = Time.unscaledTime + RiverLeafRefresh;
            riverLeaves.Clear();
            int floats = 0, eddies = 0;
            var n = (int)math.ceil(range / map.CellSize) + 1;
            for (int z = -n; z <= n; z++)
            for (int x = -n; x <= n; x++)
            {
                var c = cell + new int2(x, z);
                if (math.any(c < 0) || math.any(c >= map.Resolution))
                    continue;
                var i = c.y * map.Resolution + c.x;
                var down = map.Down[i];
                var width = map.Width[i];
                if (width <= 0f || down < 0 || RiverKindSkips(ref map, i) || RiverKindSkips(ref map, down))
                    continue;
                var a = map.Point(i);
                var b = map.Point(down);
                var length = math.distance(a, b);
                if (length < 1f || math.distance((a + b) * 0.5f, at) > range)
                    continue;
                var h = Scramble(math.hash(new int2(i, 0x1EAF7)));
                var mid = source.SampleGround((a + b) * 0.5f);
                if (!mid.HasWater || mid.WaterSurface < mid.Height + 0.1f || !LeafyAt(mid.Biomes, out var biome))
                    continue;
                var dir = (b - a) / length;
                if ((h & 0xFFFF) / 65536f < FloatSegmentShare)
                {
                    var top = source.SampleGround(a);
                    var end = source.SampleGround(b);
                    if (top.HasWater && end.HasWater)
                    {
                        // The streaks' own speed (Water.shader riverSpeed): faster down rapids and in the deep middle.
                        var speed = WaterFlowSpeed * (1f + 2.5f * mid.Rapids) * math.lerp(0.45f, 1.25f, math.saturate((mid.WaterSurface - mid.Height) / 1.6f));
                        var r = Scramble(h);
                        riverLeaves.Add(LeafAfloat(new float3(a.x, top.WaterSurface, a.y), r, LeafTint(biome, r),
                            dir * speed, math.clamp(width * 0.3f, 0.5f, 6f), length / speed, 0f, top.WaterSurface - end.WaterSurface, 1 + (int)((r >> 12) % 3u)));
                        floats++;
                    }
                }
                var next = map.Down[down];
                if (next < 0 || RiverKindSkips(ref map, next))
                    continue;
                var d2 = math.normalizesafe(map.Point(next) - b);
                var turn = dir.x * d2.y - dir.y * d2.x;
                if (math.dot(dir, d2) > math.cos(EddyTurn) || mid.Rapids > 0.3f)
                    continue;
                var bend = math.normalizesafe(dir + d2, dir);
                var inner = turn > 0f ? new float2(-bend.y, bend.x) : new float2(bend.y, -bend.x);
                var spot = b + inner * width * 0.32f;
                var pool = source.SampleGround(spot);
                if (!pool.HasWater || pool.WaterSurface < pool.Height + 0.1f)
                    continue;
                var e = Scramble(h ^ 0xEDD1u);
                // Lifted a little: the surface banks across the pool and would swallow the low side of the ring.
                riverLeaves.Add(LeafAfloat(new float3(spot.x, pool.WaterSurface + 0.06f, spot.y), e, LeafTint(biome, e),
                    float2.zero, 0f, 24f + 10f * ((e >> 8) & 0xFF) / 255f, math.sign(turn) * math.clamp(width * 0.12f, 0.6f, 2.5f), 0f, 3 + (int)((e >> 16) % 3u)));
                eddies++;
            }
            LastFloatLeaves = floats;
            LastEddyLeaves = eddies;
            LastRiversideTrees = RiversideTrees(source, ref map, at, range);
            Kit.Leaves.Set(riverLeafOwner, riverLeaves);
        }

        static bool RiverKindSkips(ref RiverMapBlob map, int i)
            => map.Kind.Length > 0 && ((RiverProfileKind)map.Kind[i] & (RiverProfileKind.Fall | RiverProfileKind.Lake)) != 0;

        bool LeafyAt(in BiomeWeights biomes, out int biome)
        {
            biome = -1;
            var best = 0f;
            for (int k = 0; k < 4; k++)
                if (biomes.Weights[k] > best)
                {
                    best = biomes.Weights[k];
                    biome = biomes.Ids[k];
                }
            return biome >= 0 && biome < leafTints.Length && leafTints[biome] != null;
        }

        Color LeafTint(int biome, uint r)
        {
            var tints = leafTints[biome];
            if (tints.Count == 0 || (petalBiome[biome] && ((r >> 4) & 3u) == 0u))
                return Petals[(r >> 6) % (uint)Petals.Length];
            var green = tints[(int)((r >> 8) % (uint)tints.Count)];
            return Color.Lerp(green, Autumn[(r >> 20) % (uint)Autumn.Length], 0.35f + 0.45f * ((r >> 24) & 0xF) / 15f);
        }

        static Kit.Plume LeafAfloat(float3 position, uint r, Color tint, float2 flow, float spread, float life, float eddy, float fallBy, int count)
        {
            var col = tint.linear;
            return new Kit.Plume
            {
                Position = position,
                Seed = ((r >> 10) & 0xFFFF) / 65536f,
                Color = new float4(col.r, col.g, col.b, 0f),
                Shape = new float4(math.length(flow) * life, spread, FloatLeafSize * (0.85f + 0.3f * ((r >> 26) & 0xF) / 15f), life),
                Timing = new float4(0f, 0f, 0.95f, count),
                Out = flow,
                Drop = -1f,
                Fall = new float4(0f, eddy, 0.3f, fallBy)
            };
        }

        int RiversideTrees(in TerrainSource source, ref RiverMapBlob map, float2 at, float range)
        {
            if (riversideSpecies.Count == 0)
                return 0;
            if (flora == null)
                flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
            if (flora == null)
                return 0;
            var count = 0;
            foreach (var species in riversideSpecies)
            {
                oneSpecies.Clear();
                oneSpecies.Add(species);
                riversideTrees.Clear();
                flora.TreesNear(at, math.min(range, 80f), oneSpecies, riversideTrees);
                var (fa, fb) = riversideFoliage[species];
                foreach (var t in riversideTrees)
                {
                    var h = Scramble((uint)t.seed ^ 0x51DE5u);
                    if ((h & 0xFFFF) / 65536f >= RiversideTreeShare)
                        continue;
                    if (!NearestChannel(ref map, t.foot.xz, out var toward, out var gap) || gap > t.crown + 2f)
                        continue;
                    var xz = t.foot.xz + toward * math.min(gap + 0.5f, t.crown * 0.7f);
                    var ground = source.SampleGround(xz);
                    var land = ground.HasWater ? math.max(ground.WaterSurface, ground.Height) : ground.Height;
                    var crownBase = t.top.y - t.height * 0.35f;
                    if (crownBase < land + 1f)
                        continue;
                    var r = Scramble(h);
                    var col = Color.Lerp(Color.Lerp(fa, fb, (r & 0xFF) / 255f), Autumn[(r >> 22) % (uint)Autumn.Length], 0.5f + 0.4f * ((r >> 18) & 0xF) / 15f).linear;
                    riverLeaves.Add(new Kit.Plume
                    {
                        Position = new float3(xz.x, crownBase, xz.y),
                        Seed = ((r >> 8) & 0xFFFF) / 65536f,
                        Color = new float4(col.r, col.g, col.b, 0f),
                        Shape = new float4(t.height * 0.2f, math.max(t.crown * 0.5f, 1f), LeafSize, 9f + 4f * ((r >> 28) & 0xF) / 15f),
                        Timing = new float4(0f, 0f, 0.95f, 2),
                        Drop = crownBase - land + 0.02f
                    });
                    count++;
                }
            }
            return count;
        }

        static bool NearestChannel(ref RiverMapBlob map, float2 xz, out float2 toward, out float gap)
        {
            toward = default;
            gap = float.MaxValue;
            var c = (int2)math.floor((xz - map.Origin) / map.CellSize);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var k = c + new int2(dx, dz);
                if (math.any(k < 0) || math.any(k >= map.Resolution))
                    continue;
                var i = k.y * map.Resolution + k.x;
                var down = map.Down[i];
                if (map.Width[i] <= 0f || down < 0 || RiverKindSkips(ref map, i))
                    continue;
                var a = map.Point(i);
                var ab = map.Point(down) - a;
                var p = a + ab * math.saturate(math.dot(xz - a, ab) / math.max(math.lengthsq(ab), 1e-4f));
                var d = math.distance(p, xz) - map.Width[i] * 0.5f;
                if (d < gap)
                {
                    gap = d;
                    toward = math.normalizesafe(p - xz);
                }
            }
            return gap < float.MaxValue;
        }

        void ClearRiverLeaves()
        {
            Kit.Leaves.Set(riverLeafOwner, null);
            riverLeafCellAt = new int2(int.MinValue);
        }
    }
}
