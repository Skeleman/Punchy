using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>#301: world-grid cells round a cave mouth that the cave mesh draws instead of the terrain; both rims take Height, so they match exactly.</summary>
    public static class MouthPatch
    {
        // A multiple of every terrain LOD step and cave voxel it is drawn at.
        public const float Cell = 4f;
        public const float Reach = 4f;
        // Cave walls are built only near.
        public const int MaxLod = 1;
        // Cave chunk size: cave levels meet on these lines, so the rim stays straight there too.
        public const float ChunkLine = 32f;

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

        public static void Touches(float2 xz, out int2 lo, out int2 hi)
        {
            var g = xz / Cell;
            hi = (int2)math.floor(g);
            lo = math.select(hi, hi - 1, g == math.floor(g));
        }

        // Straight between cell corners on a rim line, so terrain and cave points of any spacing land on it alike.
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
