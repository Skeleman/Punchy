using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-115 row 2: draws the destination chambers (CaveRoomKit) within LoadRadius, each room's meshes split by material
    /// group and drawn with copies of the cave material in the room type's colours (lit like the walls, no daylight),
    /// and requests each drawn room's lights from LightBudget (as fixtures, refactor 3.1) and adds its nearby flames to FireSystem
    /// every frame.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Within this of the camera a room's flames are drawn (m); its lights go to LightBudget, which decides.</summary>
        const float RoomFlameRange = 60f;
        /// <summary>Per room (index into the cave set's Rooms): its meshes by group, built the first time it is in range.</summary>
        readonly Dictionary<int, Mesh[]> roomMeshes = new Dictionary<int, Mesh[]>();
        /// <summary>Per room type and group, a copy of the cave material in that group's colours.</summary>
        Material[] roomMaterials;
        /// <summary>A-91: Cave.shader's _Dressed (what a kit copy is made of: -1 as is, 0 bare rock, 1 stone, 2 wood, 3 iron).</summary>
        internal static readonly int DressedId = Shader.PropertyToID("_Dressed");

        void DrawRooms(in TerrainSource source, float3 camPos)
        {
            if (!source.Caves.IsCreated)
                return;
            DrawPortals(source, camPos);
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
                        mat.SetFloat(DressedId, CaveRoomKit.Dressing(g));
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
                var kit = CaveRoomKit.Get(room);
                for (int k = 0; k < kit.Glows.Count; k++)
                {
                    var glow = kit.Glows[k];
                    if (glow.Flame && distance <= RoomFlameRange)
                        Punchy.Ambience.FireSystem.AddFlame(glow.At, 0.32f, (int)(room.Seed + (uint)k), Punchy.Ambience.FireSystem.Wood);
                    Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.Fixture, glow.At, glow.Light);
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
            foreach (var meshes in portalMeshes.Values)
                foreach (var mesh in meshes)
                    if (mesh != null)
                        Destroy(mesh);
            portalMeshes.Clear();
            if (portalMaterials != null)
                foreach (var mat in portalMaterials)
                    if (mat != null)
                        Destroy(mat);
            portalMaterials = null;
            portals.Clear();
            TunnelPortalKit.ClearCache();
        }

        // ------------------------------------------------------------------------------------------- tunnel portals

        /// <summary>The road tunnels' portals (W-110 row 2), listed once per cave set.</summary>
        readonly List<TunnelPortalKit.Portal> portals = new List<TunnelPortalKit.Portal>();
        BlobAssetReference<CaveSetBlob> portalSet;
        /// <summary>Per portal (index into portals): its meshes by group, built the first time it is in range.</summary>
        readonly Dictionary<int, Mesh[]> portalMeshes = new Dictionary<int, Mesh[]>();
        /// <summary>Per group, a copy of the cave material in the portal's colours (open to daylight, casting shadows).</summary>
        Material[] portalMaterials;

        void DrawPortals(in TerrainSource source, float3 camPos)
        {
            if (!portalSet.Equals(source.Caves))
            {
                portalSet = source.Caves;
                TunnelPortalKit.Portals(ref source.Caves.Value, portals);
            }
            if (portals.Count == 0)
                return;
            if (portalMaterials == null)
            {
                portalMaterials = new Material[TunnelPortalKit.Groups];
                for (int g = 0; g < TunnelPortalKit.Groups; g++)
                {
                    TunnelPortalKit.Colours(g, out var rock, out var dark);
                    var mat = new Material(Material) { name = "Tunnel portal " + g };
                    mat.SetColor(RockColorId, rock);
                    mat.SetColor(DarkColorId, dark);
                    // A-91: the portal's cut stone weathers like a room's; moss and ivy stay as they are.
                    mat.SetFloat(DressedId, g <= TunnelPortalKit.Trim ? 1f : -1f);
                    portalMaterials[g] = mat;
                }
            }
            for (int i = 0; i < portals.Count; i++)
            {
                var portal = portals[i];
                if (math.distancesq(portal.Floor, camPos) > LoadRadius * LoadRadius)
                    continue;
                if (!portalMeshes.TryGetValue(i, out var meshes))
                {
                    meshes = CaveRoomKit.BuildMeshes(TunnelPortalKit.Get(source, portal), 1f);
                    portalMeshes[i] = meshes;
                }
                for (int g = 0; g < meshes.Length; g++)
                {
                    var mesh = meshes[g];
                    if (mesh == null)
                        continue;
                    var rp = new RenderParams(portalMaterials[g])
                    {
                        worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true
                    };
                    Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
                }
            }
        }
    }
}
