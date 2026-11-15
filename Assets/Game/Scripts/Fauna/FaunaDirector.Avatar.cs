using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 4: the player's body and the player ghosts, moved out of FaunaDirector.cs as they were.
    public partial class FaunaDirector
    {
        // ------------------------------------------------------------------------------------------- the player's body

        const string BodyKey = "Punchy.Character.Body";
        bool bodyRestored;

        /// <summary>
        /// #68: takes the body saved when it was last picked (and that body's remembered colouring, as the Character
        /// tab keeps it), if it is still playable; a "-body" launch flag wins.
        /// </summary>
        void RestoreBody()
        {
            var name = PlayerPrefs.GetString(BodyKey, "");
            if (name.Length == 0 || System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-body") >= 0)
                return;
            var avatars = UnityEngine.Object.FindFirstObjectByType<Punchy.Net.NetAvatars>();
            var pick = avatars != null ? avatars.PlayableSpecies.Find(s => s != null && s.name == name) : species.Find(s => s != null && s.name == name);
            if (pick == null)
                return;
            PlayerSpecies = pick;
            var coat = PlayerPrefs.GetInt("Punchy.Coat." + name, -1);
            if (avatars != null)
                avatars.LocalCoat = coat;
            Debug.Log($"[character] restored body {name}, colouring {coat}");
        }

        /// <summary>
        /// Spawns the player's body if it's missing (or the species changed). There's always one: with no species
        /// chosen, a human (the player entity itself has no mesh). The body has its own runtime data, so changing
        /// species never disturbs the animals.
        /// </summary>
        void KeepAvatar()
        {
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            // #68: the body (and its colouring) picked on the Character tab last session, unless "-body" asks for one.
            if (!bodyRestored)
            {
                bodyRestored = true;
                RestoreBody();
            }
            if (PlayerSpecies == null)
                PlayerSpecies = species.Find(s => s.Role == DirectorRole.Folk) ?? species.Find(s => s.Body.Upright);
            if (PlayerSpecies != avatarSpecies)
            {
                // A body changed mid-session (a Character tab pick) is the one the next session starts in.
                if (avatarSpecies != null && PlayerSpecies != null)
                {
                    PlayerPrefs.SetString(BodyKey, PlayerSpecies.name);
                    PlayerPrefs.Save();
                    Debug.Log($"[character] body {PlayerSpecies.name} saved for next time");
                }
                DropAvatar();
            }
            if (PlayerSpecies == null)
                return;
            if (avatarQuery.CalculateEntityCount() > 0)
                return;
            if (!avatarBlob.IsCreated)
                avatarBlob = CreatureBlobBuilder.Build(PlayerSpecies);
            avatarSpecies = PlayerSpecies;
            ref var b = ref avatarBlob.Value;
            var e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody), typeof(PlayerAvatar));
            em.SetName(e, "PlayerBody");
            em.SetComponentData(e, new CreatureBody
            {
                Blob = avatarBlob,
                Species = SpeciesIndex(PlayerSpecies),
                SegmentCount = b.Segments.Length,
                Coat = b.PickCoat(rng.NextFloat())
            });
            // (At the species' middle size: a cat body is cat-sized, an osprey osprey-sized.)
            var size = (b.Size.x + b.Size.y) * 0.5f;
            em.SetComponentData(e, new CreatureState
            {
                Position = player.Position,
                Phase = rng.NextFloat(),
                Scale = size,
                Mode = CreatureMode.Puppet,
                Seed = rng.NextUInt() | 1u,
                Individual = rng.NextUInt() | 1u
            });
            // What the body can do, for player movement (PlayerBodyAbilities, shared with a steered mount): its pace and
            // height next to a human's, flight, climbing and its jump.
            var human = RoleIndex(DirectorRole.Folk);
            if (human >= 0)
            {
                ref var hb = ref blobs[human].Value;
                PlayerBodyAbilities.HumanRun = hb.RunSpeed * (hb.Size.x + hb.Size.y) * 0.5f;
                PlayerBodyAbilities.HumanStand = hb.StandHeight * (hb.Size.x + hb.Size.y) * 0.5f;
            }
            else
                PlayerBodyAbilities.HumanRun = PlayerBodyAbilities.HumanStand = 0f;
            // (Its climbing skill: the species', or "-climbskill N".)
            var climbSkill = Climbing.SkillOverride >= 0 ? (byte)math.min(Climbing.SkillOverride, Climbing.Most) : b.ClimbSkill;
            var flySpeed = PlayerSpecies.PlayerFlySpeed > 0f ? PlayerSpecies.PlayerFlySpeed : PlayerSpecies.Body.PlayerFlySpeed;
            var jump = PlayerSpecies.PlayerJump > 0f ? PlayerSpecies.PlayerJump : PlayerSpecies.Body.PlayerJump;
            var abilities = PlayerBodyAbilities.From(ref b, size, flySpeed, jump, climbSkill);
            using var bodyQuery = em.CreateEntityQuery(typeof(Punchy.Player.PlayerBody));
            if (!bodyQuery.TryGetSingletonEntity<Punchy.Player.PlayerBody>(out var bodyEntity))
                bodyEntity = em.CreateEntity(typeof(Punchy.Player.PlayerBody));
            em.SetComponentData(bodyEntity, abilities);
        }

        void DropAvatar()
        {
            if (queryWorld != null && queryWorld.IsCreated)
                queryWorld.EntityManager.DestroyEntity(avatarQuery);
            // (The creature snapshot still holds this body's blob: dropped first, A-70 row 5.)
            CreatureSnapshotSystem.Forget();
            if (avatarBlob.IsCreated)
                avatarBlob.Dispose();
            avatarBlob = default;
            avatarSpecies = null;
        }

        // ------------------------------------------------------------------------------------------- ghosts

        /// <summary>Client: gives every newly arrived creature ghost the body of its species.</summary>
        void GiveReplicasBodies()
        {
            if (queryWorld == simWorld || newReplicaQuery.IsEmpty)
                return;
            var em = queryWorld.EntityManager;
            var arrived = newReplicaQuery.ToEntityArray(Allocator.Temp);
            var ghosts = new NativeList<Entity>(arrived.Length, Allocator.Temp);
            foreach (var e in arrived)
            {
                var net = em.GetComponentData<CreatureNet>(e);
                if (net.Species >= 0 && net.Species < blobs.Count)
                    ghosts.Add(e);
            }
            arrived.Dispose();
            // (All of them given their components in one go: one archetype move for the lot, not three each.)
            em.AddComponent(ghosts.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<CreatureBody>(), ComponentType.ReadWrite<CreatureState>(),
                                                                   ComponentType.ReadWrite<CreatureReplica>()));
            foreach (var e in ghosts)
            {
                var net = em.GetComponentData<CreatureNet>(e);
                var blob = blobs[net.Species];
                ref var b = ref blob.Value;
                var t = em.GetComponentData<LocalTransform>(e);
                var forward = math.mul(t.Rotation, new float3(0f, 0f, 1f));
                em.SetComponentData(e, new CreatureBody
                {
                    Blob = blob,
                    Species = net.Species,
                    SegmentCount = b.Segments.Length,
                    Coat = math.min(net.Coat, math.max(0, b.Coats.Length - 1))
                });
                em.SetComponentData(e, new CreatureState
                {
                    Position = t.Position,
                    Yaw = math.atan2(forward.x, forward.z),
                    Phase = rng.NextFloat(),
                    Scale = net.Scale > 0f ? net.Scale : 1f,
                    Mode = (CreatureMode)net.Mode,
                    Seed = net.Individual | 1u,
                    Individual = net.Individual
                });
            }
            ghosts.Dispose();
        }
    }
}
