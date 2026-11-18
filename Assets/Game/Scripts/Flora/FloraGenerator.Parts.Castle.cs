using System.Collections.Generic;
using UnityEngine;
using Punchy.Terrain;

namespace Punchy.Flora
{
    // The castle parts (W-182, #254): kit stone through DrawBuildParts so MaterialLibrary sets the family; UnitPart.Wear ages them.
    public static partial class FloraGenerator
    {
        public static readonly string[] CastlePartIds = { "curtain wall", "castle tower", "gatehouse", "portcullis" };

        static void AddCastleParts(Dictionary<string, PartBuild> library)
        {
            library["curtain wall"] = CurtainWallPart;
            library["castle tower"] = CastleTowerPart;
            library["gatehouse"] = GatehousePart;
            library["portcullis"] = PortcullisPart;
        }

        static Vector3 Turned(float yaw, Vector3 v) => Quaternion.Euler(0f, yaw, 0f) * v;

        sealed class Castle
        {
            public readonly List<KitPart> Parts = new List<KitPart>();
            readonly List<bool> fine = new List<bool>();
            readonly List<KitBox> boxes = new List<KitBox>();

            public void Add(KitPiece piece, KitMaterial m, Vector3 centre, Vector3 size, Quaternion q, bool isFine = false)
            {
                Piece(Parts, piece, m, centre, size, q);
                fine.Add(isFine);
            }

            public void Box(Vector3 centre, Vector3 size, Quaternion q) =>
                boxes.Add(new KitBox { Centre = centre, Half = size * 0.5f, Rotation = q, Reach = size.magnitude * 0.5f });

            public void Block(Vector3 centre, Vector3 size, Quaternion q)
            {
                Add(KitPiece.Wall, KitMaterial.Stone, centre, size, q);
                Box(centre, size, q);
            }

            public void Draw(Grower g)
            {
                var drawn = Parts;
                if (g.Lod >= 2)
                {
                    drawn = new List<KitPart>();
                    for (int i = 0; i < Parts.Count; i++)
                        if (!fine[i])
                            drawn.Add(Parts[i]);
                }
                var first = g.Boxes.Count;
                DrawBuildParts(g, drawn);
                if (g.Lod != 0)
                    return;
                g.Boxes.RemoveRange(first, g.Boxes.Count - first);
                g.Boxes.AddRange(boxes);
            }
        }

        static void CurtainWallPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 a = f.At(p), b = f.To(p), s = SpanOr(f, p, new Vector3(0f, 6f, 2.6f));
            var length = Mathf.Max(0.5f, Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
            var q = Along(a, b);
            var mid = (a + b) * 0.5f;
            Vector3 At(float x, float y, float z) => mid + q * new Vector3(x, y, z);
            float h = s.y, t = s.z, wear = p.Wear;
            var n = Mathf.Max(1, Mathf.RoundToInt(length / 2.5f));
            var w = length / n;
            var heights = new float[n];
            for (int k = 0; k < n; k++)
                heights[k] = h;
            if (wear > 0.6f && n >= 3)
            {
                var dip = Mathf.Min(n - 2, 1 + (int)(g.R() * (n - 2)));
                heights[dip] = h * g.Range(0.3f, 0.5f);
                if (wear > 0.8f)
                {
                    heights[dip - 1] = h * g.Range(0.6f, 0.8f);
                    heights[dip + 1] = h * g.Range(0.55f, 0.8f);
                }
            }
            var c = new Castle();
            c.Block(At(0f, 0.4f, 0f), new Vector3(length, 0.8f, t + 0.3f), q);
            for (int k = 0; k < n;)
            {
                var run = 1;
                while (k + run < n && heights[k + run] == heights[k])
                    run++;
                float x0 = -length * 0.5f + k * w, len = run * w, cx = x0 + len * 0.5f, top = heights[k];
                c.Block(At(cx, top * 0.5f, 0f), new Vector3(len, top, t), q);
                if (top == h)
                {
                    c.Add(KitPiece.Trim, KitMaterial.Stone, At(cx, h - 0.15f, 0f), new Vector3(len, 0.3f, t + 0.24f), q);
                    Battlement(g, c, At(x0, 0f, t * 0.5f - 0.3f), At(x0 + len, 0f, t * 0.5f - 0.3f), h, 0.6f, wear);
                    c.Add(KitPiece.Trim, KitMaterial.Stone, At(cx, h + 0.2f, -t * 0.5f + 0.15f), new Vector3(len, 0.4f, 0.3f), q);
                }
                else
                {
                    RaggedTop(g, c, At(cx, top, 0f), q, len, t, wear);
                    foreach (var side in new[] { -1f, 1f })
                        Fallen(g, c, At(cx, 0f, side * (t * 0.5f + 0.9f)), q, len * 0.5f, 6 + (int)(wear * 6f));
                }
                Streaks(g, c, At(cx, top, 0f), q, len, t * 0.5f, top, wear);
                Streaks(g, c, At(cx, top, 0f), q, len, -t * 0.5f, top, wear);
                k += run;
            }
            if (wear > 0.5f)
                Fallen(g, c, At(g.Range(-0.3f, 0.3f) * length, 0f, t * 0.5f + 0.6f), q, 1.2f, 3);   // merlons fallen at the foot
            c.Draw(g);
        }

        static void CastleTowerPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(6.5f, 11f, 0f));
            float side = s.x, h = s.y, wear = p.Wear, half = side * 0.5f;
            var standing = wear > 0.6f ? h * (1f - (wear - 0.6f) * 0.5f) : h;
            var c = new Castle();
            var flat = Quaternion.identity;
            c.Block(at + new Vector3(0f, 0.5f, 0f), new Vector3(side + 0.5f, 1f, side + 0.5f), flat);
            c.Block(at + new Vector3(0f, standing * 0.5f, 0f), new Vector3(side, standing, side), flat);
            for (int face = 0; face < 4; face++)
            {
                var yaw = face * 90f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                for (var y = 2.5f; y < standing - 1.5f; y += 3.2f)
                    c.Add(KitPiece.Window, KitMaterial.Opening, at + Turned(yaw, new Vector3(side * 0.27f, y, half + 0.01f)), new Vector3(0.22f, 1.1f, 0.06f), q, true);
                Streaks(g, c, at + Turned(yaw, new Vector3(0f, standing, 0f)), q, side, half, standing, wear);
                if (standing < h)
                    continue;
                for (int k = 0, corbels = Mathf.Max(3, Mathf.RoundToInt(side / 0.9f)); k < corbels; k++)
                    c.Add(KitPiece.Trim, KitMaterial.Stone, at + Turned(yaw, new Vector3(-half + (k + 0.5f) * side / corbels, h - 0.6f, half + 0.12f)), new Vector3(0.3f, 0.45f, 0.3f), q);
                var inset = half - 0.3f;
                var reach = face % 2 == 0 ? half : half - 0.6f;
                Battlement(g, c, at + Turned(yaw, new Vector3(-reach, 0f, inset)), at + Turned(yaw, new Vector3(reach, 0f, inset)), h, 0.6f, wear);
            }
            if (standing < h)
            {
                RaggedTop(g, c, at + new Vector3(0f, standing, 0f), flat, side, side, wear);
                c.Block(at + new Vector3(-half * 0.7f, standing + h * 0.06f, -half * 0.7f), new Vector3(side * 0.3f, h * 0.12f, side * 0.3f), flat);
                Fallen(g, c, at + new Vector3(0f, 0f, half + 1f), flat, half, 8);
            }
            else
                c.Add(KitPiece.Trim, KitMaterial.Stone, at + new Vector3(0f, h - 0.2f, 0f), new Vector3(side + 0.5f, 0.4f, side + 0.5f), flat);
            c.Draw(g);
        }

        static void GatehousePart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(10f, 9f, 7f));
            float width = s.x, h = s.y, depth = s.z, wear = p.Wear;
            var pass = Mathf.Min(3.6f, width * 0.4f);
            var r = pass * 0.5f;
            var spring = Mathf.Min(3.4f, h * 0.4f);
            var ring = 0.6f;
            var lintel = spring + r + ring;
            var pier = (width - pass) * 0.5f;
            var flat = Quaternion.identity;
            var c = new Castle();
            foreach (var side in new[] { -1f, 1f })
            {
                c.Block(at + new Vector3(side * (r + pier * 0.5f), h * 0.5f, 0f), new Vector3(pier, h, depth), flat);
                c.Block(at + new Vector3(side * (r + pier * 0.5f + 0.1f), 0.5f, 0f), new Vector3(pier + 0.2f, 1f, depth + 0.4f), flat);
            }
            c.Block(at + new Vector3(0f, (lintel + h) * 0.5f, 0f), new Vector3(pass, h - lintel, depth), flat);
            const int stones = 9;
            for (int k = 0; k < stones; k++)
            {
                var a = Mathf.PI * (k + 0.5f) / stones;
                var centre = at + new Vector3(Mathf.Cos(a) * (r + ring * 0.5f), spring + Mathf.Sin(a) * (r + ring * 0.5f), 0f);
                var size = new Vector3(2f * (r + ring) * Mathf.Sin(Mathf.PI * 0.5f / stones) + 0.05f, ring, depth + 0.12f);
                var turn = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                c.Add(KitPiece.Arch, KitMaterial.Stone, centre, size, turn);
                c.Box(centre, size, turn);
            }
            const int rows = 6;
            var rowHeight = (lintel - spring) / rows;
            for (int k = 0; k < rows; k++)
            {
                var dy = (k + 0.5f) * rowHeight;
                var inner = Mathf.Sqrt(Mathf.Max(0f, (r + ring) * (r + ring) - dy * dy)) - 0.15f;
                if (inner >= r)
                    continue;
                foreach (var side in new[] { -1f, 1f })
                    c.Add(KitPiece.Wall, KitMaterial.Stone, at + new Vector3(side * (inner + r) * 0.5f, spring + dy, 0f), new Vector3(r - inner, rowHeight + 0.02f, depth), flat);
            }
            for (int face = 0; face < 4; face++)
            {
                var yaw = face * 90f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                float along = face % 2 == 0 ? width : depth, out_ = (face % 2 == 0 ? depth : width) * 0.5f;
                Streaks(g, c, at + Turned(yaw, new Vector3(0f, h, 0f)), q, along, out_, h, wear);
                if (face % 2 == 0)
                    for (int k = 0, corbels = Mathf.Max(3, Mathf.RoundToInt(along / 0.9f)); k < corbels; k++)
                        c.Add(KitPiece.Trim, KitMaterial.Stone, at + Turned(yaw, new Vector3(-along * 0.5f + (k + 0.5f) * along / corbels, h - 0.6f, out_ + 0.12f)), new Vector3(0.3f, 0.45f, 0.3f), q);
                var reach = (face % 2 == 0 ? along : along - 1.2f) * 0.5f;
                Vector3 e0 = at + Turned(yaw, new Vector3(-reach, 0f, out_ - 0.3f)), e1 = at + Turned(yaw, new Vector3(reach, 0f, out_ - 0.3f));
                if (wear > 0.6f && g.R() < wear * 0.6f)
                    RaggedTop(g, c, (e0 + e1) * 0.5f + Vector3.up * h, q, reach * 2f, 0.6f, wear);
                else
                    Battlement(g, c, e0, e1, h, 0.6f, wear);
            }
            c.Add(KitPiece.Trim, KitMaterial.Stone, at + new Vector3(0f, h - 0.2f, 0f), new Vector3(width + 0.5f, 0.4f, depth + 0.5f), flat);
            if (wear > 0.5f)
                foreach (var side in new[] { -1f, 1f })
                    Fallen(g, c, at + new Vector3(side * (r + pier * 0.6f), 0f, side * (depth * 0.5f + 0.8f)), flat, 1.2f, 4 + (int)(wear * 6f));
            c.Draw(g);
        }

        static void PortcullisPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(3.6f, 4.4f, 0.1f));
            var c = new Castle();
            var flat = Quaternion.identity;
            var bars = Mathf.Max(2, Mathf.RoundToInt(s.x / 0.4f)) + 1;
            for (int k = 0; k < bars; k++)
            {
                var x = -s.x * 0.5f + 0.1f + k * (s.x - 0.2f) / (bars - 1);
                c.Add(KitPiece.Beam, KitMaterial.Iron, at + new Vector3(x, s.y * 0.5f, 0f), new Vector3(s.z, s.y, s.z), flat);
                c.Add(KitPiece.Trim, KitMaterial.Iron, at + new Vector3(x, -0.12f, 0f), new Vector3(0.06f, 0.24f, 0.06f), flat);
            }
            for (var y = 0.35f; y < s.y; y += 0.55f)
                c.Add(KitPiece.Beam, KitMaterial.Iron, at + new Vector3(0f, y, s.z * 0.8f), new Vector3(s.x, 0.09f, s.z * 0.8f), flat, true);
            c.Box(at + new Vector3(0f, s.y * 0.5f, s.z * 0.4f), new Vector3(s.x, s.y, s.z * 2f), flat);
            c.Draw(g);
        }

        static void Battlement(Grower g, Castle c, Vector3 a, Vector3 b, float top, float thick, float wear)
        {
            var q = Along(a, b);
            var length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            var mid = (a + b) * 0.5f + Vector3.up * top;
            c.Add(KitPiece.Wall, KitMaterial.Stone, mid + Vector3.up * 0.5f, new Vector3(length, 1f, thick), q);
            var n = Mathf.Max(1, Mathf.RoundToInt(length / 1.5f));
            var high = 1f;
            for (int k = 0; k < n; k++)
            {
                if (wear > 0.3f && g.R() < (wear - 0.2f) * 0.6f)
                    continue;
                c.Add(KitPiece.Wall, KitMaterial.Stone, mid + q * new Vector3(-length * 0.5f + (k + 0.5f) * length / n, 1.4f, 0f), new Vector3(length / n * 0.55f, 0.8f, thick), q, true);
                high = 1.8f;
            }
            c.Box(mid + Vector3.up * high * 0.5f, new Vector3(length, high, thick), q);
            MossTop(g, c, mid + Vector3.up, q, length, thick, wear);
        }

        static void RaggedTop(Grower g, Castle c, Vector3 top, Quaternion q, float length, float thick, float wear)
        {
            for (int k = 0, n = 2 + Mathf.RoundToInt(length / 1.5f); k < n; k++)
            {
                var size = new Vector3(g.Range(0.5f, 1.1f), g.Range(0.3f, 0.9f), g.Range(0.5f, Mathf.Min(1.2f, thick)));
                var at = top + q * new Vector3(g.Range(-0.45f, 0.45f) * length, size.y * 0.3f, g.Range(-0.3f, 0.3f) * thick);
                c.Add(KitPiece.Wall, KitMaterial.Stone, at, size, q * Quaternion.Euler(g.Range(-12f, 12f), g.Range(-25f, 25f), g.Range(-12f, 12f)), true);
            }
            c.Box(top + Vector3.up * 0.25f, new Vector3(length * 0.9f, 0.5f, Mathf.Min(1.2f, thick)), q);
            MossTop(g, c, top, q, length, thick, wear);
        }

        static void Fallen(Grower g, Castle c, Vector3 centre, Quaternion q, float spread, int stones)
        {
            for (int k = 0; k < stones; k++)
            {
                var size = g.Range(0.25f, 0.7f);
                var at = centre + q * new Vector3(g.Range(-1f, 1f) * spread, size * 0.35f, g.Range(-0.6f, 0.6f));
                c.Add(KitPiece.Rubble, KitMaterial.Stone, at, new Vector3(size * 1.3f, size * 0.8f, size), q * Quaternion.Euler(g.Range(-15f, 15f), g.Range(0f, 360f), g.Range(-15f, 15f)), true);
            }
            c.Box(centre + Vector3.up * 0.2f, new Vector3(spread * 2f + 0.5f, 0.4f, 1.6f), q);
        }

        static void MossTop(Grower g, Castle c, Vector3 top, Quaternion q, float length, float thick, float wear)
        {
            if (wear < 0.3f)
                return;
            var n = Mathf.Max(1, Mathf.CeilToInt(length / 3f));
            for (int k = 0; k < n; k++)
                if (g.R() < wear + 0.2f)
                    c.Add(KitPiece.MossCap, KitMaterial.Moss, top + q * new Vector3(-length * 0.5f + (k + 0.5f) * length / n, 0f, 0f),
                          new Vector3(length / n * g.Range(0.6f, 0.95f), Mathf.Min(top.y, 0.25f + wear * 0.6f), thick), q);
        }

        static void Streaks(Grower g, Castle c, Vector3 top, Quaternion q, float length, float face, float height, float wear)
        {
            if (wear < 0.3f)
                return;
            for (int k = 0, n = Mathf.RoundToInt(length / 2.5f * wear); k < n; k++)
            {
                var len = Mathf.Min(height * 0.8f, g.Range(0.8f, 3f) * wear);
                c.Add(KitPiece.Moss, KitMaterial.Moss, top + q * new Vector3(g.Range(-0.45f, 0.45f) * length, -len * 0.5f, face + Mathf.Sign(face) * 0.03f),
                      new Vector3(g.Range(0.3f, 1f), len, 0.05f), q, true);
            }
        }
    }
}
