using System.Globalization;
using Punchy.Audio;
using Punchy.CameraRig;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Punchy.Settings
{
    /// <summary>
    /// The Sound and Look tabs of the main menu (P-8), kept per machine (PlayerPrefs) and put back at start.
    /// Sound: Master, Music/ambience and Effects, applied through GameAudio's own levels (Master; the World and Weather
    /// groups for music/ambience, Creatures and Player for effects). Look: mouse and pad sensitivity, invert Y, the
    /// camera's starting distance and its field of view, applied to the ThirdPersonCamera after every scene load (a join
    /// reloads it). Test flag: "-settings master=0.5,mouse=0.2,inverty=1,fov=70" applies and saves those, the same as
    /// choosing them in the menu (keys: master, music, effects, mouse, pad, inverty, distance, fov).
    /// </summary>
    public static class PlayerOptions
    {
        public static float Master { get; private set; } = 1f;
        public static float Music { get; private set; } = 1f;
        public static float Effects { get; private set; } = 1f;
        public static float Mouse { get; private set; } = DefaultMouse;
        public static float Pad { get; private set; } = DefaultPad;
        public static bool InvertY { get; private set; }
        /// <summary>P-25: first person whenever in a room.</summary>
        public static bool FirstPersonIndoors { get; private set; }
        public static float Distance { get; private set; } = DefaultDistance;
        public static float Fov { get; private set; } = DefaultFov;

        const float DefaultMouse = 0.15f, DefaultPad = 180f, DefaultDistance = 8f, DefaultFov = 60f;
        const string Prefix = "Punchy.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Master = Get("Audio.Master", 1f);
            Music = Get("Audio.Music", 1f);
            Effects = Get("Audio.Effects", 1f);
            Mouse = Get("Look.Mouse", DefaultMouse);
            Pad = Get("Look.Pad", DefaultPad);
            InvertY = Get("Look.InvertY", 0f) > 0.5f;
            FirstPersonIndoors = Get("Look.FirstPersonIndoors", 0f) > 0.5f;
            Distance = Get("Look.Distance", DefaultDistance);
            Fov = Get("Look.Fov", DefaultFov);
            var fromArgs = ReadCommandLine(Punchy.Debugging.DebugControls.LaunchArgs);
            Clamp();
            if (fromArgs)
                Save();
            ApplySound();
            Debug.Log("Settings: " + Describe() + (fromArgs ? " (from the command line, saved)" : ""));
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Punchy.UI.MainMenu.AddTab("Sound", 30, DrawSound);
            Punchy.UI.MainMenu.AddTab("Look", 40, DrawLook);
        }

        static float Get(string pref, float fallback)
        {
            var v = PlayerPrefs.GetFloat(Prefix + pref, fallback);
            return float.IsNaN(v) ? fallback : v;
        }

        static void Clamp()
        {
            Master = Mathf.Clamp01(Master);
            Music = Mathf.Clamp01(Music);
            Effects = Mathf.Clamp01(Effects);
            Mouse = Mathf.Clamp(Mouse, 0.03f, 0.5f);
            Pad = Mathf.Clamp(Pad, 60f, 400f);
            Distance = Mathf.Clamp(Distance, Punchy.CameraRig.ThirdPersonCamera.ClosestDistance, 20f);
            Fov = Mathf.Clamp(Fov, 50f, 100f);
        }

        /// <summary>"master 1.00 music 1.00 effects 1.00; mouse 0.15 pad 180 invertY off distance 8.0 fov 60".</summary>
        public static string Describe() => string.Format(CultureInfo.InvariantCulture,
            "master {0:0.00} music {1:0.00} effects {2:0.00}; mouse {3:0.00} pad {4:0} invertY {5} distance {6:0.0} fov {7:0}",
            Master, Music, Effects, Mouse, Pad, InvertY ? "on" : "off", Distance, Fov);

        /// <summary>"-settings master=0.5,inverty=1,...": true when anything was set.</summary>
        static bool ReadCommandLine(string[] args)
        {
            var found = false;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-settings")
                    continue;
                foreach (var pair in args[i + 1].Split(','))
                {
                    var kv = pair.Split('=');
                    if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        if (kv.Length == 2 && kv[0].Trim().ToLowerInvariant() == "inverty" && bool.TryParse(kv[1], out var b))
                            (InvertY, found) = (b, true);
                        continue;
                    }
                    switch (kv[0].Trim().ToLowerInvariant())
                    {
                        case "master": Master = v; found = true; break;
                        case "music": Music = v; found = true; break;
                        case "effects": Effects = v; found = true; break;
                        case "mouse": Mouse = v; found = true; break;
                        case "pad": Pad = v; found = true; break;
                        case "inverty": InvertY = v > 0.5f; found = true; break;
                        case "distance": Distance = v; found = true; break;
                        case "fov": Fov = v; found = true; break;
                    }
                }
            }
            return found;
        }

        static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Audio.Master", Master);
            PlayerPrefs.SetFloat(Prefix + "Audio.Music", Music);
            PlayerPrefs.SetFloat(Prefix + "Audio.Effects", Effects);
            PlayerPrefs.SetFloat(Prefix + "Look.Mouse", Mouse);
            PlayerPrefs.SetFloat(Prefix + "Look.Pad", Pad);
            PlayerPrefs.SetFloat(Prefix + "Look.InvertY", InvertY ? 1f : 0f);
            PlayerPrefs.SetFloat(Prefix + "Look.FirstPersonIndoors", FirstPersonIndoors ? 1f : 0f);
            PlayerPrefs.SetFloat(Prefix + "Look.Distance", Distance);
            PlayerPrefs.SetFloat(Prefix + "Look.Fov", Fov);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Through GameAudio's own levels: Master on AudioListener.volume (focus x Master, so 0 silences everything,
        /// the weather too), and the group levels its voices multiply by.
        /// </summary>
        static void ApplySound()
        {
            GameAudio.Master = Master;
            GameAudio.Volume[(int)SoundGroup.World] = Music;
            GameAudio.Volume[(int)SoundGroup.Weather] = Music;
            GameAudio.Volume[(int)SoundGroup.Creatures] = Effects;
            GameAudio.Volume[(int)SoundGroup.Player] = Effects;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyLook(true);

        /// <summary>To the player's camera: sensitivities and invert at once; distance (the zoom it starts at) only on a scene load or when changed here.</summary>
        static void ApplyLook(bool distanceToo)
        {
            ThirdPersonCamera.InvertY = InvertY;
            var rig = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (rig == null)
                return;
            rig.MouseSensitivity = Mouse;
            Punchy.CameraRig.ThirdPersonCamera.FirstPersonIndoors = FirstPersonIndoors;
            rig.StickSensitivity = Pad;
            if (distanceToo)
                rig.Distance = Mathf.Clamp(Distance, Punchy.CameraRig.ThirdPersonCamera.ClosestDistance, rig.MaxDistance);
            var cam = rig.GetComponent<Camera>();
            if (cam != null)
                cam.fieldOfView = Fov;
        }

        static GUIStyle title;

        static float Slider(Punchy.UI.MenuNav nav, string label, string shown, float value, float min, float max, float step)
        {
            GUILayout.Label($"{label}: {shown}");
            return Mathf.Round(nav.Slider(value, min, max, step) / step) * step;
        }

        static void DrawSound(Punchy.UI.MenuNav nav)
        {
            title ??= new GUIStyle(GUI.skin.label) { richText = true };
            GUILayout.Label("<b>Sound</b>", title);
            GUI.changed = false;
            var master = Slider(nav, "Master", Percent(Master), Master, 0f, 1f, 0.05f);
            var music = Slider(nav, "Music and ambience", Percent(Music), Music, 0f, 1f, 0.05f);
            var effects = Slider(nav, "Effects", Percent(Effects), Effects, 0f, 1f, 0.05f);
            if (GUI.changed)
            {
                (Master, Music, Effects) = (master, music, effects);
                Clamp();
                ApplySound();
                Save();
            }
            GUILayout.Space(6);
            if (nav.Button("Reset sound"))
            {
                (Master, Music, Effects) = (1f, 1f, 1f);
                ApplySound();
                Save();
            }
        }

        static void DrawLook(Punchy.UI.MenuNav nav)
        {
            title ??= new GUIStyle(GUI.skin.label) { richText = true };
            GUILayout.Label("<b>Look</b>", title);
            GUI.changed = false;
            var mouse = Slider(nav, "Mouse sensitivity", Mouse.ToString("0.00", CultureInfo.InvariantCulture), Mouse, 0.03f, 0.5f, 0.01f);
            var pad = Slider(nav, "Pad sensitivity", Pad.ToString("0", CultureInfo.InvariantCulture), Pad, 60f, 400f, 10f);
            var distance = Slider(nav, "Camera distance", Distance < Punchy.CameraRig.ThirdPersonCamera.FirstPersonBelow ? "first person" : $"{Distance.ToString("0.0", CultureInfo.InvariantCulture)} m",
                                  Distance, Punchy.CameraRig.ThirdPersonCamera.ClosestDistance, 20f, 0.5f);
            var fov = Slider(nav, "Field of view", $"{Fov:0} deg", Fov, 50f, 100f, 1f);
            var changed = GUI.changed;
            var distanceChanged = !Mathf.Approximately(distance, Distance);
            if (nav.Button($"Invert Y: {(InvertY ? "on" : "off")}"))
            {
                InvertY = !InvertY;
                changed = true;
            }
            if (nav.Button($"First person indoors: {(FirstPersonIndoors ? "on" : "off")}"))
            {
                FirstPersonIndoors = !FirstPersonIndoors;
                changed = true;
            }
            if (changed)
            {
                (Mouse, Pad, Distance, Fov) = (mouse, pad, distance, fov);
                Clamp();
                ApplyLook(distanceChanged);
                Save();
            }
            GUILayout.Space(6);
            if (nav.Button("Reset look"))
            {
                (Mouse, Pad, InvertY, Distance, Fov) = (DefaultMouse, DefaultPad, false, DefaultDistance, DefaultFov);
                FirstPersonIndoors = false;
                ApplyLook(true);
                Save();
            }
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
    }
}
