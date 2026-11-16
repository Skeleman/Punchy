using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-172 row 7: draws each nearby cave's wall details (CaveWallDetails), one mesh per kind per cave, with copies of
    /// the cave material in each kind's colours. A cave's details are built when it comes within LoadRadius, a couple of
    /// placements a frame and one cave at a time so no frame takes the whole build, and each kind's mesh is drawn while
    /// it is big enough on screen (DetailPolicy.ExtraReach). Drawn only: nothing collides with them.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Placements a frame while a cave's details are building.</summary>
        const int WallStepsPerFrame = 2;
        /// <summary>Per cave (index into the cave set's Caves): its built (or building) wall details.</summary>
        readonly Dictionary<int, CaveWallBuild> wallBuilds = new Dictionary<int, CaveWallBuild>();
        /// <summary>Cave * Kinds + kind for the meshes drawn last frame (DetailPolicy.Within holds them).</summary>
        readonly HashSet<int> wallShown = new HashSet<int>();
        /// <summary>Per kind, a copy of the cave material in its colours.</summary>
        Material[] wallMaterials;
        static readonly int OwnLightId = Shader.PropertyToID("_OwnLight");
        CaveWallBuild wallBuilding;
        BlobAssetReference<CaveSetBlob> wallSet;
        List<CaveStyle> wallStyles;

        void DrawWallDetails(in TerrainSource source, float3 camPos)
        {
            if (!wallSet.Equals(source.Caves))
            {
                ReleaseWallDetails();
                wallSet = source.Caves;
            }
            if (paletteQuery.IsEmpty)
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            if (palette == null)
                return;
            wallStyles ??= CaveStyleTable.Collect(palette.Landmarks);
            if (wallMaterials == null)
            {
                wallMaterials = new Material[CaveWallDetails.Kinds];
                for (int k = 0; k < CaveWallDetails.Kinds; k++)
                {
                    CaveWallDetails.Colours((CaveWallKind)k, out var rock, out var dark);
                    var mat = new Material(Material) { name = "Cave wall " + CaveWallDetails.Names[k] };
                    mat.SetColor(RockColorId, rock);
                    mat.SetColor(DarkColorId, dark);
                    mat.SetFloat(DressedId, -1f);
                    mat.SetColor(OwnLightId, CaveWallDetails.OwnLight((CaveWallKind)k));
                    wallMaterials[k] = mat;
                }
            }
            ref var set = ref source.Caves.Value;

            // Build: carry on with the current cave, or start the nearest one in range that has none.
            if (wallBuilding != null)
            {
                for (int s = 0; s < WallStepsPerFrame; s++)
                    if (wallBuilding.Step(source))
                    {
                        wallBuilding = null;
                        break;
                    }
            }
            else
            {
                var best = -1;
                var bestDistance = LoadRadius * LoadRadius;
                for (int i = 0; i < set.Caves.Length; i++)
                {
                    if (wallBuilds.ContainsKey(i))
                        continue;
                    var d = BoxDistanceSq(set.Caves[i].Min, set.Caves[i].Max, camPos);
                    if (d < bestDistance)
                    {
                        best = i;
                        bestDistance = d;
                    }
                }
                if (best >= 0)
                {
                    wallBuilding = new CaveWallBuild(source, palette, wallStyles, best);
                    wallBuilds[best] = wallBuilding;
                }
            }

            // Release caves well out of range; draw the rest.
            List<int> drop = null;
            foreach (var pair in wallBuilds)
            {
                var c = set.Caves[pair.Key];
                if (BoxDistanceSq(c.Min, c.Max, camPos) > LoadRadius * LoadRadius * 1.5625f)
                {
                    (drop ??= new List<int>()).Add(pair.Key);
                    continue;
                }
                var build = pair.Value;
                for (int k = 0; k < CaveWallDetails.Kinds; k++)
                {
                    var mesh = build.Meshes[k];
                    if (mesh == null)
                        continue;
                    var id = pair.Key * CaveWallDetails.Kinds + k;
                    var distance = math.sqrt(mesh.bounds.SqrDistance(camPos));
                    var reach = Punchy.Settings.DetailPolicy.ExtraReach(CaveWallDetails.Size[k], CaveWallDetails.Pixels);
                    if (!Punchy.Settings.DetailPolicy.Within(distance, reach, wallShown.Contains(id)))
                    {
                        wallShown.Remove(id);
                        continue;
                    }
                    wallShown.Add(id);
                    var rp = new RenderParams(wallMaterials[k])
                    {
                        worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true
                    };
                    Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
                    if (k == (int)CaveWallKind.GlowFungi)
                        foreach (var glow in build.Glows)
                            Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.AmbientGlow, glow, CaveWallDetails.GlowLight);
                }
            }
            if (drop != null)
                foreach (var cave in drop)
                {
                    if (wallBuilding == wallBuilds[cave])
                        wallBuilding = null;
                    wallBuilds[cave].Release();
                    wallBuilds.Remove(cave);
                    for (int k = 0; k < CaveWallDetails.Kinds; k++)
                        wallShown.Remove(cave * CaveWallDetails.Kinds + k);
                }
        }

        static float BoxDistanceSq(float3 min, float3 max, float3 p)
        {
            var d = math.max(math.max(min - p, p - max), 0f);
            return math.lengthsq(d);
        }

        /// <summary>The wall details built so far, by cave (for shots and tests).</summary>
        public IReadOnlyDictionary<int, CaveWallBuild> WallBuilds => wallBuilds;

        void ReleaseWallDetails()
        {
            foreach (var build in wallBuilds.Values)
                build.Release();
            wallBuilds.Clear();
            wallShown.Clear();
            wallBuilding = null;
            if (wallMaterials != null)
                foreach (var mat in wallMaterials)
                    if (mat != null)
                        Destroy(mat);
            wallMaterials = null;
            wallStyles = null;
        }
    }
}
