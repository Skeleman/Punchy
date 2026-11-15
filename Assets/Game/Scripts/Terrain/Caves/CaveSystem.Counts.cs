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

        /// <summary>
        /// The end of Update, with the running clock at each step: names the slow parts of any frame over a quarter
        /// second (startup hitch hunting, #102), and while the world loads adds the streaming and the whole frame to
        /// the start ledger (W-168).
        /// </summary>
        static void NoteFrame(double[] marks)
        {
            if (marks[7] > 250)
                Debug.Log($"[caves] slow frame {marks[7]:F0} ms: pools+glow {marks[0]:F0}, styles {marks[1] - marks[0]:F0}, stream {marks[2] - marks[1]:F0}, draw+pools {marks[3] - marks[2]:F0}, bat folk {marks[4] - marks[3]:F0}, plants {marks[5] - marks[4]:F0}, drips {marks[6] - marks[5]:F0}, glowing {marks[7] - marks[6]:F0}");
            if (!TerrainStreamingSystem.Loading || !StartLedger.Open)
                return;
            StartLedger.Add("caves stream", marks[2] - marks[1], Counts.NewBuilds + Counts.LevelBuilds + Counts.ContactBuilds);
            StartLedger.Add("caves uploads (in stream)", 0, Counts.Uploads);
            StartLedger.Add("caves rest of frame", marks[7] - (marks[2] - marks[1]), Counts.RoomKits);
        }
    }
}
