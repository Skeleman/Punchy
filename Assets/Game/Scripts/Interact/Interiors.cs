using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>Client → host: the player switched room Room's lamp.</summary>
    public struct UseLampRpc : IRpcCommand { public int Room; }

    /// <summary>Host → everyone: room Room's lamp is on (1) or off (0).</summary>
    public struct LampStateRpc : IRpcCommand { public int Room; public byte On; }

    /// <summary>
    /// House interiors (#25, P-13). One room per house type that has an inside (FloraGenerator.HouseInterior), built off
    /// the island in a row as InteriorCells boxes (the world lane's hook: the player stands on and bumps into them) and
    /// drawn as plain boxes. Each room is seeded from the world seed and its type: its size (85-100% of the house's
    /// inside), and one to three pieces of furniture (a table, a bed, a hearth) against its walls; the same on every
    /// machine, nothing sent. Using an open house door (Doors) takes the player into its type's room; using the room's
    /// door takes them back out in front of the house they came in by. Each room has a lamp: Use switches it, and the
    /// host owns which lamps are on (UseLampRpc / LampStateRpc, as Doors does). Entering and leaving are each player's
    /// own (their own body moves). Test flags: "-enterdoor N" (stand at door N, open it, go in), "-lamp" (switch the
    /// lamp 8 s after going in), "-leaveroom" (come back out 16 s after going in).
    /// </summary>
    [DefaultExecutionOrder(1000)]   // (after WeatherSystem sets the sky's ambient each frame: see LateUpdate)
    public class Interiors : MonoBehaviour
    {
        struct Room
        {
            public FloraSpecies Type;
            public float3 Min, Max;      // inside
            public float3 DoorInside;    // where you stand on coming in (facing -Z)
            public float3 LampAt, SwitchAt;
            public Light Lamp;
            public GameObject Root;
            public string Furniture;
            public bool HasShelf;
            public bool HasHearth;
        }

        static Interiors instance;
        /// <summary>"-enterdoor shelf": the first door whose room has a shelf.</summary>
        const int ShelfDoor = 1_000_000;
        readonly List<Room> rooms = new List<Room>();
        /// <summary>Each door's room (index into rooms), or -1 (a house type with no inside, or not a house).</summary>
        int[] doorRoom = System.Array.Empty<int>();
        readonly HashSet<int> lampsOn = new HashSet<int>();
        readonly HashSet<Entity> sentTo = new HashSet<Entity>();
        World server, client;
        bool built;
        /// <summary>The room the local player is in (-1: outside), and the door they came in by.</summary>
        int inRoom = -1, cameFrom = -1;
        int target;   // 1 the room's door, 2 its lamp switch, 0 nothing
        string prompt;
        GUIStyle promptStyle;
        int enterDoorArg = -1;
        bool lampArg, leaveArg;
        float testAt, enteredAt;
        static Material material;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null)
                return;
            var go = new GameObject("Interiors") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Interiors>();
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-enterdoor");
            if (at >= 0 && at + 1 < args.Length)
                if (!int.TryParse(args[at + 1], out instance.enterDoorArg) && args[at + 1] == "shelf")
                    instance.enterDoorArg = ShelfDoor;
            instance.lampArg = System.Array.IndexOf(args, "-lamp") >= 0;
            instance.leaveArg = System.Array.IndexOf(args, "-leaveroom") >= 0;
        }

        /// <summary>Whether door <paramref name="door"/>'s house has a room to go into.</summary>
        public static bool CanEnter(int door) => instance != null && door >= 0 && door < instance.doorRoom.Length && instance.doorRoom[door] >= 0;

        /// <summary>Into door <paramref name="door"/>'s room (the local player). False if its house has no room.</summary>
        public static bool Enter(int door) => instance != null && instance.EnterRoom(door);

        bool Hosting => server != null && server.IsCreated && server.IsServer();
        bool Joined => !Hosting && client != null && client.IsCreated && client.IsClient();

        void Update()
        {
            if (NetWorlds.Server != server || NetWorlds.Client != client)
            {
                (server, client) = (NetWorlds.Server, NetWorlds.Client);
                sentTo.Clear();
                if (Joined)
                    lampsOn.Clear();
                inRoom = -1;
            }
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            if (!built && !Build(world))
                return;
            if (Hosting)
                Serve();
            if (client != null && client.IsCreated && client.IsClient())
                Receive();
            for (int r = 0; r < rooms.Count; r++)
                if (rooms[r].Lamp != null)
                    rooms[r].Lamp.enabled = lampsOn.Contains(r);

            FindTarget();
            if (target != 0 && UseKey.WasPressed())
            {
                if (target == 1) Leave();
                else SwitchLamp(inRoom);
            }
            Tests();
        }

        /// <summary>The rooms, once the island and its settlements are in: one per house type with an inside.</summary>
        bool Build(World world)
        {
            var em = world.EntityManager;
            using var tq = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var pq = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!tq.TryGetSingleton<TerrainSource>(out var terrain) || pq.CalculateEntityCount() != 1 || IslandV2.ActiveVersion == 0 ||
                !terrain.Settlements.IsCreated || !terrain.Anchors.IsCreated)
                return false;
            var palette = em.GetComponentObject<BiomePalette>(pq.GetSingletonEntity());
            if (palette?.Landmarks == null)
                return false;
            built = true;
            var buildings = SettlementBaking.Buildings(palette.Landmarks);
            ref var set = ref terrain.Settlements.Value;

            // Each door anchor's plot (the door stands 0.75 x the plot's half depth + 1.4 m out from its centre).
            ref var anchors = ref terrain.Anchors.Value.Anchors;
            var doors = new List<float2>();
            for (int i = 0; i < anchors.Length; i++)
                if (anchors[i].Kind == AnchorKind.Door)
                    doors.Add(anchors[i].Position.xz);
            doorRoom = new int[doors.Count];
            var roomOf = new Dictionary<FloraSpecies, int>();
            InteriorCells.Clear();
            var row = terrain.World.WorldSize * 0.5f + 500f;
            var floorY = terrain.World.SeaLevel + 40f;
            var matched = 0;
            for (int d = 0; d < doors.Count; d++)
            {
                doorRoom[d] = -1;
                // (The plot it stands in front of: on the line out from the plot's centre along its facing, no further
                // than its depth and 3 m; AnchorBaking puts a house's door at its form's own door depth, so no one offset.)
                var best = -1;
                var bestAcross = 0.4f;
                for (int p = 0; p < set.Plots.Length; p++)
                {
                    var plot = set.Plots[p];
                    if (plot.Building < 0 || plot.Building >= buildings.Count)
                        continue;
                    var to = doors[d] - plot.Centre;
                    var along = math.dot(to, plot.Facing);
                    var across = math.abs(math.dot(to, new float2(-plot.Facing.y, plot.Facing.x)));
                    if (along > 0f && along < plot.Half.y * 2f + 3f && across < bestAcross)
                        (bestAcross, best) = (across, p);
                }
                if (best >= 0)
                {
                    var plot = set.Plots[best];
                    matched++;
                    var species = buildings[plot.Building].Species;
                    if (species == null)
                        continue;
                    if (!roomOf.TryGetValue(species, out var r))
                    {
                        r = -1;
                        if (FloraGenerator.HouseInterior(species, out _, out var inside))
                        {
                            r = rooms.Count;
                            rooms.Add(MakeRoom(species, inside, new float3(row + r * 40f, floorY, 0f), terrain.World.Seed, r));
                        }
                        roomOf[species] = r;
                    }
                    doorRoom[d] = r;
                }
            }
            var enterable = 0;
            foreach (var r in doorRoom)
                if (r >= 0) enterable++;
            Debug.Log($"Interiors: {rooms.Count} rooms (one per house type with an inside), {InteriorCells.Count} boxes; {doors.Count} doors, {matched} matched to a house, {enterable} lead inside");
            for (int r = 0; r < rooms.Count; r++)
                Debug.Log($"Interiors: room {r} {rooms[r].Type.name}: {rooms[r].Max.x - rooms[r].Min.x:F1} x {rooms[r].Max.y - rooms[r].Min.y:F1} x {rooms[r].Max.z - rooms[r].Min.z:F1} m, seed {RoomSeed(terrain.World.Seed, r):X8}, {rooms[r].Furniture}");
            return true;
        }

        static uint RoomSeed(uint worldSeed, int room) => math.hash(new uint2(worldSeed, (uint)room * 7919u + 0x25u));

        Room MakeRoom(FloraSpecies type, Vector3 inside, float3 centre, uint worldSeed, int index)
        {
            var rng = Unity.Mathematics.Random.CreateFromIndex(RoomSeed(worldSeed, index));
            // (85-100% of the house's inside, at least big enough to turn round in.)
            var size = new float3(math.max(inside.x * rng.NextFloat(0.85f, 1f), 3f), math.clamp(inside.y, 2.4f, 3.4f), math.max(inside.z * rng.NextFloat(0.85f, 1f), 3f));
            var min = centre - new float3(size.x * 0.5f, 0f, size.z * 0.5f);
            var max = centre + new float3(size.x * 0.5f, size.y, size.z * 0.5f);
            var before = InteriorCells.Count;
            InteriorCells.AddRoom(min, max, 0.3f, 1.2f, math.min(2.1f, size.y - 0.2f));
            var root = new GameObject($"Room {index} ({type.name})") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(root);
            for (int b = before; b < InteriorCells.Count; b++)
            {
                var box = InteriorCells.Get(b);
                var colour = box.Part == InteriorPart.Floor ? new Color(0.36f, 0.26f, 0.18f) : box.Part == InteriorPart.Ceiling ? new Color(0.3f, 0.26f, 0.22f) : new Color(0.72f, 0.66f, 0.55f);
                Block(root.transform, (box.Min + box.Max) * 0.5f, box.Max - box.Min, colour);
            }
            // Furniture: one to three pieces against the back and side walls (not by the door, on +Z).
            var pieces = new List<string>();
            var kinds = new[] { "table", "bed", "hearth" };
            var count = rng.NextInt(1, 4);
            var start = rng.NextInt(0, 3);
            for (int k = 0; k < count; k++)
            {
                var kind = kinds[(start + k) % 3];
                float3 half = kind == "table" ? new float3(0.6f, 0.38f, 0.4f) : kind == "bed" ? new float3(0.5f, 0.25f, 1f) : new float3(0.55f, 0.45f, 0.3f);
                // Back wall (-Z) for the hearth; the bed and table along a side wall, toward the back.
                var x = kind == "hearth" ? centre.x + rng.NextFloat(-0.3f, 0.3f) * size.x : (k % 2 == 0 ? min.x + half.x + 0.1f : max.x - half.x - 0.1f);
                var z = kind == "hearth" ? min.z + half.z + 0.05f : min.z + half.z + 0.2f + rng.NextFloat(0f, math.max(size.z * 0.35f - half.z, 0f));
                var lo = new float3(x - half.x, min.y, z - half.z);
                var hi = new float3(x + half.x, min.y + half.y * 2f, z + half.z);
                InteriorCells.Add(new InteriorBox(lo, hi, InteriorPart.Wall));
                var colour = kind == "table" ? new Color(0.5f, 0.34f, 0.2f) : kind == "bed" ? new Color(0.62f, 0.55f, 0.42f) : new Color(0.35f, 0.33f, 0.32f);
                Block(root.transform, (lo + hi) * 0.5f, hi - lo, colour);
                if (kind == "hearth")
                    Block(root.transform, new float3(x, min.y + 0.35f, z + half.z * 0.6f), new float3(0.5f, 0.3f, 0.1f), new Color(1f, 0.45f, 0.1f));
                pieces.Add(kind);
            }
            // P-14: storage, seeded: 0-2 of shelf / chest against the side walls' front halves (by the door), each a
            // container (Items) with 0-3 starter items, now and then a surprise.
            var storage = new List<string>();
            var stores = rng.NextInt(0, 3);
            for (int k = 0; k < stores; k++)
            {
                var shelf = rng.NextBool();
                float3 half = shelf ? new float3(0.2f, 0.8f, 0.6f) : new float3(0.25f, 0.25f, 0.4f);
                var x = k == 0 ? min.x + half.x + 0.05f : max.x - half.x - 0.05f;
                var z = max.z - 1.6f - half.z - rng.NextFloat(0f, math.max(size.z * 0.3f - half.z, 0f));
                var lo = new float3(x - half.x, min.y, z - half.z);
                var hi = new float3(x + half.x, min.y + half.y * 2f, z + half.z);
                InteriorCells.Add(new InteriorBox(lo, hi, InteriorPart.Wall));
                Block(root.transform, (lo + hi) * 0.5f, hi - lo, shelf ? new Color(0.45f, 0.32f, 0.2f) : new Color(0.42f, 0.27f, 0.14f));
                if (shelf)
                    for (int b = 1; b < 4; b++)
                        Block(root.transform, new float3(x + (k == 0 ? 0.05f : -0.05f), min.y + b * 0.4f, z), new float3(0.34f, 0.04f, 1.2f), new Color(0.6f, 0.45f, 0.3f));
                var id = Punchy.Items.Items.RoomBase + index * 4 + k;
                var front = new float3(x + (k == 0 ? 0.7f : -0.7f), min.y, z);
                Punchy.Items.Items.RegisterContainer(id, front, shelf ? "shelf" : "chest", math.hash(new uint3(RoomSeed(worldSeed, index), (uint)k, 0x5E1Fu)));
                storage.Add($"{(shelf ? "shelf" : "chest")} {id}: {Punchy.Items.Items.DescribeContainer(id)}");
            }
            pieces.Add(storage.Count > 0 ? "storage: " + string.Join("; ", storage) : "no storage");
            // The lamp hangs in the middle under the ceiling; its switch is on the door wall, beside the door.
            var lampAt = new float3(centre.x, max.y - 0.35f, centre.z);
            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(root.transform, false);
            lampGo.transform.position = lampAt;
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.82f, 0.55f);
            lamp.range = math.max(size.x, size.z) * 1.4f;
            lamp.intensity = 2.5f;
            lamp.enabled = false;
            Block(root.transform, lampAt + new float3(0f, 0.12f, 0f), new float3(0.25f, 0.2f, 0.25f), new Color(0.9f, 0.8f, 0.5f));
            var switchAt = new float3(centre.x + 1.0f, min.y + 1.2f, max.z - 0.05f);
            Block(root.transform, switchAt, new float3(0.12f, 0.18f, 0.06f), new Color(0.85f, 0.75f, 0.3f));
            return new Room
            {
                Type = type, Min = min, Max = max, Root = root, Lamp = lamp, LampAt = lampAt, SwitchAt = switchAt,
                DoorInside = new float3(centre.x, min.y + 0.05f, max.z - 1.0f),
                Furniture = pieces.Count > 0 ? string.Join(", ", pieces) : "bare",
                HasShelf = storage.Exists(x => x.StartsWith("shelf")),
                HasHearth = pieces.Contains("hearth"),
            };
        }

        static void Block(Transform parent, float3 centre, float3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.hideFlags = HideFlags.HideInHierarchy;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            material ??= Resources.Load<Material>("ItemMaterial");
            if (material != null)
                r.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, colour);
            r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ the local player

        bool TryPlayer(out Entity entity, out LocalTransform transform)
        {
            entity = Entity.Null;
            transform = default;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out entity))
                return false;
            transform = em.GetComponentData<LocalTransform>(entity);
            return true;
        }

        void Place(float3 at, float yawRadians)
        {
            if (!TryPlayer(out var e, out var t))
                return;
            t.Position = at;
            t.Rotation = quaternion.RotateY(yawRadians);
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            em.SetComponentData(e, t);
            em.SetComponentData(e, new PlayerMotion { Grounded = true });
            var cam = Object.FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(math.degrees(yawRadians), 18f);
        }

        bool EnterRoom(int door)
        {
            if (!CanEnter(door))
                return false;
            var r = doorRoom[door];
            cameFrom = door;
            inRoom = r;
            enteredAt = Time.realtimeSinceStartup;
            Place(rooms[r].DoorInside, math.PI);   // facing -Z, into the room
            Debug.Log($"Interiors: went into room {r} ({rooms[r].Type.name}) by door {door} ({(Hosting ? "host" : Joined ? "client" : "solo")})");
            return true;
        }

        void Leave()
        {
            if (inRoom < 0 || !Doors.TryGetDoor(cameFrom, out var p, out var f))
                return;
            var xz = p.xz + math.normalizesafe(f) * 1.2f;
            var world = World.DefaultGameObjectInjectionWorld;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            var y = q.TryGetSingleton<TerrainSource>(out var terrain) ? terrain.SampleHeight(xz) + 0.05f : p.y;
            Place(new float3(xz.x, y, xz.y), math.atan2(f.x, f.y));
            Debug.Log($"Interiors: came out of room {inRoom} at door {cameFrom} ({(Hosting ? "host" : Joined ? "client" : "solo")})");
            inRoom = -1;
        }

        void FindTarget()
        {
            target = 0;
            prompt = null;
            if (!TryPlayer(out _, out var player))
                return;
            // (Which room the player is in, by position: a join or a respawn can move them.)
            inRoom = -1;
            for (int r = 0; r < rooms.Count; r++)
                if (math.all(player.Position >= rooms[r].Min - 0.5f) && math.all(player.Position <= rooms[r].Max + 0.5f))
                    inRoom = r;
            if (inRoom < 0)
                return;
            var room = rooms[inRoom];
            var doorGap = new float3((room.Min.x + room.Max.x) * 0.5f, room.Min.y, room.Max.z);
            if (math.distance(player.Position.xz, doorGap.xz) < 1.6f && cameFrom >= 0)
            {
                target = 1;
                prompt = $"{UseKey.Name}  go outside";
            }
            else if (math.distance(player.Position.xz, room.SwitchAt.xz) < 1.8f)
            {
                target = 2;
                prompt = $"{UseKey.Name}  {(lampsOn.Contains(inRoom) ? "put out" : "light")} the lamp";
            }
        }

        void SwitchLamp(int room)
        {
            if (room < 0)
                return;
            if (Joined)
            {
                var em = client.EntityManager;
                using var q = em.CreateEntityQuery(typeof(NetworkId));
                if (q.IsEmpty)
                    return;
                var e = em.CreateEntity(typeof(UseLampRpc), typeof(SendRpcCommandRequest));
                em.SetComponentData(e, new UseLampRpc { Room = room });
                em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = q.GetSingletonEntity() });
                Debug.Log($"Interiors: asked the host to switch room {room}'s lamp");
                return;
            }
            SetLamp(room, !lampsOn.Contains(room), Hosting ? "host" : "solo");
        }

        void SetLamp(int room, bool on, string who)
        {
            if (on ? !lampsOn.Add(room) : !lampsOn.Remove(room))
                return;
            Debug.Log($"Interiors: room {room}'s lamp {(on ? "on" : "off")} ({who})");
            if (Hosting)
                Broadcast(room, on);
        }

        // ------------------------------------------------------------------ host / client

        void Serve()
        {
            var em = server.EntityManager;
            using (var q = em.CreateEntityQuery(typeof(UseLampRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var room = em.GetComponentData<UseLampRpc>(r).Room;
                    if (room >= 0 && room < rooms.Count)
                        SetLamp(room, !lampsOn.Contains(room), "host, for a client");
                    em.DestroyEntity(r);
                }
            using (var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame)))
            using (var cs = q.ToEntityArray(Allocator.Temp))
                foreach (var c in cs)
                {
                    if (!sentTo.Add(c))
                        continue;
                    foreach (var room in lampsOn)
                        Send(em, c, room, true);
                }
        }

        void Broadcast(int room, bool on)
        {
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            using var cs = q.ToEntityArray(Allocator.Temp);
            foreach (var c in cs)
                Send(em, c, room, on);
        }

        static void Send(EntityManager em, Entity connection, int room, bool on)
        {
            var e = em.CreateEntity(typeof(LampStateRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(e, new LampStateRpc { Room = room, On = (byte)(on ? 1 : 0) });
            em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }

        void Receive()
        {
            var em = client.EntityManager;
            using var q = em.CreateEntityQuery(typeof(LampStateRpc), typeof(ReceiveRpcCommandRequest));
            using var rs = q.ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                var m = em.GetComponentData<LampStateRpc>(r);
                if (Joined)
                    SetLamp(m.Room, m.On != 0, "from the host");
                em.DestroyEntity(r);
            }
        }

        // ------------------------------------------------------------------ tests and drawing

        void Tests()
        {
            if (enterDoorArg >= 0)
            {
                testAt = testAt == 0f ? Time.realtimeSinceStartup + 30f : testAt;
                if (Time.realtimeSinceStartup < testAt)
                    return;
                if (Joined)
                {
                    using var q = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamInGame));
                    if (q.IsEmpty)
                        return;
                }
                var door = enterDoorArg;
                enterDoorArg = -1;
                if (door == ShelfDoor)
                    door = System.Array.FindIndex(doorRoom, r => r >= 0 && rooms[r].HasShelf);
                Debug.Log($"Interiors: -enterdoor {door}: {(CanEnter(door) ? $"room {doorRoom[door]}" : "no room for that house")}");
                if (!EnterRoom(door) && doorRoom.Length > 0)
                {
                    // (That house has no inside: the first door that has.)
                    for (int d = 0; d < doorRoom.Length; d++)
                        if (doorRoom[d] >= 0 && EnterRoom(d))
                            break;
                }
                return;
            }
            if (inRoom < 0 || enteredAt == 0f)
                return;
            if (lampArg && Time.realtimeSinceStartup > enteredAt + 8f)
            {
                lampArg = false;
                SwitchLamp(inRoom);
            }
            if (leaveArg && Time.realtimeSinceStartup > enteredAt + 16f)
            {
                leaveArg = false;
                Leave();
            }
        }

        float indoors;
        int ambientRoom = -1;
        bool ambientLamp;

        /// <summary>
        /// P-16: a room's own light. Out in the open past the island, a room took the sky's ambient (WeatherSystem sets it
        /// every frame), so it went bright at noon and black at night. Inside, the ambient blends (over 0.6 s) to a dim,
        /// warm, steady colour, a little brighter with the lamp lit or a hearth; at the door it blends back to the sky's.
        /// </summary>
        void LateUpdate()
        {
            indoors = Mathf.MoveTowards(indoors, inRoom >= 0 ? 1f : 0f, Time.deltaTime / 0.6f);
            if (indoors <= 0f)
            {
                ambientRoom = -1;
                return;
            }
            var room = inRoom >= 0 ? inRoom : ambientRoom;
            if (room < 0 || room >= rooms.Count)
                return;
            var lamp = lampsOn.Contains(room);
            var warm = new Color(0.17f, 0.13f, 0.09f) + (lamp ? new Color(0.1f, 0.075f, 0.045f) : Color.clear) + (rooms[room].HasHearth ? new Color(0.05f, 0.03f, 0.01f) : Color.clear);
            if (room != ambientRoom || lamp != ambientLamp)
            {
                (ambientRoom, ambientLamp) = (room, lamp);
                Debug.Log($"Interiors: room {room}'s own ambient ({warm.r:F2}, {warm.g:F2}, {warm.b:F2}){(lamp ? ", lamp lit" : "")}{(rooms[room].HasHearth ? ", hearth" : "")}");
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, warm * 1.1f, indoors);
            RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor, warm, indoors);
            RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor, warm * 0.7f, indoors);
        }

        void OnGUI()
        {
            if (prompt == null || Punchy.UI.Overlay.AnyOpen)
                return;
            promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            var size = promptStyle.CalcSize(new GUIContent(prompt)) + new Vector2(24f, 10f);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.72f, size.x, size.y), prompt, promptStyle);
        }
    }
}
