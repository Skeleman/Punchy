using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>One grown variant of a species, at three levels of detail.</summary>
    public class FloraModel
    {
        public FloraSpecies Species;
        public readonly Mesh[] Lods = new Mesh[3];
        /// <summary>Bounding sphere centre height and radius (unscaled).</summary>
        public float CentreY, Radius;
        /// <summary>Radius of the trunk / stem at the ground, for collisions.</summary>
        public float TrunkRadius;
        public float Height;
        /// <summary>
        /// Collision shape for plants that aren't a single trunk (rocks): upright cylinders (x, z offset from the
        /// plant's base, radius, height of the top), before the plant's own rotation and scale. Empty = the trunk.
        /// </summary>
        public readonly List<Vector4> Colliders = new List<Vector4>();

        public void Destroy()
        {
            foreach (var m in Lods)
                if (m != null)
                    Object.Destroy(m);
        }
    }

    /// <summary>
    /// Grows plants. Each variant is decided by a seeded random sequence (its skeleton: trunk, limbs, clumps, arms,
    /// pads, petals), then emitted at three levels of detail. Every level replays the same sequence, so they share
    /// one silhouette and only differ in how finely it is built.
    /// </summary>
    public static class FloraGenerator
    {
        public static FloraModel Grow(FloraSpecies species, int variant)
        {
            var model = new FloraModel { Species = species };
            var builder = new FloraMeshBuilder();
            var seed = (int)Hash((uint)species.Seed, (uint)variant, StableHash(species.name));
            for (int lod = 0; lod < 3; lod++)
            {
                builder.Clear();
                builder.FoliageAs = species.Finish == FoliageFinish.Metallic ? FloraPart.Metal
                                  : species.Finish == FoliageFinish.Glowing ? FloraPart.Glow : FloraPart.Foliage;
                builder.PetalAs = species.GlowingAccents ? FloraPart.Glow : FloraPart.Petal;
                var g = new Grower(species, variant, lod, seed, builder);
                switch (species.Form)
                {
                    case FloraForm.BroadleafTree: Broadleaf(g); break;
                    case FloraForm.Conifer: Conifer(g); break;
                    case FloraForm.Palm: Palm(g); break;
                    case FloraForm.Shrub: Shrub(g); break;
                    case FloraForm.DryBush: DryBush(g); break;
                    case FloraForm.Flowers: Flowers(g); break;
                    case FloraForm.Saguaro: Saguaro(g); break;
                    case FloraForm.BarrelCactus: BarrelCactus(g); break;
                    case FloraForm.PricklyPear: PricklyPear(g); break;
                    case FloraForm.Ocotillo: Ocotillo(g); break;
                    case FloraForm.Mushroom: Mushroom(g); break;
                    case FloraForm.Bones: Bones(g); break;
                    case FloraForm.Boulder: Boulder(g); break;
                    case FloraForm.Spire: Spire(g); break;
                    case FloraForm.Crystal: Crystal(g); break;
                    case FloraForm.Den: Den(g); break;
                    case FloraForm.Columns: Columns(g); break;
                    case FloraForm.Hut: Hut(g); break;
                    case FloraForm.StoneHouse: StoneHouse(g); break;
                    case FloraForm.Tent: Tent(g); break;
                    case FloraForm.Tower: Tower(g); break;
                    case FloraForm.Pen: Pen(g); break;
                    case FloraForm.Stall: Stall(g); break;
                    case FloraForm.Inn: Inn(g); break;
                    case FloraForm.Smithy: Smithy(g); break;
                    case FloraForm.Forum: Forum(g); break;
                    case FloraForm.Fountain: Fountain(g); break;
                    case FloraForm.Woodpile: Woodpile(g); break;
                    case FloraForm.Well: Well(g); break;
                    case FloraForm.LilyPad: LilyPad(g); break;
                    case FloraForm.Reeds: Reeds(g); break;
                }
                if (lod == 0)
                {
                    builder.Bounds(out model.CentreY, out model.Radius);
                    model.TrunkRadius = g.TrunkRadius;
                    model.Colliders.AddRange(g.Colliders);
                    model.Height = g.Height;
                }
                model.Lods[lod] = builder.ToMesh($"{species.name} {variant} LOD{lod}", g.Height, g.Width, species.Flexibility);
            }
            return model;
        }

        static uint StableHash(string text)
        {
            var h = 2166136261u;
            foreach (var ch in text)
                h = (h ^ ch) * 16777619u;
            return h;
        }

        static uint Hash(uint a, uint b, uint c)
        {
            var h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ c * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return h;
        }

        /// <summary>Random sequences and shared settings for growing one variant at one level of detail.</summary>
        class Grower
        {
            public readonly FloraSpecies S;
            public readonly int Variant, Lod;
            public readonly FloraMeshBuilder B;
            /// <summary>Shape decisions: consumed identically at every level of detail.</summary>
            readonly System.Random rng;
            /// <summary>Fine detail that only some levels draw (loose leaves, florets).</summary>
            public readonly System.Random Detail;
            public float Height, Width, TrunkRadius;
            public readonly List<Vector4> Colliders = new List<Vector4>();

            /// <summary>
            /// Adds upright cylinders covering a rotated ellipsoid's footprint: a row of circles along its longer
            /// horizontal axis, each as wide as the shorter one, as tall as its top.
            /// </summary>
            public void AddCollider(Vector3 centre, Vector3 radii, Quaternion rotation)
            {
                var top = centre.y + radii.y;
                if (top < 0.35f)
                    return;   // low enough to step over
                var ax = rotation * new Vector3(radii.x, 0f, 0f);
                var az = rotation * new Vector3(0f, 0f, radii.z);
                var a = new Vector2(ax.x, ax.z);
                var c = new Vector2(az.x, az.z);
                var major = a.magnitude >= c.magnitude ? a : c;
                var minor = Mathf.Max(Mathf.Min(a.magnitude, c.magnitude), 0.1f);
                var count = Mathf.Clamp(Mathf.CeilToInt(major.magnitude / minor), 1, 4);
                for (int k = 0; k < count; k++)
                {
                    var t = count == 1 ? 0f : Mathf.Lerp(-1f, 1f, k / (count - 1f)) * (1f - minor / Mathf.Max(major.magnitude, minor));
                    var at = new Vector2(centre.x, centre.z) + major * t;
                    Colliders.Add(new Vector4(at.x, at.y, minor * 0.95f, top));
                }
            }

            public Grower(FloraSpecies s, int variant, int lod, int seed, FloraMeshBuilder b)
            {
                S = s;
                Variant = variant;
                Lod = lod;
                B = b;
                rng = new System.Random(seed);
                Detail = new System.Random(seed ^ 0x5BD1E995);
            }

            public float R() => (float)rng.NextDouble();
            public float Range(float a, float b) => a + (b - a) * R();
            public float Range(Vector2 v) => Range(v.x, v.y);
            public int Count(Vector2Int v) => rng.Next(Mathf.Min(v.x, v.y), Mathf.Max(v.x, v.y) + 1);
            public Vector3 Direction() => FloraMeshBuilder.RandomDirection(rng);
            public Vector3 Flat(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            public Color Bark => S.Bark.linear;
            public Color FoliageColor() => Color.Lerp(S.Foliage, S.FoliageVariation, R()).linear;
            public bool HasAccent => S.Accents.Count > 0 && S.AccentAmount > 0f;
            public Color Accent => S.Accents.Count > 0 ? S.Accents[Variant % S.Accents.Count].linear : Color.white;
            public int Sides(int lod0, int lod1, int lod2) => Lod == 0 ? lod0 : Lod == 1 ? lod1 : lod2;
        }

        // ------------------------------------------------------------------------------------------- helpers

        /// <summary>Darker deep inside and underneath a crown, lit on top and at the edges.</summary>
        static float CrownOcclusion(Vector3 p, Vector3 centre, Vector3 radii)
        {
            var rel = new Vector3((p.x - centre.x) / radii.x, (p.y - centre.y) / radii.y, (p.z - centre.z) / radii.z);
            return Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(rel.magnitude * 0.75f + rel.y * 0.3f + 0.15f));
        }

        /// <summary>Darker where the plant meets the ground.</summary>
        static float GroundOcclusion(Vector3 p, float height) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p.y / Mathf.Max(height, 0.05f)));

        /// <summary>Distance from a point inside an ellipsoid to its surface along a direction.</summary>
        static float ExitDistance(Vector3 centre, Vector3 radii, Vector3 from, Vector3 dir)
        {
            for (float d = 0.1f; d < 200f; d += 0.1f)
            {
                var p = from + dir * d - centre;
                if (p.x * p.x / (radii.x * radii.x) + p.y * p.y / (radii.y * radii.y) + p.z * p.z / (radii.z * radii.z) > 1f)
                    return d;
            }
            return 200f;
        }

        static Vector3 PointOnPath(List<Vector3> path, float y)
        {
            for (int i = 0; i < path.Count - 1; i++)
                if (y <= path[i + 1].y)
                    return Vector3.Lerp(path[i], path[i + 1], Mathf.InverseLerp(path[i].y, path[i + 1].y, y));
            return path[path.Count - 1];
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        // ------------------------------------------------------------------------------------------- forms

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
                if (spreadRatio > 1.5f)
                    u.y *= 0.5f;
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
            if (g.Lod == 2)
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
                    var detail = g.Lod == 0 ? (many ? 1 : 2) : (many ? 0 : 1);
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

            var count = g.Count(s.Branches);
            for (int i = 0; i < count; i++)
            {
                var offset = i == 0 ? Vector3.zero : g.Flat(g.R() * 6.28f) * height * s.Spread * g.Range(0.25f, 0.7f);
                var h = height * (i == 0 ? 1f : g.Range(0.4f, 0.85f));
                var lean = g.Flat(g.R() * 6.28f) * g.Range(0f, 0.25f);
                var capRadius = h * s.Spread * g.Range(0.35f, 0.55f);
                var capHeight = capRadius * g.Range(0.4f, 0.65f);
                var stemRadius = Mathf.Max(h * s.Thickness, 0.004f);
                var color = g.FoliageColor();
                var seed = g.R() * 100f;
                var top = offset + lean * h + Vector3.up * h;
                var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + lean).normalized);

                var firstStem = b.VertexCount;
                b.Tube(new List<Vector3> { offset + Vector3.down * 0.02f, offset + lean * h * 0.45f + Vector3.up * h * 0.55f, top },
                       new List<float> { stemRadius * 1.25f, stemRadius, stemRadius * 0.9f }, g.Sides(7, 5, 3), g.Bark, FloraPart.Bark);
                b.Occlude(firstStem, (p, n) => GroundOcclusion(p, h * 0.3f) * 0.9f);

                // The cap, and darker gills underneath it.
                var cap = b.Blob(top + tilt * Vector3.up * capHeight * 0.25f, new Vector3(capRadius, capHeight, capRadius), tilt,
                                 g.Lod == 0 ? 2 : g.Lod == 1 ? 1 : 0, 0.06f, seed, color, FloraPart.Foliage);
                b.Occlude(cap, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y + 0.3f)));
                if (g.Lod < 2)
                    b.Blob(top - tilt * Vector3.up * capHeight * 0.05f, new Vector3(capRadius * 0.92f, capHeight * 0.18f, capRadius * 0.92f), tilt,
                           0, 0f, seed, color * 0.45f, FloraPart.Foliage, 0.45f);
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

        static void Bones(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var size = g.Range(s.Height);   // overall length of the skeleton
            g.Height = size * 0.45f;
            g.Width = size;
            g.TrunkRadius = 0f;

            var bone = s.Bark.linear;
            var aged = Color.Lerp(bone, bone * 0.75f, g.R());
            var heading = g.Flat(g.R() * 6.28f);
            var across = Vector3.Cross(Vector3.up, heading).normalized * (g.R() < 0.5f ? 1f : -1f);
            var r = size * s.Thickness;
            var sides = g.Sides(6, 4, 3);

            // The spine lies along the ground, half buried, with a gentle sideways bend.
            var bend = g.Range(-0.12f, 0.12f) * size;
            Vector3 Spine(float t) => heading * (t - 0.5f) * size + across * bend * Mathf.Sin(t * Mathf.PI) + Vector3.down * r * 0.4f;
            var spine = new List<Vector3>();
            var spineRadii = new List<float>();
            for (int i = 0; i <= 6; i++)
            {
                var t = i / 6f;
                spine.Add(Spine(t));
                spineRadii.Add(r * Mathf.Lerp(1f, 0.45f, t));
            }
            var first = b.VertexCount;
            b.Tube(spine, spineRadii, sides, aged, FloraPart.Bark, cap: true);
            if (g.Lod == 0)
                for (int i = 1; i < 14; i++)
                    b.Diamond(Spine(i / 14f) + Vector3.up * r * 0.8f, r * 1.4f * Mathf.Lerp(1f, 0.5f, i / 14f), aged, FloraPart.Bark);

            // Ribs arch up and over from the spine to the ground beside it (the animal lies on its side). Incomplete
            // skeletons are missing some, and some are broken off part-way.
            var ribCount = g.Count(s.Branches);
            for (int k = 0; k < ribCount; k++)
            {
                var t = Mathf.Lerp(0.22f, 0.62f, ribCount > 1 ? k / (ribCount - 1f) : 0.5f);
                var present = g.R() < 0.45f + 0.55f * s.Fullness;
                var reach = g.R() < s.Fullness ? 1f : g.Range(0.45f, 0.85f);
                var sizeAlong = Mathf.Sin(Mathf.Lerp(0.25f, 0.95f, (t - 0.22f) / 0.4f) * Mathf.PI);
                var ribWidth = size * 0.28f * (0.6f + 0.4f * sizeAlong);
                var ribHeight = size * 0.3f * (0.6f + 0.4f * sizeAlong);
                if (!present)
                    continue;
                var path = new List<Vector3>();
                var radii = new List<float>();
                var steps = g.Lod == 0 ? 8 : 4;
                var root = Spine(t);
                for (int j = 0; j <= steps; j++)
                {
                    var a = j / (float)steps * Mathf.PI * reach;
                    path.Add(root + across * ribWidth * (1f - Mathf.Cos(a)) * 0.5f + Vector3.up * ribHeight * Mathf.Sin(a) - heading * size * 0.03f * Mathf.Sin(a));
                    radii.Add(r * 0.55f * Mathf.Lerp(1f, 0.6f, j / (float)steps));
                }
                b.Tube(path, radii, Mathf.Max(3, sides - 2), aged, FloraPart.Bark, cap: true);
            }

            // Skull at the front, with dark eye sockets.
            var look = Quaternion.LookRotation((heading + Vector3.down * 0.2f).normalized, Vector3.up);
            var skull = Spine(0f) - heading * size * 0.06f + Vector3.up * r * 1.5f;
            var skullRadii = new Vector3(size * 0.06f, size * 0.05f, size * 0.1f);
            b.Blob(skull, skullRadii, look, g.Lod == 0 ? 1 : 0, 0.12f, g.Variant * 5.1f, aged, FloraPart.Bark);
            if (g.Lod < 2)
                for (int side = -1; side <= 1; side += 2)
                    b.Blob(skull + look * new Vector3(side * skullRadii.x * 0.75f, skullRadii.y * 0.35f, -skullRadii.z * 0.2f),
                           Vector3.one * skullRadii.x * 0.32f, look, 0, 0f, 0f, new Color(0.05f, 0.04f, 0.035f), FloraPart.Bark, 0.4f);

            // A few long bones scattered nearby.
            var scattered = 2 + (int)(g.R() * 3f);
            for (int i = 0; i < scattered; i++)
            {
                var at = g.Flat(g.R() * 6.28f) * size * g.Range(0.35f, 0.7f);
                var dir = g.Flat(g.R() * 6.28f);
                var length = size * g.Range(0.12f, 0.22f);
                if (g.Lod == 2)
                    continue;
                var a = at - dir * length * 0.5f + Vector3.up * r * 0.2f;
                var c = at + dir * length * 0.5f + Vector3.up * r * 0.2f;
                b.Tube(new List<Vector3> { a, c }, new List<float> { r * 0.45f, r * 0.45f }, 4, aged, FloraPart.Bark);
                b.Diamond(a, r * 0.9f, aged, FloraPart.Bark);
                b.Diamond(c, r * 0.9f, aged, FloraPart.Bark);
            }
            b.Occlude(first, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(p.y / Mathf.Max(size * 0.15f, 0.05f))) * Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(n.y + 0.5f)));
        }

        // ------------------------------------------------------------------------------------------- rocks

        /// <summary>Moss or lichen on upward-facing rock, in noisy patches (the species' accent).</summary>
        static void Moss(Grower g, int first)
        {
            if (!g.HasAccent)
                return;
            var moss = g.Accent;
            var amount = g.S.AccentAmount;
            var seed = g.Variant * 7.3f;
            g.B.Recolor(first, (p, n, c) =>
            {
                var patch = Mathf.PerlinNoise(p.x * 1.3f + seed, p.z * 1.3f + p.y * 0.7f) - 0.5f;
                var cover = FloraMeshBuilder.Smooth(0.75f - amount * 0.6f, 0.95f - amount * 0.6f, n.y + patch * 0.6f);
                return Color.Lerp(c, moss, cover);
            });
        }

        /// <summary>
        /// Rocks in five families, one per variant in turn: a rounded lump, a flat slab (sometimes two stacked), an
        /// L of a long low block and an upright one, a tall standing stone, and a monstrous split mass with chunks
        /// fallen off it. Built from rounded blocks (boxy, broadly lumpy blobs), with a few loose stones round the foot.
        /// </summary>
        static void Boulder(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var size = g.Range(s.Height);
            var family = g.Variant % 5;
            var detail = g.Sides(3, 2, 1);
            var first = b.VertexCount;
            var yaw = g.R() * 360f;

            void Block(Vector3 centre, Vector3 radii, Quaternion rotation, float sink = 0.3f)
            {
                var at = centre + Vector3.up * radii.y * (1f - sink);
                var colour = Color.Lerp(g.Bark, g.Bark * 0.8f, g.R());
                b.Blob(at, radii, rotation, detail, 0.14f, g.R() * 100f, colour, FloraPart.Bark, 1f, g.Range(0.35f, 0.6f), 0.55f);
                g.AddCollider(at, radii * 0.92f, rotation);
            }
            Quaternion Turn(float tilt = 8f) => Quaternion.Euler(g.Range(-tilt, tilt), yaw + g.Range(-20f, 20f), g.Range(-tilt, tilt));

            var w = size * s.Spread;
            switch (family)
            {
                case 0:   // lump
                    Block(Vector3.zero, new Vector3(w, size * 0.55f, w * g.Range(0.6f, 0.95f)), Turn());
                    break;
                case 1:   // slab, sometimes with a second resting on it
                    Block(Vector3.zero, new Vector3(w * 1.3f, size * 0.22f, w * g.Range(0.7f, 1f)), Turn(4f), 0.4f);
                    if (g.R() < 0.5f)
                        Block(new Vector3(g.Range(-0.3f, 0.3f) * w, size * 0.25f, g.Range(-0.3f, 0.3f) * w),
                              new Vector3(w * 0.75f, size * 0.17f, w * 0.55f), Turn(6f), 0.2f);
                    break;
                case 2:   // L: a long low block with an upright one at its end
                {
                    var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    Block(-along * w * 0.35f, new Vector3(w * 1.1f, size * 0.3f, w * 0.5f), Quaternion.Euler(g.Range(-4f, 4f), yaw, g.Range(-4f, 4f)), 0.35f);
                    Block(along * w * 0.6f, new Vector3(w * 0.45f, size * 0.75f, w * 0.5f), Quaternion.Euler(g.Range(-6f, 6f), yaw + g.Range(-10f, 10f), g.Range(-6f, 6f)), 0.25f);
                    break;
                }
                case 3:   // standing
                    Block(Vector3.zero, new Vector3(w * 0.5f, size * 0.95f, w * g.Range(0.35f, 0.55f)), Turn(12f), 0.2f);
                    break;
                default:   // monstrous: a big mass split down the middle, chunks fallen from it
                {
                    var big = 1.9f;
                    var split = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * w * big * 0.32f;
                    Block(-split, new Vector3(w * big * 0.6f, size * big * 0.55f, w * big * 0.8f), Turn(5f), 0.3f);
                    Block(split, new Vector3(w * big * 0.5f, size * big * 0.48f, w * big * 0.75f), Turn(7f), 0.3f);
                    for (int k = 0; k < 2; k++)
                        Block(g.Flat(g.R() * 6.28f) * w * big * g.Range(0.9f, 1.2f), new Vector3(w * 0.45f, size * 0.35f, w * 0.4f), Turn(15f), 0.35f);
                    size *= big;
                    w *= big;
                    break;
                }
            }

            // Loose stones round the foot.
            var loose = Mathf.Max(0, g.Count(s.Branches) - 1);
            for (int k = 0; k < loose && g.Lod < 2; k++)
            {
                var r = w * g.Range(0.12f, 0.25f);
                var at = g.Flat(g.R() * 6.28f) * w * g.Range(0.9f, 1.4f) + Vector3.up * r * 0.3f;
                b.Blob(at, new Vector3(r, r * g.Range(0.45f, 0.75f), r * g.Range(0.6f, 1f)), Turn(20f), Mathf.Max(0, detail - 1), 0.12f, g.R() * 100f,
                       Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, 0.4f, 0.6f);
            }

            g.Height = size;
            g.Width = w * 2.4f;
            g.TrunkRadius = w * 0.45f;
            b.Occlude(first, (p, n) => GroundOcclusion(p, size * 0.25f) * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(n.y + 0.6f)));
            Moss(g, first);
        }

        static void Spire(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Thickness;
            var blocks = g.Count(s.Branches);
            var capScale = 1f + s.TrunkFraction * 2.5f;
            var yaw = Quaternion.Euler(0f, g.R() * 360f, 0f);
            var lean = g.Flat(g.R() * 6.28f) * g.Range(0f, 0.05f);
            var detail = g.Sides(2, 2, 1);
            var bands = s.Foliage.linear;

            var first = b.VertexCount;
            var y = -radius * 0.2f;
            var offset = Vector3.zero;
            for (int i = 0; i < blocks; i++)
            {
                var t = blocks > 1 ? i / (blocks - 1f) : 0f;
                var blockHeight = height / blocks * g.Range(0.8f, 1.2f);
                var r = radius * g.Range(0.85f, 1.1f);
                if (blocks > 1)
                    r *= Mathf.Lerp(1.35f, 1f, t);   // wider at the foot
                var cap = blocks > 1 && i == blocks - 1 && s.TrunkFraction > 0f;
                if (cap)
                {
                    r = radius * capScale;
                    blockHeight *= 0.6f;
                }
                var radii = new Vector3(r, blockHeight * 0.62f, r * s.Spread);
                var centre = offset + lean * y + Vector3.up * (y + blockHeight * 0.5f);
                var rotation = yaw * Quaternion.Euler(g.Range(-5f, 5f), g.Range(-25f, 25f), g.Range(-5f, 5f));
                var color = Color.Lerp(g.Bark, bands, g.R());
                b.Blob(centre, radii, rotation, detail, cap ? 0.1f : 0.16f, g.R() * 100f, color, FloraPart.Bark);
                y += blockHeight * 0.88f;
                offset += g.Flat(g.R() * 6.28f) * r * g.Range(0f, 0.12f);
            }
            g.Height = y;
            g.Width = radius * 2f * Mathf.Max(capScale, 1.35f);
            g.TrunkRadius = radius * (blocks > 1 ? 1.2f : Mathf.Lerp(s.Spread, 1f, 0.5f));
            b.Occlude(first, (p, n) => GroundOcclusion(p, radius * 2f) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n.y + 0.7f)));
            Moss(g, first);
        }

        static void Crystal(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = height * s.Thickness * 1.5f;

            // A rough rocky base the crystals grow out of.
            var baseRadius = height * Mathf.Max(s.Spread * 0.35f, s.Thickness * 1.6f);
            var firstRock = b.VertexCount;
            b.Blob(Vector3.up * baseRadius * 0.1f, new Vector3(baseRadius, baseRadius * 0.45f, baseRadius * 0.85f), Quaternion.Euler(0f, g.R() * 360f, 0f),
                   g.Sides(2, 1, 0), 0.3f, g.R() * 100f, g.Bark, FloraPart.Bark);
            b.Occlude(firstRock, (p, n) => GroundOcclusion(p, baseRadius));

            var count = g.Count(s.Branches);
            for (int i = 0; i < count; i++)
            {
                var main = i == 0;
                var tiltAxis = g.Flat(g.R() * 6.28f);
                var dir = Quaternion.AngleAxis(main ? g.Range(0f, 8f) : g.Range(15f, 50f), tiltAxis) * Vector3.up;
                var length = height * (main ? 1f : g.Range(0.3f, 0.75f));
                var r = length * s.Thickness * g.Range(0.8f, 1.2f);
                var root = main ? Vector3.zero : new Vector3(tiltAxis.z, 0f, -tiltAxis.x) * baseRadius * g.Range(-0.6f, 0.6f) - tiltAxis * baseRadius * g.Range(0f, 0.4f);
                root.y = -r * 0.5f;
                var color = g.FoliageColor();
                var path = new List<Vector3> { root, root + dir * length * 0.78f, root + dir * length };
                var radii = new List<float> { r, r * 0.92f, 0.002f };
                b.Tube(path, radii, 6, color, FloraPart.Foliage);
            }
        }

        static void Columns(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Thickness;
            var count = g.Count(s.Branches);
            g.Height = height;
            g.TrunkRadius = radius * 1.8f;

            // Hexagonal packing outwards from the centre; columns get shorter (and some broken) towards the edge.
            var first = b.VertexCount;
            var axial = new List<Vector2Int> { Vector2Int.zero };
            for (int ring = 1; axial.Count < count; ring++)
            {
                var hex = new Vector2Int(-ring, ring);
                Vector2Int[] steps = { new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1) };
                foreach (var step in steps)
                    for (int k = 0; k < ring && axial.Count < count; k++)
                    {
                        axial.Add(hex);
                        hex += step;
                    }
            }
            var width = 0f;
            var rotation = Quaternion.Euler(0f, 30f, 0f);
            foreach (var a in axial)
            {
                var centre = new Vector3(radius * 1.732f * (a.x + a.y * 0.5f), 0f, radius * 1.5f * a.y);
                var ring = Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.x + a.y));
                var h = height * Mathf.Clamp01(1f - ring * 0.18f * (0.5f + s.Spread) + g.Range(-0.25f, 0.25f) * s.Spread);
                if (h < radius)
                    continue;
                width = Mathf.Max(width, centre.magnitude + radius);
                var color = Color.Lerp(g.Bark, g.Bark * 0.8f, g.R());
                b.Tube(new List<Vector3> { centre + Vector3.down * 0.2f, centre + Vector3.up * h },
                       new List<float> { radius * 0.98f, radius * 0.94f }, 6, color, FloraPart.Bark);
                // A flat hexagonal top.
                var top = b.VertexCount;
                b.Vertex(centre + Vector3.up * h, Vector3.up, color, FloraPart.Bark, 1f);
                for (int k = 0; k < 6; k++)
                {
                    var angle = k / 6f * Mathf.PI * 2f;
                    b.Vertex(centre + Vector3.up * h + rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * 0.94f, Vector3.up, color, FloraPart.Bark, 1f);
                }
                for (int k = 0; k < 6; k++)
                    b.Triangle(top, top + 1 + (k + 1) % 6, top + 1 + k);
            }
            g.Width = width * 2f;
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.4f));
            Moss(g, first);
        }

        static void Hut(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var wall = height * 0.45f;
            g.Height = height;
            g.Width = radius * 2.5f;
            g.TrunkRadius = radius;
            var sides = g.Sides(14, 10, 7);

            // Walls, slightly bulging, sunk a little into the ground.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * wall * 0.5f, Vector3.up * wall },
                   new List<float> { radius, radius * 1.02f, radius * 0.98f }, sides, g.Bark, FloraPart.Bark);
            // Timber bands round the wall.
            if (g.Lod < 2)
                foreach (var y in new[] { 0.15f, wall * 0.95f })
                    b.Tube(new List<Vector3> { Vector3.up * (y - 0.06f), Vector3.up * (y + 0.06f) },
                           new List<float> { radius * 1.04f, radius * 1.04f }, sides, g.Bark * 0.6f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.6f));

            // Conical roof with an overhang, rough at the edge.
            var roof = b.VertexCount;
            var overhang = radius * 1.3f;
            var roofColor = g.FoliageColor();
            b.Tube(new List<Vector3> { Vector3.up * (wall - 0.15f), Vector3.up * (wall + (height - wall) * 0.45f), Vector3.up * height },
                   new List<float> { overhang, overhang * 0.55f, 0.05f }, sides, roofColor, FloraPart.Bark, cap: true);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y + 0.2f)));

            // Doorway facing +Z: a dark arch set into the wall, with a lintel.
            var doorHeight = Mathf.Min(wall * 0.85f, 1.9f);
            var door = new Vector3(0f, doorHeight * 0.5f, radius * 0.97f);
            b.Blob(door, new Vector3(radius * 0.28f, doorHeight * 0.5f, 0.12f), Quaternion.identity, 1, 0.02f, 0f,
                   new Color(0.02f, 0.017f, 0.015f), FloraPart.Bark, 0.2f);
            if (g.Lod < 2)
                b.Tube(new List<Vector3> { door + new Vector3(-radius * 0.35f, doorHeight * 0.5f + 0.05f, 0.08f), door + new Vector3(radius * 0.35f, doorHeight * 0.5f + 0.05f, 0.08f) },
                       new List<float> { 0.07f, 0.07f }, 5, g.Bark * 0.55f, FloraPart.Bark);
        }


        /// <summary>A doorway: a dark slab set into a wall facing +Z, at distance <paramref name="front"/>.</summary>
        static void Doorway(Grower g, float front, float width, float height)
        {
            g.B.Blob(new Vector3(0f, height * 0.5f, front), new Vector3(width * 0.5f, height * 0.5f, 0.1f), Quaternion.identity, 1, 0.02f, 0f,
                     new Color(0.02f, 0.017f, 0.015f), FloraPart.Bark, 0.2f);
        }

        /// <summary>Small square windows round a wall at a height, in the accent colour (lit at night if glowing).</summary>
        static void Windows(Grower g, float radius, float y, int count, float size, float skipAngle)
        {
            if (!g.HasAccent || g.Lod == 2)
                return;
            for (int k = 0; k < count; k++)
            {
                var a = (k + 0.5f) / count * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < skipAngle)
                    continue;   // keep clear of the door (+Z is 90° here)
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                g.B.Blob(dir * radius + Vector3.up * y, new Vector3(size, size * 1.2f, size) * 0.5f, Quaternion.LookRotation(dir), 0, 0f, 0f,
                         g.Accent, FloraPart.Petal);
            }
        }

        static void StoneHouse(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;      // half the wall's width
            var wall = height * 0.55f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 1.1f;
            var corner = half * 1.414f;              // a 4-sided tube's radius to its corners
            var square = Quaternion.Euler(0f, 45f, 0f);

            // Walls: a square prism, rotated so its faces point along the axes.
            var first = b.VertexCount;
            var path = new List<Vector3> { Vector3.down * 0.15f, Vector3.up * wall };
            b.Tube(path, new List<float> { corner, corner }, 4, g.Bark, FloraPart.Bark);
            b.Recolor(first, (p, c) => p.y < 0.6f ? c * 0.8f : c);   // darker footing
            RotateFrom(b, first, square);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.5f));

            // Low pyramid roof with an overhang, and a chimney.
            var roof = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.up * (wall - 0.1f), Vector3.up * height }, new List<float> { corner * 1.2f, 0.05f }, 4, g.FoliageColor(), FloraPart.Bark, cap: true);
            RotateFrom(b, roof, square);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            var chimney = new Vector3(half * 0.55f, 0f, -half * 0.4f);
            var stack = b.VertexCount;
            b.Tube(new List<Vector3> { chimney + Vector3.up * (wall * 0.8f), chimney + Vector3.up * (height + 0.5f) },
                   new List<float> { 0.32f, 0.3f }, 4, g.Bark * 0.85f, FloraPart.Bark);
            RotateAround(b, stack, chimney, square);

            Doorway(g, half + 0.02f, Mathf.Min(half * 0.6f, 1.2f), Mathf.Min(wall * 0.8f, 2f));
            Windows(g, half + 0.03f, wall * 0.55f, 4, 0.5f, 30f);
        }

        /// <summary>Rotates vertices added since <paramref name="first"/> about the vertical axis through the origin.</summary>
        static void RotateFrom(FloraMeshBuilder b, int first, Quaternion q) => b.Transform(first, p => q * p, n => q * n);

        static void RotateAround(FloraMeshBuilder b, int first, Vector3 centre, Quaternion q) =>
            b.Transform(first, p => centre + q * (p - centre), n => q * n);

        static void Tent(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.2f;
            g.TrunkRadius = radius * 0.85f;

            // Poles lean in to meet near the top, then poke out past it, a little crooked.
            var poles = Mathf.Max(3, g.Count(s.Branches));
            var apex = new Vector3(g.Range(-0.1f, 0.1f), height * 0.88f, g.Range(-0.1f, 0.1f));
            var feet = new List<Vector3>();
            for (int k = 0; k < poles; k++)
            {
                var a = (k + g.Range(-0.2f, 0.2f)) / poles * Mathf.PI * 2f + 0.3f;
                var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.9f, 1.05f);
                feet.Add(foot);
                var through = apex + (apex - foot).normalized * height * g.Range(0.12f, 0.22f);
                b.Tube(new List<Vector3> { foot + Vector3.down * 0.1f, apex, through }, new List<float> { radius * 0.04f, radius * 0.035f, radius * 0.025f },
                       g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }

            // Hide panels between neighbouring poles, each a slightly different hide, sagging inwards.
            var first = b.VertexCount;
            var rows = g.Sides(5, 3, 2);
            for (int k = 0; k < poles; k++)
            {
                var a = feet[k];
                var c = feet[(k + 1) % poles];
                var colour = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R());
                // The panel facing +Z is left open as the doorway.
                var mid = (a + c) * 0.5f;
                var open = mid.z > radius * 0.5f && Mathf.Abs(mid.x) < radius * 0.6f;
                var top = b.VertexCount;
                for (int r = 0; r <= rows; r++)
                {
                    var t = r / (float)rows;
                    var sag = Mathf.Sin(t * Mathf.PI) * radius * 0.06f;
                    var p0 = Vector3.Lerp(a, apex, t);
                    var p1 = Vector3.Lerp(c, apex, t);
                    var inward = -((p0 + p1) * 0.5f - Vector3.up * ((p0.y + p1.y) * 0.5f)).normalized;
                    var ao = Mathf.Lerp(0.7f, 1f, t);
                    var cut = open && t < 0.55f;
                    var n = Vector3.Cross(p1 - p0, apex - p0).normalized;
                    if (Vector3.Dot(n, (p0 + p1) * 0.5f - Vector3.up * p0.y) < 0f) n = -n;
                    b.Vertex(p0 + inward * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                    b.Vertex(p1 + inward * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                }
                for (int r = 0; r < rows; r++)
                {
                    int i0 = top + r * 2;
                    b.Triangle(i0, i0 + 2, i0 + 1);
                    b.Triangle(i0 + 1, i0 + 2, i0 + 3);
                }
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.3f));
        }

        static void Tower(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var walls = height * 0.62f;
            g.Height = height;
            g.Width = radius * 3f;
            g.TrunkRadius = radius;
            var sides = g.Sides(12, 8, 6);

            // Walls taper gently, flaring at the foot like a root.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * walls * 0.15f, Vector3.up * walls * 0.6f, Vector3.up * walls },
                   new List<float> { radius * 1.35f, radius * 1.05f, radius * 0.9f, radius * 0.85f }, sides, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, walls * 0.3f));

            // Balconies: thin rings round the walls.
            var floors = Mathf.Clamp(g.Count(s.Branches), 1, 4);
            for (int f = 1; f <= floors; f++)
            {
                var y = walls * f / (floors + 1f);
                var r = Mathf.Lerp(radius * 1.05f, radius * 0.88f, y / walls);
                b.Tube(new List<Vector3> { Vector3.up * (y - 0.05f), Vector3.up * (y + 0.05f) }, new List<float> { r * 1.45f, r * 1.45f }, sides, g.Bark * 0.8f, FloraPart.Bark);
                Windows(g, r * 0.98f, y + 0.7f, 5, 0.35f, f == 1 ? 30f : -1f);
            }

            // A tall, slightly flared roof.
            var roof = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.up * (walls - 0.05f), Vector3.up * (walls + (height - walls) * 0.25f), Vector3.up * height },
                   new List<float> { radius * 1.25f, radius * 0.7f, 0.02f }, sides, g.FoliageColor(), FloraPart.Foliage, cap: true);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));

            Doorway(g, radius * 1.1f, Mathf.Min(radius * 0.7f, 1f), Mathf.Min(walls * 0.25f, 2.2f));
        }


        // ------------------------------------------------------------------------------------------- settlement structures

        static Color Stone(Grower g) => new Color(0.42f, 0.41f, 0.39f).linear;
        static Color AccentAt(Grower g, int i) => g.S.Accents.Count > 0 ? g.S.Accents[i % g.S.Accents.Count].linear : Color.white;

        static void Pen(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.2f;
            g.TrunkRadius = 0f;
            var postRadius = Mathf.Max(0.05f, height * 0.055f);
            var posts = Mathf.Max(6, g.Count(s.Branches));
            var tops = new List<Vector3>();
            for (int k = 0; k < posts; k++)
            {
                var a = (k + 0.5f) / posts * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.97f, 1.03f);
                tops.Add(p);
                var first = b.VertexCount;
                b.Tube(new List<Vector3> { p + Vector3.down * 0.1f, p + Vector3.up * height * g.Range(0.95f, 1.08f) },
                       new List<float> { postRadius, postRadius * 0.9f }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark, cap: true);
                b.Occlude(first, (q, n) => GroundOcclusion(q, height * 0.5f));
            }
            // Two rails between neighbouring posts, except across the gate (+Z).
            for (int k = 0; k < posts; k++)
            {
                var p = tops[k];
                var q = tops[(k + 1) % posts];
                var mid = (p + q) * 0.5f;
                if (mid.z > radius * 0.85f)
                    continue;
                foreach (var y in new[] { 0.45f, 0.85f })
                    b.Beam(p + Vector3.up * height * y, q + Vector3.up * height * y, postRadius * 1.2f, postRadius * 0.9f, g.Bark * 0.9f, FloraPart.Bark);
            }
            // A trough inside.
            b.Box(new Vector3(-radius * 0.45f, 0.22f, -radius * 0.35f), new Vector3(1.4f, 0.44f, 0.5f), Quaternion.Euler(0f, 30f, 0f), g.Bark * 0.7f, FloraPart.Bark);
        }

        static void Stall(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var width = height * s.Spread;
            var depth = width * 0.7f;
            g.Height = height;
            g.Width = Mathf.Max(width, depth) * 1.3f;
            g.TrunkRadius = 0f;
            var post = 0.06f;
            var frontHeight = height * 0.82f;
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var z in new[] { -0.5f, 0.5f })
            {
                var top = z > 0f ? frontHeight : height;
                b.Tube(new List<Vector3> { new Vector3(x * width, -0.05f, z * depth), new Vector3(x * width, top, z * depth) },
                       new List<float> { post, post }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }
            // Counter across the front, and goods laid out on it.
            b.Box(new Vector3(0f, 0.45f, depth * 0.5f - 0.15f), new Vector3(width * 0.95f, 0.9f, 0.45f), Quaternion.identity, g.Bark * 0.8f, FloraPart.Bark);
            if (g.Lod < 2)
                for (int k = 0; k < 7; k++)
                {
                    var at = new Vector3(Mathf.Lerp(-0.4f, 0.4f, k / 6f) * width, 0.98f, depth * 0.5f - 0.15f + g.Range(-0.08f, 0.08f));
                    b.Blob(at, Vector3.one * g.Range(0.07f, 0.12f), Quaternion.identity, 0, 0.2f, g.R() * 50f, AccentAt(g, k + 1), FloraPart.Bark);
                }
            // A striped awning sloping from the back down over the front, overhanging it.
            var back = new Vector3(0f, height + 0.05f, -depth * 0.55f);
            var front = new Vector3(0f, frontHeight - 0.15f, depth * 0.5f + 0.45f);
            var stripes = g.Sides(6, 4, 2);
            for (int k = 0; k < stripes; k++)
            {
                var x = (k + 0.5f) / stripes - 0.5f;
                var colour = k % 2 == 0 ? g.FoliageColor() : AccentAt(g, 0);
                b.Beam(back + Vector3.right * x * width * 1.1f, front + Vector3.right * x * width * 1.1f, width * 1.1f / stripes, 0.04f, colour, FloraPart.Bark);
            }
        }

        static void Inn(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            var front = half * 0.8f;
            var wall = height * 0.6f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 0.9f;

            // Walls, with a timber band between the storeys and dark corner posts.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, wall * 0.5f - 0.1f, 0f), new Vector3(half * 2f, wall + 0.2f, front * 2f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.4f));
            var timber = g.Bark * 0.5f;
            if (g.Lod < 2)
            {
                b.Box(new Vector3(0f, wall * 0.5f, 0f), new Vector3(half * 2f + 0.08f, 0.2f, front * 2f + 0.08f), Quaternion.identity, timber, FloraPart.Bark);
                foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -1f, 1f })
                    b.Box(new Vector3(x * half, wall * 0.5f, z * front), new Vector3(0.25f, wall, 0.25f), Quaternion.identity, timber, FloraPart.Bark);
            }

            // Gabled roof: two slabs meeting at a ridge along X, and the gable ends filled in.
            var roof = g.FoliageColor();
            var ridge = height;
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, wall - 0.25f, z * (front + 0.6f)), new Vector3(0f, ridge + 0.1f, 0f), half * 2f + 0.8f, 0.18f, roof, FloraPart.Bark);
            foreach (var x in new[] { -1f, 1f })
            {
                var v0 = b.Vertex(new Vector3(x * half, wall, -front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v1 = b.Vertex(new Vector3(x * half, wall, front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v2 = b.Vertex(new Vector3(x * half, ridge, 0f), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                b.Triangle(v0, v1, v2);
            }
            b.Box(new Vector3(half * 0.6f, height * 0.85f, -front * 0.35f), new Vector3(0.6f, height * 0.5f, 0.6f), Quaternion.identity, Stone(g), FloraPart.Bark);

            // Door, windows (lit at night) and the porch out front.
            Doorway(g, front + 0.02f, 1.1f, Mathf.Min(wall * 0.42f, 2.1f));
            if (g.Lod < 2)
                foreach (var (x, y) in new[] { (-0.55f, 0.27f), (0.55f, 0.27f), (-0.55f, 0.75f), (0f, 0.75f), (0.55f, 0.75f) })
                    b.Box(new Vector3(x * half, y * wall, front + 0.03f), new Vector3(0.6f, 0.7f, 0.06f), Quaternion.identity, AccentAt(g, 0), FloraPart.Petal);
            var porch = 2.4f;
            b.Box(new Vector3(0f, 0.15f, front + porch * 0.5f), new Vector3(half * 1.8f, 0.3f, porch), Quaternion.identity, g.Bark * 0.75f, FloraPart.Bark);
            b.Beam(new Vector3(0f, wall * 0.48f, front), new Vector3(0f, wall * 0.4f, front + porch + 0.3f), half * 1.9f, 0.12f, roof, FloraPart.Bark);
            foreach (var x in new[] { -0.85f, 0.85f })
                b.Tube(new List<Vector3> { new Vector3(x * half, 0.3f, front + porch - 0.1f), new Vector3(x * half, wall * 0.41f, front + porch - 0.1f) },
                       new List<float> { 0.1f, 0.1f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
            // A sign hanging from the porch roof.
            if (g.Lod < 2)
                b.Box(new Vector3(half * 0.55f, wall * 0.3f, front + porch + 0.2f), new Vector3(0.9f, 0.55f, 0.06f), Quaternion.identity, AccentAt(g, 1), FloraPart.Bark);
        }

        static void Smithy(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            var depth = half * 0.8f;
            var eaves = height * 0.58f;
            g.Height = height;
            g.Width = half * 2.6f;
            g.TrunkRadius = 0f;

            // An open shed: corner posts under a pyramid roof.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Tube(new List<Vector3> { new Vector3(x * half, -0.05f, z * depth), new Vector3(x * half, eaves, z * depth) },
                       new List<float> { 0.12f, 0.11f }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark);
            var roof = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.up * (eaves - 0.1f), Vector3.up * height }, new List<float> { Mathf.Max(half, depth) * 1.414f * 1.15f, 0.05f },
                   4, g.FoliageColor(), FloraPart.Bark, cap: true);
            RotateFrom(b, roof, Quaternion.Euler(0f, 45f, 0f));

            // The forge at the back: a stone hearth of glowing coals, and its chimney up through the roof.
            var hearth = new Vector3(0f, 0.55f, -depth * 0.45f);
            b.Box(hearth, new Vector3(half * 1.1f, 1.1f, depth * 0.8f), Quaternion.identity, Stone(g), FloraPart.Bark);
            b.Blob(hearth + Vector3.up * 0.58f, new Vector3(half * 0.4f, 0.12f, depth * 0.28f), Quaternion.identity, 1, 0.3f, g.R() * 50f, AccentAt(g, 0), FloraPart.Glow);
            b.Box(new Vector3(0f, (1.1f + height + 0.8f) * 0.5f, -depth * 0.7f), new Vector3(0.7f, height + 0.8f - 1.1f, 0.6f), Quaternion.identity, Stone(g) * 0.85f, FloraPart.Bark);
            // The anvil on its block.
            var anvil = new Vector3(half * 0.3f, 0f, depth * 0.25f);
            var iron = new Color(0.06f, 0.06f, 0.07f);
            b.Box(anvil + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            b.Box(anvil + Vector3.up * 0.72f, new Vector3(0.75f, 0.24f, 0.3f), Quaternion.identity, iron, FloraPart.Metal);
        }

        static void Forum(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var columns = Mathf.Max(6, g.Count(s.Branches));
            var shaft = height * 0.045f;
            g.Height = height + 0.6f;
            g.Width = radius * 2.3f;
            g.TrunkRadius = 0f;

            // A low round platform.
            b.Blob(Vector3.zero, new Vector3(radius + 1.2f, 0.25f, radius + 1.2f), Quaternion.identity, g.Sides(3, 2, 1), 0f, 0f, g.Bark * 0.9f, FloraPart.Bark);
            var tops = new List<Vector3>();
            for (int k = 0; k < columns; k++)
            {
                var a = k / (float)columns * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                var first = b.VertexCount;
                b.Box(p + Vector3.up * 0.25f, new Vector3(shaft * 3f, 0.3f, shaft * 3f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), g.Bark, FloraPart.Bark);
                b.Tube(new List<Vector3> { p + Vector3.up * 0.35f, p + Vector3.up * height * 0.5f, p + Vector3.up * height },
                       new List<float> { shaft, shaft * 1.02f, shaft * 0.86f }, g.Sides(10, 6, 4), g.Bark, FloraPart.Bark, ribs: g.Lod == 0 ? 10 : 0, ribDepth: 0.06f);
                b.Box(p + Vector3.up * (height + 0.12f), new Vector3(shaft * 3f, 0.25f, shaft * 3f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), g.Bark, FloraPart.Bark);
                b.Occlude(first, (q, n) => GroundOcclusion(q, height * 0.3f));
                tops.Add(p + Vector3.up * (height + 0.4f));
            }
            // The lintel ring joining the column tops.
            for (int k = 0; k < columns; k++)
                b.Beam(tops[k], tops[(k + 1) % columns], shaft * 2.6f, 0.4f, g.Bark * 0.95f, FloraPart.Bark);
        }

        static void Fountain(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.1f;
            g.TrunkRadius = radius;
            var sides = g.Sides(18, 12, 8);

            // The basin wall, its rim, and the water inside.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.6f }, new List<float> { radius, radius }, sides, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.6f));
            b.Tube(new List<Vector3> { Vector3.up * 0.58f, Vector3.up * 0.7f }, new List<float> { radius + 0.12f, radius + 0.12f }, sides, g.Bark * 1.05f, FloraPart.Bark);
            var water = s.Foliage.linear;
            b.Blob(Vector3.up * 0.66f, new Vector3(radius * 0.97f, 0.03f, radius * 0.97f), Quaternion.identity, g.Sides(2, 1, 1), 0f, 0f, water, FloraPart.Metal);

            // A pillar holding up a smaller bowl, brimming, with a finial on top.
            b.Tube(new List<Vector3> { Vector3.up * 0.5f, Vector3.up * height * 0.72f }, new List<float> { 0.22f, 0.16f }, g.Sides(8, 6, 4), g.Bark, FloraPart.Bark);
            b.Blob(Vector3.up * height * 0.75f, new Vector3(radius * 0.38f, 0.14f, radius * 0.38f), Quaternion.identity, g.Sides(2, 1, 1), 0f, 0f, g.Bark, FloraPart.Bark);
            b.Blob(Vector3.up * (height * 0.75f + 0.1f), new Vector3(radius * 0.34f, 0.03f, radius * 0.34f), Quaternion.identity, 1, 0f, 0f, water, FloraPart.Metal);
            b.Tube(new List<Vector3> { Vector3.up * height * 0.78f, Vector3.up * height }, new List<float> { 0.09f, 0.03f }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark, cap: true);
        }


        static void Woodpile(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var length = height * s.Spread;
            var log = height / 8f;
            g.Height = height;
            g.Width = length * 1.6f;
            g.TrunkRadius = length * 0.35f;

            // Logs along X, stacked in rows that narrow upwards; each a slightly different shade.
            var rows = 4;
            for (int r = 0; r < rows; r++)
            {
                var count = rows - r + 1;
                for (int k = 0; k < count; k++)
                {
                    var z = (k - (count - 1) * 0.5f) * log * 2.05f;
                    var y = log + r * log * 1.75f;
                    var ends = g.Range(-0.15f, 0.15f);
                    b.Tube(new List<Vector3> { new Vector3(-length * 0.5f + ends, y, z), new Vector3(length * 0.5f + ends, y, z) },
                           new List<float> { log * g.Range(0.85f, 1.05f), log * g.Range(0.85f, 1.05f) }, g.Sides(7, 5, 4),
                           Color.Lerp(g.Bark, g.Bark * 1.25f, g.R()), FloraPart.Bark, cap: g.Lod < 2);
                }
            }
            // The chopping block in front, with an axe in it.
            var block = new Vector3(length * 0.25f, 0f, log * 2.05f * 2.5f + 0.6f);
            b.Tube(new List<Vector3> { block + Vector3.down * 0.05f, block + Vector3.up * 0.5f }, new List<float> { 0.3f, 0.28f }, g.Sides(9, 6, 4), g.Bark * 0.85f, FloraPart.Bark);
            b.Blob(block + Vector3.up * 0.5f, new Vector3(0.28f, 0.02f, 0.28f), Quaternion.identity, 1, 0f, 0f, g.Bark * 1.3f, FloraPart.Bark);
            if (g.Lod < 2)
            {
                var head = block + new Vector3(0.05f, 0.56f, 0f);
                b.Beam(head, head + new Vector3(-0.45f, 0.45f, 0.1f), 0.05f, 0.05f, g.Bark * 0.7f, FloraPart.Bark);
                b.Box(head + new Vector3(0.03f, 0.02f, 0f), new Vector3(0.22f, 0.12f, 0.05f), Quaternion.Euler(0f, 0f, 35f), new Color(0.12f, 0.12f, 0.13f), FloraPart.Metal);
            }
        }

        static void Well(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var stone = s.FoliageVariation.linear;
            var sides = g.Sides(16, 10, 7);
            g.Height = height;
            g.Width = radius * 2.6f;
            g.TrunkRadius = radius + 0.05f;

            // The wall (outer and inner faces, and a rim), with dark water well down inside.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.8f }, new List<float> { radius, radius }, sides, stone, FloraPart.Bark);
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.8f }, new List<float> { radius - 0.16f, radius - 0.16f }, sides, stone * 0.6f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.6f));
            b.Tube(new List<Vector3> { Vector3.up * 0.78f, Vector3.up * 0.9f }, new List<float> { radius + 0.05f, radius + 0.05f }, sides, stone * 1.05f, FloraPart.Bark);
            b.Blob(Vector3.up * 0.25f, new Vector3(radius - 0.16f, 0.02f, radius - 0.16f), Quaternion.identity, 1, 0f, 0f, new Color(0.03f, 0.05f, 0.06f), FloraPart.Metal);

            // Posts, a little gabled roof, the crank and a bucket.
            var top = height * 0.78f;
            foreach (var x in new[] { -1f, 1f })
                b.Box(new Vector3(x * (radius + 0.05f), top * 0.5f, 0f), new Vector3(0.16f, top, 0.16f), Quaternion.identity, g.Bark, FloraPart.Bark);
            var roof = g.FoliageColor();
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, top - 0.05f, z * (radius * 0.75f + 0.25f)), new Vector3(0f, height, 0f), radius * 2f + 0.6f, 0.08f, roof, FloraPart.Bark);
            b.Beam(new Vector3(-radius - 0.15f, top * 0.72f, 0f), new Vector3(radius + 0.25f, top * 0.72f, 0f), 0.08f, 0.08f, g.Bark * 0.8f, FloraPart.Bark);
            if (g.Lod < 2)
                b.Tube(new List<Vector3> { new Vector3(0f, top * 0.42f, 0f), new Vector3(0f, top * 0.55f, 0f) }, new List<float> { 0.15f, 0.17f },
                       g.Sides(8, 5, 4), g.Bark * 0.7f, FloraPart.Bark, cap: true);
        }


        static void Reeds(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread;
            g.TrunkRadius = 0f;
            var blades = g.Count(s.Branches);
            if (g.Lod == 2)
                blades = Mathf.Max(3, blades / 2);
            for (int i = 0; i < blades; i++)
            {
                var root = g.Flat(g.R() * 6.28f) * height * s.Spread * 0.15f * g.R();
                var h = height * g.Range(0.55f, 1f);
                var dir = g.Flat(g.R() * 6.28f);
                var arch = g.Range(0.05f, 0.35f);
                var colour = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R()) * g.Range(0.9f, 1.1f);
                var across = Vector3.Cross(dir, Vector3.up).normalized;
                var width = Mathf.Clamp(height * 0.025f, 0.012f, 0.04f);
                var spine = new List<Vector3>();
                var widths = new List<float>();
                var rows = g.Lod == 0 ? 4 : 2;
                for (int k = 0; k <= rows; k++)
                {
                    var t = k / (float)rows;
                    spine.Add(root + Vector3.up * h * t + dir * h * arch * t * t);
                    widths.Add(width * (1f - t * 0.9f));
                }
                b.Strip(spine, widths, across, colour, FloraPart.Foliage, 0.75f, (Vector3.up * 0.3f + Vector3.Cross(across, Vector3.up)).normalized);
                // A few straight stems carry a cattail head.
                if (g.HasAccent && g.Lod < 2 && g.R() < s.AccentAmount * 0.5f)
                {
                    var top = root + Vector3.up * h * 1.05f + dir * h * 0.04f;
                    b.Tube(new List<Vector3> { root, top }, new List<float> { width * 0.25f, width * 0.2f }, 3, colour, FloraPart.Foliage, ao: 0.8f);
                    var headLength = h * 0.16f;
                    var headBottom = top - Vector3.up * headLength * 1.3f;
                    b.Tube(new List<Vector3> { headBottom, headBottom + Vector3.up * headLength }, new List<float> { width * 0.9f, width * 0.85f },
                           g.Sides(6, 4, 3), g.Accent, FloraPart.Petal, cap: true);
                }
            }
        }

        static void LilyPad(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var size = g.Range(s.Height);
            var pads = g.Count(s.Branches);
            var spread = size * s.Spread;
            g.Height = 0.15f;
            g.Width = spread * 2f + size;
            g.TrunkRadius = 0f;
            for (int k = 0; k < pads; k++)
            {
                var r = size * 0.5f * g.Range(0.6f, 1.1f);
                var at = k == 0 ? Vector3.zero : g.Flat(g.R() * 6.28f) * spread * g.Range(0.3f, 1f);
                at.y = 0.005f * k;
                // A flat round pad with a notch cut in (left as a gap in the ring of the disc).
                var first = b.VertexCount;
                var notch = g.R() * 6.28f;
                var sides = g.Sides(14, 9, 6);
                var centre = b.Vertex(at, Vector3.up, g.FoliageColor(), FloraPart.Foliage, 1f, 0f);
                var colour = g.FoliageColor();
                for (int i = 0; i <= sides; i++)
                {
                    var a = notch + 0.35f + i / (float)sides * (6.28f - 0.7f);
                    b.Vertex(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, Vector3.up, colour * g.Range(0.92f, 1.05f), FloraPart.Foliage, 0.9f, 0f);
                }
                for (int i = 0; i < sides; i++)
                    b.Triangle(centre, centre + 1 + i, centre + 2 + i);
                // Now and then a cupped flower rising from the pad.
                if (g.HasAccent && g.R() < s.AccentAmount && g.Lod < 2)
                {
                    var flower = at + g.Flat(g.R() * 6.28f) * r * 0.3f + Vector3.up * 0.05f;
                    var petals = g.Sides(8, 6, 4);
                    for (int i = 0; i < petals; i++)
                    {
                        var a = i / (float)petals * 6.28f;
                        var dir = new Vector3(Mathf.Cos(a), 1.1f, Mathf.Sin(a)).normalized;
                        var tip = flower + dir * size * 0.28f;
                        var side = Vector3.Cross(dir, Vector3.up).normalized * size * 0.07f;
                        var v0 = b.Vertex(flower - side, dir, g.Accent, FloraPart.Petal, 0.9f, 0f);
                        var v1 = b.Vertex(flower + side, dir, g.Accent, FloraPart.Petal, 0.9f, 0f);
                        var v2 = b.Vertex(tip, dir, g.Accent, FloraPart.Petal, 1f, 0f);
                        b.Triangle(v0, v1, v2);
                    }
                    b.Diamond(flower + Vector3.up * 0.02f, size * 0.05f, new Color(0.9f, 0.75f, 0.2f).linear, FloraPart.Petal);
                }
            }
        }

        static void Den(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radii = new Vector3(height * s.Spread, height, height * s.Spread * 0.85f);
            g.Height = height;
            g.Width = radii.x * 2f;
            g.TrunkRadius = radii.x * 0.75f;
            var detail = g.Sides(3, 2, 1);

            // The hump, sunk so its widest part is near the ground.
            var first = b.VertexCount;
            var rotation = Quaternion.Euler(0f, g.Range(-15f, 15f), 0f);
            b.Blob(Vector3.up * radii.y * 0.15f, radii, rotation, detail, 0.2f, g.R() * 100f, g.Bark, FloraPart.Bark);

            // Rocks framing the mouth, which faces +Z.
            var mouth = new Vector3(0f, height * 0.32f, radii.z * 0.9f);
            for (int side = -1; side <= 1; side += 2)
            {
                var at = mouth + new Vector3(side * height * 0.55f, -height * 0.15f, -height * 0.05f);
                b.Blob(at, Vector3.one * height * g.Range(0.22f, 0.32f), Quaternion.Euler(0f, g.R() * 360f, 0f), Mathf.Max(0, detail - 1), 0.25f,
                       g.R() * 100f, g.Bark * 0.85f, FloraPart.Bark);
            }
            b.Blob(mouth + new Vector3(0f, height * 0.42f, -height * 0.02f), new Vector3(height * 0.62f, height * 0.18f, height * 0.28f), Quaternion.identity,
                   Mathf.Max(0, detail - 1), 0.2f, g.R() * 100f, g.Bark * 0.9f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.4f) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n.y + 0.6f)));
            Moss(g, first);

            // The opening: a dark, flattened disc set into the face.
            b.Blob(mouth, new Vector3(height * 0.45f, height * 0.4f, height * 0.12f), Quaternion.identity, Mathf.Max(0, detail - 1), 0.08f,
                   g.R() * 100f, new Color(0.012f, 0.01f, 0.009f), FloraPart.Bark, 0.2f);
        }
    }
}
