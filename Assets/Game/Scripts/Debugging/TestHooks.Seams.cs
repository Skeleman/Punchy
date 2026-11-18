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
    /// from the true rim, the slot or overhang a straight slice between grid points leaves. Temple entrances and cave
    /// mouths (W-174 r6) are cut in the mesh itself, so their check builds the real chunk meshes (EntranceSeams).
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>What SeamProblems last measured: per kind of site, sites, rim cells and the worst slot and overhang (m),
        /// with the grid-point edge (no seam builder) in brackets.</summary>
        public static string SeamTrail;

        /// <summary>Widest slot (drawn as cut where the ground should stay: a gap to see through) the seams may leave, in
        /// metres, per kind: temple entrances (exact rectangles), cave mouths (the rim between crossings on the grid's
        /// edges is a chord of a curved, noisy line; the collar hangs from the chord, so this is no gap) and scarp strips
        /// (their fields are not true distances).</summary>
        public const float TempleSeamTolerance = 0.06f, MouthSeamTolerance = 0.2f, ScarpSeamTolerance = 0.25f;
        /// <summary>W-174 r6 row 2: how far inside a mouth's cut the ground may still be drawn (m of the cut): the rim is
        /// a straight chord between crossings on the grid's edges, and the noisy cut bows inside it. The collar hangs from
        /// the chord, so this is an overhang of the lip, not a hole (measured up to 0.36 at LOD1).</summary>
        public const float MouthLipTolerance = 0.4f;
        /// <summary>W-174 r6 row 2: how far (m of the walls' field) a mouth collar's foot vertex may sit off the cave walls,
        /// and how far the midpoint of an edge between two feet may bow into the cave's air (the wall curves between
        /// feet a few centimetres apart; the edge then sits in front of the wall skin, never behind a slot; up to 0.07).</summary>
        public const float CollarFootTolerance = 0.01f, CollarChordTolerance = 0.08f;
        /// <summary>How far (m) a mouth's collar foot may stand off its ring sheet (W-174 r3): CaveSampling.MouthRing at the depth it
        /// uses (RingDepth, or a shallower fallback sheet), read from the surface at the walls' own grid step, as the walls clip to it; a foot on no
        /// sheet (a straight-down drop, or a bow foot on the walls' unclipped field below the ring) must not stand above the ground.</summary>
        public const float RingFootTolerance = 0.02f;
        /// <summary>W-174 r6 row 2: feet closer than this (m) are the collar's finest split; a bow between them is a crease.</summary>
        const float CollarCrease = 0.03f;
        /// <summary>Widest overhang (terrain drawn where the ground is removed: a lip over the hole) as a share of a quad.
        /// Only kept-side vertices snap, so a convex corner of a hole between two edge snaps keeps a small lip (under the
        /// temple's corner coping); it must also be no wider than the plain grid's.</summary>
        public const float SeamOverhangQuads = 0.35f;
        /// <summary>How much wider (m) than the plain grid's an overhang may be (mouth fields are not true distances).</summary>
        public const float SeamOverhangMargin = 0.05f;

        /// <summary>
        /// W-174 row 5: every open seam ("" when none). Temples and cave mouths (the first three, and the two on the
        /// steepest ground): EntranceSeams. For the first two scarps, at each LOD that cuts it, the grid round the rim is built through BuildChunkMeshJob.Seam; inside
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
                // W-174 r6 row 2: and the two mouths on the steepest ground, where a slot under the collar would show most.
                for (int pick = 0; pick < 2; pick++)
                {
                    int best = -1;
                    var flattest = 2f;
                    for (int i = 0; i < caves.Caves.Length; i++)
                    {
                        var at = caves.Caves[i].Mouth.xz;
                        if (caves.Caves[i].Tunnel != 0 || mouths.Contains(at))
                            continue;
                        var scale = s.AcrossSlope(at);
                        if (scale < flattest)
                        {
                            flattest = scale;
                            best = i;
                        }
                    }
                    if (best >= 0)
                        mouths.Add(caves.Caves[best].Mouth.xz);
                }
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
                EntranceSeams(job, config, temples[i], i, false, problems, trail);
            if (mouths.Count == 0)
                problems.Add("no mouth found");
            for (int i = 0; i < mouths.Count; i++)
                EntranceSeams(job, config, mouths[i], i, true, problems, trail);
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

        /// <summary>How far (m) round a temple's centre EntranceSeams looks: past its ruin's furthest corner.</summary>
        const float TempleSeamReach = 36f;
        /// <summary>How far (m) round a cave's mouth point EntranceSeams looks.</summary>
        const float MouthSeamReach = 16f;
        /// <summary>EntranceSeams' sample spacing (m).</summary>
        const float TempleSeamStep = 0.1f;

        /// <summary>
        /// W-174 r6 (#405): one entrance (a sunken temple's, or a cave mouth's from row 2) as the chunk meshes really cut
        /// it. At each LOD that cuts it, the chunks round it are built by BuildChunkMeshJob itself; every sample point more
        /// than the kind's tolerance outside the cut must lie on a ground triangle (else a slot) and every point that far
        /// inside on none (else a lip), collars and skirts aside. A ground vertex on a chunk edge must have its twin in the
        /// chunk across (rim crossings are found from the same two grid points in the same order, so they match to the
        /// bit). Temples: ground inside the cut must read as removed (GroundHoles.Removed). Mouths: every edge of the
        /// ground's hole must carry a collar (no gap), every collar foot must lie on the cave walls' field
        /// (CollarFootTolerance; no lip or slot against the wall skin) and so must the midpoints between feet
        /// (CollarChordTolerance), and a foot two chunks share must match to the bit. SeamTrail gets the counts.
        /// </summary>
        static void EntranceSeams(BuildChunkMeshJob job, in TerrainConfig config, float2 centre, int site, bool mouth,
                                  List<string> problems, List<string> trail)
        {
            var name = mouth ? "mouth" : "temple";
            var reach = mouth ? MouthSeamReach : TempleSeamReach;
            var tolerance = mouth ? MouthSeamTolerance : TempleSeamTolerance;
            var lipTolerance = mouth ? MouthLipTolerance : TempleSeamTolerance;
            var n = (int)math.ceil(2f * reach / TempleSeamStep) + 1;
            var corner = centre - reach;
            var cut = new float[n * n];
            if (!mouth)
            {
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
            }

            var notes = new List<string>();
            var lo = (int2)math.floor((centre - reach) / config.ChunkSize);
            var hi = (int2)math.floor((centre + reach) / config.ChunkSize);
            var wide = hi.x - lo.x + 1;
            var count = wide * (hi.y - lo.y + 1);
            var footStep = job.NormalEpsilon;
            for (int lod = 0; lod <= (mouth ? 1 : 2); lod++)
            {
                var quads = config.QuadsForLod(lod);
                var step = config.ChunkSize / quads;
                if (mouth)
                    for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                        cut[z * n + x] = BuildChunkMeshJob.MouthCutAt(job.Source, corner + new float2(x, z) * TempleSeamStep, step);
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
                var dropped = new bool[n * n];   // fine points in the quads of a speck the mesh dropped (DropSpecks)
                int specks = 0;
                // Ground vertices on chunk edges, per chunk: world xz (to 0.1 mm) -> height.
                var edges = new Dictionary<int2, Dictionary<long, float>>();
                // Mouth collar feet: world xz -> height and chunk, the first chunk's (a foot two chunks share must match;
                // within one chunk two feet may stand one above the other on a sheer wall).
                var feetSeen = new Dictionary<long, (float y, int chunk)>();
                int rims = 0, collars = 0, feet = 0, offFeet = 0, stepFeet = 0, stepChords = 0, badChords = 0, orphans = 0, footless = 0, sharedFeet = 0, strayFeet = 0, highFeet = 0, deepFeet = 0, bandFeet = 0;
                float riser = 0f, footOff = 0f, chordOff = 0f, chordSpan = 0f, rockBow = 0f, ringOver = float.NegativeInfinity;
                var why = "";
                for (int i = 0; i < count; i++)
                {
                    build.Execute(i);
                    var origin = requests[i].Origin;
                    var verts = data[i].GetVertexData<TerrainVertex>();
                    var tris = data[i].GetIndexData<uint>();
                    var skirts = BuildChunkMeshJob.VertexCount(quads);
                    // A grid vertex whose kept cut has the other sign than the mouth cut there is in a speck DropSpecks
                    // flipped: the fine points round it (one grid step) are its island, not a slot or a lip.
                    if (mouth)
                        for (int k = 0; k < (quads + 1) * (quads + 1); k++)
                        {
                            var w = verts[k].Position.xz + origin;
                            var mc = BuildChunkMeshJob.MouthCutAt(job.Source, w, step);
                            var kept = verts[k].Morph.w;
                            // (a filled hole keeps a cut >= 0, where a hole vertex holds at most mc; an opened scrap holds -mc)
                            if (math.abs(mc) <= 1e-4f || mc >= 100f || (mc < 0f ? kept < 0f : math.abs(kept + mc) > 1e-3f))
                                continue;
                            specks++;
                            var a0 = (int2)math.ceil((w - step - corner) / TempleSeamStep);
                            var a1 = (int2)math.floor((w + step - corner) / TempleSeamStep);
                            for (int z = math.max(a0.y, 0); z <= math.min(a1.y, n - 1); z++)
                            for (int x = math.max(a0.x, 0); x <= math.min(a1.x, n - 1); x++)
                                dropped[z * n + x] = true;
                        }
                    for (int k = skirts; k < verts.Length; k++)
                    {
                        if (verts[k].Morph.w == BuildChunkMeshJob.SeamEpsilon && (float)verts[k].Extra.w == 0f && verts[k].Normal.y != 0f)
                            rims++;
                        if (mouth && verts[k].Morph.w == BuildChunkMeshJob.CollarFoot)
                        {
                            var w = verts[k].Position + new float3(origin.x, 0f, origin.y);
                            feet++;
                            var surface = job.Source.MeshHeight(w.xz, footStep);
                            var scale = job.Source.AcrossSlope(w.xz);
                            var sheet = math.clamp(math.round((surface - w.y) * scale / 0.1f) * 0.1f, 0.1f, CaveSampling.RingDepth);
                            var over = math.abs(w.y - CaveSampling.MouthRing(surface, scale, sheet));
                            // Off every sheet: MouthFoot's last resort drops straight down onto the walls (anywhere from the ground
                            // to the RingDepth sheet), and a bow split moves a foot onto the walls' field, which below the ring
                            // the walls keep unclipped (the field check below holds it there). Only a foot above the ground fails.
                            if (over > RingFootTolerance)
                            {
                                if (w.y < CaveSampling.MouthRing(surface, scale) - RingFootTolerance)
                                    deepFeet++;
                                else
                                    bandFeet++;
                                over = math.max(0f, w.y - surface);
                            }
                            ringOver = math.max(ringOver, over);
                            if (over > RingFootTolerance)
                                highFeet++;
                            var off = math.abs(BuildChunkMeshJob.WallFieldAt(job.Source, w, footStep));
                            if (off > CollarFootTolerance && FieldStep(job.Source, w, footStep))
                                stepFeet++;   // on a step in the walls' own field (the walls break there too)
                            else
                            {
                                footOff = math.max(footOff, off);
                                if (off > CollarFootTolerance)
                                    offFeet++;
                            }
                            var key = Key(w.xz);
                            if (feetSeen.TryGetValue(key, out var seen))
                            {
                                if (seen.chunk != i)
                                {
                                    sharedFeet++;
                                    if (seen.y != w.y)
                                        strayFeet++;
                                }
                            }
                            else
                                feetSeen[key] = (w.y, i);
                        }
                    }
                    var edge = new Dictionary<long, float>();
                    var groundEdges = new Dictionary<long, int>();   // vertex pair -> ground triangles using it
                    var collarEdges = new HashSet<long>();
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int ia = (int)tris[t], ib = (int)tris[t + 1], ic = (int)tris[t + 2];
                        float3 a = verts[ia].Position, b = verts[ib].Position, c = verts[ic].Position;
                        float2 pa = a.xz + origin, pb = b.xz + origin, pc = c.xz + origin;
                        var area = Cross(pb - pa, pc - pa);
                        var footA = verts[ia].Morph.w == BuildChunkMeshJob.CollarFoot;
                        var footB = verts[ib].Morph.w == BuildChunkMeshJob.CollarFoot;
                        var footC = verts[ic].Morph.w == BuildChunkMeshJob.CollarFoot;
                        if (footA || footB || footC || math.abs(area) < 2e-4f)
                        {
                            // A collar (rim down to the wall top or the cave walls' field) or a skirt: no ground seen from above.
                            var hanging = (float)verts[ia].Extra.w != 0f || (float)verts[ib].Extra.w != 0f || (float)verts[ic].Extra.w != 0f;
                            if (footA || footB || footC || !hanging && (ia >= skirts || ib >= skirts || ic >= skirts))
                            {
                                collars++;
                                riser = math.max(riser, math.cmax(new float3(a.y, b.y, c.y)) - math.cmin(new float3(a.y, b.y, c.y)));
                                collarEdges.Add(Pair(ia, ib));
                                collarEdges.Add(Pair(ib, ic));
                                collarEdges.Add(Pair(ic, ia));
                                // Between two feet the collar's edge must stay on the walls too (unless it spans a step in
                                // the walls' own field, where the walls break as well).
                                if (footA && footB) Chord(a, b);
                                if (footB && footC) Chord(b, c);
                                if (footC && footA) Chord(c, a);
                            }
                            continue;
                        }
                        Cover(pa, pb, pc, area, covered, n, corner);
                        Count(ia, ib);
                        Count(ib, ic);
                        Count(ic, ia);
                        for (int k = 0; k < 3; k++)
                        {
                            var lp = verts[(int)tris[t + k]].Position;
                            if (lp.x == 0f || lp.z == 0f || lp.x == config.ChunkSize || lp.z == config.ChunkSize)
                                edge[Key(lp.xz + origin)] = lp.y;
                        }
                    }
                    edges[lo + new int2(i % wide, i / wide)] = edge;
                    // A hole's edge (one ground triangle, not along the chunk's border) with no collar on it: a gap.
                    if (mouth)
                        foreach (var kv in groundEdges)
                        {
                            if (kv.Value != 1 || collarEdges.Contains(kv.Key))
                                continue;
                            float3 a = verts[(int)(kv.Key >> 32)].Position, b = verts[(int)(kv.Key & 0xffffffffL)].Position;
                            var border = a.x == 0f && b.x == 0f || a.z == 0f && b.z == 0f ||
                                         a.x == config.ChunkSize && b.x == config.ChunkSize || a.z == config.ChunkSize && b.z == config.ChunkSize;
                            if (border || math.distance(a.xz, b.xz) <= 1e-3f)
                                continue;
                            // Told apart: no foot under one end (the walls' field has no zero near), or a collar missing.
                            var wa = a.xz + origin;
                            var wb = b.xz + origin;
                            if (!BuildChunkMeshJob.MouthFoot(job.Source, wa, footStep, out _, out _) ||
                                !BuildChunkMeshJob.MouthFoot(job.Source, wb, footStep, out _, out _))
                            {
                                footless++;
                                if (footless <= 2)
                                {
                                    var g = job.Source.MeshHeight(wa, footStep);
                                    why += $" [{wa.x:F1},{wa.y:F1} field at 0/0.1/0.4/1 m down: " +
                                           $"{BuildChunkMeshJob.WallFieldAt(job.Source, new float3(wa.x, g, wa.y), footStep):F2}/" +
                                           $"{BuildChunkMeshJob.WallFieldAt(job.Source, new float3(wa.x, g - 0.1f, wa.y), footStep):F2}/" +
                                           $"{BuildChunkMeshJob.WallFieldAt(job.Source, new float3(wa.x, g - 0.4f, wa.y), footStep):F2}/" +
                                           $"{BuildChunkMeshJob.WallFieldAt(job.Source, new float3(wa.x, g - 1f, wa.y), footStep):F2}]";
                                }
                            }
                            else
                                orphans++;
                        }

                    void Count(int x, int y)
                    {
                        var key = Pair(x, y);
                        groundEdges[key] = groundEdges.TryGetValue(key, out var c) ? c + 1 : 1;
                    }

                    void Chord(float3 x, float3 y)
                    {
                        var o = new float3(origin.x, 0f, origin.y);
                        var m = 0.5f * (x + y) + o;
                        // Signed: an edge bowing into the rock is hidden behind the wall; one bowing into the cave's air
                        // stands proud of the wall under it. Feet a few centimetres apart with the field folding between
                        // them are a crease in the walls themselves (counted with the steps).
                        var off = BuildChunkMeshJob.WallFieldAt(job.Source, m, footStep);
                        rockBow = math.max(rockBow, -off);
                        if (off <= CollarChordTolerance)
                            chordOff = math.max(chordOff, off);
                        else if (FieldStep(job.Source, x + o, footStep) || FieldStep(job.Source, y + o, footStep) || Stepped(x + o, y + o) ||
                                 math.distance(x, y) < CollarCrease || Jump(m, math.distance(x, y)))
                            stepChords++;
                        else
                        {
                            if (off > chordOff)
                                chordSpan = math.distance(x, y);
                            chordOff = math.max(chordOff, off);
                            badChords++;
                        }
                    }

                    // No wall within half the edge of its middle: the feet sit either side of a break in the walls' field
                    // (the foot jumps from one face to another), which the walls' own mesh shows too.
                    bool Jump(float3 m, float span)
                    {
                        var p = m;
                        return !BuildChunkMeshJob.OntoWall(job.Source, ref p, footStep, out _) || math.distance(p, m) > 0.5f * span;
                    }

                    // The walls' field changes sign more than once along x-y (it folds or steps between the two feet).
                    bool Stepped(float3 x, float3 y)
                    {
                        int flips = 0;
                        var last = BuildChunkMeshJob.WallFieldAt(job.Source, x, footStep);
                        for (int q = 1; q <= 16; q++)
                        {
                            var f = BuildChunkMeshJob.WallFieldAt(job.Source, math.lerp(x, y, q / 16f), footStep);
                            if (f < 0f != last < 0f && math.abs(f - last) > 2f * CollarFootTolerance)
                                flips++;
                            last = f;
                        }
                        return flips > 0;
                    }
                }

                int slots = 0, lips = 0;
                float slot = 0f, lip = 0f;
                for (int k = 0; k < cut.Length; k++)
                {
                    var c = cut[k];
                    if (dropped[k])
                        continue;
                    if (c > tolerance && !covered[k]) { slots++; slot = math.max(slot, c); }
                    else if (c < -lipTolerance && covered[k]) { lips++; lip = math.max(lip, -c); }
                }
                var where = mouth ? $"mouth {site} at {centre.x:F0},{centre.y:F0} (slope scale {job.Source.AcrossSlope(centre):F2})" : $"temple {site}";
                if (slots > 0 || lips > 0)
                    problems.Add($"{where} LOD{lod}: {slots} slot points (widest {slot:F2} m), {lips} lip points (widest {lip:F2} m)");

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
                    problems.Add($"{where} LOD{lod}: {strays} chunk-edge vertices without an identical twin across");
                if (mouth)
                {
                    if (rims > 0 && (feet == 0 || collars == 0))
                        problems.Add($"{where} LOD{lod}: {rims} rim vertices but no collar");
                    if (offFeet > 0)
                        problems.Add($"{where} LOD{lod}: {offFeet} of {feet} collar feet off the cave walls (worst {footOff:F3} m, max {CollarFootTolerance:F2})");
                    if (badChords > 0)
                        problems.Add($"{where} LOD{lod}: {badChords} collar foot edges bowing into the cave's air at their middle (worst {chordOff:F3} m over {chordSpan:F3} m, max {CollarChordTolerance:F2})");
                    if (orphans + footless > 0)
                        problems.Add($"{where} LOD{lod}: {orphans + footless} edges of the hole with no collar ({footless} with no foot under an end{why})");
                    if (highFeet > 0)
                        problems.Add($"{where} LOD{lod}: {highFeet} of {feet} collar feet off their ring sheet and above the ground (MouthRing at the walls' grid step; worst {ringOver:F3} m, max {RingFootTolerance:F2})");
                    if (strayFeet > 0)
                        problems.Add($"{where} LOD{lod}: {strayFeet} of {sharedFeet} feet shared across chunks differ");
                    notes.Add($"LOD{lod} {specks} speck vertices dropped, {rims} rim, {collars} collar tris, {feet} feet (off {footOff:F3}, {stepFeet} on field steps, chord {chordOff:F3} (into rock {rockBow:F3}), {stepChords} chords over steps, shared {sharedFeet}, ring {ringOver:F3}, {deepFeet} below the ring, {bandFeet} off-sheet above it), " +
                              $"riser {riser:F2} m, slot {slot:F2} lip {lip:F2}");
                }
                else
                    notes.Add($"LOD{lod} {rims} rim, {collars} collar tris, riser {riser:F2} m, slot {slot:F2} lip {lip:F2}");

                data.Dispose();
                requests.Dispose();
                ranges.Dispose();
                waterRanges.Dispose();
                terrainLayout.Dispose();
                waterLayout.Dispose();
            }
            trail.Add((mouth ? $"mouth {site} (slope scale {job.Source.AcrossSlope(centre):F2}): " : $"temple {site}: ") + string.Join(", ", notes));
        }

        /// <summary>True when the walls' field changes sign within 3 mm of p along some axis while |field| stays over the
        /// foot tolerance: a step in the field itself (the nearest passage's noise switching), not a foot off the wall.</summary>
        static bool FieldStep(in TerrainSource source, float3 p, float step)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var d = float3.zero;
                d[axis] = 0.003f;
                float lo = BuildChunkMeshJob.WallFieldAt(source, p - d, step), hi = BuildChunkMeshJob.WallFieldAt(source, p + d, step);
                if (lo < 0f != hi < 0f && math.abs(hi - lo) > 2f * CollarFootTolerance)
                    return true;
            }
            return false;
        }

        static long Pair(int a, int b) => ((long)math.min(a, b) << 32) | (uint)math.max(a, b);

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
