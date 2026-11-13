using System.Collections.Generic;
using Punchy.Settings;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-142 row 3b: dangling cobwebs in caves, cave rooms and road tunnels (#262). The webs are bake records
    /// (CaveSetBlob.Webs, placed by CaveBaking.PlaceWebs); this draws the nearest few with the Cave Web shader and no
    /// mesh, swaying on the shared clock and fading out as they shrink to a pixel or two on screen. Off below Detail 0.75
    /// (Low). Seed-derived and cosmetic: every player sees the same webs and nothing is sent.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>float4s per web in the buffer: (A, kind), (B, seed), (C, sway), (strand, fade, 0, 0).</summary>
        const int WebStride = 4;
        const int MaxWebs = 96;
        /// <summary>How big a web is on screen, for DetailPolicy screen size: drawn down to 6 reference pixels (about
        /// 95 m at Detail 1; its threads are well under a pixel long before that). Kept here until DetailPolicy has web
        /// sizes (R-13).</summary>
        const float WebSize = 0.6f, WebPixels = 6f, WebsOffBelowDetail = 0.75f;
        static float WebReach => DetailPolicy.ExtraReach(WebSize, WebPixels);

        BlobAssetReference<CaveSetBlob> websFrom;
        /// <summary>Per web (as in the blob): its header and middle, read once per world.</summary>
        float4[] webData = System.Array.Empty<float4>();
        float3[] webMiddle = System.Array.Empty<float3>();
        bool[] webNear = System.Array.Empty<bool>();
        readonly List<(float distance, int index)> nearWebs = new List<(float, int)>();
        readonly int[] webDrawn = new int[MaxWebs];
        int webCount;
        float3 websAround = new float3(float.MaxValue);
        readonly float4[] webUpload = new float4[MaxWebs * WebStride];
        GraphicsBuffer webBuffer;
        Material webMaterial;
        MaterialPropertyBlock webProps;

        static readonly int WebDataId = Shader.PropertyToID("_Webs");

        /// <summary>
        /// Draws the webs near the camera: the nearest 96 in reach are picked as the camera moves, and each frame they
        /// fade by distance (the fade rides in the buffer). The material comes from Resources (CaveWeb).
        /// </summary>
        void DrawWebs(in TerrainSource source, float3 camPos)
        {
            if (websFrom != source.Caves)
            {
                ClearWebs();
                websFrom = source.Caves;
                if (source.Caves.IsCreated)
                    SetWebs(ref source.Caves.Value);
            }
            if (webMiddle.Length == 0 || Punchy.Settings.GraphicsSettings.Detail < WebsOffBelowDetail)
                return;
            if (math.distancesq(camPos, websAround) > 4f)
            {
                websAround = camPos;
                PickWebs(camPos);
            }
            if (webCount == 0)
                return;
            if (webMaterial == null)
            {
                webMaterial = Resources.Load<Material>("CaveWeb");
                if (webMaterial == null)
                    return;
            }
            // Fading by distance each frame: out over the last third of the reach.
            var reach = WebReach;
            for (int i = 0; i < webCount; i++)
            {
                var d = math.distance(camPos, webMiddle[webDrawn[i]]);
                webUpload[i * WebStride + 3].y = math.saturate((reach - d) / (reach * 0.33f));
            }
            webBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxWebs * WebStride, 16);
            webBuffer.SetData(webUpload, 0, 0, webCount * WebStride);
            webProps ??= new MaterialPropertyBlock();
            webProps.SetBuffer(WebDataId, webBuffer);
            var rp = new RenderParams(webMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (reach / (1f - DetailPolicy.Hysteresis) * 2f)),
                matProps = webProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            // 36 triangles of sheet and 3 strands of 6 segments: 216 vertices a web.
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 216, webCount);
        }

        /// <summary>Reads the world's webs (a few hundred small records, main thread): each one's header and middle.</summary>
        void SetWebs(ref CaveSetBlob set)
        {
            var count = set.Webs.Length;
            webData = new float4[count * WebStride];
            webMiddle = new float3[count];
            webNear = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var w = set.Webs[i];
                var b = i * WebStride;
                webData[b] = new float4(w.A, (float)w.Kind);
                webData[b + 1] = new float4(w.B, w.Seed);
                webData[b + 2] = new float4(w.C, w.Sway);
                webData[b + 3] = new float4(w.Strand, 0f, 0f, 0f);
                var hangs = w.Kind == WebKind.Strand || w.Kind == WebKind.Cluster;
                webMiddle[i] = hangs ? w.A - new float3(0f, w.Strand * 0.5f, 0f) : (w.A + w.B + w.C) / 3f;
            }
            websAround = new float3(float.MaxValue);
        }

        /// <summary>Picks the nearest webs in reach (held by DetailPolicy.Within) into the upload list.</summary>
        void PickWebs(float3 camPos)
        {
            var reach = WebReach;
            var listed = reach / (1f - DetailPolicy.Hysteresis);
            nearWebs.Clear();
            for (int i = 0; i < webMiddle.Length; i++)
            {
                var d = math.distance(camPos, webMiddle[i]);
                if (d < listed)
                    nearWebs.Add((d, i));
                else
                    webNear[i] = false;
            }
            nearWebs.Sort((a, b) => a.distance.CompareTo(b.distance));
            webCount = 0;
            foreach (var (d, i) in nearWebs)
            {
                webNear[i] = DetailPolicy.Within(d, reach, webNear[i]);
                if (webNear[i] && webCount < MaxWebs)
                {
                    System.Array.Copy(webData, i * WebStride, webUpload, webCount * WebStride, WebStride);
                    webDrawn[webCount++] = i;
                }
            }
        }

        void ClearWebs()
        {
            webData = System.Array.Empty<float4>();
            webMiddle = System.Array.Empty<float3>();
            webNear = System.Array.Empty<bool>();
            nearWebs.Clear();
            webCount = 0;
            websAround = new float3(float.MaxValue);
        }

        void ReleaseWebs()
        {
            ClearWebs();
            webBuffer?.Release();
            webBuffer = null;
            websFrom = default;
        }
    }
}
