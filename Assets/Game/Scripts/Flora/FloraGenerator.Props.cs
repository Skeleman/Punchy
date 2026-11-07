using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, small props: eggs, mine frames, torches, seats, driftwood, fire pits, hanging things, cocoons, hives. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A clutch of eggs: smooth ovals of mixed sizes in a golden-angle spiral, standing and leaning out from the
        /// middle, heaped a little higher there, in a low nest of grit or silk. The shells are drawn as the glossy
        /// (Cactus) part, so they catch the sun and the lantern.
        /// </summary>
        static void Eggs(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var egg = g.Range(s.Height);
            var count = Mathf.Max(1, g.Count(s.Branches));
            var spread = egg * (0.35f + 0.16f * Mathf.Sqrt(count)) * Mathf.Max(s.Spread, 0.3f);
            g.Height = egg * 0.9f;
            g.Width = spread * 2.3f;
            g.TrunkRadius = 0f;

            var nest = b.Blob(Vector3.up * egg * 0.02f, new Vector3(spread * 1.2f, egg * 0.18f, spread * 1.2f), Quaternion.identity,
                              g.Lod == 0 ? 1 : 0, 0.15f, g.R() * 100f, g.Bark, FloraPart.Bark);
            b.Occlude(nest, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y)));
            var turn = g.R() * 6.28f;
            for (int i = 0; i < count; i++)
            {
                var along = Mathf.Sqrt((i + 0.5f) / count);
                var flat = g.Flat(turn + i * 2.39996f);
                var heap = (1f - along) * egg * 0.35f;
                var centre = flat * spread * along * 0.9f + Vector3.up * (egg * 0.42f + heap);
                // Lying tilted out from the middle, each at its own angle, the outer ones further over.
                var tilt = Quaternion.AngleAxis(Mathf.Lerp(8f, 35f, along) + g.Range(-8f, 8f), Vector3.Cross(Vector3.up, flat));
                var color = g.FoliageColor();
                var size = egg * g.Range(0.75f, 1.2f);
                var shell = b.Blob(centre, new Vector3(size * 0.34f, size * 0.5f, size * 0.34f), tilt, g.Lod == 0 ? 2 : g.Lod == 1 ? 1 : 0,
                                   0.01f, g.R() * 100f, color, FloraPart.Cactus);
                b.Occlude(shell, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y + 0.4f)));
            }
        }

        /// <summary>
        /// A mine's timber frame, one metre either side of the middle and one metre to the lintel (placements scale it
        /// to the passage): two posts, a lintel resting on them and short braces in the corners. Later variants stand
        /// taller (MineFrameHeights), so a frame in a steep passage, whose roof is higher over its width, still reaches it.
        /// </summary>
        /// <summary>A mine frame's height to the lintel per variant, at scale 1 (its half-width is 1).</summary>
        public static readonly float[] MineFrameHeights = { 1f, 1.2f, 1.4f, 1.65f, 1.9f };

        static void MineFrame(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var tall = MineFrameHeights[Mathf.Clamp(g.Variant, 0, MineFrameHeights.Length - 1)];
            g.Height = tall;
            g.Width = 2.2f;
            g.TrunkRadius = 0f;
            var r = Mathf.Max(s.Thickness, 0.02f);
            var sides = g.Sides(6, 4, 4);
            for (int side = -1; side <= 1; side += 2)
            {
                var foot = new Vector3(side, -0.05f, 0f);
                var lean = g.Range(-0.02f, 0.02f);
                b.Tube(new List<Vector3> { foot, foot + new Vector3(lean, 1.02f * tall, 0f) }, new List<float> { r * 1.05f, r }, sides, g.Bark, FloraPart.Bark);
                if (g.Lod < 2)
                    b.Tube(new List<Vector3> { new Vector3(side * 0.98f, 1.02f * tall - 0.24f, 0f), new Vector3(side * 0.72f, 1.02f * tall - 0.03f, 0f) },
                           new List<float> { r * 0.6f, r * 0.6f }, sides, g.Bark * 0.9f, FloraPart.Bark);
            }
            b.Tube(new List<Vector3> { new Vector3(-1.12f, 1.02f * tall + r, 0f), new Vector3(1.12f, 1.02f * tall + r, 0f) },
                   new List<float> { r * 1.1f, r * 1.1f }, sides, g.Bark * 0.95f, FloraPart.Bark);
        }

        /// <summary>A wall torch: a stick leaning out from its bracket with a charred head (the flame is FireSystem's).</summary>
        static void Torch(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var length = g.Range(s.Height);
            g.Height = length;
            g.Width = 0.2f;
            g.TrunkRadius = 0f;
            var r = Mathf.Max(length * s.Thickness, 0.015f);
            // Leaning out (+Z, away from the wall it's on) by about 20°.
            var top = new Vector3(0f, length * 0.94f, length * 0.34f);
            b.Tube(new List<Vector3> { Vector3.zero, top }, new List<float> { r * 0.8f, r }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark);
            // The bracket: a band round the stick a third of the way up, and its arm back to the wall.
            if (g.Lod < 2)
            {
                var band = top * 0.32f;
                b.Tube(new List<Vector3> { band + new Vector3(0f, 0f, -r * 3f), band }, new List<float> { r * 0.5f, r * 0.5f }, 4, g.Accent, FloraPart.Bark);
                b.Blob(band, new Vector3(r * 1.4f, r * 0.9f, r * 1.4f), Quaternion.identity, 0, 0f, 0f, g.Accent, FloraPart.Bark);
            }
            var head = b.Blob(top, new Vector3(r * 1.6f, r * 2.4f, r * 1.6f), Quaternion.FromToRotation(Vector3.up, top.normalized),
                              g.Lod == 0 ? 1 : 0, 0.2f, g.R() * 100f, g.FoliageColor(), FloraPart.Bark);
            b.Occlude(head, (p, n) => 0.7f);
        }

        // Seats: fixed sizes (a sitter's hips go at the seat's height, and towns' Sit anchors expect it), scaled by the
        // prop that places them. Low colliders so walkers go round; a sitter ignores its own seat.
        public const float BenchSeat = 0.45f, LogSeat = 0.4f, StumpSeat = 0.42f;

        /// <summary>A plank bench on two stone feet, 1.8 m long (along X), its seat 0.45 m up; a sitter faces +Z.</summary>
        static void Bench(Grower g)
        {
            var b = g.B;
            g.Height = BenchSeat;
            g.Width = 1.8f;
            g.TrunkRadius = 0f;
            var stone = g.FoliageColor();
            for (int side = -1; side <= 1; side += 2)
                b.Box(new Vector3(side * 0.62f, (BenchSeat - 0.06f) * 0.5f, 0f), new Vector3(0.22f, BenchSeat - 0.06f, 0.36f),
                      Quaternion.Euler(0f, g.Range(-3f, 3f), 0f), stone, FloraPart.Bark);
            // Two planks side by side, a hair apart; or one carved stone slab, overhanging its feet (Z-11: the elves'
            // stone benches had wooden planks).
            if (g.S.StoneSeat)
                b.Box(new Vector3(0f, BenchSeat - 0.05f, 0f), new Vector3(1.9f, 0.1f, 0.44f),
                      Quaternion.Euler(0f, g.Range(-1f, 1f), 0f), g.Bark * g.Range(0.95f, 1.03f), FloraPart.Bark);
            else
            {
                b.Wood = true;
                for (int k = 0; k < 2; k++)
                    b.Box(new Vector3(0f, BenchSeat - 0.03f, (k - 0.5f) * 0.2f), new Vector3(1.8f, 0.06f, 0.18f),
                          Quaternion.Euler(0f, g.Range(-1f, 1f), 0f), g.Bark * g.Range(0.92f, 1.05f), FloraPart.Bark);
                b.Wood = false;
            }
            g.Colliders.Add(new Vector4(-0.5f, 0f, 0.3f, BenchSeat));
            g.Colliders.Add(new Vector4(0.5f, 0f, 0.3f, BenchSeat));
        }

        /// <summary>A log lying along X to sit on, 1.8 m long, its top 0.4 m up; a sitter faces +Z.</summary>
        static void SeatLog(Grower g)
        {
            var b = g.B;
            var r = LogSeat * 0.5f;
            g.Height = LogSeat;
            g.Width = 1.8f;
            g.TrunkRadius = 0f;
            var bend = g.Range(-0.04f, 0.04f);
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { new Vector3(-0.9f, r, 0f), new Vector3(0f, r, bend), new Vector3(0.9f, r, 0f) },
                   new List<float> { r * 0.95f, r, r * 0.92f }, g.Sides(9, 6, 4), g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, r * 0.8f));
            // Pale sawn ends.
            if (g.Lod < 2)
                for (int side = -1; side <= 1; side += 2)
                    b.Blob(new Vector3(side * 0.9f, r, 0f), new Vector3(0.02f, r * 0.9f, r * 0.9f), Quaternion.identity, 0, 0f, 0f,
                           Color.Lerp(g.Bark, new Color(0.75f, 0.62f, 0.45f).linear, 0.7f), FloraPart.Bark);
            g.Colliders.Add(new Vector4(-0.5f, 0f, 0.3f, LogSeat));
            g.Colliders.Add(new Vector4(0.5f, 0f, 0.3f, LogSeat));
        }

        /// <summary>
        /// W-63 river dressing: a weathered log lying along X, Height long and Thickness in radius, gently bent, with
        /// snapped branch stubs and a flare of root stubs at one end. Driftwood on a gravel bar, or (long) a log fallen
        /// across a narrows. Its middle rests on the origin.
        /// </summary>
        static void Driftwood(Grower g)
        {
            var b = g.B;
            var len = g.Range(g.S.Height.x, Mathf.Max(g.S.Height.x, g.S.Height.y));
            var r = Mathf.Max(0.06f, g.S.Thickness * g.Range(0.85f, 1.15f));
            g.Height = r * 2f;
            g.Width = len;
            g.TrunkRadius = 0f;
            var bend = g.Range(-0.08f, 0.08f) * len;
            var sides = g.Sides(9, 6, 4);
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { new Vector3(-len * 0.5f, r * 1.1f, 0f), new Vector3(0f, r, bend), new Vector3(len * 0.5f, r * 0.8f, bend * 0.3f) },
                   new List<float> { r * 1.15f, r, r * 0.6f }, sides, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, r * 0.8f));
            if (g.Lod >= 2)
                return;
            // Root stubs flare out of the thick end, some up in the air.
            for (int k = 0, n = 3 + (int)g.Range(0f, 3f); k < n; k++)
            {
                var a = (k + g.Range(0f, 0.6f)) * Mathf.PI * 2f / n;
                var tip = new Vector3(-len * 0.5f - g.Range(0.2f, 0.6f), r * 1.1f + Mathf.Sin(a) * r * 2.2f, Mathf.Cos(a) * r * 2.2f);
                b.Tube(new List<Vector3> { new Vector3(-len * 0.5f + 0.1f, r * 1.1f, 0f), tip }, new List<float> { r * 0.45f, r * 0.12f },
                       g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }
            // A few snapped branch stubs along the length.
            for (int k = 0, n = 1 + (int)g.Range(0f, 3.5f); k < n; k++)
            {
                var x = g.Range(-0.3f, 0.4f) * len;
                var a = g.Range(-1.2f, 1.2f);
                var from = new Vector3(x, r, Mathf.Lerp(0f, bend, 1f - Mathf.Abs(x) / (len * 0.5f)));
                var dir = new Vector3(g.Range(0.2f, 0.6f), Mathf.Cos(a), Mathf.Sin(a)).normalized;
                b.Tube(new List<Vector3> { from, from + dir * (r + g.Range(0.15f, 0.5f)) }, new List<float> { r * 0.3f, r * 0.15f },
                       g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }
        }

        /// <summary>A sawn stump to sit on, its pale top 0.42 m up.</summary>
        static void Stump(Grower g)
        {
            var b = g.B;
            var r = g.Range(0.22f, 0.3f);
            g.Height = StumpSeat;
            g.Width = r * 2.4f;
            g.TrunkRadius = 0f;
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.05f, Vector3.up * StumpSeat * 0.5f, Vector3.up * StumpSeat },
                   new List<float> { r * 1.25f, r * 1.02f, r }, g.Sides(9, 6, 4), g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, StumpSeat * 0.5f));
            b.Blob(Vector3.up * StumpSeat, new Vector3(r * 0.97f, 0.015f, r * 0.97f), Quaternion.identity, 1, 0f, 0f,
                   Color.Lerp(g.Bark, new Color(0.78f, 0.65f, 0.47f).linear, 0.75f), FloraPart.Bark);
            g.Colliders.Add(new Vector4(0f, 0f, r + 0.05f, StumpSeat));
        }

        /// <summary>
        /// A fire pit: stones in a ring round a sunken ash bed, three or four charred logs leaning together over the
        /// middle, and embers glowing in the ash (the flames are a landmark Flame effect drawn by FireSystem).
        /// </summary>
        static void FirePit(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var radius = g.Range(s.Height);
            var stones = Mathf.Max(6, g.Count(s.Branches));
            g.Height = radius * 0.8f;
            g.Width = radius * 2.4f;
            g.TrunkRadius = 0f;
            // The ash bed, a little below the stones' feet.
            b.Blob(Vector3.up * 0.01f, new Vector3(radius * 0.9f, 0.04f, radius * 0.9f), Quaternion.identity, 1, 0.2f, g.R() * 50f,
                   new Color(0.09f, 0.085f, 0.08f), FloraPart.Bark);
            // The ring: rough stones of varied size, each turned its own way.
            var turn = g.R() * 6.28f;
            for (int k = 0; k < stones; k++)
            {
                var a = turn + k * Mathf.PI * 2f / stones + g.Range(-0.12f, 0.12f);
                var size = radius * g.Range(0.2f, 0.3f);
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * size * 0.35f;
                var stone = b.Blob(at, new Vector3(size, size * g.Range(0.6f, 0.85f), size * g.Range(0.8f, 1.1f)),
                                   Quaternion.Euler(0f, -a * Mathf.Rad2Deg + g.Range(-20f, 20f), 0f), g.Lod == 0 ? 1 : 0, 0.35f, g.R() * 50f,
                                   g.Bark * g.Range(0.85f, 1.1f), FloraPart.Bark);
                b.Occlude(stone, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.5f)));
            }
            // Charred logs leaning together over the middle, blackening towards the burning ends.
            var logs = g.R() < 0.5f ? 3 : 4;
            var lean = g.R() * 6.28f;
            var top = Vector3.up * radius * 0.55f;
            for (int k = 0; k < logs; k++)
            {
                var a = lean + k * Mathf.PI * 2f / logs;
                var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.7f + Vector3.up * 0.03f;
                b.Tube(new List<Vector3> { foot, Vector3.Lerp(foot, top, 0.6f), top + (foot - top) * -0.08f },
                       new List<float> { radius * 0.09f, radius * 0.08f, radius * 0.05f }, g.Sides(6, 5, 4),
                       g.FoliageColor() * 0.6f, FloraPart.Bark);
            }
            // Embers in the ash and on the logs' charred ends.
            if (g.Lod < 2)
            {
                var ember = AccentAt(g, 0);
                for (int k = 0; k < 9; k++)
                {
                    var a = g.R() * 6.28f;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.05f, 0.55f) + Vector3.up * 0.04f;
                    b.Blob(at, Vector3.one * radius * g.Range(0.04f, 0.08f), Quaternion.identity, 0, 0.3f, g.R() * 50f,
                           ember * g.Range(0.7f, 1.2f), FloraPart.Glow);
                }
            }
            g.Colliders.Add(new Vector4(0f, 0f, radius * 1.1f, radius * 0.4f));
        }

        /// <summary>
        /// Bracket fungus (FloraSpecies.Brackets, a share of a tree's variants): half-moon shelves stepping up one side
        /// of the trunk in the first accent colour, glowing when the species' accents glow.
        /// </summary>
        static void Brackets(Grower g)
        {
            if (g.R() >= g.S.Brackets || g.Lod == 2)
                return;
            var b = g.B;
            var side = g.Flat(g.R() * 6.28f);
            var color = AccentAt(g, 0);
            var part = g.S.GlowingAccents ? FloraPart.Glow : FloraPart.Cactus;
            var shelves = 3 + (int)(g.R() * 4f);
            for (int k = 0; k < shelves; k++)
            {
                var t = Mathf.Lerp(0.12f, 0.55f, (k + g.R() * 0.6f) / shelves);
                var i = Mathf.Clamp(Mathf.FloorToInt(t * (g.TrunkPath.Count - 1)), 0, g.TrunkPath.Count - 2);
                var f = t * (g.TrunkPath.Count - 1) - i;
                var at = Vector3.Lerp(g.TrunkPath[i], g.TrunkPath[i + 1], f);
                var radius = Mathf.Lerp(g.TrunkRadii[i], g.TrunkRadii[i + 1], f);
                var dir = Quaternion.AngleAxis(g.Range(-50f, 50f), Vector3.up) * side;
                var width = radius * g.Range(0.7f, 1.2f);
                var centre = at + dir * radius * 0.9f;
                var shelf = Quaternion.LookRotation(dir);
                var first = b.VertexCount;
                b.Blob(centre, new Vector3(width, width * 0.16f, width * 0.75f), shelf, 1, 0.15f, g.R() * 50f, color * g.Range(0.85f, 1.1f), part);
                b.Occlude(first, (p, n) => Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(n.y + 0.5f)));
            }
        }

        /// <summary>
        /// Strands hanging from a share of a tree's limbs (FloraSpecies.Hanging): vines and lianas, moss beards or roots in
        /// the hanging colour, as ropes a few centimetres thick with leaf clumps along them. Most fall from a limb to well
        /// below the crown; some loop from one limb to the next. They sway slowly on the shared clock (Sway).
        /// </summary>
        static void Hanging(Grower g)
        {
            if (g.R() >= g.S.Hanging)
                return;
            var s = g.S;
            var points = g.Lod == 0 ? 9 : 5;
            foreach (var limb in g.Limbs)
            {
                var strands = 6 + (int)(g.R() * 5f);
                for (int k = 0; k < strands; k++)
                {
                    var top = AlongLimb(limb, g.Range(0.35f, 1f));
                    // Down past the underside of the crown (where the limb leaves the trunk), then the strand's own
                    // length, varying a lot strand to strand.
                    var length = g.Range(s.HangingLength.x, s.HangingLength.y) * g.Range(0.5f, 1.25f) + Mathf.Max(top.y - limb[0].y, 0f);
                    var drift = g.Flat(g.R() * 6.28f) * length * 0.12f;
                    var bow = g.Flat(g.R() * 6.28f) * g.Range(0.1f, 0.3f);
                    // A slow wander and a twist about the line of fall, so no strand hangs dead straight.
                    var side = g.Flat(g.R() * 6.28f);
                    var side2 = Vector3.Cross(Vector3.up, side);
                    float bends = g.Range(1.2f, 2.5f), phase = g.R() * 6.28f, wander = g.Range(0.08f, 0.18f);
                    var path = new List<Vector3>();
                    var radii = new List<float>();
                    for (int j = 0; j < points; j++)
                    {
                        var t = j / (points - 1f);
                        var twist = (side * Mathf.Sin(t * bends * Mathf.PI + phase) + side2 * Mathf.Cos(t * bends * 1.7f * Mathf.PI + phase) * 0.6f) * wander * t;
                        path.Add(top + drift * t * t + bow * Mathf.Sin(t * Mathf.PI) + twist + Vector3.down * length * t);
                        // Tapering to a fine tip.
                        radii.Add(Mathf.Lerp(0.05f, 0.008f, Mathf.Pow(t, 0.8f)));
                    }
                    var anchor = top.y;
                    Rope(g, path, radii, p => (anchor - p.y) * 0.08f);
                }
            }
            // Loops from limb to limb, sagging below the crown: a parabola, near enough a catenary at this sag.
            for (int l = 0; l < g.Limbs.Count && g.Limbs.Count > 1; l++)
            {
                if (g.R() > 0.6f)
                    continue;
                var la = g.Limbs[l];
                var lb = g.Limbs[(l + 1) % g.Limbs.Count];
                var a = AlongLimb(la, g.Range(0.5f, 1f));
                var c = AlongLimb(lb, g.Range(0.5f, 1f));
                var span = Vector3.Distance(a, c);
                if (span < 1f || span > 9f)
                    continue;
                var low = Mathf.Min(a.y, c.y);
                var sag = low - Mathf.Min(la[0].y, lb[0].y) + span * g.Range(0.15f, 0.35f) + 0.5f;
                var path = new List<Vector3>();
                var radii = new List<float>();
                var n = points + 2;
                for (int j = 0; j < n; j++)
                {
                    var t = j / (n - 1f);
                    path.Add(Vector3.Lerp(a, c, t) + Vector3.down * sag * 4f * t * (1f - t));
                    radii.Add(0.045f);
                }
                Rope(g, path, radii, p => (low - p.y) * 0.06f);
            }
        }

        /// <summary>A point a share of the way along a limb's path.</summary>
        static Vector3 AlongLimb(List<Vector3> limb, float t)
        {
            var i = Mathf.Min(Mathf.FloorToInt(t * (limb.Count - 1)), limb.Count - 2);
            return Vector3.Lerp(limb[i], limb[i + 1], t * (limb.Count - 1) - i);
        }

        /// <summary>When off, LOD1 keeps its wisp threads (for before/after; PUNCHY_WISPLOD=0).</summary>
        public static bool WispsLodZeroOnly = System.Environment.GetEnvironmentVariable("PUNCHY_WISPLOD") != "0";

        /// <summary>One vine (see Hanging): the rope, leaves in twos and threes every half metre or so (a metre at LOD1),
        /// flat and tilted every way off the rope so they read as leaves rather than beads, and the sway. Wispy species
        /// (FloraSpecies.HangingWisps) get a few fine threads fanning a little apart instead, with no leaves.</summary>
        static void Rope(Grower g, List<Vector3> path, List<float> radii, System.Func<Vector3, float> swing)
        {
            var b = g.B;
            var first = b.VertexCount;
            var leaf = g.S.HangingColor.linear * g.Range(0.85f, 1.15f);
            if (g.S.HangingWisps)
            {
                var threads = g.Lod == 0 ? 4 : 2;
                for (int k = 0; k < threads; k++)
                {
                    var fan = g.Flat(g.R() * 6.28f) * g.Range(0.04f, 0.14f);
                    var cut = g.Range(0.55f, 1f);
                    var thread = new List<Vector3>();
                    var fine = new List<float>();
                    for (int j = 0; j < path.Count; j++)
                    {
                        var t = j / (path.Count - 1f);
                        thread.Add(path[0] + (path[j] - path[0]) * cut + fan * t);
                        fine.Add(Mathf.Lerp(0.014f, 0.003f, t));
                    }
                    var shade = leaf * g.Range(0.8f, 1.2f);
                    // A thread is 3-14 mm thick: past LOD0 (55 m on for trees) it's well under a pixel, so LOD1 skips
                    // the tubes (W-117: they were most of a wispy tree's LOD1 triangles). The draws above still run,
                    // so whatever follows grows the same.
                    if (g.Lod == 0 || !WispsLodZeroOnly)
                        b.Tube(thread, fine, 3, shade, FloraPart.Foliage);
                }
                b.Sway(first, swing);
                return;
            }
            b.Tube(path, radii, g.Sides(5, 4, 3), leaf * 0.7f, FloraPart.Foliage);
            var step = g.Lod == 0 ? 0.5f : 1f;
            var walked = g.Range(0.2f, step);
            for (int j = 1; j < path.Count; j++)
            {
                var seg = Vector3.Distance(path[j - 1], path[j]);
                for (; walked < seg; walked += step * g.Range(0.7f, 1.3f))
                {
                    var at = Vector3.Lerp(path[j - 1], path[j], walked / seg);
                    var leaves = g.Lod == 0 ? 2 + (int)(g.R() * 2f) : 1;
                    for (int n = 0; n < leaves; n++)
                    {
                        var out_ = g.Flat(g.R() * 6.28f);
                        var size = g.Range(0.09f, 0.17f);
                        var turn = Quaternion.LookRotation(out_) * Quaternion.Euler(g.Range(20f, 70f), 0f, g.Range(-40f, 40f));
                        b.Blob(at + out_ * size * 0.8f, new Vector3(size * 0.6f, size * 0.12f, size), turn, 0, 0.2f, g.R() * 50f,
                               leaf * g.Range(0.8f, 1.2f), FloraPart.Foliage);
                    }
                }
                walked -= seg;
            }
            b.Sway(first, swing);
        }

        /// <summary>
        /// A few cocoons on threads under a share of a tree's limbs (FloraSpecies.Cocoons), pale and silky, that now and
        /// then twitch (Motion).
        /// </summary>
        static void Cocoons(Grower g)
        {
            if (g.R() >= g.S.Cocoons)
                return;
            var b = g.B;
            var count = 3 + (int)(g.R() * 4f);
            for (int k = 0; k < count; k++)
            {
                var limb = g.Limbs[(int)(g.R() * g.Limbs.Count) % g.Limbs.Count];
                var top = Vector3.Lerp(limb[1], limb[limb.Count - 1], g.Range(0.2f, 0.8f));
                // Down below the crown (to where the limb leaves the trunk) so they hang in plain view.
                var thread = g.Range(0.3f, 0.9f) + Mathf.Max(top.y - limb[0].y, 0f);
                var size = g.Range(0.2f, 0.32f);
                var first = b.VertexCount;
                b.Tube(new List<Vector3> { top, top + Vector3.down * thread }, new List<float> { 0.01f, 0.01f }, 3, new Color(0.8f, 0.78f, 0.72f), FloraPart.Bark);
                b.Blob(top + Vector3.down * (thread + size), new Vector3(size * 0.55f, size, size * 0.55f), Quaternion.identity, 1, 0.12f, g.R() * 50f,
                       new Color(0.82f, 0.8f, 0.74f) * g.Range(0.85f, 1.05f), FloraPart.Cactus);
                b.Motion(first, size * 0.25f, twitch: true);
            }
        }

        /// <summary>
        /// A face in the bark of a share of a tree's variants (FloraSpecies.BarkFace): two hollow eyes that glow faintly
        /// and blink (FloraPart.Eye), brows of bark, and a dark mouth below, on one side of the trunk at eye height.
        /// </summary>
        /// <summary>The offset (x, z) of a centreline from its foot at a height, between its points (held past its ends).</summary>
        static Vector2 AxisAt(List<Vector3> axis, float y)
        {
            var at = axis[axis.Count - 1];
            for (int i = 1; i < axis.Count; i++)
                if (axis[i].y >= y)
                {
                    at = Vector3.Lerp(axis[i - 1], axis[i], Mathf.InverseLerp(axis[i - 1].y, axis[i].y, y));
                    break;
                }
            return new Vector2(at.x - axis[0].x, at.z - axis[0].z);
        }

        static void BarkFace(Grower g)
        {
            if (g.R() >= g.S.BarkFace || g.Lod == 2)
                return;
            var b = g.B;
            var y = g.Height * g.S.TrunkFraction * 0.55f;
            var at = g.TrunkPath[0];
            var radius = g.TrunkRadii[0];
            for (int i = 1; i < g.TrunkPath.Count; i++)
                if (g.TrunkPath[i].y >= y)
                {
                    var t = Mathf.InverseLerp(g.TrunkPath[i - 1].y, g.TrunkPath[i].y, y);
                    at = Vector3.Lerp(g.TrunkPath[i - 1], g.TrunkPath[i], t);
                    radius = Mathf.Lerp(g.TrunkRadii[i - 1], g.TrunkRadii[i], t);
                    break;
                }
            // On the model's front (+Z), turned with the tree's yaw like everything else.
            var front = Vector3.forward;
            var side = Vector3.Cross(Vector3.up, front);
            var facing = Quaternion.LookRotation(front);
            var eye = radius * 0.26f;
            var hollow = new Color(0.02f, 0.015f, 0.01f);
            foreach (var s in new[] { -1f, 1f })
            {
                var e = at + front * radius * 0.9f + side * s * radius * 0.4f;
                // A deep hollow (near black, with a bark rim round it), a faint glowing eye far back in it, and a heavy brow.
                b.Blob(e, new Vector3(eye, eye * 0.85f, eye * 0.35f), facing, 1, 0f, 0f, hollow, FloraPart.Bark, 0.2f);
                Ring(b, e + front * eye * 0.2f, side, Vector3.up, eye * 1.05f, eye * 0.9f, radius * 0.07f, g.Bark * 0.75f);
                b.Blob(e + front * eye * 0.12f + Vector3.down * eye * 0.15f, new Vector3(eye * 0.35f, eye * 0.28f, eye * 0.2f), facing, 1, 0f, 0f,
                       new Color(0.55f, 0.95f, 0.75f), FloraPart.Eye);
                b.Blob(e + Vector3.up * eye * 1.15f + front * eye * 0.3f, new Vector3(eye * 1.4f, eye * 0.4f, eye * 0.55f), facing * Quaternion.Euler(0f, 0f, s * 14f),
                       0, 0.1f, g.R() * 50f, g.Bark * 0.8f, FloraPart.Bark);
            }
            // A ridge of a nose between them.
            b.Blob(at + front * radius * 0.98f + Vector3.down * radius * 0.2f, new Vector3(radius * 0.12f, radius * 0.32f, radius * 0.18f), facing,
                   0, 0.15f, g.R() * 50f, g.Bark * 0.85f, FloraPart.Bark);
            // The mouth: a dark hole ringed by a thick knot of bark lips.
            var mouth = at + front * radius * 0.9f + Vector3.down * radius * 0.7f;
            b.Blob(mouth, new Vector3(radius * 0.32f, radius * 0.2f, radius * 0.25f), facing, 1, 0.2f, g.R() * 50f, hollow, FloraPart.Bark, 0.2f);
            Ring(b, mouth + front * radius * 0.08f, side, Vector3.up, radius * 0.36f, radius * 0.24f, radius * 0.1f, g.Bark * 0.8f);
        }

        /// <summary>A closed ring of bark (eye rims, a mouth's lips): an ellipse of the given half-widths in the side/up plane.</summary>
        static void Ring(FloraMeshBuilder b, Vector3 centre, Vector3 side, Vector3 up, float width, float height, float thickness, Color color)
        {
            var path = new List<Vector3>();
            var radii = new List<float>();
            for (int k = 0; k <= 12; k++)
            {
                var a = k / 12f * Mathf.PI * 2f;
                path.Add(centre + side * Mathf.Cos(a) * width + up * Mathf.Sin(a) * height);
                radii.Add(thickness);
            }
            b.Tube(path, radii, 5, color, FloraPart.Bark);
        }

        /// <summary>Where a hive tree's comb hangs, as a share of its height (see FloraGenerator.Hive).</summary>
        public static float HiveHeight(FloraSpecies s) => (s.Height.x + s.Height.y) * 0.5f * s.TrunkFraction * 0.7f;

        /// <summary>
        /// A wild bees' nest hanging from the trunk (after Broadleaf), under the crown at HiveHeight: a drape of
        /// honey-amber comb in overlapping layers, widening then narrowing to a teardrop, darkest where it joins the bark,
        /// with a dark entrance slit low on its face and a drip of honey from its foot.
        /// </summary>
        static void Hive(Grower g)
        {
            if (g.TrunkPath == null)
                return;
            var b = g.B;
            var y = HiveHeight(g.S);
            // The trunk's centre and radius at that height.
            var at = g.TrunkPath[0];
            var radius = g.TrunkRadii[0];
            for (int i = 1; i < g.TrunkPath.Count; i++)
                if (g.TrunkPath[i].y >= y)
                {
                    var t = Mathf.InverseLerp(g.TrunkPath[i - 1].y, g.TrunkPath[i].y, y);
                    at = Vector3.Lerp(g.TrunkPath[i - 1], g.TrunkPath[i], t);
                    radius = Mathf.Lerp(g.TrunkRadii[i - 1], g.TrunkRadii[i], t);
                    break;
                }
            // Always on the tree's front (+Z): placement turns hive trees to face their landmark, so the comb's
            // side is known (the Hive anchor's Facing).
            var side = Vector3.forward;
            var size = Mathf.Clamp(radius * 1.8f, 0.35f, 0.7f);
            var wax = AccentAt(g, 0);
            var root = at + side * radius * 0.8f;
            var facing = Quaternion.LookRotation(side);
            const int layers = 6;
            var bottom = root;
            for (int k = 0; k < layers; k++)
            {
                // Layers hang one below another, each overlapping the last: wide in the middle, a teardrop's point below.
                var t = k / (float)(layers - 1);
                var width = size * Mathf.Sin(Mathf.Lerp(0.35f, 2.9f, t)) * 0.75f + size * 0.12f;
                var centre = root + side * (width * 0.55f) - Vector3.up * (t * size * 1.6f) + g.Flat(g.R() * 6.28f) * width * 0.06f;
                var shade = Mathf.Lerp(0.6f, 1.08f, Mathf.Sqrt(t)) * g.Range(0.92f, 1.06f);
                var first = b.Blob(centre, new Vector3(width * 0.62f, size * 0.24f, width * 0.5f), facing,
                                   g.Lod == 0 ? 1 : 0, 0.18f, g.R() * 50f, wax * shade, g.Lod < 2 ? FloraPart.Comb : FloraPart.Cactus);
                b.Occlude(first, (p, n) => Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(n.y * 0.4f + 0.65f)));
                bottom = centre;
            }
            if (g.Lod < 2)
            {
                // The entrance: a dark slit low on the face; and a drip of honey from the point.
                var mouth = root + side * size * 0.82f - Vector3.up * size * 1.0f;
                b.Blob(mouth, new Vector3(size * 0.22f, size * 0.06f, size * 0.1f), facing, 0, 0f, 0f,
                       new Color(0.02f, 0.015f, 0.01f), FloraPart.Bark);
                var drip = bottom - Vector3.up * size * 0.12f;
                b.Tube(new List<Vector3> { drip, drip - Vector3.up * size * 0.35f }, new List<float> { size * 0.05f, size * 0.025f },
                       g.Sides(5, 4, 3), wax * 1.15f, FloraPart.Comb);
                b.Blob(drip - Vector3.up * size * 0.37f, Vector3.one * size * 0.045f, Quaternion.identity, 0, 0f, 0f, wax * 1.2f, FloraPart.Comb);
            }
        }
    }
}
