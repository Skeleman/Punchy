using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-110 row 4: going in at a door. A town household member whose day takes it indoors (home, or a shop it works
    // in) gives up its creature: the host sends the creature to that place's door, lets it go once it has stepped
    // through, and makes no new one for it until Whereabouts has it come out again. So a town's creatures follow how
    // many of its people are outdoors through the day. Host only, from creature state; nothing new is sent or stored
    // (a joined client sees the ghost go in at the door and go, and keeps the member's far figure hidden the same way).
    public partial class FaunaDirector
    {
        /// <summary>How many times a member's creature may be sent to its door before it is reported stuck (its brain
        /// gave up on the door and wandered off again).</summary>
        const int MemberSendInStuck = 2;

        /// <summary>Members whose creatures are on their way in (host), and how many times each has been sent.</summary>
        readonly Dictionary<uint, int> memberSentIn = new Dictionary<uint, int>();
        /// <summary>Since the last [members] log line (host): creatures let go at a door, and members sent again.</summary>
        int membersWentIn, membersStuck;
        /// <summary>Towns whose FolkJobs line has been logged.</summary>
        readonly HashSet<int> folkJobsLogged = new HashSet<int>();
        readonly Dictionary<FolkJob, int> folkJobCounts = new Dictionary<FolkJob, int>();

        /// <summary>
        /// Host, twice a second before SpawnMembers: every member's creature whose member Whereabouts has indoors, or
        /// walking in, goes to that place's door; one that has stepped through is let go there.
        /// </summary>
        void SendMembersIn()
        {
            if (Whereabouts.Towns.Count == 0 || !Households.Ready)
                return;
            var em = simWorld.EntityManager;
            var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var entities = wildQuery.ToEntityArray(Allocator.Temp);
            var gone = new NativeList<Entity>(Allocator.Temp);
            MemberClock(out var day, out var hour, out var seconds);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                if (s.Leash <= 0f || s.Gone || s.Individual == 0 || s.Mode == CreatureMode.Held ||
                    !Households.Of(s.Individual, out var house, out var m) ||
                    !Whereabouts.Towns.TryGetValue(house.Landmark, out var plan) || !plan.LocalOf.TryGetValue(house, out var local) ||
                    m >= plan.HouseResidents[local].Count)
                    continue;
                var index = plan.HouseResidents[local][m];
                var r = plan.Residents[index];
                if (!Whereabouts.Built(plan, index, day, hour))
                    continue;
                var w = Whereabouts.Of(plan, index, day, hour, seconds);
                var into = Whereabouts.IndoorEnd(plan, w);
                if (into < 0)
                {
                    memberSentIn.Remove(s.Individual);
                    continue;
                }
                var place = plan.Places[into];
                var door = place.At;
                var outward = math.normalizesafe(place.Face - place.At, new float2(0f, 1f));
                if (s.Mode == CreatureMode.Home && s.Stage >= 2)
                {
                    // Through the door: let go here, and its far figure stays hidden till it comes out (StillIn).
                    gone.Add(entities[i]);
                    house.Taken &= ~(1u << m) | house.Pinned | house.Lost;
                    memberLive.Remove(s.Individual);
                    housedCount = math.max(0, housedCount - 1);
                    memberSentIn.Remove(s.Individual);
                    r.GoneInTo = into;
                    r.GoneInLeg = w.Leg;
                    membersWentIn++;
                    continue;
                }
                if (s.Mode == CreatureMode.Home && s.Stage == 1)
                    continue;
                if (s.Mode == CreatureMode.Home)
                {
                    // On its way to a door already (its own idea, or ours): the one Whereabouts has it go in at.
                    if (math.distance(s.Anchor.xz, door) > 1f)
                    {
                        s.Anchor = new float3(door.x, place.Height, door.y);
                        s.AnchorYaw = math.atan2(outward.x, outward.y);
                        em.SetComponentData(entities[i], s);
                    }
                    continue;
                }
                // (Busy with something that isn't its day, a talk, a fright, a meal: it goes once that is over.)
                var calm = s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Graze ||
                           s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Work ||
                           s.Mode == CreatureMode.Sit || s.Mode == CreatureMode.Patrol || s.Mode == CreatureMode.Play ||
                           s.Mode == CreatureMode.Drink;
                if (!calm || s.ChatRole != 0)
                    continue;
                memberSentIn.TryGetValue(s.Individual, out var sent);
                memberSentIn[s.Individual] = ++sent;
                // (Sent before and back out again: its brain gave up on the door. Reported, not forced.)
                if (sent > MemberSendInStuck)
                {
                    membersStuck++;
                    if (FarFolk.Log && sent == MemberSendInStuck + 1)
                        Debug.Log($"[members] stuck {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark}: sent to its door {sent} times, " +
                                  $"{math.distance(s.Position.xz, door):F1} m from it, mode {s.Mode}, hour {hour:F2}");
                }
                s.Mode = CreatureMode.Home;
                s.ModeTime = 0f;
                s.Stage = 0;
                s.StageTime = 0f;
                s.Partner = Entity.Null;
                s.Action = IdleAction.None;
                s.ActionTime = 0f;
                s.Anchor = new float3(door.x, place.Height, door.y);
                s.AnchorYaw = math.atan2(outward.x, outward.y);
                s.Timer = 600f;
                em.SetComponentData(entities[i], s);
            }
            if (gone.Length > 0)
                em.DestroyEntity(gone.AsArray());
            gone.Dispose();
            states.Dispose();
            entities.Dispose();
        }

        /// <summary>
        /// A town's FolkJobs line, once, the first time it is in range (as a landmark's resident rows log theirs): how
        /// many of its members hold each job, and how many grown ones hold none.
        /// </summary>
        void LogFolkJobs(TownPlan plan)
        {
            if (!folkJobsLogged.Add(plan.Landmark))
                return;
            folkJobCounts.Clear();
            var free = 0;
            foreach (var r in plan.Residents)
            {
                if (r.House >= Households.All.Count)
                    continue;
                var house = Households.All[r.House];
                if (house.Gone(r.Member) || (house.Pinned >> r.Member & 1u) != 0)
                    continue;
                if (r.Job < 0 || r.Job >= plan.Places.Count)
                {
                    if (!r.Child)
                        free++;
                    continue;
                }
                var place = plan.Places[r.Job];
                var job = place.Job != FolkJob.None ? place.Job : FolkJobs.FromTask(place.Task);
                folkJobCounts.TryGetValue(job, out var n);
                folkJobCounts[job] = n + 1;
            }
            var line = new System.Text.StringBuilder();
            foreach (var pair in folkJobCounts)
                line.Append(line.Length > 0 ? ", " : "").Append(pair.Key).Append(' ').Append(pair.Value);
            Debug.Log($"FolkJobs: {plan.Name} at ({plan.Centre.x:F0}, {plan.Centre.y:F0}) radius {plan.Radius:F0}: {line}, free {free}");
        }
    }
}
