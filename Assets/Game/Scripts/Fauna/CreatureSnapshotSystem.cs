using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Punchy.Fauna
{
    /// <summary>
    /// One copy per frame of every creature's state, body and entity (#56), for the main-thread readers in the
    /// presentation group (audio, breath, burrows, wakes, folk talk, traces, the mantraps) that each used to copy all
    /// three arrays themselves. Readers take it with Get and hand it back with Release; it is read-only.
    /// A-70 row 3: the copy is a job (CopyJob) scheduled first thing in the presentation group behind the brain, not
    /// a copy on the main thread that waits for the brain to finish. Two are kept: this frame's, still being made, which
    /// CreatureRenderSystem takes (Current) and poses from behind its handle, and last frame's, long finished, which the
    /// readers that can be a frame behind get from Get without waiting for anything. "-syncsnapshot" copies on the main
    /// thread at once as before (and the renderer copies its own) for an A/B.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class CreatureSnapshotSystem : SystemBase
    {
        /// <summary>#56 A/B switch ("-oldsnapshot"): every reader copies its own arrays again.</summary>
        public static bool Off;
        /// <summary>A-70 row 3 A/B switch ("-syncsnapshot"): copied on the main thread, this frame's for every reader, as before.</summary>
        public static bool Sync;
        /// <summary>For -perflog: arrays copied (each of state, body, entity counts one) and the time spent copying on the
        /// main thread, and the time the main thread spent waiting for the creature jobs (the brain) before presenting.</summary>
        public static long Copies, CopyTicks, WaitTicks;

        /// <summary>One frame's copy, and the job making it.</summary>
        struct Snap
        {
            public NativeArray<CreatureState> States;
            public NativeArray<CreatureBody> Bodies;
            public NativeArray<Entity> Entities;
            /// <summary>Creatures held, and carriers with their arms open for one (HeldPoseSystem's work).</summary>
            public NativeReference<int> Holding;
            public JobHandle Done;
            public int Frame;

            public void Free()
            {
                Done.Complete();
                if (States.IsCreated) States.Dispose();
                if (Bodies.IsCreated) Bodies.Dispose();
                if (Entities.IsCreated) Entities.Dispose();
                if (Holding.IsCreated) Holding.Dispose();
                this = default;
            }
        }

        EntityQuery query;
        Snap current, last;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnDestroy()
        {
            current.Free();
            last.Free();
        }

        protected override void OnUpdate()
        {
            // (Last frame's copy, finished long since, becomes the one the readers get; the one before goes.)
            last.Free();
            current.Done.Complete();
            last = current;
            current = default;
            // Whether HeldPoseSystem has anything to do: someone held or arms still open in the last copy (or every
            // frame, if there is none to go by); a pickup since then tells it itself (HeldPoseSystem.Pending).
            HeldPoseSystem.Needed.Data = Off || Sync || !last.Holding.IsCreated || last.Holding.Value > 0 ? 1 : 0;
            if (Off)
                return;
            var frame = UnityEngine.Time.frameCount;
            if (Sync)
            {
                var wait = System.Diagnostics.Stopwatch.GetTimestamp();
                CompleteDependency();
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                WaitTicks += start - wait;
                current.States = query.ToComponentDataArray<CreatureState>(Allocator.Persistent);
                current.Bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Persistent);
                current.Entities = query.ToEntityArray(Allocator.Persistent);
                CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                Copies += 3;
                current.Frame = frame;
                return;
            }
            var count = query.CalculateEntityCount();
            current.States = new NativeArray<CreatureState>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            current.Bodies = new NativeArray<CreatureBody>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            current.Entities = new NativeArray<Entity>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            current.Holding = new NativeReference<int>(Allocator.Persistent);
            Dependency = new CopyJob
            {
                States = current.States,
                Bodies = current.Bodies,
                Entities = current.Entities,
                Holding = current.Holding
            }.Schedule(Dependency);
            current.Done = Dependency;
            current.Frame = frame;
            Copies += 3;
        }

        /// <summary>Every creature's state, body and entity, in the query's order (as ToComponentDataArray gives them).</summary>
        [BurstCompile]
        partial struct CopyJob : IJobEntity
        {
            public NativeArray<CreatureState> States;
            public NativeArray<CreatureBody> Bodies;
            public NativeArray<Entity> Entities;
            public NativeReference<int> Holding;

            void Execute([EntityIndexInQuery] int i, Entity e, in CreatureState s, in CreatureBody b)
            {
                States[i] = s;
                Bodies[i] = b;
                Entities[i] = e;
                if (s.Mode == CreatureMode.Held || s.CarryWidth != 0f)
                    Holding.Value++;
            }
        }

        /// <summary>
        /// A-70 row 3: this frame's copy for CreatureRenderSystem, still being made (<paramref name="done"/> finishes it);
        /// false when there is none (switched off, "-syncsnapshot", or not taken this frame) and it copies its own.
        /// </summary>
        public static bool Current(World world, out NativeArray<CreatureState> states, out NativeArray<CreatureBody> bodies,
                                   out NativeArray<Entity> entities, out JobHandle done)
        {
            var shared = Off || Sync ? null : world.GetExistingSystemManaged<CreatureSnapshotSystem>();
            if (shared != null && shared.current.Frame == UnityEngine.Time.frameCount && shared.current.States.IsCreated)
            {
                states = shared.current.States;
                bodies = shared.current.Bodies;
                entities = shared.current.Entities;
                done = shared.current.Done;
                return true;
            }
            states = default;
            bodies = default;
            entities = default;
            done = default;
            return false;
        }

        /// <summary>
        /// The creature arrays for a reader in <paramref name="world"/>: the last finished copy (last frame's; this
        /// frame's under "-syncsnapshot"), or (switched off, or none yet) the reader's own copy from
        /// <paramref name="query"/> (CreatureState + CreatureBody). Last frame's may name creatures gone since.
        /// </summary>
        public static void Get(World world, EntityQuery query, out NativeArray<CreatureState> states, out NativeArray<CreatureBody> bodies,
                               out NativeArray<Entity> entities)
        {
            var shared = Off ? null : world.GetExistingSystemManaged<CreatureSnapshotSystem>();
            if (shared != null)
            {
                if (Sync && shared.current.Frame == UnityEngine.Time.frameCount && shared.current.States.IsCreated)
                {
                    states = shared.current.States;
                    bodies = shared.current.Bodies;
                    entities = shared.current.Entities;
                    return;
                }
                if (!Sync && shared.last.States.IsCreated)
                {
                    states = shared.last.States;
                    bodies = shared.last.Bodies;
                    entities = shared.last.Entities;
                    return;
                }
            }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            entities = query.ToEntityArray(Allocator.Temp);
            CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            Copies += 3;
        }

        /// <summary>Hands back what Get gave: a reader's own copy is disposed, the shared copies are left alone.</summary>
        public static void Release(World world, NativeArray<CreatureState> states, NativeArray<CreatureBody> bodies, NativeArray<Entity> entities)
        {
            var shared = world.GetExistingSystemManaged<CreatureSnapshotSystem>();
            if (shared != null && (shared.current.States.IsCreated && states.Equals(shared.current.States) ||
                                   shared.last.States.IsCreated && states.Equals(shared.last.States)))
                return;
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }
    }
}
