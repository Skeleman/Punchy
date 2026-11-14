using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-divetest [Human|FishFolk]" (A-102 row 2): a person and a fish folk out swimming in deep water, their
        /// dives logged; the shot camera follows the one named (null: off).</summary>
        string diveTest;
        /// <summary>The test's swimmers, when they went in (Time.time), and what each did: the deepest it went, the most
        /// breath it used, whether it gasped, how many dives it came back up from, and its longest time with its head under.</summary>
        readonly List<Entity> diveBodies = new List<Entity>();
        readonly List<string> diveNames = new List<string>();
        readonly List<float4> diveMarks = new List<float4>();
        readonly List<float2> diveUnder = new List<float2>();
        readonly List<bool> diveDown = new List<bool>();
        float diveAt = -1f;
        bool diveDone;
        int diveNaN;
        /// <summary>How long the test runs after the swimmers go in (s): about three of a person's dives and rests.</summary>
        const float DiveTestTime = 150f;

        /// <summary>
        /// "-divetest": once the world has settled, finds water at least 4 m deep within 80 m of the player (solo or
        /// hosting) and puts a Human and a FishFolk (standing in for sea folk until A-103) in it out swimming (Swim,
        /// stage 1) round that spot for longer than the test runs, with the player's spot as their bank. Logs every 2 s
        /// how far down each is (CreatureMind.Dive), its breath and pitch (Vertical), and counts creatures anywhere with a
        /// non-finite position, pitch or dive (the NaN probe). After DiveTestTime, PASS when the person dived and came
        /// back up at least twice without ever running out of breath, the fish folk dived without using any, and the
        /// probe never counted one, else FAIL.
        /// </summary>
        void DiveTest()
        {
            if (diveTest == null || diveDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return;
            if (!director.CanSpawn)
            {
                Debug.LogWarning("DiveTest: needs a solo or hosting game");
                diveDone = true;
                return;
            }
            var em = director.SimWorld.EntityManager;
            if (diveAt < 0f)
            {
                // Water 4 m deep or more, nearest the player, on rings out to 80 m.
                var found = false;
                var at = float2.zero;
                for (var r = 4f; r <= 80f && !found; r += 4f)
                    for (int k = 0; k < 24 && !found; k++)
                    {
                        var a = k * math.PI * 2f / 24f;
                        var xz = player.Position.xz + new float2(math.sin(a), math.cos(a)) * r;
                        var g = source.SampleGround(xz);
                        if (g.HasWater && g.WaterSurface - g.Height >= 4f)
                        {
                            found = true;
                            at = xz;
                        }
                    }
                if (!found)
                {
                    Debug.LogWarning($"DiveTest: no water 4 m deep within 80 m of ({player.Position.x:F0}, {player.Position.z:F0})");
                    diveDone = true;
                    return;
                }
                var names = new[] { "Human", "FishFolk" };
                for (int i = 0; i < names.Length; i++)
                {
                    var index = director.SpeciesIndex(names[i]);
                    if (index < 0)
                    {
                        Debug.LogWarning($"DiveTest: no species '{names[i]}'");
                        continue;
                    }
                    var xz = at + new float2((i - 0.5f) * 2f, 0f);
                    var g = source.SampleGround(xz);
                    var e = director.Spawn(index, new float3(xz.x, g.WaterSurface - 0.3f, xz.y), 0f);
                    var c = em.GetComponentData<CreatureState>(e);
                    c.Mode = CreatureMode.Swim;
                    c.Stage = 1;
                    c.WorkSpot = player.Position.xz;
                    c.Focus = at;
                    c.Target = xz;
                    c.Timer = 1e4f;
                    em.SetComponentData(e, c);
                    diveBodies.Add(e);
                    diveNames.Add(names[i]);
                    diveMarks.Add(float4.zero);
                    diveUnder.Add(float2.zero);
                    diveDown.Add(false);
                }
                diveAt = Time.time;
                var g0 = source.SampleGround(at);
                Debug.Log($"DiveTest: {diveBodies.Count} swimmers in at ({at.x:F1}, {g0.WaterSurface:F2}, {at.y:F1}), " +
                          $"{g0.WaterSurface - g0.Height:F1} m deep");
                return;
            }
            var since = Time.time - diveAt;
            var tick = Mathf.FloorToInt(since * 0.5f) != Mathf.FloorToInt((since - Time.deltaTime) * 0.5f);
            var line = tick ? new System.Text.StringBuilder($"DiveTest {since:F0} s:") : null;
            for (int i = 0; i < diveBodies.Count; i++)
            {
                var e = diveBodies[i];
                if (!em.Exists(e) || !em.HasComponent<CreatureMind>(e))
                    continue;
                var c = em.GetComponentData<CreatureState>(e);
                var mind = em.GetComponentData<CreatureMind>(e);
                ref var b = ref em.GetComponentData<CreatureBody>(e).Blob.Value;
                var g = source.SampleGround(c.Position.xz);
                var head = Breath.Head(c, ref b);
                // Deepest, most breath used, gasped, dives come back up from.
                var mark = diveMarks[i];
                mark.x = math.max(mark.x, mind.Dive);
                mark.y = math.max(mark.y, mind.BreathUsed);
                if (mind.Gasping)
                    mark.z = 1f;
                if (mind.Dive > 1f)
                    diveDown[i] = true;
                else if (diveDown[i] && mind.Dive < 0.05f)
                {
                    diveDown[i] = false;
                    mark.w++;
                }
                diveMarks[i] = mark;
                // Time with the head under: now, longest.
                var under = diveUnder[i];
                under.x = head < g.WaterSurface ? under.x + Time.deltaTime : 0f;
                under.y = math.max(under.y, under.x);
                diveUnder[i] = under;
                line?.Append($" {diveNames[i]} {mind.Dive:F2} m down (to {mind.DiveTo:F2}, wait {mind.DiveWait:F0}) head {g.WaterSurface - head:F2} m under, " +
                             $"breath {1f - mind.BreathUsed:F2}{(mind.Gasping ? " gasping" : "")} pitch {c.Vertical:F2} mode {c.Mode}/{c.Stage};");
                if (diveTest.Length > 0 && string.Equals(diveTest, diveNames[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    // Following it: 5 m off to the side, 2.5 m over the water, looking down at it.
                    var side = new float2(math.cos(c.Yaw), -math.sin(c.Yaw));
                    var camXZ = c.Position.xz + side * 5f;
                    shotCamFrom = new Vector3(camXZ.x, math.max(g.WaterSurface + 2.5f, source.SampleHeight(camXZ) + 1f), camXZ.y);
                    shotCamTo = new Vector3(c.Position.x, c.Position.y, c.Position.z);
                    shotCamOn = true;
                }
            }
            if (line != null)
            {
                // The NaN probe: creatures anywhere with a non-finite position, pitch or dive.
                using var all = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureMind>());
                using var states = all.ToComponentDataArray<CreatureState>(Allocator.Temp);
                using var minds = all.ToComponentDataArray<CreatureMind>(Allocator.Temp);
                var bad = 0;
                for (int i = 0; i < states.Length; i++)
                    if (!math.all(math.isfinite(states[i].Position)) || !math.isfinite(states[i].Vertical) ||
                        !math.isfinite(minds[i].Dive + minds[i].DiveTo + minds[i].DiveWait))
                        bad++;
                diveNaN = math.max(diveNaN, bad);
                line.Append($" NaN probe {bad} of {states.Length}");
                Debug.Log(line.ToString());
            }
            if (since < DiveTestTime)
                return;
            diveDone = true;
            var pass = diveBodies.Count == 2 && diveNaN == 0;
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < diveBodies.Count; i++)
            {
                var m = diveMarks[i];
                var sea = diveNames[i] == "FishFolk";
                var ok = sea ? m.x > 1f && m.y <= 0f : m.x > 1f && m.w >= 2f && m.z <= 0f;
                pass &= ok;
                summary.Append($" {diveNames[i]}: deepest {m.x:F2} m, most breath used {m.y:F2}, gasped {m.z > 0f}, dives {m.w:F0}, " +
                               $"longest under {diveUnder[i].y:F0} s ({(ok ? "ok" : "WRONG")});");
            }
            Debug.Log($"DiveTest: {(pass ? "PASS" : "FAIL")}:{summary} NaN probe most {diveNaN}");
        }
    }
}
