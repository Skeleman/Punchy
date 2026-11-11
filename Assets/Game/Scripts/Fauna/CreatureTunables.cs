using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// M-05: hand-tuned proportion and key-pose adjustments, layered on top of the species and limb assets when a
    /// creature is baked (<see cref="CreatureBlobBuilder.Build"/>). The Model Lab editor window (Editor/ModelLab) is what
    /// writes it; the game only reads it.
    /// <para>
    /// It holds only differences. A species with no entry (or an entry at all 1s and no nudges) bakes exactly as its
    /// assets say, byte for byte, so the default asset (empty lists) leaves the fauna fingerprint where it was. Every
    /// multiplier is applied as a plain multiply by its value, which for 1 is exact.
    /// </para>
    /// Kept as one asset in Resources so a Lab save is one reviewable diff, and loaded once (main thread, at bake).
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Creature Tunables")]
    public class CreatureTunables : ScriptableObject
    {
        /// <summary>The Resources path the game loads it from.</summary>
        public const string ResourcePath = "CreatureTunables";

        /// <summary>The key poses in blob order (<see cref="CreatureBlob.PoseCount"/>), as the Lab names them.</summary>
        public static readonly string[] PoseNames = { "Idle", "WalkReach", "WalkPush", "WalkLift", "RunReach", "RunPush", "RunLift" };

        [Tooltip("Per species (keyed by the species asset's name; a variant follows the species it is built on).")]
        public List<CreatureTune> Creatures = new List<CreatureTune>();

        [Tooltip("Per colossus kind (keyed by the kind's name).")]
        public List<ColossusTune> Colossi = new List<ColossusTune>();

        static CreatureTunables loaded;
        static bool tried;

        /// <summary>The asset, or null when there is none (then everything bakes as its assets say).</summary>
        public static CreatureTunables Instance
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    loaded = Resources.Load<CreatureTunables>(ResourcePath);
                }
                return loaded;
            }
        }

        /// <summary>Forgets the loaded asset, so the next bake loads it again (the Lab, after creating it).</summary>
        public static void Reload()
        {
            tried = false;
            loaded = null;
        }

        /// <summary>The species' adjustments, or null for none.</summary>
        public static CreatureTune For(string species)
        {
            var set = Instance;
            if (set == null || string.IsNullOrEmpty(species))
                return null;
            foreach (var t in set.Creatures)
                if (t != null && t.Species == species)
                    return t;
            return null;
        }

        /// <summary>The colossus kind's adjustments, or null for none.</summary>
        public static ColossusTune ForColossus(string kind)
        {
            var set = Instance;
            if (set == null || string.IsNullOrEmpty(kind))
                return null;
            foreach (var t in set.Colossi)
                if (t != null && t.Kind == kind)
                    return t;
            return null;
        }
    }

    /// <summary>One species' adjustments. Every multiplier defaults to 1 (no change).</summary>
    [Serializable]
    public class CreatureTune
    {
        public string Species;
        [Tooltip("Overall size (the size range creatures are spawned at).")] public float Size = 1f;
        [Tooltip("Body length, nose to rump (and where along it the parts sit).")] public float BodyLength = 1f;
        [Tooltip("Body and limb girth.")] public float Thickness = 1f;
        [Tooltip("Leg segment lengths.")] public float Legs = 1f;
        [Tooltip("The head and everything on it.")] public float Head = 1f;
        [Tooltip("Neck segment lengths.")] public float Neck = 1f;
        [Tooltip("Tail segment lengths.")] public float Tail = 1f;
        [Tooltip("Walk and run speed.")] public float Speed = 1f;
        [Tooltip("How far the walk and run keys swing away from the idle pose.")] public float Swing = 1f;
        [Tooltip("Key-pose nudges, in degrees, added to a limb's key angles.")] public List<PoseNudge> Poses = new List<PoseNudge>();

        /// <summary>Whether it changes nothing (the Lab drops such entries on save).</summary>
        public bool IsDefault => Size == 1f && BodyLength == 1f && Thickness == 1f && Legs == 1f && Head == 1f && Neck == 1f
                              && Tail == 1f && Speed == 1f && Swing == 1f && (Poses == null || Poses.Count == 0);

        /// <summary>The summed nudge for one segment of one slot at one key (degrees), and whether there is any.</summary>
        public bool Nudge(int slot, int segment, int key, out Vector3 degrees)
        {
            degrees = Vector3.zero;
            var any = false;
            if (Poses == null)
                return false;
            foreach (var p in Poses)
                if (p.Slot == slot && p.Segment == segment && p.Key == key)
                {
                    degrees += p.Degrees;
                    any = true;
                }
            return any;
        }
    }

    /// <summary>A nudge to one key pose of one segment of one limb slot (the species' Parts index; mirrored copies share it).</summary>
    [Serializable]
    public struct PoseNudge
    {
        public int Slot;
        public int Segment;
        [Tooltip("0 Idle, 1 WalkReach, 2 WalkPush, 3 WalkLift, 4 RunReach, 5 RunPush, 6 RunLift.")] public int Key;
        public Vector3 Degrees;
    }

    /// <summary>One colossus kind's adjustments. Defaults change nothing.</summary>
    [Serializable]
    public class ColossusTune
    {
        public string Kind;
        [Tooltip("How far its idle sway (the ice mammoth's trunk) swings: 1 as built, 0 still.")] public float Sway = 1f;

        public bool IsDefault => Sway == 1f;
    }
}
