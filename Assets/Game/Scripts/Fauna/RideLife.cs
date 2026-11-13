using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-104 row 2: creatures that get on one another by themselves, seeded and sparse. A village grown-up setting off home
    /// sometimes takes a child of its folk near it up on its shoulders for the walk (down near the door, Riding.Reason);
    /// a small pet sometimes climbs on a bigger animal near it for a while (20 to 60 s). Each second, solo or on the
    /// server, after the brain has picked what everyone's doing and before RideSystem seats the riders. The rolls come
    /// from each creature's Individual and the time it set off, so they're the same on every run; what they decide goes
    /// out through the same ghost fields as any ride (Holder, Mode).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [UpdateAfter(typeof(ColossusPushSystem))]
    [UpdateBefore(typeof(RideSystem))]
    public partial struct RideLifeSystem : ISystem
    {
        /// <summary>How often the rolls are made (s).</summary>
        const float Every = 1f;
        /// <summary>Of grown-ups setting off home with a child of their folk near them, how many take it up.</summary>
        public const float ChildChance = 0.3f;
        /// <summary>Of pets' half-minute spells, in how many each looks for something to ride.</summary>
        public const float PetChance = 0.08f;

        /// <summary>Debug tests (-ridelife): 1 every child roll passes, 2 every pet roll passes, 4 no rides begin at all.</summary>
        public static readonly SharedStatic<int> Force = SharedStatic<int>.GetOrCreate<ForceKey>();
        struct ForceKey { }

        ComponentLookup<CreatureState> states;
        ComponentLookup<CreatureBody> bodies;
        ComponentLookup<GhostInstance> ghosts;
        EntityQuery creatures, riders;
        float wait;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // (Not a dungeon's guards and drinkers: their posts are theirs to keep (DungeonPostSystem sends a guard back
            // to its chest whatever mode it is in), and their Leash made them pass for town pets.)
            creatures = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBody>().WithNone<DungeonPost>().Build();
            riders = SystemAPI.QueryBuilder().WithAll<Rider>().Build();
            state.RequireForUpdate(creatures);
            state.RequireForUpdate<TerrainSource>();
            states = state.GetComponentLookup<CreatureState>();
            bodies = state.GetComponentLookup<CreatureBody>(true);
            ghosts = state.GetComponentLookup<GhostInstance>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            wait -= SystemAPI.Time.DeltaTime;
            if (wait > 0f || (Force.Data & 4) != 0)
                return;
            wait = Every;
            states.Update(ref state);
            bodies.Update(ref state);
            ghosts.Update(ref state);
            // (The lists' handles only cover gathering them: the job also waits on state.Dependency, so it never runs
            // alongside the brain's job, which writes the same states.)
            var all = creatures.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var a);
            var riding = riders.ToComponentDataListAsync<Rider>(state.WorldUpdateAllocator, state.Dependency, out var b);
            var ridingWho = riders.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var c);
            state.Dependency = new LifeJob
            {
                All = all,
                Rides = riding,
                RidingWho = ridingWho,
                States = states,
                Bodies = bodies,
                Ghosts = ghosts,
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Force = Force.Data,
                Commands = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged)
            }.Schedule(JobHandle.CombineDependencies(JobHandle.CombineDependencies(a, b, c), state.Dependency));
        }

        [BurstCompile]
        struct LifeJob : IJob
        {
            [ReadOnly] public NativeList<Entity> All, RidingWho;
            [ReadOnly] public NativeList<Rider> Rides;
            public ComponentLookup<CreatureState> States;
            [ReadOnly] public ComponentLookup<CreatureBody> Bodies;
            [ReadOnly] public ComponentLookup<GhostInstance> Ghosts;
            [ReadOnly] public TerrainSource Terrain;
            public int Force;
            public EntityCommandBuffer Commands;

            public void Execute()
            {
                // (Who's riding or ridden already, and so out of it.)
                var taken = new NativeHashSet<Entity>(math.max(16, Rides.Length * 2), Allocator.Temp);
                for (int i = 0; i < Rides.Length; i++)
                {
                    taken.Add(Rides[i].Mount);
                    taken.Add(RidingWho[i]);
                }
                for (int i = 0; i < All.Length; i++)
                {
                    var e = All[i];
                    if (taken.Contains(e))
                        continue;
                    var s = States[e];
                    ref var b = ref Bodies[e].Blob.Value;
                    if (s.Gone || s.Holder != 0 || s.Fly > 0.3f)
                        continue;
                    if (GrownUpHome(ref b, s))
                        TryChild(e, s, ref taken);
                    else if (Pet(ref b, s))
                        TryPet(e, s, ref taken);
                }
                taken.Dispose();
            }

            /// <summary>A village grown-up just set off home, its door still a fair walk away.</summary>
            static bool GrownUpHome(ref CreatureBlob b, in CreatureState s) =>
                !b.Child && b.Hands && b.Upright && s.Leash > 0f && s.Mode == CreatureMode.Home && s.Stage == 0 && s.ModeTime < Every * 1.5f &&
                math.distancesq(s.Position.xz, s.Anchor.xz) > 6f * 6f;

            /// <summary>A small tame animal (a town's cat, a dog) pottering about.</summary>
            static bool Pet(ref CreatureBlob b, in CreatureState s) =>
                !b.Hands && b.Habitat == Habitat.Land && Interactions.Tame(ref b, s) && Idle(s);

            static bool Idle(in CreatureState s) =>
                (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Graze) && s.ModeTime > 3f;

            bool Dry(in CreatureState s)
            {
                var g = Terrain.SampleGround(s.Position.xz);
                return !g.HasWater || g.WaterSurface < g.Height + 0.05f;
            }

            /// <summary>A roll 0..1 from an individual and a moment (seconds, whole).</summary>
            static float Roll(uint individual, uint moment, uint salt) =>
                SpawnTable.Scramble(individual ^ SpawnTable.Scramble(moment * 0x9E3779B9u + salt)) / (float)uint.MaxValue;

            void TryChild(Entity mount, CreatureState m, ref NativeHashSet<Entity> taken)
            {
                // (One roll per walk home: from the second it set off.)
                if ((Force & 1) == 0 && Roll(m.Individual, (uint)math.max(0f, m.Clock - m.ModeTime), 0x51u) >= ChildChance)
                    return;
                ref var mb = ref Bodies[mount].Blob.Value;
                // (No family ties yet: the nearest child of the grown-up's folk and village (their home rounds overlap: a
                // grown-up with a post lives at it, not at the village's middle), in reach and at a loose end.)
                var best = Entity.Null;
                var bestD = 12f * 12f;
                for (int i = 0; i < All.Length; i++)
                {
                    var e = All[i];
                    if (e == mount || taken.Contains(e))
                        continue;
                    var r = States[e];
                    ref var rb = ref Bodies[e].Blob.Value;
                    if (!rb.Child || !rb.Hands || !rb.Upright || r.Gone || r.Holder != 0 || r.ChatRole != 0 || r.Fly > 0.3f ||
                        rb.Comfort.Race != mb.Comfort.Race || r.Leash <= 0f ||
                        math.distance(r.Home, m.Home) > r.Leash + m.Leash)
                        continue;
                    if (r.Mode != CreatureMode.Idle && r.Mode != CreatureMode.Wander && r.Mode != CreatureMode.Play &&
                        !(r.Mode == CreatureMode.Home && r.Stage == 0))
                        continue;
                    var d = math.distancesq(r.Position.xz, m.Position.xz);
                    if (d < bestD && Riding.Check(ref rb, r, ref mb, m) == RideRefusal.None && Dry(r))
                    {
                        best = e;
                        bestD = d;
                    }
                }
                if (best != Entity.Null)
                    Begin(best, mount, m, RideKind.WalkHome, 0f, ref taken);
            }

            void TryPet(Entity rider, CreatureState r, ref NativeHashSet<Entity> taken)
            {
                // (One roll per half-minute spell, in its first ten seconds.)
                var spell = (uint)(r.Clock / 30f);
                if (r.Clock - spell * 30f > 10f)
                    return;
                if ((Force & 2) == 0 && Roll(r.Individual, spell, 0xA7u) >= PetChance)
                    return;
                if (!Dry(r))
                    return;
                ref var rb = ref Bodies[rider].Blob.Value;
                var best = Entity.Null;
                var bestD = 10f * 10f;
                for (int i = 0; i < All.Length; i++)
                {
                    var e = All[i];
                    if (e == rider || taken.Contains(e))
                        continue;
                    var m = States[e];
                    ref var mb = ref Bodies[e].Blob.Value;
                    if (mb.Hands || mb.Habitat != Habitat.Land || m.Gone || m.Holder != 0 || m.Fly > 0.3f || !Idle(m))
                        continue;
                    var d = math.distancesq(r.Position.xz, m.Position.xz);
                    if (d < bestD && Riding.Check(ref rb, r, ref mb, m) == RideRefusal.None && Dry(m))
                    {
                        best = e;
                        bestD = d;
                    }
                }
                if (best != Entity.Null)
                    Begin(rider, best, States[best], RideKind.Pet, 20f + 40f * Roll(r.Individual, spell, 0x3Cu), ref taken);
            }

            void Begin(Entity rider, Entity mount, in CreatureState m, RideKind kind, float left, ref NativeHashSet<Entity> taken)
            {
                var r = States[rider];
                var rBody = Bodies[rider];
                var mBody = Bodies[mount];
                var ghostId = Ghosts.HasComponent(mount) ? Ghosts[mount].ghostId : 0;
                Riding.Begin(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, ghostId, Terrain);
                States[rider] = r;
                Commands.AddComponent(rider, new Rider { Mount = mount, Left = left, Kind = kind });
                taken.Add(rider);
                taken.Add(mount);
                ref var log = ref RideLog.Data.Data;
                if (log.On != 0)
                    log.Begun++;
            }
        }
    }
}
