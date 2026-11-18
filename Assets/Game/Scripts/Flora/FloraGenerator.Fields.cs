using UnityEngine;

namespace Punchy.Flora
{
    public static partial class FloraGenerator
    {
        static readonly Color Soil = new Color(0.32f, 0.22f, 0.14f);
        static readonly Color Straw = new Color(0.72f, 0.62f, 0.36f);
        static readonly Color HedgeGreen = new Color(0.17f, 0.3f, 0.11f);
        static readonly Color AdobeMud = new Color(0.62f, 0.45f, 0.31f);
        static readonly Color DryBrush = new Color(0.5f, 0.41f, 0.27f);

        static void Field(Grower g)
        {
            var s = g.S;
            var b = g.B;
            // Its own stream, so every LOD lays the same rows, patches and props.
            var rng = new System.Random(g.Seed * 7919 + s.Seed);
            float U() => (float)rng.NextDouble();
            float hx = s.FieldSize.x * 0.5f, hz = s.FieldSize.y * 0.5f;
            var border = Mathf.Lerp(s.Height.x, s.Height.y, U());
            g.Height = border;
            g.Width = Mathf.Max(hx, hz) * 2f;
            g.TrunkRadius = 0f;

            var spacing = s.Crop switch
            {
                CropKind.Beans => 1.3f,
                CropKind.Agave => 1.7f,
                CropKind.Cabbage or CropKind.Kale => 0.8f,
                _ => 0.95f
            };
            var inner = 1.2f;
            var rows = Mathf.Max(2, Mathf.FloorToInt((hz - inner) * 2f / spacing) + 1);
            var hut = g.Variant % 3 == 2 && hx > 5f && hz > 4.5f;
            var hutAt = new Vector3((g.Variant / 3 % 2 == 0 ? -1f : 1f) * (hx - 2.3f), 0f, -hz + 2.1f);

            var strips = 2 + rng.Next(3);
            var growth = new float[strips];
            for (int k = 0; k < strips; k++)
                growth[k] = Mathf.Lerp(0.45f, 1f, U());
            var fails = new Vector3[1 + rng.Next(3)];
            for (int k = 0; k < fails.Length; k++)
                fails[k] = new Vector3(Mathf.Lerp(-hx, hx, U()) * 0.8f, Mathf.Lerp(-hz, hz, U()) * 0.8f, Mathf.Lerp(1.2f, 3f, U()));
            var soil = Soil.linear;
            var straw = Straw.linear;
            var leaf = s.Foliage.linear;
            var leaf2 = s.FoliageVariation.linear;
            var ripe = g.HasAccent ? g.Accent : leaf2;
            var step = s.Crop switch
            {
                CropKind.Grain => 1.1f,
                CropKind.Beans => 1.8f,
                CropKind.Agave => 1.7f,
                CropKind.Potato => 0.7f,
                _ => 0.6f
            } * (g.Lod == 1 ? 1.8f : 1f);

            for (int r = 0; r < rows; r++)
            {
                var z = Mathf.Lerp(-hz + inner, hz - inner, r / (rows - 1f));
                var x0 = -hx + inner;
                var x1 = hx - inner;
                if (g.Lod < 2)
                {
                    b.Family = Punchy.Terrain.MaterialFamily.Plaster;   // tilled earth reads as the mud family
                    b.Box(new Vector3(0f, 0.06f, z), new Vector3(x1 - x0, 0.14f, spacing * 0.45f), Quaternion.identity, soil * Mathf.Lerp(0.85f, 1.1f, U()), FloraPart.Bark, 0.8f);
                    b.Family = b.BaseFamily;
                }
                var count = Mathf.Max(2, Mathf.FloorToInt((x1 - x0) / step) + 1);
                for (int k = 0; k < count; k++)
                {
                    var x = Mathf.Lerp(x0, x1, k / (count - 1f));
                    var j = U();
                    var strip = Mathf.Clamp((int)((x - x0) / (x1 - x0) * strips), 0, strips - 1);
                    var grow = growth[strip] * Mathf.Lerp(0.85f, 1.1f, U());
                    var tint = Color.Lerp(leaf, leaf2, U());
                    var seed = U() * 50f;
                    var failed = false;
                    foreach (var f in fails)
                        if (new Vector2(x - f.x, z - f.y).sqrMagnitude < f.z * f.z * Mathf.Lerp(0.6f, 1.2f, j))
                            failed = true;
                    if (hut && Mathf.Abs(x - hutAt.x) < 2f && Mathf.Abs(z - hutAt.z) < 1.8f)
                        continue;
                    if (failed)
                    {
                        if (j < 0.5f)
                            continue;
                        grow *= 0.4f;
                        tint = Color.Lerp(tint, straw, 0.7f);
                    }
                    if (g.Lod < 2)
                        Crop(g, s.Crop, new Vector3(x + (j - 0.5f) * 0.12f, 0.12f, z), grow, step, tint, ripe, straw, seed, j);
                }
                if (g.Lod == 2)
                    for (int k = 0; k < strips; k++)
                    {
                        var a = Mathf.Lerp(x0, x1, k / (float)strips);
                        var c = Mathf.Lerp(x0, x1, (k + 1f) / strips);
                        var col = Color.Lerp(Color.Lerp(leaf, leaf2, 0.5f), ripe, s.Crop == CropKind.Grain ? growth[k] * 0.8f : 0.15f);
                        var high = CropHeight(s.Crop) * growth[k];
                        b.Box(new Vector3((a + c) * 0.5f, high * 0.5f + 0.06f, z), new Vector3(c - a, high + 0.12f, spacing * 0.75f), Quaternion.identity, col, FloraPart.Foliage);
                    }
            }

            var prop = g.Variant % 3;
            var spot = new Vector3(Mathf.Lerp(-hx, hx, U()) * 0.5f, 0f, Mathf.Lerp(-hz, hz, U()) * 0.4f);
            var yaw = U() * 360f;
            FieldEdge(g, hx, hz, border, rng);
            if (g.Lod == 2)
                return;
            if (prop == 0)
                Scarecrow(g, spot, yaw, straw);
            else if (prop == 1)
                ToolRack(g, new Vector3(2.4f, 0f, hz - 0.9f));
            else if (hut)
                FieldHut(g, hutAt, hutAt.x < 0f ? 1f : -1f, straw);
        }

        static float CropHeight(CropKind crop) => crop switch
        {
            CropKind.Grain => 0.9f,
            CropKind.Beans => 1.9f,
            CropKind.Agave => 0.9f,
            CropKind.Potato => 0.35f,
            CropKind.Kale => 0.6f,
            CropKind.GlowHerb => 0.45f,
            _ => 0.4f
        };

        static void Crop(Grower g, CropKind crop, Vector3 at, float grow, float step, Color leaf, Color ripe, Color straw, float seed, float j)
        {
            var b = g.B;
            var h = CropHeight(crop) * grow;
            var detail = g.Lod == 0 ? 1 : 0;
            switch (crop)
            {
                case CropKind.Grain:
                    var col = Color.Lerp(leaf, ripe, Mathf.Clamp01(grow * 1.2f - 0.3f));
                    b.Box(at + new Vector3(0f, h * 0.4f, 0f), new Vector3(step * 0.95f, h * 0.8f, 0.42f), Quaternion.Euler(0f, 0f, (j - 0.5f) * 6f), col * 0.85f, FloraPart.Foliage);
                    b.Box(at + new Vector3(0f, h * 0.88f, 0f), new Vector3(step * 0.9f, h * 0.18f, 0.5f), Quaternion.Euler(0f, 0f, (j - 0.5f) * 8f), Color.Lerp(col, ripe, 0.5f), FloraPart.Foliage);
                    break;
                case CropKind.Cabbage:
                    var r = 0.22f * grow + 0.06f;
                    b.Blob(at + new Vector3(0f, r * 0.6f, 0f), new Vector3(r, r * 0.8f, r), Quaternion.identity, detail, 0.35f, seed, leaf, FloraPart.Foliage);
                    break;
                case CropKind.Kale:
                    var k = 0.16f * grow + 0.05f;
                    b.Blob(at + new Vector3(0f, h * 0.55f, 0f), new Vector3(k, h * 0.55f, k), Quaternion.identity, detail, 0.6f, seed, leaf * 0.8f, FloraPart.Foliage);
                    break;
                case CropKind.Potato:
                    b.Family = Punchy.Terrain.MaterialFamily.Plaster;
                    b.Blob(at + new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.12f, 0.24f), Quaternion.identity, 0, 0.2f, seed, Soil.linear * 0.9f, FloraPart.Bark);
                    b.Family = b.BaseFamily;
                    b.Blob(at + new Vector3(0f, h * 0.6f + 0.06f, 0f), new Vector3(0.24f * grow + 0.05f, h * 0.5f, 0.2f * grow + 0.05f), Quaternion.identity, 0, 0.5f, seed + 3f, leaf, FloraPart.Foliage);
                    break;
                case CropKind.Beans:
                    var top = at + new Vector3(0f, 1.9f, 0f);
                    b.Family = Timber(g);
                    for (int p = 0; p < 3; p++)
                    {
                        var a = (p / 3f + j) * Mathf.PI * 2f;
                        b.Beam(at + new Vector3(Mathf.Cos(a) * 0.35f, -0.05f, Mathf.Sin(a) * 0.35f), top, 0.035f, 0.035f, straw * 0.7f, FloraPart.Bark);
                    }
                    b.Family = b.BaseFamily;
                    b.Blob(at + new Vector3(0f, h * 0.45f, 0f), new Vector3(0.32f, h * 0.48f, 0.32f), Quaternion.identity, detail, 0.7f, seed, leaf, FloraPart.Foliage, 1f, 0f, 2f);
                    break;
                case CropKind.Agave:
                    for (int p = 0; p < 6; p++)
                    {
                        var a = (p / 6f + j) * Mathf.PI * 2f;
                        b.Beam(at, at + new Vector3(Mathf.Cos(a) * 0.55f * grow, h, Mathf.Sin(a) * 0.55f * grow), 0.12f, 0.03f, leaf, FloraPart.Foliage);
                    }
                    b.Beam(at, at + new Vector3(0f, h * 1.1f, 0f), 0.1f, 0.05f, leaf * 0.9f, FloraPart.Foliage);
                    break;
                case CropKind.GlowHerb:
                    var herb = 0.2f * grow + 0.06f;
                    b.Blob(at + new Vector3(0f, herb * 0.7f, 0f), new Vector3(herb, herb * 0.9f, herb), Quaternion.identity, detail, 0.6f, seed, leaf, FloraPart.Foliage);
                    if (grow < 0.5f)
                        break;
                    for (int p = 0; p < 3; p++)
                    {
                        var a = (p / 3f + j) * Mathf.PI * 2f;
                        var tip = at + new Vector3(Mathf.Cos(a) * herb * 0.5f, h, Mathf.Sin(a) * herb * 0.5f);
                        b.Beam(at + Vector3.up * herb, tip, 0.02f, 0.015f, leaf * 0.8f, FloraPart.Foliage);
                        b.Blob(tip, Vector3.one * 0.06f, Quaternion.identity, 0, 0.2f, seed + p, ripe, FloraPart.Glow);
                    }
                    break;
            }
        }

        static void FieldEdge(Grower g, float hx, float hz, float height, System.Random rng)
        {
            var b = g.B;
            var kind = g.S.Border;
            var wood = g.Bark;
            const float gate = 1.4f;
            float U() => (float)rng.NextDouble();
            b.Family = kind is FieldBorder.DryStone or FieldBorder.StoneKerb ? Punchy.Terrain.MaterialFamily.Stone
                     : kind == FieldBorder.Adobe ? Punchy.Terrain.MaterialFamily.Plaster
                     : kind == FieldBorder.Hedge ? b.BaseFamily : Timber(g);
            var corners = new[] { new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz), new Vector3(-hx, 0f, -hz) };
            foreach (var c in corners)
                g.Perches.Add(c + Vector3.up * height);
            for (int side = 0; side < 4; side++)
            {
                var a = corners[side];
                var e = corners[(side + 1) % 4];
                var runs = side == 0
                    ? new[] { (a, new Vector3(-gate, 0f, hz)), (new Vector3(gate, 0f, hz), e) }
                    : new[] { (a, e) };
                foreach (var (p, q) in runs)
                {
                    if (g.Lod == 0)
                        FenceColliders(g, p, q, height);
                    var len = Vector3.Distance(p, q);
                    var rot = Quaternion.LookRotation((q - p).normalized, Vector3.up);
                    var mid = (p + q) * 0.5f;
                    switch (kind)
                    {
                        case FieldBorder.Hedge:
                            var hedge = HedgeGreen.linear;
                            if (g.Lod == 2)
                            {
                                b.Box(mid + Vector3.up * height * 0.5f, new Vector3(0.9f, height, len), rot, hedge, FloraPart.Foliage);
                                break;
                            }
                            var bushes = Mathf.Max(1, Mathf.RoundToInt(len / (g.Lod == 0 ? 1.4f : 2.8f)));
                            for (int k = 0; k < bushes; k++)
                            {
                                var size = U() * 0.25f + 0.85f;
                                b.Blob(Vector3.Lerp(p, q, (k + 0.5f) / bushes) + Vector3.up * height * 0.5f * size, new Vector3(0.6f, height * 0.55f * size, len / bushes * 0.7f), rot,
                                       g.Lod == 0 ? 1 : 0, 0.5f, U() * 50f, hedge * size, FloraPart.Foliage, 1f, 0.3f, 2f);
                            }
                            break;
                        case FieldBorder.DryStone:
                            var stone = new Color(0.46f, 0.44f, 0.4f).linear;
                            if (g.Lod == 2)
                            {
                                b.Box(mid + Vector3.up * height * 0.45f, new Vector3(0.6f, height * 0.9f, len), rot, stone, FloraPart.Bark);
                                break;
                            }
                            var blocks = Mathf.Max(1, Mathf.RoundToInt(len / 0.7f));
                            for (int course = 0; course < 2; course++)
                                for (int k = 0; k < blocks; k++)
                                {
                                    var t = (k + 0.5f + course * 0.5f) / blocks;
                                    var shade = U() * 0.3f + 0.8f;
                                    var twist = (U() - 0.5f) * 8f;
                                    if (t > 1f)
                                        continue;
                                    b.Box(Vector3.Lerp(p, q, t) + Vector3.up * height * (0.22f + course * 0.45f), new Vector3(0.6f - course * 0.12f, height * 0.44f, len / blocks * 0.96f),
                                          rot * Quaternion.Euler(0f, twist, 0f), stone * shade, FloraPart.Bark);
                                }
                            break;
                        case FieldBorder.Adobe:
                            var mud = AdobeMud.linear;
                            if (g.Lod == 2)
                            {
                                b.Box(mid + Vector3.up * height * 0.45f, new Vector3(0.45f, height * 0.9f, len), rot, mud, FloraPart.Bark);
                                break;
                            }
                            var spans = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
                            for (int k = 0; k < spans; k++)
                            {
                                var at = Vector3.Lerp(p, q, (k + 0.5f) / spans);
                                var tall = height * (U() * 0.25f + 0.8f);
                                var shade = U() * 0.2f + 0.88f;
                                b.Box(at + Vector3.up * tall * 0.5f, new Vector3(0.42f, tall, len / spans * 1.01f), rot, mud * shade, FloraPart.Bark);
                                if (g.Lod == 0)
                                    b.Box(at + Vector3.up * (tall + 0.04f), new Vector3(0.5f, 0.08f, len / spans * 0.9f), rot, mud * shade * 0.85f, FloraPart.Bark);
                            }
                            break;
                        case FieldBorder.Brush:
                            var dry = DryBrush.linear;
                            if (g.Lod == 2)
                            {
                                b.Box(mid + Vector3.up * height * 0.4f, new Vector3(0.6f, height * 0.8f, len), rot, dry, FloraPart.Bark);
                                break;
                            }
                            var stakes = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
                            for (int k = 0; k <= stakes; k++)
                                b.Box(Vector3.Lerp(p, q, k / (float)stakes) + Vector3.up * height * 0.55f, new Vector3(0.08f, height * 1.1f, 0.08f),
                                      rot * Quaternion.Euler((U() - 0.5f) * 10f, 0f, 0f), wood * 0.8f, FloraPart.Bark);
                            b.Family = Punchy.Terrain.MaterialFamily.Thatch;
                            for (int k = 0; k < stakes; k++)
                            {
                                var size = U() * 0.3f + 0.8f;
                                b.Blob(Vector3.Lerp(p, q, (k + 0.5f) / stakes) + Vector3.up * height * 0.4f * size, new Vector3(0.35f, height * 0.42f * size, len / stakes * 0.62f),
                                       rot * Quaternion.Euler(0f, (U() - 0.5f) * 14f, 0f), 0, 0.7f, U() * 50f, dry * (U() * 0.3f + 0.8f), FloraPart.Bark);
                            }
                            b.Family = Timber(g);
                            break;
                        case FieldBorder.StoneKerb:
                            var pale = new Color(0.64f, 0.64f, 0.6f).linear;
                            if (g.Lod == 2)
                            {
                                b.Box(mid + Vector3.up * height * 0.4f, new Vector3(0.5f, height * 0.8f, len), rot, pale, FloraPart.Bark);
                                break;
                            }
                            var kerbs = Mathf.Max(1, Mathf.RoundToInt(len / 0.55f));
                            for (int k = 0; k < kerbs; k++)
                            {
                                var size = U() * 0.3f + 0.8f;
                                b.Blob(Vector3.Lerp(p, q, (k + 0.5f) / kerbs) + Vector3.up * height * 0.35f * size, new Vector3(0.28f, height * 0.4f * size, len / kerbs * 0.55f), rot,
                                       0, 0.3f, U() * 50f, pale * (U() * 0.25f + 0.8f), FloraPart.Bark);
                            }
                            break;
                        default:
                            var wattle = kind == FieldBorder.Wattle;
                            var posts = Mathf.Max(1, Mathf.RoundToInt(len / (wattle ? 1.2f : 2.4f)));
                            for (int k = 0; k <= posts; k++)
                            {
                                var shade = U() * 0.25f + 0.85f;
                                if (g.Lod < 2)
                                    b.Box(Vector3.Lerp(p, q, k / (float)posts) + Vector3.up * height * 0.5f, new Vector3(0.1f, height, 0.1f), rot, wood * shade, FloraPart.Bark);
                            }
                            if (g.Lod == 2)
                                break;
                            foreach (var y in wattle ? new[] { 0.25f, 0.45f, 0.65f, 0.85f } : new[] { 0.4f, 0.8f })
                                b.Box(mid + Vector3.up * height * y, new Vector3(0.07f, wattle ? 0.16f : 0.07f, len), rot, wood * (wattle ? 0.8f : 1f), FloraPart.Bark);
                            break;
                    }
                }
            }
            b.Family = b.BaseFamily;
        }

        static void Scarecrow(Grower g, Vector3 at, float yaw, Color straw)
        {
            var b = g.B;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            b.Family = Timber(g);
            b.Box(at + Vector3.up * 0.95f, new Vector3(0.08f, 1.9f, 0.08f), rot, g.Bark, FloraPart.Bark);
            b.Box(at + Vector3.up * 1.45f, new Vector3(1.3f, 0.07f, 0.07f), rot, g.Bark, FloraPart.Bark);
            b.Family = Punchy.Terrain.MaterialFamily.Cloth;
            b.Box(at + Vector3.up * 1.3f, new Vector3(0.5f, 0.6f, 0.24f), rot, new Color(0.42f, 0.3f, 0.22f).linear, FloraPart.Bark);
            b.Family = Punchy.Terrain.MaterialFamily.Thatch;
            b.Blob(at + Vector3.up * 1.78f, new Vector3(0.15f, 0.18f, 0.15f), rot, 0, 0.3f, 5f, straw, FloraPart.Bark);
            b.Blob(at + Vector3.up * 1.95f, new Vector3(0.28f, 0.06f, 0.28f), rot, 0, 0.2f, 9f, straw * 0.7f, FloraPart.Bark);
            b.Family = b.BaseFamily;
            g.Perches.Add(at + Vector3.up * 1.98f);
        }

        static void FieldHut(Grower g, Vector3 at, float side, Color straw)
        {
            var b = g.B;
            const float w = 2.6f, d = 2.2f, eave = 1.9f;
            b.Family = Timber(g);
            foreach (var cx in new[] { -w * 0.5f, w * 0.5f })
                foreach (var cz in new[] { -d * 0.5f, d * 0.5f })
                    b.Box(at + new Vector3(cx, eave * 0.5f, cz), new Vector3(0.12f, eave, 0.12f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Box(at + new Vector3(0f, eave * 0.45f, -d * 0.5f), new Vector3(w, eave * 0.9f, 0.08f), Quaternion.identity, g.Bark * 0.85f, FloraPart.Bark);
            b.Box(at + new Vector3(-side * w * 0.5f, eave * 0.45f, 0f), new Vector3(0.08f, eave * 0.9f, d), Quaternion.identity, g.Bark * 0.8f, FloraPart.Bark);
            b.Box(at + new Vector3(side * 0.5f, 0.35f, 0.1f), new Vector3(0.9f, 0.08f, 0.5f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Family = Punchy.Terrain.MaterialFamily.Thatch;
            foreach (var s in new[] { -1f, 1f })
                b.Box(at + new Vector3(0f, eave + 0.38f, s * d * 0.3f), new Vector3(w + 0.5f, 0.16f, d * 0.68f), Quaternion.Euler(s * 30f, 0f, 0f), straw * 0.85f, FloraPart.Bark);
            b.Blob(at + new Vector3(side * 0.6f, 0.25f, 0.3f), new Vector3(0.3f, 0.25f, 0.3f), Quaternion.identity, 0, 0.3f, 7f, straw, FloraPart.Bark);
            b.Family = b.BaseFamily;
            if (g.Lod == 0)
            {
                FenceColliders(g, at + new Vector3(-w * 0.5f, 0f, -d * 0.5f), at + new Vector3(w * 0.5f, 0f, -d * 0.5f), eave);
                FenceColliders(g, at + new Vector3(-side * w * 0.5f, 0f, -d * 0.5f), at + new Vector3(-side * w * 0.5f, 0f, d * 0.5f), eave);
            }
            g.Perches.Add(at + Vector3.up * (eave + 0.7f));
        }

        static void ToolRack(Grower g, Vector3 at)
        {
            var b = g.B;
            b.Family = Timber(g);
            foreach (var x in new[] { -0.5f, 0.5f })
                b.Box(at + new Vector3(x, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Box(at + new Vector3(0f, 1.1f, 0f), new Vector3(1.1f, 0.07f, 0.07f), Quaternion.identity, g.Bark, FloraPart.Bark);
            for (int k = 0; k < 3; k++)
            {
                var x = -0.3f + k * 0.3f;
                b.Beam(at + new Vector3(x, 0f, -0.25f), at + new Vector3(x + 0.05f, 1.35f, 0.05f), 0.04f, 0.04f, g.Bark * 0.8f, FloraPart.Bark);
            }
            b.Family = Punchy.Terrain.MaterialFamily.Metal;
            b.Box(at + new Vector3(-0.3f, 0.06f, -0.27f), new Vector3(0.22f, 0.1f, 0.03f), Quaternion.identity, new Color(0.3f, 0.3f, 0.32f).linear, FloraPart.Metal);
            b.Family = Punchy.Terrain.MaterialFamily.Thatch;
            b.Blob(at + new Vector3(0.75f, 0.18f, 0.1f), new Vector3(0.22f, 0.18f, 0.22f), Quaternion.identity, 0, 0.2f, 3f, Straw.linear * 0.8f, FloraPart.Bark, 1f, 0.5f);
            b.Family = b.BaseFamily;
        }
    }
}
