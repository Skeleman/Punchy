using UnityEngine;

namespace Punchy.Weather
{
    /// <summary>One kind of weather. WeatherSystem drifts between presets, blending every value.</summary>
    [CreateAssetMenu(menuName = "Punchy/Weather Preset", fileName = "Weather")]
    public class WeatherPreset : ScriptableObject
    {
        [Header("Sky")]
        [Range(0f, 1f)] public float CloudCoverage = 0.3f;
        [Tooltip("How dark and heavy the clouds are.")]
        [Range(0f, 1f)] public float CloudDarkness = 0.1f;
        [Tooltip("Blends the sky from clear blue to grey.")]
        [Range(0f, 1f)] public float Overcast;
        [Tooltip("Sunlight strength relative to a clear day.")]
        [Range(0f, 1.5f)] public float Sunlight = 1f;

        [Header("Air")]
        [Tooltip("Fog thickness relative to the draw-distance minimum (1 = just enough to hide the terrain's edge).")]
        [Min(1f)] public float Fog = 1f;
        [Range(0f, 1f)] public float Wind = 0.3f;

        [Header("Precipitation")]
        [Tooltip("Rain (or snow on snowy/high ground). Scaled down where the local biome is dry.")]
        [Range(0f, 1f)] public float Precipitation;

        [Header("Scheduling")]
        [Tooltip("Relative chance of this weather being picked next.")]
        [Min(0f)] public float Chance = 1f;
        [Tooltip("How long this weather lasts once it arrives, in seconds (random between min and max).")]
        public Vector2 Duration = new Vector2(120f, 300f);
    }
}
