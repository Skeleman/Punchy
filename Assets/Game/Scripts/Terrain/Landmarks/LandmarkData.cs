using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A landmark type's terrain settings, flattened for Burst.</summary>
    public struct LandmarkTypeData
    {
        public float Radius;
        public LandmarkShape Shape;
        public float Height, ShapeRadius, Edge, Flatten, Roughness;
        public int FirstPool, PoolCount;
        public int OverrideWater;
        /// <summary>rgb colour (linear), a murk.</summary>
        public float4 Water;
        public float WaterGlow;
        public float WaterLava;
        public int HasGround;
        public SurfaceBlobData Ground;
        public float GroundRadius;
        public float ClearRadius;
    }

    public struct LandmarkPoolData
    {
        public float2 Offset;   // in radii, landmark frame
        public float Radius;    // in radii
        public float Depth;     // metres
    }

    /// <summary>One placed landmark.</summary>
    public struct LandmarkInstance
    {
        public float2 Position;
        /// <summary>Unit vector of the landmark's +X axis in world XZ.</summary>
        public float2 Facing;
        public float Scale;
        public int Type;
        /// <summary>Natural ground height at the centre (what Flatten levels to).</summary>
        public float BaseHeight;
        public float Seed;
        /// <summary>
        /// A stable hash of where it came from (its candidate spot, or a hand-placed landmark's position): everything
        /// derived from a landmark (its cave, its town's layout, its anchors) seeds from this, not from its index, so
        /// adding or removing other landmarks doesn't reshuffle it.
        /// </summary>
        public uint Key;
    }

    /// <summary>
    /// Every landmark in the world, with a coarse grid so a point finds the few landmarks that reach it quickly
    /// (each landmark is listed in every cell its footprint touches).
    /// </summary>
    public struct LandmarkSetBlob
    {
        public BlobArray<LandmarkTypeData> Types;
        public BlobArray<LandmarkPoolData> Pools;
        public BlobArray<LandmarkInstance> Instances;
        public float2 GridOrigin;
        public float CellSize;
        public int2 GridSize;
        public BlobArray<int2> Cells;   // (first item, count)
        public BlobArray<int> Items;    // instance indices
    }

    /// <summary>Where a point lies relative to a landmark.</summary>
    public struct LandmarkHit
    {
        public int Instance, Type;
        /// <summary>Position in the landmark's frame (metres; X is its facing).</summary>
        public float2 Local;
        /// <summary>Distance from the centre in units of the (scaled) radius: 0 at the centre, 1 at the edge.</summary>
        public float Distance;
        public float Radius;
    }

    /// <summary>How landmarks reshape, flood and repaint the ground. Used by TerrainSource.</summary>
    public static class LandmarkSampling
    {
        /// <summary>The range of grid items (indices into Items) for the cell containing a point.</summary>
        public static int2 CellRange(ref LandmarkSetBlob set, float2 xz)
        {
            var c = (int2)math.floor((xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return int2.zero;
            return set.Cells[c.y * set.GridSize.x + c.x];
        }

        public static bool Hit(ref LandmarkSetBlob set, int instance, float2 xz, out LandmarkHit hit)
        {
            ref var inst = ref set.Instances[instance];
            var radius = set.Types[inst.Type].Radius * inst.Scale;
            var d = xz - inst.Position;
            hit = new LandmarkHit
            {
                Instance = instance,
                Type = inst.Type,
                Local = new float2(math.dot(d, inst.Facing), math.dot(d, new float2(-inst.Facing.y, inst.Facing.x))),
                Distance = math.length(d) / radius,
                Radius = radius
            };
            return hit.Distance < 1f;
        }

        /// <summary>The landmark nearest its centre at a point, if any reaches it.</summary>
        public static bool Find(ref LandmarkSetBlob set, float2 xz, out LandmarkHit hit)
        {
            hit = default;
            hit.Distance = 2f;
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
                if (Hit(ref set, set.Items[k], xz, out var h) && h.Distance < hit.Distance)
                    hit = h;
            return hit.Distance < 1f;
        }

        /// <summary>Converts a point given in a landmark's frame (in radii) to world XZ.</summary>
        public static float2 ToWorld(in LandmarkInstance inst, float2 localRadii, float radius)
        {
            var l = localRadii * radius;
            return inst.Position + inst.Facing * l.x + new float2(-inst.Facing.y, inst.Facing.x) * l.y;
        }

        /// <summary>0..1 fade over the outer third of the footprint, so landmarks blend into the land around them.</summary>
        static float Mask(float distance) => math.smoothstep(1f, 0.65f, distance);

        /// <summary>The shape's rise (0..1 of Height) at a point in the landmark's frame.</summary>
        static float Profile(ref LandmarkTypeData type, float2 local, float radius)
        {
            var d = math.length(local);
            var r = radius * type.ShapeRadius;
            switch (type.Shape)
            {
                case LandmarkShape.Dome:
                    var s = math.saturate(1f - d / r);
                    return s * s * (3f - 2f * s);
                case LandmarkShape.Mesa:
                    return math.smoothstep(r, r * (1f - type.Edge), d);
                case LandmarkShape.Cliff:
                    var w = math.max(radius * type.Edge * 0.5f, 0.5f);
                    return math.smoothstep(-w, w, local.x);
                case LandmarkShape.Volcano:
                {
                    // Concave flanks rising to a rim, then a bowl-shaped crater a quarter of the height deep.
                    var crater = r * type.Edge;
                    var flank = math.pow(math.saturate(1f - math.max(d, crater) / r), 1.4f);
                    var dip = d < crater ? 0.25f * (1f - (d / crater) * (d / crater)) : 0f;
                    var foot = math.saturate(1f - d / r);
                    return flank - dip + foot * 0.05f;
                }
                default:
                    return 0f;
            }
        }

        /// <summary>One of a landmark's pools: its centre at its water level, and its radius (for drawing it).</summary>
        public static void Pool(ref LandmarkSetBlob set, int instance, int index, out float3 centre, out float radius)
        {
            ref var inst = ref set.Instances[instance];
            ref var type = ref set.Types[inst.Type];
            ref var pool = ref set.Pools[type.FirstPool + index];
            var r = type.Radius * inst.Scale;
            var xz = ToWorld(inst, pool.Offset, r);
            centre = new float3(xz.x, PoolLevel(ref inst, ref type, ref pool, r), xz.y);
            radius = pool.Radius * r;
        }

        /// <summary>The water level of one of a landmark's pools.</summary>
        static float PoolLevel(ref LandmarkInstance inst, ref LandmarkTypeData type, ref LandmarkPoolData pool, float radius)
        {
            var centre = pool.Offset * radius;
            var distance = math.length(centre) / radius;
            return inst.BaseHeight + type.Height * inst.Scale * Profile(ref type, centre, radius) * Mask(distance) - 0.3f;
        }

        /// <summary>Ground height with landmarks' shapes, levelling, bumps and pool basins applied.</summary>
        public static float Shape(ref LandmarkSetBlob set, float2 xz, float h)
        {
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                if (!Hit(ref set, set.Items[k], xz, out var hit))
                    continue;
                ref var inst = ref set.Instances[hit.Instance];
                ref var type = ref set.Types[hit.Type];
                var mask = Mask(hit.Distance);
                h = math.lerp(h, inst.BaseHeight, type.Flatten * mask);
                h += type.Height * inst.Scale * Profile(ref type, hit.Local, hit.Radius) * mask;
                if (type.Roughness > 0f)
                    h += type.Roughness * inst.Scale * mask * (noise.snoise(hit.Local * 0.11f + inst.Seed) * 0.7f + noise.snoise(hit.Local * 0.31f - inst.Seed) * 0.3f);

                // Pools: a raised lip keeps the water in on sloping ground, then the bowl is dug out of it.
                for (int p = 0; p < type.PoolCount; p++)
                {
                    ref var pool = ref set.Pools[type.FirstPool + p];
                    var r = pool.Radius * hit.Radius;
                    var dp = math.distance(hit.Local, pool.Offset * hit.Radius);
                    if (dp > r * 1.5f)
                        continue;
                    var level = PoolLevel(ref inst, ref type, ref pool, hit.Radius);
                    h = math.max(h, math.lerp(level + 0.25f, h, math.smoothstep(r * 1.15f, r * 1.5f, dp)));
                    // The bank rises gently out of the water (0.6 m per metre), so the shore line falls across the terrain
                    // grid smoothly instead of a near-vertical step between two vertices (ragged shards of water).
                    var bowl = level - 0.1f - pool.Depth * math.smoothstep(r, r * 0.35f, dp) + math.max(dp - r, 0f) * 0.6f;
                    h = math.min(h, bowl);
                }
            }
            return h;
        }

        /// <summary>
        /// The highest pool surface claiming a point, or <see cref="GroundSample.NoWater"/>. Claim fades 1 → 0 over
        /// the pool's banks (for surface rules).
        /// </summary>
        public static float PoolWater(ref LandmarkSetBlob set, float2 xz, out float claim)
        {
            claim = 0f;
            var water = GroundSample.NoWater;
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                if (!Hit(ref set, set.Items[k], xz, out var hit))
                    continue;
                ref var inst = ref set.Instances[hit.Instance];
                ref var type = ref set.Types[hit.Type];
                for (int p = 0; p < type.PoolCount; p++)
                {
                    ref var pool = ref set.Pools[type.FirstPool + p];
                    var r = pool.Radius * hit.Radius;
                    var dp = math.distance(hit.Local, pool.Offset * hit.Radius);
                    // Only inside the rim, so the water never shows over lower ground beyond it.
                    if (dp > r * 1.1f)
                        continue;
                    var level = PoolLevel(ref inst, ref type, ref pool, hit.Radius);
                    if (level > water)
                        water = level;
                    claim = math.max(claim, math.smoothstep(r * 1.1f, r * 0.9f, dp));
                }
            }
            return water;
        }

        /// <summary>Overrides the water look inside landmarks with their own (hot springs, toxic pools).</summary>
        public static void WaterLook(ref LandmarkSetBlob set, float2 xz, ref float4 look, ref float2 glow)
        {
            if (!Find(ref set, xz, out var hit))
                return;
            ref var type = ref set.Types[hit.Type];
            if (type.OverrideWater == 0)
                return;
            var t = math.smoothstep(1f, 0.8f, hit.Distance);
            look = math.lerp(look, type.Water, t);
            glow = math.lerp(glow, new float2(type.WaterGlow, type.WaterLava), t);
        }

        /// <summary>Paints landmarks' ground surfaces over the biome's.</summary>
        public static void Ground(ref LandmarkSetBlob set, float2 xz, float edgeNoise, float colorNoise, ref SurfaceProps props)
        {
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                if (!Hit(ref set, set.Items[k], xz, out var hit))
                    continue;
                ref var type = ref set.Types[hit.Type];
                if (type.HasGround == 0)
                    continue;
                var d = hit.Distance / type.GroundRadius + edgeNoise * 0.25f;
                var w = math.smoothstep(1f, 0.75f, d);
                if (w <= 0f)
                    continue;
                var ground = type.Ground.Props;
                ground.Albedo *= 1f + colorNoise * type.Ground.ColorVariation;
                props = SurfaceProps.Lerp(props, ground, w);
            }
        }
    }
}
