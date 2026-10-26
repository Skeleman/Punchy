using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Places landmarks and builds the landmark blob. Hand-placed landmarks go in as given; then candidate spots on a
    /// jittered grid each roll for one of their biome's landmark types and keep it if the ground suits it (well
    /// inside one biome, dry, not too uneven, in the type's height range) and nothing else is too close. Any type
    /// that misses out by chance is then given the first suitable spot found for it, so each appears at least once
    /// wherever the land allows. Runs after rivers are routed, so landmarks keep clear of rivers and lakes rather
    /// than redirecting them.
    /// </summary>
    public static class LandmarkBaking
    {
        const float CandidateSpacing = 180f;
        const float GridCell = 128f;

        /// <summary>Every landmark type used by the biomes or placed by hand, in blob order.</summary>
        public static List<LandmarkType> Types(IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<PlacedLandmark> placed)
        {
            var types = new List<LandmarkType>();
            foreach (var biome in palette)
                if (biome != null)
                    foreach (var l in biome.Landmarks)
                        if (l.Type != null && !types.Contains(l.Type))
                            types.Add(l.Type);
            if (placed != null)
                foreach (var p in placed)
                    if (p.Type != null && !types.Contains(p.Type))
                        types.Add(p.Type);
            return types;
        }

        struct TypeRules
        {
            public float Radius, MaxRelief;
            public float2 Elevation;
        }

        [BurstCompile]
        struct EvaluateJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float2> Points;
            [ReadOnly] public NativeArray<float> Rolls;
            /// <summary>Footprints are checked at this multiple of the radius (the largest size they could be).</summary>
            public float FootprintScale;
            /// <summary>Type every spot tries (where its biome lists it), or -1 to roll for one.</summary>
            public int Forced;
            [ReadOnly] public NativeArray<int2> BiomeRanges;
            [ReadOnly] public NativeArray<int> EntryTypes;
            [ReadOnly] public NativeArray<float> EntryChances;
            [ReadOnly] public NativeArray<TypeRules> Types;
            [WriteOnly] public NativeArray<int> OutType;
            [WriteOnly] public NativeArray<float> OutBase;
            [WriteOnly] public NativeArray<float2> OutUphill;

            public void Execute(int i)
            {
                OutType[i] = -1;
                var p = Points[i];
                var g = Source.SampleGround(p);
                var biome = Dominant(g.Biomes, out var weight);
                if (weight < 0.9f || biome >= BiomeRanges.Length)
                    return;

                var range = BiomeRanges[biome];
                var roll = Rolls[i];
                var type = -1;
                for (int e = range.x; e < range.x + range.y; e++)
                {
                    roll -= EntryChances[e];
                    if (Forced >= 0 ? EntryTypes[e] == Forced : roll < 0f)
                    {
                        type = EntryTypes[e];
                        break;
                    }
                }
                if (type < 0)
                    return;

                var rules = Types[type];
                var radius = rules.Radius * FootprintScale;
                var elevation = g.Height - Source.World.SeaLevel;
                if (elevation < rules.Elevation.x || elevation > rules.Elevation.y || Wet(g))
                    return;

                // The whole footprint (and a margin) must be dry, in the same biome and not too uneven.
                float lo = g.Height, hi = g.Height;
                for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 12; k++)
                {
                    var a = k / 12f * math.PI * 2f + ring * 0.26f;
                    var q = p + new float2(math.cos(a), math.sin(a)) * radius * (ring == 1 ? 0.55f : 1.15f);
                    var gq = Source.SampleGround(q);
                    if (Wet(gq) || Weight(gq.Biomes, biome) < 0.6f)
                        return;
                    if (ring == 1 || k % 2 == 0)
                    {
                        lo = math.min(lo, gq.Height);
                        hi = math.max(hi, gq.Height);
                    }
                }
                if (hi - lo > rules.MaxRelief)
                    return;

                var e1 = radius * 0.5f;
                OutUphill[i] = new float2(Source.SampleHeight(p + new float2(e1, 0f)) - Source.SampleHeight(p - new float2(e1, 0f)),
                                          Source.SampleHeight(p + new float2(0f, e1)) - Source.SampleHeight(p - new float2(0f, e1)));
                OutType[i] = type;
                OutBase[i] = g.Height;
            }

            static bool Wet(in GroundSample g) => g.River > 0.02f || g.WaterDepth > -1.5f || g.HasWater && g.WaterSurface > g.Height - 1f;

            static int Dominant(in BiomeWeights b, out float weight)
            {
                var best = 0;
                for (int k = 1; k < 4; k++)
                    if (b.Weights[k] > b.Weights[best])
                        best = k;
                weight = b.Weights[best];
                return b.Ids[best];
            }

            static float Weight(in BiomeWeights b, int id)
            {
                var w = 0f;
                for (int k = 0; k < 4; k++)
                    if (b.Ids[k] == id)
                        w += b.Weights[k];
                return w;
            }
        }

        public static BlobAssetReference<LandmarkSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette,
                                                                 List<LandmarkType> types, IReadOnlyList<PlacedLandmark> placed,
                                                                 Allocator allocator)
        {
            var instances = new List<LandmarkInstance>();
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(source.World.Seed, 0x1A4D3A2Bu)) | 1u);

            // Hand-placed landmarks first: generated ones keep clear of them.
            if (placed != null)
                foreach (var p in placed)
                {
                    if (p.Type == null)
                        continue;
                    var angle = math.radians(p.Rotation);
                    var position = (float2)p.Position;
                    instances.Add(new LandmarkInstance
                    {
                        Position = position,
                        Facing = new float2(math.cos(angle), math.sin(angle)),
                        Scale = math.max(p.Scale, 0.05f),
                        Type = types.IndexOf(p.Type),
                        BaseHeight = source.SampleHeight(position),
                        Seed = rng.NextFloat(-100f, 100f)
                    });
                }

            // Per biome: which types can appear, and each one's chance per candidate spot.
            var biomeRanges = new NativeArray<int2>(palette.Count, Allocator.TempJob);
            var entryTypes = new List<int>();
            var entryChances = new List<float>();
            var cellArea = CandidateSpacing * CandidateSpacing / 1e6f;
            for (int b = 0; b < palette.Count; b++)
            {
                var start = entryTypes.Count;
                if (palette[b] != null)
                    foreach (var l in palette[b].Landmarks)
                        if (l.Type != null && l.Density > 0f)
                        {
                            entryTypes.Add(types.IndexOf(l.Type));
                            entryChances.Add(l.Density * cellArea);
                        }
                biomeRanges[b] = new int2(start, entryTypes.Count - start);
            }

            if (entryTypes.Count > 0)
            {
                var half = source.World.WorldSize * 0.5f + source.World.CoastWidth;
                var perSide = (int)math.ceil(half * 2f / CandidateSpacing);
                var count = perSide * perSide;
                var points = new NativeArray<float2>(count, Allocator.TempJob);
                var rolls = new NativeArray<float>(count, Allocator.TempJob);
                var scales = new NativeArray<float>(count, Allocator.TempJob);
                var angles = new float[count];
                var seeds = new float[count];
                var priority = new uint[count];
                for (int z = 0, i = 0; z < perSide; z++)
                for (int x = 0; x < perSide; x++, i++)
                {
                    points[i] = new float2(-half) + (new float2(x, z) + rng.NextFloat2(0.15f, 0.85f)) * CandidateSpacing;
                    rolls[i] = rng.NextFloat();
                    scales[i] = rng.NextFloat();
                    angles[i] = rng.NextFloat(math.PI * 2f);
                    seeds[i] = rng.NextFloat(-100f, 100f);
                    priority[i] = rng.NextUInt();
                }
                var rules = new NativeArray<TypeRules>(types.Count, Allocator.TempJob);
                for (int t = 0; t < types.Count; t++)
                    rules[t] = new TypeRules { Radius = types[t].Radius, MaxRelief = types[t].MaxRelief, Elevation = types[t].Elevation };
                var outType = new NativeArray<int>(count, Allocator.TempJob);
                var outBase = new NativeArray<float>(count, Allocator.TempJob);
                var outUphill = new NativeArray<float2>(count, Allocator.TempJob);
                var entryTypeArray = new NativeArray<int>(entryTypes.ToArray(), Allocator.TempJob);
                var chances = new NativeArray<float>(entryChances.ToArray(), Allocator.TempJob);
                var order = new List<int>();
                var terrain = source;   // local functions can't capture an in parameter

                // Evaluates every spot (rolling for a type, or trying the forced one), then accepts the suitable ones
                // in random order, skipping any that would crowd one already accepted. Returns how many were added.
                int Place(int forced, int limit)
                {
                    new EvaluateJob
                    {
                        Source = terrain,
                        Points = points,
                        Rolls = rolls,
                        FootprintScale = 1.2f,
                        Forced = forced,
                        BiomeRanges = biomeRanges,
                        EntryTypes = entryTypeArray,
                        EntryChances = chances,
                        Types = rules,
                        OutType = outType,
                        OutBase = outBase,
                        OutUphill = outUphill
                    }.Schedule(count, 16).Complete();

                    order.Clear();
                    for (int i = 0; i < count; i++)
                        if (outType[i] >= 0)
                            order.Add(i);
                    order.Sort((a, b) => priority[a].CompareTo(priority[b]));
                    var added = 0;
                    foreach (var i in order)
                    {
                        if (added >= limit)
                            break;
                        var type = types[outType[i]];
                        var scale = math.lerp(type.Scale.x, type.Scale.y, scales[i]);
                        var radius = type.Radius * scale;
                        var crowded = false;
                        foreach (var other in instances)
                            if (math.distance(other.Position, points[i]) < radius + types[other.Type].Radius * other.Scale + 40f)
                            {
                                crowded = true;
                                break;
                            }
                        if (crowded)
                            continue;
                        // Cliffs face downhill, rising with the land behind them.
                        var angle = angles[i];
                        if (type.Shape == LandmarkShape.Cliff && math.lengthsq(outUphill[i]) > 1e-4f)
                            angle = math.atan2(outUphill[i].y, outUphill[i].x);
                        instances.Add(new LandmarkInstance
                        {
                            Position = points[i],
                            Facing = new float2(math.cos(angle), math.sin(angle)),
                            Scale = scale,
                            Type = outType[i],
                            BaseHeight = outBase[i],
                            Seed = seeds[i]
                        });
                        added++;
                    }
                    return added;
                }

                Place(-1, int.MaxValue);
                for (int t = 0; t < types.Count; t++)
                    if (entryTypes.Contains(t) && !instances.Exists(inst => inst.Type == t))
                        Place(t, 1);

                points.Dispose();
                rolls.Dispose();
                scales.Dispose();
                rules.Dispose();
                outType.Dispose();
                outBase.Dispose();
                outUphill.Dispose();
                entryTypeArray.Dispose();
                chances.Dispose();
            }
            biomeRanges.Dispose();

            if (instances.Count == 0)
                return default;
            return BuildBlob(types, instances, allocator);
        }

        static BlobAssetReference<LandmarkSetBlob> BuildBlob(IReadOnlyList<LandmarkType> types, List<LandmarkInstance> instances, Allocator allocator)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<LandmarkSetBlob>();

            var pools = new List<LandmarkPoolData>();
            var typeArray = builder.Allocate(ref root.Types, types.Count);
            for (int t = 0; t < types.Count; t++)
            {
                var type = types[t];
                var first = pools.Count;
                foreach (var p in type.Pools)
                    pools.Add(new LandmarkPoolData { Offset = p.Offset, Radius = p.Radius, Depth = p.Depth });
                var water = type.WaterColor.linear;
                typeArray[t] = new LandmarkTypeData
                {
                    Radius = type.Radius,
                    Shape = type.Shape,
                    Height = type.Height,
                    ShapeRadius = type.ShapeRadius,
                    Edge = type.Edge,
                    Flatten = type.Flatten,
                    Roughness = type.Roughness,
                    FirstPool = first,
                    PoolCount = pools.Count - first,
                    OverrideWater = type.OverrideWater ? 1 : 0,
                    Water = new float4(water.r, water.g, water.b, type.WaterMurk),
                    WaterGlow = type.WaterGlow,
                    HasGround = type.Ground != null ? 1 : 0,
                    Ground = type.Ground != null ? BiomeBaking.SurfaceData(type.Ground) : default,
                    GroundRadius = math.max(type.GroundRadius, 0.05f),
                    ClearRadius = type.ClearRadius
                };
            }
            var poolArray = builder.Allocate(ref root.Pools, pools.Count);
            for (int i = 0; i < pools.Count; i++)
                poolArray[i] = pools[i];
            var instanceArray = builder.Allocate(ref root.Instances, instances.Count);
            for (int i = 0; i < instances.Count; i++)
                instanceArray[i] = instances[i];

            // Grid over all footprints.
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var inst in instances)
            {
                var r = types[inst.Type].Radius * inst.Scale;
                min = math.min(min, inst.Position - r);
                max = math.max(max, inst.Position + r);
            }
            root.GridOrigin = min;
            root.CellSize = GridCell;
            var size = (int2)math.ceil((max - min) / GridCell) + 1;
            root.GridSize = size;
            var cells = new List<int>[size.x * size.y];
            for (int i = 0; i < instances.Count; i++)
            {
                var inst = instances[i];
                var r = types[inst.Type].Radius * inst.Scale;
                var c0 = (int2)math.floor((inst.Position - r - min) / GridCell);
                var c1 = (int2)math.floor((inst.Position + r - min) / GridCell);
                for (int z = c0.y; z <= c1.y; z++)
                for (int x = c0.x; x <= c1.x; x++)
                    (cells[z * size.x + x] ??= new List<int>()).Add(i);
            }
            var cellArray = builder.Allocate(ref root.Cells, cells.Length);
            var items = new List<int>();
            for (int c = 0; c < cells.Length; c++)
            {
                cellArray[c] = new int2(items.Count, cells[c]?.Count ?? 0);
                if (cells[c] != null)
                    items.AddRange(cells[c]);
            }
            var itemArray = builder.Allocate(ref root.Items, items.Count);
            for (int i = 0; i < items.Count; i++)
                itemArray[i] = items[i];

            var blob = builder.CreateBlobAssetReference<LandmarkSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }
    }
}
