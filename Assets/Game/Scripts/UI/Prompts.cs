using System.Collections.Generic;
using Punchy.Player;
using UnityEngine;

namespace Punchy.UI
{
    /// <summary>
    /// Draws prompt text with button icons in it. A prompt names a button with a token, made by InputGlyphs.Token from
    /// the control's path ("[[&lt;Gamepad&gt;/buttonNorth]]  pet the cat"). Drawn, each token becomes that button's icon
    /// for the device in use (InputGlyphs.Icon: a keycap, or an Xbox or PlayStation symbol), or its name where there's
    /// no icon (a generic pad, a key not in the set). Text runs keep the style's rich text. A prompt is cut into pieces
    /// once and kept, so drawing it every frame makes no garbage.
    /// </summary>
    public static class Prompts
    {
        struct Piece
        {
            public string Text;
            public Texture2D Icon;
        }

        // Cut prompts, by text and device version (an icon or a name changes when the device does).
        static readonly Dictionary<string, Piece[]> cut = new Dictionary<string, Piece[]>();
        static int cutVersion = -1;
        static readonly GUIContent content = new GUIContent();

        static Piece[] Cut(string text)
        {
            if (cutVersion != InputGlyphs.Version)
            {
                cut.Clear();
                cutVersion = InputGlyphs.Version;
            }
            if (cut.TryGetValue(text, out var pieces))
                return pieces;
            var list = new List<Piece>();
            var at = 0;
            while (at < text.Length)
            {
                var open = text.IndexOf("[[", at, System.StringComparison.Ordinal);
                var close = open >= 0 ? text.IndexOf("]]", open + 2, System.StringComparison.Ordinal) : -1;
                if (open < 0 || close < 0)
                {
                    list.Add(new Piece { Text = text.Substring(at) });
                    break;
                }
                if (open > at)
                    list.Add(new Piece { Text = text.Substring(at, open - at) });
                var path = text.Substring(open + 2, close - open - 2);
                var icon = InputGlyphs.Icon(path);
                list.Add(icon != null ? new Piece { Icon = icon } : new Piece { Text = InputGlyphs.Path(path) ?? path });
                at = close + 2;
            }
            pieces = list.ToArray();
            if (cut.Count > 256)
                cut.Clear();
            cut[text] = pieces;
            return pieces;
        }

        /// <summary>An icon's size beside text of a style: a little taller than the letters.</summary>
        static float IconSize(GUIStyle style) => Mathf.Round((style.fontSize > 0 ? style.fontSize : 13) * 1.6f);

        /// <summary>How wide and tall a prompt draws in a style (one line).</summary>
        public static Vector2 Measure(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.zero;
            var size = IconSize(style);
            var left = Left(style);
            float width = 0f, height = 0f;
            foreach (var piece in Cut(text))
            {
                if (piece.Icon != null)
                {
                    width += size;
                    height = Mathf.Max(height, size);
                    continue;
                }
                content.text = piece.Text;
                var s = left.CalcSize(content);
                width += s.x;
                height = Mathf.Max(height, s.y);
            }
            return new Vector2(width, height);
        }

        /// <summary>
        /// Draws a prompt on one line in a rect: left-aligned, or centred across it. With a shadow style, the text also
        /// gets a drop shadow (the HUD prompt).
        /// </summary>
        public static void Draw(Rect rect, string text, GUIStyle style, bool centred = false, GUIStyle shadow = null)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var total = Measure(text, style);
            var size = IconSize(style);
            var x = centred ? rect.x + (rect.width - total.x) * 0.5f : rect.x;
            var middle = rect.y + rect.height * 0.5f;
            // (The pieces are laid out with a left-aligned copy of the style, whatever the style's own alignment.)
            var left = Left(style);
            var leftShadow = shadow != null ? Left(shadow) : null;
            foreach (var piece in Cut(text))
            {
                if (piece.Icon != null)
                {
                    GUI.DrawTexture(new Rect(x, middle - size * 0.5f, size, size), piece.Icon, ScaleMode.ScaleToFit);
                    x += size;
                    continue;
                }
                content.text = piece.Text;
                var s = left.CalcSize(content);
                var r = new Rect(x, middle - s.y * 0.5f, s.x, s.y);
                if (leftShadow != null)
                    GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), content, leftShadow);
                GUI.Label(r, content, left);
                x += s.x;
            }
        }

        /// <summary>A prompt as a GUILayout element (the menu's footer and tab strip).</summary>
        public static void Layout(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            var size = Measure(text, style);
            var rect = GUILayoutUtility.GetRect(size.x, size.y, options);
            if (Event.current.type == EventType.Repaint)
                Draw(rect, text, style);
        }

        static readonly Dictionary<GUIStyle, GUIStyle> lefts = new Dictionary<GUIStyle, GUIStyle>();

        static GUIStyle Left(GUIStyle style)
        {
            if (style.alignment == TextAnchor.MiddleLeft && style.padding.horizontal == 0)
                return style;
            if (!lefts.TryGetValue(style, out var left))
            {
                left = new GUIStyle(style) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), wordWrap = false };
                lefts[style] = left;
            }
            return left;
        }
    }
}
