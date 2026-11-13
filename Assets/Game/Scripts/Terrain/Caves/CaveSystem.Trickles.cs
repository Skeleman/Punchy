using System.Collections.Generic;
using Punchy.Settings;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-141: water trickling down cave walls from a slit in the rock (#261). The trickles are bake records
    /// (CaveSetBlob.Trickles, placed by CaveBaking.PlaceTrickles); this draws them. Once per world a worker follows each
    /// trickle's wall down from its slit (a grid of points hugging the rock, so the water stays on the wall however it
    /// bulges), then the nearest few are drawn by the Cave Trickle shader with no mesh, fading out as they shrink on
    /// screen. The slit, the dark wet streak and the puddle are painted into the rock by the cave shader from a short
    /// global list, so they stay in the rock's colour at any distance. The nearest two murmur quietly. Seed-derived and
    /// cosmetic: every player sees the same trickles and nothing is sent.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>The wall grid each trickle carries: rows from the slit down to where it lands, columns across it.</summary>
        const int TrickleRows = 16, TrickleColumns = 5;
        /// <summary>float4s per trickle in the buffer: four of header, then the grid.</summary>
        const int TrickleStride = 4 + TrickleRows * TrickleColumns;
        const int MaxTrickles = 12;
        /// <summary>Trickles listed for the rock (slit, streak, puddle), the nearest first.</summary>
        const int MaxWetTrickles = 16;
        /// <summary>How big a trickle is on screen, for DetailPolicy screen size: the sheet goes at 62 reference pixels
        /// (45 m at Detail 1), the splash at 23 (20 m), and the rock's wet list keeps those within 15 (about 190 m). Kept
        /// here until DetailPolicy has trickle sizes (R-13).</summary>
        const float TrickleSize = 3f, TricklePixels = 62.35f, SplashSize = 0.5f, SplashPixels = 23.4f, WetPixels = 15f;
        static float TrickleReach => DetailPolicy.ExtraReach(TrickleSize, TricklePixels);
        static float SplashReach => DetailPolicy.ExtraReach(SplashSize, SplashPixels);
        static float WetReach => DetailPolicy.ExtraReach(TrickleSize, WetPixels);
        /// <summary>The murmur: the two nearest, heard within a few metres (sim: audio reach).</summary>
        const int MaxTrickleSounds = 2;
        const float TrickleHeard = 14f;   // sim: audio reach

        BlobAssetReference<CaveSetBlob> tricklesFrom;
        System.Threading.Tasks.Task<float4[]> trickleTask;
        /// <summary>Per trickle (as in the blob): its header and grid, worked out once per world.</summary>
        float4[] trickleData = System.Array.Empty<float4>();
        /// <summary>Per trickle: its middle, kind and drawn last gather.</summary>
        float3[] trickleMiddle = System.Array.Empty<float3>();
        bool[] trickleNear = System.Array.Empty<bool>();
        readonly List<(float distance, int index)> nearTrickles = new List<(float, int)>();
        readonly int[] trickleDrawn = new int[MaxTrickles];
        int trickleCount;
        float3 tricklesAround = new float3(float.MaxValue);
        readonly float4[] trickleUpload = new float4[MaxTrickles * TrickleStride];
        readonly float[] trickleFade = new float[MaxTrickles], splashFade = new float[MaxTrickles];
        readonly Vector4[] wetUpload = new Vector4[MaxWetTrickles * 3];
        GraphicsBuffer trickleBuffer;
        Material trickleMaterial;
        MaterialPropertyBlock trickleProps;
        readonly Dictionary<int, int> trickleLoops = new Dictionary<int, int>();
        readonly List<(float distance, int index)> trickleHeard = new List<(float, int)>();
        readonly List<int> trickleGone = new List<int>();

        static readonly int TrickleDataId = Shader.PropertyToID("_Trickles");
        static readonly int TrickleFadeId = Shader.PropertyToID("_TrickleFade");
        static readonly int SplashFadeId = Shader.PropertyToID("_SplashFade");
        static readonly int WetTricklesId = Shader.PropertyToID("_PunchyTrickles");
        static readonly int WetTrickleCountId = Shader.PropertyToID("_PunchyTrickleCount");

        /// <summary>
        /// Draws the trickles near the camera: the grids are worked out on a worker when the world changes; the nearest
        /// 12 in reach are uploaded as the camera moves (and the rock's wet list with them), and each frame they fade by
        /// distance. The material comes from Resources (CaveTrickle), so it needs no scene reference.
        /// </summary>
        void DrawTrickles(in TerrainSource source, float3 camPos)
        {
            if (tricklesFrom != source.Caves)
            {
                ClearTrickles();
                tricklesFrom = source.Caves;
                if (source.Caves.IsCreated && source.Caves.Value.Trickles.Length > 0)
                {
                    var from = source;
                    trickleTask = System.Threading.Tasks.Task.Run(() => GatherTrickles(from));
                }
            }
            if (trickleTask != null)
            {
                if (!trickleTask.IsCompleted)
                    return;
                if (trickleTask.IsFaulted)
                    Debug.LogException(trickleTask.Exception);
                else
                    SetTrickles(trickleTask.Result);
                trickleTask = null;
            }
            if (trickleMiddle.Length == 0)
                return;
            if (math.distancesq(camPos, tricklesAround) > 4f)
            {
                tricklesAround = camPos;
                UploadTrickles(camPos);
            }
            TrickleSounds(camPos);
            if (trickleCount == 0)
                return;
            if (trickleMaterial == null)
            {
                trickleMaterial = Resources.Load<Material>("CaveTrickle");
                if (trickleMaterial == null)
                    return;
            }
            // Fading by distance each frame: out over the last third of each reach.
            var reach = TrickleReach;
            var splashReach = SplashReach;
            for (int i = 0; i < MaxTrickles; i++)
            {
                if (i >= trickleCount)
                {
                    trickleFade[i] = splashFade[i] = 0f;
                    continue;
                }
                var d = math.distance(camPos, trickleMiddle[trickleDrawn[i]]);
                trickleFade[i] = math.saturate((reach - d) / (reach * 0.33f));
                splashFade[i] = math.saturate((splashReach - d) / (splashReach * 0.33f));
            }
            trickleProps ??= new MaterialPropertyBlock();
            trickleProps.SetBuffer(TrickleDataId, trickleBuffer);
            trickleProps.SetFloatArray(TrickleFadeId, trickleFade);
            trickleProps.SetFloatArray(SplashFadeId, splashFade);
            var rp = new RenderParams(trickleMaterial)
            {
                worldBounds = new Bounds(camPos, Vector3.one * (reach / (1f - DetailPolicy.Hysteresis) * 2f)),
                matProps = trickleProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            // 60 quads down the wall (15 rows by 4 columns), the splash disc and the spray: 62 quads.
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 62 * 6, trickleCount);
        }

        /// <summary>Takes the worked-out grids (main thread): each trickle's middle, for distances.</summary>
        void SetTrickles(float4[] data)
        {
            trickleData = data;
            var count = data.Length / TrickleStride;
            trickleMiddle = new float3[count];
            trickleNear = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var top = data[i * TrickleStride + 4 + TrickleColumns / 2].xyz;
                var bottom = data[i * TrickleStride + 4 + (TrickleRows - 1) * TrickleColumns + TrickleColumns / 2].xyz;
                trickleMiddle[i] = (top + bottom) * 0.5f;
            }
            tricklesAround = new float3(float.MaxValue);
        }

        /// <summary>Picks the nearest trickles in reach and uploads them, and the rock's wet list.</summary>
        void UploadTrickles(float3 camPos)
        {
            var reach = TrickleReach;
            var wetReach = WetReach;
            nearTrickles.Clear();
            for (int i = 0; i < trickleMiddle.Length; i++)
            {
                var d = math.distance(camPos, trickleMiddle[i]);
                if (d < wetReach)
                    nearTrickles.Add((d, i));
            }
            nearTrickles.Sort((a, b) => a.distance.CompareTo(b.distance));

            // The rock's list: slit, streak and puddle, the nearest 16 (all 48 slots always set, so the array keeps its size).
            var wet = math.min(nearTrickles.Count, MaxWetTrickles);
            for (int i = 0; i < MaxWetTrickles; i++)
            {
                if (i >= wet)
                {
                    wetUpload[i * 3] = wetUpload[i * 3 + 1] = wetUpload[i * 3 + 2] = Vector4.zero;
                    continue;
                }
                var b = nearTrickles[i].index * TrickleStride;
                var head = trickleData[b];
                var land = trickleData[b + 1];
                var outward = trickleData[b + 2];
                var slit = trickleData[b + 4 + TrickleColumns / 2];
                var foot = trickleData[b + 4 + (TrickleRows - 1) * TrickleColumns + TrickleColumns / 2];
                wetUpload[i * 3] = new Vector4(slit.x, slit.y, slit.z, head.y);
                wetUpload[i * 3 + 1] = new Vector4(outward.x, outward.z, outward.w, head.x);
                // The puddle sits under the foot of the water (not where the bake probed), at the landing height.
                wetUpload[i * 3 + 2] = new Vector4(foot.x, land.y, foot.z, land.w);
            }
            Shader.SetGlobalVectorArray(WetTricklesId, wetUpload);
            Shader.SetGlobalInt(WetTrickleCountId, wet);

            // The drawn ones: in reach (held by DetailPolicy.Within), the nearest 12.
            trickleCount = 0;
            for (int k = 0; k < nearTrickles.Count; k++)
            {
                var (d, i) = nearTrickles[k];
                trickleNear[i] = DetailPolicy.Within(d, reach, trickleNear[i]);
                if (trickleNear[i] && trickleCount < MaxTrickles)
                {
                    System.Array.Copy(trickleData, i * TrickleStride, trickleUpload, trickleCount * TrickleStride, TrickleStride);
                    trickleDrawn[trickleCount++] = i;
                }
            }
            for (int i = 0; i < trickleNear.Length; i++)
            {
                if (math.distance(camPos, trickleMiddle[i]) >= wetReach)
                    trickleNear[i] = false;
            }
            if (trickleCount == 0)
                return;
            trickleBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTrickles * TrickleStride, 16);
            trickleBuffer.SetData(trickleUpload, 0, 0, trickleCount * TrickleStride);
        }

        /// <summary>
        /// A quiet trickle at the nearest two (there's no trickle sound yet, so the waterfall rush, pitched up and
        /// soft), louder for more water, fading to nothing towards the edge of hearing so nothing clicks off.
        /// </summary>
        void TrickleSounds(float3 camPos)
        {
            trickleHeard.Clear();
            for (int k = 0; k < nearTrickles.Count && nearTrickles[k].distance < TrickleHeard * 2f; k++)
            {
                var i = nearTrickles[k].index;
                var d = math.distance(camPos, trickleMiddle[i]);
                if (d < TrickleHeard)
                    trickleHeard.Add((d, i));
            }
            trickleHeard.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (trickleHeard.Count > MaxTrickleSounds)
                trickleHeard.RemoveRange(MaxTrickleSounds, trickleHeard.Count - MaxTrickleSounds);
            trickleGone.Clear();
            foreach (var key in trickleLoops.Keys)
            {
                var keep = false;
                foreach (var h in trickleHeard) keep |= h.index == key;
                if (!keep) trickleGone.Add(key);
            }
            foreach (var key in trickleGone)
            {
                Punchy.Audio.GameAudio.StopLoop(trickleLoops[key]);
                trickleLoops.Remove(key);
            }
            foreach (var (d, i) in trickleHeard)
            {
                var flow = trickleData[i * TrickleStride + 3].x;
                var volume = (0.06f + 0.1f * flow) * (1f - math.smoothstep(TrickleHeard * 0.6f, TrickleHeard, d));
                var at = trickleMiddle[i];
                if (!trickleLoops.TryGetValue(i, out var handle) || handle == 0)
                    trickleLoops[i] = Punchy.Audio.GameAudio.StartLoop(Punchy.Audio.SoundId.WaterfallRush, at, volume, TrickleHeard, Punchy.Audio.SoundGroup.World, 1.7f);
                else
                    Punchy.Audio.GameAudio.MoveLoop(handle, at, volume);
            }
        }

        /// <summary>
        /// Each trickle's header and wall grid (pure: runs on a worker). Header: (kind, width, seed, threads),
        /// (land xyz, puddle), (out x, 0, out z, drop), (flow, 1 on water, 0, 0). Grid: 16 rows from the slit to the
        /// landing height, 5 columns across the water's spread (a fan and a sheet widen downwards), each a point just off
        /// the rock found by marching in from the air; where no wall is found it keeps the row above's depth.
        /// </summary>
        static float4[] GatherTrickles(TerrainSource source)
        {
            ref var set = ref source.Caves.Value;
            var data = new float4[set.Trickles.Length * TrickleStride];
            for (int t = 0; t < set.Trickles.Length; t++)
            {
                var tr = set.Trickles[t];
                var b = t * TrickleStride;
                var outward = math.normalizesafe(tr.Out, new float2(1f, 0f));
                var out3 = new float3(outward.x, 0f, outward.y);
                var along = new float3(-outward.y, 0f, outward.x);
                var onWater = tr.Puddle <= 0f ? 1f : 0f;
                data[b] = new float4((float)tr.Kind, tr.Width, tr.Seed, tr.Threads);
                data[b + 1] = new float4(tr.Land, tr.Puddle);
                data[b + 2] = new float4(out3.x, 0f, out3.z, tr.Drop);
                data[b + 3] = new float4(tr.Flow, onWater, 0f, 0f);
                var bottom = tr.Land.y + 0.02f;
                for (int c = 0; c < TrickleColumns; c++)
                {
                    var recede = 0f;
                    for (int r = 0; r < TrickleRows; r++)
                    {
                        var down = r / (float)(TrickleRows - 1);
                        var spread = tr.Kind == TrickleKind.Fan ? math.lerp(1f, 2.2f, down)
                            : tr.Kind == TrickleKind.Sheet ? math.lerp(1f, 1.3f, down) : 1f;
                        var offset = (c / (float)(TrickleColumns - 1) - 0.5f) * tr.Width * spread;
                        var y = math.lerp(tr.Slit.y, bottom, down);
                        var plane = new float3(tr.Slit.x, y, tr.Slit.z) + along * offset;
                        if (tr.Kind != TrickleKind.Overhang && r > 0)
                        {
                            var start = plane + out3;
                            if (source.CaveDistance(start) < 0f)
                            {
                                var hit = CaveBaking.WallAlong(source, start, -outward, 2f);
                                if (hit > 0f)
                                    recede = 1f - hit;
                            }
                        }
                        data[b + 4 + r * TrickleColumns + c] = new float4(plane + out3 * (recede + 0.05f), recede);
                    }
                }
            }
            return data;
        }

        void ClearTrickles()
        {
            foreach (var handle in trickleLoops.Values)
                Punchy.Audio.GameAudio.StopLoop(handle);
            trickleLoops.Clear();
            trickleTask = null;
            trickleData = System.Array.Empty<float4>();
            trickleMiddle = System.Array.Empty<float3>();
            trickleNear = System.Array.Empty<bool>();
            nearTrickles.Clear();
            trickleCount = 0;
            tricklesAround = new float3(float.MaxValue);
            System.Array.Clear(wetUpload, 0, wetUpload.Length);
            Shader.SetGlobalVectorArray(WetTricklesId, wetUpload);
            Shader.SetGlobalInt(WetTrickleCountId, 0);
        }

        void ReleaseTrickles()
        {
            ClearTrickles();
            trickleBuffer?.Release();
            trickleBuffer = null;
            tricklesFrom = default;
        }
    }
}
