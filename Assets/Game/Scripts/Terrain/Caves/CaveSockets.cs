using System.Collections.Generic;
using Punchy.Dungeon;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Dungeon sockets for natural caves (W-115 row 1): the same DungeonSocket spots the sunken temple has, read from a
    /// cave's baked network instead of kit rooms. Chambers get a creature in the middle (and one on the ceiling when
    /// tall), clutter round the floor and decor on the walls; dead ends and the main end keep a chest against the far
    /// wall; passages get a guard where they open into a chamber and clutter and decor along their sides; water pools get
    /// NearWater spots round the rim. Deep in, away from the mouth, shafts and lava, a spot is Dark. Each socket carries
    /// its chamber type (Chamber, DeadEnd, Passage tags) and the surface biome above it. Bat folk chambers are left to
    /// the bat folk. Nothing random: positions come from the cave's shape (walls found by stepping the cave field) and
    /// its Key, seeds from MakeSeed, so every machine builds the same sockets.
    /// </summary>
    public static class CaveSockets
    {
        /// <summary>Most sockets of each kind (SocketKind order) kept per cave, thinned evenly through the candidates.</summary>
        static readonly int[] Caps = { 6, 3, 8, 3, 10 };

        /// <summary>Daylight reach: spots nearer the mouth (or under a shaft, or by lava) than this aren't Dark.</summary>
        const float DarkBeyond = 20f;

        struct Candidate
        {
            public float3 P;
            public float2 Face;
            public SocketKind Kind;
            public SocketSize Size;
            public SocketTags Tags;
        }

        public static List<DungeonSocket> Build(TerrainSource source, int cave, uint worldSeed, out int chambers, out int deadEnds)
        {
            ref var set = ref source.Caves.Value;
            var c = set.Caves[cave];
            int first = c.FirstNode, count = c.NodeCount;
            var nodes = new CaveNode[count];
            var kinds = new CaveNodeKind[count];
            for (int i = 0; i < count; i++)
            {
                nodes[i] = set.Nodes[first + i];
                kinds[i] = set.Kinds[first + i];
            }
            // Links (local node indices) per node.
            var links = new List<int>[count];
            for (int i = 0; i < count; i++) links[i] = new List<int>();
            for (int s = c.FirstSegment; s < c.FirstSegment + c.SegmentCount; s++)
            {
                var seg = set.Segments[s];
                links[seg.A - first].Add(seg.B - first);
                links[seg.B - first].Add(seg.A - first);
            }
            // The main end: the widest chamber (its radius is the cave's full chamber size; the others are smaller).
            int mainEnd = -1;
            for (int i = 0; i < count; i++)
                if (kinds[i] == CaveNodeKind.Chamber && (mainEnd < 0 || nodes[i].Radius > nodes[mainEnd].Radius))
                    mainEnd = i;
            var pools = new CavePool[c.PoolCount];
            for (int k = 0; k < pools.Length; k++) pools[k] = set.Pools[c.FirstPool + k];
            var shafts = new List<float2>();
            for (int i = 0; i < count; i++)
                if (kinds[i] == CaveNodeKind.Shaft) shafts.Add(nodes[i].Position.xz);
            // Bat folk chambers (and the ground round them) are theirs.
            var folk = new List<(int node, float3 centre, float radius)>();
            if (source.BatFolk.IsCreated)
            {
                ref var sites = ref source.BatFolk.Value.Sites;
                for (int k = 0; k < sites.Length; k++)
                    if (sites[k].Cave == cave) folk.Add((sites[k].Chamber - first, sites[k].Centre, sites[k].Radius));
            }

            var cands = new List<Candidate>();
            bool Dark(float3 p)
            {
                if (math.distance(p, c.Mouth) < DarkBeyond) return false;
                foreach (var s in shafts) if (math.distance(p.xz, s) < 6f) return false;
                foreach (var pool in pools) if (pool.Lava > 0.5f && math.distance(p.xz, pool.Centre.xz) < pool.Radius + 6f) return false;
                return true;
            }
            bool Blocked(float3 p, bool rim)
            {
                foreach (var f in folk) if (math.distance(p, f.centre) < f.radius + 2f) return true;
                foreach (var pool in pools) if (math.distance(p.xz, pool.Centre.xz) < pool.Radius * (rim && pool.Lava < 0.5f ? 1.15f : 1.3f)) return true;
                return false;
            }
            // A floor spot near (x, z), looked for from height y (in the air): null if it isn't a cave floor near y.
            float? FloorAt(float x, float y, float z, float below)
            {
                var f = source.FloorUnder(new float3(x, y, z), out var inCave);
                if (!inCave || f > y - 0.3f || f < y - below) return null;
                return f;
            }
            void Add(float3 p, float2 face, SocketKind kind, SocketSize size, SocketTags tags, bool rim = false)
            {
                if (Blocked(p, rim)) return;
                // In the air with room to stand (speleothems and boulders are rock in the field too).
                if (source.CaveDistance(p + new float3(0f, kind == SocketKind.WallDecor ? 0f : 0.5f, 0f)) > -0.1f) return;
                cands.Add(new Candidate { P = p, Face = face, Kind = kind, Size = size, Tags = tags | (Dark(p) ? SocketTags.Dark : SocketTags.None) });
            }

            chambers = deadEnds = 0;
            for (int i = 0; i < count; i++)
            {
                if (kinds[i] != CaveNodeKind.Chamber || i == 0) continue;
                if (folk.Exists(site => site.node == i)) continue;
                var node = nodes[i];
                float r = node.Radius * c.Profile.x, h = node.Radius * c.Profile.y;
                var floor = FloorAt(node.Position.x, node.Position.y, node.Position.z, h * 1.6f);
                if (floor == null) continue;
                chambers++;
                var fc = floor.Value;
                bool deadEnd = links[i].Count == 1;
                if (deadEnd) deadEnds++;
                var tags = SocketTags.Chamber | (deadEnd ? SocketTags.DeadEnd : SocketTags.None);
                // The way in: towards the linked node nearest the mouth (the lowest index: nodes run outward from it).
                var inward = links[i].Count > 0 ? links[i][0] : 0;
                foreach (var l in links[i]) if (l < inward) inward = l;
                var toIn = math.normalizesafe(nodes[inward].Position.xz - node.Position.xz, new float2(1f, 0f));
                var centre = new float3(node.Position.x, fc, node.Position.z);

                var size = r >= 8f ? SocketSize.Large : r >= 4.5f ? SocketSize.Medium : SocketSize.Small;
                Add(centre + new float3(0f, 0.05f, 0f), toIn, SocketKind.Creature, size, tags);
                var ceil = Up(source, centre + new float3(0f, 1f, 0f), h * 2.5f);
                if (ceil - fc >= 4f)
                    Add(new float3(centre.x, ceil - 0.3f, centre.z), toIn, SocketKind.Creature, SocketSize.Small, tags | SocketTags.Ceiling);

                // Round the chamber: walls found by stepping out at waist height, alternately clutter on the floor and
                // decor on the wall, starting at an angle set by the cave's Key and the node.
                int dirs = r >= 8f ? 8 : 6;
                var rng = Unity.Mathematics.Random.CreateFromIndex(c.Key ^ (uint)(i * 7919));
                var a0 = rng.NextFloat(math.PI * 2f);
                var walls = new float[dirs];
                var dirv = new float2[dirs];
                for (int k = 0; k < dirs; k++)
                {
                    var a = a0 + k * math.PI * 2f / dirs;
                    dirv[k] = new float2(math.cos(a), math.sin(a));
                    walls[k] = Out(source, centre + new float3(0f, 1f, 0f), dirv[k], r * 1.4f);
                }
                for (int k = 0; k < dirs; k++)
                {
                    var d = dirv[k];
                    if (walls[k] < 1.5f) continue;
                    if (k % 2 == 0)
                    {
                        var at = centre.xz + d * walls[k] * 0.7f;
                        if (FloorAt(at.x, fc + 1.2f, at.y, 2.5f) is float f)
                            Add(new float3(at.x, f + 0.05f, at.y), -d, SocketKind.Clutter, SocketSize.Small, tags);
                    }
                    else
                    {
                        var at = centre.xz + d * (walls[k] - 0.25f);
                        if (FloorAt(at.x, fc + 1.2f, at.y, 2.5f) is float f)
                            Add(new float3(at.x, f + math.min(1.5f, (ceil - f) * 0.5f), at.y), -d, SocketKind.WallDecor, walls[k] >= 4f ? SocketSize.Medium : SocketSize.Small, tags);
                    }
                }
                // A big chamber holds two more, part way out.
                if (r >= 7f)
                    for (int k = 1; k < dirs; k += dirs / 2)
                    {
                        var at = centre.xz + dirv[k] * walls[k] * 0.45f;
                        if (FloorAt(at.x, fc + 1.2f, at.y, 2.5f) is float f)
                            Add(new float3(at.x, f + 0.05f, at.y), -dirv[k], SocketKind.Creature, SocketSize.Medium, tags);
                    }
                // A chest against the wall farthest from the way in, in dead ends and at the main end.
                if (deadEnd || i == mainEnd)
                {
                    int far = 0;
                    for (int k = 1; k < dirs; k++)
                        if (math.dot(dirv[k], toIn) < math.dot(dirv[far], toIn)) far = k;
                    var at = centre.xz + dirv[far] * math.max(0.5f, walls[far] - 0.9f);
                    if (FloorAt(at.x, fc + 1.2f, at.y, 2.5f) is float f)
                        Add(new float3(at.x, f + 0.05f, at.y), -dirv[far], SocketKind.Chest, i == mainEnd ? SocketSize.Large : SocketSize.Medium, tags | SocketTags.NearChest);
                }
            }

            // Passages: a guard where each one opens into a chamber, then clutter and decor along alternate sides.
            for (int s = c.FirstSegment; s < c.FirstSegment + c.SegmentCount; s++)
            {
                var seg = set.Segments[s];
                int a = seg.A - first, b = seg.B - first;
                if (kinds[a] == CaveNodeKind.Shaft || kinds[b] == CaveNodeKind.Shaft) continue;
                CaveNode na = nodes[a], nb = nodes[b];
                var along = nb.Position.xz - na.Position.xz;
                var len = math.length(along);
                if (len < 1f) continue;
                var dir = along / len;
                var side = new float2(-dir.y, dir.x);
                // Skip the stretch inside a chamber at either end (W-115 row 2: or the doorway and wall of a room).
                float End(CaveNodeKind kind, CaveNode node) =>
                    kind == CaveNodeKind.Chamber ? node.Radius * c.Profile.x : kind == CaveNodeKind.Room ? CaveRoom.Wall + 1.5f : 2f;
                float t0 = End(kinds[a], na);
                float t1 = len - End(kinds[b], nb);
                if ((kinds[b] == CaveNodeKind.Chamber || kinds[b] == CaveNodeKind.Room) && t1 > t0 + 1f)
                {
                    var p = math.lerp(na.Position, nb.Position, t1 / len);
                    if (FloorAt(p.x, p.y, p.z, nb.Radius * c.Profile.y * 1.8f) is float f && math.distance(p, c.Mouth) > 6f)
                        Add(new float3(p.x, f + 0.05f, p.z), -dir, SocketKind.Creature, na.Radius * c.Profile.x >= 3f ? SocketSize.Medium : SocketSize.Small, SocketTags.Passage | SocketTags.Doorway);
                }
                int n = 0;
                for (float t = t0 + 3f; t < t1 - 1f; t += 7f, n++)
                {
                    var p = math.lerp(na.Position, nb.Position, t / len);
                    if (math.distance(p, c.Mouth) < 6f) continue;
                    var radius = math.lerp(na.Radius, nb.Radius, t / len);
                    var f0 = FloorAt(p.x, p.y, p.z, radius * c.Profile.y * 1.8f);
                    if (f0 == null) continue;
                    var sign = n % 2 == 0 ? 1f : -1f;
                    var wall = Out(source, new float3(p.x, f0.Value + 1f, p.z), side * sign, radius * c.Profile.x * 1.4f);
                    if (wall < 1.2f) continue;
                    var at = p.xz + side * sign * wall * 0.65f;
                    if (FloorAt(at.x, f0.Value + 1.2f, at.y, 2.5f) is float fc)
                        Add(new float3(at.x, fc + 0.05f, at.y), -side * sign, SocketKind.Clutter, SocketSize.Small, SocketTags.Passage);
                    if (n % 2 == 0)
                    {
                        var w = p.xz - side * sign * (Out(source, new float3(p.x, f0.Value + 1f, p.z), -side * sign, radius * c.Profile.x * 1.4f) - 0.25f);
                        if (FloorAt(w.x, f0.Value + 1.2f, w.y, 2.5f) is float fw)
                            Add(new float3(w.x, fw + 1.4f, w.y), side * sign, SocketKind.WallDecor, SocketSize.Small, SocketTags.Passage);
                    }
                    else
                        Add(new float3(p.x, f0.Value + 0.05f, p.z), dir, SocketKind.Creature, radius * c.Profile.x >= 3f ? SocketSize.Medium : SocketSize.Small, SocketTags.Passage);
                }
            }

            // Water pools: something to drink or fish at, a creature come to drink and clutter, round the rim.
            for (int k = 0; k < pools.Length; k++)
            {
                var pool = pools[k];
                if (pool.Lava > 0.5f || RoomPool(ref set, c, c.FirstPool + k)) continue;
                var rng = Unity.Mathematics.Random.CreateFromIndex(c.Key ^ (uint)(k * 104729 + 17));
                var a0 = rng.NextFloat(math.PI * 2f);
                for (int j = 0; j < 3; j++)
                {
                    var a = a0 + j * math.PI * 2f / 3f;
                    var d = new float2(math.cos(a), math.sin(a));
                    var at = pool.Centre.xz + d * (pool.Radius * 1.2f + 0.6f);
                    if (FloorAt(at.x, pool.Centre.y + 2f, at.y, 4f) is not float f) continue;
                    var kind = j == 0 ? SocketKind.Creature : j == 1 ? SocketKind.Interactable : SocketKind.Clutter;
                    Add(new float3(at.x, f + 0.05f, at.y), -d, kind, SocketSize.Small, SocketTags.NearWater | SocketTags.Chamber, true);
                }
            }

            // About Caps[kind] of each kind, picked evenly through the candidates (in the order made, so it's stable).
            var kept = new List<Candidate>();
            for (int kind = 0; kind <= (int)SocketKind.Clutter; kind++)
            {
                var all = cands.FindAll(x => (int)x.Kind == kind);
                if (all.Count <= Caps[kind])
                    kept.AddRange(all);
                else
                    for (int j = 0; j < Caps[kind]; j++)
                        kept.Add(all[j * all.Count / Caps[kind]]);
            }
            // Tags from neighbours: near a chest, near water.
            for (int j = 0; j < kept.Count; j++)
            {
                var x = kept[j];
                foreach (var o in kept)
                    if (o.Kind == SocketKind.Chest && math.distance(x.P, o.P) < 4f) x.Tags |= SocketTags.NearChest;
                foreach (var pool in pools)
                    if (pool.Lava < 0.5f && math.distance(x.P.xz, pool.Centre.xz) < pool.Radius + 2.5f && math.abs(x.P.y - pool.Centre.y) < 3f) x.Tags |= SocketTags.NearWater;
                kept[j] = x;
            }

            // W-115 row 2: the destination chambers' own spots (CaveRoomKit), after the thinning so every room keeps
            // its set: each tagged as a chamber at a dead end, with its room type (and dark beyond the mouth's light).
            for (int i = c.FirstRoom; i < c.FirstRoom + c.RoomCount; i++)
            {
                var room = set.Rooms[i];
                var type = RoomTag(room.Type);
                foreach (var spot in CaveRoomKit.Get(room).Spots)
                {
                    var up = (spot.Tags & SocketTags.Ceiling) != 0 ? -0.3f : spot.Kind == SocketKind.WallDecor ? 0f : 0.5f;
                    if (source.CaveDistance(spot.At + new float3(0f, up, 0f)) > -0.1f) continue;
                    var tags = spot.Tags | SocketTags.Chamber | SocketTags.DeadEnd | type | (Dark(spot.At) ? SocketTags.Dark : SocketTags.None);
                    kept.Add(new Candidate { P = spot.At, Face = spot.Face, Kind = spot.Kind, Size = spot.Size, Tags = tags });
                }
            }

            var result = new List<DungeonSocket>(kept.Count);
            for (int j = 0; j < kept.Count; j++)
            {
                var x = kept[j];
                source.SampleBiomes(x.P.xz, out var biomes, out _);
                result.Add(new DungeonSocket
                {
                    Pos = x.P,
                    Rot = quaternion.LookRotationSafe(new float3(x.Face.x, 0f, x.Face.y), new float3(0f, 1f, 0f)),
                    Kind = x.Kind,
                    Size = (byte)x.Size,
                    Tags = (uint)x.Tags,
                    Seed = DungeonSocket.MakeSeed(worldSeed, c.Key, (uint)j),
                    // A buried ruin room's spots borrow the ruin's tables; the rest of the cave keeps the Cave rows.
                    DungeonType = (x.Tags & SocketTags.RuinRoom) != 0 ? DungeonTypes.Ruin : DungeonTypes.Cave,
                    Biome = (byte)Dominant(biomes),
                });
            }
            return result;
        }

        /// <summary>The socket tag for a <see cref="CaveRoomType"/>.</summary>
        static SocketTags RoomTag(int type) => (CaveRoomType)type switch
        {
            CaveRoomType.Lake => SocketTags.Lake,
            CaveRoomType.Roost => SocketTags.Roost,
            CaveRoomType.Ruin => SocketTags.RuinRoom,
            _ => SocketTags.Grotto,
        };

        /// <summary>Whether a pool (index into the set's pools) is a lake room's: the room's kit sets its own spots round it.</summary>
        static bool RoomPool(ref CaveSetBlob set, in CaveData c, int pool)
        {
            for (int i = c.FirstRoom; i < c.FirstRoom + c.RoomCount; i++)
                if (set.Rooms[i].Pool == pool) return true;
            return false;
        }

        /// <summary>How far out from p (horizontally along dir) the cave's rock starts, up to max.</summary>
        static float Out(TerrainSource source, float3 p, float2 dir, float max)
        {
            for (float t = 0.4f; t < max; t += 0.4f)
                if (source.CaveDistance(p + new float3(dir.x, 0f, dir.y) * t) > -0.15f)
                    return t;
            return max;
        }

        /// <summary>The ceiling's height above p (the first rock straight up), up to max.</summary>
        static float Up(TerrainSource source, float3 p, float max)
        {
            for (float t = 0.4f; t < max; t += 0.4f)
                if (source.CaveDistance(p + new float3(0f, t, 0f)) > -0.15f)
                    return p.y + t;
            return p.y + max;
        }

        static int Dominant(in BiomeWeights b)
        {
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (b.Weights[k] > b.Weights[best]) best = k;
            return b.Ids[best];
        }
    }

    /// <summary>
    /// Makes one dungeon entity per cave once the island is baked (W-115): DungeonInfo (Id the cave's Key, type Cave)
    /// plus its DungeonSocket buffer, for the animal lane's spawner to fill. Runs in every world, so solo, host and
    /// client get the same.
    /// </summary>
    public partial class CaveSocketSystem : SystemBase
    {
        // Built a few caves a frame (about 8 ms each), so the island coming up doesn't hitch for the whole set.
        const int CavesPerFrame = 2;
        int next = -1;
        uint all = 2166136261u;
        int total;
        readonly int[] totals = new int[5];
        double ms;

        protected override void OnCreate() => RequireForUpdate<TerrainSource>();

        protected override void OnUpdate()
        {
            if (next == int.MaxValue || IslandV2.ActiveVersion == 0)
                return;
            var source = SystemAPI.GetSingleton<TerrainSource>();
            if (!source.Caves.IsCreated || IslandV2.ActiveVersion != 1 && !source.Island.IsCreated)
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ref var set = ref source.Caves.Value;
            if (next < 0)
                next = 0;
            var end = math.min(set.Caves.Length, next + CavesPerFrame);
            for (int cave = next; cave < end; cave++)
            {
                // A road tunnel is a passage, not a dungeon: nothing waits in it.
                if (set.Caves[cave].Tunnel != 0)
                    continue;
                var sockets = CaveSockets.Build(source, cave, source.World.Seed, out var chambers, out var deadEnds);
                var hash = TempleSockets.Hash(sockets);
                var key = set.Caves[cave].Key;
                var entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, $"Dungeon (cave {cave})");
                EntityManager.AddComponentData(entity, new DungeonInfo { Id = key, Type = DungeonTypes.Cave, Sockets = sockets.Count, Hash = hash });
                var buffer = EntityManager.AddBuffer<DungeonSocket>(entity);
                var counts = new int[5];
                foreach (var s in sockets)
                {
                    buffer.Add(s);
                    counts[(int)s.Kind]++;
                    totals[(int)s.Kind]++;
                }
                total += sockets.Count;
                all = (all ^ hash) * 16777619u;
                Debug.Log($"[dungeon] cave {cave} (key {key:X8}): {sockets.Count} sockets from {chambers} chambers ({deadEnds} dead ends): creature {counts[0]}, chest {counts[1]}, wall decor {counts[2]}, interactable {counts[3]}, clutter {counts[4]}; hash {hash:X8} ({World.Name})");
            }
            next = end;
            ms += watch.Elapsed.TotalMilliseconds;
            if (next < set.Caves.Length)
                return;
            next = int.MaxValue;
            Debug.Log($"[dungeon] caves: {set.Caves.Length} dungeons, {total} sockets: creature {totals[0]}, chest {totals[1]}, wall decor {totals[2]}, interactable {totals[3]}, clutter {totals[4]}; cave socket hash {all:X8} in {ms:F0} ms over {(set.Caves.Length + CavesPerFrame - 1) / CavesPerFrame} frames ({World.Name})");
        }
    }
}
