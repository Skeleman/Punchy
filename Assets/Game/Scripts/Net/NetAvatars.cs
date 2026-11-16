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
    /// has (FaunaDirector's Player Species), and the menu's Multiplayer tab can switch it. It also owns the local player's
    /// eyewear (A-103 row 5): G puts the pair on or takes it off (glasses over bat eyes, glow goggles over any other),
    /// it's drawn on every body that wears it, and LocalVision is how the local view takes the light (BatVision).
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

        /// <summary>The colouring the local player picked on the Character tab (-1: the one their individual draws).</summary>
        public int LocalCoat = -1;

        /// <summary>
        /// A-124: the build the local player picked on the Character tab (a BodyBuilds.Player code), for their current
        /// body (each body remembers its own); -1: the one their individual rolls.
        /// </summary>
        public int LocalBuild = -1;
        CreatureSpecies buildSpecies;

        /// <summary>A-124: the local player's build as sent (BodyBuilds.Player): the one picked, else their individual's.</summary>
        public byte LocalBuildCode => LocalBuild >= 0 ? (byte)LocalBuild : BodyBuilds.PlayerDefault(individual);

        /// <summary>The Character tab key a body's picked build is kept under.</summary>
        public static string BuildKey(CreatureSpecies s) => "Punchy.Build." + (s != null ? s.name : "");

        /// <summary>A-124: picks (and keeps) the local player's build for their current body; -1 goes back to their own.</summary>
        public void SetLocalBuild(int code)
        {
            LocalBuild = code < 0 ? -1 : code & 255;
            if (Fauna == null || Fauna.PlayerSpecies == null)
                return;
            PlayerPrefs.SetInt(BuildKey(Fauna.PlayerSpecies), LocalBuild);
            PlayerPrefs.Save();
        }

        /// <summary>Hands the local build to the body FaunaDirector keeps (each body's own pick, reloaded on a switch).</summary>
        void SyncLocalBuild()
        {
            if (Fauna == null)
                return;
            if (Fauna.PlayerSpecies != buildSpecies)
            {
                buildSpecies = Fauna.PlayerSpecies;
                LocalBuild = buildSpecies != null ? PlayerPrefs.GetInt(BuildKey(buildSpecies), -1) : -1;
            }
            var code = individual != 0 ? LocalBuildCode : -1;
            if (code != Fauna.PlayerBuild && code >= 0 && buildSpecies != null)
                Debug.Log($"[look] own {buildSpecies.name} build {BodyBuilds.PlayerBuild((byte)code)} height step {BodyBuilds.PlayerHeightStep((byte)code)} gait {GaitStyle.Of(individual, buildSpecies.Upright, buildSpecies.Child).Kind}");
            Fauna.PlayerBuild = code;
        }

        float BodyVision => Fauna != null && Fauna.PlayerSpecies != null ? Fauna.PlayerSpecies.Vision : 0f;

        /// <summary>How the local view takes the light (0 normal, 1 a bat's): the body's own, replaced by what's worn.</summary>
        public static float LocalVision { get; private set; }

        void OnEnable() => Punchy.UI.MainMenu.AddTab("Character", 5, nav => CharacterTab.Draw(this, nav));

        /// <summary>Switches the local player's body (FaunaDirector rebuilds it).</summary>
        public void SetLocalSpecies(int index)
        {
            if (Fauna != null && index >= 0 && index < PlayableSpecies.Count)
                Fauna.PlayerSpecies = PlayableSpecies[index];
        }

        void Start()
        {
            // (Again here: on a scene load the outgoing instance's OnDisable can remove the tab after this one's OnEnable.)
            OnEnable();
            // This machine's player keeps one look (posture, gait, hair, coat) across sessions, distinct from others'.
            individual = (uint)PlayerPrefs.GetInt("Punchy.PlayerIndividual", 0);
            if (individual == 0)
            {
                individual = (uint)System.Guid.NewGuid().GetHashCode() | 1u;
                PlayerPrefs.SetInt("Punchy.PlayerIndividual", (int)individual);
            }
            // (In the editor the test flags also come from Temp/PlayArgs.txt.)
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            if (System.Array.IndexOf(args, "-newlook") >= 0)
                individual = (uint)System.Guid.NewGuid().GetHashCode() | 1u;
            // "-body Wolf" on the command line picks the starting body (handy for testing with builds).
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-body")
                    SetLocalSpecies(PlayableSpecies.FindIndex(s => s != null && s.name == args[i + 1]));
            SyncLocalBuild();
            // (How that sees: the view's exposure, bloom and haze, made at runtime beside this.)
            if (GetComponent<Punchy.CameraRig.BatVision>() == null)
                gameObject.AddComponent<Punchy.CameraRig.BatVision>();
        }

        void OnDisable()
        {
            Punchy.UI.MainMenu.RemoveTab("Character");
            ReleaseWorld();
        }

        /// <summary>Drops the puppets and blobs made for the current world (on disable, or when the client world changes).</summary>
        void ReleaseWorld()
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
            SyncLocalBuild();
            // (Eyewear is a head item since P-38 row 3: Items' gear puts it on, syncs it and draws it.)
            LocalVision = Eyewear.Vision(BodyVision, Punchy.Items.Items.OwnHead);
            // The client world, or the local world when playing alone (our own look applies there too).
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated)
                return;
            if (world != client)
            {
                // (Not OnDisable: that also removes the Character tab, which is why it never showed in A-46.)
                ReleaseWorld();
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
                em.CreateSingleton(new LocalPlayerSpecies { Index = local, Build = LocalBuildCode }, "LocalPlayerSpecies");
            else
                speciesQuery.SetSingleton(new LocalPlayerSpecies { Index = local, Build = LocalBuildCode });

            // Our own body wears our look (FaunaDirector respawns it whenever it rebuilds).
            if (individual != 0 && avatarQuery.TryGetSingletonEntity<PlayerAvatar>(out var avatar))
            {
                var state = em.GetComponentData<CreatureState>(avatar);
                var body = em.GetComponentData<CreatureBody>(avatar);
                ref var blob = ref body.Blob.Value;
                // The colouring picked on the Character tab, else the one this individual draws.
                var coat = blob.Coats.Length == 0 ? body.Coat
                    : LocalCoat >= 0 ? math.min(LocalCoat, blob.Coats.Length - 1)
                    : blob.PickCoat(((individual * 2654435761u) >> 8) / 16777216f);
                if (state.Individual != individual || body.Coat != coat)
                {
                    state.Individual = individual;
                    em.SetComponentData(avatar, state);
                    body.Coat = coat;
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
                var same = has && remote.Look.Species == look.Species && remote.Look.Coat == look.Coat && remote.Look.Individual == look.Individual &&
                           remote.Look.Build == look.Build;
                if (wants && same)
                    // (What they wear, eyewear too, is Items' gear: P-38 row 3.)
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
            // (A-124: in the build its owner picked, one blob per body and build.)
            var kind = PlayableSpecies[look.Species];
            var build = BodyBuilds.PlayerBuild(look.Build);
            var key = look.Species * BodyBuilds.Count + build;
            if (!blobs.TryGetValue(key, out var blob))
                blobs[key] = blob = CreatureBlobBuilder.Build(kind, build: BodyBuilds.Of(kind.BuildRange, build));
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
                Scale = BodyBuilds.PlayerScale(b.Size, look.Build),   // (the height its owner picked, as on their screen)
                Mode = CreatureMode.Puppet,
                Seed = look.Individual | 1u,
                Individual = look.Individual
            });
            em.SetComponentData(e, new RemoteAvatar { Ghost = ghost, Species = Fauna != null ? Fauna.SpeciesIndex(PlayableSpecies[look.Species]) : -1 });
            // (One line per puppet made: host and joined client logs show the same build for the same player.)
            Debug.Log($"[look] remote player {kind.name} build {build} height step {BodyBuilds.PlayerHeightStep(look.Build)} scale {BodyBuilds.PlayerScale(b.Size, look.Build):F3} gait {GaitStyle.Of(ref b, look.Individual).Kind}");
            return e;
        }
    }
}
