using System.Collections.Generic;
using Punchy.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    /// <summary>
    /// R-6 row 2: a stone colossus's whole-body level (0-2) by the same size-on-screen rule as the landmarks
    /// (<see cref="DetailPolicy.LandmarkLod"/>: size is the body's bounding diameter, one decision with hysteresis).
    /// <para>
    /// Each part keeps its own transform, so the far levels follow everything that moves a part: the walker, the rigs'
    /// bones (the golem's rocks), the sways. A far level is the part's own mesh with its vertices clustered on a grid
    /// about one screen pixel wide at the level's nearest switch (vertex clustering on a Burst job, once, when the colossus
    /// is built), so the outline and the colours stay what they were. The part's renderer stops drawing at LOD 1-2 and
    /// the simplified mesh is drawn in its place with the same material, layer and shadow settings. The parts' meshes
    /// are left alone: collision, the rigs' skinned copies and the wide shadow map still read the full ones, and a
    /// rig's skinned body (shown while its walker is awake, the baked part disabled) draws as before.
    /// </para>
    /// Only parts with the colossus glow material, one submesh of triangles and no UVs get far levels; anything else
    /// (lanterns, puffs) keeps drawing itself at every distance.
    /// </summary>
    public class ColossusLod : MonoBehaviour
    {
        /// <summary>Off: every part draws its full mesh (for old/new comparisons).</summary>
        public static bool Enabled = true;
        /// <summary>Every colossus with levels, for probes and logs.</summary>
        public static readonly List<ColossusLod> All = new List<ColossusLod>();
        /// <summary>Main-thread time spent choosing levels and drawing (ms, since start), and frames counted.</summary>
        public static double CpuMs;
        public static int CpuFrames;

        /// <summary>A far level's grid cell, in screen pixels at the level's nearest switch (past the hysteresis).</summary>
        const float CellPixels = 1f;

        struct Part
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Mesh Lod1, Lod2;
        }

        readonly List<Part> parts = new List<Part>();
        string label;
        float size;
        Vector3 centre;
        int level = -1;
        bool forced;
        int[] tris = new int[3];

        // The clustering jobs, until they finish.
        Mesh.MeshDataArray source;
        readonly List<(int part, int lod, Cluster job, JobHandle handle)> pending = new List<(int, int, Cluster, JobHandle)>();
        float startedAt;

        public int Level => level;
        public float Size => size;
        public string Label => label;
        public bool Ready => pending.Count == 0 && parts.Count > 0;
        public Vector3 Centre => transform.TransformPoint(centre);
        /// <summary>Triangles drawn at each level (parts without far levels count their full mesh at every level).</summary>
        public int3 Triangles => tris.Length == 3 ? new int3(tris[0], tris[1], tris[2]) : 0;

        /// <summary>Gives a built colossus its far levels (the clustering runs on workers; it stays at full detail until done).</summary>
        public static void Attach(Transform root, string label, Material material)
        {
            if (root == null || root.GetComponent<ColossusLod>() != null)
                return;
            var lod = root.gameObject.AddComponent<ColossusLod>();
            lod.label = label;
            lod.Build(material);
        }

        void Build(Material material)
        {
            var bounds = new Bounds();
            var any = false;
            var meshes = new List<Mesh>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
                var simple = r.sharedMaterial == material && mesh.isReadable && mesh.subMeshCount == 1
                             && mesh.GetTopology(0) == MeshTopology.Triangles && !mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
                parts.Add(new Part { Renderer = r, Mesh = simple ? mesh : null });
                tris[0] += (int)mesh.GetIndexCount(0) / 3;
                if (simple)
                    meshes.Add(mesh);
            }
            if (!any)
                return;
            size = bounds.size.magnitude;
            centre = transform.InverseTransformPoint(bounds.center);
            All.Add(this);
            if (meshes.Count == 0)
            {
                tris[1] = tris[2] = tris[0];
                return;
            }

            // A cell about a pixel wide where each level starts being drawn: the level's boundary past the hysteresis.
            var rule = DetailPolicy.Huge.Geometry;
            source = Mesh.AcquireReadOnlyMeshData(meshes);
            startedAt = Time.realtimeSinceStartup;
            var m = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Mesh == null)
                    continue;
                var scale = parts[i].Renderer.transform.lossyScale;
                var perUnit = math.max(1e-4f, math.cmax(math.abs((float3)(Vector3)scale)));
                for (int lod = 1; lod <= 2; lod++)
                {
                    var edge = (lod == 1 ? rule.Near : rule.Mid) * (1f + DetailPolicy.Hysteresis);
                    var cell = size / edge * CellPixels / perUnit;
                    var job = new Cluster
                    {
                        Data = source, Index = m, Cell = cell,
                        Positions = new NativeList<float3>(Allocator.Persistent),
                        Normals = new NativeList<float3>(Allocator.Persistent),
                        Colours = new NativeList<float4>(Allocator.Persistent),
                    };
                    pending.Add((i, lod, job, job.Schedule()));
                }
                m++;
            }
        }

        void Update()
        {
            if (pending.Count > 0)
                Collect();
        }

        /// <summary>Once every clustering job is done: makes the far meshes and logs the triangles per level.</summary>
        void Collect()
        {
            foreach (var p in pending)
                if (!p.handle.IsCompleted)
                    return;
            tris[1] = tris[2] = 0;
            foreach (var p in pending)
            {
                p.handle.Complete();
                var job = p.job;
                var mesh = new Mesh
                {
                    name = parts[p.part].Mesh.name + "_lod" + p.lod,
                    indexFormat = job.Positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(job.Positions.AsArray());
                mesh.SetNormals(job.Normals.AsArray());
                if (job.Colours.Length == job.Positions.Length)
                    mesh.SetColors(job.Colours.AsArray());
                var indices = new NativeArray<int>(job.Positions.Length, Allocator.Temp);
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = i;
                mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                indices.Dispose();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var part = parts[p.part];
                if (p.lod == 1) part.Lod1 = mesh; else part.Lod2 = mesh;
                parts[p.part] = part;
                job.Positions.Dispose();
                job.Normals.Dispose();
                job.Colours.Dispose();
            }
            pending.Clear();
            source.Dispose();
            foreach (var part in parts)
            {
                var mesh = part.Mesh != null ? part.Mesh : part.Renderer.GetComponent<MeshFilter>().sharedMesh;
                var full = (int)mesh.GetIndexCount(0) / 3;
                tris[1] += part.Lod1 != null ? (int)part.Lod1.GetIndexCount(0) / 3 : full;
                tris[2] += part.Lod2 != null ? (int)part.Lod2.GetIndexCount(0) / 3 : full;
            }
            var reach = DetailPolicy.LandmarkReach(size);
            Debug.Log($"[colossi] lod {label} {size:F0} m: tris {tris[0]}/{tris[1]}/{tris[2]}, switches {reach.x:F0}/{reach.y:F0} m, " +
                      $"{parts.Count} parts, built in {(Time.realtimeSinceStartup - startedAt) * 1000f:F0} ms");
        }

        void LateUpdate()
        {
            if (pending.Count > 0 || parts.Count == 0)
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var cam = Camera.main;
            if (cam != null)
                DetailPolicy.SetView(cam);
            var view = DetailPolicy.Current.Data;
            var d = math.max(0f, math.distance((float3)Centre, view.Camera) - size * 0.5f);
            level = DetailPolicy.LandmarkLod(size, d, level);
            var far = Enabled && level > 0;
            if (far != forced)
            {
                forced = far;
                foreach (var part in parts)
                    if (part.Lod1 != null && part.Renderer != null)
                        part.Renderer.forceRenderingOff = far;
            }
            if (far)
                foreach (var part in parts)
                {
                    var r = part.Renderer;
                    if (part.Lod1 == null || r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                        continue;
                    var rp = new RenderParams(r.sharedMaterial)
                    {
                        layer = r.gameObject.layer,
                        shadowCastingMode = r.shadowCastingMode,
                        receiveShadows = r.receiveShadows,
                        renderingLayerMask = r.renderingLayerMask,
                        lightProbeUsage = r.lightProbeUsage,
                        reflectionProbeUsage = r.reflectionProbeUsage,
                        motionVectorMode = r.motionVectorGenerationMode,
                    };
                    Graphics.RenderMesh(rp, level == 1 ? part.Lod1 : part.Lod2, 0, r.transform.localToWorldMatrix);
                }
            CpuMs += watch.Elapsed.TotalMilliseconds;
            if (All.Count > 0 && All[0] == this)
                CpuFrames++;
        }

        void OnDestroy()
        {
            foreach (var p in pending)
            {
                p.handle.Complete();
                p.job.Positions.Dispose();
                p.job.Normals.Dispose();
                p.job.Colours.Dispose();
            }
            if (pending.Count > 0)
                source.Dispose();
            pending.Clear();
            foreach (var part in parts)
            {
                if (part.Lod1 != null) Destroy(part.Lod1);
                if (part.Lod2 != null) Destroy(part.Lod2);
            }
            All.Remove(this);
        }

        /// <summary>
        /// Vertex clustering: every vertex moves to the mean of the vertices in its grid cell, triangles that collapse go,
        /// and triangles that land on the same three cells (in the same winding) become one, with their colours averaged.
        /// The output is a flat-shaded triangle soup like the colossi's own meshes; an indexed source keeps smooth normals
        /// (the mean per cell).
        /// </summary>
        [BurstCompile]
        struct Cluster : IJob
        {
            [ReadOnly] public Mesh.MeshDataArray Data;
            public int Index;
            public float Cell;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colours;

            public void Execute()
            {
                var md = Data[Index];
                var n = md.vertexCount;
                var verts = new NativeArray<Vector3>(n, Allocator.Temp);
                md.GetVertices(verts);
                var normals = new NativeArray<Vector3>(n, Allocator.Temp);
                var hasNormals = md.HasVertexAttribute(VertexAttribute.Normal);
                if (hasNormals)
                    md.GetNormals(normals);
                var hasColours = md.HasVertexAttribute(VertexAttribute.Color);
                var colours = new NativeArray<Color>(hasColours ? n : 0, Allocator.Temp);
                if (hasColours)
                    md.GetColors(colours);
                var count = md.GetSubMesh(0).indexCount;
                var indices = new NativeArray<int>(count, Allocator.Temp);
                md.GetIndices(indices, 0);
                var soup = n == count;

                // Cells: the sum of positions (w: how many), normals and colours of the vertices in each.
                var cellOf = new NativeParallelHashMap<int3, int>(n, Allocator.Temp);
                var id = new NativeArray<int>(n, Allocator.Temp);
                var sum = new NativeList<float4>(n / 4 + 16, Allocator.Temp);
                var nsum = new NativeList<float3>(n / 4 + 16, Allocator.Temp);
                var csum = new NativeList<float4>(n / 4 + 16, Allocator.Temp);
                for (int i = 0; i < n; i++)
                {
                    var key = (int3)math.floor((float3)verts[i] / Cell);
                    if (!cellOf.TryGetValue(key, out var c))
                    {
                        c = sum.Length;
                        cellOf.Add(key, c);
                        sum.Add(0f);
                        nsum.Add(0f);
                        csum.Add(0f);
                    }
                    id[i] = c;
                    sum[c] += new float4(verts[i], 1f);
                    nsum[c] += (float3)normals[i];
                    if (hasColours)
                        csum[c] += (float4)(Vector4)colours[i];
                }
                var mean = new NativeArray<float3>(sum.Length, Allocator.Temp);
                for (int c = 0; c < sum.Length; c++)
                    mean[c] = sum[c].xyz / sum[c].w;

                // Triangles by their three cells (rotated so the smallest comes first: the winding is kept).
                var triOf = new NativeParallelHashMap<int3, int>(count / 3 + 16, Allocator.Temp);
                var corners = new NativeList<int3>(count / 6 + 16, Allocator.Temp);
                var tcol = new NativeList<float4>(count / 6 + 16, Allocator.Temp);
                var counts = new NativeList<int>(count / 6 + 16, Allocator.Temp);
                for (int t = 0; t + 2 < count; t += 3)
                {
                    int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
                    int a = id[i0], b = id[i1], c = id[i2];
                    if (a == b || b == c || a == c)
                        continue;
                    var k = a < b && a < c ? new int3(a, b, c) : b < c ? new int3(b, c, a) : new int3(c, a, b);
                    var colour = hasColours ? ((float4)(Vector4)colours[i0] + (float4)(Vector4)colours[i1] + (float4)(Vector4)colours[i2]) / 3f : 1f;
                    if (triOf.TryGetValue(k, out var u))
                    {
                        tcol[u] += colour;
                        counts[u] += 1;
                        continue;
                    }
                    var e = math.cross(mean[k.y] - mean[k.x], mean[k.z] - mean[k.x]);
                    if (math.lengthsq(e) < 1e-12f)
                        continue;
                    triOf.Add(k, corners.Length);
                    corners.Add(k);
                    tcol.Add(colour);
                    counts.Add(1);
                }
                for (int u = 0; u < corners.Length; u++)
                {
                    var k = corners[u];
                    float3 a = mean[k.x], b = mean[k.y], c = mean[k.z];
                    var flat = math.normalizesafe(math.cross(b - a, c - a));
                    var colour = tcol[u] / (float)counts[u];
                    for (int j = 0; j < 3; j++)
                    {
                        var cell = j == 0 ? k.x : j == 1 ? k.y : k.z;
                        Positions.Add(mean[cell]);
                        Normals.Add(soup || !hasNormals ? flat : math.normalizesafe(nsum[cell], flat));
                        if (hasColours)
                            Colours.Add(soup ? colour : csum[cell] / sum[cell].w);
                    }
                }
            }
        }
    }
}
