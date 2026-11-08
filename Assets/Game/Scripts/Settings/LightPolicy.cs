using Unity.Mathematics;

namespace Punchy.Settings
{
    /// <summary>
    /// What kind of light a source is, by the role it plays (refactor roadmap 3.1 and 3.7). Every light in the game
    /// belongs to one of these, and its kind decides how it's represented at each on-screen size and how it ranks when
    /// lights compete for the budget. Inventory on main 75a6240f (2026-11-08):
    /// - Celestial: the sun / moon light (WeatherSystem), the blue moon (today an ambient term only).
    /// - SkyFlash: lightning (WeatherSystem's flash light, Sky.hlsl's bolt).
    /// - Carried: lanterns and torches someone holds: the player's and other players' cave lanterns (real lights,
    ///   CaveSystem), folk's lanterns and torches (glow list, CreatureRenderSystem).
    /// - Fixture: placed, man-made lights: interior room lamps (real, Interiors), wall and post torches and sconces in
    ///   cave rooms and the sunken temple, lit windows (SurfaceGlow), the iceberg cave light (real).
    /// - Fire: open fires that light their surroundings: landmark campfires and braziers (flames only, no light
    ///   yet), forest fires (the 6 nearest crowns light; FireSystem.Light).
    /// - Creature: lights carried by or grown on animals: colossus lanterns (lantern stag, mire walker, hermit crab,
    ///   cinder snail, magma serpent, charred treant), puffball pores, ember trails, glowing eyes and markings.
    /// - AmbientGlow: many small, soft natural glows: glowing plants (cave and surface), crystals, lava pools, toxic
    ///   water, white pools.
    /// </summary>
    public enum LightKind : byte { Celestial, SkyFlash, Carried, Fixture, Fire, Creature, AmbientGlow }

    /// <summary>
    /// How a light is shown, best first. Real and RealShadowed are Unity lights (only the cave and terrain shaders
    /// read them today); Glow is a slot in the shared glow list (Glow.hlsl: every custom shader, 16 nearest);
    /// FarGlow the 640 m night glow map; Emissive the source itself glowing with no light thrown; None not at all.
    /// </summary>
    public enum LightTier : byte { RealShadowed, Real, Glow, FarGlow, Emissive, None }

    /// <summary>
    /// The light half of the detail rule (DetailPolicy). A light's on-screen size is its reach (m, the radius it
    /// lights) over its distance, in reference pixels: a campfire lighting 9 m round it at 60 m looks as big as a
    /// lantern lighting 4.5 m at 30 m. Each kind says which tiers it may use and at what size, and how important it
    /// is when the budget is full (<see cref="Score"/>: importance x on-screen size x brightness).
    /// Global lights (Celestial, SkyFlash) are never budgeted or tiered: they're always on.
    /// </summary>
    public static class LightPolicy
    {
        public struct Rule
        {
            /// <summary>The best tier this kind may use (Real for a player's lantern, Glow for most).</summary>
            public LightTier Best;
            /// <summary>Boundaries (reference pixels of its reach) for stepping down from Best: bigger than Near keeps
            /// Best, than Mid one tier down, than Far two down (to Emissive at most), else Emissive / None.</summary>
            public DetailPolicy.Thresholds Size;
            /// <summary>The worst it goes to when tiny: Emissive (the source still glows) or None.</summary>
            public LightTier Worst;
            /// <summary>Rank when lights compete (1 = ordinary).</summary>
            public float Importance;
            /// <summary>Global: always on, never budgeted.</summary>
            public bool Global;
        }

        /// <summary>The rule for each kind (indexed by LightKind).</summary>
        public static Rule For(LightKind kind)
        {
            switch (kind)
            {
                case LightKind.Celestial:
                case LightKind.SkyFlash:
                    return new Rule { Best = LightTier.RealShadowed, Worst = LightTier.RealShadowed, Importance = 100f, Global = true };
                // Light carried in the dark is gameplay: a player sees by it, and others are seen by it.
                case LightKind.Carried:
                    return new Rule { Best = LightTier.Real, Size = new DetailPolicy.Thresholds(40f, 12f, 3f), Worst = LightTier.Emissive, Importance = 4f };
                case LightKind.Fixture:
                    return new Rule { Best = LightTier.Real, Size = new DetailPolicy.Thresholds(60f, 20f, 4f), Worst = LightTier.Emissive, Importance = 2f };
                // Fires are big and bright: they keep a glow far out, then show as their flames.
                case LightKind.Fire:
                    return new Rule { Best = LightTier.Real, Size = new DetailPolicy.Thresholds(50f, 15f, 2f), Worst = LightTier.Emissive, Importance = 2.5f };
                case LightKind.Creature:
                    return new Rule { Best = LightTier.Glow, Size = new DetailPolicy.Thresholds(20f, 4f, 1f), Worst = LightTier.Emissive, Importance = 1.5f };
                // Many and soft: never real lights, a near glow and the far glow map, then just the glowing thing.
                default:
                    return new Rule { Best = LightTier.Glow, Size = new DetailPolicy.Thresholds(25f, 5f, 1f), Worst = LightTier.Emissive, Importance = 1f };
            }
        }

        /// <summary>The tier for a light of this kind, reach (m) and distance (m) under the view, from last frame's tier
        /// (-1 when new), before any budget.</summary>
        public static LightTier Tier(LightKind kind, float reach, float distance, in DetailPolicy.View view, int previous)
        {
            var rule = For(kind);
            if (rule.Global)
                return rule.Best;
            var pixels = DetailPolicy.ScreenSize(reach, distance, view.PixelsPerMetre);
            var step = DetailPolicy.Tier(pixels, rule.Size.Scaled(math.max(view.Detail, 0.1f)),
                                         previous < 0 ? -1 : math.max(previous - (int)rule.Best, 0));
            var tier = math.min((int)rule.Best + step, (int)LightTier.Emissive);
            return (LightTier)math.min(math.max(tier, (int)rule.Best), (int)rule.Worst);
        }

        /// <summary>How much a light deserves a slot when slots run short (higher first): importance x on-screen size
        /// x brightness.</summary>
        public static float Score(LightKind kind, float reach, float distance, float intensity, in DetailPolicy.View view)
            => For(kind).Importance * DetailPolicy.ScreenSize(reach, distance, view.PixelsPerMetre) * math.max(intensity, 0f);
    }
}
