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
    // DebugControls, "-kincmd": a test driving a player build from a text file (A-106 row 3, kin deaths on a joined client).
    public partial class DebugControls
    {
        /// <summary>
        /// "-kincmd file" (A-106 row 3 tests): twice a second, runs each new line added to a text file, so a script can
        /// drive a joined client build mid-play. "ask n": asks the n housed folk nearest the player about their family
        /// (with -households, FolkConversation logs each answer that names a member, "died" for one marked lost);
        /// "move dx dz": moves the player that far (a teleport); "kill" (where folk are simulated: alone or hosting): the
        /// housed folk nearest the player dies, as TestHooks.KillHousedFolk; "mark" (a joined client): marks the housed
        /// folk nearest the player lost though its ghost is here, to see the mark cleared; "count": logs FaunaDirector.ClientKinLost and
        /// ClientKinBack. Every line run is logged as "DebugControls: kin cmd ...".
        /// </summary>
        void KinCommands()
        {
            if (Time.time < kinCmdAt)
                return;
            kinCmdAt = Time.time + 0.5f;
            if (kinCmdFile == null)
            {
                var at = System.Array.IndexOf(LaunchArgs, "-kincmd");
                kinCmdFile = at >= 0 && at + 1 < LaunchArgs.Length ? LaunchArgs[at + 1] : "";
            }
            if (kinCmdFile.Length == 0 || queryWorld == null || !queryWorld.IsCreated || !System.IO.File.Exists(kinCmdFile))
                return;
            string[] lines;
            try
            {
                lines = System.IO.File.ReadAllLines(kinCmdFile);
            }
            catch (System.IO.IOException)
            {
                return;
            }
            for (; kinCmdDone < lines.Length; kinCmdDone++)
            {
                var words = lines[kinCmdDone].Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                    continue;
                Debug.Log($"DebugControls: kin cmd {kinCmdDone}: {lines[kinCmdDone]}");
                switch (words[0])
                {
                    case "ask":
                        KinAsk(words.Length > 1 && int.TryParse(words[1], out var n) ? n : 6);
                        break;
                    case "move":
                        if (words.Length > 2 && float.TryParse(words[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dx) &&
                            float.TryParse(words[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dz))
                            KinMove(dx, dz);
                        break;
                    case "kill":
                        KinKill();
                        break;
                    case "mark":
                        KinMark();
                        break;
                    case "count":
                        Debug.Log($"DebugControls: kin cmd count: client lost {FaunaDirector.ClientKinLost}, back {FaunaDirector.ClientKinBack}");
                        break;
                }
            }
        }
        string kinCmdFile;
        int kinCmdDone;
        float kinCmdAt;

        void KinAsk(int count)
        {
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var housed = new List<(float d, Entity e)>();
            foreach (var e in entities)
            {
                var s = em.GetComponentData<CreatureState>(e);
                if (!s.Gone && Households.Of(s.Individual, out _, out _))
                    housed.Add((math.distance(s.Position, player.Position), e));
            }
            housed.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < housed.Count && i < count; i++)
            {
                var answer = FolkConversation.Ask(housed[i].e, FolkConversation.Family);
                var who = FolkNames.Of(em.GetComponentData<CreatureState>(housed[i].e).Individual);
                Debug.Log($"DebugControls: kin cmd ask: {who} ({housed[i].d:F0} m): {answer.Text}");
            }
        }

        void KinKill()
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = director.SimWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<CreatureState>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var best = Entity.Null;
            var bestD = float.MaxValue;
            foreach (var e in entities)
            {
                var c = em.GetComponentData<CreatureState>(e);
                var d = math.distance(c.Position, player.Position);
                if (!c.Gone && d < bestD && Households.Of(c.Individual, out _, out _))
                {
                    best = e;
                    bestD = d;
                }
            }
            if (best == Entity.Null)
                return;
            var s = em.GetComponentData<CreatureState>(best);
            s.Gone = true;
            em.SetComponentData(best, s);
            Debug.Log($"DebugControls: kin cmd killed {FolkNames.Of(s.Individual)} at ({s.Position.x:F1}, {s.Position.z:F1}), {bestD:F0} m from us");
        }

        void KinMark()
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var best = 0u;
            var bestD = float.MaxValue;
            foreach (var c in states)
            {
                var d = math.distance(c.Position, player.Position);
                if (d < bestD && Households.Of(c.Individual, out var house, out var m) && !house.Gone(m))
                {
                    best = c.Individual;
                    bestD = d;
                }
            }
            Debug.Log($"DebugControls: kin cmd mark {FolkNames.Of(best)}: {(best != 0 && director.MarkKinLostForTest(best) ? "marked" : "not marked")}");
        }

        void KinMove(float dx, float dz)
        {
            if (!EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var xz = t.Position.xz + new float2(dx, dz);
            t.Position = new float3(xz.x, source.SampleHeight(xz), xz.y);
            em.SetComponentData(player, t);
            Debug.Log($"DebugControls: kin cmd moved to ({xz.x:F1}, {xz.y:F1})");
        }
    }
}
