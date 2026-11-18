#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Debugging
{
    public static partial class TestHooks
    {
        public static string MouthPatchTrail;

        const float PatchProbeReach = 24f;
        const float PatchJunctionTolerance = 1e-4f;
        // Managed and Burst noise differ by millimetres; the rims are compared with each other bitwise.
        const float PatchHeightTolerance = 0.01f;

        public static string MouthPatchProblems()
        {
            MouthPatchTrail = "";
            if (!Source(out var s)) return "no terrain";
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
            if (!q.TryGetSingleton(out TerrainConfig config)) return "no terrain config";
            var caves = Object.FindFirstObjectByType<CaveSystem>();
            if (caves == null || !s.Caves.IsCreated) return "no caves";
            var job = new BuildChunkMeshJob { Source = s, ChunkSize = config.ChunkSize, NormalEpsilon = config.ChunkSize / config.Lod0Quads };

            ref var set = ref s.Caves.Value;
            var sites = new List<int>();
            for (int i = 0; i < set.Caves.Length && sites.Count < 3; i++)
                if (set.Caves[i].Tunnel == 0)
                    sites.Add(i);
            int edge = -1, steep = -1;
            float nearest = float.MaxValue, flattest = 2f;
            for (int i = 0; i < set.Caves.Length; i++)
            {
                if (set.Caves[i].Tunnel != 0 || sites.Contains(i))
                    continue;
                var at = set.Caves[i].Mouth.xz;
                var off = math.abs(at - math.round(at / config.ChunkSize) * config.ChunkSize);
                if (math.cmin(off) < nearest) { nearest = math.cmin(off); edge = i; }
                var scale = s.AcrossSlope(at);
                if (scale < flattest) { flattest = scale; steep = i; }
            }
            if (edge >= 0) sites.Add(edge);
            if (steep >= 0 && steep != edge) sites.Add(steep);

            var problems = new List<string>();
            var trail = new List<string>();
            foreach (var i in sites)
                PatchSite(job, config, caves, set.Caves[i].Mouth, i, problems, trail);
            if (sites.Count == 0)
                problems.Add("no mouth found");
            MouthPatchTrail = string.Join("; ", trail);
            return string.Join("; ", problems);
        }

        static void PatchSite(BuildChunkMeshJob job, in TerrainConfig config, CaveSystem caves, float3 mouth, int site,
                              List<string> problems, List<string> trail)
        {
            var s = job.Source;
            float reach;
            int size;
            int2 min;
            NativeArray<bool> mask;
            // Grown until the whole patch fits, so every site has its rim in reach.
            for (reach = PatchProbeReach; ; reach += 16f)
            {
                size = (int)((reach + 8f) * 2f / MouthPatch.Cell) + 1;
                min = (int2)math.floor((mouth.xz - reach - 8f) / MouthPatch.Cell);
                mask = new NativeArray<bool>(size * size, Allocator.Temp);
                MouthPatch.Fill(s, min, size, mask);
                var edge = false;
                for (int i = 0; i < size; i++)
                    edge |= mask[i] || mask[(size - 1) * size + i] || mask[i * size] || mask[i * size + size - 1];
                if (!edge || reach >= PatchProbeReach + 96f)
                    break;
                mask.Dispose();
            }
            var where = $"mouth {site} at {mouth.x:F0},{mouth.z:F0}";

            bool Inside(float2 xz, float margin = 0f) => math.all(math.abs(xz - mouth.xz) <= reach - margin);
            bool Rim(float2 xz)
            {
                MouthPatch.Membership(xz, mask, min, size, out var any, out var all);
                return any && !all;
            }
            float H(float2 xz) => MouthPatch.Height(s, xz, mask, min, size);

            var rock = new List<Vector3>();
            var rockTris = new List<int>();
            var patch = new List<Vector3>();
            var patchTris = new List<int>();
            int flips = 0;
            var lo = (int3)math.floor((mouth - new float3(reach + 4f, 32f, reach + 4f)) / 32f);
            var hi = (int3)math.floor((mouth + new float3(reach + 4f, 32f, reach + 4f)) / 32f);
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
                flips += caves.ProbePatch(s, new int3(x, y, z), rock, rockTris, patch, patchTris);
            int caveFlips = flips;

            var rockEdges = new HashSet<(int3, int3)>();
            for (int t = 0; t < rockTris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                    rockEdges.Add(EdgeKey(rock[rockTris[t + e]], rock[rockTris[t + (e + 1) % 3]]));

            var patchEdges = new Dictionary<(int3, int3), (Vector3, Vector3, int)>();
            int patchCount = 0;
            for (int t = 0; t < patchTris.Count; t += 3)
            {
                float3 a = patch[patchTris[t]], b = patch[patchTris[t + 1]], c = patch[patchTris[t + 2]];
                var n = math.cross(b - a, c - a);
                if (math.lengthsq(n) == 0f)
                    continue;
                if (n.y <= 0f)
                    flips++;
                patchCount++;
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p0 = patch[patchTris[t + e]], p1 = patch[patchTris[t + (e + 1) % 3]];
                    var k = EdgeKey(p0, p1);
                    patchEdges[k] = patchEdges.TryGetValue(k, out var had) ? (p0, p1, had.Item3 + 1) : (p0, p1, 1);
                }
            }

            int holes = 0, offRim = 0, rockJoins = 0;
            var boundary = new List<float3>();
            var caveRim = new Dictionary<int2, List<float>>();
            foreach (var kv in patchEdges)
            {
                if (kv.Value.Item3 != 1)
                    continue;
                float3 a = kv.Value.Item1, b = kv.Value.Item2;
                // The terrain's rim neighbours, one LOD1 step out, must be in reach too.
                if (!Inside(a.xz, 2f) || !Inside(b.xz, 2f))
                    continue;
                if (Rim(a.xz) && Rim(b.xz) && Rim((a.xz + b.xz) * 0.5f))
                {
                    foreach (var p in new[] { a, b })
                    {
                        if (math.abs(p.y - H(p.xz)) > PatchHeightTolerance) offRim++;
                        boundary.Add(p);
                        var xz = math.asint(p.xz);
                        if (!caveRim.TryGetValue(xz, out var ys)) caveRim[xz] = ys = new List<float>();
                        ys.Add(p.y);
                    }
                }
                else if (rockEdges.Contains(kv.Key))
                    rockJoins++;
                else
                    holes++;
            }
            var rockRim = new List<(float3, float3)>();
            for (int t = 0; t < rockTris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    float3 a = rock[rockTris[t + e]], b = rock[rockTris[t + (e + 1) % 3]];
                    if (Inside(a.xz) && Rim(a.xz) && math.abs(a.y - H(a.xz)) <= PatchHeightTolerance)
                    {
                        rockRim.Add((a, b));
                        var xz = math.asint(a.xz);
                        if (!caveRim.TryGetValue(xz, out var ys)) caveRim[xz] = ys = new List<float>();
                        ys.Add(a.y);
                    }
                }

            var notes = new List<string> { $"{patchCount} patch tris, {rockJoins} rock joins" };
            if (patchCount == 0)
                problems.Add($"{where}: no patch drawn");
            if (holes > 0 || offRim > 0)
                problems.Add($"{where}: {holes} open patch edges off the rim, {offRim} patch rim points off the rim height");
            if (caveFlips > 0 || flips > caveFlips)
                problems.Add($"{where}: {caveFlips} cave triangles flipped by the snap, {flips - caveFlips} patch triangles facing down");

            for (int lod = 0; lod <= MouthPatch.MaxLod; lod++)
                TerrainRim(job, config, mouth, reach, lod, where, xz => Inside(xz), xz => Inside(xz, 4f), Rim, H, boundary, caveRim, rockRim, problems, notes);
            mask.Dispose();
            trail.Add($"{where}: " + string.Join(", ", notes));
        }

        static void TerrainRim(BuildChunkMeshJob job, in TerrainConfig config, float3 mouth, float reach, int lod, string where,
                               System.Func<float2, bool> inside, System.Func<float2, bool> core, System.Func<float2, bool> rim, System.Func<float2, float> height,
                               List<float3> boundary, Dictionary<int2, List<float>> caveRim, List<(float3, float3)> rockRim,
                               List<string> problems, List<string> notes)
        {
            var lo = (int2)math.floor((mouth.xz - reach) / config.ChunkSize);
            var hi = (int2)math.floor((mouth.xz + reach) / config.ChunkSize);
            var wide = hi.x - lo.x + 1;
            var count = wide * (hi.y - lo.y + 1);
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

            int rims = 0, offHeight = 0, morphing = 0, split = 0, gaps = 0, flips = 0;
            var segments = new List<(float3, float3)>();
            // Burst, as streaming builds it: the cave's rim is Burst too, and the two must agree bit for bit.
            Unity.Jobs.IJobParallelForExtensions.Run(build, count);
            for (int i = 0; i < count; i++)
            {
                var origin = new float3(requests[i].Origin.x, 0f, requests[i].Origin.y);
                var verts = data[i].GetVertexData<TerrainVertex>();
                var tris = data[i].GetIndexData<uint>();
                var grid = (quads + 1) * (quads + 1);
                var isRim = new bool[grid];
                for (int k = 0; k < grid; k++)
                {
                    var w = verts[k].Position + origin;
                    if (!inside(w.xz) || !rim(w.xz))
                        continue;
                    isRim[k] = true;
                    rims++;
                    if (math.abs(w.y - height(w.xz)) > PatchHeightTolerance) offHeight++;
                    if ((float)verts[k].Emission.w != 0f) morphing++;
                    if (caveRim.TryGetValue(math.asint(w.xz), out var ys))
                    {
                        if (!ys.Contains(w.y)) split++;
                    }
                    else if (core(w.xz) && !OnAny(w, rockRim))
                        gaps++;
                }
                var sign = 0f;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = (int)tris[t], b = (int)tris[t + 1], c = (int)tris[t + 2];
                    if (a >= grid || b >= grid || c >= grid || a == b || b == c || a == c)
                        continue;
                    float3 pa = verts[a].Position, pb = verts[b].Position, pc = verts[c].Position;
                    var turn = Cross((pb - pa).xz, (pc - pa).xz);
                    if (turn == 0f)
                        continue;
                    if (sign == 0f)
                        sign = math.sign(turn);
                    if (!(isRim[a] || isRim[b] || isRim[c]))
                        continue;
                    if (math.sign(turn) != sign)
                        flips++;
                    int[] corner = { a, b, c };
                    for (int e = 0; e < 3; e++)
                    {
                        int u = corner[e], v = corner[(e + 1) % 3];
                        var wu = verts[u].Position + origin;
                        var wv = verts[v].Position + origin;
                        if (isRim[u] && isRim[v] && rim((wu.xz + wv.xz) * 0.5f))
                            segments.Add((wu, wv));
                    }
                }
            }
            foreach (var p in boundary)
                if (!OnAny(p, segments))
                    gaps++;
            notes.Add($"LOD{lod} {rims} rim verts, {segments.Count} rim edges");
            if (offHeight > 0 || morphing > 0 || split > 0 || gaps > 0 || flips > 0)
                problems.Add($"{where} LOD{lod}: {offHeight} rim verts off the rim height, {morphing} morphing, {split} split from the cave's, {gaps} gaps, {flips} flipped");

            data.Dispose();
            requests.Dispose();
            ranges.Dispose();
            waterRanges.Dispose();
            terrainLayout.Dispose();
            waterLayout.Dispose();
        }

        static bool OnAny(float3 p, List<(float3, float3)> segments)
        {
            foreach (var (a, b) in segments)
            {
                var ab = b - a;
                var t = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-12f));
                if (math.distance(p, a + ab * t) <= PatchJunctionTolerance)
                    return true;
            }
            return false;
        }

        static (int3, int3) EdgeKey(float3 a, float3 b)
        {
            int3 ka = math.asint(a), kb = math.asint(b);
            var swap = ka.x != kb.x ? ka.x > kb.x : ka.y != kb.y ? ka.y > kb.y : ka.z > kb.z;
            return swap ? (kb, ka) : (ka, kb);
        }
    }
}
#endif
