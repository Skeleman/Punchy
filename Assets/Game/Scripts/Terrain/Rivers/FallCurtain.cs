using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-153 row 3b: a fall's curtain as a mesh of the water itself, from its FallShape alone (rule 33: river falls,
    /// grand falls tiers and landmark falls all build here). A strip along the lip, as wide as the lip and following
    /// its skew and bow, that rolls over from the water behind the lip and arcs out and down to the foot, on the same
    /// path as the white sheet over it (FallPoint, LandmarkEffects.hlsl) a little behind it. Its edges are ragged
    /// (an uneven inset that grows down the arc) and fade out. Drawn with the water material, which reads each vertex's
    /// steepness from the mesh (Vertex.Fall) so the falling look holds all down the arc, flat triangles too.
    /// </summary>
    public static class FallCurtain
    {
        /// <summary>The water's vertex (WaterVertex, BuildChunkMeshJob.cs) and the curtain's own stream.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public float3 Position;
            public float4 Data;   // flow xz, depth, rapids
            public float4 Look;   // water colour rgb, murk
            public float2 Glow;
            public float2 Fall;   // how steep the water reads (0..1), 1 on the curtain (Water.shader, TEXCOORD5)

            public static readonly VertexAttributeDescriptor[] Layout =
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 2)
            };
        }

        /// <summary>Rule 20: one row down the curtain per this many reference pixels the fall looks big.</summary>
        public const float RowPixels = 14f;
        const int MinRows = 4, MaxRows = 40, MaxColumns = 32;
        /// <summary>Of the curtain's length (0..1), the part that rolls over the lip; the rest falls.</summary>
        const float RunIn = 0.12f;
        /// <summary>How far behind the white sheet the curtain lies at its foot (m; none at the lip).</summary>
        const float Behind = 0.3f;

        /// <summary>Rows and columns for a fall that looks this many reference pixels big (rows in fours, so a
        /// curtain is rebuilt only on a real change of size).</summary>
        public static int2 Grid(in FallShape s, float pixels)
        {
            var rows = math.clamp((int)math.ceil(pixels / RowPixels), MinRows, MaxRows);
            rows = (rows + 3) / 4 * 4;
            var columns = math.clamp((int)math.ceil(rows * s.Width / math.max(s.Drop + s.Run, 1f)), 2, MaxColumns);
            return new int2(columns, rows);
        }

        /// <summary>A point of the curtain: u across (0..1), f from behind the lip to the foot (0..1); FallPoint in
        /// LandmarkEffects.hlsl for an open fall, without its moving ripple or its lip's waves.</summary>
        public static float3 Point(in FallShape s, float u, float f, out float tau, out float lipIn)
        {
            var outDir = new float3(s.Out.x, 0f, s.Out.y);
            var across = new float3(s.Out.y, 0f, -s.Out.x);
            var side = u * 2f - 1f;
            var x = (u - 0.5f) * s.Width;
            lipIn = math.saturate(f / RunIn);
            tau = math.saturate((f - RunIn) / (1f - RunIn));
            // (No lip wave here: the sheet's 32 columns and the curtain's fewer cut its ~2 m ripples at different points,
            // so the curtain's joins poked through the sheet as thin dark lines, fall-121. It lies the waves' whole
            // height behind instead, behind any chord of the sheet's; its reach likewise takes the wave's least.)
            var lipShift = s.Skew * side * 0.5f - s.Bow * side * side - math.min(0.6f, s.Width * 0.08f);
            var run = s.Run * (0.9f + 0.15f * (1f - side * side));
            const float dip = 0.15f;
            var down = dip * lipIn * lipIn + (s.Drop - dip) * math.pow(tau, math.lerp(2f, 1.15f, s.Cascade));
            var along = lipShift - 1.5f * (1f - lipIn) + run * tau - Behind * tau;
            var width = s.Width * (1f - 0.12f * tau) * (1f + 0.06f * math.sin(tau * 5f + s.Seed * 20f));
            var p = s.Lip + outDir * along - new float3(0f, down + 0.04f, 0f) + across * (u - 0.5f) * width;
            p += outDir * s.Cascade * 0.3f * math.sin(tau * (s.Drop + run) * 0.9f + x * 0.8f + s.Seed * 17f) * math.sin(math.PI * tau);
            p += across * math.sign(side) * math.smoothstep(0.75f, 1f, math.abs(side)) * tau * tau * math.min(0.08f * s.Drop, 1.2f);
            return p;
        }

        /// <summary>0..1 from three integers and the fall's seed (mixed, since math.hash of a vector doesn't avalanche).</summary>
        static float Hash(int a, int b, float seed)
        {
            var h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)(seed * 16777216f);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Fall.y on the curtain's outer edge: still over 0.5 (the curtain flag in Water.shader), and the
        /// shader fades the curtain from 1 down to 0 here.</summary>
        public const float CurtainMin = 0.55f;

        /// <summary>0..1 along the curtain (f), smooth and uneven: value noise on hashed knots a varied distance
        /// apart, so the edge's bites differ in size instead of repeating per row.</summary>
        static float EdgeWander(float f, float seed)
        {
            var x = f * 7f;
            var k = (int)math.floor(x);
            var t = math.smoothstep(0f, 1f, x - k);
            var v = math.lerp(Hash(k, 11, seed), Hash(k + 1, 11, seed), t);
            var y = f * 17f + 0.37f;
            var j = (int)math.floor(y);
            var w = math.lerp(Hash(j, 12, seed), Hash(j + 1, 12, seed), math.smoothstep(0f, 1f, y - j));
            return math.saturate(v * 0.7f + w * 0.3f);
        }

        /// <summary>Appends one fall's curtain on this grid, in this water's look.</summary>
        public static void Add(in FallShape s, int2 grid, float4 look, float2 glow, List<Vertex> vertices, List<int> indices)
        {
            int columns = grid.x, rows = grid.y;
            var start = vertices.Count;
            for (int r = 0; r <= rows; r++)
            {
                // (Two rows roll over the lip; the rest share the fall.)
                var f = r <= 2 ? RunIn * r / 2f : RunIn + (1f - RunIn) * (r - 2) / (rows - 2);
                for (int i = 0; i <= columns; i++)
                {
                    var u = i / (float)columns;
                    var tauHere = math.saturate((f - RunIn) / (1f - RunIn));
                    // Ragged edges: the outer points wander in by an uneven, smooth amount, more further down (a hashed
                    // inset per row read as a row of regular teeth). The outer column fades out (Fall.y), so the edge
                    // reads soft rather than as hard triangles.
                    if (i == 0)
                        u += EdgeWander(f, s.Seed) * 0.12f * tauHere;
                    else if (i == columns)
                        u -= EdgeWander(f, s.Seed + 0.53f) * 0.12f * tauHere;
                    var edge = i == 0 || i == columns ? 0f : 1f;
                    var p = Point(s, u, f, out var tau, out var lipIn);
                    vertices.Add(new Vertex
                    {
                        Position = p,
                        Data = new float4(s.Out * (0.6f + 0.6f * tau), 1.2f, 0.6f * lipIn),
                        Look = look,
                        Glow = glow,
                        Fall = new float2(math.smoothstep(0.2f, 1f, lipIn), CurtainMin + (1f - CurtainMin) * edge)
                    });
                }
            }
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < columns; i++)
                {
                    int a = start + r * (columns + 1) + i, b = a + 1, c = a + columns + 1, d = c + 1;
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(b);
                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);
                }
        }
    }
}
