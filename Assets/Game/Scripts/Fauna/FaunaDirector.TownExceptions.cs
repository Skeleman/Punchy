using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-110 row 6: one town's exceptions as the host sends them: the town's landmark and one bit per member, in the
    /// town's fixed member order (TownPlan.Residents), set for each member who has died. 256 members at most. Sent to a
    /// client as it joins for every town that has any, and to every client when a town's list changes. The only wire
    /// data the town exceptions add (DESIGN-population.md section 6, ruling 2).
    /// </summary>
    public struct TownExceptionsRpc : IRpcCommand
    {
        public int Landmark;
        public ulong Dead0, Dead1, Dead2, Dead3;
    }

    /// <summary>
    /// One member of a town who departs from its Whereabouts day (A-110 row 6): its household (by key), which member of
    /// it, how (Dead now; displaced later), and on which day. A plain record a save or world file could carry (rule 22);
    /// it lasts the session until saving exists.
    /// </summary>
    public struct TownException
    {
        public const byte Dead = 1, Displaced = 2;
        public uint House;
        public byte Member;
        public byte Kind;
        public ushort Day;
    }

    /// <summary>
    /// The towns' exceptions (A-110 row 6), per town landmark. The host's list is the record; Household.Lost is what
    /// the readers use (Whereabouts gives the dead no figure and no creature, HomeSign doesn't count them in, family
    /// talk speaks of them as lost). A joined client keeps the bits the host sent it and sets Lost from them.
    /// </summary>
    public static class TownExceptions
    {
        /// <summary>The most members a town can have and still be sent (four ulongs of bits).</summary>
        public const int MaxMembers = 256;
        internal static readonly Dictionary<int, List<TownException>> ByTown = new Dictionary<int, List<TownException>>();

        /// <summary>How many exceptions there are on the whole island.</summary>
        public static int Count
        {
            get
            {
                var n = 0;
                foreach (var list in ByTown.Values)
                    n += list.Count;
                return n;
            }
        }

        /// <summary>A town's dead as bits in its member order, from its households (what is sent).</summary>
        internal static void Mask(TownPlan plan, out ulong d0, out ulong d1, out ulong d2, out ulong d3)
        {
            d0 = d1 = d2 = d3 = 0ul;
            for (int i = 0; i < plan.Residents.Count && i < MaxMembers; i++)
            {
                var r = plan.Residents[i];
                if (!Households.All[r.House].Gone(r.Member))
                    continue;
                var bit = 1ul << (i & 63);
                switch (i >> 6)
                {
                    case 0: d0 |= bit; break;
                    case 1: d1 |= bit; break;
                    case 2: d2 |= bit; break;
                    default: d3 |= bit; break;
                }
            }
        }

        internal static void Clear() => ByTown.Clear();
    }

    public partial class FaunaDirector
    {
        /// <summary>Host: towns whose list changed and is still to be sent to every client (sent once its plan is built).</summary>
        readonly HashSet<int> exceptionsDirty = new HashSet<int>();
        /// <summary>Host: the connections that have had every town's list, as they joined.</summary>
        readonly HashSet<Entity> exceptionsSentTo = new HashSet<Entity>();
        readonly List<Entity> exceptionsGone = new List<Entity>();
        /// <summary>Joined client: each town's bits as the host last sent them, and the towns they've been applied to.</summary>
        readonly Dictionary<int, (ulong, ulong, ulong, ulong)> exceptionsHeard = new Dictionary<int, (ulong, ulong, ulong, ulong)>();
        readonly HashSet<int> exceptionsApplied = new HashSet<int>();
        World exceptionsServer, exceptionsClient;
        EntityQuery exceptionsConnections, exceptionsReceived;

        /// <summary>The simulation world the host's list belongs to (a new one, a new session, starts it empty), and
        /// whether the list has been put back into the households since they were last built.</summary>
        World exceptionsSession;
        bool exceptionsRestored;

        /// <summary>
        /// Host or solo, from HouseMemberDied: a housed member has died, so its landmark's list gains it. Only towns
        /// (landmarks with a TownPlan) are sent; the igloos, domes, bat folk and hermits keep theirs on the host.
        /// </summary>
        void TownMemberDied(Household house, int member)
        {
            if (!TownExceptions.ByTown.TryGetValue(house.Landmark, out var list))
                TownExceptions.ByTown[house.Landmark] = list = new List<TownException>();
            list.Add(new TownException
            {
                House = house.Key,
                Member = (byte)member,
                Kind = TownException.Dead,
                Day = (ushort)(Cycle != null ? Cycle.Day : 0)
            });
            exceptionsDirty.Add(house.Landmark);
            Debug.Log($"[town exceptions] town {house.Landmark}: {house.NameOf(member)} died; {list.Count} in its list, {TownExceptions.Count} on the island");
        }

        /// <summary>
        /// Each frame: a host sends each joining client every town's list and every client a changed one; a joined
        /// client applies what it was sent to its households. Solo reads the list directly. Solo and host put their
        /// list back into the households when those are rebuilt (the list is the record, Lost what readers use).
        /// </summary>
        void ServeTownExceptions()
        {
            if (simWorld != exceptionsSession)
            {
                exceptionsSession = simWorld;
                TownExceptions.Clear();
                exceptionsDirty.Clear();
                exceptionsRestored = false;
            }
            if (simWorld != null && !exceptionsRestored && Households.Ready)
            {
                exceptionsRestored = true;
                RestoreTownExceptions();
            }
            if (simWorld != null && simWorld.IsCreated && simWorld.IsServer())
                SendTownExceptions(simWorld);
            if (queryWorld != null && queryWorld.IsCreated && queryWorld.IsClient())
                HearTownExceptions(queryWorld, simWorld == null);
        }

        void SendTownExceptions(World server)
        {
            var em = server.EntityManager;
            if (exceptionsServer != server)
            {
                exceptionsServer = server;
                exceptionsConnections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                exceptionsSentTo.Clear();
            }
            using var connections = exceptionsConnections.ToEntityArray(Allocator.Temp);
            // (A connection gone is forgotten, so an entity reused by a later one is sent everything again.)
            exceptionsGone.Clear();
            foreach (var c in exceptionsSentTo)
                if (connections.IndexOf(c) < 0)
                    exceptionsGone.Add(c);
            foreach (var c in exceptionsGone)
                exceptionsSentTo.Remove(c);
            foreach (var c in connections)
            {
                if (exceptionsSentTo.Contains(c))
                    continue;
                exceptionsSentTo.Add(c);
                var sent = 0;
                foreach (var landmark in TownExceptions.ByTown.Keys)
                    if (Whereabouts.Towns.TryGetValue(landmark, out var plan))
                    {
                        SendTownMask(em, c, plan);
                        sent++;
                    }
                if (sent > 0)
                    Debug.Log($"[town exceptions] host: sent {sent} towns' lists to a joining client ({TownExceptions.Count} exceptions)");
            }
            if (exceptionsDirty.Count == 0)
                return;
            foreach (var landmark in exceptionsDirty)
            {
                if (!Whereabouts.Towns.TryGetValue(landmark, out var plan))
                    continue;
                foreach (var c in connections)
                    SendTownMask(em, c, plan);
                if (connections.Length > 0)
                    Debug.Log($"[town exceptions] host: town {landmark}'s list sent to {connections.Length} clients");
            }
            // (A landmark with no plan yet waits for TownPlanBuilt, which marks it again; one that never gets a plan is
            // not a town and is never sent.)
            exceptionsDirty.Clear();
        }

        static void SendTownMask(EntityManager em, Entity connection, TownPlan plan)
        {
            TownExceptions.Mask(plan, out var d0, out var d1, out var d2, out var d3);
            var rpc = em.CreateEntity(typeof(TownExceptionsRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(rpc, new TownExceptionsRpc { Landmark = plan.Landmark, Dead0 = d0, Dead1 = d1, Dead2 = d2, Dead3 = d3 });
            em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        /// <summary>From WatchFarFolk, when a town's plan is (re)built: a host sends its list again to every client, as
        /// a client that joined while the plan was missing got none for it.</summary>
        internal void TownPlanBuilt(int landmark)
        {
            if (TownExceptions.ByTown.ContainsKey(landmark))
                exceptionsDirty.Add(landmark);
        }

        /// <summary>A client world: takes the host's lists in; a joined client (apply) sets its households from them.
        /// (A host's own client world gets them too, and drops them: its households are the host's.)</summary>
        void HearTownExceptions(World client, bool apply)
        {
            var em = client.EntityManager;
            if (exceptionsClient != client)
            {
                exceptionsClient = client;
                exceptionsReceived = em.CreateEntityQuery(ComponentType.ReadOnly<TownExceptionsRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
                exceptionsHeard.Clear();
                exceptionsApplied.Clear();
                if (apply)
                    TownExceptions.Clear();  // (a new host: its lists replace the last one's)
            }
            if (!exceptionsReceived.IsEmpty)
            {
                using var rpcs = exceptionsReceived.ToEntityArray(Allocator.Temp);
                foreach (var e in rpcs)
                {
                    var m = em.GetComponentData<TownExceptionsRpc>(e);
                    em.DestroyEntity(e);
                    if (!apply)
                        continue;
                    exceptionsHeard[m.Landmark] = (m.Dead0, m.Dead1, m.Dead2, m.Dead3);
                    exceptionsApplied.Remove(m.Landmark);
                    var dead = math.countbits(m.Dead0) + math.countbits(m.Dead1) + math.countbits(m.Dead2) + math.countbits(m.Dead3);
                    Debug.Log($"[town exceptions] client: town {m.Landmark}'s list from the host, {dead} dead");
                }
            }
            if (!apply || !Households.Ready)
                return;
            foreach (var heard in exceptionsHeard)
            {
                if (exceptionsApplied.Contains(heard.Key) || !Whereabouts.Towns.TryGetValue(heard.Key, out var plan))
                    continue;
                exceptionsApplied.Add(heard.Key);
                ApplyTownMask(plan, heard.Value);
            }
        }

        /// <summary>Solo or host, after the households are (re)built: each record's member is dead in its house again.</summary>
        void RestoreTownExceptions()
        {
            var restored = 0;
            foreach (var town in TownExceptions.ByTown)
            {
                if (!Households.ByLandmark.TryGetValue(town.Key, out var houses))
                    continue;
                foreach (var x in town.Value)
                    foreach (var h in houses)
                    {
                        var house = Households.All[h];
                        if (house.Key != x.House || x.Member >= house.Members.Length || x.Kind != TownException.Dead)
                            continue;
                        house.Lost |= 1u << x.Member;
                        house.Taken |= 1u << x.Member;
                        restored++;
                        break;
                    }
            }
            if (restored > 0)
                Debug.Log($"[town exceptions] {restored} dead put back into the rebuilt households");
        }

        /// <summary>A joined client: a town's households' dead, exactly as the host's bits have them, and its list as
        /// records (the day isn't sent: 0).</summary>
        void ApplyTownMask(TownPlan plan, (ulong, ulong, ulong, ulong) bits)
        {
            var list = new List<TownException>();
            for (int i = 0; i < plan.Residents.Count && i < TownExceptions.MaxMembers; i++)
            {
                var word = (i >> 6) switch { 0 => bits.Item1, 1 => bits.Item2, 2 => bits.Item3, _ => bits.Item4 };
                var r = plan.Residents[i];
                var house = Households.All[r.House];
                if ((word >> (i & 63) & 1ul) != 0)
                {
                    house.Lost |= 1u << r.Member;
                    list.Add(new TownException { House = house.Key, Member = (byte)r.Member, Kind = TownException.Dead });
                    if (LogHouses)
                        Debug.Log($"[households] client: died {house.NameOf(r.Member)} {house.Members[r.Member].Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) landmark {house.Landmark} (from the host's list)");
                }
                else
                    house.Lost &= ~(1u << r.Member);
            }
            if (list.Count > 0)
                TownExceptions.ByTown[plan.Landmark] = list;
            else
                TownExceptions.ByTown.Remove(plan.Landmark);
            Debug.Log($"[town exceptions] client: town {plan.Landmark} applied, {list.Count} dead; {TownExceptions.Count} on the island");
        }

        /// <summary>The town exceptions' part of Teardown. The host's list lasts the session (households keep their
        /// dead the same way); a rebuild re-applies a joined client's bits to the new houses.</summary>
        void TeardownTownExceptions()
        {
            exceptionsRestored = false;
            exceptionsApplied.Clear();
            exceptionsSentTo.Clear();
            exceptionsServer = null;
        }
    }
}
