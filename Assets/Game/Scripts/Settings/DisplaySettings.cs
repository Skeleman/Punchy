using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// The screen (#78): resolution and window mode (full screen, borderless or windowed), picked in the main menu's
    /// Graphics tab, kept per machine (PlayerPrefs) and put back at start. The list is the monitor's own
    /// (Screen.resolutions), one entry per size whatever its refresh rates. The rows only pick; "Apply display" changes
    /// the screen, so dragging the slider doesn't flicker the window through every size on the way. In the editor the
    /// rows show but the Game view keeps its own size.
    /// Test flag: "-display 1280x720,windowed" (or full / borderless) applies and saves that, the same as choosing it in
    /// the menu, so a second launch without it can check it was kept.
    /// </summary>
    public static class DisplaySettings
    {
        /// <summary>The window modes, in the order the menu steps through them.</summary>
        static readonly FullScreenMode[] Modes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        const string WidthKey = "Punchy.Display.Width", HeightKey = "Punchy.Display.Height", ModeKey = "Punchy.Display.Mode";

        /// <summary>The monitor's sizes, smallest first, one per width and height.</summary>
        static readonly List<Vector2Int> sizes = new List<Vector2Int>();

        public static Vector2Int Size { get; private set; }
        public static FullScreenMode Mode { get; private set; }

        // What the menu rows have picked, applied with the Apply button.
        static int pickedSize;
        static int pickedMode;

        public static string Describe(FullScreenMode mode) => mode switch
        {
            FullScreenMode.ExclusiveFullScreen => "Full screen",
            FullScreenMode.FullScreenWindow => "Borderless",
            FullScreenMode.MaximizedWindow => "Borderless",
            _ => "Windowed",
        };

        public static string Describe() => $"{Size.x} x {Size.y} {Describe(Mode)}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            ReadSizes();
            var saved = PlayerPrefs.HasKey(WidthKey);
            Size = saved ? new Vector2Int(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey)) : new Vector2Int(Screen.width, Screen.height);
            Mode = saved ? ModeFrom(PlayerPrefs.GetInt(ModeKey, (int)Screen.fullScreenMode)) : Screen.fullScreenMode;
            var fromArgs = ReadCommandLine(Punchy.Debugging.DebugControls.LaunchArgs);
            // (A size the monitor doesn't have, from the command line or after a monitor change: the biggest one it has
            // that fits, so what's saved, applied and shown on the slider are all one of its own.)
            Size = sizes.Count > 0 ? sizes[Nearest(Size)] : Size;
            if (fromArgs)
                Save();
            if (saved || fromArgs)
                Apply();
            Debug.Log("Display: " + Describe() + (fromArgs ? " (from the command line, saved)" : saved ? " (saved, applied)" : " (Unity's own)") +
                      $", {sizes.Count} sizes: " + string.Join(" ", sizes.ConvertAll(s => $"{s.x}x{s.y}")));
            Pick();
        }

        static void ReadSizes()
        {
            sizes.Clear();
            foreach (var r in Screen.resolutions)
            {
                var s = new Vector2Int(r.width, r.height);
                if (!sizes.Contains(s))
                    sizes.Add(s);
            }
            sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        }

        static FullScreenMode ModeFrom(int value) => System.Array.IndexOf(Modes, (FullScreenMode)value) >= 0 ? (FullScreenMode)value : FullScreenMode.FullScreenWindow;

        /// <summary>The index of <paramref name="size"/>, else of the biggest (by area) that fits in it, else the smallest.</summary>
        static int Nearest(Vector2Int size)
        {
            var exact = sizes.IndexOf(size);
            if (exact >= 0)
                return exact;
            var best = 0;
            for (int i = 0; i < sizes.Count; i++)
                if (sizes[i].x <= size.x && sizes[i].y <= size.y && sizes[i].x * sizes[i].y > sizes[best].x * sizes[best].y)
                    best = i;
            return best;
        }

        /// <summary>"-display 1280x720,windowed" (full, borderless or windowed; either part may be left out).</summary>
        static bool ReadCommandLine(string[] args)
        {
            var found = false;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-display")
                    continue;
                foreach (var part in args[i + 1].ToLowerInvariant().Split(','))
                {
                    var wh = part.Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h) && w > 0 && h > 0)
                    {
                        Size = new Vector2Int(w, h);
                        found = true;
                    }
                    else if (part == "full" || part == "fullscreen")
                        (Mode, found) = (FullScreenMode.ExclusiveFullScreen, true);
                    else if (part == "borderless")
                        (Mode, found) = (FullScreenMode.FullScreenWindow, true);
                    else if (part == "windowed")
                        (Mode, found) = (FullScreenMode.Windowed, true);
                }
            }
            return found;
        }

        static void Apply()
        {
#if !UNITY_EDITOR
            Screen.SetResolution(Size.x, Size.y, Mode);
#endif
        }

        static void Save()
        {
            PlayerPrefs.SetInt(WidthKey, Size.x);
            PlayerPrefs.SetInt(HeightKey, Size.y);
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.Save();
        }

        /// <summary>Sets the menu rows back to what's applied.</summary>
        static void Pick()
        {
            pickedSize = Mathf.Max(0, sizes.IndexOf(Size));
            pickedMode = Mathf.Max(0, System.Array.IndexOf(Modes, Mode));
        }

        /// <summary>Applies and saves a size and mode, as the menu's Apply button does.</summary>
        public static void Set(Vector2Int size, FullScreenMode mode)
        {
            Size = size;
            Mode = ModeFrom((int)mode);
            Apply();
            Save();
            Pick();
            Debug.Log("Display: " + Describe() + " (chosen)");
        }

        /// <summary>The Graphics tab's display rows: resolution (a slider, left / right on the pad), window mode, Apply.</summary>
        public static void DrawRows(Punchy.UI.MenuNav nav)
        {
            if (sizes.Count == 0)
                ReadSizes();
            var size = sizes.Count > 0 ? sizes[Mathf.Clamp(pickedSize, 0, sizes.Count - 1)] : Size;
            var mode = Modes[pickedMode];
            var changed = size != Size || mode != Mode;
            GUILayout.Label($"Resolution: {size.x} x {size.y}" + (changed ? "   (not applied)" : ""));
            if (sizes.Count > 1)
                pickedSize = Mathf.RoundToInt(nav.Slider(pickedSize, 0, sizes.Count - 1, 1f));
            if (nav.Button($"Window: {Describe(mode)}"))
                pickedMode = (pickedMode + 1) % Modes.Length;
            if (nav.Button(changed ? "Apply display" : "Display applied") && changed)
                Set(size, mode);
        }
    }
}
