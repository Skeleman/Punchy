using System;
using Punchy.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// The in-world menu (P-39, #397): Select (View) on a pad, or Tab / I on the keyboard, opens and closes it; B /
    /// Circle closes it too. It holds the in-world things, a tab each from the systems that own them (Items and Quests
    /// from Items, Status from PlayerStats), on the same MenuTabs as Start's MainMenu, which keeps the out-of-world
    /// things. Unlike the main menu it doesn't stop the world: the left stick and WASD still walk while the d-pad and
    /// arrows move the focus (MenuNav) and LB / RB (Q / E) switch tab. While it's open, Overlay.FieldMenu holds the
    /// game's button actions.
    /// Tabs draw where they like on the screen (the inventory keeps its place on the right); AddPanelTab gives a
    /// GUILayout tab the standard box, under the strip.
    /// Test flag: "-fieldmenu &lt;Tab&gt;" opens it on that tab (for shots).
    /// </summary>
    public class FieldMenu : MonoBehaviour
    {
        /// <summary>The menu is showing.</summary>
        public static bool Open { get; private set; }

        /// <summary>The focus, shared by the tabs (the one showing draws its stops into it).</summary>
        public static readonly MenuNav Nav = new MenuNav();

        static readonly MenuTabs tabs = new MenuTabs();
        static FieldMenu instance;
        GUIStyle box, footer;

        /// <summary>The tab showing, by title (null while closed).</summary>
        public static string Showing => Open ? tabs.Showing : null;

        /// <summary>Adds a tab that draws itself anywhere on the screen, or replaces the one with that title.</summary>
        public static void AddTab(string title, int order, Action<MenuNav> draw) => tabs.Add(title, order, draw);

        /// <summary>Adds a GUILayout tab drawn in the menu's own box under the strip (Status).</summary>
        public static void AddPanelTab(string title, int order, Action<MenuNav> draw) => tabs.Add(title, order, nav =>
        {
            instance.box ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(12, 12, 10, 10) };
            GUILayout.BeginArea(PanelRect, instance.box);
            draw(nav);
            GUILayout.EndArea();
        });

        public static void RemoveTab(string title) => tabs.Remove(title);

        /// <summary>Where a panel tab draws: on the right, under the strip, where the inventory stands.</summary>
        public static Rect PanelRect => new Rect(Screen.width - 440f, StripRect.yMax + 28f, 420f, Mathf.Min(480f, Screen.height - StripRect.yMax - 60f));

        static Rect StripRect => new Rect(Mathf.Round((Screen.width - 420f) * 0.5f), 12f, 420f, 34f);

        /// <summary>Opens the menu (on the titled tab, when given) or closes it: a container opening, shot flags.</summary>
        public static void Show(string title, bool open)
        {
            if (title != null)
                tabs.Select(title);
            SetOpen(open);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Open = false;
            tabs.CurrentTitle = null;
            tabs.Changed = () => Nav.Reset();
            if (instance != null)
                return;
            var go = new GameObject("FieldMenu");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<FieldMenu>();
        }

        void Start()
        {
            GuiHook.Add(gameObject, DrawGui, () => Open && !Overlay.Frozen, true);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = Array.IndexOf(args, "-fieldmenu");
            if (at >= 0)
                Show(at + 1 < args.Length && !args[at + 1].StartsWith("-") ? args[at + 1] : null, true);
            // (The old "-inventory": the menu on its Items tab.)
            else if (Array.IndexOf(args, "-inventory") >= 0)
                Show("Items", true);
        }

        static void SetOpen(bool open)
        {
            if (open == Open)
                return;
            Open = open;
            Nav.Reset();
            Nav.Active = open && InputGlyphs.Gamepad;
            Debug.Log($"FieldMenu: {(open ? $"open on {tabs.Showing}" : "closed")}");
        }

        void Update()
        {
            // (The main menu or a talk has everything while it's up; this one waits underneath.)
            if (Overlay.Frozen)
                return;
            var keyboard = Keyboard.current;
            // (Any pad's View / B, not only Gamepad.current: P-16's virtual pad wasn't current on its first press. View
            // since Zach 11-12: LB is the left hand's block.)
            bool view = false, back = false;
            foreach (var g in Gamepad.all)
            {
                view |= g.selectButton.wasPressedThisFrame;
                back |= g.buttonEast.wasPressedThisFrame;
            }
            var typing = GUIUtility.keyboardControl != 0;
            var toggle = view || !typing && keyboard != null && (keyboard.tabKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame);
            if (toggle || Open && back)
                SetOpen(toggle && !Open);
            if (!Open)
                return;
            // (The menu has the buttons, not the walk.)
            Overlay.HoldForFieldMenu();
            tabs.ReadInput();
            Nav.ReadInput();
        }

        void DrawGui()
        {
            footer ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, alignment = TextAnchor.MiddleCenter };
            Nav.Begin();
            GUILayout.BeginArea(StripRect, GUI.skin.box);
            tabs.DrawStrip();
            GUILayout.EndArea();
            Prompts.Draw(new Rect(StripRect.x, StripRect.yMax, StripRect.width, 20f), FooterPrompt(), footer, true);
            tabs.DrawCurrent(Nav);
            Nav.End();
        }

        static string FooterPrompt() => InputGlyphs.Gamepad
            ? $"{InputGlyphs.Token("<Gamepad>/buttonSouth")} choose    {InputGlyphs.Token("<Gamepad>/buttonEast")} / {InputGlyphs.Token("<Gamepad>/select")} close"
            : $"{InputGlyphs.Token("<Keyboard>/enter")} choose    {InputGlyphs.Token("<Keyboard>/tab")} close";
    }
}
