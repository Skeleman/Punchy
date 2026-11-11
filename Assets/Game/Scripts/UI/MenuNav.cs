using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// Controller and keyboard focus for the IMGUI main menu. IMGUI only gives keyboard focus to text fields, so the menu's
    /// controls (buttons, toggles, sliders, text fields) go through this instead: each one drawn becomes a focus stop, in
    /// draw order. The UI action map's Navigate (d-pad, left stick, arrows/WASD) moves the focus, with key repeat; left
    /// and right step a focused slider. Submit (A / Cross / Enter) presses the focused control. The mouse works as it
    /// always has. The focus only shows once the menu has been navigated, and hides again on a mouse click.
    /// P-32: the focus moves by the layout, not the draw order. Each stop's screen rect is kept from the last Repaint;
    /// up/down goes to the nearest row in that direction (its cell closest across), wrapping to the far end when there
    /// is none, and left/right to the nearest stop in the same row (staying put at a row's end). Until rects are known
    /// (the first frame), the old flat order. "-navtest" drives it for tests (see TestPress).
    /// Presses act in the Layout pass (the first pass of each frame's OnGUI), so a press that changes what's drawn changes
    /// it for the whole frame, the way a mouse click does. A press in the Repaint pass would leave Repaint drawing
    /// different controls from the ones Layout laid out.
    /// </summary>
    public class MenuNav
    {
        /// <summary>
        /// The on-screen text entry, for playing with only a controller (a Steam Deck): the main menu sets it to its
        /// TextWheel. Called with the field's current text, its longest length and the characters it takes (null:
        /// any), plus a callback for the result (null: cancelled). While unset, a text field chosen on a controller
        /// says that typing needs a keyboard.
        /// </summary>
        public static Action<string, int, string, Action<string>> TextEntry;

        /// <summary>The focused stop, and how many stops the last full pass drew.</summary>
        public int Focus;
        int count, drawing;
        /// <summary>The focus is showing (the menu has been navigated since the last mouse click).</summary>
        public bool Active;

        // Input caught in Update, used up in the next Layout pass: moves by layout (moveX/moveY: right/down positive),
        // and left/right steps a focused slider takes first.
        bool submit;
        int moveX, moveY, slide;
        // Each stop's rect on screen from the last Repaint, and how many stops that pass drew.
        Rect[] rects = new Rect[32];
        int rectCount;
        Vector2 heldDirection;
        float repeatAt;
        bool ignoreHeld;

        // A text entry's result waiting for its field, and which stop asked.
        string enteredText;
        int enteredFor = -1;
        /// <summary>When "typing needs a keyboard" was last asked for, for the field's notice.</summary>
        float noKeyboardUntil;
        int noKeyboardFor = -1;

        InputAction navigate, submitAction;

        /// <summary>Reads the menu's navigation input; called from the menu's Update while it's open.</summary>
        public void ReadInput()
        {
            var actions = InputSystem.actions;
            navigate ??= actions?.FindAction("UI/Navigate");
            submitAction ??= actions?.FindAction("UI/Submit");
            // (Not while a text field is being typed in: there the arrows, WASD and Enter belong to the field.)
            if (GUIUtility.keyboardControl != 0)
            {
                heldDirection = Vector2.zero;
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                Active = false;
            var direction = navigate != null ? navigate.ReadValue<Vector2>() : Vector2.zero;
            if (TestPress(out var pressed, out var pressSubmit))
                direction = pressed;
            // One step on a fresh push, then repeats after a moment while held.
            var dir = Mathf.Abs(direction.y) >= Mathf.Abs(direction.x) ? new Vector2(0f, Mathf.Abs(direction.y) > 0.5f ? Mathf.Sign(direction.y) : 0f)
                                                                       : new Vector2(Mathf.Abs(direction.x) > 0.5f ? Mathf.Sign(direction.x) : 0f, 0f);
            var now = Time.unscaledTime;
            var fire = false;
            if (ignoreHeld)
            {
                // (A direction still held from before, the stick pointing in the text wheel, isn't a push.)
                ignoreHeld = false;
                heldDirection = dir;
                repeatAt = float.MaxValue;
            }
            else if (dir != heldDirection)
            {
                heldDirection = dir;
                fire = dir != Vector2.zero;
                repeatAt = now + 0.4f;
            }
            else if (dir != Vector2.zero && now >= repeatAt)
            {
                fire = true;
                repeatAt = now + 0.12f;
            }
            if (fire)
            {
                if (!Active)
                    Active = true;
                else if (dir.y != 0f)
                    moveY -= (int)dir.y;
                else
                    slide += (int)dir.x;
            }
            if ((submitAction != null && submitAction.WasPressedThisFrame()) || pressSubmit)
            {
                if (Active)
                    submit = true;
                Active = true;
            }
        }

        /// <summary>Starts a pass of the menu's OnGUI. In Layout, uses up the navigation caught since the last frame.</summary>
        public void Begin()
        {
            drawing = 0;
            if (Event.current.type != EventType.Layout)
                return;
            if (count > 0 && (moveX != 0 || moveY != 0))
            {
                if (rectCount == count)
                {
                    for (int i = 0; i < Mathf.Abs(moveY); i++)
                        Focus = Spatial(Focus, 0, Math.Sign(moveY));
                    for (int i = 0; i < Mathf.Abs(moveX); i++)
                        Focus = Spatial(Focus, Math.Sign(moveX), 0);
                }
                else
                    Focus = ((Focus + moveY + moveX) % count + count) % count;
            }
            moveX = moveY = 0;
        }

        /// <summary>
        /// The stop one push from <paramref name="from"/> by the layout: down/up (dy +1/-1) to the nearest row below or
        /// above, the cell closest across (overlapping first, then by centres), else wrapping to the far row; right/left
        /// (dx +1/-1) to the nearest stop in the same row (overlapping it vertically), else staying put.
        /// </summary>
        int Spatial(int from, int dx, int dy)
        {
            if (from < 0 || from >= rectCount)
                return from;
            var a = rects[from];
            var best = from;
            if (dy != 0)
            {
                // The nearest row in that direction: the least distance down (or up) between centres, past a few pixels.
                var nearest = float.MaxValue;
                for (int i = 0; i < rectCount; i++)
                {
                    var along = (rects[i].center.y - a.center.y) * dy;
                    if (i != from && along > 4f)
                        nearest = Mathf.Min(nearest, along);
                }
                // None: wrap, as the flat order did, to the row furthest the other way.
                var wrap = nearest == float.MaxValue;
                if (wrap)
                    for (int i = 0; i < rectCount; i++)
                        if (i != from)
                            nearest = Mathf.Min(nearest, rects[i].center.y * dy);
                var bestGap = float.MaxValue;
                var bestSide = float.MaxValue;
                for (int i = 0; i < rectCount; i++)
                {
                    if (i == from)
                        continue;
                    var r = rects[i];
                    var along = wrap ? r.center.y * dy : (r.center.y - a.center.y) * dy;
                    if (!wrap && along <= 4f)
                        continue;
                    // (Same row: centres within half the smaller height of the nearest one.)
                    if (along - nearest > Mathf.Max(4f, Mathf.Min(a.height, r.height) * 0.5f))
                        continue;
                    var gap = Mathf.Max(0f, Mathf.Max(a.xMin, r.xMin) - Mathf.Min(a.xMax, r.xMax));
                    var side = Mathf.Abs(r.center.x - a.center.x);
                    if (gap < bestGap - 0.5f || (gap < bestGap + 0.5f && side < bestSide))
                    {
                        best = i;
                        bestGap = gap;
                        bestSide = side;
                    }
                }
                return best;
            }
            var closest = float.MaxValue;
            for (int i = 0; i < rectCount; i++)
            {
                if (i == from)
                    continue;
                var r = rects[i];
                var overlap = Mathf.Min(a.yMax, r.yMax) - Mathf.Max(a.yMin, r.yMin);
                var across = (r.center.x - a.center.x) * dx;
                if (overlap < Mathf.Min(a.height, r.height) * 0.5f || across <= 1f)
                    continue;
                if (across < closest)
                {
                    best = i;
                    closest = across;
                }
            }
            return best;
        }

        /// <summary>Ends a pass: the stops counted, and anything not used this frame dropped.</summary>
        public void End()
        {
            if (Event.current.type == EventType.Repaint)
                rectCount = drawing;
            if (Event.current.type != EventType.Layout)
                return;
            count = drawing;
            if (count > 0)
                Focus = Mathf.Clamp(Focus, 0, count - 1);
            submit = false;
            // (Left/right on a stop that isn't a slider moves the focus instead, next frame.)
            moveX += slide;
            slide = 0;
        }

        /// <summary>Whatever direction is held now doesn't move the focus until it's let go (after the text wheel).</summary>
        public void IgnoreHeld() => ignoreHeld = true;

        /// <summary>Back to the first stop (on opening the menu or changing tab).</summary>
        public void Reset()
        {
            Focus = 0;
            moveX = moveY = slide = 0;
            submit = false;
        }

        /// <summary>Keeps the stop just drawn's rect on screen (in Repaint, where layout rects are final).</summary>
        void Drawn()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var i = drawing - 1;
            if (i >= rects.Length)
                Array.Resize(ref rects, rects.Length * 2);
            rects[i] = GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
        }

        /// <summary>The focused stop's rect on screen, as last drawn (for tests); false before it's known.</summary>
        public bool FocusRect(out Rect rect)
        {
            rect = Focus >= 0 && Focus < rectCount ? rects[Focus] : default;
            return Focus >= 0 && Focus < rectCount;
        }

        bool Next(out bool pressed)
        {
            var focused = Active && drawing == Focus;
            drawing++;
            pressed = focused && submit && Event.current.type == EventType.Layout;
            if (pressed)
                submit = false;
            return focused;
        }

        /// <summary>A button: true when clicked, or pressed while focused.</summary>
        public bool Button(string text, params GUILayoutOption[] options) => Button(text, GUI.skin.button, options);

        /// <summary>A button in a style of its own (the menu's Exit game).</summary>
        public bool Button(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            var focused = Next(out var pressed);
            var colour = GUI.backgroundColor;
            if (focused)
                GUI.backgroundColor = Highlight;
            var clicked = GUILayout.Button(text, style, options);
            Drawn();
            GUI.backgroundColor = colour;
            return clicked || pressed;
        }

        /// <summary>A toggle drawn as a button (a preset): true when chosen.</summary>
        public bool Toggle(bool on, string text, GUIStyle style, params GUILayoutOption[] options)
        {
            var focused = Next(out var pressed);
            var colour = GUI.backgroundColor;
            if (focused)
                GUI.backgroundColor = Highlight;
            var now = GUILayout.Toggle(on, text, style, options);
            Drawn();
            GUI.backgroundColor = colour;
            return pressed || now;
        }

        /// <summary>A slider; while focused, left and right step it by <paramref name="stepBy"/>.</summary>
        public float Slider(float value, float min, float max, float stepBy, params GUILayoutOption[] options)
        {
            var focused = Next(out _);
            if (focused && slide != 0 && Event.current.type == EventType.Layout)
            {
                value = Mathf.Clamp(value + slide * stepBy, min, max);
                slide = 0;
                GUI.changed = true;
            }
            var colour = GUI.backgroundColor;
            if (focused)
                GUI.backgroundColor = Highlight;
            value = GUILayout.HorizontalSlider(value, min, max, options);
            Drawn();
            GUI.backgroundColor = colour;
            return value;
        }

        /// <summary>
        /// A text field. Clicked, it types as usual. Chosen with the controller, it asks <see cref="TextEntry"/> for the
        /// text, or says it needs a keyboard while there's none.
        /// </summary>
        public string TextField(string text, int maxLength, string allowed, params GUILayoutOption[] options)
        {
            var me = drawing;
            var focused = Next(out var pressed);
            if (enteredFor == me && Event.current.type == EventType.Layout)
            {
                text = enteredText;
                enteredFor = -1;
            }
            if (pressed)
            {
                if (TextEntry != null)
                    TextEntry(text, maxLength, allowed, result =>
                    {
                        if (result == null)
                            return;
                        enteredText = result;
                        enteredFor = me;
                    });
                else
                {
                    noKeyboardFor = me;
                    noKeyboardUntil = Time.unscaledTime + 3f;
                }
            }
            var colour = GUI.backgroundColor;
            if (focused)
                GUI.backgroundColor = Highlight;
            text = maxLength > 0 ? GUILayout.TextField(text, maxLength, options) : GUILayout.TextField(text, options);
            Drawn();
            GUI.backgroundColor = colour;
            return text;
        }

        /// <summary>Whether the text field just drawn should say that typing needs a keyboard.</summary>
        public bool NeedsKeyboardNotice => noKeyboardFor == drawing - 1 && Time.unscaledTime < noKeyboardUntil;

        static readonly Color Highlight = new Color(1f, 0.82f, 0.3f);

        // "-navtest U,D,L,R,A,..." (P-32): once a menu reads input, presses those in turn, 1.2 s apart from 6 s in, and
        // logs where the focus went; with "-navshot <dir>", a screenshot after each. For shots and checks of the layout.
        static string[] testKeys;
        static string testShots;
        static int testIndex;
        static float testNextAt = -1f, testLogAt = -1f;
        static MenuNav testLast;

        static MenuNav()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = Array.IndexOf(args, "-navtest");
            if (at >= 0 && at + 1 < args.Length)
                testKeys = args[at + 1].Split(',');
            at = Array.IndexOf(args, "-navshot");
            if (at >= 0 && at + 1 < args.Length)
                testShots = args[at + 1];
        }

        /// <summary>The next "-navtest" press due this frame, as a Navigate direction or Submit; and the log line and
        /// shot of the one before. False without the flag.</summary>
        bool TestPress(out Vector2 direction, out bool submitNow)
        {
            direction = Vector2.zero;
            submitNow = false;
            if (testKeys == null)
                return false;
            var now = Time.unscaledTime;
            if (testNextAt < 0f)
                testNextAt = now + 6f;
            if (testLogAt >= 0f && now >= testLogAt && testLast == this)
            {
                testLogAt = -1f;
                var key = testKeys[testIndex - 1];
                var where = FocusRect(out var r) ? $"({r.center.x:0}, {r.center.y:0}) {r.width:0}x{r.height:0}" : "(no rect)";
                Debug.Log($"MenuNav test: {testIndex} {key}: focus {Focus} of {count} at {where}");
                if (testShots != null)
                {
                    var path = System.IO.Path.Combine(testShots, $"nav-{testIndex:00}-{key}.png");
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log($"MenuNav test: shot {path}");
                }
            }
            if (testIndex >= testKeys.Length || now < testNextAt)
                return true;
            var k = testKeys[testIndex++].Trim().ToUpperInvariant();
            testNextAt = now + 1.2f;
            testLogAt = now + 0.6f;
            testLast = this;
            switch (k)
            {
                case "U": direction = Vector2.up; break;
                case "D": direction = Vector2.down; break;
                case "L": direction = Vector2.left; break;
                case "R": direction = Vector2.right; break;
                case "A": submitNow = true; break;
            }
            return true;
        }
    }
}
