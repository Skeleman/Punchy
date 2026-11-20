using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Ambience
{
    /// <summary>
    /// The measured shape of every river waterfall (<see cref="FallShape.River"/>), worked out once per terrain on
    /// worker threads. Measuring one takes thousands of ground samples, so redoing the near ones on every rebuild of
    /// the emitters stalled the game for over a second each time the near set changed (#712).
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        NativeArray<FallShape> riverShapes;
        JobHandle riverShapesJob;
        bool riverShapesReady;

        [BurstCompile]
        struct RiverShapeJob : IJobParallelFor
        {
            [ReadOnly] public TerrainSource Source;
            public NativeArray<FallShape> Shapes;

            public void Execute(int index) =>
                Shapes[index] = FallShape.River(Source, Source.Rivers.Value.Falls[index], RiverFallSeed(index));
        }

        static float RiverFallSeed(int index) => math.frac((index + RiverFallBase) * 0.618f);

        void BeginRiverShapes(in TerrainSource source)
        {
            DisposeRiverShapes();
            if (!source.Rivers.IsCreated || source.Rivers.Value.Falls.Length == 0)
                return;
            riverShapes = new NativeArray<FallShape>(source.Rivers.Value.Falls.Length, Allocator.Persistent);
            riverShapesJob = new RiverShapeJob { Source = source, Shapes = riverShapes }.Schedule(riverShapes.Length, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        bool RiverShapesReady()
        {
            if (!riverShapesReady && riverShapes.IsCreated && riverShapesJob.IsCompleted)
            {
                riverShapesJob.Complete();
                riverShapesReady = true;
            }
            return riverShapesReady;
        }

        void DisposeRiverShapes()
        {
            riverShapesJob.Complete();
            if (riverShapes.IsCreated)
                riverShapes.Dispose();
            riverShapesReady = false;
        }
    }
}
