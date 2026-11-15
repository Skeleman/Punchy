using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-114 (#314): where a folk's hand goes to put an unsheathed item away or take it out. Picked by the item and the
    /// body: a letter into the chest of the coat; small things into a hip pocket, a belt pouch for those in wraps with no
    /// pockets (sea folk), or into the fold of a robe (turtle folk); big things over the shoulder onto the back or into a
    /// pack, else, with the back taken by wings or a shell and no pack, just held lower down by the side.
    /// </summary>
    public enum PocketPlace : byte { None, Hip, Pouch, Chest, Shoulder, Low }

    /// <summary>
    /// A-114: one arm's (or both arms') reach to a PocketPlace for CreatureAnimator.Evaluate. Amount 0 is no reach, 1 the
    /// hand at the place (where the item shows or hides). Side: +1 the right arm, -1 the left, 0 both.
    /// </summary>
    public struct PocketReach
    {
        public float Amount;
        public sbyte Side;
        public PocketPlace Place;
    }

    // A-114 (#314): putting an item away and taking one out, drawn only. Each machine sees CreatureState.Held change (the
    // brain sets it; it already replicates) and plays the reach from that, so nothing new is sent.
    public partial class CreatureRenderSystem
    {
        /// <summary>How long one reach takes, out to the place and back (s): the item shows or hides half way.</summary>
        const float PocketTime = 0.6f;

        /// <summary>
        /// A-114 shots: when 0 or more, every move under way is held this many seconds after its start (from 0 to
        /// 2 x PocketTime), so a strip can be shot frame by frame. Set from eval; -1 (off) in play.
        /// </summary>
        public static float PocketScrub = -1f;

        /// <summary>A-114 checks: how many moves have started on this machine (a joined client logs its own).</summary>
        public static int PocketStarts;

        /// <summary>A-114 checks: -pocketlog logs each frame's new moves (a player build has no eval).</summary>
        static bool PocketLog;   // (set in CreatePockets: no Unity calls in a static initialiser Burst might run)

        /// <summary>The time a move is sampled at: now, or its start plus PocketScrub while scrubbing.</summary>
        static float PocketClock(in PocketMove move, float time, float scrub) => scrub >= 0f ? move.Start + scrub : time;

        /// <summary>
        /// A-114: an item being put away (Old) and another taken out (New), started at Start. The out reach comes first
        /// (when Old is pocketable), then the in reach (when New is). Places are picked once, at the start.
        /// </summary>
        public struct PocketMove
        {
            public HeldItem Old, New;
            public float Start;
            public PocketPlace OldPlace, NewPlace;
        }

        /// <summary>A-114: the item each upright body with hands had when the render last kept it, and that frame.</summary>
        public struct PocketSeen
        {
            public HeldItem Held;
            public int Frame;
        }

        NativeParallelHashMap<Entity, PocketSeen> pocketSeen;
        NativeParallelHashMap<Entity, PocketMove> pocketMoves;
        NativeArray<int> pocketStarted;

        void CreatePockets()
        {
            pocketSeen = new NativeParallelHashMap<Entity, PocketSeen>(256, Allocator.Persistent);
            pocketMoves = new NativeParallelHashMap<Entity, PocketMove>(16, Allocator.Persistent);
            pocketStarted = new NativeArray<int>(1, Allocator.Persistent);
            PocketLog = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-pocketlog") >= 0;
        }

        void DisposePockets()
        {
            pocketSeen.Dispose();
            pocketMoves.Dispose();
            pocketStarted.Dispose();
        }

        /// <summary>
        /// Forgets bodies not kept this frame (despawned, or out of view): an entry only counts when it's from the frame
        /// before (TrackPocket), and one coming back is written afresh, so dropping them changes nothing. A move whose
        /// body is gone is dropped once it would be long over (never while scrubbing a strip). After CompactJob is done.
        /// </summary>
        void PrunePockets(int frame, float time)
        {
            if (!pocketSeen.IsEmpty)
            {
                var seen = pocketSeen.GetKeyValueArrays(Allocator.Temp);
                for (int i = 0; i < seen.Length; i++)
                    if (seen.Values[i].Frame < frame)
                        pocketSeen.Remove(seen.Keys[i]);
                seen.Dispose();
            }
            if (PocketScrub >= 0f || pocketMoves.IsEmpty)
                return;
            var moves = pocketMoves.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < moves.Length; i++)
                if (time - moves.Values[i].Start > 4f * PocketTime)
                    pocketMoves.Remove(moves.Keys[i]);
            moves.Dispose();
        }

        /// <summary>
        /// Items put away by hand. Sheathed ones (a spear or pick on the back or belt) keep their own Draw and Sheathe;
        /// honey and goggles aren't held in a hand.
        /// </summary>
        public static bool Pocketable(HeldItem item) =>
            item == HeldItem.Lantern || item == HeldItem.Torch || item == HeldItem.Stick || item == HeldItem.Stone ||
            item == HeldItem.Letter || item == HeldItem.Staff || item == HeldItem.Rod || item == HeldItem.OreSack;

        /// <summary>Which hand holds it (PoseGear and PoseGuard draw it there): a lantern the left, a letter or a
        /// sack both, the rest the right.</summary>
        public static sbyte PocketSide(HeldItem item) =>
            item == HeldItem.Lantern ? (sbyte)-1 : item == HeldItem.Letter || item == HeldItem.OreSack ? (sbyte)0 : (sbyte)1;

        /// <summary>Where this body puts this item (PocketPlace).</summary>
        public static PocketPlace PlaceFor(HeldItem item, ref CreatureBlob b, in CreatureState s)
        {
            if (item == HeldItem.Letter)
                return PocketPlace.Chest;
            var big = item == HeldItem.Staff || item == HeldItem.Rod || item == HeldItem.OreSack;
            if (!big)
                return b.TurtleJobs ? PocketPlace.Chest : b.BreathesWater ? PocketPlace.Pouch : PocketPlace.Hip;
            if ((s.Worn & CreatureWorn.Pack) != 0 || s.Task == CreatureTask.Travel)
                return PocketPlace.Shoulder;
            // (A turtle's shell or a bat's wings on its back: nowhere to sling it.)
            var backTaken = b.TurtleJobs;
            for (int i = 0; i < b.Parts.Length && !backTaken; i++)
                backTaken = b.Parts[i].Role == LimbRole.Wing;
            return backTaken ? PocketPlace.Low : PocketPlace.Shoulder;
        }

        /// <summary>
        /// A-114: the move at this time: the item shown in hand, and the arm's reach (Amount 0 once it's over, when
        /// done is set).
        /// </summary>
        public static PocketReach SamplePocket(in PocketMove move, float time, out HeldItem shown, out bool done)
        {
            var outTime = Pocketable(move.Old) ? PocketTime : 0f;
            var inTime = Pocketable(move.New) ? PocketTime : 0f;
            var e = time - move.Start;
            done = e >= outTime + inTime || e < 0f;
            shown = move.New;
            if (done)
                return default;
            if (e < outTime)
            {
                var t = e / outTime;
                shown = t < 0.5f ? move.Old : inTime > 0f ? HeldItem.None : move.New;
                return new PocketReach { Amount = math.sin(t * math.PI), Side = PocketSide(move.Old), Place = move.OldPlace };
            }
            var u = (e - outTime) / inTime;
            shown = u >= 0.5f ? move.New : outTime > 0f ? HeldItem.None : move.Old;
            return new PocketReach { Amount = math.sin(u * math.PI), Side = PocketSide(move.New), Place = move.NewPlace };
        }

        /// <summary>
        /// A-114, in CompactJob: an upright body with hands whose item changed since last frame (and that was kept then
        /// too, so one coming back into view doesn't play a stale change) starts a move; one under way shows the item
        /// it has in hand at this point of it (state.Held, for its parts count and its drawing alike).
        /// </summary>
        static void TrackPocket(ref CreatureState state, ref CreatureBlob b, Entity entity, float time, float scrub, int frame,
                                NativeParallelHashMap<Entity, PocketSeen> seen, NativeParallelHashMap<Entity, PocketMove> moves,
                                ref int started)
        {
            if (!b.Upright || !b.Hands)
                return;
            var held = state.Held;
            if (seen.TryGetValue(entity, out var was) && was.Frame == frame - 1 && was.Held != held &&
                (Pocketable(was.Held) || Pocketable(held)))
            {
                // (A change mid-move puts away whatever was last meant to be in hand.)
                moves[entity] = new PocketMove
                {
                    Old = was.Held, New = held, Start = time,
                    OldPlace = PlaceFor(was.Held, ref b, state), NewPlace = PlaceFor(held, ref b, state)
                };
                started++;
            }
            seen[entity] = new PocketSeen { Held = held, Frame = frame };
            if (!moves.TryGetValue(entity, out var move))
                return;
            SamplePocket(move, PocketClock(move, time, scrub), out var shown, out var done);
            if (done)
                moves.Remove(entity);
            else
                state.Held = shown;
        }
    }
}
