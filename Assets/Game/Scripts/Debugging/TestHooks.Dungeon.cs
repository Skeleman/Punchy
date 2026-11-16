#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Dungeon;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for dungeon life's dead masks (#24 phase 2 row 3): every dungeon's residents fit the 64 bits of its
    /// mask, and the residents a joined client builds from the seed are the host's records, ordinal for ordinal; r5,
    /// the posts' behaviours from socket tags.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>"registered/dungeons" where animals live: how many dungeons the director has residents for, of every
        /// dungeon with sockets (caves are made a few per frame after load).</summary>
        public static string DungeonsRegistered()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return "no animals world";
            var em = director.SimWorld.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            using var infos = query.ToComponentDataArray<DungeonInfo>(Allocator.Temp);
            var registered = 0;
            foreach (var info in infos)
                if (director.DungeonRecordOf(FaunaDirector.DungeonKey(info.Type, info.Id), out _))
                    registered++;
            return $"{registered}/{infos.Length}";
        }

        /// <summary>
        /// #24 phase 2 row 4 (rule 20): problems with the dungeon props' screen-size tiers, "" when fine. A 1 m prop at
        /// the reference view is drawn whole at 30 m, as its biggest part at 100 m and not at all at 400 m; props were
        /// placed, and last frame each was drawn whole, as its biggest part or not at all.
        /// </summary>
        public static string DungeonPropTiers()
        {
            var problems = new System.Text.StringBuilder();
            foreach (var (distance, want) in new[] { (30f, 0), (100f, 1), (400f, 2) })
                if (DungeonProps.TierAt(1f, distance) is var got && (want == 2 ? got < 2 : got != want))
                    problems.Append($"1 m prop at {distance} m: tier {got}, want {want}; ");
            var (placed, full, low, hidden) = DungeonProps.Counts;
            Debug.Log($"[dungeon props] {DungeonProps.Describe()}");
            if (placed == 0)
                problems.Append("no props placed; ");
            else if (full + low + hidden != placed)
                problems.Append($"{placed} props but {full + low + hidden} judged last frame; ");
            return problems.ToString();
        }

        /// <summary>
        /// Every dungeon whose creature picks (each takes an ordinal, so a bit) are more than the dead mask holds, as
        /// "key:count; " (empty: none). Logs "[dungeon residents] n dungeons, most m (key)".
        /// </summary>
        public static string DungeonResidentOverflow()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return "no animals world";
            var problems = new System.Text.StringBuilder();
            int dungeons = 0, most = 0;
            uint mostKey = 0;
            foreach (var (key, _, ordinals) in RebuiltResidents(director))
            {
                dungeons++;
                if (ordinals > most)
                    (most, mostKey) = (ordinals, key);
                if (ordinals > FaunaDirector.MaxDungeonResidents)
                    problems.Append($"{key:X8}:{ordinals}; ");
            }
            Debug.Log($"[dungeon residents] {dungeons} dungeons, most {most} ({mostKey:X8}), mask holds {FaunaDirector.MaxDungeonResidents}");
            return problems.ToString();
        }

        /// <summary>
        /// Where a joined client's residents (rebuilt from the dungeon's sockets as a client builds them) differ from
        /// the host's records: count, then per resident its ordinal, species, socket and post (empty: none).
        /// </summary>
        public static string DungeonOrdinalProblems()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return "no animals world";
            var problems = new System.Text.StringBuilder();
            int dungeons = 0, residents = 0;
            foreach (var (key, rebuilt, _) in RebuiltResidents(director))
            {
                dungeons++;
                if (!director.DungeonRecordOf(key, out var host))
                {
                    problems.Append($"{key:X8} not registered; ");
                    continue;
                }
                if (host.Length != rebuilt.Count)
                {
                    problems.Append($"{key:X8} {rebuilt.Count} residents, host {host.Length}; ");
                    continue;
                }
                for (int i = 0; i < host.Length; i++)
                {
                    var a = rebuilt[i];
                    var b = host[i];
                    residents++;
                    if (a.Ordinal != b.Ordinal || a.Species != b.Species || a.Socket != b.Socket ||
                        math.any(a.Post.Anchor != b.Post.Anchor) || a.Post.Behaviour != b.Post.Behaviour ||
                        a.Post.Name != b.Post.Name || a.Post.Dungeon != b.Post.Dungeon)
                        problems.Append($"{key:X8}[{i}] ordinal {a.Ordinal}/{b.Ordinal} species {a.Species}/{b.Species} " +
                                        $"socket {a.Socket}/{b.Socket} post {a.Post.Name}/{b.Post.Name}; ");
                }
            }
            Debug.Log($"[dungeon residents] ordinals: {dungeons} dungeons, {residents} residents compared with the host's records");
            return problems.ToString();
        }

        /// <summary>
        /// #24 phase 2 r5: problems with the posts' behaviours from socket tags, "" when fine. Only bats roost; patrol
        /// beats run 4 to 20 m; nothing that flies, and no ghost, naps; and on the pinned seed some creature has each of
        /// the r5 behaviours (roost, patrol, sleep) or the tags never reach them. Logs the count of each behaviour.
        /// </summary>
        public static string DungeonPostProblems()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return "no animals world";
            var problems = new System.Text.StringBuilder();
            var counts = new int[System.Enum.GetValues(typeof(DungeonBehaviour)).Length];
            foreach (var (key, residents, _) in RebuiltResidents(director))
                foreach (var r in residents)
                {
                    var post = r.Post;
                    counts[(int)post.Behaviour]++;
                    var name = post.Name.ToString();
                    if (post.Behaviour == DungeonBehaviour.Roost && name != nameof(DungeonContent.Bat))
                        problems.Append($"{key:X8} socket {r.Socket}: {name} roosts; ");
                    if (post.Behaviour == DungeonBehaviour.Sleep &&
                        (name == nameof(DungeonContent.Bat) || name == nameof(DungeonContent.GlowMoth) || name == nameof(DungeonContent.Ghost)))
                        problems.Append($"{key:X8} socket {r.Socket}: {name} naps; ");
                    if (post.Behaviour == DungeonBehaviour.Patrol)
                    {
                        var beat = math.distance(post.Anchor.xz, post.Beat.xz);
                        if (beat < 4f || beat > 20f)
                            problems.Append($"{key:X8} socket {r.Socket}: beat {beat:F1} m; ");
                    }
                }
            var line = new System.Text.StringBuilder();
            for (int i = 0; i < counts.Length; i++)
                line.Append($"{(DungeonBehaviour)i} {counts[i]} ");
            Debug.Log($"[dungeon posts] {line}");
            if (counts[(int)DungeonBehaviour.Roost] == 0 || counts[(int)DungeonBehaviour.Patrol] == 0 || counts[(int)DungeonBehaviour.Sleep] == 0)
                problems.Append($"some r5 behaviour never picked: {line}");
            return problems.ToString();
        }

        /// <summary>Each dungeon in the animals' world, its residents built afresh from its sockets (picks, then
        /// <see cref="DungeonSpawner.Residents"/>, as a joined client does) and its creature picks.</summary>
        static IEnumerable<(uint key, List<DungeonResident> residents, int ordinals)> RebuiltResidents(FaunaDirector director)
        {
            var em = director.SimWorld.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            var entities = query.ToEntityArray(Allocator.Temp);
            var picks = new List<DungeonContent>();
            var counts = new int[5];
            try
            {
                foreach (var e in entities)
                {
                    var info = em.GetComponentData<DungeonInfo>(e);
                    var sockets = em.GetBuffer<DungeonSocket>(e, true);
                    DungeonSpawner.PickAll(sockets, picks, counts);
                    var residents = new List<DungeonResident>();
                    var ordinals = DungeonSpawner.Residents(sockets, picks, info.Id, director, residents);
                    yield return (FaunaDirector.DungeonKey(info.Type, info.Id), residents, ordinals);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
    }
}
#endif
