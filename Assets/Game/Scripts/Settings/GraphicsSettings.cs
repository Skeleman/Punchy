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
    /// world and can join each other. Kept per machine (PlayerPrefs) and changed in the main menu's Graphics tab.
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

        /// <summary>A far range (terrain, trees, landmark effects) at the current View setting.</summary>
        public static float ViewRange(float baseline) => baseline * View;
        /// <summary>A near range (grass, critters, flames, small details) at the current Detail setting.</summary>
        public static float DetailRange(float baseline) => baseline * Detail;

        /// <summary>
        /// How many real (Unity) local lights LightBudget may have on at once (refactor 3.1): 4 at Low, 6 at Medium, 8 at
        /// High and Ultra, by Detail. "-lightbudget N" overrides it for the run (benches). Glow ranges follow Detail
        /// through LightPolicy's thresholds.
        /// </summary>
        public static int RealLights => lightBudgetArg >= 0 ? lightBudgetArg : Mathf.Clamp(Mathf.RoundToInt(2f + 4f * Detail), 4, 8);
        static int lightBudgetArg = -1;

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
        /// <summary>
        /// The sun's shadow cascades by Shadows setting (W-105, Zach 11-05): Low 2, Medium 3, High and Ultra 4. Custom
        /// settings take the nearest preset's count by these limits. Set on the running pipeline only, never saved.
        /// </summary>
        const float TwoCascadesBelow = 0.8f, ThreeCascadesBelow = 1.2f;

        const string ViewKey = "Punchy.Gfx.View", DetailKey = "Punchy.Gfx.Detail", ShadowsKey = "Punchy.Gfx.Shadows";

        // The pipeline's own shadow settings, scaled from and put back on quitting (in the editor play mode would
        // otherwise save the scaled values into the asset).
        static UniversalRenderPipelineAsset pipeline;
        static float baseShadowDistance;
        static int baseCascades;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
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

            Punchy.UI.MainMenu.AddTab("Graphics", 20, DrawTab);
        }

        /// <summary>"-gfx Ultra" and "-gfxset view=1.3,detail=0.8,shadows=1" (applied over the saved settings).</summary>
        static void ReadCommandLine(string[] args)
        {
            lightBudgetArg = -1;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-lightbudget" && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lights))
                    lightBudgetArg = Mathf.Clamp(lights, 0, 64);
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
            "view {0:0.0#} detail {1:0.0#} shadows {2:0.0#} ({3}, {4} cascades, {5} real lights)", View, Detail, Shadows, Preset, Cascades, RealLights);

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
            pipeline.shadowCascadeCount = Cascades;
        }

        /// <summary>How many shadow cascades the sun casts at the current Shadows setting.</summary>
        public static int Cascades => Shadows < TwoCascadesBelow ? 2 : Shadows < ThreeCascadesBelow ? 3 : 4;

        static void RestorePipeline()
        {
            if (pipeline == null)
                return;
            pipeline.shadowDistance = baseShadowDistance;
            pipeline.shadowCascadeCount = baseCascades;
        }

        /// <summary>
        /// The main menu's Graphics tab: the presets, the three sliders and Reset, applied as they move, and the frame
        /// cap (also on the Options button), and the resolution and window mode (DisplaySettings). Quit is in the menu's foot, for every tab.
        /// </summary>
        static void DrawTab(Punchy.UI.MenuNav nav)
        {
            title ??= new GUIStyle(GUI.skin.label) { richText = true };
            GUILayout.Label($"<b>Graphics</b>   {Preset}", title);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Presets.Length; i++)
                if (nav.Toggle(Preset == (GraphicsPreset)i, ((GraphicsPreset)i).ToString(), GUI.skin.button))
                    Set((GraphicsPreset)i);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            // (Only when a slider was moved: a value from the command line, off the sliders' steps, stays as it is
            // and unsaved until someone changes it here.)
            GUI.changed = false;
            var view = Slider(nav, "View distance", View);
            var detail = Slider(nav, "Detail distance", Detail);
            var shadows = Slider(nav, "Shadows", Shadows);
            if (GUI.changed)
                Set(view, detail, shadows);
            GUILayout.Space(6);
            if (nav.Button("Reset (Medium)"))
                Set(GraphicsPreset.Medium);
            GUILayout.Space(6);
            DisplaySettings.DrawRows(nav);
            GUILayout.Space(6);
            if (nav.Button($"Frame cap: {FrameRateCap.Describe(FrameRateCap.Current)}"))
                FrameRateCap.Next();
        }

        static GUIStyle title;

        static float Slider(Punchy.UI.MenuNav nav, string label, float value)
        {
            GUILayout.Label($"{label}: {value.ToString("0.00", CultureInfo.InvariantCulture)}x");
            // (In steps of 0.05, so a preset's values can be found again by hand.)
            return Mathf.Round(nav.Slider(value, Min, Max, 0.05f) * 20f) / 20f;
        }
    }
}
