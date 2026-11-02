using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Flora
{
    /// <summary>
    /// One model's walkable tops: a grid over its footprint (model space, scale 1) holding, per cell, up to
    /// SupportGrids.Layers heights of the upward-facing surfaces there (a well's roof above its rim, a fountain's bowl
    /// above its rim), highest first, NaN where there's none; and, for fountains, the water surface. Built from the
    /// visible mesh (FloraMeshBuilder.SupportGrid), so tops are where they look.
    /// </summary>
    public struct SupportGridInfo
    {
        public float2 Min;
        public float Cell;
        public int2 Res;
        /// <summary>First value in SupportGridSet.Values (Res.x × Res.y × Layers), and in Water (Res.x × Res.y), or -1.</summary>
        public int First, WaterFirst;
        /// <summary>How far from the model's origin any part of the grid reaches (model space).</summary>
        public float Reach;
        /// <summary>The largest gap between the grid and the mesh found when it was built (m, model space).</summary>
        public float Error;
    }

    public struct SupportGridSet
    {
        public BlobArray<SupportGridInfo> Grids;
        public BlobArray<float> Values;
        public BlobArray<float> Water;
    }

    /// <summary>Every model's support grid (FloraSystem builds it with the models), as a singleton.</summary>
    public struct SupportGrids : IComponentData
    {
        public const int Layers = 3;
        public BlobAssetReference<SupportGridSet> Set;
    }

    /// <summary>
    /// A placed prop or building with walkable tops near a player (FloraSystem refreshes them round every player, local
    /// and remote): where it stands, how it's turned and scaled, and its model's grid in SupportGrids.
    /// </summary>
    public struct FloraSupport : IBufferElementData
    {
        public float3 Position;
        public float Yaw;
        public float Scale;
        public int Grid;
    }

    /// <summary>A surface to stand on, found by PropSupport.</summary>
    public struct SupportHit
    {
        /// <summary>World height of the surface.</summary>
        public float Height;
        public float3 Normal;
        /// <summary>Standing in a fountain's water: its surface is WaterHeight.</summary>
        public bool Wading;
        public float WaterHeight;
        /// <summary>Ledge only: the point on the top's rim nearest the climber, at the top's height, and the rim's outward
        /// normal (towards the climber, in world XZ), where the hands go and the body faces.</summary>
        public float3 Edge;
        public float2 EdgeNormal;
    }

    /// <summary>
    /// Standing on things: the walkable tops of rocks, roofs, rims, benches and the rest near the players (FloraSupport,
    /// SupportGrids). Burst-safe; a short loop over the props near the players. Seed-derived, so every machine agrees.
    /// Callers take the higher of this and the ground (TerrainSource.FloorUnder).
    /// </summary>
    public static class PropSupport
    {
        /// <summary>
        /// The highest surface under a body of this radius at <paramref name="feet"/> whose top is at most
        /// <paramref name="stepUp"/> above the feet (so nothing far overhead catches it: eaves, arches, a well's roof).
        /// </summary>
        public static bool Sample(ref SupportGridSet set, in DynamicBuffer<FloraSupport> props, float3 feet, float radius, float stepUp, out SupportHit hit)
        {
            hit = default;
            hit.Height = float.MinValue;
            var found = false;
            var limit = feet.y + stepUp;
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                ref var g = ref set.Grids[p.Grid];
                if (math.distancesq(p.Position.xz, feet.xz) > math.square(g.Reach * p.Scale + radius))
                    continue;
                // The middle and four points round the body's edge: the highest surface any of them finds.
                for (int k = 0; k < 5; k++)
                {
                    var at = feet.xz + (k == 0 ? float2.zero : new float2(k == 1 ? 1 : k == 2 ? -1 : 0, k == 3 ? 1 : k == 4 ? -1 : 0) * radius * 0.7f);
                    var local = Local(p, at);
                    if (!Below(ref set, ref g, local, (limit - p.Position.y) / p.Scale, out var top))
                        continue;
                    var world = p.Position.y + top * p.Scale;
                    if (world > hit.Height)
                    {
                        hit.Height = world;
                        hit.Normal = Normal(ref set, ref g, local, top, p.Yaw);
                        found = true;
                    }
                }
                // Fountain water over whatever the body stands on.
                if (g.WaterFirst >= 0)
                {
                    var local = Local(p, feet.xz);
                    var water = WaterAt(ref set, ref g, local);
                    if (!float.IsNaN(water))
                    {
                        hit.Wading = true;
                        hit.WaterHeight = p.Position.y + water * p.Scale;
                    }
                }
            }
            if (hit.Wading && found && hit.Height >= hit.WaterHeight)
                hit.Wading = false;
            return found;
        }

        /// <summary>
        /// A wall: the highest surface at <paramref name="at"/> between a step up and head height (so too high to step
        /// onto and in the way), e.g. a fountain's rim seen from outside, a boulder, a house's eaves at head height.
        /// </summary>
        public static bool Wall(ref SupportGridSet set, in DynamicBuffer<FloraSupport> props, float3 at, float stepUp, float headHeight, out float wallTop)
        {
            wallTop = float.MinValue;
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                ref var g = ref set.Grids[p.Grid];
                if (math.distancesq(p.Position.xz, at.xz) > math.square(g.Reach * p.Scale))
                    continue;
                var local = Local(p, at.xz);
                if (!Cell(ref g, local, out var c))
                    continue;
                for (int l = 0; l < SupportGrids.Layers; l++)
                {
                    var v = set.Values[g.First + c * SupportGrids.Layers + l];
                    if (float.IsNaN(v))
                        break;
                    var world = p.Position.y + v * p.Scale;
                    if (world > at.y + stepUp && world < at.y + headHeight)
                        wallTop = math.max(wallTop, world);
                }
            }
            return wallTop > float.MinValue;
        }

        /// <summary>
        /// A ledge to climb onto ahead: the first surface along <paramref name="forward"/> (within reach) that is more
        /// than a step and at most <paramref name="maxRise"/> above the feet, with headroom on it.
        /// </summary>
        public static bool Ledge(ref SupportGridSet set, in DynamicBuffer<FloraSupport> props, float3 feet, float2 forward, float reach, float stepUp, float maxRise, out SupportHit hit)
        {
            hit = default;
            for (var d = 0.3f; d <= reach; d += 0.2f)
            {
                var at = feet + new float3(forward.x, 0f, forward.y) * d;
                if (Sample(ref set, props, at, 0f, maxRise, out var top) && top.Height > feet.y + stepUp)
                {
                    // Headroom: nothing over the ledge within a body's height.
                    if (Wall(ref set, props, new float3(at.x, top.Height, at.z), 0.05f, 1.7f, out _))
                        return false;
                    // The rim: between the last spot short of the ledge and this one.
                    float near = d - 0.2f, far = d;
                    for (int k = 0; k < 6; k++)
                    {
                        var mid = (near + far) * 0.5f;
                        var probe = feet + new float3(forward.x, 0f, forward.y) * mid;
                        if (Sample(ref set, props, probe, 0f, maxRise, out var h) && h.Height > feet.y + stepUp)
                            far = mid;
                        else
                            near = mid;
                    }
                    hit = top;
                    hit.Edge = feet + new float3(forward.x, 0f, forward.y) * far;
                    hit.Edge.y = top.Height;
                    hit.EdgeNormal = -math.normalizesafe(forward);
                    return true;
                }
            }
            return false;
        }

        static float2 Local(in FloraSupport p, float2 at)
        {
            // Placed models are turned by yaw (model +Z to (-sin, cos)); undo it and the scale.
            var d = (at - p.Position.xz) / p.Scale;
            float c = math.cos(p.Yaw), s = math.sin(p.Yaw);
            return new float2(d.x * c + d.y * s, -d.x * s + d.y * c);
        }

        static bool Cell(ref SupportGridInfo g, float2 local, out int index)
        {
            var c = (int2)math.floor((local - g.Min) / g.Cell);
            index = c.y * g.Res.x + c.x;
            return math.all(c >= 0) && math.all(c < g.Res);
        }

        /// <summary>The highest layer at a point at or below a height (model space), smoothed with its neighbours.</summary>
        static bool Below(ref SupportGridSet set, ref SupportGridInfo g, float2 local, float limit, out float top)
        {
            top = float.MinValue;
            if (!Cell(ref g, local, out var c))
                return false;
            for (int l = 0; l < SupportGrids.Layers; l++)
            {
                var v = set.Values[g.First + c * SupportGrids.Layers + l];
                if (float.IsNaN(v))
                    break;
                if (v <= limit)
                {
                    top = Smooth(ref set, ref g, local, v);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Bilinear between the four nearest cells' layers close to a height, where they all have one.</summary>
        static float Smooth(ref SupportGridSet set, ref SupportGridInfo g, float2 local, float near)
        {
            var f = (local - g.Min) / g.Cell - 0.5f;
            var i = (int2)math.floor(f);
            var t = f - i;
            float sum = 0f, weight = 0f;
            for (int k = 0; k < 4; k++)
            {
                var c = i + new int2(k & 1, k >> 1);
                if (math.any(c < 0) || math.any(c >= g.Res))
                    continue;
                var w = ((k & 1) == 1 ? t.x : 1f - t.x) * ((k >> 1) == 1 ? t.y : 1f - t.y);
                for (int l = 0; l < SupportGrids.Layers; l++)
                {
                    var v = set.Values[g.First + (c.y * g.Res.x + c.x) * SupportGrids.Layers + l];
                    if (float.IsNaN(v))
                        break;
                    if (math.abs(v - near) < 0.35f)
                    {
                        sum += v * w;
                        weight += w;
                        break;
                    }
                }
            }
            return weight > 0.5f ? sum / weight : near;
        }

        static float3 Normal(ref SupportGridSet set, ref SupportGridInfo g, float2 local, float top, float yaw)
        {
            var e = g.Cell;
            var hx0 = Near(ref set, ref g, local - new float2(e, 0f), top);
            var hx1 = Near(ref set, ref g, local + new float2(e, 0f), top);
            var hz0 = Near(ref set, ref g, local - new float2(0f, e), top);
            var hz1 = Near(ref set, ref g, local + new float2(0f, e), top);
            var n = math.normalize(new float3(hx0 - hx1, 2f * e, hz0 - hz1));
            // Back into the world: turn by yaw.
            float c = math.cos(yaw), s = math.sin(yaw);
            return new float3(n.x * c - n.z * s, n.y, n.x * s + n.z * c);
        }

        static float Near(ref SupportGridSet set, ref SupportGridInfo g, float2 local, float top)
        {
            if (!Cell(ref g, local, out var c))
                return top;
            for (int l = 0; l < SupportGrids.Layers; l++)
            {
                var v = set.Values[g.First + c * SupportGrids.Layers + l];
                if (float.IsNaN(v))
                    break;
                if (math.abs(v - top) < 0.6f)
                    return v;
            }
            return top;
        }

        static float WaterAt(ref SupportGridSet set, ref SupportGridInfo g, float2 local)
        {
            if (!Cell(ref g, local, out var c))
                return float.NaN;
            return set.Water[g.WaterFirst + c];
        }
    }
}
