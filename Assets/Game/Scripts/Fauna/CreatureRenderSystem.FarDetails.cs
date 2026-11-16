using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Small things round the towns (A-110 row 7, the details tenet, with A-109 row 4), drawn only: a boat moored by the
    /// wharf (or on the shore of a lake beside a town with none) that the town's fisher rows out before dawn with a lamp
    /// in the bow, and washing on a line beside some houses on some days. The people in them are the town's real
    /// residents: the rower is its first living fisher (or a grown worker), and only on a morning Whereabouts has that member at home (so it
    /// has no creature anywhere, and is never in two places). The lanterns after dark and the fishers' waves are on the
    /// far figures themselves (CreatureRenderSystem.FarFolk). Everything is a pure function of placement records
    /// (households, the town plan, the wharf prop, the lake map), the day and the shared clock: no entities, no random
    /// draw, nothing sent or stored, so every machine shows the same. Picked by on-screen size.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        /// <summary>How big (reference pixels) a washing cloth or a boat has to look to be drawn at all (thinned by
        /// dither down to half of it), and how big before a line's posts and cord are drawn with it.</summary>
        const float FarDetailPixels = 2f, FarDetailNearPixels = 8f;
        /// <summary>The boat's morning (rowing out, lying out, rowing back: after the folk day turns, before the fisher's
        /// day starts), and the hours washing hangs.</summary>
        const float BoatOut = 4.1f, BoatThere = 4.6f, BoatBack = 5.3f, BoatHome = 5.8f;
        const float WashFrom = 7.5f, WashTo = 17.25f;

        /// <summary>One landmark's boat: its mooring, the spot it rows to, and who rows it on which day.</summary>
        sealed class FarPlace
        {
            public int Landmark;
            public string Name;
            public uint Seed;
            public bool Pier, Boat;
            public float3 PierEnd, Moor, Out;
            public float2 PierOut;
            public int RowerDay = int.MinValue, Rower = -1;
            /// <summary>The town's plan once it is built (Whereabouts.Towns), or null.</summary>
            public TownPlan Plan => Whereabouts.Towns.TryGetValue(Landmark, out var plan) ? plan : null;
        }

        struct WashLine
        {
            public float3 A, B;
            public float2 Across;
            public uint Seed;
        }

        readonly List<FarPlace> farPlaces = new List<FarPlace>();
        readonly List<WashLine> farLines = new List<WashLine>();
        readonly List<SegmentInstance> farDetailParts = new List<SegmentInstance>();
        int farDetailsBuilt = -1;
        bool farDetailLog, farDetailLogRead;
        float farDetailLogNext;

        bool FarDetailLog
        {
            get
            {
                if (!farDetailLogRead)
                {
                    farDetailLogRead = true;
                    farDetailLog = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-fardetails") >= 0;
                }
                return farDetailLog;
            }
        }

        /// <summary>The landmark's wharf (its Wharf prop, placed as the art is) and its spots, as the brain's FindWharf
        /// and WharfSpots.GullSpots find it.</summary>
        static bool WharfAt(ref FloraBlob f, ref WharfSpotSet w, in TerrainSource terrain, int landmark,
                            out FloraSystem.FloraInstance wharf, out int2 range)
        {
            wharf = default;
            range = default;
            ref var set = ref terrain.Landmarks.Value;
            ref var inst = ref set.Instances[landmark];
            if (inst.Type >= f.LandmarkTypeCount)
                return false;
            var radius = set.Types[inst.Type].Radius * inst.Scale;
            for (int l = 0; l < 3; l++)
            {
                var placed = f.PropRanges[inst.Type * 3 + l];
                for (int e = placed.x; e < placed.x + placed.y; e++)
                {
                    var prop = f.Props[e];
                    if (prop.Count == 0 || f.Rules[prop.Species].PerVillage == 0 || f.Rules[prop.Species].FaceLandmark != 2)
                        continue;
                    var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle), radius);
                    wharf = FloraPlacement.Place(ref f, terrain, (int2)math.floor(spot / f.CellSizes[l]), l);
                    if (wharf.Model < 0 || wharf.Model >= w.ModelSpots.Length)
                        continue;
                    range = w.ModelSpots[wharf.Model];
                    if (range.y > 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Whether there is water to float a boat at a point, and its surface.</summary>
        static bool Afloat(in TerrainSource terrain, float2 xz, float depth, out float3 at)
        {
            var g = terrain.SampleGround(xz);
            at = new float3(xz.x, g.WaterSurface, xz.y);
            return g.HasWater && g.WaterSurface - g.Height >= depth;
        }

        /// <summary>Works out every landmark's details from the placement records, once per set of households.</summary>
        void BuildFarDetails(in TerrainSource terrain)
        {
            farPlaces.Clear();
            farLines.Clear();
            if (!terrain.Landmarks.IsCreated)
                return;
            ref var set = ref terrain.Landmarks.Value;
            var hasFlora = SystemAPI.TryGetSingleton<FloraLookup>(out var flora) && flora.Blob.IsCreated;
            var wharves = default(WharfSpotLookup);
            var hasWharves = hasFlora && SystemAPI.TryGetSingleton(out wharves) && wharves.Blob.IsCreated;
            var houses = Households.All;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                var homes = Households.ByLandmark.TryGetValue(i, out var which) && which.Count > 0 &&
                            (houses[which[0]].Kind == "house" || houses[which[0]].Kind == "hut");
                var place = new FarPlace
                {
                    Landmark = i,
                    Name = "landmark",
                    Seed = BiomeSampling.Scramble((uint)i * 0x9E3779B9u + (homes ? houses[which[0]].Key : 0x51ED27u))
                };
                foreach (var town in FarFolk.Towns)
                    if (town.Landmark == i)
                    {
                        place.Name = town.Name;
                    }

                // The pier's end, and a boat moored beside it that rows straight out from it.
                if (hasWharves && WharfAt(ref flora.Blob.Value, ref wharves.Blob.Value, terrain, i, out var wharf, out var range))
                {
                    ref var w = ref wharves.Blob.Value;
                    for (int k = range.x; k < range.x + range.y && !place.Pier; k++)
                        if (w.Spots[k].Kind == WharfSpotKind.End)
                        {
                            var xz = WharfSpotSet.ToWorld(w.Spots[k].At, wharf.Position, wharf.Yaw, wharf.Scale);
                            place.PierEnd = new float3(xz.x, wharf.Position.y + WharfSpotSet.DeckHeight(w.Spots[k].At) * wharf.Scale, xz.y);
                            place.PierOut = math.normalizesafe(xz - wharf.Position.xz, new float2(0f, 1f));
                            place.Pier = true;
                        }
                    if (place.Pier)
                    {
                        var side = new float2(place.PierOut.y, -place.PierOut.x);
                        if (Afloat(terrain, place.PierEnd.xz + place.PierOut * 3f + side * 3f, 0.5f, out place.Moor))
                            for (float d = 45f; d >= 15f && !place.Boat; d -= 10f)
                                place.Boat = Afloat(terrain, place.PierEnd.xz + place.PierOut * d, 0.8f, out place.Out);
                    }
                }
                // No wharf: a town beside a lake keeps a boat on its near shore, and rows to the lake's nearest deep water.
                else if (homes && terrain.Rivers.IsCreated)
                {
                    ref var map = ref terrain.Rivers.Value;
                    var reach = radius + 200f;
                    var lo = math.clamp((int2)math.floor((inst.Position - reach - map.Origin) / map.CellSize), 0, map.Resolution - 1);
                    var hi = math.clamp((int2)math.floor((inst.Position + reach - map.Origin) / map.CellSize), 0, map.Resolution - 1);
                    var best = reach * reach;
                    var deep = float2.zero;
                    for (int z = lo.y; z <= hi.y; z++)
                    for (int x = lo.x; x <= hi.x; x++)
                    {
                        if (map.LakeDepth[z * map.Resolution + x] < 1.2f)
                            continue;
                        var at = map.Origin + (new float2(x, z) + 0.5f) * map.CellSize;
                        var d = math.distancesq(at, inst.Position);
                        if (d < best)
                        {
                            best = d;
                            deep = at;
                        }
                    }
                    if (best < reach * reach && Afloat(terrain, deep, 0.8f, out place.Out))
                    {
                        // (Back towards the houses until the water gives out: the last spot that floats is the mooring.)
                        var back = math.normalizesafe(inst.Position - deep);
                        place.Moor = place.Out;
                        for (float d = 3f; d <= 60f; d += 3f)
                        {
                            if (!Afloat(terrain, deep + back * d, 0.45f, out var at))
                                break;
                            place.Moor = at;
                        }
                        place.Boat = math.distance(place.Moor, place.Out) > 6f;
                    }
                }
                if (place.Boat)
                    farPlaces.Add(place);

                // Washing lines: beside the door of about a third of the houses, on whichever side is clear of the
                // neighbours' doors.
                if (!homes)
                    continue;
                foreach (var h in which)
                {
                    var house = houses[h];
                    if (FarFolk.Hash01(house.Key, 31) >= 0.35f)
                        continue;
                    var outward = new float2(math.sin(house.Door.w), math.cos(house.Door.w));
                    var along = new float2(outward.y, -outward.x) * (FarFolk.Hash01(house.Key, 32) < 0.5f ? 1f : -1f);
                    for (int tries = 0; tries < 2; tries++, along = -along)
                    {
                        var a = house.Door.xz + along * 1.8f + outward * 1.6f;
                        var b = house.Door.xz + along * 5.4f + outward * 1.6f;
                        var clear = true;
                        foreach (var o in which)
                            clear &= o == h || math.distancesq(houses[o].Door.xz, (a + b) * 0.5f) > 36f;
                        if (!clear)
                            continue;
                        farLines.Add(new WashLine
                        {
                            A = new float3(a.x, terrain.SampleHeight(a), a.y),
                            B = new float3(b.x, terrain.SampleHeight(b), b.y),
                            Across = outward,
                            Seed = house.Key
                        });
                        break;
                    }
                }
            }
            if (FarDetailLog)
            {
                foreach (var p in farPlaces)
                    Debug.Log($"[fardetails] {p.Name} #{p.Landmark}: " +
                              $"pier {(p.Pier ? "(" + p.PierEnd.x.ToString("F0") + "," + p.PierEnd.z.ToString("F0") + ")" : "none")}, " +
                              $"boat moored ({p.Moor.x:F0},{p.Moor.y:F1},{p.Moor.z:F0}) out ({p.Out.x:F0},{p.Out.z:F0})");
                Debug.Log($"[fardetails] {farPlaces.Count} places, {farLines.Count} washing lines of {houses.Count} houses");
            }
        }

        /// <summary>Whether a resident works a fishing post (a resident row's Fish task, or a village Fisher job).</summary>
        static bool IsFisher(TownPlan plan, Resident r) =>
            r.Job >= 0 && r.Job < plan.Places.Count &&
            (plan.Places[r.Job].Task == CreatureTask.Fish || plan.Places[r.Job].Job == FolkJob.Fisher);

        /// <summary>
        /// Who rows the place's boat this morning (a resident index, or -1): the town's first living fisher, or in a
        /// town without one a grown worker, and only when Whereabouts keeps that member in at home from before the boat
        /// goes out till after it is back, so the rower is never also standing somewhere else. Worked out once a day
        /// (the boat's hours are after the folk day turns, so the asks stay in the day being drawn).
        /// </summary>
        static int Rower(FarPlace p, int day, double t, bool log)
        {
            if (p.RowerDay == day)
                return p.Rower;
            // (No plan yet: asked again next frame. A landmark that is no town never has one.)
            var plan = p.Plan;
            if (plan == null)
                return -1;
            p.RowerDay = day;
            p.Rower = -1;
            var why = "nobody at home all morning";
            // (Its first living fisher; a town with none sends the first grown worker it keeps at home, asking
            // Whereabouts about a few at most.)
            for (int pass = 0; pass < 2 && p.Rower < 0; pass++)
                for (int i = 0, asked = 0; i < plan.Residents.Count && asked < (pass == 0 ? 1 : 8); i++)
                {
                    var r = plan.Residents[i];
                    if (Whereabouts.Dead(r) || r.Child || r.Job < 0 || IsFisher(plan, r) != (pass == 0))
                        continue;
                    asked++;
                    if (Whereabouts.Of(plan, i, day, BoatOut, t).Inside != r.House || Whereabouts.Of(plan, i, day, BoatHome, t).Inside != r.House)
                        continue;
                    p.Rower = i;
                    why = $"rowed by {(pass == 0 ? "fisher" : "worker")} {i} (house {r.House}, works {math.distance(plan.Places[r.Job].At, p.Moor.xz):F0} m from the mooring)";
                    break;
                }
            if (log)
                Debug.Log($"[fardetails] {p.Name} #{p.Landmark} day {day}: {why}");
            return p.Rower;
        }

        /// <summary>0 outside from..to (hours), 1 inside, easing over a quarter hour at each end.</summary>
        static float Hours(float hour, float from, float to) =>
            math.saturate((hour - from) / 0.25f) * math.saturate((to - hour) / 0.25f);

        /// <summary>A light: a small glowing ball, never drawn under the mote size wide.</summary>
        void FarLight(float3 at, float radius, float distance, float metresPerPixel, float faded) =>
            farDetailParts.Add(Part(at, quaternion.identity, 0.02f, math.max(radius, FarFolkMotePixels * 0.5f * distance * metresPerPixel),
                                    math.max(radius, FarFolkMotePixels * 0.5f * distance * metresPerPixel), 1f, new float4(1f, 0.78f, 0.32f, 1f), faded));

        /// <summary>
        /// The details in view now (t: seconds on the shared clock; hour: the time of day). Walking figures join the
        /// far figures gathered this frame; the rest is kept for WriteFarDetails. Returns how many capsules both need.
        /// </summary>
        int GatherFarDetails(in TerrainSource terrain, float3 cam, double t, float hour)
        {
            farDetailParts.Clear();
            if (!Households.Ready || CaveSystem.SurfaceHidden >= 0.99f)
            {
                farDetailsBuilt = Households.Ready ? farDetailsBuilt : -1;
                return 0;
            }
            // (After the far towns are built, so the places carry their names: a few seconds at most.)
            if (farDetailsBuilt != Households.All.Count)
            {
                if (UnityEngine.Time.timeSinceLevelLoad < 15f)
                    return 0;
                farDetailsBuilt = Households.All.Count;
                BuildFarDetails(terrain);
            }
            var pixelsPerMetre = DetailPolicy.Current.Data.PixelsPerMetre;
            var metresPerPixel = 1f / math.max(pixelsPerMetre, 1f);
            var dt = SystemAPI.Time.DeltaTime;
            var day = skyCycle != null ? skyCycle.Day : 0;
            var time = (float)(t % 10000.0);
            var up = new float3(0f, 1f, 0f);
            var parts = 0;
            int lamps = 0, boats = 0, rowed = 0, lines = 0;

            foreach (var p in farPlaces)
            {
                // The boat: moored by day and night, rowed out before dawn with a lamp in the bow, and back after sunrise.
                if (p.Boat)
                {
                    var outing = math.smoothstep(BoatOut, BoatThere, hour) * (1f - math.smoothstep(BoatBack, BoatHome, hour));
                    // (Manned only by the morning's rower, and not while that member's creature is in the world.)
                    var rower = hour > BoatOut && hour < BoatHome ? Rower(p, day, t, FarDetailLog) : -1;
                    var manned = rower >= 0 && p.Plan.Residents[rower].LiveScan != FarFolk.Scan;
                    var heading = math.normalizesafe(p.Out.xz - p.Moor.xz, new float2(0f, 1f));
                    // (Lying out it turns slowly on the water; under way it points where it is going.)
                    var drift = FarFolk.Hash01(p.Seed, 43) * 6.28f + (outing >= 0.999f ? time * 0.05f : 0f);
                    if (outing <= 0.001f || outing >= 0.999f)
                        heading = new float2(math.sin(drift), math.cos(drift));
                    else if (hour > BoatBack)
                        heading = -heading;
                    var at = math.lerp(p.Moor, p.Out, outing) + up * (0.03f * math.sin(time * 1.3f + drift));
                    var distance = math.distance(cam, at);
                    var pixels = DetailPolicy.ScreenSize(3.6f, distance, pixelsPerMetre);
                    var faded = 1f - math.saturate(pixels / (FarDetailPixels * 0.5f) - 1f);
                    if (faded < 0.99f)
                    {
                        var forward = new float3(heading.x, 0f, heading.y);
                        var hull = new float4(0.3f, 0.2f, 0.12f, 0f);
                        farDetailParts.Add(Part(at - forward * 1.8f + up * 0.12f, quaternion.LookRotationSafe(forward, up), 3.6f, 0.42f, 0.3f, 0.55f, hull, faded));
                        if (manned)
                        {
                            // The rower sits amidships, leaning to the oars while under way.
                            var lean = outing > 0.001f && outing < 0.999f ? 0.25f * math.sin(time * 2.2f) : 0f;
                            var spine = math.normalize(up + forward * lean);
                            var who = p.Plan.Residents[rower];
                            farDetailParts.Add(Part(at + up * 0.3f, quaternion.LookRotationSafe(spine, forward), 0.5f, 0.17f, 0.19f, 1f, who.Body, faded));
                            farDetailParts.Add(Part(at + up * 0.3f + spine * 0.72f, quaternion.LookRotationSafe(spine, forward), 0.06f, 0.115f, 0.115f, 1f, who.Limbs, faded));
                            FarLight(at + forward * 1.5f + up * 0.75f, 0.09f, distance, metresPerPixel,
                                     1f - math.saturate(pixels / (FarDetailPixels * 0.25f) - 1f));
                            lamps++;
                        }
                        boats++;
                        rowed += manned ? 1 : 0;
                    }
                }

            }

            // Washing: out on about half the days, three or four cloths to a line, stirring a little.
            var hanging = Hours(hour, WashFrom, WashTo);
            if (hanging > 0f)
                foreach (var line in farLines)
                {
                    if (FarFolk.Hash01(BiomeSampling.Scramble(line.Seed + (uint)day * 0x85EBCA6Bu), 33) >= 0.55f)
                        continue;
                    var distance = math.distance(cam, line.A);
                    var pixels = DetailPolicy.ScreenSize(0.9f, distance, pixelsPerMetre);
                    var faded = 1f - hanging * math.saturate(pixels / (FarDetailPixels * 0.5f) - 1f);
                    if (faded >= 0.99f)
                        continue;
                    lines++;
                    var a = line.A + up * 1.7f;
                    var b = line.B + up * 1.7f;
                    var across = new float3(line.Across.x, 0f, line.Across.y);
                    if (pixels >= FarDetailNearPixels)
                    {
                        var upright = quaternion.LookRotationSafe(up, across);
                        var wood = new float4(0.28f, 0.2f, 0.13f, 0f);
                        farDetailParts.Add(Part(line.A, upright, 1.72f, 0.045f, 0.04f, 1f, wood, faded));
                        farDetailParts.Add(Part(line.B, upright, 1.72f, 0.045f, 0.04f, 1f, wood, faded));
                        farDetailParts.Add(Part(a, quaternion.LookRotationSafe(math.normalize(b - a), up), math.distance(a, b), 0.012f, 0.012f, 1f,
                                                new float4(0.6f, 0.58f, 0.5f, 0f), faded));
                    }
                    var count = 3 + (FarFolk.Hash01(line.Seed, 34) < 0.5f ? 1 : 0);
                    for (int c = 0; c < count; c++)
                    {
                        var u = FarFolk.Hash01(line.Seed, 50 + c);
                        var shade = FarFolk.Hash01(line.Seed, 60 + c);
                        var colour = shade < 0.45f ? new float4(0.82f, 0.8f, 0.74f, 0f)
                                   : shade < 0.65f ? new float4(0.55f, 0.2f, 0.16f, 0f)
                                   : shade < 0.85f ? new float4(0.22f, 0.32f, 0.5f, 0f)
                                   : new float4(0.62f, 0.5f, 0.2f, 0f);
                        var sway = 0.12f * math.sin(time * 1.1f + u * 6.28f) + 0.05f * math.sin(time * 2.7f + c);
                        var hang = math.normalize(-up + across * sway);
                        var from = math.lerp(a, b, (c + 0.5f) / count) - up * 0.24f;
                        farDetailParts.Add(Part(from, quaternion.LookRotationSafe(hang, across), 0.45f + 0.45f * u, 0.24f, 0.26f, 0.12f, colour, faded));
                    }
                }

            if (FarDetailLog && UnityEngine.Time.time >= farDetailLogNext)
            {
                farDetailLogNext = UnityEngine.Time.time + 5f;
                Debug.Log($"[fardetails] day {day} hour {hour:F2}: {boats} boats ({rowed} rowed, {lamps} lamps), {lines} washing lines drawn, {parts + farDetailParts.Count} capsules");
            }
            return parts + farDetailParts.Count;
        }

        /// <summary>Writes the gathered details (the walking figures are written with the far figures).</summary>
        int WriteFarDetails(NativeArray<SegmentInstance> output, int at)
        {
            foreach (var part in farDetailParts)
                output[at++] = part;
            return at;
        }
    }
}
