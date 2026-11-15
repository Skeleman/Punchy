using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-120 row 1: a household's pet (FaunaDirector.SpawnHouseholdPets). Its house's door (Households.DoorKey) and its
    /// bed beside it. Host or solo only, never sent: what it makes the pet do goes out through the usual ghost fields.
    /// </summary>
    public struct HouseholdPet : IComponentData
    {
        public int2 Door;
        public float2 Bed;
        /// <summary>How far it strays from its bed by day (m).</summary>
        public float Leash;
        /// <summary>The member of its household it is following about, if any, and for how much longer (s).</summary>
        public Entity Following;
        public float FollowLeft;
    }

    /// <summary>
    /// A-120 row 1: household pets' day. Each second, solo or on the server, after the brain: at night a pet walks back to
    /// its bed and sleeps there; by day it potters within its leash of the bed, and now and then (a seeded roll from its
    /// Individual and the second) picks up one of its own folk near it and follows them about for a minute or few. The
    /// search looks only at the 3 x 3 cells round the pet (the A-112 lazy grid, made the first tick anyone searches),
    /// never the whole world.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    public partial struct HouseholdPetSystem : ISystem
    {
        const float Every = 1f;
        /// <summary>Per second by day, the chance a pet not following anyone looks for one of its folk to follow.</summary>
        public const float FollowChance = 1f / 240f;
        /// <summary>How near its folk must be to be picked up (m), and how far they may get before it gives up (m).</summary>
        const float FollowPick = 15f, FollowLose = 40f;
        const float CellSize = 16f;

        /// <summary>Debug tests: 1 every follow roll passes.</summary>
        public static readonly SharedStatic<int> Force = SharedStatic<int>.GetOrCreate<ForceKey>();
        struct ForceKey { }

        ComponentLookup<CreatureState> states;
        ComponentLookup<HouseholdPet> pets;
        ComponentLookup<CreatureBody> bodies;
        EntityQuery petQuery, creatures;
        float wait;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            petQuery = SystemAPI.QueryBuilder().WithAll<HouseholdPet, CreatureState>().Build();
            creatures = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBody>().WithNone<DungeonPost>().Build();
            state.RequireForUpdate(petQuery);
            state.RequireForUpdate<FaunaClock>();
            states = state.GetComponentLookup<CreatureState>();
            pets = state.GetComponentLookup<HouseholdPet>();
            bodies = state.GetComponentLookup<CreatureBody>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            wait -= SystemAPI.Time.DeltaTime;
            if (wait > 0f)
                return;
            wait = Every;
            states.Update(ref state);
            pets.Update(ref state);
            bodies.Update(ref state);
            var petList = petQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var a);
            var all = creatures.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var b);
            state.Dependency = new PetJob
            {
                Pets = petList,
                All = all,
                States = states,
                PetData = pets,
                Bodies = bodies,
                Night = SystemAPI.GetSingleton<FaunaClock>().Night,
                Force = Force.Data
            }.Schedule(JobHandle.CombineDependencies(a, b, state.Dependency));
        }

        [BurstCompile]
        struct PetJob : IJob
        {
            [ReadOnly] public NativeList<Entity> Pets, All;
            public ComponentLookup<CreatureState> States;
            public ComponentLookup<HouseholdPet> PetData;
            [ReadOnly] public ComponentLookup<CreatureBody> Bodies;
            public float Night;
            public int Force;

            public void Execute()
            {
                var grid = default(NativeParallelMultiHashMap<int2, int>);
                var near = new NativeList<int>(64, Allocator.Temp);
                for (int i = 0; i < Pets.Length; i++)
                {
                    var e = Pets[i];
                    var s = States[e];
                    var p = PetData[e];
                    if (s.Gone || s.Holder != 0 || Busy(s))
                        continue;
                    if (Night > 0.5f)
                        Bedtime(ref s, ref p);
                    else if (p.Following != Entity.Null)
                        Follow(ref s, ref p);
                    else if ((Force & 1) != 0 || Roll(s.Individual, (uint)s.Clock, 0x46u) < FollowChance)
                        Pick(e, ref s, ref p, ref grid, near);
                    States[e] = s;
                    PetData[e] = p;
                }
                if (grid.IsCreated)
                    grid.Dispose();
                near.Dispose();
            }

            /// <summary>Held, being petted, fleeing, scripted or greeting someone: left to that.</summary>
            static bool Busy(in CreatureState s) =>
                s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Petted || s.Mode == CreatureMode.Flee ||
                s.Mode == CreatureMode.Puppet || s.Mode == CreatureMode.Greet;

            static bool Pottering(in CreatureState s) =>
                s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Graze;

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

            static void Stop(ref CreatureState s, ref HouseholdPet p)
            {
                p.Following = Entity.Null;
                p.FollowLeft = 0f;
                s.Home = p.Bed;
                s.Leash = p.Leash;
            }

            /// <summary>Night: back to its bed, then asleep there till morning (the nap kept topped up while it's dark).</summary>
            static void Bedtime(ref CreatureState s, ref HouseholdPet p)
            {
                if (p.Following != Entity.Null)
                    Stop(ref s, ref p);
                s.Home = p.Bed;
                s.Leash = p.Leash;
                if (math.distancesq(s.Position.xz, p.Bed) > 1.5f * 1.5f)
                {
                    if (s.Mode != CreatureMode.Wander || math.distancesq(s.Target, p.Bed) > 0.01f || s.Stage != 0)
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        s.Target = p.Bed;
                    }
                }
                else if (s.Mode != CreatureMode.Sleep)
                {
                    SetMode(ref s, CreatureMode.Sleep);
                    s.Timer = 600f;
                }
                else
                    s.Timer = math.max(s.Timer, 600f);
            }

            /// <summary>Following one of its folk: leashed to them while they're out and near, then home to its bed.</summary>
            void Follow(ref CreatureState s, ref HouseholdPet p)
            {
                p.FollowLeft -= Every;
                if (p.FollowLeft <= 0f || !States.HasComponent(p.Following))
                {
                    Stop(ref s, ref p);
                    return;
                }
                var m = States[p.Following];
                if (m.Gone || m.Mode == CreatureMode.Home && m.Stage >= 1 || m.Mode == CreatureMode.Sleep ||
                    math.distancesq(m.Position.xz, s.Position.xz) > FollowLose * FollowLose)
                {
                    Stop(ref s, ref p);
                    return;
                }
                s.Home = m.Position.xz;
                s.Leash = 3f;
                if (Pottering(s) && math.distancesq(m.Position.xz, s.Position.xz) > 4f * 4f)
                {
                    // (A step behind them, to one side, so it trots along rather than walking into their heels.)
                    var away = math.normalizesafe(s.Position.xz - m.Position.xz, new float2(1f, 0f));
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = m.Position.xz + away * 1.5f;
                }
            }

            static int2 Cell(float2 xz) => (int2)math.floor(xz / CellSize);

            void Near(float2 at, ref NativeParallelMultiHashMap<int2, int> grid, NativeList<int> near)
            {
                if (!grid.IsCreated)
                {
                    grid = new NativeParallelMultiHashMap<int2, int>(math.max(16, All.Length), Allocator.Temp);
                    for (int i = 0; i < All.Length; i++)
                        grid.Add(Cell(States[All[i]].Position.xz), i);
                }
                near.Clear();
                var c = Cell(at);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (grid.TryGetFirstValue(c + new int2(dx, dz), out var i, out var it))
                        do near.Add(i);
                        while (grid.TryGetNextValue(out i, ref it));
                near.Sort();
            }

            /// <summary>Looks for one of its own folk out and about near it, and follows the nearest for 60 to 180 s.</summary>
            void Pick(Entity self, ref CreatureState s, ref HouseholdPet p, ref NativeParallelMultiHashMap<int2, int> grid, NativeList<int> near)
            {
                if (!Pottering(s))
                    return;
                Near(s.Position.xz, ref grid, near);
                var best = Entity.Null;
                var bestD = FollowPick * FollowPick;
                for (int k = 0; k < near.Length; k++)
                {
                    var o = All[near[k]];
                    if (o == self)
                        continue;
                    var m = States[o];
                    if (m.Gone || math.all(m.OwnDoor.xz == float2.zero) || math.any((int2)math.round(m.OwnDoor.xz * 2f) != p.Door) ||
                        m.Mode == CreatureMode.Home && m.Stage >= 1 || m.Mode == CreatureMode.Sleep || !Bodies[o].Blob.Value.Hands)
                        continue;
                    var d = math.distancesq(m.Position.xz, s.Position.xz);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = o;
                    }
                }
                if (best == Entity.Null)
                    return;
                p.Following = best;
                p.FollowLeft = 60f + Roll(s.Individual, (uint)s.Clock, 0x47u) * 120f;
            }

            /// <summary>A roll 0..1 from an individual and a moment (seconds, whole).</summary>
            static float Roll(uint individual, uint moment, uint salt) =>
                SpawnTable.Scramble(individual ^ SpawnTable.Scramble(moment * 0x9E3779B9u + salt)) / (float)uint.MaxValue;
        }
    }
}
