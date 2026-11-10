using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, crystals, fulgurites, arc spires, debris, hazard shelters, floating rocks and columns. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
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
        /// <summary>ArcSpire prong collision (W-42): prongs shorter than this stay walk-through; slices are this tall
        /// and stop at this height above the base (the player's body never reaches higher).</summary>
        const float MinShardCollider = 2f, ShardSlice = 0.5f, ShardColliderTop = 4.5f;

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
                // Collision that leans with the prong (W-42): short upright slices stacked up its axis, each centred on
                // the axis at the slice's middle, as wide as the tube there plus half the axis's drift across the
                // slice, so no part of the tilted tube pokes out of them. Only up to ShardColliderTop: the player's
                // body never reaches higher. Prongs under MinShardCollider stay walk-through.
                if (tall < MinShardCollider)
                    continue;
                var axis = new[] { foot + Vector3.down * 0.3f, Vector3.Lerp(foot, tip, 0.45f) + lean * 0.2f, Vector3.Lerp(foot, tip, 0.8f), tip };
                var radii = new[] { r, r * 0.75f, r * 0.4f, r * 0.05f };
                var top = Mathf.Min(ShardColliderTop, tip.y);
                for (var y0 = 0f; y0 < top - 0.05f; y0 += ShardSlice)
                {
                    var y1 = Mathf.Min(y0 + ShardSlice, top);
                    Vector3 lo = AxisAt(axis, y0, out var rLo), hi = AxisAt(axis, y1, out _), mid = AxisAt(axis, (y0 + y1) * 0.5f, out _);
                    var drift = Mathf.Max(Vector2.Distance(new Vector2(lo.x, lo.z), new Vector2(mid.x, mid.z)),
                                          Vector2.Distance(new Vector2(hi.x, hi.z), new Vector2(mid.x, mid.z)));
                    g.Colliders.Add(new Vector4(mid.x, mid.z, rLo + drift, y1));
                    g.ColliderBase.Add(y0);
                }

                // The tube's axis point and radius at height y (its four rings, linearly between).
                Vector3 AxisAt(Vector3[] pts, float y, out float radius)
                {
                    for (int i = 0; i < pts.Length - 1; i++)
                        if (y <= pts[i + 1].y || i == pts.Length - 2)
                        {
                            var t = Mathf.Clamp01((y - pts[i].y) / Mathf.Max(1e-4f, pts[i + 1].y - pts[i].y));
                            radius = Mathf.Lerp(radii[i], radii[i + 1], t);
                            return Vector3.Lerp(pts[i], pts[i + 1], t);
                        }
                    radius = radii[0];
                    return pts[0];
                }
            }
            // The cracked base mound: a low core, so the player can't slip in between the prongs' feet.
            g.Colliders.Add(new Vector4(0f, 0f, baseRadius * 0.6f, baseRadius * 0.5f));
            g.ColliderBase.Add(0f);
            // Far off (LOD2) the arcs stay as three-point, thicker strokes, so a wasteland still crackles from afar (W-101).
            var arcRadius = g.Lod == 2 ? 0.09f : 0.03f;
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
                    radii.Add(arcRadius);
                }
                b.Tube(path, radii, 3, arc, FloraPart.Arc);
            }
        }

        /// <summary>
        /// Floating debris (FloraForm.Debris): lumps of dark rock hovering over a cracked mound, the cluster slowly orbiting
        /// it and each lump bobbing (FloraMeshBuilder.Orbit). The mound and any lumps low enough to hit lie still and are
        /// solid; the floating ones are not.
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
            var moundTurn = Quaternion.Euler(0f, g.R() * 360f, 0f);
            b.Blob(Vector3.up * mound * 0.05f, new Vector3(mound, mound * 0.3f, mound * 0.9f), moundTurn,
                   g.Sides(2, 1, 0), 0.4f, g.R() * 100f, g.Bark * 0.7f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, mound));
            // Solid (#70): the mound where it stands above a step (its turn isn't kept, so the narrower, round footprint).
            g.AddSteppedCollider(Vector3.up * mound * 0.05f, new Vector3(mound * 0.9f, mound * 0.3f, mound * 0.9f), Quaternion.identity);
            g.AddChunkBoxes(first, moundTurn);

            // Pieces that would hang within walking reach (bottom under head height) have dropped instead: they lie
            // still on the ground or the mound, half sunk, and are solid; only the high ones orbit (#70).
            var pieces = g.Count(s.Branches);
            var high = new List<(Vector3 at, Vector3 radii, Quaternion turn, float seed, Color color)>();
            for (int k = 0; k < pieces; k++)
            {
                var a = g.R() * 6.28f;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * g.Range(0.3f, 1f) + Vector3.up * height * g.Range(0.3f, 1f);
                var size = height * g.Range(0.05f, 0.16f);
                var radii = new Vector3(size, size * g.Range(0.6f, 0.9f), size * g.Range(0.7f, 1f));
                var turn = Quaternion.Euler(g.R() * 360f, g.R() * 360f, g.R() * 360f);
                var seed = g.R() * 100f;
                var color = Color.Lerp(g.Bark, g.Bark * 1.4f, g.R());
                if (at.y - size >= DebrisHoldAbove)
                {
                    high.Add((at, radii, turn, seed, color));
                    continue;
                }
                if (at.y - size >= DebrisHeadroom)
                {
                    // W-106 (#70): a piece that would orbit within a jumping head's reach is held still instead, low
                    // over the ground: a flat stepping stone hovering at knee to hip height, solid (a kit box, so its
                    // top is a floor and its underside a ceiling), that the player can jump onto. No new draws from
                    // the rng (its height comes from the piece's seed), so the pieces after it keep their shapes.
                    var stoneTop = DebrisStoneTop.x + (DebrisStoneTop.y - DebrisStoneTop.x) * Mathf.Repeat(seed * 7.31f, 1f);
                    var stoneRadii = new Vector3(Mathf.Max(radii.x, 0.45f), Mathf.Min(radii.y, 0.22f), Mathf.Max(radii.z, 0.4f));
                    var stoneAt = new Vector3(at.x, stoneTop - stoneRadii.y, at.z);
                    var stoneTurn = Quaternion.Euler(0f, turn.eulerAngles.y, 0f);
                    b.Blob(stoneAt, stoneRadii, stoneTurn, g.Sides(1, 1, 0), 0.25f, seed, color, FloraPart.Metal, 1f, 0.5f);
                    // A small grain of rock held under it, as if pinned there by the field.
                    b.Blob(stoneAt + Vector3.down * (stoneRadii.y + 0.25f), stoneRadii * 0.22f, turn, 1, 0.3f, seed + 3f, color * 0.9f, FloraPart.Metal, 1f, 0.5f);
                    var half = new Vector3(stoneRadii.x * 0.75f, stoneRadii.y, stoneRadii.z * 0.75f);
                    g.Boxes.Add(new KitBox { Centre = stoneAt, Half = half, Rotation = stoneTurn, Reach = half.magnitude });
                    continue;
                }
                var flat = new Vector2(at.x, at.z);
                var onMound = flat.magnitude < mound * 0.85f ? mound * 0.35f * Mathf.Sqrt(1f - Mathf.Pow(flat.magnitude / mound, 2f)) : 0f;
                var rest = new Vector3(at.x, onMound + radii.y * 0.45f, at.z);
                var lying = Quaternion.Euler(g.Range(-12f, 12f), turn.eulerAngles.y, g.Range(-12f, 12f));
                // W-93 (#70): a chunk too thin to stand on lies pressed flat into the ground, a decal walked over.
                if (rest.y + radii.y < ChunkStep && onMound <= 0f)
                {
                    radii.y = Mathf.Min(radii.y, 0.08f);
                    rest.y = radii.y * 0.3f;
                }
                var chunk = b.Blob(rest, radii, lying, g.Sides(1, 1, 0), 0.3f, seed, color * 0.8f, FloraPart.Bark, 1f, 0.5f);
                g.AddSteppedCollider(rest, radii, lying);
                g.AddChunkBoxes(chunk, Quaternion.Euler(0f, lying.eulerAngles.y, 0f));
            }
            var floating = b.VertexCount;
            foreach (var p in high)
                b.Blob(p.at, p.radii, p.turn, g.Sides(1, 1, 0), 0.3f, p.seed, p.color, FloraPart.Metal, 1f, 0.5f);
            b.Orbit(floating, height * 0.06f);
        }

        /// <summary>How high (m, before scaling) a Debris piece's bottom must float to stay in the air: above a head.</summary>
        const float DebrisHeadroom = 2.2f;

        /// <summary>W-106 (#70): a piece whose bottom floats under this (m) is in reach of a jumping head, so it's held still as a stepping stone.</summary>
        const float DebrisHoldAbove = 3.4f;

        /// <summary>W-106 (#70): a held stepping stone's top height range (m): under the 1.28 m a jump clears.</summary>
        static readonly Vector2 DebrisStoneTop = new Vector2(0.75f, 1.1f);

        /// <summary>W-93 (#70): a wasteland chunk topping out under this (m, before scaling) is walked over, so it gets no boxes.</summary>
        const float ChunkStep = 0.45f;

        /// <summary>
        /// A shelter against a harsh biome's hazard (FloraForm.HazardShelter, #69): settlements in ash, snow and storm
        /// country put one on a house plot (SettlementBuilding.OnlyIn). The style is data (FloraSpecies.ShelterStyle).
        /// </summary>
        static void HazardShelter(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.6f;
            g.TrunkRadius = 0f;
            var heap = s.FoliageVariation.linear;
            var detail = g.Sides(2, 1, 0);

            if (s.ShelterStyle == ShelterStyle.SnowLeanTo)
            {
                // A roof of poles and hides sloping from the snow at the back (-Z) up to two posts at the open front.
                var back = -radius * 0.9f;
                var front = radius * 0.7f;
                var rise = height;
                var slope = Mathf.Atan2(rise, front - back) * Mathf.Rad2Deg;
                var length = Mathf.Sqrt(rise * rise + (front - back) * (front - back));
                var mid = new Vector3(0f, rise * 0.5f, (front + back) * 0.5f);
                var tilt = Quaternion.Euler(-slope, 0f, 0f);
                b.Box(mid, new Vector3(radius * 1.9f, 0.12f, length), tilt, g.FoliageColor(), FloraPart.Bark);
                // Snow lying on the roof, thicker low down.
                b.Box(mid + tilt * new Vector3(0f, 0.12f, -length * 0.1f), new Vector3(radius * 1.8f, 0.1f, length * 0.75f), tilt, heap, FloraPart.Bark);
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Tube(new List<Vector3> { new Vector3(side * radius * 0.9f, -0.2f, front), new Vector3(side * radius * 0.9f, rise + 0.25f, front + 0.1f) },
                           new List<float> { 0.11f, 0.08f }, g.Sides(6, 5, 4), g.Bark, FloraPart.Bark);
                    // Snow blocks stacked up each side as a wind wall, lower towards the front.
                    for (int k = 0; k < 3; k++)
                    {
                        var z = Mathf.Lerp(back + 0.5f, front - 0.6f, k / 2f);
                        var wallTop = Mathf.Lerp(rise * 0.25f, rise * 0.85f, (z - back) / (front - back)) * 0.8f;
                        b.Box(new Vector3(side * radius, wallTop * 0.5f, z), new Vector3(0.45f, wallTop, 1.05f), Quaternion.Euler(0f, g.Range(-4f, 4f), 0f), heap * 0.95f, FloraPart.Bark);
                    }
                }
                // The drift banked over the back, and loose drifts round the sides.
                var first = b.VertexCount;
                b.Blob(new Vector3(0f, 0f, back - 0.2f), new Vector3(radius * 1.4f, rise * 0.45f, radius * 0.7f), Quaternion.identity, detail, 0.25f, g.R() * 100f, heap, FloraPart.Bark);
                for (int side = -1; side <= 1; side += 2)
                    b.Blob(new Vector3(side * radius * 1.25f, 0f, back * 0.3f), new Vector3(radius * 0.45f, rise * 0.22f, radius * 0.9f), Quaternion.identity, detail, 0.3f, g.R() * 100f, heap, FloraPart.Bark);
                b.Occlude(first, (p, n) => GroundOcclusion(p, rise * 0.4f));
                // Solid: the drifted back and the snow walls; the front is open to walk in.
                g.Colliders.Add(new Vector4(0f, back - 0.2f, radius * 0.7f, rise * 0.45f));
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Colliders.Add(new Vector4(side * radius, back + 0.6f, 0.45f, rise * 0.4f));
                    g.Colliders.Add(new Vector4(side * radius, (back + front) * 0.5f, 0.45f, rise * 0.55f));
                }
                g.Perches.Add(new Vector3(0f, rise + 0.25f, front + 0.1f));
                return;
            }

            // A dome of rough stone, sunk a quarter into the ground (Ash: tall; Shard: low and wide).
            var shard = s.ShelterStyle == ShelterStyle.ShardDome;
            var domeY = -radius * 0.2f;
            var domeH = shard ? height * 0.75f : height * 1.05f;
            var dome = b.VertexCount;
            b.Blob(Vector3.up * domeY, new Vector3(radius, domeH - domeY, radius * 0.95f), Quaternion.identity, g.Sides(3, 2, 1), 0.35f, g.R() * 100f, g.Bark, FloraPart.Bark, 1f, 0.25f);
            b.Occlude(dome, (p, n) => GroundOcclusion(p, domeH * 0.6f));
            // A smoke hole's ring on top.
            b.Tube(new List<Vector3> { Vector3.up * (domeH - 0.15f), Vector3.up * (domeH + 0.2f) }, new List<float> { 0.3f, 0.26f }, g.Sides(8, 6, 4), g.Bark * 0.7f, FloraPart.Bark);
            g.Chimneys.Add(Vector3.up * (domeH + 0.2f));
            // The crawl tunnel to the door (+Z): a half-buried vault, its dark mouth, a lintel stone.
            var tunnelR = Mathf.Min(1.15f, radius * 0.4f);
            var tunnelEnd = radius * 1.3f;
            b.Tube(new List<Vector3> { new Vector3(0f, 0f, radius * 0.6f), new Vector3(0f, 0f, tunnelEnd) }, new List<float> { tunnelR, tunnelR * 0.95f },
                   g.Sides(10, 7, 5), g.Bark * 0.9f, FloraPart.Bark);
            b.Box(new Vector3(0f, tunnelR * 0.4f, tunnelEnd + 0.01f), new Vector3(tunnelR * 1.1f, tunnelR * 0.8f, 0.05f), Quaternion.identity, new Color(0.02f, 0.02f, 0.02f), FloraPart.Bark);
            b.Box(new Vector3(0f, tunnelR * 0.92f, tunnelEnd + 0.05f), new Vector3(tunnelR * 1.6f, 0.22f, 0.4f), Quaternion.Euler(0f, 0f, g.Range(-4f, 4f)), g.Bark * 1.15f, FloraPart.Bark);
            g.Colliders.Add(new Vector4(0f, 0f, radius * 0.95f, domeH * 0.7f));
            for (int side = -1; side <= 1; side += 2)
                g.Colliders.Add(new Vector4(side * tunnelR * 0.8f, (radius * 0.6f + tunnelEnd) * 0.5f, 0.3f, tunnelR));
            g.Perches.Add(Vector3.up * (domeH + 0.2f));

            if (!shard)
            {
                // Ash fall: grey ash heaped on the back and drifted against the foot all round.
                var ash = b.VertexCount;
                b.Blob(new Vector3(0f, domeH * 0.55f, -radius * 0.35f), new Vector3(radius * 0.8f, domeH * 0.35f, radius * 0.6f), Quaternion.identity, detail, 0.3f, g.R() * 100f, heap, FloraPart.Bark);
                var drifts = g.Lod == 2 ? 3 : 6;
                for (int k = 0; k < drifts; k++)
                {
                    var a = (k + 0.5f) / drifts * Mathf.PI * 1.6f + Mathf.PI * 0.7f;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.95f;
                    b.Blob(at, new Vector3(radius * 0.35f, g.Range(0.2f, 0.45f), radius * 0.25f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), detail, 0.3f, g.R() * 100f, heap, FloraPart.Bark);
                }
                b.Occlude(ash, (p, n) => Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n.y)));
                return;
            }

            // Electric storms: glass shards stood round the dome, leaning out, tips up (they take the strikes), and a
            // tall one by the door.
            var shards = Mathf.Max(3, g.Count(s.Branches));
            var glass = FloraPart.Foliage;   // the builder draws Foliage as glass when Finish is Glass
            for (int k = 0; k <= shards; k++)
            {
                var tall = k == shards;
                var a = tall ? Mathf.PI * 0.5f + 0.5f : k / (float)shards * Mathf.PI * 2f + g.Range(-0.25f, 0.25f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (!tall && dir.z > 0.8f)
                    continue;   // keep the tunnel clear
                var len = tall ? height * 1.6f : height * g.Range(0.6f, 1.05f);
                var foot = dir * (tall ? radius * 1.35f : radius * g.Range(0.95f, 1.1f));
                var lean = Quaternion.AngleAxis(g.Range(8f, 22f), Vector3.Cross(Vector3.up, dir)) * Quaternion.Euler(0f, g.R() * 360f, 0f);
                var w = g.Range(0.18f, 0.32f) * (tall ? 1.3f : 1f);
                b.Box(foot + lean * Vector3.up * len * 0.45f, new Vector3(w, len, w * 0.6f), lean, g.FoliageColor(), glass);
                if (tall)
                    g.Colliders.Add(new Vector4(foot.x, foot.z, w + 0.1f, len * 0.8f));
            }
            // Scorch and fused sand round the foot.
            var scorch = b.VertexCount;
            b.Blob(Vector3.up * 0.02f, new Vector3(radius * 1.5f, 0.06f, radius * 1.5f), Quaternion.identity, 1, 0.2f, g.R() * 50f, heap, FloraPart.Bark);
            b.Occlude(scorch, (p, n) => 0.8f);
        }

        /// <summary>
        /// An enormous magnetic rock floating over the electric wastelands (G-37), turning slowly and bobbing (Orbit): a
        /// jagged mass of a few blocks, shaped by variant (a tall shard, a flat slab, a cluster), smaller shards round it,
        /// and arcs crackling from it to some of them; a scorched, cracked mound on the ground beneath.
        /// </summary>
        static void FloatingRock(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var size = g.Range(s.Height);
            var lift = size * g.Range(0.6f, 1.1f) + 2f;
            g.Height = lift + size;
            g.Width = size * 1.6f;
            g.TrunkRadius = 0f;

            var mound = size * 0.6f;
            var first = b.VertexCount;
            var moundTurn = Quaternion.Euler(0f, g.R() * 360f, 0f);
            b.Blob(Vector3.up * mound * 0.04f, new Vector3(mound, mound * 0.18f, mound * 0.9f), moundTurn,
                   g.Sides(2, 1, 0), 0.45f, g.R() * 100f, g.Bark * 0.6f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, mound));
            // Solid (#70): the scorched mound (the flat chunk underfoot). The rock itself floats at least ~5 m up, far
            // over a head, so it stays non-solid.
            g.AddSteppedCollider(Vector3.up * mound * 0.04f, new Vector3(mound * 0.9f, mound * 0.18f, mound * 0.9f), Quaternion.identity);
            g.AddChunkBoxes(first, moundTurn);

            var floating = b.VertexCount;
            var centre = Vector3.up * (lift + size * 0.5f);
            var kind = g.Variant % 3;
            var dims = kind == 0 ? new Vector3(0.42f, 1f, 0.5f) : kind == 1 ? new Vector3(1f, 0.32f, 0.8f) : new Vector3(0.72f, 0.62f, 0.7f);
            dims *= size * 0.5f;
            var blocks = kind == 2 ? 4 : 2;
            for (int k = 0; k < blocks; k++)
            {
                var offset = k == 0 ? Vector3.zero : Vector3.Scale(new Vector3(g.Range(-0.7f, 0.7f), g.Range(-0.5f, 0.5f), g.Range(-0.7f, 0.7f)), dims);
                var scale = k == 0 ? 1f : g.Range(0.45f, 0.7f);
                b.Blob(centre + offset, dims * scale, Quaternion.Euler(g.Range(-25f, 25f), g.R() * 360f, g.Range(-25f, 25f)),
                       g.Sides(2, 1, 1), 0.38f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 1.5f, g.R() * 0.5f), FloraPart.Bark);
            }
            // The mass only turns (a per-vertex bob would warp something this big); the shards and arcs bob as well.
            b.Orbit(floating, 0f);
            var loose = b.VertexCount;
            // Shards round it.
            var shards = new List<Vector3>();
            var count = g.Lod == 2 ? 3 : 7;
            for (int k = 0; k < count; k++)
            {
                var a = k / (float)count * 6.28f + g.Range(-0.3f, 0.3f);
                var at = centre + new Vector3(Mathf.Cos(a), g.Range(-0.6f, 0.6f), Mathf.Sin(a)) * size * g.Range(0.75f, 1.05f);
                var piece = size * g.Range(0.05f, 0.12f);
                b.Blob(at, new Vector3(piece, piece * g.Range(0.6f, 1.2f), piece * g.Range(0.6f, 1f)), Quaternion.Euler(g.R() * 360f, g.R() * 360f, g.R() * 360f),
                       g.Sides(1, 1, 0), 0.35f, g.R() * 100f, g.Bark * 1.3f, FloraPart.Metal, 1f, 0.5f);
                shards.Add(at);
            }
            // Arcs from the rock to some of its shards (they crackle: FloraPart.Arc).
            // Far off (LOD2) half as many, thicker and three points each (W-101).
            {
                var arcColor = s.Foliage.linear;
                for (int k = 0; k < shards.Count; k += g.Lod == 2 ? 4 : 2)
                {
                    var from = centre + (shards[k] - centre).normalized * Mathf.Min(dims.x, dims.z) * 0.9f;
                    var to = shards[k];
                    var path = new List<Vector3>();
                    var radii = new List<float>();
                    var points = g.Sides(8, 5, 3);
                    for (int j = 0; j < points; j++)
                    {
                        var t = j / (points - 1f);
                        var zig = j == 0 || j == points - 1 ? Vector3.zero : (new Vector3(g.R(), g.R(), g.R()) - Vector3.one * 0.5f) * Vector3.Distance(from, to) * 0.2f;
                        path.Add(Vector3.Lerp(from, to, t) + zig);
                        radii.Add(size * (g.Lod == 2 ? 0.016f : 0.006f));
                    }
                    b.Tube(path, radii, 3, arcColor, FloraPart.Arc);
                }
            }
            b.Orbit(loose, size * 0.03f);
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
    }
}
