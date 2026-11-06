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
    // DebugControls, goto, cave entry, spawns, settling and -walk. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        /// <summary>Puts the player on the floor beside the nearest anchor of a kind, anywhere in the world.</summary>
        void GoToAnchor(in TerrainSource source, AnchorKind kind)
        {
            if (!source.Anchors.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            ref var anchors = ref source.Anchors.Value.Anchors;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player);
            var best = -1;
            for (int i = 0; i < anchors.Length; i++)
                if (anchors[i].Kind == kind && (best < 0 || math.distancesq(anchors[i].Position, at.Position) < math.distancesq(anchors[best].Position, at.Position)))
                    best = i;
            if (best < 0)
            {
                Debug.LogWarning($"DebugControls: no {kind} anchors");
                return;
            }
            var anchor = anchors[best];
            // A step back from it, the way it faces (so not in the water or the plant), on whatever floor is there.
            var p = anchor.Position - new float3(anchor.Facing.x, 0f, anchor.Facing.y) * (anchor.Size + 1f);
            var floor = source.FloorUnder(p + new float3(0f, 0.5f, 0f), out var inCave);
            at.Position = new float3(p.x, inCave ? floor + 0.05f : source.SampleHeight(p.xz), p.z);
            em.SetComponentData(player, at);
            Debug.Log($"DebugControls: at {kind} anchor {best} ({anchor.Position.x:F0}, {anchor.Position.y:F0}, {anchor.Position.z:F0}), in cave {anchor.InCave}");
        }

        /// <summary>Puts the player on the floor of the deepest chamber of the cave whose mouth is nearest.</summary>
        void GoIntoCave(in TerrainSource source)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            ref var set = ref source.Caves.Value;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player);
            var best = -1;
            for (int i = 0; i < set.Caves.Length; i++)
                if (set.Caves[i].NodeCount >= 2 && (best < 0 || math.distance(set.Caves[i].Mouth, at.Position) < math.distance(set.Caves[best].Mouth, at.Position)))
                    best = i;
            if (best < 0)
                return;
            ref var cave = ref set.Caves[best];
            var node = set.Nodes[cave.FirstNode + cave.NodeCount - 1];
            var floor = source.FloorUnder(node.Position, out var inCave);
            at.Position = new float3(node.Position.x, inCave ? floor + 0.05f : node.Position.y, node.Position.z);
            em.SetComponentData(player, at);
            Debug.Log($"DebugControls: into cave {best}, chamber floor at ({at.Position.x:F0}, {at.Position.y:F0}, {at.Position.z:F0})");
        }

        /// <summary>
        /// Spawns the "-spawn" requests near the player (where the game simulates animals: alone or hosting), each on
        /// ground or in water to suit it, a few seconds after the world is up.
        /// </summary>
        void SpawnRequested()
        {
            if (pendingSpawns.Count == 0 || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null)
                return;
            if (!director.CanSpawn)
            {
                // A joined client doesn't simulate animals: only a host or a solo game can spawn them.
                Debug.LogWarning("DebugControls: -spawn needs a solo or hosting game");
                pendingSpawns.Clear();
                return;
            }
            var rng = new Unity.Mathematics.Random(1234u);
            foreach (var request in pendingSpawns)
            {
                var parts = request.Split(':');
                var count = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 3;
                var index = director.SpeciesIndex(parts[0]);
                if (index < 0)
                {
                    Debug.LogWarning($"DebugControls: no species '{parts[0]}' to spawn");
                    continue;
                }
                var wantsWater = director.SpeciesHabitat(index) == Habitat.Swims || director.SpeciesHabitat(index) == Habitat.Floats;
                var spawned = 0;
                // Cave dwellers go on the cave floor round the player (who should be in a cave: -incave, -at), and so
                // do cave visitors when the player is in one.
                source.FloorUnder(player.Position + new float3(0f, 0.5f, 0f), out var playerInCave);
                if (director.SpeciesCaveDweller(index) || director.SpeciesVisitsCaves(index) && playerInCave)
                {
                    for (int attempt = 0; attempt < 400 && spawned < count; attempt++)
                    {
                        var xz = player.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(2f, 15f);
                        var floor = source.FloorUnder(new float3(xz.x, player.Position.y + 0.5f, xz.y), out var inCave);
                        if (!inCave || math.abs(floor - player.Position.y) > 2f || source.CaveDistance(new float3(xz.x, floor + 0.3f, xz.y)) >= -0.05f)
                            continue;
                        director.Spawn(index, new float3(xz.x, floor, xz.y));
                        spawned++;
                    }
                    Debug.Log($"DebugControls: spawned {spawned} {parts[0]} on the cave floor");
                    continue;
                }
                for (int attempt = 0; attempt < 400 && spawned < count; attempt++)
                {
                    var xz = player.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(math.min(6f, spawnRadius * 0.5f), spawnRadius);
                    var g = source.SampleGround(xz);
                    var depth = g.HasWater ? g.WaterSurface - g.Height : -1f;
                    if (wantsWater ? depth < 1f : depth > 0f)
                        continue;
                    director.Spawn(index, new float3(xz.x, wantsWater ? g.WaterSurface - math.min(depth * 0.45f, 0.6f) : g.Height, xz.y));
                    spawned++;
                }
                Debug.Log($"DebugControls: spawned {spawned} {parts[0]}");
            }
            pendingSpawns.Clear();
        }

        /// <summary>
        /// Parses "-walk": one point "x,y,z" (the old form) or "x,z", or waypoints split by ';'. Fills walkPath with
        /// the legs after the first and returns the first, or null if any point does not parse.
        /// </summary>
        static float3? ParseWalkPath(string text)
        {
            walkPath.Clear();
            walkLeg = 0;
            foreach (var part in text.Split(';'))
            {
                if (part.Length == 0)
                    continue;
                var p = ParseFloats(part);
                if (p == null || p.Length < 2)
                {
                    walkPath.Clear();
                    return null;
                }
                walkPath.Add(p.Length >= 3 ? new float3(p[0], p[1], p[2]) : new float3(p[0], 0f, p[1]));
            }
            if (walkPath.Count == 0)
                return null;
            var first = walkPath[0];
            walkPath.RemoveAt(0);
            return first;
        }

        /// <summary>
        /// W-67b: after a -goto, once kit boxes reach the point, lifts the player onto the first kit floor over the
        /// ground there (a wharf deck, not the seabed under it). With an explicit "x,y,z" it stays on the floor under
        /// that height, so a forced under-deck walk is still possible. Gives up after 5 s (no kit there).
        /// </summary>
        void SettleGoto()
        {
            if (!gotoSettle.HasValue || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var goer))
                return;
            var spot = gotoSettle.Value;
            var em = queryWorld.EntityManager;
            using var kitQuery = em.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            var ready = kitQuery.TryGetSingletonBuffer<KitBox>(out var kit, true) && KitCollision.AnyNear(kit, spot.xz, 0.5f);
            if (!ready)
            {
                if (Time.unscaledTime > gotoSettleUntil)
                {
                    Debug.Log($"DebugControls: goto no kit boxes at {V(spot.x)},{V(spot.z)} after 5 s");
                    gotoSettle = null;
                }
                return;
            }
            gotoSettle = null;
            var t = em.GetComponentData<LocalTransform>(goer);
            var ground = t.Position.y;
            float top;
            var found = float.IsNaN(spot.y)
                ? KitCollision.LowestTopAt(kit, spot.xz, ground - 0.3f, 1.7f, out top, RoofGap(spot.xz))
                : KitCollision.TopAt(kit, spot.xz, spot.y + 1f, 1.7f, out top);
            if (found && top > ground)
            {
                t.Position.y = top + 0.05f;
                em.SetComponentData(goer, t);
            }
            Debug.Log($"DebugControls: goto kit ready, floor {(found ? V(top) : "none")} (ground {V(ground)})");
        }

        /// <summary>"-walk": steers the player at the line's walk target, and logs probe lines on the way.</summary>
        void WalkTo()
        {
            if (!walkTarget.HasValue || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingleton<LocalTransform>(out var walker))
                return;
            // W-67b: hold still till the -goto has its kit boxes (and has landed on a deck if there is one).
            if (gotoSettle.HasValue)
            {
                PlayerInputSystem.TestMove = float2.zero;
                walkStillSince = Time.unscaledTime;
                walkLastY = walker.Position.y;
                return;
            }
            var to = walkTarget.Value.xz - walker.Position.xz;
            var distance = math.length(to);
            // Waypoints: within 0.6 m of a leg's end, log it and head for the next one (the last stops at 0.3 m as before).
            if (distance <= 0.6f && walkPath.Count > 0)
            {
                walkLeg++;
                Debug.Log($"DebugControls: walk leg {walkLeg} reached at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {walkPath.Count} to go");
                walkTarget = walkPath[0];
                walkPath.RemoveAt(0);
                to = walkTarget.Value.xz - walker.Position.xz;
                distance = math.length(to);
            }
            PlayerInputSystem.TestMove = distance > 0.3f ? to / distance : float2.zero;
            // W-65c: the walk's end, for the shot list (a shot after a walk waits for it): arrived on the last leg, or
            // stood still for 2 s (blocked: a door, a wall).
            if (walkEnd == null)
            {
                // (W-67: and not still falling, as after a -goto drop onto the target.)
                if (walkPath.Count == 0 && distance <= 0.35f && math.abs(walker.Position.y - walkLastY) < 0.01f)
                    walkEnd = "ARRIVED";
                else if (math.distance(walker.Position.xz, walkStillAt) > 0.05f)
                {
                    walkStillAt = walker.Position.xz;
                    walkStillSince = Time.unscaledTime;
                    walkMoves++;
                }
                else if (walkMoves > 5 && Time.unscaledTime - walkStillSince > 2f)   // (not while the world first loads)
                    walkEnd = "STOPPED";
                // W-67: arrived means on the target's floor too, not just over its x, z (a walker under a deck was
                // ARRIVED): feet within 0.3 m of the expected top, else FAIL.
                if (walkEnd == "ARRIVED" && ExpectedFloor(walkTarget.Value, walker.Position, out var expected))
                {
                    Debug.Log($"DebugControls: walk foot y {V(walker.Position.y)} expected {V(expected)} ({V(walker.Position.y - expected)} m)");
                    if (math.abs(walker.Position.y - expected) > 0.3f)
                        walkEnd = "FAIL";
                }
                walkLastY = walker.Position.y;
                if (walkEnd != null)
                    Debug.Log($"DebugControls: walk {walkEnd} at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {V(distance)} m to go, {walkPath.Count} legs left");
            }
            // Probe lines every quarter second: where it is, its clearance over the floor, and how far the push-out has moved it.
            if (Time.unscaledTime >= walkLogNext && EnsureLocations(out var walkGround))
            {
                walkLogNext = Time.unscaledTime + 0.25f;
                var floor = walkGround.FloorUnder(walker.Position + new float3(0f, 0.5f, 0f), out var walkInCave);
                Debug.Log($"DebugControls: walk at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {V(distance)} m to go, " +
                          $"{V(walker.Position.y - floor)} m over the floor (in cave {walkInCave}), pushed out {V(Punchy.Player.PlayerMovementSystem.PushedTotal.Data)} m in all" +
                          (Punchy.Player.PlayerMovementSystem.NoPushOut.Data ? " (push-out off)" : ""));
            }
        }
    }
}
