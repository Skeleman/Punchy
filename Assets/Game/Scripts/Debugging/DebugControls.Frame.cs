using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, "-frame": the camera placed by its subject rather than by coordinates (T-1 row 2).
    public partial class DebugControls
    {
        // "-frame <target>" picks what to frame:
        //   x,z or x,y,z        a world point
        //   #12                 landmark instance 12
        //   CaveEntrance        the nearest landmark of that type (exact name first, then a name containing it)
        //   colossus            the dunes colossus
        //   flora:Saguaro       the nearest dense stand of a plant species (buildings too: flora:Inn)
        //   clearing            the nearest open glade ringed by trees (a ground shot stands inside it, looking across)
        // A landmark with a cave (CaveEntrance) is framed at its cave's mouth, from outside.
        // Any target takes "@x,z" to measure "nearest" from there instead of the player (flora:Saguaro@-1200,300).
        // "-framing close|mid|aerial|ground" (mid by default) sets the distance and pitch; "-from water" keeps the lens over
        // water. The player is moved to the subject so its kit boxes and plants come in, then 12 headings x 3 distances are
        // scored by how much of the subject five rays (centre, top, left, right, base) can see past the terrain, the kit
        // boxes and the tree trunks and crowns. The best one wins (ties go to the preferred side: a cliff's open face), the
        // lens is lifted out of the ground, water and any box, and the player is parked under or behind it. The shot tool's
        // ready gate holds while this runs.

        string frameTarget;
        string framePreset = "mid";
        bool frameFromWater;
        int framePhase;
        float frameUntil;
        float frameMovedAt;
        float3 frameAim;
        float frameSize;
        float2 framePrefer;      // the preferred side to shoot from (zero: none)
        string frameLabel;
        bool frameWantKit;       // a building or landmark: wait a little longer for its kit boxes
        bool frameInside;        // a clearing: a ground shot stands inside the subject, not outside it

        /// <summary>Called from the option loop: "-frame" with its preset and "-from" read from the same line.</summary>
        void FrameFlag(string[] args, int i)
        {
            if (args[i] != "-frame")
                return;
            frameTarget = args[i + 1];
            framePreset = "mid";
            frameFromWater = false;
            for (int k = 0; k < args.Length - 1; k++)
            {
                if (args[k] == "-framing")
                    framePreset = args[k + 1].ToLowerInvariant();
                if (args[k] == "-from" && args[k + 1].ToLowerInvariant() == "water")
                    frameFromWater = true;
            }
            framePhase = 0;
            frameUntil = Time.unscaledTime + 30f;
            frameOwner = this;
            ShotTool.ExtraWait ??= () => FrameBusy;
        }

        static string FrameBusy => frameOwner != null && frameOwner.frameTarget != null ? "framing" : null;
        static DebugControls frameOwner;

        /// <summary>Runs a pending "-frame" a step a frame: find the subject, wait for it to load, then place the camera.</summary>
        void FrameUpdate()
        {
            if (frameTarget == null)
                return;
            frameOwner = this;
            if (Time.unscaledTime > frameUntil)
            {
                Debug.LogWarning($"[frame] FAIL '{frameTarget}': {(framePhase == 0 ? "subject not found" : "timed out")}");
                frameTarget = null;
                return;
            }
            if (!EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            if (framePhase == 0)
            {
                var from = em.GetComponentData<LocalTransform>(player).Position.xz;
                var spec = frameTarget;
                var at = spec.IndexOf('@');
                if (at >= 0)
                {
                    if (ParseFloats(spec.Substring(at + 1)) is float[] o && o.Length >= 2)
                        from = new float2(o[0], o[1]);
                    spec = spec.Substring(0, at);
                }
                var found = FrameResolve(source, spec, from);
                if (found == null)
                    return;   // (waits: the flora lookup may not be up yet; a bad name times out)
                if (found == "")
                {
                    frameTarget = null;
                    return;
                }
                frameLabel = found;
                // To the subject (a little to the preferred side), so its kit boxes and plants are placed.
                var t = em.GetComponentData<LocalTransform>(player);
                var stand = frameAim.xz + framePrefer * frameSize * 0.6f;
                t.Position = new float3(stand.x, source.FloorUnder(new float3(stand.x, frameAim.y + 2f, stand.y), out _) + 0.1f, stand.y);
                em.SetComponentData(player, t);
                frameMovedAt = Time.unscaledTime;
                framePhase = 1;
                return;
            }
            if (framePhase == 1)
            {
                // Wait for the kit boxes round the subject, or (no kit there) for the plants round it, at least a second.
                using var kitQuery = em.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                var kitIn = kitQuery.TryGetSingletonBuffer<KitBox>(out var kit, true) && KitCollision.AnyNear(kit, frameAim.xz, frameSize + 5f);
                var flora = FindAnyObjectByType<FloraSystem>();
                var plantsIn = flora == null || flora.ShotReady(new Vector3(frameAim.x, frameAim.y, frameAim.z), out _);
                var waited = Time.unscaledTime - frameMovedAt;
                if (!(kitIn && waited > 0.5f || plantsIn && waited > (frameWantKit ? 3f : 1f) || waited > 6f))
                    return;
                FramePlace(source, em, player, kitIn ? kit : default, kitIn);
                frameTarget = null;
            }
        }

        /// <summary>
        /// Sets frameAim, frameSize and framePrefer for a target. Returns a label, null to try again next frame, or "" when it
        /// can't be found.
        /// </summary>
        string FrameResolve(in TerrainSource source, string spec, float2 from)
        {
            framePrefer = float2.zero;
            frameWantKit = false;
            frameInside = false;
            if (string.Equals(spec, "clearing", System.StringComparison.OrdinalIgnoreCase))
                return FrameClearing(source, from);
            if (ParseFloats(spec) is float[] p && p.Length >= 2)
            {
                var xz = new float2(p[0], p[^1]);
                var y = p.Length >= 3 ? source.FloorUnder(new float3(xz.x, p[1] + 1f, xz.y), out _) : source.SampleHeight(xz);
                frameAim = new float3(xz.x, y, xz.y);
                frameSize = 20f;
                return $"point {V(xz.x)},{V(xz.y)}";
            }
            if (string.Equals(spec, "colossus", System.StringComparison.OrdinalIgnoreCase))
            {
                var c = DunesColossus.Position;
                if (math.all(c == float3.zero))
                    return null;
                frameAim = c;
                frameSize = 30f;
                return "colossus";
            }
            if (spec.StartsWith("flora:", System.StringComparison.OrdinalIgnoreCase))
                return FrameStand(source, spec.Substring(6), from);
            // A landmark: "#n", or the nearest of a type.
            if (!source.Landmarks.IsCreated)
                return null;
            ref var set = ref source.Landmarks.Value;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var best = -1;
            if (spec.StartsWith("#") && int.TryParse(spec.Substring(1), out var id))
                best = id >= 0 && id < set.Instances.Length ? id : -1;
            else
                for (int pass = 0; pass < 2 && best < 0; pass++)
                {
                    var bestDistance = float.MaxValue;
                    for (int i = 0; i < set.Instances.Length; i++)
                    {
                        var type = set.Instances[i].Type < palette.Landmarks.Length ? palette.Landmarks[set.Instances[i].Type] : null;
                        if (type == null)
                            continue;
                        var match = pass == 0 ? string.Equals(type.name, spec, System.StringComparison.OrdinalIgnoreCase)
                                              : type.name.IndexOf(spec, System.StringComparison.OrdinalIgnoreCase) >= 0;
                        var d = math.distance(set.Instances[i].Position, from);
                        if (match && d < bestDistance)
                        {
                            best = i;
                            bestDistance = d;
                        }
                    }
                }
            if (best < 0)
            {
                // (Maybe a plant species named without "flora:".)
                var director = FindAnyObjectByType<FaunaDirector>();
                if (director != null)
                    foreach (var s in director.FloraSpeciesList)
                        if (s != null && string.Equals(s.name, spec, System.StringComparison.OrdinalIgnoreCase))
                            return FrameStand(source, spec, from);
                Debug.LogWarning($"[frame] FAIL no landmark or species '{spec}'");
                return "";
            }
            ref var inst = ref set.Instances[best];
            frameSize = math.max(set.Types[inst.Type].Radius * inst.Scale, 6f);
            frameAim = new float3(inst.Position.x, source.SampleHeight(inst.Position), inst.Position.y);
            frameWantKit = true;
            if (set.Types[inst.Type].Shape == LandmarkShape.Cliff)
                framePrefer = -inst.Facing;
            // A cave's landmark: its mouth, seen from the side it opens to (out from the passage's first nodes).
            if (source.Caves.IsCreated)
            {
                ref var caves = ref source.Caves.Value;
                var mouth = -1;
                var mouthDistance = math.max(frameSize * 1.5f, 40f);
                for (int c = 0; c < caves.Caves.Length; c++)
                {
                    var d = math.distance(caves.Caves[c].Mouth.xz, inst.Position);
                    if (caves.Caves[c].Tunnel == 0 && caves.Caves[c].NodeCount > 1 && d < mouthDistance)
                    {
                        mouth = c;
                        mouthDistance = d;
                    }
                }
                if (mouth >= 0)
                {
                    ref var cave = ref caves.Caves[mouth];
                    var inner = caves.Nodes[cave.FirstNode + math.min(2, cave.NodeCount - 1)];
                    var opening = caves.Nodes[cave.FirstNode].Radius * cave.Profile.x;
                    framePrefer = math.normalizesafe(cave.Mouth.xz - inner.Position.xz, framePrefer);
                    frameAim = cave.Mouth;
                    frameSize = math.clamp(opening * 2.5f, 6f, 20f);
                    return $"landmark #{best} {palette.Landmarks[inst.Type].name}, cave {mouth} mouth (opening {V(opening)} m)";
                }
            }
            return $"landmark #{best} {palette.Landmarks[inst.Type].name}";
        }

        /// <summary>
        /// "flora:Species": every one placed within reach (landmark props, town plots, and the biome's scatter, from a
        /// Burst scan of each layer it grows in), binned in 32 m squares; the nearest square holding at least 60% of the
        /// fullest one's count is the stand. Its middle is the aim, its spread (or the plant's height) the size.
        /// </summary>
        string FrameStand(in TerrainSource source, string name, float2 from)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated)
                return null;
            var list = director.FloraSpeciesList;
            var index = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.Equals(list[i].name, name, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index < 0)
            {
                Debug.LogWarning($"[frame] FAIL no plant species '{name}'");
                return "";
            }
            var blob = director.FloraPlacementBlob;
            ref var flora = ref blob.Value;
            var first = flora.Rules[index].FirstModel;
            var last = first + flora.Rules[index].Variants;
            var found = new List<(float3 at, float height, float2 front, string where)>();
            var scatter = 0;
            if (source.Landmarks.IsCreated)
            {
                ref var set = ref source.Landmarks.Value;
                for (int n = 0; n < set.Instances.Length; n++)
                {
                    var inst = set.Instances[n];
                    if (inst.Type >= flora.LandmarkTypeCount)
                        continue;
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    for (int l = 0; l < 3; l++)
                    {
                        var range = flora.PropRanges[inst.Type * 3 + l];
                        for (int e = range.x; e < range.x + range.y; e++)
                        {
                            var prop = flora.Props[e];
                            if (prop.Species != index)
                                continue;
                            if (prop.Count == 0 && prop.Centrepiece == 0)
                            {
                                scatter |= 1 << l;
                                continue;
                            }
                            for (int k = 0; k < math.max(prop.Count, 1); k++)
                            {
                                var spot = prop.Count == 0 ? inst.Position
                                    : LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), radius);
                                TryProp(ref flora, source, (int2)math.floor(spot / flora.CellSizes[l]), l, first, last, inst.Position - spot, "landmark", found);
                            }
                        }
                    }
                }
            }
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int k = 0; k < towns.Plots.Length; k++)
                {
                    var plot = towns.Plots[k];
                    if (plot.Building < 0 || plot.Building >= flora.SettlementSpecies.Length || flora.SettlementSpecies[plot.Building].x != index)
                        continue;
                    var layer = flora.SettlementSpecies[plot.Building].y;
                    TryProp(ref flora, source, (int2)math.floor(plot.Centre / flora.CellSizes[layer]), layer, first, last, plot.Facing, "town", found);
                }
            }
            for (int e = 0; e < flora.Entries.Length; e++)
                if (flora.Entries[e].Species == index)
                    for (int r = 0; r < flora.Ranges.Length; r++)
                        if (e >= flora.Ranges[r].x && e < flora.Ranges[r].x + flora.Ranges[r].y)
                            scatter |= 1 << (r % 3);
            // The scatter: each layer's cells out to a reach that keeps the scan near 250,000 cells.
            for (int l = 0; l < 3; l++)
            {
                if ((scatter & (1 << l)) == 0)
                    continue;
                var size = flora.CellSizes[l];
                var reach = math.min(1200f, 250f * size);
                var width = (int)math.ceil(2f * reach / size);
                var origin = (int2)math.floor((from - reach) / size);
                using var hits = new NativeArray<float4>(width * width, Allocator.TempJob);
                new StandScanJob { Flora = blob, Source = source, Layer = l, Width = width, Origin = origin, First = first, Last = last, Found = hits }
                    .Schedule(width * width, 512).Complete();   // (a debug flag, once per shot)
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i].w > 0f)
                        found.Add((hits[i].xyz, hits[i].w, new float2(1f, 0f), "scatter"));
            }
            if (found.Count == 0)
            {
                Debug.LogWarning($"[frame] FAIL no {name} within reach");
                return "";
            }
            // The stand: 32 m bins.
            var bins = new Dictionary<int2, List<int>>();
            for (int i = 0; i < found.Count; i++)
            {
                var b = (int2)math.floor(found[i].at.xz / 32f);
                if (!bins.TryGetValue(b, out var members))
                    bins[b] = members = new List<int>();
                members.Add(i);
            }
            var most = 0;
            foreach (var kv in bins)
                most = math.max(most, kv.Value.Count);
            List<int> stand = null;
            var nearest = float.MaxValue;
            foreach (var kv in bins)
            {
                if (kv.Value.Count < math.max(1, (int)math.ceil(most * 0.6f)))
                    continue;
                var d = math.distance(((float2)kv.Key + 0.5f) * 32f, from);
                if (d < nearest)
                {
                    nearest = d;
                    stand = kv.Value;
                }
            }
            float3 sum = 0f;
            float tallest = 0f;
            foreach (var i in stand)
            {
                sum += found[i].at;
                tallest = math.max(tallest, found[i].height);
            }
            var middle = sum / stand.Count;
            // The plant nearest the middle is the aim (a stand's middle can be bare ground between them).
            var aimAt = found[stand[0]];
            foreach (var i in stand)
                if (math.distance(found[i].at.xz, middle.xz) < math.distance(aimAt.at.xz, middle.xz))
                    aimAt = found[i];
            var spread = 0f;
            foreach (var i in stand)
                spread = math.max(spread, math.distance(found[i].at.xz, aimAt.at.xz));
            frameSize = math.max(tallest, stand.Count > 1 ? math.min(spread, 24f) : 0f);
            frameSize = math.max(frameSize, 3f);
            frameAim = aimAt.at + new float3(0f, aimAt.height * 0.45f, 0f);
            if (aimAt.where == "town" || aimAt.where == "landmark")
            {
                framePrefer = aimAt.front;
                frameWantKit = true;
                frameSize = math.max(frameSize, 8f);   // (a building's height undersells its footprint)
            }
            return $"{list[index].name} stand of {stand.Count} ({found.Count} found, densest bin {most}) at {V(aimAt.at.x)},{V(aimAt.at.z)}, {aimAt.where}";
        }

        /// <summary>
        /// "clearing": the trees (layer 2, over 4 m) within 160 m, from the same Burst scan as a stand; then a 6 m grid
        /// out to 110 m: dry, gentle spots with no tree within 14 m, scored by the trees ringing them within 45 m. The
        /// best one's middle is the aim, its open radius (to the nearest tree) the size.
        /// </summary>
        string FrameClearing(in TerrainSource source, float2 from)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated)
                return null;
            var blob = director.FloraPlacementBlob;
            var size = blob.Value.CellSizes[2];
            const float reach = 160f;
            var width = (int)math.ceil(2f * reach / size);
            var origin = (int2)math.floor((from - reach) / size);
            var trees = new List<float2>();
            using (var hits = new NativeArray<float4>(width * width, Allocator.TempJob))
            {
                new StandScanJob { Flora = blob, Source = source, Layer = 2, Width = width, Origin = origin, First = 0, Last = blob.Value.Models.Length, Found = hits }
                    .Schedule(width * width, 512).Complete();   // (a debug flag, once per shot)
                foreach (var h in hits)
                    if (h.w > 4f)
                        trees.Add(h.xz);
            }
            float bestScore = -1f, bestOpen = 0f;
            var bestAt = from;
            var bestRing = 0;
            for (var x = -110f; x <= 110f; x += 6f)
                for (var z = -110f; z <= 110f; z += 6f)
                {
                    var at = from + new float2(x, z);
                    var ground = source.SampleGround(at);
                    if (ground.HasWater && ground.WaterDepth > -0.2f)
                        continue;
                    var h0 = source.SampleHeight(at);
                    var steep = false;
                    for (int k = 0; k < 4; k++)
                    {
                        var o = k == 0 ? new float2(8f, 0f) : k == 1 ? new float2(-8f, 0f) : k == 2 ? new float2(0f, 8f) : new float2(0f, -8f);
                        steep |= math.abs(source.SampleHeight(at + o) - h0) > 3f;
                    }
                    if (steep)
                        continue;
                    var open = float.MaxValue;
                    var ring = 0;
                    foreach (var t in trees)
                    {
                        var d = math.distance(t, at);
                        open = math.min(open, d);
                        if (d < 45f)
                            ring++;
                    }
                    if (open < 14f)
                        continue;
                    var score = ring - 0.02f * math.length(new float2(x, z));   // (ties to the nearest)
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAt = at;
                        bestOpen = open;
                        bestRing = ring;
                    }
                }
            if (bestScore < 0f || bestRing < 6)
            {
                Debug.LogWarning($"[frame] FAIL no clearing ringed by trees near {V(from.x)},{V(from.y)} ({trees.Count} trees)");
                return "";
            }
            frameAim = new float3(bestAt.x, source.SampleHeight(bestAt) + 1.5f, bestAt.y);
            frameSize = math.min(bestOpen, 30f);
            frameInside = true;
            return $"clearing at {V(bestAt.x)},{V(bestAt.y)}, open {V(bestOpen)} m, {bestRing} trees within 45 m";
        }

        [BurstCompile]
        struct StandScanJob : IJobParallelFor
        {
            [ReadOnly] public BlobAssetReference<FloraBlob> Flora;
            [ReadOnly] public TerrainSource Source;
            public int Layer, Width, First, Last;
            public int2 Origin;
            [WriteOnly] public NativeArray<float4> Found;

            public void Execute(int i)
            {
                var plant = FloraPlacement.Place(ref Flora.Value, Source, Origin + new int2(i % Width, i / Width), Layer);
                Found[i] = plant.Model >= First && plant.Model < Last
                    ? new float4(plant.Position, Flora.Value.Models[plant.Model].Height * plant.Scale)
                    : new float4(0f, 0f, 0f, -1f);
            }
        }

        /// <summary>Scores every heading and distance, poses the camera at the best and parks the player out of shot.</summary>
        void FramePlace(in TerrainSource source, EntityManager em, Entity player, DynamicBuffer<KitBox> kit, bool hasKit)
        {
            float distance, pitch;
            switch (framePreset)
            {
                case "close": distance = math.max(1.6f * frameSize, 6f); pitch = 12f; break;
                case "aerial": distance = math.max(4f * frameSize, 40f); pitch = 45f; break;
                case "ground": case "ground-eye": distance = frameInside ? 0.8f * frameSize : math.max(2f * frameSize, 15f); pitch = 0f; break;
                default: framePreset = "mid"; distance = math.max(3f * frameSize, 25f); pitch = 22f; break;
            }
            var ground = framePreset.StartsWith("ground");
            var aim = frameAim;
            if (ground)
                aim.y = math.max(aim.y, source.SampleHeight(aim.xz) + math.min(frameSize * 0.25f, 3f));
            var blob = FindAnyObjectByType<FaunaDirector>() is FaunaDirector director && director.FloraPlacementBlob.IsCreated
                ? director.FloraPlacementBlob : default;
            var cells = new Dictionary<int3, FloraSystem.FloraInstance>();
            var factors = frameInside && framePreset.StartsWith("ground") ? new[] { 1f, 0.7f } : new[] { 1f, 0.7f, 1.4f };
            var prefer = math.lengthsq(framePrefer) > 1e-4f ? math.normalize(framePrefer) : float2.zero;
            var startAngle = math.lengthsq(prefer) > 0f ? math.atan2(prefer.y, prefer.x) : 0f;
            float bestScore = float.MaxValue, bestHidden = 1f;
            float3 bestEye = default;
            var bestHeading = 0f;
            var bestFactor = 1f;
            var tried = 0;
            for (int h = 0; h < 12; h++)
            {
                var angle = startAngle + h * math.PI / 6f;
                var side = new float2(math.cos(angle), math.sin(angle));
                foreach (var f in factors)
                {
                    var d = distance * f;
                    var eye = new float3(aim.x + side.x * d * math.cos(math.radians(pitch)), aim.y + d * math.sin(math.radians(pitch)), aim.z + side.y * d * math.cos(math.radians(pitch)));
                    var floor = source.FloorUnder(eye + new float3(0f, 2f, 0f), out _);
                    eye.y = ground ? floor + 1.7f : math.max(eye.y, floor + 1.2f);
                    var water = source.SampleGround(eye.xz);
                    if (water.HasWater)
                        eye.y = math.max(eye.y, water.WaterSurface + (ground ? 1.7f : 1f));
                    if (hasKit)
                        eye = LiftOutOfBoxes(kit, eye);
                    var hidden = FrameHidden(source, kit, hasKit, blob, cells, eye, aim, frameSize);
                    var score = hidden + (f == 1f ? 0f : 0.05f);
                    if (math.lengthsq(prefer) > 0f)
                        score += 0.1f * math.min(h, 12 - h) / 6f;
                    if (frameFromWater && !(water.HasWater && water.WaterDepth > 0.5f))
                        score += 1f;
                    tried++;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestHidden = hidden;
                        bestEye = eye;
                        bestHeading = angle;
                        bestFactor = f;
                    }
                }
            }
            var lens = (Vector3)bestEye;
            var look = Quaternion.LookRotation((Vector3)aim - lens);
            PoseCamera(lens, look);
            // The player: under the lens (out of a downward shot), or two metres behind it for a ground-eye shot.
            var back = new float2(math.cos(bestHeading), math.sin(bestHeading));
            var park = bestEye.xz + (ground ? back * 2f : float2.zero);
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(park.x, source.FloorUnder(new float3(park.x, bestEye.y, park.y), out _) + 0.1f, park.y);
            em.SetComponentData(player, t);
            var compass = (math.degrees(math.atan2(-back.x, -back.y)) + 360f) % 360f;   // the direction the camera looks, 0 = +z
            Debug.Log(System.FormattableString.Invariant(
                $"[frame] '{frameTarget}' {frameLabel}, aim ({aim.x:F1}, {aim.y:F1}, {aim.z:F1}) size {frameSize:F0} m, {framePreset}{(frameFromWater ? " from water" : "")}: ") +
                System.FormattableString.Invariant($"looking {compass:F0} deg from {math.distance(bestEye, aim):F0} m (x{bestFactor:F1}), hidden {bestHidden:F2} (best of {tried}), lens ({lens.x:F1}, {lens.y:F1}, {lens.z:F1}){(hasKit ? "" : ", no kit boxes")}"));
        }

        /// <summary>The share of five rays (centre, top, left, right, base of the subject) that something blocks.</summary>
        static float FrameHidden(in TerrainSource source, DynamicBuffer<KitBox> kit, bool hasKit, BlobAssetReference<FloraBlob> blob,
                                 Dictionary<int3, FloraSystem.FloraInstance> cells, float3 eye, float3 aim, float size)
        {
            var toward = math.normalizesafe(new float3(aim.x - eye.x, 0f, aim.z - eye.z), new float3(1f, 0f, 0f));
            var right = new float3(toward.z, 0f, -toward.x);
            var baseY = math.max(aim.y - size * 0.3f, source.SampleHeight(aim.xz) + 0.3f);
            var points = new[]
            {
                aim, aim + new float3(0f, size * 0.4f, 0f), aim + right * size * 0.4f, aim - right * size * 0.4f,
                new float3(aim.x, baseY, aim.z)
            };
            var blocked = 0;
            foreach (var p in points)
            {
                var length = math.distance(eye, p);
                var stop = math.min(size * 0.45f, length * 0.8f);
                var end = eye + (p - eye) * ((length - stop) / math.max(length, 1e-3f));
                if (TerrainBlocks(source, eye, end) || hasKit && KitCollision.Blocks(kit, eye, end) || blob.IsCreated && PlantsBlock(ref blob.Value, source, cells, eye, end))
                    blocked++;
            }
            return blocked / 5f;
        }

        /// <summary>Whether the line passes under the ground (a cave's open air doesn't count), sampled about every 1.5 m.</summary>
        static bool TerrainBlocks(in TerrainSource source, float3 a, float3 b)
        {
            var length = math.distance(a, b);
            var steps = (int)math.clamp(length / 1.5f, 2f, 200f);
            for (int s = 1; s < steps; s++)
            {
                var q = math.lerp(a, b, s / (float)steps);
                if (q.y < source.SampleHeight(q.xz) - 0.2f)
                {
                    source.FloorUnder(q, out var inCave);
                    if (!inCave)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the line passes through a plant's trunk or crown (layers 1 and 2, anything over 2.5 m): the trunk a
        /// cylinder, the crown a cylinder of 80% its spread from the crown base to the top. Sampled about every metre.
        /// </summary>
        static bool PlantsBlock(ref FloraBlob flora, in TerrainSource source, Dictionary<int3, FloraSystem.FloraInstance> cells, float3 a, float3 b)
        {
            var length = math.distance(a, b);
            var steps = (int)math.clamp(length, 2f, 400f);
            for (int s = 1; s < steps; s++)
            {
                var q = math.lerp(a, b, s / (float)steps);
                for (int l = 1; l < 3; l++)
                {
                    var size = flora.CellSizes[l];
                    var c = (int2)math.floor(q.xz / size);
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var key = new int3(c.x + dx, c.y + dz, l);
                        if (!cells.TryGetValue(key, out var plant))
                            cells[key] = plant = FloraPlacement.Place(ref flora, source, key.xy, l);
                        if (plant.Model < 0)
                            continue;
                        ref var shape = ref flora.Models[plant.Model];
                        var height = shape.Height * plant.Scale;
                        if (height < 2.5f)
                            continue;
                        var up = q.y - plant.Position.y;
                        if (up < 0f || up > height)
                            continue;
                        var radius = up >= shape.CrownBase * plant.Scale ? math.max(shape.CrownRadius * 0.8f, shape.TrunkRadius) * plant.Scale : shape.TrunkRadius * plant.Scale;
                        if (math.distancesq(q.xz, plant.Position.xz) < radius * radius)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>A lens inside a kit box (a wall, a roof) is lifted to just over that box, a few times over.</summary>
        static float3 LiftOutOfBoxes(DynamicBuffer<KitBox> kit, float3 eye)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var moved = false;
                for (int i = 0; i < kit.Length; i++)
                {
                    var box = kit[i];
                    if (math.distancesq(box.Centre, eye) > math.square(box.Reach + 0.3f))
                        continue;
                    var local = math.mul(math.inverse(box.Rotation), eye - box.Centre);
                    if (math.all(math.abs(local) <= box.Half + 0.3f))
                    {
                        eye.y = box.Centre.y + box.Reach + 0.4f;
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }
            return eye;
        }
    }
}
