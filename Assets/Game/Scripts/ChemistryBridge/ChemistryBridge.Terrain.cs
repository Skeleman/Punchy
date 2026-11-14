using System;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Punchy.Chemistry
{
    /// <summary>
    /// Height changes (README "Height changes"): the core's cells have a height in half-metre steps that spells dig
    /// (craters) and raise (Raise Earth, Move Earth, Wall of Stone). The cells keep the steps their chunk was filled with
    /// (0 on land, -2 under water: the overlay doesn't model Punchy's slopes), so a cell's change is its steps now less
    /// those, times 0.5 m, laid over Punchy's own ground through TerrainEdits (one hook in TerrainSource's samples). The
    /// edits are the view's window's, on the surface only: walking on re-lays them under the new window (from the cells,
    /// which keep their heights), and in a cave the surface above stays as it is.
    /// Every height in the game follows: the player walks into a crater, the camera and grass sit in it. The ground's
    /// chunks, grass tiles and (for a metre or more) the plants on what moved are rebuilt by Punchy's own systems.
    /// </summary>
    public partial class ChemistryBridge
    {
        // At most one swap of the edits this often (s): the buffer a swap fills was last read a swap before.
        const float PublishEvery = 0.25f;
        // A change this big (m) re-places the plants on it (FloraSystem.FlagRebuild); smaller ones leave them.
        const float FloraMove = 1f;

        int[] seededSteps;      // each cell's height (steps) as seeded
        int[] shownSteps;       // as last laid over the ground
        float[] deltas;         // metres, (shown - seeded) * 0.5
        readonly UnsafeList<float>[] buffers = new UnsafeList<float>[2];
        int front;
        bool landDirty;
        int2 dirtyMin, dirtyMax;    // cells changed since the last swap
        float dirtyMove;            // the most one of them moved (m)
        int2 everMin, everMax;      // every cell changed in this window (to put back when it's rebuilt)
        bool everMoved;
        float nextPublish;
        int movedCells;
        double landMsThisSecond;

        /// <summary>Cells whose height differs from what they were seeded with.</summary>
        public int MovedCells => movedCells;
        /// <summary>The edits' version, bumped each swap (0: none laid yet).</summary>
        public int EditsVersion => TerrainEditLayer.Current.Version;

        /// <summary>Takes the window's edits off the ground (before it's seeded again, and when the bridge goes).</summary>
        void ForgetLand()
        {
            ref var e = ref TerrainEditLayer.Current;
            e.On = false;
            if (everMoved)
                FlagRects(everMin, everMax, true);
            everMoved = landDirty = false;
        }

        // A new view: no cell's ground known yet (SeedLand gives each as its chunk turns up filled).
        void ResetLand()
        {
            seededSteps ??= new int[Size * Size];
            shownSteps ??= new int[Size * Size];
            deltas ??= new float[Size * Size];
            Array.Clear(seededSteps, 0, seededSteps.Length);
            Array.Clear(shownSteps, 0, shownSteps.Length);
            Array.Clear(deltas, 0, deltas.Length);
            movedCells = 0;
        }

        // A window cell's ground height (steps) as its chunk was filled (0 on land, -2 under water).
        void SeedLand(int i, int steps) => seededSteps[i] = shownSteps[i] = steps;

        /// <summary>One cell, from the map's walk: notes a change of height since it was last laid over the ground.</summary>
        void NoteLand(int x, int y, int i, int steps)
        {
            if (steps == shownSteps[i])
                return;
            var was = shownSteps[i];
            if (was == seededSteps[i])
                movedCells++;
            else if (steps == seededSteps[i])
                movedCells--;
            var move = (steps - was) * 0.5f;
            shownSteps[i] = steps;
            deltas[i] = (steps - seededSteps[i]) * 0.5f;
            if (seeded[i] != KindWater)
                heights[i] += move;   // the flames and puffs sit on the new ground
            var c = new int2(x, y);
            dirtyMin = landDirty ? math.min(dirtyMin, c) : c;
            dirtyMax = landDirty ? math.max(dirtyMax, c) : c;
            dirtyMove = landDirty ? math.max(dirtyMove, math.abs(move)) : math.abs(move);
            landDirty = true;
        }

        /// <summary>Lays the changed heights over the ground (a fresh buffer, swapped in) and flags what to rebuild.</summary>
        void PublishLand()
        {
            if (!landDirty || Time.unscaledTime < nextPublish)
                return;
            watch.Restart();
            CompleteGround();   // (a ground job may read the edits: none is under way while they change)
            var k = 1 - front;
            if (!buffers[k].IsCreated)
                buffers[k] = new UnsafeList<float>(Size * Size, Allocator.Persistent);
            buffers[k].CopyFrom(new ReadOnlySpan<float>(deltas));
            ref var e = ref TerrainEditLayer.Current;
            e.Deltas = buffers[k];
            e.Origin = corner;
            e.Size = new int2(Size, Size);
            e.Version++;
            e.On = true;
            front = k;
            FlagRects(dirtyMin, dirtyMax, dirtyMove >= FloraMove);
            everMin = everMoved ? math.min(everMin, dirtyMin) : dirtyMin;
            everMax = everMoved ? math.max(everMax, dirtyMax) : dirtyMax;
            everMoved = true;
            landDirty = false;
            nextPublish = Time.unscaledTime + PublishEvery;
            landMsThisSecond += watch.Elapsed.TotalMilliseconds;
            Debug.Log($"[Chemistry] ground moved: cells ({dirtyMin.x}, {dirtyMin.y}) to ({dirtyMax.x}, {dirtyMax.y}), up to {dirtyMove:F1} m; {movedCells} cells off their seeded height (edits v{e.Version})");
        }

        // A sample within a metre of a changed cell's centre moves (bilinear between centres).
        void FlagRects(int2 cellMin, int2 cellMax, bool flora)
        {
            var min = (float2)(corner + cellMin) - 1f;
            var max = (float2)(corner + cellMax) + 2f;
            TerrainStreamingSystem.FlagRebuild(min, max);
            GroundDetailSystem.FlagRebuild(min, max);
            if (flora)
                Punchy.Flora.FloraSystem.FlagRebuild(min, max);
        }

        void DisposeLand()
        {
            CompleteGround();
            TerrainEditLayer.Current = default;
            for (int k = 0; k < buffers.Length; k++)
                if (buffers[k].IsCreated)
                {
                    buffers[k].Dispose();
                    buffers[k] = default;
                }
        }

        // ------------------------------------------------------------------------------------------------ the check

        /// <summary>
        /// Samples TerrainSource (SampleHeight and SampleGround) on the window's grid, at cell centres and at a point inside
        /// each cell, with the edits as they are and with them off, in a Burst job like the game's. Returns how many samples
        /// there were, how many differ in any bit, and how many of those differ where the edits are 0 (none should).
        /// With nothing laid yet it lays the window's (all-zero) edits first, so "on" is really on.
        /// </summary>
        public (int samples, int differ, int differOutside) HeightCheck()
        {
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain) || !Ready)
                return (0, 0, 0);
            CompleteGround();
            ref var e = ref TerrainEditLayer.Current;
            var saved = e;
            if (!e.On)
            {
                var k = 1 - front;
                if (!buffers[k].IsCreated)
                    buffers[k] = new UnsafeList<float>(Size * Size, Allocator.Persistent);
                buffers[k].CopyFrom(new ReadOnlySpan<float>(deltas));
                e.Deltas = buffers[k];
                e.Origin = corner;
                e.Size = new int2(Size, Size);
                e.On = true;
            }
            var n = Size * Size * 2;
            var on = new NativeArray<float2>(n, Allocator.TempJob);
            var off = new NativeArray<float2>(n, Allocator.TempJob);
            new CheckJob { Source = terrain, Corner = corner, Heights = on }.Schedule(n, 256).Complete();
            var lit = e;
            e.On = false;
            new CheckJob { Source = terrain, Corner = corner, Heights = off }.Schedule(n, 256).Complete();
            e = saved;
            int differ = 0, outside = 0;
            for (int i = 0; i < n; i++)
            {
                bool same = math.asuint(on[i].x) == math.asuint(off[i].x) && math.asuint(on[i].y) == math.asuint(off[i].y);
                if (same)
                    continue;
                differ++;
                if (lit.At(Point(corner, i)) == 0f)
                    outside++;
            }
            on.Dispose();
            off.Dispose();
            Debug.Log($"[Chemistry] height check: {n} samples, {differ} differ with the edits on, {outside} of them where the edits are 0; {movedCells} cells moved");
            return (n, differ, outside);
        }

        // Cell centres, then a point off-centre in each cell.
        static float2 Point(int2 corner, int i)
        {
            var j = i % (Size * Size);
            var f = i < Size * Size ? new float2(0.5f) : new float2(0.137f, 0.781f);
            return new float2(corner.x + j % Size, corner.y + j / Size) + f;
        }

        [BurstCompile]
        struct CheckJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int2 Corner;
            [WriteOnly] public NativeArray<float2> Heights;

            public void Execute(int i)
            {
                var xz = Point(Corner, i);
                Heights[i] = new float2(Source.SampleHeight(xz), Source.SampleGround(xz).Height);
            }
        }
    }
}
