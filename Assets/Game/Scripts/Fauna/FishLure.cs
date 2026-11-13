using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-100 row 2: the bobbers in the water that ambient fish notice. The player's fishing (Fishing, on the host or
    /// solo, where fish are simulated) says when a line is cast, when a fish bites and when the water is splashed or the
    /// bite missed; CreatureBrainSystem hands the list to the brain each frame, where swimming fish near one turn and
    /// drift towards it, the nearest nibbles once or twice before the real bite, and all of them scatter on a splash.
    /// Fish are simulated state, so what a joined client sees comes in with their snapshots: nothing here is sent.
    /// A SharedStatic (not a singleton) so the player's code can call it without an entity, and Burst can read it.
    /// </summary>
    public static class FishLure
    {
        public enum Stage : byte
        {
            /// <summary>In the water, no bite yet: fish come over, the nearest nibbles near the end.</summary>
            Waiting,
            /// <summary>A fish has it: the nearest holds at the bobber, tugging; the rest keep back.</summary>
            Bite,
            /// <summary>A splash or a miss: every fish in reach bolts (kept ScatterTime, then gone).</summary>
            Scatter
        }

        public struct Lure
        {
            /// <summary>Whose line (any number unique per line, e.g. the caster's network id); Splash uses NoKey.</summary>
            public int Key;
            /// <summary>The bobber, on the water's surface.</summary>
            public float3 At;
            /// <summary>Seconds since it was cast (or since it scattered), and when after the cast the bite comes.</summary>
            public float Age, BiteIn;
            public Stage Stage;
        }

        /// <summary>How far fish notice a bobber from (m), and how many come over to it (the nearest nibbles).</summary>
        public const float Reach = 8f;
        public const int Interested = 3;
        /// <summary>How long a scatter lasts before the lure is gone (s): fish within Reach then start fleeing.</summary>
        public const float ScatterTime = 0.5f;
        /// <summary>The nibbles before the bite: this long before it, each NibbleTime long (s).</summary>
        public const float FirstNibble = 1.7f, SecondNibble = 0.85f, NibbleTime = 0.45f;
        /// <summary>A line left in longer than this past its bite is dropped (a lost call; s).</summary>
        const float Stale = 120f;
        public const int NoKey = int.MinValue;

        struct DataKey { }
        /// <summary>The lures now in the water (main thread writes; the brain reads a copy each frame).</summary>
        public static readonly SharedStatic<FixedList512Bytes<Lure>> Data = SharedStatic<FixedList512Bytes<Lure>>.GetOrCreate<DataKey>();

        /// <summary>A line lands at <paramref name="at"/>; the bite will come <paramref name="biteIn"/> seconds later
        /// (FishBite.Wait). Replaces the key's last line.</summary>
        public static void Cast(int key, float3 at, float biteIn)
        {
            Clear(key);
            ref var list = ref Data.Data;
            if (list.Length < list.Capacity && math.all(math.isfinite(at)))
                list.Add(new Lure { Key = key, At = at, BiteIn = math.max(biteIn, 0f), Stage = Stage.Waiting });
        }

        /// <summary>The fish bites (FishStage.Bite; it stays so while hooked).</summary>
        public static void Bite(int key) => SetStage(key, Stage.Bite);

        /// <summary>A miss, a fish lost or the line jerked out: every fish near it bolts, and the lure is gone.</summary>
        public static void Scatter(int key) => SetStage(key, Stage.Scatter);

        /// <summary>A splash with no line (something thrown in, someone jumping in): fish near it bolt.</summary>
        public static void Splash(float3 at)
        {
            ref var list = ref Data.Data;
            if (list.Length < list.Capacity && math.all(math.isfinite(at)))
                list.Add(new Lure { Key = NoKey, At = at, Stage = Stage.Scatter });
        }

        /// <summary>The line is out of the water quietly (reeled in, caught): fish go back to what they were doing.</summary>
        public static void Clear(int key)
        {
            ref var list = ref Data.Data;
            for (int i = list.Length - 1; i >= 0; i--)
                if (list[i].Key == key)
                    list.RemoveAt(i);
        }

        public static void ClearAll() => Data.Data.Clear();

        /// <summary>How many lures are in the water, and one of them (for tests and debug readouts).</summary>
        public static int Count => Data.Data.Length;
        public static Lure Get(int i) => Data.Data[i];

        /// <summary>Ages the lures in the water by <paramref name="dt"/> (see the overload).</summary>
        public static void Tick(float dt) => Tick(ref Data.Data, dt);

        static void SetStage(int key, Stage stage)
        {
            ref var list = ref Data.Data;
            for (int i = 0; i < list.Length; i++)
                if (list[i].Key == key && list[i].Stage != Stage.Scatter)
                {
                    var l = list[i];
                    l.Stage = stage;
                    if (stage == Stage.Scatter)
                        l.Age = 0f;
                    list[i] = l;
                }
        }

        /// <summary>Ages every lure by <paramref name="dt"/>, dropping spent scatters and stale lines (the brain, once a frame).</summary>
        public static void Tick(ref FixedList512Bytes<Lure> list, float dt)
        {
            for (int i = list.Length - 1; i >= 0; i--)
            {
                var l = list[i];
                l.Age += dt;
                if (l.Stage == Stage.Scatter ? l.Age > ScatterTime : l.Age > l.BiteIn + Stale)
                    list.RemoveAt(i);
                else
                    list[i] = l;
            }
        }

        /// <summary>Where the nearest fish is in a nibble at this lure's age: 0 outside one, else 0..1 through it.</summary>
        public static float NibblePhase(in Lure l)
        {
            if (l.Stage != Stage.Waiting)
                return 0f;
            var first = l.BiteIn - FirstNibble;
            var second = l.BiteIn - SecondNibble;
            // (Only once the fish has had time to come over: a quick bite gets one nibble, or none.)
            if (first >= 1f && l.Age >= first && l.Age < first + NibbleTime)
                return (l.Age - first) / NibbleTime;
            if (second >= 1f && l.Age >= second && l.Age < second + NibbleTime)
                return (l.Age - second) / NibbleTime;
            return 0f;
        }
    }
}
