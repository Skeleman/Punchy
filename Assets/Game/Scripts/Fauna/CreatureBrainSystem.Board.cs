using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: the town's notice board in the townsfolk's day (A-116, #316). Readers stand before it a while
        // (Sit mode at a standing stage, as at the fire), scanning the sheets; with company, one now and then points a
        // notice out and reads it aloud; and now and then one walks over with a new sheet and pins it up (row 2). The
        // pose is the animator's, from the stage, on every machine. Split by area
        // (#61); the system, Execute and Think live in CreatureBrainSystem.cs.
        partial struct BrainJob
        {
            /// <summary>Whether this one could stop at the board now: grown townsfolk (not the watch, the guards or a
            /// traveller), awake, by day.</summary>
            bool BoardTime(in CreatureState s, ref CreatureBlob b, float sleepy) =>
                b.Upright && b.Hands && !b.Child && s.Leash > 0f && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Watch &&
                s.Task != CreatureTask.Travel && sleepy < 0.3f && Night < 0.3f;

            /// <summary>The chance, at one Decide, that it goes to the board: most in the morning, when the day's
            /// notices are new.</summary>
            float BoardChance => Hour >= 7f && Hour < 10.5f ? 0.25f : 0.07f;

            /// <summary>
            /// A place before its town's notice board taken (one of NoticeBoard.Places, by individual and the next along
            /// if that's taken), standing facing it a while. With another reader already there it may be the one who
            /// points a notice out (BoardPoint). False with no town, the board out of its reach, or no room.
            /// </summary>
            bool StartBoard(Entity self, ref CreatureState s, ref Random rng)
            {
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return false;
                var board = NoticeBoard.At(Terrain.Settlements.Value.Settlements[town].Centre);
                if (!InReach(in s, board))
                    return false;
                var first = (int)(s.Individual % NoticeBoard.Places);
                var place = -1;
                var company = false;
                for (int i = 0; i < NoticeBoard.Places; i++)
                {
                    var k = (first + i) % NoticeBoard.Places;
                    var spot = NoticeBoard.Place(board, k);
                    if (Taken(self, spot, 0.5f))
                        company = true;
                    else if (place < 0 && Walkable(spot, Habitat.Land))
                        place = k;
                }
                if (place < 0)
                    return false;
                SetMode(ref s, CreatureMode.Sit);
                s.Target = NoticeBoard.Place(board, place);
                // (Square on to the board, not all turned to its middle.)
                s.Focus = s.Target + new float2(0f, 4f);
                s.TargetHeight = company && rng.NextFloat() < 0.5f ? CreatureAnimator.BoardPoint : CreatureAnimator.BoardRead;
                s.Timer = rng.NextFloat(10f, 25f);
                return true;
            }

            /// <summary>
            /// The poster's errand (row 2): with a sheet of its town's due up within the hour (NoticeBoard.Due, from the
            /// shared day and clock) and nobody at the spot before that sheet's slot, it walks over with the sheet in hand
            /// (HeldItem.Letter) and stands pinning it (BoardPin) until its hour, then a moment more, and goes. Draws
            /// nothing from rng, so a day with nothing due leaves every draw where it was.
            /// </summary>
            bool StartPoster(Entity self, ref CreatureState s)
            {
                var town = SettlementOf(s.Home);
                if (town < 0 || !NoticeBoard.Due(town, Day, Hour, out var slot, out _))
                    return false;
                var board = NoticeBoard.At(Terrain.Settlements.Value.Settlements[town].Centre);
                var spot = NoticeBoard.PinSpot(board, slot);
                if (!InReach(in s, spot) || Taken(self, spot, 0.5f) || !Walkable(spot, Habitat.Land))
                    return false;
                SetMode(ref s, CreatureMode.Sit);
                s.Target = spot;
                s.Focus = spot + new float2(0f, 4f);
                s.TargetHeight = CreatureAnimator.BoardPin;
                s.Timer = PinAfter + 1f;
                return true;
            }

            /// <summary>Whether a spot at the board is inside its home range (with a step to spare). Beyond it, KeepToLeash
            /// cuts the walk short at the range's edge and it would stand reading (or pinning) at nothing, metres off.</summary>
            static bool InReach(in CreatureState s, float2 spot) => math.distance(spot, s.Home) < s.Leash - 1.5f;

            /// <summary>How long a poster stays once its sheet is up (s).</summary>
            const float PinAfter = 3f;

            /// <summary>A poster at the board waits on while its sheet is still to go up (its town has one due).</summary>
            void Pinning(ref CreatureState s)
            {
                var town = SettlementOf(s.Home);
                if (town >= 0 && NoticeBoard.Due(town, Day, Hour, out _, out _))
                    s.Timer = math.max(s.Timer, PinAfter);
            }

            /// <summary>Whether a poster still has its sheet in hand: on the way, and at the board until its hour.</summary>
            static bool CarryingSheet(in CreatureState s) =>
                s.Mode == CreatureMode.Sit && s.TargetHeight == CreatureAnimator.BoardPin && s.Timer > PinAfter - 0.5f;

            static bool AtBoard(float stage) =>
                stage == CreatureAnimator.BoardRead || stage == CreatureAnimator.BoardPoint || stage == CreatureAnimator.BoardPin;
        }
    }
}
