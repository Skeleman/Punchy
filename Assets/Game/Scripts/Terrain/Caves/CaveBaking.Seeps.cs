using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-142 row 3 (#262): lava seeps. W-141's slit and streak in lava: a slow glowing ooze out of a crack in a steep
    /// wall, a dark cooling crust round it, dripping into a lava pool or onto a small glowing puddle at the wall's foot.
    /// Only in hot caves (a style with lava pools: one to three, most by the pools) and caves under volcanic ground (none
    /// or one), so they stay rarer than water trickles. They are CaveTrickle records with Lava set, so the trickles' wall
    /// grid, rock painting and drawing carry them. Placed after the vents against the caves as built (rule 22: plain
    /// records from each cave's own seed, every draw of a try made before any test can skip it); appended after the
    /// water trickles, so those keep their indices. They carve nothing, so nothing placed before them moves.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>How much of the ground's biome blend over a cave's nodes must be volcanic for it to seep.</summary>
        const float SeepVolcanic = 0.6f;

        static void PlaceSeeps(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<CaveData> caves, List<CaveNode> nodes,
                               List<CaveNodeKind> kinds, List<CaveStyle> styles, Extras extras)
        {
            System.Array.Clear(TrickleMisses, 0, TrickleMisses.Length);
            var byKind = new int[4];
            int hotCaves = 0, volcanicCaves = 0, hot = 0, warm = 0;
            var log = new System.Text.StringBuilder();
            var volcanic = BiomeTags.InFamily(palette, BiomeFamily.Volcanic);
            var spots = new List<float3>();
            var keepClear = new List<float3>();
            var first = extras.Trickles.Count;
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel != 0 || cave.NodeCount == 0)
                    continue;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var isHot = style != null && style.LavaPools;
                if (!isHot && (volcanic.Count == 0 || VolcanicShare(source, volcanic, cave, nodes) < SeepVolcanic))
                    continue;
                // gameplay: rarer than water trickles (1-3 in a hot cave, 0-1 under volcanic ground)
                var rng = new Random(math.hash(new uint2(cave.Key, 0x5EE9u)) | 1u);
                var want = isHot ? rng.NextInt(1, 4) : rng.NextInt(0, 2);

                // Hot spots: the cave's lava pools, which a seep drips into when it is right there.
                spots.Clear();
                for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount; p++)
                    if (extras.Pools[p].Lava >= 0.5f)
                        spots.Add(extras.Pools[p].Centre);
                keepClear.Clear();
                foreach (var plant in extras.Plants)
                    if (math.all(plant.Position >= cave.Min - 2f) && math.all(plant.Position <= cave.Max + 2f))
                        keepClear.Add(plant.Position);

                var before = extras.Trickles.Count;
                for (int attempt = 0; attempt < want * TrickleTries && extras.Trickles.Count - before < want; attempt++)
                {
                    // Every draw of the try first, so a miss never shifts the next try's draws.
                    var byPool = rng.NextFloat();
                    var spot = rng.NextInt(math.max(spots.Count, 1));
                    var node = cave.FirstNode + rng.NextInt(cave.NodeCount);
                    var angle = rng.NextFloat(math.PI * 2f);
                    var shift = rng.NextFloat2(-1f, 1f);
                    var height = rng.NextFloat();
                    var shape = rng.NextFloat();
                    var threads = rng.NextInt(2, 4);
                    var size = rng.NextFloat();
                    var flow = rng.NextFloat(0.4f, 1f);
                    var seed = rng.NextFloat();

                    float3 from;
                    if (spots.Count > 0 && byPool < 0.7f)
                        from = spots[spot] + new float3(shift.x, 0f, shift.y) * 2f;
                    else
                    {
                        if (kinds[node] == CaveNodeKind.Shaft)
                            continue;
                        from = nodes[node].Position + new float3(shift.x, 0f, shift.y) * nodes[node].Radius * cave.Profile.x * 0.4f;
                    }
                    var dir = new float2(math.cos(angle), math.sin(angle));
                    // Lava oozes: mostly one thick thread or a sheet, now and then a fan or drops off an overhang. Seeps
                    // sit low (the lower half of the slit band), where the heat is.
                    var pick = shape < 0.4f ? TrickleKind.Thread : shape < 0.55f ? TrickleKind.Fan : shape < 0.85f ? TrickleKind.Sheet : TrickleKind.Overhang;
                    if (TryTrickle(source, extras, c, cave, from, dir, height * 0.5f, pick, threads, size, flow, seed, keepClear, true, out var seep))
                    {
                        extras.Trickles.Add(seep);
                        byKind[(int)seep.Kind]++;
                    }
                }
                var placed = extras.Trickles.Count - before;
                if (isHot) { hotCaves++; hot += placed; }
                else { volcanicCaves++; warm += placed; }
                if (placed > 0)
                    log.Append($" {c}{(isHot ? "h" : "v")}:{placed}");
            }
            UnityEngine.Debug.Log($"[cave-seeps] kept {extras.Trickles.Count - first}: hot caves {hotCaves} ({hot}), volcanic caves {volcanicCaves} ({warm}); " +
                                  $"thread {byKind[0]}, fan {byKind[1]}, sheet {byKind[2]}, overhang {byKind[3]}; misses: floor {TrickleMisses[0]}, " +
                                  $"wall {TrickleMisses[1]}, steep {TrickleMisses[2]}, low {TrickleMisses[3]}, broken {TrickleMisses[4]}, crowded {TrickleMisses[5]}; per cave{log}");
        }

        /// <summary>The volcanic share of the ground's biome blend, averaged over a cave's nodes.</summary>
        static float VolcanicShare(in TerrainSource source, List<int> volcanic, in CaveData cave, List<CaveNode> nodes)
        {
            var share = 0f;
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
            {
                var g = source.SampleGround(nodes[n].Position.xz);
                for (int k = 0; k < 4; k++)
                    if (volcanic.Contains(g.Biomes.Ids[k]))
                        share += g.Biomes.Weights[k];
            }
            return share / cave.NodeCount;
        }
    }
}
