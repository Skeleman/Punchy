namespace Punchy.Terrain
{
    // T-1: what the shot tool's ready gate asks the terrain before it takes a picture. Read only.
    public partial class TerrainStreamingSystem
    {
        /// <summary>
        /// True once every chunk in range of the focus is built at its level of detail: nothing wanted and no batch
        /// meshing. (Deep in a cave nothing streams, so it is idle too.)
        /// </summary>
        public bool ShotIdle => building.Count == 0 && (wanted.Count == 0 || CaveSystem.SurfaceHidden >= 0.99f);

        /// <summary>Chunks still wanted or meshing, for the gate's log line.</summary>
        public int ShotPending => wanted.Count + building.Count;
    }
}
