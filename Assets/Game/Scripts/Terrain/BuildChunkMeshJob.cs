using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>Must match TerrainAttributes in TerrainSurface.hlsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct TerrainVertex
    {
        public float3 Position;
        public float3 Normal;
        public half4 Surface;   // albedo rgb (linear), smoothness
        public half4 Extra;     // metallic, grass density, snow depth, skirt drop (0 on the surface)
        public half4 Emission;  // emission rgb (linear HDR), geomorph offset (coarse height − height)
        public float4 Morph;    // geomorph start / complete distance from the focus; z surface bumpiness; w distance to cave air
        public half4 Strata;    // x rock strata (> 0) or cracks (< 0), cobble paving amount, hollow depth (m, for puddles), flagstones (W-108)
        public half2 Soak;      // porosity (how much rain soaks in, item 22), the next-coarser LOD's hollow depth (W-169)

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float16, 2)
        };

        public void SetSurface(in SurfaceProps s)
        {
            Surface = new half4(new float4(s.Albedo, s.Smoothness));
            Extra = new half4(new float4(s.Metallic, s.Grass, s.Snow, 0f));
            Emission = new half4(new float4(s.Emission, 0f));
            // Signed: no channel was free, and strata (mesa cliffs) never share ground with cracks (salt flats).
            Strata = new half4(new float4(s.Strata - s.Cracks, s.Paving, 0f, s.Slabs));
            Soak = new half2(new float2(s.Porosity, 0f));
        }
    }

    /// <summary>Must match WaterAttributes in Water.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct WaterVertex
    {
        public float3 Position;
        public float4 Data;     // flow direction xz, depth below the surface, rapids 0..1
        public float4 Look;     // biome water colour rgb (linear), murk 0..1
        public float2 Glow;     // self-glow (toxic water), lava 0..1

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2)
        };
    }

    struct ChunkBuildRequest
    {
        public float2 Origin;
        public int Quads;
        public int Lod;
        /// <summary>Whether a coarser LOD exists to morph towards.</summary>
        public bool CanMorph;
        public float2 MorphRange;
        /// <summary>W-136: the nearest a chunk of this LOD gets to the camera (m), for the ground contacts' screen gate.</summary>
        public float Near;
    }

    /// <summary>
    /// Builds two meshes per chunk. Terrain (index i): a (Quads+1)² height grid in chunk-local space with
    /// geomorph data, plus a skirt hanging down from each edge to cover cracks while neighbours are mid-rebuild.
    /// Water (index Count + i): a sheet at the local water surface wherever sea, lake or river water lies,
    /// or an empty mesh when the chunk is dry.
    /// </summary>
    [BurstCompile]
    partial struct BuildChunkMeshJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Meshes;
        [ReadOnly] public NativeArray<ChunkBuildRequest> Requests;
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> WaterLayout;
        /// <summary>W-174 r6: TerrainVertex.Layout, for a chunk whose temple collar changes its vertex count (unset: no collars).</summary>
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> TerrainLayout;
        public TerrainSource Source;
        public float ChunkSize;
        public float NormalEpsilon;
        public float SkirtDepth;
        /// <summary>W-136: placed plants and their feet, darkening the ground where they meet it (unset: no darkening).</summary>
        public BlobAssetReference<Punchy.Flora.FloraBlob> Flora;
        public BlobAssetReference<GroundContacts.Table> Contacts;
        public float4 ContactStrength, ContactReach;
        public float ContactPixelsPerMetre;
        /// <summary>W-155: clay view (launch flag -clay): every vertex takes one flat grey surface.</summary>
        public bool Clay;
        /// <summary>Output: (min, max) local Y of the terrain mesh, for bounds.</summary>
        public NativeArray<float2> HeightRanges;
        /// <summary>Output: (min, max) Y of the water mesh; x &gt; y when the chunk has no water.</summary>
        public NativeArray<float2> WaterRanges;

        public static int VertexCount(int quads) => (quads + 1) * (quads + 1) + 4 * (quads + 1);
        public static int IndexCount(int quads) => quads * quads * 6 + 4 * quads * 12;

        public void Execute(int index)
        {
            var request = Requests[index];
            var mesh = Meshes[index];
            var verts = mesh.GetVertexData<TerrainVertex>();
            var indices = mesh.GetIndexData<uint>();

            int q = request.Quads;
            int v = q + 1;
            float step = ChunkSize / q;
            float minY = float.MaxValue, maxY = float.MinValue;
            var water = new NativeArray<float4>(v * v, Allocator.Temp);   // surface, flow xz, terrain height
            var rapids = new NativeArray<float>(v * v, Allocator.Temp);
            var looks = new NativeArray<float4>(v * v, Allocator.Temp);   // water colour + murk, glow in a separate array
            var glows = new NativeArray<float2>(v * v, Allocator.Temp);
            var pooled = new NativeArray<bool>(v * v, Allocator.Temp);
            var templeLod = request.Lod <= TempleMaxLod && Source.Landmarks.IsCreated && TerrainLayout.IsCreated;
            var entranceLod = templeLod;
            var templeCut = new NativeArray<float>(entranceLod ? v * v : 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var worlds = new NativeArray<float2>(entranceLod ? v * v : 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var rawCave = new NativeArray<float>(entranceLod ? v * v : 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var collared = false;
            var corners = new NativeList<float2>(16, Allocator.Temp);
            if (templeLod)
                TempleCorners(request.Origin, step, ref corners);
            // Cave entrances: each vertex carries its distance to cave air, and the terrain shader cuts away the
            // surface just inside the cave's edge (per pixel, so the opening follows the cave, not the grid).
            // W-136: darkening where trunks, rocks, walls and props meet the ground, multiplied into the albedo.
            var shaded = Flora.IsCreated && Contacts.IsCreated;
            var shade = new NativeArray<float>(shaded ? v * v : 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            if (shaded)
            {
                for (int i = 0; i < shade.Length; i++)
                    shade[i] = 1f;
                GroundContacts.Rasterise(ref Flora.Value, ref Contacts.Value, Source, request.Origin, v, step, request.Near,
                                         ContactPixelsPerMetre, ContactStrength, ContactReach, shade);
            }

            var patchMin = (int2)math.floor(request.Origin / MouthPatch.Cell) - 1;
            var patchSize = (int)(ChunkSize / MouthPatch.Cell) + 2;
            var patch = new NativeArray<bool>(request.Lod <= MouthPatch.MaxLod ? patchSize * patchSize : 1, Allocator.Temp);
            var patched = request.Lod <= MouthPatch.MaxLod && MouthPatch.Fill(Source, patchMin, patchSize, patch);
            var rims = new NativeArray<bool>(patched ? v * v : 1, Allocator.Temp);

            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                var local = new float2(x, z) * step;
                var world = request.Origin + local;
                var grid = world;          // the water sheet keeps the grid point (it is built on the grid)
                var ground = Source.SampleGround(world);
                var gridGround = ground;
                var h = ground.Height;
                // Cut where the ground is replaced (see Cut). W-174 row 4 (#405), the one seam builder: a ground vertex
                // near a rim moves onto it, so the per-pixel cut's edge runs through vertices on the rim (exact corners and
                // noisy mouth rims, no slot beside a temple wall) instead of between grid points.
                world = Seam(world, h, step, request.Lod, out var cave, out var strip, out var stripY);
                // W-174 r6: the vertex nearest a corner of the temple's entrance takes it, so the rim turns on a vertex.
                if (templeLod && world.Equals(grid) && SnapCorner(corners, grid, step, out var corner))
                    world = corner;
                var rim = false;
                if (patched)
                {
                    MouthPatch.Membership(grid, patch, patchMin, patchSize, out var any, out var all);
                    rim = any && !all;
                    rims[z * v + x] = rim;
                }
                if (rim)
                {
                    world = grid;
                    strip = 100f;
                }
                if (!world.Equals(grid))
                {
                    local = world - request.Origin;
                    ground = Source.SampleGround(world);
                    h = ground.Height;
                }
                if (rim)
                    h = MouthPatch.Height(Source, grid, patch, patchMin, patchSize);
                // W-174 r6: entrance cuts are not snapped to; their triangles are split on the rim (Collars). They still go
                // into the vertex's cut, so a triangle Collars can't split is clipped per pixel as before.
                if (entranceLod)
                {
                    var tc = templeLod ? Source.TempleCut(world) : 100f;
                    templeCut[z * v + x] = tc;
                    worlds[z * v + x] = world;
                    collared |= tc < 0f;
                    rawCave[z * v + x] = cave;   // before the entrance cut, for a dropped speck (DropSpecks)
                    if (math.abs(tc) < RimOn)
                    {
                        tc = 0f;
                        cave = math.min(cave, SeamEpsilon);
                    }
                    else
                        cave = math.min(cave, tc);
                    templeCut[z * v + x] = tc;
                }
                // Fixed-scale normals keep lighting identical across LODs, so LOD switches don't flicker.
                var normal = Source.SampleNormal(world, NormalEpsilon);
                minY = math.min(minY, h);
                maxY = math.max(maxY, h);

                var vertex = new TerrainVertex { Position = new float3(local.x, h, local.y), Normal = normal };
                var surface = Source.SampleSurface(world, ground, normal);
                if (shaded)
                    surface.Albedo *= shade[z * v + x];
                if (Clay)
                    surface = ScarpClay.Surface();
                vertex.SetSurface(surface);
                // Ground just above the water stays wet (a dark band along shores); stored as a negative skirt drop.
                if (ground.HasWater)
                    vertex.Extra.w = (half)(-(1f - math.smoothstep(0.1f, 1.4f, h - ground.WaterSurface)));
                // W-155: the vertices in a scarp's strip carry the level run-on of the rim or the foot, so the cut
                // edges land on both.
                if (strip < 100f)
                {
                    if (strip < 0f)
                    {
                        vertex.Position.y = stripY;
                        minY = math.min(minY, stripY);
                        maxY = math.max(maxY, stripY);
                    }
                }
                vertex.Morph = new float4(request.MorphRange, surface.Bump, cave);
                // W-98: how far this point sits below the ground round it (rings at 2.5 m and 6 m), so rain puddles
                // gather in the dips (TerrainSurface.shader) rather than anywhere flat. Only near (puddles fade far off).
                // Item 22: only a true hollow, with the ground rising on every side: the depth is capped by the lowest
                // rise (twice it), so a gully running downhill, or anywhere on a slope, has a side that falls away
                // and gets none, however far it sits below its banks on average.
                if (request.Lod <= HollowMaxLod)
                {
                    float ring = 0f, lowest = float.MaxValue;
                    for (int k = 0; k < 8; k++)
                    {
                        math.sincos(k * (math.PI / 4f) + (k & 1) * 0.3f, out var sn, out var cs);
                        var around = Source.SampleHeight(world + new float2(cs, sn) * ((k & 1) == 0 ? 2.5f : 6f));
                        ring += around;
                        lowest = math.min(lowest, around);
                    }
                    vertex.Strata.z = (half)math.clamp(math.min(ring / 8f - h, 2f * (lowest - h)), -1f, 1f);
                }
                vertex.Soak.y = vertex.Strata.z;   // (an unmorphed chunk keeps its own; WriteMorphOffsets sets the rest)
                verts[z * v + x] = vertex;
                // Landmark pools are left out of the sheet: CaveSystem bakes each on the finer 0.5 m water lattice, fitted
                // to its basin (W-148; a small pool spans only a few quads of this grid, which gave it a ragged edge).
                var sheet = gridGround.WaterSurface;
                if (Source.Landmarks.IsCreated)
                {
                    var pool = LandmarkSampling.PoolWater(ref Source.Landmarks.Value, grid, out var claim);
                    if (claim > 0f && pool >= gridGround.WaterSurface - 0.001f)
                        sheet = GroundSample.NoWater;
                    pooled[z * v + x] = claim > 0f;
                }
                water[z * v + x] = new float4(sheet, gridGround.Flow, gridGround.Height);
                rapids[z * v + x] = gridGround.Rapids;
                looks[z * v + x] = Source.WaterLook(grid, gridGround.Biomes, out var glow);

                glows[z * v + x] = glow;
            }

            // W-174 r3 (#365): stray specks of the mouth cut (a lone hole beside a mouth, a scrap of ground inside one)
            // are dropped here, at the grid's own resolution: each flipped vertex takes back its cut without the entrance.
            if (collared)
            {
                var flip = new NativeArray<bool>(v * v, Allocator.Temp);
                if (DropSpecks(templeCut, v, step, flip))
                {
                    collared = false;
                    for (int i = 0; i < v * v; i++)
                    {
                        if (flip[i])
                        {
                            var tc = -templeCut[i];
                            templeCut[i] = tc;
                            var vertex = verts[i];
                            vertex.Morph.w = math.min(rawCave[i], tc);
                            verts[i] = vertex;
                        }
                        collared |= templeCut[i] < 0f;
                    }
                }
                flip.Dispose();
            }
            rawCave.Dispose();

            ExtendStillWater(verts, water, pooled, q, step, request.Origin);
            pooled.Dispose();

            if (request.CanMorph)
                WriteMorphOffsets(verts, q, request.Lod + 1 <= HollowMaxLod);
            // #301: the patch doesn't morph, so its rim mustn't either.
            for (int i = 0; patched && i < v * v; i++)
            {
                if (!rims[i])
                    continue;
                var vertex = verts[i];
                vertex.Emission.w = default;
                vertex.Soak.y = vertex.Strata.z;
                verts[i] = vertex;
            }
            rims.Dispose();

            var depth = SkirtDepth * (request.Lod + 1);
            int skirtBase = v * v;
            for (int edge = 0; edge < 4; edge++)
            for (int k = 0; k < v; k++)
            {
                var g = verts[EdgeVertex(edge, k, q)];
                // A skirt hangs metres below the surface, so over a shallow cave it would poke down through the ceiling
                // as a flat face: there it stops short of the cave's air (at every LOD).
                var drop = SkirtOverCave(request.Origin + g.Position.xz, g.Position.y, depth);
                g.Position.y -= drop;
                // The shader lifts skirt vertices back by this much for shadows, so skirts neither
                // cast shadows nor sit in the shadow of the surface above them.
                g.Extra.w = (half)drop;
                verts[skirtBase + edge * v + k] = g;
            }

            int t = 0;
            for (int z = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                uint i0 = (uint)(z * v + x);
                uint i1 = i0 + 1;
                uint i2 = i0 + (uint)v;
                uint i3 = i2 + 1;
                if (patched && InPatch(request.Origin + (new float2(x, z) + 0.5f) * step))
                {
                    for (int k = 0; k < 6; k++)
                        indices[t++] = i0;
                    continue;
                }
                indices[t++] = i0; indices[t++] = i2; indices[t++] = i1;
                indices[t++] = i1; indices[t++] = i2; indices[t++] = i3;
            }

            // Skirts are double-sided so winding doesn't depend on which edge they're on.
            for (int edge = 0; edge < 4; edge++)
            for (int k = 0; k < q; k++)
            {
                uint a = (uint)EdgeVertex(edge, k, q);
                uint b = (uint)EdgeVertex(edge, k + 1, q);
                uint sa = (uint)(skirtBase + edge * v + k);
                uint sb = sa + 1;
                var inside = 0.5f * (verts[(int)a].Position.xz + verts[(int)b].Position.xz);
                if (patched && InPatch(request.Origin + inside + math.sign(ChunkSize * 0.5f - inside) * (step * 0.5f)))
                {
                    for (int n = 0; n < 12; n++)
                        indices[t++] = a;
                    continue;
                }
                indices[t++] = a; indices[t++] = b; indices[t++] = sa;
                indices[t++] = b; indices[t++] = sb; indices[t++] = sa;
                indices[t++] = a; indices[t++] = sa; indices[t++] = b;
                indices[t++] = b; indices[t++] = sa; indices[t++] = sb;
            }

            if (collared)
                Collars(ref mesh, verts, worlds, templeCut, request, step, depth, ref minY, ref maxY);
            templeCut.Dispose();
            patch.Dispose();

            bool InPatch(float2 xz)
            {
                var cell = (int2)math.floor(xz / MouthPatch.Cell) - patchMin;
                return patch[cell.y * patchSize + cell.x];
            }
            corners.Dispose();
            worlds.Dispose();

            HeightRanges[index] = new float2(minY - depth, maxY);
            WaterRanges[index] = BuildWater(Meshes[Requests.Length + index], water, rapids, looks, glows, q, step, request.Origin);
            water.Dispose();
            rapids.Dispose();
            looks.Dispose();
            glows.Dispose();
        }

        /// <summary>
        /// Stores, per vertex, how far it must move to lie on the next-coarser LOD's surface. Even vertices exist in
        /// the coarse grid; odd ones sit on a coarse edge or on the coarse quad's diagonal (the same anti-diagonal
        /// split the triangles use), so a fully morphed mesh matches the coarse mesh exactly.
        /// </summary>
        static void WriteMorphOffsets(NativeArray<TerrainVertex> verts, int q, bool coarseHollow)
        {
            int v = q + 1;
            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                bool oddX = (x & 1) == 1, oddZ = (z & 1) == 1;
                var vertex = verts[z * v + x];
                // W-169: the hollow depth morphs with the height, onto what the next-coarser LOD carries (none past
                // HollowMaxLod), so puddles shrink away across the morph band instead of stopping at the chunk edge.
                if (!coarseHollow)
                    vertex.Soak.y = (half)0f;
                if (oddX || oddZ)
                {
                    int2 a, b;
                    if (oddX && oddZ) { a = new int2(x + 1, z - 1); b = new int2(x - 1, z + 1); }
                    else if (oddX) { a = new int2(x - 1, z); b = new int2(x + 1, z); }
                    else { a = new int2(x, z - 1); b = new int2(x, z + 1); }
                    var va = verts[a.y * v + a.x];
                    var vb = verts[b.y * v + b.x];
                    vertex.Emission.w = (half)(0.5f * (va.Position.y + vb.Position.y) - vertex.Position.y);
                    if (coarseHollow)
                        vertex.Soak.y = (half)(0.5f * ((float)va.Strata.z + (float)vb.Strata.z));
                }
                verts[z * v + x] = vertex;
            }
        }

        /// <summary>The coarsest LOD that carries the hollow depth (puddles): its 2.5 m and 6 m rings need about 2 m vertices.</summary>
        const int HollowMaxLod = 1;

        /// <summary>How far (metres) still water reaches out over dry vertices below its level, and wets the shore.</summary>
        const float StillReach = 8f;
        /// <summary>Extended still water never gets deeper than this over its vertex (a real basin is claimed already).</summary>
        const float StillReachDepth = 1.5f;
        /// <summary>W-111: how far below a neighbour's surface a dry vertex may lie and still get a tucked sheet edge.</summary>
        const float TuckDrop = 1.5f;

        /// <summary>
        /// Shallow beaches: the ground's water claim (SampleGround) is decided per coarse cell, so on a beach that dips
        /// only centimetres below a lake or the sea the claim stops along cell lines, leaving dry vertices under the
        /// water level (holes and straight cuts in the sheet) and a wet-sand band cut into rectangles. Here, still
        /// water (no flow: lakes, sea) reaches up to StillReach out from its edge: a dry vertex takes the level when the
        /// ground all the way to the water lies below it (so it never floats over sand or spills past a ridge), and
        /// ground above the level near still water gets the wet band. Mesh only: the ground and its hashes are unchanged.
        /// Each vertex is decided from world samples (an apron round the chunk), so chunks and LODs agree on borders.
        /// </summary>
        void ExtendStillWater(NativeArray<TerrainVertex> verts, NativeArray<float4> water, NativeArray<bool> pooled,
                              int q, float step, float2 origin)
        {
            int v = q + 1;
            float maxStill = float.MinValue;
            for (int i = 0; i < v * v; i++)
                if (water[i].x > GroundSample.NoWater * 0.5f && math.lengthsq(water[i].yz) < 1e-6f)
                    maxStill = math.max(maxStill, water[i].x);
            if (maxStill == float.MinValue)
                return;

            int a = math.max(1, (int)math.ceil(StillReach / step));
            int w = v + 2 * a;
            // x: still-water level (NoWater where dry, flowing or a landmark pool), y: ground (+inf blocks a pool).
            var apron = new NativeArray<float2>(w * w, Allocator.Temp);
            for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                int lx = x - a, lz = z - a;
                float4 s;
                bool pool;
                if (lx >= 0 && lz >= 0 && lx < v && lz < v)
                {
                    s = water[lz * v + lx];
                    pool = pooled[lz * v + lx];
                }
                else
                {
                    var at = origin + new float2(lx, lz) * step;
                    s = WaterAt(at);
                    pool = false;
                    if (Source.Landmarks.IsCreated)
                    {
                        LandmarkSampling.PoolWater(ref Source.Landmarks.Value, at, out var claim);
                        pool = claim > 0f;
                    }
                }
                var still = s.x > GroundSample.NoWater * 0.5f && math.lengthsq(s.yz) < 1e-6f;
                apron[z * w + x] = pool ? new float2(GroundSample.NoWater, float.MaxValue)
                                        : new float2(still ? s.x : GroundSample.NoWater, s.w);
                if (still)
                    maxStill = math.max(maxStill, s.x);
            }

            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                var i = z * v + x;
                var h = water[i].w;
                var own = water[i].x > GroundSample.NoWater * 0.5f;
                if (pooled[i] || h > maxStill + 1.4f || own && water[i].x > h)
                    continue;

                float sheet = GroundSample.NoWater, near = GroundSample.NoWater, nearDist = float.MaxValue;
                for (int d = 0; d < 8; d++)
                {
                    var dir = d switch { 0 => new int2(1, 0), 1 => new int2(-1, 0), 2 => new int2(0, 1), 3 => new int2(0, -1),
                                         4 => new int2(1, 1), 5 => new int2(-1, 1), 6 => new int2(1, -1), _ => new int2(-1, -1) };
                    var pathTop = float.MinValue;
                    for (int k = 1; k <= a; k++)
                    {
                        var c = apron[(z + a + dir.y * k) * w + x + a + dir.x * k];
                        if (c.x > GroundSample.NoWater * 0.5f)
                        {
                            if (!own && c.x > h + 0.005f && c.x - h < StillReachDepth && c.x > pathTop)
                                sheet = math.max(sheet, c.x);
                            break;
                        }
                        pathTop = math.max(pathTop, c.y);
                        if (c.y == float.MaxValue)
                            break;
                    }
                }

                // W-53: the nearest still water by true distance (square rings outward, stopping once a ring can't
                // beat the best), so the band's reach is round instead of the 8-ray octagon with straight diagonals.
                for (int k = 1; k <= a && k * step < nearDist; k++)
                for (int dz = -k; dz <= k; dz++)
                {
                    var stride = dz == -k || dz == k ? 1 : 2 * k;
                    for (int dx = -k; dx <= k; dx += stride)
                    {
                        var c = apron[(z + a + dz) * w + x + a + dx];
                        if (c.x <= GroundSample.NoWater * 0.5f)
                            continue;
                        var dist = math.sqrt(dx * dx + dz * dz) * step;
                        if (dist < nearDist)
                        {
                            nearDist = dist;
                            near = c.x;
                        }
                    }
                }

                // Under extended water the sheet would hang in a cave's air where a passage runs just below.
                if (sheet > GroundSample.NoWater * 0.5f && Source.Caves.IsCreated)
                {
                    var at = origin + new float2(x, z) * step;
                    if (CaveSampling.NearColumn(ref Source.Caves.Value, at) && Source.CaveDistance(new float3(at.x, sheet, at.y)) < 0.5f)
                        sheet = GroundSample.NoWater;
                }
                if (sheet > GroundSample.NoWater * 0.5f)
                {
                    var s = water[i];
                    s.x = sheet;
                    s.yz = 0f;
                    water[i] = s;
                    near = sheet;
                    nearDist = 0f;
                }

                // Wet band from the nearest still water, fading out with distance so it never ends on a cell line.
                if (near > GroundSample.NoWater * 0.5f)
                {
                    var wet = (1f - math.smoothstep(0.1f, 1.4f, h - near)) * (1f - math.smoothstep(StillReach * 0.5f, StillReach, nearDist));
                    var vertex = verts[i];
                    vertex.Extra.w = (half)math.min((float)vertex.Extra.w, -wet);
                    verts[i] = vertex;
                }
            }
            apron.Dispose();
        }

        /// <summary>Builds the water sheet; returns its (min, max) height, or (1, 0) when the chunk is dry.</summary>
        float2 BuildWater(Mesh.MeshData mesh, NativeArray<float4> water, NativeArray<float> rapids,
                          NativeArray<float4> looks, NativeArray<float2> glows, int q, float step, float2 origin)
        {
            int v = q + 1;

            // Vertices with no water body of their own take the highest neighbouring surface, so a sheet's border
            // extends flat under the banks instead of sloping off towards nothing; but only where the ground is
            // above that surface (a real bank), never out over lower ground beyond a raised pool's rim.
            var surface = new NativeArray<float>(v * v, Allocator.Temp);
            var flow = new NativeArray<float2>(v * v, Allocator.Temp);
            for (int i = 0; i < v * v; i++)
            {
                surface[i] = water[i].x;
                flow[i] = water[i].yz;
            }
            // Neighbours just across the chunk's edge count too (sampled here), or a sheet would stop short with a
            // straight edge wherever a shore runs along a chunk border.
            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                var i = z * v + x;
                if (water[i].x > GroundSample.NoWater * 0.5f)
                    continue;
                var best = GroundSample.NoWater;
                var edge = x == 0 || z == 0 || x == v - 1 || z == v - 1;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var nz = z + dz;
                    var inside = nx >= 0 && nz >= 0 && nx < v && nz < v;
                    if (!inside && !edge)
                        continue;
                    var n = inside ? water[nz * v + nx] : WaterAt(origin + new float2(nx, nz) * step);
                    if (n.x > best && water[i].w >= n.x - 0.05f)
                    {
                        best = n.x;
                        flow[i] = n.yz;
                    }
                }
                // W-111: a dry vertex a little below a neighbour's surface (outside a pond's thin rim, which the mesh
                // can't resolve) used to get nothing, so the quad between dropped and the sheet ended in a staircase
                // of whole quads short of the shore. It now takes a surface tucked just under its own ground: the
                // quad is drawn and the sheet meets the ground along a smooth line inside it. Not past TuckDrop (a
                // cliff below a raised pool keeps its edge rather than a steep sloping sheet).
                if (best <= GroundSample.NoWater * 0.5f)
                {
                    var top = GroundSample.NoWater;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var nz = z + dz;
                        var inside = nx >= 0 && nz >= 0 && nx < v && nz < v;
                        if (!inside && !edge)
                            continue;
                        var n = inside ? water[nz * v + nx] : WaterAt(origin + new float2(nx, nz) * step);
                        if (n.x > top && n.x > n.w + 0.02f && water[i].w >= n.x - TuckDrop)
                        {
                            top = n.x;
                            flow[i] = n.yz;
                        }
                    }
                    if (top > GroundSample.NoWater * 0.5f)
                        best = water[i].w - 0.05f;
                }
                // Under a bank the sheet is hidden in the ground, unless a cave runs through the bank: there it would
                // hang in the cave's air as a flat face across the passage.
                if (best > GroundSample.NoWater * 0.5f && Source.Caves.IsCreated)
                {
                    var at = origin + new float2(x, z) * step;
                    if (CaveSampling.NearColumn(ref Source.Caves.Value, at) && Source.CaveDistance(new float3(at.x, best, at.y)) < 0.5f)
                        best = GroundSample.NoWater;
                }
                surface[i] = best;
            }

            // A quad is water if all corners have a surface and at least one corner is actually submerged.
            int quadCount = 0;
            var wetQuad = new NativeArray<bool>(q * q, Allocator.Temp);
            for (int z = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                int i0 = z * v + x, i1 = i0 + 1, i2 = i0 + v, i3 = i2 + 1;
                var s = new float4(surface[i0], surface[i1], surface[i2], surface[i3]);
                var ground = new float4(water[i0].w, water[i1].w, water[i2].w, water[i3].w);
                var wet = math.all(s > GroundSample.NoWater * 0.5f) && math.any(s > ground + 0.02f);
                wetQuad[z * q + x] = wet;
                if (wet)
                    quadCount++;
            }

            // Unsplit, a step between two levels with no fall (a raised pool by a river) draws as a ramp over the lower bank.
            int splits = 0;
            for (int z = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                if (!wetQuad[z * q + x])
                    continue;
                int i0 = z * v + x, i1 = i0 + 1, i2 = i0 + v, i3 = i2 + 1;
                var diagonal = StepEdge(surface, i1, i2, v, step, origin);
                splits += (diagonal || StepEdge(surface, i0, i2, v, step, origin) || StepEdge(surface, i0, i1, v, step, origin) ? 1 : 0) +
                          (diagonal || StepEdge(surface, i1, i3, v, step, origin) || StepEdge(surface, i2, i3, v, step, origin) ? 1 : 0);
            }

            var range = new float2(1f, 0f);
            if (quadCount == 0)
            {
                mesh.SetVertexBufferParams(0, WaterLayout);
                mesh.SetIndexBufferParams(0, IndexFormat.UInt32);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            else
            {
                mesh.SetVertexBufferParams(v * v + splits * 4, WaterLayout);
                mesh.SetIndexBufferParams(quadCount * 6 + splits * 18, IndexFormat.UInt32);
                var verts = mesh.GetVertexData<WaterVertex>();
                var indices = mesh.GetIndexData<uint>();
                range = new float2(float.MaxValue, float.MinValue);
                for (int z = 0; z < v; z++)
                for (int x = 0; x < v; x++)
                {
                    var i = z * v + x;
                    var y = surface[i] > GroundSample.NoWater * 0.5f ? surface[i] : water[i].w;
                    verts[i] = new WaterVertex
                    {
                        Position = new float3(x * step, y, z * step),
                        Data = new float4(flow[i], y - water[i].w, rapids[i]),
                        Look = looks[i],
                        Glow = glows[i]
                    };
                    range = new float2(math.min(range.x, y), math.max(range.y, y));
                }

                int t = 0;
                int extra = v * v;
                for (int z = 0; z < q; z++)
                for (int x = 0; x < q; x++)
                {
                    if (!wetQuad[z * q + x])
                        continue;
                    int i0 = z * v + x, i1 = i0 + 1, i2 = i0 + v, i3 = i2 + 1;
                    SplitStep(verts, indices, water, surface, i0, i2, i1, v, step, origin, ref t, ref extra);
                    SplitStep(verts, indices, water, surface, i1, i2, i3, v, step, origin, ref t, ref extra);
                }
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, t), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            surface.Dispose();
            flow.Dispose();
            wetQuad.Dispose();
            return range;
        }

        /// <summary>A water triangle's surface rise (m) between corners above which it is split by level (SplitStep).</summary>
        const float StepRise = 1f;

        bool NearFall(float2 xz)
        {
            if (!Source.Rivers.IsCreated)
                return false;
            ref var falls = ref Source.Rivers.Value.Falls;
            for (int f = 0; f < falls.Length; f++)
                if (math.distance(xz, falls[f].Lip.xz) < falls[f].Width * 0.5f + 30f)
                    return true;
            return false;
        }

        /// <summary>Whether grid edge (a, b) joins two levels further apart than StepRise, away from any fall.</summary>
        bool StepEdge(NativeArray<float> surface, int a, int b, int v, float step, float2 origin) =>
            math.abs(surface[a] - surface[b]) > StepRise && !NearFall(origin + (Grid(a, v) + Grid(b, v)) * 0.5f * step);

        static float2 Grid(int i, int v) => new float2(i % v, i / v);

        /// <summary>
        /// Emits water triangle (a, b, c), split on its step edges (StepEdge): each side keeps its corners' levels out to
        /// where the water's level crosses the step, joined by a two-sided curtain. One step edge splits it in two; two
        /// (or three) cut off the corner they share.
        /// </summary>
        void SplitStep(NativeArray<WaterVertex> verts, NativeArray<uint> indices, NativeArray<float4> water,
                       NativeArray<float> surface, int a, int b, int c, int v, float step, float2 origin, ref int t, ref int extra)
        {
            bool ab = StepEdge(surface, a, b, v, step, origin), bc = StepEdge(surface, b, c, v, step, origin), ca = StepEdge(surface, c, a, v, step, origin);
            if (!ab && !bc && !ca)
            {
                indices[t++] = (uint)a; indices[t++] = (uint)b; indices[t++] = (uint)c;
                return;
            }
            int x = a, y = b, z = c;
            if ((ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0) == 1)
            {
                if (ab) { x = b; y = c; z = a; }
                else if (bc) { x = c; y = a; z = b; }
                var f = StepAt(surface, z, x, v, step, origin);
                uint pz = (uint)extra, px = pz + 1;
                verts[extra++] = OnEdge(verts, water, z, x, f, z);
                verts[extra++] = OnEdge(verts, water, z, x, f, x);
                indices[t++] = (uint)x; indices[t++] = (uint)y; indices[t++] = px;
                indices[t++] = pz; indices[t++] = (uint)y; indices[t++] = (uint)z;
                indices[t++] = px; indices[t++] = (uint)y; indices[t++] = pz;
                indices[t++] = pz; indices[t++] = (uint)y; indices[t++] = px;
                return;
            }
            if (ab && bc && !ca) { x = b; y = c; z = a; }
            else if (bc && ca && !ab) { x = c; y = a; z = b; }
            uint xy = (uint)extra, xz = xy + 1, yx = xy + 2, zx = xy + 3;
            var fy = StepAt(surface, x, y, v, step, origin);
            var fz = StepAt(surface, x, z, v, step, origin);
            verts[extra++] = OnEdge(verts, water, x, y, fy, x);
            verts[extra++] = OnEdge(verts, water, x, z, fz, x);
            verts[extra++] = OnEdge(verts, water, x, y, fy, y);
            verts[extra++] = OnEdge(verts, water, x, z, fz, z);
            indices[t++] = (uint)x; indices[t++] = xy; indices[t++] = xz;
            indices[t++] = yx; indices[t++] = (uint)y; indices[t++] = (uint)z;
            indices[t++] = yx; indices[t++] = (uint)z; indices[t++] = zx;
            indices[t++] = xy; indices[t++] = xz; indices[t++] = zx;
            indices[t++] = xy; indices[t++] = zx; indices[t++] = yx;
            indices[t++] = xy; indices[t++] = zx; indices[t++] = xz;
            indices[t++] = xy; indices[t++] = yx; indices[t++] = zx;
        }

        /// <summary>Where (0..1 from a) the water's level crosses the middle of the step along edge (a, b); the middle of
        /// the edge where the corners' levels are borrowed rather than their own.</summary>
        float StepAt(NativeArray<float> surface, int a, int b, int v, float step, float2 origin)
        {
            // From the lower index, so the triangles and chunks sharing the edge split it at one point.
            var flip = a > b;
            if (flip)
                (a, b) = (b, a);
            var pa = origin + Grid(a, v) * step;
            var pb = origin + Grid(b, v) * step;
            var mid = (surface[a] + surface[b]) * 0.5f;
            var highA = surface[a] > surface[b];
            if (WaterAt(pa).x > mid != highA || WaterAt(pb).x > mid == highA)
                return 0.5f;
            float lo = 0f, hi = 1f;
            for (int k = 0; k < 10; k++)
            {
                var m = (lo + hi) * 0.5f;
                if (WaterAt(math.lerp(pa, pb, m)).x > mid == highA)
                    lo = m;
                else
                    hi = m;
            }
            return flip ? 1f - (lo + hi) * 0.5f : (lo + hi) * 0.5f;
        }

        /// <summary>The point f along edge (a, b) at corner own's level, with own's flow and looks.</summary>
        static WaterVertex OnEdge(NativeArray<WaterVertex> verts, NativeArray<float4> water, int a, int b, float f, int own)
        {
            var o = verts[own];
            var y = o.Position.y;
            var ground = math.lerp(water[a].w, water[b].w, f);
            o.Position = new float3(math.lerp(verts[a].Position.xz, verts[b].Position.xz, f), y).xzy;
            o.Data.z = y - ground;
            return o;
        }

        /// <summary>
        /// The terrain's cut at a point (negative where the ground is replaced; the shader clips it per pixel): the cave
        /// walls' field where a mouth opens (MouthCut; the walls carry a skin of the surface round the opening, under the
        /// terrain), the sunken temple's entrance cut, and a scarp's stepped strip, left to its skin mesh (strip, and its
        /// level run-on stripY; strip is 100 off any strip). The same holes as GroundHoles.Removed.
        /// </summary>
        public float Cut(float2 xz, float h, float step, bool cutHoles, int lod, out float strip, out float stripY)
        {
            var cave = SeamCut(xz, h, step, cutHoles, lod, out strip, out stripY);
            // W-56: the temple's entrance cut (exact; W-174 r6: to LOD2, with the triangles split on its rim by Collars).
            if (lod <= TempleMaxLod && Source.Landmarks.IsCreated)
                cave = math.min(cave, Source.TempleCut(xz));
            return cave;
        }

        /// <summary>Cut without the temple (W-174 r6: Collars splits the triangles on its rim, so Seam leaves it alone).</summary>
        float SeamCut(float2 xz, float h, float step, bool cutHoles, int lod, out float strip, out float stripY)
        {
            var cave = cutHoles ? MouthCut(Source, xz, h, step) : 100f;
            strip = 100f;
            stripY = 0f;
            if (lod <= 2 && Source.Scarps.IsCreated && ScarpSet.Strip(ref Source.Scarps.Value, xz, step, h, out var s, out var sy))
            {
                strip = s;
                stripY = sy;
                cave = math.min(cave, s);
            }
            return cave;
        }

        /// <summary>
        /// W-174 row 4 (#405): the one seam builder for every replaced patch of ground (cave mouths, temple entrances,
        /// scarp strips). Returns where a grid vertex goes (the grid point, or the rim point it snaps to) and its cut
        /// there. Only vertices on the kept side snap (holding SeamEpsilon, so the clip edge runs through them): if both sides
        /// snapped, a triangle with every corner on the rim would put its edge through the midpoints, off the rim at a
        /// corner. With one side, a straight rim through a snapped corner can't cross its triangle. Only Source
        /// is read, so TestHooks.SeamProblems builds the same vertices to check the seams.
        /// </summary>
        public float2 Seam(float2 grid, float h, float step, int lod, out float cave, out float strip, out float stripY)
        {
            var cutHoles = false;   // W-174 r6 row 2: cave mouths are split on their rim by Collars, not snapped to
            cave = SeamCut(grid, h, step, cutHoles, lod, out strip, out stripY);
            if (!(cave > 0f && cave < 1.3f * step) || !SeamSnap(grid, cave, step, cutHoles, lod, out var rim))
                return grid;
            // One Newton step lands on the rim of a straight edge or a smooth field, but not across a kink (where two
            // rooms' edges meet, a mouth's 5-tap min): a vertex that misses the rim keeps its grid point.
            var at = SeamCut(rim, Source.SampleHeight(rim), step, cutHoles, lod, out var rimStrip, out var rimStripY);
            if (math.abs(at) > SeamMiss + 0.25f * math.abs(cave))
                return grid;
            cave = math.max(at, SeamEpsilon);
            strip = rimStrip;
            stripY = rimStripY;
            return rim;
        }

        /// <summary>
        /// Moves a point near a rim onto it with one Newton step along the cut's gradient (exact for the temple's walls,
        /// close for a mouth's noisy field). False where the field is flat there (MouthCut's rock value is capped) or
        /// the move would leave the vertex's fan of triangles (see SeamReach).
        /// </summary>
        bool SeamSnap(float2 xz, float cut, float step, bool cutHoles, int lod, out float2 rim)
        {
            rim = xz;
            var e = math.min(SeamProbe, 0.25f * step);
            float2 ax = xz + new float2(e, 0f), az = xz + new float2(0f, e);
            var g = new float2(SeamCut(ax, Source.SampleHeight(ax), step, cutHoles, lod, out _, out _) - cut,
                               SeamCut(az, Source.SampleHeight(az), step, cutHoles, lod, out _, out _) - cut) / e;
            var g2 = math.lengthsq(g);
            if (g2 < 0.04f)
                return false;
            var move = -cut * g / g2;
            // The fan round a vertex (split i0-i2-i1 / i1-i2-i3, diagonals along x+z) is the hexagon
            // max(|x|, |z|, |x+z|) < 1 quad: inside it no triangle flips.
            if (math.cmax(math.abs(new float3(move, move.x + move.y))) > SeamReach * step)
                return false;
            rim = xz + move;
            return true;
        }

        /// <summary>Finite-difference distance (m) for the seam builder's gradient.</summary>
        const float SeamProbe = 0.2f;
        /// <summary>The cut value a snapped vertex keeps (its side's sign), so the clip edge runs through it.</summary>
        public const float SeamEpsilon = 0.001f;
        /// <summary>How far off the rim (m of cut, plus a quarter of the grid point's) a snap may land and still count.</summary>
        const float SeamMiss = 0.02f;
        /// <summary>How far a vertex may move onto a rim, as a share of its fan (quads): most of it, so a ground vertex
        /// can reach a convex corner of a hole instead of the chord between two edge snaps cutting the corner off.</summary>
        const float SeamReach = 0.9f;

        /// <summary>
        /// The terrain's cut at a vertex: the lowest MouthCut at it and half a quad away each way. An opening narrower
        /// than a quad (the slit at the top of a mouth) lies between vertices that all read as rock, and the triangles
        /// spanning it would stay, a strip of ground bridging the slit; this way the triangles round it are cut, and
        /// the cave walls' skin under the terrain fills in.
        /// </summary>
        static float MouthCut(in TerrainSource source, float2 xz, float h, float step)
        {
            var cut = source.MouthCut(xz, h, TerrainSource.MeshCutDepth, out var cave);
            if (cut >= 100f)
                return cut;
            var r = step * 0.5f;
            var at = new float2x4(xz + new float2(r, 0f), xz - new float2(r, 0f), xz + new float2(0f, r), xz - new float2(0f, r));
            var heights = new float4(source.SampleHeight(at.c0), source.SampleHeight(at.c1), source.SampleHeight(at.c2), source.SampleHeight(at.c3));
            // Over most of a cave's footprint the passage is metres down. Once the field is at least SettledCave from
            // every neighbour's sample point, each neighbour's cut is the same settled value as this one's and can't
            // lower the min, so they're skipped: the samples lie within reach of this one (half a quad across, their
            // height difference up or down, twice the sampling depth for the normals), and the field changes by at
            // most FieldSlope per metre. Only vertices within a few metres of an opening pay for all five.
            var reach = r + math.cmax(math.abs(heights - h)) + 2f * TerrainSource.MouthCutDepth;
            if (cave > SettledCave + FieldSlope * reach)
                return cut;
            for (int k = 0; k < 4; k++)
                cut = math.min(cut, source.MouthCut(at[k], heights[k], TerrainSource.MeshCutDepth, out _));
            return cut;
        }

        /// <summary>Cave field distance beyond which MouthCut is settled at its rock value (the smooth min's blend is
        /// spent: MouthCutDepth - CutGap + MouthBlend), with half a metre to spare.</summary>
        const float SettledCave = TerrainSource.MouthCutDepth - CaveSampling.CutGap + CaveSampling.MouthBlend + 0.5f;
        /// <summary>A safe bound on how fast the cave field changes per metre (it's a smoothed, noise-moved distance
        /// that can overstate by up to 1.8 times, so not a true 1-per-metre distance).</summary>
        const float FieldSlope = 2f;

        /// <summary>
        /// How far a skirt may hang below the surface at (xz, top): its full depth, or less where cave air lies within
        /// it (found by bisection), stopping 0.3 m above that air, and none within a few metres of a mouth.
        /// </summary>
        float SkirtOverCave(float2 xz, float top, float depth)
        {
            // W-113 (item 12): over the sunken temple a 6-24 m skirt hangs through its rooms as a flat, untextured
            // sand-coloured wall the player walks through. Stop it 0.3 m above the first room air below the surface.
            if (Source.Landmarks.IsCreated && Source.TempleCut(xz) < 99f)  // 100 away from any temple
                for (float d = 0.25f; d <= depth; d += 0.25f)
                    if (Source.TempleAir(new float3(xz.x, top - d, xz.y)))
                    {
                        depth = math.max(d - 0.3f, 0.05f);
                        break;
                    }
            if (!Source.Caves.IsCreated || !CaveSampling.NearColumn(ref Source.Caves.Value, xz))
                return depth;
            // Round a mouth, none at all: a skirt there hangs into the opening as a long flat ribbon (seen from inside the
            // mouth). The cave walls' skin of the surface (CaveSystem.WallReach) covers any crack it would have hidden.
            if (Source.CaveClearance(xz, top) < 4f)
                return 0f;
            if (Source.CaveDistance(new float3(xz.x, top - depth, xz.y)) >= 0.3f)
                return depth;
            float rock = 0f, air = depth;
            for (int i = 0; i < 6; i++)
            {
                var mid = (rock + air) * 0.5f;
                if (Source.CaveDistance(new float3(xz.x, top - mid, xz.y)) >= 0.3f)
                    rock = mid;
                else
                    air = mid;
            }
            return math.max(rock, 0.05f);
        }

        /// <summary>Water surface, flow and ground height at a point (as the chunk's own vertices store them).</summary>
        float4 WaterAt(float2 world)
        {
            var g = Source.SampleGround(world);
            return new float4(g.WaterSurface, g.Flow, g.Height);
        }

        static int EdgeVertex(int edge, int k, int q)
        {
            int v = q + 1;
            switch (edge)
            {
                case 0: return k;               // south (z = 0)
                case 1: return q * v + k;       // north (z = q)
                case 2: return k * v;           // west  (x = 0)
                default: return k * v + q;      // east  (x = q)
            }
        }
    }
}
