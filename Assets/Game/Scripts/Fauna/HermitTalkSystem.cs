using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-106 row 2: hermits talk to themselves. A hermit near the player now and then mutters a line about living alone,
    /// from its biome's work set (FolkWorkSets.AloneLinesFor: ash, snow, sand and storm country each have their own, and
    /// the wild set covers anywhere else), with {name} its own name: on setting to work, on sitting down (by its door,
    /// its fire or out at its lookout), and once in a while as it works or sits. Lines go up through FolkTalkSystem.Shown
    /// like any other. Each hermit waits a while before it speaks again. Works on every machine from what's synced (Task,
    /// Mode, Stage, Individual, Position); each machine picks its own lines, so nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(Unity.Entities.PresentationSystemGroup))]
    [UpdateAfter(typeof(FolkTalkSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class HermitTalkSystem : SystemBase
    {
        /// <summary>How near the player lines are said (m); how often to look (s); how long a hermit waits to speak
        /// again (s).</summary>
        const float Reach = 30f, Every = 0.25f, Again = 30f;
        /// <summary>The chance of a line on setting to work; on sitting down; per look while working or sitting.</summary>
        const float WorkChance = 0.5f, SitChance = 0.4f, IdleChance = 0.01f;

        /// <summary>Tests ("-hermittalk"): log each line said.</summary>
        public static bool LogLines;
        /// <summary>Lines said since start (tests).</summary>
        public static int Said;

        readonly Dictionary<Entity, (CreatureMode Mode, byte Stage)> seen = new Dictionary<Entity, (CreatureMode, byte)>();
        readonly Dictionary<Entity, float> nextSpeak = new Dictionary<Entity, float>();
        EntityQuery query, playerQuery;
        float nextLook;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x4E3A17u);

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            // (Statics outlive a play with domain reload off.)
            Said = 0;
        }

        protected override void OnUpdate()
        {
            var now = UnityEngine.Time.time;
            if (now < nextLook)
                return;
            nextLook = now + Every;
            if (FolkTalkSystem.Quiet || playerQuery.CalculateEntityCount() != 1 || !SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            var player = playerQuery.GetSingleton<LocalTransform>().Position;
            var hasKit = SystemAPI.TryGetSingletonBuffer<Punchy.Flora.KitBox>(out var kit, true) && kit.Length > 0;
            var palette = SpawnTable.Palette;

            CreatureSnapshotSystem.Get(World, query, out var states, out var bodies, out var entities);
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (s.Task != CreatureTask.Hermit)
                    continue;
                if (math.distancesq(s.Position, player) > Reach * Reach)
                {
                    seen.Remove(entities[i]);
                    continue;
                }
                var had = seen.TryGetValue(entities[i], out var was);
                seen[entities[i]] = (s.Mode, s.Stage);
                if (!had)
                    continue;

                // What just happened, if anything worth a mutter.
                string what = null;
                var chance = 0f;
                if (s.Mode == CreatureMode.Work && s.Stage != 0 && (was.Mode != CreatureMode.Work || was.Stage == 0))
                {
                    what = "working";
                    chance = WorkChance;
                }
                else if (s.Mode == CreatureMode.Sit && s.Stage != 0 && s.Stage != 3 && (was.Mode != CreatureMode.Sit || was.Stage == 0))
                {
                    what = "sitting";
                    chance = SitChance;
                }
                else if ((s.Mode == CreatureMode.Work || s.Mode == CreatureMode.Sit && s.Stage != 3) && s.Stage != 0)
                {
                    what = "musing";
                    chance = IdleChance;
                }
                if (what == null || rng.NextFloat() >= chance)
                    continue;
                if (nextSpeak.TryGetValue(entities[i], out var next) && now < next)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                if (hasKit && FolkTalkSystem.Walled(kit, s, ref b, player))
                    continue;

                var ground = terrain.SampleGround(s.Position.xz).Biomes.Dominant();
                var biome = palette != null && ground >= 0 && ground < palette.Length ? palette[ground] : null;
                var lines = FolkWorkSets.AloneLinesFor(biome);
                if (lines.Length == 0)
                    continue;
                var name = FolkNames.Of(s.Individual) ?? "you";
                var text = lines[rng.NextInt(lines.Length)].Replace("{name}", name);
                nextSpeak[entities[i]] = now + Again * rng.NextFloat(0.8f, 1.4f);
                FolkTalkSystem.Shown.Add(new FolkTalkSystem.Said
                {
                    Who = entities[i], Text = text, Head = s.Position + new float3(0f, b.StandHeight * s.Scale + 0.45f, 0f),
                    Start = now, Until = now + 3.5f + text.Length * 0.05f
                });
                Said++;
                if (LogLines)
                    Debug.Log($"HermitTalk: {name} ({(biome != null ? biome.name : "no biome")}, {what}, {FolkJobs.Shown(s)}) " +
                              $"at ({s.Position.x:F0}, {s.Position.z:F0}): {text}");
            }
            CreatureSnapshotSystem.Release(World, states, bodies, entities);
            FolkTalkLabels.Ensure();
            FolkTalkLabels.Show(FolkTalkSystem.Shown.Count > 0 || FolkTalkSystem.Bubbles.Count > 0);
            if (seen.Count > 256)
                seen.Clear();
            if (nextSpeak.Count > 256)
                nextSpeak.Clear();
        }
    }
}
