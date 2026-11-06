using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.UI
{
    /// <summary>
    /// A script's OnGUI, carried by its own small component that is enabled only while the script has something to draw
    /// (P-20, profiler finding 11). Unity runs its IMGUI pass, and allocates for it (about 3.3 KB a frame), whenever any
    /// enabled MonoBehaviour has an OnGUI, even one that returns at once; a disabled one costs nothing. The owner keeps
    /// its drawing in a plain method and gives a test for "something to draw"; GuiHooks checks the tests once a frame.
    /// "layout" false (the hook draws with GUI.*, no GUILayout) also skips IMGUI's layout event.
    /// </summary>
    public class GuiHook : MonoBehaviour
    {
        Action draw;
        Func<bool> wanted;
        static readonly List<GuiHook> hooks = new List<GuiHook>();

        /// <summary>Adds the hook to the owner's object; draw and wanted are made once (keep them free of per-frame allocations).</summary>
        public static GuiHook Add(GameObject on, Action draw, Func<bool> wanted, bool layout)
        {
            var hook = on.AddComponent<GuiHook>();
            hook.draw = draw;
            hook.wanted = wanted;
            hook.useGUILayout = layout;
            hook.enabled = wanted();
            hooks.Add(hook);
            GuiHooks.Ensure();
            return hook;
        }

        void OnDestroy() => hooks.Remove(this);

        void OnGUI() => draw();

        /// <summary>Each hook on while its owner has something to draw (GuiHooks, once a frame after every Update).</summary>
        internal static void Refresh()
        {
            for (int i = 0; i < hooks.Count; i++)
            {
                var hook = hooks[i];
                var on = hook.wanted();
                if (hook.enabled != on)
                    hook.enabled = on;
            }
        }
    }
}
