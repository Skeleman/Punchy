using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, -place spots: caves, temples, streams, falls, trees, props, crossings, meadows, lakes and ledges. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        /// <summary>
        /// "-place cave:Ice" (any cave style): in the first cave of that style, on the floor of its first chamber,
        /// facing back along the passage.
        /// </summary>
        void PlaceInCave(in TerrainSource source, string styleName)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            var style = styles.FindIndex(s => s != null && string.Equals(s.name, styleName, System.StringComparison.OrdinalIgnoreCase));
            if (style < 0)
            {
                Debug.LogWarning($"DebugControls: no cave style '{styleName}' ({string.Join(", ", styles.ConvertAll(s => s != null ? s.name : "-"))})");
                return;
            }
            ref var set = ref source.Caves.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                if (set.Caves[c].Style != style + 1)
                    continue;
                ref var cave = ref set.Caves[c];
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var node = set.Nodes[n].Position;
                    var floor = source.FloorUnder(node + new float3(0f, 0.5f, 0f), out _);
                    var back = n > cave.FirstNode ? set.Nodes[n - 1].Position - node : new float3(0f, 0f, 1f);
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(player);
                    t.Position = new float3(node.x, floor + 0.05f, node.z);
                    t.Rotation = quaternion.RotateY(math.atan2(back.x, back.z));
                    em.SetComponentData(player, t);
                    Debug.Log($"DebugControls: in a {styleName} cave ({c}), chamber at ({node.x:F0}, {floor:F0}, {node.z:F0})");
                    return;
                }
            }
            Debug.LogWarning($"DebugControls: no {styleName} cave with a chamber");
        }

        /// <summary>
        /// A-67/A-69 "-place batfolk": on the floor of the first bat folk chamber with a pod (BatFolkHomes, W-96), a few
        /// metres back from the pod's door toward the chamber's middle, facing the pod.
        /// </summary>
        bool PlaceAtBatFolk(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return false;
            ref var bats = ref source.BatFolk.Value;
            for (int i = 0; i < bats.Sites.Length; i++)
            {
                if (BatFolkHomes.Count(source, BatFolkHomeKind.BatFolkPod, i) == 0 || !BatFolkHomes.Get(source, BatFolkHomeKind.BatFolkPod, i, 0, out var pod))
                    continue;
                var site = bats.Sites[i];
                var at = pod.Door;
                // Back toward the chamber's middle, so the pod and its keeper are in view.
                var back = math.normalizesafe(site.Centre.xz - at.xz, new float2(0f, 1f)) * math.min(4f, math.max(1.5f, site.Radius * 0.5f));
                var stand = new float3(at.x + back.x, at.y + 0.5f, at.z + back.y);
                var floor = source.FloorUnder(stand, out _);
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(player);
                t.Position = new float3(stand.x, floor + 0.05f, stand.z);
                t.Rotation = quaternion.RotateY(math.atan2(-back.x, -back.y));
                em.SetComponentData(player, t);
                Debug.Log($"DebugControls: at bat folk chamber {i}, pod door at ({at.x:F0}, {at.y:F0}, {at.z:F0}), pod floor at y {pod.Stand.y:F1}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// A-78: "-place batfolk" again whenever the island's landmarks change, until it times out. The baked first
        /// island's chambers came first, and the v3 pass replaces its caves and bat folk homes, so a placement made on the
        /// first island left the player on v3's open ground (spawning 0 cave folk). Like PlaceAtLandmarkPending (A-74).
        /// </summary>
        void PlaceAtBatFolkPending()
        {
            if (batFolkPlaceUntil <= 0f)
                return;
            if (Time.unscaledTime > batFolkPlaceUntil)
            {
                if (!batFolkPlaced)
                    Debug.LogWarning("DebugControls: no bat folk chamber with a pod");
                batFolkPlaceUntil = 0f;
                return;
            }
            // (No key change: placed on this island already. A failed try keeps the old key, to retry next frame while
            // the homes are still baking.)
            if (!EnsureLocations(out var source) || math.all(locationsKey == batFolkPlaceKey) || !PlaceAtBatFolk(source))
                return;
            batFolkPlaceKey = locationsKey;
            batFolkPlaced = true;
            rockWatchSince = -1f; // (-rockwatch counts from there.)
        }

        /// <summary>
        /// P-5/P-7 "-place temple" (or "temple:N" for room N): the player on a sunken temple room's floor. The rooms are
        /// the ruin kit's layout in the temple's own frame (RuinRooms: u, y above Base, v); without N, the biggest closed
        /// room. Its centre's floor (plus drifted sand) goes back to the world through SunkenTempleShape.World.
        /// </summary>
        void PlaceInTemple(in TerrainSource source, string spec)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player) || !source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            var temple = -1;
            for (int i = 0; i < set.Instances.Length && temple < 0; i++)
                if (set.Types[set.Instances[i].Type].Temple != 0)
                    temple = i;
            if (temple < 0 || set.RuinRooms.Length == 0)
            {
                Debug.LogWarning($"DebugControls: no sunken temple ({set.RuinRooms.Length} ruin rooms)");
                return;
            }
            var room = -1;
            var colon = spec.IndexOf(':');
            if (colon < 0 || !int.TryParse(spec.Substring(colon + 1), out room) || room < 0 || room >= set.RuinRooms.Length)
            {
                room = -1;
                var best = 0f;
                for (int i = 0; i < set.RuinRooms.Length; i++)
                {
                    var r = set.RuinRooms[i];
                    var area = (r.U1 - r.U0) * (r.V1 - r.V0);
                    if (r.Kind == 0 && area > best)
                        (best, room) = (area, i);
                }
                if (room < 0)
                    room = 0;
            }
            var rr = set.RuinRooms[room];
            float u = (rr.U0 + rr.U1) * 0.5f, v = (rr.V0 + rr.V1) * 0.5f;
            var layout = SunkenTempleShape.Layout(set.Instances[temple]);
            var floor = rr.FloorAt(u) + SunkenTempleShape.Sand(ref set, room, u, v);
            var p = SunkenTempleShape.World(layout, new float3(u, floor + 0.05f, v));
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = p;
            // (Facing along the room's long side.)
            var along = rr.U1 - rr.U0 >= rr.V1 - rr.V0 ? layout.F : layout.R;
            t.Rotation = quaternion.RotateY(math.atan2(along.x, along.y));
            em.SetComponentData(player, t);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            // P-9: the rooms touching this one, and whether each is open to the sky (kind 1: a cut through the ground).
            var touching = new System.Text.StringBuilder();
            for (int j = 0; j < set.RuinRooms.Length; j++)
            {
                var o = set.RuinRooms[j];
                float ou = math.min(rr.U1, o.U1) - math.max(rr.U0, o.U0), ov = math.min(rr.V1, o.V1) - math.max(rr.V0, o.V0);
                if (j != room && ou >= -0.25f && ov >= -0.25f && math.max(ou, ov) >= 0.5f)
                    touching.Append($" {j}{(o.Kind == 1 ? " (open to the sky)" : "")}");
            }
            Debug.Log($"DebugControls: temple room {room} touches:{touching}");
            Debug.Log($"DebugControls: in sunken temple room {room}/{set.RuinRooms.Length} ({rr.U1 - rr.U0:F0} x {rr.V1 - rr.V0:F0} m, ceiling {rr.Ceil - floor:F1} m, kind {rr.Kind}) at ({p.x:F1}, {p.y:F1}, {p.z:F1}); the ground is {source.SampleHeight(p.xz) - p.y:F1} m above");
        }

        /// <summary>
        /// "-place cavestream" ("#2" the second nearest): by the end of the nearest cave stream (where it meets its pool),
        /// the camera a few metres back up the stream looking at the end. Logs the 8 nearest streams' two ends.
        /// </summary>
        void PlaceByCaveStream(in TerrainSource source, string spec)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var pick = 1;
            if (spec.StartsWith("#"))
                int.TryParse(spec.Substring(1), out pick);
            ref var set = ref source.Caves.Value;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var ends = new List<(float distance, int stream)>();
            for (int s = 0; s < set.Streams.Length; s++)
            {
                ref var stream = ref set.Streams[s];
                if (stream.PointCount < 2)
                    continue;
                var end = set.StreamPoints[stream.FirstPoint + stream.PointCount - 1].Position;
                ends.Add((math.distance(end.xz, t.Position.xz), s));
            }
            if (ends.Count == 0)
            {
                Debug.LogWarning("DebugControls: no cave streams");
                return;
            }
            ends.Sort((p, q) => p.distance.CompareTo(q.distance));
            for (int k = 0; k < math.min(8, ends.Count); k++)
            {
                ref var stream = ref set.Streams[ends[k].stream];
                var a = set.StreamPoints[stream.FirstPoint].Position;
                var b = set.StreamPoints[stream.FirstPoint + stream.PointCount - 1].Position;
                Debug.Log($"DebugControls: cave stream {ends[k].stream} ({stream.PointCount} points) from ({a.x:F1}, {a.y:F1}, {a.z:F1}) to ({b.x:F1}, {b.y:F1}, {b.z:F1})");
            }
            ref var chosen = ref set.Streams[ends[math.clamp(pick - 1, 0, ends.Count - 1)].stream];
            var last = set.StreamPoints[chosen.FirstPoint + chosen.PointCount - 1].Position;
            // Back up the stream about 5 m (or to its start).
            var back = last;
            for (int k = chosen.PointCount - 2; k >= 0; k--)
            {
                back = set.StreamPoints[chosen.FirstPoint + k].Position;
                if (math.distance(back, last) > 5f)
                    break;
            }
            var floor = source.FloorUnder(back + new float3(0f, 0.5f, 0f), out _);
            t.Position = new float3(back.x, floor + 0.05f, back.z);
            em.SetComponentData(player, t);
            var eye = (Vector3)(back + new float3(0f, 2.5f, 0f));
            PoseCamera(eye, Quaternion.LookRotation((Vector3)last - eye));
            Debug.Log($"DebugControls: by cave stream end ({last.x:F1}, {last.y:F1}, {last.z:F1}), camera {eye.x:F1},{eye.y:F1},{eye.z:F1}");
        }

        /// <summary>
        /// "-place cavepool" ("#2" the second deepest; P-20, #22): standing in the middle of the deepest water pool in the
        /// caves, on its bed, for shots with the camera under cave water. Logs the 5 deepest water pools.
        /// </summary>
        void PlaceInCavePool(in TerrainSource source, string spec, bool lava = false)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var pick = 1;
            if (spec.StartsWith("#"))
                int.TryParse(spec.Substring(1), out pick);
            ref var set = ref source.Caves.Value;
            var pools = new List<int>();
            for (int k = 0; k < set.Pools.Length; k++)
                // ("-place cavelava": the lava pools instead, for checking how lava looks.)
                if (set.Pools[k].Lava < 0.5f != lava)
                    pools.Add(k);
            if (pools.Count == 0)
            {
                Debug.LogWarning("DebugControls: no cave water pools");
                return;
            }
            var all = set.Pools.ToArray();
            pools.Sort((a, b) => all[b].Depth.CompareTo(all[a].Depth));
            for (int k = 0; k < math.min(5, pools.Count); k++)
            {
                var pool = all[pools[k]];
                Debug.Log($"DebugControls: cave pool {pools[k]}: surface ({pool.Centre.x:F1}, {pool.Centre.y:F1}, {pool.Centre.z:F1}), radius {pool.Radius:F1} m, depth {pool.Depth:F1} m");
            }
            var chosen = all[pools[math.clamp(pick - 1, 0, pools.Count - 1)]];
            // (A lava pool: on its rim, a step past its edge, looking in, not on its bed.)
            if (lava)
                chosen.Centre += new float3(chosen.Radius + 1.5f, 1.5f, 0f);
            var floor = source.FloorUnder(chosen.Centre - new float3(0f, 0.1f, 0f), out _);
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(chosen.Centre.x, floor + 0.05f, chosen.Centre.z);
            em.SetComponentData(player, t);
            Debug.Log($"DebugControls: in cave pool at ({chosen.Centre.x:F1}, {floor:F1}, {chosen.Centre.z:F1}), {chosen.Centre.y - floor:F1} m under its surface");
        }

        /// <summary>
        /// "-place riverfall[#n]" (island v2): below the nth nearest river waterfall of 4-8 m (the small ones), the camera 12 m
        /// downstream at standing eye height looking at the middle of the fall. Logs the 8 nearest small falls.
        /// </summary>
        void PlaceByRiverFall(in TerrainSource source, string spec)
        {
            if (!source.Rivers.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var pick = 1;
            if (spec.StartsWith("#"))
                int.TryParse(spec.Substring(1), out pick);
            ref var falls = ref source.Rivers.Value.Falls;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var near = new List<(float distance, int fall)>();
            for (int f = 0; f < falls.Length; f++)
                if (falls[f].Drop < 8f)
                    near.Add((math.distance(falls[f].Lip.xz, t.Position.xz), f));
            if (near.Count == 0)
            {
                Debug.LogWarning("DebugControls: no small river falls (island v2 only)");
                return;
            }
            near.Sort((p, q) => p.distance.CompareTo(q.distance));
            for (int k = 0; k < math.min(8, near.Count); k++)
            {
                ref var f = ref falls[near[k].fall];
                Debug.Log($"DebugControls: river fall {near[k].fall} drop {f.Drop:F1} width {f.Width:F1} lip ({f.Lip.x:F1}, {f.Lip.y:F1}, {f.Lip.z:F1})");
            }
            ref var chosen = ref falls[near[math.clamp(pick - 1, 0, near.Count - 1)].fall];
            var outDir = math.normalizesafe(chosen.Out);
            var foot = chosen.Lip.xz + outDir * 1.6f;
            var stand = foot + outDir * 12f;
            var ground = source.SampleHeight(stand);
            t.Position = new float3(stand.x, ground + 0.05f, stand.y);
            em.SetComponentData(player, t);
            var eye = new Vector3(stand.x, ground + 1.7f, stand.y);
            var target = new Vector3(chosen.Lip.x, chosen.Lip.y - chosen.Drop * 0.5f, chosen.Lip.z);
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            Debug.Log($"DebugControls: below river fall (drop {chosen.Drop:F1} m) lip ({chosen.Lip.x:F1}, {chosen.Lip.y:F1}, {chosen.Lip.z:F1}), camera {eye.x:F1},{eye.y:F1},{eye.z:F1}");
        }

        /// <summary>
        /// "-place tree:Oak" (any tree species): beside the nearest tree of that species that stands alone (no other
        /// tree within three cells), the camera framing it from a little way off.
        /// </summary>
        void PlaceByTree(in TerrainSource source, string speciesName)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var list = director.FloraSpeciesList;
            var index = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.Equals(list[i].name, speciesName, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no plant species '{speciesName}'");
                return;
            }
            ref var flora = ref director.FloraPlacementBlob.Value;
            var first = flora.Rules[index].FirstModel;
            var last = first + flora.Rules[index].Variants;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var cellSize = flora.CellSizes[FloraPlacement.TreeLayer];
            var centre = (int2)math.floor(t.Position.xz / cellSize);
            // Outwards in rings, to 400 cells out.
            for (int r = 0; r < 400; r++)
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (math.max(math.abs(dx), math.abs(dz)) != r)
                        continue;
                    var cell = centre + new int2(dx, dz);
                    var plant = FloraPlacement.Place(ref flora, source, cell, FloraPlacement.TreeLayer);
                    if (plant.Model < first || plant.Model >= last)
                        continue;
                    var alone = true;
                    for (int z = -3; z <= 3 && alone; z++)
                        for (int x = -3; x <= 3 && alone; x++)
                            if ((x != 0 || z != 0) && IsTree(ref flora, source, cell + new int2(x, z)))
                                alone = false;
                    if (!alone)
                        continue;
                    var at = plant.Position;
                    var height = flora.Models[plant.Model].Height * plant.Scale;
                    mimicTree = at;
                    mimicTreeHeight = height;
                    mimicTreeSet = true;
                    t.Position = new float3(at.x + 6f, source.SampleHeight(at.xz + new float2(6f, 0f)) + 0.05f, at.z);
                    // Facing the trunk (west), so walking on climbs it.
                    t.Rotation = quaternion.RotateY(-math.PI * 0.5f);
                    em.SetComponentData(player, t);
                    var eye = (Vector3)at + new Vector3(16f, 7f, -16f);
                    PoseCamera(eye, Quaternion.LookRotation((Vector3)at + Vector3.up * height * 0.6f - eye));
                    Debug.Log($"DebugControls: by a lone {speciesName} at ({at.x:F0}, {at.z:F0}), {height:F0} m tall");
                    StartClimbTest(t.Position, at, at + new float3(0f, height * 0.3f, 0f), false, $"trunk at ({at.x:F1}, {at.y:F1}, {at.z:F1})");
                    return;
                }
            Debug.LogWarning($"DebugControls: no lone {speciesName} near");
        }

        /// <summary>A "-place prop:" spec waiting for the world, the player and the flora lookup, and until when (#137).</summary>
        static string propPending;
        static float propPendingUntil;
        const float PropWait = 120f;

        /// <summary>Whether a "-place prop:" request is still waiting (for tests).</summary>
        public static bool PropPlacePending => propPending != null;

        /// <summary>#137: retries a waiting "-place prop:" each frame until it can run, warning if it never can.</summary>
        void PlacePropPending()
        {
            if (propPending == null)
                return;
            if (Time.unscaledTime > propPendingUntil)
            {
                Debug.LogWarning($"DebugControls: -place prop:{propPending} gave up; the flora lookup never came up");
                propPending = null;
            }
            else if (EnsureLocations(out var source) && PlaceByProp(source, propPending))
                propPending = null;
        }

        /// <summary>
        /// "-place prop:Bench" (any plant species, wherever it's placed: a landmark's props, a town's buildings, or the
        /// biome's own scatter): logs the eight nearest with their coordinates and puts the player in front of the
        /// nearest, the camera framing it from a little way off. "prop:Bench#3" picks the third nearest, and
        /// "prop:Bench@115,-401" (or "prop:Bench@115,-401#2") measures from that point instead of the player. For lining
        /// up shots: the logged spot, height and front go straight into -camlook.
        /// </summary>
        /// <remarks>Returns false while the flora lookup or the player isn't up yet, so the request can wait (#137).</remarks>
        bool PlaceByProp(in TerrainSource source, string spec)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return false;
            var pick = 1;
            var hash = spec.IndexOf('#');
            if (hash >= 0)
            {
                int.TryParse(spec.Substring(hash + 1), out pick);
                spec = spec.Substring(0, hash);
            }
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var from = t.Position.xz;
            var near = spec.IndexOf('@');
            if (near >= 0)
            {
                if (ParseFloats(spec.Substring(near + 1)) is float[] p && p.Length >= 2)
                    from = new float2(p[0], p[1]);
                spec = spec.Substring(0, near);
            }
            var list = director.FloraSpeciesList;
            var index = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.Equals(list[i].name, spec, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no plant species '{spec}'");
                return true;
            }
            ref var flora = ref director.FloraPlacementBlob.Value;
            var first = flora.Rules[index].FirstModel;
            var last = first + flora.Rules[index].Variants;
            var found = new List<(float3 at, float height, float2 front, string where)>();
            // Placed in landmarks (each prop takes the cell holding its spot) or as a landmark's scatter (its layers
            // searched below, with the biome's).
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
                                TryProp(ref flora, source, (int2)math.floor(spot / flora.CellSizes[l]), l, first, last, inst.Position - spot, $"landmark {n}", found);
                            }
                        }
                    }
                }
            }
            // Town buildings: each plot's building takes the cell holding the plot's centre, facing its street.
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int k = 0; k < towns.Plots.Length; k++)
                {
                    var plot = towns.Plots[k];
                    if (plot.Building < 0 || plot.Building >= flora.SettlementSpecies.Length || flora.SettlementSpecies[plot.Building].x != index)
                        continue;
                    var layer = flora.SettlementSpecies[plot.Building].y;
                    TryProp(ref flora, source, (int2)math.floor(plot.Centre / flora.CellSizes[layer]), layer, first, last, plot.Facing, $"town {plot.Settlement}", found);
                }
            }
            // The biome's own scatter: outwards in rings from the point, in each layer it grows in, to about 1.5 km or
            // 200,000 cells, stopping once eight are found.
            for (int e = 0; e < flora.Entries.Length; e++)
                if (flora.Entries[e].Species == index)
                    for (int r = 0; r < flora.Ranges.Length; r++)
                        if (e >= flora.Ranges[r].x && e < flora.Ranges[r].x + flora.Ranges[r].y)
                            scatter |= 1 << (r % 3);
            for (int l = 0; l < 3; l++)
            {
                if ((scatter & (1 << l)) == 0)
                    continue;
                var size = flora.CellSizes[l];
                var centre = (int2)math.floor(from / size);
                var rings = (int)math.min(1500f / size, math.sqrt(200000f) * 0.5f);
                var before = found.Count;
                for (int r = 0; r <= rings && found.Count - before < 8; r++)
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (math.max(math.abs(dx), math.abs(dz)) == r)
                            TryProp(ref flora, source, centre + new int2(dx, dz), l, first, last, new float2(1f, 0f), "scatter", found);
            }
            if (found.Count == 0)
            {
                Debug.LogWarning($"DebugControls: no {spec} found");
                return true;
            }
            var origin = from;
            found.Sort((a, b) => math.distance(a.at.xz, origin).CompareTo(math.distance(b.at.xz, origin)));
            for (int k = 0; k < math.min(8, found.Count); k++)
            {
                var c = found[k];
                Debug.Log(System.FormattableString.Invariant(
                    $"DebugControls: {spec} #{k + 1} at ({c.at.x:F1}, {c.at.y:F1}, {c.at.z:F1}), {math.distance(c.at.xz, origin):F0} m off, {c.height:F1} m tall, front ({c.front.x:F2}, {c.front.y:F2}), {c.where}"));
            }
            Debug.Log($"DebugControls: {found.Count} {spec} found in all");
            pick = math.clamp(pick, 1, found.Count);
            var chosen = found[pick - 1];
            var back = math.max(6f, chosen.height * 1.6f);
            // From the front, a little to one side.
            var view = math.normalize(chosen.front + new float2(chosen.front.y, -chosen.front.x) * 0.5f);
            var stand = chosen.at.xz + view * (back + 3f);
            t.Position = new float3(stand.x, source.SampleHeight(stand) + 0.05f, stand.y);
            em.SetComponentData(player, t);
            var eyeXZ = chosen.at.xz + view * back;
            var eye = new Vector3(eyeXZ.x, math.max(source.SampleHeight(eyeXZ), chosen.at.y) + 1.5f + chosen.height * 0.4f, eyeXZ.y);
            PoseCamera(eye, Quaternion.LookRotation((Vector3)chosen.at + Vector3.up * chosen.height * 0.45f - eye));
            Debug.Log(System.FormattableString.Invariant($"DebugControls: framing {spec} #{pick} from ({eye.x:F1}, {eye.y:F1}, {eye.z:F1})"));
            return true;
        }

        /// <summary>For PlaceByProp: the plant in a cell, kept if it's one of the species' models and not already listed.</summary>
        static void TryProp(ref FloraBlob flora, in TerrainSource source, int2 cell, int layer, int first, int last, float2 front, string where,
                            List<(float3 at, float height, float2 front, string where)> found)
        {
            var plant = FloraPlacement.Place(ref flora, source, cell, layer);
            if (plant.Model < first || plant.Model >= last)
                return;
            foreach (var e in found)
                if (math.distance(e.at.xz, plant.Position.xz) < 0.5f)
                    return;
            found.Add((plant.Position, flora.Models[plant.Model].Height * plant.Scale, math.normalizesafe(front, new float2(1f, 0f)), where));
        }

        /// <summary>
        /// "-place meadow": the nearest flat, grassy stretch of Grassland, the camera at eye height looking a little
        /// down across it; the player parked out of shot.
        /// </summary>
        static float2? flatSpot;
        static readonly System.Collections.Generic.List<float2> flatSpots = new System.Collections.Generic.List<float2>();
        static readonly System.Collections.Generic.List<float> flatSpreads = new System.Collections.Generic.List<float>();
        static float flatSpread;

        /// <summary>The nearest dry spot (rings of 30 m from the origin) whose ground height, centre and two rings out to 15 m, varies under 0.5 m.</summary>
        static float2? FindFlatSpot(in TerrainSource source, out float spread, System.Collections.Generic.List<float2> awayFrom)
        {
            spread = 0f;
            for (int ring = 0; ring <= 150; ring++)
                for (int k = 0; k < math.max(ring * 8, 1); k++)
                {
                    var angle = k * 2f * math.PI / math.max(ring * 8, 1);
                    var at = new float2(math.cos(angle), math.sin(angle)) * ring * 30f;
                    var near = false;
                    foreach (var other in awayFrom)
                        near |= math.distance(other, at) < 150f;
                    if (near)
                        continue;
                    var lo = float.MaxValue;
                    var hi = float.MinValue;
                    var dry = true;
                    for (int r = 0; r < 17 && dry; r++)
                    {
                        var xz = r == 0 ? at : at + new float2(math.cos(r * math.PI / 4f), math.sin(r * math.PI / 4f)) * (r <= 8 ? 7.5f : 15f);
                        var g = source.SampleGround(xz);
                        dry = !(g.HasWater && g.WaterSurface > g.Height - 0.5f);
                        lo = math.min(lo, g.Height);
                        hi = math.max(hi, g.Height);
                    }
                    if (dry && hi - lo < 0.5f)
                    {
                        spread = hi - lo;
                        return at;
                    }
                }
            return null;
        }

        /// <summary>
        /// "-place crossing" (A-31): beside the first bridge whose seeded roll gave it a fisher (else any with one), the
        /// camera about 9 m off up the bank, looking past the fisher to the bridge and the river.
        /// </summary>
        void PlaceByCrossing(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            RoadCrossings.Refresh(source);
            var pick = RoadCrossings.List.FindIndex(c => c.Fisher && c.HasSpot && c.Bridge);
            if (pick < 0)
                pick = RoadCrossings.List.FindIndex(c => c.Fisher && c.HasSpot);
            if (pick < 0)
            {
                Debug.LogWarning("DebugControls: no crossing with a fisher");
                return;
            }
            var x = RoadCrossings.List[pick];
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            // (The player on the road a little back from the bank, out of the frame behind the camera.)
            var park = x.Bank + x.Outward * 14f;
            t.Position = new float3(park.x, source.SampleHeight(park) + 0.05f, park.y);
            em.SetComponentData(player, t);
            var away = math.normalizesafe(x.Spot - x.Centre, x.Outward);
            var eyeXZ = x.Spot + away * 8f + new float2(-away.y, away.x) * 3f;
            var spotY = source.SampleHeight(x.Spot);
            var eye = new Vector3(eyeXZ.x, math.max(source.SampleHeight(eyeXZ), spotY) + 3f, eyeXZ.y);
            var lookXZ = math.lerp(x.Spot, x.Centre, 0.35f);
            var look = new Vector3(lookXZ.x, spotY + 0.8f, lookXZ.y);
            PoseCamera(eye, Quaternion.LookRotation(look - eye));
            Debug.Log($"DebugControls: at crossing {pick} ({x.Centre.x:F1}, {x.Centre.y:F1}, {(x.Bridge ? "bridge" : "ford")}), fisher spot ({x.Spot.x:F1}, {x.Spot.y:F1})");
        }

        void PlaceInMeadow(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var start = t.Position.xz;
            for (int ring = 1; ring <= 200; ring++)
                for (int k = 0; k < ring * 8; k++)
                {
                    var angle = k * 2f * math.PI / (ring * 8);
                    var at = start + new float2(math.cos(angle), math.sin(angle)) * ring * 25f;
                    if (!IsMeadow(source, palette, at, 0.98f, 0.9f, true))
                        continue;
                    var flat = true;
                    for (int d = 0; d < 8 && flat; d++)
                    {
                        var a = d * math.PI / 4f;
                        flat = IsMeadow(source, palette, at + new float2(math.cos(a), math.sin(a)) * 15f, 0.97f, 0.7f, false);
                    }
                    if (!flat)
                        continue;
                    var height = source.SampleHeight(at);
                    // For a shot list the player is parked 30 m off and the camera posed over the grass; played by
                    // hand (item 24) the player stands in the meadow itself and the camera stays free to look round.
                    var shooting = shotIndex >= 0;
                    var park = shooting ? at - new float2(0f, 30f) : at;
                    t.Position = new float3(park.x, source.SampleHeight(park) + 0.05f, park.y);
                    em.SetComponentData(player, t);
                    if (shooting)
                        PoseCamera(new Vector3(at.x, height + 1.7f, at.y), Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f)));
                    Debug.Log($"DebugControls: in a meadow at ({at.x:F0}, {height:F0}, {at.y:F0})");
                    return;
                }
            Debug.LogWarning("DebugControls: no meadow near");
        }

        static bool IsMeadow(in TerrainSource source, BiomePalette palette, float2 at, float flatness, float grass, bool grassland)
        {
            var g = source.SampleGround(at);
            if (g.HasWater)
                return false;
            if (grassland && !palette.Biomes[g.Biomes.Dominant()].HasTag(BiomeTags.Meadow))
                return false;
            var n = source.SampleNormal(at, 1f);
            return n.y >= flatness && source.SampleSurface(at, g, n).Grass >= grass;
        }

        /// <summary>
        /// "-place clearlake": on a dry bank beside the nearest clear (not murky) lake at least 1.5 m deep, facing
        /// the water.
        /// </summary>
        void PlaceByClearLake(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var start = t.Position.xz;
            for (int ring = 1; ring <= 400; ring++)
                for (int k = 0; k < ring * 8; k++)
                {
                    var angle = k * 2f * math.PI / (ring * 8);
                    var at = start + new float2(math.cos(angle), math.sin(angle)) * ring * 12f;
                    var g = source.SampleGround(at);
                    if (!g.HasWater || g.WaterSurface - g.Height < 1.5f || g.River > 0.02f || source.WaterLook(at, g.Biomes, out _).w > 0.05f)
                        continue;
                    for (int d = 0; d < 16; d++)
                    {
                        var a = d * math.PI / 8f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var stand = at + dir * 8f;
                        var s = source.SampleGround(stand);
                        if (s.HasWater && s.WaterSurface > s.Height - 0.1f)
                            continue;
                        if (s.Height - g.WaterSurface > 4f)
                            continue;
                        t.Position = new float3(stand.x, s.Height + 0.05f, stand.y);
                        t.Rotation = quaternion.RotateY(math.atan2(-dir.x, -dir.y));
                        em.SetComponentData(player, t);
                        lastLake = at;
                        Debug.Log($"DebugControls: by a clear lake at ({at.x:F0}, {at.y:F0}), {g.WaterSurface - g.Height:F1} m deep");
                        return;
                    }
                }
            Debug.LogWarning("DebugControls: no clear lake near");
        }

        /// <summary>
        /// "-place ledge" (after the jump to the nearest settlement, once the props' tops round the player have loaded):
        /// on open ground 1.5 m in front of the nearest rim this body can climb onto, facing it: well over a step and
        /// within its reach (for a human about 1-1.8 m up).
        /// </summary>
        void PlaceByLedge()
        {
            if (!ledgeRequest || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source))
                return;
            var em = queryWorld.EntityManager;
            var supportQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SupportGrids>(), ComponentType.ReadOnly<FloraSupport>());
            var holder = supportQuery.CalculateEntityCount() == 1 ? supportQuery.GetSingletonEntity() : Entity.Null;
            var grids = holder != Entity.Null ? em.GetComponentData<SupportGrids>(holder) : default;
            if (!grids.Set.IsCreated || em.GetBuffer<FloraSupport>(holder, true).Length == 0)
            {
                // (The tops load a little after the world: give them a while. On a first play after a domain reload the
                // world is found again after the jump, and the nearest settlement may have moved: jump there once more.)
                if (!ledgeRejumped && Time.timeSinceLevelLoad - optionsAt > settle + 15f)
                {
                    ledgeRejumped = true;
                    var town = locations.FindIndex(l => l.Name.StartsWith("Nearest settlement"));
                    if (town >= 0)
                    {
                        current = town - 1;
                        Jump(1);
                        Debug.Log("DebugControls: -place ledge: no props yet, to the nearest settlement again");
                    }
                }
                if (Time.timeSinceLevelLoad - optionsAt > settle + 45f)
                {
                    Debug.LogWarning("DebugControls: -place ledge: no props with tops near");
                    ledgeRequest = false;
                }
                return;
            }
            // (A wall search moving on to another settlement: its props take a moment to load round the player.)
            if (wallRequest && Time.timeSinceLevelLoad < wallSearchAt)
                return;
            ledgeRequest = false;
            var props = em.GetBuffer<FloraSupport>(holder, true);
            var playerEntity = playerQuery.GetSingletonEntity();
            var t = em.GetComponentData<LocalTransform>(playerEntity);
            var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            var size = bodyQuery.TryGetSingleton<PlayerBody>(out var body) && body.Size > 0f ? body.Size : 1f;
            // As PlayerMovementSystem: a step is 0.4 m, ledges are looked for 0.9 m ahead and climbed up to 1.8 m (all
            // times the body's size).
            float stepUp = 0.4f * size, reach = 0.9f * size, maxRise = 1.8f * size;
            // (A wall for a climber: probed at a human's scale, any wall over a metre and up to 6 m, flat top or not.)
            if (wallRequest)
            {
                stepUp = 0.4f;
                reach = 0.9f;
                maxRise = 6f;
            }
            ref var set = ref grids.Set.Value;
            var best = float.MaxValue;
            float3 bestFeet = default;
            float2 bestForward = default;
            SupportHit bestHit = default;
            // Round each prop, nearest first, walking in from outside it until a ledge comes within reach; the first prop
            // with a flat one will do. (Every probe looks through every prop: this keeps it to a moment.)
            var order = new List<int>();
            for (int p = 0; p < props.Length; p++)
                order.Add(p);
            var playerXZ = t.Position.xz;
            order.Sort((x, y) => math.distancesq(props[x].Position.xz, playerXZ).CompareTo(math.distancesq(props[y].Position.xz, playerXZ)));
            foreach (var p in order)
            {
                if (best < SlopedTop)
                    break;
                var prop = props[p];
                var outer = set.Grids[prop.Grid].Reach * prop.Scale + 2f;
                for (int k = 0; k < 24; k++)
                {
                    var a = k * math.PI / 12f;
                    var dir = new float2(math.cos(a), math.sin(a));
                    for (var d = outer; d > 0.5f; d -= 0.25f)
                    {
                        var xz = prop.Position.xz + dir * d;
                        var g = source.SampleGround(xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                            break;
                        var feet = new float3(xz.x, g.Height, xz.y);
                        // (Already up on something, a step or a deck: not open ground.)
                        if (PropSupport.Sample(ref set, props, feet, 0.3f, stepUp, out var under) && under.Height > g.Height + 0.05f)
                            break;
                        if (!PropSupport.Ledge(ref set, props, feet, -dir, reach, stepUp, maxRise, out var hit))
                            continue;
                        // The first ledge in reach this way: a real climb (not a tall step), with level ground to set off from.
                        var stand = xz + dir * 1.5f;
                        var level = math.abs(source.SampleHeight(stand) - g.Height) < stepUp &&
                                    !PropSupport.Sample(ref set, props, new float3(stand.x, g.Height, stand.y), 0.3f, stepUp, out _);
                        // A flat top to stand on past the rim (a wall's top, a well's rim, a stall's counter), not the eave
                        // of a sloping roof: level within 15 cm for 1.2 m in and half a metre to either side.
                        var flat = true;
                        var inward = new float3(-dir.x, 0f, -dir.y);
                        var across = new float3(-dir.y, 0f, dir.x);
                        // (A wall's cap is thin: for a wall climber's test only the first 0.4 m in and the sides are asked.)
                        for (int q = 0; q < (wallRequest ? 5 : 5) && flat; q++)
                        {
                            if (wallRequest && (q == 1 || q == 2))
                                continue;
                            var probe = hit.Edge + inward * (q < 3 ? 0.4f * (q + 1) : 0.8f) + across * (q == 3 ? 0.5f : q == 4 ? -0.5f : 0f);
                            probe.y = hit.Height - 0.3f;
                            flat = PropSupport.Sample(ref set, props, probe, 0.2f, 0.6f, out var top) && math.abs(top.Height - hit.Height) < 0.15f;
                        }
                        // (A sloping one only if nothing near has a flat one.)
                        var distance = math.distance(xz, t.Position.xz) + (flat ? 0f : SlopedTop);
                        if (wallRequest && flat && level)
                            wallTallest = math.max(wallTallest, hit.Height - feet.y);
                        // (A-14 #33: a wall climber's test wants a flat-topped wall of WallMin or more.)
                        if (hit.Height - feet.y >= (wallRequest ? WallMin : 0.55f * maxRise) && level && (flat || !wallRequest) && distance < best)
                        {
                            best = distance;
                            bestFeet = feet;
                            bestForward = -dir;
                            bestHit = hit;
                        }
                        break;
                    }
                }
            }
            if (best == float.MaxValue && wallRequest && NextWallSettlement(source, em, playerEntity, t))
                return;
            if (best == float.MaxValue)
            {
                Debug.LogWarning("DebugControls: -place ledge: no ledge in reach near");
                return;
            }
            var from = bestFeet.xz - bestForward * 1.5f;
            t.Position = new float3(from.x, source.SampleHeight(from) + 0.05f, from.y);
            t.Rotation = quaternion.RotateY(math.atan2(bestForward.x, bestForward.y));
            em.SetComponentData(playerEntity, t);
            em.SetComponentData(playerEntity, new PlayerMotion { Grounded = true });
            var rim = bestHit.Edge;
            // Over the rim, onto the top (a little further in than a mantle puts it, so it stops there).
            var over = rim + new float3(bestForward.x, 0f, bestForward.y) * 0.8f;
            if (wallRequest)
            {
                PlayerMovementSystem.WallProbe.Data = 1;
                StartClimbTest(t.Position, rim, rim, false, $"wall at ({V(rim.x)}, {V(rim.y)}, {V(rim.z)}), {bestHit.Height - bestFeet.y:F1} m up; player from ({V(t.Position.x)}, {V(t.Position.z)})");
                climbWall = true;
                // (A-15 #33: the mid-climb shot from 9 m back off the wall face and 1.5 m to one side, looking at the
                // lower half of the wall. A-13/14 framed it from 5 m off to the side, which put the camera inside the
                // building on a long wall and dithered the face away (PlayerReveal); from out in front, the wall is
                // behind the climber and nothing of it is cut.)
                // (Square on to the face itself, by the rim's outward normal: the line from the player to the rim can
                // be well off it, which showed the face edge-on.)
                var wallOut = math.normalizesafe(bestHit.EdgeNormal, math.normalizesafe(t.Position.xz - rim.xz, new float2(0f, -1f)));
                var wallSide = new float2(wallOut.y, -wallOut.x);
                var wallHeight = rim.y - bestFeet.y;
                climbWallTop = wallHeight;
                // (A-101 row 1, "-place climb": a marked band on the face, centred on the rim the player faces, from
                // its foot to its top; logged as a flag a joined client marks the same band with.)
                if (climbBandGrade > Climbing.None)
                {
                    var band = new ClimbBand
                    {
                        Base = new float3(rim.x, bestFeet.y, rim.z), Out = wallOut,
                        HalfWidth = climbBandGrade == Climbing.Ladder ? 0.45f : 1.5f, Height = wallHeight + 0.5f, Grade = climbBandGrade
                    };
                    ClimbBands.Add(band);
                    MarkClimbBand(band);
                    Debug.Log($"DebugControls: climb band: grade {climbBandGrade}, {V(band.HalfWidth * 2f)} m wide, {V(wallHeight)} m up; for a client: -climbband {ClimbBandArg(band)}");
                }
                var eyeXZ = rim.xz + wallOut * 9f + wallSide * 1.5f;
                var eyeY = math.max(source.SampleHeight(eyeXZ) + 1.6f, bestFeet.y + 1.8f);
                var wallEye = new Vector3(eyeXZ.x, eyeY, eyeXZ.y);
                var wallAim = new Vector3(rim.x, bestFeet.y + math.min(wallHeight * 0.4f, 2f), rim.z);
                PoseCamera(wallEye, Quaternion.LookRotation(wallAim - wallEye));
                // (A narrower view, so a lizard a quarter of a metre long reads from 9 m off.)
                if (Camera.main != null)
                {
                    if (wallFovWas == 0f)
                        wallFovWas = Camera.main.fieldOfView;
                    Camera.main.fieldOfView = 28f;
                }
                Debug.Log($"DebugControls: wall camera at ({V(wallEye.x)}, {V(wallEye.y)}, {V(wallEye.z)}) looking at ({V(wallAim.x)}, {V(wallAim.y)}, {V(wallAim.z)}), wall {V(wallHeight)} m");
                return;
            }
            climbWall = false;
            StartClimbTest(t.Position, over, rim, true, $"ledge rim at ({V(rim.x)}, {V(rim.y)}, {V(rim.z)}), {bestHit.Height - bestFeet.y:F1} m up, " +
                                                        (best < SlopedTop ? "flat top" : "sloping top (no flat one near)"));
        }

        /// <summary>How much further off a sloping top counts than a flat one, choosing a ledge (m).</summary>
        const float SlopedTop = 1000f;
    }
}
