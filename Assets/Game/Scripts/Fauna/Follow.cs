using Punchy.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The follow hook for "lead" quests (A-71 row 6, player lane P-17): a creature or one of the folk follows a given
    /// player, a few metres behind, waiting when they stop, until it's stopped, they get too far ahead (GiveUp m) or its
    /// time runs out. Started and stopped from outside the brain, on the host (or solo): it sets CreatureMind's
    /// FollowHolder and FollowTime in the authoritative world, and CreatureBrainSystem.Following does the rest. What
    /// others see is the creature walking along (CreatureState is synced; the follow itself is host-only).
    /// </summary>
    public static class Follow
    {
        /// <summary>How long a follow lasts unless stopped (s); how far ahead the player may get before it gives up (m);
        /// the gap it keeps behind them (m).</summary>
        public const float DefaultSeconds = 600f, GiveUp = 40f, Near = 2f, Far = 4f;

        /// <summary>The world the brain runs in: the server world when hosting, the local one solo (null on a joined client).</summary>
        static World Sim => NetWorlds.Server;

        /// <summary>
        /// Starts entity following the player with this network id (solo: 0), for seconds (DefaultSeconds if 0 or less).
        /// False on a joined client, or if the entity isn't a creature of the authoritative world.
        /// </summary>
        public static bool Start(Entity entity, int playerNetworkId, float seconds = 0f)
        {
            var world = Sim;
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity) ||
                !world.EntityManager.HasComponent<CreatureMind>(entity))
                return false;
            var em = world.EntityManager;
            var mind = em.GetComponentData<CreatureMind>(entity);
            mind.FollowHolder = Interactions.HolderId(playerNetworkId);
            mind.FollowTime = seconds > 0f ? seconds : DefaultSeconds;
            mind.FollowDetourTime = 0f;
            em.SetComponentData(entity, mind);
            return true;
        }

        /// <summary>Releases it: it goes back to its own life from where it is.</summary>
        public static void Stop(Entity entity)
        {
            var world = Sim;
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity) ||
                !world.EntityManager.HasComponent<CreatureMind>(entity))
                return;
            var em = world.EntityManager;
            var mind = em.GetComponentData<CreatureMind>(entity);
            mind.FollowHolder = 0;
            mind.FollowTime = 0f;
            mind.FollowDetourTime = 0f;
            em.SetComponentData(entity, mind);
        }

        /// <summary>Whether it's following someone now (it may have given up by itself), and whom (their network id, else -1).</summary>
        public static bool IsFollowing(Entity entity) => Following(entity) >= 0;

        public static int Following(Entity entity)
        {
            var world = Sim;
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity) ||
                !world.EntityManager.HasComponent<CreatureMind>(entity))
                return -1;
            var holder = world.EntityManager.GetComponentData<CreatureMind>(entity).FollowHolder;
            return holder > 0 ? holder - 1 : -1;
        }

        /// <summary>
        /// A candidate to follow: the nearest creature of this species (its asset name) within radius of a place, awake
        /// and free (not a player's body, gone, held, asleep or indoors); Entity.Null if there's none.
        /// </summary>
        public static Entity Find(string species, float3 at, float radius)
        {
            var world = Sim;
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex(species) : -1;
            if (world == null || !world.IsCreated || index < 0)
                return Entity.Null;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(),
                ComponentType.ReadOnly<CreatureBody>(), ComponentType.ReadOnly<CreatureMind>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var best = Entity.Null;
            var bestDistance = radius * radius;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (bodies[i].Species != index || s.Gone || s.Mode == CreatureMode.Puppet || s.Mode == CreatureMode.Held ||
                    s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Home && s.Stage >= 2)
                    continue;
                var d = math.distancesq(s.Position, at);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = entities[i];
                }
            }
            return best;
        }
    }
}
