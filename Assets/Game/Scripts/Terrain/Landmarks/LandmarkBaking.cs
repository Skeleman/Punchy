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
    /// Stable seeds: every spot's jitter, size, angle and order come from a hash of its grid cell (not a running random
    /// sequence), and each type rolls for itself at each spot from a hash of the spot and its own name, the winner
    /// being the type that beat its chance by most. So adding a type to a biome list only takes the spots it wins
    /// (and moves what those crowd); the rest of the island stays put. Instances carry a Key that everything
    /// downstream seeds from.
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
            public float Radius, MinRelief, MaxRelief;
            public float2 Elevation;
            /// <summary>A settlement's folk (FolkCulture bit), or 0 for other landmarks; Camp: a palisaded camp.</summary>
            public int Culture, Camp;
            /// <summary>1: only placed as another landmark's companion.</summary>
            public int CompanionOnly;
        }

        /// <summary>Set in a biome's culture mask when it takes camps only (Habitability.CampsOnly).</summary>
        const int CampsOnlyFlag = 1 << 30;

        [BurstCompile]
        struct EvaluateJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float2> Points;
            /// <summary>Per spot: its stable hash (see Build).</summary>
            [ReadOnly] public NativeArray<uint> Keys;
            /// <summary>Per type: NameHash.</summary>
            [ReadOnly] public NativeArray<uint> TypeHashes;
            /// <summary>Footprints are checked at this multiple of the radius (the largest size they could be).</summary>
            public float FootprintScale;
            /// <summary>Type every spot tries (where its biome lists it), or -1 to roll for one.</summary>
            public int Forced;
            /// <summary>
            /// How much of the footprint's outer ring must still be the centre's biome (0.6). The pass that makes sure
            /// every type appears somewhere lowers it, so a big landmark in a small biome patch (a volcano cone among
            /// its ashlands) can spill over the patch's edge.
            /// </summary>
            public float OuterWeight;
            /// <summary>Per biome: the folk that may settle there, plus <see cref="CampsOnlyFlag"/>.</summary>
            [ReadOnly] public NativeArray<int> BiomeCultures;
            /// <summary>Per biome: its realm (from the cell table), or -1.</summary>
            [ReadOnly] public NativeArray<int> BiomeRealm;
            /// <summary>Only spots in this realm (the pass that gives every realm a settlement), or -1.</summary>
            public int ForcedRealm;
            /// <summary>Only spots within z metres of xy (a companion beside its landmark); z 0 = anywhere.</summary>
            public float3 Near;
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
                if (Near.z > 0f && math.distance(p, Near.xy) > Near.z)
                    return;
                var g = Source.SampleGround(p);
                var biome = Dominant(g.Biomes, out var weight);
                if (weight < 0.9f || biome >= BiomeRanges.Length)
                    return;

                // Each listed type rolls for itself here (a hash of the spot and its name) against its own chance;
                // of those that pass, the one that passed by most wins (lowest roll / chance, ties by name hash).
                var range = BiomeRanges[biome];
                var type = -1;
                var best = float.MaxValue;
                var bestHash = uint.MaxValue;
                for (int e = range.x; e < range.x + range.y; e++)
                {
                    var t = EntryTypes[e];
                    if (Forced >= 0)
                    {
                        if (t == Forced)
                        {
                            type = t;
                            break;
                        }
                        continue;
                    }
                    var chance = EntryChances[e];
                    var u = (BiomeSampling.Scramble(math.hash(new uint2(Keys[i], TypeHashes[t]))) >> 8) * (1f / 16777216f);
                    if (u >= chance)
                        continue;
                    var ratio = u / chance;
                    if (ratio < best || ratio == best && TypeHashes[t] < bestHash)
                    {
                        best = ratio;
                        bestHash = TypeHashes[t];
                        type = t;
                    }
                }
                if (type < 0)
                    return;

                var rules = Types[type];
                if (rules.CompanionOnly != 0 && Near.z <= 0f)
                    return;
                // Settlements only where the biome under the centre allows their folk (and only camps where it takes
                // camps only).
                if (rules.Culture != 0)
                {
                    var allowed = BiomeCultures[biome];
                    if ((allowed & rules.Culture) == 0 || (allowed & CampsOnlyFlag) != 0 && rules.Camp == 0)
                        return;
                }
                var realm = BiomeRealm[biome];
                if (ForcedRealm >= 0 && realm != ForcedRealm)
                    return;
                var radius = rules.Radius * FootprintScale;
                var elevation = g.Height - Source.World.SeaLevel;
                if (elevation < rules.Elevation.x || elevation > rules.Elevation.y || Wet(g))
                    return;

                // The whole footprint (and a margin) must be dry, in the same biome and not too uneven. A settlement
                // only has to stay in its realm (a village may spread over grassland and forest alike).
                var byRealm = rules.Culture != 0 && realm >= 0;
                float lo = g.Height, hi = g.Height;
                for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 12; k++)
                {
                    var a = k / 12f * math.PI * 2f + ring * 0.26f;
                    var q = p + new float2(math.cos(a), math.sin(a)) * radius * (ring == 1 ? 0.55f : 1.15f);
                    var gq = Source.SampleGround(q);
                    if (Wet(gq) || (byRealm ? RealmWeight(gq.Biomes, realm) : Weight(gq.Biomes, biome)) < (ring == 1 ? 0.6f : OuterWeight))
                        return;
                    if (ring == 1 || k % 2 == 0)
                    {
                        lo = math.min(lo, gq.Height);
                        hi = math.max(hi, gq.Height);
                    }
                }
                if (hi - lo > rules.MaxRelief || hi - lo < rules.MinRelief)
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

            float RealmWeight(in BiomeWeights b, int realm)
            {
                var w = 0f;
                for (int k = 0; k < 4; k++)
                    if (b.Weights[k] > 0f && b.Ids[k] < BiomeRealm.Length && BiomeRealm[b.Ids[k]] == realm)
                        w += b.Weights[k];
                return w;
            }
        }

        /// <summary>A stable hash of a landmark type's name (FNV-1a), so its rolls don't depend on its place in any list.</summary>
        public static uint NameHash(LandmarkType type)
        {
            var h = 2166136261u;
            foreach (var ch in type.name)
            {
                h ^= ch;
                h *= 16777619u;
            }
            return h;
        }

        public static BlobAssetReference<LandmarkSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette,
                                                                 List<LandmarkType> types, IReadOnlyList<PlacedLandmark> placed,
                                                                 Allocator allocator)
        {
            var instances = new List<LandmarkInstance>();
            var worldSeed = source.World.Seed;

            // Hand-placed landmarks first: generated ones keep clear of them.
            if (placed != null)
                foreach (var p in placed)
                {
                    if (p.Type == null)
                        continue;
                    var angle = math.radians(p.Rotation);
                    var position = (float2)p.Position;
                    var key = math.hash(new uint4(worldSeed, math.asuint(position.x), math.asuint(position.y), 0x9A1Cu)) | 1u;
                    var rng = new Unity.Mathematics.Random(key);
                    instances.Add(new LandmarkInstance
                    {
                        Position = position,
                        Facing = new float2(math.cos(angle), math.sin(angle)),
                        Scale = math.max(p.Scale, 0.05f),
                        Type = types.IndexOf(p.Type),
                        BaseHeight = source.SampleHeight(position),
                        Seed = rng.NextFloat(-100f, 100f),
                        Key = key
                    });
                }

            // Per biome: who may settle there, and its realm (every cell's biome belongs to the cell's realm).
            var biomeCultures = new NativeArray<int>(palette.Count, Allocator.TempJob);
            var biomeRealm = new NativeArray<int>(palette.Count, Allocator.TempJob);
            for (int b = 0; b < palette.Count; b++)
            {
                biomeRealm[b] = -1;
                var biome = palette[b];
                if (biome != null)
                    biomeCultures[b] = biome.Habitability == Habitability.None ? 0
                        : (int)biome.Cultures & ~CampsOnlyFlag | (biome.Habitability == Habitability.CampsOnly ? CampsOnlyFlag : 0);
            }
            if (source.BiomeCells.IsCreated)
            {
                ref var table = ref source.BiomeCells.Value;
                for (int i = 0; i < table.Realms.Length; i++)
                    if (table.Realms[i] != BiomeSampling.NoRealm && table.Ids[i] < palette.Count)
                        biomeRealm[table.Ids[i]] = table.Realms[i];
            }

            // Per biome: which types can appear, and each one's chance per candidate spot.
            var biomeRanges = new NativeArray<int2>(palette.Count, Allocator.TempJob);
            var entryTypes = new List<int>();
            var entryChances = new List<float>();
            var entryPerRealm = new List<int>();
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
                            entryPerRealm.Add(math.max(1, l.PerRealm));
                        }
                biomeRanges[b] = new int2(start, entryTypes.Count - start);
            }

            if (entryTypes.Count > 0)
            {
                var half = source.World.WorldSize * 0.5f + source.World.CoastWidth;
                var perSide = (int)math.ceil(half * 2f / CandidateSpacing);
                var count = perSide * perSide;
                var points = new NativeArray<float2>(count, Allocator.TempJob);
                var keys = new NativeArray<uint>(count, Allocator.TempJob);
                var scales = new NativeArray<float>(count, Allocator.TempJob);
                var angles = new float[count];
                var seeds = new float[count];
                var priority = new uint[count];
                // Each spot's own values, from a hash of its cell and the world seed.
                for (int z = 0, i = 0; z < perSide; z++)
                for (int x = 0; x < perSide; x++, i++)
                {
                    keys[i] = math.hash(new uint4(worldSeed, (uint)x, (uint)z, 0x1A4D3A2Bu)) | 1u;
                    var rng = new Unity.Mathematics.Random(keys[i]);
                    points[i] = new float2(-half) + (new float2(x, z) + rng.NextFloat2(0.15f, 0.85f)) * CandidateSpacing;
                    scales[i] = rng.NextFloat();
                    angles[i] = rng.NextFloat(math.PI * 2f);
                    seeds[i] = rng.NextFloat(-100f, 100f);
                    priority[i] = rng.NextUInt();
                }
                var typeHashes = new NativeArray<uint>(types.Count, Allocator.TempJob);
                for (int t = 0; t < types.Count; t++)
                    typeHashes[t] = NameHash(types[t]);
                var rules = new NativeArray<TypeRules>(types.Count, Allocator.TempJob);
                for (int t = 0; t < types.Count; t++)
                {
                    var layout = types[t].Settlement;
                    rules[t] = new TypeRules
                    {
                        Radius = types[t].Radius,
                        MinRelief = types[t].MinRelief,
                        MaxRelief = types[t].MaxRelief,
                        Elevation = types[t].Elevation,
                        Culture = layout != null ? (int)layout.Culture : 0,
                        Camp = layout != null && layout.Pattern == SettlementPattern.Camp ? 1 : 0,
                        CompanionOnly = types[t].OnlyAsCompanion ? 1 : 0
                    };
                }
                var outType = new NativeArray<int>(count, Allocator.TempJob);
                var outBase = new NativeArray<float>(count, Allocator.TempJob);
                var outUphill = new NativeArray<float2>(count, Allocator.TempJob);
                var entryTypeArray = new NativeArray<int>(entryTypes.ToArray(), Allocator.TempJob);
                var chances = new NativeArray<float>(entryChances.ToArray(), Allocator.TempJob);
                var order = new List<int>();
                var terrain = source;   // local functions can't capture an in parameter

                // Evaluates every spot (rolling for a type, or trying the forced one), then accepts the suitable ones
                // in random order, skipping any that would crowd one already accepted. Returns how many were added.
                int Place(int forced, int limit, int forcedRealm = -1, float3 near = default)
                {
                    new EvaluateJob
                    {
                        Source = terrain,
                        Points = points,
                        Keys = keys,
                        TypeHashes = typeHashes,
                        FootprintScale = 1.2f,
                        Forced = forced,
                        OuterWeight = forced >= 0 ? 0.1f : 0.6f,
                        BiomeCultures = biomeCultures,
                        BiomeRealm = biomeRealm,
                        ForcedRealm = forcedRealm,
                        Near = near,
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
                            Seed = seeds[i],
                            Key = keys[i]
                        });
                        added++;
                    }
                    return added;
                }

                Place(-1, int.MaxValue);
                // Types that missed out, in name-hash order (not list order, which a new type would shift).
                var byName = new List<int>();
                for (int t = 0; t < types.Count; t++)
                    byName.Add(t);
                byName.Sort((a, b) => typeHashes[a].CompareTo(typeHashes[b]));
                foreach (var t in byName)
                    if (entryTypes.Contains(t) && !types[t].OnlyAsCompanion && !instances.Exists(inst => inst.Type == t) && Place(t, 1) == 0)
                        PlaceFiner(t);

                // A type that found no free spot on the candidate grid (its only suitable one taken by a bigger
                // neighbour, say) searches three more grids offset by half a cell before giving up, so "at least once
                // wherever the land allows" still holds when it's crowded out.
                void PlaceFiner(int type)
                {
                    var saved = new float2[count];
                    var savedKeys = new uint[count];
                    for (int i = 0; i < count; i++)
                    {
                        saved[i] = points[i];
                        savedKeys[i] = keys[i];
                    }
                    for (int o = 1; o < 4; o++)
                    {
                        var offset = new float2(o & 1, o >> 1) * CandidateSpacing * 0.5f;
                        for (int i = 0; i < count; i++)
                        {
                            points[i] = saved[i] + offset;
                            keys[i] = math.hash(new uint2(savedKeys[i], (uint)o * 0x9E37u)) | 1u;
                        }
                        if (Place(type, 1) > 0)
                            break;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        points[i] = saved[i];
                        keys[i] = savedKeys[i];
                    }
                }

                // Companions: beside each landmark that has them (a mining village by its mine), the first that the
                // biome there lists and that finds a spot in range.
                // The candidate grid is too coarse this close, so a ring of spots round the host stands in for its first
                // few (put back afterwards).
                const int Ring = 48;
                var savedPoints = new float2[math.min(Ring, count)];
                var savedKeys = new uint[savedPoints.Length];
                for (int j = 0; j < savedPoints.Length; j++)
                {
                    savedPoints[j] = points[j];
                    savedKeys[j] = keys[j];
                }
                var placedSoFar = instances.Count;
                for (int k = 0; k < placedSoFar; k++)
                {
                    var host = instances[k];
                    var hostType = types[host.Type];
                    if (hostType.Companions == null || hostType.Companions.Count == 0)
                        continue;
                    for (int j = 0; j < savedPoints.Length; j++)
                    {
                        var a = j * 2.39996f + host.Seed;
                        var d = hostType.CompanionRange * math.lerp(0.55f, 1f, math.frac(j * 0.618034f));
                        points[j] = host.Position + new float2(math.cos(a), math.sin(a)) * d;
                        keys[j] = math.hash(new uint3(host.Key, (uint)j, 0xC0A1u)) | 1u;
                    }
                    foreach (var companion in hostType.Companions)
                    {
                        var ct = companion != null ? types.IndexOf(companion) : -1;
                        if (ct >= 0 && entryTypes.Contains(ct) && Place(ct, 1, -1, new float3(host.Position, hostType.CompanionRange)) > 0)
                            break;
                    }
                }
                for (int j = 0; j < savedPoints.Length; j++)
                {
                    points[j] = savedPoints[j];
                    keys[j] = savedKeys[j];
                }

                // Every settlement type a realm's biomes list gets at least one settlement in that realm (or the
                // entry's Per Realm), so each realm has its own folk (a human village in the temperate lands even if
                // one already stands in the desert) wherever the land allows.
                var realmCount = 0;
                for (int b = 0; b < palette.Count; b++)
                    realmCount = math.max(realmCount, biomeRealm[b] + 1);
                for (int r = 0; r < realmCount; r++)
                for (int b = 0; b < palette.Count; b++)
                {
                    if (biomeRealm[b] != r)
                        continue;
                    for (int e = biomeRanges[b].x; e < biomeRanges[b].x + biomeRanges[b].y; e++)
                    {
                        var t = entryTypes[e];
                        if (types[t].Settlement == null || types[t].OnlyAsCompanion)
                            continue;
                        var present = 0;
                        foreach (var inst in instances)
                        {
                            if (inst.Type != t)
                                continue;
                            terrain.SampleBiomes(inst.Position, out var at, out _);
                            if (at.Dominant() < palette.Count && biomeRealm[at.Dominant()] == r)
                                present++;
                        }
                        while (present < entryPerRealm[e] && Place(t, 1, r) > 0)
                            present++;
                    }
                }

                points.Dispose();
                keys.Dispose();
                typeHashes.Dispose();
                scales.Dispose();
                rules.Dispose();
                outType.Dispose();
                outBase.Dispose();
                outUphill.Dispose();
                entryTypeArray.Dispose();
                chances.Dispose();
            }
            biomeCultures.Dispose();
            biomeRealm.Dispose();
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
                    WaterLava = type.WaterLava,
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
