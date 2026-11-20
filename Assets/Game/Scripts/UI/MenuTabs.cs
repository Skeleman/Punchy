using System;
using System.Collections.Generic;
using Punchy.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// A row of titled tabs, shared by both menus (P-39, #397; rule 33): Start's MainMenu (out-of-world things:
    /// Character, Multiplayer, the settings) and Select's FieldMenu (in-world things: Items, Quests, Status). Tabs are
    /// added by the systems that own them, show in Order, and are remembered by title so the one showing stays put
    /// when tabs come and go. LB / RB, or Q / E on the keyboard when nothing is being typed, switch tab; the mouse
    /// clicks the strip.
    /// </summary>
    public class MenuTabs
    {
        struct Tab
        {
            public string Title;
            public int Order;
            public Action<MenuNav> Draw;
        }

        readonly List<Tab> tabs = new List<Tab>();
        /// <summary>The tab showing, by title.</summary>
        public string CurrentTitle;
        /// <summary>Called when the tab changes (the menus reset their focus and questions).</summary>
        public Action Changed;

        GUIStyle tabOn, tabOff, key;

        /// <summary>Adds a tab, or replaces the one with that title.</summary>
        public void Add(string title, int order, Action<MenuNav> draw)
        {
            Remove(title);
            tabs.Add(new Tab { Title = title, Order = order, Draw = draw });
            tabs.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public void Remove(string title) => tabs.RemoveAll(t => t.Title == title);

        public int Count => tabs.Count;

        /// <summary>The index of the tab showing (the first, when its title has gone).</summary>
        public int Current()
        {
            var i = tabs.FindIndex(t => t.Title == CurrentTitle);
            return i >= 0 ? i : 0;
        }

        /// <summary>The title of the tab showing, or null while there are none.</summary>
        public string Showing => tabs.Count > 0 ? tabs[Current()].Title : null;

        public void Switch(int by)
        {
            if (tabs.Count == 0)
                return;
            Select(tabs[((Current() + by) % tabs.Count + tabs.Count) % tabs.Count].Title);
        }

        public void Select(string title)
        {
            if (title == Showing && title == CurrentTitle)
                return;
            CurrentTitle = title;
            Changed?.Invoke();
        }

        /// <summary>Reads the shoulder buttons and Q / E (not while typing in a field); called from the menu's Update.</summary>
        public void ReadInput()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            var typing = GUIUtility.keyboardControl != 0;
            if (pad != null && pad.leftShoulder.wasPressedThisFrame || !typing && keyboard != null && keyboard.qKey.wasPressedThisFrame)
                Switch(-1);
            if (pad != null && pad.rightShoulder.wasPressedThisFrame || !typing && keyboard != null && keyboard.eKey.wasPressedThisFrame)
                Switch(1);
        }

        /// <summary>The strip, in a GUILayout horizontal row: the switch keys either side of a button per tab.</summary>
        public void DrawStrip()
        {
            tabOn ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            tabOff ??= new GUIStyle(GUI.skin.button);
            key ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
            var current = Current();
            GUILayout.BeginHorizontal();
            Prompts.Layout(Key(-1), key, GUILayout.ExpandWidth(false));
            for (int i = 0; i < tabs.Count; i++)
                if (GUILayout.Button(tabs[i].Title, i == current ? tabOn : tabOff) && i != current)
                    Select(tabs[i].Title);
            Prompts.Layout(Key(1), key, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        /// <summary>Draws the tab showing.</summary>
        public void DrawCurrent(MenuNav nav)
        {
            if (tabs.Count == 0)
                return;
            var current = Current();
            CurrentTitle = tabs[current].Title;
            tabs[current].Draw(nav);
        }

        static string Key(int side) => InputGlyphs.Token(InputGlyphs.Gamepad ? side < 0 ? "<Gamepad>/leftShoulder" : "<Gamepad>/rightShoulder"
                                                                              : side < 0 ? "<Keyboard>/q" : "<Keyboard>/e");
    }
}
