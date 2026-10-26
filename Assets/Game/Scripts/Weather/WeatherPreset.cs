using UnityEngine;

namespace Punchy.Weather
{
    /// <summary>
    /// One kind of weather. In the world it appears as cells (a rain cell, a sandstorm…) that form where the biome
    /// odds allow, drift with the wind, then fade. The first preset in WeatherSystem's list is the fair-weather
    /// background between cells.
    /// </summary>
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
        [Tooltip("Height of the cloud bottoms above sea level, in metres. Rain falls from here.")]
        [Min(200f)] public float CloudBase = 1100f;
        [Tooltip("Vertical extent of the clouds, in metres: a few hundred for flat cumulus/stratus, " +
                 "several thousand for towering cumulonimbus (which spread into anvils at the top).")]
        [Min(50f)] public float CloudThickness = 700f;

        [Header("Air")]
        [Tooltip("Fog thickness relative to the draw-distance minimum (1 = just enough to hide the terrain's edge).")]
        [Min(1f)] public float Fog = 1f;
        [Range(0f, 1f)] public float Wind = 0.3f;

        [Header("Precipitation")]
        [Tooltip("Rain (or snow on snowy/high ground). Scaled down where the local biome is dry.")]
        [Range(0f, 1f)] public float Precipitation;
        [Tooltip("Precipitation always falls as snow (blizzards).")]
        public bool AlwaysSnow;
        [Tooltip("Hailstones mixed into the precipitation (0..1).")]
        [Range(0f, 1f)] public float Hail;

        [Header("Dust")]
        [Tooltip("Airborne dust or sand: tints the sky and fog and blows sand past the camera.")]
        [Range(0f, 1f)] public float Dust;
        public Color DustColor = new Color(0.78f, 0.62f, 0.42f);

        [Header("Lightning")]
        [Min(0f)] public float LightningPerMinute;

        [Header("Scheduling")]
        [Tooltip("Relative chance of this weather being picked next, in biomes that don't set their own chance for it.")]
        [Min(0f)] public float Chance = 1f;
        [Tooltip("Only happens in biomes that list this preset in their Weather section (e.g. sandstorms in deserts).")]
        public bool OnlyInListedBiomes;
        [Tooltip("How long a cell of this weather lingers at full strength, in seconds (random between min and max).")]
        public Vector2 Duration = new Vector2(120f, 300f);
        [Tooltip("Radius of a cell of this weather, in metres (random between min and max).")]
        public Vector2 Size = new Vector2(800f, 1600f);
    }
}
