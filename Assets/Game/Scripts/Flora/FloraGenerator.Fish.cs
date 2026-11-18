using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, hung fish (#550): a drying rack's or a market stall's catch, shaped and coloured from the real fish
    // species the fishing code lands (FloraSpecies.HungFish), so every rack and stall shares one fish.
    public static partial class FloraGenerator
    {
        /// <summary>
        /// One fish hung head down by its tail inside a kit part's box (KitMaterial.Fish): the box's height is the fish
        /// with its tail fin, its width the fish's depth (back to belly) and its depth the fish's thickness. The body's
        /// proportions (BodyRadius, BodyTaper, BodyFlatten) and colours (a coat, or Base and Accent) come from one of the
        /// species' hung fish; the fins are generic (forked tail, dorsal fin). Glossy (FloraPart.Cactus), never stone.
        /// </summary>
        static void HungFish(Grower g, in Punchy.Terrain.KitPart p, uint seed)
        {
            var b = g.B;
            var pool = g.S.HungFish;
            var pick = seed * 0x9E3779B9u;
            pick ^= pick >> 15;
            pick *= 0x85EBCA77u;
            pick ^= pick >> 13;
            var fish = pool != null && pool.Length > 0 ? pool[(int)(pick % (uint)pool.Length)] : null;
            // A plain silver fish when the species names none.
            float ratio = 0.14f, taper = 0.75f, flatten = 1.45f;
            Color belly = new Color(0.7f, 0.74f, 0.76f), back = new Color(0.22f, 0.34f, 0.4f);
            if (fish != null)
            {
                ratio = Mathf.Clamp(fish.BodyRadius / Mathf.Max(fish.BodyLength, 0.01f), 0.09f, 0.22f);
                taper = fish.BodyTaper;
                flatten = fish.BodyFlatten;
                belly = fish.Base;
                back = fish.Accent;
                if (fish.Coats != null && fish.Coats.Count > 0)
                {
                    var coat = fish.Coats[(int)((pick >> 8) % (uint)fish.Coats.Count)];
                    belly = coat.Base;
                    back = coat.Accent;
                }
            }
            // Dried a little: duller and darker than in the water, the back darker than the belly.
            belly = Color.Lerp(belly, new Color(0.62f, 0.58f, 0.5f), 0.2f) * 1.05f;
            back = Color.Lerp(back, belly, 0.25f) * 0.75f;

            var r = p.Rotation;
            var top = p.Centre + r * new Vector3(0f, p.Size.y * 0.5f, 0f);
            var tailLength = p.Size.y * 0.22f;
            var length = p.Size.y - tailLength;
            var radius = Mathf.Min(length * ratio, p.Size.x * 0.5f / Mathf.Max(flatten, 0.5f));
            var dorsal = r * Vector3.right;    // back (+) to belly (-)
            var side = r * Vector3.forward;
            var down = r * Vector3.down;
            var sides = g.Lod == 0 ? 8 : 6;
            var steps = g.Lod == 0 ? 9 : 5;
            var peduncle = top + down * tailLength;
            var tilt = ((pick >> 20) & 0xFF) / 255f;

            // Body: rings from the tail's narrow root down to the head, a spindle swelling a little past the middle.
            var rings = new List<Vector3[]>();
            var colours = new List<Color[]>();
            var ao = new List<float[]>();
            for (int k = 0; k <= steps; k++)
            {
                var s = k / (float)steps;              // 0 at the tail root, 1 at the snout
                var shape = k == steps ? 0.06f : Mathf.Sin(Mathf.Lerp(0.18f, 0.92f, s) * Mathf.PI) * Mathf.Lerp(taper, 1f, s);
                var rad = radius * Mathf.Max(shape, 0.06f);
                var centre = peduncle + down * (length * s);
                var ring = new Vector3[sides];
                var cols = new Color[sides];
                var occ = new float[sides];
                for (int j = 0; j < sides; j++)
                {
                    var a = j / (float)sides * Mathf.PI * 2f;
                    var up = Mathf.Cos(a);
                    ring[j] = centre + dorsal * (up * rad * flatten) + side * (Mathf.Sin(a) * rad);
                    cols[j] = Color.Lerp(belly, back, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, 0.7f, up)));
                    occ[j] = Mathf.Lerp(0.8f, 1f, up * 0.5f + 0.5f);
                }
                rings.Add(ring);
                colours.Add(cols);
                ao.Add(occ);
            }
            b.Rings(rings, colours, ao, FloraPart.Cactus);

            // Tail: two lobes forking up from the root, flat across the fish's depth.
            var fin = Color.Lerp(back, belly, 0.6f) * 0.95f;
            foreach (var lobe in new[] { -1f, 1f })
            {
                var tip = peduncle - down * tailLength + dorsal * (lobe * tailLength * (0.55f + tilt * 0.15f));
                b.Strip(new[] { peduncle + down * (length * 0.02f), tip }, new[] { radius * 0.3f, radius * 0.6f }, dorsal, fin, FloraPart.Cactus, 0.9f, side);
            }
            if (g.Lod == 0)
            {
                // Dorsal fin along the back, a little behind the middle; a dark eye each side near the snout.
                var from = peduncle + down * (length * 0.3f) + dorsal * (radius * flatten * 0.9f);
                var to = peduncle + down * (length * 0.62f) + dorsal * (radius * flatten * 0.9f);
                b.Strip(new[] { from, Vector3.Lerp(from, to, 0.3f), to }, new[] { 0.002f, radius * 1.1f, 0.002f }, dorsal, fin, FloraPart.Cactus, 0.9f, side);
                var eyeAt = peduncle + down * (length * 0.86f) + dorsal * (radius * flatten * 0.25f);
                foreach (var e in new[] { -1f, 1f })
                    b.Diamond(eyeAt + side * (e * radius * 0.62f), radius * 0.18f, new Color(0.06f, 0.06f, 0.07f), FloraPart.Cactus);
            }
        }
    }
}
