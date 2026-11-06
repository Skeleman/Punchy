using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-115 row 2: draws the destination chambers (CaveRoomKit) within LoadRadius, each room's meshes split by material
    /// group and drawn with copies of the cave material in the room type's colours (lit like the walls, no daylight),
    /// and adds each nearby room's lights to the glow list and its flames to FireSystem every frame.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Within this of the camera a room's lights and flames are added (m).</summary>
        const float RoomLightRange = 60f;
        /// <summary>Per room (index into the cave set's Rooms): its meshes by group, built the first time it is in range.</summary>
        readonly Dictionary<int, Mesh[]> roomMeshes = new Dictionary<int, Mesh[]>();
        /// <summary>Per room type and group, a copy of the cave material in that group's colours.</summary>
        Material[] roomMaterials;

        void DrawRooms(in TerrainSource source, float3 camPos)
        {
            if (!source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;
            if (set.Rooms.Length == 0)
                return;
            if (roomMaterials == null)
            {
                roomMaterials = new Material[4 * CaveRoomKit.Groups];
                for (int t = 0; t < 4; t++)
                    for (int g = 0; g < CaveRoomKit.Groups; g++)
                    {
                        CaveRoomKit.Colours(t, g, out var rock, out var dark);
                        var mat = new Material(Material) { name = "Cave room " + t + "." + g };
                        mat.SetColor(RockColorId, rock);
                        mat.SetColor(DarkColorId, dark);
                        roomMaterials[t * CaveRoomKit.Groups + g] = mat;
                    }
            }
            for (int i = 0; i < set.Rooms.Length; i++)
            {
                var room = set.Rooms[i];
                var distance = math.distance(room.Centre, camPos);
                if (distance > LoadRadius)
                    continue;
                if (!roomMeshes.TryGetValue(i, out var meshes))
                {
                    meshes = CaveRoomKit.BuildMeshes(CaveRoomKit.Get(room));
                    roomMeshes[i] = meshes;
                }
                var type = math.clamp(room.Type, 0, 3);
                for (int g = 0; g < meshes.Length; g++)
                {
                    var mesh = meshes[g];
                    if (mesh == null)
                        continue;
                    var rp = new RenderParams(roomMaterials[type * CaveRoomKit.Groups + g])
                    {
                        worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true
                    };
                    Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
                }
                if (distance > RoomLightRange)
                    continue;
                var kit = CaveRoomKit.Get(room);
                for (int k = 0; k < kit.Glows.Count; k++)
                {
                    var glow = kit.Glows[k];
                    var at = new Vector4(glow.At.x, glow.At.y, glow.At.z, 0f);
                    if (glow.Flame)
                        Punchy.Ambience.FireSystem.AddFlame(glow.At, 0.32f, (int)(room.Seed + (uint)k), Punchy.Ambience.FireSystem.Wood);
                    glowing.Add((math.distance(glow.At, camPos), at, at, (Vector4)glow.Light));
                }
            }
        }

        void ReleaseRooms()
        {
            foreach (var meshes in roomMeshes.Values)
                foreach (var mesh in meshes)
                    if (mesh != null)
                        Destroy(mesh);
            roomMeshes.Clear();
            if (roomMaterials != null)
                foreach (var mat in roomMaterials)
                    if (mat != null)
                        Destroy(mat);
            roomMaterials = null;
            CaveRoomKit.ClearCache();
        }
    }
}
