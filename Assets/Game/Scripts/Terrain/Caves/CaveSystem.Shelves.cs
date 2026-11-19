using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Punchy.Flora;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>W-181 r3 (#534): draws cliff kit cave shelves in the cave's light.</summary>
    public partial class CaveSystem
    {
        const float ShelfSize = 3f;
        readonly Dictionary<int, Mesh> shelfMeshes = new Dictionary<int, Mesh>();
        readonly HashSet<int> shelfShown = new HashSet<int>();
        Material shelfMaterial;

        void DrawShelves(in TerrainSource source, int cave, float3 camPos)
        {
            if (!source.CliffKit.IsCreated)
                return;
            if (!shelfMeshes.TryGetValue(cave, out var mesh))
            {
                mesh = BuildShelves(source, cave);
                shelfMeshes[cave] = mesh;
            }
            if (mesh == null)
                return;
            if (shelfMaterial == null)
            {
                shelfMaterial = new Material(Material) { name = "Cave shelf" };
                shelfMaterial.SetFloat(StyleAirId, 1f);
            }
            var distance = math.sqrt(mesh.bounds.SqrDistance(camPos));
            var reach = Punchy.Settings.DetailPolicy.ExtraReach(ShelfSize, CaveWallDetails.Pixels);
            if (!Punchy.Settings.DetailPolicy.Within(distance, reach, shelfShown.Contains(cave)))
            {
                shelfShown.Remove(cave);
                return;
            }
            shelfShown.Add(cave);
            var rp = new RenderParams(shelfMaterial) { worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
        }

        static Mesh BuildShelves(in TerrainSource source, int cave)
        {
            ref var kit = ref source.CliffKit.Value;
            ref var set = ref source.Caves.Value;
            var c = set.Caves[cave];
            var wall = new CaveWallMesh(c.Mouth, c.Style);
            var boxes = new FixedList512Bytes<KitBox>();
            for (int i = 0; i < kit.Pieces.Length; i++)
            {
                var piece = kit.Pieces[i];
                if (piece.Face != CliffKit.CaveFace || math.any(piece.Position < c.Min - 2f) || math.any(piece.Position > c.Max + 2f))
                    continue;
                var lip = piece.Position + new float3(math.sin(piece.Yaw) * 0.5f, 1.6f * piece.Scale.y, math.cos(piece.Yaw) * 0.5f);
                CaveSampling.Distance(ref set, lip, out var owner);
                if (owner != cave)
                    continue;
                boxes.Clear();
                CliffKit.Boxes(piece, ref boxes);
                for (int k = 0; k < boxes.Length; k++)
                    wall.Box(boxes[k].Centre, boxes[k].Half, boxes[k].Rotation, piece.Tint);
            }
            return wall.ToMesh("Cave shelves");
        }

        void DropShelves(int cave)
        {
            if (shelfMeshes.TryGetValue(cave, out var mesh) && mesh != null)
                Destroy(mesh);
            shelfMeshes.Remove(cave);
            shelfShown.Remove(cave);
        }

        void ReleaseShelves()
        {
            foreach (var mesh in shelfMeshes.Values)
                if (mesh != null)
                    Destroy(mesh);
            shelfMeshes.Clear();
            shelfShown.Clear();
            if (shelfMaterial != null)
                Destroy(shelfMaterial);
            shelfMaterial = null;
        }
    }
}
