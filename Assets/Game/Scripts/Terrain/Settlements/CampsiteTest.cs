using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// #72 (W-87): "-campsitetest swamp" (or forest, snow, desert, ash, fungal: the end of a campsite look's name) adds
    /// one campsite with that look at the flat dry spot of a matching biome nearest the island's middle, after every
    /// other place, and "-campsitetest" in DebugControls then puts the player beside it. Debug only: without the flag
    /// nothing is added, so normal placement and the landmark hash don't change.
    /// </summary>
    public static class CampsiteTest
    {
        /// <summary>Where the test campsite went (NaN when none), for DebugControls to go there.</summary>
        public static float2 At = new float2(float.NaN);
        public static string LastLog = "";

        public static string Requested()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var i = Array.IndexOf(args, "-campsitetest");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Adds the test campsite to the source's landmarks when the flag asks for one.</summary>
        public static void Add(ref TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<LandmarkType> types, Allocator allocator)
        {
            At = new float2(float.NaN);
            var want = Requested();
            if (want == null || !source.Landmarks.IsCreated)
                return;
            BiomeLook look = null;
            foreach (var type in types)
                if (type != null && type.Anchors == WildAnchors.Campsite && type.BiomeLooks != null)
                    foreach (var l in type.BiomeLooks)
                        if (l?.Look != null && l.Look.name.EndsWith(want, StringComparison.OrdinalIgnoreCase))
                            look = l;
            var lookIndex = look == null ? -1 : types.IndexOf(look.Look);
            if (lookIndex < 0)
            {
                LastLog = $"[campsitetest] no campsite look ending in '{want}'";
                UnityEngine.Debug.Log(LastLog);
                return;
            }
            ref var set = ref source.Landmarks.Value;
            var sea = source.World.SeaLevel;
            var half = source.World.WorldSize * 0.5f;
            var best = new float2(float.NaN);
            var bestDist = float.MaxValue;
            var wet = 0;
            for (float z = -half; z < half; z += 16f)
            for (float x = -half; x < half; x += 16f)
            {
                var p = new float2(x, z);
                var d = math.lengthsq(p);
                if (d >= bestDist) continue;
                var h = source.SampleHeight(p);
                if (h < sea + 2f || InRiver(source, p)) continue;
                // W-90: no tent, fire or log on water: the whole pad (flattened to h) must stay dry.
                if (RoadsideBaking.PadTouchesWater(source, p, look.Look.Radius, h)) { wet++; continue; }
                var slope = math.max(math.abs(source.SampleHeight(p + new float2(5f, 0f)) - h), math.abs(source.SampleHeight(p + new float2(0f, 5f)) - h));
                slope = math.max(slope, math.max(math.abs(source.SampleHeight(p - new float2(5f, 0f)) - h), math.abs(source.SampleHeight(p - new float2(0f, 5f)) - h)));
                if (slope > 1.2f) continue;
                var biomes = source.SampleGround(p).Biomes;
                var top = 0;
                for (int k = 1; k < 4; k++)
                    if (biomes.Weights[k] > biomes.Weights[top]) top = k;
                var id = (int)biomes.Ids[top];
                if (id < 0 || id >= palette.Count || !look.Biomes.Contains(palette[id])) continue;
                var clear = true;
                for (int i = 0; i < set.Instances.Length && clear; i++)
                    clear = math.distance(set.Instances[i].Position, p) > 70f;
                if (!clear) continue;
                best = p;
                bestDist = d;
            }
            if (math.any(math.isnan(best)))
            {
                LastLog = $"[campsitetest] no flat dry {look.Look.name} spot";
                UnityEngine.Debug.Log(LastLog);
                return;
            }
            var camp = new LandmarkInstance
            {
                Position = best, Facing = new float2(1f, 0f), Scale = 1f, Type = lookIndex,
                BaseHeight = source.SampleHeight(best), Seed = 0.5f, Key = math.hash(best) | 1u
            };
            var before = source.Landmarks;
            source.Landmarks = LandmarkBaking.Append(before, types, new List<LandmarkInstance> { camp }, allocator);
            before.Dispose();
            At = best;
            LastLog = $"[campsitetest] {look.Look.name} at {best.x:F0},{best.y:F0} (ground {camp.BaseHeight - sea:F1} m over the sea; " +
                      $"{wet} nearer spots rejected for water on the pad; roadside campsites {RoadsideBaking.CampsDry} placed, {RoadsideBaking.CampsWet} wet sites rejected)";
            UnityEngine.Debug.Log(LastLog);
        }

        static bool InRiver(in TerrainSource source, float2 xz)
        {
            if (!source.Rivers.IsCreated) return false;
            ref var rivers = ref source.Rivers.Value;
            var cell = (int2)math.floor((xz - rivers.Origin) / rivers.CellSize);
            if (math.any(cell < 0) || math.any(cell >= rivers.Resolution))
                return false;
            return rivers.Width[cell.y * rivers.Resolution + cell.x] > 0f;
        }
    }
}
