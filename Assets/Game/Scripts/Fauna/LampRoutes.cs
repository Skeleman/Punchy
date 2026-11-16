using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>A-115: one street light on a lamplighter's round.</summary>
    public struct LampStop
    {
        /// <summary>Where its light comes from (as SurfaceGlow lights it).</summary>
        public float3 Light;
        /// <summary>The lamp's foot (xz), where the lamplighter stands to reach up.</summary>
        public float2 Foot;
        /// <summary>The hour it's lit at dusk (x) and put out at dawn (y).</summary>
        public float2 Hours;

        /// <summary>How long a lamp takes to come on or go out (hours).</summary>
        public const float Fade = 0.02f;

        /// <summary>How lit it is at this hour (0 out, 1 lit). (Here, not on LampRoutes, so Burst code can call it.)</summary>
        public static float Lit(float2 hours, float hour) =>
            hour >= 12f ? math.saturate((hour - hours.x) / Fade) : 1f - math.saturate((hour - hours.y) / Fade);

        /// <summary>Where a lamplighter coming from "from" stands to reach this lamp: a step short of its foot.</summary>
        public static float2 StandAt(float2 foot, float2 from) => foot + math.normalizesafe(from - foot, new float2(0f, -1f)) * Reach;

        /// <summary>How far from the lamp's foot its lamplighter stands (m).</summary>
        public const float Reach = 0.7f;
    }

    /// <summary>A-115: one lamplighter's round: its stops in walking order, its town and its hours.</summary>
    public struct LampRoute
    {
        public int First, Count;
        public int Settlement, Landmark;
        public float DuskStart, DuskEnd, DawnStart, DawnEnd;
    }

    public struct LampRouteBlob
    {
        public BlobArray<LampStop> Stops;
        public BlobArray<LampRoute> Routes;
    }

    /// <summary>The lamp routes, for the animals' world (on the Flora Lookup entity, host only).</summary>
    public struct LampRouteLookup : IComponentData
    {
        public BlobAssetReference<LampRouteBlob> Blob;
    }

    /// <summary>
    /// A-115 (#315): each town's street lights on a lamplighter's round. At dusk the round lights them one by one, at
    /// dawn it puts them out again, so a town lights up street by street. Seed-derived: every machine builds the same
    /// routes and works out each lamp from the clock and its place on the round, so nothing is sent; the lamplighter
    /// folk (host simulated, replicated as any folk) walks the same round at the same hours. Lights not on a round
    /// (beacons, pier lamps, anything outside a town) behave exactly as before: LitAt is 1 for them.
    /// </summary>
    public static class LampRoutes
    {
        /// <summary>When the dusk round starts and the dawn round ends (DayNightCycle hours): windows are coming on by
        /// 18.3 and still on at 5.7.</summary>
        public const float DuskStart = 18.3f, DawnEnd = 5.7f;
        /// <summary>The lamplighter's walk (m/s) and the time at each lamp (s), for the rounds' lengths: a little slower
        /// than folk really walk (about 1.1 m/s), so it's at each lamp with its pole up before the lamp comes on.</summary>
        public const float Walk = 0.95f, PerLamp = 4f;
        /// <summary>The longest a round may take (hours): a longer one is hurried (its lamplighter runs).</summary>
        public const float MaxWindow = 4f;
        /// <summary>A round longer than this is split between two lamplighters.</summary>
        public const int MaxStops = 28;
        public static BlobAssetReference<LampRouteBlob> Blob;
        /// <summary>The hour the lamps were last worked out for (FaunaDirector sets it every frame on every machine).</summary>
        public static float Hour = 12f;
        /// <summary>How many route lamps are lit now (SurfaceGlow rescans its far map when this changes).</summary>
        public static int LitCount;
        /// <summary>Each route lamp by its foot (Key).</summary>
        static readonly Dictionary<int2, int> byFoot = new Dictionary<int2, int>();
        public static string LastLog = "";

        public static int2 Key(float2 xz) => (int2)math.floor(xz * 4f);

        /// <summary>How lit a lamp is at this hour (0 out, 1 lit).</summary>
        public static float Lit(float2 hours, float hour) => LampStop.Lit(hours, hour);

        public static float2 StandAt(float2 foot, float2 from) => LampStop.StandAt(foot, from);

        /// <summary>How lit the light at this xz is now: 1 for any light not on a round.</summary>
        public static float LitAt(float2 xz)
        {
            if (!Blob.IsCreated || !byFoot.TryGetValue(Key(xz), out var i))
                return 1f;
            return Lit(Blob.Value.Stops[i].Hours, Hour);
        }

        /// <summary>The routes' stop index of the light at this xz, or -1.</summary>
        public static int StopAt(float2 xz) => Blob.IsCreated && byFoot.TryGetValue(Key(xz), out var i) ? i : -1;

        /// <summary>
        /// Every settlement's street lights (FloraPlacement.TownLights) as rounds: a nearest-neighbour walk from the
        /// light nearest the square, split in two when long. secondsPerHour is the clock's (DayNightCycle).
        /// </summary>
        public static BlobAssetReference<LampRouteBlob> Build(ref Punchy.Flora.FloraBlob f, in TerrainSource source, float secondsPerHour)
        {
            var stops = new List<LampStop>();
            var routes = new List<LampRoute>();
            var lights = new List<Punchy.Flora.FloraPlacement.TownLight>();
            var count = source.Settlements.IsCreated ? source.Settlements.Value.Settlements.Length : 0;
            for (int s = 0; s < count; s++)
            {
                lights.Clear();
                Punchy.Flora.FloraPlacement.TownLights(ref f, source, s, lights);
                // (Two candidate spots can land on one light; keep it once.)
                for (int a = lights.Count - 1; a > 0; a--)
                    for (int b = 0; b < a; b++)
                        if (math.all(Key(lights[a].Foot) == Key(lights[b].Foot)))
                        {
                            lights.RemoveAt(a);
                            break;
                        }
                if (lights.Count == 0)
                    continue;
                ref var town = ref source.Settlements.Value.Settlements[s];
                var tour = Tour(lights, town.Centre);
                var parts = tour.Count > MaxStops ? 2 : 1;
                for (int p = 0; p < parts; p++)
                {
                    int from = tour.Count * p / parts, to = tour.Count * (p + 1) / parts;
                    var route = new LampRoute { First = stops.Count, Count = to - from, Settlement = s, Landmark = town.Landmark };
                    // How far the round walks, to each stop and in all.
                    var cum = new float[route.Count];
                    var total = 0f;
                    for (int k = 1; k < route.Count; k++)
                    {
                        total += math.distance(lights[tour[from + k - 1]].Foot, lights[tour[from + k]].Foot) / Walk + PerLamp;
                        cum[k] = total;
                    }
                    total += PerLamp;
                    var window = math.clamp(total / math.max(secondsPerHour, 1f), 1f, MaxWindow);
                    route.DuskStart = DuskStart;
                    route.DuskEnd = DuskStart + window;
                    route.DawnStart = DawnEnd - window;
                    route.DawnEnd = DawnEnd;
                    for (int k = 0; k < route.Count; k++)
                    {
                        var t = window * cum[k] / total;
                        var light = lights[tour[from + k]];
                        stops.Add(new LampStop
                        {
                            Light = light.Light, Foot = light.Foot,
                            Hours = new float2(route.DuskStart + t, route.DawnStart + t)
                        });
                    }
                    routes.Add(route);
                }
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<LampRouteBlob>();
            var stopArray = builder.Allocate(ref root.Stops, stops.Count);
            for (int i = 0; i < stops.Count; i++)
                stopArray[i] = stops[i];
            var routeArray = builder.Allocate(ref root.Routes, routes.Count);
            for (int i = 0; i < routes.Count; i++)
                routeArray[i] = routes[i];
            var blob = builder.CreateBlobAssetReference<LampRouteBlob>(Allocator.Persistent);
            builder.Dispose();
            LastLog = $"[lamp routes] {routes.Count} rounds, {stops.Count} lamps";
            return blob;
        }

        /// <summary>A nearest-neighbour walk through the lights from the one nearest the centre (indices into lights).</summary>
        static List<int> Tour(List<Punchy.Flora.FloraPlacement.TownLight> lights, float2 centre)
        {
            var tour = new List<int>(lights.Count);
            var used = new bool[lights.Count];
            var at = centre;
            for (int n = 0; n < lights.Count; n++)
            {
                int best = -1;
                var bestDist = float.MaxValue;
                for (int i = 0; i < lights.Count; i++)
                {
                    if (used[i])
                        continue;
                    var d = math.distancesq(at, lights[i].Foot);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }
                used[best] = true;
                tour.Add(best);
                at = lights[best].Foot;
            }
            return tour;
        }

        /// <summary>Takes the built routes in (every machine): indexes them by foot.</summary>
        public static void Set(BlobAssetReference<LampRouteBlob> blob)
        {
            Blob = blob;
            byFoot.Clear();
            LitCount = 0;
            if (!blob.IsCreated)
                return;
            ref var stops = ref blob.Value.Stops;
            for (int i = 0; i < stops.Length; i++)
                byFoot[Key(stops[i].Foot)] = i;
        }

        /// <summary>Lets the routes go (before the blob is freed): every light then behaves as before.</summary>
        public static void Clear()
        {
            Blob = default;
            byFoot.Clear();
            LitCount = 0;
            Shader.SetGlobalFloat(LampCountId, 0f);
        }

        static int LampsId => Ids.Lamps;
        static int LampCountId => Ids.Count;

        /// <summary>(Its own class: no Unity calls in LampRoutes' static initialiser.)</summary>
        static class Ids
        {
            public static readonly int Lamps = Shader.PropertyToID("_PunchyLamps");
            public static readonly int Count = Shader.PropertyToID("_PunchyLampCount");
        }
        /// <summary>How many lamps the flora shader can hold dark (Flora.shader's _PunchyLamps).</summary>
        public const int ShaderLamps = 64;
        static readonly Vector4[] shaderLamps = new Vector4[ShaderLamps];
        static readonly List<(float Dist, int Stop, float Lit)> near = new List<(float, int, float)>();

        /// <summary>
        /// Every frame on every machine, after the clock: the hour, how many route lamps are lit, and the nearest
        /// unlit (or half lit) route lamps for the flora shader, which keeps their glass dark. None (count 0, the shader
        /// exactly as before) by day and once the rounds are done.
        /// </summary>
        public static void Tick(float hour, float night, float3 camera)
        {
            Hour = hour;
            var count = 0;
            near.Clear();
            if (Blob.IsCreated)
            {
                ref var stops = ref Blob.Value.Stops;
                for (int i = 0; i < stops.Length; i++)
                {
                    var lit = Lit(stops[i].Hours, hour);
                    if (lit >= 0.5f)
                        count++;
                    if (lit < 1f && night > 0.01f)
                        near.Add((math.distancesq(stops[i].Light, camera), i, lit));
                }
            }
            LitCount = count;
            var n = 0;
            if (near.Count > 0)
            {
                near.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                n = math.min(near.Count, ShaderLamps);
                ref var stops = ref Blob.Value.Stops;
                for (int k = 0; k < n; k++)
                {
                    var light = stops[near[k].Stop].Light;
                    shaderLamps[k] = new Vector4(light.x, light.y, light.z, near[k].Lit);
                }
                Shader.SetGlobalVectorArray(LampsId, shaderLamps);
            }
            Shader.SetGlobalFloat(LampCountId, n);
        }
    }
}
