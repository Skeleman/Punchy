using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// The Ashlands ribcage's layout (W-114), derived from its landmark instance alone so every machine builds the same
    /// bones: its frame, the broken ribs (one bit per rib) and the two fallen rib pieces lying in the ash.
    /// </summary>
    public struct RibcageLayout
    {
        public float2 Centre, Axis, Side;
        /// <summary>The level the bones stand on (the instance's base height).</summary>
        public float Base;
        public uint Key;
        public float Seed;
        /// <summary>Bit (i * 2 + side) set when that rib is broken.</summary>
        public uint Broken;
        /// <summary>The fallen pieces (local u, v): each a bent bone of two segments a-b-c, and how many there are.</summary>
        public float2 FallenA0, FallenB0, FallenC0, FallenA1, FallenB1, FallenC1;
        public int Fallen;
    }

    /// <summary>
    /// The ribcage's shape (W-114): the skeleton of something enormous half sunk in the ash, big enough to read as a
    /// mountain from afar. Eleven pairs of ribs arch from a spine 74 m up down into ash drifts banked at their feet; a
    /// neck runs down to a half-buried skull and a tail runs out into the ash. Some ribs are snapped (an upper stub on
    /// the spine, a stump standing in the drift) and two of the missing pieces lie fallen beside the cage.
    /// Local u runs along Facing (the skull at -u), v along Facing turned a quarter left, y up from Base (matching
    /// LandmarkHit.Local). All analytic (no blob): LandmarkSampling.Shape banks the drifts (Ground),
    /// TerrainSource.TowerWall keeps bodies out of the bones (Wall) and FloorUnder stands them on the fallen pieces
    /// and the skull (Floor), so what's drawn (RibcageSystem) is exactly what's met. Seed-derived, so solo, host
    /// and client all have the same bones.
    /// </summary>
    public static class RibcageShape
    {
        public const int Pairs = 11, RibSegments = 12;
        public const float FirstRib = -90f, RibSpacing = 18f, Sweep = 12f, RibEnd = 1.75f;
        public const float SpineTop = 74f, SpineRadius = 4f;
        public const float NeckStart = -130f, SpineStart = -100f, SpineEnd = 95f, TailEnd = 150f;
        public const float SkullU = -142f, SkullY = 6f;
        public static readonly float3 SkullRadii = new float3(17f, 10f, 12f); // u, y, v
        public const float FallenRadius = 2.8f, FallenSink = 1.2f;
        /// <summary>A body's radius, kept off the bones; how far a top may be above the feet and still be stood on.</summary>
        public const float Margin = 0.4f, FloorReach = 0.45f;
        /// <summary>Nothing of the ribcage (bones or drifts) reaches further from the centre than this.</summary>
        public const float Reach = 165f;

        public static RibcageLayout Layout(in LandmarkInstance inst)
        {
            var l = new RibcageLayout
            {
                Centre = inst.Position,
                Axis = inst.Facing,
                Side = new float2(-inst.Facing.y, inst.Facing.x),
                Base = inst.BaseHeight,
                Key = inst.Key,
                Seed = inst.Seed
            };
            for (int i = 0; i < Pairs; i++)
                for (int side = 0; side < 2; side++)
                {
                    if (!Break(inst.Key, i, side, out var tb, out _))
                        continue;
                    l.Broken |= 1u << (i * 2 + side);
                    if (l.Fallen >= 2)
                        continue;
                    // The piece that snapped off lies beside the cage, roughly along it, half sunk in the ash.
                    var h = Hash(inst.Key, i, side, 0x51EDu);
                    var s = side == 0 ? 1f : -1f;
                    var w = Width(i);
                    var mid = new float2(RibU(i) + 4f + Frac(h, 0) * 8f, s * (w + 12f + Frac(h, 8) * 8f));
                    var turn = (Frac(h, 16) - 0.5f) * 1.2f;
                    var dir = new float2(math.cos(turn), math.sin(turn));
                    var bend = new float2(-dir.y, dir.x) * (s * (1.5f + Frac(h, 24) * 1.5f));
                    var half = 9f + 4f * tb;
                    if (l.Fallen == 0) { l.FallenA0 = mid - dir * half; l.FallenB0 = mid + bend; l.FallenC0 = mid + dir * half; }
                    else { l.FallenA1 = mid - dir * half; l.FallenB1 = mid + bend; l.FallenC1 = mid + dir * half; }
                    l.Fallen++;
                }
            return l;
        }

        static uint Hash(uint key, int i, int side, uint salt) => BiomeSampling.Scramble(key + 0x9E3779B9u * (uint)(i * 2 + side + 1) + salt);
        static float Frac(uint h, int shift) => ((h >> shift) & 255u) / 255f;

        /// <summary>Whether rib (i, side) is snapped: its upper stub ends at angle tb, its standing stump starts at tb2.</summary>
        public static bool Break(uint key, int i, int side, out float tb, out float tb2)
        {
            var b = Hash(key, i, side, 0u);
            tb = 0.55f + 0.7f * Frac(b, 8);
            tb2 = tb + 0.25f + 0.4f * Frac(b, 16);
            return (b & 255u) < 70u;
        }

        /// <summary>The spine's height above Base along the body (u), from the skull's neck to the tail's tip.</summary>
        public static float SpineY(float u)
        {
            if (u < SpineStart)
                return 7f + 44f * math.smoothstep(NeckStart, SpineStart, u);
            if (u > SpineEnd)
                return -1f + 44f * math.smoothstep(TailEnd, SpineEnd, u);
            var x = (u + 10f) / 105f;
            return SpineTop * (1f - 0.42f * x * x);
        }

        /// <summary>The spine's radius: thick over the cage, thinning down the neck and out along the tail.</summary>
        public static float SpineR(float u) =>
            u < SpineStart ? math.lerp(2.6f, SpineRadius, math.saturate((u - NeckStart) / (SpineStart - NeckStart)))
            : u > SpineEnd ? math.lerp(SpineRadius, 1.2f, math.saturate((u - SpineEnd) / (TailEnd - SpineEnd)))
            : SpineRadius;

        public static float RibU(int i) => FirstRib + RibSpacing * i;
        static float RibTop(int i) => SpineY(RibU(i)) - 2f;
        /// <summary>Rib pair i's half-width at its widest (m out from the spine).</summary>
        public static float Width(int i) => 0.8f * RibTop(i) + 10f;

        /// <summary>A point on rib (i, side) at angle t (0 at the spine, RibEnd buried at its foot), local (u, y, v).</summary>
        public static float3 Rib(int i, int side, float t)
        {
            var s = side == 0 ? 1f : -1f;
            return new float3(RibU(i) + Sweep * (1f - math.cos(t)), RibTop(i) * math.cos(t), s * Width(i) * math.sin(t));
        }

        /// <summary>The rib's radius at angle t: thick at the spine, thinner towards the foot.</summary>
        public static float RibR(float t) => 3.4f - 1.0f * t / RibEnd;

        /// <summary>A rib foot (where it enters the ash), local (u, v).</summary>
        public static float2 Foot(int i, int side)
        {
            var p = Rib(i, side, RibEnd * 0.9f);
            return new float2(p.x, p.z);
        }

        /// <summary>The skull's centre, local (u, y, v).</summary>
        public static float3 Skull => new float3(SkullU, SkullY, 0f);

        /// <summary>Fallen piece k's three points (local u, v); its axis lies FallenRadius - FallenSink above Base.</summary>
        public static void Fallen(in RibcageLayout l, int k, out float2 a, out float2 b, out float2 c)
        {
            if (k == 0) { a = l.FallenA0; b = l.FallenB0; c = l.FallenC0; }
            else { a = l.FallenA1; b = l.FallenB1; c = l.FallenC1; }
        }

        public static float FallenY => FallenRadius - FallenSink;

        static float SegDist(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var s = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return math.distance(p, a + ab * s);
        }

        /// <summary>
        /// Ground with the ash drifts: a mound at every rib foot (short inside, a long fall outside), a low bank along
        /// each side joining them, drifts round the skull, the tail and the fallen pieces (with a ramp up onto one end
        /// of each, so they can be climbed), and fine wind ripples on the ash. Only ever adds, and fades in from nothing.
        /// </summary>
        public static float Ground(in LandmarkInstance inst, float2 local, float h)
        {
            var u = local.x;
            var v = local.y;
            var side = v >= 0f ? 0 : 1;
            var s = side == 0 ? 1f : -1f;
            var add = 0f;

            // Rib feet: only the nearest few ribs on this side.
            var near = (int)math.round((u - FirstRib - 12f) / RibSpacing);
            for (int i = math.max(0, near - 1); i <= math.min(Pairs - 1, near + 1); i++)
            {
                var foot = Foot(i, side);
                var d = local - foot;
                var outward = d.y * s > 0f;
                var r = math.length(new float2(d.x / 11f, d.y / (outward ? 24f : 12f)));
                if (r < 1f)
                {
                    var hf = 5f + 5f * Frac(Hash(inst.Key, i, side, 0xA54u), 0);
                    var k = 1f - r;
                    add = math.max(add, hf * k * k * (3f - 2f * k));
                }
            }

            // The bank joining the feet, fading in and out past the end ribs.
            {
                var uc = math.clamp(u - 12f, FirstRib, RibU(Pairs - 1));
                var w = (0.8f * (SpineY(uc) - 2f) + 10f) * 0.97f;
                var d = (v * s - w) / (v * s > w ? 20f : 12f);
                var along = math.smoothstep(FirstRib - 10f, FirstRib + 15f, u - 12f) * math.smoothstep(RibU(Pairs - 1) + 25f, RibU(Pairs - 1), u - 12f);
                var bank = (2.2f + 1.2f * noise.snoise(new float2(u * 0.04f, inst.Seed * 31f + side))) * math.saturate(1f - d * d) * along;
                add = math.max(add, bank);
            }

            // The skull, sunk to its cheeks.
            {
                var d = new float2((u - SkullU) / 26f, v / 20f);
                var r = math.length(d);
                if (r < 1f)
                    add = math.max(add, 5f * (1f - r) * (1f - r) * (2f - (1f - r)));
            }

            // The tail's tip runs out under the ash.
            {
                var t = math.saturate((u - 115f) / 40f) * math.smoothstep(165f, 145f, u);
                var d = math.abs(v) / 9f;
                if (d < 1f)
                    add = math.max(add, 2.5f * t * (1f - d * d));
            }

            // Fallen pieces: banked ash round each, and a drift ramp onto its first end.
            var l = Layout(inst);
            for (int k = 0; k < l.Fallen; k++)
            {
                Fallen(l, k, out var a, out var b, out var c);
                var d = math.min(SegDist(local, a, b), SegDist(local, b, c));
                if (d < 9f)
                    add = math.max(add, 1.8f * (1f - d / 9f) * (1f - d / 9f) + 0.4f);
                var dir = math.normalizesafe(a - b);
                var along = math.dot(local - a, dir);
                var across = math.abs(math.dot(local - a, new float2(-dir.y, dir.x)));
                if (along > -2f && along < 13f && across < 3.5f)
                {
                    var top = FallenY + FallenRadius - 0.35f;
                    var ramp = top * math.saturate(1f - along / 13f) * math.saturate((3.5f - across) / 1.5f);
                    add = math.max(add, ramp);
                }
            }

            if (add <= 0f)
                return h;
            // Wind ripples on the drifts (sharp-crested, across the body), stronger where the ash lies deep.
            var across2 = u * 0.9f + 2.2f * math.sin(v * 0.05f + inst.Seed * 6f);
            var ripple = 0.12f * (math.sin(across2) + 0.35f * math.sin(2f * across2)) * math.saturate(add / 3f);
            var target = LandmarkSampling.SmoothMax(h, inst.BaseHeight + add + ripple, 3f);
            return math.lerp(h, target, math.smoothstep(0f, 1f, add));
        }

        /// <summary>A world point in the ribcage's frame: (u, y above Base, v).</summary>
        public static float3 Local(in RibcageLayout l, float3 p)
        {
            var d = p.xz - l.Centre;
            return new float3(math.dot(d, l.Axis), p.y - l.Base, math.dot(d, l.Side));
        }

        /// <summary>Squared distance between segments p0-p1 and q0-q1.</summary>
        static float SegSeg(float3 p0, float3 p1, float3 q0, float3 q1)
        {
            var d1 = p1 - p0;
            var d2 = q1 - q0;
            var r = p0 - q0;
            float a = math.dot(d1, d1), e = math.dot(d2, d2), f = math.dot(d2, r);
            float s, t;
            if (a < 1e-6f)
            {
                s = 0f;
                t = math.saturate(f / math.max(e, 1e-6f));
            }
            else
            {
                var c = math.dot(d1, r);
                var b = math.dot(d1, d2);
                var denom = a * e - b * b;
                s = denom > 1e-6f ? math.saturate((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / math.max(e, 1e-6f);
                if (t < 0f) { t = 0f; s = math.saturate(-c / a); }
                else if (t > 1f) { t = 1f; s = math.saturate((b - c) / a); }
            }
            return math.lengthsq(p0 + d1 * s - (q0 + d2 * t));
        }

        /// <summary>Whether an arc of rib (i, side) from angle t0 to t1 comes within its radius (plus Margin) of a body.</summary>
        static bool RibArc(int i, int side, float t0, float t1, float3 b0, float3 b1)
        {
            var steps = math.max(2, (int)math.ceil(RibSegments * (t1 - t0) / RibEnd));
            var prev = Rib(i, side, t0);
            for (int k = 1; k <= steps; k++)
            {
                var t = math.lerp(t0, t1, k / (float)steps);
                var next = Rib(i, side, t);
                var r = RibR(t) + Margin;
                if (SegSeg(prev, next, b0, b1) < r * r)
                    return true;
                prev = next;
            }
            return false;
        }

        /// <summary>The y span of an ellipsoid (centre c, radii r as u, y, v) over local (u, v), if it's over it.</summary>
        static bool Ellipsoid(float3 c, float3 r, float u, float v, out float lo, out float hi)
        {
            var x = (u - c.x) / r.x;
            var z = (v - c.z) / r.z;
            var k = 1f - x * x - z * z;
            lo = hi = 0f;
            if (k <= 0f)
                return false;
            var dy = r.y * math.sqrt(k);
            lo = c.y - dy;
            hi = c.y + dy;
            return true;
        }

        static bool Overlap(float lo, float hi, float a, float b) => hi > a && lo < b;

        /// <summary>
        /// Whether a body with its feet at p would stand in a bone (rib, spine, skull or fallen piece), counting only
        /// bone above stepUp over its feet and below its head, so it walks under the arches and between the ribs.
        /// </summary>
        public static bool Wall(in RibcageLayout l, float3 p, float stepUp, float head)
        {
            var q = Local(l, p);
            if (math.abs(q.x) > Reach || math.abs(q.z) > Reach)
                return false;
            float lo = q.y + stepUp, hi = q.y + head;
            var b0 = new float3(q.x, lo, q.z);
            var b1 = new float3(q.x, hi, q.z);

            // Ribs: only the ones whose sweep spans this u, on this side.
            var side = q.z >= 0f ? 0 : 1;
            if (math.abs(q.z) < 90f)
                for (int i = 0; i < Pairs; i++)
                {
                    var u0 = RibU(i);
                    if (q.x < u0 - 4f || q.x > u0 + Sweep * 1.2f + 4f)
                        continue;
                    if ((l.Broken & (1u << (i * 2 + side))) != 0)
                    {
                        Break(l.Key, i, side, out var tb, out var tb2);
                        if (RibArc(i, side, 0f, tb, b0, b1) || RibArc(i, side, tb2, RibEnd, b0, b1))
                            return true;
                    }
                    else if (RibArc(i, side, 0f, RibEnd, b0, b1))
                        return true;
                }

            // Spine, neck and tail (its slope ignored: a round section at this u).
            if (q.x > NeckStart && q.x < TailEnd)
            {
                var r = SpineR(q.x) + Margin;
                if (math.abs(q.z) < r)
                {
                    var y = SpineY(q.x);
                    var dy = math.sqrt(r * r - q.z * q.z);
                    if (Overlap(y - dy, y + dy, lo, hi))
                        return true;
                }
            }

            // The skull.
            if (Ellipsoid(Skull, SkullRadii + Margin, q.x, q.z, out var slo, out var shi) && Overlap(slo, shi, lo, hi))
                return true;

            // Fallen pieces.
            var local = new float2(q.x, q.z);
            for (int k = 0; k < l.Fallen; k++)
            {
                Fallen(l, k, out var a, out var b, out var c);
                var d = math.min(SegDist(local, a, b), SegDist(local, b, c));
                var r = FallenRadius + Margin;
                if (d < r)
                {
                    var dy = math.sqrt(r * r - d * d);
                    if (Overlap(FallenY - dy, FallenY + dy, lo, hi))
                        return true;
                }
            }
            return false;
        }

        /// <summary>The highest fallen-piece or skull top under p that's no more than FloorReach above it, if any.</summary>
        public static bool Floor(in RibcageLayout l, float3 p, out float top)
        {
            top = float.MinValue;
            var q = Local(l, p);
            if (math.abs(q.x) > Reach || math.abs(q.z) > Reach)
                return false;
            var found = false;
            var limit = q.y + FloorReach;
            if (Ellipsoid(Skull, SkullRadii, q.x, q.z, out _, out var shi) && shi <= limit)
            {
                top = shi;
                found = true;
            }
            var local = new float2(q.x, q.z);
            for (int k = 0; k < l.Fallen; k++)
            {
                Fallen(l, k, out var a, out var b, out var c);
                var d = math.min(SegDist(local, a, b), SegDist(local, b, c));
                if (d >= FallenRadius)
                    continue;
                var t = FallenY + math.sqrt(FallenRadius * FallenRadius - d * d);
                if (t <= limit && t > top)
                {
                    top = t;
                    found = true;
                }
            }
            if (found)
                top += l.Base;
            return found;
        }
    }

    /// <summary>
    /// Places the Ashlands ribcage (W-114) after every other landmark, on its own spot: the deepest level, clear cell of
    /// the largest patch of the biome tagged BiomeTags.Ribcage, clear of every landmark's footprint. Nothing else
    /// moves; on an island without that biome there's simply no ribcage.
    /// </summary>
    public static class RibcageBaking
    {
        public const string TypeName = "AshRibcage";
        const float Footprint = 120f, MaxRelief = 12f, ReliefStep = 20f;

        public static LandmarkType LoadType() => Resources.Load<LandmarkType>(TypeName);

        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types)
        {
            var result = new List<LandmarkInstance>();
            var t = -1;
            for (int i = 0; i < types.Count; i++)
                if (types[i] != null && types[i].name == TypeName)
                    t = i;
            var biome = BiomeTags.First(palette, BiomeTags.Ribcage);
            if (t < 0 || biome < 0 || !source.Landmarks.IsCreated)
                return result;
            var listed = new byte[palette.Count];
            listed[biome] = 1;
            if (!SaltTowerBaking.FindSpot(source, types, listed, TypeName, Footprint, MaxRelief, ReliefStep, new List<LandmarkInstance>(),
                                          out var at, out var relief, out _, out var patch, out var inFromEdge, out var why))
            {
                Debug.Log(why ?? $"[landmarks] {TypeName}: no spot");
                return result;
            }
            var key = math.hash(new uint3(math.asuint(at.x), math.asuint(at.y), 0xB0E5u)) | 1u;
            var spin = BiomeSampling.Scramble(key);
            var facing = (spin & 0xFFFF) * (2f * math.PI / 65536f);
            var axis = new float2(math.cos(facing), math.sin(facing));
            var side = new float2(-axis.y, axis.x);
            // Base: the median ground over the cage, so it neither floats over hollows nor sinks into rises.
            var heights = new List<float>();
            for (float u = -150f; u <= 150f; u += 15f)
                for (float v = -75f; v <= 75f; v += 15f)
                    heights.Add(source.SampleHeight(at + axis * u + side * v));
            heights.Sort();
            var inst = new LandmarkInstance
            {
                Position = at,
                Facing = axis,
                Scale = 1f,
                Type = t,
                BaseHeight = heights[heights.Count / 2],
                Seed = (BiomeSampling.Scramble(spin) >> 8) * (1f / 16777216f),
                Key = key
            };
            result.Add(inst);
            Debug.Log($"[landmarks] {TypeName} at {at.x:F1},{at.y:F1}: facing {math.degrees(facing):F0} deg, base {inst.BaseHeight:F1}, " +
                      $"relief {relief:F2} m, patch {patch} cells, {inFromEdge:F0} m in from its edge");
            return result;
        }
    }
}
