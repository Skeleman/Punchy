using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// What a light seen from far off is (R-10 row 4c). Each has a row in <see cref="FarLightPolicy"/>'s table.
    /// - Window: a lit house's windows. StreetLight: a town's street lamp. Both are fire-lit, so they're in the Fire tier.
    /// - Fire: a burning tree (the Ember Forest), a beacon tower's fire, surface lava.
    /// - GlowPlant: glowing mushrooms and other glowing plants (the fungal forest).
    /// - Crystal: glass crystals' faint inner glow.
    /// - Creature: glowing eyes and markings, colossus lanterns (in the table, not drawn as far points yet: they move).
    /// - Campfire: a landmark's own flame (a camp's fire, a shrine's strange fire), in its own colour (R-25 row 3).
    /// </summary>
    public enum FarLightKind : byte { Window, StreetLight, Fire, GlowPlant, Crystal, Creature, Campfire }

    /// <summary>How strong a kind of light reads far off, strongest first (Zach 11-10: lights aren't equal).</summary>
    public enum FarLightTier : byte { Fire, Glow, Faint }

    /// <summary>
    /// One rule for how every static light looks from far off (R-10 row 4c, Zach 11-10: the town was a little too bright
    /// and the Ember Forest "pales in comparison"). Each far light is a small camera-facing point (FarLights.shader): a
    /// bright core at least a few pixels across, a faint wide halo so a cluster of them runs together, thinned by the
    /// haze much less than the land. How bright a light of a given strength reads comes from its tier; its size, halo,
    /// haze and hand-off from its kind's row. Sources keep their own colour.
    ///
    /// The hand-off is by screen size (rule 20): a far point fades in as its near light's reach shrinks on screen from
    /// <see cref="Rule.FadeFrom"/> to <see cref="Rule.FadeTo"/> reference pixels (Detail scales both), so it takes over
    /// where the near lights and the glow maps leave off, at whatever field of view.
    /// </summary>
    public static class FarLightPolicy
    {
        public const int KindCount = 7;

        public struct Rule
        {
            public FarLightTier Tier;
            /// <summary>The core's width (m) at light 1 (it grows with the square root of the light).</summary>
            public float Size;
            /// <summary>The least the core is drawn across (pixels), so it still shows from 3 km.</summary>
            public float MinPixels;
            /// <summary>The halo: how far it reaches (in core sizes) and how bright it is next to the core.</summary>
            public float HaloReach, HaloShare;
            /// <summary>How much of the land's haze thins it (a light: a small share).</summary>
            public float HazeShare;
            /// <summary>The reach (m) of the light it stands in for up close, for the screen-size hand-off.</summary>
            public float Reach;
            /// <summary>The hand-off: none while that reach looks bigger than FadeFrom reference pixels, full below FadeTo.</summary>
            public float FadeFrom, FadeTo;
        }

        /// <summary>HDR brightness of a light of strength 1 in the Fire tier (over 1 blooms). Row 4b's town lights were 8;
        /// 4c is 5 (Zach: "turn the intensity down a little"; the bloom and the tonemap clip the cores, so a far town
        /// reads about 25% dimmer, not 37%).</summary>
        public static float Brightness = 5f;

        /// <summary>The tier table: each tier's brightness against the Fire tier's.</summary>
        public static float TierStrength(FarLightTier tier)
        {
            switch (tier)
            {
                case FarLightTier.Fire: return 1f;
                case FarLightTier.Glow: return 0.4f;
                default: return 0.25f;
            }
        }

        /// <summary>The kind table. Fade 56-30 px of a 9 m reach is 150-280 m at the default view, as row 4b's town lights.</summary>
        public static Rule For(FarLightKind kind)
        {
            switch (kind)
            {
                case FarLightKind.Window:
                    return new Rule { Tier = FarLightTier.Fire, Size = 1.4f, MinPixels = 5f, HaloReach = 4f, HaloShare = 0.15f, HazeShare = 0.05f, Reach = 9f, FadeFrom = 56f, FadeTo = 30f };
                case FarLightKind.StreetLight:
                    return new Rule { Tier = FarLightTier.Fire, Size = 1.1f, MinPixels = 5f, HaloReach = 4f, HaloShare = 0.15f, HazeShare = 0.05f, Reach = 9f, FadeFrom = 56f, FadeTo = 30f };
                // A burning crown is big: a larger core, and a wider halo so a forest's fires run into one blaze.
                case FarLightKind.Fire:
                    return new Rule { Tier = FarLightTier.Fire, Size = 2.4f, MinPixels = 5f, HaloReach = 5f, HaloShare = 0.18f, HazeShare = 0.05f, Reach = 9f, FadeFrom = 56f, FadeTo = 30f };
                // Soft natural glows: smaller, thinned a little more by the haze, and handed off from the glow maps' reach.
                case FarLightKind.GlowPlant:
                    return new Rule { Tier = FarLightTier.Glow, Size = 1.2f, MinPixels = 4f, HaloReach = 4f, HaloShare = 0.2f, HazeShare = 0.08f, Reach = 8f, FadeFrom = 56f, FadeTo = 30f };
                // One small fire. Its flames stop being drawn at the flame range (150 m at the default view, which is
                // 56 px of its 9 m reach), so the point is full by then: it fades in from 90 px (94 m) to 60 px (140 m).
                case FarLightKind.Campfire:
                    return new Rule { Tier = FarLightTier.Fire, Size = 1.2f, MinPixels = 4f, HaloReach = 4f, HaloShare = 0.15f, HazeShare = 0.05f, Reach = 9f, FadeFrom = 90f, FadeTo = 60f };
                case FarLightKind.Crystal:
                    return new Rule { Tier = FarLightTier.Faint, Size = 1f, MinPixels = 3f, HaloReach = 3f, HaloShare = 0.15f, HazeShare = 0.1f, Reach = 6f, FadeFrom = 56f, FadeTo = 30f };
                default:
                    return new Rule { Tier = FarLightTier.Faint, Size = 0.8f, MinPixels = 3f, HaloReach = 3f, HaloShare = 0.15f, HazeShare = 0.1f, Reach = 6f, FadeFrom = 56f, FadeTo = 30f };
            }
        }

        /// <summary>A light of this kind and strength's HDR brightness far off (before the haze).</summary>
        public static float FarBrightness(FarLightKind kind, float light) => Brightness * TierStrength(For(kind).Tier) * light;

        /// <summary>The table as the shader reads it, one row a kind: x core size, y least pixels, z halo reach, w halo
        /// share; then x haze share, y reach (m), z fade from, w fade to (pixels, Detail folded in).</summary>
        public static void Pack(Vector4[] shape, Vector4[] fade, float detail)
        {
            var q = math.max(detail, 0.1f);
            for (int k = 0; k < KindCount; k++)
            {
                var r = For((FarLightKind)k);
                shape[k] = new Vector4(r.Size, r.MinPixels, r.HaloReach, r.HaloShare);
                fade[k] = new Vector4(r.HazeShare, r.Reach, r.FadeFrom / q, r.FadeTo / q);
            }
        }
    }
}
