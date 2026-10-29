using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>How a settlement's streets run; each culture's pattern gives its towns their own silhouette.</summary>
    public enum SettlementPattern : byte
    {
        /// <summary>A square in the middle with lanes running out from it (human villages).</summary>
        Square,
        /// <summary>Straight streets crossing at right angles round a small square (dwarf holds).</summary>
        Grid,
        /// <summary>A ring street round a green at the heart, with paths in (elf groves).</summary>
        Ring,
        /// <summary>No streets: huts round an open yard inside a palisade (goblin and ogre camps).</summary>
        Camp
    }

    /// <summary>
    /// What a plot is for. Baked into plots, so phase 5's buildings only have to fill them; a byte, append only.
    /// </summary>
    public enum PlotRole : byte
    {
        House,
        /// <summary>The leader's hall, on the best side of the square.</summary>
        Seat,
        Shrine,
        /// <summary>A small green plot between houses: a garden, an orchard, a few trees. Plants are kept.</summary>
        Landscaping,
        Inn,
        Smithy,
        Market,
        Store,
        Workshop,
        Stable,
        /// <summary>A well or fountain on the square (the layout's Water Feature), where the landmark doesn't place one.</summary>
        Well,
        /// <summary>A signpost where a road leaves the settlement (the layout's Signpost).</summary>
        Signpost,
        /// <summary>One span of a road's bridge over water (the layout's Bridge): not levelled; its deck is walkable.</summary>
        Bridge,
        /// <summary>One length of the palisade round a camp (the layout's Palisade Wall): not levelled.</summary>
        Palisade,
        /// <summary>One length of rail fence along a road just outside the settlement (the layout's Road Fence): not levelled.</summary>
        Fence
    }

    /// <summary>One kind of building a settlement puts on its plots.</summary>
    [System.Serializable]
    public class SettlementBuilding
    {
        public PlotRole Role;
        [Tooltip("What stands on the plot (a building flora form). Empty = the plot is levelled and left for later.")]
        public Punchy.Flora.FloraSpecies Species;
        [Tooltip("Among buildings of the same role, how often this one is picked.")]
        [Min(0f)] public float Weight = 1f;
        [Tooltip("How many plots take this role (roles other than House and Landscaping; the Seat is always one).")]
        [Min(0)] public int Count = 1;
    }

    /// <summary>
    /// The plan of a settlement landmark: its street pattern, plot sizes for its folk, and what goes on the plots.
    /// SettlementBaking lays it out from the landmark's seed; TerrainSource levels plots and streets and paints the
    /// streets; the flora system puts each plot's building on it, facing its street.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Settlement Layout")]
    public class SettlementLayout : ScriptableObject
    {
        public SettlementPattern Pattern;
        [Tooltip("Whose settlement this is: it's only built where the biome under its centre allows this folk " +
                 "(BiomeDefinition.Cultures). A Camp pattern counts as a camp for Camps Only biomes.")]
        public FolkCulture Culture = FolkCulture.Human;
        [Tooltip("How big the folk are next to humans (1). Plots, streets and the square scale with it, and phase 5's " +
                 "doors and eaves read it from the settlement.")]
        [Min(0.3f)] public float FolkScale = 1f;
        [Tooltip("A house plot's frontage and depth (m, at folk scale 1).")]
        public Vector2 PlotSize = new Vector2(8f, 9f);
        [Tooltip("Space between neighbouring plots (m, at folk scale 1).")]
        [Min(0f)] public float PlotGap = 2.5f;
        [Tooltip("Space between a street's edge and the plots along it (m, at folk scale 1).")]
        [Min(0f)] public float Setback = 1f;
        [Tooltip("The main street's width (m, at folk scale 1).")]
        [Min(1f)] public float StreetWidth = 4.5f;
        [Tooltip("Other streets' and lanes' width (m, at folk scale 1).")]
        [Min(1f)] public float LaneWidth = 3f;
        [Tooltip("Across the square, green or yard in the middle (m, at folk scale 1).")]
        [Min(0f)] public float SquareSize = 22f;
        [Tooltip("Lanes out of the square (Square), or paths in from the ring street (Ring).")]
        [Range(1, 6)] public int Lanes = 3;
        [Tooltip("Square: a lane round the town linking the ends of the lanes out of the square, and in big towns (radius " +
                 "over 70 m at folk scale 1) cross lanes between neighbouring lanes nearer the middle, so there are real " +
                 "rounds to walk.")]
        public bool PerimeterLane = true;
        [Tooltip("How many plots are shrines (on the square or at the edge).")]
        [Range(0, 3)] public int Shrines = 1;
        [Tooltip("Share of the remaining plots left green (gardens and trees).")]
        [Range(0f, 0.5f)] public float Landscaping = 0.15f;
        [Tooltip("A palisade round the settlement (Camp), with gates where the roads come in.")]
        public bool Palisade;
        [Tooltip("The most a plot's ground may rise across it (m) before it is skipped.")]
        [Min(0.2f)] public float MaxRelief = 2.5f;
        [Tooltip("What the streets, square and paths are paved or trodden with. Empty = left as they are.")]
        public TerrainSurface StreetSurface;
        [Tooltip("Scales every building on a plot: the folk's own size against the size its building species were made " +
                 "for (ogre tents bigger, goblin huts smaller). Plots already scale with Folk Scale.")]
        [Min(0.3f)] public float BuildingScale = 1f;
        [Tooltip("A well or fountain placed on the square if the landmark doesn't place one itself.")]
        public Punchy.Flora.FloraSpecies WaterFeature;
        [Header("Roads")]
        [Tooltip("Width of the roads that leave this settlement for others (m). They're paved with Street Surface.")]
        [Min(1f)] public float RoadWidth = 3.2f;
        [Tooltip("Stands where each road leaves the settlement.")]
        public Punchy.Flora.FloraSpecies Signpost;
        [Tooltip("One 6.6 m span of a bridge (tree layer), repeated across water too deep to ford (deck at the origin, posts down).")]
        public Punchy.Flora.FloraSpecies Bridge;
        [Tooltip("One 3 m length of palisade, repeated round the camp (if Palisade is on), with gaps for streets and roads.")]
        public Punchy.Flora.FloraSpecies PalisadeWall;
        [Tooltip("One 3 m length of rail fence, run along some roads for a stretch just outside the settlement (fields), " +
                 "with gaps at forks and signposts. None if empty.")]
        public Punchy.Flora.FloraSpecies RoadFence;
        public List<SettlementBuilding> Buildings = new List<SettlementBuilding>();
    }
}
