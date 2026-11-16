namespace Punchy.Terrain
{
    // T-1: what the shot tool's ready gate asks the terrain before it takes a picture (read only), and W-168's first-view world ready.
    public partial class TerrainStreamingSystem
    {
        /// <summary>
        /// True once every chunk in range of the focus is built at its level of detail: nothing wanted and no batch
        /// meshing, far tiles included (W-131). (Deep in a cave nothing streams, so it is idle too.)
        /// </summary>
        public bool ShotIdle => building.Count == 0 && ((wanted.Count == 0 && !FarPending) || CaveSystem.SurfaceHidden >= 0.99f);

        /// <summary>
        /// W-151 row 2: how many terrain chunks within <paramref name="radius"/> of this point have no mesh yet (any level
        /// of detail counts: a chunk waiting only for a finer mesh already shows ground). 0 deep in a cave, where nothing
        /// on the surface streams. The load veil lifts on this; read only.
        /// </summary>
        public int MissingNear(Unity.Mathematics.float2 at, float radius)
        {
            if (CaveSystem.SurfaceHidden >= 0.99f || !initialized)
                return initialized ? 0 : 1;
            var size = lastConfig.ChunkSize;
            if (size <= 0f)
                return 1;
            var r = (int)Unity.Mathematics.math.ceil(radius / size) + 1;
            var centre = (Unity.Mathematics.int2)Unity.Mathematics.math.floor(at / size);
            var missing = 0;
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                var c = centre + new Unity.Mathematics.int2(x, z);
                var min = (Unity.Mathematics.float2)c * size;
                if (Unity.Mathematics.math.distance(Unity.Mathematics.math.clamp(at, min, min + size), at) < radius && !loaded.ContainsKey(c))
                    missing++;
            }
            return missing;
        }

        /// <summary>Chunks still wanted or meshing, for the gate's log line.</summary>
        public int ShotPending => wanted.Count + building.Count + farWanted.Count + farBuilding.Count;

        // W-168 row 2: the world is ready once the first view is built, not the whole disc. Chunks the camera can see
        // (and a ring round the focus, for a turn of the head) come first and at their final level of detail, so the
        // rest streams in behind with no hole and no pop; the load boost carries on until every chunk is in, smaller.

        /// <summary>True once the first view is built (the world-ready line); re-armed when a new island bakes.</summary>
        bool viewReady;
        /// <summary>The load boost once the first view is in: the player is looking, so each batch's apply stays
        /// small (BENCH job 87 sizes it; provisional).</summary>
        const int LoadBoostBehind = 2;
        /// <summary>Chunks round the focus counted in the first view whether or not the camera faces them.</summary>
        const float NearRingChunks = 2f;
        /// <summary>The height span a chunk or far tile is tested over (m): generous, so a ridge is never missed.</summary>
        const float ViewLow = -200f, ViewHigh = 2500f;
        static readonly UnityEngine.Plane[] viewPlanes = new UnityEngine.Plane[6];
        bool haveView;

        /// <summary>Takes the main camera's frustum for this frame; false (no first-view rule) without a camera.</summary>
        bool TakeView(Unity.Mathematics.float2 focus, float chunkSize)
        {
            var cam = UnityEngine.Camera.main;
            // A camera not yet at the focus (the first frames) can't say what the first view is.
            haveView = cam != null && Unity.Mathematics.math.distance(((Unity.Mathematics.float3)cam.transform.position).xz, focus) < chunkSize * 3f;
            if (haveView)
                UnityEngine.GeometryUtility.CalculateFrustumPlanes(cam, viewPlanes);
            return haveView;
        }

        /// <summary>True when the square [min, min + size] on the ground is in the first view.</summary>
        bool InFirstView(Unity.Mathematics.float2 min, float size, Unity.Mathematics.float2 focus, float chunkSize)
        {
            var near = Unity.Mathematics.math.distance(Unity.Mathematics.math.clamp(focus, min, min + size), focus) <= NearRingChunks * chunkSize;
            if (near || !haveView)
                return true;
            var bounds = new UnityEngine.Bounds();
            bounds.SetMinMax(new UnityEngine.Vector3(min.x, ViewLow, min.y), new UnityEngine.Vector3(min.x + size, ViewHigh, min.y + size));
            return UnityEngine.GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
        }

        /// <summary>Wanted chunks in the first view sort ahead of the rest while it is still loading.</summary>
        void FirstViewFirst(in TerrainConfig config, Unity.Mathematics.float2 focus)
        {
            if (viewReady || !haveView)
                return;
            for (int i = 0; i < wanted.Count; i++)
            {
                var w = wanted[i];
                if (!InFirstView((Unity.Mathematics.float2)w.Coord * config.ChunkSize, config.ChunkSize, focus, config.ChunkSize))
                {
                    w.Distance += 1e6f;
                    wanted[i] = w;
                }
            }
        }

        bool FarInView(System.Collections.Generic.List<FarItem> tiles, Unity.Mathematics.float2 focus, float size)
        {
            foreach (var f in tiles)
            {
                var span = (FarTileChunks << f.Tile.z) * size;
                if (InFirstView((Unity.Mathematics.float2)f.Tile.xy * span, span, focus, size))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Logs the world-ready line (and the start ledger) the first time nothing in the first view is wanted or
        /// meshing, far tiles included. Without a camera it waits for every chunk, as before.
        /// </summary>
        void CheckFirstView(in TerrainConfig config, Unity.Mathematics.float2 focus)
        {
            if (viewReady || !Loading || Punchy.Terrain.IslandV2.ActiveVersion == 0 || building.Count > 0 || loaded.Count == 0)
                return;
            var size = config.ChunkSize;
            var all = wanted.Count == 0 && !FarPending;
            if (!all)
            {
                if (!haveView)
                    return;
                foreach (var w in wanted)
                    if (InFirstView((Unity.Mathematics.float2)w.Coord * size, size, focus, size))
                        return;
                if (FarInView(farWanted, focus, size) || FarInView(farBuilding, focus, size))
                    return;
            }
            viewReady = true;
            UnityEngine.Debug.Log($"[stream] world ready at {UnityEngine.Time.realtimeSinceStartup:F1} s since start, {loaded.Count} chunks, "
                + $"{wanted.Count} more behind the first view (load boost {(LoadBoostOn ? LoadBoost : 1)}x, {World.Name})");
            StartLedger.Report(loaded.Count, World.Name);
        }
    }
}
