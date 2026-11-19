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
        static readonly int StyleAirId = Shader.PropertyToID("_StyleAir");
        static readonly int CaveAirId = Shader.PropertyToID("_PunchyCaveAir");
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
                    // Their vertices carry the cave's slot: take its style's everywhere light, as the walls do.
                    mat.SetFloat(StyleAirId, 1f);
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
                DrawShelves(source, pair.Key, camPos);
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
                    DropShelves(cave);
                    for (int k = 0; k < CaveWallDetails.Kinds; k++)
                        wallShown.Remove(cave * CaveWallDetails.Kinds + k);
                }
            Shader.SetGlobalVector(CaveAirId, CameraCaveAir(ref set, camPos));
            DrawMushrooms(camPos);
            GatherClimbs(camPos);
            DrawHangings(camPos);
        }

        /// <summary>
        /// W-179: the everywhere light (CaveStyle.GlowEverywhere: Crystal's purple, Ice's blue) of the cave the camera is
        /// in, fading out from a metre inside its air to four metres out (the distance is to the nearest passage tube, so
        /// a room's middle can read near zero). Surface.hlsl's PunchySurfaceFinish adds it to every
        /// surface that can stand in a cave (cave flora, creatures, items, props), so they sit in the same light as the
        /// walls around them; w is the fade.
        /// </summary>
        Vector4 CameraCaveAir(ref CaveSetBlob set, float3 camPos)
        {
            var style = 0;
            var distance = CaveSampling.Distance(ref set, camPos, out var cave);
            if (distance < 4f && cave >= 0 && cave < set.Caves.Length)
                style = set.Caves[cave].Style;
            if (style <= 0 || style > wallStyles.Count || wallStyles[style - 1] == null)
                return Vector4.zero;
            var s = wallStyles[style - 1];
            // As CaveStyle.Pack and Cave.shader: glow colour x glow x the everywhere share, in the table's raw values.
            var fade = math.saturate((4f - distance) / 3f);
            var air = s.GlowColor * (s.Glow * s.GlowEverywhere * fade);
            return new Vector4(air.r, air.g, air.b, fade);
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
            Shader.SetGlobalVector(CaveAirId, Vector4.zero);
            foreach (var build in wallBuilds.Values)
                build.Release();
            wallBuilds.Clear();
            wallShown.Clear();
            wallBuilding = null;
            ReleaseMushrooms();
            ReleaseHangings();
            ReleaseShelves();
            if (wallMaterials != null)
                foreach (var mat in wallMaterials)
                    if (mat != null)
                        Destroy(mat);
            wallMaterials = null;
            wallStyles = null;
        }
    }
}
