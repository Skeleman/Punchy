using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-142 (#262): leaves from the vegetated ground above sift into caves, down hanging roots and grassy shafts,
    /// hashed per plant or shaft so the same spots fall on every visit.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        const float CaveLeafMaxRange = 40f;
        const float ShaftLeafReach = 40f;

        public static int LastCaveLeaves;

        readonly object caveLeafOwner = new object();
        readonly List<Kit.Plume> caveLeaves = new List<Kit.Plume>();
        readonly HashSet<int> rootSpecies = new HashSet<int>();
        readonly Dictionary<int, Color> rootGreen = new Dictionary<int, Color>();
        float3 caveLeavesAt = new float3(float.MaxValue);
        float nextCaveLeaves;

        void CaveLeafTables(BiomePalette palette)
        {
            rootSpecies.Clear();
            rootGreen.Clear();
            var species = CaveFloraTable.Collect(palette.Landmarks);
            foreach (var style in CaveStyleTable.Collect(palette.Landmarks))
            {
                if (style == null)
                    continue;
                foreach (var f in style.Flora)
                    if (f != null && f.Hanging && f.Species != null && species.IndexOf(f.Species) is var i and >= 0 && rootSpecies.Add(i))
                        rootGreen[i] = f.Species.Foliage;
            }
            caveLeavesAt = new float3(float.MaxValue);
        }

        void CaveLeaves(in TerrainSource source, Vector3 eye)
        {
            if (!source.Caves.IsCreated)
                return;
            if (Time.unscaledTime < nextCaveLeaves && math.distancesq(caveLeavesAt, (float3)eye) < 16f)
                return;
            nextCaveLeaves = Time.unscaledTime + 3f;
            caveLeavesAt = eye;
            var gone = Kit.Leaves.Effect != null ? Kit.Leaves.Effect.GonePixels : 2f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(LeafSize, gone), CaveLeafMaxRange) + 4f;
            caveLeaves.Clear();
            ref var set = ref source.Caves.Value;
            if (rootSpecies.Count > 0)
                for (int i = 0; i < set.Plants.Length; i++)
                {
                    var plant = set.Plants[i];
                    if (!rootSpecies.Contains(plant.Species) || math.distancesq(plant.Position, eye) > range * range)
                        continue;
                    var h = Scramble(math.hash(plant.Position) ^ 0x2007u);
                    var floor = FloorBelow(source, plant.Position - new float3(0f, 0.4f, 0f), 12f);
                    var drop = plant.Position.y - 0.1f - floor;
                    if (drop >= 1.5f)
                        caveLeaves.Add(CaveLeaf(plant.Position - new float3(0f, 0.1f, 0f), drop, 0.35f + 0.4f * plant.Scale, rootGreen[plant.Species], 7, h));
                }
            for (int n = 0; n + 1 < set.Kinds.Length; n++)
            {
                if (set.Kinds[n] != CaveNodeKind.Shaft || set.Kinds[n + 1] == CaveNodeKind.Shaft && math.distancesq(set.Nodes[n + 1].Position.xz, set.Nodes[n].Position.xz) < 1f)
                    continue;
                var node = set.Nodes[n];
                if (math.distance(node.Position.xz, ((float3)eye).xz) > range + node.Radius)
                    continue;
                var ground = source.SampleGround(node.Position.xz);
                if (ground.HasWater || source.SampleSurface(node.Position.xz, ground, source.SampleNormal(node.Position.xz, 1f)).Grass < 0.15f)
                    continue;
                var floor = FloorBelow(source, node.Position, ShaftLeafReach);
                var top = new float3(node.Position.x, math.min(ground.Height, node.Position.y + node.Radius * 2f), node.Position.z);
                var drop = top.y - floor;
                if (drop < 3f || math.abs(top.y - eye.y) > range + drop)
                    continue;
                // Deep shafts stack streams so the leaves stay as dense near the floor as at the lip.
                for (int k = 0, streams = math.max(1, (int)(drop / 10f)); k < streams; k++)
                    caveLeaves.Add(CaveLeaf(top, drop, node.Radius * 0.55f, Autumn[2], LeafParticles - 3, Scramble(math.hash(new int3(n, k, 0x5AF7)))));
            }
            LastCaveLeaves = caveLeaves.Count;
            Kit.Leaves.Set(caveLeafOwner, caveLeaves);
        }

        static float FloorBelow(in TerrainSource source, float3 from, float reach)
        {
            var floor = from.y;
            while (floor > from.y - reach && source.CaveDistance(new float3(from.x, floor - 0.25f, from.z)) < 0f)
                floor -= 0.25f;
            return floor;
        }

        static Kit.Plume CaveLeaf(float3 at, float drop, float width, Color green, int particles, uint h)
        {
            var r = Scramble(h);
            var col = Color.Lerp(green, Autumn[h % (uint)Autumn.Length], 0.5f + 0.35f * (r & 0xF) / 15f).linear;
            return new Kit.Plume
            {
                Position = at,
                Seed = ((r >> 8) & 0xFFFF) / 65536f,
                Color = new float4(col.r, col.g, col.b, 0f),
                Shape = new float4(0f, width, LeafSize * (0.85f + 0.3f * ((r >> 24) & 0xF) / 15f), drop / (0.7f + 0.3f * ((r >> 28) & 0xF) / 15f)),
                Timing = new float4(0f, 0f, 0.95f, particles + (int)((h >> 16) % 3u)),
                Drop = drop,
                UnderRock = 1f,
                Fall = new float4(0.5f, 0f, 0f, 0f)
            };
        }

        void ClearCaveLeaves()
        {
            Kit.Leaves.Set(caveLeafOwner, null);
            caveLeavesAt = new float3(float.MaxValue);
        }
    }
}
