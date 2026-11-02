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
    /// Presses act in the Layout pass (the first pass of each frame's OnGUI), so a press that changes what's drawn changes
    /// it for the whole frame, the way a mouse click does. A press in the Repaint pass would leave Repaint drawing
    /// different controls from the ones Layout laid out.
    /// </summary>
    public class MenuNav
    {
        /// <summary>
        /// Where an on-screen text entry (a digit pad for the IP, letters for a join code) can plug in later, for
        /// playing with only a controller (a Steam Deck). Called with the field's current text, its longest length and
        /// the characters it takes (null: any), plus a callback for the result (null: cancelled). While unset, a text
        /// field chosen on a controller says that typing needs a keyboard.
        /// </summary>
        public static Action<string, int, string, Action<string>> TextEntry;

        /// <summary>The focused stop, and how many stops the last full pass drew.</summary>
        public int Focus;
        int count, drawing;
        /// <summary>The focus is showing (the menu has been navigated since the last mouse click).</summary>
        public bool Active;

        // Input caught in Update, used up in the next Layout pass.
        bool submit;
        int step, slide;
        Vector2 heldDirection;
        float repeatAt;

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
            // One step on a fresh push, then repeats after a moment while held.
            var dir = Mathf.Abs(direction.y) >= Mathf.Abs(direction.x) ? new Vector2(0f, Mathf.Abs(direction.y) > 0.5f ? Mathf.Sign(direction.y) : 0f)
                                                                       : new Vector2(Mathf.Abs(direction.x) > 0.5f ? Mathf.Sign(direction.x) : 0f, 0f);
            var now = Time.unscaledTime;
            var fire = false;
            if (dir != heldDirection)
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
                    step -= (int)dir.y;
                else
                    slide += (int)dir.x;
            }
            if (submitAction != null && submitAction.WasPressedThisFrame())
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
            if (count > 0 && step != 0)
                Focus = ((Focus + step) % count + count) % count;
            step = 0;
        }

        /// <summary>Ends a pass: the stops counted, and anything not used this frame dropped.</summary>
        public void End()
        {
            if (Event.current.type != EventType.Layout)
                return;
            count = drawing;
            if (count > 0)
                Focus = Mathf.Clamp(Focus, 0, count - 1);
            submit = false;
            // (Left/right on a stop that isn't a slider moves the focus instead, next frame.)
            step += slide;
            slide = 0;
        }

        /// <summary>Back to the first stop (on opening the menu or changing tab).</summary>
        public void Reset()
        {
            Focus = 0;
            step = slide = 0;
            submit = false;
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
        public bool Button(string text, params GUILayoutOption[] options)
        {
            var focused = Next(out var pressed);
            var colour = GUI.backgroundColor;
            if (focused)
                GUI.backgroundColor = Highlight;
            var clicked = GUILayout.Button(text, options);
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
            GUI.backgroundColor = colour;
            return text;
        }

        /// <summary>Whether the text field just drawn should say that typing needs a keyboard.</summary>
        public bool NeedsKeyboardNotice => noKeyboardFor == drawing - 1 && Time.unscaledTime < noKeyboardUntil;

        static readonly Color Highlight = new Color(1f, 0.82f, 0.3f);
    }
}
