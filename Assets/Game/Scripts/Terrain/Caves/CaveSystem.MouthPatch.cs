using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    public partial class CaveSystem
    {
        const int PatchCells = 12;

        partial class Built
        {
            public NativeArray<bool> Patch;
            public NativeList<TerrainVertex> PatchVerts;
            public NativeList<int> PatchIndices;
            public NativeArray<int> PatchFlips;
            public Mesh PatchMesh;
            public TerrainVertex[] PatchBase;
            public int PatchContacts = -1;

            public void DisposePatch(bool mesh)
            {
                if (Patch.IsCreated) Patch.Dispose();
                if (PatchVerts.IsCreated) PatchVerts.Dispose();
                if (PatchIndices.IsCreated) PatchIndices.Dispose();
                if (PatchFlips.IsCreated) PatchFlips.Dispose();
                if (mesh && PatchMesh != null) Destroy(PatchMesh);
            }
        }

        int2 PatchMin(int3 key) => key.xz * (int)(chunkSize / MouthPatch.Cell) - 2;

        JobHandle StartPatch(in TerrainSource source, int3 key, Built b, float gridStep)
        {
            b.Patch = new NativeArray<bool>(gridStep > 0f ? PatchCells * PatchCells : 1, Allocator.Persistent);
            b.PatchVerts = new NativeList<TerrainVertex>(Allocator.Persistent);
            b.PatchIndices = new NativeList<int>(Allocator.Persistent);
            b.PatchFlips = new NativeArray<int>(1, Allocator.Persistent);
            return gridStep > 0f ? new PatchJob { Source = source, Min = PatchMin(key), Mask = b.Patch }.Schedule() : default;
        }

        [BurstCompile]
        struct PatchJob : IJob
        {
            public TerrainSource Source;
            public int2 Min;
            public NativeArray<bool> Mask;

            public void Execute() => MouthPatch.Fill(Source, Min, PatchCells, Mask);
        }

        // The wall field's zero sits SkinGap under Surface, so this puts the skin on the patch.
        static float PatchSurface(in TerrainSource source, float2 xz, float h, float scale, NativeArray<bool> patch, int2 min)
        {
            if (patch.Length < PatchCells * PatchCells)
                return h;
            MouthPatch.Membership(xz, patch, min, PatchCells, out var any, out _);
            return any ? MouthPatch.Height(source, xz, patch, min, PatchCells) + CaveSampling.SkinGap / scale : h;
        }

        bool FinishPatch(Built b)
        {
            if (b.PatchIndices.Length == 0)
                return false;
            b.PatchBase = b.PatchVerts.AsArray().ToArray();
            b.PatchMesh = new Mesh { name = "Cave mouth patch", indexFormat = IndexFormat.UInt32 };
            b.PatchMesh.SetVertexBufferParams(b.PatchBase.Length, TerrainVertex.Layout);
            b.PatchMesh.SetIndexBufferParams(b.PatchIndices.Length, IndexFormat.UInt32);
            b.PatchMesh.SetIndexBufferData(b.PatchIndices.AsArray(), 0, 0, b.PatchIndices.Length);
            b.PatchMesh.SetSubMesh(0, new SubMeshDescriptor(0, b.PatchIndices.Length), MeshUpdateFlags.DontRecalculateBounds);
            ShadePatch(b);
            return true;
        }

        // W-136 contact shade, as BuildChunkMeshJob puts it in the chunk round the patch; redone when the flora republishes.
        void ShadePatch(Built b)
        {
            b.PatchContacts = GroundContacts.Version;
            var verts = new NativeArray<TerrainVertex>(b.PatchBase, Allocator.TempJob);
            if (GroundContacts.Flora.IsCreated && GroundContacts.Contacts.IsCreated)
                new PatchShadeJob
                {
                    Flora = GroundContacts.Flora, Contacts = GroundContacts.Contacts, Source = lastSource, Verts = verts,
                    Strength = ContactShade.Strength, Reach = ContactShade.ReachScale,
                    PixelsPerMetre = Punchy.Settings.DetailPolicy.Current.Data.PixelsPerMetre * Punchy.Settings.GraphicsSettings.Detail
                }.Run();
            b.PatchMesh.SetVertexBufferData(verts, 0, 0, verts.Length);
            b.PatchMesh.RecalculateBounds();
            verts.Dispose();
        }

        [BurstCompile]
        struct PatchShadeJob : IJob
        {
            [ReadOnly] public BlobAssetReference<Punchy.Flora.FloraBlob> Flora;
            [ReadOnly] public BlobAssetReference<GroundContacts.Table> Contacts;
            public TerrainSource Source;
            public NativeArray<TerrainVertex> Verts;
            public float4 Strength, Reach;
            public float PixelsPerMetre;

            public void Execute()
            {
                const float step = 1f;
                float2 lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < Verts.Length; i++)
                {
                    lo = math.min(lo, Verts[i].Position.xz);
                    hi = math.max(hi, Verts[i].Position.xz);
                }
                var origin = math.floor(lo / step) * step;
                var v = (int)math.ceil(math.cmax(hi - origin) / step) + 2;
                var shade = new NativeArray<float>(v * v, Allocator.Temp);
                for (int i = 0; i < shade.Length; i++)
                    shade[i] = 1f;
                GroundContacts.Rasterise(ref Flora.Value, ref Contacts.Value, Source, origin, v, step, 0f, PixelsPerMetre, Strength, Reach, shade);
                for (int i = 0; i < Verts.Length; i++)
                {
                    var vertex = Verts[i];
                    var g = (vertex.Position.xz - origin) / step;
                    var c = math.min((int2)math.floor(g), v - 2);
                    var f = g - c;
                    var k = c.y * v + c.x;
                    var s = math.lerp(math.lerp(shade[k], shade[k + 1], f.x), math.lerp(shade[k + v], shade[k + v + 1], f.x), f.y);
                    vertex.Surface.xyz = (half3)((float3)vertex.Surface.xyz * s);
                    Verts[i] = vertex;
                }
            }
        }

        internal int ProbePatch(in TerrainSource source, int3 key, System.Collections.Generic.List<Vector3> rock, System.Collections.Generic.List<int> rockTris,
                                System.Collections.Generic.List<Vector3> patch, System.Collections.Generic.List<int> patchTris)
        {
            var b = StartBuild(source, key, 0);
            b.Job.Complete();
            var first = rock.Count;
            foreach (var v in b.Positions)
                rock.Add(v);
            foreach (var i in b.Indices)
                rockTris.Add(first + i);
            first = patch.Count;
            foreach (var v in b.PatchVerts)
                patch.Add(v.Position);
            foreach (var i in b.PatchIndices)
                patchTris.Add(first + i);
            var flips = b.PatchFlips[0];
            b.Dispose();
            return flips;
        }

        TerrainStreamingSystem patchStreaming;

        // The terrain leaves its patch cells empty only at LOD MaxLod or finer.
        void DrawPatch(Built b)
        {
            if (b.PatchMesh == null || configQuery.IsEmpty)
                return;
            if (b.PatchContacts != GroundContacts.Version)
                ShadePatch(b);
            var config = configQuery.GetSingleton<TerrainConfig>();
            var material = config.Material.Value;
            patchStreaming ??= World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<TerrainStreamingSystem>();
            var center = b.PatchMesh.bounds.center;
            var coord = (int2)math.floor(new float2(center.x, center.z) / config.ChunkSize);
            if (material == null || patchStreaming == null || !patchStreaming.TryGetLod(coord, out var lod) || lod > MouthPatch.MaxLod)
                return;
            Graphics.RenderMesh(new RenderParams(material) { worldBounds = b.PatchMesh.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true },
                b.PatchMesh, 0, Matrix4x4.identity);
        }

        partial struct MeshJob
        {
            [ReadOnly] public NativeArray<bool> Patch;
            public int2 PatchMin;
            public NativeList<TerrainVertex> PatchVerts;
            public NativeList<int> PatchIndices;
            public NativeArray<int> PatchFlips;

            bool Patched => Patch.Length == PatchCells * PatchCells;

            float2 ColumnXZ(int x, int z) => (float2)(Origin.xz + new int2(x, z)) * Voxel;

            bool PatchColumn(int x, int z)
            {
                if (!Patched)
                    return false;
                MouthPatch.Membership(ColumnXZ(x + 1, z + 1), Patch, PatchMin, PatchCells, out var any, out _);
                return any;
            }

            // -2: not snapped, -1: snapped onto a new vertex, else the column's shared vertex.
            int Snap(ref float3 p, ref float3 normal, int x, int y, int z, NativeHashMap<int2, int> columns, NativeHashMap<int, float3> raw)
            {
                if (!Patched)
                    return -2;
                var xz = ColumnXZ(x + 1, z + 1);
                MouthPatch.Membership(xz, Patch, PatchMin, PatchCells, out var any, out var all);
                if (!any)
                    return -2;
                if (all)
                {
                    var near = float.MaxValue;
                    for (int c = 0; c < 8; c++)
                        near = math.min(near, CaveField[Point(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))]);
                    if (near <= CaveSampling.MouthBlend + Voxel)
                        return -2;
                }
                var h = MouthPatch.Height(Source, xz, Patch, PatchMin, PatchCells);
                if (all)
                {
                    // Inside, only the rim has to meet the terrain: dropping straight down keeps the field's layout, so nothing folds.
                    var g = Source.SampleGround(p.xz).Height;
                    if (math.abs(p.y - g) > 0.5f * Voxel)
                        return -2;
                    raw[Positions.Length] = p;
                    p.y = g;
                    normal = Source.SampleNormal(p.xz, GridStep);
                    return -1;
                }
                // Only the ground's own crossing: walls and passages under the patch keep their place.
                if (math.abs(p.y - h) > 2f * Voxel + CaveSampling.SkinGap)
                    return -2;
                var column = new int2(x, z);
                if (columns.TryGetValue(column, out var shared))
                    return shared;
                columns[column] = Positions.Length;
                raw[Positions.Length] = p;
                p = new float3(xz.x, h, xz.y);
                normal = Source.SampleNormal(xz, GridStep);
                return -1;
            }

            bool InPatch(float2 xz)
            {
                var cell = (int2)math.floor(xz / MouthPatch.Cell) - PatchMin;
                return math.all(cell >= 0 & cell < PatchCells) && Patch[cell.y * PatchCells + cell.x];
            }

            void SplitPatch(NativeHashMap<int, float3> raw, NativeList<bool> edge)
            {
                if (!Patched)
                    return;
                // A snap that folds a triangle over is undone, except on the rim and the seam, which neighbours must agree on.
                for (int pass = 0, undone = 1; pass < 12 && undone > 0; pass++)
                {
                    undone = 0;
                    for (int t = 0; t < Indices.Length; t += 3)
                    {
                        if (!Flipped(t, raw) && !FacesDown(t, raw))
                            continue;
                        if (OtherDiagonal(t - t % 6, raw))
                            continue;
                        var before = undone;
                        for (int k = 0; k < 3; k++)
                        {
                            var v = Indices[t + k];
                            if (Inner(v) && !edge[v] && raw.TryGetValue(v, out var r))
                            {
                                Positions[v] = r;
                                Normals[v] = Gradient(r);
                                raw.Remove(v);
                                undone++;
                            }
                        }
                        if (undone == before && Collapse(t, raw, edge))
                            undone++;
                    }
                }
                var map = new NativeArray<int>(Positions.Length, Allocator.Temp);
                for (int i = 0; i < map.Length; i++)
                    map[i] = -1;
                var kept = 0;
                for (int t = 0; t < Indices.Length; t += 3)
                {
                    int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
                    if (a == b || b == c || a == c)
                        continue;
                    bool sa = raw.ContainsKey(a), sb = raw.ContainsKey(b), sc = raw.ContainsKey(c);
                    var inside = InPatch((Positions[a].xz + Positions[b].xz + Positions[c].xz) / 3f);
                    if ((sa || sb || sc) && !inside)
                        continue;
                    var down = FacesDown(t, raw);
                    if (Flipped(t, raw))
                        PatchFlips[0]++;
                    if (!(sa && sb && sc) && !(inside && Ground(a, b, c)))
                    {
                        Indices[kept++] = a;
                        Indices[kept++] = b;
                        Indices[kept++] = c;
                        continue;
                    }
                    // The terrain material culls the other side from the cave's. A fold of fixed rim corners still lies on the ground, so it is wound up.
                    PatchIndices.Add(PatchVertex(map, a));
                    PatchIndices.Add(PatchVertex(map, down ? b : c));
                    PatchIndices.Add(PatchVertex(map, down ? c : b));
                }
                Indices.ResizeUninitialized(kept);
                map.Dispose();
            }

            void Quad(NativeArray<int> cellVertex, int c0, int c1, int c2, int c3, bool flip)
            {
                int v0 = cellVertex[c0], v1 = cellVertex[c1], v2 = cellVertex[c2], v3 = cellVertex[c3];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                    return;
                if (flip)
                    (v1, v3) = (v3, v1);
                Indices.Add(v0); Indices.Add(v1); Indices.Add(v2);
                Indices.Add(v0); Indices.Add(v2); Indices.Add(v3);
            }

            // Snapped corners reversed the triangle's winding from where the field put them.
            bool Flipped(int t, NativeHashMap<int, float3> raw)
            {
                int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
                bool sa = raw.ContainsKey(a), sb = raw.ContainsKey(b), sc = raw.ContainsKey(c);
                if (sa == sb && sb == sc)
                    return false;
                float3 ra = Raw(raw, a), rb = Raw(raw, b), rc = Raw(raw, c);
                var before = math.cross(rb - ra, rc - ra);
                var after = math.cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                // A sliver's winding is rounding noise either way.
                var sliver = 0.08f * Voxel * Voxel;
                if (math.length(before) < sliver || math.length(after) < sliver)
                    return false;
                return math.dot(before, after) < 0f;
            }

            bool Inner(int v)
            {
                MouthPatch.Membership(Positions[v].xz, Patch, PatchMin, PatchCells, out _, out var all);
                return all;
            }

            // Rim vertices are fixed, so a fold against one is closed by merging the nearest free corner into it.
            bool Collapse(int t, NativeHashMap<int, float3> raw, NativeList<bool> edge)
            {
                int from = -1, to = -1;
                // One voxel left folds at steep mouths (1601, 1049), where the free corner sits up to two voxels off.
                var best = 2f * Voxel;
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    int r = Indices[t + i], u = Indices[t + j];
                    if (i == j || Inner(r) || !raw.ContainsKey(r) || edge[u] || (!Inner(u) && raw.ContainsKey(u)))
                        continue;
                    var d = math.distance(Positions[r], Positions[u]);
                    if (d < best)
                        (best, from, to) = (d, u, r);
                }
                if (from < 0)
                    return false;
                for (int i = 0; i < Indices.Length; i++)
                    if (Indices[i] == from)
                        Indices[i] = to;
                return true;
            }

            bool OtherDiagonal(int q, NativeHashMap<int, float3> raw)
            {
                int v0 = Indices[q], v1 = Indices[q + 1], v2 = Indices[q + 2], v3 = Indices[q + 5];
                Indices[q] = v0; Indices[q + 1] = v1; Indices[q + 2] = v3;
                Indices[q + 3] = v1; Indices[q + 4] = v2; Indices[q + 5] = v3;
                if (!Flipped(q, raw) && !FacesDown(q, raw) && !Flipped(q + 3, raw) && !FacesDown(q + 3, raw))
                    return true;
                Indices[q] = v0; Indices[q + 1] = v1; Indices[q + 2] = v2;
                Indices[q + 3] = v0; Indices[q + 4] = v2; Indices[q + 5] = v3;
                return false;
            }

            bool FacesDown(int t, NativeHashMap<int, float3> raw)
            {
                int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
                if (!raw.ContainsKey(a) || !raw.ContainsKey(b) || !raw.ContainsKey(c))
                    return false;
                var n = math.cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                return n.y >= 0f && math.lengthsq(n) > 0f;
            }

            // Skin the field left off the ground, steep or flat, is still ground: only faces turned into the passage keep the rock look.
            bool Ground(int a, int b, int c)
            {
                float3 pa = Positions[a], pb = Positions[b], pc = Positions[c];
                var n = math.normalizesafe(math.cross(pb - pa, pc - pa));
                var centre = (pa + pb + pc) / 3f;
                var up = Source.SampleNormal(centre.xz, GridStep);
                return -n.y > 0.2f && -math.dot(n, up) > 0.7f && math.abs(centre.y - Source.SampleGround(centre.xz).Height) * up.y < 2f * Voxel;
            }

            float3 Raw(NativeHashMap<int, float3> raw, int v) => raw.TryGetValue(v, out var r) ? r : Positions[v];

            int PatchVertex(NativeArray<int> map, int v)
            {
                if (map[v] < 0)
                {
                    map[v] = PatchVerts.Length;
                    var p = Positions[v];
                    PatchVerts.Add(MouthPatch.GroundVertex(Source, p.xz, p.y, GridStep));
                }
                return map[v];
            }
        }
    }
}
