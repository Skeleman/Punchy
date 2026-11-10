using System;
using UnityEngine;

namespace Punchy.Ambience
{
    /// <summary>One colour a fire can burn in: the flame's outer, middle and core colours, and the light it throws.</summary>
    [Serializable]
    public struct FireColour
    {
        public string Name;
        [Tooltip("The flame's edge and tips (linear, HDR).")]
        [ColorUsage(false, true)] public Color Outer;
        [Tooltip("The flame's body; the glow round its base and its embers take this too.")]
        [ColorUsage(false, true)] public Color Mid;
        [Tooltip("The hot core low in the middle.")]
        [ColorUsage(false, true)] public Color Core;
        [Tooltip("The light it throws on rock and walls (a LightBudget request), times the effect's light intensity.")]
        [ColorUsage(false, true)] public Color Light;
    }

    /// <summary>
    /// The standard fire (#131): flame, glow, light, embers and smoke, in one asset every fire draws from, so a
    /// campfire, a wall torch, a ruin sconce and a brazier all look alike and are tuned in one place. Callers pick a
    /// colour by index (FireSystem.Wood, Blue, Green, Violet, or any later entry); FireSystem.ColourOverride (debug key
    /// F4) turns every fire to one colour. Resources/StandardFire; edits apply live in Play mode.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Fire Effect", fileName = "Fire")]
    public class FireEffect : ScriptableObject
    {
        /// <summary>The shader reads this many colours (Corner.w keeps the colour below 10).</summary>
        public const int MaxColours = 10;

        [Tooltip("Colours by index: 0 the standard orange wood fire, 1 blue, 2 green, 3 violet; add more at the end.")]
        public FireColour[] Colours = Array.Empty<FireColour>();

        [Header("Glow")]
        [Tooltip("The soft glow round the flame's base, as a multiple of the flame's height.")]
        [Min(0f)] public float GlowSize = 2.2f;
        [Tooltip("How bright the glow is (0 for none).")]
        [Min(0f)] public float GlowStrength = 0.1f;

        [Header("Light")]
        [Tooltip("Scales each colour's light.")]
        [Min(0f)] public float LightIntensity = 1f;
        [Tooltip("How far the light reaches (m).")]
        [Min(0f)] public float LightReach = 9f;

        [Header("Embers")]
        [Tooltip("The share of ember cycles in which an ember flies up.")]
        [Range(0f, 1f)] public float EmberChance = 0.4f;
        [Tooltip("Ember cycles a second.")]
        [Min(0.01f)] public float EmberRate = 0.8f;
        [Tooltip("How high an ember climbs over its flight, in flame heights.")]
        [Min(0f)] public float EmberRise = 2.4f;

        [Header("Smoke")]
        [Tooltip("How thick the smoke is (0 for none).")]
        [Range(0f, 1f)] public float SmokeStrength = 0.22f;
        [Tooltip("How high a puff climbs before it's gone, in flame heights.")]
        [Min(0f)] public float SmokeRise = 3.5f;
        [Tooltip("Puffs a second (each flame has two, half a cycle apart).")]
        [Min(0.01f)] public float SmokeRate = 0.35f;
        [Tooltip("Smoke colour (alpha unused: Smoke Strength sets how thick).")]
        public Color SmokeColour = new Color(0.16f, 0.15f, 0.14f, 1f);
    }
}
