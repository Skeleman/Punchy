using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
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
        public float4 Morph;    // distance from the focus where geomorphing starts / completes; z = surface bumpiness

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4)
        };

        public void SetSurface(in SurfaceProps s)
        {
            Surface = new half4(new float4(s.Albedo, s.Smoothness));
            Extra = new half4(new float4(s.Metallic, s.Grass, s.Snow, 0f));
            Emission = new half4(new float4(s.Emission, 0f));
        }
    }

    /// <summary>Must match WaterAttributes in Water.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct WaterVertex
    {
        public float3 Position;
        public float4 Data;     // flow direction xz, depth below the surface, rapids 0..1

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4)
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
    }

    /// <summary>
    /// Builds two meshes per chunk. Terrain (index i): a (Quads+1)² height grid in chunk-local space with
    /// geomorph data, plus a skirt hanging down from each edge to cover cracks while neighbours are mid-rebuild.
    /// Water (index Count + i): a sheet at the local water surface wherever sea, lake or river water lies,
    /// or an empty mesh when the chunk is dry.
    /// </summary>
    [BurstCompile]
    struct BuildChunkMeshJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Meshes;
        [ReadOnly] public NativeArray<ChunkBuildRequest> Requests;
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> WaterLayout;
        public TerrainSource Source;
        public float ChunkSize;
        public float NormalEpsilon;
        public float SkirtDepth;
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

            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                var local = new float2(x, z) * step;
                var world = request.Origin + local;
                var ground = Source.SampleGround(world);
                var h = ground.Height;
                // Fixed-scale normals keep lighting identical across LODs, so LOD switches don't flicker.
                var normal = Source.SampleNormal(world, NormalEpsilon);
                minY = math.min(minY, h);
                maxY = math.max(maxY, h);

                var vertex = new TerrainVertex { Position = new float3(local.x, h, local.y), Normal = normal };
                var surface = Source.SampleSurface(world, ground, normal);
                vertex.SetSurface(surface);
                vertex.Morph = new float4(request.MorphRange, surface.Bump, 0f);
                verts[z * v + x] = vertex;
                water[z * v + x] = new float4(ground.WaterSurface, ground.Flow, h);
                rapids[z * v + x] = ground.Rapids;
            }

            if (request.CanMorph)
                WriteMorphOffsets(verts, q);

            var depth = SkirtDepth * (request.Lod + 1);
            int skirtBase = v * v;
            for (int edge = 0; edge < 4; edge++)
            for (int k = 0; k < v; k++)
            {
                var g = verts[EdgeVertex(edge, k, q)];
                g.Position.y -= depth;
                // The shader lifts skirt vertices back by this much for shadows, so skirts neither
                // cast shadows nor sit in the shadow of the surface above them.
                g.Extra.w = (half)depth;
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
                indices[t++] = a; indices[t++] = b; indices[t++] = sa;
                indices[t++] = b; indices[t++] = sb; indices[t++] = sa;
                indices[t++] = a; indices[t++] = sa; indices[t++] = b;
                indices[t++] = b; indices[t++] = sa; indices[t++] = sb;
            }

            HeightRanges[index] = new float2(minY - depth, maxY);
            WaterRanges[index] = BuildWater(Meshes[Requests.Length + index], water, rapids, q, step);
            water.Dispose();
            rapids.Dispose();
        }

        /// <summary>
        /// Stores, per vertex, how far it must move to lie on the next-coarser LOD's surface. Even vertices exist in
        /// the coarse grid; odd ones sit on a coarse edge or on the coarse quad's diagonal (the same anti-diagonal
        /// split the triangles use), so a fully morphed mesh matches the coarse mesh exactly.
        /// </summary>
        static void WriteMorphOffsets(NativeArray<TerrainVertex> verts, int q)
        {
            int v = q + 1;
            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                bool oddX = (x & 1) == 1, oddZ = (z & 1) == 1;
                if (!oddX && !oddZ)
                    continue;

                float coarse;
                if (oddX && oddZ)
                    coarse = 0.5f * (H(verts, v, x + 1, z - 1) + H(verts, v, x - 1, z + 1));
                else if (oddX)
                    coarse = 0.5f * (H(verts, v, x - 1, z) + H(verts, v, x + 1, z));
                else
                    coarse = 0.5f * (H(verts, v, x, z - 1) + H(verts, v, x, z + 1));

                var vertex = verts[z * v + x];
                vertex.Emission.w = (half)(coarse - vertex.Position.y);
                verts[z * v + x] = vertex;
            }
        }

        static float H(NativeArray<TerrainVertex> verts, int v, int x, int z) => verts[z * v + x].Position.y;

        /// <summary>Builds the water sheet; returns its (min, max) height, or (1, 0) when the chunk is dry.</summary>
        float2 BuildWater(Mesh.MeshData mesh, NativeArray<float4> water, NativeArray<float> rapids, int q, float step)
        {
            int v = q + 1;

            // Vertices with no water body of their own take the highest neighbouring surface, so a sheet's border
            // extends flat under the banks instead of sloping off towards nothing.
            var surface = new NativeArray<float>(v * v, Allocator.Temp);
            var flow = new NativeArray<float2>(v * v, Allocator.Temp);
            for (int i = 0; i < v * v; i++)
            {
                surface[i] = water[i].x;
                flow[i] = water[i].yz;
            }
            for (int z = 0; z < v; z++)
            for (int x = 0; x < v; x++)
            {
                var i = z * v + x;
                if (water[i].x > GroundSample.NoWater * 0.5f)
                    continue;
                var best = GroundSample.NoWater;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < v && nz < v && water[nz * v + nx].x > best)
                    {
                        best = water[nz * v + nx].x;
                        flow[i] = water[nz * v + nx].yz;
                    }
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
                mesh.SetVertexBufferParams(v * v, WaterLayout);
                mesh.SetIndexBufferParams(quadCount * 6, IndexFormat.UInt32);
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
                        Data = new float4(flow[i], y - water[i].w, rapids[i])
                    };
                    range = new float2(math.min(range.x, y), math.max(range.y, y));
                }

                int t = 0;
                for (int z = 0; z < q; z++)
                for (int x = 0; x < q; x++)
                {
                    if (!wetQuad[z * q + x])
                        continue;
                    uint i0 = (uint)(z * v + x);
                    uint i1 = i0 + 1;
                    uint i2 = i0 + (uint)v;
                    uint i3 = i2 + 1;
                    indices[t++] = i0; indices[t++] = i2; indices[t++] = i1;
                    indices[t++] = i1; indices[t++] = i2; indices[t++] = i3;
                }
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, t), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            surface.Dispose();
            flow.Dispose();
            wetQuad.Dispose();
            return range;
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
