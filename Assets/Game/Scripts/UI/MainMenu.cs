using System;
using System.Collections.Generic;
using Punchy.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// The main menu (UI-1). Esc, or Start on a controller, opens and closes it. It has a tab for each player-facing
    /// panel (Multiplayer from NetMenu, Graphics from GraphicsSettings; each adds its own with AddTab) and Quit at the
    /// foot. Controller or keyboard: the d-pad or left stick moves (MenuNav), A / Cross chooses, B / Circle backs out
    /// (cancels the quit question, otherwise closes), LB / RB (Q / E on the keyboard) switches tab. A text field chosen
    /// on the controller opens the TextWheel to type in it. The mouse clicks
    /// everything as before. While it's open, Overlay.AnyOpen holds the camera, frees the cursor and stops the player's
    /// actions (their action map is off).
    /// Test flags: "-menu &lt;Tab&gt;" opens it on that tab (for shots); with "-forcepad" (InputGlyphs) its prompts show a pad's.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        /// <summary>The menu is showing.</summary>
        public static bool Open { get; private set; }

        struct Tab
        {
            public string Title;
            public int Order;
            public Action<MenuNav> Draw;
        }

        static readonly List<Tab> tabs = new List<Tab>();
        static MainMenu instance;
        /// <summary>The tab showing, by title, so it stays put when tabs come and go (NetMenu's, across a join).</summary>
        static string currentTitle;

        readonly MenuNav nav = new MenuNav();
        readonly TextWheel wheel = new TextWheel();
        bool confirmQuit;
        int openedFrame = -1;
        InputAction cancel;
        GUIStyle box, heading, tabOn, tabOff, footer;

        /// <summary>
        /// Adds a tab, or replaces the one with that title (NetMenu adds its own again after a join reloads the scene).
        /// Tabs show in Order.
        /// </summary>
        public static void AddTab(string title, int order, Action<MenuNav> draw)
        {
            RemoveTab(title);
            tabs.Add(new Tab { Title = title, Order = order, Draw = draw });
            tabs.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public static void RemoveTab(string title) => tabs.RemoveAll(t => t.Title == title);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Open = false;
            currentTitle = null;
            if (instance != null)
                return;
            var go = new GameObject("MainMenu");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<MainMenu>();
        }

        void Awake() => MenuNav.TextEntry = wheel.Begin;

        void OnDestroy()
        {
            if (MenuNav.TextEntry == (Action<string, int, string, Action<string>>)wheel.Begin)
                MenuNav.TextEntry = null;
        }

        void Start()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = Array.IndexOf(args, "-menu");
            if (at >= 0)
            {
                if (at + 1 < args.Length && !args[at + 1].StartsWith("-"))
                    currentTitle = args[at + 1];
                SetOpen(true);
            }
        }

        void SetOpen(bool open)
        {
            if (open == Open)
                return;
            Open = open;
            wheel.Close(false);
            confirmQuit = false;
            nav.Reset();
            nav.Active = open && InputGlyphs.Gamepad;
            GUIUtility.keyboardControl = 0;
            if (open)
            {
                openedFrame = Time.frameCount;
                // (The menu's own actions: Navigate, Submit, Cancel.)
                InputSystem.actions?.FindActionMap("UI")?.Enable();
            }
            else
                PlayerPrefs.Save();
            SetPlayerActions(!open);
        }

        /// <summary>The player's own action map, off while the menu is open (so A doesn't jump, nor the stick walk).</summary>
        static void SetPlayerActions(bool on)
        {
            var map = InputSystem.actions?.FindActionMap("Player");
            if (map == null || map.enabled == on)
                return;
            if (on)
                map.Enable();
            else
                map.Disable();
        }

        void Update()
        {
            InputGlyphs.Update();
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            var start = pad != null && pad.startButton.wasPressedThisFrame;
            if (!Open)
            {
                if (start || keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    SetOpen(true);
                return;
            }
            // (The player systems turn their map back on when a join rebuilds the world: keep it off.)
            SetPlayerActions(false);
            if (Time.frameCount == openedFrame)
                return;
            // The text wheel (typing on a controller) has the input while it's open, and the frame it closes.
            if (wheel.Busy)
            {
                wheel.Update();
                if (!wheel.Open)
                    nav.IgnoreHeld();
                return;
            }
            if (start)
            {
                SetOpen(false);
                return;
            }
            // Back (B / Circle / Esc): out of a text field, then out of the quit question, then the menu closes.
            cancel ??= InputSystem.actions?.FindAction("UI/Cancel");
            if (cancel != null && cancel.WasPressedThisFrame())
            {
                if (GUIUtility.keyboardControl != 0)
                    GUIUtility.keyboardControl = 0;
                else if (confirmQuit)
                    confirmQuit = false;
                else
                {
                    SetOpen(false);
                    return;
                }
            }
            // Tabs: the shoulder buttons, or Q / E (not while typing in a field).
            var typing = GUIUtility.keyboardControl != 0;
            if (pad != null && pad.leftShoulder.wasPressedThisFrame || !typing && keyboard != null && keyboard.qKey.wasPressedThisFrame)
                SwitchTab(-1);
            if (pad != null && pad.rightShoulder.wasPressedThisFrame || !typing && keyboard != null && keyboard.eKey.wasPressedThisFrame)
                SwitchTab(1);
            nav.ReadInput();
        }

        int Current()
        {
            var i = tabs.FindIndex(t => t.Title == currentTitle);
            return i >= 0 ? i : 0;
        }

        void SwitchTab(int by)
        {
            if (tabs.Count == 0)
                return;
            currentTitle = tabs[((Current() + by) % tabs.Count + tabs.Count) % tabs.Count].Title;
            confirmQuit = false;
            nav.Reset();
        }

        void OnGUI()
        {
            if (!Open)
                return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(12, 12, 10, 10) };
            heading ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            tabOn ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            tabOff ??= new GUIStyle(GUI.skin.button);
            footer ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };

            nav.Begin();
            // (Behind the text wheel the menu is drawn, but greyed and not clickable.)
            var enabled = GUI.enabled;
            GUI.enabled = enabled && !wheel.Open;
            const float width = 420f, height = 500f;
            GUILayout.BeginArea(new Rect(Mathf.Round((Screen.width - width) * 0.5f), 50f, width, height), box);

            // The tab strip (clicked with the mouse, or switched with the shoulder buttons / Q E: not focus stops).
            var current = Current();
            GUILayout.BeginHorizontal();
            Prompts.Layout(TabKey(-1), footer, GUILayout.ExpandWidth(false));
            for (int i = 0; i < tabs.Count; i++)
                if (GUILayout.Button(tabs[i].Title, i == current ? tabOn : tabOff) && i != current)
                {
                    currentTitle = tabs[i].Title;
                    confirmQuit = false;
                    nav.Reset();
                }
            Prompts.Layout(TabKey(1), footer, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (tabs.Count > 0)
            {
                currentTitle = tabs[current].Title;
                tabs[current].Draw(nav);
            }

            GUILayout.FlexibleSpace();
            // Quit, for every tab (it asks first).
            if (!confirmQuit)
            {
                if (nav.Button("Quit game"))
                    confirmQuit = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Quit the game?");
                if (nav.Button("Quit"))
                {
                    PlayerPrefs.Save();
                    Quit();
                }
                if (nav.Button("Cancel"))
                    confirmQuit = false;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            Prompts.Layout(FooterPrompt(), footer);
            GUILayout.EndArea();
            GUI.enabled = enabled;
            nav.End();
            wheel.Draw(new Vector2(Mathf.Round(Screen.width * 0.5f), 50f + height * 0.5f));
        }

        static string TabKey(int side) => InputGlyphs.Token(InputGlyphs.Gamepad ? side < 0 ? "<Gamepad>/leftShoulder" : "<Gamepad>/rightShoulder"
                                                                                 : side < 0 ? "<Keyboard>/q" : "<Keyboard>/e");

        static string FooterPrompt() => InputGlyphs.Gamepad
            ? $"{InputGlyphs.Token("<Gamepad>/buttonSouth")} choose    {InputGlyphs.Token("<Gamepad>/buttonEast")} back    {InputGlyphs.Token("<Gamepad>/start")} close"
            : $"{InputGlyphs.Token("<Keyboard>/enter")} choose    {InputGlyphs.Token("<Keyboard>/escape")} back / close";

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
