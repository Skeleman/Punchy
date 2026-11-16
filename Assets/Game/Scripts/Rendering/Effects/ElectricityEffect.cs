using System;
using UnityEngine;

namespace Punchy.Rendering.Effects
{
    /// <summary>One colour an arc can crackle in: its white-hot core, the glow round it, and the light it throws.</summary>
    [Serializable]
    public struct ArcColour
    {
        public string Name;
        [Tooltip("The thin core of the bolt (linear, HDR: bright enough to bloom).")]
        [ColorUsage(false, true)] public Color Core;
        [Tooltip("The soft glow either side of the core, and the afterglow left on the last path.")]
        [ColorUsage(false, true)] public Color Glow;
        [Tooltip("The light it throws (a LightBudget request), times the effect's light intensity.")]
        [ColorUsage(false, true)] public Color Light;
    }

    /// <summary>
    /// R-29 row 2 (#320, rule 30): the standard electricity, in one asset every arc draws from, as StandardFire is for
    /// fire. Spire arcs, the golem's arcs and any later arc (a shocked creature, a charged ruin, a storm bolt) look alike
    /// and are tuned in one place. Callers pick a colour by index (ElectricitySystem.Blue, Violet, Green, Gold or a later
    /// entry). Resources/StandardElectricity; edits apply live in Play mode.
    /// SKETCH (11-16, uncompiled): field set and defaults to settle at the first editor slot.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Electricity Effect", fileName = "Electricity")]
    public class ElectricityEffect : ScriptableObject
    {
        /// <summary>The shader reads this many colours.</summary>
        public const int MaxColours = 8;

        [Tooltip("Colours by index: 0 blue-white, 1 violet, 2 toxic green, 3 gold; add more at the end.")]
        public ArcColour[] Colours = Array.Empty<ArcColour>();

        [Header("Shape")]
        [Tooltip("How far the spine strays from the straight line, as a share of the arc's length (capped by Jag Max).")]
        [Range(0f, 0.5f)] public float Jag = 0.12f;
        [Tooltip("The most the spine strays (m), however long the arc.")]
        [Min(0f)] public float JagMax = 2.5f;
        [Tooltip("The core's half width (m) at width 1.")]
        [Min(0.001f)] public float CoreWidth = 0.05f;
        [Tooltip("The glow's half width, as a multiple of the core's.")]
        [Min(1f)] public float GlowWidth = 6f;
        [Tooltip("How bright the glow is next to the core (0 for none).")]
        [Min(0f)] public float GlowStrength = 0.35f;

        [Header("Forks")]
        [Tooltip("The chance each of an arc's two fork slots has a fork on a given path.")]
        [Range(0f, 1f)] public float ForkChance = 0.55f;
        [Tooltip("A fork's length as a share of the arc's.")]
        [Range(0f, 1f)] public float ForkLength = 0.3f;
        [Tooltip("How many forks miss: they leave the spine and die in the air instead of reaching back.")]
        [Range(0f, 1f)] public float ForkMiss = 0.7f;

        [Header("Re-strike")]
        [Tooltip("New paths a second, on average; each gap is drawn at random between 0.15x and 1.85x the mean, so there's never a beat.")]
        [Min(0.1f)] public float RejagRate = 7f;
        [Tooltip("How long the old path glows on after a re-jag (s).")]
        [Min(0f)] public float Afterglow = 0.08f;
        [Tooltip("The share of 1/30 s slots in which the bolt drops out entirely (flicker).")]
        [Range(0f, 1f)] public float Dropout = 0.2f;
        [Tooltip("How much each new path's brightness varies (0 every path alike).")]
        [Range(0f, 1f)] public float BrightnessJitter = 0.5f;

        [Header("Light")]
        [Tooltip("Scales each colour's light.")]
        [Min(0f)] public float LightIntensity = 1f;
        [Tooltip("How far the light reaches (m) at width 1.")]
        [Min(0f)] public float LightReach = 8f;

        [Header("Sound")]
        [Tooltip("The chance a re-jag of a steady arc cracks (struck arcs always crack on the strike).")]
        [Range(0f, 1f)] public float CrackChance = 0.15f;
        [Tooltip("How far a crack is heard (m) at width 1.")]
        [Min(0f)] public float CrackReach = 70f;
        [Min(0f)] public float CrackVolume = 1f;
        [Min(0.1f)] public float CrackPitch = 2.4f;
    }
}
