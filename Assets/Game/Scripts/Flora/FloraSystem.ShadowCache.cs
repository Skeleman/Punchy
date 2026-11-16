using Punchy.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Flora
{
    /// <summary>
    /// W-107 (FloraShadowOptions.CachedFarShadows, on by default): cached far-tree shadows. While on, trees cast in
    /// the real-time cascades only inside NearRange; the trees between there and CacheRange are drawn from the sun
    /// (orthographic, the Flora shader's FloraCache pass) into a float texture that keeps, per texel, how far along the
    /// sunlight the nearest tree is. The cache is redrawn only when the sun has turned CacheSunDegrees or the camera
    /// moved CacheMove (like the colossi's far shadows, re-laid after 10 m), and each redraw is spread over CacheFrames
    /// frames into a second texture, swapped in when done, so no frame pays for the whole forest. Every frame one
    /// full-screen triangle (Punchy/FloraShadowCache) darkens the pixels a cached tree stands between and the sun,
    /// where the real-time shadows don't already. Cosmetic and local: each client caches its own.
    /// </summary>
    public partial class FloraSystem
    {
        /// <summary>The flora layer that caches (trees).</summary>
        const int CacheLayer = 2;
        /// <summary>The cache's half width past CacheRange (m, room for long shadows) and the sun view's depth (m).</summary>
        const float CacheMargin = 15f, CacheDepth = 300f;

        static readonly int CacheVPId = Shader.PropertyToID("_FloraCacheVP");
        static readonly int CacheSunId = Shader.PropertyToID("_FloraCacheSun");
        static readonly int CacheBaseId = Shader.PropertyToID("_FloraCacheBase");
        static readonly int CacheTexId = Shader.PropertyToID("_FloraCacheTex");
        static readonly int CacheMatrixId = Shader.PropertyToID("_FloraCacheMatrix");
        static readonly int ReceiveSunId = Shader.PropertyToID("_FloraReceiveSun");
        static readonly int CacheParamsId = Shader.PropertyToID("_FloraCacheParams");
        static readonly int CacheFadeId = Shader.PropertyToID("_FloraCacheFade");
        static readonly int CacheOldTexId = Shader.PropertyToID("_FloraCacheOldTex");
        static readonly int CacheOldMatrixId = Shader.PropertyToID("_FloraCacheOldMatrix");
        static readonly int ReceiveOldSunId = Shader.PropertyToID("_FloraReceiveOldSun");
        static readonly int CacheBlendId = Shader.PropertyToID("_FloraCacheBlend");
        /// <summary>R-16 row 3: a new cache blends in over the old one over this long (s; 0 swaps at once). The sun
        /// turns CacheSunDegrees in about 2 s on the 24 min day, so a blend ends before the next turn redraw.</summary>
        public static float CacheBlendSeconds = 1f;

        /// <summary>Last finished redraw, for the world probe: "frames, trees, centre, sun".</summary>
        public static string LastCacheLog = "";
        /// <summary>Redraws finished this session (the sun-moving check counts them).</summary>
        public static int CacheRedraws;
        /// <summary>GPU memory the cache holds (both textures, their depth and its buffers), in bytes.</summary>
        public static long CacheBytes;

        RenderTexture cacheFront, cacheBack;
        GraphicsBuffer cacheVisible, cacheCounts, cacheArgs;
        GraphicsBuffer.IndirectDrawIndexedArgs[] cacheArgsData;
        MaterialPropertyBlock[] cacheBlocks;
        CommandBuffer cacheCommands;
        Material cacheReceiver;
        bool cacheReady;
        /// <summary>The redraw under way's next frame (-1: none).</summary>
        int cacheStep = -1;
        Matrix4x4 cacheBackVP, cacheBackUv, cacheFrontUv, cacheOldUv;
        Vector3 cacheOldSun;
        float cacheSwappedAt = -1e6f;
        bool cacheOldReady;
        Vector3 cacheBackSun, cacheFrontSun, cacheLaidSun;
        float3 cacheLaidAt;
        /// <summary>The near range the cache was laid for (it's redrawn when the near range changes).</summary>
        float cacheLaidNear = -1f;
        GraphicsBuffer cacheFor;
        int cacheForCount;

        /// <summary>FloraCull.compute's _Sharp (3.7 step 3): the real-time tier's floor and ceiling, and for the cache's
        /// cull how far inside a tree's real-time reach the cache starts (0: the camera's cull).</summary>
        static Vector4 SharpTier(float shadowDistance, float cacheInside)
            => new Vector4(FloraShadowOptions.NearRange, math.max(shadowDistance, FloraShadowOptions.NearRange), cacheInside, 0f);

        static float SunShadowDistance()
            => GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                ? urp.shadowDistance : QualitySettings.shadowDistance;

        void DrawShadowCache(Camera cam, float3 eye)
        {
            if (!FloraShadowOptions.CachedFarShadows)
            {
                if (cacheFront != null)
                    ReleaseShadowCache();
                return;
            }
            var sun = RenderSettings.sun;
            Layer l = null;
            foreach (var layer in layers)
                if (layer != null && layer.Index == CacheLayer)
                    l = layer;
            if (sun == null || !sun.isActiveAndEnabled || l == null || l.Count == 0 || l.ModelCount == 0 || l.Visible == null)
                return;
            var dir = sun.transform.forward;
            // The sun at or under the horizon casts nothing worth caching.
            var up = math.saturate((-dir.y - 0.03f) / 0.1f);
            if (up <= 0f)
                return;
            if (!CreateCache(l))
                return;

            if (cacheStep >= 0 && (l.Instances != cacheFor || l.Count != cacheForCount))
                cacheStep = -1;   // the layer re-uploaded mid-redraw: its visible list points at old plants
            // R-16 row 3: the sun's redraws wait for the last blend (the old cache sits in the back texture the next
            // redraw draws into); the camera leaving the laid view or a new near range does not wait.
            if (cacheStep < 0 && (!cacheReady || math.distance(eye, cacheLaidAt) > FloraShadowOptions.CacheMove ||
                                  cacheLaidNear != FloraShadowOptions.NearRange ||
                                  CacheBlend() >= 1f && Vector3.Angle(dir, cacheLaidSun) > FloraShadowOptions.CacheSunDegrees))
                StartCacheRedraw(l, eye, dir);
            if (cacheStep >= 0)
                DrawCacheSlice(l);
            if (cacheReady)
                DrawCacheReceiver(cam, eye, sun, up);
        }

        bool CreateCache(Layer l)
        {
            var size = FloraShadowOptions.CacheSize;
            if (cacheFront == null || cacheFront.width != size)
            {
                ReleaseCacheTextures();
                cacheFront = CacheTexture(size, "Flora shadow cache A");
                cacheBack = CacheTexture(size, "Flora shadow cache B");
            }
            var draws = l.ModelCount * Draws;
            if (cacheVisible == null || cacheVisible.count != l.Visible.count)
            {
                cacheVisible?.Release();
                cacheVisible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, l.Visible.count, sizeof(uint) * 2);
                cacheStep = -1;
            }
            if (cacheCounts == null || cacheCounts.count != draws)
            {
                cacheCounts?.Release();
                cacheArgs?.Release();
                cacheCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                cacheArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                cacheArgsData = new GraphicsBuffer.IndirectDrawIndexedArgs[draws];
                cacheBlocks = new MaterialPropertyBlock[l.ModelCount];
                cacheStep = -1;
            }
            if (cacheCommands == null)
                cacheCommands = new CommandBuffer { name = "Flora shadow cache" };
            if (cacheReceiver == null)
            {
                var shader = Resources.Load<Shader>("FloraShadowCache");
                if (shader == null)
                    return false;
                cacheReceiver = new Material(shader) { name = "Flora shadow cache receiver" };
            }
            // Two float textures with 16-bit depth, the visible list and two sets of draw arguments.
            CacheBytes = 2L * size * size * (4 + 2) + (long)cacheVisible.count * 8 + 2L * draws * GraphicsBuffer.IndirectDrawIndexedArgs.size;
            return true;
        }

        static RenderTexture CacheTexture(int size, string name)
        {
            var rt = new RenderTexture(size, size, 16, RenderTextureFormat.RFloat)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false
            };
            rt.Create();
            return rt;
        }

        /// <summary>Lays a new sun view round the camera and culls the trees in it (once; the draws then take a few frames).</summary>
        void StartCacheRedraw(Layer l, float3 eye, Vector3 dir)
        {
            var reach = FloraShadowOptions.CacheRange + CacheMargin;
            var up = math.abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up;
            // R-16 row 3: the view's centre snaps to the cache's texels across the sun's view, so a tree's shadow lands on
            // the same texels from one redraw to the next (only the sun turning moves it) instead of crawling as the
            // camera drifts a part of a texel each time.
            var rot = Quaternion.LookRotation(dir, up);
            var texel = 2f * reach / FloraShadowOptions.CacheSize;
            var local = Quaternion.Inverse(rot) * (Vector3)eye;
            local.x = Mathf.Round(local.x / texel) * texel;
            local.y = Mathf.Round(local.y / texel) * texel;
            var centre = rot * local;
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                       Matrix4x4.TRS(centre - dir * CacheDepth, rot, Vector3.one).inverse;
            var proj = Matrix4x4.Ortho(-reach, reach, -reach, reach, 1f, CacheDepth * 2f);
            var vp = proj * view;
            cacheBackVP = GL.GetGPUProjectionMatrix(proj, true) * view;
            cacheBackUv = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f)) * Matrix4x4.Scale(new Vector3(0.5f, 0.5f, 1f)) * vp;
            cacheBackSun = dir;
            cacheLaidSun = dir;
            cacheLaidAt = eye;
            cacheLaidNear = FloraShadowOptions.NearRange;
            cacheFor = l.Instances;
            cacheForCount = l.Count;

            // Same mesh ranges as the layer's own draws; instances counted afresh. The draws aren't RenderMeshIndirect's,
            // so they start at instance 0 and each is given its region of the visible list (_FloraCacheBase).
            for (int d = 0; d < cacheArgsData.Length; d++)
            {
                cacheArgsData[d] = l.ArgsData[d];
                cacheArgsData[d].instanceCount = 0;
                cacheArgsData[d].startInstance = 0;
            }
            cacheCounts.SetData(cacheArgsData);

            GeometryUtility.CalculateFrustumPlanes(vp, planes);
            for (int i = 0; i < 6; i++)
                planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            var cull = Culling.FindKernel("Cull");
            Culling.SetInt(CountId, l.Count);
            Culling.SetInt(ModelBaseId, l.ModelBase);
            Culling.SetVectorArray(PlanesId, planeVectors);
            Culling.SetVector(CameraId, (Vector3)eye);
            Culling.SetVector(LodId, new Vector4(l.Lod1Distance, l.Lod2Distance, l.Radius, l.Radius));
            // Every tree out to CacheRange goes to the first shadow list (full mesh). The cache starts where the real-time
            // shadows end, less the CacheMove the camera can drift before the next redraw and 2 m of overlap: otherwise
            // the trees between the near range round the laid spot and round the camera cast nowhere (a gap that grows
            // as the near range shrinks). The overlap can't double a shadow: the receiver leaves real-time shade alone.
            // (3.7 step 3: each tree from its own real-time reach, by its size on screen, as in the camera's cull; under
            // -oldfloralod every tree's is the near range.)
            Culling.SetVector(ShadowId, new Vector4(FloraShadowOptions.CacheRange, 0f, 1e6f, 0f));
            Culling.SetVector(SharpId, SharpTier(SunShadowDistance(), FloraShadowOptions.CacheMove + 2f));
            Culling.SetBuffer(cull, InstancesId, l.Instances);
            Culling.SetBuffer(cull, VisibleId, cacheVisible);
            Culling.SetBuffer(cull, ArgsId, cacheCounts);
            Culling.SetBuffer(cull, RegionsId, l.Regions);
            Culling.SetBuffer(cull, BoundsId, l.Bounds);
            Culling.SetBuffer(cull, ModelReachId, l.ReachBuffer);
            Culling.SetBuffer(cull, ModelShadowId, l.SharpBuffer);
            Culling.SetBuffer(cull, CastStateId, l.CastState);
            Culling.SetBuffer(cull, ModelImpostorId, l.ImpostorBuffer);
            Culling.SetFloat(CastHoldId, 0f);   // the cache is laid now and then: no last frame to hold to
            Culling.Dispatch(cull, (l.Count + 63) / 64, 1, 1);
            Culling.SetFloat(CastHoldId, OldLod ? 0f : 1f);
            Graphics.CopyBuffer(cacheCounts, cacheArgs);
            cacheStep = 0;
        }

        /// <summary>Draws this frame's share of the models into the back texture; swaps it in after the last.</summary>
        void DrawCacheSlice(Layer l)
        {
            var frames = math.max(1, FloraShadowOptions.CacheFrames);
            var from = l.ModelCount * cacheStep / frames;
            var to = l.ModelCount * (cacheStep + 1) / frames;
            var pass = l.Material.FindPass("FloraCache");
            if (pass < 0)
                return;
            var cmd = cacheCommands;
            cmd.Clear();
            cmd.SetRenderTarget(cacheBack);
            if (cacheStep == 0)
                cmd.ClearRenderTarget(true, true, new Color(1e6f, 0f, 0f, 0f));
            cmd.SetGlobalMatrix(CacheVPId, cacheBackVP);
            cmd.SetGlobalVector(CacheSunId, cacheBackSun);
            for (int m = from; m < to; m++)
            {
                var model = models[l.ModelBase + m];
                if (l.ModelCounts[m] == 0 || !model.CastsShadows)
                    continue;
                var block = cacheBlocks[m] ??= new MaterialPropertyBlock();
                block.SetBuffer(VisibleId, cacheVisible);
                block.SetInteger(CacheBaseId, (int)l.ArgsData[m * Draws + 3].startInstance);
                cmd.DrawMeshInstancedIndirect(model.Lods[0], 0, l.Material, pass, cacheArgs,
                    (m * Draws + 3) * GraphicsBuffer.IndirectDrawIndexedArgs.size, block);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            if (++cacheStep < frames)
                return;
            cacheOldReady = cacheReady;
            cacheOldUv = cacheFrontUv;
            cacheOldSun = cacheFrontSun;
            cacheSwappedAt = Time.unscaledTime;
            (cacheFront, cacheBack) = (cacheBack, cacheFront);
            cacheFrontUv = cacheBackUv;
            cacheFrontSun = cacheBackSun;
            cacheReady = true;
            cacheStep = -1;
            CacheRedraws++;
            LastCacheLog = $"{frames} frames, {l.ModelCount} models, at ({cacheLaidAt.x:0}, {cacheLaidAt.z:0}), sun {cacheFrontSun}";
        }

        void DrawCacheReceiver(Camera cam, float3 eye, Light sun, float up)
        {
            var m = cacheReceiver;
            var near = FloraShadowOptions.NearRange;
            var far = FloraShadowOptions.CacheRange;
            m.SetTexture(CacheTexId, cacheFront);
            m.SetMatrix(CacheMatrixId, cacheFrontUv);
            m.SetVector(ReceiveSunId, cacheFrontSun);
            var blend = CacheBlend();
            m.SetVector(CacheBlendId, new Vector4(blend, 0f, 0f, 0f));
            m.SetTexture(CacheOldTexId, blend < 1f ? cacheBack : cacheFront);
            m.SetMatrix(CacheOldMatrixId, cacheOldUv);
            m.SetVector(ReceiveOldSunId, cacheOldSun);
            m.SetVector(CacheParamsId, new Vector4(eye.x, eye.y, eye.z, FloraShadowOptions.CacheDarkness * sun.shadowStrength * up));
            // In from half the near range (the real-time shadows keep it from doubling), out over the cache's last 8 m.
            // (R-6 row 0: 3D distance from the camera, as the cull hands trees over by; on the ground plane a summit's
            // view of the forest below faded the cache out where its trees had no real-time shadow.)
            m.SetVector(CacheFadeId, new Vector4(near * 0.5f, near * 0.8f, far - 8f, far));
            var rp = new RenderParams(m)
            {
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 4000f),
                camera = cam,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 3);
        }

        /// <summary>How far the last swapped-in cache has blended over the one before (1 when done or there was none).</summary>
        float CacheBlend() => !cacheOldReady || cacheStep >= 0 || CacheBlendSeconds <= 0f
            ? 1f : math.saturate((Time.unscaledTime - cacheSwappedAt) / CacheBlendSeconds);

        void ReleaseCacheTextures()
        {
            foreach (var rt in new[] { cacheFront, cacheBack })
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            cacheFront = cacheBack = null;
            cacheReady = false;
            cacheOldReady = false;
        }

        void ReleaseShadowCache()
        {
            ReleaseCacheTextures();
            cacheVisible?.Release();
            cacheCounts?.Release();
            cacheArgs?.Release();
            cacheVisible = cacheCounts = cacheArgs = null;
            cacheCommands?.Release();
            cacheCommands = null;
            if (cacheReceiver != null)
                Destroy(cacheReceiver);
            cacheReceiver = null;
            cacheStep = -1;
            CacheBytes = 0;
        }
    }
}
