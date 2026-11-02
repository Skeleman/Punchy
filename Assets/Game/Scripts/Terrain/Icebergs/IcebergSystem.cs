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
                ice = Resources.Load<Material>("IcebergIce");
                caveIce = Resources.Load<Material>("IcebergCave");
                if (ice == null || caveIce == null)
                {
                    if (!warned)
                        Debug.LogWarning("[icebergs] IcebergIce or IcebergCave material missing from Resources");
                    warned = true;
                    return;
                }
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

        protected override void OnDestroy() => Clear();

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

        /// <summary>Flat-shaded triangles: three fresh vertices each, wound so their face points along a hint.</summary>
        class Builder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<int> Tris = new List<int>();
            public float3 Origin;

            public void Tri(float3 a, float3 b, float3 c, float3 hint)
            {
                if (math.dot(math.cross(b - a, c - a), hint) < 0f)
                    (b, c) = (c, b);
                foreach (var p in new[] { a, b, c })
                {
                    Tris.Add(Verts.Count);
                    Verts.Add(p - Origin);
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

        /// <summary>One berg as polar rings: the flat top, the sides down to the waterline, an underwater bulge and a keel.</summary>
        static void Berg(Builder b, ref IcebergSetBlob set, in IcebergData berg, float sea)
        {
            var isCave = berg.Kind == 2;
            var m = isCave ? 56 : berg.Kind == 1 ? berg.Sides * 2 : berg.Sides;
            // Heights (above the sea) of the side rings: the cave berg gets close rings low down so its mouth is a neat cut.
            var levels = new List<float> { berg.Height };
            if (isCave)
            {
                foreach (var y in new[] { berg.Height * 0.6f, 6f, 3.2f, 2.2f, 1.1f })
                    if (y < berg.Height - 0.5f) levels.Add(y);
            }
            else if (berg.Kind == 1)
                levels.Add(berg.Height * 0.5f);
            levels.Add(0f);
            var entrance = 0f;
            var holeTop = 3.2f;
            if (isCave)
            {
                var mouth = set.Tunnel[0].xz - berg.Centre;
                entrance = math.atan2(mouth.y, mouth.x);
            }
            var rings = levels.Count + 2;
            var pts = new float3[m, rings];
            var rWlMean = 0f;
            for (int j = 0; j < m; j++)
            {
                var a = berg.Rotation - math.PI + j * 2f * math.PI / m;
                var dir = new float2(math.cos(a), math.sin(a));
                var top = IcebergShape.TopRadius(berg, a);
                var s = IcebergShape.Slope(berg, a);
                for (int k = 0; k < levels.Count; k++)
                {
                    var r = top + (berg.Height - levels[k]) / s;
                    // Facets: below the rim each side vertex leans in or out a little (looks only; the shape stands you on the plain slope).
                    if (k > 0 && berg.Kind > 0)
                        r *= 1f + 0.07f * (IcebergShape.Hash01(berg.Seed, 1000 + j * 13 + k) - 0.5f);
                    pts[j, k] = new float3(berg.Centre.x + dir.x * r, sea + levels[k], berg.Centre.y + dir.y * r);
                }
                var wl = top + berg.Height / s;
                rWlMean += wl / m;
                var bulge = wl * 1.1f + (berg.Kind == 0 ? 0.3f : 1f);
                pts[j, levels.Count] = new float3(berg.Centre.x + dir.x * bulge, sea - 0.3f * berg.Draft, berg.Centre.y + dir.y * bulge);
                pts[j, levels.Count + 1] = new float3(berg.Centre.x + dir.x * wl * 0.55f, sea - 0.85f * berg.Draft, berg.Centre.y + dir.y * wl * 0.55f);
            }
            var topCentre = new float3(berg.Centre.x, sea + berg.Height, berg.Centre.y);
            var keel = new float3(berg.Centre.x, sea - berg.Draft, berg.Centre.y);
            var inside = new float3(berg.Centre.x, sea - 0.3f * berg.Draft, berg.Centre.y);
            var holeHalf = isCave ? math.atan2(1.5f, rWlMean) + math.PI / m : 0f;
            for (int j = 0; j < m; j++)
            {
                var n = (j + 1) % m;
                b.Tri(topCentre, pts[j, 0], pts[n, 0], new float3(0, 1, 0));
                for (int k = 0; k + 1 < rings; k++)
                {
                    if (isCave && k + 1 < levels.Count && levels[k] <= holeTop + 0.01f)
                    {
                        var mid = berg.Rotation - math.PI + (j + 0.5f) * 2f * math.PI / m;
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
        }
    }
}
