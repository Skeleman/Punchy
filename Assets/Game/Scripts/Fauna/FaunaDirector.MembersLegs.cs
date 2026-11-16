using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-110 row 5: low-rate brains. A town household member's creature beyond talk range of every player has no mind
    // of its own: it only follows its day (Whereabouts), walking after the spot its far figure would be at, on the
    // brain LOD's low rate (CreatureBrainSystem.Legs). So it is where its day has it whenever a player comes near, and
    // the brain's own homegoing, drifting and wandering off end for far members. The full brain wakes when a player
    // comes within talk range or something disturbs it (the brain decides that), and this puts it back on legs once
    // it is calm and back at its day's spot. Host only, from creature state and the town plans; the legs live in
    // CreatureMind, host-only brain memory, so nothing new is sent or stored.
    public partial class FaunaDirector
    {
        /// <summary>A member goes on legs only this far (m) from every player: a little past the wake range, so one at
        /// the edge doesn't flip between the two. // sim:</summary>
        const float MemberLegsRange = CreatureBrainSystem.LegsRange + 4f;
        /// <summary>How near (m) its day's spot a woken creature must be to go back on legs; further off, its full brain
        /// walks it back there first (round what is in the way, which legs don't do).</summary>
        const float MemberLegsJoin = 3f;

        /// <summary>Member creatures per town (landmark), on legs and on the full brain, at the last pass: the
        /// [members] line's brain tiers.</summary>
        readonly Dictionary<int, (int legs, int full)> memberTiers = new Dictionary<int, (int, int)>();
        bool largestTownLogged;

        /// <summary>
        /// Host, twice a second after SendMembersIn: every member's creature that is outdoors on its day, calm and
        /// beyond talk range goes on (or stays on) legs, handed where its day has it now and how fast that spot moves,
        /// in the mode its day calls for (walking, at its post, standing about).
        /// </summary>
        void MembersLegs()
        {
            memberTiers.Clear();
            if (Whereabouts.Towns.Count == 0 || !Households.Ready || players.Count == 0)
                return;
            LogLargestTown();
            var em = simWorld.EntityManager;
            var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var entities = wildQuery.ToEntityArray(Allocator.Temp);
            MemberClock(out var day, out var hour, out var seconds);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                if (s.Leash <= 0f || s.Gone || s.Individual == 0 || s.Mode == CreatureMode.Held ||
                    !Households.Of(s.Individual, out var house, out var m) ||
                    !Whereabouts.Towns.TryGetValue(house.Landmark, out var plan) || !plan.LocalOf.TryGetValue(house, out var local) ||
                    m >= plan.HouseResidents[local].Count || !em.HasComponent<CreatureMind>(entities[i]))
                    continue;
                var index = plan.HouseResidents[local][m];
                var r = plan.Residents[index];
                var mind = em.GetComponentData<CreatureMind>(entities[i]);
                var onLegs = OnLegs(plan, index, r, ref s, ref mind, day, hour, seconds, out var stateChanged);
                if (onLegs != (mind.Legs != 0))
                {
                    mind.Legs = (byte)(onLegs ? 1 : 0);
                    mind.LodDebt = 0f;
                    mind.LodCoast = float2.zero;
                }
                em.SetComponentData(entities[i], mind);
                if (stateChanged)
                    em.SetComponentData(entities[i], s);
                memberTiers.TryGetValue(plan.Landmark, out var tiers);
                memberTiers[plan.Landmark] = onLegs ? (tiers.legs + 1, tiers.full) : (tiers.legs, tiers.full + 1);
            }
            states.Dispose();
            entities.Dispose();
        }

        /// <summary>
        /// Whether a member's creature should be on legs now, and if so its legs and mode set from its day. One that
        /// may go but is too far from its day's spot is sent walking back to it on its full brain (stateChanged when
        /// the creature's state was changed).
        /// </summary>
        bool OnLegs(TownPlan plan, int index, Resident r, ref CreatureState s, ref CreatureMind mind, int day, float hour,
                    double seconds, out bool stateChanged)
        {
            stateChanged = false;
            // (Not while it is busy with something its day didn't give it, sent in at a door, followed, guarding the
            // streets, or near a player.)
            if (!Calm(s) || s.Mode == CreatureMode.Home || s.Holder != 0 || mind.FollowHolder != 0 ||
                s.Task == CreatureTask.Patrol || memberSentIn.ContainsKey(s.Individual) ||
                NearestPlayer(s.Position.xz) <= MemberLegsRange || !Whereabouts.Built(plan, index, day, hour))
                return false;
            // (A creature already on legs that the brain hasn't woken keeps its mode; a calm full-brain one in a mode
            // legs don't run, sitting or resting, is put back into its day's.)
            var w = Whereabouts.Of(plan, index, day, hour, seconds);
            if (!w.Outdoors || Whereabouts.IndoorEnd(plan, w) >= 0 || Whereabouts.StillIn(r, w))
                return false;
            var spot = w.Position.xz;
            if (mind.Legs == 0 && math.distance(s.Position.xz, spot) > MemberLegsJoin)
            {
                // Back to where its day has it, on its own feet first.
                if (s.Mode != CreatureMode.Wander || math.distance(s.Target, spot) > 1f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Task = CreatureTask.None;
                    s.Target = spot;
                    s.Timer = 6f;
                    stateChanged = true;
                }
                return false;
            }

            // Where its day has it now and a second on (how fast and which way that spot moves).
            mind.LegsAt = spot;
            mind.LegsSet = s.Clock;
            mind.LegsVel = float2.zero;
            var nextHour = hour + 1f / math.max(Whereabouts.SecondsPerHour, 1f);
            var nextDay = day;
            if (nextHour >= 24f)
            {
                nextDay++;
                nextHour -= 24f;
            }
            if (Whereabouts.Built(plan, index, nextDay, nextHour))
            {
                var next = Whereabouts.Of(plan, index, nextDay, nextHour, seconds + 1.0);
                if (next.Outdoors)
                    mind.LegsVel = next.Position.xz - spot;
            }
            mind.LegsFace = spot + w.Facing * 3f;

            // The mode its day calls for, changed only when it changes (so a work pose, once struck, stays).
            var job = r.Job >= 0 && r.Job < plan.Places.Count ? plan.Places[r.Job] : default;
            var walking = (w.Doing == Doing.Walk || w.Doing == Doing.Carry) && w.From != w.To && w.To >= 0 && w.To < plan.Places.Count;
            if (walking)
            {
                var to = plan.Places[w.To].At;
                if (s.Mode != CreatureMode.Wander || math.distance(s.Target, to) > 1f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Task = CreatureTask.None;
                    s.Target = to;
                    TownLeash(ref s, plan);
                    stateChanged = true;
                }
            }
            else if (w.Doing == Doing.Work && w.To == r.Job && job.Task != CreatureTask.None)
            {
                // At its post (the spot its day has it at there, so the far figure and the creature stand alike).
                if (s.Mode != CreatureMode.Work || s.Task != job.Task || math.distance(s.WorkSpot, spot) > 0.5f)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.Task = job.Task;
                    s.Job = (byte)job.Job;
                    s.WorkSpot = spot;
                    s.WorkFace = job.WorkFace;
                    s.Home = job.At;
                    s.Leash = 7f;
                    stateChanged = true;
                }
                mind.LegsFace = job.WorkFace;
            }
            else if (s.Mode != CreatureMode.Idle)
            {
                // Standing about: at a well, on a bench, at play, by its door.
                SetMode(ref s, CreatureMode.Idle);
                s.Task = CreatureTask.None;
                TownLeash(ref s, plan);
                stateChanged = true;
            }
            return true;
        }

        /// <summary>Off its post: held to its town again, as SetUpMember makes it (a post holds it 7 m from the post).</summary>
        static void TownLeash(ref CreatureState s, TownPlan plan)
        {
            s.Home = plan.Centre;
            s.Leash = plan.Radius * 0.8f;
        }

        /// <summary>A fresh mode from the director: its stage and timers from the start, no partner, no idle action.</summary>
        static void SetMode(ref CreatureState s, CreatureMode mode)
        {
            s.Mode = mode;
            s.ModeTime = 0f;
            s.Stage = 0;
            s.StageTime = 0f;
            s.Partner = Entity.Null;
            s.Action = IdleAction.None;
            s.ActionTime = 0f;
            s.Timer = 6f;
        }

        /// <summary>The [members] line's brain tiers for a town: its member creatures on legs and on the full brain.</summary>
        string MemberTiers(int landmark) =>
            memberTiers.TryGetValue(landmark, out var t) ? $", legs {t.legs}, full {t.full}" : ", legs 0, full 0";

        /// <summary>-farfolk: once, the town with the most household members, where it is (the row's second count run).</summary>
        void LogLargestTown()
        {
            if (largestTownLogged || !FarFolk.Log)
                return;
            largestTownLogged = true;
            TownPlan largest = null;
            foreach (var plan in Whereabouts.Towns.Values)
                if (largest == null || plan.Residents.Count > largest.Residents.Count ||
                    plan.Residents.Count == largest.Residents.Count && plan.Landmark < largest.Landmark)
                    largest = plan;
            if (largest != null)
                Debug.Log($"[members] largest town: {largest.Name} #{largest.Landmark} at ({largest.Centre.x:F0}, {largest.Centre.y:F0}), " +
                          $"{largest.Houses.Count} houses, {largest.Residents.Count} members, of {Whereabouts.Towns.Count} towns");
        }
    }
}
