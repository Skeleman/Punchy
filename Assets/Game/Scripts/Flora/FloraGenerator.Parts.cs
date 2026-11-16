using System.Collections.Generic;
using UnityEngine;
using Family = Punchy.Terrain.MaterialFamily;

namespace Punchy.Flora
{
    // FloraGenerator, the part library (W-175, #415): things defined once (a pole, an awning, an anvil) that units
    // (UnitDef, FloraGenerator.Units) are made of. Each part names its own materials (W-166 MaterialFamily) and reads
    // its place and size from its UnitPart in the unit's frame. Parts stay code because their shapes are seeded and
    // procedural; units are data. See docs/agents/parts-and-units.md.
    public static partial class FloraGenerator
    {
        delegate void PartBuild(Grower g, UnitFrame f, UnitPart p);

        /// <summary>The part library: id to builder. Units name parts by these ids.</summary>
        static readonly Dictionary<string, PartBuild> PartLibrary = new Dictionary<string, PartBuild>
        {
            ["pole"] = Pole,
            ["post"] = Post,
            ["counter"] = Counter,
            ["wares"] = Wares,
            ["awning"] = Awning,
            ["pyramid roof"] = PyramidRoof,
            ["forge hearth"] = ForgeHearth,
            ["chimney stack"] = ChimneyStack,
            ["anvil"] = Anvil,
            ["hide tent"] = HideTent,
            ["door mat"] = DoorMatPart,
            ["fire ring"] = FireRing,
            ["log tepee"] = LogTepee,
            ["embers"] = Embers,
        };

        /// <summary>Every part library id (for the unit tests: each unit's parts must exist).</summary>
        public static IReadOnlyCollection<string> PartIds => PartLibrary.Keys;

        /// <summary>
        /// Draws a unit: grows its frame from the species (height from Height, width from Spread), then each part in
        /// order. Each part starts from the form's own family (FormFamily) and names its own materials.
        /// </summary>
        static void BuildUnit(Grower g, UnitDef unit)
        {
            var b = g.B;
            var height = g.Range(g.S.Height);
            var frame = new UnitFrame(height * g.S.Spread, height, unit.DepthOfWidth);
            g.Height = height;
            g.Width = Mathf.Max(frame.Width, frame.Depth) * 1.3f;
            g.TrunkRadius = 0f;
            foreach (var part in unit.Parts)
            {
                b.Family = b.BaseFamily;
                PartLibrary[part.Part](g, frame, part);
            }
            b.Family = b.BaseFamily;
        }

        /// <summary>A thin round timber pole from At (its foot) to To (its top), radius SpanMetres.x (a stall's posts, Z-30 grain).</summary>
        static void Pole(Grower g, UnitFrame f, UnitPart p)
        {
            var r = f.Span(p).x;
            g.B.Family = Timber(g);
            g.B.Tube(new List<Vector3> { f.At(p), f.To(p) }, new List<float> { r, r }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
        }

        /// <summary>A heavy wooden post from At to To, radius Span.x at the foot and Span.y at the top (a shed's corner posts).</summary>
        static void Post(Grower g, UnitFrame f, UnitPart p)
        {
            var span = f.Span(p);
            g.B.Wood = true;
            g.B.Tube(new List<Vector3> { f.At(p), f.To(p) }, new List<float> { span.x, span.y }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark);
            g.B.Wood = false;
        }

        /// <summary>A timber counter: a box centred on At, Span in size.</summary>
        static void Counter(Grower g, UnitFrame f, UnitPart p)
        {
            g.B.Family = Timber(g);
            g.B.Box(f.At(p), f.Span(p), Quaternion.identity, g.Bark * 0.8f, FloraPart.Bark);
        }

        /// <summary>
        /// Goods laid out in a row from At to To (crocks and wares in the species' accent colours, plaster), seven of them,
        /// each nudged a little front to back. Dropped at the far level of detail.
        /// </summary>
        static void Wares(Grower g, UnitFrame f, UnitPart p)
        {
            if (g.Lod >= 2)
                return;
            g.B.Family = Family.Plaster;
            for (int k = 0; k < 7; k++)
            {
                // Lerp the shares, then scale (the coded stall's float order, so the goods land on the same bits).
                var t = k / 6f;
                var at = f.Point(Vector3.Lerp(p.At, p.To, t), Vector3.Lerp(p.AtMetres, p.ToMetres, t)) + Vector3.forward * g.Range(-0.08f, 0.08f);
                g.B.Blob(at, Vector3.one * g.Range(0.07f, 0.12f), Quaternion.identity, 0, 0.2f, g.R() * 50f, AccentAt(g, k + 1), FloraPart.Bark);
            }
        }

        /// <summary>
        /// A striped cloth awning from its back bar (At) down to its front bar (To), Span.x wide, sagging up to Span.y between
        /// the bars (held at both, so it only ripples between them). Stripes alternate the species' foliage colour and its
        /// first accent.
        /// </summary>
        static void Awning(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            Vector3 back = f.At(p), front = f.To(p), span = f.Span(p);
            var stripes = g.Sides(6, 4, 2);
            b.Family = Family.Cloth;
            var awning = b.VertexCount;
            for (int k = 0; k < stripes; k++)
            {
                var x = k / (float)stripes - 0.5f;
                var colour = k % 2 == 0 ? g.FoliageColor() : AccentAt(g, 0);
                b.Sheet(back + Vector3.right * x * span.x, Vector3.right * span.x / stripes, front - back, 1, g.Sides(5, 2, 1), colour, FloraPart.Bark);
            }
            var sag = span.y;
            b.Cloth(awning, q => sag * Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(back.z, front.z, q.z)) * Mathf.PI), taut: true);
        }

        /// <summary>
        /// A four-sided pyramid roof with a rounded eave (RoofTube), its eave at At's height, its apex at To (a perch), its
        /// rim reaching past the larger of Span.x and Span.z (half width, half depth). In the species' foliage colour; the
        /// roofing is the form's own family.
        /// </summary>
        static void PyramidRoof(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            Vector3 eave = f.At(p), apex = f.To(p), span = f.Span(p);
            var roof = b.VertexCount;
            RoofTube(b, eave.y - 0.2f, Mathf.Max(span.x, span.z) * 1.414f * 1.15f, 4, g.FoliageColor(), FloraPart.Bark, apex.y, 0.05f, 0f, 0f);
            RotateFrom(b, roof, Quaternion.Euler(0f, 45f, 0f));
            g.Perches.Add(apex);
        }

        /// <summary>
        /// A smith's forge hearth centred on At, Span.x wide and Span.z deep: a stone bed with its coals sunk in a pit inside
        /// a raised rim (Z-40: they sat on top), glowing embers among dark coals.
        /// </summary>
        static void ForgeHearth(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            Vector3 hearth = f.At(p), span = f.Span(p);
            float hearthX = span.x, hearthZ = span.z, bed = 0.85f, rim = 0.15f;
            b.Family = Family.Stone;   // W-166: the hearth is stone, not the walls' plaster
            b.Box(new Vector3(hearth.x, bed * 0.5f, hearth.z), new Vector3(hearthX, bed, hearthZ), Quaternion.identity, Stone(g), FloraPart.Bark);
            foreach (var side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(hearth.x, (bed + 1.1f) * 0.5f, hearth.z + side * (hearthZ - rim) * 0.5f), new Vector3(hearthX, 1.1f - bed, rim),
                      Quaternion.identity, Stone(g), FloraPart.Bark);
                b.Box(new Vector3(hearth.x + side * (hearthX - rim) * 0.5f, (bed + 1.1f) * 0.5f, hearth.z), new Vector3(rim, 1.1f - bed, hearthZ - rim * 2f),
                      Quaternion.identity, Stone(g), FloraPart.Bark);
            }
            b.Family = b.BaseFamily;
            var pit = new Vector2(hearthX * 0.5f - rim - 0.08f, hearthZ * 0.5f - rim - 0.08f);
            var embers = g.Lod == 0 ? 7 : 3;
            for (int k = 0; k < embers; k++)
            {
                var at = new Vector3(hearth.x + g.Range(-pit.x, pit.x), bed + 0.02f, hearth.z + g.Range(-pit.y, pit.y));
                var size = g.Range(0.08f, 0.17f);
                var glowing = k % 3 != 2;
                b.Blob(at, new Vector3(size, size * 0.6f, size * g.Range(0.8f, 1.2f)), Quaternion.Euler(0f, g.R() * 360f, 0f), 1, 0.3f, g.R() * 50f,
                       glowing ? AccentAt(g, 0) : g.Bark * 0.25f, glowing ? FloraPart.Glow : FloraPart.Bark);
            }
        }

        /// <summary>
        /// A square chimney stack in fired brick (W-166 row 3, Materials/Brick.hlsl) from At up to To, Span.x by Span.z in
        /// section, with its chimney top (ChimneyTop) and a perch on it.
        /// </summary>
        static void ChimneyStack(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 foot = f.At(p), top = f.To(p), span = f.Span(p);
            g.B.Family = Family.Brick;
            g.B.Box(new Vector3(foot.x, (foot.y + top.y) * 0.5f, foot.z), new Vector3(span.x, top.y - foot.y, span.z), Quaternion.identity, Stone(g) * 0.85f, FloraPart.Bark);
            ChimneyTop(g, top, span.z * 0.5f, Stone(g) * 0.85f);
            g.B.Family = g.B.BaseFamily;
            g.Perches.Add(top);
        }

        /// <summary>An anvil standing at At: an iron body on a wooden block (Z-30 grain).</summary>
        static void Anvil(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            var at = f.At(p);
            b.Wood = true;
            b.Box(at + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            b.Wood = false;
            b.Box(at + Vector3.up * 0.72f, new Vector3(0.75f, 0.24f, 0.3f), Quaternion.identity, new Color(0.06f, 0.06f, 0.07f), FloraPart.Metal);
        }

        /// <summary>
        /// A hide tent's shell, the full height of the frame and its width across (a goblin's or an ogre's tent): poles
        /// leaning in to cross under the apex, hide panels sagging between them (the +Z one left open as the doorway), and
        /// stepped ring colliders that follow the slope. Poles and panels share their feet, so they are one part. Sets the
        /// unit's bounds.
        /// </summary>
        static void HideTent(Grower g, UnitFrame f, UnitPart u)
        {
            var s = g.S;
            var b = g.B;
            var height = f.Height;
            var radius = f.Width * 0.5f;
            g.Width = radius * 2.2f;
            g.TrunkRadius = radius * 0.85f;

            // Poles lean in to meet near the top, then poke out past it, a little crooked.
            var poles = Mathf.Max(3, g.Count(s.Branches));
            var apex = new Vector3(g.Range(-0.1f, 0.1f), height * 0.88f, g.Range(-0.1f, 0.1f));
            var rows = g.Sides(5, 3, 2);
            // Each edge sags towards the axis under the apex, not along its own panel's middle (W-43): a pole's edge is
            // shared by two panels, so both put it in the same place and no slit opens at the corner.
            Vector3 Inward(Vector3 p) => -new Vector3(p.x - apex.x, 0f, p.z - apex.z).normalized;
            float Sag(float t) => Mathf.Sin(t * Mathf.PI) * radius * 0.06f;
            var feet = new List<Vector3>();
            for (int k = 0; k < poles; k++)
            {
                var a = (k + g.Range(-0.2f, 0.2f)) / poles * Mathf.PI * 2f + 0.3f;
                var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.9f, 1.05f);
                feet.Add(foot);
                var through = apex + (apex - foot).normalized * height * g.Range(0.12f, 0.22f);
                // W-113 (item 9): the pole bows with the hide's sag and stands just inside it, so the hide covers it from
                // the ground up and only its tip shows past the apex (a straight pole stood outside the sagging hide its
                // whole length and poked out at the foot).
                // Deep enough that even a three- or four-pole tent's flat panels cover a pole's faceted sides; it closes
                // only just under the apex, where the poles cross.
                var inset = radius * 0.12f;
                var path = new List<Vector3>();
                var radii = new List<float>();
                for (int r = 0; r < rows; r++)
                {
                    var t = r / (float)rows;
                    var p = Vector3.Lerp(foot, apex, t);
                    path.Add(p + Inward(p) * (Sag(t) + inset * (1f - t * t)) + (r == 0 ? Vector3.down * 0.1f : Vector3.zero));
                    radii.Add(radius * Mathf.Lerp(0.04f, 0.035f, t));
                }
                path.Add(apex);
                radii.Add(radius * 0.035f);
                path.Add(through);
                radii.Add(radius * 0.025f);
                b.Family = Timber(g);   // W-166: the poles are wood, the hides the form's cloth
                b.Tube(path, radii, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
                b.Family = b.BaseFamily;
            }

            // Hide panels between neighbouring poles, each a slightly different hide, sagging inwards.
            var first = b.VertexCount;
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
                    var sag = Sag(t);
                    var p0 = Vector3.Lerp(a, apex, t);
                    var p1 = Vector3.Lerp(c, apex, t);
                    var ao = Mathf.Lerp(0.7f, 1f, t);
                    var cut = open && t < 0.55f;
                    var n = Vector3.Cross(p1 - p0, apex - p0).normalized;
                    if (Vector3.Dot(n, (p0 + p1) * 0.5f - Vector3.up * p0.y) < 0f) n = -n;
                    b.Vertex(p0 + Inward(p0) * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                    b.Vertex(p1 + Inward(p1) * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                }
                for (int r = 0; r < rows; r++)
                {
                    int i0 = top + r * 2;
                    b.Triangle(i0, i0 + 2, i0 + 1);
                    b.Triangle(i0 + 1, i0 + 2, i0 + 3);
                }
            }
            // The panels are hide (W-113): cloth's snow, held still on the poles, but leather's mottle and stitched seams
            // instead of W-99's linen weave (the tents read as linen).
            b.Hide(first);
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.3f));

            // Collision follows the slope (W-43): stepped rings, each as wide as the hide half a metre below its top, so
            // a body stops where the slope meets it about the chest, not with its head through the hide (a small goblin
            // tent too). (With none, the whole tent was one fat trunk cylinder; its hides' support grid gave the climb
            // logic a "ledge" all round it, so it is left out of the support grid: FloraGenerator.Grow.)
            foreach (var top in new[] { 1.1f, 2f, 3f })
                if (top < apex.y - 0.5f)
                    g.Colliders.Add(new Vector4(apex.x * top / apex.y, apex.z * top / apex.y, radius * 0.95f * (1f - (top - 0.5f) / apex.y), top));
        }

        /// <summary>A door mat (DoorMat) before a door facing +Z at At.z, Span.x the door's width; on about half the variants.</summary>
        static void DoorMatPart(Grower g, UnitFrame f, UnitPart p) => DoorMat(g, f.At(p).z, f.Span(p).x);

        /// <summary>
        /// A fire ring of radius Span.y (a fire pit's): a sunken ash bed ringed by rough stones of varied size, each turned
        /// its own way, and its collider. Sets the unit's bounds.
        /// </summary>
        static void FireRing(Grower g, UnitFrame f, UnitPart u)
        {
            var s = g.S;
            var b = g.B;
            var radius = f.Span(u).y;
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
            g.Colliders.Add(new Vector4(0f, 0f, radius * 1.1f, radius * 0.4f));
        }

        /// <summary>Three or four charred logs leaning together over a fire of radius Span.y, blackening towards the burning ends.</summary>
        static void LogTepee(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            var radius = f.Span(p).y;
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
        }

        /// <summary>
        /// Embers glowing in the ash of a fire of radius Span.y and on its logs' charred ends, in the first accent colour
        /// (the flames are a landmark Flame effect drawn by FireSystem). Dropped at the far level of detail.
        /// </summary>
        static void Embers(Grower g, UnitFrame f, UnitPart p)
        {
            var b = g.B;
            var radius = f.Span(p).y;
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
        }
    }
}
