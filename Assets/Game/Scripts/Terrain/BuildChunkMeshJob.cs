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
        public half4 Emission;  // emission rgb (linear HDR), unused

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4)
        };

        public void SetSurface(in SurfaceProps s)
        {
            Surface = new half4(new float4(s.Albedo, s.Smoothness));
            Extra = new half4(new float4(s.Metallic, s.Grass, s.Snow, 0f));
            Emission = new half4(new float4(s.Emission, 0f));
        }
    }

    struct ChunkBuildRequest
    {
        public float2 Origin;
        public int Quads;
        public int Lod;
    }

    /// <summary>
    /// Builds one chunk mesh per index: a (Quads+1)² height grid in chunk-local space, plus a skirt
    /// hanging down from each edge so neighbouring chunks at a different LOD never show cracks.
    /// </summary>
    [BurstCompile]
    struct BuildChunkMeshJob : IJobParallelFor
    {
        public Mesh.MeshDataArray Meshes;
        [ReadOnly] public NativeArray<ChunkBuildRequest> Requests;
        public TerrainSource Source;
        public float ChunkSize;
        public float NormalEpsilon;
        public float SkirtDepth;
        /// <summary>Output: (min, max) local Y per mesh, for bounds.</summary>
        public NativeArray<float2> HeightRanges;

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
                vertex.SetSurface(Source.SampleSurface(world, ground, normal));
                verts[z * v + x] = vertex;
            }

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
