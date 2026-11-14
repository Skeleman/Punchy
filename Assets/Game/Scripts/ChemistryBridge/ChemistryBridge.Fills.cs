using System;
using System.Collections.Generic;
using System.Linq;
using Chem;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The host's fills off the main thread (D20; a teleport into new ground used to stall the host's frame 100-250 ms):
    /// each chunk's ground is sampled by its own Burst job (GroundJob) over the worker threads, started as soon as a
    /// character's reach wants the chunk (checked every frame, and before the bridge's own teleports land), and the
    /// planner stamps a chunk only once its job is done. Only the host samples and a fill reaches every peer as a command
    /// on a tick, so when a job finishes never touches sync. A chunk whose job is still under way is "not filled yet", as
    /// before (casts there are refused). <c>-chem-sync-fills</c> samples on the main thread as before, for A/B runs.
    /// </summary>
    public partial class ChemistryBridge
    {
        // The most ground jobs under way at once (the nearest chunks first): Punchy's own terrain jobs share the workers.
        const int MostGroundJobs = 16;
        // The most fills stamped a tick (the planner's own default).
        const int MostFillsPerTick = 16;
        // How long (s) a job's ground is kept for a chunk no reach wants (a teleport's destination, asked for early).
        const float GroundKeep = 10f;
        // A frame and the fills' main-thread share are watched this long (s) after one of the bridge's own teleports.
        const float TeleportWatch = 5f;

        sealed class GroundTask
        {
            public JobHandle Job;
            public NativeArray<byte> Kinds;
            public float Until;
        }

        readonly Dictionary<ChunkKey, GroundTask> groundTasks = new Dictionary<ChunkKey, GroundTask>();
        // The fills stamped and not run yet, by the tick they land on (as the planner keeps them).
        readonly Dictionary<ChunkKey, int> fillsStamped = new Dictionary<ChunkKey, int>();
        readonly bool syncFills = Array.IndexOf(Environment.GetCommandLineArgs(), "-chem-sync-fills") >= 0;
        readonly System.Diagnostics.Stopwatch fillWatch = new System.Diagnostics.Stopwatch();
        // The characters as this tick's stamping read them (the planner asks again, and must see the same).
        List<(string who, int layer, int x, int y)> charactersNow;
        int groundJobs;
        bool warnedNoJob;
        double fillMsFrame;
        // The worst frame and the worst frame's fill work (main thread) since the last sum line, in ms.
        double frameWorstMs, fillWorstMs;
        // The last of the bridge's own teleports: when, and the worst since.
        float teleportAt = -1f;
        double teleportFrameWorst, teleportFillWorst;
        int teleportSlowFrames;
        bool teleportCastSeen;

        /// <summary>
        /// Host and solo, each frame before the session ticks: starts the jobs for the chunks the reaches want (nearest
        /// first), lets go of what nobody wants any more, and keeps the frame's numbers.
        /// </summary>
        void FillFrame()
        {
            var dt = Time.unscaledDeltaTime * 1000.0;
            frameWorstMs = Math.Max(frameWorstMs, dt);
            fillWorstMs = Math.Max(fillWorstMs, fillMsFrame);
            WatchTeleport(dt);
            fillMsFrame = 0;
            if (syncFills || planner == null || world == null)
                return;
            fillWatch.Restart();
            StartGround(Wanted(world, Characters()), Time.unscaledTime + GroundKeep);
            var now = Time.unscaledTime;
            List<ChunkKey> done = null;
            foreach (var t in groundTasks)
                if (t.Value.Until < now && t.Value.Job.IsCompleted)
                    (done ??= new List<ChunkKey>()).Add(t.Key);
            if (done != null)
                foreach (var k in done)
                {
                    groundTasks[k].Job.Complete();
                    groundTasks[k].Kinds.Dispose();
                    groundTasks.Remove(k);
                }
            fillMsFrame += fillWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// The session's stamping (host and solo), as OpenWorldPlanner.Attach does it, but with the planner's fills this
        /// tick held to the nearest chunks whose jobs are done: the planner takes the unfilled chunks in its own order
        /// (nearest, then by key), so it's told to take just the ones in front that are ready.
        /// </summary>
        void StampOpen(int tick)
        {
            fillWatch.Restart();
            var w = session.World.World;
            int ran = session.NetTick;
            foreach (var k in fillsStamped.Where(f => f.Value <= ran).Select(f => f.Key).ToList())
                fillsStamped.Remove(k);
            charactersNow = Characters();
            if (syncFills)
                planner.FillsPerTick = MostFillsPerTick;
            else
            {
                var wanted = Wanted(w, charactersNow);
                StartGround(wanted, Time.unscaledTime + GroundKeep);
                int ready = 0;
                while (ready < wanted.Count && ready < MostFillsPerTick && groundTasks.TryGetValue(wanted[ready], out var t) && t.Job.IsCompleted)
                    ready++;
                planner.FillsPerTick = ready;
            }
            var plan = planner.Plan(w, ran, tick);
            charactersNow = null;
            foreach (var f in plan.Fills)
            {
                session.Stamp(fills, f);
                fillsStamped[f.Key] = tick;
            }
            foreach (var s in plan.Switches)
                session.Stamp(chunks, s);
            fillMsFrame += fillWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>The planner's sample: the chunk's job's ground (done by now), or sampled here if it has none.</summary>
        ChunkGround TakeGround(ChunkKey key)
        {
            if (!syncFills && groundTasks.TryGetValue(key, out var t))
            {
                groundTasks.Remove(key);
                sampleWatch.Restart();
                t.Job.Complete();
                var g = GroundOf(t.Kinds);
                t.Kinds.Dispose();
                sampled++;
                sampleMs += sampleWatch.Elapsed.TotalMilliseconds;
                return g;
            }
            if (!syncFills && !warnedNoJob)
            {
                warnedNoJob = true;
                Debug.LogWarning($"[Chemistry] lockstep: chunk {key} had no ground job; sampled on the main thread");
            }
            return SampleChunk(key);
        }

        /// <summary>
        /// The chunks a reach wants and that aren't filled or coming: within the rules' reach and a ring of each character,
        /// nearest first, then by key (the planner's own order).
        /// </summary>
        List<ChunkKey> Wanted(Chem.World w, List<(string who, int layer, int x, int y)> characters)
        {
            var around = new Dictionary<ChunkKey, int>();
            int distance = Math.Max(0, w.Rules.Sim.Streaming.Reach) + 1;
            foreach (var c in characters.Select(c => ChunkKey.OfCell(c.layer, c.x, c.y)).Distinct())
                Around(around, c, distance);
            return around.Where(a => !fillsStamped.ContainsKey(a.Key) && !w.IsFilled(a.Key))
                .OrderBy(a => a.Value).ThenBy(a => a.Key).Select(a => a.Key).ToList();
        }

        static void Around(Dictionary<ChunkKey, int> around, ChunkKey c, int distance)
        {
            for (int dy = -distance; dy <= distance; dy++)
                for (int dx = -distance; dx <= distance; dx++)
                {
                    var key = new ChunkKey(c.Layer, c.X + dx, c.Y + dy);
                    if (!key.Fits)
                        continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (!around.TryGetValue(key, out int was) || d < was)
                        around[key] = d;
                }
        }

        /// <summary>Starts a job for each chunk in order that has none, while fewer than MostGroundJobs are under way.</summary>
        void StartGround(List<ChunkKey> keys, float until)
        {
            int running = 0;
            foreach (var t in groundTasks.Values)
                if (!t.Job.IsCompleted)
                    running++;
            bool started = false;
            foreach (var key in keys)
            {
                if (groundTasks.TryGetValue(key, out var had))
                {
                    had.Until = Math.Max(had.Until, until);
                    continue;
                }
                if (running >= MostGroundJobs)
                    break;
                var kinds = new NativeArray<byte>(Chunks.Area, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                groundTasks[key] = new GroundTask { Job = GroundJobFor(key, kinds).Schedule(Chunks.Area, 256), Kinds = kinds, Until = until };
                running++;
                groundJobs++;
                started = true;
            }
            if (started)
                JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>
        /// Host and solo: one of the bridge's own teleports is about to put the local player at <paramref name="at"/>, so
        /// the chunks round it are asked for now (they're often filled on arrival). A client's own teleport isn't known
        /// to the host until the client's player has moved there.
        /// </summary>
        void AskAhead(float3 at)
        {
            if (syncFills || planner == null || world == null || !TerrainSettled)
                return;
            fillWatch.Restart();
            var c = ChunkKey.OfCell(LayerOf(terrainNow, at), ChunkKey.CellOf(at.x, 1), ChunkKey.CellOf(at.z, 1));
            var around = new Dictionary<ChunkKey, int>();
            Around(around, c, Math.Max(0, world.Rules.Sim.Streaming.Reach) + 1);
            var w = world;
            var keys = around.Where(a => !fillsStamped.ContainsKey(a.Key) && !w.IsFilled(a.Key))
                .OrderBy(a => a.Value).ThenBy(a => a.Key).Select(a => a.Key).ToList();
            StartGround(keys, Time.unscaledTime + GroundKeep);
            fillMsFrame += fillWatch.Elapsed.TotalMilliseconds;
            Debug.Log($"[Chemistry] lockstep: asked ahead for {keys.Count} chunks round ({at.x:F0}, {at.z:F0}) in layer {c.Layer}");
        }

        /// <summary>Waits for every job under way (before the height edits a job reads are written again).</summary>
        void CompleteGround()
        {
            foreach (var t in groundTasks.Values)
                t.Job.Complete();
        }

        /// <summary>Drops every job and what it sampled (the session goes, or the terrain changed).</summary>
        void CancelGround()
        {
            foreach (var t in groundTasks.Values)
            {
                t.Job.Complete();
                t.Kinds.Dispose();
            }
            groundTasks.Clear();
            fillsStamped.Clear();
        }

        // ------------------------------------------------------------------------------------------------ the numbers

        /// <summary>One of the bridge's own teleports: asks ahead, then watches the frames for TeleportWatch s.</summary>
        void Teleporting(float3 to)
        {
            AskAhead(to);
            teleportAt = lastTeleport = Time.unscaledTime;
            teleportFrameWorst = teleportFillWorst = 0;
            teleportSlowFrames = 0;
            teleportCastSeen = false;
        }

        void WatchTeleport(double frameMs)
        {
            if (teleportAt < 0f)
                return;
            // (The frame the teleport was made in is before it: its own time doesn't count.)
            if (Time.unscaledTime > teleportAt)
            {
                teleportFrameWorst = Math.Max(teleportFrameWorst, frameMs);
                teleportFillWorst = Math.Max(teleportFillWorst, fillMsFrame);
                if (frameMs > 1000.0 / 60.0)
                    teleportSlowFrames++;
            }
            if (Time.unscaledTime - teleportAt < TeleportWatch)
                return;
            Debug.Log($"[Chemistry] script: after the teleport: worst frame {teleportFrameWorst:F1} ms, {teleportSlowFrames} frames over 16.7 ms, " +
                      $"fills' main-thread worst {teleportFillWorst:F2} ms a frame{(syncFills ? " (sync fills)" : "")}");
            teleportAt = -1f;
        }

        // A cast of this peer's wizard landing: how long after the bridge's own last teleport, the first time.
        string SinceTeleport(string by)
        {
            if (teleportCastSeen || by != Me || lastTeleport < 0f || Time.unscaledTime - lastTeleport > 30f)
                return "";
            teleportCastSeen = true;
            return $" ({Time.unscaledTime - lastTeleport:F2} s after the teleport)";
        }

        float lastTeleport = -1f;

        // ------------------------------------------------------------------------------------------------ the check

        /// <summary>
        /// -chem-fillcheck: the jobs' ground against the main thread's, cell for cell, on chunks round the player in both
        /// layers (half each), and what a chunk costs: the jobs on the workers, one Burst job on one thread, the same code
        /// managed (Mono), and the ground's making from the kinds. Logs one line; a long frame, once.
        /// </summary>
        void FillCheck(float3 player, int count)
        {
            var keys = new List<ChunkKey>();
            int side = (int)math.ceil(math.sqrt(count / 2f));
            int cx = Chunks.ChunkOf((int)math.floor(player.x)) - side / 2, cy = Chunks.ChunkOf((int)math.floor(player.z)) - side / 2;
            for (int layer = SurfaceLayer; layer <= CaveLayer; layer++)
                for (int k = 0; k < side * side && keys.Count < (layer + 1) * count / 2; k++)
                    keys.Add(new ChunkKey(layer, cx + k % side, cy + k / side));
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
            // The jobs, all scheduled at once, run on the workers while the main thread samples the same chunks itself.
            var async = new NativeArray<byte>[keys.Count];
            var handles = new NativeArray<JobHandle>(keys.Count, Allocator.Temp);
            for (int k = 0; k < keys.Count; k++)
            {
                async[k] = new NativeArray<byte>(Chunks.Area, Allocator.TempJob);
                handles[k] = GroundJobFor(keys[k], async[k]).Schedule(Chunks.Area, 256);
            }
            JobHandle.ScheduleBatchedJobs();
            int differ = 0, differCave = 0, chunksDiffer = 0;
            var kinds = new NativeArray<byte>(Chunks.Area, Allocator.TempJob);
            double syncMs = 0, oneThreadMs = 0, managedMs = 0, groundMs = 0;
            int managedChunks = 0, managedDiffer = 0;
            int[] counts = new int[8];
            for (int k = 0; k < keys.Count; k++)
            {
                watch2.Restart();
                GroundJobFor(keys[k], kinds).Schedule(Chunks.Area, 256).Complete();
                syncMs += watch2.Elapsed.TotalMilliseconds;
                handles[k].Complete();
                int d = 0;
                for (int i = 0; i < Chunks.Area; i++)
                {
                    counts[kinds[i] & 7]++;
                    if (kinds[i] != async[k][i])
                        d++;
                }
                differ += d;
                if (keys[k].Layer != SurfaceLayer)
                    differCave += d;
                if (d > 0)
                    chunksDiffer++;
                watch2.Restart();
                GroundOf(async[k]);
                groundMs += watch2.Elapsed.TotalMilliseconds;
                if (k % 25 == 0)
                {
                    // One thread, Burst (Run), and the same code managed: the cost of the work against the cost of the calls.
                    var one = new NativeArray<byte>(Chunks.Area, Allocator.TempJob);
                    watch2.Restart();
                    GroundJobFor(keys[k], one).Run(Chunks.Area);
                    oneThreadMs += watch2.Elapsed.TotalMilliseconds;
                    var managed = GroundJobFor(keys[k], one);
                    watch2.Restart();
                    for (int i = 0; i < Chunks.Area; i++)
                        managed.Execute(i);
                    managedMs += watch2.Elapsed.TotalMilliseconds;
                    for (int i = 0; i < Chunks.Area; i++)
                        if (one[i] != kinds[i])
                            managedDiffer++;
                    managedChunks++;
                    one.Dispose();
                }
                async[k].Dispose();
            }
            kinds.Dispose();
            handles.Dispose();
            Debug.Log($"[Chemistry] fillcheck: {keys.Count} chunks ({keys.Count / 2} in layer 0, {keys.Count - keys.Count / 2} in layer 1), " +
                      $"{differ} cells differ between the jobs and the main thread ({differCave} in layer 1, {chunksDiffer} chunks); " +
                      $"kinds dirt {counts[KindDirt]} grass {counts[KindGrass]} water {counts[KindWater]} rock {counts[KindRock]}; " +
                      $"a chunk: {syncMs / keys.Count:F2} ms scheduled and waited for (the old way), {oneThreadMs / managedChunks:F2} ms on one thread in Burst, " +
                      $"{managedMs / managedChunks:F1} ms managed ({managedDiffer} cells differ from Burst over {managedChunks} chunks), " +
                      $"{groundMs / keys.Count:F3} ms making the ground from the kinds; {Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount} workers");
        }
    }
}
