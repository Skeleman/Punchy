using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-135 row 6: what the cave streaming did each frame, counted (never timed, rule 21) so a hitch can be named by
    /// what landed in it: chunks started (new, at another wall level, for a changed contact list), meshes uploaded and
    /// their vertices, rebuilds swapped in, surface-skip flips, room kits and tile meshes made, contact re-sorts.
    /// A handful of int increments a frame; a probe or test reads them through FrameCounted.
    /// </summary>
    public partial class CaveSystem
    {
        public struct FrameCounts
        {
            public int Frame;
            public int NewBuilds, LevelBuilds, ContactBuilds;
            /// <summary>Of LevelBuilds: to a finer level, to a coarser one, and of chunks with no mesh (built finer in
            /// case a coarse grid missed a sliver of wall).</summary>
            public int Finer, Coarser, EmptyRebuilds;
            /// <summary>Field points the builds started this frame sample (the work a build hands the job workers).</summary>
            public int BuildPoints;
            public int Uploads, UploadVertices, Swaps;
            public int SkipFlips;
            public int RoomKits, RoomTiles, Sorts;
            /// <summary>Builds in flight and chunks still waiting to start, at the end of streaming.</summary>
            public int Building, Wanted;
            /// <summary>Rebuilds (another level or contact list) in flight at the end of streaming.</summary>
            public int Rebuilding;
            /// <summary>W-135 row 6b: field points of the builds in flight once streaming has started this frame's.</summary>
            public int FlightPoints;
        }

        /// <summary>This frame's counts so far.</summary>
        public static FrameCounts Counts;
        /// <summary>Called with each frame's counts when the next frame's streaming starts.</summary>
        public static System.Action<FrameCounts> FrameCounted;

        static void BeginFrameCounts()
        {
            FrameCounted?.Invoke(Counts);
            Counts = new FrameCounts { Frame = Time.frameCount };
        }
    }
}
