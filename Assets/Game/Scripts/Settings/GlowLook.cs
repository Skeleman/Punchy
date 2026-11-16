using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// R-39 row 2 (#402): the night look that lets a glow (igloo runes, glowing mushrooms, crystals) read as light
    /// falling on the ground and walls round it. Zach picked this one (option 3 of the trial): darker nights (ambient,
    /// moons and the far haze) and a bright pool close to each glow (Glow.hlsl: brightness, reach and falloff power).
    /// </summary>
    public static class GlowLook
    {
        /// <summary>The share of the night's ambient and moonlight kept at full night.</summary>
        public const float NightKept = 0.35f;
        /// <summary>The share of the night haze's brightness kept at full night (Sky.hlsl, PunchyAtmosphere).</summary>
        public const float NightHazeKept = 0.5f;
        /// <summary>Glow slot brightness, reach scale and falloff power ((1 - d / reach)^power; 1, 1, 2 before).</summary>
        public const float Brightness = 2.6f, Reach = 1.15f, Power = 3f;

        /// <summary>How much of the night's light (ambient, moons) is kept at this much night (0 day, 1 full night).</summary>
        public static float NightScale(float night) => math.lerp(1f, NightKept, math.saturate(night));

        /// <summary>How much of the haze's brightness is kept at this much night (0 day, 1 full night).</summary>
        public static float NightHazeScale(float night) => math.lerp(1f, NightHazeKept, math.saturate(night));

        static readonly int GlowLookId = Shader.PropertyToID("_PunchyGlowLook");
        static readonly int NightHazeId = Shader.PropertyToID("_PunchyNightHaze");

        /// <summary>Hands the falloff to the shaders (once a frame, CaveSystem).</summary>
        public static void Upload() => Shader.SetGlobalVector(GlowLookId, new Vector4(Brightness, 1f / Reach, Power, 0f));

        /// <summary>Hands the night's haze scale to the shaders (once a frame, WeatherSystem).</summary>
        public static void UploadNight(float night) => Shader.SetGlobalFloat(NightHazeId, NightHazeScale(night));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Upload();
            UploadNight(0f);
        }
    }
}
