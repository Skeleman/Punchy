using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Punchy.Terrain;

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
        /// <summary>A-120 row 2: the folk coming over to pet it (or crouched petting it), whether the petting has been
        /// done to it yet, and how long before anyone else will try (s).</summary>
        public Entity Petter;
        public bool Petted;
        public float PetCool;
        /// <summary>A-120 row 3: the Petter is a child come to carry it into its house, not to pet it.</summary>
        public bool Carry;
    }

    /// <summary>
    /// A-120 row 1: household pets' day. Each second, solo or on the server, after the brain: at night a pet walks back to
    /// its bed and sleeps there; by day it potters within its leash of the bed, and now and then (a seeded roll from its
    /// Individual and the second) picks up one of its own folk near it and follows them about for a minute or few. The
    /// search looks only at the 3 x 3 cells round the pet (the A-112 lazy grid, made the first tick anyone searches),
    /// never the whole world.
    /// A-120 row 2: by day, folk near a pet may go over and pet it, by their Beasts trait (FolkPersona.BeastsOf: the
    /// fonder, the likelier; its own household's folk three times as likely, and from further off); those who can't
    /// abide animals shoo it off instead. The petting itself is done here, to the pet (Interactions.Apply, with the folk's
    /// key as its Holder: Interactions.FolkHolder), once the folk is crouched by it (the brain's Petting walks it there).
    /// A-120 row 3: a child going over to it (its own household's most likely, a neighbour's at half the chance) may
    /// instead pick it up and carry it into the child's house and out again, where it squirms free (Carried; the
    /// brain's Held does the door and the squirm).
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
        /// <summary>A-120 row 2: per second, the chance a folk near a pet (within PetPick, its own folk OwnPetPick) goes
        /// over to pet it, at the top of the Beasts trait (it goes with the trait squared, and three times for its own
        /// folk); and the chance one in the bottom band shoos it, when it's within ShooPick.</summary>
        public const float PetChance = 1f / 40f, ShooChance = 1f / 40f;
        const float PetPick = 12f, OwnPetPick = 20f, ShooPick = 5f;
        /// <summary>How long after being petted or shooed before anyone else tries (s).</summary>
        const float PetCoolMin = 30f, PetCoolMax = 60f;
        /// <summary>A-120 row 3: the share of its own household's children going over to it who pick it up and carry it
        /// indoors instead of petting it (half that for a neighbour's child); how near they must be to lift it (m); the heaviest a child will try (kg); and how
        /// long they stay in with it (s).</summary>
        public const float CarryChance = 1f / 3f;
        const float CarryReach = 1.3f, ChildCarry = 20f, CarryInMin = 6f, CarryInMax = 12f;

        /// <summary>Debug tests: 1 every follow roll passes, 2 every petting (or shooing) roll, 4 every child's carry roll.</summary>
        public static readonly SharedStatic<int> Force = SharedStatic<int>.GetOrCreate<ForceKey>();
        struct ForceKey { }

        ComponentLookup<CreatureState> states;
        ComponentLookup<HouseholdPet> pets;
        ComponentLookup<CreatureBody> bodies;
        ComponentLookup<GhostInstance> ghosts;
        EntityQuery petQuery, creatures;
        float wait;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            petQuery = SystemAPI.QueryBuilder().WithAll<HouseholdPet, CreatureState>().Build();
            creatures = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBody>().WithNone<DungeonPost>().Build();
            state.RequireForUpdate(petQuery);
            state.RequireForUpdate<FaunaClock>();
            state.RequireForUpdate<TerrainSource>();
            states = state.GetComponentLookup<CreatureState>();
            pets = state.GetComponentLookup<HouseholdPet>();
            bodies = state.GetComponentLookup<CreatureBody>(true);
            ghosts = state.GetComponentLookup<GhostInstance>(true);
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
            ghosts.Update(ref state);
            var petList = petQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var a);
            var all = creatures.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var b);
            state.Dependency = new PetJob
            {
                Pets = petList,
                All = all,
                States = states,
                PetData = pets,
                Bodies = bodies,
                Ghosts = ghosts,
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
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
            [ReadOnly] public ComponentLookup<GhostInstance> Ghosts;
            [ReadOnly] public TerrainSource Terrain;
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
                    p.PetCool = math.max(p.PetCool - Every, 0f);
                    // (Being gone over to, or petted, by one of the folk: its own step, before Busy, as Petted is busy.)
                    if (p.Petter != Entity.Null)
                    {
                        Tend(e, ref s, ref p);
                        States[e] = s;
                        PetData[e] = p;
                        continue;
                    }
                    if (s.Gone || s.Holder != 0 || Busy(s))
                    {
                        PetData[e] = p;
                        continue;
                    }
                    if (Night > 0.5f)
                        Bedtime(ref s, ref p);
                    else if (p.PetCool <= 0f && Pottering(s) && Petters(e, ref s, ref p, ref grid, near))
                    {
                    }
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

            /// <summary>One of the folk who could stop for an animal: about on its own (walking or standing, not talking,
            /// keeping a post, reading a letter), with hands, not a ghost. (One on its way to a seat may stop too: it
            /// decides afresh after. Not one walking to work: a chat keeps that walk to pick up again, this can't.)</summary>
            static bool PetFree(in CreatureState m, ref CreatureBlob b) =>
                b.Upright && b.Hands && !b.Haunts && !m.Gone && m.Leash > 0f && m.ChatRole == 0 &&
                m.Task != CreatureTask.Patrol && m.Task != CreatureTask.Watch && m.Held != HeldItem.Letter &&
                (m.Mode == CreatureMode.Wander || m.Mode == CreatureMode.Idle && m.Stage < CreatureAnimator.MessageWait ||
                 m.Mode == CreatureMode.Sit && m.Stage == 0);

            /// <summary>
            /// A-120 row 2: whether any of the folk near it (3 x 3 cells) stops for it this second, by their Beasts trait:
            /// the nearest whose roll passes goes over to pet it (the pet waits for them), or, one in the bottom band and
            /// not its own folk, shoos it off. True when someone did.
            /// </summary>
            bool Petters(Entity self, ref CreatureState s, ref HouseholdPet p, ref NativeParallelMultiHashMap<int2, int> grid, NativeList<int> near)
            {
                Near(s.Position.xz, ref grid, near);
                var best = Entity.Null;
                var bestD = float.MaxValue;
                var shoo = false;
                var bestOwn = false;
                for (int k = 0; k < near.Length; k++)
                {
                    var o = All[near[k]];
                    if (o == self)
                        continue;
                    var m = States[o];
                    var body = Bodies[o];
                    if (!PetFree(in m, ref body.Blob.Value))
                        continue;
                    var own = !math.all(m.OwnDoor.xz == float2.zero) && math.all((int2)math.round(m.OwnDoor.xz * 2f) == p.Door);
                    var d = math.distancesq(m.Position.xz, s.Position.xz);
                    var reach = own ? OwnPetPick : PetPick;
                    if (d > reach * reach || d >= bestD)
                        continue;
                    var beasts = FolkPersona.BeastsOf(m.Individual);
                    var hates = FolkPersona.Band(beasts) == 0 && !own;
                    if (hates && d > ShooPick * ShooPick)
                        continue;
                    var chance = hates ? ShooChance * (1f - 3f * beasts) : PetChance * beasts * beasts * (own ? 3f : 1f);
                    if ((Force & 2) == 0 && Roll(m.Individual, (uint)s.Clock, 0x50u) >= chance)
                        continue;
                    best = o;
                    bestD = d;
                    shoo = hates;
                    bestOwn = own;
                }
                if (best == Entity.Null)
                    return false;
                var f = States[best];
                SetMode(ref f, CreatureMode.Idle);
                f.Partner = self;
                if (shoo)
                {
                    // Flapped off: it scuttles a few metres away, and leaves people be a while.
                    f.Stage = CreatureAnimator.PetShoo;
                    f.Timer = 2.5f;
                    SetMode(ref s, CreatureMode.Flee);
                    var away = math.normalizesafe(s.Position.xz - f.Position.xz, new float2(1f, 0f));
                    s.Target = s.Position.xz + away * 7f;
                    s.Focus = f.Position.xz;
                    s.Timer = 3f;
                    p.PetCool = PetCoolMin + Roll(s.Individual, (uint)s.Clock, 0x51u) * (PetCoolMax - PetCoolMin);
                }
                else
                {
                    // Over to it; the pet stops where it is and waits for them (Tend keeps it waiting). Row 3: a child may
                    // come to scoop it up and take it into its own house instead, if it's light enough: its own household's
                    // pet most likely, a neighbour's now and then (few houses with children have a pet of their own).
                    var carry = Bodies[best].Blob.Value.Child && !math.all(f.OwnDoor.xz == float2.zero) &&
                                Bodies[self].Blob.Value.WeightAt(s.Scale) <= ChildCarry &&
                                ((Force & 4) != 0 || Roll(f.Individual, (uint)s.Clock, 0x53u) < CarryChance * (bestOwn ? 1f : 0.5f));
                    f.Stage = carry ? CreatureAnimator.PetFetch : CreatureAnimator.PetGo;
                    f.Timer = 20f;
                    SetMode(ref s, CreatureMode.Idle);
                    s.Timer = 5f;
                    p.Petter = best;
                    p.Petted = false;
                    p.Carry = carry;
                }
                States[best] = f;
                return true;
            }

            /// <summary>
            /// A-120 row 2: one of the folk on its way to pet it, or crouched by it. Waiting for them; once they're down by
            /// it, the petting (Interactions.Apply, Holder their key: a wary pet may walk off instead); and when they've
            /// stood again, or gone off to something else, free (and left alone a while).
            /// </summary>
            void Tend(Entity self, ref CreatureState s, ref HouseholdPet p)
            {
                var gone = !States.HasComponent(p.Petter);
                var f = gone ? default : States[p.Petter];
                if (p.Carry)
                {
                    Carried(self, ref s, ref p, gone, f);
                    return;
                }
                var busy = !gone && !f.Gone && f.Mode == CreatureMode.Idle && f.Partner == self &&
                           (f.Stage == CreatureAnimator.PetGo || f.Stage == CreatureAnimator.PetCrouch);
                if (!busy)
                {
                    p.Petter = Entity.Null;
                    p.PetCool = PetCoolMin + Roll(s.Individual, (uint)s.Clock, 0x52u) * (PetCoolMax - PetCoolMin);
                    return;
                }
                if (f.Stage == CreatureAnimator.PetGo)
                {
                    // (Waiting where it is, unless something else took it: it isn't held to it.)
                    if (s.Mode == CreatureMode.Idle)
                        s.Timer = math.max(s.Timer, 2f);
                    return;
                }
                if (p.Petted || f.ActionTime < 0.1f || s.Gone || s.Holder != 0 || s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Held)
                    return;
                p.Petted = true;
                var key = Interactions.FolkHolder(p.Petter, Ghosts.HasComponent(p.Petter) ? Ghosts[p.Petter].ghostId : 0);
                var body = Bodies[self];
                Interactions.Apply(ref s, ref body.Blob.Value, Terrain, InteractAct.Pet, key, f.Position, f.Yaw, true);
            }

            /// <summary>
            /// A-120 row 3: a child come to carry it into its house. Waiting for them; once they're by it,
            /// picked up (Interactions.Apply, Holder their key: a wary pet may still walk off) and the child sent home with
            /// it, in at its own door (Home mode) for a few seconds and out again. While they're through the door it fades
            /// with them, and once they're out it squirms free: both the brain's Held. Free again (and left alone a while)
            /// when it's out of their arms, or they went off to something else before they got it.
            /// </summary>
            void Carried(Entity self, ref CreatureState s, ref HouseholdPet p, bool gone, CreatureState f)
            {
                var key = Interactions.FolkHolder(p.Petter, Ghosts.HasComponent(p.Petter) ? Ghosts[p.Petter].ghostId : 0);
                if (!gone && !f.Gone && f.Mode == CreatureMode.Idle && f.Partner == self && f.Stage == CreatureAnimator.PetFetch)
                {
                    if (s.Mode == CreatureMode.Idle)
                        s.Timer = math.max(s.Timer, 2f);
                    if (math.distancesq(f.Position.xz, s.Position.xz) > CarryReach * CarryReach || s.Gone || s.Holder != 0 || Busy(s))
                        return;
                    var body = Bodies[self];
                    Interactions.Apply(ref s, ref body.Blob.Value, Terrain, InteractAct.PickUp, key, f.Position, f.Yaw, true);
                    if (s.Mode != CreatureMode.Held)
                    {
                        // (It wouldn't be picked up: the child shrugs and goes on.)
                        SetMode(ref f, CreatureMode.Idle);
                        f.Timer = 2f;
                        States[p.Petter] = f;
                        Free(ref s, ref p);
                        return;
                    }
                    // Settled in their arms until they're in and out again (the brain then lets it squirm free).
                    s.Timer = 1e6f;
                    SetMode(ref f, CreatureMode.Home);
                    f.Anchor = new float3(f.OwnDoor.x, Terrain.SampleHeight(f.OwnDoor.xz), f.OwnDoor.z);
                    f.AnchorYaw = f.OwnDoor.w;
                    f.Timer = CarryInMin + Roll(f.Individual, (uint)s.Clock, 0x54u) * (CarryInMax - CarryInMin);
                    States[p.Petter] = f;
                    return;
                }
                if (s.Mode == CreatureMode.Held && s.Holder == key)
                    return;
                Free(ref s, ref p);
            }

            void Free(ref CreatureState s, ref HouseholdPet p)
            {
                p.Petter = Entity.Null;
                p.Carry = false;
                p.PetCool = PetCoolMin + Roll(s.Individual, (uint)s.Clock, 0x55u) * (PetCoolMax - PetCoolMin);
            }

            /// <summary>A roll 0..1 from an individual and a moment (seconds, whole).</summary>
            static float Roll(uint individual, uint moment, uint salt) =>
                SpawnTable.Scramble(individual ^ SpawnTable.Scramble(moment * 0x9E3779B9u + salt)) / (float)uint.MaxValue;
        }
    }
}
