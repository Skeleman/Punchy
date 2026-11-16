using System.Collections.Generic;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-108 row 4: fish rising on far lakes at dawn (and less often at dusk), drawn only: a ring spreading out on the
    /// still water where one took a fly, with a second, fainter ring following it in. Nothing is simulated or sent: the
    /// lake map is cut into 128 m patches, each with a couple of lanes, and every few seconds a hash of (patch, lane,
    /// slot) on the shared clock decides whether a fish rises there and where, so every machine shows the same rings.
    /// Picked by the ring's size on screen: too small to see is skipped, and near enough to be big the water's own
    /// ripples (CreatureWakeSystem, for the real fish) take over, faded across so nothing pops.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        struct LakeRing
        {
            public float3 Centre;
            public float Radius, Faded, Width;
            public int Parts;
        }

        /// <summary>Patch size (m), lanes per patch, how often each lane may have a rise (s), how long a ring spreads (s), and
        /// how wide it spreads (m).</summary>
        const float RingCell = 128f, RingSlot = 5f, RingLife = 3.5f, RingSpread = 2.6f;
        const int RingLanes = 2;
        /// <summary>How big a ring must look (reference pixels across) to be drawn, and the band in which a big one gives way
        /// to the water's own ripples.</summary>
        const float RingGonePixels = 1.5f, RingNearFromPixels = 60f, RingNearToPixels = 90f;
        /// <summary>The most patches looked at a frame each way from the camera's (bounds the loop at long draw distances).</summary>
        const int RingCellsMax = 16;
        readonly List<LakeRing> lakeRings = new List<LakeRing>();
        /// <summary>Rings drawn last frame (probes).</summary>
        public static int LakeRingsDrawn;

        /// <summary>The fish rings round the camera now (t, hour: the shared clock). Returns how many capsules they need.</summary>
        int GatherLakeRings(float3 cam, double t, float hour, in TerrainSource terrain)
        {
            lakeRings.Clear();
            LakeRingsDrawn = 0;
            if (!terrain.Rivers.IsCreated || CaveSystem.SurfaceHidden >= 0.99f)
                return 0;
            // Fish rise most in the first hours of light, a little in the evening, hardly at all otherwise.
            var rate = hour >= 4.8f && hour < 8.5f ? 0.7f : hour >= 17.5f && hour < 20.5f ? 0.3f : 0f;
            if (rate <= 0f)
                return 0;
            ref var map = ref terrain.Rivers.Value;
            var ppm = DetailPolicy.Current.Data.PixelsPerMetre;
            var reach = math.min(TerrainStreamingSystem.DrawDistance, RingSpread * 2f * ppm / RingGonePixels);
            var centre = (int2)math.floor(cam.xz / RingCell);
            var span = math.min((int)math.ceil(reach / RingCell), RingCellsMax);
            var parts = 0;
            for (int cz = -span; cz <= span; cz++)
            for (int cx = -span; cx <= span; cx++)
            {
                var cell = centre + new int2(cx, cz);
                for (int lane = 0; lane < RingLanes; lane++)
                {
                    var laneShift = (math.hash(new int3(cell, lane + 77)) % 1000u) / 1000.0 * RingSlot;
                    var slot = (long)math.floor((t + laneShift) / RingSlot);
                    var rng = new Random(math.hash(new int4(cell, lane + 77, (int)slot)) | 1u);
                    if (rng.NextFloat() >= rate)
                        continue;
                    var start = slot * (double)RingSlot - laneShift + rng.NextFloat(0f, RingSlot - RingLife);
                    var u = (float)(t - start) / RingLife;
                    if (u < 0f || u > 1f)
                        continue;
                    // Somewhere in the patch, if that spot is open lake (deep enough to hold fish, off the shallows).
                    var xz = ((float2)cell + rng.NextFloat2()) * RingCell;
                    var grid = (int2)math.floor((xz - map.Origin) / map.CellSize);
                    if (math.any(grid < 0) || math.any(grid >= map.Resolution))
                        continue;
                    var index = grid.y * map.Resolution + grid.x;
                    if (map.LakeDepth[index] < 1.5f)
                        continue;
                    var at = new float3(xz.x, map.LakeLevel[index] + 0.03f, xz.y);
                    var d = math.distance(at, cam);
                    if (d > reach)
                        continue;
                    // The ring and, a moment behind it, a fainter one inside.
                    for (int ring = 0; ring < 2; ring++)
                    {
                        var v = ring == 0 ? u : (u - 0.25f) / 0.75f;
                        if (v <= 0f)
                            continue;
                        var radius = math.lerp(0.25f, RingSpread * (ring == 0 ? 1f : 0.7f), math.sqrt(v));
                        var px = DetailPolicy.ScreenSize(radius * 2f, d, ppm);
                        if (px < RingGonePixels || px > RingNearToPixels)
                            continue;
                        var show = (1f - v) * (ring == 0 ? 0.85f : 0.5f);
                        show *= math.saturate((px - RingGonePixels) / RingGonePixels);
                        show *= 1f - math.saturate((px - RingNearFromPixels) / (RingNearToPixels - RingNearFromPixels));
                        if (show <= 0.02f)
                            continue;
                        var n = math.clamp((int)(px / 3f), 8, 20);
                        lakeRings.Add(new LakeRing
                        {
                            Centre = at,
                            Radius = radius,
                            Faded = 1f - show,
                            // (A line kept a pixel and a half wide on screen, however far off.)
                            Width = math.max(0.04f, 0.75f * d / math.max(ppm, 1f)),
                            Parts = n
                        });
                        parts += n;
                    }
                    LakeRingsDrawn++;
                }
            }
            return parts;
        }

        /// <summary>Writes the gathered rings: each a loop of thin flat strokes lying on the water.</summary>
        int WriteLakeRings(NativeArray<SegmentInstance> output, int at)
        {
            var up = new float3(0f, 1f, 0f);
            var colour = new float4(0.84f, 0.88f, 0.92f, 0f);
            foreach (var r in lakeRings)
            {
                var chord = 2f * math.PI * r.Radius / r.Parts;
                for (int k = 0; k < r.Parts; k++)
                {
                    var a = (k + 0.1f) * 2f * math.PI / r.Parts;
                    var from = r.Centre + new float3(math.cos(a), 0f, math.sin(a)) * r.Radius;
                    var tangent = new float3(-math.sin(a), 0f, math.cos(a));
                    output[at++] = Part(from, quaternion.LookRotationSafe(tangent, up), chord * 0.85f, r.Width, r.Width, 0.15f, colour, r.Faded);
                }
            }
            return at;
        }
    }
}
