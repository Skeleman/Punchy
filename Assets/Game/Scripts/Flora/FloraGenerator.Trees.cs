using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, trees and bushes: broadleaf, conifer, palm, shrub, dry bush. Code moved from FloraGenerator.cs (W-116); the class summary is there.
    public static partial class FloraGenerator
    {
        static void Broadleaf(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var width = height * s.Spread;
            var crownBase = height * s.TrunkFraction;
            var r0 = height * s.Thickness;
            g.Height = height;
            g.Width = width;
            g.TrunkRadius = r0;

            // Trunk: a gently leaning, wiggling column with a flared base.
            var lean = g.Flat(g.R() * Mathf.PI * 2f) * g.Range(0f, 0.07f) * height;
            var top = height * g.Range(0.74f, 0.86f);
            var trunk = new List<Vector3>();
            var trunkRadii = new List<float>();
            for (int i = 0; i <= 6; i++)
            {
                var t = i / 6f;
                var wiggle = g.Direction() * r0 * 0.6f;
                wiggle.y = 0f;
                trunk.Add(new Vector3(0f, Mathf.Lerp(-0.6f, top, t), 0f) + lean * (t * t) + (i > 0 ? wiggle : Vector3.zero));
                trunkRadii.Add(r0 * Mathf.Lerp(1f, 0.3f, t) * (i == 0 ? 1.45f : i == 1 ? 1.1f : 1f));
            }
            g.TrunkPath = trunk;
            g.TrunkAxis = trunk;
            g.TrunkRadii = trunkRadii;

            var crownCentre = new Vector3(lean.x * 0.6f, (crownBase + height) * 0.5f, lean.z * 0.6f);
            var crownRadii = new Vector3(width * 0.5f, (height - crownBase) * 0.5f, width * 0.5f);
            var fullness = Mathf.Lerp(0.5f, 1.1f, s.Fullness);   // sparse crowns: smaller clumps, more branch showing
            // Clumps never get much deeper than the crown, so wide, flat crowns (canopy trees) are built from many
            // small clumps rather than a few huge ones hanging down to the ground.
            var clumpCap = Mathf.Max((height - crownBase) * 0.6f, 0.3f);
            float Clump(float min, float max) => Mathf.Min(width * g.Range(min, max) * fullness, clumpCap);
            var spreadRatio = Mathf.Clamp(Mathf.Pow(width / (clumpCap * 2.5f), 2f), 1f, 10f);

            var limbs = new List<(List<Vector3> path, List<float> radii)>();
            var twigs = new List<(List<Vector3> path, List<float> radii)>();
            var clumps = new List<(Vector3 centre, float radius, Color color, float seed)>();

            var count = g.Count(s.Branches);
            for (int i = 0; i < count; i++)
            {
                var azimuth = (i + g.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                var startY = Mathf.Lerp(crownBase * 0.92f, top * 0.92f, g.Range(0f, 0.65f));
                var start = PointOnPath(trunk, startY);
                var elevation = g.Range(22f, 58f) * Mathf.Deg2Rad;
                var dir = g.Flat(azimuth) * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
                var length = ExitDistance(crownCentre, crownRadii, start, dir) * g.Range(0.7f, 0.88f);
                var path = new List<Vector3>();
                var radii = new List<float>();
                for (int k = 0; k <= 3; k++)
                {
                    var t = k / 3f;
                    path.Add(start + dir * length * t + Vector3.up * length * 0.15f * t * t + (k > 0 ? g.Direction() * length * 0.05f : Vector3.zero));
                    radii.Add(r0 * 0.55f * (1f - 0.75f * t));
                }
                limbs.Add((path, radii));
                (g.Limbs ??= new List<List<Vector3>>()).Add(path);
                clumps.Add((path[3], Clump(0.17f, 0.24f), g.FoliageColor(), g.R() * 100f));

                for (int k = 0; k < 2; k++)
                {
                    var from = Vector3.Lerp(path[1], path[2], g.R());
                    var twigDir = (dir + g.Direction() * 0.8f + Vector3.up * 0.3f).normalized;
                    var twigLength = length * g.Range(0.32f, 0.48f);
                    var end = from + twigDir * twigLength;
                    twigs.Add((new List<Vector3> { from, Vector3.Lerp(from, end, 0.5f) + Vector3.up * twigLength * 0.06f, end },
                               new List<float> { r0 * 0.2f, r0 * 0.12f, r0 * 0.05f }));
                    clumps.Add((end, Clump(0.13f, 0.19f), g.FoliageColor(), g.R() * 100f));
                }
            }
            clumps.Add((trunk[6] + Vector3.up * Mathf.Min(width * 0.05f, clumpCap * 0.3f), Clump(0.2f, 0.25f), g.FoliageColor(), g.R() * 100f));
            var fill = Mathf.Min(Mathf.RoundToInt((1f + s.Fullness * 4f) * spreadRatio), 40);
            var fillReach = spreadRatio > 1.5f ? 0.85f : 0.5f;
            for (int i = 0; i < fill; i++)
            {
                var u = g.Direction();
                // Wide crowns spread their fill out flat; giants (the great tree) keep some depth to layer in.

                if (spreadRatio > 1.5f)
                    u.y *= height < 20f ? 0.5f : 0.8f;
                var p = crownCentre + Vector3.Scale(u * g.Range(0.1f, fillReach), crownRadii);
                clumps.Add((p, Clump(0.17f, 0.24f), g.FoliageColor(), g.R() * 100f));
            }

            // Emit.
            var sides = g.Sides(8, 5, 4);
            var firstBark = b.VertexCount;
            if (g.Lod == 2)
            {
                b.Tube(trunk.GetRange(0, 5), trunkRadii.GetRange(0, 5), sides, g.Bark, FloraPart.Bark);
            }
            else
            {
                b.Tube(trunk, trunkRadii, sides, g.Bark, FloraPart.Bark, cap: true);
                foreach (var limb in limbs)
                    b.Tube(limb.path, limb.radii, sides - 2, g.Bark, FloraPart.Bark, cap: true);
                if (g.Lod == 0)
                    foreach (var twig in twigs)
                        b.Tube(twig.path, twig.radii, 3, g.Bark, FloraPart.Bark);
            }
            b.Occlude(firstBark, (p, n) => GroundOcclusion(p, height * 0.15f) * (p.y > crownBase ? 0.7f : 1f));

            var firstLeaf = b.VertexCount;
            // Fullness 0: a bare tree (dead or burnt), limbs and twigs only.
            if (s.Fullness <= 0.02f)
            {
            }
            else if (g.Lod == 2)
            {
                var avg = Color.Lerp(s.Foliage, s.FoliageVariation, 0.5f).linear;
                b.Blob(crownCentre, crownRadii * 1.02f, Quaternion.identity, 1, 0.22f, g.Variant * 3.7f, avg, FloraPart.Foliage);
            }
            else
            {
                foreach (var c in clumps)
                {
                    var radii = new Vector3(c.radius, c.radius * 0.78f, c.radius);
                    // Crowns made of many clumps (wide canopies) use simpler clumps; the silhouette carries them.
                    var many = clumps.Count > 20;

                    // ...but a giant is seen big from far off, so its clumps stay round.
                    var detail = (g.Lod == 0 ? (many ? 1 : 2) : (many ? 0 : 1)) + (height >= 20f ? 1 : 0);
                    var first = b.Blob(c.centre, radii, Quaternion.identity, detail, 0.22f, c.seed, c.color, FloraPart.Foliage);
                    b.BlendNormalsFrom(first, crownCentre, crownRadii, 0.55f);
                    if (g.Lod == 0)
                        b.Leaves(c.centre, radii, (int)(16 + 26 * s.Fullness), c.radius * 0.3f, g.Detail, c.color, c.color * 1.15f);
                }
            }
            b.Occlude(firstLeaf, (p, n) => CrownOcclusion(p, crownCentre, crownRadii));
        }

        static void Conifer(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var crownBase = height * s.TrunkFraction;
            var r0 = height * s.Thickness;
            var maxRadius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = maxRadius * 2f;
            g.TrunkRadius = r0;

            var lean = g.Flat(g.R() * Mathf.PI * 2f) * g.Range(0f, 0.03f) * height;
            Vector3 Axis(float y) => lean * Mathf.Clamp01(y / height);
            g.TrunkAxis = new List<Vector3> { Vector3.zero, Axis(height) + Vector3.up * height };

            var tierCount = Mathf.RoundToInt(Mathf.Lerp(5f, 11f, s.Fullness));
            var tiers = new List<(float y, float radius, float tierHeight, float twist, Color color, float[] jitter)>();
            for (int i = 0; i < tierCount; i++)
            {
                var f = i / (float)tierCount;
                var y = crownBase + (height * 0.9f - crownBase) * f;
                var radius = maxRadius * Mathf.Pow(1f - f, 0.85f) * g.Range(0.85f, 1.1f);
                var jitter = new float[24];
                for (int j = 0; j < jitter.Length; j++)
                    jitter[j] = g.R();
                tiers.Add((y, radius, (height - crownBase) / tierCount * g.Range(1.5f, 1.9f), g.R() * Mathf.PI * 2f, g.FoliageColor(), jitter));
            }

            var firstBark = b.VertexCount;
            b.Tube(new List<Vector3> { new Vector3(0f, -0.6f, 0f), Axis(height * 0.5f) + Vector3.up * height * 0.5f, Axis(height) + Vector3.up * height * 0.97f },
                   new List<float> { r0 * 1.35f, r0 * 0.6f, r0 * 0.12f }, g.Sides(7, 5, 4), g.Bark, FloraPart.Bark);
            b.Occlude(firstBark, (p, n) => GroundOcclusion(p, height * 0.1f) * (p.y > crownBase ? 0.6f : 1f));

            // Each tier is a drooping, jagged skirt of needles: a cone from near the trunk out to spiky tips, folding
            // back underneath to the trunk.
            var n = g.Sides(11, 7, 5);
            void Tier(Vector3 axis, float radius, float tierHeight, float twist, Color color, float[] jitter, float shade)
            {
                var droop = radius * 0.3f;
                var rings = new List<Vector3[]>();
                var colors = new List<Color[]>();
                var ao = new List<float[]>();
                var count = n * 2;
                var tip = color * 1.2f;
                for (int k = 0; k < 4; k++)
                {
                    var ring = new Vector3[count];
                    var ringColors = new Color[count];
                    var ringAo = new float[count];
                    for (int j = 0; j < count; j++)
                    {
                        var spike = j % 2 == 0;
                        var a = twist + j / (float)count * Mathf.PI * 2f;
                        var rand = jitter[j % jitter.Length];
                        float r, y, o;
                        Color c = color;
                        switch (k)
                        {
                            case 0: r = radius * 0.16f; y = tierHeight; o = 0.8f; break;
                            case 1: r = radius * (spike ? 0.62f : 0.55f); y = tierHeight * 0.35f - droop * 0.2f; o = 0.95f; break;
                            case 2:
                                r = radius * (spike ? 0.92f + 0.18f * rand : 0.7f);
                                y = -droop * (spike ? 1f : 0.45f) - rand * droop * 0.3f;
                                o = spike ? 1f : 0.8f;
                                if (spike) c = tip;
                                break;
                            default: r = radius * 0.12f; y = -droop * 0.15f + tierHeight * 0.1f; o = 0.3f; break;
                        }
                        ring[j] = axis + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                        ringColors[j] = c;
                        ringAo[j] = o * shade;
                    }
                    rings.Add(ring);
                    colors.Add(ringColors);
                    ao.Add(ringAo);
                }
                var first = b.Rings(rings, colors, ao, FloraPart.Foliage);
                b.BlendNormalsFrom(first, axis + Vector3.up * tierHeight * 0.2f, new Vector3(radius, tierHeight * 1.5f, radius), 0.5f);
            }

            if (g.Lod == 2)
            {
                // Three broad cones spanning the crown.
                for (int i = 0; i < 3; i++)
                {
                    var t = tiers[Mathf.Min(i * tierCount / 3, tierCount - 1)];
                    var tierHeight = (height - crownBase) * (i == 2 ? 0.55f : 0.45f);
                    Tier(Axis(t.y) + Vector3.up * t.y, t.radius, tierHeight, t.twist, t.color, t.jitter, Mathf.Lerp(0.75f, 1f, i / 2f));
                }
            }
            else
            {
                for (int i = 0; i < tiers.Count; i++)
                {
                    var t = tiers[i];
                    Tier(Axis(t.y) + Vector3.up * t.y, t.radius, t.tierHeight, t.twist, t.color, t.jitter, Mathf.Lerp(0.7f, 1f, i / (float)tierCount));
                }
            }
            // A pointed leader.
            var last = tiers[tiers.Count - 1];
            var leaderBase = Axis(last.y) + Vector3.up * last.y;
            var firstTop = b.VertexCount;
            b.Tube(new List<Vector3> { leaderBase, Axis(height) + Vector3.up * height },
                   new List<float> { Mathf.Max(last.radius * 0.45f, r0), 0.02f }, g.Sides(7, 5, 4), last.color, FloraPart.Foliage);
            b.Occlude(firstTop, (p, nn) => 0.9f);
        }

        static void Palm(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var r0 = height * s.Thickness;
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = r0;

            // A trunk that curves away and back up, with ring scars.
            var bendDir = g.Flat(g.R() * Mathf.PI * 2f);
            var bend = g.Range(0.08f, 0.25f) * height;
            Vector3 TrunkAt(float t) => bendDir * bend * Mathf.Sin(t * Mathf.PI * 0.8f) + Vector3.up * Mathf.Lerp(-0.5f, height * 0.94f, t);
            var top = TrunkAt(1f);

            var frondCount = g.Count(s.Branches);
            var fronds = new List<(Vector3 dir, float elevation, float length, Color color, float droop)>();
            for (int i = 0; i < frondCount; i++)
            {
                var azimuth = (i + g.Range(-0.25f, 0.25f)) / frondCount * Mathf.PI * 2f;
                var dead = i < 2 && g.R() < 0.6f;
                fronds.Add((g.Flat(azimuth), dead ? -50f : g.Range(5f, 50f), height * s.Spread * g.Range(0.4f, 0.55f),
                            dead ? Color.Lerp(s.Bark, s.FoliageVariation, 0.3f).linear : g.FoliageColor(), g.Range(0.45f, 0.7f)));
            }
            var nuts = new List<Vector3>();
            for (int i = 0; i < 4; i++)
                nuts.Add(top + g.Flat(g.R() * 6.28f) * r0 * 1.2f + Vector3.down * g.Range(0.1f, 0.35f));

            var path = new List<Vector3>();
            var radii = new List<float>();
            var rings = g.Lod == 0 ? 22 : g.Lod == 1 ? 8 : 4;
            for (int i = 0; i <= rings; i++)
            {
                var t = i / (float)rings;
                path.Add(TrunkAt(t));
                radii.Add(r0 * Mathf.Lerp(1.4f, 0.8f, Mathf.Sqrt(t)) * (g.Lod == 0 ? (i % 2 == 0 ? 1.06f : 0.95f) : 1f));
            }
            g.TrunkAxis = new List<Vector3>(path);
            var firstBark = b.VertexCount;
            b.Tube(path, radii, g.Sides(8, 6, 4), g.Bark, FloraPart.Bark);
            b.Blob(top, Vector3.one * r0 * 1.5f, Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.3f, g.Variant, (g.Bark * 0.8f), FloraPart.Bark);
            b.Occlude(firstBark, (p, n) => GroundOcclusion(p, 0.6f));
            if (g.Lod < 2 && g.HasAccent)
                foreach (var nut in nuts)
                    b.Blob(nut, Vector3.one * r0 * 0.6f, Quaternion.identity, 0, 0.1f, 0f, g.Accent, FloraPart.Bark, 0.6f);

            // Fronds: an arching spine with leaflets angled down either side (a serrated V).
            var segments = g.Sides(12, 6, 3);
            foreach (var f in fronds)
            {
                var e = f.elevation * Mathf.Deg2Rad;
                var start = f.dir * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e);
                var across = Vector3.Cross(Vector3.up, f.dir).normalized;
                Vector3 Spine(float t) => top + start * f.length * t + Vector3.down * f.length * f.droop * t * t;
                for (int k = 0; k < segments; k++)
                {
                    float t0 = k / (float)segments, t1 = (k + 1f) / segments;
                    Vector3 a = Spine(t0), c = Spine(t1);
                    var along = (c - a).normalized;
                    var w = f.length * 0.2f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(t0, t1, 0.5f)), 0.6f) + 0.02f;
                    var ao = Mathf.Lerp(0.6f, 1f, t1);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var leafTip = Vector3.Lerp(a, c, 0.8f) + across * side * w + Vector3.down * w * 0.45f + along * w * 0.4f;
                        var normal = (Vector3.up * 0.8f + across * side * 0.3f).normalized;
                        int v0 = b.Vertex(a, normal, f.color, FloraPart.Foliage, ao);
                        int v1 = b.Vertex(c, normal, f.color, FloraPart.Foliage, ao);
                        int v2 = b.Vertex(leafTip, normal, f.color * 1.1f, FloraPart.Foliage, ao);
                        b.Triangle(v0, v1, v2);
                    }
                }
            }
        }

        static void Shrub(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var width = height * s.Spread;
            g.Height = height;
            g.Width = width;
            g.TrunkRadius = Mathf.Min(width, height) * 0.3f;

            // A dome sitting on (and a little into) the ground. Full shrubs are a few big overlapping clumps; sparse
            // ones many small clumps with gaps, showing the stems that hold them up.
            var domeCentre = new Vector3(0f, height * 0.36f, 0f);
            var domeRadii = new Vector3(width * 0.5f, height * 0.64f, width * 0.5f);
            var clumpCount = Mathf.RoundToInt(Mathf.Lerp(11f, 6f, s.Fullness));
            var clumpSize = Mathf.Lerp(0.24f, 0.46f, s.Fullness);
            var clumps = new List<(Vector3 centre, float radius, Color color, float seed)>();
            for (int i = 0; i < clumpCount; i++)
            {
                var u = g.Direction();
                u.y = Mathf.Lerp(-0.15f, 1f, Mathf.Abs(u.y));
                var r = Mathf.Max(domeRadii.x, domeRadii.y) * clumpSize * g.Range(0.8f, 1.15f);
                var centre = domeCentre + Vector3.Scale(u.normalized * g.Range(0.35f, 0.62f), domeRadii);
                centre.y = Mathf.Max(centre.y, r * 0.55f);
                clumps.Add((centre, r, g.FoliageColor(), g.R() * 100f));
            }
            var stems = new List<(Vector3 from, int clump)>();
            var stemCount = g.Count(s.Branches);
            for (int i = 0; i < stemCount; i++)
                stems.Add((g.Flat(g.R() * 6.28f) * width * 0.06f + Vector3.down * 0.1f, i % clumpCount));

            if (g.Lod < 2 && s.Fullness < 0.6f || g.Lod == 0)
                foreach (var stem in stems)
                {
                    var to = clumps[stem.clump].centre;
                    var radius = Mathf.Max(height * s.Thickness, 0.008f);
                    b.Tube(new List<Vector3> { stem.from, Vector3.Lerp(stem.from, to, 0.5f) + Vector3.up * height * 0.05f, to },
                           new List<float> { radius, radius * 0.7f, radius * 0.4f }, 3, g.Bark, FloraPart.Bark, ao: 0.85f);
                }

            var firstLeaf = b.VertexCount;
            if (g.Lod == 2 && s.Fullness >= 0.6f)
            {
                var avg = Color.Lerp(s.Foliage, s.FoliageVariation, 0.5f).linear;
                b.Blob(domeCentre, domeRadii * 0.92f, Quaternion.identity, 1, 0.25f, g.Variant * 2.3f, avg, FloraPart.Foliage);
            }
            else
            {
                foreach (var c in clumps)
                {
                    var radii = new Vector3(c.radius, c.radius * 0.8f, c.radius);
                    var first = b.Blob(c.centre, radii, Quaternion.identity, g.Lod == 0 ? 2 : g.Lod == 1 ? 1 : 0, 0.25f, c.seed, c.color, FloraPart.Foliage);
                    b.BlendNormalsFrom(first, domeCentre, domeRadii, 0.5f);
                    if (g.Lod == 0)
                    {
                        b.Leaves(c.centre, radii, (int)(10 + 16 * s.Fullness), c.radius * 0.35f, g.Detail, c.color, c.color * 1.15f);
                        Sprigs(g, c.centre, radii, c.color, height * s.Thickness);
                        if (g.HasAccent)
                            for (int k = 0; k < Mathf.RoundToInt(s.AccentAmount * 10f); k++)
                            {
                                var u = FloraMeshBuilder.RandomDirection(g.Detail);
                                u.y = Mathf.Abs(u.y);
                                b.Diamond(c.centre + Vector3.Scale(u, radii) * 1.02f, Mathf.Max(height * 0.025f, 0.015f), g.Accent, FloraPart.Petal);
                            }
                    }
                }
            }
            // Flowers or berries also speckle the clumps themselves, so they still show from a distance.
            if (g.HasAccent)
            {
                var accent = g.Accent;
                var threshold = 1f - s.AccentAmount * 0.7f;
                var frequency = 6f / Mathf.Max(height, 0.3f);
                b.Recolor(firstLeaf, (p, c) =>
                {
                    var n = Unity.Mathematics.noise.snoise(new Unity.Mathematics.float3(p.x, p.y, p.z) * frequency) * 0.5f + 0.5f;
                    return Color.Lerp(c, accent, FloraMeshBuilder.Smooth(threshold - 0.05f, threshold + 0.05f, n) * 0.85f);
                });
            }
            b.Occlude(firstLeaf, (p, n) => CrownOcclusion(p, domeCentre, domeRadii) * GroundOcclusion(p, height * 0.25f));
        }

        /// <summary>
        /// W-121 (#21): leafy sprigs poking out past a clump's outline up close: a thin twig with pointed leaves in
        /// pairs along it and a paler, fresher leaf at its tip, so a shrub reads as twigs and leaves rather than a
        /// lumpy ball. Draws only from the detail stream, so the shrub's shape and placement do not move.
        /// </summary>
        static void Sprigs(Grower g, Vector3 centre, Vector3 radii, Color color, float twigRadius)
        {
            var b = g.B;
            var rng = g.Detail;
            float R() => (float)rng.NextDouble();
            var fresh = Color.Lerp(color, new Color(0.75f, 0.85f, 0.25f).linear, 0.3f) * 1.15f;
            for (int k = 0, n = 3 + rng.Next(3); k < n; k++)
            {
                var u = FloraMeshBuilder.RandomDirection(rng);
                u.y = Mathf.Lerp(-0.1f, 1f, Mathf.Abs(u.y));
                u.Normalize();
                var from = centre + Vector3.Scale(u, radii) * 0.65f;
                var tip = centre + Vector3.Scale(u, radii) * (1.2f + R() * 0.3f) + Vector3.up * radii.y * 0.1f;
                var dir = (tip - from).normalized;
                var radius = Mathf.Max(twigRadius * 0.4f, 0.004f);
                b.Tube(new List<Vector3> { from, tip }, new List<float> { radius, radius * 0.5f }, 3, g.Bark, FloraPart.Bark, ao: 0.8f);
                var side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 0.01f)
                    side = Vector3.right;
                side.Normalize();
                var leaf = radii.x * (0.28f + R() * 0.1f);
                for (int j = 0; j < 4; j++)
                {
                    var t = 0.4f + j * 0.18f;
                    var at = Vector3.Lerp(from, tip, t);
                    var flip = j % 2 == 0 ? 1f : -1f;
                    var sz = leaf * (j == 3 ? 1.1f : 0.8f + R() * 0.3f);
                    var go = j == 3 ? dir : (dir * 0.55f + side * flip + Vector3.up * 0.35f).normalized;
                    var across = Vector3.Cross(go, Vector3.up);
                    if (across.sqrMagnitude < 0.01f)
                        across = side;
                    across.Normalize();
                    var normal = Vector3.Cross(across, go).normalized;
                    if (normal.y < 0f)
                        normal = -normal;
                    var droop = Vector3.down * sz * 0.15f;
                    var tint = j == 3 ? fresh : Color.Lerp(color, fresh, R() * 0.4f) * (0.9f + R() * 0.15f);
                    b.Strip(new List<Vector3> { at, at + go * sz * 0.35f, at + go * sz * 0.75f + droop * 0.5f, at + go * sz + droop },
                            new List<float> { sz * 0.06f, sz * 0.36f, sz * 0.26f, 0f }, across, tint, FloraPart.Foliage, 0.85f, normal);
                }
            }
        }

        static void DryBush(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = height * 0.2f;

            var twigs = new List<(List<Vector3> path, float radius, int depth)>();
            void Twig(Vector3 start, Vector3 dir, float length, float radius, int depth)
            {
                var bendA = g.Direction() * length * 0.12f;
                var end = start + dir * length + g.Direction() * length * 0.1f;
                twigs.Add((new List<Vector3> { start, Vector3.Lerp(start, end, 0.5f) + bendA, end }, radius, depth));
                if (depth >= 2)
                    return;
                var children = 2 + (g.R() < 0.5f ? 1 : 0);
                for (int i = 0; i < children; i++)
                {
                    var from = Vector3.Lerp(start, end, g.Range(0.45f, 0.9f));
                    var childDir = (dir + g.Direction() * 0.9f + Vector3.up * 0.2f).normalized;
                    Twig(from, childDir, length * g.Range(0.45f, 0.65f), radius * 0.6f, depth + 1);
                }
            }
            var stems = g.Count(s.Branches);
            for (int i = 0; i < stems; i++)
            {
                var elevation = g.Range(25f, 75f) * Mathf.Deg2Rad;
                var dir = g.Flat(g.R() * 6.28f) * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
                Twig(Vector3.down * 0.05f, dir, height * g.Range(0.55f, 0.85f) / Mathf.Max(Mathf.Sin(elevation), 0.5f) * Mathf.Min(1f, s.Spread), height * s.Thickness, 0);
            }

            var maxDepth = g.Sides(2, 1, 0);
            var first = b.VertexCount;
            foreach (var t in twigs)
            {
                if (t.depth > maxDepth)
                    continue;
                var radius = Mathf.Max(t.radius, 0.004f) * (g.Lod == 2 ? 1.6f : 1f);
                b.Tube(t.path, new List<float> { radius, radius * 0.75f, radius * 0.45f }, 3, g.Bark, FloraPart.Bark);
                if (g.Lod == 0 && t.depth == 2 && s.Fullness > 0.05f)
                    b.Leaves(t.path[2], Vector3.one * height * 0.06f, Mathf.RoundToInt(5f * s.Fullness), height * 0.05f, g.Detail,
                             s.Foliage.linear, s.FoliageVariation.linear);
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.4f));
        }
    }
}
