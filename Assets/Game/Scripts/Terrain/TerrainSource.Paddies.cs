using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Stepped pools (a hillside swamp's paddies), the last water a bake places (W-153 row 2, "still water last").
    /// A pond that stays above sea level is cut into paddy cells about <see cref="PaddySize"/> across (a warped,
    /// jittered cell grid, W-25). Each cell that can hold water gets one level, taken from the finished ground
    /// (rivers, gorges, landmarks and falls already cut) at its centre, and baked into <see cref="Paddies"/>. A cell
    /// on a slope too steep for a pool, over other water or at the sea's edge gets none and stays plain ground.
    /// At run time the pool only follows the ground it stands on: it is a shallow cut along the contour at its level
    /// with a low rim on the downhill side, so it can never refill a gorge or stand as a wall above its surroundings.
    /// </summary>
    public partial struct TerrainSource
    {
        /// <summary>
        /// One water level per paddy cell (0 = no pool there), built at the very end of a bake by
        /// <see cref="BuildPaddies"/>. Without it there are no stepped pools. Not a height field: read it with PaddyLevel.
        /// </summary>
        public BlobAssetReference<HeightmapBlob> Paddies;

        /// <summary>W-25: paddy cell size (m), how far a pool's rim stands above its water, and the pools' depth.</summary>
        public const float PaddySize = 15f, TerraceBank = 0.3f, TerraceDepth = 0.45f;
        /// <summary>Metres per step of a stored paddy level.</summary>
        const float PaddyLevelStep = 0.02f;
        /// <summary>A pool's water stands this far below the ground at its cell's centre.</summary>
        const float PaddyDrop = 0.15f;
        /// <summary>
        /// Gates of a cell (see BuildPaddies): the ground is checked on a grid of PaddyProbeStep out to PaddyProbeReach
        /// from the centre; two neighbouring probes may differ by PaddyMaxStep at most (about a 17 % slope: steeper, and a pool one rim high would stand as a wall), the lowest
        /// must be PaddySeaClear above the sea, and the pond must cover the centre by PaddyMinCover.
        /// </summary>
        const float PaddyProbeStep = 3.5f, PaddyProbeReach = 12.5f, PaddyMaxStep = 0.6f, PaddySeaClear = 1f, PaddyMinCover = 0.3f;
        const int PaddyProbes = 9;

        /// <summary>A point in paddy cell units. The grid is warped so cells aren't rows of boxes.</summary>
        static float2 PaddyPoint(float2 xz) =>
            (xz + new float2(noise.snoise(xz * 0.03f + 11.3f), noise.snoise(xz * 0.03f - 7.9f)) * PaddySize * 0.3f) / PaddySize;

        /// <summary>W-25: a paddy cell's centre, in cell units (jittered inside its grid square).</summary>
        static float2 PaddySite(int2 c)
        {
            var x = math.hash(c);
            // math.hash has no avalanche on small ints: scramble before using the bits.
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (float2)c + 0.5f + (new float2(x & 0xffff, x >> 16) / 65535f - 0.5f) * 0.8f;
        }

        /// <summary>Where a paddy cell's centre is in the world: PaddyPoint undone (its warp is gentle, so a few rounds settle).</summary>
        static float2 PaddyCentre(int2 c)
        {
            var target = PaddySite(c) * PaddySize;
            var x = target;
            for (int i = 0; i < 4; i++)
                x = target - (PaddyPoint(x) * PaddySize - x);
            return x;
        }

        /// <summary>The water level of a paddy cell, or float.MinValue when it holds no pool.</summary>
        static float PaddyLevel(ref HeightmapBlob grid, int2 c)
        {
            c += grid.Resolution / 2;
            if (math.any(c < 0) || math.any(c >= grid.Resolution))
                return float.MinValue;
            var v = grid.Heights[c.y * grid.Resolution + c.x];
            return v == 0 ? float.MinValue : grid.MinHeight + v * grid.HeightStep;
        }

        /// <summary>A bump that lifts the ground d metres to a rim, fading out where the lift would be a wall (over 0.6 m).</summary>
        static float Rim(float d) => math.max(d, 0f) * (1f - math.smoothstep(0.6f, 1.2f, d));

        /// <summary>
        /// The ground of one pool, as metres from its water level, where the finished ground stands u above that level.
        /// Well below the level: the ground itself, with a rim where it comes within reach. Around the level: the
        /// pool's bed. Above it: the bed cut into the slope, back to the ground half a metre up.
        /// </summary>
        static float PoolProfile(float u)
        {
            if (u < -0.25f)
                return u + Rim(TerraceBank - u);
            var bed = math.lerp(TerraceBank, -TerraceDepth, math.smoothstep(-0.25f, 0f, u));
            return math.lerp(bed, u, math.smoothstep(0.5f, 1.1f, u));
        }

        /// <summary>
        /// The ground of a stepped pool at xz (h the finished ground without ponds, cover as from PondDig) and its
        /// water level (float.MinValue when dry). The nearest paddy cell gives the pool; towards each neighbouring
        /// cell the ground rises to a shared rim just above the higher of the two pools, wider where they differ
        /// more, so two levels never meet as a step. Nothing here lifts the ground more than a rim's height.
        /// </summary>
        float Terrace(float2 xz, float h, float cover, out float level)
        {
            level = float.MinValue;
            if (!Paddies.IsCreated)
                return h;
            ref var grid = ref Paddies.Value;
            var p = PaddyPoint(xz);
            var cell = (int2)math.floor(p);
            // Nearest cell centre (A) first.
            var dA = float.MaxValue;
            int jA = 0;
            for (int j = 0; j < 9; j++)
            {
                var d = math.distance(p, PaddySite(cell + new int2(j % 3 - 1, j / 3 - 1)));
                if (d < dA) { dA = d; jA = j; }
            }
            var levelA = PaddyLevel(ref grid, cell + new int2(jA % 3 - 1, jA / 3 - 1));
            var poolA = levelA > float.MinValue;
            // Then the rim (the same from both sides of a border, so the ground stays continuous) and how far
            // inside its own cell this point is, 0 on a border.
            var rim = 0f;
            var inner = 1f;
            for (int j = 0; j < 9; j++)
            {
                var c = cell + new int2(j % 3 - 1, j / 3 - 1);
                var e = (math.distance(p, PaddySite(c)) - dA) * 0.5f * PaddySize;   // metres to that border (approx.)
                if (e > 8f)
                    continue;
                var levelC = PaddyLevel(ref grid, c);
                var poolC = levelC > float.MinValue;
                if (poolC)
                    rim = math.max(rim, Rim(levelC + TerraceBank - h) * (1f - math.smoothstep(3f, 8f, e)));
                if (j == jA)
                    continue;
                var width = poolA && poolC ? math.lerp(3f, 6.5f, math.smoothstep(0.4f, 0.8f, math.abs(levelC - levelA))) : 3f;
                inner = math.min(inner, e / width);
            }
            if (!poolA && rim <= 0f)
                return h;
            var g = math.lerp(h + rim, poolA ? levelA + PoolProfile(h - levelA) : h, math.smoothstep(0f, 1f, inner));
            // Out towards the pond's edge the paddy blends back into the ground, past its rim first.
            var inside = math.smoothstep(0.3f, 0.65f, math.saturate(cover));
            g = math.lerp(h, math.lerp(h + rim, g, inside), math.smoothstep(0f, 0.3f, cover));
            if (poolA && inside > 0f && h > levelA - 0.25f && g < levelA - 0.02f)
                level = levelA;
            return g;
        }

        /// <summary>
        /// Chooses which paddy cells hold a pool and at what level, from the finished ground (every river, landmark
        /// and fall in place, ponds off). Call it last in a bake and assign the result to <see cref="Paddies"/>,
        /// which must be unset during the call. Default when no cell qualifies. Rows run in parallel (sampling only
        /// reads blobs).
        /// </summary>
        public BlobAssetReference<HeightmapBlob> BuildPaddies(Unity.Collections.Allocator allocator, out string report)
        {
            report = "no paddies";
            if (!PondsOn || !HasBiomes)
                return default;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int n = (int)math.ceil(World.WorldSize / PaddySize) + 4;
            var levels = new ushort[n * n];
            var self = this;
            var dry = this;
            dry.PondsOn = false;
            dry.Paddies = default;
            var sea = World.SeaLevel;
            int kept = 0, steep = 0, wet = 0, low = 0;
            System.Threading.Tasks.Parallel.For(0, n, z =>
            {
                var heights = new float[PaddyProbes * PaddyProbes];
                for (int x = 0; x < n; x++)
                {
                    var centre = PaddyCentre(new int2(x, z) - n / 2);
                    self.SampleBiomes(centre, out _, out var shape);
                    self.PondDig(centre, shape, out var cover);
                    if (cover < PaddyMinCover)
                        continue;
                    // The ground round the centre: no other water, clear of the sea, and gentle enough for a level pool.
                    bool isWet = false, isLow = false, isSteep = false;
                    for (int i = 0; i < heights.Length; i++)
                    {
                        var offset = (new float2(i % PaddyProbes, i / PaddyProbes) - (PaddyProbes - 1) * 0.5f) * PaddyProbeStep;
                        heights[i] = float.NaN;
                        if (math.length(offset) > PaddyProbeReach)
                            continue;
                        var g = dry.SampleGround(centre + offset);
                        heights[i] = g.Height;
                        isWet |= g.River > 0.02f || (g.HasWater && g.WaterSurface > g.Height - 0.5f);
                        isLow |= g.Height < sea + PaddySeaClear;
                    }
                    for (int i = 0; i < heights.Length && !isSteep; i++)
                    {
                        if (float.IsNaN(heights[i]))
                            continue;
                        if (i % PaddyProbes > 0 && math.abs(heights[i] - heights[i - 1]) > PaddyMaxStep)   // NaN compares false
                            isSteep = true;
                        if (i >= PaddyProbes && math.abs(heights[i] - heights[i - PaddyProbes]) > PaddyMaxStep)
                            isSteep = true;
                    }
                    if (isWet) { System.Threading.Interlocked.Increment(ref wet); continue; }
                    if (isLow) { System.Threading.Interlocked.Increment(ref low); continue; }
                    if (isSteep) { System.Threading.Interlocked.Increment(ref steep); continue; }
                    var level = heights[heights.Length / 2] - PaddyDrop;
                    levels[z * n + x] = (ushort)math.clamp((int)math.round((level - sea) / PaddyLevelStep), 1, ushort.MaxValue);
                    System.Threading.Interlocked.Increment(ref kept);
                }
            });
            report = $"{kept} pools; no pool on {steep} steep cells, {wet} over other water, {low} at the sea's edge; {watch.ElapsedMilliseconds} ms";
            if (kept == 0)
                return default;
            using var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var root = ref builder.ConstructRoot<HeightmapBlob>();
            root.Resolution = n;
            root.Origin = new float2(-n / 2 * PaddySize);
            root.WorldSize = n * PaddySize;
            root.MinHeight = sea;
            root.HeightStep = PaddyLevelStep;
            var stored = builder.Allocate(ref root.Heights, levels.Length);
            for (int i = 0; i < levels.Length; i++) stored[i] = levels[i];
            return builder.CreateBlobAssetReference<HeightmapBlob>(allocator);
        }
    }
}
