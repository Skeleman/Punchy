using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Birds high in the sky, drawn only: skeins of geese and cranes crossing in a V (mostly at dawn and dusk) and big
    /// birds circling on a thermal by day. Nothing is simulated or sent: each 512 m patch of sky has a few flight lanes,
    /// and every 90 s a hash of (patch, lane, slot) decides whether a flight goes over and what it is, so every machine
    /// works out the same birds in the same places from the shared day and time.
    /// A-108 row 4: a lone eagle over a high summit by day, wheeling slow and wide just above the top (each sky cell's
    /// summit found once from a coarse grid of its ground).
    /// </summary>
    public partial class CreatureRenderSystem
    {
        enum SkyKind : byte { Geese, Cranes, Soarers, Eagle }

        struct SkyFlight
        {
            public SkyKind Kind;
            public int Count;
            public uint Seed;
            /// <summary>Crossing flocks: the start of the line and its heading, the speed and when it set off.</summary>
            public float3 From;
            public float3 Heading;
            public float Speed;
            public double Start;
            /// <summary>Soarers: the thermal's middle (at height) and radius.</summary>
            public float3 Centre;
            public float Radius;
            /// <summary>0 in full view, 1 gone (far off, or a soaring spell's start and end).</summary>
            public float Faded;
        }

        const float SkyCell = 512f, SkySlot = 90f, SkyCrossing = 1300f, SkyRange = 900f;
        const int SkyLanes = 3;
        readonly List<SkyFlight> skyFlights = new List<SkyFlight>();
        /// <summary>#63: each sky cell's ground height (the island's terrain, keyed by its landmarks blob).</summary>
        readonly Dictionary<int2, float> skyGround = new Dictionary<int2, float>();
        /// <summary>A-108 row 4: each sky cell's highest ground (xyz) on a coarse grid, for an eagle over its summit.</summary>
        readonly Dictionary<int2, float3> skySummit = new Dictionary<int2, float3>();
        /// <summary>How high (m above the sea) and how far over its cell's middle a summit stands for an eagle to hold it.</summary>
        const float EagleSummit = 140f, EagleRise = 30f;
        int skyGroundKey;
        /// <summary>How many sky cell grounds were sampled since the start (A-48 check: a handful, not 9 a frame).</summary>
        public static int SkyGroundSamples;

        static int SkyParts(SkyKind kind) => kind == SkyKind.Cranes || kind == SkyKind.Eagle ? 5 : 4;

        /// <summary>
        /// The flights over the sky round the camera now (t: seconds on the shared clock, counting days; hour: the time
        /// of day). Returns how many capsules they need.
        /// </summary>
        int GatherSkyBirds(float3 cam, double t, float hour, in TerrainSource terrain)
        {
            skyFlights.Clear();
            if (CaveSystem.SurfaceHidden >= 0.99f)
                return 0;
            var dusk = hour >= 5f && hour < 8.5f || hour >= 17f && hour < 20.5f;
            var day = hour >= 8.5f && hour < 17f;
            if (!dusk && !day)
                return 0;
            var parts = 0;
            // (Forgotten when the island changes, v1 baked then v2, or when the camera has crossed many cells.)
            var islandKey = terrain.Landmarks.GetHashCode();
            if (islandKey != skyGroundKey || skyGround.Count > 256)
            {
                skyGround.Clear();
                skySummit.Clear();
                skyGroundKey = islandKey;
            }
            var centreCell = (int2)math.floor(cam.xz / SkyCell);
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                var cell = centreCell + new int2(cx, cz);
                var middle = ((float2)cell + 0.5f) * SkyCell;
                // (#63: the ground under a sky cell, sampled once per cell rather than 9 times a frame in managed code.)
                if (!skyGround.TryGetValue(cell, out var ground))
                {
                    ground = math.max(terrain.SampleHeight(middle), 0f);
                    skyGround[cell] = ground;
                    SkyGroundSamples++;
                }
                if (!skySummit.TryGetValue(cell, out var summit))
                {
                    // (A 5 x 5 grid over the cell, once: its highest point, near enough to the top for a bird 30 m up.)
                    summit = new float3(middle.x, float.MinValue, middle.y);
                    for (int sz = 0; sz < 5; sz++)
                    for (int sx = 0; sx < 5; sx++)
                    {
                        var xz = (float2)cell * SkyCell + (new float2(sx, sz) + 0.5f) * (SkyCell / 5f);
                        var y = terrain.SampleHeight(xz);
                        if (y > summit.y)
                            summit = new float3(xz.x, y, xz.y);
                    }
                    skySummit[cell] = summit;
                    SkyGroundSamples += 25;
                }
                var peak = summit.y >= EagleSummit && summit.y - ground >= EagleRise;
                for (int lane = 0; lane < SkyLanes; lane++)
                {
                    // (Scrambled: math.hash of a vector has no final mix, so next-door patches' draws would rise in step.)
                    var laneShift = (BiomeSampling.Scramble(math.hash(new int3(cell, lane))) % 1000u) / 1000.0 * SkySlot;
                    var slot = (long)math.floor((t + laneShift) / SkySlot);
                    // (This slot's flight and the last one's, which may still be crossing.)
                    for (long n = slot - 1; n <= slot; n++)
                    {
                        var rng = new Random(BiomeSampling.Scramble(math.hash(new int4(cell, lane, (int)n))) | 1u);
                        var start = n * (double)SkySlot - laneShift + rng.NextFloat(0f, SkySlot * 0.4f);
                        var roll = rng.NextFloat();
                        SkyFlight f = default;
                        f.Seed = rng.NextUInt();
                        if (lane == 1 && peak && (day || dusk) && roll < (day ? 0.6f : 0.25f))
                        {
                            // An eagle holding the summit: alone, wide and slow just above the top, for a few slots.
                            f.Kind = SkyKind.Eagle;
                            f.Count = 1;
                            f.Centre = new float3(summit.x, summit.y + rng.NextFloat(25f, 55f), summit.z);
                            f.Radius = rng.NextFloat(40f, 75f);
                            var u = (float)(t - start) / (SkySlot * 2f);
                            if (u < 0f || u > 1f)
                                continue;
                            f.Faded = 1f - math.smoothstep(0f, 0.08f, u) * (1f - math.smoothstep(0.92f, 1f, u));
                        }
                        else if (lane == 0 && day && roll < 0.35f)
                        {
                            // A spell of soaring over this patch: a few slots long, the birds fading in and out at its ends.
                            f.Kind = SkyKind.Soarers;
                            f.Count = 1 + rng.NextInt(3);
                            var c = middle + rng.NextFloat2Direction() * rng.NextFloat(0f, 180f);
                            f.Centre = new float3(c.x, ground + rng.NextFloat(70f, 130f), c.y);
                            f.Radius = rng.NextFloat(35f, 70f);
                            var u = (float)(t - start) / (SkySlot * 1.5f);
                            if (u < 0f || u > 1f)
                                continue;
                            f.Faded = 1f - math.smoothstep(0f, 0.1f, u) * (1f - math.smoothstep(0.9f, 1f, u));
                        }
                        else if (roll < (dusk ? 0.15f : 0.03f))
                        {
                            // A skein crossing the patch in a V: geese, or (less often) cranes, legs trailing.
                            f.Kind = rng.NextFloat() < 0.7f ? SkyKind.Geese : SkyKind.Cranes;
                            f.Count = 7 + rng.NextInt(9);
                            var dir = rng.NextFloat2Direction();
                            var across = new float2(-dir.y, dir.x) * rng.NextFloat(-200f, 200f);
                            var from = middle - dir * SkyCrossing * 0.5f + across;
                            f.From = new float3(from.x, ground + rng.NextFloat(60f, 110f), from.y);
                            f.Heading = new float3(dir.x, 0f, dir.y);
                            f.Speed = rng.NextFloat(12f, 16f);
                            f.Start = start;
                            var gone = (float)(t - start) * f.Speed;
                            if (gone < 0f || gone > SkyCrossing)
                                continue;
                        }
                        else
                            continue;
                        var at = SkyLeader(f, t);
                        // (R-6 row 0: 3D, as every draw distance; the flights' cells stay on the ground plane.)
                        var far = math.distance(at, cam);
                        if (far > SkyRange)
                            continue;
                        f.Faded = math.max(f.Faded, math.saturate((far - SkyRange * 0.7f) / (SkyRange * 0.3f)));
                        skyFlights.Add(f);
                        parts += f.Count * SkyParts(f.Kind);
                    }
                }
            }
            return parts;
        }

        static float3 SkyLeader(in SkyFlight f, double t) =>
            f.Kind == SkyKind.Soarers || f.Kind == SkyKind.Eagle ? f.Centre : f.From + f.Heading * (float)((t - f.Start) * f.Speed);

        /// <summary>Writes the gathered flights' birds.</summary>
        int WriteSkyBirds(double t, NativeArray<SegmentInstance> output, int at)
        {
            var up = new float3(0f, 1f, 0f);
            var time = (float)(t % 10000.0);
            foreach (var f in skyFlights)
            {
                var rng = new Random(f.Seed | 1u);
                for (int k = 0; k < f.Count; k++)
                {
                    float3 p, ahead;
                    var phase = rng.NextFloat(0f, 6.28f);
                    if (f.Kind == SkyKind.Soarers || f.Kind == SkyKind.Eagle)
                    {
                        // Round and round the thermal, each at its own height and pace, banking into the turn (an
                        // eagle slower and wider, rising and sinking a little as it goes round).
                        var eagle = f.Kind == SkyKind.Eagle;
                        var w = (eagle ? rng.NextFloat(0.05f, 0.08f) : rng.NextFloat(0.08f, 0.14f)) * (rng.NextBool() ? 1f : -1f);
                        var a = phase + time * w;
                        var r = f.Radius * rng.NextFloat(0.7f, 1.2f);
                        p = f.Centre + new float3(math.cos(a) * r, rng.NextFloat(-12f, 12f) + math.sin(time * 0.05f + phase) * 4f, math.sin(a) * r);
                        ahead = math.normalize(new float3(-math.sin(a), 0f, math.cos(a)) * math.sign(w));
                    }
                    else
                    {
                        // Its place in the V: behind the leader and out to one side, a rank further back each pair.
                        var spacing = f.Kind == SkyKind.Cranes ? 3.2f : 2.5f;
                        var rank = (k + 1) / 2;
                        var side = k == 0 ? 0f : (k % 2 == 1 ? 1f : -1f);
                        var right = math.cross(up, f.Heading);
                        p = SkyLeader(f, t) - f.Heading * rank * spacing + right * side * rank * spacing * 0.75f +
                            up * math.sin(time * 0.6f + phase) * 0.4f;
                        ahead = f.Heading;
                    }
                    at = SkyBird(f.Kind, p, ahead, time, phase, f.Faded, output, at);
                }
            }
            return at;
        }

        /// <summary>One bird: body, neck and head, two wings (beating, or held out gliding), and a crane's trailing legs.</summary>
        static int SkyBird(SkyKind kind, float3 p, float3 ahead, float time, float phase, float faded, NativeArray<SegmentInstance> output, int at)
        {
            var up = new float3(0f, 1f, 0f);
            var right = math.normalize(math.cross(up, ahead));
            var look = quaternion.LookRotationSafe(ahead, up);
            float body, girth, neck, span, chord, rate, beat;
            float4 colour, neckColour;
            switch (kind)
            {
                case SkyKind.Cranes:
                    body = 0.75f; girth = 0.13f; neck = 0.65f; span = 0.95f; chord = 0.14f; rate = 2.2f; beat = 0.45f;
                    colour = new float4(0.6f, 0.62f, 0.64f, 0f); neckColour = new float4(0.15f, 0.15f, 0.16f, 0f);
                    break;
                case SkyKind.Eagle:
                    // Big and dark, wings long and broad, held out flat with the odd slow beat.
                    body = 0.85f; girth = 0.17f; neck = 0.24f; span = 1.6f; chord = 0.3f; rate = 0.9f; beat = 0.06f;
                    colour = new float4(0.16f, 0.11f, 0.07f, 0f); neckColour = new float4(0.55f, 0.42f, 0.24f, 0f);
                    break;
                case SkyKind.Soarers:
                    body = 0.6f; girth = 0.13f; neck = 0.22f; span = 1.05f; chord = 0.19f; rate = 1.4f; beat = 0.08f;
                    colour = new float4(0.2f, 0.15f, 0.11f, 0f); neckColour = new float4(0.3f, 0.24f, 0.18f, 0f);
                    break;
                default:
                    body = 0.7f; girth = 0.14f; neck = 0.45f; span = 0.85f; chord = 0.13f; rate = 2.8f; beat = 0.5f;
                    colour = new float4(0.42f, 0.38f, 0.32f, 0f); neckColour = new float4(0.1f, 0.1f, 0.1f, 0f);
                    break;
            }
            output[at++] = Part(p - ahead * body * 0.5f, look, body, girth, girth * 0.6f, 1f, colour, faded);
            output[at++] = Part(p + ahead * body * 0.4f, look, neck, girth * 0.35f, girth * 0.3f, 1f, neckColour, faded);
            // Wings: a slow full beat (soarers barely move theirs), each from the shoulder out and up or down with it.
            var a = math.sin(time * rate * 2f * math.PI + phase) * beat + 0.08f;
            for (int side = -1; side <= 1; side += 2)
            {
                var wing = math.normalize(right * side * math.cos(a) + up * math.sin(a));
                var flat = math.normalize(math.cross(wing, ahead));
                output[at++] = Part(p + ahead * 0.05f, quaternion.LookRotationSafe(wing, flat), span, chord, chord * 0.5f, 0.15f, colour, faded);
            }
            if (kind == SkyKind.Cranes)
                output[at++] = Part(p - ahead * body * 0.45f, quaternion.LookRotationSafe(-ahead, up), 0.7f, 0.02f, 0.012f, 1f, neckColour, faded);
            else if (kind == SkyKind.Eagle)
                // A broad fanned tail behind.
                output[at++] = Part(p - ahead * body * 0.95f, quaternion.LookRotationSafe(-ahead, up), 0.4f, 0.1f, 0.16f, 0.2f, colour, faded);
            return at;
        }
    }
}
