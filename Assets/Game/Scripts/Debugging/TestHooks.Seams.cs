#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for the ground's seams (W-174 rows 4-5, #405): rebuilds the terrain's cut edge round every replaced
    /// patch of ground the way BuildChunkMeshJob does (its own Seam and Cut) and measures how far the drawn edge strays
    /// from the true rim, the slot or overhang a straight slice between grid points leaves.
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
        /// W-174 row 5: every open seam ("" when none). For each sunken temple, the first three cave mouths and the first
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

            Kind("temple", temples, 2, TempleSeamTolerance, 1);
            Kind("mouth", mouths, 2, MouthSeamTolerance, 0);
            Kind("scarp", scarps, 3, ScarpSeamTolerance, 0);
            SeamTrail = string.Join("; ", trail);
            return string.Join("; ", problems);

            void Kind(string name, List<float2> sites, int lods, float tolerance, int temple)
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
                    var half = temple != 0 ? SunkenTempleShape.Reach : 14f;
                    var r = Rim(job, sites[site], half, step, lod, temple != 0, out var g);
                    cells += r.w > 0f ? (int)r.w : 0;
                    if (r.x > tolerance || r.y > SeamOverhangQuads * step || r.y > g.y + SeamOverhangMargin)
                        problems.Add($"{name} {site} at {sites[site].x:F0},{sites[site].y:F0} LOD{lod}: slot {r.x:F2} m (max {tolerance:F2}), " +
                                     $"overhang {r.y:F2} m (max {math.min(SeamOverhangQuads * step, g.y + SeamOverhangMargin):F2})");
                    if (r.z > 0f)
                        problems.Add($"{name} {site} LOD{lod}: {r.z:F0} points inside the cut not removed (GroundHoles)");
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
        /// One site at one LOD: (worst slot, worst overhang, points in a temple cut not removed, rim cells) through the
        /// seam builder, and in grid the worst slot and overhang with plain grid vertices. Cells are skipped unless a
        /// corner's cut is within two quads of the rim; for a temple, 4 m tiles far from its rooms' rims are skipped first.
        /// </summary>
        static float4 Rim(BuildChunkMeshJob job, float2 centre, float half, float step, int lod, bool temple, out float2 grid)
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
                if (temple)
                {
                    var tile = math.floor((float2)c * step / 4f) * 4f + 2f;
                    if (math.abs(job.Source.TempleCut(tile)) > 5f) continue;
                }
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
                if (temple)
                    for (int k = 0; k < 4; k++)
                    {
                        var p = ((float2)c + new float2(k & 1, k >> 1) * 0.5f + 0.25f) * step;
                        if (job.Source.TempleCut(p) < -0.05f && !GroundHoles.Removed(job.Source, p))
                            result.z++;
                    }
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
    }
}
#endif
