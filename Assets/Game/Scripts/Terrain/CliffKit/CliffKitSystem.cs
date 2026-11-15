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
    /// vertex layout and material, coloured from the face they stand in (SampleSurface per height band, times the
    /// piece's tint, tops darker), so they carry the cliff's rock and strata. Two levels by on-screen size (rule 20):
    /// chipped, jittered boxes up close, plain boxes further out, none once a piece is a few pixels; far off, a 4x4
    /// group of regions draws as one plain mesh (row 1b). Meshes are built lazily, a couple a frame, and dropped when
    /// out of sight. Cosmetic; the pieces are seed-derived, so every
    /// client draws the same.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CliffKitSystem : SystemBase
    {
        const float RegionSize = 128f;
        /// <summary>Regions per side of a far group: once none of its regions can be near, its 4x4 regions draw as one
        /// plain mesh (row 1b: the red country drew ~300 regions one call each).</summary>
        const int GroupRegions = 4;
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

        /// <summary>A 512 m square of regions: one plain mesh while every region in it is far, else its regions.</summary>
        class FarGroup
        {
            public List<Region> Regions = new List<Region>();
            public Bounds Bounds;
            public int Tier = -1;
            public Mesh Far;
        }

        BlobAssetReference<CliffKitSetBlob> builtFrom;
        readonly List<Region> regions = new List<Region>();
        readonly List<FarGroup> farGroups = new List<FarGroup>();
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
            int builds = 0, tris = 0, draws = 0, near = 0, far = 0, grouped = 0;
            var renderParams = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            void Draw(Mesh mesh, Bounds bounds)
            {
                renderParams.worldBounds = bounds;
                Graphics.RenderMesh(renderParams, mesh, 0, Matrix4x4.identity);
                tris += (int)mesh.GetIndexCount(0) / 3;
                draws++;
            }
            foreach (var g in farGroups)
            {
                // The group by its nearest point: plain as one mesh only when even that is past the near size.
                var groupDistance = math.sqrt(g.Bounds.SqrDistance(view.Camera));
                g.Tier = DetailPolicy.Tier(DetailPolicy.ScreenSize(PieceSize, groupDistance, ppm), tiers, g.Tier);
                if (g.Tier >= 1)
                {
                    foreach (var r in g.Regions)
                    {
                        r.Tier = -1;
                        Drop(ref r.Near);
                        Drop(ref r.Far);
                    }
                    if (g.Tier >= 2)
                    {
                        Drop(ref g.Far);
                        continue;
                    }
                    if (g.Far == null && builds < BuildsPerFrame)
                    {
                        g.Far = Build(source, g.Regions, g.Bounds);
                        builds++;
                    }
                    if (g.Far != null)
                    {
                        Draw(g.Far, g.Bounds);
                        grouped++;
                    }
                    continue;
                }
                var drewAll = true;
                foreach (var r in g.Regions)
                {
                    var distance = math.sqrt(r.Bounds.SqrDistance(view.Camera));
                    r.Tier = DetailPolicy.Tier(DetailPolicy.ScreenSize(PieceSize, distance, ppm), tiers, r.Tier);
                    Mesh mesh = null;
                    if (r.Tier == 0)
                    {
                        if (r.Near == null && builds < BuildsPerFrame)
                        {
                            r.Near = Build(source, r.Pieces, r.Bounds, true);
                            builds++;
                        }
                        mesh = r.Near ?? r.Far;
                        near++;
                    }
                    else if (r.Tier == 1)
                    {
                        if (r.Far == null && builds < BuildsPerFrame)
                        {
                            r.Far = Build(source, r.Pieces, r.Bounds, false);
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
                    if (mesh != null)
                        Draw(mesh, r.Bounds);
                    else if (r.Tier <= 1)
                        drewAll = false;
                }
                // Keep the group mesh until its regions all draw, so stepping closer never blinks a cliff bare.
                if (drewAll)
                    Drop(ref g.Far);
                else if (g.Far != null)
                    Draw(g.Far, g.Bounds);
            }
            Stats = $"cliff kit: {builtFrom.Value.Pieces.Length} pieces in {regions.Count} regions; near {near}, far {far}, far groups {grouped}; {tris} tris in {draws} draws";
        }

        protected override void OnDestroy() => Clear();

        void Clear()
        {
            foreach (var r in regions)
            {
                Drop(ref r.Near);
                Drop(ref r.Far);
            }
            foreach (var g in farGroups)
                Drop(ref g.Far);
            regions.Clear();
            farGroups.Clear();
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
            var groups = new Dictionary<int2, FarGroup>();
            foreach (var pair in byKey)
            {
                var key = (int2)math.floor((float2)pair.Key / GroupRegions);
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new FarGroup { Bounds = pair.Value.Bounds };
                    groups.Add(key, g);
                    farGroups.Add(g);
                }
                g.Regions.Add(pair.Value);
                g.Bounds.Encapsulate(pair.Value.Bounds);
            }
        }

        // ------------------------------------------------------------------------------------------- meshes

        readonly List<TerrainVertex> verts = new List<TerrainVertex>();
        readonly List<int> indices = new List<int>();
        readonly Dictionary<int, SurfaceProps> bands = new Dictionary<int, SurfaceProps>();

        Mesh Build(in TerrainSource source, List<int> pieces, Bounds bounds, bool chamfered)
        {
            verts.Clear();
            indices.Clear();
            ref var set = ref source.CliffKit.Value;
            var boxes = new FixedList512Bytes<KitBox>();
            foreach (var i in pieces)
                AddPiece(source, set.Pieces[i], ref boxes, chamfered);
            return Finish(bounds, chamfered);
        }

        /// <summary>A far group's one plain mesh: every piece of its regions.</summary>
        Mesh Build(in TerrainSource source, List<Region> group, Bounds bounds)
        {
            verts.Clear();
            indices.Clear();
            ref var set = ref source.CliffKit.Value;
            var boxes = new FixedList512Bytes<KitBox>();
            foreach (var r in group)
                foreach (var i in r.Pieces)
                    AddPiece(source, set.Pieces[i], ref boxes, false);
            return Finish(bounds, false);
        }

        void AddPiece(in TerrainSource source, in CliffPiece piece, ref FixedList512Bytes<KitBox> boxes, bool chamfered)
        {
            bands.Clear();
            boxes.Clear();
            CliffKit.Boxes(piece, ref boxes);
            for (int k = 0; k < boxes.Length; k++)
                AddBox(source, piece, boxes[k], (uint)k, chamfered);
        }

        Mesh Finish(Bounds bounds, bool chamfered)
        {
            var mesh = new Mesh { name = chamfered ? "Cliff kit (near)" : "Cliff kit (far)", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(verts.Count, TerrainVertex.Layout);
            mesh.SetVertexBufferData(verts, 0, 0, verts.Count);
            mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Count);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Count), MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>
        /// One box: plain (12 triangles) or chamfered (its faces inset, bevelled edges and cut corners: 44 triangles),
        /// its corners cut and nudged by the piece's seed so no two look machined. Flat shaded. The face's surface is
        /// sampled per height band, so its strata run on through the piece.
        /// </summary>
        void AddBox(in TerrainSource source, in CliffPiece piece, in KitBox box, uint index, bool chamfered)
        {
            var rng = new Unity.Mathematics.Random(BiomeSampling.Scramble(piece.Seed ^ (index * 0x9E3779B9u)) | 1u);
            // Local functions can't capture an in parameter: copies.
            var h = box.Half;
            var centre = box.Centre;
            var rotation = box.Rotation;
            var small = math.cmin(h);
            // Chipped: each corner cut by its own bevel (15-45% of the box's smallest half) and nudged, so no two edges
            // read machined. The three faces at a corner share its points, so the box stays closed.
            var bevel = new float[8];
            var jitter = new float3[8];
            for (int c = 0; c < 8; c++)
            {
                bevel[c] = chamfered ? small * rng.NextFloat(0.15f, 0.45f) : 0f;
                jitter[c] = chamfered ? rng.NextFloat3(-1f, 1f) * (small * 0.14f) : 0f;
            }

            // Coloured from the face the piece stands in (its anchor, with the face's own normal), not the ground under
            // the box, which may be the flat foot below the cliff; at each face's own height, so the face's strata run
            // on through the piece. Snow and grass off (they read as pale boards stuck on the cliff), a little darker
            // than the face as rock standing out of it sits in its own shade, and the tops darker still (row 1b: a
            // sunlit top read as a pale plank).
            var anchor = piece.Position.xz;
            var ground = source.SampleGround(anchor);
            var faceNormal = source.SampleNormal(anchor, 2f);
            var tint = piece.Tint;
            var src = source;
            var cache = bands;
            // Strata can't show on plain boxes: coarser bands keep a far group's build cheap.
            var bandSize = chamfered ? 1f : 8f;

            SurfaceProps Rock(float y)
            {
                // One sample per band per piece (the cache is cleared per piece).
                var band = (int)math.floor(y / bandSize);
                if (cache.TryGetValue(band, out var cached))
                    return cached;
                var at = ground;
                at.Height = (band + 0.5f) * bandSize;
                var rock = src.SampleSurface(anchor, at, faceNormal);
                rock.Albedo *= tint * 0.8f;
                rock.Snow = 0f;
                rock.Grass = 0f;
                rock.Bump = math.max(rock.Bump, 1f);
                cache.Add(band, rock);
                return rock;
            }

            // V(corner, axis): the corner's point on the face across that axis, pulled in from the other two by the bevel.
            float3 V(int c, int axis)
            {
                var s = new float3((c & 1) != 0 ? 1f : -1f, (c & 2) != 0 ? 1f : -1f, (c & 4) != 0 ? 1f : -1f);
                var p = s * (h - bevel[c]);
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
                var surface = Rock(centroid.y);
                if (n.y > 0.6f)
                    surface.Albedo *= 0.6f;
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
