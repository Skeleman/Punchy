using Chem;
using Punchy.Player;
using Punchy.UI;
using UnityEngine;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The hands' HUD, in IMGUI through a GuiHook like Punchy's other HUD pieces: two hand icons at the bottom of the
    /// screen, each in its element's colour with its buttons above it (InputGlyphs' icons for the pad in use) and a ring
    /// round it that fills as its gesture winds up and charges (white, then gold, then orange at each level), greys while
    /// the hand cools down and pulses while it attunes. While both hands make one spell the merge's name shows between
    /// them, and a merge flashes as it starts and as it goes off. Holding d-pad up shows the element radial round the
    /// screen centre, with the hand it will attune.
    /// </summary>
    public partial class ChemistryBridge
    {
        const float MergeFlash = 0.6f;
        const int RingDots = 28;

        static readonly Color Fire = new Color(1f, 0.45f, 0.12f), Water = new Color(0.25f, 0.6f, 1f),
                              Earth = new Color(0.62f, 0.44f, 0.24f), Air = new Color(0.78f, 0.93f, 1f), Empty = new Color(0.6f, 0.6f, 0.6f);
        static readonly Color[] ChargeColours = { Color.white, new Color(1f, 0.85f, 0.3f), new Color(1f, 0.5f, 0.15f) };
        static readonly string[] HandButtons =
        {
            InputGlyphs.Token("<Gamepad>/leftTrigger") + " " + InputGlyphs.Token("<Gamepad>/leftShoulder"),
            InputGlyphs.Token("<Gamepad>/rightTrigger") + " " + InputGlyphs.Token("<Gamepad>/rightShoulder"),
        };

        Texture2D disc;
        GUIStyle label, small, shadow, smallShadow, big, bigShadow;

        bool HudWanted() => enabled && Ready && padMap != null && world.HandsOn(Me) && !Overlay.AnyOpen;

        static Color ColourOf(string element) => element switch
        {
            "fire" => Fire,
            "water" => Water,
            "earth" => Earth,
            "air" => Air,
            _ => Empty,
        };

        void EnsureStyles()
        {
            if (disc == null)
            {
                const int n = 64;
                disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ChemistryHudDisc", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((1f - d) * 12f) * 255));
                    }
                disc.SetPixels32(px);
                disc.Apply(false, true);
            }
            if (label != null)
                return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true };
            label.normal.textColor = Color.white;
            shadow = new GUIStyle(label);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            small = new GUIStyle(label) { fontSize = 12 };
            smallShadow = new GUIStyle(small);
            smallShadow.normal.textColor = shadow.normal.textColor;
            big = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            bigShadow = new GUIStyle(big);
            bigShadow.normal.textColor = shadow.normal.textColor;
        }

        void DrawHud()
        {
            if (Event.current.type != EventType.Repaint || world == null)
                return;
            EnsureStyles();
            var keep = GUI.color;
            // (Wide apart, so Punchy's own prompt, centred above the bottom edge, shows between them.)
            float cx = Screen.width * 0.5f, cy = Screen.height - 84f;
            var h = world.HandsOf(Me);
            for (int hand = 0; hand < HandsDef.Count; hand++)
                DrawHand(h, hand, new Vector2(cx + (hand == HandsDef.Left ? -190f : 190f), cy));
            var merge = world.CastOf(Me)?.Merge;
            float flash = 1f - (Time.unscaledTime - mergeFlashAt) / MergeFlash;
            if (flash > 0f)
            {
                float r = Mathf.Lerp(260f, 60f, flash);
                GUI.color = new Color(1f, 1f, 1f, 0.4f * flash);
                GUI.DrawTexture(new Rect(cx - r, cy - r * 0.45f, r * 2f, r * 0.9f), disc);
            }
            if (merge != null || flash > 0f)
            {
                var id = merge ?? mergeShown;
                var name = rules.Spell(id)?.Name ?? id;
                float pulse = merge != null ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f) : Mathf.Clamp01(flash);
                GUI.color = new Color(1f, 1f, 1f, pulse);
                Text(new Rect(cx - 160f, cy - 20f, 320f, 30f), name, big, bigShadow);
            }
            GUI.color = keep;
            if (radialOpen)
                DrawRadial();
            GUI.color = keep;
        }

        void DrawHand(PlayerHands h, int hand, Vector2 at)
        {
            var holds = world.HandHolds(Me, hand);
            bool own = h.Holds[hand] != null;
            const float r = 30f, ring = 41f;
            // The ring: what the hand's meter has run, or its cooldown, or its attuning moment.
            float fill = 0f;
            var ringColour = Color.white;
            if (h.Gesture[hand] != HandsDef.NoGesture && h.Total[hand] > 0)
            {
                fill = 1f - (float)h.Meter[hand] / h.Total[hand];
                ringColour = ChargeColours[Mathf.Clamp(world.HandCharge(Me, hand), 1, 3) - 1];
                if (h.Down[hand] && h.Meter[hand] <= 1)
                    fill = 1f;
            }
            else if (h.AttuneLeft > 0 && h.AttuneHand == hand)
            {
                fill = 1f - (float)h.AttuneLeft / Mathf.Max(1, rules.Sim.Hold.Hands.AttuneTicks);
                ringColour = ColourOf(h.AttuneTo);
            }
            else if (world.HandCooling(Me, hand) > 0)
            {
                fill = (float)world.HandCooling(Me, hand) / Mathf.Max(1, rules.Sim.Hold.Hands.HandCooldown);
                ringColour = new Color(0.55f, 0.55f, 0.55f);
            }
            for (int i = 0; i < RingDots; i++)
            {
                float a = i / (float)RingDots * Mathf.PI * 2f;
                var p = at + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * ring;
                bool lit = i < fill * RingDots;
                GUI.color = lit ? ringColour : new Color(0f, 0f, 0f, 0.35f);
                float s = lit ? 7f : 5f;
                GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), disc);
            }
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(at.x - r - 3f, at.y - r - 3f, (r + 3f) * 2f, (r + 3f) * 2f), disc);
            var c = ColourOf(holds);
            // A hand that holds nothing of its own wears the other's element, paler (one element is on both hands).
            GUI.color = own ? c : Color.Lerp(c, new Color(0.2f, 0.2f, 0.2f), 0.45f);
            float grow = h.Down[hand] ? 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 14f) : 1f;
            GUI.DrawTexture(new Rect(at.x - r * grow, at.y - r * grow, r * 2f * grow, r * 2f * grow), disc);
            GUI.color = Color.white;
            Text(new Rect(at.x - 50f, at.y - 10f, 100f, 20f), holds ?? "empty", label, shadow);
            var gesture = HandsDef.GestureName(h.Gesture[hand]);
            var below = gesture != null ? (h.Down[hand] ? $"{gesture}  charge {world.HandCharge(Me, hand)}" : gesture) : Side(hand);
            Text(new Rect(at.x - 70f, at.y + ring + 6f, 140f, 18f), below, small, smallShadow);
            Prompts.Draw(new Rect(at.x - 60f, at.y - ring - 30f, 120f, 22f), HandButtons[hand], small, true, smallShadow);
        }

        void DrawRadial()
        {
            var elements = rules.Combos.Elements;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            const float reach = 92f, r = 28f;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(cx - reach - r - 14f, cy - reach - r - 14f, (reach + r + 14f) * 2f, (reach + r + 14f) * 2f), disc);
            int hand = RadialHand();
            for (int i = 0; i < elements.Count; i++)
            {
                float a = i / (float)elements.Count * Mathf.PI * 2f;
                var p = new Vector2(cx + Mathf.Sin(a) * reach, cy - Mathf.Cos(a) * reach);
                bool picked = i == radialPick;
                float s = picked ? r * 1.3f : r;
                GUI.color = picked ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                GUI.DrawTexture(new Rect(p.x - s - 4f, p.y - s - 4f, (s + 4f) * 2f, (s + 4f) * 2f), disc);
                GUI.color = ColourOf(elements[i]);
                GUI.DrawTexture(new Rect(p.x - s, p.y - s, s * 2f, s * 2f), disc);
                GUI.color = Color.white;
                Text(new Rect(p.x - 50f, p.y - 10f, 100f, 20f), elements[i], label, shadow);
            }
            GUI.color = Color.white;
            Text(new Rect(cx - 90f, cy - 20f, 180f, 20f), $"attune the {Side(hand)} hand", small, smallShadow);
            Prompts.Draw(new Rect(cx - 90f, cy + 2f, 180f, 20f), $"{InputGlyphs.Token("<Gamepad>/rightTrigger")} right hand", small, true, smallShadow);
        }

        static void Text(Rect rect, string text, GUIStyle style, GUIStyle under)
        {
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, under);
            GUI.Label(rect, text, style);
        }
    }
}
