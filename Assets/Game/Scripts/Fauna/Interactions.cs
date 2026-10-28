using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Punchy.Fauna
{
    // Interactions: petting, picking up and putting down
    // ---------------------------------------------------
    // The player's machine chooses (InteractionInputSystem): the animal in front of the player, and what to do with it.
    // It writes an InteractionRequest, numbered so each is acted on once. Where the animals are simulated (the one
    // world of a game without netcode, or the server) InteractionApplySystem acts on it: alone, straight from the
    // request; networked, from the request riding on the player's NetPlayerCommand (the target named by ghost id).
    // The animal then reacts by its nature: a friendly one enjoys it, a domestic one enjoys it or puts up with it (or
    // walks off), a wild one bolts. A carried animal follows its holder's hands (on the holder's own machine, placed
    // at the hands directly, so it doesn't lag behind its interpolated ghost) and is never despawned.

    /// <summary>What a player does to an animal. Startle: a lunge at it (the player's side of hunting, harmless until
    /// there's a health system): it bolts.</summary>
    public enum InteractAct : byte { None, Pet, PickUp, PutDown, Startle }

    /// <summary>
    /// The local player's latest interaction (on the player's machine: alone, or a client), and the pose their body
    /// takes for it.
    /// </summary>
    public struct InteractionRequest : IComponentData
    {
        public InteractAct Act;
        /// <summary>Bumped for each new request, so it is acted on once.</summary>
        public byte Serial;
        /// <summary>The animal: the entity (alone) or its ghost id (networked).</summary>
        public Entity Target;
        public int TargetGhost;
        /// <summary>The player's body pose (IdleAction.Pet, Carry, Throw, an emote, or None).</summary>
        public IdleAction Pose;
    }

    public static class Interactions
    {
        /// <summary>How far in front of the player an animal can be reached.</summary>
        public const float Reach = 1.6f;
        /// <summary>How far a lunge (Attack) reaches.</summary>
        public const float LungeReach = 3.5f;
        /// <summary>How long a pet lasts, at most (the animal may leave sooner).</summary>
        public const float PetTime = 6f;

        /// <summary>Who someone is, as an animal's Holder: the connection's NetworkId + 1 (alone: 1). 0 is nobody.</summary>
        public static int HolderId(int networkId) => networkId + 1;

        /// <summary>Whether people can get near it and touch it at all (not a person, not up a tree, in the air, in water…).</summary>
        public static bool CanPet(ref CreatureBlob b, in CreatureState s) =>
            !b.Upright && b.Habitat != Habitat.Swims && s.Fly < 0.5f && !s.Gone &&
            s.Mode != CreatureMode.Climb && s.Mode != CreatureMode.Web && s.Mode != CreatureMode.Burrow &&
            s.Mode != CreatureMode.Roost && s.Mode != CreatureMode.Puppet && s.Mode != CreatureMode.Held;

        /// <summary>Small enough to pick up (cats, chickens, rabbits, songbirds…).</summary>
        public static bool CanCarry(ref CreatureBlob b, in CreatureState s) =>
            CanPet(ref b, s) && b.Habitat != Habitat.Floats && b.BodyLength * s.Scale <= 0.5f;

        /// <summary>Used to people: friendly animals and settlements' animals (not their folk: those are upright).</summary>
        public static bool Tame(ref CreatureBlob b, in CreatureState s) => b.Friendly || s.Leash > 0f;

        /// <summary>Where a carried animal is: in the holder's arms, low against their chest (held sideways, so its
        /// shape shows).</summary>
        public static float3 Hands(float3 holder, float yaw) =>
            holder + new float3(math.sin(yaw), 0f, math.cos(yaw)) * 0.38f + new float3(0f, 0.98f, 0f);

        /// <summary>
        /// Acts on a request where the animal is simulated. Wild animals bolt from a hand reaching for them; tame ones
        /// let themselves be petted or picked up (a few would rather walk off).
        /// </summary>
        public static void Apply(ref CreatureState s, ref CreatureBlob b, in TerrainSource terrain, InteractAct act, int holder,
                                 float3 player, float yaw)
        {
            var rng = new Random(s.Seed | 1u);
            switch (act)
            {
                case InteractAct.Pet:
                case InteractAct.PickUp:
                    var carry = act == InteractAct.PickUp;
                    if (carry ? !CanCarry(ref b, s) : !CanPet(ref b, s))
                        break;
                    if (!Tame(ref b, s))
                    {
                        Bolt(ref s, ref b, terrain, player, ref rng);
                        break;
                    }
                    // A domestic animal that isn't in the mood walks off (a friendly one always is).
                    if (!b.Friendly && rng.NextFloat() < (carry ? 0.25f : 0.2f))
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        var away = math.normalizesafe(s.Position.xz - player.xz, new float2(1f, 0f));
                        s.Target = s.Position.xz + away * rng.NextFloat(3f, 6f) + rng.NextFloat2Direction();
                        break;
                    }
                    if (carry)
                    {
                        SetMode(ref s, CreatureMode.Held);
                        // Friendly ones settle in; others put up with it a while, then squirm (see the brain).
                        s.Timer = b.Friendly ? 1e6f : rng.NextFloat(5f, 14f);
                    }
                    else
                    {
                        SetMode(ref s, CreatureMode.Petted);
                        // Friendly ones love it; others enjoy it, or put up with it.
                        s.Stage = (byte)(b.Friendly || rng.NextFloat() < 0.6f ? 0 : 1);
                        s.Timer = rng.NextFloat(PetTime * 0.6f, PetTime);
                    }
                    s.Holder = holder;
                    s.Focus = player.xz;
                    s.Speed = 0f;
                    break;

                case InteractAct.Startle:
                    if (s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Puppet || s.Gone || b.Upright)
                        break;
                    Bolt(ref s, ref b, terrain, player, ref rng);
                    break;

                case InteractAct.PutDown:
                    if (s.Mode != CreatureMode.Held || s.Holder != holder)
                        break;
                    var forward = new float3(math.sin(yaw), 0f, math.cos(yaw));
                    var at = player + forward * 0.6f;
                    PutDown(ref s, terrain, at, ref rng);
                    s.Yaw = yaw;
                    break;
            }
            s.Seed = rng.state;
        }

        /// <summary>Back on its feet (on whatever floor is there), looking about before it does anything else.</summary>
        public static void PutDown(ref CreatureState s, in TerrainSource terrain, float3 at, ref Random rng)
        {
            var floor = terrain.FloorUnder(at + new float3(0f, 0.6f, 0f), out _);
            s.Position = new float3(at.x, floor, at.z);
            SetMode(ref s, CreatureMode.Idle);
            s.Timer = rng.NextFloat(1f, 3f);
            s.Holder = 0;
            s.Speed = 0f;
        }

        /// <summary>Runs from the person who reached for it (a bird takes off, to come down further off).</summary>
        public static void Bolt(ref CreatureState s, ref CreatureBlob b, in TerrainSource terrain, float3 from, ref Random rng)
        {
            SetMode(ref s, CreatureMode.Flee);
            var away = math.normalizesafe(s.Position.xz - from.xz, rng.NextFloat2Direction());
            s.Target = s.Position.xz + away * (b.Habitat == Habitat.Flies ? 35f : 25f) + rng.NextFloat2Direction() * 5f;
            if (b.Habitat == Habitat.Flies)
            {
                s.Stage = 1;
                s.TargetHeight = terrain.SampleHeight(s.Target);
            }
            s.Focus = from.xz;
            s.Holder = 0;
        }

        static void SetMode(ref CreatureState s, CreatureMode mode)
        {
            s.Mode = mode;
            s.ModeTime = 0f;
            s.Stage = 0;
            s.StageTime = 0f;
            s.Partner = Entity.Null;
            s.Action = IdleAction.None;
            s.ActionTime = 0f;
        }
    }

    /// <summary>
    /// Where the animals are simulated, acts on players' interaction requests: alone, the local request; on a server,
    /// each player's request as sent on their NetPlayerCommand (the animal named by its ghost id).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CreatureBrainSystem))]
    public partial struct InteractionApplySystem : ISystem
    {
        /// <summary>The last request acted on, per holder.</summary>
        NativeHashMap<int, byte> handled;
        ComponentLookup<CreatureState> states;
        ComponentLookup<CreatureBody> bodies;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            handled = new NativeHashMap<int, byte>(8, Allocator.Persistent);
            states = state.GetComponentLookup<CreatureState>();
            bodies = state.GetComponentLookup<CreatureBody>(true);
            state.RequireForUpdate<TerrainSource>();
            state.RequireForUpdate<CreatureState>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (handled.IsCreated)
                handled.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            states.Update(ref state);
            bodies.Update(ref state);

            // Alone: the local player's request.
            if (SystemAPI.TryGetSingleton<InteractionRequest>(out var request) && request.Act != InteractAct.None)
                foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
                    Handle(Interactions.HolderId(0), request.Act, request.Serial, request.Target, t.ValueRO, terrain);

            // Networked: each player's, from their command.
            var ghosts = default(NativeHashMap<int, Entity>);
            foreach (var (command, owner, t) in SystemAPI.Query<RefRO<NetPlayerCommand>, RefRO<GhostOwner>, RefRO<LocalTransform>>().WithAll<NetPlayer>())
            {
                var c = command.ValueRO;
                var holder = Interactions.HolderId(owner.ValueRO.NetworkId);
                if (c.Act == 0 || handled.TryGetValue(holder, out var last) && last == c.ActSerial)
                    continue;
                if (!ghosts.IsCreated)
                {
                    ghosts = new NativeHashMap<int, Entity>(256, Allocator.Temp);
                    foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<CreatureState>().WithEntityAccess())
                        ghosts[ghost.ValueRO.ghostId] = entity;
                }
                ghosts.TryGetValue(c.ActTarget, out var target);
                Handle(holder, (InteractAct)c.Act, c.ActSerial, target, t.ValueRO, terrain);
            }
        }

        void Handle(int holder, InteractAct act, byte serial, Entity target, in LocalTransform player, in TerrainSource terrain)
        {
            if (handled.TryGetValue(holder, out var last) && last == serial)
                return;
            handled[holder] = serial;
            if (!states.HasComponent(target) || !bodies.HasComponent(target))
                return;
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward.x, forward.z);
            var s = states[target];
            // Too far to have reached it (an old request, or it moved off): nothing happens.
            var reach = act == InteractAct.Startle ? Interactions.LungeReach : Interactions.Reach;
            if (act != InteractAct.PutDown && math.distance(s.Position.xz, player.Position.xz) > reach + 1.5f)
                return;
            Interactions.Apply(ref s, ref bodies[target].Blob.Value, terrain, act, holder, player.Position, yaw);
            states[target] = s;
        }
    }
}
