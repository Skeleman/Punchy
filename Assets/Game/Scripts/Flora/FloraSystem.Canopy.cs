using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// R-22 row 3 (#298, far forest): one map of the island's tree cover, baked at load from the placement rules, so forest
    /// still reads as forest past where the trees themselves are drawn. Every tree-layer cell is placed exactly as the
    /// streaming places it (no stride), and each 9 m texel sums its own 2 x 2 cells: crown cover, how far its trees are
    /// drawn (their size reach), snow, bare share, crown colour and height. The terrain (Canopy.hlsl) turns that into a
    /// rough, dark crown cover that fades in exactly where the trees fade out (FloraCull.compute's end for an always-drawn
    /// tree, by its size on screen: DetailPolicy, rule 20), on the streamed ground, the far ring and the far island alike.
    ///
    /// Cosmetic and seed-derived: which trees exist doesn't change, every machine bakes the same map, nothing is sent.
    /// The bake is a function of placement only, so a season or a fire can rewrite it through <see cref="RebakeCanopy"/>.
    /// Worker threads only; the upload is two SetPixelData calls when it's done. "-farforest 0" turns it off for A/B shots.
    /// </summary>
    public partial class FloraSystem
    {
        /// <summary>Texels a side: 9 m each on the 9216 m island, two tree cells across (keep in step with Canopy.hlsl).</summary>
        public const int CanopyMapSize = 1024;
        /// <summary>The data map's reach channel holds a texel's mean size reach (m per reference pixel-per-metre, see
        /// ModelReach's z) over this, and the colour map's alpha its mean tree height over HeightScale (m).</summary>
        const float CanopyReachScale = 16f, CanopyHeightScale = 64f;
        /// <summary>A crown's radius against the model's bounding radius (which takes in the trunk), and how much of the
        /// ground an open crown (Fullness under OpenCrown) or a bare one (under BareCrown) still hides.</summary>
        const float CrownOfRadius = 0.45f, BareCrown = 0.25f, OpenCover = 0.6f, BareCover = 0.35f;

        /// <summary>"-farforest 0" leaves the far forest out (the map isn't baked or drawn), for shots with and without it.</summary>
        public static bool CanopyOn = !FlagOff("-farforest");

        /// <summary>The baked maps, once done (<see cref="CanopyReady"/>), their corner and width (m), and the bake's time (ms).</summary>
        public Texture2D CanopyData, CanopyColour;
        public float2 CanopyCorner;
        public float CanopyExtent;
        public bool CanopyReady;
        public float CanopyBakeMs;

        NativeArray<float4> canopyModel;     // per model: x crown cover at scale 1 (m²), y size reach, z height (m), w bare
        NativeArray<float4> canopyTint;      // per model: crown colour (sRGB), w 1 for a counted tree
        NativeArray<float4> canopySums;      // per texel, unblurred: cover, reach x cover, snow x cover, bare x cover
        NativeArray<float4> canopySumsB;     // per texel, unblurred: colour x cover, height x cover
        NativeArray<Color32> canopyOutA, canopyOutB;
        JobHandle canopyJob;
        bool canopyRunning, canopyStarted;
        System.Diagnostics.Stopwatch canopyClock;

        static readonly int CanopyDataId = Shader.PropertyToID("_PunchyCanopyData");
        static readonly int CanopyColourId = Shader.PropertyToID("_PunchyCanopyColour");
        static readonly int CanopyRectId = Shader.PropertyToID("_PunchyCanopyRect");
        static readonly int CanopyReachId = Shader.PropertyToID("_PunchyCanopyReach");
        static readonly int CanopyScaleId = Shader.PropertyToID("_PunchyCanopyScale");
        static readonly int CanopyThinId = Shader.PropertyToID("_PunchyCanopyThinFrom");

        static bool FlagOff(string flag)
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length && args[at + 1] == "0";
        }

        /// <summary>Throws the map away and bakes it again from the placement rules (a season or a fire changed them).</summary>
        public void RebakeCanopy()
        {
            ResetCanopy();
        }

        /// <summary>Starts the bake once the flora is built, picks it up when it's done, and hands the terrain this
        /// frame's reach (the same numbers the cull gets, so the cover comes in as the trees go out).</summary>
        void UpdateCanopy(in TerrainSource source)
        {
            if (canopyRunning && canopyJob.IsCompleted)
                FinishCanopy();
            if (!canopyStarted && CanopyOn && built && layers[FarReachLayer] != null && layers[FarReachLayer].ModelCount > 0)
                StartCanopy(source);
            var trees = layers[FarReachLayer];
            var on = CanopyReady && trees != null;
            Shader.SetGlobalVector(CanopyRectId, on
                ? new Vector4(CanopyCorner.x, CanopyCorner.y, 1f / CanopyExtent, CanopyExtent / CanopyMapSize)
                : Vector4.zero);
            if (!on)
                return;
            var ppm = FarPpmNow();
            Shader.SetGlobalTexture(CanopyDataId, CanopyData);
            Shader.SetGlobalTexture(CanopyColourId, CanopyColour);
            Shader.SetGlobalVector(CanopyReachId, new Vector4(ppm, trees.Radius, trees.FarRadius, trees.Settings.ThinTo));
            Shader.SetGlobalFloat(CanopyThinId, trees.Settings.ThinFrom * trees.Radius);   // (as the cull's _Thin.x)
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            Shader.SetGlobalVector(CanopyScaleId, new Vector4(CanopyReachScale, CanopyHeightScale, 1f,
                view.PixelsPerMetre * math.max(view.Detail, 0.1f)));
        }

        void StartCanopy(in TerrainSource source)
        {
            canopyStarted = true;
            var trees = layers[FarReachLayer];
            canopyModel = new NativeArray<float4>(math.max(models.Count, 1), Allocator.Persistent);
            canopyTint = new NativeArray<float4>(math.max(models.Count, 1), Allocator.Persistent);
            var any = false;
            for (int m = 0; m < trees.ModelCount; m++)
            {
                var model = models[trees.ModelBase + m];
                // Only the trees drawn on past the radius by their size: those are the ones whose end the cover meets.
                if (model.Species == null || trees.Reach[m].z >= 0f
                    || model.DetailClass() != Punchy.Settings.DetailPolicy.FloraClass.Tree)
                    continue;
                var s = model.Species;
                var bare = s.Fullness < BareCrown ? 1f : 0f;
                var crown = model.Radius * CrownOfRadius;
                var cover = math.PI * crown * crown * (bare > 0f ? BareCover : s.Fullness < OpenCrown ? OpenCover : 1f);
                var leaf = (s.Foliage + s.FoliageVariation) * 0.5f;
                var colour = bare > 0f ? s.Bark : Color.Lerp(leaf, s.Bark, 0.15f);   // (a crown's trunk and branches show through a little)
                canopyModel[trees.ModelBase + m] = new float4(cover, -trees.Reach[m].z, model.Height, bare);
                canopyTint[trees.ModelBase + m] = new float4(colour.r, colour.g, colour.b, 1f);
                any = true;
            }
            if (!any)
            {
                canopyModel.Dispose();
                canopyTint.Dispose();
                return;
            }
            CanopyExtent = math.max(source.World.WorldSize, IslandV2.WorldSizeM);   // (as the world glow: the v3 island is wider than the settings say)
            CanopyCorner = new float2(-CanopyExtent * 0.5f);
            var texels = CanopyMapSize * CanopyMapSize;
            canopySums = new NativeArray<float4>(texels, Allocator.Persistent);
            canopySumsB = new NativeArray<float4>(texels, Allocator.Persistent);
            canopyOutA = new NativeArray<Color32>(texels, Allocator.Persistent);
            canopyOutB = new NativeArray<Color32>(texels, Allocator.Persistent);
            canopyClock = System.Diagnostics.Stopwatch.StartNew();
            var bake = new CanopyBakeJob
            {
                Flora = placement,
                Source = source,
                Models = canopyModel,
                Tints = canopyTint,
                Corner = CanopyCorner,
                Texel = CanopyExtent / CanopyMapSize,
                Sums = canopySums,
                SumsB = canopySumsB
            }.Schedule(CanopyMapSize, 4);
            canopyJob = new CanopyBlurJob
            {
                Sums = canopySums,
                SumsB = canopySumsB,
                OutA = canopyOutA,
                OutB = canopyOutB
            }.Schedule(CanopyMapSize, 16, bake);
            canopyRunning = true;
        }

        void FinishCanopy()
        {
            canopyJob.Complete();
            canopyRunning = false;
            CanopyBakeMs = (float)canopyClock.Elapsed.TotalMilliseconds;
            CanopyData = NewCanopyTexture("Far forest (cover, reach, snow, bare)", true);
            CanopyData.SetPixelData(canopyOutA, 0);
            CanopyData.Apply(true, true);   // (mips from level 0; no CPU copy kept)
            CanopyColour = NewCanopyTexture("Far forest (colour, height)", false);
            CanopyColour.SetPixelData(canopyOutB, 0);
            CanopyColour.Apply(true, true);
            var forest = 0;
            for (int i = 0; i < canopyOutA.Length; i++)
                if (canopyOutA[i].r > 64)
                    forest++;
            CanopyReady = true;
            Debug.Log($"[flora] far forest map baked in {CanopyBakeMs:F0} ms: {CanopyMapSize}² over {CanopyExtent:F0} m, " +
                      $"{forest * 100f / canopyOutA.Length:F1}% of texels a quarter covered or more");
            DisposeCanopyWork();
        }

        static Texture2D NewCanopyTexture(string name, bool linear) =>
            new Texture2D(CanopyMapSize, CanopyMapSize, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave
            };

        void DisposeCanopyWork()
        {
            if (canopySums.IsCreated) canopySums.Dispose();
            if (canopySumsB.IsCreated) canopySumsB.Dispose();
            if (canopyOutA.IsCreated) canopyOutA.Dispose();
            if (canopyOutB.IsCreated) canopyOutB.Dispose();
            if (canopyModel.IsCreated) canopyModel.Dispose();
            if (canopyTint.IsCreated) canopyTint.Dispose();
        }

        /// <summary>Drops the maps and any bake in progress; the next frame starts a new one (if the flora is built).</summary>
        void ResetCanopy()
        {
            if (canopyRunning)
            {
                canopyJob.Complete();
                canopyRunning = false;
            }
            DisposeCanopyWork();
            if (CanopyData != null) Destroy(CanopyData);
            if (CanopyColour != null) Destroy(CanopyColour);
            CanopyData = CanopyColour = null;
            CanopyReady = false;
            canopyStarted = false;
            Shader.SetGlobalVector(CanopyRectId, Vector4.zero);
        }

        /// <summary>
        /// One row of texels: each places its own tree cells (a cell belongs to the texel its corner lies in, so no two
        /// rows share one) and sums what grows there. Cover is crown area over the texel's area; the rest are cover-weighted.
        /// </summary>
        [BurstCompile]
        struct CanopyBakeJob : IJobParallelFor
        {
            [ReadOnly] public BlobAssetReference<FloraBlob> Flora;
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float4> Models, Tints;
            public float2 Corner;
            public float Texel;
            [NativeDisableParallelForRestriction] public NativeArray<float4> Sums, SumsB;

            public void Execute(int row)
            {
                ref var f = ref Flora.Value;
                var cellSize = f.CellSizes[FarReachLayer];   // (the size Place itself steps by)
                var area = 1f / (Texel * Texel);
                var y0 = (int)math.ceil((Corner.y + row * Texel) / cellSize);
                var y1 = (int)math.ceil((Corner.y + (row + 1) * Texel) / cellSize);
                for (int x = 0; x < CanopyMapSize; x++)
                {
                    var x0 = (int)math.ceil((Corner.x + x * Texel) / cellSize);
                    var x1 = (int)math.ceil((Corner.x + (x + 1) * Texel) / cellSize);
                    float4 a = 0f, b = 0f;
                    for (int cy = y0; cy < y1; cy++)
                    for (int cx = x0; cx < x1; cx++)
                    {
                        var plant = FloraPlacement.Place(ref f, Source, new int2(cx, cy), FarReachLayer);
                        if (plant.Model < 0 || plant.Model >= Models.Length)
                            continue;
                        var tint = Tints[plant.Model];
                        if (tint.w <= 0f)
                            continue;
                        var model = Models[plant.Model];
                        var cover = model.x * plant.Scale * plant.Scale * area;
                        a += new float4(cover, model.y * plant.Scale * cover, math.max(plant.Snow, 0f) * cover, model.w * cover);
                        b += new float4(tint.xyz * cover, model.z * plant.Scale * cover);
                    }
                    Sums[row * CanopyMapSize + x] = a;
                    SumsB[row * CanopyMapSize + x] = b;
                }
            }
        }

        /// <summary>
        /// A 3 x 3 tent over the sums, then each texel's means. A texel with no trees of its own takes its neighbours'
        /// colour, reach and height (cover-weighted), so the bilinear read at a forest's edge never fades towards black.
        /// </summary>
        [BurstCompile]
        struct CanopyBlurJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> Sums, SumsB;
            [WriteOnly] public NativeArray<Color32> OutA, OutB;

            public void Execute(int row)
            {
                for (int x = 0; x < CanopyMapSize; x++)
                {
                    float4 a = 0f, b = 0f;
                    float weight = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var sx = x + dx;
                        var sy = row + dy;
                        if (sx < 0 || sy < 0 || sx >= CanopyMapSize || sy >= CanopyMapSize)
                            continue;
                        var w = (2 - math.abs(dx)) * (2 - math.abs(dy)) / 16f;
                        a += Sums[sy * CanopyMapSize + sx] * w;
                        b += SumsB[sy * CanopyMapSize + sx] * w;
                        weight += w;
                    }
                    var cover = a.x / weight;
                    var inv = a.x > 1e-6f ? 1f / a.x : 0f;
                    OutA[row * CanopyMapSize + x] = new Color32(Byte(cover), Byte(a.y * inv / CanopyReachScale),
                                                                Byte(a.z * inv), Byte(a.w * inv));
                    var colour = b.xyz * inv;
                    OutB[row * CanopyMapSize + x] = new Color32(Byte(colour.x), Byte(colour.y), Byte(colour.z),
                                                                Byte(b.w * inv / CanopyHeightScale));
                }
            }

            static byte Byte(float v) => (byte)math.clamp((int)math.round(v * 255f), 0, 255);
        }
    }
}
