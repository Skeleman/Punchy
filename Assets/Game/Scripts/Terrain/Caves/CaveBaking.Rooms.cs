using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-115: destination chambers. Some caves turn a dead-end chamber (the main passage's end, and in big networks a
    /// side passage's) into a tile-built room: a crystal grotto, an underground lake, a bat roost or a buried ruin room,
    /// picked by the cave's style from its own seed. A room is a box of air walled off from the cave's own rock noise,
    /// with a doorway stub through its wall where the passage meets it, so the tiles CaveRoomMeshes lays sit flush on
    /// it and collide as drawn. Each room is measured against the passage it ends (floor, head room and width at the
    /// door) and kept only where it fits: rock all round, cover above, and no other air inside its walls. Bat folk
    /// chambers (wide ones) and stream ends are left as they are.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Chambers this wide or wider (radius × width) can house bat folk (BatFolkBaking.MinChamber): never rooms.</summary>
        const float MaxRoomChamber = 9f;

        static void PlaceRooms(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                               List<CaveSegment> segments, List<CaveStyle> styles, Extras extras)
        {
            var plans = new List<CaveRoom>();
            var oldRadius = new List<float>();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var chance = style != null ? style.RoomChance : 0.55f;
                var weights = style != null ? (float4)(UnityEngine.Vector4)style.RoomWeights : new float4(1f);
                var rng = new Random(math.hash(new uint2(cave.Key, 0x800Du)) | 1u);
                // Every draw is made whatever is picked, so one cave's choice never shifts another's.
                float mainRoll = rng.NextFloat(), sideRoll = rng.NextFloat();
                var sidePick = rng.NextUInt();
                // A-91 row 3: a second side room in the grown networks (drawn after the first, so its rolls are unchanged).
                float sideRoll2 = rng.NextFloat();
                var sidePick2 = rng.NextUInt();
                // The stream's own roll (PlaceStreams): a stream runs down the main passage, so its end stays a chamber.
                var streamRng = new Random(math.hash(new uint2(cave.Key, 0x5711u)) | 1u);
                var stream = style != null && style.StreamChance > 0f && streamRng.NextFloat() < style.StreamChance && cave.NodeCount >= 4;

                var degree = new int[cave.NodeCount];
                for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                {
                    degree[segments[s].A - cave.FirstNode]++;
                    degree[segments[s].B - cave.FirstNode]++;
                }
                var mainEnd = cave.FirstNode + 1;
                while (mainEnd + 1 < cave.FirstNode + cave.NodeCount && Joined(mainEnd, mainEnd + 1, cave, segments))
                    mainEnd++;
                bool Fits(int n) => kinds[n] == CaveNodeKind.Chamber && degree[n - cave.FirstNode] == 1 && n != cave.FirstNode &&
                                    nodes[n].Radius * cave.Profile.x < MaxRoomChamber;
                var sides = new List<int>();
                for (int n = cave.FirstNode + 1; n < cave.FirstNode + cave.NodeCount; n++)
                    if (n != mainEnd && Fits(n))
                        sides.Add(n);

                void Plan(int n, uint salt)
                {
                    var pick = new Random(math.hash(new uint3(cave.Key, (uint)(n - cave.FirstNode), salt)) | 1u);
                    var total = math.csum(weights);
                    if (total <= 0f)
                        return;
                    var roll = pick.NextFloat(total);
                    var type = 0;
                    while (type < 3 && roll >= weights[type])
                        roll -= weights[type++];
                    var prev = -1;
                    for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                        if (segments[s].B == n) prev = segments[s].A;
                        else if (segments[s].A == n) prev = segments[s].B;
                    var f = math.normalizesafe(nodes[n].Position.xz - nodes[prev].Position.xz);
                    if (prev < 0 || math.lengthsq(f) < 0.5f)
                        return;
                    float2 hx, hz, h;
                    switch ((CaveRoomType)type)
                    {
                        case CaveRoomType.Lake: hx = new float2(4.5f, 5.5f); hz = new float2(4.5f, 6f); h = new float2(5f, 6.5f); break;
                        case CaveRoomType.Roost: hx = new float2(3.5f, 4.5f); hz = new float2(4f, 5.5f); h = new float2(6f, 8f); break;
                        // A-91 row 3 (Zach 11-10): ruins about 4x: twice as wide and deep, laid out as four rooms
                        // (CaveRoomKit.Ruin). Measure shrinks one that doesn't fit back towards the old size.
                        case CaveRoomType.Ruin: hx = new float2(7f, 9f); hz = new float2(7f, 9f); h = new float2(5f, 6f); break;
                        default: hx = new float2(3.5f, 4.5f); hz = new float2(3.5f, 5f); h = new float2(4.5f, 6f); break;
                    }
                    plans.Add(new CaveRoom
                    {
                        Cave = c, Node = n, Type = type, F = f, Origin = nodes[n].Position,
                        HX = pick.NextFloat(hx.x, hx.y), HZ = pick.NextFloat(hz.x, hz.y), Height = pick.NextFloat(h.x, h.y),
                        Seed = pick.NextUInt() | 1u, Pool = -1
                    });
                    // The node narrows to the passage's width: the room's own air takes over past the door.
                    oldRadius.Add(nodes[n].Radius);
                    nodes[n] = new CaveNode { Position = nodes[n].Position, Radius = math.min(nodes[prev].Radius, nodes[n].Radius) };
                    kinds[n] = CaveNodeKind.Room;
                }
                if (!stream && Fits(mainEnd) && mainRoll < chance)
                    Plan(mainEnd, 1u);
                var first = -1;
                if (sides.Count >= 2 && sideRoll < chance * 0.5f)
                    Plan(first = sides[(int)(sidePick % (uint)sides.Count)], 2u);
                if (sides.Count >= 4 && sideRoll2 < chance * 0.5f)
                {
                    sides.Remove(first);
                    Plan(sides[(int)(sidePick2 % (uint)sides.Count)], 3u);
                }
            }
            if (plans.Count == 0)
                return;

            // Measure each against the passages as they now are (no rooms yet), and keep the ones that fit.
            var probe = BuildBlob(caves, nodes, kinds, segments, new Extras(), Allocator.Persistent);
            var at = source;
            at.Caves = probe;
            var kept = new List<CaveRoom>();
            System.Array.Clear(RoomMisses, 0, RoomMisses.Length);
            for (int i = 0; i < plans.Count; i++)
            {
                var room = plans[i];
                if (Measure(at, caves[room.Cave], nodes, ref room, kept))
                {
                    kept.Add(room);
                    continue;
                }
                // A-91 row 3: a grown ruin that doesn't fit tries three quarters, then half its size (the old one);
                // any other room tries three quarters once (under thin rock or near other air a smaller room fits).
                {
                    var fit = false;
                    var tries = room.Type == (int)CaveRoomType.Ruin ? 2 : 1;
                    for (int k = 1; k <= tries && !fit; k++)
                    {
                        room = plans[i];
                        room.HX *= 1f - k * 0.25f;
                        room.HZ *= 1f - k * 0.25f;
                        fit = Measure(at, caves[room.Cave], nodes, ref room, kept);
                    }
                    if (fit)
                    {
                        kept.Add(room);
                        continue;
                    }
                }
                nodes[room.Node] = new CaveNode { Position = nodes[room.Node].Position, Radius = oldRadius[i] };
                kinds[room.Node] = CaveNodeKind.Chamber;
            }
            probe.Dispose();

            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                cave.FirstRoom = extras.Rooms.Count;
                foreach (var room in kept)
                    if (room.Cave == c)
                    {
                        extras.Rooms.Add(room);
                        RoomBox(room, out var lo, out var hi);
                        cave.Min = math.min(cave.Min, lo - 1f);
                        cave.Max = math.max(cave.Max, hi + 1f);
                    }
                cave.RoomCount = extras.Rooms.Count - cave.FirstRoom;
                caves[c] = cave;
            }
            var counts = new int[4];
            foreach (var room in kept)
                counts[room.Type]++;
            UnityEngine.Debug.Log($"[cave-rooms] planned {plans.Count}, kept {kept.Count}: grotto {counts[0]}, lake {counts[1]}, roost {counts[2]}, ruin {counts[3]}; misses: floor {RoomMisses[0]}, head room {RoomMisses[1]}, width {RoomMisses[2]}, door top {RoomMisses[3]}, cover {RoomMisses[4]}, air {RoomMisses[5]}, overlap {RoomMisses[6]}");
        }

        /// <summary>Why planned rooms didn't fit in the last bake, by Measure's checks in order (for the log).</summary>
        static readonly int[] RoomMisses = new int[7];
        static bool Miss(int why)
        {
            RoomMisses[why]++;
            return false;
        }

        /// <summary>
        /// Sets a planned room's floor and doorway from the passage it ends, and whether it fits: a floor at the door,
        /// at least 2.2 m of head room and 1.9 m of width there, cover above the whole room, no other air (any cave's)
        /// inside its walls, and clear of rooms already kept.
        /// </summary>

        static bool Measure(in TerrainSource source, in CaveData cave, List<CaveNode> nodes, ref CaveRoom room, List<CaveRoom> kept)
        {
            const float t = CaveRoom.Wall;
            var node = nodes[room.Node];
            var door = node.Position.xz - room.F * (t + 0.3f);
            var floor = source.FloorUnder(new float3(door.x, node.Position.y, door.y), out var inCave);
            if (!inCave)
                return Miss(0);
            var gap = source.CeilingOver(new float3(door.x, floor + 0.5f, door.y)) - floor;
            if (gap < 2.2f)
                return Miss(1);
            room.Origin = new float3(node.Position.x, floor, node.Position.z);
            var half = 4f;
            for (int side = -1; side <= 1; side += 2)
                for (float s = 0f; s < half; s += 0.1f)
                    if (source.CaveDistance(room.World(new float3(side * s, 1f, -t - 0.3f))) >= 0f)
                    {
                        half = s;
                        break;
                    }
            if (half * 2f < 1.9f)
                return Miss(2);
            room.DoorWidth = math.clamp(half * 2f - 0.6f, 1.6f, 2.4f);
            room.DoorHeight = math.clamp(gap - 0.3f, 2f, 3f);
            // The doorway's top corners must be in the passage's air too (a sloping roof lowers it).
            for (int k = 0; k < 3; k++)
            {
                var corner = room.World(new float3(room.DoorWidth * 0.5f, room.DoorHeight, -t - 0.3f));
                var other = room.World(new float3(-room.DoorWidth * 0.5f, room.DoorHeight, -t - 0.3f));
                if (source.CaveDistance(corner) < 0f && source.CaveDistance(other) < 0f)
                    break;
                room.DoorHeight -= 0.4f;
            }
            if (room.DoorHeight < 2f)
                return Miss(3);
            // The passage's floor a ramp's length out from the wall's face, over the room's floor: the ramp in front of
            // the porch meets it, so the doorway is a slope up or down instead of a step or a pit.
            room.Approach = 0f;
            var far = room.World(new float3(0f, 0f, -t - CaveRoom.Ramp));
            for (float y = -2f; y <= 5f; y += 0.25f)
                if (source.CaveDistance(new float3(far.x, floor + y, far.z)) < -0.3f)
                {
                    room.Approach = source.FloorUnder(new float3(far.x, floor + y, far.z), out _) - floor;
                    break;
                }

            // Cover over the room, at its corners and middle. A-91 row 3: under thin rock the room is made lower to
            // keep its cover (down to 3.2 m, a roost to 4.5 m for its perches) rather than left out: the longer, gently
            // graded caves end shallower than they used to.
            var ground = float.MaxValue;
            for (int k = 0; k < 9; k++)
            {
                var l = new float3((k % 3 - 1) * (room.HX + t), 0f, (k / 3) * (room.HZ + t * 0.5f));
                ground = math.min(ground, source.SampleHeight(room.World(l).xz));
            }
            room.Height = math.min(room.Height, ground - floor - t - Cover);
            if (room.Height < (room.Type == (int)CaveRoomType.Roost ? 4.5f : 3.2f))
                return Miss(4);
            // No air inside the walls but the passage's own end (any other passage or cave would be cut off).
            var pass = node.Radius * math.cmax(cave.Profile) * 1.2f + 1.5f;
            for (float z = -t; z <= room.HZ * 2f + t; z += 1.5f)
            for (float x = -room.HX - t; x <= room.HX + t; x += 1.5f)
            for (float y = -2f; y <= room.Height + t; y += 1.5f)
            {
                if (z < pass && math.abs(x) < pass)
                    continue;
                if (source.CaveDistance(room.World(new float3(x, y, z))) < 0.3f)
                    return Miss(5);
            }
            RoomBox(room, out var lo, out var hi);
            foreach (var other in kept)
            {
                RoomBox(other, out var olo, out var ohi);
                if (math.all(lo < ohi + 2f) && math.all(olo < hi + 2f))
                    return Miss(6);
            }
            return true;
        }

        /// <summary>World box round a room's walls (and its doorway stub).</summary>
        internal static void RoomBox(in CaveRoom room, out float3 lo, out float3 hi)
        {
            const float t = CaveRoom.Wall;
            lo = float.MaxValue;
            hi = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var l = new float3((k & 1) == 0 ? -room.HX - t : room.HX + t, (k & 2) == 0 ? -3f : room.Height + t,
                                   (k & 4) == 0 ? -t - 1f : room.HZ * 2f + t);
                var w = room.World(l);
                lo = math.min(lo, w);
                hi = math.max(hi, w);
            }
        }

        /// <summary>Whether a point is inside a room's walls or its doorway (with a margin).</summary>
        internal static bool InRoom(in CaveRoom room, float3 p, float margin)
        {
            const float t = CaveRoom.Wall;
            var l = room.Local(p);
            return math.abs(l.x) <= room.HX + t + margin && l.z >= -t - CaveRoom.Ramp - margin && l.z <= room.HZ * 2f + t + margin &&
                   l.y >= -3f - margin && l.y <= room.Height + t + margin;
        }

        /// <summary>
        /// After everything else is placed: takes the rock decorations and plants placed for the old chamber out of the
        /// rooms (they bring their own), and gives each lake room its pool.
        /// </summary>
        static void FinishRooms(List<CaveData> caves, Extras extras)
        {
            if (extras.Rooms.Count == 0)
                return;
            bool Inside(float3 p)
            {
                foreach (var room in extras.Rooms)
                    if (InRoom(room, p, 0.5f))
                        return true;
                return false;
            }
            extras.Speleothems.RemoveAll(s => Inside(s.Root) || Inside(s.Tip) || Inside((s.Root + s.Tip) * 0.5f));
            extras.Plants.RemoveAll(p => Inside(p.Position));
            for (int i = 0; i < extras.Rooms.Count; i++)
            {
                var room = extras.Rooms[i];
                if (room.Type != (int)CaveRoomType.Lake)
                    continue;
                var cave = caves[room.Cave];
                room.Pool = cave.FirstPool + cave.PoolCount;
                extras.Pools.Insert(room.Pool, new CavePool
                {
                    Centre = room.Centre - new float3(0f, 0.12f, 0f),
                    Radius = CaveSampling.LakeRadius(room),
                    Depth = CaveSampling.LakeDepth - 0.12f,
                    Lava = 0f
                });
                cave.PoolCount++;
                caves[room.Cave] = cave;
                for (int later = room.Cave + 1; later < caves.Count; later++)
                {
                    var next = caves[later];
                    next.FirstPool++;
                    caves[later] = next;
                }
                extras.Rooms[i] = room;
            }
        }
    }
}
