using System.Collections.Generic;
using Punchy.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Job 115: the herds' far form, member by member, in Burst. GatherHerds decided each herd (in sight, where the clock
    /// has it, its ground) on the main thread and then worked out every animal in it there too: its place in the group,
    /// distance, size on screen and fade, ~1.5 ms a frame of managed math on a walk (q201), the whole of the creature
    /// renderer's gather pass. Now each herd in sight is queued (HerdWork) and one Burst job, run in place, does every
    /// animal, the same sums in the same order; only those it keeps come back to the managed side for their look.
    /// "-oldherds" works them out on the main thread, as before.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        /// <summary>A/B: every herd's animals worked out on the main thread, as before (-oldherds).</summary>
        public static bool OldHerds;

        /// <summary>A herd in sight, as GatherHerds decided it, for the member job.</summary>
        struct HerdWork
        {
            public HerdRecord Herd;
            public HerdShape Shape;
            public HerdPlace Place;
            public HerdGround Ground;
            public bool Handing;
            public float Woke;
        }

        /// <summary>One animal the member job kept: which herd (its HerdWork) and animal, and what the managed side needs.</summary>
        struct HerdHit
        {
            public int Work, K;
            public float2 Xz;
            public float3 At;
            public float Y, Distance, Pixels, Show, Scale, Yaw;
        }

        /// <summary>The herds in sight this frame, beside their work items: look, herd index and dust.</summary>
        readonly List<(HerdLook Look, int Herd, float4 Dust)> herdWorkInfo = new List<(HerdLook, int, float4)>();

        [BurstCompile]
        struct HerdMembersJob : IJob
        {
            [ReadOnly] public NativeArray<HerdWork> Work;
            public NativeList<HerdHit> Hits;
            public float3 Camera;
            public double T;
            public float Draw, PixelsPerMetre, Now;

            public void Execute()
            {
                for (int w = 0; w < Work.Length; w++)
                {
                    var work = Work[w];
                    var h = work.Herd;
                    var shape = work.Shape;
                    var place = work.Place;
                    var ground = work.Ground;
                    for (int k = 0; k < h.Count; k++)
                    {
                        var xz = place.Centre + HerdClock.Offset(h, shape, k, T, place.Walking);
                        var y = ground.Height + math.dot(xz - ground.At, ground.Slope);
                        var at = new float3(xz.x, y, xz.y);
                        var d = math.distance(at, Camera);
                        if (d > Draw)
                            continue;
                        var scale = HerdClock.Scale(h, shape, k);
                        var px = DetailPolicy.ScreenSize(shape.BodyLength * scale, d, PixelsPerMetre);
                        if (px < DetailPolicy.HerdGonePixels)
                            continue;
                        var show = math.saturate((px - DetailPolicy.HerdGonePixels) / (DetailPolicy.HerdFadePixels - DetailPolicy.HerdGonePixels));
                        show *= 1f - math.saturate((d - Draw * 0.85f) / (Draw * 0.1f));
                        if (work.Handing)
                            show *= math.max(math.saturate((d - HerdHandOver) / HerdHandOverRange),
                                             1f - math.saturate((Now - work.Woke - HerdWakeGrace) / HerdHandOverTime));
                        if (show <= 0.001f)
                            continue;
                        Hits.Add(new HerdHit
                        {
                            Work = w, K = k, Xz = xz, At = at, Y = y, Distance = d, Pixels = px, Show = show, Scale = scale,
                            Yaw = HerdClock.Yaw(h, place, k, T)
                        });
                    }
                }
            }
        }

        /// <summary>Runs the member job over the queued herds and turns what it kept into members. Returns their capsules.</summary>
        int GatherHerdMembers(NativeList<HerdWork> work, in Punchy.Terrain.TerrainSource terrain, float3 cam, double t, float draw, float ppm, float now)
        {
            var hits = new NativeList<HerdHit>(256, Allocator.TempJob);
            new HerdMembersJob { Work = work.AsArray(), Hits = hits, Camera = cam, T = t, Draw = draw, PixelsPerMetre = ppm, Now = now }.Run();
            var parts = 0;
            var last = -1;
            for (int n = 0; n < hits.Length; n++)
            {
                var hit = hits[n];
                var (look, herd, dust) = herdWorkInfo[hit.Work];
                var h = work[hit.Work].Herd;
                var member = new HerdMember
                {
                    Look = look,
                    Position = hit.At,
                    Ground = hit.Y,
                    Yaw = hit.Yaw,
                    Scale = hit.Scale,
                    Faded = 1f - hit.Show,
                    Coat = HerdClock.Coat(ref look.Blob.Value, h, hit.K),
                    Tier = (byte)(hit.Pixels >= DetailPolicy.HerdLegsPixels ? 2 : hit.Pixels >= DetailPolicy.HerdBodyPixels ? 1 : 0),
                    Lying = work[hit.Work].Place.Lying,
                    Walking = work[hit.Work].Place.Walking
                };
                // (Legs stand on the ground under the animal itself, not the herd's plane.)
                if (member.Tier == 2)
                    member.Ground = MemberGround(terrain, herd, hit.K, hit.Xz, hit.Y);
                herdMembers.Add(member);
                parts += HerdParts(member);
                if (dust.w > 0f)
                    parts += HerdDustBehind(member, hit.K, t, hit.Distance, ppm, hit.Show, dust);
                if (hit.Work != last)
                {
                    last = hit.Work;
                    HerdsDrawn++;
                    herdDrawnHash = herdDrawnHash * 31u + (uint)herd + 1u;
                }
            }
            hits.Dispose();
            return parts;
        }
    }
}
