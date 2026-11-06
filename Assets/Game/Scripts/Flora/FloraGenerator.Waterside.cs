using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, roots, reeds, lily pads and dens. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
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
