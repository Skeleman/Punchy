using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the icebergs (W-28): every berg's faceted mesh, built once from the same IcebergShape the player stands on,
    /// merged into one mesh per 1 km region so the whole set costs a draw call per region in view, plus one mesh for the
    /// cave berg's tunnel and chamber in the glowing cave ice. Cosmetic and seed-derived, so every client builds the same.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class IcebergSystem : SystemBase
    {
        const float Region = 1024f;

        /// <summary>What the last build made, for the report: regions (draw calls), triangles, and the cave's triangles.</summary>
        public static string Stats = "";

        BlobAssetReference<IcebergSetBlob> builtFrom;
        readonly List<(Mesh mesh, Vector3 origin)> regions = new List<(Mesh, Vector3)>();
        Mesh caveMesh;
        GameObject caveLight;
        Vector3 caveOrigin;
        Material ice, caveIce;
        Texture2D palette;
        bool warned;

        protected override void OnCreate() => RequireForUpdate<TerrainSource>();

        protected override void OnUpdate()
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            if (!source.Icebergs.IsCreated)
                return;
            if (source.Icebergs != builtFrom)
            {
                builtFrom = source.Icebergs;
                Build(source);
            }
            if (ice == null || caveIce == null)
            {
                var loaded = Resources.Load<Material>("IcebergIce");
                caveIce = Resources.Load<Material>("IcebergCave");
                if (loaded == null || caveIce == null)
                {
                    if (!warned)
                        Debug.LogWarning("[icebergs] IcebergIce or IcebergCave material missing from Resources");
                    warned = true;
                    return;
                }
                // A copy that reads its colour from the palette strip (the asset stays untouched).
                palette = MakePalette();
                ice = new Material(loaded) { name = "Iceberg ice (palette)" };
                ice.SetTexture("_BaseMap", palette);
                ice.SetColor("_BaseColor", Color.white);
            }
            foreach (var (mesh, origin) in regions)
            {
                var b = mesh.bounds;
                b.center += origin;
                Graphics.RenderMesh(new RenderParams(ice) { worldBounds = b, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, mesh, 0, Matrix4x4.Translate(origin));
            }
            if (caveMesh != null)
            {
                var b = caveMesh.bounds;
                b.center += caveOrigin;
                Graphics.RenderMesh(new RenderParams(caveIce) { worldBounds = b, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true }, caveMesh, 0, Matrix4x4.Translate(caveOrigin));
            }
        }

        protected override void OnDestroy()
        {
            Clear();
            if (ice != null) Object.Destroy(ice);
            if (palette != null) Object.Destroy(palette);
        }

        void Clear()
        {
            foreach (var (mesh, _) in regions)
                Object.Destroy(mesh);
            regions.Clear();
            if (caveMesh != null)
                Object.Destroy(caveMesh);
            caveMesh = null;
            if (caveLight != null)
                Object.Destroy(caveLight);
            caveLight = null;
        }

        /// <summary>
        /// W-28b: the ice's colours along a 64x1 strip the meshes index with u: snow (0), pale and deeper blue faces (0.3 to
        /// 0.55), grey weathered ice (0.75) and a dark blue streak (1). Built in code so the palette lives with the mesh logic.
        /// </summary>
        static Texture2D MakePalette()
        {
            var stops = new (float u, Color c)[] { (0f, new Color(0.97f, 0.98f, 1f)), (0.3f, new Color(0.82f, 0.91f, 0.98f)), (0.55f, new Color(0.68f, 0.83f, 0.95f)), (0.75f, new Color(0.72f, 0.78f, 0.84f)), (0.88f, new Color(0.6f, 0.76f, 0.92f)), (1f, new Color(0.36f, 0.6f, 0.88f)) };
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { name = "Iceberg palette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 64; x++)
            {
                var u = x / 63f;
                int s = 1;
                while (s < stops.Length - 1 && stops[s].u < u) s++;
                tex.SetPixel(x, 0, Color.Lerp(stops[s - 1].c, stops[s].c, Mathf.InverseLerp(stops[s - 1].u, stops[s].u, u)));
            }
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Flat-shaded triangles: three fresh vertices each, wound so their face points along a hint, coloured per face.</summary>
        class Builder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Tris = new List<int>();
            public float3 Origin;
            /// <summary>The current berg's face colour on the palette, its seed for per-face jitter and its streak's angle.</summary>
            public float SideU = 0.4f, Streak = -10f;
            public uint Seed;

            public void Tri(float3 a, float3 b, float3 c, float3 hint)
            {
                var n = math.cross(b - a, c - a);
                if (math.dot(n, hint) < 0f)
                {
                    (b, c) = (c, b);
                    n = -n;
                }
                n = math.normalizesafe(n);
                // Snow on faces that look up, the berg's own blue or grey on the sides (a little different per face),
                // a deeper blue under the water and on its streak.
                var jitter = IcebergShape.Hash01(Seed, 5000 + Tris.Count / 3) - 0.5f;
                var below = (a.y + b.y + c.y) / 3f - Origin.y < -0.3f;
                float u;
                if (n.y > 0.6f && !below) u = 0.02f + 0.12f * (jitter + 0.5f);
                else if (below) u = 0.6f + 0.1f * jitter;
                else if (Streak > -5f && math.abs(Wrap(math.atan2(n.z, n.x) - Streak)) < 0.3f) u = 0.97f;
                else u = SideU + 0.16f * jitter;
                foreach (var p in new[] { a, b, c })
                {
                    Tris.Add(Verts.Count);
                    Verts.Add(p - Origin);
                    Uvs.Add(new Vector2(u, 0.5f));
                }
            }

            public void Quad(float3 a, float3 b, float3 c, float3 d, float3 hint)
            {
                Tri(a, b, c, hint);
                Tri(a, c, d, hint);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(Verts);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        void Build(in TerrainSource source)
        {
            Clear();
            ref var set = ref source.Icebergs.Value;
            var sea = source.World.SeaLevel;
            var builders = new Dictionary<int2, Builder>();
            for (int i = 0; i < set.Bergs.Length; i++)
            {
                var berg = set.Bergs[i];
                var key = (int2)math.floor(berg.Centre / Region);
                if (!builders.TryGetValue(key, out var b))
                    builders[key] = b = new Builder { Origin = new float3(key.x * Region, sea, key.y * Region) };
                b.Seed = berg.Seed;
                b.SideU = 0.26f + 0.5f * berg.Tint;
                b.Streak = berg.Streak;
                Berg(b, ref set, berg, sea);
            }
            int tris = 0;
            foreach (var kv in builders)
            {
                tris += kv.Value.Tris.Count / 3;
                regions.Add((kv.Value.ToMesh($"Icebergs {kv.Key.x},{kv.Key.y}"), kv.Value.Origin));
            }
            var caveTris = 0;
            if (set.CaveBerg >= 0)
            {
                var cave = new Builder { Origin = set.Chamber };
                Cave(cave, ref set);
                caveTris = cave.Tris.Count / 3;
                caveMesh = cave.ToMesh("Iceberg cave");
                caveOrigin = set.Chamber;
                // Soft blue light in the chamber, as if the sun came through the ice (cosmetic, local to each client).
                caveLight = new GameObject("Iceberg cave light");
                var light = caveLight.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.62f, 0.86f, 1f);
                light.intensity = 6f;
                light.range = 16f;
                light.shadows = LightShadows.None;
                caveLight.transform.position = (Vector3)(set.Chamber + new float3(0f, 4.2f, 0f));
            }
            Stats = $"[icebergs] meshes: {regions.Count} region draws, {tris} tris ({(set.Bergs.Length > 0 ? tris / set.Bergs.Length : 0)} per berg avg), cave {caveTris} tris in 1 draw";
            Debug.Log(Stats);
        }

        static float Wrap(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));

        /// <summary>
        /// One berg (W-28b) sampled from the same IcebergShape it's stood on: uneven directions in its own stretched frame, a
        /// top that's a fan for slabs or rings around the peak or crown for the rest, the sides down the slope (undercut
        /// on some, a waterline ledge on some), an underwater bulge and a keel.
        /// </summary>
        static void Berg(Builder b, ref IcebergSetBlob set, in IcebergData berg, float sea)
        {
            var isCave = berg.Kind == 2;
            var big = berg.Kind >= 1;
            var m = isCave ? 56 : big ? berg.Sides * 2 : berg.Sides;
            var slab = isCave || berg.Shape == IcebergShape.Tabular || berg.Shape == IcebergShape.Tilted;
            // Where the top's rings gather: the centre for a slab, else the peak or crown (in the berg's own frame).
            var apex = slab ? float2.zero : berg.PeakA * berg.Radius;
            var topFractions = slab || !big ? new float[0] : new[] { 0.35f, 0.65f, 0.88f };
            var ledge = big && !isCave && berg.Ledge > 0f;
            // Side rings per direction: the rim, the side's fractions of the rim height, then the waterline (or the ledge).
            var sideFractions = isCave ? null : big ? new[] { 1f, 0.75f, 0.4f } : new[] { 1f };
            var caveLevels = new[] { 0.6f, 6f, 3.2f, 2.2f, 1.1f, 0f };
            var sides = isCave ? 1 + caveLevels.Length : sideFractions.Length + (ledge ? 3 : 1);
            var rings = sides + 2;
            var entrance = 0f;
            const float holeTop = 3.2f;
            if (isCave)
            {
                var mouth = set.Tunnel[0].xz - berg.Centre;
                entrance = math.atan2(mouth.y, mouth.x);
            }
            var pts = new float3[m, rings];
            var topPts = new float3[m, topFractions.Length];
            var worldAngle = new float[m];
            var rWlMean = 0f;
            float3 At(float2 xz, float y) => new float3(xz.x, sea + y, xz.y);
            for (int j = 0; j < m; j++)
            {
                // Uneven facets: each direction wanders up to 0.2 of a step (the cave berg keeps even ones for its mouth).
                var la = -math.PI + (j + (isCave ? 0f : 0.4f * (IcebergShape.Hash01(berg.Seed, 900 + j) - 0.5f))) * 2f * math.PI / m;
                var qd = new float2(math.cos(la), math.sin(la));
                var top = IcebergShape.TopRadius(berg, la);
                var rimQ = qd * top;
                var rimXZ = IcebergShape.World(berg, rimQ);
                var rimH = IcebergShape.TopHeight(berg, rimQ);
                var off = rimXZ - berg.Centre;
                var dist = math.length(off);
                var dir = off / dist;
                worldAngle[j] = math.atan2(dir.y, dir.x);
                var s = IcebergShape.Slope(berg, worldAngle[j]);
                var centreXZ = berg.Centre;
                var seed = berg.Seed;
                float2 Out(float d) => centreXZ + dir * d;
                float Jitter(int k) => 1f + 0.07f * (IcebergShape.Hash01(seed, 1000 + j * 13 + k) - 0.5f);
                for (int t = 0; t < topFractions.Length; t++)
                {
                    var q = math.lerp(apex, rimQ, topFractions[t]);
                    var y = IcebergShape.TopHeight(berg, q) + berg.Height * 0.05f * (IcebergShape.Hash01(berg.Seed, 1400 + j * 7 + t) - 0.5f);
                    topPts[j, t] = At(IcebergShape.World(berg, q), y);
                }
                var wl = dist + rimH / s;
                pts[j, 0] = At(rimXZ, rimH);
                if (isCave)
                {
                    for (int k = 0; k < caveLevels.Length; k++)
                    {
                        var y = k == 0 ? rimH * caveLevels[0] : caveLevels[k];
                        pts[j, k + 1] = At(Out((dist + (rimH - y) / s) * Jitter(k + 1)), y);
                    }
                }
                else
                {
                    for (int k = 1; k < sideFractions.Length; k++)
                    {
                        var y = rimH * sideFractions[k];
                        var d = dist + (rimH - y) / s;
                        // An undercut under the rim on some: the upper side pulled in (looks only).
                        if (k == 1) d -= berg.Overhang * 1.3f;
                        pts[j, k] = At(Out(d * Jitter(k)), y);
                    }
                    var k0 = sideFractions.Length;
                    if (ledge)
                    {
                        var w = IcebergShape.LedgeWidth(berg, la);
                        pts[j, k0] = At(Out(dist + (rimH - IcebergShape.LedgeHeight) / s), IcebergShape.LedgeHeight);
                        pts[j, k0 + 1] = At(Out(wl + w), IcebergShape.LedgeHeight);
                        wl += w + 0.25f;
                        pts[j, k0 + 2] = At(Out(wl), 0f);
                    }
                    else
                        pts[j, k0] = At(Out(wl * (big ? Jitter(k0) : 1f)), 0f);
                }
                rWlMean += wl / m;
                var bulge = wl * 1.1f + (big ? 1f : 0.3f);
                pts[j, sides] = At(Out(bulge), -0.3f * berg.Draft);
                pts[j, sides + 1] = At(Out(wl * 0.55f), -0.85f * berg.Draft);
            }
            var apexPoint = At(IcebergShape.World(berg, apex), IcebergShape.TopHeight(berg, apex));
            var keel = new float3(berg.Centre.x, sea - berg.Draft, berg.Centre.y);
            var inside = new float3(berg.Centre.x, sea - 0.3f * berg.Draft, berg.Centre.y);
            var up = new float3(0, 1, 0);
            var holeHalf = isCave ? math.atan2(1.5f, rWlMean) + math.PI / m : 0f;
            for (int j = 0; j < m; j++)
            {
                var n = (j + 1) % m;
                // The top: from the apex out through its rings to the rim.
                var last = topFractions.Length;
                float3 TopRing(int t, int i) => t < last ? topPts[i, t] : pts[i, 0];
                b.Tri(apexPoint, TopRing(0, j), TopRing(0, n), up);
                for (int t = 0; t < last; t++)
                    b.Quad(TopRing(t, j), TopRing(t, n), TopRing(t + 1, n), TopRing(t + 1, j), up);
                for (int k = 0; k + 1 < rings; k++)
                {
                    if (isCave && k + 1 < sides && k >= 3)
                    {
                        // Leave the mouth open: no side quads below the hole's top (levels 3.2 and down) facing the tunnel.
                        var mid = worldAngle[j] + 0.5f * Wrap(worldAngle[n] - worldAngle[j]);
                        if (math.abs(Wrap(mid - entrance)) < holeHalf)
                            continue;
                    }
                    var c = (pts[j, k] + pts[n, k] + pts[j, k + 1] + pts[n, k + 1]) * 0.25f;
                    b.Quad(pts[j, k], pts[n, k], pts[n, k + 1], pts[j, k + 1], c - inside);
                }
                var cb = (pts[j, rings - 1] + pts[n, rings - 1] + keel) / 3f;
                b.Tri(pts[j, rings - 1], pts[n, rings - 1], keel, cb - inside);
            }
        }

        /// <summary>The ice cave: an arched tube along the tunnel (both faces, so its mouth reads from outside too), a dome and a floor.</summary>
        static void Cave(Builder b, ref IcebergSetBlob set)
        {
            var profile = new float2[] { new(-1f, 0f), new(-1f, 0.65f), new(-0.75f, 0.8f), new(-0.4f, 0.94f), new(0f, 1f), new(0.4f, 0.94f), new(0.75f, 0.8f), new(1f, 0.65f), new(1f, 0f) };
            var nodes = set.Tunnel.Length;
            var rings = new float3[nodes, profile.Length];
            for (int i = 0; i < nodes; i++)
            {
                var p = set.Tunnel[i];
                var d0 = math.normalize(set.Tunnel[math.min(i + 1, nodes - 1)].xz - set.Tunnel[math.max(i - 1, 0)].xz);
                var side = new float2(-d0.y, d0.x);
                // Mitre: the joint's sideways direction halves the turn, stretched so the tube keeps its width.
                if (i > 0 && i < nodes - 1)
                {
                    var a = math.normalize(p.xz - set.Tunnel[i - 1].xz);
                    var c = math.normalize(set.Tunnel[i + 1].xz - p.xz);
                    var pa = new float2(-a.y, a.x);
                    side = math.normalize(pa + new float2(-c.y, c.x));
                    side /= math.max(0.5f, math.dot(side, pa));
                }
                for (int k = 0; k < profile.Length; k++)
                {
                    var xz = p.xz + side * profile[k].x * IcebergShape.TunnelHalfWidth;
                    rings[i, k] = new float3(xz.x, p.y + profile[k].y * IcebergShape.TunnelHeight, xz.y);
                }
            }
            for (int i = 0; i + 1 < nodes; i++)
                for (int k = 0; k < profile.Length; k++)
                {
                    var n = (k + 1) % profile.Length;
                    var q0 = rings[i, k]; var q1 = rings[i, n]; var q2 = rings[i + 1, n]; var q3 = rings[i + 1, k];
                    var axis = (set.Tunnel[i] + set.Tunnel[i + 1]) * 0.5f + new float3(0, IcebergShape.TunnelHeight * 0.5f, 0);
                    var c = (q0 + q1 + q2 + q3) * 0.25f;
                    b.Quad(q0, q1, q2, q3, axis - c);
                    b.Quad(q0, q1, q2, q3, c - axis);
                }

            // The dome: a wall, a ledge and a cap of rings, facing in, with a gap where the tunnel comes in.
            const int m = 48;
            var centre = set.Chamber;
            float rc = set.ChamberRadius, hd = set.ChamberHeight;
            var ringR = new List<float> { rc, rc, rc };
            var ringY = new List<float> { 0f, 1.3f, 2.6f };
            var t0 = math.asin(math.min(1f, 2.6f / hd));
            for (int s = 0; s <= 6; s++)
            {
                var t = math.lerp(t0, math.PI * 0.5f, s / 6f);
                ringR.Add(rc * math.cos(t));
                ringY.Add(math.max(2.6f, hd * math.sin(t)));
            }
            var last = set.Tunnel[nodes - 1].xz - centre.xz;
            var mouth = math.atan2(last.y, last.x);
            var holeHalf = math.atan2(1.5f, rc) + math.PI / m;
            var eye = centre + new float3(0, 1.5f, 0);
            for (int j = 0; j < m; j++)
            {
                float a0 = j * 2f * math.PI / m, a1 = (j + 1) * 2f * math.PI / m;
                // Each dome vertex sits a little in or out, so the ice reads as chipped facets.
                float3 P(float a, int r)
                {
                    var idx = (int)math.round(a / (2f * math.PI) * m) % m;
                    var rr = ringR[r] + (r > 0 && r < ringR.Count - 1 ? 0.5f * (IcebergShape.Hash01((uint)idx * 7919u + 17u, r) - 0.5f) : 0f);
                    return centre + new float3(math.cos(a) * rr, ringY[r], math.sin(a) * rr);
                }
                var inMouth = math.abs(Wrap((a0 + a1) * 0.5f - mouth)) < holeHalf;
                for (int r = 0; r + 1 < ringR.Count; r++)
                {
                    if (inMouth && ringY[r + 1] <= 2.61f)
                        continue;
                    var c = (P(a0, r) + P(a1, r + 1)) * 0.5f;
                    b.Quad(P(a0, r), P(a1, r), P(a1, r + 1), P(a0, r + 1), eye - c);
                }
                b.Tri(centre, P(a0, 0), P(a1, 0), new float3(0, 1, 0));
            }

            // W-28b: the dome's hole is cut in whole facets, so it's wider and taller than the tube; a frame just outside
            // the wall, from the tube's arch out to a bigger arch, closes the gap where the berg's outside showed through.
            var prev = set.Tunnel[nodes - 2];
            var end = set.Tunnel[nodes - 1];
            var radial = math.normalizesafe(end.xz - centre.xz);
            var dPrev = math.distance(prev.xz, centre.xz);
            var dEnd = math.distance(end.xz, centre.xz);
            var frameD = rc + 0.35f;
            var ft = math.saturate((dPrev - frameD) / math.max(0.01f, dPrev - dEnd));
            var frameFloor = math.lerp(prev.y, end.y, ft);
            var frameCentre = centre.xz + radial * frameD;
            var across = new float2(-radial.y, radial.x);
            var tubeSide = rings[nodes - 1, profile.Length - 1].xz - rings[nodes - 1, 0].xz;
            if (math.dot(across, tubeSide) < 0f)
                across = -across;
            var inner = new float3[profile.Length];
            var outer = new float3[profile.Length];
            for (int k = 0; k < profile.Length; k++)
            {
                inner[k] = math.lerp(rings[nodes - 2, k], rings[nodes - 1, k], ft);
                var xz = frameCentre + across * profile[k].x * 2.7f;
                outer[k] = new float3(xz.x, frameFloor - 0.4f + profile[k].y * 4.3f, xz.y);
            }
            var frameHint = new float3(radial.x, 0f, radial.y);
            for (int k = 0; k + 1 < profile.Length; k++)
            {
                b.Quad(inner[k], inner[k + 1], outer[k + 1], outer[k], frameHint);
                b.Quad(inner[k], inner[k + 1], outer[k + 1], outer[k], -frameHint);
            }
        }
    }
}
