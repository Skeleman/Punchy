using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Punchy.Settings
{
    /// <summary>The graphics presets; Custom when the sliders match none of them.</summary>
    public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }

    /// <summary>
    /// How far things are drawn: three multipliers on the game's own draw and detail ranges, so a strong PC can push
    /// them out and a weak one pull them in. 1 is the game as tuned (Medium). View scales the far things (terrain and
    /// its detail levels, trees and bushes, landmark effects), Detail the near ones (grass, pebbles, small plants,
    /// critters, flames, worm marks, creature details), Shadows the sun's shadow distance. Systems read their range as
    /// <see cref="ViewRange"/> or <see cref="DetailRange"/> of their own default where they use it. Only what's drawn
    /// changes, never what the game simulates, collides with or sends: players with different settings see the same
    /// world and can join each other. Kept per machine (PlayerPrefs) and changed in the F5 panel.
    /// "-gfx Low|Medium|High|Ultra" and "-gfxset view=1.3,detail=0.8,shadows=1" set them for that run only (benches),
    /// and are never saved.
    /// </summary>
    public static class GraphicsSettings
    {
        public const float Min = 0.5f, Max = 2f;

        public static float View { get; private set; } = 1f;
        public static float Detail { get; private set; } = 1f;
        public static float Shadows { get; private set; } = 1f;
        public static GraphicsPreset Preset { get; private set; } = GraphicsPreset.Medium;
        /// <summary>Bumped on every change, for systems that hash their settings and rebuild when they change.</summary>
        public static int Version { get; private set; }
        /// <summary>Set from the command line for this run (not saved).</summary>
        public static bool FromCommandLine { get; private set; }
        /// <summary>The F5 panel is showing (see Punchy.UI.Overlay).</summary>
        public static bool PanelOpen { get; private set; }

        /// <summary>A far range (terrain, trees, landmark effects) at the current View setting.</summary>
        public static float ViewRange(float baseline) => baseline * View;
        /// <summary>A near range (grass, critters, flames, small details) at the current Detail setting.</summary>
        public static float DetailRange(float baseline) => baseline * Detail;

        /// <summary>
        /// How far worms' marks are drawn and fed, at Detail 1 (m). One source for both: CreatureBurrowSystem feeds
        /// marks within DetailRange(BurrowRange), and the terrain shader draws them within _PunchyBurrowRange.
        /// </summary>
        public const float BurrowRange = 150f;
        static readonly int BurrowRangeId = Shader.PropertyToID("_PunchyBurrowRange");

        /// <summary>Each preset's view, detail and shadows (Medium is the game as tuned).</summary>
        static readonly Vector3[] Presets =
        {
            new Vector3(0.6f, 0.5f, 0.6f),
            new Vector3(1f, 1f, 1f),
            new Vector3(1.3f, 1.5f, 1.4f),
            new Vector3(1.7f, 2f, 2f),
        };
        /// <summary>Below this Shadows setting the sun casts two cascades instead of the pipeline's own count.</summary>
        const float FewCascadesBelow = 0.75f;

        const string ViewKey = "Punchy.Gfx.View", DetailKey = "Punchy.Gfx.Detail", ShadowsKey = "Punchy.Gfx.Shadows";

        // The pipeline's own shadow settings, scaled from and put back on quitting (in the editor play mode would
        // otherwise save the scaled values into the asset).
        static UniversalRenderPipelineAsset pipeline;
        static float baseShadowDistance;
        static int baseCascades;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            PanelOpen = false;
            FromCommandLine = false;
            View = Clamp(PlayerPrefs.GetFloat(ViewKey, 1f));
            Detail = Clamp(PlayerPrefs.GetFloat(DetailKey, 1f));
            Shadows = Clamp(PlayerPrefs.GetFloat(ShadowsKey, 1f));
            ReadCommandLine(Punchy.Debugging.DebugControls.LaunchArgs);
            Preset = Match();

            pipeline = UniversalRenderPipeline.asset;
            if (pipeline != null)
            {
                baseShadowDistance = pipeline.shadowDistance;
                baseCascades = pipeline.shadowCascadeCount;
            }
            Application.quitting -= RestorePipeline;
            Application.quitting += RestorePipeline;
            Apply();
            Debug.Log("Graphics: " + Describe() + (FromCommandLine ? " (from the command line)" : ""));

            // (Something to watch F5 and draw the panel, kept across the scene reload a join does.)
            var watcher = new GameObject("GraphicsSettings");
            Object.DontDestroyOnLoad(watcher);
            watcher.hideFlags = HideFlags.HideInHierarchy;
            watcher.AddComponent<Panel>();
        }

        /// <summary>"-gfx Ultra" and "-gfxset view=1.3,detail=0.8,shadows=1" (applied over the saved settings).</summary>
        static void ReadCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gfx" && System.Enum.TryParse<GraphicsPreset>(args[i + 1], true, out var preset) && preset != GraphicsPreset.Custom)
                {
                    var p = Presets[(int)preset];
                    (View, Detail, Shadows) = (p.x, p.y, p.z);
                    FromCommandLine = true;
                }
                if (args[i] == "-gfxset")
                    foreach (var pair in args[i + 1].Split(','))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                            continue;
                        switch (kv[0].Trim().ToLowerInvariant())
                        {
                            case "view": View = Clamp(value); FromCommandLine = true; break;
                            case "detail": Detail = Clamp(value); FromCommandLine = true; break;
                            case "shadows": Shadows = Clamp(value); FromCommandLine = true; break;
                        }
                    }
            }
        }

        /// <summary>Sets all three (clamped), applies them now, and saves them for this machine.</summary>
        public static void Set(float view, float detail, float shadows)
        {
            view = Clamp(view);
            detail = Clamp(detail);
            shadows = Clamp(shadows);
            if (view == View && detail == Detail && shadows == Shadows)
                return;
            (View, Detail, Shadows) = (view, detail, shadows);
            Preset = Match();
            FromCommandLine = false;
            Version++;
            Apply();
            PlayerPrefs.SetFloat(ViewKey, View);
            PlayerPrefs.SetFloat(DetailKey, Detail);
            PlayerPrefs.SetFloat(ShadowsKey, Shadows);
        }

        public static void Set(GraphicsPreset preset)
        {
            if (preset == GraphicsPreset.Custom)
                return;
            var p = Presets[(int)preset];
            Set(p.x, p.y, p.z);
        }

        /// <summary>"view 1.0 detail 1.0 shadows 1.0 (Medium)".</summary>
        public static string Describe() => string.Format(CultureInfo.InvariantCulture,
            "view {0:0.0#} detail {1:0.0#} shadows {2:0.0#} ({3})", View, Detail, Shadows, Preset);

        static float Clamp(float value) => Mathf.Clamp(float.IsNaN(value) ? 1f : value, Min, Max);

        static GraphicsPreset Match()
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Mathf.Approximately(Presets[i].x, View) && Mathf.Approximately(Presets[i].y, Detail) && Mathf.Approximately(Presets[i].z, Shadows))
                    return (GraphicsPreset)i;
            return GraphicsPreset.Custom;
        }

        /// <summary>What's applied here rather than read by each system: the sun's shadows and the worm marks' range.</summary>
        static void Apply()
        {
            Shader.SetGlobalFloat(BurrowRangeId, DetailRange(BurrowRange));
            if (pipeline == null)
                return;
            pipeline.shadowDistance = baseShadowDistance * Shadows;
            pipeline.shadowCascadeCount = Shadows < FewCascadesBelow ? Mathf.Min(baseCascades, 2) : baseCascades;
        }

        static void RestorePipeline()
        {
            if (pipeline == null)
                return;
            pipeline.shadowDistance = baseShadowDistance;
            pipeline.shadowCascadeCount = baseCascades;
        }

        /// <summary>The F5 panel: the presets, the three sliders and Reset, applied as they move, and Quit (Z-01).</summary>
        class Panel : MonoBehaviour
        {
            GUIStyle box, title;
            /// <summary>Quit was pressed once: the panel asks before closing the game.</summary>
            bool confirmQuit;

            void Update()
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
                {
                    PanelOpen = !PanelOpen;
                    confirmQuit = false;
                    if (!PanelOpen)
                        PlayerPrefs.Save();
                }
            }

            void OnGUI()
            {
                if (!PanelOpen)
                    return;
                box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) };
                title ??= new GUIStyle(GUI.skin.label) { richText = true };
                GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 170, 60, 340, 290), box);
                GUILayout.Label($"<b>Graphics</b>  (F5)   {Preset}", title);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Presets.Length; i++)
                    if (GUILayout.Toggle(Preset == (GraphicsPreset)i, ((GraphicsPreset)i).ToString(), GUI.skin.button))
                        Set((GraphicsPreset)i);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                // (Only when a slider was moved: a value from the command line, off the sliders' steps, stays as it is
                // and unsaved until someone changes it here.)
                GUI.changed = false;
                var view = Slider("View distance", View);
                var detail = Slider("Detail distance", Detail);
                var shadows = Slider("Shadows", Shadows);
                if (GUI.changed)
                    Set(view, detail, shadows);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset (Medium)"))
                    Set(GraphicsPreset.Medium);
                if (GUILayout.Button("Close"))
                {
                    PanelOpen = false;
                    PlayerPrefs.Save();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                if (!confirmQuit)
                {
                    if (GUILayout.Button("Quit game"))
                        confirmQuit = true;
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Quit the game?");
                    if (GUILayout.Button("Quit"))
                    {
                        PlayerPrefs.Save();
                        Quit();
                    }
                    if (GUILayout.Button("Cancel"))
                        confirmQuit = false;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
            }

            static void Quit()
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

            static float Slider(string label, float value)
            {
                GUILayout.Label($"{label}: {value.ToString("0.00", CultureInfo.InvariantCulture)}x");
                // (In steps of 0.05, so a preset's values can be found again by hand.)
                return Mathf.Round(GUILayout.HorizontalSlider(value, Min, Max) * 20f) / 20f;
            }
        }
    }
}
