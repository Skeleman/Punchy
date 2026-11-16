using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// P-37 r3 row 3 (#369): houses walked into (StructureKit.WalkIn, with Seamless on). Their doors lead to no room off
    /// the island: Use only opens and shuts them (Doors), and the player walks in. Interiors keeps a per-house lookup
    /// (32 m cells) of whether a point is inside one and how far past its doorway, which blends the ambient light (by
    /// depth, not over time) and pulls the camera in. The house type's room is still made (its index, seeds and stores
    /// stay what the teleport rooms had), but no door leads to it; its stores become the door's own container just
    /// inside the doorway (Interiors.Storage). Built on the P-30 prototype (player/seamless).
    /// Test flag: "-walkin N" (with -atdoor N -usedoor N; shots with -shotdir).
    /// </summary>
    public partial class Interiors
    {
        /// <summary>A house walked into: its plot's centre and facing (out through the door), its inside's half size
        /// across and along, its floor (the door's foot), how high it is inside, its door and its type's room.</summary>
        struct WalkIn
        {
            public float2 Centre, Facing, Half;
            public float Floor, Height;
            /// <summary>A round house's inner radius (an elf tower's, P-51 row 2), or 0 for a rectangle (Half).</summary>
            public float Radius;
            public int Door, Room;
        }

        readonly List<WalkIn> walkIns = new List<WalkIn>();
        /// <summary>The per-house lookup: walk-in houses by 32 m cell, so a point asks only the houses round it.</summary>
        readonly Dictionary<int2, List<int>> walkInCells = new Dictionary<int2, List<int>>();
        /// <summary>Door anchor index to its walk-in house (index into walkIns).</summary>
        readonly Dictionary<int, int> walkInOf = new Dictionary<int, int>();
        const float WalkInCell = 32f;
        /// <summary>The 0.5 m plinth a walk-in house's floor stands on, over its door's foot.</summary>
        const float WalkInPlinth = 0.5f;
        /// <summary>The house the local player is in (-1: none) and how far in: 0 at the door, 1 from 1.5 m in.</summary>
        int walkInAt = -1;
        float walkInDepth;
        float walkInLog, ambientLog;
        /// <summary>Seamless as it was when the houses were sorted (Build); a change after that is logged, not rebuilt.</summary>
        bool builtSeamless, seamlessWarned;
        GameObject walkInChests;

        void ClearWalkIns()
        {
            walkIns.Clear();
            walkInCells.Clear();
            walkInOf.Clear();
            builtSeamless = StructureKit.Seamless;
        }

        void AddWalkIn(float2 centre, float2 facing, float2 half, float floor, float height, int door, int room, float radius = 0f)
        {
            var i = walkIns.Count;
            walkIns.Add(new WalkIn { Centre = centre, Facing = math.normalizesafe(facing, new float2(0f, 1f)), Half = half, Floor = floor, Height = height, Radius = radius,
                                     Door = door, Room = room });
            walkInOf[door] = i;
            var r = math.cmax(half) + 1f;
            var lo = (int2)math.floor((centre - r) / WalkInCell);
            var hi = (int2)math.floor((centre + r) / WalkInCell);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                {
                    if (!walkInCells.TryGetValue(new int2(x, y), out var list))
                        walkInCells[new int2(x, y)] = list = new List<int>();
                    list.Add(i);
                }
        }

        /// <summary>Whether door <paramref name="door"/> is a walk-in house's (Use opens and shuts it; no room).</summary>
        public static bool IsWalkIn(int door) => instance != null && instance.walkInOf.ContainsKey(door);

        /// <summary>Whether a point is inside a house walked into, which, and how far in from its door (0..1).</summary>
        public static bool InWalkIn(float3 p, out int house, out float depth)
        {
            (house, depth) = (-1, 0f);
            if (instance == null || instance.walkIns.Count == 0 || !instance.walkInCells.TryGetValue((int2)math.floor(p.xz / WalkInCell), out var list))
                return false;
            foreach (var i in list)
            {
                var w = instance.walkIns[i];
                var to = p.xz - w.Centre;
                var along = math.dot(to, w.Facing);
                var across = math.dot(to, new float2(-w.Facing.y, w.Facing.x));
                if (p.y < w.Floor - 0.5f || p.y > w.Floor + w.Height + 0.5f)
                    continue;
                if (w.Radius > 0f ? math.lengthsq(to) > w.Radius * w.Radius : math.abs(across) > w.Half.x || math.abs(along) > w.Half.y)
                    continue;
                house = i;
                depth = math.saturate(((w.Radius > 0f ? w.Radius : w.Half.y) - along) / 1.5f);
                return true;
            }
            return false;
        }

        /// <summary>The camera's distance in a walk-in house, once the player is half way past its doorway (0: not).</summary>
        float WalkInArm() =>
            inRoom < 0 && walkInAt >= 0 && walkInAt < walkIns.Count && walkInDepth > 0.5f ? math.clamp(math.cmax(walkIns[walkInAt].Half), 2.5f, 3.5f) : 0f;

        /// <summary>Where a walk-in door's own container stands: just inside, against the side wall by the doorway.</summary>
        float3 WalkInChestAt(in WalkIn w)
        {
            var across = new float2(-w.Facing.y, w.Facing.x);
            if (w.Radius > 0f)
            {
                // A round tower: against the wall, 50° round from the doorway away from the stair's foot, on its ground
                // floor (no plinth).
                var round = math.radians(-50f);
                var at = w.Centre + (w.Facing * math.cos(round) + across * math.sin(round)) * math.max(w.Radius - 0.5f, 0f);
                return new float3(at.x, w.Floor, at.y);
            }
            var xz = w.Centre + w.Facing * math.max(w.Half.y - 1.1f, 0f) + across * math.max(w.Half.x - 0.45f, 0f);
            return new float3(xz.x, w.Floor + WalkInPlinth, xz.y);
        }

        /// <summary>Draws a walk-in door's container (a plain chest block; row 4 moves it to the layout's store spots).</summary>
        void DrawWalkInChest(float3 at, float2 facing)
        {
            if (walkInChests == null)
            {
                walkInChests = new GameObject("Walk-in chests") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(walkInChests);
            }
            var half = new float3(0.25f, 0.25f, 0.4f);
            var go = new GameObject("Chest");
            go.transform.SetParent(walkInChests.transform, false);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
            Block(go.transform, at + new float3(0f, half.y, 0f), half * 2f, new Color(0.42f, 0.27f, 0.14f));
        }

        void UpdateWalkIn()
        {
            if (built && builtSeamless != StructureKit.Seamless && !seamlessWarned)
            {
                seamlessWarned = true;
                Debug.LogWarning($"Interiors: walk-in houses turned {(StructureKit.Seamless ? "on" : "off")} after the houses were sorted; this session keeps them {(builtSeamless ? "on" : "off")}");
            }
            if (walkIns.Count == 0)
                return;
            if (!TryPlayer(out _, out var player))
                return;
            var was = walkInAt;
            InWalkIn(player.Position, out walkInAt, out walkInDepth);
            if (walkInAt != was || walkInAt >= 0 && Time.realtimeSinceStartup > walkInLog)
            {
                walkInLog = Time.realtimeSinceStartup + 1f;
                Debug.Log(walkInAt >= 0 ? $"Interiors: in walk-in house {walkInAt} (door {walkIns[walkInAt].Door}), {walkInDepth:F2} in, at {player.Position}" : "Interiors: out of the walk-in house");
            }
        }

        /// <summary>
        /// Inside a walk-in house the light follows the player through the doorway rather than easing in over time: the
        /// sky's at the threshold, the room's 1.5 m in. False (and nothing set) when not in one.
        /// </summary>
        bool WalkInAmbient()
        {
            if (inRoom >= 0 || walkInDepth <= 0f)
                return false;
            indoors = walkInDepth;
            var dim = new Color(0.17f, 0.13f, 0.09f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, dim * 1.1f, indoors);
            RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor, dim, indoors);
            RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor, dim * 0.7f, indoors);
            if (Time.realtimeSinceStartup > ambientLog)
            {
                ambientLog = Time.realtimeSinceStartup + 1f;
                Debug.Log($"Interiors: walk-in light {indoors:F2} in: ambient equator {RenderSettings.ambientEquatorColor}, camera arm {IndoorArm:F2}");
            }
            return true;
        }

        // ------------------------------------------------------------------ test

        /// <summary>
        /// "-walkin N": with -atdoor N and -usedoor N, the player walks from in front of door N through it to 2 m inside
        /// (a second after the door opens), stands, pushes at the side wall for 2.5 s, then walks back out; each step
        /// logged with where it got to and how far in the house it reads (InWalkIn). Shots with -shotdir.
        /// </summary>
        int walkInTest = -2, shotsTaken;
        float2 stepAt;
        bool reachedEntry, leftEntry;
        float walkInTestAt, walkInStepLog;

        static string ShotDir()
        {
            var args = System.Environment.GetCommandLineArgs();
            var at = System.Array.IndexOf(args, "-shotdir");
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        void WalkInTest()
        {
            if (walkInTest == -2)
            {
                var args = System.Environment.GetCommandLineArgs();
                var at = System.Array.IndexOf(args, "-walkin");
                walkInTest = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var n) ? n : -1;
            }
            if (walkInTest < 0 || !Doors.TryGetDoor(walkInTest, out var door, out var facing))
                return;
            // (From the moment the door stands open: -usedoor's own time depends on when the world came up.)
            if (walkInTestAt <= 0f)
            {
                if (!FloraDoors.IsOpen(door, 1f))
                    return;
                walkInTestAt = Time.realtimeSinceStartup + 3f;   // (time for the outside shot first)
                Debug.Log($"Interiors: -walkin {walkInTest}: door open{(IsWalkIn(walkInTest) ? "" : " (not a walk-in house's door)")}, walking in from 1 s");
            }
            var t = Time.realtimeSinceStartup - walkInTestAt + 30f;
            var world = World.DefaultGameObjectInjectionWorld;
            if (!TryPlayer(out _, out var player))
                return;
            var f = math.normalizesafe(facing);
            var across = new float2(-f.y, f.x);
            float2? to = null;
            string step;
            // (The door anchor stands out from the plot's middle; the kit's doorway can be a bay to one side. So: to the
            // door's step first, the low box in front of it, then straight in from there.)
            if (stepAt.x == 0f && stepAt.y == 0f)
            {
                using var sq = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                if (sq.TryGetSingletonBuffer<KitBox>(out var boxes, true))
                    foreach (var kbox in boxes)
                        if (kbox.Half.y < 0.16f && kbox.Half.x < 1.2f && math.abs(kbox.Centre.y - kbox.Half.y - door.y) < 0.2f && math.distance(kbox.Centre.xz, door.xz) < 3f)
                        {
                            stepAt = kbox.Centre.xz;
                            Debug.Log($"Interiors: -walkin {walkInTest}: the door's step at {kbox.Centre}, {math.distance(kbox.Centre.xz, door.xz):F2} m from the anchor");
                        }
            }
            var entry = stepAt.x != 0f || stepAt.y != 0f ? stepAt + f * 0.8f : door.xz;
            var threshold = entry - f * 1.2f;   // (on the sill, in the doorway)
            if (t < 30f) { step = "wait"; }
            else if (t < 33.5f) { to = !reachedEntry ? entry : threshold; step = "in"; reachedEntry |= math.distance(entry, player.Position.xz) < 0.35f; }
            else if (t < 36f) { to = threshold; step = "doorway"; }
            else if (t < 39f) { to = entry - f * 3.2f; step = "in"; }
            else if (t < 42f) step = "stand";
            else if (t < 44.5f) { to = player.Position.xz + across * 5f; step = "wall"; }
            else if (t < 52f)
            {
                var inner = entry - f * 2.3f;   // (in the room, on the doorway's line, then out through it)
                to = !leftEntry ? inner : entry + f * 2f;
                step = "out";
                leftEntry |= math.distance(inner, player.Position.xz) < 0.35f;
            }
            else
            {
                PlayerInputSystem.TestMove = null;
                Debug.Log($"Interiors: -walkin {walkInTest} done");
                walkInTest = -1;
                return;
            }
            PlayerInputSystem.TestMove = to is float2 goal && math.distance(goal, player.Position.xz) > 0.3f ? math.normalizesafe(goal - player.Position.xz) : float2.zero;
            // Shots through the T-1 tool (with -shotdir): outside before the walk, in the doorway, and inside standing.
            var shotDir = ShotDir();
            if (shotDir != null)
            {
                InWalkIn(player.Position, out var inHouse, out var inDepth);
                string shot = null;
                if (shotsTaken == 0 && t >= 27.5f) shot = "seamless-outside";
                else if (shotsTaken == 1 && step == "doorway" && t >= 34.5f) shot = "seamless-doorway";
                else if (shotsTaken == 2 && step == "stand" && t >= 40f) shot = "seamless-inside";
                if (shot != null && Punchy.Debugging.ShotTool.Take(System.IO.Path.Combine(shotDir, shot + ".png"), 1280, 720, 20f, true))
                {
                    shotsTaken++;
                    Debug.Log($"Interiors: -walkin shot {shot} at ({player.Position.x:F2}, {player.Position.y:F2}, {player.Position.z:F2}), house {inHouse}, depth {inDepth:F2}, arm {IndoorArm:F2}");
                }
            }
            if (t >= walkInStepLog)
            {
                walkInStepLog = t + 0.5f;
                InWalkIn(player.Position, out var house, out var depth);
                var off = player.Position.xz - door.xz;
                // (What stands just ahead, 0.5 m on: kit boxes spanning the body's height there, and round obstacles.)
                var ahead = player.Position.xz + (to is float2 g2 ? math.normalizesafe(g2 - player.Position.xz) : float2.zero) * 0.5f;
                var em = world.EntityManager;
                using var bq = em.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                using var oq = em.CreateEntityQuery(ComponentType.ReadOnly<FloraObstacle>());
                var what = "";
                if (bq.TryGetSingletonBuffer<KitBox>(out var kb, true))
                    foreach (var kbox in kb)
                        if (KitCollision.Span(kbox, ahead, out var lo, out var hi) && hi > player.Position.y + 0.3f && lo < player.Position.y + 1.7f)
                            what += $" box at {kbox.Centre} half {kbox.Half} spans {lo:F2}..{hi:F2};";
                if (oq.TryGetSingletonBuffer<FloraObstacle>(out var ob, true))
                    foreach (var o in ob)
                        if (math.distance(o.Position.xz, ahead) < o.Radius + 0.4f)
                            what += $" circle at {o.Position} r {o.Radius:F2} h {o.Height:F1};";
                if (what.Length > 0)
                    Debug.Log($"Interiors: -walkin ahead:{what}");
                Debug.Log($"Interiors: -walkin {step}: at ({player.Position.x:F2}, {player.Position.y:F2}, {player.Position.z:F2}), {math.dot(off, -f):F2} m in past the door, {math.dot(off, across):F2} across; house {house}, depth {depth:F2}; ambient {RenderSettings.ambientEquatorColor}");
            }
        }
    }
}
