#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for the ground's seams (W-174 rows 4-5, #405): rebuilds the terrain's cut edge round every replaced
    /// patch of ground the way BuildChunkMeshJob does (its own Seam and Cut) and measures how far the drawn edge strays
    /// from the true rim, the slot or overhang a straight slice between grid points leaves. Temple entrances (W-174 r6)
    /// are cut in the mesh itself, so their check builds the real chunk meshes (TempleSeams).
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>What SeamProblems last measured: per kind of site, sites, rim cells and the worst slot and overhang (m),
        /// with the grid-point edge (no seam builder) in brackets.</summary>
        public static string SeamTrail;

        /// <summary>Widest slot (drawn as cut where the ground should stay: a gap to see through) the seams may leave, in
        /// metres, per kind: temple entrances (exact rectangles), cave mouths and scarp strips (their fields are not true
        /// distances; MouthCut is capped at 0.25 in rock).</summary>
        public const float TempleSeamTolerance = 0.06f, MouthSeamTolerance = 0.2f, ScarpSeamTolerance = 0.25f;
        /// <summary>Widest overhang (terrain drawn where the ground is removed: a lip over the hole) as a share of a quad.
        /// Only kept-side vertices snap, so a convex corner of a hole between two edge snaps keeps a small lip (under the
        /// temple's corner coping); it must also be no wider than the plain grid's.</summary>
        public const float SeamOverhangQuads = 0.35f;
        /// <summary>How much wider (m) than the plain grid's an overhang may be (mouth fields are not true distances).</summary>
        public const float SeamOverhangMargin = 0.05f;

        /// <summary>
        /// W-174 row 5: every open seam ("" when none). Temples: TempleSeams. For each sunken temple, the first three cave mouths and the first
        /// two scarps, at each LOD that cuts it, the grid round the rim is built through BuildChunkMeshJob.Seam; inside
        /// each triangle (the mesh's i0-i2-i1 / i1-i2-i3 split) the interpolated cut, which the shader clips per pixel,
        /// is compared with the cut at the interpolated point: a slot wider than the kind's tolerance, or an overhang past
        /// SeamOverhangQuads or wider than the plain grid's, is an open seam. Ground inside a temple's cut must also read as removed (GroundHoles.Removed).
        /// </summary>
        public static string SeamProblems()
        {
            SeamTrail = "";
            if (!Source(out var s)) return "no terrain";
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
            if (!q.TryGetSingleton(out TerrainConfig config)) return "no terrain config";
            var job = new BuildChunkMeshJob { Source = s, ChunkSize = config.ChunkSize, NormalEpsilon = config.ChunkSize / config.Lod0Quads };
            var problems = new List<string>();
            var trail = new List<string>();

            var temples = new List<float2>();
            if (s.Landmarks.IsCreated)
            {
                ref var set = ref s.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                    if (set.Types[set.Instances[i].Type].Temple != 0)
                        temples.Add(set.Instances[i].Position);
            }
            var mouths = new List<float2>();
            if (s.Caves.IsCreated)
            {
                ref var caves = ref s.Caves.Value;
                for (int i = 0; i < caves.Caves.Length && mouths.Count < 3; i++)
                    if (caves.Caves[i].Tunnel == 0)
                        mouths.Add(caves.Caves[i].Mouth.xz);
            }
            var scarps = new List<float2>();
            if (s.Scarps.IsCreated)
            {
                ref var scarpSet = ref s.Scarps.Value;
                for (int i = 0; i < scarpSet.Scarps.Length && scarps.Count < 2; i++)
                {
                    ref var nodes = ref scarpSet.Scarps[i].Nodes;
                    if (nodes.Length > 0)
                        scarps.Add(nodes[nodes.Length / 2].P);
                }
            }

            if (temples.Count == 0)
                problems.Add("no temple found");
            for (int i = 0; i < temples.Count; i++)
                TempleSeams(job, config, temples[i], i, problems, trail);
            Kind("mouth", mouths, 2, MouthSeamTolerance);
            Kind("scarp", scarps, 3, ScarpSeamTolerance);
            SeamTrail = string.Join("; ", trail);
            return string.Join("; ", problems);

            void Kind(string name, List<float2> sites, int lods, float tolerance)
            {
                if (sites.Count == 0)
                {
                    problems.Add($"no {name} found");
                    return;
                }
                int cells = 0;
                float slot = 0f, over = 0f, gridSlot = 0f, gridOver = 0f;
                for (int site = 0; site < sites.Count; site++)
                for (int lod = 0; lod < lods; lod++)
                {
                    var step = config.ChunkSize / config.QuadsForLod(lod);
                    var r = Rim(job, sites[site], 14f, step, lod, out var g);
                    cells += r.w > 0f ? (int)r.w : 0;
                    if (r.x > tolerance || r.y > SeamOverhangQuads * step || r.y > g.y + SeamOverhangMargin)
                        problems.Add($"{name} {site} at {sites[site].x:F0},{sites[site].y:F0} LOD{lod}: slot {r.x:F2} m (max {tolerance:F2}), " +
                                     $"overhang {r.y:F2} m (max {math.min(SeamOverhangQuads * step, g.y + SeamOverhangMargin):F2})");
                    slot = math.max(slot, r.x);
                    over = math.max(over, r.y);
                    gridSlot = math.max(gridSlot, g.x);
                    gridOver = math.max(gridOver, g.y);
                }
                if (cells == 0)
                    problems.Add($"{name}: no rim cells found");
                trail.Add($"{name} x{sites.Count}: {cells} rim cells, slot {slot:F2} ({gridSlot:F2}) m, overhang {over:F2} ({gridOver:F2}) m");
            }
        }

        /// <summary>
        /// One site at one LOD: (worst slot, worst overhang, unused, rim cells) through the seam builder, and in grid the
        /// worst slot and overhang with plain grid vertices. Cells are skipped unless a corner's cut is within two quads
        /// of the rim.
        /// </summary>
        static float4 Rim(BuildChunkMeshJob job, float2 centre, float half, float step, int lod, out float2 grid)
        {
            grid = 0f;
            var cutHoles = lod <= 1 && job.Source.Caves.IsCreated;
            var lo = (int2)math.floor((centre - half) / step);
            var hi = (int2)math.ceil((centre + half) / step);
            var seam = new Dictionary<int2, float3>();   // snapped xz, cut
            var raw = new Dictionary<int2, float>();
            float3 Vertex(int2 c)
            {
                if (seam.TryGetValue(c, out var vtx)) return vtx;
                var p = (float2)c * step;
                var at = job.Seam(p, job.Source.SampleHeight(p), step, lod, out var cut, out _, out _);
                vtx = new float3(at, cut);
                seam[c] = vtx;
                return vtx;
            }
            float Raw(int2 c)
            {
                if (raw.TryGetValue(c, out var cut)) return cut;
                var p = (float2)c * step;
                return raw[c] = job.Cut(p, job.Source.SampleHeight(p), step, cutHoles, lod, out _, out _);
            }
            float True(float2 p) => job.Cut(p, job.Source.SampleHeight(p), step, cutHoles, lod, out _, out _);

            var result = float4.zero;
            for (int z = lo.y; z < hi.y; z++)
            for (int x = lo.x; x < hi.x; x++)
            {
                var c = new int2(x, z);
                float4 rc = new float4(Raw(c), Raw(c + new int2(1, 0)), Raw(c + new int2(0, 1)), Raw(c + 1));
                if (math.cmin(math.abs(rc)) > 2f * step && math.all(rc > 0f) | math.all(rc < 0f)) continue;
                result.w++;
                // Both triangles: (i0, i2, i1) and (i1, i2, i3).
                var v0 = Vertex(c); var v1 = Vertex(c + new int2(1, 0)); var v2 = Vertex(c + new int2(0, 1)); var v3 = Vertex(c + 1);
                var g0 = new float3((float2)c * step, rc.x); var g1 = new float3((float2)(c + new int2(1, 0)) * step, rc.y);
                var g2 = new float3((float2)(c + new int2(0, 1)) * step, rc.z); var g3 = new float3((float2)(c + 1) * step, rc.w);
                Triangle(v0, v2, v1, ref result.x, ref result.y);
                Triangle(v1, v2, v3, ref result.x, ref result.y);
                Triangle(g0, g2, g1, ref grid.x, ref grid.y);
                Triangle(g1, g2, g3, ref grid.x, ref grid.y);
            }
            return result;

            void Triangle(float3 a, float3 b, float3 c, ref float slot, ref float over)
            {
                const int n = 4;
                for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                {
                    var w = new float3(i, j, n - i - j) / n;
                    var p = a.xy * w.x + b.xy * w.y + c.xy * w.z;
                    var drawn = a.z * w.x + b.z * w.y + c.z * w.z;
                    var truth = True(p);
                    if (drawn < 0f && truth > 0f) slot = math.max(slot, truth);
                    else if (drawn >= 0f && truth < 0f) over = math.max(over, -truth);
                }
            }
        }

        /// <summary>How far (m) round a temple's centre TempleSeams looks: past its ruin's furthest corner.</summary>
        const float TempleSeamReach = 36f;
        /// <summary>TempleSeams' sample spacing (m).</summary>
        const float TempleSeamStep = 0.1f;

        /// <summary>
        /// W-174 r6 (#405): one temple's entrance as the chunk meshes really cut it. At each LOD that cuts it, the chunks
        /// round the temple are built by BuildChunkMeshJob itself; every sample point more than TempleSeamTolerance
        /// outside the cut must lie on a ground triangle (else a slot) and every point that far inside on none (else a
        /// lip), collars and skirts aside (they have no area seen from above). A ground vertex on a chunk edge must have
        /// its twin in the chunk across (rim crossings are found from the same two grid points in the same order, so they
        /// match to the bit), and ground inside the cut must read as removed (GroundHoles.Removed). SeamTrail gets the
        /// rim and collar counts and the tallest collar.
        /// </summary>
        static void TempleSeams(BuildChunkMeshJob job, in TerrainConfig config, float2 centre, int site,
                                List<string> problems, List<string> trail)
        {
            var n = (int)math.ceil(2f * TempleSeamReach / TempleSeamStep) + 1;
            var corner = centre - TempleSeamReach;
            var cut = new float[n * n];
            int unremoved = 0;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var p = corner + new float2(x, z) * TempleSeamStep;
                var c = cut[z * n + x] = job.Source.TempleCut(p);
                if (c < -0.05f && (x & 3) == 0 && (z & 3) == 0 && !GroundHoles.Removed(job.Source, p))
                    unremoved++;
            }
            if (unremoved > 0)
                problems.Add($"temple {site}: {unremoved} points inside the cut not removed (GroundHoles)");

            var notes = new List<string>();
            var lo = (int2)math.floor((centre - TempleSeamReach) / config.ChunkSize);
            var hi = (int2)math.floor((centre + TempleSeamReach) / config.ChunkSize);
            var wide = hi.x - lo.x + 1;
            var count = wide * (hi.y - lo.y + 1);
            for (int lod = 0; lod <= 2; lod++)
            {
                var quads = config.QuadsForLod(lod);
                var data = Mesh.AllocateWritableMeshData(count * 2);
                var requests = new NativeArray<ChunkBuildRequest>(count, Allocator.TempJob);
                var ranges = new NativeArray<float2>(count, Allocator.TempJob);
                var waterRanges = new NativeArray<float2>(count, Allocator.TempJob);
                var terrainLayout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.TempJob);
                var waterLayout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.TempJob);
                var indexCount = BuildChunkMeshJob.IndexCount(quads);
                for (int i = 0; i < count; i++)
                {
                    requests[i] = new ChunkBuildRequest
                    {
                        Origin = (float2)(lo + new int2(i % wide, i / wide)) * config.ChunkSize, Quads = quads, Lod = lod,
                        CanMorph = lod < config.LodLevels - 1 && quads > 1, MorphRange = config.MorphRange(lod)
                    };
                    var d = data[i];
                    d.SetVertexBufferParams(BuildChunkMeshJob.VertexCount(quads), terrainLayout);
                    d.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                    d.subMeshCount = 1;
                    d.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
                var build = job;
                build.Meshes = data;
                build.Requests = requests;
                build.TerrainLayout = terrainLayout;
                build.WaterLayout = waterLayout;
                build.SkirtDepth = config.SkirtDepth;
                build.HeightRanges = ranges;
                build.WaterRanges = waterRanges;

                var covered = new bool[n * n];
                // Ground vertices on chunk edges, per chunk: world xz (to 0.1 mm) -> height.
                var edges = new Dictionary<int2, Dictionary<long, float>>();
                int rims = 0, collars = 0;
                var riser = 0f;
                for (int i = 0; i < count; i++)
                {
                    build.Execute(i);
                    var origin = requests[i].Origin;
                    var verts = data[i].GetVertexData<TerrainVertex>();
                    var tris = data[i].GetIndexData<uint>();
                    var skirts = BuildChunkMeshJob.VertexCount(quads);
                    for (int k = skirts; k < verts.Length; k++)
                        if (verts[k].Morph.w == BuildChunkMeshJob.SeamEpsilon && (float)verts[k].Extra.w == 0f && verts[k].Normal.y != 0f)
                            rims++;
                    var edge = new Dictionary<long, float>();
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int ia = (int)tris[t], ib = (int)tris[t + 1], ic = (int)tris[t + 2];
                        float3 a = verts[ia].Position, b = verts[ib].Position, c = verts[ic].Position;
                        float2 pa = a.xz + origin, pb = b.xz + origin, pc = c.xz + origin;
                        var area = Cross(pb - pa, pc - pa);
                        if (math.abs(area) < 2e-4f)
                        {
                            // A collar (rim down to the wall top) or a skirt: no ground seen from above.
                            var hanging = (float)verts[ia].Extra.w != 0f || (float)verts[ib].Extra.w != 0f || (float)verts[ic].Extra.w != 0f;
                            if (!hanging && (ia >= skirts || ib >= skirts || ic >= skirts))
                            {
                                collars++;
                                riser = math.max(riser, math.cmax(new float3(a.y, b.y, c.y)) - math.cmin(new float3(a.y, b.y, c.y)));
                            }
                            continue;
                        }
                        Cover(pa, pb, pc, area, covered, n, corner);
                        for (int k = 0; k < 3; k++)
                        {
                            var lp = verts[(int)tris[t + k]].Position;
                            if (lp.x == 0f || lp.z == 0f || lp.x == config.ChunkSize || lp.z == config.ChunkSize)
                                edge[Key(lp.xz + origin)] = lp.y;
                        }
                    }
                    edges[lo + new int2(i % wide, i / wide)] = edge;
                }

                int slots = 0, lips = 0;
                float slot = 0f, lip = 0f;
                for (int k = 0; k < cut.Length; k++)
                {
                    var c = cut[k];
                    if (c > TempleSeamTolerance && !covered[k]) { slots++; slot = math.max(slot, c); }
                    else if (c < -TempleSeamTolerance && covered[k]) { lips++; lip = math.max(lip, -c); }
                }
                if (slots > 0 || lips > 0)
                    problems.Add($"temple {site} LOD{lod}: {slots} slot points (widest {slot:F2} m), {lips} lip points (widest {lip:F2} m)");

                // Twins across chunk edges: the same point at the same height on both sides.
                int strays = 0;
                foreach (var kv in edges)
                foreach (var v in kv.Value)
                {
                    var p = new float2((int)(v.Key >> 32) * 1e-4f, (int)(v.Key & 0xffffffffL) * 1e-4f);
                    for (int k = 0; k < 4; k++)
                    {
                        var other = kv.Key + (k == 0 ? new int2(-1, 0) : k == 1 ? new int2(1, 0) : k == 2 ? new int2(0, -1) : new int2(0, 1));
                        if (!edges.TryGetValue(other, out var across))
                            continue;
                        var o = (float2)other * config.ChunkSize;
                        if (math.any(p < o - 1e-3f) || math.any(p > o + config.ChunkSize + 1e-3f))
                            continue;
                        if (!across.TryGetValue(v.Key, out var y) || y != v.Value)
                            strays++;
                    }
                }
                if (strays > 0)
                    problems.Add($"temple {site} LOD{lod}: {strays} chunk-edge vertices without an identical twin across");
                notes.Add($"LOD{lod} {rims} rim, {collars} collar tris, riser {riser:F2} m, slot {slot:F2} lip {lip:F2}");

                data.Dispose();
                requests.Dispose();
                ranges.Dispose();
                waterRanges.Dispose();
                terrainLayout.Dispose();
                waterLayout.Dispose();
            }
            trail.Add($"temple {site}: " + string.Join(", ", notes));
        }

        static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        static long Key(float2 p) => ((long)(int)math.round(p.x * 1e4f) << 32) | (uint)(int)math.round(p.y * 1e4f);

        /// <summary>Marks the sample points (TempleSeamStep apart from corner) inside triangle a-b-c (area: twice its signed area).</summary>
        static void Cover(float2 a, float2 b, float2 c, float area, bool[] covered, int n, float2 corner)
        {
            var lo = math.max((int2)math.floor((math.min(a, math.min(b, c)) - corner) / TempleSeamStep), 0);
            var hi = math.min((int2)math.ceil((math.max(a, math.max(b, c)) - corner) / TempleSeamStep), n - 1);
            const float eps = -1e-5f;
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var p = corner + new float2(x, z) * TempleSeamStep;
                float w0 = Cross(b - p, c - p) / area, w1 = Cross(c - p, a - p) / area, w2 = 1f - w0 - w1;
                if (w0 >= eps && w1 >= eps && w2 >= eps)
                    covered[z * n + x] = true;
            }
        }
    }
}
#endif
