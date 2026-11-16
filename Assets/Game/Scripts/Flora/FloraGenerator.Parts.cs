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
    }
}
