using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-122: places the marsh folk's (lizardfolk) stilt villages after every other landmark, on their own spots: up to
    /// <see cref="Villages"/> of them in the biome tagged BiomeTags.MarshFolk (the Foggy marsh), clear of every landmark's
    /// footprint and of each other, with their reed huts on stilts round a smoke hut over the water.
    /// W-128: a village stands in natural shallow water, a lake or the sea, never a pool it digs for itself
    /// (it used to flatten a dry spot and dig one): the spot whose middle and hut ring are most fully covered by water
    /// <see cref="ShallowLo"/>..<see cref="ShallowHi"/> m deep. Nothing else moves; on an island without that biome, or
    /// with no such water in it, there are simply none.
    /// </summary>
    public static class MarshFolkBaking
    {
        public const string TypeName = "MarshFolkVillage";
        const int Villages = 2;
        /// <summary>W-128: the water a stilt village stands in (m deep), and the grid its middle is searched on (m).</summary>
        const float ShallowLo = 0.4f, ShallowHi = 3f, Cell = 10f, Extent = 5120f;
        /// <summary>W-128: how far round a spot the water is tested (the huts stand at StructureKit.MarshRing), how many of
        /// the 16 ring points must be water, and how far a village keeps from other landmarks past their radius (m).</summary>
        const float RingOuter = 22f, RingInner = 12f, Footprint = 50f;
        const int RingNeeded = 14;
        /// <summary>W-128: the landing walk runs out from this ring to that one (landmark radii; FaunaDirector.MarshWalks),
        /// and its far end must stand on ground or water shallower than BankDepth (m), so the village can be walked to.</summary>
        const float LandingFrom = 0.5f, LandingTo = 1.05f, BankDepth = 0.3f;

        public static LandmarkType LoadType() => Resources.Load<LandmarkType>(TypeName);

        /// <summary>W-128: per grid cell, how many of its 16 ring points are natural shallow water (-1: its middle isn't,
        /// or it's outside the marsh biome).</summary>
        [BurstCompile]
        struct WaterJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int Size, Biome;
            [WriteOnly] public NativeArray<sbyte> Score;

            public void Execute(int i)
            {
                var p = new float2(i % Size, i / Size) * Cell - Extent;
                Score[i] = -1;
                var g = Source.SampleGround(p);
                var weight = 0f;
                for (int k = 0; k < 4; k++)
                    if (g.Biomes.Ids[k] == Biome)
                        weight += g.Biomes.Weights[k];
                if (weight < 0.5f || !Shallow(p))
                    return;
                sbyte n = 0;
                for (int a = 0; a < 16; a++)
                {
                    var angle = a * math.PI / 8f;
                    if (Shallow(p + ((a & 1) == 0 ? RingOuter : RingInner) * new float2(math.cos(angle), math.sin(angle))))
                        n++;
                }
                Score[i] = n;
            }

            /// <summary>Lake or sea water ShallowLo..ShallowHi deep, not a river and not a landmark's own pool.</summary>
            bool Shallow(float2 p)
            {
                var g = Source.SampleGround(p);
                var depth = g.HasWater ? g.WaterSurface - g.Height : -99f;
                if (depth < ShallowLo || depth > ShallowHi || g.River > 0.02f)
                    return false;
                LandmarkSampling.PoolWater(ref Source.Landmarks.Value, p, out var claim);
                if (claim > 0f)
                    return false;
                var lake = RiverSampling.Lake(ref Source.Rivers.Value, p, out _, out _) > 0.5f;
                var sea = Source.OceanFactor(p) > 0.5f && g.Height < Source.World.SeaLevel;
                return lake || sea;
            }
        }

        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types)
        {
            var result = new List<LandmarkInstance>();
            var t = -1;
            for (int i = 0; i < types.Count; i++)
                if (types[i] != null && types[i].name == TypeName)
                    t = i;
            var biome = BiomeTags.First(palette, BiomeTags.MarshFolk);
            if (t < 0 || biome < 0 || !source.Landmarks.IsCreated)
                return result;
            var size = (int)(2f * Extent / Cell) + 1;
            var score = new NativeArray<sbyte>(size * size, Allocator.TempJob);
            new WaterJob { Source = source, Size = size, Biome = biome, Score = score }.Schedule(size * size, 256).Complete();
            var cells = score.ToArray();
            score.Dispose();
            // Best covered first; ties by grid order, so every machine picks the same spots.
            var order = new List<int>();
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] >= RingNeeded)
                    order.Add(i);
            order.Sort((a, b) => cells[a] != cells[b] ? cells[b].CompareTo(cells[a]) : a.CompareTo(b));
            ref var set = ref source.Landmarks.Value;
            foreach (var c in order)
            {
                if (result.Count >= Villages)
                    break;
                var at = new float2(c % size, c / size) * Cell - Extent;
                if (!SaltTowerBaking.Clear(ref set, types, at, Footprint) || !SaltTowerBaking.ClearOf(result, types, at, Footprint))
                    continue;
                var key = math.hash(new uint3(math.asuint(at.x), math.asuint(at.y), 0x3A75u)) | 1u;
                var spin = BiomeSampling.Scramble(key);
                // The landing walk (StructureKit.MarshWalk, at the 30 deg spot past the hut ring) must reach the bank:
                // turn the village from its drawn facing in 16 steps until the walk starts on water and ends on land.
                var drawn = (spin & 0xFFFF) * (2f * math.PI / 65536f);
                var facing = drawn;
                var landed = false;
                for (int k = 0; k < 16 && !landed; k++)
                {
                    facing = drawn + k * (math.PI / 8f);
                    var turned = new LandmarkInstance { Position = at, Facing = new float2(math.cos(facing), math.sin(facing)) };
                    var start = source.SampleGround(LandmarkSampling.ToWorld(turned, LandmarkLayout.Spot(LandingFrom, 30f), types[t].Radius));
                    var end = source.SampleGround(LandmarkSampling.ToWorld(turned, LandmarkLayout.Spot(LandingTo, 30f), types[t].Radius));
                    landed = start.HasWater && start.WaterSurface - start.Height >= ShallowLo &&
                             (!end.HasWater || end.WaterSurface - end.Height < BankDepth);
                }
                if (!landed)
                    continue;
                // Base: the water line, where the huts' decks are measured from (they float on it, Floats).
                var ground = source.SampleGround(at);
                var inst = new LandmarkInstance
                {
                    Position = at,
                    Facing = new float2(math.cos(facing), math.sin(facing)),
                    Scale = 1f,
                    Type = t,
                    BaseHeight = ground.WaterSurface,
                    Seed = (BiomeSampling.Scramble(spin) >> 8) * (1f / 16777216f),
                    Key = key
                };
                result.Add(inst);
                Debug.Log($"[landmarks] {TypeName} {result.Count} at {at.x:F1},{at.y:F1}: facing {math.degrees(facing):F0} deg, water {ground.WaterSurface:F1} " +
                          $"({ground.WaterSurface - ground.Height:F1} m deep), {cells[c]}/16 of its ring in water");
            }
            if (result.Count == 0)
                Debug.Log($"[landmarks] {TypeName}: no natural shallow water in the marsh, no village");
            return result;
        }
    }
}
