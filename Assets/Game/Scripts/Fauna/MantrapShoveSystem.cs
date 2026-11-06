using Punchy.Player;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-83 (#62): a Mantrap's bite on the local player (a hop, and a push away from the stalk over 0.25 s), applied in
    /// the player's simulation group between the input and the movement, in a job. Mantrap asks for it from its Update
    /// (<see cref="Request"/>) instead of writing the player's transform and motion itself, which waited on whatever
    /// jobs still had them and ran outside any written-down order.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    [UpdateBefore(typeof(PlayerMovementSystem))]
    public partial class MantrapShoveSystem : SystemBase
    {
        /// <summary>How long a shove pushes (s), and how fast (m/s).</summary>
        const float Duration = 0.25f, Speed = 6f;
        /// <summary>The hop a shove starts with (m/s up).</summary>
        const float LiftSpeed = 4.5f;

        Entity player;
        float3 dir;
        float left;
        bool lift;

        /// <summary>Starts a shove of the player's entity in that world, away along dir (flat, normalized).</summary>
        public static void Request(World world, Entity player, float3 dir)
        {
            if (world == null || !world.IsCreated)
                return;
            var system = world.GetExistingSystemManaged<MantrapShoveSystem>();
            if (system == null)
                return;
            system.player = player;
            system.dir = dir;
            system.left = Duration;
            system.lift = true;
        }

        protected override void OnUpdate()
        {
            if (left <= 0f)
                return;
            // (Frame time as Mantrap's Update had it, capped at 0.1 s.)
            var step = math.min(math.min(UnityEngine.Time.deltaTime, 0.1f), left);
            left -= step;
            Dependency = new ShoveJob
            {
                Player = player,
                Move = dir * (step * Speed),
                Lift = lift,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Motions = SystemAPI.GetComponentLookup<PlayerMotion>(),
            }.Schedule(Dependency);
            lift = false;
        }

        [BurstCompile]
        struct ShoveJob : IJob
        {
            public Entity Player;
            public float3 Move;
            public bool Lift;
            public ComponentLookup<LocalTransform> Transforms;
            public ComponentLookup<PlayerMotion> Motions;

            public void Execute()
            {
                if (!Transforms.HasComponent(Player))
                    return;
                if (Lift && Motions.HasComponent(Player))
                {
                    var m = Motions[Player];
                    m.VerticalVelocity = LiftSpeed;
                    m.Grounded = false;
                    Motions[Player] = m;
                }
                var t = Transforms[Player];
                t.Position += Move;
                Transforms[Player] = t;
            }
        }
    }
}
