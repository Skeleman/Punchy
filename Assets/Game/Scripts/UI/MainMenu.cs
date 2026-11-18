using System;
using System.Collections.Generic;
using Punchy.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// The main menu (UI-1). Esc, or Start on a controller, opens and closes it. It holds the out-of-world things
    /// (P-39, #397): a tab for each (Character, Multiplayer from NetMenu, Graphics from GraphicsSettings...; each adds its
    /// own with AddTab) and Exit game at the foot. In-world things (items, quests, status) are Select's FieldMenu, which
    /// shares its MenuTabs. Controller or keyboard: the d-pad or the arrows move (MenuNav), A / Cross chooses, B / Circle
    /// backs out (cancels the exit question, otherwise closes), LB / RB (Q / E on the keyboard) switches tab. A text field chosen
    /// on the controller opens the TextWheel to type in it. The mouse clicks
    /// everything as before. While it's open, Overlay.AnyOpen holds the camera, frees the cursor and stops the player's
    /// actions (their action map is off).
    /// Test flags: "-menu &lt;Tab&gt;" opens it on that tab (for shots); with "-forcepad" (InputGlyphs) its prompts show a pad's.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        /// <summary>The menu is showing.</summary>
        public static bool Open { get; private set; }

        /// <summary>The tabs; the one showing is kept by title, so it stays put when tabs come and go (NetMenu's, across a join).</summary>
        static readonly MenuTabs tabs = new MenuTabs();
        static MainMenu instance;

        readonly MenuNav nav = new MenuNav();
        readonly TextWheel wheel = new TextWheel();
        bool confirmQuit;
        int openedFrame = -1;
        InputAction cancel;
        GUIStyle box, heading, footer, exit;

        /// <summary>
        /// Adds a tab, or replaces the one with that title (NetMenu adds its own again after a join reloads the scene).
        /// Tabs show in Order.
        /// </summary>
        public static void AddTab(string title, int order, Action<MenuNav> draw) => tabs.Add(title, order, draw);

        public static void RemoveTab(string title) => tabs.Remove(title);

        /// <summary>Opens the menu (on the titled tab, when given) or closes it: shot lists' "-menu Title" lines.</summary>
        public static void Show(string title, bool open)
        {
            if (instance == null)
                return;
            if (title != null)
                tabs.CurrentTitle = title;
            instance.SetOpen(open);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Open = false;
            tabs.CurrentTitle = null;
            if (instance != null)
                return;
            var go = new GameObject("MainMenu");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<MainMenu>();
        }

        void Awake()
        {
            MenuNav.TextEntry = wheel.Begin;
            tabs.Changed = () =>
            {
                confirmQuit = false;
                nav.Reset();
            };
        }

        void OnDestroy()
        {
            if (MenuNav.TextEntry == (Action<string, int, string, Action<string>>)wheel.Begin)
                MenuNav.TextEntry = null;
        }

        // P-20 (profiler finding 11): drawn through a GuiHook, on only while there is something to draw, so this
        // script stays out of Unity's IMGUI pass (and its per-frame garbage) the rest of the time.
        void Start()
        {
            Punchy.UI.GuiHook.Add(gameObject, DrawGui, () => Open, true);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = Array.IndexOf(args, "-menu");
            if (at >= 0)
            {
                if (at + 1 < args.Length && !args[at + 1].StartsWith("-"))
                    tabs.CurrentTitle = args[at + 1];
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
        public static void SetPlayerActions(bool on)
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
                // (Esc or B during a talk ends the talk: FolkTalk.)
                if (!Overlay.Talking && (start || keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
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
            // Back (B / Circle / Esc): out of a text field, then out of the exit question, then the menu closes.
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
            tabs.ReadInput();
            nav.ReadInput();
        }

        void DrawGui()
        {
            if (!Open)
                return;
            box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(12, 12, 10, 10) };
            heading ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            footer ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
            exit ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 14 };

            nav.Begin();
            // (Behind the text wheel the menu is drawn, but greyed and not clickable.)
            var enabled = GUI.enabled;
            GUI.enabled = enabled && !wheel.Open;
            const float width = 420f, height = 500f;
            GUILayout.BeginArea(new Rect(Mathf.Round((Screen.width - width) * 0.5f), 50f, width, height), box);

            // The tab strip (clicked with the mouse, or switched with the shoulder buttons / Q E: not focus stops).
            tabs.DrawStrip();
            GUILayout.Space(8);
            tabs.DrawCurrent(nav);

            GUILayout.FlexibleSpace();
            // Exit game, on every tab: full width, bold and red so it's easy to find. It asks first; Cancel comes
            // first in the question, so the focus (which stays where Exit game was) lands on it and A A doesn't quit.
            var colour = GUI.backgroundColor;
            GUI.backgroundColor = ExitColour;
            if (!confirmQuit)
            {
                if (nav.Button("Exit game", exit, GUILayout.Height(30)))
                    confirmQuit = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Exit the game?", heading, GUILayout.Height(30));
                GUI.backgroundColor = colour;
                if (nav.Button("Cancel", exit, GUILayout.Height(30), GUILayout.Width(90)))
                    confirmQuit = false;
                GUI.backgroundColor = ExitColour;
                if (nav.Button("Exit", exit, GUILayout.Height(30), GUILayout.Width(90)))
                {
                    PlayerPrefs.Save();
                    Quit();
                }
                GUILayout.EndHorizontal();
            }
            GUI.backgroundColor = colour;
            GUILayout.Space(6);
            Prompts.Layout(FooterPrompt(), footer);
            GUILayout.EndArea();
            GUI.enabled = enabled;
            nav.End();
            wheel.Draw(new Vector2(Mathf.Round(Screen.width * 0.5f), 50f + height * 0.5f));
        }

        static string FooterPrompt() => InputGlyphs.Gamepad
            ? $"{InputGlyphs.Token("<Gamepad>/buttonSouth")} choose    {InputGlyphs.Token("<Gamepad>/buttonEast")} back    {InputGlyphs.Token("<Gamepad>/start")} close"
            : $"{InputGlyphs.Token("<Keyboard>/enter")} choose    {InputGlyphs.Token("<Keyboard>/escape")} back / close";

        static readonly Color ExitColour = new Color(1f, 0.45f, 0.4f);

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
