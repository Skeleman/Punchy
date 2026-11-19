using UnityEngine;

namespace Punchy.Rendering.Effects
{
    /// <summary>
    /// R-29 row 3 (#320, rule 30): one family of rising puffs (steam, smoke) in one asset every caller of that family
    /// draws through, as StandardElectricity is for arcs. Each caller still gives its own puffs' colour, rise, width and
    /// count (a geyser's spout is not a kettle's wisp); the asset tunes the whole family on top of that in one place,
    /// and sets how small on screen its puffs may get before they fade (rule 20). At the defaults (all 1, 16.8 px)
    /// the look is the one the callers had before the family moved onto the kit.
    /// Resources/StandardSteam and Resources/StandardSmoke; edits apply live in Play mode at the callers' next gather.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Plume Effect", fileName = "Plume")]
    public class PlumeEffect : ScriptableObject
    {
        [Tooltip("Multiplies every puff's colour in the family (white: the callers' own colours).")]
        public Color Tint = Color.white;
        [Tooltip("Multiplies every puff's opacity.")]
        [Min(0f)] public float Opacity = 1f;
        [Tooltip("Multiplies every puff's size.")]
        [Min(0f)] public float Size = 1f;
        [Tooltip("Multiplies how high every plume rises.")]
        [Min(0f)] public float Rise = 1f;
        [Tooltip("Rule 20: a plume is gone below this many reference pixels (the largest of its rise, width and puff) " +
                 "and fades in over the last fifth of that reach (DetailPolicy.ExtraReach).")]
        [Min(0.1f)] public float GonePixels = 16.8f;
    }
}
