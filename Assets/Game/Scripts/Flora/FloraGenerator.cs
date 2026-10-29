using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>One grown variant of a species, at three levels of detail.</summary>
    /// <summary>A model's walkable tops, built from its mesh (FloraMeshBuilder.SupportGrid; see PropSupport).</summary>
    public class SupportGridData
    {
        public Vector2 Min;
        public float Cell;
        public Vector2Int Res;
        /// <summary>Res.x × Res.y × SupportGrids.Layers heights, highest first per cell, NaN where none.</summary>
        public float[] Values;
        /// <summary>Res.x × Res.y water heights (NaN where none), or null.</summary>
        public float[] Water;
        public float Reach, Error;
    }

    public class FloraModel
    {
        /// <summary>Its walkable tops, for forms people can climb and stand on (rocks, buildings, rims), else null.</summary>
        public SupportGridData Support;
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
        /// <summary>
        /// Places a bird can sit on its top (a roof's peak or ridge ends, a chimney, a post), before the plant's own
        /// rotation and scale. Empty for most plants (see FloraPlacement.NearPerches).
        /// </summary>
        public readonly List<Vector3> Perches = new List<Vector3>();
        /// <summary>Whether it casts shadows. Small props (fences, palisades, signposts, garden rows, woodpiles,
        /// stalls, flowers) don't: towns have hundreds of them, and every shadow-casting draw costs a shadow pass.</summary>
        public bool CastsShadows = true;
        /// <summary>Height of its door at scale 1 (0 if it has none): doors are sized for human folk, about 1.3 times a
        /// person's height, and each settlement scales its buildings by its folk's size (SettlementLayout.BuildingScale).</summary>
        public float DoorHeight;

        /// <summary>Forms that cast no shadow (see CastsShadows).</summary>
        public static bool Shadowless(FloraForm form) =>
            form is FloraForm.Pen or FloraForm.Palisade or FloraForm.Signpost or FloraForm.Garden or FloraForm.Woodpile
                 or FloraForm.Stall or FloraForm.Flowers;

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
            model.CastsShadows = !FloraModel.Shadowless(species.Form);
            var seed = (int)Hash((uint)species.Seed, (uint)variant, StableHash(species.name));
            for (int lod = 0; lod < 3; lod++)
            {
                builder.Clear();
                builder.FoliageAs = species.Finish == FoliageFinish.Metallic ? FloraPart.Metal
                                  : species.Finish == FoliageFinish.Glowing ? FloraPart.Glow
                                  : species.Finish == FoliageFinish.Glass ? FloraPart.Glass : FloraPart.Foliage;
                builder.PetalAs = species.GlowingAccents ? FloraPart.Glow : FloraPart.Petal;
                builder.GlowPulse = species.GlowPulse;
                builder.BarkSeams = species.GlowingSeams;
                builder.BarkVeins = species.GlowingVeins;
                builder.BarkStrata = species.RockStrata;
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
                    case FloraForm.Eggs: Eggs(g); break;
                    case FloraForm.MineFrame: MineFrame(g); break;
                    case FloraForm.Torch: Torch(g); break;
                    case FloraForm.Bench: Bench(g); break;
                    case FloraForm.Log: SeatLog(g); break;
                    case FloraForm.Stump: Stump(g); break;
                    case FloraForm.FirePit: FirePit(g); break;
                    case FloraForm.HiveTree: Broadleaf(g); Hive(g); break;
                    case FloraForm.IceArch: IceArch(g); break;
                    case FloraForm.Fulgurite: Fulgurite(g); break;
                    case FloraForm.ArcSpire: ArcSpire(g); break;
                    case FloraForm.Debris: Debris(g); break;
                    case FloraForm.Bones: Bones(g); break;
                    case FloraForm.Boulder: Boulder(g); break;
                    case FloraForm.Spire: Spire(g); break;
                    case FloraForm.Crystal: Crystal(g); break;
                    case FloraForm.Den: Den(g); break;
                    case FloraForm.Columns: Columns(g); break;
                    case FloraForm.Hut: Hut(g); break;
                    case FloraForm.StoneHouse: StoneHouse(g); break;
                    case FloraForm.Ruin: Ruin(g); break;
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
                    case FloraForm.Roots: Roots(g); break;
                    case FloraForm.Hall: Hall(g); break;
                    case FloraForm.Shrine: Shrine(g); break;
                    case FloraForm.Garden: Garden(g); break;
                    case FloraForm.Lodge: Lodge(g); break;
                    case FloraForm.Signpost: Signpost(g); break;
                    case FloraForm.Bridge: Bridge(g); break;
                    case FloraForm.Palisade: Palisade(g); break;
                }
                if (species.Brackets > 0f && g.TrunkPath != null)
                    Brackets(g);
                if (g.Limbs != null && g.Lod < 2)
                {
                    if (species.Hanging > 0f) Hanging(g);
                    if (species.Cocoons > 0f) Cocoons(g);
                }
                if (species.BarkFace > 0f && g.TrunkPath != null)
                    BarkFace(g);
                if (Founded(species.Form))
                    builder.Skirt(FoundationDepth);
                if (lod == 0)
                {
                    if (Standable(species.Form))
                    {
                        // Clear ice arches stand like rock; a fountain's water (drawn as metal) is for wading.
                        var fountain = species.Form == FloraForm.Fountain;
                        model.Support = builder.SupportGrid(
                            part => part == FloraPart.Bark || part == FloraPart.Cactus || part == FloraPart.Comb || part == FloraPart.Metal && !fountain ||
                                    part == FloraPart.Glass && species.Form == FloraForm.IceArch,
                            part => fountain && part == FloraPart.Metal);
                    }
                    builder.Bounds(out model.CentreY, out model.Radius);
                    model.TrunkRadius = g.TrunkRadius;
                    model.Colliders.AddRange(g.Colliders);
                    model.Perches.AddRange(g.Perches);
                    model.DoorHeight = g.DoorHeight;
                    model.Height = g.Height;
                }
                model.Lods[lod] = builder.ToMesh($"{species.name} {variant} LOD{lod}", g.Height, g.Width, species.Flexibility);
            }
            return model;
        }

        /// <summary>
        /// A variant's shape without growing it: trees draw their height first from the same seeded sequence Grow
        /// uses, so this matches the model exactly. Anything that isn't a tree is TreeKind.None.
        /// </summary>
        public static FloraShape Shape(FloraSpecies species, int variant)
        {
            var kind = species.Form == FloraForm.BroadleafTree ? TreeKind.Broadleaf
                     : species.Form == FloraForm.Conifer ? TreeKind.Conifer
                     : species.Form == FloraForm.Palm ? TreeKind.Palm : TreeKind.None;
            if (kind == TreeKind.None)
                return default;
            var seed = (int)Hash((uint)species.Seed, (uint)variant, StableHash(species.name));
            var height = Mathf.Lerp(species.Height.x, species.Height.y, (float)new System.Random(seed).NextDouble());
            return new FloraShape
            {
                Kind = kind,
                Height = height,
                TrunkRadius = height * species.Thickness,
                // Palms carry their fronds at the top; the others branch from TrunkFraction up.
                CrownBase = kind == TreeKind.Palm ? height * 0.88f : height * species.TrunkFraction,
                CrownRadius = height * species.Spread * 0.5f
            };
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
            public float Height, Width, TrunkRadius, DoorHeight;
            /// <summary>A tree's trunk centreline and radii, bottom to top (Broadleaf), for things that hang on it.</summary>
            public List<Vector3> TrunkPath;
            public List<float> TrunkRadii;
            /// <summary>A tree's limbs (Broadleaf), each a path from the trunk outwards, for things that hang from them.</summary>
            public List<List<Vector3>> Limbs;
            public readonly List<Vector4> Colliders = new List<Vector4>();
            public readonly List<Vector3> Perches = new List<Vector3>();

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
            g.TrunkPath = trunk;
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

                // The cap, and darker gills underneath it.
                var cap = b.Blob(top + tilt * Vector3.up * capHeight * 0.25f, new Vector3(capRadius, capHeight, capRadius), tilt,
                                 g.Lod == 0 ? 2 : g.Lod == 1 ? 1 : 0, 0.06f, seed, color, FloraPart.Foliage);
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
                // they show from beneath and never from the side.
                if (giant && g.Lod == 0)
                {
                    var centre = top + tilt * Vector3.up * capHeight * 0.25f;
                    var gill = Color.Lerp(color, g.Bark, 0.4f) * 0.45f;
                    Vector3 Under(Vector3 dir, float r) =>
                        centre + tilt * (dir * r - Vector3.up * (capHeight * 0.3f * Mathf.Sqrt(Mathf.Max(1f - r * r / (capRadius * capRadius), 0f)) + 0.01f));
                    for (int k = 0; k < 30; k++)
                    {
                        var dir = Quaternion.AngleAxis(k * 12f + g.Range(-3f, 3f), Vector3.up) * Vector3.forward;
                        var inner = Under(dir, stemRadius * 1.3f);
                        var mid = Under(dir, capRadius * 0.55f);
                        var outer = Under(dir, capRadius * 0.88f);
                        var shade = gill * g.Range(0.8f, 1.2f);
                        b.Beam(inner, mid, capRadius * 0.05f, capRadius * 0.006f, shade, FloraPart.Bark);
                        b.Beam(mid, outer, capRadius * 0.045f, capRadius * 0.006f, shade, FloraPart.Bark);
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
            FloraForm.Boulder or FloraForm.Spire or FloraForm.Den or FloraForm.Log or FloraForm.Woodpile or FloraForm.IceArch => true,
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
            FloraForm.Signpost or FloraForm.Bench or FloraForm.Stump or FloraForm.Ruin => true,
            _ => false
        };

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
        /// to the passage): two posts, a lintel resting on them and short braces in the corners.
        /// </summary>
        static void MineFrame(Grower g)
        {
            var s = g.S;
            var b = g.B;
            g.Height = 1f;
            g.Width = 2.2f;
            g.TrunkRadius = 0f;
            var r = Mathf.Max(s.Thickness, 0.02f);
            var sides = g.Sides(6, 4, 4);
            for (int side = -1; side <= 1; side += 2)
            {
                var foot = new Vector3(side, -0.05f, 0f);
                var lean = g.Range(-0.02f, 0.02f);
                b.Tube(new List<Vector3> { foot, foot + new Vector3(lean, 1.02f, 0f) }, new List<float> { r * 1.05f, r }, sides, g.Bark, FloraPart.Bark);
                if (g.Lod < 2)
                    b.Tube(new List<Vector3> { new Vector3(side * 0.98f, 0.78f, 0f), new Vector3(side * 0.72f, 0.99f, 0f) },
                           new List<float> { r * 0.6f, r * 0.6f }, sides, g.Bark * 0.9f, FloraPart.Bark);
            }
            b.Tube(new List<Vector3> { new Vector3(-1.12f, 1.02f + r, 0f), new Vector3(1.12f, 1.02f + r, 0f) },
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
            // Two planks side by side, a hair apart.
            for (int k = 0; k < 2; k++)
                b.Box(new Vector3(0f, BenchSeat - 0.03f, (k - 0.5f) * 0.2f), new Vector3(1.8f, 0.06f, 0.18f),
                      Quaternion.Euler(0f, g.Range(-1f, 1f), 0f), g.Bark * g.Range(0.92f, 1.05f), FloraPart.Bark);
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
                    b.Tube(thread, fine, 3, leaf * g.Range(0.8f, 1.2f), FloraPart.Foliage);
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

        /// <summary>
        /// A glacier ice arch facing +Z: a bow of ice from one foot to the other along X, thick and splayed at the feet,
        /// thinner over the top, its thickness and line broken up ring by ring so it reads as carved by melt rather
        /// than turned, with icicles under the crown. Drawn as glass (clear ice) when the species' finish is Glass, else as
        /// rock in its colour. The legs carry colliders, so the way under it stays open.
        /// </summary>
        static void IceArch(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * Mathf.Max(s.Spread, 0.5f) * 0.5f;
            var leg = height * Mathf.Max(s.Thickness, 0.05f);
            g.Height = height;
            g.Width = half * 2f + leg * 2f;
            g.TrunkRadius = 0f;
            var part = s.Finish == FoliageFinish.Glass ? FloraPart.Glass : FloraPart.Bark;
            var lean = g.Range(-0.12f, 0.12f) * half;
            var rings = g.Lod == 0 ? 18 : g.Lod == 1 ? 11 : 7;
            var sides = g.Sides(9, 7, 5);
            var path = new List<Vector3>();
            var radii = new List<float>();
            for (int i = 0; i <= rings; i++)
            {
                var t = i / (float)rings;
                var a = t * Mathf.PI;
                // A half ellipse from foot to foot (feet a little below the ground), leaning slightly one way.
                var p = new Vector3(-Mathf.Cos(a) * half, Mathf.Sin(a) * height - 0.3f, Mathf.Sin(a) * lean);
                p += new Vector3(g.Range(-0.08f, 0.08f), g.Range(-0.06f, 0.06f), g.Range(-0.1f, 0.1f)) * leg;
                path.Add(p);
                var thick = Mathf.Lerp(1f, 0.55f, Mathf.Sin(a));
                radii.Add(leg * thick * (i == 0 || i == rings ? 1.35f : 1f) * g.Range(0.88f, 1.1f));
            }
            var first = b.VertexCount;
            b.Tube(path, radii, sides, g.FoliageColor(), part);
            // Darker underneath, where the bow shades itself.
            b.Occlude(first, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y * 0.5f + 0.6f)));
            if (g.Lod < 2)
            {
                // Icicles under the crown.
                for (int k = 0; k < 6; k++)
                {
                    var t = g.Range(0.3f, 0.7f);
                    var a = t * Mathf.PI;
                    var top = new Vector3(-Mathf.Cos(a) * half, Mathf.Sin(a) * height - 0.3f - leg * 0.45f, Mathf.Sin(a) * lean);
                    var length = leg * g.Range(0.8f, 2.2f);
                    b.Tube(new List<Vector3> { top, top + Vector3.down * length }, new List<float> { leg * 0.16f, leg * 0.02f },
                           g.Sides(5, 4, 3), g.FoliageColor(), part);
                }
            }
            foreach (var x in new[] { -half, half })
                g.Colliders.Add(new Vector4(x, 0f, leg * 1.3f, height * 0.4f));
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

        /// <summary>
        /// A fulgurite (FloraForm.Fulgurite): a knobbly tube of glass wandering up out of a crusted sand mound, tapering,
        /// with a fork or two partway up, and lumps of sand crust clinging along it.
        /// </summary>
        static void Fulgurite(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * 0.5f;
            g.TrunkRadius = height * s.Thickness * 1.4f;

            var mound = height * 0.16f;
            var first = b.VertexCount;
            b.Blob(Vector3.up * mound * 0.1f, new Vector3(mound, mound * 0.45f, mound * 0.85f), Quaternion.Euler(0f, g.R() * 360f, 0f),
                   g.Sides(2, 1, 0), 0.35f, g.R() * 100f, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, mound));

            void Branch(Vector3 root, Vector3 dir, float length, float radius, bool fork)
            {
                var points = g.Sides(7, 5, 3);
                var path = new List<Vector3>();
                var radii = new List<float>();
                var at = root;
                for (int j = 0; j < points; j++)
                {
                    var t = j / (points - 1f);
                    path.Add(at);
                    radii.Add(radius * Mathf.Lerp(1f, 0.3f, t) * g.Range(0.7f, 1.3f));
                    // Wandering, but always rising.
                    dir = (dir + g.Flat(g.R() * 6.28f) * 0.35f + Vector3.up * 0.12f).normalized;
                    at += dir * length / (points - 1);
                }
                radii[points - 1] = radius * 0.12f;
                b.Tube(path, radii, g.Sides(6, 5, 4), g.FoliageColor(), FloraPart.Foliage);
                if (g.Lod < 2)
                    for (int k = 0; k < points - 1; k++)
                        if (g.R() < 0.55f)
                            b.Blob(path[k] + g.Flat(g.R() * 6.28f) * radii[k] * 0.75f, Vector3.one * radii[k] * g.Range(0.5f, 0.9f), Quaternion.identity, 0, 0.3f,
                                   g.R() * 50f, g.Bark * g.Range(0.9f, 1.1f), FloraPart.Bark);
                if (!fork)
                    return;
                var forks = g.Count(s.Branches);
                for (int f = 0; f < forks; f++)
                {
                    var k = Mathf.Clamp(1 + (int)(g.R() * (points - 3)), 1, points - 2);
                    var along = (path[k + 1] - path[k]).normalized;
                    var side = Quaternion.AngleAxis(g.Range(20f, 40f), Vector3.Cross(along, g.Flat(g.R() * 6.28f)).normalized) * along;
                    Branch(path[k], side, length * g.Range(0.3f, 0.55f), radii[k] * 0.7f, false);
                }
            }
            Branch(Vector3.down * 0.1f, Vector3.up, height, height * s.Thickness, true);
        }

        /// <summary>
        /// An arcing spire (FloraForm.ArcSpire): a few tall, tapering prongs of dark stone (drawn metallic, so they glint)
        /// leaning out of a cracked base, with jagged electric arcs (FloraPart.Arc) between neighbouring tips that crackle
        /// in bursts and jump about, all in the shaders.
        /// </summary>
        static void ArcSpire(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = height * s.Spread * 1.2f;
            var baseRadius = height * s.Spread * 0.55f;
            g.TrunkRadius = baseRadius * 0.8f;

            var first = b.VertexCount;
            b.Blob(Vector3.up * baseRadius * 0.1f, new Vector3(baseRadius, baseRadius * 0.4f, baseRadius * 0.9f), Quaternion.Euler(0f, g.R() * 360f, 0f),
                   g.Sides(2, 1, 0), 0.35f, g.R() * 100f, g.Bark * 0.8f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, baseRadius));

            var prongs = Mathf.Max(2, g.Count(s.Branches));
            var tips = new List<Vector3>();
            var start = g.R() * 6.28f;
            for (int k = 0; k < prongs; k++)
            {
                var a = start + k * 6.28f / prongs + g.Range(-0.3f, 0.3f);
                var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * baseRadius * 0.45f;
                var tall = height * (k == 0 ? 1f : g.Range(0.65f, 0.9f));
                var lean = new Vector3(foot.x, 0f, foot.z).normalized * tall * g.Range(0.08f, 0.2f);
                var tip = foot + lean + Vector3.up * tall;
                var r = tall * s.Thickness;
                b.Tube(new List<Vector3> { foot + Vector3.down * 0.3f, Vector3.Lerp(foot, tip, 0.45f) + lean * 0.2f, Vector3.Lerp(foot, tip, 0.8f), tip },
                       new List<float> { r, r * 0.75f, r * 0.4f, r * 0.05f }, g.Sides(6, 5, 4), Color.Lerp(g.Bark, g.Bark * 1.3f, g.R()), FloraPart.Metal);
                tips.Add(tip);
            }
            g.Colliders.Add(new Vector4(0f, 0f, baseRadius * 0.6f, height));
            if (g.Lod == 2)
                return;
            var arc = g.HasAccent ? AccentAt(g, 0) : new Color(0.65f, 0.85f, 1f);
            for (int k = 0; k < tips.Count; k++)
            {
                var a = tips[k] + Vector3.down * 0.15f;
                var c = tips[(k + 1) % tips.Count] + Vector3.down * 0.15f;
                var path = new List<Vector3>();
                var radii = new List<float>();
                var points = g.Sides(9, 5, 3);
                for (int j = 0; j < points; j++)
                {
                    var t = j / (points - 1f);
                    var zig = j == 0 || j == points - 1 ? Vector3.zero : (new Vector3(g.R(), g.R(), g.R()) - Vector3.one * 0.5f) * Vector3.Distance(a, c) * 0.18f;
                    path.Add(Vector3.Lerp(a, c, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * Vector3.Distance(a, c) * 0.12f + zig);
                    radii.Add(0.03f);
                }
                b.Tube(path, radii, 3, arc, FloraPart.Arc);
            }
        }

        /// <summary>
        /// Floating debris (FloraForm.Debris): lumps of dark rock hovering over a cracked mound, the cluster slowly orbiting
        /// it and each lump bobbing (FloraMeshBuilder.Orbit). No collision.
        /// </summary>
        static void Debris(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            var ring = height * s.Spread;
            g.Width = ring * 2.2f;
            g.TrunkRadius = 0f;

            var mound = ring * 0.35f;
            var first = b.VertexCount;
            b.Blob(Vector3.up * mound * 0.05f, new Vector3(mound, mound * 0.3f, mound * 0.9f), Quaternion.Euler(0f, g.R() * 360f, 0f),
                   g.Sides(2, 1, 0), 0.4f, g.R() * 100f, g.Bark * 0.7f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, mound));

            var floating = b.VertexCount;
            var pieces = g.Count(s.Branches);
            for (int k = 0; k < pieces; k++)
            {
                var a = g.R() * 6.28f;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * g.Range(0.3f, 1f) + Vector3.up * height * g.Range(0.3f, 1f);
                var size = height * g.Range(0.05f, 0.16f);
                b.Blob(at, new Vector3(size, size * g.Range(0.6f, 0.9f), size * g.Range(0.7f, 1f)), Quaternion.Euler(g.R() * 360f, g.R() * 360f, g.R() * 360f),
                       g.Sides(1, 1, 0), 0.3f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 1.4f, g.R()), FloraPart.Metal, 1f, 0.5f);
            }
            b.Orbit(floating, height * 0.06f);
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
            var wall = height * 0.55f;
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
            g.Perches.Add(Vector3.up * height);

            // A door facing +Z in a shallow porch whose sides run back into the curved wall, and a couple of windows.
            // The door stands out past the wall's slight bulge, so the curve doesn't poke through it.
            var doorWidth = Mathf.Min(radius * 0.5f, 1.25f);
            Porch(g, radius * 1.05f, WallBehind(radius, doorWidth), doorWidth, Mathf.Min(wall * 0.8f, HumanDoor), 0.4f, g.Bark, roofColor, FloraPart.Bark);
            // On the wall itself: its radius at that height (between the bulge and the top).
            Windows(g, radius * Mathf.Lerp(1.02f, 0.98f, 0.16f), wall * 0.58f, 4, 0.55f, 40f, sides);
        }


        /// <summary>
        /// A door's height for human folk (about 1.3 times a 1.85 m person, so there's headroom); buildings are drawn
        /// for humans and each settlement scales them by its folk's size, so dwarf and goblin doors come out lower and
        /// ogre doors higher.
        /// </summary>
        const float HumanDoor = 2.4f;

        /// <summary>
        /// How far in front of its origin a building's door stands (along +Z, unscaled, at its mean height), or -1 for
        /// forms without one or where it isn't known. The same sums as the builders (Hut, StoneHouse, Tower, Inn, Hall), so
        /// a Door anchor can stand at the door itself (AnchorBaking); variants differ from it by their height's spread.
        /// </summary>
        public static float DoorFront(FloraSpecies s)
        {
            var half = (s.Height.x + s.Height.y) * 0.5f * s.Spread * 0.5f;
            return s.Form switch
            {
                FloraForm.Hut => half * 1.05f,
                FloraForm.StoneHouse => half + 0.02f,
                FloraForm.Tower => half * 1.38f + 0.05f,
                FloraForm.Inn => half * 0.8f + 0.02f,
                FloraForm.Hall => half * 0.55f + 0.04f,
                _ => -1f
            };
        }

        /// <summary>A doorway in a wall facing +Z, at distance <paramref name="front"/> (see Door).</summary>
        static void Doorway(Grower g, float front, float width, float height) =>
            Door(g, new Vector3(0f, 0f, front), Quaternion.identity, width, height);

        /// <summary>
        /// A door standing in a wall, its foot at <paramref name="at"/>, facing out along <paramref name="facing"/>'s +Z:
        /// a timber frame (two jambs and a lintel standing proud of the wall), a plank door set back in it with a
        /// handle and iron straps, and a stone step. Far away, just the door's dark rectangle.
        /// </summary>
        static void Door(Grower g, Vector3 at, Quaternion facing, float width, float height)
        {
            var b = g.B;
            g.DoorHeight = Mathf.Max(g.DoorHeight, height);
            var wood = new Color(0.3f, 0.2f, 0.12f) * g.Range(0.85f, 1.15f);
            var frame = g.Bark * 0.55f;
            Vector3 P(float x, float y, float z) => at + facing * new Vector3(x, y, z);
            if (g.Lod == 2)
            {
                b.Box(P(0f, height * 0.5f, 0.02f), new Vector3(width, height, 0.06f), facing, wood * 0.6f, FloraPart.Bark);
                return;
            }
            var post = Mathf.Clamp(width * 0.12f, 0.1f, 0.18f);
            // The leaf: vertical planks with thin gaps, set back a little in the frame, hung on its left edge.
            var leaf = b.VertexCount;
            var planks = 4;
            var plank = (width - 0.03f * (planks - 1)) / planks;
            for (int k = 0; k < planks; k++)
            {
                var x = -width * 0.5f + plank * 0.5f + k * (plank + 0.03f);
                b.Box(P(x, height * 0.5f, -0.02f), new Vector3(plank, height, 0.08f), facing, wood * g.Range(0.9f, 1.1f), FloraPart.Bark);
            }
            // Two iron straps and a handle, on the leaf.
            if (g.Lod == 0)
            {
                foreach (var y in new[] { height * 0.22f, height * 0.78f })
                    b.Box(P(0f, y, 0.03f), new Vector3(width * 0.92f, 0.07f, 0.02f), facing, new Color(0.12f, 0.12f, 0.13f), FloraPart.Bark);
                b.Box(P(width * 0.3f, height * 0.48f, 0.06f), new Vector3(0.05f, 0.14f, 0.05f), facing, new Color(0.15f, 0.14f, 0.13f), FloraPart.Bark);
            }
            b.Hinge(leaf, P(-width * 0.5f, 0f, 0f), facing * Vector3.right, facing.eulerAngles.y * Mathf.Deg2Rad);
            // The dark doorway behind the leaf (what shows when it swings open).
            b.Box(P(0f, height * 0.5f, -0.08f), new Vector3(width, height, 0.04f), facing, new Color(0.03f, 0.025f, 0.02f), FloraPart.Bark, 0.4f);
            // Frame: jambs and a lintel standing proud of the wall.
            foreach (var side in new[] { -1f, 1f })
                b.Box(P(side * (width * 0.5f + post * 0.5f), height * 0.5f + 0.05f, 0.04f), new Vector3(post, height + 0.1f, 0.2f), facing, frame, FloraPart.Bark);
            b.Box(P(0f, height + post * 0.5f, 0.05f), new Vector3(width + post * 2.6f, post, 0.24f), facing, frame, FloraPart.Bark);
            // A stone step.
            b.Box(P(0f, 0.06f, 0.32f), new Vector3(width + post * 2f, 0.16f, 0.5f), facing, new Color(0.45f, 0.44f, 0.41f) * g.Range(0.9f, 1.1f), FloraPart.Bark);
        }

        /// <summary>
        /// Where a round wall of the given radius is at the edges of a door of that width (plus its frame): the
        /// curve falls back from the door's middle, so anything meant to meet the wall starts here, or behind it.
        /// </summary>
        static float WallBehind(float radius, float doorWidth)
        {
            var half = doorWidth * 0.5f + 0.35f;
            return Mathf.Sqrt(Mathf.Max(radius * radius - half * half, 0f)) - 0.12f;
        }

        /// <summary>
        /// A door in a shallow entrance porch facing +Z, for round walls: the door (see Door) stands at
        /// <paramref name="doorZ"/>, and two side cheeks and a lintel in the wall's colour run from
        /// <paramref name="backZ"/> (inside the wall, see WallBehind) to <paramref name="depth"/> in front of the door,
        /// under a small roof. However the wall curves behind it, no gap shows at the sides.
        /// </summary>
        static void Porch(Grower g, float doorZ, float backZ, float width, float height, float depth, Color wall, Color roof, FloraPart roofPart)
        {
            var b = g.B;
            Door(g, new Vector3(0f, 0f, doorZ), Quaternion.identity, width, height);
            var post = Mathf.Clamp(width * 0.12f, 0.1f, 0.18f);
            var cheek = 0.16f;
            var front = doorZ + depth;
            var length = front - backZ;
            var mid = (front + backZ) * 0.5f;
            var top = height + post + 0.08f;
            var inner = width * 0.5f + post;
            foreach (var side in new[] { -1f, 1f })
                b.Box(new Vector3(side * (inner + cheek * 0.5f), (top - 0.1f) * 0.5f, mid), new Vector3(cheek, top + 0.1f, length), Quaternion.identity, wall, FloraPart.Bark);
            b.Box(new Vector3(0f, top + 0.1f, mid), new Vector3((inner + cheek) * 2f, 0.2f, length), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            if (g.Lod < 2)
            {
                // A small roof, sloping out over the porch.
                var tilt = Quaternion.Euler(14f, 0f, 0f);
                b.Box(new Vector3(0f, top + 0.3f, mid + 0.12f), new Vector3((inner + cheek) * 2f + 0.3f, 0.1f, length + 0.35f), tilt, roof, roofPart);
            }
        }

        /// <summary>
        /// Windows round a round wall of this radius (a tube of <paramref name="sides"/>) at a height: a timber frame
        /// with a sill and a cross bar, round a pane in the accent colour (FloraPart.Window: dark glass by day, lit at
        /// night). The frame reaches back into the wall past both its curve across the window and its facets, so it
        /// reads as set into the wall from any side, never as a plane floating off it.
        /// </summary>
        static void Windows(Grower g, float radius, float y, int count, float size, float skipAngle, int sides)
        {
            var reach = size * 0.5f + 0.1f;
            var sag = radius - Mathf.Sqrt(Mathf.Max(radius * radius - reach * reach, 0f)) + radius * (1f - Mathf.Cos(Mathf.PI / sides));
            if (!g.HasAccent || g.Lod == 2)
                return;
            for (int k = 0; k < count; k++)
            {
                var a = (k + 0.5f) / count * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < skipAngle)
                    continue;   // keep clear of the door (+Z is 90° here)
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Window(g, dir * (radius + 0.02f) + Vector3.up * y, Quaternion.LookRotation(dir), size, size * 1.25f, null, sag);
            }
        }

        /// <summary>
        /// One window facing out along <paramref name="facing"/>'s +Z, centred at <paramref name="at"/>: the pane, set
        /// back 8 cm behind the front of its frame (jambs, lintel and a projecting sill), whose back edge runs
        /// <paramref name="sag"/> plus 12 cm behind the pane, into the wall (curved walls pass how far the wall falls
        /// away across the window, so no gap shows at its sides).
        /// </summary>
        static void Window(Grower g, Vector3 at, Quaternion facing, float width, float height, Color? light = null, float sag = 0f)
        {
            var b = g.B;
            var frame = g.Bark * 0.55f;
            // Each pane's colour a touch different: the shader keys its own lighting-up hour on it.
            b.Box(at, new Vector3(width, height, 0.06f), facing, (light ?? g.Accent) * g.Range(0.96f, 1.04f), FloraPart.Window);
            if (g.Lod == 2)
                return;
            var bar = Mathf.Clamp(width * 0.1f, 0.04f, 0.08f);
            const float front = 0.08f;
            var back = sag + 0.12f;
            var mid = (front - back) * 0.5f;
            Vector3 P(float x, float y, float z) => at + facing * new Vector3(x, y, z);
            foreach (var side in new[] { -1f, 1f })
            {
                b.Box(P(side * (width + bar) * 0.5f, 0f, mid), new Vector3(bar, height + bar * 2f, front + back), facing, frame, FloraPart.Bark);
                b.Box(P(0f, side * (height + bar) * 0.5f, mid), new Vector3(width + bar * 2f, bar, front + back), facing, frame, FloraPart.Bark);
            }
            if (g.Lod > 0)
                return;
            b.Box(P(0f, 0f, 0.035f), new Vector3(bar * 0.6f, height, 0.04f), facing, frame, FloraPart.Bark);
            // The sill sticks out past the frame and runs back into the wall with it.
            b.Box(P(0f, -height * 0.5f - bar * 1.4f, (front + 0.08f - back) * 0.5f), new Vector3(width + bar * 3f, bar * 0.8f, front + 0.08f + back), facing, frame, FloraPart.Bark);
        }

        static void StoneHouse(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;      // half the wall's width
            var wall = height * 0.65f;
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

            // Wall detail: dressed quoins up each corner (alternating long and short blocks), a plinth course round
            // the foot, and a string course under the eaves.
            if (g.Lod < 2)
            {
                var dressed = g.Bark * 1.18f;
                var course = 0.42f;
                for (int c = 0; c < 4; c++)
                {
                    var cx = (c & 1) == 0 ? -half : half;
                    var cz = (c & 2) == 0 ? -half : half;
                    for (int k = 0; (k + 0.5f) * course < wall - 0.2f; k++)
                    {
                        var longOnX = k % 2 == 0;
                        var size = new Vector3(longOnX ? 0.7f : 0.35f, course * 0.92f, longOnX ? 0.35f : 0.7f);
                        var at = new Vector3(cx - Mathf.Sign(cx) * (size.x * 0.5f - 0.04f), (k + 0.5f) * course, cz - Mathf.Sign(cz) * (size.z * 0.5f - 0.04f));
                        b.Box(at, size, Quaternion.identity, dressed * g.Range(0.92f, 1.08f), FloraPart.Bark);
                    }
                }
                b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(half * 2f + 0.14f, 0.4f, half * 2f + 0.14f), Quaternion.identity, g.Bark * 0.72f, FloraPart.Bark);
                b.Box(new Vector3(0f, wall - 0.15f, 0f), new Vector3(half * 2f + 0.12f, 0.16f, half * 2f + 0.12f), Quaternion.identity, dressed, FloraPart.Bark);
            }

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
            g.Perches.Add(Vector3.up * height);
            g.Perches.Add(chimney + Vector3.up * (height + 0.5f));

            Doorway(g, half + 0.02f, Mathf.Min(half * 0.6f, 1.25f), Mathf.Min(wall * 0.8f, HumanDoor));
            // A window in the middle of each side wall and the back (the four-way Windows would put them on corners).
            if (g.HasAccent && g.Lod < 2)
                foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.back })
                    Window(g, dir * (half + 0.03f) + Vector3.up * wall * 0.55f, Quaternion.LookRotation(dir), 0.6f, 0.75f);
            // ...and one either side of the door.
            if (g.HasAccent && g.Lod < 2 && half > 1.6f)
                foreach (var x in new[] { -1f, 1f })
                    Window(g, new Vector3(x * half * 0.62f, wall * 0.55f, half + 0.03f), Quaternion.identity, 0.6f, 0.75f);
            // Collision: a square, not the circle round its middle, so its corners stop you too.
            for (int k = 0; k < 4; k++)
                g.Colliders.Add(new Vector4((k & 1) == 0 ? -half * 0.5f : half * 0.5f, (k & 2) == 0 ? -half * 0.5f : half * 0.5f, half * 0.55f, wall));
        }

        /// <summary>
        /// A ruined stone house: four walls of stone piers, each wall's tops sloping down towards one end (the way walls
        /// fall, from a corner) with a ragged edge, one wall nearly gone, a gap for the door at +Z, a window hole in a
        /// tall side wall, moss on some broken tops and fallen stones round the foot. No roof.
        /// </summary>
        static void Ruin(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 1.1f;
            const float thick = 0.55f;
            var pier = g.Sides(1, 2, 4) * 0.55f;      // finer piers up close, so the broken edge reads as stones
            var doorHalf = Mathf.Min(half * 0.3f, 0.7f);
            var gone = g.Variant % 4;                   // the wall that has nearly all fallen

            var first = b.VertexCount;
            for (int w = 0; w < 4; w++)
            {
                var perch = Vector3.zero;
                var facing = Quaternion.Euler(0f, w * 90f, 0f);     // wall 0 faces +Z, the front
                var keep = w == gone ? g.Range(0.06f, 0.2f) : g.Range(0.45f, 1f);
                var length = half * 2f + thick;
                var n = Mathf.Max(2, Mathf.RoundToInt(length / pier));
                var step = length / n;
                var fromLeft = g.R() < 0.5f;
                for (int i = 0; i < n; i++)
                {
                    var x = -length * 0.5f + (i + 0.5f) * step;
                    if (w == 0 && Mathf.Abs(x) < doorHalf)
                        continue;
                    var u = (i + 0.5f) / n;
                    var top = Mathf.Max(0.25f, height * keep * Mathf.Lerp(0.3f, 1f, fromLeft ? u : 1f - u) * g.Range(0.75f, 1.05f));
                    var colour = Color.Lerp(g.Bark, g.Bark * 1.15f, g.R());
                    // A window hole: a pier in the middle of a tall side wall, split round the opening.
                    if (w != 0 && i == n / 2 && top > 2.3f && g.Lod < 2)
                    {
                        b.Box(facing * new Vector3(x, 0.4f, half), new Vector3(step * 1.02f, 1.1f, thick), facing, colour, FloraPart.Bark);
                        b.Box(facing * new Vector3(x, (1.85f + top) * 0.5f, half), new Vector3(step * 1.02f, top - 1.85f, thick), facing, colour, FloraPart.Bark);
                    }
                    else
                        b.Box(facing * new Vector3(x, top * 0.5f - 0.15f, half), new Vector3(step * 1.02f, top + 0.3f, thick), facing, colour, FloraPart.Bark);
                    if (g.HasAccent && g.Lod < 2 && g.R() < s.AccentAmount)
                        b.Box(facing * new Vector3(x, top + 0.03f, half), new Vector3(step * 0.95f, 0.07f, thick * 1.06f), facing, g.Accent, FloraPart.Foliage);
                    if (top > 0.7f)
                    {
                        var c = facing * new Vector3(x, 0f, half);
                        g.Colliders.Add(new Vector4(c.x, c.z, Mathf.Max(step, thick) * 0.6f, top));
                    }
                    if (top > perch.y)
                        perch = facing * new Vector3(x, top, half);
                }
                if (perch.y > 1f)
                    g.Perches.Add(perch);
            }
            b.Recolor(first, (p, c) => p.y < 0.5f ? c * 0.8f : c);   // darker footing
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.5f));

            // Fallen stones, most of them below the wall that came down.
            if (g.Lod < 2)
            {
                var rubble = b.VertexCount;
                var fallen = Quaternion.Euler(0f, gone * 90f, 0f);
                var stones = g.Count(s.Branches);
                for (int k = 0; k < stones; k++)
                {
                    var size = g.Range(0.2f, 0.5f);
                    var at = k % 3 != 0
                        ? fallen * new Vector3(g.Range(-half, half), 0f, half + g.Range(-0.8f, 1.6f))
                        : new Vector3(g.Range(-half, half) * 1.3f, 0f, g.Range(-half, half) * 1.3f);
                    at.y = size * 0.25f;
                    b.Box(at, new Vector3(size * g.Range(1f, 1.8f), size, size * g.Range(0.8f, 1.2f)),
                          Quaternion.Euler(g.Range(-15f, 15f), g.R() * 360f, g.Range(-15f, 15f)), Color.Lerp(g.Bark * 0.85f, g.Bark * 1.1f, g.R()), FloraPart.Bark);
                }
                b.Occlude(rubble, (p, n) => GroundOcclusion(p, 0.5f));
            }
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
                // On the wall at the window's own height (it tapers).
                var wy = y + 0.7f;
                var wr = wy < walls * 0.6f ? Mathf.Lerp(radius * 1.05f, radius * 0.9f, Mathf.InverseLerp(walls * 0.15f, walls * 0.6f, wy))
                                           : Mathf.Lerp(radius * 0.9f, radius * 0.85f, Mathf.InverseLerp(walls * 0.6f, walls, wy));
                Windows(g, wr, wy, 5, 0.35f, f == 1 ? 30f : -1f, sides);
            }

            // A tall, slightly flared roof.
            var roof = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.up * (walls - 0.05f), Vector3.up * (walls + (height - walls) * 0.25f), Vector3.up * height },
                   new List<float> { radius * 1.25f, radius * 0.7f, 0.02f }, sides, g.FoliageColor(), FloraPart.Foliage, cap: true);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            g.Perches.Add(Vector3.up * height);

            // The door in a porch standing out past the flared foot (1.35 × radius at the ground), so the flare tucks
            // in behind the porch's sides and nothing crosses the opening; the sides run back into the wall above it.
            var doorWidth = Mathf.Min(radius * 0.7f, 1.25f);
            Porch(g, radius * 1.38f + 0.05f, WallBehind(radius * 0.9f, doorWidth), doorWidth, Mathf.Min(walls * 0.4f, HumanDoor), 0.35f,
                  g.Bark, g.FoliageColor(), FloraPart.Foliage);
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
                var postTop = p + Vector3.up * height * g.Range(0.95f, 1.08f);
                b.Tube(new List<Vector3> { p + Vector3.down * 0.1f, postTop },
                       new List<float> { postRadius, postRadius * 0.9f }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark, cap: true);
                if (k % 2 == 0)
                    g.Perches.Add(postTop);
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
            // The rails stop the player (all but the gate).
            for (int k = 0; k < posts; k++)
            {
                var p = tops[k];
                var q = tops[(k + 1) % posts];
                if ((p + q).z * 0.5f <= radius * 0.85f)
                    FenceColliders(g, p, q, height);
            }
        }

        /// <summary>
        /// Colliders along a fence line from a to b: small upright cylinders about 0.9 m apart, close enough that the
        /// player (whose own radius adds to theirs) can't slip between them, even sprinting at an angle.
        /// </summary>
        static void FenceColliders(Grower g, Vector3 a, Vector3 b, float height)
        {
            var count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.9f));
            for (int k = 0; k <= count; k++)
            {
                var p = Vector3.Lerp(a, b, k / (float)count);
                g.Colliders.Add(new Vector4(p.x, p.z, 0.25f, height));
            }
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
            // Cloth held at both bars, so it only ripples between them.
            var stripes = g.Sides(6, 4, 2);
            var awning = b.VertexCount;
            for (int k = 0; k < stripes; k++)
            {
                var x = k / (float)stripes - 0.5f;
                var colour = k % 2 == 0 ? g.FoliageColor() : AccentAt(g, 0);
                b.Sheet(back + Vector3.right * x * width * 1.1f, Vector3.right * width * 1.1f / stripes, front - back, 1, g.Sides(5, 3, 1), colour, FloraPart.Bark);
            }
            b.Cloth(awning, p => 0.3f * Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(back.z, front.z, p.z)) * Mathf.PI));
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
            g.Perches.Add(new Vector3(-half - 0.2f, ridge + 0.19f, 0f));
            g.Perches.Add(new Vector3(half + 0.2f, ridge + 0.19f, 0f));
            g.Perches.Add(new Vector3(half * 0.6f, height * 1.1f, -front * 0.35f));

            // Door, windows (lit at night) and the porch out front.
            Doorway(g, front + 0.02f, 1.25f, Mathf.Min(wall * 0.42f, HumanDoor));
            if (g.Lod < 2)
                foreach (var (x, y) in new[] { (-0.55f, 0.27f), (0.55f, 0.27f), (-0.55f, 0.75f), (0f, 0.75f), (0.55f, 0.75f) })
                    Window(g, new Vector3(x * half, y * wall, front + 0.03f), Quaternion.identity, 0.6f, 0.75f);
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

        /// <summary>
        /// A leader's hall, its door facing +Z: a long timber hall on a stone plinth, corner and wall posts, a steep
        /// gabled roof with its ridge along X and crossed beams at each gable, a wide door up two steps, lit windows (first
        /// accent) along the front and back, and two banners (second accent) flanking the door. Collision covers its
        /// length.
        /// </summary>
        static void Hall(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;      // half its length (along X)
            var front = half * 0.55f;                 // half its depth (along Z)
            var plinth = 0.6f;
            var wall = plinth + height * 0.38f;
            g.Height = height;
            g.Width = half * 2.4f;
            g.TrunkRadius = front;
            var timber = g.Bark * 0.5f;
            var stone = Stone(g) * g.Range(0.9f, 1.1f);

            // Stone plinth, then timber walls with posts.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, plinth * 0.5f - 0.15f, 0f), new Vector3(half * 2f + 0.4f, plinth + 0.3f, front * 2f + 0.4f), Quaternion.identity, stone, FloraPart.Bark);
            b.Box(new Vector3(0f, (plinth + wall) * 0.5f, 0f), new Vector3(half * 2f, wall - plinth, front * 2f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.4f));
            if (g.Lod < 2)
            {
                var posts = Mathf.Max(2, Mathf.RoundToInt(half / 1.6f));
                for (int k = 0; k <= posts; k++)
                {
                    var x = Mathf.Lerp(-half, half, k / (float)posts);
                    foreach (var z in new[] { -1f, 1f })
                        b.Box(new Vector3(x, (plinth + wall) * 0.5f, z * (front + 0.06f)), new Vector3(0.28f, wall - plinth, 0.2f), Quaternion.identity, timber, FloraPart.Bark);
                }
                b.Box(new Vector3(0f, wall - 0.12f, 0f), new Vector3(half * 2f + 0.12f, 0.25f, front * 2f + 0.12f), Quaternion.identity, timber, FloraPart.Bark);
            }

            // Steep gabled roof along X, overhanging; gable ends filled; crossed beams rising past the ridge at each end.
            var roof = g.FoliageColor();
            var ridge = height;
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, wall - 0.35f, z * (front + 0.9f)), new Vector3(0f, ridge + 0.1f, 0f), half * 2f + 1.2f, 0.22f, roof, FloraPart.Bark);
            foreach (var x in new[] { -1f, 1f })
            {
                var v0 = b.Vertex(new Vector3(x * half, wall, -front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v1 = b.Vertex(new Vector3(x * half, wall, front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v2 = b.Vertex(new Vector3(x * half, ridge, 0f), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                b.Triangle(v0, v1, v2);
                if (g.Lod < 2)
                    foreach (var z in new[] { -1f, 1f })
                        b.Beam(new Vector3(x * (half + 0.6f), wall + (ridge - wall) * 0.55f, z * front * 0.55f), new Vector3(x * (half + 0.6f), ridge + 1.1f, -z * 0.5f),
                               0.22f, 0.18f, timber, FloraPart.Bark);
                g.Perches.Add(new Vector3(x * half * 0.9f, ridge + 0.21f, 0f));
                g.Perches.Add(new Vector3(x * (half + 0.6f), ridge + 1.19f, 0.5f));
            }

            // A wide door up two steps, windows along both long walls, banners either side of the door.
            Door(g, new Vector3(0f, plinth - 0.05f, front + 0.04f), Quaternion.identity, 1.8f, Mathf.Min(wall - plinth - 0.4f, HumanDoor * 1.15f));
            b.Box(new Vector3(0f, plinth * 0.29f, front + 0.75f), new Vector3(3.2f, plinth * 0.75f, 0.8f), Quaternion.identity, stone, FloraPart.Bark);
            var light = AccentAt(g, 0);
            if (g.Lod < 2)
            {
                var windows = Mathf.Max(2, Mathf.RoundToInt(half / 2.2f));
                for (int k = 0; k < windows; k++)
                {
                    var x = Mathf.Lerp(-half * 0.75f, half * 0.75f, (k + 0.5f) / windows);
                    if (Mathf.Abs(x) < 1.6f)
                        continue;   // clear of the door
                    var y = plinth + (wall - plinth) * 0.55f;
                    Window(g, new Vector3(x, y, front + 0.09f), Quaternion.identity, 0.7f, 1f, light);
                    Window(g, new Vector3(x, y, -front - 0.09f), Quaternion.LookRotation(Vector3.back), 0.7f, 1f, light);
                }
                foreach (var x in new[] { -2.2f, 2.2f })
                {
                    var foot = new Vector3(x, 0f, front + 1.4f);
                    b.Tube(new List<Vector3> { foot + Vector3.down * 0.2f, foot + Vector3.up * (wall + 1.2f) }, new List<float> { 0.08f, 0.06f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
                    g.Perches.Add(foot + Vector3.up * (wall + 1.2f));
                    // A banner hung from a short bar near the top of the pole, free to wave below it.
                    var top = wall + 1.1f;
                    var banner = b.VertexCount;
                    b.Sheet(foot + new Vector3(x * 0.12f - 0.45f, top, 0f), Vector3.right * 0.9f, Vector3.down * 1.8f, g.Sides(4, 2, 1), g.Sides(6, 3, 1), AccentAt(g, 1), FloraPart.Bark);
                    b.Cloth(banner, p => (foot.y + top - p.y) / 1.8f);
                    b.Box(foot + new Vector3(x * 0.12f, top, 0f), new Vector3(1.0f, 0.05f, 0.05f), Quaternion.identity, timber, FloraPart.Bark);
                }
            }

            // Collision: a row of cylinders along its length.
            var count = Mathf.Max(2, Mathf.CeilToInt(half / front));
            for (int k = 0; k < count; k++)
                g.Colliders.Add(new Vector4(Mathf.Lerp(-half + front * 0.9f, half - front * 0.9f, count == 1 ? 0.5f : k / (count - 1f)), 0f, front * 1.05f, wall));
        }

        /// <summary>
        /// A shrine, facing +Z: a stepped stone platform, four posts carrying a small pyramid roof, and an altar under it
        /// with a stone that glows in the accent colour, a pair of offering bowls in front.
        /// </summary>
        static void Shrine(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = half * 2.6f;
            g.TrunkRadius = half * 0.5f;
            var stone = Stone(g) * g.Range(0.95f, 1.15f);
            var timber = g.Bark * 0.6f;

            // Two stone steps up to the platform.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(half * 2.4f, 0.45f, half * 2.4f), Quaternion.identity, stone * 0.92f, FloraPart.Bark);
            b.Box(new Vector3(0f, 0.38f, 0f), new Vector3(half * 2f, 0.25f, half * 2f), Quaternion.identity, stone, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.5f));
            var floor = 0.5f;
            var eave = floor + height * 0.55f;
            // Posts and the roof.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Tube(new List<Vector3> { new Vector3(x * half * 0.82f, floor, z * half * 0.82f), new Vector3(x * half * 0.82f, eave, z * half * 0.82f) },
                       new List<float> { 0.12f, 0.1f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
            var roof = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.up * (eave - 0.1f), Vector3.up * height }, new List<float> { half * 1.45f, 0.05f }, 4, g.FoliageColor(), FloraPart.Bark, cap: true);
            RotateFrom(b, roof, Quaternion.Euler(0f, 45f, 0f));
            g.Perches.Add(Vector3.up * height);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            // The altar and its glowing stone.
            b.Box(new Vector3(0f, floor + 0.45f, 0f), new Vector3(half * 0.9f, 0.9f, half * 0.6f), Quaternion.identity, stone * 1.1f, FloraPart.Bark);
            b.Blob(new Vector3(0f, floor + 1.1f, 0f), new Vector3(0.22f, 0.26f, 0.22f), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.15f, 3f, g.Accent, FloraPart.Glow);
            if (g.Lod == 0)
                foreach (var x in new[] { -0.45f, 0.45f })
                    b.Tube(new List<Vector3> { new Vector3(x, floor + 0.02f, half * 0.55f), new Vector3(x, floor + 0.14f, half * 0.55f) },
                           new List<float> { 0.1f, 0.14f }, 6, stone * 0.8f, FloraPart.Bark, cap: true);
        }

        /// <summary>
        /// A garden, its gate facing +Z: a low wattle fence (posts and two rails) round a square with a gap at the front,
        /// rows of vegetables across it (leafy clumps in the foliage colours), and a few flowers in the accents.
        /// </summary>
        static void Garden(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var fence = g.Range(s.Height);
            var half = fence * s.Spread * 0.5f;
            g.Height = fence;
            g.Width = half * 2f;
            g.TrunkRadius = 0f;
            var wood = g.Bark;

            // The fence: posts round the square, rails between them, a gap in the middle of the front.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                g.Perches.Add(new Vector3(x * half, fence * 0.925f, z * half));
            if (g.Lod < 2)
            {
                var posts = Mathf.Max(3, Mathf.RoundToInt(half / 0.9f));
                for (int side = 0; side < 4; side++)
                {
                    var rot = Quaternion.Euler(0f, side * 90f, 0f);
                    for (int k = 0; k <= posts; k++)
                    {
                        var x = Mathf.Lerp(-half, half, k / (float)posts);
                        if (side == 0 && Mathf.Abs(x) < 0.7f)
                            continue;   // the gate gap
                        b.Box(rot * new Vector3(x, fence * 0.45f, half), new Vector3(0.08f, fence * 0.95f, 0.08f), rot, wood * g.Range(0.85f, 1.1f), FloraPart.Bark);
                    }
                    if (g.Lod == 0)
                    {
                        // The fence stops the player, except at the gate.
                        if (side == 0)
                            foreach (var dir in new[] { -1f, 1f })
                                FenceColliders(g, rot * new Vector3(dir * half, 0f, half), rot * new Vector3(dir * 0.75f, 0f, half), fence);
                        else
                            FenceColliders(g, rot * new Vector3(-half, 0f, half), rot * new Vector3(half, 0f, half), fence);
                    }
                    foreach (var y in new[] { fence * 0.35f, fence * 0.75f })
                    {
                        if (side == 0)
                        {
                            foreach (var dir in new[] { -1f, 1f })
                                b.Box(rot * new Vector3(dir * (half + 0.7f) * 0.5f, y, half), new Vector3(half - 0.7f, 0.05f, 0.05f), rot, wood, FloraPart.Bark);
                        }
                        else
                            b.Box(rot * new Vector3(0f, y, half), new Vector3(half * 2f, 0.05f, 0.05f), rot, wood, FloraPart.Bark);
                    }
                }
            }

            // Rows of vegetables, running front to back, with a path down the middle from the gate.
            var rows = Mathf.Max(2, Mathf.RoundToInt(half / 0.7f));
            for (int r = 0; r < rows; r++)
            {
                var x = Mathf.Lerp(-half + 0.5f, half - 0.5f, r / (rows - 1f));
                if (Mathf.Abs(x) < 0.45f)
                    continue;
                var count = Mathf.Max(2, Mathf.RoundToInt((half * 2f - 1f) / (g.Lod == 2 ? 1.4f : 0.6f)));
                for (int k = 0; k < count; k++)
                {
                    var z = Mathf.Lerp(-half + 0.5f, half - 0.6f, k / (count - 1f));
                    var size = g.Range(0.18f, 0.3f);
                    var color = g.FoliageColor();
                    if (g.HasAccent && g.R() < s.AccentAmount * 0.2f)
                        color = g.Accent;
                    b.Blob(new Vector3(x + g.Range(-0.06f, 0.06f), size * 0.6f, z), new Vector3(size, size * 0.8f, size), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.3f, g.R() * 50f, color, FloraPart.Foliage);
                }
            }
        }

        /// <summary>
        /// A big folk's lodge, its door facing +Z: crossed pole pairs along its length carrying a ridge pole, hides
        /// sagging between them down to the ground (patchy, each hide its own shade), a hide flap over the door between two
        /// great tusks, and skulls on the ridge ends. Low and wide; collision along its length.
        /// </summary>
        static void Lodge(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;     // half its length, along X
            var foot = height * 0.95f;               // half its width at the ground, along Z
            g.Height = height;
            g.Width = half * 2.4f;
            g.TrunkRadius = foot * 0.7f;
            var pole = g.Bark;
            var bays = Mathf.Max(3, Mathf.RoundToInt(half / 1.8f));

            // Pole pairs crossing above the ridge, and the ridge pole.
            for (int k = 0; k <= bays; k++)
            {
                var x = Mathf.Lerp(-half, half, k / (float)bays) + g.Range(-0.15f, 0.15f);
                foreach (var z in new[] { -1f, 1f })
                    b.Tube(new List<Vector3> { new Vector3(x, -0.2f, z * foot), new Vector3(x, height, 0f), new Vector3(x, height + 0.7f, -z * 0.45f) },
                           new List<float> { 0.16f, 0.12f, 0.08f }, g.Sides(6, 4, 3), pole * g.Range(0.85f, 1.1f), FloraPart.Bark);
            }
            b.Tube(new List<Vector3> { new Vector3(-half - 0.6f, height - 0.05f, 0f), new Vector3(half + 0.6f, height - 0.05f, 0f) },
                   new List<float> { 0.13f, 0.13f }, g.Sides(6, 4, 3), pole, FloraPart.Bark);

            // Hides: one sagging panel per bay and side, from the ridge to the ground, and the two ends closed.
            var rows = g.Sides(4, 2, 1);
            var first = b.VertexCount;
            for (int k = 0; k < bays; k++)
            {
                var x0 = Mathf.Lerp(-half, half, k / (float)bays);
                var x1 = Mathf.Lerp(-half, half, (k + 1f) / bays);
                foreach (var z in new[] { -1f, 1f })
                {
                    var hide = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R()) * g.Range(0.85f, 1.1f);
                    var start = b.VertexCount;
                    for (int r = 0; r <= rows; r++)
                    {
                        var t = r / (float)rows;
                        var sag = Mathf.Sin(t * Mathf.PI) * 0.35f;
                        var p = new Vector3(0f, Mathf.Lerp(height, -0.1f, t), Mathf.Lerp(0f, z * foot, t));
                        var n = new Vector3(0f, foot, z * height).normalized;
                        p -= n * sag;
                        b.Vertex(new Vector3(x0 + 0.05f, p.y, p.z), n, hide, FloraPart.Bark, Mathf.Lerp(1f, 0.75f, t));
                        b.Vertex(new Vector3(x1 - 0.05f, p.y, p.z), n, hide, FloraPart.Bark, Mathf.Lerp(1f, 0.75f, t));
                    }
                    for (int r = 0; r < rows; r++)
                    {
                        int a = start + r * 2;
                        if (z > 0) { b.Triangle(a, a + 1, a + 2); b.Triangle(a + 1, a + 3, a + 2); }
                        else { b.Triangle(a, a + 2, a + 1); b.Triangle(a + 1, a + 2, a + 3); }
                    }
                }
            }
            foreach (var x in new[] { -1f, 1f })
            {
                var hide = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R()) * 0.9f;
                var v0 = b.Vertex(new Vector3(x * half, -0.1f, -foot), Vector3.right * x, hide, FloraPart.Bark, 0.8f);
                var v1 = b.Vertex(new Vector3(x * half, -0.1f, foot), Vector3.right * x, hide, FloraPart.Bark, 0.8f);
                var v2 = b.Vertex(new Vector3(x * half, height, 0f), Vector3.right * x, hide, FloraPart.Bark, 0.9f);
                if (x > 0) b.Triangle(v0, v2, v1); else b.Triangle(v0, v1, v2);
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.3f));

            // The door: a darker hide flap in the middle bay on the +Z side, between two great tusks; skulls on the ridge.
            var bone = AccentAt(g, 0);
            var doorZ = foot * 0.55f + 0.1f;
            var doorY = height * 0.45f;
            // A heavy hide hung from its top edge: it stirs at the bottom, no more.
            var flapTurn = Quaternion.Euler(-Mathf.Atan2(foot, height) * Mathf.Rad2Deg * 0.4f, 0f, 0f);
            var flapTop = new Vector3(0f, doorY * 0.5f, doorZ) + flapTurn * new Vector3(-0.8f, doorY * 0.5f, 0.04f);
            var flap = b.VertexCount;
            b.Sheet(flapTop, flapTurn * Vector3.right * 1.6f, flapTurn * Vector3.down * doorY, g.Sides(4, 2, 1), g.Sides(4, 2, 1), s.Foliage.linear * 0.5f, FloraPart.Bark, 0.6f);
            b.Cloth(flap, p => (flapTop.y - p.y) / doorY * 0.45f);
            if (g.Lod < 2)
            {
                foreach (var x in new[] { -1.3f, 1.3f })
                {
                    var root = new Vector3(x, -0.1f, foot * 0.62f + 0.4f);
                    b.Tube(new List<Vector3> { root, root + new Vector3(x * 0.15f, height * 0.35f, 0.5f), root + new Vector3(-x * 0.35f, height * 0.6f, 0.2f) },
                           new List<float> { 0.22f, 0.14f, 0.03f }, g.Sides(7, 5, 3), bone, FloraPart.Bark, cap: true);
                }
                foreach (var x in new[] { -half - 0.6f, half + 0.6f })
                    b.Blob(new Vector3(x, height + 0.15f, 0f), new Vector3(0.35f, 0.3f, 0.42f), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.2f, x, bone, FloraPart.Bark);
            }

            var count = Mathf.Max(2, Mathf.CeilToInt(half / foot) + 1);
            for (int k = 0; k < count; k++)
                g.Colliders.Add(new Vector4(Mathf.Lerp(-half + foot * 0.6f, half - foot * 0.6f, k / (count - 1f)), 0f, foot * 0.75f, height * 0.6f));
        }

        /// <summary>A signpost: a post, two or three arm boards at different heights pointing different ways, a cap.</summary>
        static void Signpost(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = 1.6f;
            g.TrunkRadius = 0.08f;
            b.Box(new Vector3(0f, height * 0.5f - 0.2f, 0f), new Vector3(0.14f, height + 0.4f, 0.14f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Box(new Vector3(0f, height + 0.04f, 0f), new Vector3(0.24f, 0.08f, 0.24f), Quaternion.identity, g.Bark * 0.7f, FloraPart.Bark);
            g.Perches.Add(Vector3.up * (height + 0.08f));
            var arms = g.Range(0f, 1f) < 0.5f ? 2 : 3;
            for (int k = 0; k < arms; k++)
            {
                var yaw = (k * 2.1f + g.Range(-0.3f, 0.3f)) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var y = height - 0.25f - k * 0.32f;
                var board = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R());
                b.Box(rot * new Vector3(0.45f, y, 0f), new Vector3(0.85f, 0.2f, 0.04f), rot, board, FloraPart.Bark);
                if (g.Lod == 0)
                    b.Box(rot * new Vector3(0.9f, y, 0f), new Vector3(0.12f, 0.12f, 0.04f), rot * Quaternion.Euler(0f, 0f, 45f), board, FloraPart.Bark);
            }
        }

        /// <summary>
        /// One 6.6 m span of a plank bridge along its +Z, deck top at the origin: planks across two stringers, two pairs
        /// of piles down into the water, and a rail each side. It's long so that spans 6.4 m apart each get their own cell
        /// in the tree layer, which is drawn to the horizon. Spans are laid end to end across a river, so each draws the
        /// rail posts at its start only. No colliders: the deck is walked on through SettlementSampling.Deck.
        /// </summary>
        static void Bridge(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var rail = g.Range(s.Height);
            var half = Mathf.Max(s.Spread, 1f) * 0.5f;
            const float length = 6.6f;
            g.Height = rail;
            g.Width = length;
            g.TrunkRadius = 0f;
            var wood = g.Bark;
            // Planks, each a slightly different shade and height so the deck doesn't read as one board.
            var planks = g.Lod == 0 ? 22 : g.Lod == 1 ? 11 : 1;
            for (int k = 0; k < planks; k++)
            {
                var z = Mathf.Lerp(-length * 0.5f, length * 0.5f, (k + 0.5f) / planks);
                b.Box(new Vector3(g.Range(-0.04f, 0.04f), -0.06f + g.Range(-0.012f, 0.012f), z),
                      new Vector3(half * 2f + g.Range(0f, 0.14f), 0.1f, length / planks * 0.92f), Quaternion.identity,
                      wood * g.Range(0.82f, 1.08f), FloraPart.Bark);
            }
            foreach (var x in new[] { -1f, 1f })
            {
                // A stringer under the planks, a pile down into the riverbed, and the rail.
                b.Box(new Vector3(x * (half - 0.25f), -0.24f, 0f), new Vector3(0.2f, 0.26f, length), Quaternion.identity, wood * 0.7f, FloraPart.Bark);
                foreach (var z in new[] { -length * 0.25f, length * 0.25f })
                    b.Tube(new List<Vector3> { new Vector3(x * (half - 0.25f), -5f, z), new Vector3(x * (half - 0.25f), -0.2f, z) },
                           new List<float> { 0.16f, 0.14f }, g.Sides(6, 4, 3), wood * 0.6f, FloraPart.Bark);
                b.Box(new Vector3(x * (half - 0.05f), rail * 0.5f, -length * 0.5f), new Vector3(0.14f, rail + 0.2f, 0.14f), Quaternion.identity, wood * 0.85f, FloraPart.Bark);
                b.Box(new Vector3(x * (half - 0.05f), rail, 0f), new Vector3(0.1f, 0.1f, length), Quaternion.identity, wood * 0.95f, FloraPart.Bark);
                if (g.Lod == 0)
                    b.Box(new Vector3(x * (half - 0.05f), rail * 0.5f, 0f), new Vector3(0.06f, 0.07f, length), Quaternion.identity, wood * 0.8f, FloraPart.Bark);
            }
        }

        /// <summary>
        /// One 3 m length of palisade along its X: a row of sharpened logs (Bark) of uneven height sunk into the ground,
        /// with a binding rail across the back (-Z, inside the camp). Laid end to end round a camp.
        /// </summary>
        static void Palisade(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            const float length = 3.1f;
            g.Height = height;
            g.Width = length;
            g.TrunkRadius = 0f;
            var logs = g.Lod == 2 ? 4 : 10;
            for (int k = 0; k < logs; k++)
            {
                var x = Mathf.Lerp(-length * 0.5f, length * 0.5f, (k + 0.5f) / logs);
                var r = length / logs * 0.5f * g.Range(0.95f, 1.15f);
                var top = height * g.Range(0.85f, 1.1f);
                var lean = new Vector3(g.Range(-0.06f, 0.06f), 0f, g.Range(-0.05f, 0.05f));
                // A cone cap on a tube is as tall as the tube is wide, so a thin last ring gives a sharpened point.
                b.Tube(new List<Vector3> { new Vector3(x, -1f, 0f), new Vector3(x, 0f, 0f) + lean * 0.2f, new Vector3(x, top - r * 2.5f, 0f) + lean, new Vector3(x, top, 0f) + lean * 1.05f },
                       new List<float> { r, r, r * 0.95f, r * 0.12f }, g.Sides(6, 5, 4), g.Bark * g.Range(0.8f, 1.1f), FloraPart.Bark);
                if (k == 2 || k == logs - 3)
                    g.Perches.Add(new Vector3(x, top, 0f) + lean * 1.05f);
            }
            if (g.Lod < 2)
                foreach (var y in new[] { 0.25f, 0.7f })
                    b.Box(new Vector3(0f, height * y, -0.2f), new Vector3(length, 0.12f, 0.1f), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            for (int k = -1; k <= 1; k++)
                g.Colliders.Add(new Vector4(k * 1f, 0f, 0.55f, height));
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
            g.Perches.Add(Vector3.up * height);
            g.Perches.Add(new Vector3(0f, height + 0.8f, -depth * 0.7f));
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
            g.TrunkRadius = 0f;   // the basin is wadeable and its rim a ledge (PropSupport); only the pillar blocks
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
            g.Colliders.Add(new Vector4(0f, 0f, 0.3f, height * 0.72f));
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
            g.TrunkRadius = 0f;   // stand on the rim (PropSupport); the shaft itself is walled off
            g.Colliders.Add(new Vector4(0f, 0f, radius - 0.16f, 0.8f));

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
            foreach (var x in new[] { -1f, 0f, 1f })
                g.Perches.Add(new Vector3(x * (radius + 0.2f), height + 0.04f, 0f));
            b.Beam(new Vector3(-radius - 0.15f, top * 0.72f, 0f), new Vector3(radius + 0.25f, top * 0.72f, 0f), 0.08f, 0.08f, g.Bark * 0.8f, FloraPart.Bark);
            if (g.Lod < 2)
                b.Tube(new List<Vector3> { new Vector3(0f, top * 0.42f, 0f), new Vector3(0f, top * 0.55f, 0f) }, new List<float> { 0.15f, 0.17f },
                       g.Sides(8, 5, 4), g.Bark * 0.7f, FloraPart.Bark, cap: true);
        }


        /// <summary>
        /// Roots hanging from a cave ceiling: strands from a patch round the origin (the ceiling point), each a
        /// tapering tube that wanders as it drops, longest in the middle of the patch, with a side root or two. Everything
        /// is below the origin.
        /// </summary>
        static void Roots(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var patch = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = patch * 2f;
            g.TrunkRadius = 0f;
            var strands = g.Count(s.Branches);
            if (g.Lod == 2)
                strands = Mathf.Max(3, strands / 3);
            var sides = g.Sides(5, 4, 3);
            var rows = g.Lod == 0 ? 6 : 3;
            for (int i = 0; i < strands; i++)
            {
                var off = g.Flat(g.R() * 6.28f) * patch * Mathf.Sqrt(g.R());
                var length = height * g.Range(0.35f, 1f) * (1f - 0.5f * off.magnitude / Mathf.Max(patch, 0.01f));
                var r0 = Mathf.Clamp(length * s.Thickness, 0.012f, 0.08f);
                var path = new List<Vector3>();
                var radii = new List<float>();
                var drift = g.Flat(g.R() * 6.28f) * length * 0.25f;
                for (int k = 0; k <= rows; k++)
                {
                    var t = k / (float)rows;
                    var wobble = k > 0 && k < rows ? g.Flat(g.R() * 6.28f) * length * 0.05f : Vector3.zero;
                    path.Add(off + Vector3.up * (0.05f - length * t) + drift * t * t + wobble);
                    radii.Add(r0 * (1f - 0.85f * t));
                }
                var colour = Color.Lerp(g.Bark, g.Bark * 1.25f, g.R());
                b.Tube(path, radii, sides, colour, FloraPart.Bark, cap: true);
                if (g.Lod == 0 && length > height * 0.5f)
                {
                    // A side root from partway down, splaying out.
                    var from = path[rows / 2];
                    var dir = (g.Flat(g.R() * 6.28f) - Vector3.up * 1.2f).normalized;
                    var sideLength = length * g.Range(0.2f, 0.4f);
                    b.Tube(new List<Vector3> { from, from + dir * sideLength * 0.5f + g.Direction() * sideLength * 0.1f, from + dir * sideLength },
                           new List<float> { radii[rows / 2] * 0.6f, radii[rows / 2] * 0.35f, radii[rows / 2] * 0.1f }, 3, colour, FloraPart.Bark);
                }
            }
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
