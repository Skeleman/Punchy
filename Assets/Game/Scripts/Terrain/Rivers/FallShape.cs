using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>What a fall's water lands on.</summary>
    public enum FallLanding
    {
        /// <summary>Deep, still water.</summary>
        Pool = 0,
        /// <summary>Rock or ground, dry or barely wet.</summary>
        Ledge = 1,
        /// <summary>Shallow water that keeps running downhill.</summary>
        Rapids = 2,
        /// <summary>The sea.</summary>
        Sea = 3
    }

    /// <summary>
    /// R-21 row 4: one description of a waterfall, whatever made it (a river step or chain, a cliff plunge, a grand
    /// falls tier, a cave's notch or chute, a landmark's fall). Everything that shows a fall is built from this alone:
    /// its sheet, its spray, the face the water leaves out under it and the way the water moves above its lip and at
    /// its foot (LandmarkEffectSystem.cs, LandmarkEffects.hlsl, RiverCurtains.hlsl). Measured from TerrainSource, so
    /// it is the same on every machine; it moves nothing in the world.
    /// </summary>
    public struct FallShape
    {
        /// <summary>The middle of the lip line, on the water.</summary>
        public float3 Lip;
        /// <summary>Which way the water pours (unit, on the ground plane).</summary>
        public float2 Out;
        /// <summary>How wide the sheet is at its lip (m).</summary>
        public float Width;
        /// <summary>From the lip down to where it lands (m).</summary>
        public float Drop;
        /// <summary>How far out from the lip it lands (m).</summary>
        public float Run;
        /// <summary>
        /// The lip line's shape: Skew is how much further out the lip is at the sheet's one side than the other (m, an
        /// oblique lip; + on the side of Out turned a quarter clockwise seen from above), Bow how much further out its
        /// middle is than its sides (m; negative for a horseshoe).
        /// </summary>
        public float Skew, Bow;
        /// <summary>0 for a sheer drop that falls free, up to 1 for a cascade that slides down a slope.</summary>
        public float Cascade;
        public FallLanding Landing;
        /// <summary>0..1, the fall's own: its lip's unevenness, its thin places and its timing follow it.</summary>
        public float Seed;
        /// <summary>A cave's fall: lit as underground, leaving a notch.</summary>
        public bool UnderRock;
        /// <summary>
        /// River falls only: the water's own surface is left out under the sheet from this far behind the lip line (m); 0
        /// where the water has no face of its own to hide.
        /// </summary>
        public float ClipBack;

        /// <summary>Where the middle of the sheet lands.</summary>
        public float3 Foot => Lip + new float3(Out.x, 0f, Out.y) * Run - new float3(0f, Drop, 0f);
        /// <summary>How big the fall is, for how big it looks (rule 20): the larger of its drop and its width.</summary>
        public float Size => math.max(Drop, Width);

        /// <summary>
        /// How far along a line (from origin, along o) the water first drops away below a level: where a fall's face
        /// starts. NaN where it never does.
        /// </summary>
        static float FaceStart(in TerrainSource source, float2 origin, float2 o, float top, float from, float to)
        {
            for (float s = from; s < to; s += 0.25f)
                if (source.SampleGround(origin + o * s).WaterSurface < top - 0.25f)
                    return s;
            return float.NaN;
        }

        /// <summary>
        /// A river fall, measured on the water (its RiverFall only roughly gives it): the face starts where the water
        /// first drops away below the lip and ends where it reaches the pool; the sheet is as wide as the wet water
        /// just above it; the lip line's skew and bow come from where the face starts either side of the middle. A
        /// sheer drop is thrown clear (as far as water falling that long carries, while there is water to land in); a
        /// face that runs out further than that is a cascade.
        /// </summary>
        public static FallShape River(in TerrainSource source, in RiverFall f, float seed)
        {
            var o = math.normalizesafe(f.Out, new float2(0f, 1f));
            var across = new float2(o.y, -o.x);
            var top = f.Lip.y;
            var pool = f.Lip.y - f.Drop;
            float start = 0f, foot = 4f;
            var started = false;
            for (float s = -4f; s < 14f; s += 0.25f)
            {
                var w = source.SampleGround(f.Lip.xz + o * s).WaterSurface;
                if (!started && w < top - 0.25f)
                {
                    start = s;
                    started = true;
                }
                if (w < pool + 0.4f)
                {
                    foot = s;
                    break;
                }
            }
            foot = math.max(foot, start + 0.5f);
            // The wet width just above the face (water over the ground at about the lip's level), and its middle.
            // (Walked out from the middle until the water ends, so a pool spilling over the whole step is covered.)
            var above = f.Lip.xz + o * (start - 0.5f);
            var half = f.Width * 0.5f + 16f;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int dir = -1; dir <= 1; dir += 2)
                for (float c = 0f; c <= half; c += 0.5f)
                {
                    var g = source.SampleGround(above + across * (c * dir));
                    if (!(g.HasWater && g.WaterSurface > g.Height + 0.02f && math.abs(g.WaterSurface - top) < 1.5f))
                    {
                        if (c > f.Width * 0.5f)
                            break;
                        continue;
                    }
                    lo = math.min(lo, c * dir);
                    hi = math.max(hi, c * dir);
                }
            if (lo > hi)
            {
                lo = -f.Width * 0.5f;
                hi = f.Width * 0.5f;
            }
            var centre = (lo + hi) * 0.5f;
            var mid = above + across * centre + o * 0.2f;
            var level = source.SampleGround(mid).WaterSurface;
            if (!(math.abs(level - top) < 1.5f))
                level = top;
            var shape = new FallShape
            {
                Lip = new float3(mid.x, level + 0.05f, mid.y),
                Out = o,
                Width = math.max(hi - lo + 0.5f, 2f),
                Seed = seed
            };
            shape.Drop = math.max(shape.Lip.y - pool, 1f);
            var measured = math.max(foot - start + 0.3f, 0.6f);

            // The lip line: where the face starts a third of the width out on each side, against the middle.
            var third = shape.Width * 0.33f;
            var plus = FaceStart(source, f.Lip.xz + across * (centre + third), o, top, start - 6f, start + 6f);
            var minus = FaceStart(source, f.Lip.xz + across * (centre - third), o, top, start - 6f, start + 6f);
            if (!math.isnan(plus) && !math.isnan(minus))
            {
                var reachSide = math.min(shape.Width * 0.5f, 6f);
                shape.Skew = math.clamp((plus - minus) / 0.66f, -reachSide, reachSide);
                shape.Bow = math.clamp((start - (plus + minus) * 0.5f) / 0.436f, -2.5f, 2.5f);
            }

            // Thrown clear: water leaving the lip at a walking pace or a little over lands this far out. It lands
            // there only where there is water at the pool's level to land in (not on the far bank of a tight bend).
            var speed = math.lerp(2f, 3.5f, math.frac(seed * 7.31f));
            var thrown = speed * math.sqrt(2f * shape.Drop / 9.81f);
            shape.Run = measured;
            for (var r = thrown; r > measured; r -= 0.5f)
            {
                var g = source.SampleGround(shape.Lip.xz + o * r);
                if (g.HasWater && g.WaterSurface > g.Height + 0.02f && g.WaterSurface < pool + 0.6f)
                {
                    shape.Run = r;
                    break;
                }
            }
            shape.Cascade = math.saturate((measured - thrown) / math.max(shape.Drop, 1f));
            shape.ClipBack = 1.5f;

            // What it lands on.
            var landing = source.SampleGround(shape.Lip.xz + o * shape.Run);
            var beyond = source.SampleGround(shape.Lip.xz + o * (shape.Run + 6f));
            var depth = landing.HasWater ? landing.WaterSurface - landing.Height : 0f;
            if (depth < 0.15f)
                shape.Landing = FallLanding.Ledge;
            else if (math.abs(landing.WaterSurface - source.World.SeaLevel) < 0.3f)
                shape.Landing = FallLanding.Sea;
            else if (depth < 0.8f && beyond.HasWater && beyond.WaterSurface < landing.WaterSurface - 0.6f)
                shape.Landing = FallLanding.Rapids;
            else
                shape.Landing = FallLanding.Pool;
            return shape;
        }

        /// <summary>A cave's fall: from its notch (or its chute's lip) into its pool, as the cave bake placed it.</summary>
        public static FallShape Cave(in CaveWaterfall f, float seed) => new FallShape
        {
            Lip = f.Lip,
            Out = f.Out,
            Width = f.Width,
            Drop = f.Drop,
            Run = f.Carry,
            Seed = seed,
            UnderRock = true
        };

        /// <summary>
        /// One sheet of a grand falls tier (W-29): the main drop is three sheets side by side and throws a wider,
        /// longer arc; a lake shelf stretch is one sheet as wide as its chord of the lip (W-81) and lands in the lake.
        /// </summary>
        public static FallShape Grand(in GrandFallTier t, float seed, int sheet, int sheets)
        {
            var main = t.Drop > 30f;
            var side = new float3(-t.Out.y, 0f, t.Out.x);
            return new FallShape
            {
                Lip = t.Lip + side * (sheets == 1 ? 0f : (sheet - 1) * t.Width * 0.3f),
                Out = t.Out,
                Width = t.Width * (t.Shelf ? 1f : main ? 0.4f : 0.85f),
                Drop = t.Drop,
                Run = main ? 3f + t.Drop * 0.06f : 1.6f + t.Drop * 0.05f,
                // (The three sheets of a main drop stand a little apart in depth, the middle one furthest out.)
                Bow = sheets == 1 ? 0f : 0.6f,
                Seed = math.frac(seed + sheet * 0.37f)
            };
        }

        /// <summary>
        /// A landmark's fall: from its cliff's lip out (against its facing) and down to whatever lies below, pool or
        /// ground. The lip is where the ground first reaches the cliff's top, going in from the effect's place, since a
        /// rough rim can dip where that lands and the water then started low down the face (F-48).
        /// </summary>
        public static FallShape Landmark(in TerrainSource source, float2 at, float2 facing, float run, float width, float seed)
        {
            var lipAt = at;
            var lipHeight = source.SampleHeight(at);
            var rim = lipHeight;
            for (var d = 0.5f; d <= 10f; d += 0.5f)
                rim = math.max(rim, source.SampleHeight(at + facing * d));
            for (var d = 0f; d <= 10f && lipHeight < rim - 0.5f; d += 0.5f)
            {
                lipHeight = source.SampleHeight(at + facing * d);
                lipAt = at + facing * d;
            }
            var below = source.SampleGround(lipAt - facing * run);
            var wet = below.HasWater && below.WaterSurface > below.Height + 0.15f;
            var shape = new FallShape
            {
                Lip = new float3(lipAt.x, lipHeight + 0.2f, lipAt.y),
                Out = -facing,
                Width = width,
                Run = run,
                Seed = seed,
                Landing = wet ? FallLanding.Pool : FallLanding.Ledge
            };
            shape.Drop = math.max(shape.Lip.y - (below.HasWater ? math.max(below.Height, below.WaterSurface) : below.Height), 1f);
            return shape;
        }
    }
}
