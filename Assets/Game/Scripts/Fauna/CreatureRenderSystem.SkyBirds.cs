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
    /// </summary>
    public partial class CreatureRenderSystem
    {
        enum SkyKind : byte { Geese, Cranes, Soarers }

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

        static int SkyParts(SkyKind kind) => kind == SkyKind.Cranes ? 5 : 4;

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
            var centreCell = (int2)math.floor(cam.xz / SkyCell);
            for (int cz = -1; cz <= 1; cz++)
            for (int cx = -1; cx <= 1; cx++)
            {
                var cell = centreCell + new int2(cx, cz);
                var middle = ((float2)cell + 0.5f) * SkyCell;
                var ground = math.max(terrain.SampleHeight(middle), 0f);
                for (int lane = 0; lane < SkyLanes; lane++)
                {
                    var laneShift = (math.hash(new int3(cell, lane)) % 1000u) / 1000.0 * SkySlot;
                    var slot = (long)math.floor((t + laneShift) / SkySlot);
                    // (This slot's flight and the last one's, which may still be crossing.)
                    for (long n = slot - 1; n <= slot; n++)
                    {
                        var rng = new Random(math.hash(new int4(cell, lane, (int)n)) | 1u);
                        var start = n * (double)SkySlot - laneShift + rng.NextFloat(0f, SkySlot * 0.4f);
                        var roll = rng.NextFloat();
                        SkyFlight f = default;
                        f.Seed = rng.NextUInt();
                        if (lane == 0 && day && roll < 0.35f)
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
                        var far = math.distance(at.xz, cam.xz);
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
            f.Kind == SkyKind.Soarers ? f.Centre : f.From + f.Heading * (float)((t - f.Start) * f.Speed);

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
                    if (f.Kind == SkyKind.Soarers)
                    {
                        // Round and round the thermal, each at its own height and pace, banking into the turn.
                        var w = rng.NextFloat(0.08f, 0.14f) * (rng.NextBool() ? 1f : -1f);
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
            return at;
        }
    }
}
