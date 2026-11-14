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
    // many of its people are outdoors through the day. It is sent when its own walk to the door takes as long as its
    // far figure's, so the two go in together, and one in first waits inside till its far figure is in too. (Coming
    // out is SpawnMembers': made in the doorway.) Host only, from creature state; nothing new is sent or stored
    // (a joined client sees the ghost go in at the door and go, and keeps the member's far figure hidden the same way).
    public partial class FaunaDirector
    {
        /// <summary>How many times a member's creature may be sent to its door before it is reported stuck (its brain
        /// gave up on the door and wandered off again).</summary>
        const int MemberSendInStuck = 2;
        /// <summary>A creature's pace to its door against its walking speed, straight-line (it goes round houses and
        /// corners): measured at town 73, 0.55 to 0.6 of it. (One held up short of its door steps through from there,
        /// GoIndoors, so the slow tail no longer makes it late.)</summary>
        const float MemberDoorPace = 0.6f;

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
            var now = Time.time;
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
                var ahead = false;
                // (Row 4 (c): its day takes it in soon, and its creature is farther from that door than its far figure,
                // at work or chatting a way off: sent now, so the two still go in together.)
                // (One sent ahead already, walking in or waiting inside, keeps that door while the far figure's leg in
                // is still to come.)
                if (into < 0 && (memberSentIn.ContainsKey(s.Individual) ? s.Mode == CreatureMode.Home : Calm(s)))
                {
                    into = DoorAhead(plan, index, r, s, day, hour, seconds, memberSentIn.ContainsKey(s.Individual));
                    ahead = into >= 0;
                }
                if (into < 0)
                {
                    memberSentIn.Remove(s.Individual);
                    r.InSince = -1f;
                    r.WaitSince = -1f;
                    continue;
                }
                var place = plan.Places[into];
                var door = place.At;
                var outward = math.normalizesafe(place.Face - place.At, new float2(0f, 1f));
                // (When Whereabouts first has it fully in while its creature is still out: how late the creature is.)
                if (!w.Outdoors && r.InSince < 0f)
                {
                    r.InSince = now;
                    // (Why it is late, unless it is already stepping through: what it was doing and how far it had to go.)
                    if (FarFolk.Log && !(s.Mode == CreatureMode.Home && s.Stage >= 1))
                    {
                        // (And whether others crowd that door: creatures within 1.5 m of it, and the one nearest itself.)
                        var crowd = 0;
                        var nearest = 999f;
                        for (int j = 0; j < states.Length; j++)
                        {
                            if (j == i || states[j].Gone)
                                continue;
                            if (math.distance(states[j].Position.xz, door) < 1.5f)
                                crowd++;
                            nearest = math.min(nearest, math.distance(states[j].Position.xz, s.Position.xz));
                        }
                        Debug.Log($"[members] late {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark}: mode {s.Mode} stage {s.Stage}, " +
                                  $"chat {s.ChatRole}, {math.distance(s.Position.xz, door):F1} m from its door ({crowd} at it, nearest other " +
                                  $"{nearest:F1} m), speed {s.Speed:F2}, mode time {s.ModeTime:F0} s, hour {hour:F2}");
                    }
                }
                if (s.Mode == CreatureMode.Home && s.Stage == 2)
                {
                    // (Row 4 (c): in ahead of its far figure, its own idea or a quick walk: it waits inside, unseen, till
                    // Whereabouts has it in too, so the two settle together. Its brain is kept from stepping out again.
                    // The far figure is hidden meanwhile on every machine: a creature in at a door hides it, FarFolk.)
                    if (w.Outdoors)
                    {
                        if (r.WaitSince < 0f)
                        {
                            r.WaitSince = now;
                            // (Why it is in early: sent by us, or its own idea; and how long its far figure has to go.)
                            if (FarFolk.Log)
                                Debug.Log($"[members] in early {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark}: " +
                                          $"{(memberSentIn.ContainsKey(s.Individual) ? (ahead ? "sent ahead" : "sent") : "own idea")}, " +
                                          $"far figure {(ahead ? "not yet walking in" : $"{w.Remaining / math.max(r.Speed, 0.3f):F0} s from the door")}, hour {hour:F2}");
                        }
                        // (A watchman's shift would step it out again at dusk: it is off duty while it waits.)
                        if (s.Timer < 5f || s.Task != CreatureTask.None)
                        {
                            s.Timer = math.max(s.Timer, 5f);
                            s.Task = CreatureTask.None;
                            em.SetComponentData(entities[i], s);
                        }
                        continue;
                    }
                    // How long it waited inside, or how late it was through its door against its far figure, in game
                    // minutes (1 real s = 1 game minute at 60 s a game hour).
                    var minutes = 60f / math.max(Whereabouts.SecondsPerHour, 1f);
                    if (r.WaitSince >= 0f)
                        RecordInLag(-(now - r.WaitSince) * minutes);
                    else
                    {
                        var late = (r.InSince >= 0f ? now - r.InSince : 0f) * minutes;
                        RecordInLag(late);
                        if (FarFolk.Log && late > 2f)
                            Debug.Log($"[members] in late {FolkNames.Of(r.Seed)} id {r.Seed:X8} of {plan.Name} #{plan.Landmark}: {late:F1} min, hour {hour:F2}");
                    }
                    r.InSince = -1f;
                    r.WaitSince = -1f;
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
                // (Stepping in, or stepping out of the door it was made at, row 4 (a): left to finish.)
                if (s.Mode == CreatureMode.Home && (s.Stage == 1 || s.Stage >= 3))
                    continue;
                if (s.Mode == CreatureMode.Home)
                {
                    // (Still out 20 s after its far figure went in: where it is, to see what holds it up.)
                    if (FarFolk.Log && r.InSince >= 0f && now - r.InSince >= 20f && now - r.InSince < 20.5f)
                        Debug.Log($"[members] still out {FolkNames.Of(r.Seed)} id {r.Seed:X8}: stage {s.Stage}, {math.distance(s.Position.xz, door):F1} m " +
                                  $"from its door, speed {s.Speed:F2}, target {math.distance(s.Target, door):F1} m from it, mode time {s.ModeTime:F0} s");
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
                if (!Calm(s))
                    continue;
                // (Row 4 (c): sent when its own walk to the door and step through take as long as its far figure's
                // walk still does, so the two go in together; at once if its far figure is in already. Its own walk is
                // reckoned at a bit over half its walking pace: that is how fast town 73's creatures made their doors,
                // straight-line, round houses and corners (dusk run, 0.8 to 0.9 m/s at a 1.5 m/s walk). Better early
                // than late: one in early waits inside, unseen, above.)
                if (w.Outdoors && !ahead && OwnWayIn(r, s, door) < w.Remaining / math.max(r.Speed, 0.3f))
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

        /// <summary>Since the last [members] log line (host): the longest a creature waited inside for its far figure,
        /// and the most one went in late against it (game minutes).</summary>
        float inEarlyMost, inLateMost;

        /// <summary>Furthest ahead (real seconds) a creature is sent to the door its day takes it into next.</summary>
        const float MemberLookAhead = 180f;
        const float MemberLookStep = 5f;

        /// <summary>Not busy with something that isn't its day (a talk, a fright, a meal): free to be sent in.</summary>
        static bool Calm(in CreatureState s) =>
            s.ChatRole == 0 &&
            (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Graze ||
             s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Work ||
             s.Mode == CreatureMode.Sit || s.Mode == CreatureMode.Patrol || s.Mode == CreatureMode.Play ||
             s.Mode == CreatureMode.Drink);

        /// <summary>A creature's own way in to a door, in real seconds: its walk there at door pace, the step through,
        /// and a little to spare.</summary>
        float OwnWayIn(Resident r, in CreatureState s, float2 door)
        {
            var pace = 1f + CreatureBlob.Trait(s.Individual, 5) * 0.08f;
            var walk = math.max(blobs[r.Species].Value.WalkSpeed * s.Scale * pace * MemberDoorPace, 0.3f);
            return math.distance(s.Position.xz, door) / walk + 0.9f + 2f;
        }

        /// <summary>
        /// Row 4 (c): the indoor place a member's day takes it into next, when its creature must set off for that door
        /// now to be in with its far figure (its own way in is as long as the time till the far figure is in), or -1.
        /// committed: it was sent already; the door is kept while the far figure's way in is within the look-ahead.
        /// Read from Whereabouts a few seconds at a time; days not yet built are not built here.
        /// </summary>
        int DoorAhead(TownPlan plan, int index, Resident r, in CreatureState s, int day, float hour, double seconds, bool committed)
        {
            var speed = math.max(r.Speed, 0.3f);
            for (var t = MemberLookStep; t <= MemberLookAhead; t += MemberLookStep)
            {
                var d = day;
                var h = hour + t / math.max(Whereabouts.SecondsPerHour, 1f);
                if (h >= 24f)
                {
                    d++;
                    h -= 24f;
                }
                if (!Whereabouts.Built(plan, index, d, h))
                    return -1;
                var fw = Whereabouts.Of(plan, index, d, h, seconds + t);
                var fi = Whereabouts.IndoorEnd(plan, fw);
                if (fi < 0)
                    continue;
                if (committed)
                    return fi;
                var tIn = t + (fw.Outdoors ? fw.Remaining / speed : 0f);
                return OwnWayIn(r, s, plan.Places[fi].At) >= tIn ? fi : -1;
            }
            return -1;
        }

        void RecordInLag(float minutes)
        {
            if (minutes < 0f)
                inEarlyMost = math.max(inEarlyMost, -minutes);
            else
                inLateMost = math.max(inLateMost, minutes);
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
