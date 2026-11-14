using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>What made a scarp, which sets its proportions (see <see cref="ScarpBake.Style"/>).</summary>
    public enum ScarpKind : byte
    {
        /// <summary>A tall canyon wall: stepped beds, buttresses and gullies, a scree apron at the foot.</summary>
        Canyon,
        /// <summary>A low bank a river has cut: near vertical, undercut at the foot, a flat wash below.</summary>
        RiverCut
    }

    /// <summary>One bed of a scarp's layer stack: how thick, and how hard (0 soft: weathers back into a recess,
    /// 1 hard: stands out as a face or a ledge).</summary>
    public struct ScarpLayer
    {
        public float Thickness, Hardness;
    }

    /// <summary>One point of a baked rim, every couple of metres along it.</summary>
    public struct ScarpNode
    {
        /// <summary>The rim, and the level direction towards the down side.</summary>
        public float2 P, N;
        /// <summary>Height of the rim and of the foot here, the width of the stepped strip, distance along the rim.</summary>
        public float RimY, FootY, W, S;
    }

    /// <summary>
    /// One baked scarp (W-155): a rim line where the ground steps down. The ground is cut to a true step along it
    /// (<see cref="ScarpSet.Shape"/>, inside TerrainSource so everything agrees), and the stepped strip is drawn by a
    /// skin mesh built from the same face function (<see cref="ScarpSet.Face"/>), which can show what a height field
    /// cannot: recesses under hard beds and overhangs.
    /// </summary>
    public struct Scarp
    {
        /// <summary>Min xz, max xz of everything the scarp changes.</summary>
        public float4 Bounds;
        public float MinY, MaxY;
        public ScarpKind Kind;
        public uint Seed;
        /// <summary>0 fresh and sharp, 1 old and rounded.</summary>
        public float Age;
        /// <summary>Full width of the stepped strip, and how far past its foot the floor is graded.</summary>
        public float Width, Reach;
        /// <summary>Slope of the floor away from the foot, set-back of the face from the rim, lean of the face per
        /// metre of height, how far a bed stands out or weathers back, crack size, toe slope, undercut depth.</summary>
        public float Apron, Setback, Lean, Push, Crack, Toe, Notch;
        /// <summary>How much the beds tilt along the rim (m per m).</summary>
        public float Dip;
        /// <summary>Slope of the bench a soft bed makes (run per metre of height); Lean is a hard bed's.</summary>
        public float Bench;
        public float Length, MaxDrop, LayerSpan;
        public BlobArray<ScarpNode> Nodes;
        public BlobArray<ScarpLayer> Layers;
    }

    public struct ScarpSetBlob
    {
        public float4 Bounds;
        public BlobArray<Scarp> Scarps;
    }

    /// <summary>Where a point sits against a scarp: along the rim, and how far out from it on the down side.</summary>
    public struct ScarpAt
    {
        public int Segment;
        public float T, D, S, RimY, FootY, W;
    }

    /// <summary>
    /// The scarps' maths, shared by the ground (TerrainSource), the terrain mesh (which leaves the strip to the skin)
    /// and the skin itself. Plain static functions on the blob, safe in Burst.
    /// </summary>
    public static class ScarpSet
    {
        /// <summary>Behind the rim the ground is levelled to the rim's height over TopRamp, flat for the last TopFlat,
        /// so the terrain mesh meets the skin's top row exactly.</summary>
        public const float TopRamp = 4f, TopFlat = 1.5f;
        /// <summary>The terrain mesh leaves a strip to the skin only where it is this many grid steps wide.</summary>
        public const float ClipSteps = 2.5f;

        static bool Inside(float4 bounds, float2 xz) => math.all(xz >= bounds.xy) && math.all(xz <= bounds.zw);

        /// <summary>The ground with every scarp cut into it.</summary>
        public static float Shape(ref ScarpSetBlob set, float2 xz, float h)
        {
            if (!Inside(set.Bounds, xz))
                return h;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                ref var sc = ref set.Scarps[i];
                if (Inside(sc.Bounds, xz))
                    h = Shape(ref sc, xz, h);
            }
            return h;
        }

        static float Shape(ref Scarp sc, float2 xz, float h)
        {
            if (!Locate(ref sc, xz, out var at))
                return h;
            // The floor's reach shrinks with the strip towards the ends of the rim, so nothing is cut along the line
            // square off the rim's last node (a crease down the slope otherwise).
            float reach = sc.Reach * math.saturate(at.W / sc.Width);
            if (at.D <= -TopRamp || at.D >= at.W + reach)
                return h;
            float y;
            if (at.D <= 0f)
                // Behind the rim: a level shelf at the rim's height, eased into the ground.
                y = math.lerp(h, at.RimY, math.smoothstep(-TopRamp, -TopFlat, at.D) * math.saturate(at.W / 2f));
            else if (at.D < at.W)
                y = Envelope(ref sc, at);
            else
            {
                // Past the foot: the floor falls away from the wall until it meets the ground it was cut from.
                var r = at.D - at.W;
                y = math.lerp(h, math.min(h, at.FootY - sc.Apron * r), 1f - math.smoothstep(reach * 0.75f, reach, r));
            }
            // Nothing changes at the very ends of the rim (the strip has no width there either).
            var ends = math.smoothstep(0f, 2f, at.S) * math.smoothstep(0f, 2f, sc.Length - at.S);
            return math.lerp(h, y, ends);
        }

        /// <summary>
        /// The nearest point of the rim. False past either end of it. Rims are gentle curves, so the nearest of every
        /// fourth node finds the neighbourhood and the segments round it are tested exactly.
        /// </summary>
        public static bool Locate(ref Scarp sc, float2 xz, out ScarpAt at)
        {
            ref var nodes = ref sc.Nodes;
            int n = nodes.Length, best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i += 4)
            {
                var d = math.distancesq(xz, nodes[i].P);
                if (d < bestD) { bestD = d; best = i; }
            }
            int lo = math.max(best - 8, 0), hi = math.min(best + 8, n - 2), seg = lo;
            float t = 0f, raw = 0f;
            bestD = float.MaxValue;
            for (int i = lo; i <= hi; i++)
            {
                var a = nodes[i].P;
                var ab = nodes[i + 1].P - a;
                var tr = math.dot(xz - a, ab) / math.max(math.lengthsq(ab), 1e-6f);
                var ts = math.saturate(tr);
                var d = math.distancesq(xz, a + ab * ts);
                if (d < bestD) { bestD = d; seg = i; t = ts; raw = tr; }
            }
            at = default;
            if (seg == 0 && raw < 0f || seg == n - 2 && raw > 1f)
                return false;
            Column(ref sc, seg, t, out at, out var c, out var dir);
            var dist = math.sqrt(bestD);
            at.D = math.dot(xz - c, SegmentNormal(ref sc, seg)) >= 0f ? dist : -dist;
            return true;
        }

        /// <summary>The level direction across a segment, towards the down side.</summary>
        public static float2 SegmentNormal(ref Scarp sc, int seg)
        {
            var ab = sc.Nodes[seg + 1].P - sc.Nodes[seg].P;
            var nrm = math.normalizesafe(new float2(ab.y, -ab.x));
            return math.dot(nrm, sc.Nodes[seg].N + sc.Nodes[seg + 1].N) < 0f ? -nrm : nrm;
        }

        /// <summary>The rim at a place along a segment: the point, the direction its face is pushed out along, and the
        /// heights and width there. Used the same way by the ground and by the skin, so they agree.</summary>
        public static void Column(ref Scarp sc, int seg, float t, out ScarpAt at, out float2 c, out float2 dir)
        {
            var a = sc.Nodes[seg];
            var b = sc.Nodes[seg + 1];
            c = math.lerp(a.P, b.P, t);
            dir = t <= 0f ? a.N : t >= 1f ? b.N : SegmentNormal(ref sc, seg);
            at = new ScarpAt
            {
                Segment = seg, T = t, S = math.lerp(a.S, b.S, t),
                RimY = math.lerp(a.RimY, b.RimY, t), FootY = math.lerp(a.FootY, b.FootY, t), W = math.lerp(a.W, b.W, t)
            };
        }

        /// <summary>The ground over the stepped strip: the highest rock at this distance out from the rim (what you
        /// stand on; an overhang hides what is under it).</summary>
        static float Envelope(ref Scarp sc, in ScarpAt at)
        {
            float drop = at.RimY - at.FootY;
            if (drop <= 0.01f)
                return at.FootY;
            var cracks = Cracks(ref sc, at.S);
            int steps = (int)math.clamp(drop / 0.6f, 8f, 64f);
            float above = at.RimY;
            for (int i = 1; i <= steps; i++)
            {
                float y = at.RimY - drop * i / steps;
                if (Face(ref sc, at, cracks, y) >= at.D)
                {
                    float lo = y, hi = above;
                    for (int k = 0; k < 6; k++)
                    {
                        var mid = 0.5f * (lo + hi);
                        if (Face(ref sc, at, cracks, mid) >= at.D) lo = mid; else hi = mid;
                    }
                    return 0.5f * (lo + hi);
                }
                above = y;
            }
            return at.FootY;
        }

        /// <summary>
        /// How far out from the rim the rock face stands at height y: 0 at the rim, the strip's width at the foot.
        /// The face steps back from the rim, then goes down the beds: near vertical through a hard bed, out along a
        /// bench through a soft one, weathered back under the hard bed above it (an overhang). Gullies run the height
        /// of the face on a slant, joints break each hard bed into slabs of its own, and it ends in a toe: scree
        /// for a canyon wall, a flat wash under an undercut for a river bank.
        /// </summary>
        public static float Face(ref Scarp sc, in ScarpAt at, float3 cracks, float y)
        {
            float u = at.RimY - y, up = y - at.FootY, q = math.saturate(at.W / sc.Width);
            // The top edge is rounded over a height that grows with age.
            float roll = math.smoothstep(0f, 0.4f + 1.2f * sc.Age, u);
            float seed = sc.Seed % 977u * 1.37f;
            // Beds aren't level: they tilt and roll along the rim (cracks.y).
            float bedY = y + cracks.y;
            float lean = Run(ref sc, at.RimY + cracks.y, out _) - Run(ref sc, bedY, out var bed);
            float hard = Hardness(ref sc, bedY);
            // Each bed stands out or weathers back by its own amount.
            float push = sc.Push * (0.65f + 0.7f * math.frac(math.sin(bed * 12.9898f + seed) * 43758.5453f));
            var slot = Slots(ref sc, at, cracks, u, bed);
            float slots = -sc.Crack * (4.5f * slot.x + 0.9f * hard * slot.y);
            float body = q * (roll * (sc.Setback + push * (hard - 0.5f) + cracks.x + slots) + lean);
            if (sc.Notch > 0f)
                body -= sc.Notch * q * math.smoothstep(0.1f, 0.5f, up) * (1f - math.smoothstep(0.8f, 1.3f, up));
            float toe = (at.W - up / sc.Toe) * roll;
            return math.clamp(math.max(body, toe), 0f, at.W);
        }

        /// <summary>
        /// The narrow slots in otherwise flat faces, 0 none to 1 full depth: x, gullies the height of the wall
        /// (widest at the top, missing along some stretches, cracks.z); y, joints that stop at their bed (a bed is
        /// jointed along parts of its run only, so no two beds break up alike). u is the depth below the rim.
        /// </summary>
        public static float2 Slots(ref Scarp sc, in ScarpAt at, float3 cracks, float u, float bed)
        {
            float seed = sc.Seed % 977u * 1.37f;
            float gully = math.abs(noise.snoise(new float2((at.S + 0.14f * u) / 22f, seed)));
            float joint = math.abs(noise.snoise(new float2((at.S - 0.08f * u) / 13f, seed + 31f + 7.3f * bed)));
            float open = math.lerp(0.4f, 0.12f, math.saturate(u / math.max(at.RimY - at.FootY, 1f)));
            float jointed = math.saturate(0.6f + 1.4f * math.sin(at.S * 0.07f + bed * 2.4f + seed));
            return new float2(cracks.z * (1f - math.smoothstep(0f, open, gully)), jointed * (1f - math.smoothstep(0f, 0.22f, joint)));
        }

        /// <summary>Which bed a height is in, counted up through every repeat of the stack.</summary>
        public static float Bed(ref Scarp sc, float y)
        {
            Run(ref sc, y, out var bed);
            return bed;
        }

        /// <summary>How far the beds below a height carry the face outwards in all (it rises with height, so the
        /// difference between two heights is the run between them): a hard bed is near vertical (Lean), a soft one
        /// a bench (Bench). Also which bed the height is in, counted up through every repeat of the stack.</summary>
        static float Run(ref Scarp sc, float y, out float bed)
        {
            ref var layers = ref sc.Layers;
            int n = layers.Length;
            bed = 0f;
            if (n == 0 || sc.LayerSpan <= 0f)
                return sc.Lean * y;
            float total = 0f;
            for (int i = 0; i < n; i++)
                total += layers[i].Thickness * math.lerp(sc.Bench, sc.Lean, math.smoothstep(0.3f, 0.7f, layers[i].Hardness));
            float stack = math.floor(y / sc.LayerSpan), e = y - sc.LayerSpan * stack, run = stack * total;
            for (int i = 0; i < n; i++)
            {
                float thick = layers[i].Thickness, slope = math.lerp(sc.Bench, sc.Lean, math.smoothstep(0.3f, 0.7f, layers[i].Hardness));
                if (e < thick || i == n - 1)
                {
                    bed = stack * n + i;
                    return run + slope * e;
                }
                run += slope * thick;
                e -= thick;
            }
            return run;
        }

        /// <summary>Hardness of the bed at a height. Beds are level and repeat up the stack; the change from one bed
        /// to the next is eased over a quarter metre.</summary>
        public static float Hardness(ref Scarp sc, float y)
        {
            ref var layers = ref sc.Layers;
            int n = layers.Length;
            if (n == 0 || sc.LayerSpan <= 0f)
                return 0.5f;
            float e = y - sc.LayerSpan * math.floor(y / sc.LayerSpan), bottom = 0f;
            const float blend = 0.25f;
            for (int i = 0; i < n; i++)
            {
                float thick = layers[i].Thickness;
                if (e < bottom + thick || i == n - 1)
                {
                    float into = e - bottom, left = thick - into, hard = layers[i].Hardness;
                    if (into < blend)
                        return math.lerp(0.5f * (layers[(i + n - 1) % n].Hardness + hard), hard, into / blend);
                    if (left < blend)
                        return math.lerp(0.5f * (layers[(i + 1) % n].Hardness + hard), hard, math.max(left, 0f) / blend);
                    return hard;
                }
                bottom += thick;
            }
            return 0.5f;
        }

        /// <summary>What a column of the face takes from its place along the rim alone: x, how far the wall swings
        /// in and out there (bays, and roughness); y, how far the beds are lifted there (their tilt and roll);
        /// z, how deep gullies cut there (0 none).</summary>
        public static float3 Cracks(ref Scarp sc, float s)
        {
            float seed = sc.Seed % 977u * 1.37f;
            float bay = noise.snoise(new float2(s / 45f, seed + 5f));
            float fine = noise.snoise(new float2(s / 1.2f, seed + 67f));
            float roll = noise.snoise(new float2(s / 35f, seed + 11f));
            float cut = math.saturate(0.5f + 1.2f * noise.snoise(new float2(s / 60f, seed + 90f)));
            return new float3(sc.Crack * (2.5f * bay + 0.3f * fine), sc.Dip * (s - 0.5f * sc.Length) + 3f * sc.Crack * roll, cut);
        }

        /// <summary>
        /// For the terrain mesh: whether a vertex is in a strip its grid can leave to the skin. cut is the distance to
        /// the strip's edge (negative inside, where the terrain shader cuts the surface away); y is the height the
        /// mesh carries there instead of the stepped ground, the level run-on of the side it is nearer, so the cut
        /// edges land on the rim and on the foot; h is the true ground there, which the mesh keeps where the strip
        /// is about to get too thin to cut, so the end of the cut shows the stepped ground and not a wall across
        /// it. False (and the mesh unchanged) away from any strip.
        /// </summary>
        public static bool Strip(ref ScarpSetBlob set, float2 xz, float step, float h, out float cut, out float y)
        {
            cut = 100f;
            y = 0f;
            if (!Inside(set.Bounds, xz))
                return false;
            bool found = false;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                ref var sc = ref set.Scarps[i];
                if (!Inside(sc.Bounds, xz) || !Locate(ref sc, xz, out var at))
                    continue;
                float thin = ClipSteps * step - at.W;
                var c = math.max(math.max(-at.D, at.D - at.W), thin);
                if (c >= cut || c > 2f * step + 1f)
                    continue;
                cut = c;
                y = math.lerp(at.D < 0.5f * at.W ? at.RimY : at.FootY + sc.Apron * (at.W - at.D), h, math.saturate(1f + thin / step));
                found = true;
            }
            return found;
        }
    }
}
