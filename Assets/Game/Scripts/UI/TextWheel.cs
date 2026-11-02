using System;
using System.Collections.Generic;
using System.Text;
using Punchy.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.UI
{
    /// <summary>
    /// Typing with only a controller (a Steam Deck), for the menu's text fields (#5): a wheel of characters. The left
    /// stick points at one (it stays chosen when the stick is let go), A / Cross enters it at the cursor. LB / RB (or
    /// the d-pad up and down) switch between the rings: the letters, and the digits with . : -. A field that takes
    /// only some characters (the IP address: digits and dots) gets only those. B / Circle deletes (held, it repeats);
    /// on an empty field it cancels, as does the View / Share button. Y / Triangle or Start is done. The d-pad left and
    /// right move the cursor. The mouse can click the characters, and a keyboard types into it (Enter done, Esc
    /// cancel), though with a keyboard the field itself is quicker: clicked, it types as it always has.
    /// It opens when a text field is chosen on the controller (MenuNav.TextEntry) and owns the input while it's open:
    /// the menu behind it doesn't move, switch tab or close.
    /// </summary>
    public class TextWheel
    {
        static readonly string[] AllRings = { "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "0123456789.:-" };
        static readonly string[] RingNames = { "letters", "numbers" };

        /// <summary>The wheel is showing.</summary>
        public bool Open { get; private set; }
        /// <summary>Open, or closed this frame (so the button that closed it doesn't also act on the menu).</summary>
        public bool Busy => Open || closedFrame == Time.frameCount;

        readonly StringBuilder text = new StringBuilder();
        readonly List<string> rings = new List<string>();
        readonly List<string> ringNames = new List<string>();
        string allowedAll;
        int ring, selected, cursor, maxLength, closedFrame = -1;
        Action<string> done;
        float deleteAt = -1f, leftAt = -1f, rightAt = -1f;

        GUIStyle letter, chosen, field, hint;
        static Texture2D disc;

        /// <summary>Opens the wheel on a field's text (MenuNav.TextEntry's shape).</summary>
        public void Begin(string current, int max, string allowed, Action<string> result)
        {
            rings.Clear();
            ringNames.Clear();
            for (int i = 0; i < AllRings.Length; i++)
            {
                var r = Filter(AllRings[i], allowed);
                if (r.Length == 0)
                    continue;
                rings.Add(r);
                ringNames.Add(RingNames[i]);
            }
            if (rings.Count == 0)
            {
                result?.Invoke(null);
                return;
            }
            allowedAll = string.Concat(rings);
            text.Clear().Append(current ?? "");
            cursor = text.Length;
            maxLength = max;
            done = result;
            // (A field with no letters, the IP address, has only the numbers ring.)
            ring = 0;
            selected = 0;
            deleteAt = leftAt = rightAt = -1f;
            Open = true;
            var keyboard = Keyboard.current;
            if (keyboard != null)
                keyboard.onTextInput += Typed;
        }

        static string Filter(string chars, string allowed)
        {
            if (allowed == null)
                return chars;
            var sb = new StringBuilder();
            foreach (var c in chars)
                if (allowed.IndexOf(c) >= 0 || allowed.IndexOf(char.ToLowerInvariant(c)) >= 0)
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Closes it: with the text (done), or null (cancelled, the field keeps what it had).</summary>
        public void Close(bool keep)
        {
            if (!Open)
                return;
            Open = false;
            closedFrame = Time.frameCount;
            var keyboard = Keyboard.current;
            if (keyboard != null)
                keyboard.onTextInput -= Typed;
            var callback = done;
            done = null;
            callback?.Invoke(keep ? text.ToString() : null);
        }

        void Typed(char c)
        {
            if (!Open || char.IsControl(c))
                return;
            c = char.ToUpperInvariant(c);
            if (allowedAll.IndexOf(c) >= 0)
                Insert(c);
        }

        void Insert(char c)
        {
            if (maxLength > 0 && text.Length >= maxLength)
                return;
            text.Insert(cursor, c);
            cursor++;
        }

        void Delete()
        {
            if (cursor == 0)
                return;
            text.Remove(cursor - 1, 1);
            cursor--;
        }

        void SwitchRing(int by)
        {
            if (rings.Count < 2)
                return;
            var old = rings[ring].Length;
            ring = ((ring + by) % rings.Count + rings.Count) % rings.Count;
            // (Roughly the same place around the wheel.)
            selected = Mathf.RoundToInt(selected * (float)rings[ring].Length / old) % rings[ring].Length;
        }

        /// <summary>One step on a fresh press, then repeats while held. <paramref name="at"/> is -1 while not held.</summary>
        static bool Repeat(bool held, ref float at)
        {
            var now = Time.unscaledTime;
            if (!held)
            {
                at = -1f;
                return false;
            }
            if (at < 0f)
            {
                at = now + 0.4f;
                return true;
            }
            if (now < at)
                return false;
            at = now + 0.12f;
            return true;
        }

        /// <summary>Reads the wheel's input; the menu's Update calls it, and does nothing else while it's Busy.</summary>
        public void Update()
        {
            if (!Open)
                return;
            var pad = Gamepad.current;
            var keyboard = Keyboard.current;
            var chars = rings[ring];

            if (pad != null)
            {
                var stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.25f)
                {
                    // Clockwise from the top, as the wheel is drawn.
                    var angle = Mathf.Atan2(stick.x, stick.y);
                    if (angle < 0f)
                        angle += Mathf.PI * 2f;
                    selected = Mathf.RoundToInt(angle / (Mathf.PI * 2f / chars.Length)) % chars.Length;
                }
                if (pad.buttonSouth.wasPressedThisFrame)
                    Insert(chars[selected]);
                var fresh = deleteAt < 0f;
                if (Repeat(pad.buttonEast.isPressed, ref deleteAt))
                {
                    if (text.Length == 0 && fresh)
                    {
                        Close(false);
                        return;
                    }
                    Delete();
                }
                if (pad.leftShoulder.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame)
                    SwitchRing(-1);
                if (pad.rightShoulder.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame)
                    SwitchRing(1);
                if (Repeat(pad.dpad.left.isPressed, ref leftAt))
                    cursor = Mathf.Max(0, cursor - 1);
                if (Repeat(pad.dpad.right.isPressed, ref rightAt))
                    cursor = Mathf.Min(text.Length, cursor + 1);
                if (pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)
                {
                    Close(true);
                    return;
                }
                if (pad.selectButton.wasPressedThisFrame)
                {
                    Close(false);
                    return;
                }
            }
            if (keyboard != null)
            {
                if (keyboard.backspaceKey.wasPressedThisFrame)
                    Delete();
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                    cursor = Mathf.Max(0, cursor - 1);
                if (keyboard.rightArrowKey.wasPressedThisFrame)
                    cursor = Mathf.Min(text.Length, cursor + 1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                    Close(true);
                else if (keyboard.escapeKey.wasPressedThisFrame)
                    Close(false);
            }
        }

        /// <summary>Draws the wheel over the menu, centred at <paramref name="centre"/>; the menu's OnGUI calls it last.</summary>
        public void Draw(Vector2 centre)
        {
            if (!Open)
                return;
            letter ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17, normal = { textColor = Color.white }, hover = { textColor = Highlight } };
            chosen ??= new GUIStyle(letter) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = Highlight }, hover = { textColor = Highlight } };
            field ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, richText = true, normal = { textColor = Color.white } };
            hint ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, richText = true, wordWrap = false };
            disc ??= MakeDisc(128);

            var chars = rings[ring];
            const float radius = 135f, size = 2f * radius + 70f;
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), disc, ScaleMode.StretchToFill);

            for (int i = 0; i < chars.Length; i++)
            {
                var angle = Mathf.PI * 2f * i / chars.Length;
                var at = centre + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;
                var box = i == selected ? 40f : 28f;
                // (Buttons, so the mouse can click them too.)
                if (GUI.Button(new Rect(at.x - box * 0.5f, at.y - box * 0.5f, box, box), chars[i].ToString(), i == selected ? chosen : letter))
                {
                    selected = i;
                    Insert(chars[i]);
                }
            }

            // The text in the middle, with its cursor, and how many characters it can take.
            var shown = text.ToString();
            var withCursor = shown.Substring(0, cursor) + "<color=#ffd04d>|</color>" + shown.Substring(cursor);
            GUI.Box(new Rect(centre.x - 95f, centre.y - 18f, 190f, 36f), withCursor, field);
            var under = maxLength > 0 ? $"{text.Length}/{maxLength}" : "";
            if (rings.Count > 1)
                under = ringNames[ring] + (under.Length > 0 ? "   " + under : "");
            GUI.Label(new Rect(centre.x - 95f, centre.y + 20f, 190f, 20f), under, hint);

            // The buttons, below the wheel.
            var y = centre.y + size * 0.5f + 6f;
            Prompts.Draw(new Rect(centre.x - 250f, y, 500f, 24f), PadPrompt(), hint, true);
            GUI.Label(new Rect(centre.x - 250f, y + 24f, 500f, 20f), InputGlyphs.Gamepad
                ? "Left stick: point at a character.  D-pad left / right: move the cursor.  B with nothing typed: cancel."
                : "Type, or click the characters.  Enter: done.  Esc: cancel.", hint);
        }

        string PadPrompt()
        {
            if (!InputGlyphs.Gamepad)
                return "";
            var prompt = $"{InputGlyphs.Token("<Gamepad>/buttonSouth")} enter    {InputGlyphs.Token("<Gamepad>/buttonEast")} delete    " +
                         $"{InputGlyphs.Token("<Gamepad>/buttonNorth")} done";
            if (rings.Count > 1)
                prompt += $"    {InputGlyphs.Token("<Gamepad>/leftShoulder")}{InputGlyphs.Token("<Gamepad>/rightShoulder")} {ringNames[(ring + 1) % rings.Count]}";
            return prompt;
        }

        /// <summary>A soft dark disc behind the wheel.</summary>
        static Texture2D MakeDisc(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    var a = Mathf.Clamp01((1f - d) * 24f) * 0.88f;
                    pixels[y * size + x] = new Color32(16, 18, 24, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        static readonly Color Highlight = new Color(1f, 0.82f, 0.3f);
    }
}
