using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    public partial class FaunaDirector
    {
        // ---- A-106 row 3: kin deaths on a joined client ----
        //
        // The host marks a housed folk who dies (HouseMemberDied, Household.Lost), so its family speaks of it as lost
        // (FolkConversation's "gone" lines). A joined client has no brain and no death to see, only ghosts, and nothing
        // about deaths is sent. But the host lets a folk go only when it is farther than DespawnDistance from every
        // player (the client's included) or when it has died, so a housed folk's ghost that vanishes well inside that
        // distance of the client's own player died. The client marks it lost then. Guards against the other ways a
        // ghost drops: the client's player jumping (a teleport or respawn moves where the host thinks it is, and with it
        // what is near it), the connection going or not yet in game, and many vanishing at once (a reset, not deaths).
        // A lost folk's ghost that comes back clears its mark. Deaths this client didn't see (away from it, or before
        // it joined) stay unknown to it: the host's list of the dead belongs with the saved world state (P-35).

        /// <summary>How near the client's player a vanished ghost counts as died (m): well inside DespawnDistance, as
        /// the host sees the player a little late.</summary>
        const float KinLostReach = 150f;
        /// <summary>A jump of the client's player this far in one look (m) is a teleport or respawn: hold off this long
        /// (s) while the host catches up on where it is.</summary>
        const float KinLostJump = 20f, KinLostSettle = 3f;
        /// <summary>More housed ghosts than this vanishing in one look is a reset, not deaths.</summary>
        const int KinLostMaxAtOnce = 3;
        /// <summary>A-111 row 1: seconds between looks over the ghosts, unless the set of ghosts changed first. A kin
        /// death still reaches its family well within a second.</summary>
        const float KinLookEvery = 0.25f;

        /// <summary>Tests ("-households" logs too): housed folk this client has marked lost, and marks cleared again.</summary>
        public static int ClientKinLost, ClientKinBack;

        /// <summary>Tests ("-kincmd" mark): marks a housed folk lost as a vanished ghost would, so the clearing of a mark
        /// whose ghost is still (or again) here can be seen. False when it isn't housed or is already lost.</summary>
        public bool MarkKinLostForTest(uint individual)
        {
            if (simWorld != null || !Households.Of(individual, out var house, out var m) || house.Gone(m))
                return false;
            house.Lost |= 1u << m;
            kinMarked.Add(individual);
            ClientKinLost++;
            return true;
        }

        /// <summary>Takes back every lost mark this client set, when it stops being a joined client without the households
        /// being rebuilt (a teardown, as when a lost connection falls back to solo, drops them with the houses).</summary>
        void ClearKinMarks()
        {
            if (kinMarked.Count == 0)
                return;
            foreach (var individual in kinMarked)
                if (Households.Of(individual, out var house, out var m))
                    house.Lost &= ~(1u << m);
            if (LogHouses)
                Debug.Log($"[households] client: {kinMarked.Count} lost marks taken back (no longer a joined client)");
            kinMarked.Clear();
        }

        World kinWorld;
        EntityQuery kinReplicaQuery, kinConnectionQuery;
        // (A-111 row 1: two dictionaries kept and swapped each look, not copied; nothing allocated once they've grown.)
        Dictionary<Entity, (uint Individual, float3 Position)> kinSeen = new Dictionary<Entity, (uint, float3)>();
        Dictionary<Entity, (uint Individual, float3 Position)> kinNow = new Dictionary<Entity, (uint, float3)>();
        readonly HashSet<uint> kinMarked = new HashSet<uint>();
        float3 kinPlayer;
        float kinHoldUntil, kinNextLook;
        int kinOrderVersion = -1;
        /// <summary>Tests ("-households" logs them): looks over the ghosts, frames watched, and managed bytes the watch
        /// allocated, since the last log line.</summary>
        int kinLooks, kinFrames;
        long kinBytes;
        float kinLogAt;

        /// <summary>
        /// A joined client (no simulation world here) a few times a second, or as soon as the ghosts come or go: marks
        /// housed folk whose ghosts vanished near its player as lost in their household, and clears the mark of one whose
        /// ghost is back.
        /// </summary>
        void WatchKinLost(in TerrainSource source)
        {
            if (!LogHouses)
            {
                WatchKinLostNow(source);
                return;
            }
            // (-households: what the watch costs in looks and managed allocations, every 10 s. No timing: rule 21.)
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            WatchKinLostNow(source);
            kinBytes += System.GC.GetAllocatedBytesForCurrentThread() - before;
            kinFrames++;
            if (Time.time >= kinLogAt)
            {
                if (simWorld == null && kinFrames > 0)
                    Debug.Log($"[households] client kin watch: {kinLooks} looks in {kinFrames} frames, {kinBytes} bytes allocated, {kinSeen.Count} housed ghosts");
                kinLooks = kinFrames = 0;
                kinBytes = 0;
                kinLogAt = Time.time + 10f;
            }
        }

        void WatchKinLostNow(in TerrainSource source)
        {
            if (simWorld != null || queryWorld == null || !queryWorld.IsCreated)
            {
                // No longer a joined client (fallen back to solo, or hosting): the marks were this client's guesses, and
                // folk simulated here keep their own record of the dead.
                ClearKinMarks();
                return;
            }
            var em = queryWorld.EntityManager;
            if (kinWorld != queryWorld)
            {
                kinWorld = queryWorld;
                kinReplicaQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.CreatureReplica>(), ComponentType.ReadOnly<CreatureState>());
                kinConnectionQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                kinSeen.Clear();
                ClearKinMarks();
                kinHoldUntil = Time.time + KinLostSettle;
                kinOrderVersion = -1;
            }
            // (Not connected and in game, or no player of our own: nothing can be told; start over once we are.)
            if (kinConnectionQuery.IsEmpty || playerQuery.CalculateEntityCount() != 1)
            {
                kinSeen.Clear();
                kinHoldUntil = Time.time + KinLostSettle;
                return;
            }
            EnsureHouses(source);
            var player = playerQuery.GetSingleton<LocalTransform>().Position;
            if (math.distance(player.xz, kinPlayer.xz) > KinLostJump)
                kinHoldUntil = Time.time + KinLostSettle;
            kinPlayer = player;

            // (A-111 row 1: look again only when due, or when ghosts came or went since the last look.)
            var order = kinReplicaQuery.GetCombinedComponentOrderVersion(false);
            if (Time.time < kinNextLook && order == kinOrderVersion)
                return;
            kinNextLook = Time.time + KinLookEvery;
            kinOrderVersion = order;
            kinLooks++;

            kinNow.Clear();
            var entities = kinReplicaQuery.ToEntityArray(Allocator.Temp);
            var states = kinReplicaQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                if (s.Individual == 0 || !Households.Of(s.Individual, out var house, out var m))
                    continue;
                kinNow[entities[i]] = (s.Individual, s.Position);
                if (kinMarked.Remove(s.Individual))
                {
                    // Back after all: whatever took it from view, it lives.
                    house.Lost &= ~(1u << m);
                    ClientKinBack++;
                    if (LogHouses)
                        Debug.Log($"[households] client: back {FolkNames.Of(s.Individual)} {house.Members[m].Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) landmark {house.Landmark}: not lost");
                }
            }
            entities.Dispose();
            states.Dispose();

            if (Time.time >= kinHoldUntil)
            {
                var vanished = 0;
                foreach (var seen in kinSeen)
                    if (!kinNow.ContainsKey(seen.Key))
                        vanished++;
                if (vanished > 0 && vanished <= KinLostMaxAtOnce)
                    foreach (var seen in kinSeen)
                    {
                        if (kinNow.ContainsKey(seen.Key) || math.distance(seen.Value.Position.xz, player.xz) > KinLostReach)
                            continue;
                        if (!Households.Of(seen.Value.Individual, out var house, out var m) || house.Gone(m))
                            continue;
                        house.Lost |= 1u << m;
                        kinMarked.Add(seen.Value.Individual);
                        ClientKinLost++;
                        if (LogHouses)
                            Debug.Log($"[households] client: died {FolkNames.Of(seen.Value.Individual)} {house.Members[m].Role} of {house.Kind} at ({house.Door.x:F1}, {house.Door.z:F1}) landmark {house.Landmark}: " +
                                      $"ghost gone {math.distance(seen.Value.Position.xz, player.xz):F0} m from us");
                    }
            }
            (kinSeen, kinNow) = (kinNow, kinSeen);
        }
    }
}
