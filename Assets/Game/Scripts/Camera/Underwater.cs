using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Punchy.CameraRig
{
    /// <summary>
    /// The camera under water (#22, P-19, first slice): when the lens is in a body of water (the sea, a lake or a river:
    /// TerrainSource.SampleGround's water, between its bed and its surface; or a cave stream or pool:
    /// ThirdPersonCamera.CaveWaterAbove), a blue-green fog with short visibility, a tint over the screen, and the sound
    /// muffled (a low-pass filter on the listener). Blended over <see cref="Blend"/> s, going in and coming out with a
    /// few centimetres between (so a lens bobbing at the waterline doesn't flicker). Water surfaces are only read.
    /// Runs after WeatherSystem sets the fog each frame and after the camera has moved.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class Underwater : MonoBehaviour
    {
        const float Blend = 0.3f, Margin = 0.04f, Cutoff = 700f;
        static readonly Color FogColour = new Color(0.04f, 0.22f, 0.25f);
        static readonly Color Tint = new Color(0.08f, 0.32f, 0.36f, 0.32f);
        const float FogDensity = 0.11f;

        bool under;
        float amount;
        AudioLowPassFilter lowPass;
        EntityQuery terrainQuery;
        World queryWorld;
        GUIStyle none;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Attach();
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode m) => Attach();

        static void Attach()
        {
            var rig = FindAnyObjectByType<ThirdPersonCamera>();
            if (rig != null && rig.GetComponent<Underwater>() == null)
                rig.gameObject.AddComponent<Underwater>();
        }

        void LateUpdate()
        {
            var surface = SurfaceAt(transform.position, out var kind);
            var y = transform.position.y;
            var was = under;
            under = surface > float.MinValue && (under ? y < surface + Margin : y < surface - Margin);
            if (under != was)
                Debug.Log($"Underwater: {(under ? "in" : "out of")} the water ({kind}, lens {y:F2}, surface {(surface > float.MinValue ? surface.ToString("F2") : "-")}); low-pass {(under ? $"on ({Cutoff:F0} Hz)" : "off")}");
            amount = Mathf.MoveTowards(amount, under ? 1f : 0f, Time.deltaTime / Blend);
            if (amount > 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, FogColour, amount);
                RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, FogDensity, amount);
            }
            Muffle();
        }

        /// <summary>A low-pass filter on the listener, its cutoff blended with the rest (off when dry).</summary>
        void Muffle()
        {
            if (lowPass == null)
            {
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener == null)
                    return;
                lowPass = listener.GetComponent<AudioLowPassFilter>() ?? listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            lowPass.enabled = amount > 0f;
            lowPass.cutoffFrequency = Mathf.Lerp(22000f, Cutoff, amount * amount);
        }

        /// <summary>The surface of the water the point is in (float.MinValue: not in water), and what kind it is.</summary>
        float SurfaceAt(Vector3 p, out string kind)
        {
            kind = "none";
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return float.MinValue;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return float.MinValue;
            var at = new float3(p.x, p.y, p.z);
            if (terrain.Caves.IsCreated)
            {
                var cave = ThirdPersonCamera.CaveWaterAbove(ref terrain.Caves.Value, at);
                if (cave > float.MinValue)
                {
                    kind = "cave water";
                    return cave;
                }
            }
            var ground = terrain.SampleGround(at.xz);
            // (In the water column: above its bed, and not deep in rock under it, a cave under a lake.)
            if (ground.HasWater && p.y > ground.Height - 0.5f && p.y < ground.WaterSurface + 1f)
            {
                kind = math.abs(ground.WaterSurface - terrain.World.SeaLevel) < 0.05f ? "the sea" : "a lake or river";
                return ground.WaterSurface;
            }
            return float.MinValue;
        }

        void OnGUI()
        {
            if (amount <= 0f || Event.current.type != EventType.Repaint)
                return;
            var c = GUI.color;
            GUI.color = new Color(Tint.r, Tint.g, Tint.b, Tint.a * amount);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }

        void OnDisable()
        {
            if (lowPass != null)
                lowPass.enabled = false;
        }
    }
}
