using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>W-142 (#262): grit trickles from cave ceilings, hashed per cave node so the same spots fall on every visit.</summary>
    public partial class LandmarkEffectSystem
    {
        const float CeilingShare = 0.3f;
        const float GritSize = 0.06f;
        const float CeilingMaxRange = 40f;
        static readonly Color Grit = new Color(0.95f, 0.88f, 0.76f), Frost = new Color(0.82f, 0.9f, 0.96f);

        readonly object ceilingOwner = new object();
        readonly List<Kit.Plume> ceilingGrit = new List<Kit.Plume>();
        Color[] gritOf;
        float3 ceilingAt = new float3(float.MaxValue);
        float nextCeiling;

        void CeilingTables(BiomePalette palette)
        {
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            gritOf = new Color[styles.Count];
            for (int i = 0; i < styles.Count; i++)
                gritOf[i] = styles[i] == null || styles[i].LavaPools ? Color.clear : styles[i].Stone == StoneKind.Ice ? Frost : Grit;
            ceilingAt = new float3(float.MaxValue);
        }

        void CeilingFalls(in TerrainSource source, Vector3 eye)
        {
            if (!source.Caves.IsCreated || gritOf == null)
                return;
            if (Time.unscaledTime < nextCeiling && math.distancesq(ceilingAt, (float3)eye) < 16f)
                return;
            nextCeiling = Time.unscaledTime + 3f;
            ceilingAt = eye;
            var gone = Kit.Leaves.Effect != null ? Kit.Leaves.Effect.GonePixels : 2f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(GritSize, gone), CeilingMaxRange) + 4f;
            ceilingGrit.Clear();
            ref var set = ref source.Caves.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                if (cave.Style <= 0 || cave.Style > gritOf.Length || gritOf[cave.Style - 1].a <= 0f)
                    continue;
                if (math.any((float3)eye + range < cave.Min) || math.any((float3)eye - range > cave.Max))
                    continue;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    var node = set.Nodes[n];
                    if (math.distance(node.Position, eye) > range + node.Radius)
                        continue;
                    var h = Scramble(math.hash(new int2(n, 0xCE11)));
                    if ((h & 0xFFFF) / 65536f >= CeilingShare)
                        continue;
                    var r = Scramble(h);
                    var angle = (r & 0xFFFF) / 65536f * 6.2832f;
                    var xz = node.Position.xz + new float2(math.cos(angle), math.sin(angle)) * node.Radius * 0.5f * ((r >> 16) & 0xFF) / 255f;
                    if (GritSpot(source, new float3(xz.x, node.Position.y, xz.y), node.Radius * 3f, Scramble(r), gritOf[cave.Style - 1], out var plume) &&
                        math.distance(plume.Position, eye) <= range)
                        ceilingGrit.Add(plume);
                }
            }
            Kit.Leaves.Set(ceilingOwner, ceilingGrit);
        }

        static bool GritSpot(in TerrainSource source, float3 from, float reach, uint h, Color tint, out Kit.Plume plume)
        {
            plume = default;
            if (source.CaveDistance(from) >= 0f)
                return false;
            var ceiling = from.y;
            while (ceiling < from.y + reach && source.CaveDistance(new float3(from.x, ceiling + 0.25f, from.z)) < 0f)
                ceiling += 0.25f;
            var floor = from.y;
            while (floor > from.y - reach && source.CaveDistance(new float3(from.x, floor - 0.25f, from.z)) < 0f)
                floor -= 0.25f;
            var drop = ceiling - floor;
            if (ceiling >= from.y + reach || drop < 1.5f)
                return false;
            var a = (h & 0xFF) / 255f;
            var col = (tint * (0.8f + 0.35f * a)).linear;
            plume = new Kit.Plume
            {
                Position = new float3(from.x, ceiling - 0.05f, from.z),
                Seed = ((h >> 8) & 0xFFFF) / 65536f,
                Color = new float4(col.r, col.g, col.b, 0f),
                Shape = new float4(0f, 0.15f + 0.25f * ((h >> 24) & 0xF) / 15f, GritSize * (0.8f + 0.4f * a), drop / (2.2f + 1.2f * a)),
                Timing = new float4(0f, 0f, 0.9f, 10 + (int)((h >> 28) % 4u)),
                Drop = drop - 0.05f,
                UnderRock = 1f,
                Fall = new float4(1f, 0f, 0f, 0f)
            };
            return true;
        }

        void ClearCeilingFalls()
        {
            Kit.Leaves.Set(ceilingOwner, null);
            ceilingAt = new float3(float.MaxValue);
        }
    }
}
