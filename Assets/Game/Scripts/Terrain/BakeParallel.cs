using System;
using System.Threading;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-103: a parallel for-loop for the island bake's managed steps (the ground rows, the river bank samples). It runs on
    /// its own short-lived threads rather than the .NET thread pool, because the plants pregrow on that pool beside the bake
    /// (#102) and a Parallel.For queued behind hundreds of plant tasks ran on the main thread alone. The threads run
    /// above normal priority: the bake is on the critical path, the plants have until the flora build. Each index runs
    /// once with the same managed maths as a plain loop, so results are identical as long as indices write only their
    /// own outputs.
    /// </summary>
    public static class BakeParallel
    {
        public static void For(int count, Action<int> body)
        {
            var workers = Math.Min(Environment.ProcessorCount - 1, count - 1);
            if (workers <= 0)
            {
                for (int i = 0; i < count; i++) body(i);
                return;
            }
            var next = -1;
            Exception error = null;
            void Run()
            {
                try
                {
                    int i;
                    while ((i = Interlocked.Increment(ref next)) < count) body(i);
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref error, e, null);
                    Interlocked.Exchange(ref next, count);
                }
            }
            var threads = new Thread[workers];
            for (int t = 0; t < workers; t++)
            {
                threads[t] = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Bake" };
                threads[t].Start();
            }
            Run();
            foreach (var t in threads) t.Join();
            if (error != null) throw new AggregateException(error);
        }
    }
}
