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
        /// <summary>Shaped after settlement plots are levelled (a river notch's bank, W-153), not with the other scarps.</summary>
        public bool Late;
        /// <summary>The stone the scarp is cut through (StoneKind, W-166), from the biome at the middle of its face.
        /// ScarpBake.Stone folds its row into Crack, Push and Setback; the rest is paint, kept as offsets so a
        /// zeroed scarp paints as plain rock: the tone's tint less one, how much stronger the beds' light and dark
        /// and the joints' grime read (0 as plain), and how much smoother the face is.</summary>
        public byte Stone;
        public float3 Tint;
        public float Beds, Joints, Polish;
        /// <summary>Shape offsets from the stone, 0 as plain (W-166 row 2b): how much wider apart its gullies run
        /// (rib spacing, less one), and how much likelier a chimney is in each cell along the rim (jaggedness).</summary>
        public float Gully, Chimneys;
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
        public static float Shape(ref ScarpSetBlob set, float2 xz, float h, bool late = false)
        {
            if (!Inside(set.Bounds, xz))
                return h;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                ref var sc = ref set.Scarps[i];
                if (sc.Late == late && Inside(sc.Bounds, xz))
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
        /// bench through a soft one, weathered back under the hard bed above it (an overhang). Nothing holds along
        /// the rim for long (row 1b): beds thicken, thin and wander (<see cref="BedY"/>), each bed juts out or sinks
        /// back in blocks of its own length with sharp or rounded corners, benches widen, narrow and break off
        /// (<see cref="Vary"/>). Gullies and chimneys run the height of the face on a slant, joints break each hard
        /// bed into slabs of its own, and it ends in a toe: scree for a canyon wall, a flat wash under an undercut
        /// for a river bank.
        /// </summary>
        public static float Face(ref Scarp sc, in ScarpAt at, float3 cracks, float y)
        {
            float u = at.RimY - y, up = y - at.FootY, q = math.saturate(at.W / sc.Width);
            // The top edge is rounded over a height that grows with age.
            float roll = math.smoothstep(0f, 0.4f + 1.2f * sc.Age, u);
            // Beds aren't level or even: they tilt, roll and swell along the rim.
            float bedY = BedY(ref sc, at.S, y, cracks), topY = BedY(ref sc, at.S, at.RimY, cracks);
            float lean = Run(ref sc, at.S, math.min(bedY, topY), topY);
            // How far the bed here stands out (hard, pushed out by its own amount along this stretch) or weathers back.
            float stand = Stand(ref sc, at.S, bedY, out var bed, out var hard);
            var slot = Slots(ref sc, at, cracks, u, bed);
            float slots = -sc.Crack * (4.5f * slot.x + 0.9f * hard * slot.y);
            float body = q * (roll * (sc.Setback + sc.Push * stand + cracks.x + slots) + lean);
            // Row 2: the turf cap juts out a little over the soil under it, which weathers back (a small overhang
            // along the rim), broken where the turf has slumped away.
            float turf = Turf(ref sc, at.S, out var depth);
            body += q * turf * (math.smoothstep(0f, 0.12f, u) * (1f - math.smoothstep(depth, depth + 0.25f, u))
                                - 0.8f * math.smoothstep(depth, depth + 0.3f, u) * (1f - math.smoothstep(depth + 0.5f, depth + 1.2f, u)));
            if (sc.Notch > 0f)
                body -= sc.Notch * q * math.smoothstep(0.1f, 0.5f, up) * (1f - math.smoothstep(0.8f, 1.3f, up));
            float toe = (at.W - up / sc.Toe) * roll;
            return math.clamp(math.max(body, toe), 0f, at.W);
        }

        /// <summary>
        /// The narrow slots in otherwise flat faces, 0 none to 1 full depth: x, gullies the height of the wall
        /// (widest at the top, missing along some stretches, cracks.z) and the odd chimney (a narrow slot the full
        /// height, anywhere); y, joints that stop at their bed (a bed is jointed along parts of its run only, so no
        /// two beds break up alike). u is the depth below the rim.
        /// </summary>
        public static float2 Slots(ref Scarp sc, in ScarpAt at, float3 cracks, float u, float bed)
        {
            float seed = sc.Seed % 977u * 1.37f;
            float gully = math.abs(noise.snoise(new float2((at.S + 0.14f * u) / (22f * (1f + sc.Gully)), seed)));
            float joint = math.abs(noise.snoise(new float2((at.S - 0.08f * u) / 13f, seed + 31f + 7.3f * bed)));
            float open = math.lerp(0.4f, 0.12f, math.saturate(u / math.max(at.RimY - at.FootY, 1f)));
            float jointed = math.saturate(0.6f + 1.4f * math.sin(at.S * 0.07f + bed * 2.4f + seed));
            // Chimneys: about one cell in three along the rim has one, at its own place, width and slant.
            float scale = Scale(ref sc), cell = 16f * scale, c = math.floor(at.S / cell);
            float chimney = 0f;
            if (Rand(sc.Seed, 7001, c, 0) > math.clamp(0.65f - sc.Chimneys, 0.3f, 0.95f))
            {
                float mid = (c + 0.2f + 0.6f * Rand(sc.Seed, 7001, c, 1)) * cell;
                float half = (0.5f + 1.1f * Rand(sc.Seed, 7001, c, 2)) * scale;
                float off = math.abs(at.S + 0.1f * u * (Rand(sc.Seed, 7001, c, 3) - 0.5f) - mid);
                chimney = 0.7f * (1f - math.smoothstep(0.35f * half, half, off));
            }
            float gullies = cracks.z * (1f - math.smoothstep(0f, open, gully));
            return new float2(math.max(gullies, chimney), jointed * (1f - math.smoothstep(0f, 0.22f, joint)));
        }

        /// <summary>The height a point of the face reads its bed from: its own height lifted by the beds' tilt and
        /// roll at this place along the rim (cracks.y), and warped along the rim at two sizes so beds thicken and
        /// thin from place to place. The warp never folds (it rises with height), so the beds keep their order.</summary>
        public static float BedY(ref Scarp sc, float s, float y, float3 cracks)
        {
            float span = math.max(sc.LayerSpan, 0.5f), seed = sc.Seed % 977u * 1.37f;
            float a = 0.06f * span;
            float big = noise.snoise(new float2(s / math.max(1.1f * span, 6f), y / (0.35f * span) + seed + 130f));
            float small = noise.snoise(new float2(s / math.max(0.3f * span, 2f), y / (0.2f * span) + seed + 170f));
            return y + cracks.y + a * (big + 0.5f * small);
        }

        /// <summary>Which bed a bed height (<see cref="BedY"/>) is in, counted up through every repeat of the
        /// stack.</summary>
        public static float Bed(ref Scarp sc, float bedY)
        {
            Locate(ref sc, bedY, out var bed, out _, out _);
            return bed;
        }

        /// <summary>Hardness of the bed at a bed height, eased into the next bed over a width that changes along
        /// the rim (sharp corners in places, rounded ones in others).</summary>
        public static float Hardness(ref Scarp sc, float s, float bedY)
        {
            Stand(ref sc, s, bedY, out _, out var hard);
            return hard;
        }

        /// <summary>
        /// How far the bed at a bed height stands out from the face, in units of the scarp's Push: a hard bed
        /// stands out by its own amount along this stretch of rim, a soft one weathers back. Eased into the bed
        /// above and below over a width of their own (a narrow ease makes a square ledge or overhang, a wide one a
        /// sloped shelf or a rounded nose). Also the bed and its hardness, eased the same way.
        /// </summary>
        static float Stand(ref Scarp sc, float s, float bedY, out float bed, out float hard)
        {
            ref var layers = ref sc.Layers;
            int n = layers.Length;
            Locate(ref sc, bedY, out bed, out var i, out var into);
            if (n == 0)
            {
                hard = 0.5f;
                return 0f;
            }
            float thick = layers[i].Thickness, left = thick - into;
            hard = layers[i].Hardness;
            var vary = Vary(ref sc, bed, s, bedY);
            float stand = vary.x * (hard - 0.5f);
            // Ease to the bed below (over this bed's own width) or above (over the width of the one above).
            int below = (i + n - 1) % n, above = (i + 1) % n;
            float easeLo = math.min(vary.z, 0.45f * math.min(thick, layers[below].Thickness));
            if (into < easeLo)
            {
                float other = layers[below].Hardness, k = 0.5f + 0.5f * into / easeLo;
                float otherStand = Vary(ref sc, bed - 1f, s, bedY).x * (other - 0.5f);
                hard = math.lerp(other, hard, k);
                return math.lerp(otherStand, stand, k);
            }
            var up = Vary(ref sc, bed + 1f, s, bedY);
            float easeHi = math.min(up.z, 0.45f * math.min(thick, layers[above].Thickness));
            if (left < easeHi)
            {
                float other = layers[above].Hardness, k = 0.5f + 0.5f * math.max(left, 0f) / easeHi;
                hard = math.lerp(other, hard, k);
                return math.lerp(up.x * (other - 0.5f), stand, k);
            }
            return stand;
        }

        /// <summary>
        /// What one bed does at one place along the rim, seeded per scarp and bed (rule 22): x, how far it stands
        /// out (0.1 to 2 times Push, in blocks a few metres long that start and stop with a corner, sharp or
        /// rounded, plus a small wobble); y, how wide its bench is if it is soft (0 none, where the bench pinches
        /// out, to 1.6 times Bench), breaking off every few metres; z, the height it eases into the next bed over.
        /// </summary>
        static float3 Vary(ref Scarp sc, float bed, float s, float bedY)
        {
            float scale = Scale(ref sc);
            int b = (int)bed;
            // Block ends lean, each bed its own way, so blocks are not boxes and their ends do not line up.
            float cut = s + 1.6f * (Rand(sc.Seed, b, 0f, 20) - 0.5f) * bedY;
            // Blocks: each bed has its own block length and phase; each block its own push and corner.
            float len = (3f + 7f * Rand(sc.Seed, b, 0f, 11)) * scale;
            float x = cut / len + 7f * Rand(sc.Seed, b, 0f, 12), c = math.floor(x), f = x - c;
            float corner = 0.12f + 0.4f * Rand(sc.Seed, b, c, 13);
            float v = math.lerp(Rand(sc.Seed, b, c, 14), Rand(sc.Seed, b, c + 1f, 14), math.smoothstep(1f - corner, 1f, f));
            float push = (0.1f + 1.9f * v * math.sqrt(v)) * (0.6f + 0.8f * Rand(sc.Seed, b, 0f, 15));
            push = math.max(push + 0.25f * (2f * Smooth(sc.Seed, b, s / (1.5f * scale), 16) - 1f), 0f);
            float r = Rand(sc.Seed, b, c, 19);
            float ease = (0.08f + 0.9f * r * r) * scale;
            return new float3(push, Bench(ref sc, b, s), ease);
        }

        /// <summary>How wide a soft bed's bench is here, 0 (pinched out) to 1.6 times Bench: wide in places, gone
        /// in others, at two sizes.</summary>
        static float Bench(ref Scarp sc, int b, float s)
        {
            float scale = Scale(ref sc);
            float n = 0.75f * Smooth(sc.Seed, b, s / (6f * scale), 17) + 0.25f * Smooth(sc.Seed, b, s / (2f * scale), 18);
            return 1.6f * math.smoothstep(0.32f, 0.68f, n);
        }

        /// <summary>The run of the face between two bed heights (lo below hi): through each bed its slope times
        /// the height of it in between, near vertical for a hard bed (Lean), a bench for a soft one (Bench times
        /// how wide it is here, <see cref="Vary"/>, so a bench pinches out where that is 0).</summary>
        static float Run(ref Scarp sc, float s, float lo, float hi)
        {
            ref var layers = ref sc.Layers;
            int n = layers.Length;
            if (n == 0 || sc.LayerSpan <= 0f || hi <= lo)
                return sc.Lean * math.max(hi - lo, 0f);
            Locate(ref sc, lo, out var bed, out var i, out var into);
            float y = lo, run = 0f;
            for (int guard = 0; guard < 256 && y < hi; guard++)
            {
                float top = y - into + layers[i].Thickness;
                float k = math.smoothstep(0.3f, 0.7f, layers[i].Hardness);
                float soft = k < 1f ? sc.Lean + (sc.Bench - sc.Lean) * Bench(ref sc, (int)bed, s) : sc.Lean;
                float slope = math.lerp(soft, sc.Lean, k);
                run += slope * (math.min(top, hi) - y);
                y = top;
                into = 0f;
                bed += 1f;
                i = (i + 1) % n;
            }
            return run;
        }

        /// <summary>Which bed a bed height is in (counted up through every repeat of the stack), which layer of
        /// the stack that is, and how far up into it the height is.</summary>
        static void Locate(ref Scarp sc, float y, out float bed, out int layer, out float into)
        {
            ref var layers = ref sc.Layers;
            int n = layers.Length;
            bed = 0f;
            layer = 0;
            into = 0f;
            if (n == 0 || sc.LayerSpan <= 0f)
                return;
            float stack = math.floor(y / sc.LayerSpan), e = y - sc.LayerSpan * stack;
            for (int i = 0; i < n; i++)
            {
                if (e < layers[i].Thickness || i == n - 1)
                {
                    bed = stack * n + i;
                    layer = i;
                    into = math.min(e, layers[i].Thickness);
                    return;
                }
                e -= layers[i].Thickness;
            }
        }

        /// <summary>How big the along-rim pieces are for this scarp: full size on a canyon wall, smaller on a low
        /// bank (but not so small they turn to noise).</summary>
        static float Scale(ref Scarp sc) => math.lerp(0.35f, 1f, math.saturate(sc.Width / 32f));

        /// <summary>
        /// The turf cap at this place along the rim (row 2): how far it juts out over the soil under it (m, 0 where
        /// it has slumped away) and how deep the turf and root mat is (depth, m). Thicker and more broken on a river
        /// bank (earth over rock), thin on a rocky wall. Seeded per scarp (rule 22).
        /// </summary>
        public static float Turf(ref Scarp sc, float s, out float depth)
        {
            bool earth = sc.Kind == ScarpKind.RiverCut;
            float seed = sc.Seed % 977u * 1.37f;
            float n = noise.snoise(new float2(s / (earth ? 3.5f : 9f), seed + 61f));
            float fine = noise.snoise(new float2(s / 0.9f, seed + 67f));
            depth = (earth ? 0.3f : 0.15f) + (earth ? 0.25f : 0.1f) * (0.5f + 0.5f * fine);
            return (earth ? 0.4f : 0.18f) * math.smoothstep(-0.35f, 0.25f, n + 0.25f * fine);
        }

        /// <summary>A repeatable draw in [0, 1) from a scarp seed, a bed (or other key), a cell and a channel
        /// (a full-avalanche integer hash, so neighbouring cells don't correlate).</summary>
        static float Rand(uint seed, int key, float cell, int channel)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)key * 0x85EBCA77u ^ (uint)(int)cell * 0xC2B2AE3Du ^ (uint)channel * 0x27D4EB2Fu;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h >> 8) * (1f / 16777216f);
        }

        /// <summary>Smooth value noise in [0, 1] along x, its own for each seed, key and channel.</summary>
        static float Smooth(uint seed, int key, float x, int channel)
        {
            float c = math.floor(x), f = x - c;
            return math.lerp(Rand(seed, key, c, channel), Rand(seed, key, c + 1f, channel), f * f * (3f - 2f * f));
        }

        /// <summary>What a column of the face takes from its place along the rim alone: x, how far the wall swings
        /// in and out there (bays, and roughness); y, how far the beds are lifted there (their tilt and roll, at
        /// two sizes); z, how deep gullies cut there (0 none).</summary>
        public static float3 Cracks(ref Scarp sc, float s)
        {
            float seed = sc.Seed % 977u * 1.37f;
            float bay = noise.snoise(new float2(s / 45f, seed + 5f));
            float fine = noise.snoise(new float2(s / 1.2f, seed + 67f));
            float roll = noise.snoise(new float2(s / 35f, seed + 11f));
            float wave = noise.snoise(new float2(s / (12f * Scale(ref sc)), seed + 13f));
            float cut = math.saturate(0.5f + 1.2f * noise.snoise(new float2(s / 60f, seed + 90f)));
            return new float3(sc.Crack * (2.5f * bay + 0.3f * fine), sc.Dip * (s - 0.5f * sc.Length) + sc.Crack * (3f * roll + 1.2f * wave), cut);
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

        /// <summary>
        /// Distance from xz to the nearest scarp strip's edge (negative inside the strip), or 100 away from any. For
        /// scatters laid on a coarse height grid (grass, pebbles): heights interpolated across the step float off the
        /// face, so they stay out of the strip and a margin round it.
        /// </summary>
        public static float Edge(ref ScarpSetBlob set, float2 xz)
        {
            float edge = 100f;
            if (!Inside(set.Bounds, xz))
                return edge;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                ref var sc = ref set.Scarps[i];
                if (Inside(sc.Bounds, xz) && Locate(ref sc, xz, out var at))
                    edge = math.min(edge, math.max(-at.D, at.D - at.W));
            }
            return edge;
        }
    }
}
