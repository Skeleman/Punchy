using System;

namespace Punchy.Terrain
{
    /// <summary>
    /// P-37 r3 row 1 (#369): seamless interiors. A walk-in house type has no room you are taken to: you walk in through
    /// its door. Its door leaf swings and blocks only while shut. Its kit walls and floor are the colliders, and nothing
    /// fills its middle. Built on the P-30 prototype (player/seamless). Row 1 covers the storm house only. More types
    /// follow in row 5.
    /// </summary>
    public static partial class StructureKit
    {
        /// <summary>
        /// Whether walk-in houses are on this session. Off by default until rows 1 and 3 are verified in play; "-seamless"
        /// turns it on and "-teleportrooms" keeps the old rooms (it wins over -seamless, and stays meaningful once the
        /// default flips). A joined client takes the host's choice (WorldSync's IslandVersionRpc, before its island and
        /// flora build); a world that isn't joined goes back to <see cref="LaunchSeamless"/>. It must be set before the
        /// flora models are built: their colliders are baked from it.
        /// </summary>
        public static bool Seamless { get; set; } = LaunchSeamless;

        /// <summary>This launch's own choice, from its command line.</summary>
        public static bool LaunchSeamless =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-seamless") >= 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-teleportrooms") < 0;

        /// <summary>Whether this kit shape is walked into this session (row 1: the storm house, with Seamless on).</summary>
        public static bool WalkIn(KitShape s) => Seamless && s == KitShape.StormHouse;

        /// <summary>Whether elf towers are walked into this session (P-51 row 2: with Seamless on, a stair up their storeys).</summary>
        public static bool WalkInTower => Seamless;

        /// <summary>
        /// A walk-in house's door step height (m). DoorBay lays 0.16 m. With the 0.5 m plinth floor solid, that left a
        /// 0.34 m second rise, over the 0.32 m step a body climbs. 0.25 m makes both rises climbable, as a ruin's step is.
        /// </summary>
        public const float WalkInStep = 0.25f;
    }
}
