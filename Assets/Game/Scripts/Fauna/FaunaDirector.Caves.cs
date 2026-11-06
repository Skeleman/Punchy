using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 4: the cave dwellers' spawns, moved out of FaunaDirector.cs as they were (the chamber folk are in
    // FaunaDirector.CaveFolk.cs).
    public partial class FaunaDirector
    {
        void SpawnCaveDwellers(in TerrainSource source)
        {
            if (!source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;
            scratch.Clear();
            foreach (var i in occupiedCaves)
                if (NearestPlayer(set.Caves[i].Mouth.xz) > DespawnDistance + 20f)
                    scratch.Add(i);
            foreach (var i in scratch)
                occupiedCaves.Remove(i);

            for (int i = 0; i < set.Caves.Length; i++)
            {
                ref var cave = ref set.Caves[i];
                if (occupiedCaves.Contains(i) || cave.NodeCount < 2 || NearestPlayer(cave.Mouth.xz) > Radius)
                    continue;
                if (caveCount >= MaxCave)
                    break;
                occupiedCaves.Add(i);
                caveGlows.Clear();
                SpawnCaveFromTable(source, ref set, ref cave, i);
            }
        }

        readonly List<int> caveFitting = new List<int>();

        /// <summary>
        /// Fills one cave from the cave table (A-80 row 3): now and then a den visitor (a bear, a wolf, a fox that lives
        /// round the mouth) in the outer half of the passage, moths round each glowing plant, and one pick per chamber
        /// from its chamber type's rows (pool, deepest, other), seeded by the cave and node so a chamber always holds
        /// the same kind. Chamber folk are SpawnChamberBands'.
        /// </summary>
        void SpawnCaveFromTable(in TerrainSource source, ref CaveSetBlob set, ref CaveData cave, int caveIndex)
        {
            if (caveTable == null)
                return;
            var biome = source.SampleGround(cave.Mouth.xz).Biomes.Dominant();
            var time = PhaseNow;
            // Chambers first: one seeded pick each from its chamber type's rows, so moths round a big cave's hundreds of
            // glowing plants never use up the cave cap before the chambers are filled.
            var deepest = DeepestChamber(ref set, ref cave);
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount && caveCount < MaxCave; n++)
            {
                var spot = ChamberSpot(ref set, ref cave, n, deepest);
                if (spot < 0)
                    continue;
                var socket = CaveSocket(spot, ref cave, n, biome, time);
                var pick = caveTable.PickRow(socket);
                var sp = pick >= 0 ? SpeciesIndex(caveTable.Rows[pick].Species) : -1;
                if (sp < 0)
                    continue;
                var count = caveTable.CountOf(pick, socket);
                int before = caveCount;
                for (int k = 0; k < count && caveCount < MaxCave; k++)
                    CaveFloorSpawn(source, sp, set.Nodes[n], 0.6f, SpawnTier.Cave);
                if (caveFillLogs < 12)
                    caveFill.Append($" {(CaveSpot)spot} node {n - cave.FirstNode} {species[sp].name} {caveCount - before}/{count} at {(int3)set.Nodes[n].Position} r{set.Nodes[n].Radius:0};");
            }
            // Den visitors: every Passage row that fits, each on its own chance (the species' cave affinity) where it lives.
            caveTable.AllFitting(CaveSocket((int)CaveSpot.Passage, ref cave, cave.FirstNode, biome, time), caveFitting);
            foreach (var ri in caveFitting)
            {
                var row = caveTable.Rows[ri];
                var sp = SpeciesIndex(row.Species);
                if (sp < 0 || rng.NextFloat() >= species[sp].CaveAffinity || !LivesAt(source, sp, cave.Mouth.xz))
                    continue;
                var count = rng.NextInt(row.Count.x, math.max(row.Count.x, row.Count.y) + 1);
                for (int k = 0; k < count && caveCount < MaxCave; k++)
                {
                    var node = set.Nodes[cave.FirstNode + rng.NextInt(1, math.max(2, cave.NodeCount / 2))];
                    CaveFloorSpawn(source, sp, node, 0.5f, SpawnTier.Wild);
                }
            }
            // Moths: every Glow row, half a group of each round each of the cave's glowing plants (none without any).
            caveTable.AllFitting(CaveSocket((int)CaveSpot.Glow, ref cave, cave.FirstNode, biome, time), caveFitting);
            if (caveFitting.Count > 0 && source.Anchors.IsCreated)
            {
                ref var anchors = ref source.Anchors.Value;
                for (int a = 0; a < anchors.Anchors.Length; a++)
                    if (anchors.Anchors[a].Kind == AnchorKind.Glow && anchors.Anchors[a].InCave && anchors.Anchors[a].OwnerIndex == caveIndex)
                        caveGlows.Add(a);
                foreach (var ri in caveFitting)
                {
                    var sp = SpeciesIndex(caveTable.Rows[ri].Species);
                    if (sp < 0)
                        continue;
                    var body = species[sp].Body;
                    var count = math.max(1, rng.NextInt(math.max(1, body.GroupSize.x), math.max(body.GroupSize.x, body.GroupSize.y) + 1) / 2);
                    // (The cap holds within a cave too: a big cave has hundreds of glowing plants.)
                    foreach (var a in caveGlows)
                        for (int k = 0; k < count && caveCount < MaxCave; k++)
                            Enqueue(sp, anchors.Anchors[a].Position + new float3(rng.NextFloat(-0.6f, 0.6f), rng.NextFloat(0.4f, 1.4f), rng.NextFloat(-0.6f, 0.6f)), SpawnTier.Cave);
                }
            }
            if (caveFillLogs < 12)
            {
                caveFillLogs++;
                Debug.Log($"[spawn tables] cave {caveIndex} filled from the cave table ({time}, cave cap {caveCount}/{MaxCave}):{caveFill}");
            }
            caveFill.Clear();
        }

        // (The first few caves' chamber picks, for the log.)
        readonly System.Text.StringBuilder caveFill = new System.Text.StringBuilder();
        int caveFillLogs;

        /// <summary>Spawns one on the floor near a cave node (within a share of its radius), if really under the rock there.</summary>
        void CaveFloorSpawn(in TerrainSource source, int sp, CaveNode node, float spread, SpawnTier tier)
        {
            var at = node.Position + new float3(rng.NextFloat(-spread, spread), 0f, rng.NextFloat(-spread, spread)) * node.Radius;
            var floor = source.FloorUnder(at, out var inCave);
            // (Really under the rock: not where a passage opens to the sky.)
            if (inCave && source.CaveDistance(new float3(at.x, floor + 0.3f, at.z)) < -0.05f)
                Enqueue(sp, new float3(at.x, floor, at.z), tier);
        }

        /// <summary>Whether a species is among the animals of the biome at a point.</summary>
        bool LivesAt(in TerrainSource source, int speciesIndex, float2 at)
        {
            var biome = source.SampleGround(at).Biomes.Dominant();
            if (biome < 0 || biome >= biomes.Length || biomes[biome] == null)
                return false;
            foreach (var f in biomes[biome].Fauna)
                if (f.Species != null && f.Species.name == species[speciesIndex].name)
                    return true;
            return false;
        }
    }
}
