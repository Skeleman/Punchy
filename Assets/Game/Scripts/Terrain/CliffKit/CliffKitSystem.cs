using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the cliff kit (W-150 row 1): the pieces of each 128 m region merged into one mesh in the terrain's own
    /// vertex layout and material, coloured from the ground they stand in (SampleSurface on their tops and sides, times
    /// the piece's tint), so they carry the cliff's rock and strata. Two levels by on-screen size (rule 20): chamfered,
    /// jittered boxes up close, plain boxes further out, none once a piece is a few pixels. Meshes are built lazily, a
    /// couple of regions a frame, and dropped when out of sight. Cosmetic; the pieces are seed-derived, so every
    /// client draws the same.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CliffKitSystem : SystemBase
    {
        const float RegionSize = 128f;
        /// <summary>A piece's typical size (m) for its on-screen size.</summary>
        const float PieceSize = 10f;
        const int BuildsPerFrame = 2;
        /// <summary>Reference pixels for a piece: chamfered above Near, plain boxes above Mid, nothing below.</summary>
        static readonly DetailPolicy.Thresholds PieceTiers = new DetailPolicy.Thresholds(60f, 4f, 0f);

        /// <summary>For the report: pieces, regions per level, triangles and draw calls drawn last frame.</summary>
        public static string Stats = "";

        class Region
        {
            public List<int> Pieces = new List<int>();
            public Bounds Bounds;
            public int Tier = -1;
            public Mesh Near, Far;
        }

        BlobAssetReference<CliffKitSetBlob> builtFrom;
        readonly List<Region> regions = new List<Region>();
        EntityQuery configQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
            configQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
        }

        protected override void OnUpdate()
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            if (!source.CliffKit.IsCreated || configQuery.IsEmpty)
                return;
            if (source.CliffKit != builtFrom)
            {
                builtFrom = source.CliffKit;
                Group(ref source.CliffKit.Value);
            }
            var material = configQuery.GetSingleton<TerrainConfig>().Material.Value;
            if (material == null)
                return;
            var view = DetailPolicy.Current.Data;
            var ppm = view.PixelsPerMetre > 0f ? view.PixelsPerMetre : DetailPolicy.PixelsPerMetreAt(DetailPolicy.ReferenceFov);
            var tiers = view.Detail > 0f ? PieceTiers.Scaled(view.Detail) : PieceTiers;
            int builds = 0, tris = 0, draws = 0, near = 0, far = 0;
            foreach (var r in regions)
            {
                var distance = math.sqrt(r.Bounds.SqrDistance(view.Camera));
                r.Tier = DetailPolicy.Tier(DetailPolicy.ScreenSize(PieceSize, distance, ppm), tiers, r.Tier);
                Mesh mesh = null;
                if (r.Tier == 0)
                {
                    if (r.Near == null && builds < BuildsPerFrame)
                    {
                        r.Near = Build(source, r, true);
                        builds++;
                    }
                    mesh = r.Near ?? r.Far;
                    near++;
                }
                else if (r.Tier == 1)
                {
                    if (r.Far == null && builds < BuildsPerFrame)
                    {
                        r.Far = Build(source, r, false);
                        builds++;
                    }
                    mesh = r.Far ?? r.Near;
                    far++;
                    Drop(ref r.Near);
                }
                else
                {
                    Drop(ref r.Near);
                    Drop(ref r.Far);
                }
                if (mesh == null)
                    continue;
                Graphics.RenderMesh(new RenderParams(material) { worldBounds = r.Bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, mesh, 0, Matrix4x4.identity);
                tris += (int)mesh.GetIndexCount(0) / 3;
                draws++;
            }
            Stats = $"cliff kit: {builtFrom.Value.Pieces.Length} pieces in {regions.Count} regions; near {near}, far {far}; {tris} tris in {draws} draws";
        }

        protected override void OnDestroy() => Clear();

        void Clear()
        {
            foreach (var r in regions)
            {
                Drop(ref r.Near);
                Drop(ref r.Far);
            }
            regions.Clear();
        }

        static void Drop(ref Mesh mesh)
        {
            if (mesh != null)
                Object.Destroy(mesh);
            mesh = null;
        }

        /// <summary>Sorts the pieces into regions and sizes each region's bounds from its boxes.</summary>
        void Group(ref CliffKitSetBlob set)
        {
            Clear();
            var byKey = new Dictionary<int2, Region>();
            var boxes = new FixedList512Bytes<KitBox>();
            for (int i = 0; i < set.Pieces.Length; i++)
            {
                var piece = set.Pieces[i];
                var key = (int2)math.floor(piece.Position.xz / RegionSize);
                if (!byKey.TryGetValue(key, out var r))
                {
                    r = new Region { Bounds = new Bounds(piece.Position, Vector3.zero) };
                    byKey.Add(key, r);
                    regions.Add(r);
                }
                r.Pieces.Add(i);
                boxes.Clear();
                CliffKit.Boxes(piece, ref boxes);
                for (int k = 0; k < boxes.Length; k++)
                    r.Bounds.Encapsulate(new Bounds(boxes[k].Centre, Vector3.one * (2f * boxes[k].Reach)));
            }
        }

        // ------------------------------------------------------------------------------------------- meshes

        readonly List<TerrainVertex> verts = new List<TerrainVertex>();
        readonly List<int> indices = new List<int>();
        readonly Vector3[] corners = new Vector3[8];

        Mesh Build(in TerrainSource source, Region r, bool chamfered)
        {
            verts.Clear();
            indices.Clear();
            ref var set = ref source.CliffKit.Value;
            var boxes = new FixedList512Bytes<KitBox>();
            foreach (var i in r.Pieces)
            {
                var piece = set.Pieces[i];
                boxes.Clear();
                CliffKit.Boxes(piece, ref boxes);
                for (int k = 0; k < boxes.Length; k++)
                    AddBox(source, piece, boxes[k], (uint)k, chamfered);
            }
            var mesh = new Mesh { name = chamfered ? "Cliff kit (near)" : "Cliff kit (far)", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(verts.Count, TerrainVertex.Layout);
            mesh.SetVertexBufferData(verts, 0, 0, verts.Count);
            mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Count);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Count), MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = r.Bounds;
            return mesh;
        }

        /// <summary>
        /// One box: plain (12 triangles) or chamfered (its faces inset, bevelled edges and cut corners: 44 triangles),
        /// its corners nudged by the piece's seed so no two look machined. Flat shaded. The ground's surface is sampled
        /// once for its top and once for its sides.
        /// </summary>
        void AddBox(in TerrainSource source, in CliffPiece piece, in KitBox box, uint index, bool chamfered)
        {
            var rng = new Unity.Mathematics.Random(BiomeSampling.Scramble(piece.Seed ^ (index * 0x9E3779B9u)) | 1u);
            // Local functions can't capture an in parameter: copies.
            var h = box.Half;
            var centre = box.Centre;
            var rotation = box.Rotation;
            var small = math.cmin(h);
            var bevel = chamfered ? small * 0.22f : 0f;
            // Jittered corner offsets, shared by the three faces at each corner so the box stays closed.
            var jitter = new float3[8];
            for (int c = 0; c < 8; c++)
                jitter[c] = chamfered ? rng.NextFloat3(-1f, 1f) * (small * 0.1f) : 0f;

            // Coloured from the face the piece stands in (its anchor, with the face's own normal for the sides), not the
            // ground under the box, which may be the flat foot below the cliff.
            var anchor = piece.Position.xz;
            var ground = source.SampleGround(anchor);
            var faceNormal = source.SampleNormal(anchor, 2f);
            // One rock for every face: an up normal would give the tops the ground's snow or grass, which read as pale
            // boards stuck on the cliff. A little darker than the face, as rock standing out of it sits in its own shade.
            var wall = source.SampleSurface(anchor, ground, faceNormal);
            wall.Albedo *= piece.Tint * 0.85f;
            wall.Snow = 0f;
            wall.Grass = 0f;
            wall.Bump = math.max(wall.Bump, 1f);
            var top = wall;

            // V(corner, axis): the corner's point on the face across that axis, pulled in from the other two by the bevel.
            float3 V(int c, int axis)
            {
                var s = new float3((c & 1) != 0 ? 1f : -1f, (c & 2) != 0 ? 1f : -1f, (c & 4) != 0 ? 1f : -1f);
                var p = s * (h - bevel);
                p[axis] = s[axis] * h[axis];
                return centre + math.mul(rotation, p + jitter[c]);
            }

            void Poly(float3 a, float3 b, float3 c, float3 d, bool quad)
            {
                // a b c (d) run round the face; Tri winds each triangle to face out of the box.
                var centroid = quad ? (a + b + c + d) * 0.25f : (a + b + c) / 3f;
                var n = math.normalizesafe(quad ? math.cross(c - a, d - b) : math.cross(b - a, c - a));
                if (math.dot(n, centroid - centre) < 0f)
                    n = -n;
                var surface = n.y > 0.6f ? top : wall;
                var start = verts.Count;
                Add(a, n, surface);
                Add(b, n, surface);
                Add(c, n, surface);
                Tri(start, start + 1, start + 2, n);
                if (quad)
                {
                    Add(d, n, surface);
                    Tri(start, start + 2, start + 3, n);
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                int j = (axis + 1) % 3, l = (axis + 2) % 3;
                int bitA = 1 << axis, bitJ = 1 << j, bitL = 1 << l;
                // The two faces across this axis: corners round the face in order.
                for (int s = 0; s < 2; s++)
                {
                    var baseC = s != 0 ? bitA : 0;
                    Poly(V(baseC, axis), V(baseC | bitJ, axis), V(baseC | bitJ | bitL, axis), V(baseC | bitL, axis), true);
                }
                if (!chamfered)
                    continue;
                // The four bevelled edges running along this axis, between the j and l faces.
                for (int e = 0; e < 4; e++)
                {
                    var c0 = ((e & 1) != 0 ? bitJ : 0) | ((e & 2) != 0 ? bitL : 0);
                    var c1 = c0 | bitA;
                    Poly(V(c0, j), V(c1, j), V(c1, l), V(c0, l), true);
                }
            }
            if (chamfered)
                for (int c = 0; c < 8; c++)
                    Poly(V(c, 0), V(c, 1), V(c, 2), default, false);
        }

        void Add(float3 p, float3 n, in SurfaceProps surface)
        {
            var v = new TerrainVertex { Position = p, Normal = n };
            v.SetSurface(surface);
            // No geomorph (its range far beyond any view), the ground's bumpiness, and far from cave air.
            v.Morph = new float4(1e5f, 2e5f, surface.Bump, 100f);
            verts.Add(v);
        }

        void Tri(int a, int b, int c, float3 n)
        {
            var pa = verts[a].Position;
            var face = math.cross(verts[b].Position - pa, verts[c].Position - pa);
            // Unity's front faces wind clockwise seen from outside.
            if (math.dot(face, n) > 0f)
                (b, c) = (c, b);
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }
}
