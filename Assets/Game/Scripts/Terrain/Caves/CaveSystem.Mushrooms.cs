using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Punchy.Settings;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-177: the mushroom clusters on cave Fungi and GlowFungi wall patches (CaveWallBuild.Mushrooms), grown by the flora
    /// Mushroom grower from the cave styles' Mushrooms and GlowMushrooms species (rule 33: one mushroom system) and drawn
    /// with the flora material like the cave plants. Each kind has upright models for floors and wall-grown ones
    /// (FloraSpecies.WallGrown, from a runtime copy of the species) whose stems leave the rock and turn their caps up.
    /// A cluster is drawn while it is big enough on screen (DetailPolicy.ExtraReach), at its finest LOD when close.
    /// Drawn only: nothing collides with them, and nothing is baked, so the world's layout is unchanged.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>Kinds (plain, glowing) x (upright, wall-grown).</summary>
        const int MushroomGroups = 4, MushroomVariants = 3;
        /// <summary>A cluster's size on screen for its reach (a few caps a hand or two across).</summary>
        const float MushroomSize = 0.3f;

        readonly Punchy.Flora.FloraModel[] mushroomModels = new Punchy.Flora.FloraModel[MushroomGroups * MushroomVariants];
        readonly List<Punchy.Flora.FloraSpecies> mushroomClones = new List<Punchy.Flora.FloraSpecies>();
        readonly HashSet<long> mushroomShown = new HashSet<long>();
        readonly HashSet<long> mushroomFine = new HashSet<long>();
        readonly List<Mesh> mushroomDraws = new List<Mesh>();
        readonly List<(int key, float3 position, float yaw, float scale, float random)> mushroomKeys =
            new List<(int, float3, float, float, float)>();
        Punchy.Flora.FloraSpecies[] mushroomSpecies;
        Material mushroomMaterial;
        GraphicsBuffer mushroomInstances, mushroomVisible, mushroomArgs;
        float3 mushroomBuiltAt = new float3(float.MaxValue);
        int mushroomBuiltCount = -1, mushroomGrown;
        float nextMushroomLookup;

        void DrawMushrooms(float3 camPos)
        {
            if (wallStyles == null || wallBuilds.Count == 0)
                return;
            if (mushroomMaterial == null)
            {
                // Looking it up is slow; without one, try again only now and then.
                if (Time.unscaledTime < nextMushroomLookup)
                    return;
                nextMushroomLookup = Time.unscaledTime + 1f;
                var flora = FindFirstObjectByType<Punchy.Flora.FloraSystem>();
                mushroomMaterial = flora != null && flora.Material != null ? new Material(flora.Material) { name = "Cave mushrooms" } : null;
                if (mushroomMaterial == null)
                    return;
                mushroomMaterial.SetFloat(FloraInCaveId, 1f);
            }
            if (mushroomSpecies == null)
            {
                mushroomSpecies = new Punchy.Flora.FloraSpecies[MushroomGroups];
                foreach (var style in wallStyles)
                {
                    if (style == null)
                        continue;
                    mushroomSpecies[0] ??= style.Mushrooms;
                    mushroomSpecies[2] ??= style.GlowMushrooms;
                }
                for (int g = 0; g < MushroomGroups; g += 2)
                    if (mushroomSpecies[g] != null)
                    {
                        // The wall-grown form: the same species with its stems leaving the rock (a copy, never saved).
                        var wall = Object.Instantiate(mushroomSpecies[g]);
                        wall.name = mushroomSpecies[g].name + " (wall)";
                        wall.WallGrown = true;
                        mushroomClones.Add(wall);
                        mushroomSpecies[g + 1] = wall;
                    }
            }
            // Grow one model a frame until all are grown (a cluster waits for its model).
            if (mushroomGrown < mushroomModels.Length)
            {
                var g = mushroomGrown / MushroomVariants;
                if (mushroomSpecies[g] != null)
                    mushroomModels[mushroomGrown] = Punchy.Flora.FloraGenerator.Grow(mushroomSpecies[g], mushroomGrown % MushroomVariants);
                mushroomGrown++;
                mushroomBuiltCount = -1;
            }

            var total = 0;
            foreach (var build in wallBuilds.Values)
                total += build.Mushrooms.Count;
            if (total == 0)
                return;
            if (total != mushroomBuiltCount || math.distancesq(camPos, mushroomBuiltAt) > 16f)
            {
                mushroomBuiltCount = total;
                mushroomBuiltAt = camPos;
                RebuildMushrooms(camPos);
            }
            if (mushroomDraws.Count == 0)
                return;
            var box = DetailPolicy.ExtraReach(MushroomSize, DetailPolicy.CavePlantPixels) / (1f - DetailPolicy.Hysteresis) * 2f + 8f;
            var rp = new RenderParams(mushroomMaterial)
            {
                worldBounds = new Bounds(camPos, new Vector3(box, box, box)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true
            };
            for (int d = 0; d < mushroomDraws.Count; d++)
                Graphics.RenderMeshIndirect(rp, mushroomDraws[d], mushroomArgs, 1, d);
        }

        void RebuildMushrooms(float3 camPos)
        {
            var reach = DetailPolicy.ExtraReach(MushroomSize, DetailPolicy.CavePlantPixels);
            var fine = DetailPolicy.ViewReach(MushroomSize, DetailPolicy.CavePlantFinestPixels);
            mushroomKeys.Clear();
            foreach (var pair in wallBuilds)
            {
                var list = pair.Value.Mushrooms;
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    var model = m.model * MushroomVariants + (int)(m.random * MushroomVariants) % MushroomVariants;
                    if (mushroomModels[model] == null)
                        continue;
                    var id = ((long)pair.Key << 20) | (long)i;
                    var d = math.distance(m.position, camPos);
                    if (!DetailPolicy.Within(d, reach, mushroomShown.Contains(id)))
                    {
                        mushroomShown.Remove(id);
                        mushroomFine.Remove(id);
                        continue;
                    }
                    mushroomShown.Add(id);
                    var lod = DetailPolicy.Within(d, fine, mushroomFine.Contains(id)) ? 0 : 1;
                    if (lod == 0)
                        mushroomFine.Add(id);
                    else
                        mushroomFine.Remove(id);
                    mushroomKeys.Add((model * 2 + lod, m.position, m.yaw, m.scale, m.random));
                }
            }
            mushroomKeys.Sort((a, b) => a.key.CompareTo(b.key));
            mushroomDraws.Clear();
            var count = mushroomKeys.Count;
            if (count == 0)
                return;
            if (mushroomInstances == null || mushroomInstances.count < count)
            {
                mushroomInstances?.Release();
                mushroomVisible?.Release();
                var size = math.ceilpow2(count);
                mushroomInstances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size,
                    System.Runtime.InteropServices.Marshal.SizeOf<Punchy.Flora.FloraSystem.FloraInstance>());
                mushroomVisible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 8);
            }
            var instances = new NativeArray<Punchy.Flora.FloraSystem.FloraInstance>(count, Allocator.Temp);
            var visible = new NativeArray<uint2>(count, Allocator.Temp);
            var fullyVisible = math.asuint(1f);
            for (int k = 0; k < count; k++)
            {
                var m = mushroomKeys[k];
                instances[k] = new Punchy.Flora.FloraSystem.FloraInstance
                {
                    Position = m.position, Yaw = m.yaw, Scale = m.scale, Model = m.key / 2, Random = m.random, Snow = 0f
                };
                visible[k] = new uint2((uint)k, fullyVisible);
            }
            mushroomInstances.SetData(instances);
            mushroomVisible.SetData(visible);
            instances.Dispose();
            visible.Dispose();

            // One draw per run of the same model and LOD.
            var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
            for (int k = 0; k < count;)
            {
                var key = mushroomKeys[k].key;
                var start = k;
                while (k < count && mushroomKeys[k].key == key)
                    k++;
                var mesh = mushroomModels[key / 2].Lods[key % 2];
                mushroomDraws.Add(mesh);
                args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = mesh.GetIndexCount(0),
                    startIndex = mesh.GetIndexStart(0),
                    baseVertexIndex = mesh.GetBaseVertex(0),
                    instanceCount = (uint)(k - start),
                    startInstance = (uint)start
                });
            }
            if (mushroomArgs == null || mushroomArgs.count < args.Count)
            {
                mushroomArgs?.Release();
                mushroomArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, math.ceilpow2(args.Count), GraphicsBuffer.IndirectDrawIndexedArgs.size);
            }
            mushroomArgs.SetData(args);
            mushroomMaterial.SetBuffer(FloraInstancesId, mushroomInstances);
            mushroomMaterial.SetBuffer(FloraVisibleId, mushroomVisible);
        }

        /// <summary>The mushroom clusters drawn at the last rebuild (for shots and tests).</summary>
        public int MushroomsDrawn => mushroomKeys.Count;

        void ReleaseMushrooms()
        {
            for (int i = 0; i < mushroomModels.Length; i++)
            {
                mushroomModels[i]?.Destroy();
                mushroomModels[i] = null;
            }
            foreach (var clone in mushroomClones)
                if (clone != null)
                    Destroy(clone);
            mushroomClones.Clear();
            mushroomSpecies = null;
            mushroomGrown = 0;
            mushroomBuiltCount = -1;
            mushroomBuiltAt = new float3(float.MaxValue);
            mushroomShown.Clear();
            mushroomFine.Clear();
            mushroomKeys.Clear();
            mushroomDraws.Clear();
            mushroomInstances?.Release();
            mushroomVisible?.Release();
            mushroomArgs?.Release();
            mushroomInstances = mushroomVisible = mushroomArgs = null;
            if (mushroomMaterial != null)
                Destroy(mushroomMaterial);
            mushroomMaterial = null;
        }
    }
}
