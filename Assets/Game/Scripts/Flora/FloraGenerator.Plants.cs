using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, flowers, cacti and mushrooms, plus Standable/Founded. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        static void Flowers(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = 0f;

            var head = g.Accent;
            var centreColor = s.FlowerHead == FlowerHead.Cup ? new Color(0.08f, 0.06f, 0.04f).linear : new Color(1f, 0.78f, 0.15f).linear;
            // W-121 (#21): a yellow cup (buttercup) has a green-gold heart, not a poppy's dark one.
            if (s.FlowerHead == FlowerHead.Cup && head.r > 0.5f && head.g > 0.45f && head.b < 0.2f)
                centreColor = new Color(0.62f, 0.66f, 0.12f).linear;
            // W-121 (#21): close-up detail (stem leaves, calyx, stamens or a domed disc, buds) on the nearest LOD. It
            // draws only from the detail stream, so the flowers' layout does not move.
            var rich = g.Lod == 0 && s.FlowerHead != FlowerHead.Spike;
            var dr = g.Detail;
            float D() => (float)dr.NextDouble();
            var stemColor = s.Foliage.linear;
            var stemRadius = Mathf.Clamp(height * 0.018f, 0.003f, 0.012f);

            var count = g.Count(s.Branches);
            for (int f = 0; f < count; f++)
            {
                var offset = f == 0 ? Vector3.zero : g.Flat(g.R() * 6.28f) * height * s.Spread * g.Range(0.15f, 0.5f);
                var h = height * g.Range(0.6f, 1f);
                var lean = g.Flat(g.R() * 6.28f) * g.Range(0.05f, 0.25f);
                var top = offset + lean * h + Vector3.up * h;
                var facing = (Vector3.up + lean * 2.5f).normalized;
                var size = height * (s.FlowerHead == FlowerHead.Spike ? 0.06f : s.FlowerHead == FlowerHead.Cup ? 0.13f : 0.16f) * g.Range(0.8f, 1.2f);
                var petalCount = s.FlowerHead == FlowerHead.Cup ? 5 + (g.R() < 0.5f ? 1 : 0) : 9 + (int)(g.R() * 5f);
                var twist = g.R() * 6.28f;
                var tint = Color.Lerp(head, head * g.Range(0.8f, 1.2f), 0.5f);

                // Stem.
                var stem = g.Lod == 0
                    ? new List<Vector3> { offset + Vector3.down * 0.02f, offset + lean * h * 0.35f + Vector3.up * h * 0.55f, top }
                    : new List<Vector3> { offset + Vector3.down * 0.02f, top };
                var stemRadii = g.Lod == 0 ? new List<float> { stemRadius, stemRadius * 0.85f, stemRadius * 0.7f } : new List<float> { stemRadius * 1.3f, stemRadius };
                b.Tube(stem, stemRadii, 3, stemColor, FloraPart.Foliage, ao: 0.8f);

                // Head, in a frame facing up-ish.
                var right = Vector3.Cross(facing, Vector3.forward).normalized;
                var forward = Vector3.Cross(right, facing);

                if (rich)
                {
                    // W-121 (#21): one or two narrow leaves partway up the stem, and a green calyx cupping the head.
                    for (int k = 0, n = 1 + (D() < 0.5f ? 1 : 0); k < n; k++)
                    {
                        var at = Vector3.Lerp(stem[0], top, 0.25f + k * 0.22f + D() * 0.1f);
                        var go = (g.Flat(D() * 6.28f) + Vector3.up * (0.6f + D() * 0.5f)).normalized;
                        var len = h * (0.22f + D() * 0.1f);
                        var across = Vector3.Cross(Vector3.up, go).normalized;
                        var sag = Vector3.down * len * 0.25f;
                        b.Strip(new List<Vector3> { at, at + go * len * 0.4f, at + go * len * 0.8f + sag * 0.5f, at + go * len + sag },
                                new List<float> { len * 0.04f, len * 0.2f, len * 0.14f, 0f }, across, stemColor * (0.95f + D() * 0.15f),
                                FloraPart.Foliage, 0.8f, Vector3.Cross(across, go).normalized);
                    }
                    b.Blob(top - facing * size * 0.06f, new Vector3(size * 0.17f, size * 0.12f, size * 0.17f), Quaternion.FromToRotation(Vector3.up, facing),
                           0, 0.25f, D() * 50f, stemColor * 0.85f, FloraPart.Foliage, 0.8f);
                }
                Vector3 Head(float angle, float radius, float up) =>
                    top + (right * Mathf.Cos(angle + twist) + forward * Mathf.Sin(angle + twist)) * radius + facing * up;

                switch (s.FlowerHead)
                {
                    case FlowerHead.Daisy:
                    {
                        var centre = b.Vertex(top + facing * size * 0.12f, facing, centreColor, FloraPart.Petal, 1f);
                        for (int p = 0; p < petalCount; p++)
                        {
                            var a = p / (float)petalCount * Mathf.PI * 2f;
                            var w = Mathf.PI / petalCount * 0.8f;
                            var normal = facing;
                            if (g.Lod == 0)
                            {
                                int v0 = b.Vertex(Head(a - w * 0.5f, size * 0.22f, 0.03f * size), normal, tint, FloraPart.Petal, 0.9f);
                                int v1 = b.Vertex(Head(a + w * 0.5f, size * 0.22f, 0.03f * size), normal, tint, FloraPart.Petal, 0.9f);
                                int v2 = b.Vertex(Head(a - w * 0.25f, size, 0.12f * size), normal, tint, FloraPart.Petal, 1f);
                                int v3 = b.Vertex(Head(a + w * 0.25f, size, 0.12f * size), normal, tint, FloraPart.Petal, 1f);
                                b.Triangle(v0, v2, v1);
                                b.Triangle(v1, v2, v3);
                                int c0 = b.Vertex(Head(a, size * 0.22f, 0.08f * size), facing, centreColor, FloraPart.Petal, 1f);
                                int c1 = b.Vertex(Head(a + Mathf.PI * 2f / petalCount, size * 0.22f, 0.08f * size), facing, centreColor, FloraPart.Petal, 1f);
                                b.Triangle(centre, c0, c1);
                                if (rich)
                                {
                                    // W-121 (#21): an inner ring of shorter petals between the outer ones, a little raised.
                                    var a2 = a + Mathf.PI / petalCount;
                                    var inner = tint * 0.93f;
                                    int i0 = b.Vertex(Head(a2 - w * 0.4f, size * 0.2f, 0.08f * size), normal, inner, FloraPart.Petal, 0.85f);
                                    int i1 = b.Vertex(Head(a2 + w * 0.4f, size * 0.2f, 0.08f * size), normal, inner, FloraPart.Petal, 0.85f);
                                    int i2 = b.Vertex(Head(a2, size * 0.72f, 0.24f * size), normal, inner, FloraPart.Petal, 1f);
                                    b.Triangle(i0, i2, i1);
                                }
                            }
                            else if (p % 2 == 0)
                            {
                                int v0 = b.Vertex(Head(a - w, size * 0.1f, 0f), normal, tint, FloraPart.Petal, 1f);
                                int v1 = b.Vertex(Head(a + w, size * 0.1f, 0f), normal, tint, FloraPart.Petal, 1f);
                                int v2 = b.Vertex(Head(a, size, 0.1f * size), normal, tint, FloraPart.Petal, 1f);
                                b.Triangle(v0, v2, v1);
                            }
                        }
                        break;
                    }
                    case FlowerHead.Cup:
                    {
                        if (rich)
                        {
                            // W-121 (#21): a ring of stamens standing round a small knob in the cup's heart.
                            for (int k = 0, n = 6 + (int)(D() * 3f); k < n; k++)
                                b.Diamond(Head(k * Mathf.PI * 2f / n, size * 0.14f, size * 0.32f), size * 0.045f, Color.Lerp(centreColor, head, 0.25f) * 1.3f, FloraPart.Petal);
                            b.Blob(top + facing * size * 0.12f, new Vector3(size * 0.11f, size * 0.14f, size * 0.11f), Quaternion.FromToRotation(Vector3.up, facing),
                                   0, 0.2f, D() * 50f, centreColor, FloraPart.Petal);
                        }
                        for (int p = 0; p < petalCount; p++)
                        {
                            var a = p / (float)petalCount * Mathf.PI * 2f;
                            var w = Mathf.PI * 2f / petalCount * 0.75f;
                            var outward = (Head(a, 1f, 0f) - top).normalized;
                            var normal = (outward + facing).normalized;
                            if (g.Lod == 0)
                            {
                                int v0 = b.Vertex(Head(a, size * 0.08f, 0f), normal, centreColor, FloraPart.Petal, 0.6f);
                                int v1 = b.Vertex(Head(a - w * 0.5f, size * 0.45f, size * 0.4f), normal, tint, FloraPart.Petal, 0.9f);
                                int v2 = b.Vertex(Head(a + w * 0.5f, size * 0.45f, size * 0.4f), normal, tint, FloraPart.Petal, 0.9f);
                                int v3 = b.Vertex(Head(a - w * 0.35f, size * 0.62f, size * 0.95f), normal, tint * 1.1f, FloraPart.Petal, 1f);
                                int v4 = b.Vertex(Head(a + w * 0.35f, size * 0.62f, size * 0.95f), normal, tint * 1.1f, FloraPart.Petal, 1f);
                                b.Triangle(v0, v1, v2);
                                b.Triangle(v1, v3, v2);
                                b.Triangle(v2, v3, v4);
                            }
                            else
                            {
                                int v0 = b.Vertex(Head(a, size * 0.05f, 0f), normal, tint, FloraPart.Petal, 0.8f);
                                int v1 = b.Vertex(Head(a - w * 0.5f, size * 0.6f, size * 0.8f), normal, tint, FloraPart.Petal, 1f);
                                int v2 = b.Vertex(Head(a + w * 0.5f, size * 0.6f, size * 0.8f), normal, tint, FloraPart.Petal, 1f);
                                b.Triangle(v0, v1, v2);
                            }
                        }
                        break;
                    }
                    default:
                    {
                        // Florets climbing the top of the stem, smaller towards the tip.
                        var florets = g.Lod == 0 ? 10 : 4;
                        for (int k = 0; k < florets; k++)
                        {
                            var t = k / (float)(florets - 1);
                            var p = Vector3.Lerp(offset + lean * h * 0.6f + Vector3.up * h * 0.62f, top + facing * size * 1.5f, t);
                            var r = size * Mathf.Lerp(1f, 0.45f, t) * (g.Lod == 0 ? 1f : 1.5f);
                            b.Diamond(p + Head(k * 2.4f, r * 0.6f, 0f) - top, r, Color.Lerp(tint, tint * 1.3f, t), FloraPart.Petal);
                        }
                        break;
                    }
                }
                if (rich && s.FlowerHead == FlowerHead.Daisy)   // W-121 (#21): a domed, bumpy disc in the middle
                    b.Blob(top + facing * size * 0.13f, new Vector3(size * 0.23f, size * 0.12f, size * 0.23f), Quaternion.FromToRotation(Vector3.up, facing),
                           1, 0.35f, D() * 50f, centreColor, FloraPart.Petal);
            }

            // W-121 (#21): now and then an unopened bud on its own short, nodding stem.
            if (rich)
                for (int k = 0, n = D() < 0.6f ? 1 + (D() < 0.3f ? 1 : 0) : 0; k < n; k++)
                {
                    var foot = g.Flat(D() * 6.28f) * height * s.Spread * (0.1f + D() * 0.3f);
                    var bh = height * (0.45f + D() * 0.3f);
                    var nod = g.Flat(D() * 6.28f) * bh * 0.18f;
                    var bud = foot + Vector3.up * bh + nod;
                    b.Tube(new List<Vector3> { foot + Vector3.down * 0.02f, foot + Vector3.up * bh * 0.6f + nod * 0.3f, bud },
                           new List<float> { stemRadius, stemRadius * 0.85f, stemRadius * 0.7f }, 3, stemColor, FloraPart.Foliage, ao: 0.8f);
                    var bs = height * 0.07f;
                    var up = (Vector3.up + nod.normalized * 0.6f).normalized;
                    b.Blob(bud + up * bs * 0.7f, new Vector3(bs * 0.55f, bs, bs * 0.55f), Quaternion.FromToRotation(Vector3.up, up), 0, 0.2f, D() * 50f,
                           Color.Lerp(stemColor, head, 0.35f), FloraPart.Petal);
                }

            // Leaves at the base.
            var leafCount = 2 + (int)(g.R() * 3f);
            for (int i = 0; i < leafCount; i++)
            {
                var dir = g.Flat(g.R() * 6.28f);
                var length = height * g.Range(0.3f, 0.5f);
                var across = Vector3.Cross(Vector3.up, dir);
                var rise = g.Range(0.3f, 0.7f);
                if (g.Lod == 0)
                {
                    var spine = new List<Vector3>();
                    var widths = new List<float>();
                    for (int k = 0; k <= 3; k++)
                    {
                        var t = k / 3f;
                        spine.Add(dir * length * t + Vector3.up * length * rise * Mathf.Sin(t * Mathf.PI * 0.7f));
                        widths.Add(length * 0.16f * Mathf.Sin(Mathf.Lerp(0.15f, 1f, t) * Mathf.PI));
                    }
                    b.Strip(spine, widths, across, stemColor, FloraPart.Foliage, 0.7f, Vector3.up);
                }
                else
                {
                    int v0 = b.Vertex(-across * length * 0.08f, Vector3.up, stemColor, FloraPart.Foliage, 0.7f);
                    int v1 = b.Vertex(across * length * 0.08f, Vector3.up, stemColor, FloraPart.Foliage, 0.7f);
                    int v2 = b.Vertex(dir * length + Vector3.up * length * rise * 0.5f, Vector3.up, stemColor, FloraPart.Foliage, 0.9f);
                    b.Triangle(v0, v2, v1);
                }
            }
        }

        static void Saguaro(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var r = Mathf.Max(height * s.Thickness, 0.16f);
            g.Height = height;
            g.Width = r * 2f;
            g.TrunkRadius = r;
            var green = g.FoliageColor();

            var lean = g.Flat(g.R() * 6.28f) * g.Range(0f, 0.03f) * height;
            var trunk = new List<Vector3>();
            var radii = new List<float>();
            for (int i = 0; i <= 7; i++)
            {
                var t = i / 7f;
                trunk.Add(Vector3.up * Mathf.Lerp(-0.5f, height - r * 0.9f, t) + lean * t);
                radii.Add(r * (0.88f + 0.12f * FloraMeshBuilder.Smooth(0f, 0.25f, t)) * (1f + 0.04f * Mathf.Sin(t * 9f + g.Variant)));
            }

            var armCount = g.Count(s.Branches);
            if (height < 3.5f) armCount = 0;
            else if (height < 5f) armCount = Mathf.Min(armCount, 1);
            var arms = new List<(List<Vector3> path, float radius)>();
            for (int i = 0; i < armCount; i++)
            {
                var dir = g.Flat((i + g.Range(-0.2f, 0.2f)) / Mathf.Max(armCount, 1) * 6.28f + g.Variant);
                var y0 = height * g.Range(0.3f, 0.55f);
                var outward = r * g.Range(1.6f, 2.8f);
                var rise = Mathf.Min(height * g.Range(0.18f, 0.4f), height * 0.92f - y0);
                var p0 = PointOnPath(trunk, y0);
                var elbow = p0 + dir * outward + Vector3.up * r * 1.1f;
                var path = new List<Vector3>();
                for (int k = 0; k <= 4; k++)
                    path.Add(Bezier(p0, p0 + dir * outward * 1.05f, elbow, k / 4f));
                path.Add(elbow + Vector3.up * rise * 0.5f + dir * r * 0.15f);
                path.Add(elbow + Vector3.up * rise + dir * r * 0.25f);
                arms.Add((path, r * g.Range(0.65f, 0.8f)));
            }
            var blooms = g.HasAccent && g.Variant % 2 == 0;
            var bloomSpots = new List<Vector3>();
            for (int i = 0; i < 6; i++)
                bloomSpots.Add(g.Flat(g.R() * 6.28f) * g.Range(0.2f, 0.6f));

            var sides = g.Sides(24, 12, 6);
            var ribs = g.Sides(12, 6, 0);
            var first = b.VertexCount;
            b.Tube(trunk, radii, sides, green, FloraPart.Cactus, cap: true, ribs: ribs, ribDepth: 0.1f);
            foreach (var arm in arms)
            {
                var armRadii = new List<float>();
                for (int k = 0; k < arm.path.Count; k++)
                    armRadii.Add(arm.radius * (k == 0 ? 0.9f : 1f));
                b.Tube(arm.path, armRadii, g.Sides(16, 8, 5), green, FloraPart.Cactus, cap: true, ribs: g.Sides(8, 4, 0), ribDepth: 0.1f);
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.8f) * Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(n.y + 0.5f)));
            if (blooms && g.Lod == 0)
            {
                var crown = trunk[trunk.Count - 1] + Vector3.up * r * 0.7f;
                foreach (var spot in bloomSpots)
                    b.Diamond(crown + spot * r, r * 0.22f, g.Accent, FloraPart.Petal);
            }
        }

        static void BarrelCactus(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2f;
            g.TrunkRadius = radius;
            var green = g.FoliageColor();
            var tilt = Quaternion.AngleAxis(g.Range(0f, 14f), g.Flat(g.R() * 6.28f));
            var flowers = new List<Vector3>();
            for (int i = 0; i < 9; i++)
                flowers.Add(g.Flat(i / 9f * 6.28f + g.Range(-0.2f, 0.2f)) * radius * g.Range(0.25f, 0.45f));

            var path = new List<Vector3>();
            var radii = new List<float>();
            for (int i = 0; i <= 5; i++)
            {
                var t = i / 5f;
                path.Add(tilt * (Vector3.up * Mathf.Lerp(-0.1f, height - radius * 0.75f, t)));
                radii.Add(radius * (0.84f + 0.16f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.2f))));
            }
            var first = b.VertexCount;
            b.Tube(path, radii, g.Sides(36, 16, 8), green, FloraPart.Cactus, cap: true, ribs: g.Sides(18, 8, 0), ribDepth: 0.16f);
            b.Occlude(first, (p, n) => GroundOcclusion(p, radius));

            if (g.HasAccent)
            {
                var crown = path[path.Count - 1] + tilt * Vector3.up * radius * 0.7f;
                if (g.Lod == 0)
                {
                    foreach (var f in flowers)
                        b.Diamond(crown + tilt * f, radius * 0.12f, g.Accent, FloraPart.Petal);
                }
                else
                {
                    var accent = g.Accent;
                    var crownY = crown.y - radius * 0.35f;
                    b.Recolor(first, (p, c) => p.y > crownY ? Color.Lerp(c, accent, 0.7f) : c);
                }
            }
        }

        static void PricklyPear(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = height * 0.25f;

            var pads = new List<(Vector3 centre, Vector3 radii, Quaternion rotation, Color color, float seed, int level)>();
            var fruit = new List<Vector3>();
            void Pad(Vector3 attach, Vector3 axis, float padHeight, int level)
            {
                var facing = Vector3.Cross(axis, g.Flat(g.R() * 6.28f)).normalized;
                var rotation = Quaternion.LookRotation(facing, axis);
                var radii = new Vector3(padHeight * 0.36f, padHeight * 0.5f, padHeight * 0.075f);
                var centre = attach + axis * padHeight * 0.46f;
                pads.Add((centre, radii, rotation, g.FoliageColor(), g.R() * 100f, level));
                var children = g.R() < s.Fullness ? 2 : 1;
                for (int i = 0; i < children; i++)
                {
                    var rim = centre + rotation * new Vector3(g.Range(-0.5f, 0.5f) * radii.x, radii.y * 0.85f, 0f);
                    var childAxis = Quaternion.AngleAxis(g.Range(-30f, 30f), Vector3.up) * (Quaternion.AngleAxis(g.Range(-45f, 45f), facing) * axis);
                    childAxis.y = Mathf.Max(childAxis.y, 0.3f);
                    if (level < 2)
                        Pad(rim, childAxis.normalized, padHeight * g.Range(0.8f, 0.95f), level + 1);
                    else if (g.R() < s.AccentAmount)
                        fruit.Add(rim);
                }
            }
            var baseCount = g.Count(s.Branches);
            for (int i = 0; i < baseCount; i++)
            {
                var axis = (Vector3.up + g.Flat(g.R() * 6.28f) * g.Range(0.1f, 0.6f)).normalized;
                Pad(g.Flat(g.R() * 6.28f) * height * 0.15f + Vector3.down * 0.05f, axis, height * 0.4f, 0);
            }

            var first = b.VertexCount;
            foreach (var p in pads)
                if (g.Lod < 2 || p.level < 2)
                    b.Blob(p.centre, p.radii, p.rotation, g.Lod == 0 ? 1 : 0, 0.08f, p.seed, p.color, FloraPart.Cactus);
            // Pale specks where the spines grow.
            var specks = s.Bark.linear;
            b.Recolor(first, (p, c) =>
            {
                var n = Unity.Mathematics.noise.snoise(new Unity.Mathematics.float3(p.x, p.y, p.z) * 28f);
                return n > 0.55f ? Color.Lerp(c, specks, 0.5f) : c;
            });
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.4f));
            if (g.HasAccent && g.Lod < 2)
                foreach (var f in fruit)
                    b.Blob(f + Vector3.up * height * 0.03f, new Vector3(0.6f, 1f, 0.6f) * height * 0.045f, Quaternion.identity, 0, 0.05f, 0f, g.Accent, FloraPart.Cactus);
        }

        static void Ocotillo(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = height * 0.04f;

            var canes = new List<List<Vector3>>();
            var count = g.Count(s.Branches);
            for (int i = 0; i < count; i++)
            {
                var tilt = g.Range(4f, 28f) * Mathf.Deg2Rad;
                var outward = g.Flat(g.R() * 6.28f);
                var length = height * g.Range(0.7f, 1f);
                var basePoint = outward * height * 0.02f + Vector3.down * 0.1f;
                var path = new List<Vector3>();
                for (int k = 0; k <= 4; k++)
                {
                    var t = k / 4f;
                    var bend = Mathf.Sin(tilt) * (1f + 0.4f * t);
                    path.Add(basePoint + (outward * bend + Vector3.up * Mathf.Cos(tilt)).normalized * length * t + g.Direction() * length * 0.012f * k);
                }
                canes.Add(path);
            }

            var r = height * s.Thickness;
            var first = b.VertexCount;
            for (int i = 0; i < canes.Count; i++)
            {
                if (g.Lod == 2 && i % 2 == 1)
                    continue;
                b.Tube(canes[i], new List<float> { r, r * 0.85f, r * 0.7f, r * 0.5f, r * 0.3f }, g.Sides(4, 3, 3), g.Bark, FloraPart.Bark,
                       tipColor: g.HasAccent ? g.Accent : (Color?)null);
                if (g.Lod == 0 && s.Fullness > 0.05f)
                    for (int k = 1; k < 4; k++)
                        b.Leaves(canes[i][k], Vector3.one * r * 1.2f, Mathf.RoundToInt(6f * s.Fullness), height * 0.025f, g.Detail,
                                 s.Foliage.linear, s.FoliageVariation.linear);
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.15f));
        }

        static void Mushroom(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = 0f;

            // Puffballs (a very short stem share): round balls on a stub. Giants (over 2 m): domed or bell caps on
            // stems that flare into roots and bend, the smaller ones gathered close round the biggest.
            var puff = s.TrunkFraction < 0.12f;
            var count = g.Count(s.Branches);
            for (int i = 0; i < count; i++)
            {
                var giant = height > 2f;
                var offset = i == 0 ? Vector3.zero : g.Flat(g.R() * 6.28f) * height * s.Spread * g.Range(giant ? 0.18f : 0.25f, giant ? 0.45f : 0.7f);
                var h = height * (i == 0 ? 1f : g.Range(giant ? 0.25f : 0.4f, giant ? 0.65f : 0.85f));
                var lean = g.Flat(g.R() * 6.28f) * g.Range(0f, giant ? 0.18f : 0.25f) + (i > 0 ? offset.normalized * 0.15f : Vector3.zero);
                var capRadius = h * s.Spread * g.Range(0.35f, 0.55f);
                var capHeight = capRadius * (giant ? g.Range(0.55f, 0.95f) : g.Range(0.4f, 0.65f));
                if (puff)
                {
                    capRadius = h * g.Range(0.42f, 0.55f);
                    capHeight = capRadius * g.Range(0.8f, 0.95f);
                }
                var stemRadius = Mathf.Max(h * s.Thickness, 0.004f);
                var color = g.FoliageColor();
                var seed = g.R() * 100f;
                var top = offset + lean * h + Vector3.up * h;
                var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + lean).normalized);

                // A giant mushroom's main stem stops walkers like a trunk.
                if (i == 0 && h > 2f)
                    g.TrunkRadius = stemRadius * 1.25f;
                var firstStem = b.VertexCount;
                if (puff)
                {
                    top = offset + Vector3.up * capRadius * 0.6f;
                    b.Tube(new List<Vector3> { offset + Vector3.down * 0.02f, top }, new List<float> { capRadius * 0.45f, capRadius * 0.4f },
                           g.Sides(7, 5, 3), g.Bark, FloraPart.Bark);
                }
                else if (giant)
                {
                    // Flared into the ground, then bending up into the cap.
                    var bend = Vector3.Cross(Vector3.up, lean.sqrMagnitude > 1e-4f ? lean.normalized : Vector3.right) * g.Range(-0.12f, 0.12f) * h;
                    b.Tube(new List<Vector3> { offset + Vector3.down * 0.3f, offset + Vector3.up * h * 0.06f,
                                               offset + lean * h * 0.3f + bend * 0.6f + Vector3.up * h * 0.4f,
                                               offset + lean * h * 0.7f + bend + Vector3.up * h * 0.78f, top },
                           new List<float> { stemRadius * 2.4f, stemRadius * 1.45f, stemRadius, stemRadius * 0.92f, stemRadius * 0.85f },
                           g.Sides(9, 6, 4), g.Bark, FloraPart.Bark);
                    // A few roots spreading from the foot.
                    if (g.Lod < 2)
                        for (int k = 0; k < 4; k++)
                        {
                            var dir = g.Flat(g.R() * 6.28f);
                            var foot = offset + Vector3.up * h * 0.04f;
                            b.Tube(new List<Vector3> { foot, foot + dir * stemRadius * 2.6f + Vector3.down * 0.15f, foot + dir * stemRadius * 4.2f + Vector3.down * 0.35f },
                                   new List<float> { stemRadius * 0.55f, stemRadius * 0.35f, stemRadius * 0.12f }, g.Sides(5, 4, 3), g.Bark * 0.85f, FloraPart.Bark);
                        }
                }
                else
                    b.Tube(new List<Vector3> { offset + Vector3.down * 0.02f, offset + lean * h * 0.45f + Vector3.up * h * 0.55f, top },
                           new List<float> { stemRadius * 1.25f, stemRadius, stemRadius * 0.9f }, g.Sides(7, 5, 3), g.Bark, FloraPart.Bark);
                b.Occlude(firstStem, (p, n) => GroundOcclusion(p, h * 0.3f) * 0.9f);

                // The cap, and darker gills underneath it. A glowing giant's cap is plain (a natural colour between its
                // glow and its stem's), its light only in the veins of its gills (G-31/F-46: not mostly glowing).
                var glowingGiant = giant && s.Finish == FoliageFinish.Glowing;
                // ...and leans brown, from dark umber to tawny, keeping a hint of its kind's hue, so the forest's crowns read as
                // one earthy canopy rather than pastel green, violet and blue (Z-38).
                // (Drawn only for them, so other mushrooms' random draws and shapes stay as they were.)
                var capColor = color;
                if (glowingGiant)
                {
                    var brown = Color.Lerp(new Color(0.34f, 0.22f, 0.13f), new Color(0.56f, 0.42f, 0.27f), g.R()).linear;
                    capColor = Color.Lerp(Color.Lerp(color, g.Bark, 0.6f) * 0.75f, brown, 0.6f);
                }
                var cap = b.Blob(top + tilt * Vector3.up * capHeight * 0.25f, new Vector3(capRadius, capHeight, capRadius), tilt,
                                 g.Lod == 0 ? 2 : g.Lod == 1 ? 1 : 0, 0.06f, seed, capColor, glowingGiant ? FloraPart.Bark : FloraPart.Foliage);
                // Giants: a near-flat underside (the cap's lower half squashed in its own frame), so the gills under it
                // hide from a side view.
                if (giant)
                {
                    var capCentre = top + tilt * Vector3.up * capHeight * 0.25f;
                    var untilt = Quaternion.Inverse(tilt);
                    b.Transform(cap, q =>
                    {
                        var l = untilt * (q - capCentre);
                        if (l.y < 0f) l.y *= 0.3f;
                        return capCentre + tilt * l;
                    }, nrm =>
                    {
                        var l = untilt * nrm;
                        if (l.y < 0f) l = new Vector3(l.x, l.y / 0.3f, l.z).normalized;
                        return tilt * l;
                    });
                }
                // Giants: the cap's underside much darker (shade under the overhang).
                // Swells by up to 6.5% of its radius and back every 4 s.
                if (s.Breathes)
                    b.Motion(cap, capRadius * 0.065f);
                System.Func<Vector3, Vector3, float> capShade = (p, n) => giant && n.y < -0.15f ? 0.32f : Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y + 0.3f));
                b.Occlude(cap, capShade);
                if (g.Lod < 2 && !puff && !giant)
                    b.Blob(top - tilt * Vector3.up * capHeight * 0.05f, new Vector3(capRadius * 0.92f, capHeight * 0.18f, capRadius * 0.92f), tilt,
                           0, 0f, seed, color * 0.45f, FloraPart.Foliage, 0.45f);
                // Giants: gills, thin dark lines radiating from the stem under the cap, just below its lower curve, so
                // they show from beneath and never from the side; a glowing giant's glow in its colour (fewer a step
                // further off, so the light still shows there).
                if (giant && (g.Lod == 0 || glowingGiant && g.Lod == 1))
                {
                    var centre = top + tilt * Vector3.up * capHeight * 0.25f;
                    var gill = glowingGiant ? color : Color.Lerp(color, g.Bark, 0.4f) * 0.45f;
                    var gillPart = glowingGiant ? FloraPart.Glow : FloraPart.Bark;
                    var gillCount = g.Lod == 0 ? 30 : 14;
                    Vector3 Under(Vector3 dir, float r) =>
                        centre + tilt * (dir * r - Vector3.up * (capHeight * 0.3f * Mathf.Sqrt(Mathf.Max(1f - r * r / (capRadius * capRadius), 0f)) + 0.01f));
                    for (int k = 0; k < gillCount; k++)
                    {
                        var dir = Quaternion.AngleAxis(k * 360f / gillCount + g.Range(-3f, 3f), Vector3.up) * Vector3.forward;
                        var inner = Under(dir, stemRadius * 1.3f);
                        var mid = Under(dir, capRadius * 0.55f);
                        var outer = Under(dir, capRadius * 0.88f);
                        var shade = gill * g.Range(0.8f, 1.2f);
                        b.Beam(inner, mid, capRadius * 0.05f, capRadius * 0.006f, shade, gillPart);
                        b.Beam(mid, outer, capRadius * 0.045f, capRadius * 0.006f, shade, gillPart);
                    }
                }
                // Spots on the cap.
                if (g.HasAccent && g.Lod == 0)
                    for (int k = 0; k < Mathf.RoundToInt(s.AccentAmount * 12f); k++)
                    {
                        var u = FloraMeshBuilder.RandomDirection(g.Detail);
                        u.y = Mathf.Abs(u.y) * 0.8f + 0.2f;
                        var spot = top + tilt * (Vector3.up * capHeight * 0.25f + Vector3.Scale(u.normalized, new Vector3(capRadius, capHeight, capRadius)) * 1.01f);
                        b.Diamond(spot, capRadius * 0.12f, g.Accent, FloraPart.Petal);
                    }
            }
        }

        /// <summary>Forms people can climb onto and stand on (their tops go into PropSupport).</summary>
        public static bool Standable(FloraForm form) => Founded(form) || form switch
        {
            FloraForm.Boulder or FloraForm.Spire or FloraForm.Den or FloraForm.Log or FloraForm.Driftwood or FloraForm.Woodpile or FloraForm.IceArch => true,
            // Not fire pits: nobody stands in the flames (their ring collider keeps people out).
            _ => false
        };

        /// <summary>How far built things' foundations reach below the ground they stand on (m, before scaling).</summary>
        public const float FoundationDepth = 1.6f;

        /// <summary>
        /// Forms standing on the ground on walls, plinths, posts or feet: their bases carry on FoundationDepth into the
        /// ground (FloraMeshBuilder.Skirt), so they never float over a slope. Not lying things (logs, woodpiles) or
        /// bridges (their piles reach the water bed).
        /// </summary>
        public static bool Founded(FloraForm form) => form switch
        {
            FloraForm.Hut or FloraForm.StoneHouse or FloraForm.Tent or FloraForm.Tower or FloraForm.Stall or FloraForm.Inn or
            FloraForm.Smithy or FloraForm.Forum or FloraForm.Fountain or FloraForm.Well or FloraForm.Hall or FloraForm.Shrine or
            FloraForm.Columns or FloraForm.Lodge or FloraForm.Palisade or FloraForm.Pen or FloraForm.Garden or
            FloraForm.Signpost or FloraForm.Bench or FloraForm.Stump or FloraForm.Ruin or FloraForm.RailFence or FloraForm.HazardShelter or FloraForm.KitBuilding or FloraForm.KitRuin => true,
            _ => false
        };
    }
}
