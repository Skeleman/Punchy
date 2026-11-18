using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// #301: the ground round a cave mouth is drawn by the cave mesh, not the terrain, on whole cells of a world grid
    /// (Cell m). The terrain drops its quads in those cells and the cave mesh draws the ground there from the same heights,
    /// so the two meet on shared vertices: every point on the patch's rim gets its height from Height, which both sides
    /// call with the same inputs, so their rims match bit for bit instead of being estimated.
    /// </summary>
    public static class MouthPatch
    {
        /// <summary>The patch grid (m): a multiple of every terrain LOD step and cave voxel it is drawn at.</summary>
        public const float Cell = 4f;
        /// <summary>A cell is in the patch when the mouth cut is under this (m) anywhere on its 3x3 samples.</summary>
        public const float Reach = 4f;
        /// <summary>The coarsest terrain LOD that hands its mouth cells to the patch (the cave walls are built only near).</summary>
        public const int MaxLod = 1;
        /// <summary>The cave chunk size (m): the patch's rim is also kept straight along these lines (levels meet there).</summary>
        public const float ChunkLine = 32f;

        /// <summary>Whether the world grid cell holds part of a mouth.</summary>
        public static bool In(in TerrainSource source, int2 cell)
        {
            if (!source.Caves.IsCreated)
                return false;
            var min = (float2)cell * Cell;
            if (!CaveSampling.NearRect(ref source.Caves.Value, min - Reach, min + Cell + Reach))
                return false;
            for (int k = 0; k < 9; k++)
                if (BuildChunkMeshJob.MouthCutAt(source, min + new float2(k % 3, k / 3) * (Cell * 0.5f), 1f) < Reach)
                    return true;
            return false;
        }

        /// <summary>Fills a mask of In for the cells min .. min + size - 1.</summary>
        public static bool Fill(in TerrainSource source, int2 min, int size, NativeArray<bool> mask)
        {
            var any = false;
            for (int i = 0; i < size * size; i++)
            {
                var on = In(source, min + new int2(i % size, i / size));
                mask[i] = on;
                any |= on;
            }
            return any;
        }

        /// <summary>The cells a lattice point touches: 1, 2 or 4 (lo .. hi on each axis).</summary>
        public static void Touches(float2 xz, out int2 lo, out int2 hi)
        {
            var g = xz / Cell;
            hi = (int2)math.floor(g);
            lo = math.select(hi, hi - 1, g == math.floor(g));
        }

        /// <summary>The patch's height at a lattice point (mask over the cells from min, size across): exact on the
        /// heightmap, except on a rim line, where it runs straight between the line's two cell corners.</summary>
        public static float Height(in TerrainSource source, float2 xz, NativeArray<bool> mask, int2 min, int size)
        {
            Touches(xz, out var lo, out var hi);
            var onX = lo.x != hi.x;
            if (onX == (lo.y != hi.y))
                return source.SampleGround(xz).Height;
            int2 a = lo, b = onX ? new int2(hi.x, lo.y) : new int2(lo.x, hi.y);
            bool ia = mask[(a.y - min.y) * size + a.x - min.x], ib = mask[(b.y - min.y) * size + b.x - min.x];
            var line = onX ? xz.x : xz.y;
            var border = math.frac(line / ChunkLine) == 0f;
            if (ia == ib && !(border && ia))
                return source.SampleGround(xz).Height;
            var along = onX ? xz.y : xz.x;
            var c0 = math.floor(along / Cell) * Cell;
            var h0 = source.SampleGround(onX ? new float2(xz.x, c0) : new float2(c0, xz.y)).Height;
            var h1 = source.SampleGround(onX ? new float2(xz.x, c0 + Cell) : new float2(c0 + Cell, xz.y)).Height;
            return math.lerp(h0, h1, (along - c0) / Cell);
        }

        /// <summary>Whether any cell the point touches is in the patch, and whether they all are.</summary>
        public static void Membership(float2 xz, NativeArray<bool> mask, int2 min, int size, out bool any, out bool all)
        {
            Touches(xz, out var lo, out var hi);
            any = false;
            all = true;
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var on = mask[(z - min.y) * size + x - min.x];
                any |= on;
                all &= on;
            }
        }

        /// <summary>A ground vertex of the patch, in world space, shaded as the terrain shades its own.</summary>
        internal static TerrainVertex GroundVertex(in TerrainSource source, float2 xz, float h, float normalEpsilon)
        {
            var ground = source.SampleGround(xz);
            var normal = source.SampleNormal(xz, normalEpsilon);
            var vertex = new TerrainVertex { Position = new float3(xz.x, h, xz.y), Normal = normal };
            var surface = source.SampleSurface(xz, ground, normal);
            vertex.SetSurface(surface);
            if (ground.HasWater)
                vertex.Extra.w = (half)(-(1f - math.smoothstep(0.1f, 1.4f, h - ground.WaterSurface)));
            vertex.Morph = new float4(1e6f, 2e6f, surface.Bump, 100f);
            return vertex;
        }
    }
}
