using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// Gives other players their bodies. Playable Species is the network's table of player bodies: a player's species
    /// travels as its index here, so every machine must have the same list in the same order (append, never reorder).
    /// For each remote NetPlayer this spawns a creature puppet of their species, coat and individual, which
    /// PlayerAvatarSystem drives from the interpolated ghost; it also tells the network which body the local player
    /// has (FaunaDirector's Player Species), and F2's panel can switch it.
    /// </summary>
    public class NetAvatars : MonoBehaviour
    {
        public FaunaDirector Fauna;
        [Tooltip("Bodies players can have, in network order. Append new ones at the end; never reorder or remove.")]
        public List<CreatureSpecies> PlayableSpecies = new List<CreatureSpecies>();

        struct Remote
        {
            public Entity Puppet;
            public NetPlayerLook Look;
        }

        readonly Dictionary<int, BlobAssetReference<CreatureBlob>> blobs = new Dictionary<int, BlobAssetReference<CreatureBlob>>();
        readonly Dictionary<Entity, Remote> remotes = new Dictionary<Entity, Remote>();
        readonly List<Entity> gone = new List<Entity>();
        World world;
        EntityQuery ghostQuery, speciesQuery, avatarQuery;
        bool warned;
        uint individual;

        /// <summary>The local player's index in the table (-1 when their body isn't in it).</summary>
        public int LocalIndex => Fauna != null && Fauna.PlayerSpecies != null ? PlayableSpecies.IndexOf(Fauna.PlayerSpecies) : -1;

        /// <summary>Switches the local player's body (FaunaDirector rebuilds it).</summary>
        public void SetLocalSpecies(int index)
        {
            if (Fauna != null && index >= 0 && index < PlayableSpecies.Count)
                Fauna.PlayerSpecies = PlayableSpecies[index];
        }

        void Start()
        {
            // This machine's player keeps one look (posture, gait, hair, coat) across sessions, distinct from others'.
            individual = (uint)PlayerPrefs.GetInt("Punchy.PlayerIndividual", 0);
            if (individual == 0)
            {
                individual = (uint)System.Guid.NewGuid().GetHashCode() | 1u;
                PlayerPrefs.SetInt("Punchy.PlayerIndividual", (int)individual);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-newlook") >= 0)
                individual = (uint)System.Guid.NewGuid().GetHashCode() | 1u;
            // "-body Wolf" on the command line picks the starting body (handy for testing with builds).
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-body")
                    SetLocalSpecies(PlayableSpecies.FindIndex(s => s != null && s.name == args[i + 1]));
        }

        void OnDisable()
        {
            if (world != null && world.IsCreated)
                foreach (var r in remotes.Values)
                    if (world.EntityManager.Exists(r.Puppet))
                        world.EntityManager.DestroyEntity(r.Puppet);
            remotes.Clear();
            foreach (var b in blobs.Values)
                if (b.IsCreated) b.Dispose();
            blobs.Clear();
            world = null;
        }

        void Update()
        {
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated || !client.IsClient())
                return;
            if (world != client)
            {
                OnDisable();
                world = client;
                var em0 = world.EntityManager;
                ghostQuery = em0.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<NetPlayerLook>(), ComponentType.ReadOnly<LocalTransform>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                });
                speciesQuery = em0.CreateEntityQuery(ComponentType.ReadWrite<LocalPlayerSpecies>());
                avatarQuery = em0.CreateEntityQuery(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>(), ComponentType.ReadOnly<PlayerAvatar>());
            }
            var em = world.EntityManager;

            // Tell the network which body we have.
            var local = LocalIndex;
            if (local < 0 && Fauna != null && Fauna.PlayerSpecies != null && !warned)
            {
                warned = true;
                Debug.LogWarning($"NetAvatars: the player's species '{Fauna.PlayerSpecies.name}' isn't in Playable Species, so other players see a capsule.");
            }
            if (speciesQuery.IsEmpty)
                em.CreateSingleton(new LocalPlayerSpecies { Index = local }, "LocalPlayerSpecies");
            else
                speciesQuery.SetSingleton(new LocalPlayerSpecies { Index = local });

            // Our own body wears our look (FaunaDirector respawns it whenever it rebuilds).
            if (individual != 0 && avatarQuery.TryGetSingletonEntity<PlayerAvatar>(out var avatar))
            {
                var state = em.GetComponentData<CreatureState>(avatar);
                if (state.Individual != individual)
                {
                    state.Individual = individual;
                    em.SetComponentData(avatar, state);
                    var body = em.GetComponentData<CreatureBody>(avatar);
                    ref var blob = ref body.Blob.Value;
                    if (blob.Coats.Length > 0)
                        body.Coat = blob.PickCoat(((individual * 2654435761u) >> 8) / 16777216f);
                    em.SetComponentData(avatar, body);
                }
            }

            // A body for every remote player who has one; gone when they go (or change species).
            gone.Clear();
            foreach (var ghost in remotes.Keys)
                if (!em.Exists(ghost))
                    gone.Add(ghost);
            var ghosts = ghostQuery.ToEntityArray(Allocator.Temp);
            foreach (var ghost in ghosts)
            {
                var look = em.GetComponentData<NetPlayerLook>(ghost);
                var isLocal = em.HasComponent<GhostOwnerIsLocal>(ghost) && em.IsComponentEnabled<GhostOwnerIsLocal>(ghost);
                var position = em.GetComponentData<LocalTransform>(ghost).Position;
                var wants = !isLocal && look.Species >= 0 && look.Species < PlayableSpecies.Count && PlayableSpecies[look.Species] != null &&
                            math.lengthsq(position) > 1e-6f;
                remotes.TryGetValue(ghost, out var remote);
                var has = remote.Puppet != Entity.Null && em.Exists(remote.Puppet);
                var same = has && remote.Look.Species == look.Species && remote.Look.Coat == look.Coat && remote.Look.Individual == look.Individual;
                if (wants && same)
                    continue;
                if (has)
                    em.DestroyEntity(remote.Puppet);
                if (wants)
                    remotes[ghost] = new Remote { Puppet = Spawn(em, ghost, look, position), Look = look };
                else
                    remotes.Remove(ghost);
            }
            ghosts.Dispose();
            foreach (var ghost in gone)
            {
                if (remotes.TryGetValue(ghost, out var r) && em.Exists(r.Puppet))
                    em.DestroyEntity(r.Puppet);
                remotes.Remove(ghost);
            }
        }

        Entity Spawn(EntityManager em, Entity ghost, NetPlayerLook look, float3 position)
        {
            if (!blobs.TryGetValue(look.Species, out var blob))
                blobs[look.Species] = blob = CreatureBlobBuilder.Build(PlayableSpecies[look.Species]);
            ref var b = ref blob.Value;
            var e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody), typeof(RemoteAvatar));
            em.SetName(e, "RemotePlayerBody");
            em.SetComponentData(e, new CreatureBody
            {
                Blob = blob,
                Species = -1,
                SegmentCount = b.Segments.Length,
                Coat = math.clamp(look.Coat, 0, math.max(0, b.Coats.Length - 1))
            });
            em.SetComponentData(e, new CreatureState
            {
                Position = position,
                Scale = 1f,
                Mode = CreatureMode.Puppet,
                Seed = look.Individual | 1u,
                Individual = look.Individual
            });
            em.SetComponentData(e, new RemoteAvatar { Ghost = ghost });
            return e;
        }
    }
}
