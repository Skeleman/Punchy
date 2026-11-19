using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>Each seep's mini fall: a narrow ribbon of the water material's lava hugging the rock below its lip, built in the fall curtains' vertex (FallCurtain) so the real lava look draws it.</summary>
    public partial class LandmarkEffectSystem
    {
        const int SeepColumns = 4;
        const float SeepLift = 0.04f;
        static readonly float4 SeepLavaLook = new float4(0.08f, 0.04f, 0.02f, 0f);

        readonly List<FallCurtain.Vertex> seepVertices = new List<FallCurtain.Vertex>();
        readonly List<int> seepIndices = new List<int>();
        Mesh seepMesh;

        void SeepFallMesh(in TerrainSource source, float3 eye, float range)
        {
            seepVertices.Clear();
            seepIndices.Clear();
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var perMetre = view.PixelsPerMetre > 0f ? view.PixelsPerMetre : Punchy.Settings.DetailPolicy.PixelsPerMetreAt(Punchy.Settings.DetailPolicy.ReferenceFov);
            var detail = view.PixelsPerMetre > 0f ? view.Detail : 1f;
            foreach (var s in seeps)
            {
                var distance = math.distance(s.Lip, eye);
                var rows = math.clamp((int)math.ceil(Punchy.Settings.DetailPolicy.ScreenSize(s.Drop, distance, perMetre) * detail / FallCurtain.RowPixels), 3, 16);
                // (Built per seep cell, so the fade steps every few metres across the last quarter of the reach.)
                var fade = 1f - math.smoothstep(0.75f, 1f, distance / math.max(range, 1f));
                SeepRibbon(source, s, rows, fade);
                if (s.UnderRock)
                    SeepPuddle(s, fade);
            }
            if (seepMesh == null)
            {
                seepMesh = new Mesh { name = "Seep falls", indexFormat = IndexFormat.UInt32 };
                seepMesh.MarkDynamic();
            }
            seepMesh.Clear();
            if (seepVertices.Count == 0)
                return;
            seepMesh.SetVertexBufferParams(seepVertices.Count, FallCurtain.Vertex.Layout);
            seepMesh.SetVertexBufferData(seepVertices, 0, 0, seepVertices.Count, 0, MeshUpdateFlags.DontRecalculateBounds);
            seepMesh.SetIndexBufferParams(seepIndices.Count, IndexFormat.UInt32);
            seepMesh.SetIndexBufferData(seepIndices, 0, 0, seepIndices.Count, MeshUpdateFlags.DontRecalculateBounds);
            seepMesh.subMeshCount = 1;
            seepMesh.SetSubMesh(0, new SubMeshDescriptor(0, seepIndices.Count), MeshUpdateFlags.DontRecalculateBounds);
            seepMesh.RecalculateBounds();
        }

        void SeepRibbon(in TerrainSource source, in SeepSpot s, int rows, float fade)
        {
            var dir = math.normalizesafe(s.Run, new float2(1f, 0f));
            var across = new float3(-dir.y, 0f, dir.x);
            var start = seepVertices.Count;
            for (int r = 0; r <= rows; r++)
            {
                var t = r / (float)rows;
                float3 centre;
                if (s.UnderRock)
                    centre = s.Lip + new float3(s.Run.x, -1f, s.Run.y) * s.Drop * t + new float3(dir.x, 0f, dir.y) * 0.03f;
                else
                {
                    // On open ground the ribbon follows the slope as sampled, so it lies on the rock rather than on the lip-to-foot line.
                    var xz = s.Lip.xz + s.Run * s.Drop * t;
                    centre = new float3(xz.x, source.SampleHeight(xz) + SeepLift * math.sqrt(1f + 1f / math.max(math.lengthsq(s.Run), 0.01f)), xz.y);
                }
                var wander = (Wobble(s.Hash, r * 0.7f) - 0.5f) * s.Width * 0.5f * t;
                var width = s.Width * (1f - 0.35f * t) * (0.8f + 0.4f * Wobble(s.Hash ^ 0x51u, r * 1.3f));
                var along = math.smoothstep(0f, 0.15f, t) * (1f - math.smoothstep(0.8f, 1f, t)) * 0.6f + 0.4f * (r == 0 || r == rows ? 0f : 1f);
                for (int i = 0; i <= SeepColumns; i++)
                {
                    var u = i / (float)SeepColumns;
                    var edge = i == 0 || i == SeepColumns ? 0f : 1f - math.abs(u - 0.5f);
                    seepVertices.Add(new FallCurtain.Vertex
                    {
                        Position = centre + across * ((u - 0.5f) * width + wander),
                        Data = new float4(dir * 0.8f, 1.2f, 0f),
                        Look = SeepLavaLook,
                        Glow = new float2(0f, 1f),
                        Fall = new float2(1f, FallCurtain.CurtainMin + (1f - FallCurtain.CurtainMin) * edge * along * fade)
                    });
                }
            }
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < SeepColumns; i++)
                {
                    int a = start + r * (SeepColumns + 1) + i, b = a + 1, c = a + SeepColumns + 1, d = c + 1;
                    seepIndices.Add(a); seepIndices.Add(b); seepIndices.Add(c);
                    seepIndices.Add(b); seepIndices.Add(d); seepIndices.Add(c);
                }
        }

        void SeepPuddle(in SeepSpot s, float fade)
        {
            const int sides = 8;
            var foot = s.Lip + new float3(s.Run.x, -1f, s.Run.y) * s.Drop + new float3(0f, 0.03f, 0f);
            var radius = s.Width * 0.9f;
            var centre = seepVertices.Count;
            seepVertices.Add(new FallCurtain.Vertex
            {
                Position = foot, Data = new float4(0f, 0f, 0.2f, 0f), Look = SeepLavaLook, Glow = new float2(0f, 1f),
                Fall = new float2(0f, FallCurtain.CurtainMin + (1f - FallCurtain.CurtainMin) * fade)
            });
            for (int k = 0; k < sides; k++)
            {
                var a = k / (float)sides * 6.2832f;
                var reach = radius * (0.7f + 0.5f * Wobble(s.Hash ^ 0xA7u, k * 0.9f));
                seepVertices.Add(new FallCurtain.Vertex
                {
                    Position = foot + new float3(math.cos(a), 0f, math.sin(a)) * reach, Data = new float4(0f, 0f, 0.2f, 0f), Look = SeepLavaLook,
                    Glow = new float2(0f, 1f), Fall = new float2(0f, FallCurtain.CurtainMin)
                });
                seepIndices.Add(centre); seepIndices.Add(centre + 1 + (k + 1) % sides); seepIndices.Add(centre + 1 + k);
            }
        }

        static float Wobble(uint hash, float x)
        {
            var k = (int)math.floor(x);
            var a = (Scramble(hash ^ (uint)k * 0x9E3779B1u) & 0xFFFF) / 65536f;
            var b = (Scramble(hash ^ (uint)(k + 1) * 0x9E3779B1u) & 0xFFFF) / 65536f;
            return math.lerp(a, b, math.smoothstep(0f, 1f, x - k));
        }

        void DrawSeepFalls()
        {
            if (seepMesh == null || seepMesh.vertexCount == 0 || !CurtainMaterial())
                return;
            Graphics.RenderMesh(new RenderParams(curtainMaterial) { worldBounds = seepMesh.bounds, shadowCastingMode = ShadowCastingMode.Off, layer = 0 },
                seepMesh, 0, Matrix4x4.identity);
        }

        void ClearSeepFalls()
        {
            if (seepMesh != null)
                seepMesh.Clear();
        }
    }
}
