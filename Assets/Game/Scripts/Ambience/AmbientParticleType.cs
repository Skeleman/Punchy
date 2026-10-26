using System;
using UnityEngine;

namespace Punchy.Ambience
{
    public enum AmbientParticleStyle : byte
    {
        [Tooltip("Points of light added to the scene (sparkles, spores, embers). Bloom gives them a halo.")] Glow,
        [Tooltip("Soft translucent puffs lit like the air around them (fog wisps, drifting ash).")] Mist
    }

    public enum AmbientParticleCondition : byte
    {
        Always,
        [Tooltip("Only at night.")] Night,
        [Tooltip("Only in daylight.")] Day,
        [Tooltip("Only in foggy weather (e.g. marsh fog drifting into a cold biome).")] Fog,
        [Tooltip("Only in dusty weather (sandstorms, ash storms).")] Dust
    }

    /// <summary>
    /// A kind of ambient particle that hangs in the air around the player: sparkles, spores, embers, drifting ash,
    /// low fog wisps. Biomes list which ones they have and how many; any biome can reuse any type, and a condition
    /// can tie it to the time of day or the weather. Particles are placed in the world and animated on the GPU.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Ambient Particles", fileName = "Particles")]
    public class AmbientParticleType : ScriptableObject
    {
        public AmbientParticleStyle Style = AmbientParticleStyle.Glow;
        public AmbientParticleCondition Condition = AmbientParticleCondition.Always;
        [Tooltip("Colour; for Glow particles also brightness (HDR). Alpha sets Mist opacity.")]
        [ColorUsage(true, true)] public Color Color = new Color(1f, 0.9f, 0.6f, 1f);
        [Tooltip("Size range in metres.")]
        public Vector2 Size = new Vector2(0.03f, 0.06f);
        [Tooltip("Height range above the ground, in metres.")]
        public Vector2 Height = new Vector2(0.3f, 3f);
        [Tooltip("Radius of the lazy wandering around each particle's home, in metres.")]
        [Min(0f)] public float Wander = 1f;
        [Tooltip("Speed of steady drift, in m/s: upwards (embers, spores; negative to fall like ash), and with the wind.")]
        public float Rise;
        [Min(0f)] public float WindDrift;
        [Tooltip("Distance a drifting particle travels before fading out and starting again, in metres.")]
        [Min(0.1f)] public float Travel = 4f;
        [Tooltip("How much glow particles flicker and twinkle (0 = steady).")]
        [Range(0f, 1f)] public float Twinkle = 0.5f;
        [Tooltip("Drawn out to this distance.")]
        [Min(5f)] public float Range = 50f;
    }

    /// <summary>An ambient particle type in a biome, and how many.</summary>
    [Serializable]
    public class BiomeParticles
    {
        public AmbientParticleType Type;
        [Tooltip("Particles per 100 m².")]
        [Min(0f)] public float Density = 10f;
    }
}
