using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-162 row 3 (#327): the Climbability grade of bare rock, cliff faces and cave walls alike, from the same shapes the
    /// rock wall draws (RockWall.hlsl): a craggy face (4) or, in patches, a smooth one (5); a bed's step (its ledge top or
    /// the lip over it) and a slab's seam (a crack) a grade easier. Seed-derived from the position alone, so every
    /// player and creature reads the same grade and nothing is sent. The numbers are A-101's scale (Punchy.Fauna.Climbing:
    /// Terrain can't see Fauna); vines, roots and ropes (2) and ladders (1) are their own sections, the easiest winning.
    /// The beds and slabs are the shader's at full detail (m 1, both sets), ported with its value noise.
    /// </summary>
    public static class RockGrades
    {
        public const byte Craggy = 4, Smooth = 5;
        /// <summary>How much of the rock is smooth (of 1): where the patch noise is over 1 - this.</summary>
        public const float SmoothShare = 0.3f;
        /// <summary>A bed step this steep (bed shade per metre of height) is a ledge or lip to hold.</summary>
        public const float LedgeRise = 0.35f;
        /// <summary>Slab noise this near its middle is the seam between two slabs: a crack.</summary>
        public const float CrackHalf = 0.035f;

        /// <summary>The grade of the rock at p (a point in or on it; the caller knows it's rock).</summary>
        public static byte At(in TerrainSource source, float3 p)
        {
            var h = Scramble(source.World.Seed ^ 0x9e3779b9u);
            var off = new float2(h & 1023u, (h >> 10) & 1023u) * 1.37f;
            // Smooth patches some tens of metres across, wandering with height too so a face isn't one grade top to bottom.
            var patch = ValueNoise(new float2((p.x + p.z) * 0.035f, p.y * 0.05f) + off);
            var grade = patch > 1f - SmoothShare ? Smooth : Craggy;
            Beds(p.xz, p.y, out var rise, out var wy);
            var seam = math.abs(Slabs(p.xz, wy) - 0.5f) < CrackHalf;
            if (math.abs(rise) > LedgeRise || seam)
                grade--;
            return grade;
        }

        /// <summary>PunchyRockBeds at m 1 with both sets: the bed shade's rise per metre at y, and the wandered height.</summary>
        static void Beds(float2 xz, float y, out float rise, out float wy)
        {
            y += (ValueNoise(xz * 0.013f + 5.1f) - 0.5f) * 8f;
            y += (ValueNoise(xz * 0.07f + 1.7f) - 0.5f) * 1.6f;
            wy = y;
            const float ey = 0.2f;
            var b0 = ValueNoise(new float2(y / 6f, 19.7f)) + 0.5f * ValueNoise(new float2(y / 1.4f, 23.1f));
            var b1 = ValueNoise(new float2((y + ey) / 6f, 19.7f)) + 0.5f * ValueNoise(new float2((y + ey) / 1.4f, 23.1f));
            rise = (b1 - b0) / ey;
        }

        /// <summary>PunchyRockSlabs' noise (both headings) before its threshold: 0.5 is a seam.</summary>
        static float Slabs(float2 xz, float wy)
        {
            var y = wy + (ValueNoise(xz * 0.21f + 2.9f) - 0.5f) * 1.2f;
            var sA = ValueNoise(new float2(xz.x * 0.22f + ValueNoise(xz * 0.09f) * 2.5f, y * 0.75f));
            var sB = ValueNoise(new float2(xz.y * 0.22f + ValueNoise(xz * 0.09f + 4.1f) * 2.5f, y * 0.75f + 31.7f));
            var sC = ValueNoise(new float2((xz.x + xz.y) * 0.5f, y * 1.6f + 9.3f));
            return (sA + sB) * 0.35f + sC * 0.3f;
        }

        /// <summary>PunchyCellHash (SkyCommon.hlsl): a lattice cell to 0..1.</summary>
        static float CellHash(int2 cell)
        {
            var c = math.asuint(cell);
            var h = (c.x * 0x8da6b343u) ^ (c.y * 0xd8163841u);
            h ^= h >> 13;
            h *= 0x85ebca6bu;
            h ^= h >> 16;
            return h * (1f / 4294967296f);
        }

        /// <summary>PunchyValueNoise (SkyCommon.hlsl), so the grades follow the drawn rock.</summary>
        public static float ValueNoise(float2 p)
        {
            var fl = math.floor(p);
            var i = (int2)fl;
            var f = p - fl;
            var u = f * f * (3f - 2f * f);
            return math.lerp(math.lerp(CellHash(i), CellHash(i + new int2(1, 0)), u.x),
                             math.lerp(CellHash(i + new int2(0, 1)), CellHash(i + new int2(1, 1)), u.x), u.y);
        }

        static uint Scramble(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }
    }
}
