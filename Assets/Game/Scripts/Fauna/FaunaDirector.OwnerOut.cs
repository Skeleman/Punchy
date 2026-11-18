using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-134 row 4: the owner steps out. A theft seen by one of the household at home used to wait for the thief to meet
    // one of them out on their day, up to TheftOwnerWait. Now the host picks one of those at home (Whereabouts) and,
    // once a player is near its door (the thief out of the room again, or someone in the street), makes its creature in
    // the doorway, stepping out, the same way SpawnMembers makes a member coming out of a door; it walks up to the
    // nearest player, and the owner's line is said on the thief's machine as soon as it is near (SayTheftLines). After
    // OwnerOutStay it is SendMembersIn's again, which takes it back in at its door. Host or solo only, from creature
    // state; nothing new is sent or stored (a joined client sees the member's ghost, as any other).
    public partial class FaunaDirector
    {
        /// <summary>How long (s) a member called out stays out to face the thief before it goes back in, how near (m) it
        /// walks up to a player, and how near (m) a player must be for it to walk up at all.</summary>
        const float OwnerOutStay = 45f, OwnerStands = 2f, OwnerSeesThief = 30f;

        /// <summary>A member at home still to be called out at its door: who, from which town, and until when (Time.time)
        /// it waits for a player near that door.</summary>
        struct OwnerCall
        {
            public TownPlan Plan;
            public int Index;
            public float Until;
        }

        /// <summary>Host or solo: calls still to be answered, and the members out at their door now (Individual → until
        /// when, Time.time).</summary>
        readonly List<OwnerCall> ownerCalls = new List<OwnerCall>();
        readonly Dictionary<uint, float> ownersOut = new Dictionary<uint, float>();
        readonly List<int> ownerHome = new List<int>();
        readonly List<uint> ownersDone = new List<uint>();

        /// <summary>Whether a member's creature is out at its door to face a thief (host).</summary>
        bool OwnerOut(uint individual) => ownersOut.TryGetValue(individual, out var until) && Time.time < until;

        /// <summary>
        /// Host or solo, from JudgeTakes: one of a household at home (a grown one first) is called out at its door. Only
        /// a town's households have members to call (a house outside the town plans has no one to make).
        /// </summary>
        void CallOwnerOut(int houseIndex, int day, float hour)
        {
            var house = Households.All[houseIndex];
            if (!Whereabouts.Towns.TryGetValue(house.Landmark, out var plan) || !plan.LocalOf.TryGetValue(house, out var local))
            {
                Debug.Log($"[thefts] nobody steps out of {Ownership.Whose(houseIndex, "house")}: not a town's household");
                return;
            }
            ownerHome.Clear();
            Whereabouts.InHouse(plan, local, day, hour, out _, ownerHome);
            var pick = -1;
            foreach (var index in ownerHome)
            {
                var r = plan.Residents[index];
                if ((house.Pinned >> r.Member & 1u) != 0)
                    continue;
                if (pick < 0 || plan.Residents[pick].Child && !r.Child)
                    pick = index;
            }
            if (pick < 0)
            {
                Debug.Log($"[thefts] nobody steps out of {Ownership.Whose(houseIndex, "house")}: none of its {ownerHome.Count} at home can");
                return;
            }
            // (Called again before the first was answered: the one call stands.)
            foreach (var c in ownerCalls)
                if (c.Plan == plan && c.Index == pick)
                    return;
            ownerCalls.Add(new OwnerCall { Plan = plan, Index = pick, Until = Time.time + TheftOwnerWait });
            Debug.Log($"[thefts] {FolkNames.Of(plan.Residents[pick].Seed)} ({plan.Residents[pick].Role}) is called out of {Ownership.Whose(houseIndex, "house")}");
        }

        /// <summary>
        /// Host, twice a second before SendMembersIn: each call whose member is still at home is answered once a player is
        /// within Radius of its door (its creature made stepping out of it); each member out walks up to the nearest
        /// player within OwnerSeesThief, or stands at its door; past OwnerOutStay it is let go back in.
        /// </summary>
        void StepOwnersOut()
        {
            if (ownerCalls.Count == 0 && ownersOut.Count == 0)
                return;
            var now = Time.time;
            MemberClock(out var day, out var hour, out var seconds);
            for (int k = ownerCalls.Count - 1; k >= 0; k--)
            {
                var call = ownerCalls[k];
                var plan = call.Plan;
                var index = call.Index;
                var r = plan.Residents[index];
                var house = r.House < Households.All.Count ? Households.All[r.House] : null;
                if (house == null || house.Gone(r.Member) || now >= call.Until)
                {
                    Debug.Log($"[thefts] {FolkNames.Of(r.Seed)} never stepped out: {(now >= call.Until ? "no player came near its door in time" : "gone")}");
                    ownerCalls.RemoveAt(k);
                    continue;
                }
                var w = Whereabouts.Of(plan, index, day, hour, seconds);
                // (Its day took it out meanwhile: it is out as itself already, met as any member is.)
                if (w.Inside != r.House || memberLive.Contains(r.Seed))
                {
                    Debug.Log($"[thefts] {FolkNames.Of(r.Seed)} is out on its day already: not called out");
                    ownerCalls.RemoveAt(k);
                    continue;
                }
                var door = plan.Places[r.Home];
                if (NearestPlayer(door.At) > Radius || housedCount >= MaxHoused)
                    continue;   // (the thief still in the room, say: it waits for someone to come out)
                ownerCalls.RemoveAt(k);
                r.Asked = now;
                ownersOut[r.Seed] = now + OwnerOutStay;
                var outward = math.normalizesafe(door.Face - door.At, new float2(0f, 1f));
                Enqueue(r.Species, new float3(door.At.x, door.Height, door.At.y), SpawnTier.Housed, e => SetUpOwnerOut(e, plan, index),
                        math.atan2(outward.x, outward.y));
                Debug.Log($"[thefts] {FolkNames.Of(r.Seed)} steps out of its door at ({door.At.x:F1}, {door.At.y:F1}), nearest player {NearestPlayer(door.At):F0} m");
            }
            if (ownersOut.Count == 0)
                return;
            var em = simWorld.EntityManager;
            using var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var entities = wildQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                if (s.Individual == 0 || s.Gone || !OwnerOut(s.Individual) || s.Mode == CreatureMode.Home || s.Mode == CreatureMode.Held)
                    continue;   // (still stepping out of the doorway, or not one of them)
                var p = NearestPlayerAt(s.Position.xz);
                var d = math.distance(p.xz, s.Position.xz);
                if (d > OwnerSeesThief)
                    continue;   // (nobody near: it stands about at its door)
                var to = (p.xz - s.Position.xz) / math.max(d, 0.01f);
                if (d > OwnerStands + 0.5f)
                {
                    var spot = p.xz - to * OwnerStands;
                    if (s.Mode == CreatureMode.Wander && math.distance(s.Target, spot) < 1f)
                        continue;
                    SetMode(ref s, CreatureMode.Wander);
                    s.Task = CreatureTask.None;
                    s.Target = spot;
                }
                else
                {
                    if (s.Mode == CreatureMode.Idle && s.ModeTime < 5f)
                        continue;
                    SetMode(ref s, CreatureMode.Idle);
                    s.Yaw = math.atan2(to.x, to.y);
                }
                em.SetComponentData(entities[i], s);
            }
            ownersDone.Clear();
            foreach (var o in ownersOut)
                if (now >= o.Value)
                    ownersDone.Add(o.Key);
            foreach (var id in ownersDone)
            {
                ownersOut.Remove(id);
                Debug.Log($"[thefts] {FolkNames.Of(id)} goes back in");
            }
        }

        /// <summary>A called-out member's creature, just made: set up as itself (SetUpMember), then stepping out of its
        /// own door.</summary>
        void SetUpOwnerOut(Entity e, TownPlan plan, int index)
        {
            SetUpMember(e, plan, index);
            var em = simWorld.EntityManager;
            var r = plan.Residents[index];
            var st = em.GetComponentData<CreatureState>(e);
            StepOutOf(ref st, plan.Places[r.Home]);
            st.Task = CreatureTask.None;
            em.SetComponentData(e, st);
            if (em.HasComponent<LocalTransform>(e))
                em.SetComponentData(e, LocalTransform.FromPositionRotation(st.Position, quaternion.RotateY(st.Yaw)));
        }

        /// <summary>The nearest player to a spot (the sim world's), or far away when there are none.</summary>
        float3 NearestPlayerAt(float2 xz)
        {
            var best = new float3(1e9f, 0f, 1e9f);
            var bestD = float.MaxValue;
            foreach (var p in players)
            {
                var d = math.distance(p.xz, xz);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
