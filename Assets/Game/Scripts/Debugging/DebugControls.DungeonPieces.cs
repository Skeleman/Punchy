using Punchy.Dungeon;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, "-place piece:hall|deadend|pool" (P-45): stand in one of each dungeon piece that already exists on
    // main, for shots and the -dungeonwalk check. Halls and dead ends are sunken temple rooms picked by the same shape
    // rules TempleSockets uses for its sockets; the pool is the first cave Lake room (CaveRoomKit). No new geometry.
    public partial class DebugControls
    {
        /// <summary>The pending "-place piece:..." spec, the landmarks key it last placed on, and when it stops trying.</summary>
        string piecePending;
        float3 piecePlaceKey;
        float piecePlaceUntil;

        /// <summary>
        /// "-place piece:..." again whenever the island's landmarks change, for 180 s: the launch options run on the
        /// baked first island, whose temple the v2/v3 pass moves (the first hall shot stood on bare sand above nothing).
        /// </summary>
        void PlacePiecePending()
        {
            if (piecePending == null)
                return;
            if (Time.unscaledTime > piecePlaceUntil)
            {
                piecePending = null;
                return;
            }
            if (!EnsureLocations(out var source) || math.all(locationsKey == piecePlaceKey))
                return;
            piecePlaceKey = locationsKey;
            PlacePiece(source, piecePending);
        }

        /// <summary>
        /// "-place piece:hall" / "piece:deadend": the best temple room TempleSockets calls a hall or a dead end (roofed,
        /// then widest, then biggest), then the usual PlaceInTemple. "piece:pool": in the first cave Lake room, 1.5 m in
        /// from its doorway, facing the pool.
        /// </summary>
        void PlacePiece(in TerrainSource source, string spec)
        {
            var kind = spec.Substring(spec.IndexOf(':') + 1).ToLowerInvariant();
            if (kind == "pool")
            {
                PlaceInLakeRoom(source);
                return;
            }
            if (!source.Landmarks.IsCreated || source.Landmarks.Value.RuinRooms.Length == 0)
            {
                Debug.LogWarning("DebugControls: no temple rooms for -place piece");
                return;
            }
            ref var set = ref source.Landmarks.Value;
            var rooms = set.RuinRooms.Length;
            int halls = 0, deadEnds = 0, pick = -1;
            var pickScore = -1f;
            for (int i = 0; i < rooms; i++)
            {
                var r = set.RuinRooms[i];
                var neighbours = 0;
                for (int j = 0; j < rooms; j++)
                    if (j != i && TempleSockets.Touch(r, set.RuinRooms[j]))
                        neighbours++;
                bool hall = TempleSockets.IsHall(r), deadEnd = TempleSockets.IsDeadEnd(r, neighbours);
                halls += hall ? 1 : 0;
                deadEnds += deadEnd ? 1 : 0;
                // A roofed one (kind 0: a cut open to the sky is often drifted full of sand), then the widest (a 1 m
                // corridor puts the camera in the wall), then the biggest.
                float lu = r.U1 - r.U0, lv = r.V1 - r.V0;
                var score = (r.Kind == 0 ? 10000f : 0f) + math.min(lu, lv) * 100f + lu * lv;
                if ((kind == "hall" ? hall : kind == "deadend" && deadEnd) && score > pickScore)
                    (pick, pickScore) = (i, score);
            }
            Debug.Log($"DebugControls: temple pieces: {halls} halls, {deadEnds} dead ends in {rooms} rooms");
            if (pick < 0)
            {
                Debug.LogWarning($"DebugControls: no temple room is a '{kind}' piece (try hall, deadend or pool)");
                return;
            }
            Debug.Log($"DebugControls: piece {kind} is temple room {pick}");
            PlaceInTemple(source, $"temple:{pick}");
            if (kind == "hall")
                PoseDownHall(ref set, pick);
        }

        /// <summary>
        /// A hall is a corridor (TempleSockets.IsHall), so the follow camera beside the player sees only the near wall:
        /// stand the camera 0.4 m in from one end, at eye height, looking down the hall's length to the other end.
        /// </summary>
        void PoseDownHall(ref LandmarkSetBlob set, int room)
        {
            var temple = -1;
            for (int i = 0; i < set.Instances.Length && temple < 0; i++)
                if (set.Types[set.Instances[i].Type].Temple != 0)
                    temple = i;
            if (temple < 0)
                return;
            var r = set.RuinRooms[room];
            var layout = SunkenTempleShape.Layout(set.Instances[temple]);
            bool alongU = r.U1 - r.U0 >= r.V1 - r.V0;
            float cu = (r.U0 + r.U1) * 0.5f, cv = (r.V0 + r.V1) * 0.5f;
            float2 a = alongU ? new float2(r.U0 + 0.4f, cv) : new float2(cu, r.V0 + 0.4f);
            float2 b = alongU ? new float2(r.U1 - 0.4f, cv) : new float2(cu, r.V1 - 0.4f);
            // Eye height: 1.6 m over the floor and any drifted sand, or lower under a low ceiling.
            var ya = r.FloorAt(a.x) + SunkenTempleShape.Sand(ref set, room, a.x, a.y) + math.min(1.6f, (r.Ceil - r.FloorAt(a.x)) * 0.6f);
            var yb = r.FloorAt(b.x) + SunkenTempleShape.Sand(ref set, room, b.x, b.y) + math.min(1.6f, (r.Ceil - r.FloorAt(b.x)) * 0.6f);
            var eye = (Vector3)SunkenTempleShape.World(layout, new float3(a.x, ya, a.y));
            var end = (Vector3)SunkenTempleShape.World(layout, new float3(b.x, yb - 0.3f, b.y));
            PoseCamera(eye, Quaternion.LookRotation(end - eye));
        }

        /// <summary>The first cave Lake room: standing just inside its doorway on the cave floor, camera behind and above.</summary>
        void PlaceInLakeRoom(in TerrainSource source)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            ref var caves = ref source.Caves.Value;
            var lake = -1;
            for (int i = 0; i < caves.Rooms.Length && lake < 0; i++)
                if (caves.Rooms[i].Type == (int)CaveRoomType.Lake)
                    lake = i;
            if (lake < 0)
            {
                Debug.LogWarning($"DebugControls: no cave Lake room ({caves.Rooms.Length} cave rooms)");
                return;
            }
            var room = caves.Rooms[lake];
            var stand = room.World(new float3(0f, 0.5f, 1.5f));
            var floor = source.FloorUnder(stand, out _);
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(stand.x, floor + 0.05f, stand.z);
            t.Rotation = quaternion.RotateY(math.atan2(room.F.x, room.F.y));
            em.SetComponentData(player, t);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var eye = (Vector3)room.World(new float3(0f, math.min(2.5f, room.Height * 0.6f), -0.5f));
            PoseCamera(eye, Quaternion.LookRotation((Vector3)room.Centre - eye));
            Debug.Log($"DebugControls: in cave Lake room {lake}/{caves.Rooms.Length} (cave {room.Cave}, {room.HX * 2f:F0} x {room.HZ * 2f:F0} m, height {room.Height:F1} m, pool {room.Pool}) at ({t.Position.x:F1}, {t.Position.y:F1}, {t.Position.z:F1})");
        }
    }
}
