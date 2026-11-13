using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-breathtest" (A-102 row 1): air and water breathers held on a lake bed, their breath logged.</summary>
        bool breathTest;
        /// <summary>The test's creatures (Human, Cat, FishFolk, Carp), when they went in (Time.time), and what each did:
        /// the most breath it used, whether it emptied, and whether its head came out while it was gasping.</summary>
        readonly List<Entity> breathBodies = new List<Entity>();
        readonly List<string> breathNames = new List<string>();
        readonly List<float3> breathMarks = new List<float3>();
        float breathAt = -1f;
        bool breathDone;
        /// <summary>How long the test runs after the creatures go in (s): a person's 30 s hold, the rise, and some.</summary>
        const float BreathTestTime = 50f;

        /// <summary>
        /// "-breathtest": once the world has settled, finds water at least 3 m deep within 80 m of the player (solo or
        /// hosting) and puts a Human, a Cat, a FishFolk (standing in for sea folk until A-103) and a Carp on its bed, 1.5
        /// m apart, as puppets (they stay where they are put). Logs each second where each one is, how deep its head is
        /// and how much breath it has, and the player's; after BreathTestTime, PASS when the person and the cat both
        /// emptied their breath and came up for air while the fish folk and the carp never used any, else FAIL. Also
        /// counts the island's other air breathers out of breath at that moment (everyday life should have none).
        /// </summary>
        void BreathTest()
        {
            if (!breathTest || breathDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return;
            if (!director.CanSpawn)
            {
                Debug.LogWarning("BreathTest: needs a solo or hosting game");
                breathDone = true;
                return;
            }
            var em = director.SimWorld.EntityManager;
            if (breathAt < 0f)
            {
                // Water 3 m deep or more (not a cave's), nearest the player, on rings out to 80 m.
                var found = false;
                var at = float2.zero;
                for (var r = 4f; r <= 80f && !found; r += 4f)
                    for (int k = 0; k < 24 && !found; k++)
                    {
                        var a = k * math.PI * 2f / 24f;
                        var xz = player.Position.xz + new float2(math.sin(a), math.cos(a)) * r;
                        var g = source.SampleGround(xz);
                        if (g.HasWater && g.WaterSurface - g.Height >= 3f)
                        {
                            found = true;
                            at = xz;
                        }
                    }
                if (!found)
                {
                    Debug.LogWarning($"BreathTest: no water 3 m deep within 80 m of ({player.Position.x:F0}, {player.Position.z:F0})");
                    breathDone = true;
                    return;
                }
                var names = new[] { "Human", "Cat", "FishFolk", "Carp" };
                for (int i = 0; i < names.Length; i++)
                {
                    var index = director.SpeciesIndex(names[i]);
                    if (index < 0)
                    {
                        Debug.LogWarning($"BreathTest: no species '{names[i]}'");
                        continue;
                    }
                    var xz = at + new float2((i - 1.5f) * 1.5f, 0f);
                    var e = director.Spawn(index, new float3(xz.x, source.SampleHeight(xz), xz.y), 0f);
                    var c = em.GetComponentData<CreatureState>(e);
                    c.Mode = CreatureMode.Puppet;
                    c.Speed = 0f;
                    em.SetComponentData(e, c);
                    breathBodies.Add(e);
                    breathNames.Add(names[i]);
                    breathMarks.Add(float3.zero);
                }
                breathAt = Time.time;
                var g0 = source.SampleGround(at);
                Debug.Log($"BreathTest: {breathBodies.Count} creatures on the bed at ({at.x:F1}, {g0.Height:F2}, {at.y:F1}), " +
                          $"water surface {g0.WaterSurface:F2} ({g0.WaterSurface - g0.Height:F1} m deep)");
                return;
            }
            var since = Time.time - breathAt;
            var tick = Mathf.FloorToInt(since) != Mathf.FloorToInt(since - Time.deltaTime);
            var line = tick ? new System.Text.StringBuilder($"BreathTest {since:F0} s:") : null;
            for (int i = 0; i < breathBodies.Count; i++)
            {
                var e = breathBodies[i];
                if (!em.Exists(e) || !em.HasComponent<CreatureMind>(e))
                    continue;
                var c = em.GetComponentData<CreatureState>(e);
                var mind = em.GetComponentData<CreatureMind>(e);
                ref var b = ref em.GetComponentData<CreatureBody>(e).Blob.Value;
                var g = source.SampleGround(c.Position.xz);
                var head = Breath.Head(c, ref b);
                var mark = breathMarks[i];
                mark.x = math.max(mark.x, mind.BreathUsed);
                if (mind.Gasping)
                    mark.y = 1f;
                if (mind.Gasping && head >= g.WaterSurface)
                    mark.z = 1f;
                breathMarks[i] = mark;
                line?.Append($" {breathNames[i]} y {c.Position.y:F2} head {g.WaterSurface - head:F2} m down breath {1f - mind.BreathUsed:F2}" +
                             $"{(mind.Gasping ? " gasping" : "")} (hold {b.HoldBreath:F0} s{(b.BreathesWater ? ", breathes water" : "")});");
            }
            if (line != null)
            {
                var playerEm = queryWorld.EntityManager;
                using var local = playerEm.CreateEntityQuery(ComponentType.ReadOnly<PlayerMotion>(), ComponentType.ReadOnly<PlayerTag>());
                if (local.TryGetSingleton<PlayerMotion>(out var motion))
                    line.Append($" player breath {1f - motion.BreathUsed:F2}");
                Debug.Log(line.ToString());
            }
            if (since < BreathTestTime)
                return;
            breathDone = true;
            var pass = breathBodies.Count == 4;
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < breathBodies.Count; i++)
            {
                var m = breathMarks[i];
                var waterBreather = breathNames[i] == "FishFolk" || breathNames[i] == "Carp";
                var ok = waterBreather ? m.x <= 0f : m.y > 0f && m.z > 0f;
                pass &= ok;
                summary.Append($" {breathNames[i]}: most used {m.x:F2}, emptied {m.y > 0f}, came up {m.z > 0f} ({(ok ? "ok" : "WRONG")});");
            }
            // The island's other air breathers out of breath now (everyday life: none).
            using var all = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureMind>());
            using var minds = all.ToComponentDataArray<CreatureMind>(Allocator.Temp);
            using var allE = all.ToEntityArray(Allocator.Temp);
            var others = 0;
            for (int i = 0; i < minds.Length; i++)
                if (minds[i].BreathUsed > 0f && !breathBodies.Contains(allE[i]))
                    others++;
            Debug.Log($"BreathTest: {(pass ? "PASS" : "FAIL")}:{summary} others out of breath {others} of {minds.Length}");
        }
    }
}
