using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-153 row 3b: the boxed falls' curtains as one mesh of the water itself (FallCurtain.cs), under their white
    /// sheets, standing in for the water's face the boxes leave out. Rebuilt only when the boxed falls or their grids
    /// change (rule 20: each fall's rows from how big it looks, DetailPolicy.ScreenSize); drawn every frame.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        /// <summary>The falls that got a box this gather (SetCurtainBoxes), in its order.</summary>
        readonly List<FallShape> curtainShapes = new List<FallShape>();
        readonly List<int2> curtainGrids = new List<int2>(), curtainGridsNow = new List<int2>();
        readonly List<FallCurtain.Vertex> curtainVertices = new List<FallCurtain.Vertex>();
        readonly List<int> curtainIndices = new List<int>();
        readonly Dictionary<float3, float4> curtainLooks = new Dictionary<float3, float4>();
        readonly Dictionary<float3, float2> curtainGlows = new Dictionary<float3, float2>();
        Mesh curtainMesh;
        Material curtainMaterial;
        EntityQuery configQuery;
        World configWorld;

        /// <summary>Rebuilds the curtain mesh when the boxed falls changed or any of them wants another grid.</summary>
        void CurtainMesh(in TerrainSource source, float3 camera, bool rebuilt)
        {
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var perMetre = view.PixelsPerMetre > 0f ? view.PixelsPerMetre : Punchy.Settings.DetailPolicy.PixelsPerMetreAt(Punchy.Settings.DetailPolicy.ReferenceFov);
            var detail = view.PixelsPerMetre > 0f ? view.Detail : 1f;
            curtainGridsNow.Clear();
            foreach (var s in curtainShapes)
            {
                var pixels = Punchy.Settings.DetailPolicy.ScreenSize(s.Size, FallDistance(s.Lip, s.Drop, camera), perMetre) * detail;
                curtainGridsNow.Add(FallCurtain.Grid(s, pixels));
            }
            var same = !rebuilt && curtainGridsNow.Count == curtainGrids.Count;
            for (int k = 0; same && k < curtainGrids.Count; k++)
                same = curtainGrids[k].Equals(curtainGridsNow[k]);
            if (same)
                return;
            curtainGrids.Clear();
            curtainGrids.AddRange(curtainGridsNow);
            if (rebuilt)
            {
                curtainLooks.Clear();
                curtainGlows.Clear();
            }
            curtainVertices.Clear();
            curtainIndices.Clear();
            for (int k = 0; k < curtainShapes.Count; k++)
            {
                var s = curtainShapes[k];
                if (!curtainLooks.TryGetValue(s.Lip, out var look))
                {
                    // (The water's own look where it tips over, as the chunk water there has it.)
                    source.SampleBiomes(s.Lip.xz, out var biomes, out _);
                    look = source.WaterLook(s.Lip.xz, biomes, out var glow);
                    curtainLooks[s.Lip] = look;
                    curtainGlows[s.Lip] = glow;
                }
                FallCurtain.Add(s, curtainGrids[k], look, curtainGlows[s.Lip], curtainVertices, curtainIndices);
            }
            if (curtainMesh == null)
            {
                curtainMesh = new Mesh { name = "Fall curtains", indexFormat = IndexFormat.UInt32 };
                curtainMesh.MarkDynamic();
            }
            curtainMesh.Clear();
            if (curtainVertices.Count == 0)
                return;
            curtainMesh.SetVertexBufferParams(curtainVertices.Count, FallCurtain.Vertex.Layout);
            curtainMesh.SetVertexBufferData(curtainVertices, 0, 0, curtainVertices.Count, 0, MeshUpdateFlags.DontRecalculateBounds);
            curtainMesh.SetIndexBufferParams(curtainIndices.Count, IndexFormat.UInt32);
            curtainMesh.SetIndexBufferData(curtainIndices, 0, 0, curtainIndices.Count, MeshUpdateFlags.DontRecalculateBounds);
            curtainMesh.subMeshCount = 1;
            curtainMesh.SetSubMesh(0, new SubMeshDescriptor(0, curtainIndices.Count), MeshUpdateFlags.DontRecalculateBounds);
            curtainMesh.RecalculateBounds();
        }

        void DrawCurtains()
        {
            if (curtainMesh == null || curtainMesh.vertexCount == 0 || !CurtainMaterial())
                return;
            Graphics.RenderMesh(new RenderParams(curtainMaterial) { worldBounds = curtainMesh.bounds, shadowCastingMode = ShadowCastingMode.Off, layer = 0 },
                curtainMesh, 0, Matrix4x4.identity);
        }

        /// <summary>The curtains' material: the water's own (the plain one, not the fine sheet's), made once.</summary>
        bool CurtainMaterial()
        {
            if (curtainMaterial != null)
                return true;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (configWorld != world)
            {
                configQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
                configWorld = world;
            }
            if (!configQuery.TryGetSingleton<TerrainConfig>(out var config) || config.WaterMaterial.Value == null)
                return false;
            curtainMaterial = new Material(config.WaterMaterial.Value) { name = "Water (fall curtains)" };
            return true;
        }

        void ClearCurtainMesh()
        {
            curtainShapes.Clear();
            curtainGrids.Clear();
            if (curtainMesh != null)
                curtainMesh.Clear();
        }
    }
}
