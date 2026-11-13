using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-142 row 2 (#262): smoke vents. Fissures in volcanic ground (in small rows along a crack), cave floors beside lava
    /// pools, the rock round hot springs and the foot of a mine shaft or two, each a steady wisp, a puffing vent or one
    /// that hisses now and then. Placed after the trickles against the caves as built (rule 22: plain records; every try
    /// seeds from its own hash, so a miss never shifts another). They carve nothing, so nothing placed before them moves.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Tries over the island for fissures, the most fissures kept, and the least room between two (m).</summary>
        // gameplay: a few dozen an island, so a smoking crack stays a find, not a carpet
        const int FissureTries = 6000, MostFissures = 70;
        const float FissureSpacing = 70f;   // sim: bake spacing, not a view range
        /// <summary>How much of the ground's biome blend must be volcanic for a fissure.</summary>
        const float FissureVolcanic = 0.7f;
        /// <summary>Mine shafts that smoke, island-wide.</summary>
        const int SmokingShafts = 2;

        /// <summary>Vent misses this bake: [0] not volcanic, [1] water or sea, [2] steep, [3] a place or town there,
        /// [4] crowded, [5] cave floor (none, lava, no headroom).</summary>
        static readonly int[] VentMisses = new int[6];

        static void PlaceVents(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types,
                               List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds, List<CaveStyle> styles, Extras extras)
        {
            System.Array.Clear(VentMisses, 0, VentMisses.Length);
            var byKind = new int[3];
            var byPlace = new int[4];
            var byBiome = new Dictionary<int, int>();
            var log = new System.Text.StringBuilder();
            var vents = extras.Vents;

            // Fissures in the open: random tries over the island, each kept on volcanic, dry, gentle ground clear of
            // places and towns, then one to three vents along its crack.
            var volcanic = BiomeTags.InFamily(palette, BiomeFamily.Volcanic);
            var half = source.World.WorldSize * 0.5f;
            var fissures = new List<float2>();
            if (volcanic.Count > 0)
                for (uint t = 0; t < FissureTries && fissures.Count < MostFissures; t++)
                {
                    var rng = new Random(math.hash(new uint3(source.World.Seed, t, 0x5E47u)) | 1u);
                    var at = rng.NextFloat2(-half, half);
                    var angle = rng.NextFloat(math.PI);
                    var count = rng.NextInt(1, 4);
                    var spread = rng.NextFloat(2f, 4.5f);
                    if (!FissureGround(source, volcanic, at, out var biome))
                        continue;
                    var crowded = false;
                    foreach (var f in fissures)
                        crowded |= math.distance(f, at) < FissureSpacing;
                    if (crowded || NearPlace(source, at))
                    {
                        VentMisses[crowded ? 4 : 3]++;
                        continue;
                    }
                    fissures.Add(at);
                    var along = new float2(math.cos(angle), math.sin(angle));
                    for (int v = 0; v < count; v++)
                    {
                        // The middle vent first, then either side of it along the crack.
                        var offset = v == 0 ? 0f : (v == 1 ? 1f : -1f) * spread * (0.8f + 0.4f * rng.NextFloat());
                        var kindRoll = rng.NextFloat();
                        var strength = rng.NextFloat(0.5f, 1f);
                        var seed = rng.NextFloat();
                        var p = at + along * offset;
                        if (v > 0 && !FissureGround(source, volcanic, p, out _))
                            continue;
                        vents.Add(new CaveVent
                        {
                            Position = new float3(p.x, source.SampleHeight(p), p.y),
                            Cave = -1,
                            Kind = PickVent(kindRoll),
                            Place = VentPlace.Fissure,
                            Strength = v == 0 ? strength : strength * 0.7f,
                            Seed = seed
                        });
                        byBiome[biome] = byBiome.TryGetValue(biome, out var n) ? n + 1 : 1;
                    }
                }

            // Hot springs: one or two vents on the rock round each spring (a type that bubbles without glowing).
            if (source.Landmarks.IsCreated)
            {
                ref var set = ref source.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var inst = ref set.Instances[i];
                    var type = (uint)inst.Type < (uint)types.Count ? types[inst.Type] : null;
                    if (type == null || !IsHotSpring(type))
                        continue;
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    var rng = new Random(math.hash(new uint2(inst.Key, 0x5E48u)) | 1u);
                    var want = rng.NextInt(1, 3);
                    var first = vents.Count;
                    for (int attempt = 0; attempt < 10 && vents.Count - first < want; attempt++)
                    {
                        var angle = rng.NextFloat(math.PI * 2f);
                        var ring = rng.NextFloat(0.5f, 0.95f);
                        var kindRoll = rng.NextFloat();
                        var strength = rng.NextFloat(0.5f, 0.9f);
                        var seed = rng.NextFloat();
                        var p = inst.Position + new float2(math.cos(angle), math.sin(angle)) * radius * ring;
                        if (!DryGentle(source, p) || Crowded(vents, new float3(p.x, 0f, p.y), 3f, true))
                            continue;
                        vents.Add(new CaveVent
                        {
                            Position = new float3(p.x, source.SampleHeight(p), p.y),
                            Cave = -1,
                            Kind = PickVent(kindRoll),
                            Place = VentPlace.Spring,
                            Strength = strength,
                            Seed = seed
                        });
                    }
                }
            }

            // Caves: floors beside lava pools (two or three a cave), and the foot of a shaft in a mine or two.
            var mines = new List<(uint rank, int cave, int node)>();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel != 0 || cave.NodeCount == 0)
                    continue;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style != null && style.Ladder != null)
                {
                    // The lowest shaft node of this mine, if it has a shaft; ranked by the cave's own hash.
                    int lowest = -1;
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                        if (kinds[n] == CaveNodeKind.Shaft && (lowest < 0 || nodes[n].Position.y < nodes[lowest].Position.y))
                            lowest = n;
                    if (lowest >= 0)
                        mines.Add((math.hash(new uint2(cave.Key, 0x5E49u)), c, lowest));
                }
                var lava = new List<int>();
                for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount; p++)
                    if (extras.Pools[p].Lava >= 0.5f)
                        lava.Add(p);
                if (lava.Count == 0)
                    continue;
                var rngCave = new Random(math.hash(new uint2(cave.Key, 0x5E47u)) | 1u);
                var wantCave = rngCave.NextInt(2, 4);   // lava caves are few (one or two an island): two or three each
                var firstCave = vents.Count;
                for (int attempt = 0; attempt < wantCave * 12 && vents.Count - firstCave < wantCave; attempt++)
                {
                    var pick = rngCave.NextInt(lava.Count);
                    var angle = rngCave.NextFloat(math.PI * 2f);
                    var reach = rngCave.NextFloat(1f, 4f);
                    var kindRoll = rngCave.NextFloat();
                    var strength = rngCave.NextFloat(0.6f, 1f);
                    var seed = rngCave.NextFloat();
                    var pool = extras.Pools[lava[pick]];
                    var p = pool.Centre + new float3(math.cos(angle), 0f, math.sin(angle)) * (pool.Radius + reach);
                    if (!CaveFloor(source, p + new float3(0f, 1.2f, 0f), out var floor) || Crowded(vents, floor, 4f, false))
                        continue;
                    vents.Add(new CaveVent { Position = floor, Cave = c, Kind = PickVent(kindRoll), Place = VentPlace.Lava, Strength = strength, Seed = seed });
                    log.Append($" {c}:lava");
                }
            }
            mines.Sort((a, b) => a.rank.CompareTo(b.rank));
            int shafts = 0;
            foreach (var (rank, c, node) in mines)
            {
                if (shafts >= SmokingShafts)
                    break;
                if (!CaveFloor(source, nodes[node].Position, out var floor))
                    continue;
                vents.Add(new CaveVent
                {
                    Position = floor, Cave = c, Kind = VentKind.Wisp, Place = VentPlace.Mine,
                    Strength = 0.6f + 0.3f * (rank & 0xFFFF) / 65535f, Seed = (rank >> 16) / 65535f
                });
                log.Append($" {c}:mine");
                shafts++;
            }

            foreach (var v in vents)
            {
                byKind[(int)v.Kind]++;
                byPlace[(int)v.Place]++;
            }
            var biomes = new System.Text.StringBuilder();
            foreach (var pair in byBiome)
                biomes.Append($" {((uint)pair.Key < (uint)palette.Count && palette[pair.Key] != null ? palette[pair.Key].name : pair.Key.ToString())} {pair.Value}");
            UnityEngine.Debug.Log($"[cave-vents] kept {vents.Count}: fissure {byPlace[0]} ({fissures.Count} cracks), lava {byPlace[1]}, spring {byPlace[2]}, " +
                                  $"mine {byPlace[3]}; wisp {byKind[0]}, puff {byKind[1]}, hiss {byKind[2]}; by biome{biomes}; misses: not volcanic " +
                                  $"{VentMisses[0]}, water {VentMisses[1]}, steep {VentMisses[2]}, place {VentMisses[3]}, crowded {VentMisses[4]}, " +
                                  $"cave floor {VentMisses[5]}; caves{log}");
        }

        /// <summary>Half the vents are steady wisps, three in ten puff and the rest hiss now and then.</summary>
        static VentKind PickVent(float roll) => roll < 0.5f ? VentKind.Wisp : roll < 0.8f ? VentKind.Puff : VentKind.Hiss;

        /// <summary>Volcanic, dry, gentle ground (and its strongest biome), counting the misses.</summary>
        static bool FissureGround(in TerrainSource source, List<int> volcanic, float2 xz, out int biome)
        {
            var g = source.SampleGround(xz);
            biome = g.Biomes.Ids.x;
            float share = 0f, best = -1f;
            for (int k = 0; k < 4; k++)
            {
                if (volcanic.Contains(g.Biomes.Ids[k]))
                    share += g.Biomes.Weights[k];
                if (g.Biomes.Weights[k] > best)
                {
                    best = g.Biomes.Weights[k];
                    biome = g.Biomes.Ids[k];
                }
            }
            if (share < FissureVolcanic)
            {
                VentMisses[0]++;
                return false;
            }
            return DryGentle(source, xz);
        }

        /// <summary>Above the sea, out of water and cave mouths, and no steeper than about 37 degrees.</summary>
        static bool DryGentle(in TerrainSource source, float2 xz)
        {
            var g = source.SampleGround(xz);
            if (g.Height < source.World.SeaLevel + 1f || g.HasWater && g.WaterSurface > g.Height - 0.05f || source.IsCaveHole(xz))
            {
                VentMisses[1]++;
                return false;
            }
            if (source.SampleNormal(xz, 1f).y < 0.8f)
            {
                VentMisses[2]++;
                return false;
            }
            return true;
        }

        /// <summary>Within a landmark's ground or a town (with a margin): those keep their own look.</summary>
        static bool NearPlace(in TerrainSource source, float2 xz)
        {
            if (source.Landmarks.IsCreated)
            {
                ref var set = ref source.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                    if (math.distance(set.Instances[i].Position, xz) < set.Types[set.Instances[i].Type].Radius * set.Instances[i].Scale + 8f)
                        return true;
            }
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value.Settlements;
                for (int i = 0; i < towns.Length; i++)
                    if (math.distance(towns[i].Centre, xz) < towns[i].Radius + 20f)
                        return true;
            }
            return false;
        }

        /// <summary>A spring's vents bubble up by water: a type with bubbles that don't glow (not lava).</summary>
        static bool IsHotSpring(LandmarkType type)
        {
            foreach (var e in type.Effects)
                if (e != null && e.Kind == LandmarkEffectKind.Bubbles && e.Glow <= 0f)
                    return true;
            return false;
        }

        /// <summary>Another vent within this distance (flat, or in 3D).</summary>
        static bool Crowded(List<CaveVent> vents, float3 p, float spacing, bool flat)
        {
            foreach (var v in vents)
                if ((flat ? math.distance(v.Position.xz, p.xz) : math.distance(v.Position, p)) < spacing)   // sim: bake spacing
                {
                    VentMisses[4]++;
                    return true;
                }
            return false;
        }

        /// <summary>A cave floor under a point: dry, not lava, with headroom for the smoke.</summary>
        static bool CaveFloor(in TerrainSource source, float3 above, out float3 floor)
        {
            var y = source.FloorUnder(above, out var inCave);
            floor = new float3(above.x, y, above.z);
            if (!inCave || source.CaveDistance(floor + new float3(0f, 0.6f, 0f)) >= 0f || WaterOver(source, floor, y) > y + 0.01f ||
                source.LavaAt(floor + new float3(0f, 0.05f, 0f)) > 0.5f || source.CeilingOver(floor + new float3(0f, 0.6f, 0f)) - y < 2.5f)
            {
                VentMisses[5]++;
                return false;
            }
            return true;
        }
    }
}
