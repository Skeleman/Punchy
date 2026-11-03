using Unity.Collections;
using Unity.Entities;

namespace Punchy.Fauna
{
    /// <summary>
    /// One copy per frame of every creature's state, body and entity (#56), taken first thing in the presentation group,
    /// after the brain has finished, for the main-thread readers there (audio, breath, burrows, wakes, folk talk, traces)
    /// that each used to copy all three arrays themselves. Readers take it with Get and hand it back with Release; it
    /// is read-only, and only good for the frame it was taken in (otherwise Get makes a reader its own copy, as before).
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class CreatureSnapshotSystem : SystemBase
    {
        /// <summary>#56 A/B switch ("-oldsnapshot"): every reader copies its own arrays again.</summary>
        public static bool Off;
        /// <summary>For -perflog: arrays copied (each of state, body, entity counts one) and the time spent copying.</summary>
        public static long Copies, CopyTicks;

        EntityQuery query;
        NativeArray<CreatureState> states;
        NativeArray<CreatureBody> bodies;
        NativeArray<Entity> entities;
        int frame = -1;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnDestroy() => Free();

        protected override void OnUpdate()
        {
            Free();
            frame = -1;
            if (Off)
                return;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            states = query.ToComponentDataArray<CreatureState>(Allocator.Persistent);
            bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Persistent);
            entities = query.ToEntityArray(Allocator.Persistent);
            CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            Copies += 3;
            frame = UnityEngine.Time.frameCount;
        }

        void Free()
        {
            if (states.IsCreated) states.Dispose();
            if (bodies.IsCreated) bodies.Dispose();
            if (entities.IsCreated) entities.Dispose();
        }

        /// <summary>
        /// This frame's creature arrays for a reader in <paramref name="world"/>: the shared snapshot, or (switched off, or
        /// not taken this frame) the reader's own copy from <paramref name="query"/> (CreatureState + CreatureBody).
        /// </summary>
        public static void Get(World world, EntityQuery query, out NativeArray<CreatureState> states, out NativeArray<CreatureBody> bodies,
                               out NativeArray<Entity> entities)
        {
            var shared = Off ? null : world.GetExistingSystemManaged<CreatureSnapshotSystem>();
            if (shared != null && shared.frame == UnityEngine.Time.frameCount && shared.states.IsCreated)
            {
                states = shared.states;
                bodies = shared.bodies;
                entities = shared.entities;
                return;
            }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            entities = query.ToEntityArray(Allocator.Temp);
            CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            Copies += 3;
        }

        /// <summary>Hands back what Get gave: a reader's own copy is disposed, the shared snapshot is left alone.</summary>
        public static void Release(World world, NativeArray<CreatureState> states, NativeArray<CreatureBody> bodies, NativeArray<Entity> entities)
        {
            var shared = world.GetExistingSystemManaged<CreatureSnapshotSystem>();
            if (shared != null && shared.states.IsCreated && states.Equals(shared.states))
                return;
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }
    }
}
