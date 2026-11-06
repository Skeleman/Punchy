using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Dungeon
{
    /// <summary>Dungeon types, for <see cref="DungeonSocket.DungeonType"/> (the spawner's tables key on it).</summary>
    public static class DungeonTypes
    {
        /// <summary>A natural cave (W-115): sockets from its chambers, passages and pools (CaveSockets). 0 is the spawner's Cave row.</summary>
        public const ushort Cave = 0;
        public const ushort SunkenTemple = 1;
    }

    /// <summary>On a dungeon's entity, next to its DungeonSocket buffer: which dungeon, and the sockets' count and hash.</summary>
    public struct DungeonInfo : IComponentData
    {
        public uint Id;
        public ushort Type;
        public int Sockets;
        /// <summary>FNV-1a of every socket (position to the cm, kind, size, tags, seed): equal runs give equal hashes.</summary>
        public uint Hash;
    }

    /// <summary>
    /// The first dungeon (#24, P-7): the sunken temple's own kit rooms, with sockets made from its baked layout
    /// (RuinRooms, RuinBoxes, RuinSconces, in the temple's frame). Rooms don't say which kit piece they came from, so
    /// they're read by shape: a hall is long and narrow, a dead end is a closed room touching one other, a doorway is
    /// where two rooms touch, a pedestal is a box in a room well under its ceiling, and a room with no torch is dark.
    /// Candidates of each kind are thinned evenly to about <see cref="PerKind"/>; the result is the same on every
    /// machine (no randomness: the seeds come from MakeSeed). Pools (NearWater) wait for a world-lane tile.
    /// </summary>
    public static class TempleSockets
    {
        public const int PerKind = 15;

        struct Candidate
        {
            public float3 Q;     // local (u, y above Base, v)
            public float2 Face;  // local facing (u, v)
            public SocketKind Kind;
            public SocketSize Size;
            public SocketTags Tags;
            public int Room;
        }

        public static List<DungeonSocket> Build(ref LandmarkSetBlob set, int temple, uint worldSeed, byte biome)
        {
            var layout = SunkenTempleShape.Layout(set.Instances[temple]);
            int n = set.RuinRooms.Length;
            var rooms = new RuinRoom[n];
            for (int i = 0; i < n; i++) rooms[i] = set.RuinRooms[i];
            // (Copied out of the blob: the local functions below can't read a ref parameter.)
            var drifts = new RuinDrift[set.RuinDrifts.Length];
            for (int k = 0; k < drifts.Length; k++) drifts[k] = set.RuinDrifts[k];

            // Doorways: where two rooms touch along at least half a metre (rectangles 25 cm apart or overlapping).
            var neighbours = new int[n];
            var doors = new List<(float3 q, int a, int b)>();
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    RuinRoom ra = rooms[a], rb = rooms[b];
                    float ou = math.min(ra.U1, rb.U1) - math.max(ra.U0, rb.U0), ov = math.min(ra.V1, rb.V1) - math.max(ra.V0, rb.V0);
                    if (ou < -0.25f || ov < -0.25f || math.max(ou, ov) < 0.5f)
                        continue;
                    neighbours[a]++;
                    neighbours[b]++;
                    float u = (math.min(ra.U1, rb.U1) + math.max(ra.U0, rb.U0)) * 0.5f, v = (math.min(ra.V1, rb.V1) + math.max(ra.V0, rb.V0)) * 0.5f;
                    doors.Add((new float3(u, math.max(ra.FloorAt(u), rb.FloorAt(u)), v), a, b));
                }

            var cands = new List<Candidate>();
            for (int i = 0; i < n; i++)
            {
                var r = rooms[i];
                float lu = r.U1 - r.U0, lv = r.V1 - r.V0, longer = math.max(lu, lv), shorter = math.min(lu, lv);
                float cu = (r.U0 + r.U1) * 0.5f, cv = (r.V0 + r.V1) * 0.5f;
                float Floor(float u, float v) => r.FloorAt(u) + Sand(drifts, i, u, v);
                var headroom = r.Ceil - Floor(cu, cv);
                bool hall = longer >= 2.2f * shorter && shorter <= 5f;
                bool deadEnd = neighbours[i] == 1 && r.Kind == 0;
                bool dark = true;
                for (int k = 0; k < set.RuinSconces.Length && dark; k++)
                {
                    var s = set.RuinSconces[k].At;
                    if (s.x >= r.U0 - 0.5f && s.x <= r.U1 + 0.5f && s.z >= r.V0 - 0.5f && s.z <= r.V1 + 0.5f)
                        dark = false;
                }
                var roomTags = dark ? SocketTags.Dark : SocketTags.None;
                void Add(float u, float v, float up, float2 face, SocketKind kind, SocketSize size, SocketTags tags = SocketTags.None) =>
                    cands.Add(new Candidate { Q = new float3(u, Floor(u, v) + up, v), Face = face, Kind = kind, Size = size, Tags = roomTags | tags, Room = i });

                // The way along the room, and across it.
                var along = lu >= lv ? new float2(1f, 0f) : new float2(0f, 1f);
                var across = new float2(along.y, along.x);
                if (hall)
                {
                    // Creatures down the middle every 4 m; clutter by alternate walls every 3 m, decor on the far wall.
                    float2 At(float t, float off) => lu >= lv ? new float2(r.U0 + t, cv + off) : new float2(cu + off, r.V0 + t);
                    for (float t = 1.5f; t <= longer - 1.5f; t += 4f)
                    {
                        var c = At(t, 0f);
                        Add(c.x, c.y, 0.05f, along, SocketKind.Creature, shorter >= 3f ? SocketSize.Medium : SocketSize.Small);
                    }
                    int side = 0;
                    for (float t = 1f; t <= longer - 1f; t += 3f, side ^= 1)
                    {
                        var sign = side == 0 ? -1f : 1f;
                        var c = At(t, sign * (shorter * 0.5f - 0.5f));
                        Add(c.x, c.y, 0.05f, across * -sign, SocketKind.Clutter, SocketSize.Small);
                        if (side == 0)
                        {
                            var w = At(t, shorter * 0.5f - 0.05f);
                            Add(w.x, w.y, math.min(1.6f, headroom - 0.5f), -across, SocketKind.WallDecor, SocketSize.Small);
                        }
                    }
                    // P-9: a closed hall 6 m or longer keeps a chest at its end farthest from its doorways (an alcove).
                    if (r.Kind == 0 && longer >= 6f && !deadEnd)
                    {
                        float2 endA = At(0.8f, 0f), endB = At(longer - 0.8f, 0f);
                        float dA = float.MaxValue, dB = float.MaxValue;
                        foreach (var d in doors)
                            if (d.a == i || d.b == i)
                            {
                                dA = math.min(dA, math.distance(endA, d.q.xz));
                                dB = math.min(dB, math.distance(endB, d.q.xz));
                            }
                        var end = dA >= dB ? endA : endB;
                        Add(end.x, end.y, 0.05f, math.normalizesafe(new float2(cu, cv) - end), SocketKind.Chest, SocketSize.Medium, SocketTags.NearChest);
                    }
                }
                else
                {
                    // A room: a creature in the middle (large in a big room), clutter in the corners, decor mid-wall.
                    var area = lu * lv;
                    Add(cu, cv, 0.05f, along, SocketKind.Creature, area >= 40f ? SocketSize.Large : SocketSize.Medium);
                    if (headroom >= 4f)
                        Add(cu, cv, headroom - 0.2f, along, SocketKind.Creature, SocketSize.Small, SocketTags.Ceiling);
                    for (int c = 0; c < 4; c++)
                    {
                        float u = c % 2 == 0 ? r.U0 + 0.8f : r.U1 - 0.8f, v = c < 2 ? r.V0 + 0.8f : r.V1 - 0.8f;
                        Add(u, v, 0.05f, math.normalizesafe(new float2(cu - u, cv - v)), SocketKind.Clutter, SocketSize.Small);
                    }
                    var decorUp = math.min(1.6f, headroom - 0.5f);
                    Add(cu, r.V0 + 0.05f, decorUp, new float2(0f, 1f), SocketKind.WallDecor, lu >= 4f ? SocketSize.Medium : SocketSize.Small);
                    Add(cu, r.V1 - 0.05f, decorUp, new float2(0f, -1f), SocketKind.WallDecor, lu >= 4f ? SocketSize.Medium : SocketSize.Small);
                    Add(r.U0 + 0.05f, cv, decorUp, new float2(1f, 0f), SocketKind.WallDecor, lv >= 4f ? SocketSize.Medium : SocketSize.Small);
                    Add(r.U1 - 0.05f, cv, decorUp, new float2(-1f, 0f), SocketKind.WallDecor, lv >= 4f ? SocketSize.Medium : SocketSize.Small);
                    // A chest spot in the corner farthest from every doorway (dead ends add one at their far wall too).
                    if (r.Kind == 0 && !deadEnd)
                    {
                        float2 far = new float2(cu, cv);
                        var farD = -1f;
                        for (int c = 0; c < 4; c++)
                        {
                            var corner = new float2(c % 2 == 0 ? r.U0 + 1f : r.U1 - 1f, c < 2 ? r.V0 + 1f : r.V1 - 1f);
                            var nearest = float.MaxValue;
                            foreach (var d in doors)
                                if (d.a == i || d.b == i)
                                    nearest = math.min(nearest, math.distance(corner, d.q.xz));
                            if (nearest > farD)
                                (farD, far) = (nearest, corner);
                        }
                        Add(far.x, far.y, 0.05f, math.normalizesafe(new float2(cu, cv) - far), SocketKind.Chest, SocketSize.Medium, SocketTags.NearChest);
                        // P-9: a big room (40 m2 or more) keeps a second, in the opposite corner.
                        if (lu * lv >= 40f)
                        {
                            var opposite = new float2(r.U0 + r.U1, r.V0 + r.V1) - far;
                            Add(opposite.x, opposite.y, 0.05f, math.normalizesafe(new float2(cu, cv) - opposite), SocketKind.Chest, SocketSize.Medium, SocketTags.NearChest);
                        }
                    }
                }
                if (deadEnd)
                {
                    // The chest against the wall farthest from the one doorway, facing into the room.
                    var door = doors.Find(d => d.a == i || d.b == i).q;
                    float2 best = new float2(cu, cv), face = along;
                    var bestD = -1f;
                    foreach (var w in new[] { new float2(cu, r.V0 + 0.6f), new float2(cu, r.V1 - 0.6f), new float2(r.U0 + 0.6f, cv), new float2(r.U1 - 0.6f, cv) })
                    {
                        var d = math.distance(w, door.xz);
                        if (d > bestD)
                            (bestD, best, face) = (d, w, math.normalizesafe(new float2(cu, cv) - w));
                    }
                    Add(best.x, best.y, 0.05f, face, SocketKind.Chest, SocketSize.Medium, SocketTags.NearChest);
                }
            }
            // Pedestals and altars: a small box inside a room, its top well under the ceiling: something to use on top.
            for (int k = 0; k < set.RuinBoxes.Length; k++)
            {
                var b = set.RuinBoxes[k];
                float bu = (b.UV.x + b.UV.y) * 0.5f, bv = (b.UV.z + b.UV.w) * 0.5f;
                if (b.UV.y - b.UV.x > 3f || b.UV.w - b.UV.z > 3f)
                    continue;
                for (int i = 0; i < n; i++)
                {
                    var r = rooms[i];
                    if (!r.Contains(bu, bv) || b.Y.y > r.Ceil - 1f || b.Y.y < r.FloorAt(bu) + 0.3f)
                        continue;
                    cands.Add(new Candidate { Q = new float3(bu, b.Y.y, bv), Face = new float2(1f, 0f), Kind = SocketKind.Interactable, Size = SocketSize.Small, Tags = SocketTags.NearAltar, Room = i });
                    break;
                }
            }
            // Torches: each one is something to light or put out.
            for (int k = 0; k < set.RuinSconces.Length; k++)
            {
                var s = set.RuinSconces[k];
                cands.Add(new Candidate { Q = s.At, Face = s.N, Kind = SocketKind.Interactable, Size = SocketSize.Small, Tags = SocketTags.None, Room = -1 });
            }
            // Guards at doorways: a creature socket on each doorway (Doorway).
            foreach (var d in doors)
                cands.Add(new Candidate { Q = d.q + new float3(0f, 0.05f, 0f), Face = new float2(1f, 0f), Kind = SocketKind.Creature, Size = SocketSize.Medium, Tags = SocketTags.Doorway, Room = d.a });

            // About PerKind of each kind, picked evenly through the candidates (in the order made, so it's stable).
            var kept = new List<Candidate>();
            for (int kind = 0; kind <= (int)SocketKind.Clutter; kind++)
            {
                var all = cands.FindAll(c => (int)c.Kind == kind);
                if (all.Count <= PerKind)
                    kept.AddRange(all);
                else
                    for (int j = 0; j < PerKind; j++)
                        kept.Add(all[j * all.Count / PerKind]);
            }
            // Tags from neighbours: near a doorway, a chest or an altar.
            for (int j = 0; j < kept.Count; j++)
            {
                var c = kept[j];
                foreach (var d in doors)
                    if (math.distance(c.Q.xz, d.q.xz) < 1.5f) c.Tags |= SocketTags.Doorway;
                foreach (var o in kept)
                {
                    if (o.Kind == SocketKind.Chest && math.distance(c.Q.xz, o.Q.xz) < 4f) c.Tags |= SocketTags.NearChest;
                    if ((o.Tags & SocketTags.NearAltar) != 0 && o.Kind == SocketKind.Interactable && math.distance(c.Q.xz, o.Q.xz) < 3f) c.Tags |= SocketTags.NearAltar;
                }
                kept[j] = c;
            }

            var dungeonId = (uint)temple;
            var result = new List<DungeonSocket>(kept.Count);
            for (int j = 0; j < kept.Count; j++)
            {
                var c = kept[j];
                var dir = c.Face.x * layout.F + c.Face.y * layout.R;
                result.Add(new DungeonSocket
                {
                    Pos = SunkenTempleShape.World(layout, c.Q),
                    Rot = quaternion.LookRotationSafe(new float3(dir.x, 0f, dir.y), new float3(0f, 1f, 0f)),
                    Kind = c.Kind,
                    Size = (byte)c.Size,
                    Tags = (uint)c.Tags,
                    Seed = DungeonSocket.MakeSeed(worldSeed, dungeonId, (uint)j),
                    DungeonType = DungeonTypes.SunkenTemple,
                    Biome = biome,
                });
            }
            return result;
        }

        /// <summary>Sand drifted onto room <paramref name="room"/>'s floor at (u, v): SunkenTempleShape.Sand, from a copy of the drifts.</summary>
        static float Sand(RuinDrift[] drifts, int room, float u, float v)
        {
            var s = 0f;
            foreach (var d in drifts)
            {
                if (d.Room != room)
                    continue;
                float du = (u - d.Ellipse.x) / d.Ellipse.z, dv = (v - d.Ellipse.y) / d.Ellipse.w;
                s += d.Height * math.smoothstep(0f, 1f, 1f - du * du - dv * dv);
            }
            return s;
        }

        /// <summary>FNV-1a over every socket: position to the centimetre, kind, size, tags and seed.</summary>
        public static uint Hash(List<DungeonSocket> sockets)
        {
            uint h = 2166136261u;
            void Add(uint x) { for (int i = 0; i < 4; i++) { h = (h ^ (x & 0xFF)) * 16777619u; x >>= 8; } }
            foreach (var s in sockets)
            {
                Add((uint)(int)math.round(s.Pos.x * 100f)); Add((uint)(int)math.round(s.Pos.y * 100f)); Add((uint)(int)math.round(s.Pos.z * 100f));
                Add((uint)s.Kind | (uint)s.Size << 8); Add(s.Tags); Add(s.Seed);
            }
            return h;
        }
    }

    /// <summary>
    /// Makes the temple dungeon's entity once the island is baked: DungeonInfo plus its DungeonSocket buffer (the
    /// animal lane's spawner reads it, never writes it). Runs in every world, so solo, host and client get the same.
    /// </summary>
    public partial class TempleSocketSystem : SystemBase
    {
        bool done;

        protected override void OnCreate() => RequireForUpdate<TerrainSource>();

        protected override void OnUpdate()
        {
            if (done || IslandV2.ActiveVersion == 0)
                return;
            var source = SystemAPI.GetSingleton<TerrainSource>();
            if (!source.Landmarks.IsCreated || IslandV2.ActiveVersion != 1 && !source.Island.IsCreated)
                return;
            done = true;
            ref var set = ref source.Landmarks.Value;
            var temple = -1;
            for (int i = 0; i < set.Instances.Length && temple < 0; i++)
                if (set.Types[set.Instances[i].Type].Temple != 0)
                    temple = i;
            if (temple < 0 || set.RuinRooms.Length == 0)
            {
                Debug.Log($"[dungeon] no sunken temple: no sockets ({World.Name})");
                return;
            }
            var sockets = TempleSockets.Build(ref set, temple, source.World.Seed, 0);
            var hash = TempleSockets.Hash(sockets);
            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "Dungeon (sunken temple)");
            EntityManager.AddComponentData(entity, new DungeonInfo { Id = (uint)temple, Type = DungeonTypes.SunkenTemple, Sockets = sockets.Count, Hash = hash });
            var buffer = EntityManager.AddBuffer<DungeonSocket>(entity);
            foreach (var s in sockets)
                buffer.Add(s);
            var counts = new int[5];
            foreach (var s in sockets)
                counts[(int)s.Kind]++;
            Debug.Log($"[dungeon] sunken temple {temple}: {sockets.Count} sockets from {set.RuinRooms.Length} rooms: creature {counts[0]}, chest {counts[1]}, wall decor {counts[2]}, interactable {counts[3]}, clutter {counts[4]}; hash {hash:X8} ({World.Name})");
        }
    }

    /// <summary>
    /// Debug draw for dungeon sockets (#24): F9 toggles it, "-sockets" starts it on. A small coloured square per socket
    /// within 40 m, lettered by kind: Creature red, Chest yellow, Wall decor cyan, Interactable magenta, Clutter green.
    /// </summary>
    public class SocketDebugDraw : MonoBehaviour
    {
        static readonly Color[] Colours = { new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.3f, 1f), new Color(0.3f, 1f, 0.3f) };
        static readonly string[] Letters = { "C", "$", "W", "I", "x" };
        bool on;
        EntityQuery query;
        World queryWorld;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var go = new GameObject("SocketDebugDraw");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<SocketDebugDraw>().on = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-sockets") >= 0;
        }

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
                on = !on;
        }

        void OnGUI()
        {
            if (!on || Event.current.type != EventType.Repaint)
                return;
            var cam = Camera.main;
            var world = World.DefaultGameObjectInjectionWorld;
            if (cam == null || world == null || !world.IsCreated)
                return;
            if (queryWorld != world)
            {
                query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
                queryWorld = world;
            }
            if (query.IsEmpty)
                return;
            style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 11 };
            var em = world.EntityManager;
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            var colour = GUI.color;
            foreach (var e in entities)
            {
                var buffer = em.GetBuffer<DungeonSocket>(e, true);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var s = buffer[i];
                    Vector3 p = s.Pos;
                    if ((p - cam.transform.position).sqrMagnitude > 40f * 40f)
                        continue;
                    var sp = cam.WorldToScreenPoint(p);
                    if (sp.z <= 0f)
                        continue;
                    var size = Mathf.Clamp(120f / sp.z, 8f, 22f);
                    var rect = new Rect(sp.x - size * 0.5f, Screen.height - sp.y - size * 0.5f, size, size);
                    GUI.color = Colours[(int)s.Kind];
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                    GUI.color = Color.black;
                    GUI.Label(rect, Letters[(int)s.Kind], style);
                }
            }
            GUI.color = colour;
        }
    }
}
