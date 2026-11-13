namespace Punchy.Terrain
{
    // T-1: what the shot tool's ready gate asks the terrain before it takes a picture. Read only.
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
    }
}
