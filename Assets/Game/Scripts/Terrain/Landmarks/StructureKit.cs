using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>What a structure-kit part is, so builders and later systems (docks, ladders, residents) can tell them apart.</summary>
    public enum KitPiece : byte
    {
        Wall, Floor, Roof, Stair, Column, Arch, Platform, Balcony, Rail, Window, Door, Trim, Rubble, Moss, Beam, Chimney,
        /// <summary>
        /// W-64 (#24): a moss sheet draped over a broken wall top, drawn as one thin ragged sheet (FloraMeshBuilder.MossSheet),
        /// not a box. Centre: the middle of the wall's top; Size: x along the wall, y how far it hangs down each face, z the
        /// wall's thickness it wraps over.
        /// </summary>
        MossCap,
        /// <summary>
        /// W-147: a ladder (Climbability grade 1), drawn as two rails and rungs (wood, or rope rails with wood rungs in
        /// Net). Its local +Y runs up the ladder, +Z is the side a climber stands on, X across. Centre and Size cover the
        /// rails, which reach a grab (StructureKit.LadderGrab) past the landing; StructureKit.LadderFoot and LadderLanding
        /// read the ends back. Decor: no collision.
        /// </summary>
        Ladder
    }

    /// <summary>Which of a culture's trim-set colours a part is drawn in.</summary>
    public enum KitMaterial : byte
    {
        Wall, Trim, Roof, Wood, Opening, Moss, Stone,
        /// <summary>A lantern's glass: drawn as a window pane, so it lights up at dusk (fishing villages, #36).</summary>
        Lamp,
        /// <summary>Net and rope: a pale cord colour.</summary>
        Net,
        /// <summary>A turtle folk rune or ice pane (#38): a pale blue-green glow, drawn as a window pane so it lights at dusk.</summary>
        Rune,
        /// <summary>Packed snow and ice blocks: the turtle folk's igloo shell, a little bluer than the wall colour.</summary>
        Ice,
        /// <summary>A hazard house's (#69) lightning rod and its strap: weathered copper.</summary>
        Copper,
        /// <summary>Wind-blown sand drifted against a desert house (#69).</summary>
        Sand,
        /// <summary>Grey volcanic ash heaped on and around an ash house (#69).</summary>
        Ash,
        /// <summary>Trodden dirt: a slab shelter's (#69) floor and ramp, tinted a little toward the local rock.</summary>
        Dirt,
        /// <summary>W-138: a lantern turned low (a tunnel's wall lantern): glass drawn as a window pane like Lamp, but a
        /// deeper amber, so a small flame reads as flame light rather than a white box in a dark-adapted cave.</summary>
        Flame
    }

    /// <summary>
    /// What a Kit Building species builds (#36): a house, a fishing folk's stilt house, a dock running out over the
    /// water from its land end, or a cluster of shore details (boats pulled up, nets drying, fish racks, crab pots).
    /// </summary>
    public enum KitShape : byte
    {
        House, StiltHouse, Dock, Shore,
        /// <summary>A turtle folk's domed igloo (#38) with a tall tunnel door.</summary>
        Igloo,
        /// <summary>A cluster of mountaintop details (#38): prayer-flag lines, snow-buried pots, rune stones, cairns.</summary>
        Alpine,
        /// <summary>A sea folk's shell dome on the lake bed (#37), ribbed like a scallop, glowing within.</summary>
        ShellDome,
        /// <summary>A sea folk's tall spiral shell spire (#37), standing on the bed and breaking the surface.</summary>
        Spire,
        /// <summary>A sea folk's landing pier (#37): runs from the bank out over deep water on long piles.</summary>
        Pier,
        /// <summary>A cluster of lake-bed details (#37): kelp, shell mosaics, bubble vents, sunken goods, coral arches, anemones.</summary>
        Reef,
        /// <summary>A fishing village's wharf (W-65): a small network of walkways from the bank with houses on stilts over the water.</summary>
        Wharf,
        /// <summary>A harsh-biome hazard house (#69): one storey of House in a thick stone skin under an ash hood (Ashlands, Volcano).</summary>
        AshHouse,
        /// <summary>A hazard house (#69) half sunk in snow banked up its back and sides (Tundra, Glacier, Mountains).</summary>
        SnowHouse,
        /// <summary>A hazard house (#69) grounded by a copper rod down to a stake and plate (ElectricWastelands).</summary>
        StormHouse,
        /// <summary>A hazard house (#69) with thick walls, a slatted shade porch and sand drifts (Dunes, SaltFlat, GlassDesert).</summary>
        SandHouse,
        /// <summary>
        /// W-95 (#69): a harsh-biome slab shelter, a crooked ramada of three leaning rock slabs under a fourth laid across
        /// as the roof, dug into the ground with a dirt ramp up out of its open front (+Z). No shell, no inside cylinder.
        /// </summary>
        SlabShelter,
        /// <summary>
        /// W-119 (#36): one short piece of a fishing village's shore boardwalk, a plank deck on posts along +Z about its
        /// middle. FloraPlacement lays the pieces end to end between the village's dock and wharf ramps and its stilt
        /// house stairs; each variant is its own length (<see cref="StructureKit.BoardwalkLength"/>).
        /// </summary>
        Boardwalk,
        /// <summary>W-120 (#37): a sea folk glow stone, a standing lake-bed stone banded with glowing rune shell, ringing the spire.</summary>
        GlowStone,
        /// <summary>W-120 (#37): a sunken boat on a lake bed, keeled over and grown with shells, coral and kelp.</summary>
        Wreck,
        /// <summary>W-120 (#37): a bed of giant clams on a lake bed, some open on a glowing pearl.</summary>
        ClamBed,
        /// <summary>W-120 (#37): two or three bubble vents close together in a ring of scorched stones.</summary>
        VentField,
        /// <summary>W-121 (#37): a floating shell buoy on a sea folk village's lake surface, a glowing pearl on a short pole, its cord running down.</summary>
        ShellBuoy,
        /// <summary>W-121 (#37): a sea folk's log raft moored by a landing, with a shell-scale sail, baskets and a mooring line.</summary>
        SeaRaft,
        /// <summary>W-122: a marsh folk reed hut on stilts over a pool, its plank walk running +Z to the smoke hut's deck.</summary>
        MarshHut,
        /// <summary>W-122: the marsh folk's central smoke hut on a wide plank deck over the pool, smoke rising from its roof.</summary>
        SmokeHut,
        /// <summary>W-122: a plank walk across from hut to hut (along X), or out to the bank (along -Z), of boardwalk pieces.</summary>
        MarshCross,
        /// <summary>W-122: the marsh folk's walk from the huts out to the bank (along -Z), ending in a step down.</summary>
        MarshWalk,
        /// <summary>W-127: a street light (the species' Fixture Style) that a settlement puts by doors, along streets and
        /// at piers (SettlementLayout.Fixtures, LandmarkType.Fixtures), lit after dark.</summary>
        Fixture,
        /// <summary>W-147: a free-standing ladder from its foot (the origin) up StructureKit.LadderPieceHeight(variant),
        /// climbed from +Z: the mines' ladders between levels and up shafts.</summary>
        Ladder
    }

    /// <summary>
    /// W-127: what a Fixture kit species builds. Its light's colour and reach follow from it (FloraPlacement.GlowLight);
    /// which folk and biomes use which is data (the fixture tables). A byte, append only.
    /// </summary>
    public enum FixtureStyle : byte
    {
        /// <summary>A post with an arm and a hanging lantern.</summary>
        Lantern,
        /// <summary>A waist-high stub of wall by a path with a lamp on a bracket.</summary>
        Sconce,
        /// <summary>An iron fire bowl on three legs, its coals glowing.</summary>
        Brazier,
        /// <summary>A tall stand with a ring of candles.</summary>
        Candelabra,
        /// <summary>A standing stone with a glowing rune cut in it.</summary>
        Rune,
        /// <summary>A pier post hung with a fish-oil lamp, a net and a float.</summary>
        PierLamp,
        /// <summary>A stone beacon tower on a town's square, a stair winding up its outside to a railed top where a
        /// fire burns after dark (LandmarkEffectSystem lights it; seen from far off).</summary>
        Beacon,
        /// <summary>W-138 row 4c: a small lantern on an iron bracket, fixed to a wall, turned low: dimmer and shorter
        /// reaching than a street Lantern (a road tunnel's lamps). Its foot is the wall at the bracket; the lantern
        /// hangs out along +Z.</summary>
        WallLantern
    }

    /// <summary>The small things a sea folk settlement leaves on the lake bed (#37), picked by weight from SeaDetails.</summary>
    public enum SeaDetail : byte
    {
        KelpGarden, ShellMosaic, BubbleVent, TradeGoods, CoralArch, Anemone
    }

    /// <summary>The small things a turtle folk settlement leaves about its mountaintop (#38), picked by weight from AlpineDetails.</summary>
    public enum AlpineDetail : byte
    {
        PrayerFlags, SnowPot, RuneStone, Cairn, IceLantern, Sled
    }

    /// <summary>The small things a fishing village leaves about (#36), picked by weight from CoastDetails.</summary>
    public enum CoastDetail : byte
    {
        Boat, NetFrame, FishRack, CrabPots, Driftwood, Lantern,
        /// <summary>W-119: a coil of rope lying on the ground or a deck.</summary>
        RopeCoil,
        /// <summary>W-119: an old post or two standing up out of the sand, a gull on one.</summary>
        GullPost
    }

    /// <summary>How a culture roofs its buildings.</summary>
    public enum KitRoof : byte
    {
        /// <summary>Two pitched slabs over stepped gable ends (humans).</summary>
        Gable,
        /// <summary>Shrinking tiers with deep eaves (elves).</summary>
        Tiered,
        /// <summary>A flat roof behind a crenellated parapet (dwarves).</summary>
        Flat,
        /// <summary>A steep thatch-like gable down to low eaves (snow folk, goblins).</summary>
        Steep
    }

    /// <summary>
    /// How a kit surface is drawn up close (W-99): the flora shader's planks, shingles or thatch (FloraMeshBuilder.Surface
    /// takes the number). Plain surfaces keep the weathered render or plaster look.
    /// </summary>
    public enum KitSurface : byte { Plain, Planks, Shingles, Thatch }

    /// <summary>One box of a kit structure, in the structure's own space (+Z is the front, y up from the ground).</summary>
    public struct KitPart
    {
        public KitPiece Piece;
        public KitMaterial Material;
        public Vector3 Centre, Size;
        public Quaternion Rotation;
        /// <summary>Stands on the ground and stops the player (ruins collide per standing wall bay).</summary>
        public bool Solid;
        /// <summary>Not carried into the ground by the foundation skirt: boats keep their hulls (W-100).</summary>
        public bool Afloat;
        /// <summary>W-128: a moored boat's bottom plank; FloraModel.Boats keeps where its underside sits (the waterline check).</summary>
        public bool Keel;
    }

    /// <summary>
    /// A culture's trim set: the colours and proportions that make the same kit pieces read as one folk's buildings.
    /// Fishing villages (#36) and new races only need a new set, not new pieces.
    /// </summary>
    [System.Serializable]
    public struct CultureTrim
    {
        public Color Wall, Trim, RoofColour, Wood, Opening, Moss;
        public KitRoof Roof;
        /// <summary>Storey height (m), bay width (m) and roof pitch (degrees).</summary>
        public float Storey, Bay, Pitch;
        /// <summary>Dressed quoins up the corners, crenels on parapets and towers, shutters by windows.</summary>
        public bool Quoins, Crenels, Shutters;
        /// <summary>Chance of a corner tower, a balcony and an outside stair on a building of two storeys or more.</summary>
        public float TowerChance, BalconyChance, StairChance;
    }

    /// <summary>
    /// The general structure kit (#24): RuinKit's set pieces join rooms underground; these pieces build what stands above
    /// it. Multi-storey walls split into bays (solid, window or door), string courses, quoins, plinths, roofs in four
    /// styles, balconies, outside stairs, corner towers, gates and docks-ready platforms, each as plain data (KitPart),
    /// coloured by a culture's trim set. A house is fully decided by its seed; with decay above 0 the same house comes
    /// out broken: walls fallen from one corner, roof gone but a few rafters, floors showing, rubble and moss.
    /// RuinKit.Temple is untouched by this (its layouts and fingerprints stay the same).
    /// </summary>
    public static class StructureKit
    {
        public const float Thick = 0.4f;

        /// <summary>The trim set of a folk's buildings.</summary>
        public static CultureTrim For(FolkCulture culture)
        {
            switch (culture)
            {
                case FolkCulture.Dwarf:
                    return new CultureTrim
                    {
                        Wall = new Color(0.52f, 0.47f, 0.42f), Trim = new Color(0.36f, 0.33f, 0.31f), RoofColour = new Color(0.33f, 0.31f, 0.30f),
                        Wood = new Color(0.30f, 0.20f, 0.13f), Opening = new Color(0.10f, 0.08f, 0.07f), Moss = new Color(0.30f, 0.40f, 0.20f),
                        Roof = KitRoof.Flat, Storey = 3.2f, Bay = 2.6f, Pitch = 0f, Quoins = true, Crenels = true, Shutters = false,
                        TowerChance = 0.45f, BalconyChance = 0.2f, StairChance = 0.6f
                    };
                case FolkCulture.Elf:
                    return new CultureTrim
                    {
                        Wall = new Color(0.82f, 0.80f, 0.70f), Trim = new Color(0.55f, 0.62f, 0.45f), RoofColour = new Color(0.30f, 0.45f, 0.36f),
                        Wood = new Color(0.45f, 0.33f, 0.20f), Opening = new Color(0.12f, 0.14f, 0.10f), Moss = new Color(0.36f, 0.50f, 0.24f),
                        Roof = KitRoof.Tiered, Storey = 3.4f, Bay = 2.2f, Pitch = 0f, Quoins = false, Crenels = false, Shutters = false,
                        TowerChance = 0.35f, BalconyChance = 0.7f, StairChance = 0.3f
                    };
                case FolkCulture.Goblin:
                case FolkCulture.Ogre:
                case FolkCulture.SnowFolk:
                    return new CultureTrim
                    {
                        Wall = new Color(0.50f, 0.42f, 0.33f), Trim = new Color(0.36f, 0.27f, 0.18f), RoofColour = new Color(0.58f, 0.50f, 0.32f),
                        Wood = new Color(0.33f, 0.24f, 0.15f), Opening = new Color(0.09f, 0.07f, 0.05f), Moss = new Color(0.34f, 0.42f, 0.20f),
                        Roof = KitRoof.Steep, Storey = 2.6f, Bay = 2.2f, Pitch = 50f, Quoins = false, Crenels = false, Shutters = true,
                        TowerChance = 0.1f, BalconyChance = 0.1f, StairChance = 0.2f
                    };
                case FolkCulture.FishFolk:
                    // The coastal trim (#36): weathered grey-blue planks, white-washed trim, reed thatch, low single
                    // storeys; no towers or balconies (stilts and porches come from KitShape.StiltHouse).
                    return new CultureTrim
                    {
                        Wall = new Color(0.52f, 0.55f, 0.56f), Trim = new Color(0.84f, 0.83f, 0.78f), RoofColour = new Color(0.62f, 0.55f, 0.36f),
                        Wood = new Color(0.40f, 0.33f, 0.25f), Opening = new Color(0.08f, 0.09f, 0.10f), Moss = new Color(0.32f, 0.42f, 0.30f),
                        Roof = KitRoof.Steep, Storey = 2.6f, Bay = 2.0f, Pitch = 46f, Quoins = false, Crenels = false, Shutters = true,
                        TowerChance = 0f, BalconyChance = 0f, StairChance = 0f
                    };
                case FolkCulture.TurtleFolk:
                    // The turtle folk's trim (#38): snow-white block walls, glacier-blue trim, a pale shell-green for
                    // wood and doors, tall 3.6 m storeys for a tall race; their igloos are domes (KitShape.Igloo).
                    return new CultureTrim
                    {
                        Wall = new Color(0.88f, 0.91f, 0.94f), Trim = new Color(0.62f, 0.76f, 0.86f), RoofColour = new Color(0.80f, 0.86f, 0.92f),
                        Wood = new Color(0.42f, 0.50f, 0.38f), Opening = new Color(0.06f, 0.10f, 0.14f), Moss = new Color(0.40f, 0.52f, 0.42f),
                        Roof = KitRoof.Flat, Storey = 3.6f, Bay = 2.4f, Pitch = 0f, Quoins = false, Crenels = false, Shutters = false,
                        TowerChance = 0f, BalconyChance = 0f, StairChance = 0f
                    };
                case FolkCulture.BatFolk:
                    // The bat folk's trim (#35): guano-white daub walls, dark leather-brown trim and shutters (kept shut:
                    // they hate light), steep hide roofs, low 2.2 m storeys and narrow bays; no towers, balconies or
                    // stairs (they fly up). Their houses hang from cave walls and ceilings (BatFolkBaking).
                    return new CultureTrim
                    {
                        Wall = new Color(0.86f, 0.84f, 0.78f), Trim = new Color(0.26f, 0.19f, 0.14f), RoofColour = new Color(0.34f, 0.27f, 0.22f),
                        Wood = new Color(0.30f, 0.22f, 0.15f), Opening = new Color(0.05f, 0.04f, 0.04f), Moss = new Color(0.40f, 0.42f, 0.34f),
                        Roof = KitRoof.Steep, Storey = 2.2f, Bay = 1.6f, Pitch = 56f, Quoins = false, Crenels = false, Shutters = true,
                        TowerChance = 0f, BalconyChance = 0f, StairChance = 0f
                    };
                case FolkCulture.SeaFolk:
                    // The sea folk's trim (#37): pearl-pink shell walls, coral trim, nacre roofs, driftwood, kelp-green moss.
                    return new CultureTrim
                    {
                        Wall = new Color(0.93f, 0.84f, 0.80f), Trim = new Color(0.92f, 0.48f, 0.40f), RoofColour = new Color(0.78f, 0.86f, 0.88f),
                        Wood = new Color(0.52f, 0.46f, 0.38f), Opening = new Color(0.04f, 0.10f, 0.14f), Moss = new Color(0.20f, 0.44f, 0.26f),
                        Roof = KitRoof.Flat, Storey = 3.2f, Bay = 2.4f, Pitch = 0f, Quoins = false, Crenels = false, Shutters = false,
                        TowerChance = 0f, BalconyChance = 0f, StairChance = 0f
                    };
                case FolkCulture.MarshFolk:
                    // W-122: the marsh folk's trim: reed-ochre walls, dark bog-oak wood, pale straw thatch on steep roofs,
                    // green-brown moss; low storeys, no towers, balconies or stairs (walks and stilts come from the shapes).
                    return new CultureTrim
                    {
                        Wall = new Color(0.44f, 0.37f, 0.24f), Trim = new Color(0.30f, 0.24f, 0.14f), RoofColour = new Color(0.66f, 0.55f, 0.30f),
                        Wood = new Color(0.27f, 0.21f, 0.14f), Opening = new Color(0.06f, 0.06f, 0.04f), Moss = new Color(0.30f, 0.38f, 0.16f),
                        Roof = KitRoof.Steep, Storey = 2.4f, Bay = 2.0f, Pitch = 52f, Quoins = false, Crenels = false, Shutters = false,
                        TowerChance = 0f, BalconyChance = 0f, StairChance = 0f
                    };
                default:
                    return new CultureTrim
                    {
                        Wall = new Color(0.78f, 0.72f, 0.62f), Trim = new Color(0.55f, 0.50f, 0.45f), RoofColour = new Color(0.55f, 0.27f, 0.20f),
                        Wood = new Color(0.36f, 0.24f, 0.15f), Opening = new Color(0.10f, 0.09f, 0.08f), Moss = new Color(0.33f, 0.45f, 0.22f),
                        Roof = KitRoof.Gable, Storey = 3f, Bay = 2.4f, Pitch = 38f, Quoins = true, Crenels = false, Shutters = true,
                        TowerChance = 0.18f, BalconyChance = 0.45f, StairChance = 0.35f
                    };
            }
        }


        /// <summary>
        /// A folk's wall and roof looks (W-99): timber folk (fish folk, goblins, ogres, snow folk) build in planks under
        /// thatch; humans roof in shingles or planks, chosen by the building's seed; elves in shingles. Stone, daub, shell
        /// and ice stay plain. Drawing only: no draw is taken from the layout's own random stream.
        /// </summary>
        public static (KitSurface Wall, KitSurface Roof) Surfaces(FolkCulture culture, uint seed)
        {
            switch (culture)
            {
                case FolkCulture.FishFolk:
                case FolkCulture.MarshFolk:
                case FolkCulture.Goblin:
                case FolkCulture.Ogre:
                case FolkCulture.SnowFolk:
                    return (KitSurface.Planks, KitSurface.Thatch);
                case FolkCulture.Elf:
                    return (KitSurface.Plain, KitSurface.Shingles);
                case FolkCulture.Dwarf:
                case FolkCulture.TurtleFolk:
                case FolkCulture.BatFolk:
                case FolkCulture.SeaFolk:
                    return (KitSurface.Plain, KitSurface.Plain);
                default:
                    return (KitSurface.Plain, ((seed * 0x9E3779B1u) >> 31) == 0 ? KitSurface.Shingles : KitSurface.Planks);
            }
        }

        /// <summary>W-127: lamplight's colour (lamps, and a lived-in house's lit windows).</summary>
        public static readonly Color LampLight = new Color(1f, 0.72f, 0.38f);

        /// <summary>W-138 row 4c: a turned-down lantern's glass (KitMaterial.Flame): deeper and redder than LampLight.</summary>
        public static readonly Color FlameLight = new Color(0.42f, 0.15f, 0.03f);

        /// <summary>A culture's trim colour for a material.</summary>
        public static Color Colour(in CultureTrim t, KitMaterial m) => m switch
        {
            KitMaterial.Trim => t.Trim,
            KitMaterial.Roof => t.RoofColour,
            KitMaterial.Wood => t.Wood,
            KitMaterial.Opening => t.Opening,
            KitMaterial.Moss => t.Moss,
            KitMaterial.Stone => Color.Lerp(t.Wall, t.Trim, 0.5f) * 0.85f,
            KitMaterial.Lamp => LampLight,
            KitMaterial.Flame => FlameLight,
            KitMaterial.Net => new Color(0.70f, 0.66f, 0.55f),
            KitMaterial.Rune => new Color(0.55f, 0.95f, 1f),
            KitMaterial.Ice => Color.Lerp(t.Wall, t.Trim, 0.25f),
            KitMaterial.Copper => new Color(0.62f, 0.40f, 0.24f),
            KitMaterial.Sand => new Color(0.84f, 0.73f, 0.52f),
            KitMaterial.Ash => new Color(0.36f, 0.34f, 0.33f),
            KitMaterial.Dirt => Color.Lerp(new Color(0.52f, 0.41f, 0.29f), t.Wall, 0.15f),
            _ => t.Wall
        };

        // ------------------------------------------------------------------------------------------- coast (#36)

        /// <summary>Which shore details turn up and how often (data: add a kind here and a case in Detail).</summary>
        public static readonly (CoastDetail Kind, float Weight)[] CoastDetails =
        {
            (CoastDetail.Boat, 3f), (CoastDetail.NetFrame, 2f), (CoastDetail.FishRack, 2f),
            (CoastDetail.CrabPots, 2f), (CoastDetail.Driftwood, 1.5f), (CoastDetail.Lantern, 1f),
            (CoastDetail.RopeCoil, 1f), (CoastDetail.GullPost, 1f)
        };

        /// <summary>
        /// W-119: how many fishing trades a village can follow (FloraPlacement gives each village one): 0 crabbers,
        /// 1 net fishers, 2 fish driers. The trade weights the shore details and picks the boardwalk's deck details.
        /// </summary>
        public const int FishTrades = 3;

        /// <summary>W-128: how far a dock's or wharf's base stands above the water (FloraPlacement.DockShore puts it there).</summary>
        public const float DockWater = 0.25f;
        /// <summary>W-128: how deep a moored boat's hull sits in the water, underside to waterline.</summary>
        public const float BoatDraft = 0.18f;

        // ------------------------------------------------------------------------------------------------ ladders (W-147)

        /// <summary>W-147: how far a ladder's rails reach above its landing (something to hold stepping off).</summary>
        public const float LadderGrab = 0.9f;
        /// <summary>W-147: a ladder's width, rail to rail.</summary>
        public const float LadderWidth = 0.55f;
        /// <summary>W-147: how far a water ladder's foot reaches under the water.</summary>
        public const float LadderWet = 1.4f;
        /// <summary>W-147: a ladder's Climbability grade (A-101: 5 smooth wall .. 1 ladder).</summary>
        public const byte LadderGrade = 1;
        /// <summary>
        /// W-147: the mine ladder pieces (KitShape.Ladder) by variant: 2 m to 4 m in 0.5 m steps. No more than the cave's
        /// models per species (CaveSystem.PlantVariants, 5): a variant past those has no model and isn't drawn.
        /// </summary>
        public const int LadderPieces = 5;
        public static float LadderPieceHeight(int variant) => 2f + Mathf.Clamp(variant, 0, LadderPieces - 1) * 0.5f;

        /// <summary>
        /// W-147: adds a ladder from <paramref name="foot"/> up to <paramref name="landing"/> (where a climber steps off),
        /// climbed from the <paramref name="outward"/> side (horizontal). The rails reach <paramref name="grab"/> past the
        /// landing. Rope rails (with wood rungs) at fishing villages, wood elsewhere.
        /// </summary>
        public static void Ladder(List<KitPart> parts, Vector3 foot, Vector3 landing, Vector3 outward, bool rope, float grab = LadderGrab)
        {
            var axis = landing - foot;
            var length = axis.magnitude;
            if (length < 0.3f)
                return;
            var up = axis / length;
            outward.y = 0f;
            var forward = outward - up * Vector3.Dot(outward, up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.Cross(up, Vector3.right);
            var total = length + grab;
            parts.Add(new KitPart
            {
                Piece = KitPiece.Ladder, Material = rope ? KitMaterial.Net : KitMaterial.Wood,
                Centre = foot + up * (total * 0.5f), Size = new Vector3(LadderWidth, total, 0.08f),
                Rotation = Quaternion.LookRotation(forward.normalized, up), Solid = false, Afloat = foot.y < -0.01f   // (a water ladder keeps its foot: no skirt)
            });
        }

        /// <summary>W-147: a ladder part's foot (the bottom of its rails).</summary>
        public static Vector3 LadderFoot(in KitPart p) => p.Centre - p.Rotation * Vector3.up * (p.Size.y * 0.5f);
        /// <summary>W-147: a ladder part's landing (where a climber steps off, <paramref name="grab"/> below the rails' tops).</summary>
        public static Vector3 LadderLanding(in KitPart p, float grab = LadderGrab) => p.Centre + p.Rotation * Vector3.up * (p.Size.y * 0.5f - grab);

        /// <summary>
        /// W-147: a mine's ladder piece, <paramref name="height"/> tall from its foot at the origin, climbed from +Z (the
        /// rock it stands against is at -Z). No grab past the top: pieces stack up a shaft, and the bake ends the top
        /// piece LadderGrab above the landing.
        /// </summary>
        public static List<KitPart> LadderPiece(float height)
        {
            var parts = new List<KitPart>();
            Ladder(parts, Vector3.zero, Vector3.up * height, Vector3.forward, false, 0f);
            return parts;
        }

        /// <summary>
        /// W-147: the long rail on one side (<paramref name="side"/> -1 or 1, its x sign) of a Platform built along Z,
        /// split round a gap at <paramref name="z"/> so a ladder can come up through it.
        /// </summary>
        static void SplitRail(List<KitPart> parts, float side, float z, float gap = 0.9f)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Piece != KitPiece.Rail || p.Size.z < 2f || Mathf.Sign(p.Centre.x) != side || p.Rotation != Quaternion.identity)
                    continue;
                float z0 = p.Centre.z - p.Size.z * 0.5f, z1 = p.Centre.z + p.Size.z * 0.5f;
                if (z <= z0 || z >= z1)
                    continue;
                var a = new Vector2(z0, z - gap * 0.5f);
                var b = new Vector2(z + gap * 0.5f, z1);
                p.Centre.z = (a.x + a.y) * 0.5f;
                p.Size.z = a.y - a.x;
                parts[i] = p;
                p.Centre.z = (b.x + b.y) * 0.5f;
                p.Size.z = b.y - b.x;
                parts.Add(p);
                return;
            }
        }

        /// <summary>
        /// W-147: the ladders on a dock or pier (a Platform along +Z from the origin, <paramref name="length"/> long, its
        /// deck <paramref name="deck"/> up, the water at <paramref name="water"/>): one down into the water off the far
        /// end, and one or two on the sides between posts, clear of what's on the deck there (crab pots, lamps) and off
        /// the side a boat is moored. Its own draws, after the structure's, so the rest is as it was. Parts from
        /// <paramref name="detailsFrom"/> on are the things to keep clear of.
        /// </summary>
        static void WaterLadders(List<KitPart> parts, uint seed, float length, float width, float deck, float water, int posts, int detailsFrom, bool rope)
        {
            var rng = new System.Random((int)(Scramble(seed ^ 0x1ADDu) & 0x7FFFFFFF));
            var foot = water - LadderWet;
            // The far end, a little off the middle (a dock's lantern stands at +X).
            var endX = -width * (0.12f + 0.1f * (float)rng.NextDouble());
            Ladder(parts, new Vector3(endX, foot, length + 0.12f), new Vector3(endX, deck, length + 0.12f), Vector3.forward, rope);
            // The sides: the side away from a moored boat, else either.
            var boat = 0f;
            for (int i = detailsFrom; i < parts.Count; i++)
                if (parts[i].Keel)
                    boat = Mathf.Sign(parts[i].Centre.x);
            var side = boat != 0f ? -boat : (rng.Next(2) == 0 ? -1f : 1f);
            var gaps = posts - 1;
            var count = length > 14f ? 2 : 1;
            var used = -1;
            for (int n = 0; n < count && gaps >= 3; n++)
            {
                // Bays from the second to the second last (not by the ramp, not by the end ladder), from a random start.
                var start = rng.Next(gaps - 2);
                for (int t = 0; t < gaps - 2; t++)
                {
                    var k = 1 + (start + t) % (gaps - 2);
                    if (used >= 0 && Mathf.Abs(k - used) < 2)
                        continue;
                    var z = (k + 0.5f) * length / gaps;
                    var edge = new Vector3(side * (width * 0.5f + 0.1f), deck, z);
                    var clear = true;
                    for (int i = detailsFrom; i < parts.Count && clear; i++)
                        if (parts[i].Piece != KitPiece.Ladder && !parts[i].Keel && parts[i].Centre.y > deck - 0.3f &&
                            new Vector2(parts[i].Centre.x - edge.x, parts[i].Centre.z - edge.z).magnitude < 1.1f)
                            clear = false;
                    if (!clear)
                        continue;
                    SplitRail(parts, side, z);
                    Ladder(parts, new Vector3(edge.x, foot, z), edge, new Vector3(side, 0f, 0f), rope);
                    used = k;
                    break;
                }
                side = -side;   // a second one on the other side
            }
        }

        /// <summary>
        /// W-147: a share of plain houses, by their folk's roof, get a ladder up to the roof: a flat roof half the time,
        /// up a side wall to the roof behind its parapet; one pitched roof in five, up the back wall to the eave.
        /// Tiered roofs never. Its own draws from the seed (the house is as it was). Wood.
        /// </summary>
        public static void RoofLadder(List<KitPart> parts, uint seed, CultureTrim t, float width, float depth, int storeys)
        {
            var hash = Scramble(seed ^ 0x2ADDu);
            var roll = (hash & 0xFFFF) / 65535f;   // straight from the hash: System.Random's first draw follows its seed
            var rng = new System.Random((int)(Scramble(hash) & 0x7FFFFFFF));
            var top = Mathf.Max(1, storeys) * t.Storey;
            switch (t.Roof)
            {
                case KitRoof.Flat:
                {
                    if (roll > 0.5f)
                        return;
                    // Up a side wall, at a bay joint (the windows sit mid-bay).
                    var side = rng.Next(2) == 0 ? -1f : 1f;
                    var bays = Mathf.Max(1, Mathf.RoundToInt(depth / t.Bay));
                    var z = bays > 1 ? -depth * 0.5f + (1 + rng.Next(bays - 1)) * depth / bays : -depth * 0.5f + 0.5f;
                    var x = side * (width * 0.5f + Thick * 0.5f + 0.1f);
                    Ladder(parts, new Vector3(x, 0f, z), new Vector3(x, top + 0.7f, z), new Vector3(side, 0f, 0f), false);
                    break;
                }
                case KitRoof.Gable:
                case KitRoof.Steep:
                {
                    if (roll > 0.2f)
                        return;
                    // Up the back wall, its foot a little out, leaning in to the eave's edge.
                    const float eave = 0.45f, slab = 0.2f;
                    var pitch = Mathf.Max(t.Pitch, 20f) * Mathf.Deg2Rad;
                    var edge = top + slab - eave * Mathf.Tan(pitch);
                    var x = (rng.NextDouble() < 0.5 ? -1f : 1f) * width * 0.25f;
                    var z = -(depth * 0.5f + eave + 0.1f);
                    Ladder(parts, new Vector3(x, 0f, z - 0.35f), new Vector3(x, edge, z), Vector3.back, false);
                    break;
                }
            }
        }

        /// <summary>W-119: how much more often a trade's own details turn up on its shore.</summary>
        static float TradeWeight(CoastDetail kind, int trade) => trade switch
        {
            0 => kind == CoastDetail.CrabPots ? 4f : kind == CoastDetail.Boat ? 1.5f : 1f,
            1 => kind == CoastDetail.NetFrame ? 4f : kind == CoastDetail.RopeCoil ? 2f : 1f,
            2 => kind == CoastDetail.FishRack ? 4f : kind == CoastDetail.GullPost ? 2.5f : 1f,
            _ => 1f
        };

        /// <summary>A seeded pick from CoastDetails by weight (W-119: leaning to a village's trade, or none for -1).</summary>
        public static CoastDetail PickDetail(System.Random rng, int trade = -1)
        {
            var total = 0f;
            foreach (var d in CoastDetails) total += d.Weight * TradeWeight(d.Kind, trade);
            var roll = (float)rng.NextDouble() * total;
            foreach (var d in CoastDetails)
                if ((roll -= d.Weight * TradeWeight(d.Kind, trade)) < 0f)
                    return d.Kind;
            return CoastDetails[0].Kind;
        }

        /// <summary>
        /// A fishing folk's house (#36): one storey of House raised on stilts over a plank deck with a porch at the door
        /// (+Z), and a detail or two by its stair (a net frame, a fish rack, a boat pulled up). W-105: the house stands
        /// out over the shallows: a railed plank walkway runs from the porch back to the shore, and the stair at its end
        /// comes down at the origin (the plot, on the bank), so the house itself is <see cref="StiltReach"/> or so out
        /// along -Z. Posts that reach the ground carry on <see cref="StiltPile"/> m down as piles into the sea bed.
        /// </summary>
        public static List<KitPart> StiltHouse(uint seed, CultureTrim t, float width, float depth, float stilts)
        {
            var parts = House(seed, t, width, depth, 1, 0f);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.Centre.y += stilts;
                parts[i] = p;
            }
            Platform(parts, Vector3.zero, 0f, width + 1.6f, depth + 1.6f, stilts, false);
            // W-100: the porch and its stair line up with the door, which sits off centre when the front has an even
            // number of bays (they used to stand at the middle, half a bay to one side of it).
            var doorX = DoorX(t, width);
            Platform(parts, new Vector3(doorX, 0f, depth * 0.5f + 0.8f + 1.1f), 0f, 2.4f, 2.2f, stilts, true);
            var steps = Mathf.Max(2, Mathf.CeilToInt(stilts / 0.28f));
            // W-105: the walkway, its length from the seed's hash (no new draws: the details below are as they were).
            var porchEnd = depth * 0.5f + 0.8f + 2.2f;
            var walk = StiltWalkMin + (Scramble(seed ^ 0x3A1C5u) & 0xFF) / 255f * StiltWalkSpread;
            Platform(parts, new Vector3(doorX, 0f, porchEnd + walk * 0.5f), 0f, 1.6f, walk, stilts, true);
            Stair(parts, new Vector3(doorX, 0f, porchEnd + walk + steps * 0.15f + 0.5f), 180f, stilts, 1.3f);
            // W-147: a rope ladder off the deck's seaward (back) edge, down into the water (no draws).
            var backX = doorX + ((Scramble(seed ^ 0x3ADDu) & 0xFF) / 255f - 0.5f) * width * 0.6f;
            var backZ = -(depth + 1.6f) * 0.5f - 0.12f;
            Ladder(parts, new Vector3(backX, -LadderWet - 0.5f, backZ), new Vector3(backX, stilts, backZ), Vector3.back, true);
            // Everything moves back so the stair's foot is at the origin; posts down to the ground become piles.
            var foot = porchEnd + walk + 0.65f + steps * 0.3f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.Centre.z -= foot;
                p.Centre.x -= doorX;
                if (p.Piece == KitPiece.Column && p.Centre.y - p.Size.y * 0.5f < 0.1f)
                {
                    p.Centre.y -= StiltPile * 0.5f;
                    p.Size.y += StiltPile;
                }
                parts[i] = p;
            }
            var rng = new System.Random((int)(Scramble(seed ^ 0xC0A57u) & 0x7FFFFFFF));
            for (int k = 0, n = 1 + rng.Next(2); k < n; k++)
            {
                var side = k == 0 ? (rng.Next(2) == 0 ? -1f : 1f) : -1f;
                // On the bank beside the stair's foot (they stood beside the house when it was on land).
                var at = new Vector3(side * (2.4f + k * 0.8f), 0f, 0.6f + (float)rng.NextDouble() * 0.5f * depth);
                Detail(parts, PickDetail(rng), at, (float)rng.NextDouble() * 360f, rng, t);
            }
            return parts;
        }

        /// <summary>W-119: how far apart FloraPlacement sets a boardwalk's pieces along its run (m).</summary>
        public const float BoardwalkStep = 3f;
        /// <summary>W-119: a boardwalk piece's length for a variant: 3.2 to 4.6 m in 0.2 m steps, two variants per
        /// length, so the pieces overlap their neighbours a little round bends.</summary>
        public static float BoardwalkLength(int variant) => 3.2f + (variant / 2 % 8) * 0.2f;
        /// <summary>W-119: a boardwalk piece's deck height; odd variants 2 cm higher, so where neighbours overlap the
        /// planks never fight.</summary>
        public static float BoardwalkDeck(int variant) => 0.4f + (variant & 1) * 0.02f;

        /// <summary>
        /// W-119 (#36): a piece of a fishing village's shore boardwalk, along +Z about the origin: planks across a
        /// 1.8 m deck with a solid bed under them (W-62c, so walkers never drop through a gap), posts at its ends and
        /// middle running 2.5 m down so the deck stands clear where the bank dips, and now and then a mooring post
        /// with a coil of rope on top, all from the seed.
        /// </summary>
        /// <remarks>
        /// W-119 row 3: <paramref name="detail"/> above 0 adds the deck details of trade detail - 1 (see FishTrades) on
        /// one edge: crabbers stack pots, net fishers hang a net over a short rail, driers rack fish; a rope coil or a
        /// gull may join them. FloraPlacement picks which pieces carry them.
        /// </remarks>
        public static List<KitPart> Boardwalk(uint seed, CultureTrim t, float length, float deck, int detail = 0)
        {
            const float W = 1.8f;
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xB0A2Du) & 0x7FFFFFFF));
            Platform(parts, Vector3.zero, 0f, W, length, deck, false);
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, deck - 0.17f, 0f), new Vector3(W * 0.94f, 0.1f, length), 0f, false);
            foreach (var z in new[] { -0.5f, 0f, 0.5f })
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(side * W * 0.42f, (deck - 2.5f) * 0.5f, z * (length - 0.3f)), new Vector3(0.22f, deck + 2.5f, 0.22f), 0f, false);
            if (rng.NextDouble() < 0.35)
            {
                // A mooring post on one edge, a little proud of the deck, rope wound round its head.
                var x = (rng.Next(2) == 0 ? -1f : 1f) * (W * 0.5f + 0.1f);
                var z = ((float)rng.NextDouble() - 0.5f) * (length - 0.8f);
                Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(x, (deck + 0.7f - 2.5f) * 0.5f, z), new Vector3(0.26f, deck + 0.7f + 2.5f, 0.26f), 0f, false);
                for (int k = 0; k < 3; k++)
                    Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(x, deck + 0.45f + k * 0.07f, z), new Vector3(0.34f, 0.06f, 0.34f), k * 30f, false);
                if (rng.NextDouble() < 0.4)
                    Gull(parts, new Vector3(x, deck + 0.7f, z), (float)rng.NextDouble() * 360f);
            }
            if (detail > 0)
            {
                // The trade's deck details on one edge (the other edge stays clear to walk), the far side of any post.
                var side = rng.Next(2) == 0 ? -1f : 1f;
                var edge = side * (W * 0.5f - 0.35f);
                var z = ((float)rng.NextDouble() - 0.5f) * (length - 2.6f);
                float R() => (float)rng.NextDouble();
                switch ((detail - 1) % FishTrades)
                {
                    case 0:
                        Detail(parts, CoastDetail.CrabPots, new Vector3(edge, deck, z), side > 0f ? 180f : 0f, rng, t);   // the buoy on top inboard
                        break;
                    case 1:
                    {
                        // A short rail on two posts at the edge, a net hung over it: a long fall outside down towards
                        // the water, a short one inside, a few floats along its edge.
                        var x = side * (W * 0.5f - 0.08f);
                        var run = 1.6f + R() * 0.6f;
                        foreach (var e in new[] { -0.5f, 0.5f })
                            Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(x, deck + 0.5f, z + e * run), new Vector3(0.1f, 1f, 0.1f), 0f, false);
                        Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(x, deck + 0.98f, z), new Vector3(0.08f, 0.08f, run + 0.2f), 0f, false);
                        // Hung in panels of uneven fall (the net bunches between them), each swung out a little.
                        for (int k = 0, panels = 3 + rng.Next(2); k < panels; k++)
                        {
                            var fall = 0.9f + R() * 0.9f;
                            var pz = z + ((k + 0.5f) / panels - 0.5f) * run;
                            var foot = NetHang(parts, x + side * 0.06f, deck + 1f, pz, run / panels * 0.9f, fall, side, 4f + R() * 8f);
                            Add(parts, KitPiece.Trim, KitMaterial.Roof, foot + new Vector3(side * 0.04f, 0.02f, 0f), new Vector3(0.11f, 0.11f, 0.11f), 0f, false);
                        }
                        NetHang(parts, x - side * 0.05f, deck + 1f, z + (R() - 0.5f) * 0.3f, run * 0.6f, 0.5f, -side, 5f);
                        break;
                    }
                    default:
                        Detail(parts, CoastDetail.FishRack, new Vector3(edge, deck, z), 90f, rng, t);
                        break;
                }
                if (R() < 0.5f)
                    Detail(parts, CoastDetail.RopeCoil, new Vector3(-edge * 0.6f, deck, z + (R() < 0.5f ? -1f : 1f) * (length * 0.5f - 0.6f)), R() * 360f, rng, t);
            }
            return parts;
        }

        /// <summary>W-105: a stilt house's walkway is this long, plus up to <see cref="StiltWalkSpread"/> more (m, unscaled).</summary>
        public const float StiltWalkMin = 4f, StiltWalkSpread = 2.5f;
        /// <summary>How far past the ground a stilt house's posts run down into the sea bed (m, before the skirt).</summary>
        public const float StiltPile = 8f;
        /// <summary>About how far out from its plot a stilt house's middle stands (m, unscaled: stair, walkway, porch and half a house).</summary>
        public const float StiltReach = 15f;

        /// <summary>
        /// Where House puts its door along the +Z front: the middle bay, which is half a bay right of centre when the
        /// front has an even number of bays. Porches, stairs and lamps line up with this.
        /// </summary>
        public static float DoorX(CultureTrim t, float width)
        {
            var bays = Mathf.Max(1, Mathf.RoundToInt(width / t.Bay));
            var bw = width / bays;
            return -width * 0.5f + (bays / 2 + 0.5f) * bw;
        }

        /// <summary>Whether a kit shape is a walled house with a door and an inside (House, StiltHouse and the #69 hazard houses).</summary>
        public static bool HouseLike(KitShape s) => s == KitShape.House || s == KitShape.StiltHouse || s >= KitShape.AshHouse && s <= KitShape.SlabShelter;   // W-119: Boardwalk comes after them

        // ------------------------------------------------------------------------------------------- hazard houses (#69)

        /// <summary>
        /// A harsh-biome hazard house (#69): one storey of House (its door bay on +Z) with what the biome asks for, from
        /// the seed with its own draws (so House's own draws, and every other kit building, are unchanged).
        /// AshHouse: a second stone skin round the walls, a pitched hood over the door on stone cheeks, ash heaped against
        /// the walls and drifted on the hood. SnowHouse: snow banked steeply up the back and sides so the walls stand half
        /// sunk, low banks flanking the door, snow along the ridge. StormHouse: a copper rod up past the ridge at a back
        /// corner, its strap down to a stone plate and on to an earth stake, a strap along the ridge. SandHouse: a stone
        /// skin, a four-post porch under slats and a cloth, sand drifted against the walls. The door's front stays clear.
        /// </summary>
        /// <summary>How deep (m) a slab shelter's (#69) plot is dug (SettlementBaking lowers its pad by this).</summary>
        public const float SlabDig = 0.9f;

        /// <summary>
        /// W-95 (#69): a slab shelter. Three rough rock slabs stand as back and side walls, each buried a little, leaning
        /// and twisted by the seed, with a few lumps on their faces; a fourth rests across their uneven tops as the roof,
        /// tipped to match. Wall is the local rock (ash, granite, scorched, sandstone), Trim its cap (ash, snow, char,
        /// sand) on the roof and wall tops. The floor is the dug plot (SlabDig below the street); a dirt ramp runs from
        /// the open front (+Z) up the bank. Every slab is its own collision box (no Solid circles: you walk in under it).
        /// </summary>
        public static List<KitPart> SlabShelter(uint seed, CultureTrim t, float width, float depth)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x51AB5u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            float S() => R() * 2f - 1f;
            float hw = width * 0.5f, hd = depth * 0.5f;
            const float bury = 0.5f;
            void Lumps(Vector3 c, Vector3 size, float yaw, float pitch, float roll, Vector3 outward)
            {
                var q = Quaternion.Euler(pitch, yaw, roll);
                var n = 2 + rng.Next(3);
                for (int i = 0; i < n; i++)
                {
                    var along = new Vector3(S() * size.x * 0.35f, R() * size.y * 0.35f, S() * size.z * 0.35f);
                    along -= Vector3.Scale(along, new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z)));   // keep it on the face
                    var lump = new Vector3(0.7f + R() * 0.9f, 0.5f + R() * 0.7f, 0.7f + R() * 0.9f);
                    var thick = Mathf.Abs(Vector3.Dot(size, outward));
                    Add(parts, KitPiece.Rubble, KitMaterial.Wall, c + q * (along + outward * (thick * 0.5f + 0.12f)), lump, yaw + S() * 25f, false, pitch + S() * 15f, roll + S() * 15f);
                }
            }
            // The back slab, leaning in or out a little.
            var backH = 2.6f + R() * 0.6f;
            var back = new Vector3(S() * 0.3f, backH * 0.5f - bury, -hd);
            var backSize = new Vector3(width + 0.4f + R() * 0.6f, backH, 0.5f + R() * 0.25f);
            float bYaw = S() * 10f, bPitch = (R() < 0.5f ? -1f : 1f) * (5f + R() * 9f), bRoll = S() * 8f;
            Add(parts, KitPiece.Wall, KitMaterial.Wall, back, backSize, bYaw, false, bPitch, bRoll);
            Lumps(back, backSize, bYaw, bPitch, bRoll, Vector3.back);
            // The side slabs, each its own height, mostly leaning in.
            var tops = new float[2];
            for (int k = 0; k < 2; k++)
            {
                var side = k == 0 ? -1f : 1f;
                var h = backH * (0.75f + R() * 0.35f);
                var len = depth * (0.85f + R() * 0.2f);
                var c = new Vector3(side * hw + S() * 0.2f, h * 0.5f - bury, -hd + len * 0.5f - 0.1f);
                var size = new Vector3(0.45f + R() * 0.25f, h, len);
                float yaw = S() * 14f, pitch = S() * 8f, roll = side * (7f + R() * 11f) * (R() < 0.75f ? 1f : -1f);
                Add(parts, KitPiece.Wall, KitMaterial.Wall, c, size, yaw, false, pitch, roll);
                Lumps(c, size, yaw, pitch, roll, side < 0f ? Vector3.left : Vector3.right);
                tops[k] = h - bury;
                // Its cap along the top edge.
                Add(parts, KitPiece.Trim, KitMaterial.Trim, c + Quaternion.Euler(pitch, yaw, roll) * (Vector3.up * (h * 0.5f + 0.02f)),
                    new Vector3(size.x * 1.1f, 0.12f, len * (0.5f + R() * 0.4f)), yaw, false, pitch, roll);
            }
            // The roof slab across their tops, tipped to their uneven heights, overhanging the front.
            var roofT = 0.5f + R() * 0.25f;
            var roof = new Vector3(S() * 0.6f, (tops[0] + tops[1]) * 0.5f + roofT * 0.5f, 0.35f + R() * 0.4f);
            var roofSize = new Vector3(width + 1.2f + R() * 0.8f, roofT, depth + 0.9f + R() * 0.8f);
            var rRoll = Mathf.Atan2(tops[1] - tops[0], width) * Mathf.Rad2Deg;
            float rYaw = S() * 18f, rPitch = S() * 9f;
            Add(parts, KitPiece.Roof, KitMaterial.Wall, roof, roofSize, rYaw, false, rPitch, rRoll);
            Lumps(roof, roofSize, rYaw, rPitch, rRoll, Vector3.up);
            // Its cap: a ragged sheet or two of the local cover (ash, snow, char, sand).
            for (int i = 0, n = 1 + rng.Next(2); i < n; i++)
                Add(parts, KitPiece.Trim, KitMaterial.Trim, roof + Quaternion.Euler(rPitch, rYaw, rRoll) * new Vector3(S() * width * 0.2f, roofT * 0.5f + 0.05f, S() * depth * 0.2f),
                    new Vector3(roofSize.x * (0.45f + R() * 0.35f), 0.14f, roofSize.z * (0.45f + R() * 0.35f)), rYaw + S() * 20f, false, rPitch, rRoll);
            // The floor and the ramp: trodden dirt from the back slab out of the open front and up the dug bank.
            Add(parts, KitPiece.Trim, KitMaterial.Dirt, new Vector3(0f, 0.03f, 0.2f), new Vector3(width * 0.95f, 0.08f, depth + 0.4f), S() * 4f, false);
            var run = 4.2f + R() * 1.2f;
            var slope = Mathf.Atan2(SlabDig, run) * Mathf.Rad2Deg;
            Add(parts, KitPiece.Trim, KitMaterial.Dirt, new Vector3(S() * 0.3f, SlabDig * 0.5f - 0.04f, hd + 0.4f + run * 0.5f),
                new Vector3(width * (0.55f + R() * 0.2f), 0.1f, Mathf.Sqrt(run * run + SlabDig * SlabDig)), S() * 6f, false, -slope);
            return parts;
        }

        public static List<KitPart> HazardHouse(uint seed, CultureTrim t, KitShape shape, float width, float depth)
        {
            var parts = House(seed, t, width, depth, 1, 0f);
            var rng = new System.Random((int)(Scramble(seed ^ 0x5AFE5u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var door = 0f;
            foreach (var p in parts)
                if (p.Piece == KitPiece.Door) { door = p.Centre.x; break; }
            float hw = width * 0.5f, hd = depth * 0.5f, h = t.Storey;
            var ridge = h + (t.Roof == KitRoof.Flat ? 0.7f : hw * Mathf.Tan(Mathf.Max(t.Pitch, 20f) * Mathf.Deg2Rad));
            switch (shape)
            {
                case KitShape.AshHouse:
                {
                    Skin(parts, KitMaterial.Stone, hw, hd, h * 0.92f, 0.45f, door, 1.8f);
                    // The hood: a pitched slab out over the door on two stone cheeks, ash drifted on top.
                    var hz = hd + Thick * 0.5f + 0.45f + 0.75f;
                    Add(parts, KitPiece.Roof, KitMaterial.Stone, new Vector3(door, h * 0.86f, hz), new Vector3(2.7f, 0.22f, 1.8f), 0f, false, 16f);
                    Add(parts, KitPiece.Rubble, KitMaterial.Ash, new Vector3(door + (R() - 0.5f) * 0.6f, h * 0.86f + 0.17f, hz - 0.2f), new Vector3(1.6f + R() * 0.6f, 0.12f, 1.0f), (R() - 0.5f) * 20f, false, 16f);
                    foreach (var side in new[] { -1f, 1f })
                        Add(parts, KitPiece.Wall, KitMaterial.Stone, new Vector3(door + side * 1.2f, h * 0.41f, hz - 0.1f), new Vector3(0.3f, h * 0.82f, 1.5f), 0f, false);
                    Bank(parts, rng, KitMaterial.Ash, hw + Thick * 0.5f + 0.45f, hd + Thick * 0.5f + 0.45f, 0.5f, 0.4f, door, 3.6f);
                    break;
                }
                case KitShape.SnowHouse:
                    Bank(parts, rng, KitMaterial.Ice, hw + Thick * 0.5f, hd + Thick * 0.5f, h * 0.55f, 0.5f, door, 2.2f);
                    Add(parts, KitPiece.Trim, KitMaterial.Ice, new Vector3(0f, ridge - 0.05f, 0f), new Vector3(0.9f, 0.25f, depth * 0.9f), 0f, false);
                    break;
                case KitShape.StormHouse:
                {
                    var cx = (rng.Next(2) == 0 ? -1f : 1f) * (hw + Thick * 0.5f + 0.12f);
                    var cz = -hd - Thick * 0.5f - 0.12f;
                    var rodTop = ridge + 2.2f + R() * 0.8f;
                    Add(parts, KitPiece.Column, KitMaterial.Copper, new Vector3(cx, rodTop * 0.5f, cz), new Vector3(0.09f, rodTop, 0.09f), 0f, false);
                    for (int k = 0; k < 3; k++)   // a crown of points at the top
                        Add(parts, KitPiece.Trim, KitMaterial.Copper, new Vector3(cx, rodTop + 0.12f, cz), new Vector3(0.04f, 0.4f, 0.04f), k * 120f, false, 22f);
                    for (var y = 0.9f; y < ridge; y += 1.3f)   // stand-off brackets back to the wall
                        Add(parts, KitPiece.Trim, KitMaterial.Copper, new Vector3(cx - Mathf.Sign(cx) * 0.1f, y, cz + 0.06f), new Vector3(0.22f, 0.05f, 0.05f), 0f, false);
                    Add(parts, KitPiece.Platform, KitMaterial.Stone, new Vector3(cx, 0.04f, cz), new Vector3(0.9f, 0.08f, 0.9f), R() * 40f, false);
                    var stake = new Vector3(cx + Mathf.Sign(cx) * (1.2f + R()), 0f, cz - 0.8f - R());
                    var run = stake - new Vector3(cx, 0f, cz);
                    Add(parts, KitPiece.Trim, KitMaterial.Copper, new Vector3(cx, 0.03f, cz) + run * 0.5f, new Vector3(0.05f, 0.04f, run.magnitude), Mathf.Atan2(run.x, run.z) * Mathf.Rad2Deg, false);
                    Add(parts, KitPiece.Column, KitMaterial.Copper, stake + Vector3.up * 0.25f, new Vector3(0.07f, 0.5f, 0.07f), 0f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Copper, new Vector3(0f, ridge + 0.05f, 0f), new Vector3(0.06f, 0.05f, depth + 0.3f), 0f, false);
                    break;
                }
                case KitShape.SandHouse:
                {
                    Skin(parts, KitMaterial.Stone, hw, hd, h * 0.85f, 0.4f, door, 1.8f);
                    // The porch: four posts, slats across and a cloth over them, high enough to walk under.
                    var top = Mathf.Max(2.4f, h * 0.85f);
                    var pz0 = hd + Thick * 0.5f + 0.4f;
                    var pz1 = pz0 + 2.4f + R() * 0.4f;
                    foreach (var side in new[] { -1f, 1f })
                        foreach (var z in new[] { pz0 + 0.2f, pz1 })
                            Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(door + side * 1.5f, top * 0.5f, z), new Vector3(0.16f, top, 0.16f), 0f, false);
                    for (var z = pz0 + 0.15f; z < pz1 + 0.1f; z += 0.42f)
                        Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(door, top + 0.06f, z), new Vector3(3.3f, 0.08f, 0.12f), 0f, false);
                    Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(door + (R() - 0.5f) * 0.3f, top + 0.16f, (pz0 + pz1) * 0.5f), new Vector3(3.0f, 0.04f, pz1 - pz0 - 0.2f), (R() - 0.5f) * 6f, false, -4f);
                    Bank(parts, rng, KitMaterial.Sand, hw + Thick * 0.5f + 0.4f, hd + Thick * 0.5f + 0.4f, 0.75f, 0.5f, door, 3.6f);
                    break;
                }
            }
            return parts;
        }

        /// <summary>A second wall skin round a house's four sides (#69), <paramref name="thick"/> deep, with a gap at the door.</summary>
        static void Skin(List<KitPart> parts, KitMaterial m, float hw, float hd, float height, float thick, float door, float gap)
        {
            var ox = hw + Thick * 0.5f + thick * 0.5f;
            var oz = hd + Thick * 0.5f + thick * 0.5f;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, m, new Vector3(side * ox, height * 0.5f, 0f), new Vector3(thick, height, oz * 2f + thick), 0f, false);
            Add(parts, KitPiece.Wall, m, new Vector3(0f, height * 0.5f, -oz), new Vector3(ox * 2f - thick, height, thick), 0f, false);
            float left = -ox + thick * 0.5f, right = ox - thick * 0.5f, a = door - gap * 0.5f, b = door + gap * 0.5f;
            if (a - left > 0.2f) Add(parts, KitPiece.Wall, m, new Vector3((left + a) * 0.5f, height * 0.5f, oz), new Vector3(a - left, height, thick), 0f, false);
            if (right - b > 0.2f) Add(parts, KitPiece.Wall, m, new Vector3((b + right) * 0.5f, height * 0.5f, oz), new Vector3(right - b, height, thick), 0f, false);
        }

        /// <summary>
        /// Drifts (#69) banked against a house's back, sides and front corners (faces at hw, hd): sloped slabs leaning on
        /// the walls, each a little different in height and length, the front kept clear <paramref name="gap"/> wide round
        /// the door.
        /// </summary>
        static void Bank(List<KitPart> parts, System.Random rng, KitMaterial m, float hw, float hd, float height, float vary, float door, float gap)
        {
            float R() => (float)rng.NextDouble();
            void Lean(Vector3 foot, float yaw, float length, float rise)
            {
                // A 0.6 m slab whose top face runs from rise up the wall down to the ground 1.6 rise out (about 32 degrees).
                var run = rise * 1.6f;
                var slope = Mathf.Sqrt(run * run + rise * rise);
                var pitch = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
                var drop = 0.3f / Mathf.Cos(pitch * Mathf.Deg2Rad);
                var centre = foot + Turn(yaw, new Vector3(0f, rise * 0.5f - drop, run * 0.5f));
                Add(parts, KitPiece.Platform, m, centre, new Vector3(length, 0.6f, slope), yaw, false, pitch);
            }
            for (int s = 0; s < 3; s++)       // back, left, right: two or three overlapping drifts each
            {
                var along = s == 0 ? hw * 2f : hd * 2f;
                var yaw = s == 0 ? 180f : s == 1 ? 270f : 90f;
                var n = 2 + rng.Next(2);
                for (int k = 0; k < n; k++)
                {
                    var off = (k + 0.5f) / n * along - along * 0.5f + (R() - 0.5f) * 0.4f;
                    var foot = s == 0 ? new Vector3(off, 0f, -hd) : new Vector3(s == 1 ? -hw : hw, 0f, off);
                    Lean(foot, yaw, along / n + 0.6f, height * (1f - vary * 0.5f + vary * R()));
                }
            }
            foreach (var side in new[] { -1f, 1f })   // the front, each side of the door
            {
                var inner = door + side * gap * 0.5f;
                var outer = side * hw;
                if ((outer - inner) * side > 0.6f)
                    Lean(new Vector3((inner + outer) * 0.5f, 0f, hd), 0f, Mathf.Abs(outer - inner), height * (0.45f + 0.3f * R()));
            }
        }

        /// <summary>
        /// A dock (#36) from its land end (the origin) out along +Z: a Platform deck on piles that run on down into the
        /// water, a plank ramp up from the shore, a lantern post at the far end, and what the folk leave on it (crab
        /// pots, a coil of net) with a boat moored alongside, all from the seed.
        /// </summary>
        public static List<KitPart> Dock(uint seed, CultureTrim t, float length, float width, float deck)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xD0C4u) & 0x7FFFFFFF));
            Platform(parts, new Vector3(0f, 0f, length * 0.5f), 0f, width, length, deck, true);
            // W-62c: the same solid bed under the planks as the sea folk pier (W-62b), so no support grid cell centre
            // can fall in a plank gap and drop a walker to the sea bed.
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, deck - 0.17f, length * 0.5f), new Vector3(width * 0.94f, 0.1f, length), 0f, false);
            for (int k = 0, piles = Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1); k < piles; k++)
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(side * width * 0.45f, -1.8f, k * length / (piles - 1)), new Vector3(0.26f, 3.6f, 0.26f), 0f, false);
            // A plank ramp up from the bank to the deck (W-59c: the old short stair's treads fell between the support
            // grid's samples, so the player met a 0.5 to 1.6 m wall at the land end). It starts 0.4 m into the ground,
            // about 15 degrees, so it is walked up like a slope; cleats across it for the look.
            const float rampDrop = 0.4f, rampRun = 4.6f;
            var rise = deck + rampDrop;
            var pitch = -Mathf.Atan2(rise, rampRun) * Mathf.Rad2Deg;
            var slope = Mathf.Sqrt(rise * rise + rampRun * rampRun);
            var down = Quaternion.Euler(pitch, 0f, 0f) * Vector3.up * 0.08f;
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, (deck - rampDrop) * 0.5f, -rampRun * 0.5f) - down, new Vector3(width * 0.8f, 0.16f, slope), 0f, false, pitch);
            for (int k = 1; k < 5; k++)
                Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, deck - rise * k / 5f + 0.02f, -rampRun * k / 5f), new Vector3(width * 0.78f, 0.05f, 0.08f), 0f, false);
            var details = parts.Count;
            Detail(parts, CoastDetail.Lantern, new Vector3(width * 0.35f, deck, length - 0.4f), 0f, rng, t);
            // W-59d: the crab pots stand along the deck's left edge, their row turned along the dock and the buoy on the
            // outside, so a walk lane down the middle stays clear (they used to sit across the centre line). Same draws.
            if (rng.NextDouble() < 0.6)
                Detail(parts, CoastDetail.CrabPots, new Vector3(-width * 0.5f + 0.42f, deck, length * (0.4f + 0.4f * (float)rng.NextDouble())), 270f + ((float)rng.NextDouble() - 0.5f) * 8f, rng, t);
            if (rng.NextDouble() < 0.75)   // moored alongside, riding at the water
                Detail(parts, CoastDetail.Boat, new Vector3((rng.Next(2) == 0 ? -1f : 1f) * (width * 0.5f + 1.1f), -DockWater, length * (0.45f + 0.35f * (float)rng.NextDouble())), 0f, rng, t, true);
            // W-147: rope ladders down into the water off the end and a side, so a swimmer can climb out.
            WaterLadders(parts, seed, length, width, deck, -DockWater, Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1), details, true);
            return parts;
        }

        /// <summary>
        /// A fishing village's wharf (W-65, #36): a small network of plank walkways from its land end (the origin) out
        /// along +Z, with two to four houses standing over the water on it. One main walkway with a plank ramp up from
        /// the bank, then one to three branches across it (a T or an L at the far end, maybe a cross part way out). Each
        /// house's door faces a branch that runs tight along its front, parallel to it, with no rail between; rails run
        /// only along open-water edges. Lanterns at the joins, skiffs tied alongside, a ladder down to the water, nets
        /// drying over a rail, crab pots, all from the seed. Every walkway has a solid bed under its planks (W-62c) and
        /// piles down into the water. <paramref name="houses"/> gets each house as (centre x, centre z, width, depth),
        /// its door on the side towards the walkway it faces (FloraGenerator sets its colliders from these).
        /// </summary>
        public static List<KitPart> Wharf(uint seed, CultureTrim t, float deck, List<Vector4> houses)
        {
            const float W = 2.4f, half = W * 0.5f;
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x3A4Fu) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var length = 16f + R() * 6f;
            // Walkways as rectangles (x0, z0, x1, z1). The +X side stays short: a fishing village's second dock stands
            // some 12 m off that way.
            var walks = new List<Vector4> { new Vector4(-half, 0f, half, length + half) };
            var endT = R() < 0.55f;
            walks.Add(endT ? new Vector4(-(7f + R() * 5f), length - half, 5f + R() * 2f, length + half)
                           : new Vector4(-(11f + R() * 3f), length - half, half, length + half));
            var branches = 1 + rng.Next(3);
            var mid = 8f + R() * (length - 16f);
            if (branches >= 2)
                walks.Add(new Vector4(-(7f + R() * 6f), mid - half, half, mid + half));
            if (branches >= 3)
                walks.Add(new Vector4(-half, mid - half, 5f + R() * 2f, mid + half));

            // Houses: tried along each branch, seaward then shoreward, front wall 0.2 m behind the walkway edge.
            houses.Clear();
            var want = 2 + rng.Next(3);
            var homes = new List<(Vector4 box, float yaw)>();
            bool Overlaps(Vector4 a, Vector4 b, float gap) => a.x < b.z + gap && b.x < a.z + gap && a.y < b.w + gap && b.y < a.w + gap;
            for (int pass = 0; pass < 2 && homes.Count < want; pass++)
                for (int bi = 1; bi < walks.Count && homes.Count < want; bi++)
                    foreach (var seaward in new[] { true, false })
                    {
                        var br = walks[bi];
                        var w = pass == 0 ? 4.6f + R() * 1.2f : 4.4f;
                        var d = pass == 0 ? 4.2f + R() * 1.0f : 4.2f;
                        var zb = (br.y + br.w) * 0.5f;
                        var cz = seaward ? zb + half + 0.2f + d * 0.5f : zb - half - 0.2f - d * 0.5f;
                        if (!seaward && cz - d * 0.5f < 4f)
                            continue;   // keep shoreward houses out over the water
                        var span = br.z - br.x - w;
                        if (span < 0f)
                            continue;
                        for (int k = 0; k < 6 && homes.Count < want; k++)
                        {
                            var cx = br.x + w * 0.5f + span * (k == 0 ? R() : k / 5f);
                            var box = new Vector4(cx - w * 0.5f, cz - d * 0.5f, cx + w * 0.5f, cz + d * 0.5f);
                            var clear = true;
                            foreach (var wk in walks)
                                clear &= !Overlaps(box, wk, 0.1f);
                            foreach (var h in homes)
                                clear &= !Overlaps(box, h.box, 0.8f);
                            if (!clear)
                                continue;
                            homes.Add((box, seaward ? 180f : 0f));
                            houses.Add(new Vector4(cx, cz, w, d));
                            break;
                        }
                    }

            // W-104 (#36): the network joins back on itself. When the mid branch and the end branch both reach out on
            // the -X side, a walkway runs between their tips, parallel to the main one, so the planks close a loop
            // round a patch of open water. It runs as near the shorter branch's tip as it can, clear of the houses;
            // it draws from its own stream, so the walkways and houses above are as before.
            var branchCount = walks.Count;
            var extra = new System.Random((int)(Scramble(seed ^ 0x100Bu) & 0x7FFFFFFF));
            if (branches >= 2 && extra.NextDouble() < 0.8)
            {
                // From the shorter branch's tip inwards in 0.5 m steps, the first place clear of every house.
                for (var x = Mathf.Max(walks[1].x, walks[2].x); x + W < -half - 1.2f; x += 0.5f)
                {
                    var loop = new Vector4(x, mid + half, x + W, length - half);
                    var fits = loop.w - loop.y > 3f;
                    foreach (var hm in homes)
                        fits &= !Overlaps(loop, hm.box, 0.3f);
                    if (!fits)
                        continue;
                    walks.Add(loop);
                    break;
                }
            }

            // Decks: planks, a solid bed under them, piles down into the water.
            foreach (var wk in walks)
            {
                var alongZ = wk.w - wk.y > wk.z - wk.x;
                var c = new Vector3((wk.x + wk.z) * 0.5f, 0f, (wk.y + wk.w) * 0.5f);
                var len = alongZ ? wk.w - wk.y : wk.z - wk.x;
                var yaw = alongZ ? 0f : 90f;
                Platform(parts, c, yaw, W, len, deck, false);
                Add(parts, KitPiece.Platform, KitMaterial.Wood, c + Vector3.up * (deck - 0.17f), new Vector3(W * 0.94f, 0.1f, len), yaw, false);
                for (int k = 0, piles = Mathf.Max(2, Mathf.RoundToInt(len / 2.4f) + 1); k < piles; k++)
                    foreach (var side in new[] { -1f, 1f })
                        Add(parts, KitPiece.Column, KitMaterial.Wood, c + Turn(yaw, new Vector3(side * W * 0.45f, deck - 3.2f, -len * 0.5f + k * len / (piles - 1))), new Vector3(0.26f, 6f, 0.26f), yaw, false);
            }
            const float rampDrop = 0.4f, rampRun = 6f;
            var rise = deck + rampDrop;
            // Shore steps rather than a pitched ramp: flat treads 0.2 m apart read as solid support on a low bank too
            // (a pitched ramp's support sampled in steps over 0.4 m where the bank fell away beside it).
            // W-65d: 10 treads (0.14 m each) over 6 m, so a walker still on the bank beside the low treads meets each
            // next tread well within the 0.4 m step-up (with 7 the gap to the bank outgrew it near the deck end).
            // W-65f (#36): each tread is solid (a collider up to its top), so a walker on the low bank beside or below the
            // shore end is stopped by any tread more than a step up and only gets onto the steps where one is within a
            // step; it can no longer walk in under the higher treads (the player stopped under them at -3285.9,-1644.0).
            const int treads = 10;
            for (int k = 0; k < treads; k++)
            {
                var top = deck - rise * (k + 1) / treads;
                var run = rampRun / treads;
                Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, (top - 0.6f) * 0.5f, -run * (k + 0.5f)), new Vector3(W * 0.8f, top + 0.6f, run + 0.02f), 0f, true);
                Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, top + 0.02f, -run * k - 0.04f), new Vector3(W * 0.82f, 0.05f, 0.08f), 0f, false);
            }

            // The houses: one storey of House turned to face its walkway, on a plank floor and piles.
            for (int i = 0; i < homes.Count; i++)
            {
                var (box, yaw) = homes[i];
                var h = houses[i];
                var at = new Vector3(h.x, deck, h.y);
                var turn = Quaternion.Euler(0f, yaw, 0f);
                foreach (var p in House(Scramble(seed + 977u * (uint)(i + 1)), t, h.z, h.w, 1, 0f))
                {
                    var q = p;
                    q.Centre = at + turn * p.Centre;
                    q.Rotation = turn * p.Rotation;
                    parts.Add(q);
                }
                Platform(parts, new Vector3(h.x, 0f, h.y), yaw, h.z + 0.6f, h.w + 0.4f, deck, false);
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                        Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(h.x + sx * h.z * 0.45f, deck - 3.3f, h.y + sz * h.w * 0.45f), new Vector3(0.3f, 6f, 0.3f), 0f, false);
                // A lamp by the door, lit at dusk, on a bracket out of the wall (W-100: beside the door wherever House put
                // it, and hung from something rather than floating in front of the wall).
                var side = rng.Next(2) == 0 ? -1f : 1f;
                var lampX = DoorX(t, h.z) + side * 1.05f;
                if (Mathf.Abs(lampX) > h.z * 0.5f - 0.3f)
                    lampX = DoorX(t, h.z) - side * 1.05f;
                Add(parts, KitPiece.Trim, KitMaterial.Wood, at + turn * new Vector3(lampX, 2.3f, h.w * 0.5f + 0.12f), new Vector3(0.06f, 0.06f, 0.3f), yaw, false);
                Add(parts, KitPiece.Trim, KitMaterial.Wood, at + turn * new Vector3(lampX, 2.2f, h.w * 0.5f + 0.27f), new Vector3(0.03f, 0.2f, 0.03f), yaw, false);
                Add(parts, KitPiece.Window, KitMaterial.Lamp, at + turn * new Vector3(lampX, 1.95f, h.w * 0.5f + 0.27f), new Vector3(0.24f, 0.32f, 0.24f), yaw, false);
            }

            // Rails: walked in 0.5 m steps round every walkway edge; a step gets rail unless it opens onto another
            // walkway (a join), a house front, the ramp or the ladder gap.
            var ladder = -1;
            var runs = new List<(Vector3 a, Vector3 b, float yaw)>();
            foreach (var wk in walks)
            {
                var edges = new[]
                {
                    (new Vector2(wk.x, wk.y), new Vector2(wk.z, wk.y), new Vector2(0f, -1f)),
                    (new Vector2(wk.x, wk.w), new Vector2(wk.z, wk.w), new Vector2(0f, 1f)),
                    (new Vector2(wk.x, wk.y), new Vector2(wk.x, wk.w), new Vector2(-1f, 0f)),
                    (new Vector2(wk.z, wk.y), new Vector2(wk.z, wk.w), new Vector2(1f, 0f)),
                };
                foreach (var (e0, e1, n) in edges)
                {
                    var steps = Mathf.Max(1, Mathf.RoundToInt((e1 - e0).magnitude / 0.5f));
                    var start = -1;
                    for (int k = 0; k <= steps; k++)
                    {
                        var open = k == steps;
                        if (!open)
                        {
                            var p = Vector2.Lerp(e0, e1, (k + 0.5f) / steps) + n * 0.1f;
                            if (p.y < 0.05f)
                                open = true;    // the land end: the ramp comes up here
                            foreach (var o in walks)
                                open |= p.x > o.x && p.x < o.z && p.y > o.y && p.y < o.w;
                            foreach (var hm in homes)
                                open |= p.x > hm.box.x - 0.5f && p.x < hm.box.z + 0.5f && p.y > hm.box.y - 0.5f && p.y < hm.box.w + 0.5f;
                        }
                        if (!open && start < 0)
                            start = k;
                        if (open && start >= 0)
                        {
                            var a = Vector2.Lerp(e0, e1, (float)start / steps) - n * 0.12f;
                            var b = Vector2.Lerp(e0, e1, (float)k / steps) - n * 0.12f;
                            runs.Add((new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y), n.y != 0f ? 90f : 0f));
                            start = -1;
                        }
                    }
                }
            }
            // The ladder takes the middle of the longest seaward-facing run, so it goes down to deep water.
            for (int i = 0; i < runs.Count; i++)
                if ((runs[i].b - runs[i].a).magnitude > 3f && (ladder < 0 || runs[i].a.z > runs[ladder].a.z))
                    ladder = i;
            for (int i = 0; i < runs.Count; i++)
            {
                var (a, b, yaw) = runs[i];
                var pieces = new List<(Vector3, Vector3)>();
                if (i == ladder)
                {
                    var m = (a + b) * 0.5f;
                    var dir = (b - a).normalized;
                    pieces.Add((a, m - dir * 0.5f));
                    pieces.Add((m + dir * 0.5f, b));
                    // The far edge faces +Z; a side edge faces away from the main walkway.
                    Vector3 outward;
                    if (Mathf.Abs(dir.x) > 0.5f)
                        outward = new Vector3(0f, 0f, m.z >= length ? 1f : -1f);
                    else
                        outward = new Vector3(Mathf.Sign(m.x == 0f ? 1f : m.x), 0f, 0f);
                    // W-147: the kit's rope ladder (grade 1), from under the water up to the deck.
                    var foot = m + outward * 0.2f;
                    Ladder(parts, foot + Vector3.up * (-DockWater - LadderWet), foot + Vector3.up * deck, outward, true);
                }
                else
                    pieces.Add((a, b));
                foreach (var (p0, p1) in pieces)
                {
                    var len = (p1 - p0).magnitude;
                    if (len < 0.3f)
                        continue;
                    var c = (p0 + p1) * 0.5f;
                    for (int k = 0, posts = Mathf.Max(2, Mathf.RoundToInt(len / 2f) + 1); k < posts; k++)
                        Add(parts, KitPiece.Rail, KitMaterial.Wood, Vector3.Lerp(p0, p1, (float)k / (posts - 1)) + Vector3.up * (deck + 0.5f), new Vector3(0.12f, 1f, 0.12f), yaw, false);
                    Add(parts, KitPiece.Rail, KitMaterial.Wood, c + Vector3.up * (deck + 0.95f), new Vector3(0.1f, 0.1f, len), yaw, false);
                    // Now and then a net hung over the rail to dry.
                    if (len > 2.5f && R() < 0.35f)
                        Add(parts, KitPiece.Trim, KitMaterial.Net, c + Vector3.up * (deck + 0.62f), new Vector3(0.04f, 0.7f, Mathf.Min(len - 0.6f, 1.4f + R() * 1.4f)), yaw, false, 0f, (R() - 0.5f) * 8f);
                }
            }

            // Lantern posts at each join with the main walkway, on the corner away from the houses.
            for (int bi = 1; bi < branchCount; bi++)
            {
                var br = walks[bi];
                var zb = (br.y + br.w) * 0.5f;
                var sx = br.x < -half - 0.1f ? -1f : 1f;
                var corner = new Vector3(sx * (half + 0.3f), deck, bi == 1 ? zb + half - 0.3f : zb - half + 0.3f);
                Detail(parts, CoastDetail.Lantern, corner, sx < 0f ? 180f : 0f, rng, t);
                // and one at the branch's far tip
                var tip = new Vector3(sx < 0f ? br.x + 0.3f : br.z - 0.3f, deck, zb + (R() < 0.5f ? -1f : 1f) * (half - 0.3f));
                if (R() < 0.6f)
                    Detail(parts, CoastDetail.Lantern, tip, sx < 0f ? 0f : 180f, rng, t);
            }

            // Skiffs tied along the main walkway's +X edge towards its far end, riding at the water, a bollard on the deck by
            // each. W-128: they used to tie off the branches, whose outer ends can reach the bank; the far end of the main
            // walk always stands in water (the shore fit puts its base on the waterline).
            var lastTied = 0f;
            for (int ti = 0, tied = 0; ti < 2; ti++)
            {
                if (R() < 0.3f)
                    continue;
                var yaw = R() < 0.5f ? 0f : 180f;
                var z = Mathf.Lerp(length * 0.5f, length - half - 3.3f, R());
                var boat = new Vector3(half + 1.1f, -DockWater, z);
                var free = tied == 0 || Mathf.Abs(z - lastTied) > 6f;
                for (int bi = 1; bi < walks.Count; bi++)
                    free &= !(walks[bi].z > half && z + 3.3f > walks[bi].y && z - 3.3f < walks[bi].w);
                foreach (var hm in homes)
                    free &= !(boat.x > hm.box.x - 2f && boat.x < hm.box.z + 2f && boat.z > hm.box.y - 3.3f && boat.z < hm.box.w + 3.3f);
                if (!free)
                    continue;
                Detail(parts, CoastDetail.Boat, boat, yaw, rng, t, true);
                Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(half - 0.25f, deck + 0.2f, z), new Vector3(0.22f, 0.4f, 0.22f), 0f, false);
                lastTied = z;
                tied++;
            }
            // Crab pots stacked at the far end of the main walkway's left edge, clear of the walk lane.
            if (R() < 0.7f)
                Detail(parts, CoastDetail.CrabPots, new Vector3(-half + 0.42f, deck, 2f + R() * Mathf.Max(0.5f, (branches >= 2 ? mid : length) - half - 4.5f)), 270f, rng, t);

            // W-104 (#36): crates stacked by the main walkway's right edge near the land end (one to three, the odd one
            // turned or set on top), and two to four buoys riding the water off the far end, each a float with a
            // little mast, on their own stream.
            for (int k = 0, n = 1 + extra.Next(3); k < n; k++)
            {
                var s = 0.5f + (float)extra.NextDouble() * 0.15f;
                var top = k == 2;
                var at = new Vector3(half - 0.4f, deck + (top ? s + s * 0.5f : s * 0.5f), 3f + (top ? 0.3f : k * 0.75f));
                Add(parts, KitPiece.Trim, KitMaterial.Wood, at, new Vector3(s, s, s), (float)(extra.NextDouble() - 0.5) * (top ? 40f : 12f), !top);
                Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Vector3.up * (s * 0.5f + 0.01f), new Vector3(s * 0.9f, 0.03f, 0.1f), 0f, false);
            }
            for (int k = 0, n = 2 + extra.Next(3); k < n; k++)
            {
                var ang = ((float)extra.NextDouble() - 0.5f) * 140f * Mathf.Deg2Rad;
                var dist = 5f + (float)extra.NextDouble() * 7f;
                var at = new Vector3(Mathf.Sin(ang) * dist, -0.15f, length + half + Mathf.Cos(ang) * dist);
                // W-108: clear of the other props' footprints. A buoy could ride under a house's piles seaward of the
                // end branch, or drift over to the second dock 12 m off on +X. It keeps its draws (the stream and the
                // other buoys stay put): it moves in from the dock's side, then out to sea until it is 1 m clear of
                // every walkway and house.
                at.x = Mathf.Min(at.x, 7f);
                for (var step = 0; step < 40; step++)
                {
                    var spot = new Vector4(at.x - 0.3f, at.z - 0.3f, at.x + 0.3f, at.z + 0.3f);
                    var clear = true;
                    foreach (var wk in walks)
                        clear &= !Overlaps(spot, wk, 1f);
                    foreach (var hm in homes)
                        clear &= !Overlaps(spot, hm.box, 1f);
                    if (clear)
                        break;
                    at.z += 0.5f;
                }
                var tilt = ((float)extra.NextDouble() - 0.5f) * 14f;
                Add(parts, KitPiece.Trim, KitMaterial.Roof, at, new Vector3(0.55f, 0.5f, 0.55f), (float)extra.NextDouble() * 90f, false, tilt);
                Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Vector3.up * 0.3f, new Vector3(0.6f, 0.1f, 0.6f), 0f, false, tilt);
                // W-106 (A-73): the mast is a Column, not Trim, so it lands in the model's Boxes and gulls can perch on it.
                Add(parts, KitPiece.Column, KitMaterial.Wood, at + Vector3.up * 0.7f, new Vector3(0.06f, 0.8f, 0.06f), 0f, false, tilt);
            }
            return parts;
        }

        /// <summary>A cluster of two to four shore details round the origin (#36), each picked from CoastDetails by the seed.</summary>
        public static List<KitPart> Shore(uint seed, CultureTrim t, float spread, int trade = -1)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x5408Eu) & 0x7FFFFFFF));
            for (int k = 0, n = 2 + rng.Next(3); k < n; k++)
            {
                var a = (k + (float)rng.NextDouble() * 0.6f) * Mathf.PI * 2f / n;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (k == 0 ? 0f : spread * (0.6f + 0.4f * (float)rng.NextDouble()));
                Detail(parts, PickDetail(rng, trade), at, (float)rng.NextDouble() * 360f, rng, t);
            }
            return parts;
        }

        /// <summary>
        /// One coast detail standing at <paramref name="at"/> (its base), turned by <paramref name="yaw"/> degrees. A
        /// <paramref name="moored"/> boat floats: <paramref name="at"/> is the water surface, its hull sits BoatDraft deep
        /// and it is never upturned.
        /// </summary>
        public static void Detail(List<KitPart> parts, CoastDetail kind, Vector3 at, float yaw, System.Random rng, CultureTrim t, bool moored = false)
        {
            float R() => (float)rng.NextDouble();
            switch (kind)
            {
                case CoastDetail.Boat:
                {
                    // A rowing boat: a flat bottom, sides flaring out that close in from the waist to a stem post, a
                    // transom, thwarts; now and then upturned on the shore. W-128: the planks meet (the old keel stopped
                    // 0.27 m short of the sides and the bow was open, so the hull showed gaps), and a moored boat floats.
                    var boatFirst = parts.Count;
                    var len = 3.6f + R() * 1.6f;
                    var flip = R() < 0.3f && !moored;   // (drawn either way, so the draws after it stay the same)
                    if (moored)
                        at.y -= BoatDraft;
                    // In the boat's frame, y up from the underside, bow to +Z: (material, centre, size, yaw, roll).
                    var hull = new List<(KitMaterial m, Vector3 c, Vector3 s, float yaw, float roll)>();
                    const float bottomHalf = 0.4f, sideH = 0.5f, flare = 18f, plank = 0.07f, foreFlare = flare * 0.6f;
                    var fl = Mathf.Sin(flare * Mathf.Deg2Rad) * sideH * 0.5f;
                    var sideX = bottomHalf - 0.02f + fl;   // side centre, its lower edge just inside the bottom's
                    var sideY = 0.05f + Mathf.Cos(flare * Mathf.Deg2Rad) * sideH * 0.5f;
                    float aft = -len * 0.4f, waist = len * 0.2f, bow = len * 0.48f;
                    hull.Add((KitMaterial.Wood, new Vector3(0f, 0.05f, (aft + waist) * 0.5f), new Vector3(bottomHalf * 2f, 0.1f, waist - aft + 0.02f), 0f, 0f));
                    // The fore bottom in strips, each as wide as the closing sides at its middle.
                    var foreLow = sideX - Mathf.Sin(foreFlare * Mathf.Deg2Rad) * sideH * 0.5f;   // the fore side's lower edge at the waist
                    const int strips = 6;
                    for (int k = 0; k < strips; k++)
                    {
                        var u = (k + 0.5f) / strips;
                        var half = Mathf.Lerp(foreLow, 0.02f, u) + 0.02f;
                        hull.Add((KitMaterial.Wood, new Vector3(0f, 0.05f, Mathf.Lerp(waist, bow, u)), new Vector3(half * 2f, 0.1f, (bow - waist) / strips + 0.01f), 0f, 0f));
                    }
                    foreach (var side in new[] { -1f, 1f })
                    {
                        hull.Add((KitMaterial.Trim, new Vector3(side * sideX, sideY, (aft + waist) * 0.5f), new Vector3(plank, sideH, waist - aft + 0.04f), 0f, -side * flare));
                        // the fore side, turned in from the waist to the stem post
                        float dx = sideX - 0.06f, dz = bow - waist;
                        var turn = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                        hull.Add((KitMaterial.Trim, new Vector3(side * (sideX + 0.06f) * 0.5f, sideY, (waist + bow) * 0.5f), new Vector3(plank, sideH, Mathf.Sqrt(dx * dx + dz * dz) + 0.06f), -side * turn, -side * foreFlare));
                    }
                    hull.Add((KitMaterial.Wood, new Vector3(0f, sideY, aft), new Vector3(sideX * 2f + 0.08f, sideH, 0.08f), 0f, 0f));          // transom
                    hull.Add((KitMaterial.Wood, new Vector3(0f, sideY + 0.06f, bow), new Vector3(0.16f, sideH + 0.12f, 0.2f), 0f, 0f));      // stem post
                    if (!flip)
                    {
                        hull.Add((KitMaterial.Wood, new Vector3(0f, 0.36f, 0.1f), new Vector3((sideX + fl * 0.4f) * 2f, 0.06f, 0.25f), 0f, 0f));   // thwarts
                        hull.Add((KitMaterial.Wood, new Vector3(0f, 0.36f, aft + 0.45f), new Vector3((sideX + fl * 0.4f) * 2f, 0.06f, 0.22f), 0f, 0f));
                    }
                    var height = sideY + sideH * 0.5f + 0.06f;
                    foreach (var (m, c, sz, py, pr) in hull)
                    {
                        var cc = c;
                        var roll = pr;
                        if (flip)   // upturned: mirrored top to bottom, resting on its gunwales
                        {
                            cc.y = height - c.y;
                            roll = -pr;
                        }
                        Add(parts, KitPiece.Trim, m, at + Turn(yaw, cc), sz, yaw + py, false, 0f, roll);
                    }
                    if (!flip)   // an oar across the thwarts
                        Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, new Vector3(0.3f, 0.42f, -0.4f)), new Vector3(0.06f, 0.06f, 2.4f), yaw, false, 8f);
                    // W-100: boats are exempt from the foundation skirt; on a beach it showed the hull stretched into the sand.
                    for (int b = boatFirst; b < parts.Count; b++)
                    {
                        var bp = parts[b];
                        bp.Afloat = true;
                        bp.Keel = b == boatFirst && moored;
                        parts[b] = bp;
                    }
                    break;
                }
                case CoastDetail.NetFrame:
                {
                    // Two poles and a bar with a net hung to dry, sagging in a few panels.
                    var w = 2.2f + R() * 1.4f;
                    foreach (var side in new[] { -0.5f, 0.5f })
                        Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * w, 0.9f, 0f)), new Vector3(0.12f, 1.8f, 0.12f), yaw, false);
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, 1.78f, 0f)), new Vector3(w + 0.2f, 0.1f, 0.1f), yaw, false);
                    for (int k = 0, panels = 3; k < panels; k++)
                    {
                        var drop = 0.9f + R() * 0.5f;
                        Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3((k - 1) * w / panels, 1.75f - drop * 0.5f, 0.03f)), new Vector3(w / panels * 0.96f, drop, 0.03f), yaw, false, 0f, (R() - 0.5f) * 6f);
                    }
                    break;
                }
                case CoastDetail.FishRack:
                {
                    // A drying rack: an A-frame each end, three rails, fish hung in rows.
                    var w = 2f + R();
                    foreach (var side in new[] { -0.5f, 0.5f })
                        foreach (var lean in new[] { -12f, 12f })
                            Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * w, 0.75f, 0f)), new Vector3(0.1f, 1.6f, 0.1f), yaw, false, lean);
                    for (int r = 0; r < 2; r++)
                    {
                        var h = 0.9f + r * 0.5f;
                        Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, h, 0f)), new Vector3(w + 0.2f, 0.06f, 0.06f), yaw, false);
                        for (int f = 0, fish = 4 + rng.Next(4); f < fish; f++)
                            Add(parts, KitPiece.Trim, KitMaterial.Stone, at + Turn(yaw, new Vector3((f + 0.5f) / fish * w - w * 0.5f, h - 0.22f, 0f)), new Vector3(0.1f, 0.38f, 0.04f), yaw, false, 0f, (R() - 0.5f) * 10f);
                    }
                    break;
                }
                case CoastDetail.CrabPots:
                    // A stack of slatted pots, a buoy or two on top.
                    for (int k = 0, pots = 2 + rng.Next(4); k < pots; k++)
                    {
                        var p = new Vector3((k % 2 - 0.5f) * 0.7f, 0.25f + (k / 2) * 0.48f, (R() - 0.5f) * 0.2f);
                        // W-119: a cage, not a crate: a slatted base and lid, four corner sticks, the net body set in.
                        var py = yaw + (R() - 0.5f) * 20f;
                        foreach (var y in new[] { -0.2f, 0.2f })
                            Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, p + new Vector3(0f, y, 0f)), new Vector3(0.62f, 0.05f, 0.62f), py, false);
                        foreach (var c in new[] { new Vector3(-0.28f, 0f, -0.28f), new Vector3(0.28f, 0f, -0.28f), new Vector3(-0.28f, 0f, 0.28f), new Vector3(0.28f, 0f, 0.28f) })
                            Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, p) + Quaternion.Euler(0f, py, 0f) * c, new Vector3(0.05f, 0.4f, 0.05f), py, false);
                        Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, p), new Vector3(0.5f, 0.36f, 0.5f), py, false);
                    }
                    Add(parts, KitPiece.Trim, KitMaterial.Roof, at + Turn(yaw, new Vector3(0.6f, 0.15f, 0.5f)), new Vector3(0.3f, 0.3f, 0.3f), yaw, false);
                    break;
                case CoastDetail.Driftwood:
                    // Bleached logs and a forked branch washed up and lying askew.
                    for (int k = 0, logs = 1 + rng.Next(3); k < logs; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(yaw, new Vector3((R() - 0.5f) * 1.5f, 0.12f + k * 0.08f, (R() - 0.5f) * 1.5f)),
                            new Vector3(0.22f + R() * 0.15f, 0.2f, 1.6f + R() * 2.2f), yaw + R() * 90f, false, 0f, (R() - 0.5f) * 8f);
                    break;
                case CoastDetail.Lantern:
                    // A post with an arm and a hanging lantern that lights at dusk.
                    Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, 1.1f, 0f)), new Vector3(0.14f, 2.2f, 0.14f), yaw, false);
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(0.3f, 2.12f, 0f)), new Vector3(0.6f, 0.08f, 0.08f), yaw, false);
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, at + Turn(yaw, new Vector3(0.52f, 1.86f, 0f)), new Vector3(0.24f, 0.32f, 0.24f), yaw, false);
                    break;
                case CoastDetail.RopeCoil:
                {
                    // W-119: rope coiled flat in two or three turns (three squares a turn, turned 30 degrees apart, read
                    // as a round coil), its loose end trailing off.
                    var r = 0.5f + R() * 0.2f;
                    for (int k = 0, turns = 2 + rng.Next(2); k < turns; k++)
                        for (int s = 0; s < 3; s++)
                            Add(parts, KitPiece.Trim, KitMaterial.Net, at + new Vector3(0f, 0.03f + k * 0.055f, 0f), new Vector3(r - k * 0.08f, 0.05f, r - k * 0.08f), yaw + s * 30f + k * 15f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3(r * 0.5f + 0.35f, 0.03f, 0.1f)), new Vector3(0.7f, 0.04f, 0.05f), yaw + 20f, false);
                    break;
                }
                case CoastDetail.GullPost:
                {
                    // W-119: one to three old weathered posts leaning out of the sand, a gull standing on the tallest.
                    var posts = 1 + rng.Next(3);
                    var top = Vector3.zero;
                    for (int k = 0; k < posts; k++)
                    {
                        var h = (k == 0 ? 1.5f : 0.6f + R() * 0.7f);
                        var p = at + Turn(yaw, new Vector3(k * 0.7f, h * 0.5f - 0.3f, (R() - 0.5f) * 0.4f));
                        Add(parts, KitPiece.Column, KitMaterial.Trim, p, new Vector3(0.24f, h + 0.6f, 0.24f), yaw + R() * 40f, false, (R() - 0.5f) * 8f);
                        if (k == 0)
                            top = p + new Vector3(0f, (h + 0.6f) * 0.5f, 0f);
                    }
                    Gull(parts, top, yaw + R() * 360f);
                    break;
                }
            }
        }

        /// <summary>
        /// W-119: a net hung from a rail along Z at (<paramref name="x"/>, <paramref name="top"/>): a grid of thin cords
        /// (three falls, three rows narrowing as the net bunches) swung <paramref name="tilt"/> degrees out towards
        /// <paramref name="side"/>, so it reads as mesh rather than a board. Returns the middle of its bottom edge.
        /// </summary>
        static Vector3 NetHang(List<KitPart> parts, float x, float top, float z, float width, float fall, float side, float tilt)
        {
            float sin = Mathf.Sin(tilt * Mathf.Deg2Rad), cos = Mathf.Cos(tilt * Mathf.Deg2Rad);
            Vector3 At(float depth, float dz) => new Vector3(x + side * depth * sin, top - depth * cos, z + dz);
            for (int i = 0; i < 3; i++)
                Add(parts, KitPiece.Trim, KitMaterial.Net, At(fall * 0.5f, (i * 0.5f - 0.5f) * width * 0.9f), new Vector3(0.025f, fall, 0.025f), 0f, false, 0f, side * tilt);
            for (int r = 0; r < 3; r++)
                Add(parts, KitPiece.Trim, KitMaterial.Net, At(fall * (0.32f + r * 0.32f), 0f), new Vector3(0.025f, 0.025f, width * (1f - r * 0.12f)), 0f, false, 0f, side * tilt);
            return At(fall, 0f);
        }

        /// <summary>
        /// W-119: a gull standing at <paramref name="at"/> (its feet), facing <paramref name="yaw"/>: a white body and
        /// head, grey wings folded along its back, a dark tail, an orange bill and legs. A carved-still bird for posts
        /// and decks; the live gulls (WharfSpots) keep to the wharves.
        /// </summary>
        public static void Gull(List<KitPart> parts, Vector3 at, float yaw)
        {
            Add(parts, KitPiece.Trim, KitMaterial.Copper, at + Turn(yaw, new Vector3(0f, 0.05f, 0f)), new Vector3(0.1f, 0.1f, 0.03f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Wall, at + Turn(yaw, new Vector3(0f, 0.17f, 0f)), new Vector3(0.16f, 0.15f, 0.34f), yaw, false, -8f);
            Add(parts, KitPiece.Trim, KitMaterial.Wall, at + Turn(yaw, new Vector3(0f, 0.3f, 0.16f)), new Vector3(0.12f, 0.12f, 0.13f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Copper, at + Turn(yaw, new Vector3(0f, 0.29f, 0.26f)), new Vector3(0.035f, 0.035f, 0.09f), yaw, false);
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Trim, KitMaterial.Ash, at + Turn(yaw, new Vector3(side * 0.085f, 0.2f, -0.04f)), new Vector3(0.04f, 0.09f, 0.32f), yaw, false, -6f);
            Add(parts, KitPiece.Trim, KitMaterial.Ash, at + Turn(yaw, new Vector3(0f, 0.19f, -0.23f)), new Vector3(0.12f, 0.03f, 0.12f), yaw, false, -12f);
        }

        // ------------------------------------------------------------------------------------- mountaintops (#38)

        /// <summary>Which mountaintop details turn up and how often (data: add a kind here and a case in Alpine).</summary>
        public static readonly (AlpineDetail Kind, float Weight)[] AlpineDetails =
        {
            (AlpineDetail.PrayerFlags, 3f), (AlpineDetail.SnowPot, 2f), (AlpineDetail.RuneStone, 2f),
            (AlpineDetail.Cairn, 2.5f), (AlpineDetail.IceLantern, 1.5f), (AlpineDetail.Sled, 1f)
        };

        /// <summary>A seeded pick from AlpineDetails by weight.</summary>
        public static AlpineDetail PickAlpine(System.Random rng)
        {
            var total = 0f;
            foreach (var d in AlpineDetails) total += d.Weight;
            var roll = (float)rng.NextDouble() * total;
            foreach (var d in AlpineDetails)
                if ((roll -= d.Weight) < 0f)
                    return d.Kind;
            return AlpineDetails[0].Kind;
        }

        /// <summary>
        /// A turtle folk igloo (#38) centred on the origin, door at +Z: a dome of snow blocks laid in rings that lean in
        /// as they rise (the bottom ring is solid), a tall tunnel porch (3.4 m clear, for a tall race) ending in a closed
        /// door, ice panes and a rune band that glow faintly, and by the seed: a shell-ribbed crown over the porch or a
        /// plain arch, a smaller side dome, a snow drift, a vent cap, and a detail or two beside the door.
        /// </summary>
        public static List<KitPart> Igloo(uint seed, CultureTrim t, float diameter)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x1600u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var radius = diameter * 0.5f;
            // W-64 (#38): a taller dome (0.95-1.15 of its radius, was 0.82-1.02) on a bigger species (10.5-12 m, was 6-8.5),
            // so the tall door sits inside the dome wall with snow blocks round it and over it, not a door bigger than its igloo.
            var squash = 0.95f + R() * 0.2f;                 // how tall the dome stands against its radius
            const float doorW = 1.7f, doorH = 3.4f;
            Dome(parts, Vector3.zero, radius, squash, true, rng, doorW * 0.5f + 0.7f, doorH + 0.15f);

            // The tunnel porch at +Z: two block walls, a vaulted roof of three slabs, and the door at its inner end. The
            // door stands where the dome's inner face is 1 m above the door's head; the tunnel runs out from there through
            // the gap in the blocks to 1-1.8 m past the dome's foot.
            var lintel = Mathf.Clamp01((doorH + 1f) / (radius * squash));
            var z0 = Mathf.Max(radius * Mathf.Sqrt(1f - lintel * lintel) - 0.5f, 1.5f);   // where the door stands
            var tunnel = radius - z0 + 1f + R() * 0.8f;
            var zMid = z0 + tunnel * 0.5f;
            // W-65c (#38): the tunnel roof sits `lift` above the door, so a 1.8 m walker clears it even where the seated
            // igloo's slope leaves the tunnel floor above the dome's (walls, crown and arch rise with it; a lintel of
            // snow fills the gap over the door).
            const float lift = 0.6f;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Ice, new Vector3(side * (doorW * 0.5f + 0.35f), (doorH + lift) * 0.5f, zMid), new Vector3(0.7f, doorH + lift, tunnel), 0f, false);
            Add(parts, KitPiece.Roof, KitMaterial.Ice, new Vector3(0f, doorH + lift + 0.3f, zMid), new Vector3(doorW + 0.5f, 0.6f, tunnel), 0f, false);
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Roof, KitMaterial.Ice, new Vector3(side * (doorW * 0.5f + 0.25f), doorH + lift + 0.05f, zMid), new Vector3(0.9f, 0.6f, tunnel + 0.02f), 0f, false, 0f, side * -35f);
            Add(parts, KitPiece.Wall, KitMaterial.Ice, new Vector3(0f, doorH + lift * 0.5f, z0 + 0.1f), new Vector3(doorW + 0.4f, lift + 0.05f, 0.3f), 0f, false);
            Add(parts, KitPiece.Door, KitMaterial.Wood, new Vector3(0f, doorH * 0.5f, z0 + 0.1f), new Vector3(doorW, doorH, 0.14f), 0f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Rune, new Vector3(0f, doorH * 0.62f, z0 + 0.19f), new Vector3(0.16f, 0.5f, 0.04f), 0f, false);
            if (R() < 0.55f)
            {
                // A shell-shaped crown: ribs fanning out over the porch mouth, like a scallop.
                for (int k = 0, ribs = 7; k < ribs; k++)
                {
                    var a = -60f + k * 120f / (ribs - 1);
                    var dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, doorH + lift + 0.15f, z0 + tunnel + 0.05f) + dir * 0.55f, new Vector3(0.14f, 1.1f, 0.12f), 0f, false, 0f, a);
                }
            }
            else
                Add(parts, KitPiece.Arch, KitMaterial.Trim, new Vector3(0f, doorH + lift + 0.1f, z0 + tunnel + 0.04f), new Vector3(doorW + 1.2f, 0.32f, 0.2f), 0f, false);
            // A path of packed snow out through the porch (walked on, not solid).
            Add(parts, KitPiece.Floor, KitMaterial.Ice, new Vector3(0f, 0.05f, z0 + tunnel * 0.5f + 0.4f), new Vector3(doorW + 0.3f, 0.1f, tunnel + 0.8f), 0f, false);
            // (W-61's buried steps out of the mouth are gone: being under y = 0, the foundation skirt sank them 1.6 m
            // out of reach. W-61b seats the igloo instead: FloraPlacement.PorchSeat.)

            // Ice panes round the dome and a faint rune band low on the wall: the inner glow showing through.
            for (int k = 0, panes = 2 + rng.Next(3); k < panes; k++)
            {
                var a = 55f + k * 250f / panes + (R() - 0.5f) * 20f;  // round the back and sides, never on the door
                var at = Turn(a, new Vector3(0f, radius * squash * 0.45f, radius * 0.93f));
                Add(parts, KitPiece.Window, KitMaterial.Rune, at, new Vector3(0.7f, 0.9f, 0.12f), a, false, -24f);
            }
            if (R() < 0.6f)
                for (int k = 0; k < 16; k++)
                {
                    var a = k * 22.5f;
                    if (Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < 25f) continue;
                    Add(parts, KitPiece.Trim, KitMaterial.Rune, Turn(a, new Vector3(0f, 0.55f, radius * 1.005f)), new Vector3(0.5f, 0.07f, 0.04f), a, false);
                }
            // A smaller side dome (a store or a sleeping room), a snow drift on the windward side, a vent cap on top.
            if (R() < 0.45f)
            {
                // W-64: tucked round the back (was out to the side, 1.55 radii off), so neighbours on the 15 m ring keep apart.
                var side = R() < 0.5f ? -1f : 1f;
                Dome(parts, new Vector3(side * radius * 0.62f, 0f, -radius * 0.72f), radius * 0.42f, 0.8f, false, rng);
            }
            var drift = R() * 360f;
            if (Mathf.Abs(Mathf.DeltaAngle(drift, 0f)) > 50f)
                Add(parts, KitPiece.Trim, KitMaterial.Wall, Turn(drift, new Vector3(0f, 0.25f, radius * 1.05f)), new Vector3(radius * 1.1f, 0.7f, 1.4f), drift, false, -18f);
            Add(parts, KitPiece.Chimney, KitMaterial.Trim, new Vector3(0f, radius * squash + 0.15f, 0f), new Vector3(0.7f, 0.35f, 0.7f), R() * 90f, false);
            for (int k = 0, n = 1 + rng.Next(2); k < n; k++)
            {
                var side = k == 0 ? (rng.Next(2) == 0 ? -1f : 1f) : -1f;
                var dx = side * (doorW + 1.8f + R());
                var dz = z0 + tunnel * (0.3f + 0.6f * R());
                var clear = radius + 0.7f;                   // W-64: out on the snow beside the porch, never inside the dome
                if (dx * dx + dz * dz < clear * clear)
                    dz = Mathf.Sqrt(clear * clear - dx * dx);
                Alpine(parts, PickAlpine(rng), new Vector3(dx, 0f, dz), R() * 360f, rng);
            }
            // (W-65b's snow block behind the door is gone: it showed through the door. W-65c stops walkers with a
            // collider-only circle behind the door instead: FloraGenerator, the igloo's inside cylinders.)
            return parts;
        }

        // ------------------------------------------------------------------------------------------- sea folk (#37)

        /// <summary>Which lake-bed details turn up and how often (data: add a kind here and a case in Sea).</summary>
        public static readonly (SeaDetail Kind, float Weight)[] SeaDetails =
        {
            (SeaDetail.KelpGarden, 3f), (SeaDetail.ShellMosaic, 1.5f), (SeaDetail.BubbleVent, 1.5f),
            (SeaDetail.TradeGoods, 1.5f), (SeaDetail.CoralArch, 1.5f), (SeaDetail.Anemone, 2.5f)
        };

        /// <summary>A sea detail drawn by weight from SeaDetails.</summary>
        public static SeaDetail PickSea(System.Random rng)
        {
            var total = 0f;
            foreach (var d in SeaDetails) total += d.Weight;
            var x = (float)rng.NextDouble() * total;
            foreach (var d in SeaDetails)
                if ((x -= d.Weight) < 0f) return d.Kind;
            return SeaDetails[0].Kind;
        }

        /// <summary>
        /// A sea folk's shell dome (#37): a shell-block dome with a door gap at +Z, coral ribs fanning over it like a
        /// scallop, glowing pearl panes, a pearl crown, a coral arch over the door and a lake-bed detail or two beside it.
        /// </summary>
        public static List<KitPart> ShellDome(uint seed, CultureTrim t, float diameter)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x5E11u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var radius = diameter * 0.5f;
            var squash = 0.9f + R() * 0.3f;
            Dome(parts, Vector3.zero, radius, squash, true, rng);
            // Coral ribs over the dome, each a run of short slabs following the curve.
            for (int k = 0, ribs = 6 + rng.Next(4); k < ribs; k++)
            {
                var a = k * 360f / ribs + 360f / ribs * 0.5f;
                for (int s = 1; s < 6; s++)
                {
                    var phi = s * 15f;
                    var at = Turn(a, new Vector3(0f, radius * squash * Mathf.Sin(phi * Mathf.Deg2Rad) + 0.1f, radius * Mathf.Cos(phi * Mathf.Deg2Rad) + 0.08f));
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, at, new Vector3(0.28f, radius * 0.3f, 0.18f), a, false, -(90f - phi));
                }
            }
            for (int k = 0, panes = 3 + rng.Next(3); k < panes; k++)
            {
                var a = 50f + k * 260f / panes + (R() - 0.5f) * 20f;
                Add(parts, KitPiece.Window, KitMaterial.Rune, Turn(a, new Vector3(0f, radius * squash * 0.4f, radius * 0.95f)), new Vector3(0.6f, 0.6f, 0.12f), a, false, -24f);
            }
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, new Vector3(0f, radius * squash + 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.6f), 45f, false, 45f);
            const float doorW = 1.6f, doorH = 2.6f;
            var z0 = radius * 0.82f;
            Add(parts, KitPiece.Door, KitMaterial.Opening, new Vector3(0f, doorH * 0.5f, z0 + 0.1f), new Vector3(doorW, doorH, 0.14f), 0f, false);
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(side * (doorW * 0.5f + 0.25f), doorH * 0.5f, z0 + 0.5f), new Vector3(0.4f, doorH, 0.4f), 0f, false);
            Add(parts, KitPiece.Arch, KitMaterial.Trim, new Vector3(0f, doorH + 0.2f, z0 + 0.5f), new Vector3(doorW + 1.1f, 0.4f, 0.4f), 0f, false);
            if (R() < 0.5f)
                Dome(parts, new Vector3((R() < 0.5f ? -1f : 1f) * radius * 1.05f, 0f, -radius * 0.2f), radius * 0.5f, 0.9f, false, rng);
            for (int k = 0, n = 1 + rng.Next(2); k < n; k++)
                Sea(parts, PickSea(rng), new Vector3((k == 0 ? 1f : -1f) * (doorW + 2f + R()), 0f, z0 + 1f + R() * 1.5f), R() * 360f, rng, t);
            return parts;
        }

        /// <summary>
        /// A sea folk's spire (#37): a spiral shell of stacked, turning, tapering whorls <paramref name="height"/> tall, coral
        /// bands between them, a glowing lamp gallery near the top and a pearl on the tip, so it reads from the shore.
        /// </summary>
        public static List<KitPart> Spire(uint seed, CultureTrim t, float height)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x5B12Eu) & 0x7FFFFFFF));
            var whorls = Mathf.Max(6, Mathf.RoundToInt(height / 2.2f));
            var baseR = 1.8f + (float)rng.NextDouble() * 0.8f;
            var turn = 18f + (float)rng.NextDouble() * 14f;
            var step = height / whorls;
            for (int k = 0; k < whorls; k++)
            {
                var f = (float)k / whorls;
                var r = baseR * (1f - f * 0.8f);
                Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(0f, (k + 0.5f) * step, 0f), new Vector3(r * 2f, step * 1.02f, r * 2f), k * turn, k == 0);
                Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, (k + 1f) * step, 0f), new Vector3(r * 2.2f, 0.22f, r * 2.2f), k * turn + 45f, false);
            }
            var gallery = height * 0.84f;
            var gr = baseR * (1f - 0.84f * 0.8f) + 0.3f;
            for (int k = 0; k < 4; k++)
                Add(parts, KitPiece.Window, KitMaterial.Lamp, Turn(k * 90f + 45f, new Vector3(0f, gallery, gr)), new Vector3(0.4f, 0.7f, 0.1f), k * 90f + 45f, false);
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, new Vector3(0f, height + 0.45f, 0f), new Vector3(0.8f, 0.8f, 0.8f), 45f, false, 45f);
            // Kelp and an anemone round its foot.
            Sea(parts, SeaDetail.KelpGarden, new Vector3(baseR + 1.2f, 0f, 0f), (float)rng.NextDouble() * 360f, rng, t);
            Sea(parts, SeaDetail.Anemone, new Vector3(-baseR - 0.8f, 0f, 0.6f), 0f, rng, t);
            return parts;
        }

        /// <summary>
        /// A sea folk's landing pier (#37) along +Z from the bank: a ramp up from the ground, a <paramref name="length"/> deck
        /// on piles long enough to reach a deep lake bed, shell lamps on the rails and a coral arch at the far end.
        /// </summary>
        public static List<KitPart> Pier(uint seed, CultureTrim t, float length, float width, float deck, float piles)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x913Eu) & 0x7FFFFFFF));
            Platform(parts, new Vector3(0f, 0f, length * 0.5f), 0f, width, length, deck, true);
            // W-62b: a solid bed right under the planks. The planks have thin gaps, and on a pier this long the support
            // grid's cell centres fell in three of them, so a walker dropped through the deck to the lake bed.
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, deck - 0.17f, length * 0.5f), new Vector3(width * 0.94f, 0.1f, length), 0f, false);
            for (int k = 0, n = Mathf.Max(2, Mathf.RoundToInt(length / 3f) + 1); k < n; k++)
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(side * width * 0.45f, -piles * 0.5f, k * length / (n - 1)), new Vector3(0.3f, piles, 0.3f), 0f, false);
            // The ramp from the bank, as on a fishing dock (W-59c): starts 0.4 m in the ground, walked up like a slope.
            // W-62b: longer than a dock's (8 m, about 10 degrees). A pier is long, so its support grid's cells are
            // about 0.8 m, and a 4.6 m ramp rose 0.5 m a cell: the player met a step at the deck's land end.
            const float rampDrop = 0.4f, rampRun = 8f;
            var rise = deck + rampDrop;
            var pitch = -Mathf.Atan2(rise, rampRun) * Mathf.Rad2Deg;
            var slope = Mathf.Sqrt(rise * rise + rampRun * rampRun);
            var down = Quaternion.Euler(pitch, 0f, 0f) * Vector3.up * 0.08f;
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, (deck - rampDrop) * 0.5f, -rampRun * 0.5f) - down, new Vector3(width * 0.8f, 0.16f, slope), 0f, false, pitch);
            // Shell lamps on every other rail post, glowing at dusk.
            var details = parts.Count;
            for (int k = 1, n = Mathf.RoundToInt(length / 6f); k <= n; k++)
            {
                var side = k % 2 == 0 ? -1f : 1f;
                Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(side * width * 0.45f, deck + 1.25f, k * length / (n + 0.5f)), new Vector3(0.3f, 0.36f, 0.3f), 45f, false, 45f);
            }
            // A coral arch over the far end and a pearl on it.
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(side * (width * 0.5f + 0.1f), deck + 1.6f, length - 0.3f), new Vector3(0.36f, 3.2f, 0.36f), (float)rng.NextDouble() * 30f, false);
            Add(parts, KitPiece.Arch, KitMaterial.Trim, new Vector3(0f, deck + 3.3f, length - 0.3f), new Vector3(width + 0.9f, 0.4f, 0.4f), 0f, false);
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, new Vector3(0f, deck + 3.8f, length - 0.3f), new Vector3(0.5f, 0.5f, 0.5f), 45f, false, 45f);
            // W-147: wooden ladders down into the lake off the end and the sides.
            WaterLadders(parts, seed, length, width, deck, -DockWater, Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1), details, false);
            return parts;
        }

        /// <summary>A cluster of two to four lake-bed details round the origin (#37), each picked from SeaDetails by the seed.</summary>
        public static List<KitPart> Reef(uint seed, CultureTrim t, float spread)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x2EEFu) & 0x7FFFFFFF));
            for (int k = 0, n = 2 + rng.Next(3); k < n; k++)
            {
                var a = (k + (float)rng.NextDouble() * 0.6f) * Mathf.PI * 2f / n;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (k == 0 ? 0f : spread * (0.6f + 0.4f * (float)rng.NextDouble()));
                Sea(parts, PickSea(rng), at, (float)rng.NextDouble() * 360f, rng, t);
            }
            return parts;
        }

        /// <summary>
        /// W-120 (#37): a sea folk glow stone, <paramref name="height"/> tall: a rough standing stone of stacked, leaning
        /// blocks, two or three glowing rune bands round it, a glowing pearl on top, a shell or two at its foot and,
        /// now and then, an anemone or a curl of kelp. A ring of them lights the spire's square at dusk.
        /// </summary>
        /// <summary>
        /// W-127: a street light of <paramref name="style"/>, standing at the origin, its lit part about
        /// <see cref="FixtureLightHeight"/> up (where its glow comes from). Every choice comes from the seed.
        /// </summary>
        public static List<KitPart> Fixture(uint seed, CultureTrim t, FixtureStyle style)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xF1C7u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var lean = (R() - 0.5f) * 3f;
            switch (style)
            {
                case FixtureStyle.Lantern:
                {
                    var h = 2.3f + R() * 0.4f;
                    Add(parts, KitPiece.Trim, KitMaterial.Stone, new Vector3(0f, 0.12f, 0f), new Vector3(0.4f, 0.24f, 0.4f), R() * 90f, false);
                    Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.14f, h, 0.14f), 0f, true, lean);
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0.28f, h - 0.1f, 0f), new Vector3(0.6f, 0.08f, 0.08f), 0f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0.5f, h - 0.24f, 0f), new Vector3(0.3f, 0.06f, 0.3f), 45f, false);
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0.5f, h - 0.44f, 0f), new Vector3(0.24f, 0.32f, 0.24f), 0f, false);
                    break;
                }
                case FixtureStyle.Sconce:
                {
                    var h = 1.5f + R() * 0.3f;
                    Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.7f, h, 0.35f), 0f, true);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, h + 0.05f, 0f), new Vector3(0.8f, 0.1f, 0.45f), 0f, false);
                    Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(0f, h - 0.15f, 0.3f), new Vector3(0.05f, 0.05f, 0.3f), 0f, false);
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0f, h - 0.32f, 0.42f), new Vector3(0.2f, 0.28f, 0.2f), 0f, false);
                    break;
                }
                case FixtureStyle.Brazier:
                {
                    for (int k = 0; k < 3; k++)
                    {
                        var a = k * 120f + R() * 10f;
                        Add(parts, KitPiece.Column, KitMaterial.Trim, Turn(a, new Vector3(0f, 0.5f, 0.28f)), new Vector3(0.06f, 1.05f, 0.06f), a, false, -14f);
                    }
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 1.02f, 0f), new Vector3(0.8f, 0.22f, 0.8f), R() * 90f, true);
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0f, 1.16f, 0f), new Vector3(0.62f, 0.14f, 0.62f), R() * 90f, false);
                    for (int k = 0, n = 2 + rng.Next(2); k < n; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3((R() - 0.5f) * 0.3f, 1.24f, (R() - 0.5f) * 0.3f), new Vector3(0.08f, 0.08f, 0.5f), R() * 180f, false, (R() - 0.5f) * 30f);
                    break;
                }
                case FixtureStyle.Candelabra:
                {
                    var h = 1.9f + R() * 0.3f;
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 0.06f, 0f), new Vector3(0.5f, 0.12f, 0.5f), 45f, false);
                    Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.08f, h, 0.08f), 0f, true);
                    var arms = 3 + rng.Next(3);
                    for (int k = 0; k < arms; k++)
                    {
                        var a = k * 360f / arms + R() * 8f;
                        Add(parts, KitPiece.Beam, KitMaterial.Trim, Turn(a, new Vector3(0f, h - 0.1f, 0.2f)), new Vector3(0.04f, 0.04f, 0.4f), a, false);
                        Add(parts, KitPiece.Window, KitMaterial.Lamp, Turn(a, new Vector3(0f, h + 0.02f, 0.38f)), new Vector3(0.07f, 0.2f, 0.07f), a, false);
                    }
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0f, h + 0.12f, 0f), new Vector3(0.08f, 0.24f, 0.08f), 0f, false);
                    break;
                }
                case FixtureStyle.Rune:
                {
                    var h = 1.3f + R() * 0.5f;
                    Add(parts, KitPiece.Rubble, KitMaterial.Stone, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.6f, h, 0.4f), R() * 360f, true, lean);
                    Add(parts, KitPiece.Window, KitMaterial.Rune, new Vector3(0f, h * 0.62f, 0.19f), new Vector3(0.22f, 0.4f, 0.06f), 0f, false, lean);
                    Add(parts, KitPiece.Window, KitMaterial.Rune, new Vector3(0f, h * 0.62f, -0.19f), new Vector3(0.22f, 0.4f, 0.06f), 0f, false, lean);
                    break;
                }
                case FixtureStyle.PierLamp:
                {
                    // A tarred pier post, a crossbar with the oil lamp off it, a net and a float hung to dry.
                    var h = 2.6f + R() * 0.4f;
                    Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.2f, h, 0.2f), 0f, true, lean);
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0f, h - 0.15f, 0f), new Vector3(1.1f, 0.1f, 0.1f), 0f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(0.45f, h - 0.3f, 0f), new Vector3(0.02f, 0.2f, 0.02f), 0f, false);
                    Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0.45f, h - 0.55f, 0f), new Vector3(0.22f, 0.3f, 0.22f), 0f, false, 0f, (R() - 0.5f) * 8f);
                    Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(-0.4f, h - 0.7f, 0f), new Vector3(0.5f, 0.9f, 0.04f), 0f, false, 0f, 6f);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(-0.4f, h - 1.2f, 0.05f), new Vector3(0.16f, 0.16f, 0.16f), 45f, false, 45f);
                    break;
                }
                case FixtureStyle.Beacon:
                    Beacon(parts, rng);
                    break;
                case FixtureStyle.WallLantern:
                {
                    // A plate on the wall (reaching back into rough rock), an arm out with a brace under it, and the
                    // lantern hung off it: a cap, the glass and a drip tray.
                    var armOut = WallLanternAt.z;
                    var first = parts.Count;
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 0.12f, -0.06f), new Vector3(0.14f, 0.34f, 0.2f), 0f, false);
                    Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(0f, 0.3f, armOut * 0.5f - 0.04f), new Vector3(0.035f, 0.035f, armOut + 0.1f), 0f, false);
                    Add(parts, KitPiece.Beam, KitMaterial.Trim, new Vector3(0f, 0.15f, 0.1f), new Vector3(0.03f, 0.03f, 0.28f), 0f, false, -38f);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 0.25f, armOut), new Vector3(0.02f, 0.08f, 0.02f), 0f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, WallLanternAt.y + 0.16f, armOut), new Vector3(0.2f, 0.05f, 0.2f), 45f, false);
                    Add(parts, KitPiece.Window, KitMaterial.Flame, new Vector3(0f, WallLanternAt.y, armOut), new Vector3(0.15f, 0.24f, 0.15f), R() * 10f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, WallLanternAt.y - 0.14f, armOut), new Vector3(0.18f, 0.04f, 0.18f), 0f, false);
                    // It hangs on a wall, off the ground: keep the foundation skirt from carrying its foot to the floor.
                    for (int k = first; k < parts.Count; k++)
                    {
                        var part = parts[k];
                        part.Afloat = true;
                        parts[k] = part;
                    }
                    break;
                }
            }
            return parts;
        }

        /// <summary>
        /// W-127: a beacon tower's deck height, the half width of its footprint (core and stair) and where its fire burns
        /// (above its foot), at scale 1. Fixed, so LandmarkEffectSystem and the world glow find the fire without the model.
        /// </summary>
        public const float BeaconDeck = 10.4f, BeaconHalf = 2.5f, BeaconFire = BeaconDeck + 0.8f;
        const float BeaconCore = 1.5f, BeaconStairWidth = 1f;

        /// <summary>
        /// W-127: a beacon tower: a square stone shaft, its stair of cantilevered treads winding up the outside (rises
        /// under KitStep, a landing at each corner), a railed deck on top with an iron fire bowl on a plinth and a stack of
        /// fuel, a door and slit windows in the shaft and a woodpile at the stair's foot. Every part but the decor is solid
        /// (the collision is the art), so the stair is climbed like any other.
        /// </summary>
        static void Beacon(List<KitPart> parts, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            const float c = BeaconCore, w = BeaconStairWidth, mid = c + w * 0.5f, run = 0.3f;
            const int perSide = 10;   // perSide x run spans the shaft's face (2c) between corner landings
            // The shaft on a plinth sunk into the ground (it squares up on a slope), a cornice band under the deck.
            Add(parts, KitPiece.Wall, KitMaterial.Stone, new Vector3(0f, -0.45f, 0f), new Vector3(2f * c + 0.3f, 1.1f, 2f * c + 0.3f), 0f, true);
            Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(0f, (BeaconDeck - 0.3f) * 0.5f, 0f), new Vector3(2f * c, BeaconDeck - 0.3f, 2f * c), 0f, true);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, BeaconDeck - 0.45f, 0f), new Vector3(2f * c + 0.16f, 0.2f, 2f * c + 0.16f), 0f, false);
            Add(parts, KitPiece.Platform, KitMaterial.Stone, new Vector3(0f, BeaconDeck - 0.15f, 0f), new Vector3(2f * c + 0.2f, 0.3f, 2f * c + 0.2f), 0f, false);
            // The stair: along each face in turn (side s turns s x 90 degrees), rising the same at every tread to the deck.
            var steps = Mathf.CeilToInt(BeaconDeck / 0.28f);
            var rise = BeaconDeck / steps;
            for (int k = 0; k < steps; k++)
            {
                var side = k / perSide;
                var i = k % perSide;
                var top = (k + 1) * rise;
                var thick = top < 1.2f ? top + 0.4f : 0.42f;   // the low treads go down to the ground
                Add(parts, KitPiece.Stair, KitMaterial.Stone, Turn(side * 90f, new Vector3(-c + (i + 0.5f) * run, top - thick * 0.5f, mid)),
                    new Vector3(run + 0.02f, thick, w), side * 90f, false);
                if (i == perSide - 1 && k < steps - 1)
                    Add(parts, KitPiece.Stair, KitMaterial.Stone, Turn(side * 90f, new Vector3(mid, top - thick * 0.5f, mid)),
                        new Vector3(w, thick, w), side * 90f, false);
            }
            // The deck's rail: all round but where the stair comes up (the last face, past its first third).
            var last = (steps - 1) / perSide;
            // A landing at the stair's head, level with the deck, so a climber can turn onto the deck (not only off a
            // single tread's width) and doesn't walk off the end of the flight.
            var head = -c + ((steps - 1) % perSide + 1) * run;
            Add(parts, KitPiece.Stair, KitMaterial.Stone, Turn(last * 90f, new Vector3((head + mid + w * 0.5f) * 0.5f, BeaconDeck - 0.21f, mid)),
                new Vector3(mid + w * 0.5f - head, 0.42f, w), last * 90f, false);
            for (int s = 0; s < 4; s++)
            {
                var from = -c - 0.1f;
                var to = s == last ? -0.3f : c + 0.1f;
                Add(parts, KitPiece.Rail, KitMaterial.Wood, Turn(s * 90f, new Vector3((from + to) * 0.5f, BeaconDeck + 0.95f, c + 0.04f)), new Vector3(to - from, 0.1f, 0.1f), s * 90f, false);
                for (var x = from + 0.05f; x <= to; x += 0.75f)
                    Add(parts, KitPiece.Rail, KitMaterial.Wood, Turn(s * 90f, new Vector3(x, BeaconDeck + 0.47f, c + 0.04f)), new Vector3(0.1f, 0.95f, 0.1f), s * 90f, false);
            }
            // The fire: an iron bowl of glowing coals and split logs on a stone plinth.
            Add(parts, KitPiece.Column, KitMaterial.Stone, new Vector3(0f, BeaconDeck + 0.25f, 0f), new Vector3(0.7f, 0.5f, 0.7f), 0f, true);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, BeaconDeck + 0.6f, 0f), new Vector3(1.3f, 0.24f, 1.3f), 45f, false);
            Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(0f, BeaconDeck + 0.72f, 0f), new Vector3(1.04f, 0.12f, 1.04f), 45f, false);
            for (int k = 0; k < 4; k++)
                Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3((R() - 0.5f) * 0.4f, BeaconDeck + 0.82f, (R() - 0.5f) * 0.4f), new Vector3(0.1f, 0.1f, 0.7f), k * 45f + R() * 30f, false, (R() - 0.5f) * 24f);
            // Fuel stacked in a deck corner, away from the stair's head.
            var corner = Turn((last + 2) * 90f, new Vector3(-c + 0.45f, 0f, c - 0.35f));
            for (int k = 0; k < 5; k++)
                Add(parts, KitPiece.Trim, KitMaterial.Wood, corner + new Vector3(0f, BeaconDeck + 0.06f + (k < 3 ? 0f : 0.11f), 0f) + Turn((last + 2) * 90f, new Vector3(((k < 3 ? k : k - 3 + 0.5f) - 1f) * 0.12f, 0f, 0f)),
                    new Vector3(0.11f, 0.11f, 0.6f), (last + 2) * 90f, false);
            // A door at the foot (on the face the stair reaches last) and slit windows up the faces.
            Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(last * 90f, new Vector3(-c * 0.5f, 0.95f, c + 0.01f)), new Vector3(0.8f, 1.9f, 0.06f), last * 90f, false);
            for (int s = 0; s < 4; s++)
                for (var y = 2.2f + s * 0.9f + R() * 0.4f; y < BeaconDeck - 1.5f; y += 3.6f)
                    Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(s * 90f, new Vector3(c * (R() - 0.5f), y, c + 0.01f)), new Vector3(0.2f, 0.8f, 0.06f), s * 90f, false);
            // A woodpile by the stair's foot: the fuel waiting to be carried up.
            var pile = Turn(270f, new Vector3(-c + 0.6f, 0f, mid + 0.95f));
            for (int k = 0; k < 6; k++)
            {
                var row = k < 3 ? 0 : k < 5 ? 1 : 2;
                var x = (k < 3 ? k - 1f : k < 5 ? k - 3.5f : 0f) * 0.16f;
                Add(parts, KitPiece.Trim, KitMaterial.Wood, pile + Turn(270f, new Vector3(x, 0.08f + row * 0.14f, 0f)), new Vector3(0.15f, 0.15f, 0.8f + R() * 0.15f), 270f, false);
            }
        }

        /// <summary>W-127: about where a fixture's light comes from, at scale 1 (m above its foot).</summary>
        public const float FixtureLightHeight = 1.9f;

        /// <summary>W-138 row 4c: where a wall lantern's glass hangs, from its foot on the wall (m, +Z out from the wall).</summary>
        static readonly Vector3 WallLanternAt = new Vector3(0f, 0.04f, 0.36f);

        /// <summary>W-138 row 4c: where a fixture style's light comes from, from its foot at scale 1 (+Z is the model's
        /// own forward, so turn it by the fixture's yaw).</summary>
        public static Vector3 FixtureLightAt(FixtureStyle style)
            => style == FixtureStyle.WallLantern ? WallLanternAt : new Vector3(0f, FixtureLightHeight, 0f);

        /// <summary>W-127: a fixture style's light (linear rgb, strength folded in) and reach (m).</summary>
        public static Vector4 FixtureLight(FixtureStyle style)
        {
            var lamp = LampLight.linear;
            switch (style)
            {
                // W-138 row 4c: a lantern turned low, about four fifths of a street Lantern's light over a shorter reach.
                case FixtureStyle.WallLantern: return new Vector4(lamp.r * 1.05f, lamp.g * 1.05f, lamp.b * 1.05f, 7f);
                case FixtureStyle.Brazier: return new Vector4(1.6f, 0.62f, 0.22f, 12f);
                case FixtureStyle.Rune: return new Vector4(0.32f, 0.95f, 0.85f, 8f);
                case FixtureStyle.Sconce: return new Vector4(lamp.r * 1.1f, lamp.g * 1.1f, lamp.b * 1.1f, 7f);
                case FixtureStyle.Candelabra: return new Vector4(lamp.r * 1.2f, lamp.g * 1.2f, lamp.b * 1.2f, 8f);
                case FixtureStyle.PierLamp: return new Vector4(lamp.r * 1.3f, lamp.g * 1.17f, lamp.b * 0.91f, 10f);
                case FixtureStyle.Beacon: return Vector4.zero;   // its fire is lit as a fire (LandmarkEffectSystem), not a lamp
                default: return new Vector4(lamp.r * 1.3f, lamp.g * 1.3f, lamp.b * 1.3f, 9f);
            }
        }

        public static List<KitPart> GlowStone(uint seed, CultureTrim t, float height)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x6105Eu) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var blocks = 3 + rng.Next(2);
            var step = height / blocks;
            var lean = (R() - 0.5f) * 10f;
            var x = 0f;
            for (int k = 0; k < blocks; k++)
            {
                var w = 1.1f - k * 0.16f + R() * 0.2f;
                Add(parts, KitPiece.Rubble, KitMaterial.Stone, new Vector3(x, (k + 0.5f) * step, 0f), new Vector3(w, step * 1.04f, w * 0.8f), R() * 360f, k == 0, lean, (R() - 0.5f) * 6f);
                x += Mathf.Sin(lean * Mathf.Deg2Rad) * step;
                if (k > 0 && (k < blocks - 1 || blocks == 3))
                    Add(parts, KitPiece.Trim, KitMaterial.Rune, new Vector3(x, k * step, 0f), new Vector3(w * 1.08f, 0.16f, w * 0.88f), R() * 360f, false, lean);
            }
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, new Vector3(x, height + 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f), 45f, false, 45f);
            for (int k = 0, n = 1 + rng.Next(2); k < n; k++)
            {
                var a = R() * 360f;
                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(a, new Vector3(0f, 0.1f, 0.9f + R() * 0.3f)), new Vector3(0.36f, 0.16f, 0.3f), a, false, 0f, 20f);
            }
            var extra = R();
            if (extra < 0.3f) Sea(parts, SeaDetail.Anemone, Turn(R() * 360f, new Vector3(0f, 0f, 1.4f)), R() * 360f, rng, t);
            else if (extra < 0.55f) Sea(parts, SeaDetail.KelpGarden, Turn(R() * 360f, new Vector3(0f, 0f, 1.6f)), R() * 360f, rng, t);
            return parts;
        }

        /// <summary>
        /// W-120 (#37): a sunken boat about <paramref name="length"/> long along +Z: a plank hull (keel, bottom, sloped
        /// sides, ribs, thwarts) keeled over on its side and half sunk in the bed, its mast snapped, its stern sometimes
        /// broken open, and grown over with shell clusters, coral knobs and a strand or two of kelp.
        /// </summary>
        public static List<KitPart> Wreck(uint seed, CultureTrim t, float length)
        {
            var hull = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x3EC4u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var beam = length * 0.32f;
            var deep = length * 0.2f;
            var broken = R() < 0.4f;   // the stern stove in: its last planks gone
            Add(hull, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, -deep * 0.5f, 0f), new Vector3(0.1f + length * 0.014f, 0.1f + length * 0.014f, length), 0f, true);
            Add(hull, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, -deep * 0.38f, 0f), new Vector3(beam * 0.5f, 0.1f, length * 0.9f), 0f, false);
            foreach (var side in new[] { -1f, 1f })
                for (int k = 0; k < 4; k++)
                {
                    var z = (k - 1.5f) * length * 0.24f;
                    if (broken && k == 0 && side > 0f) continue;
                    var taper = 1f - Mathf.Abs(k - 1.5f) * 0.22f;
                    Add(hull, KitPiece.Wall, KitMaterial.Wood, new Vector3(side * beam * 0.36f * taper, 0f, z), new Vector3(0.05f + length * 0.012f, deep, length * 0.25f), 0f, false, 0f, side * -28f);
                }
            for (int k = 0, n = 4; k < n; k++)
                Add(hull, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, -deep * 0.1f, (k - 1.5f) * length * 0.22f), new Vector3(beam * 0.8f, 0.1f, 0.14f), 0f, false);
            Add(hull, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, deep * 0.2f, length * 0.05f), new Vector3(beam * 0.7f, 0.08f, 0.4f), 0f, false);
            var mast = length * (0.25f + R() * 0.2f);
            Add(hull, KitPiece.Column, KitMaterial.Wood, new Vector3(0f, deep * 0.2f + mast * 0.5f, length * 0.1f), new Vector3(0.18f, mast, 0.18f), 0f, false, (R() - 0.5f) * 12f);
            // Keel it over and sink it: the hull frame turned about Z, bedded so its low gunwale is under the sand.
            var roll = (R() < 0.5f ? -1f : 1f) * (55f + R() * 25f);
            var q = Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler((R() - 0.5f) * 8f, 0f, 0f);
            var parts = new List<KitPart>();
            var sink = new Vector3(0f, beam * 0.3f + 0.15f, 0f);   // its low side rests on the bed, the rest stands clear
            foreach (var p in hull)
            {
                var c = p;
                c.Centre = q * p.Centre + sink;
                c.Rotation = q * p.Rotation;
                parts.Add(c);
            }
            // Shell growth on the upturned side: pearl shell clusters and coral knobs, set on the hull's top surface.
            var up = q * Vector3.up;
            for (int k = 0, n = 6 + rng.Next(5); k < n; k++)
            {
                var along = (R() - 0.5f) * length * 0.85f;
                var across = (R() - 0.5f) * beam * 0.5f;
                var at = q * new Vector3(across, -deep * 0.5f - 0.12f, along) + sink;
                at.y = Mathf.Max(at.y, 0.1f);
                var s = (0.18f + R() * 0.22f) * Mathf.Max(1f, length / 9f);
                var m = R() < 0.55f ? KitMaterial.Wall : KitMaterial.Trim;
                Add(parts, KitPiece.Trim, m, at, new Vector3(s, s * 0.6f, s), R() * 360f, false, R() * 40f, R() * 40f);
            }
            for (int k = 0, n = 3 + rng.Next(3); k < n; k++)
            {
                var at = q * new Vector3((R() - 0.5f) * beam * 0.6f, deep * 0.4f, (R() - 0.5f) * length * 0.8f) + sink;
                at.y = Mathf.Max(at.y, 0.1f);
                Add(parts, KitPiece.Column, KitMaterial.Trim, at, new Vector3(0.14f, 0.4f + R() * 0.4f, 0.14f), R() * 360f, false, (R() - 0.5f) * 40f);
            }
            Sea(parts, SeaDetail.KelpGarden, new Vector3(-Mathf.Sign(roll) * (beam * 0.7f + 0.6f), 0f, (R() - 0.5f) * length * 0.5f), R() * 360f, rng, t);
            if (R() < 0.5f)
                Sea(parts, SeaDetail.TradeGoods, new Vector3(Mathf.Sign(roll) * (beam + 0.8f), 0f, length * 0.3f), R() * 360f, rng, t);
            return parts;
        }

        /// <summary>
        /// W-121 (#37): a shell buoy floating on a sea folk village's lake surface (origin at the waterline), about
        /// <paramref name="size"/> across: a ribbed shell float riding half under, a short pole with a glowing pearl on
        /// top, a few barnacle shells, and its cord running down into the water towards the village below.
        /// </summary>
        public static List<KitPart> ShellBuoy(uint seed, CultureTrim t, float size)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xB0E1u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var tilt = (R() - 0.5f) * 14f;
            var spin = R() * 360f;
            // The float: a flattened shell, its ribs fanning over the top, half under the surface.
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, size * 0.05f, 0f), new Vector3(size, size * 0.45f, size * 0.9f), spin, false, tilt);
            for (int k = -2; k <= 2; k++)
                Add(parts, KitPiece.Trim, KitMaterial.Wall, Turn(spin + k * 24f, new Vector3(0f, size * 0.26f, size * 0.22f)), new Vector3(size * 0.08f, size * 0.08f, size * 0.5f), spin + k * 24f, false, tilt - 18f);
            // A ring of lashing round its waist.
            Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(0f, size * 0.02f, 0f), new Vector3(size * 1.04f, size * 0.07f, size * 0.94f), spin, false, tilt);
            // The pole and its pearl, sometimes a kelp streamer tied below it.
            var pole = size * (1.1f + R() * 0.5f);
            Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(0f, size * 0.2f + pole * 0.5f, 0f), new Vector3(size * 0.09f, pole, size * 0.09f), spin, false, tilt);
            var top = Quaternion.Euler(tilt, spin, 0f) * new Vector3(0f, pole, 0f) + new Vector3(0f, size * 0.2f, 0f);
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, top + new Vector3(0f, size * 0.12f, 0f), Vector3.one * size * 0.26f, 45f, false, 45f);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, top - new Vector3(0f, size * 0.04f, 0f), new Vector3(size * 0.3f, size * 0.08f, size * 0.3f), spin, false);
            if (R() < 0.6f)
                Add(parts, KitPiece.Trim, KitMaterial.Moss, top * 0.8f + Turn(spin, new Vector3(size * 0.22f, 0f, 0f)), new Vector3(size * 0.06f, size * 0.5f, size * 0.14f), spin, false, 0f, 22f + R() * 20f);
            for (int k = 0, n = 2 + rng.Next(3); k < n; k++)
            {
                var a = R() * 360f;
                var c = size * (0.1f + R() * 0.08f);
                Add(parts, KitPiece.Trim, KitMaterial.Wall, Turn(a, new Vector3(0f, size * 0.12f, size * 0.42f)), new Vector3(c, c * 0.6f, c), a, false, R() * 40f);
            }
            // The cord down towards its anchor stone on the bed, swaying a little off true.
            Add(parts, KitPiece.Column, KitMaterial.Net, new Vector3(size * 0.1f, -2.2f, 0f), new Vector3(0.05f, 4.4f, 0.05f), spin, false, 0f, (R() - 0.5f) * 16f);
            return parts;
        }

        /// <summary>
        /// W-121 (#37): a sea folk raft about <paramref name="length"/> long along +Z, floating at the origin (the
        /// waterline) by a landing: a deck of lashed logs over two cross beams, a short mast with a sail of overlapping
        /// shell scales, a woven basket or two and a pot of glowing pearls, a paddle laid across, and its mooring line
        /// slanting off the bow (+Z) down into the water.
        /// </summary>
        public static List<KitPart> SeaRaft(uint seed, CultureTrim t, float length)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x5EA2u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var logs = 5 + rng.Next(3);
            var log = length * 0.11f;
            var width = logs * log;
            for (int k = 0; k < logs; k++)
            {
                var l = length * (0.88f + R() * 0.12f);
                Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3((k - (logs - 1) * 0.5f) * log, log * 0.15f, (R() - 0.5f) * length * 0.06f), new Vector3(log * 0.95f, l, log * 0.95f), 0f, k == 0, 90f);
            }
            foreach (var z in new[] { -0.32f, 0.32f })
            {
                Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0f, log * 0.7f, z * length), new Vector3(width * 1.08f, log * 0.4f, log * 0.5f), 0f, false);
                Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(0f, log * 0.72f, z * length), new Vector3(width * 1.1f, log * 0.12f, log * 0.62f), 0f, false);
            }
            var deck = log * 0.65f;
            // Mast and shell-scale sail, tied a little off square.
            var mast = length * (0.7f + R() * 0.25f);
            Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(0f, deck + mast * 0.5f, length * 0.1f), new Vector3(log * 0.45f, mast, log * 0.45f), 0f, false, (R() - 0.5f) * 6f);
            var sailYaw = (R() - 0.5f) * 40f;
            Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0f, deck + mast * 0.92f, length * 0.1f), new Vector3(width * 0.95f, log * 0.25f, log * 0.25f), sailYaw, false);
            const int rows = 4;
            for (int r = 0; r < rows; r++)
            {
                var w = width * (0.9f - r * 0.08f);
                var y = deck + mast * (0.86f - (r + 0.5f) * 0.6f / rows);
                Add(parts, KitPiece.Wall, r % 2 == 0 ? KitMaterial.Trim : KitMaterial.Wall, new Vector3(0f, y, length * 0.1f + 0.05f) + Turn(sailYaw, new Vector3(0f, 0f, 0.04f * r)),
                    new Vector3(w, mast * 0.6f / rows * 1.15f, 0.06f), sailYaw, false, -6f);
            }
            // Cargo: baskets, a pot of pearls, a coil of line, a paddle laid across the stern.
            for (int k = 0, n = 1 + rng.Next(2); k < n; k++)
            {
                var b = log * (1.4f + R() * 0.6f);
                Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3((R() - 0.5f) * width * 0.6f, deck + b * 0.4f, -length * (0.15f + R() * 0.2f)), new Vector3(b, b * 0.8f, b), R() * 90f, false);
            }
            var pot = new Vector3(width * 0.28f, deck, length * 0.32f);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, pot + new Vector3(0f, log * 0.45f, 0f), new Vector3(log * 1.1f, log * 0.9f, log * 1.1f), 45f, false);
            Add(parts, KitPiece.Chimney, KitMaterial.Rune, pot + new Vector3(0f, log * 1.0f, 0f), Vector3.one * log * 0.5f, 45f, false, 45f);
            Add(parts, KitPiece.Trim, KitMaterial.Net, new Vector3(-width * 0.25f, deck + 0.06f, length * 0.3f), new Vector3(log * 1.4f, 0.12f, log * 1.4f), R() * 90f, false);
            Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0f, deck + 0.05f, -length * 0.42f), new Vector3(width * 1.3f, 0.07f, log * 0.35f), 8f + R() * 10f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(width * 0.66f, deck + 0.05f, -length * 0.4f), new Vector3(log * 0.9f, 0.06f, log * 0.5f), 10f, false);
            // The mooring line off the bow, slanting down into the water.
            var lineLength = length * 0.9f;
            Add(parts, KitPiece.Column, KitMaterial.Net, new Vector3(0f, deck * 0.5f, length * 0.5f + lineLength * 0.45f), new Vector3(0.05f, lineLength, 0.05f), 0f, false, 82f);
            return parts;
        }

        /// <summary>
        /// W-120 (#37): a pearl clam bed, five to nine giant clams scattered within <paramref name="spread"/> of the origin
        /// in a patch of shell grit: each a ribbed lower shell and a lid tipped open (wide or barely), about a third
        /// of them holding a glowing pearl; a few closed ones half buried.
        /// </summary>
        public static List<KitPart> ClamBed(uint seed, CultureTrim t, float spread)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xC1A3u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            for (int k = 0, n = 5 + rng.Next(5); k < n; k++)
            {
                var a = k * 137.5f + R() * 30f;
                var at = Turn(a, new Vector3(0f, 0f, k == 0 ? 0f : spread * Mathf.Sqrt((k + R()) / n)));
                var size = 0.7f + R() * 0.8f;
                var yaw = R() * 360f;
                var open = R() < 0.25f ? 0f : 18f + R() * 42f;
                var buried = open == 0f ? size * 0.12f : 0f;
                Add(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, size * 0.14f - buried, 0f), new Vector3(size, size * 0.28f, size * 0.75f), yaw, false);
                for (int r = -1; r <= 1; r++)   // ribs on the lower shell
                    Add(parts, KitPiece.Trim, KitMaterial.Wall, at + Turn(yaw, new Vector3(r * size * 0.3f, size * 0.17f - buried, size * 0.34f)), new Vector3(size * 0.12f, size * 0.22f, 0.06f), yaw, false);
                // The lid hinges at the back (-Z) and tips up by `open` degrees.
                var hinge = at + Turn(yaw, new Vector3(0f, size * 0.28f - buried, -size * 0.37f));
                var lid = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-open, 0f, 0f);
                parts.Add(new KitPart { Piece = KitPiece.Trim, Material = KitMaterial.Trim, Centre = hinge + lid * new Vector3(0f, 0.05f, size * 0.37f), Size = new Vector3(size * 0.98f, size * 0.12f, size * 0.74f), Rotation = lid, Solid = false });
                if (open > 0f)   // the mantle inside, glowing faintly, and in most open ones a pearl on it
                    Add(parts, KitPiece.Floor, KitMaterial.Rune, at + new Vector3(0f, size * 0.29f, 0f), new Vector3(size * 0.8f, 0.03f, size * 0.55f), yaw, false);
                if (open > 25f && R() < 0.7f)
                    Add(parts, KitPiece.Chimney, KitMaterial.Rune, at + new Vector3(0f, size * 0.34f, 0f), Vector3.one * size * 0.2f, 45f, false, 45f);
            }
            for (int k = 0, n = 8 + rng.Next(6); k < n; k++)   // shell grit and broken shells between them
            {
                var at = Turn(R() * 360f, new Vector3(0f, 0.03f, R() * spread * 1.2f));
                Add(parts, KitPiece.Floor, R() < 0.5f ? KitMaterial.Wall : KitMaterial.Roof, at, new Vector3(0.22f, 0.05f, 0.18f), R() * 360f, false, 0f, R() * 20f);
            }
            return parts;
        }

        /// <summary>W-120 (#37): two or three bubble vents close together, in a ring of dark stones with a glowing crack between.</summary>
        public static List<KitPart> VentField(uint seed, CultureTrim t, float spread)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0x7E47u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var n = 2 + rng.Next(2);
            for (int k = 0; k < n; k++)
                Sea(parts, SeaDetail.BubbleVent, Turn(k * 360f / n + R() * 40f, new Vector3(0f, 0f, k == 0 && n == 3 ? 0f : spread * (0.4f + R() * 0.3f))), R() * 360f, rng, t);
            for (int k = 0, m = 7 + rng.Next(4); k < m; k++)
            {
                var a = k * 360f / m + R() * 20f;
                var s = 0.5f + R() * 0.5f;
                Add(parts, KitPiece.Rubble, KitMaterial.Stone, Turn(a, new Vector3(0f, s * 0.3f, spread * (1.1f + R() * 0.3f))), new Vector3(s, s * 0.6f, s * 0.8f), a + R() * 60f, false, (R() - 0.5f) * 20f);
            }
            for (int k = 0; k < 3; k++)
            {
                var a = R() * 360f;
                Add(parts, KitPiece.Floor, KitMaterial.Rune, Turn(a, new Vector3(0f, 0.03f, spread * 0.5f)), new Vector3(0.12f, 0.04f, spread * 0.7f), a, false);
            }
            return parts;
        }

        /// <summary>One lake-bed detail standing at <paramref name="at"/> (its base), turned by <paramref name="yaw"/> degrees.</summary>
        public static void Sea(List<KitPart> parts, SeaDetail kind, Vector3 at, float yaw, System.Random rng, CultureTrim t)
        {
            float R() => (float)rng.NextDouble();
            switch (kind)
            {
                case SeaDetail.KelpGarden:
                    // Tall swaying strands in a loose clump. W-120: each strand is a chain of short ribbons that snakes from
                    // side to side as it climbs (each segment starts where the last one ended), narrowing to the tip, with a
                    // leaf blade off every other joint, so a strand reads as kelp rather than a flat green board.
                    for (int k = 0, n = 5 + rng.Next(5); k < n; k++)
                    {
                        var p = at + Turn(yaw + k * 137f, new Vector3(0f, 0f, R() * 1.4f));
                        var tall = 2.5f + R() * 4.5f;
                        var face = yaw + k * 53f;
                        var sway = 14f + R() * 14f;
                        var phase = R() * 6.28f;
                        var segs = 6;
                        var seg = tall / segs;
                        var foot = p;
                        for (int s = 0; s < segs; s++)
                        {
                            var lean = sway * Mathf.Sin(phase + s * 1.3f);
                            var dir = Quaternion.Euler(lean, face, 0f) * Vector3.up;
                            var w = 0.34f * (1f - s * 0.1f);
                            Add(parts, KitPiece.Moss, KitMaterial.Moss, foot + dir * (seg * 0.5f), new Vector3(w, seg + 0.06f, 0.05f), face, false, lean);
                            if (s % 2 == 1 && s < segs - 1)
                            {
                                var side = s % 4 == 1 ? 1f : -1f;
                                Add(parts, KitPiece.Moss, KitMaterial.Moss, foot + dir * seg + Turn(face, new Vector3(side * 0.22f, 0f, 0f)), new Vector3(0.14f, seg * 0.7f, 0.04f), face, false, lean, side * 40f);
                            }
                            foot += dir * seg;
                        }
                    }
                    break;
                case SeaDetail.ShellMosaic:
                    // A round floor of shell tiles in rings, alternating pearl, coral and nacre.
                    for (int r = 0; r < 3; r++)
                        for (int k = 0, n = r == 0 ? 1 : r * 7; k < n; k++)
                        {
                            var a = yaw + k * 360f / n;
                            var m = (r + k) % 3 == 0 ? KitMaterial.Wall : (r + k) % 3 == 1 ? KitMaterial.Trim : KitMaterial.Roof;
                            Add(parts, KitPiece.Floor, m, at + Turn(a, new Vector3(0f, 0.04f, r * 0.7f)), new Vector3(0.6f, 0.08f, 0.6f), a + 45f, false);
                        }
                    break;
                case SeaDetail.BubbleVent:
                    // A stone chimney with a glowing mouth and a column of bubbles rising off it.
                    for (int s = 0; s < 3; s++)
                        Add(parts, KitPiece.Rubble, KitMaterial.Stone, at + new Vector3(0f, 0.3f + s * 0.5f, 0f), new Vector3(1.4f - s * 0.35f, 0.55f, 1.4f - s * 0.35f), yaw + s * 30f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Rune, at + new Vector3(0f, 1.55f, 0f), new Vector3(0.4f, 0.08f, 0.4f), yaw, false);
                    for (int k = 0, n = 4 + rng.Next(4); k < n; k++)
                    {
                        var b = 0.12f + R() * 0.16f;
                        Add(parts, KitPiece.Trim, KitMaterial.Ice, at + new Vector3((R() - 0.5f) * 0.4f, 1.9f + k * (0.5f + R() * 0.4f), (R() - 0.5f) * 0.4f), new Vector3(b, b, b), R() * 90f, false, 45f);
                    }
                    break;
                case SeaDetail.TradeGoods:
                    // Sunken trade goods: crates, amphorae on their sides, a spilled chest of gold.
                    for (int k = 0, n = 2 + rng.Next(3); k < n; k++)
                    {
                        var p = at + Turn(yaw + k * 95f, new Vector3(0f, 0f, 0.6f + R() * 1.2f));
                        var c = 0.6f + R() * 0.3f;
                        if (R() < 0.5f)
                            Add(parts, KitPiece.Trim, KitMaterial.Wood, p + new Vector3(0f, c * 0.5f - 0.1f, 0f), new Vector3(c, c, c), R() * 90f, false, (R() - 0.5f) * 20f);
                        else
                        {
                            Add(parts, KitPiece.Trim, KitMaterial.Trim, p + new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.8f), yaw + k * 70f, false);
                            Add(parts, KitPiece.Trim, KitMaterial.Trim, p + Turn(yaw + k * 70f, new Vector3(0f, 0.25f, 0.55f)), new Vector3(0.2f, 0.2f, 0.4f), yaw + k * 70f, false);
                        }
                    }
                    Add(parts, KitPiece.Trim, KitMaterial.Wood, at + new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.6f, 0.65f), yaw, false, 0f, 12f);
                    Add(parts, KitPiece.Trim, KitMaterial.Lamp, at + Turn(yaw, new Vector3(0.2f, 0.08f, 0.6f)), new Vector3(0.9f, 0.14f, 0.5f), yaw + 20f, false);
                    break;
                case SeaDetail.CoralArch:
                    // Two knobbly coral legs and a span between them, lumps along it.
                    var span = 2.4f + R() * 1.6f;
                    var high = 2.2f + R() * 1.4f;
                    foreach (var side in new[] { -1f, 1f })
                        for (int s = 0; s < 3; s++)
                            Add(parts, KitPiece.Column, KitMaterial.Trim, at + Turn(yaw, new Vector3(side * span * 0.5f + side * (R() - 0.5f) * 0.2f, (s + 0.5f) * high / 3f, 0f)), new Vector3(0.6f - s * 0.1f, high / 3f + 0.05f, 0.6f - s * 0.1f), yaw + R() * 40f, false);
                    Add(parts, KitPiece.Arch, KitMaterial.Trim, at + new Vector3(0f, high + 0.2f, 0f), new Vector3(span + 0.6f, 0.45f, 0.5f), yaw, false);
                    for (int k = 0; k < 3; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Wall, at + Turn(yaw, new Vector3((k - 1) * span * 0.3f, high + 0.5f, 0f)), new Vector3(0.35f, 0.35f, 0.35f), R() * 90f, false, 45f);
                    break;
                default: // SeaDetail.Anemone
                    // A stone with glowing tentacles fanning up off it.
                    Add(parts, KitPiece.Rubble, KitMaterial.Stone, at + new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.4f, 0.9f), yaw, false);
                    for (int k = 0, n = 6 + rng.Next(5); k < n; k++)
                    {
                        var a = yaw + k * 360f / n;
                        Add(parts, KitPiece.Trim, KitMaterial.Rune, at + Turn(a, new Vector3(0f, 0.65f, 0.18f)), new Vector3(0.08f, 0.6f + R() * 0.3f, 0.08f), a, false, -25f - R() * 20f);
                    }
                    break;
            }
        }

        /// <summary>A dome of snow blocks in rings (each ring a little smaller and leaning in), with a gap at +Z for the porch.</summary>
        /// <remarks>
        /// With <paramref name="doorHalf"/> &gt; 0 (an igloo's tunnel, W-64) the door gap is cut to the tunnel instead: every
        /// front block within doorHalf of the axis whose foot is under <paramref name="doorTop"/>, any ring. Without it, the
        /// old cut (the bottom three rings, 18-22 degrees each side; sea folk domes).
        /// </remarks>
        static void Dome(List<KitPart> parts, Vector3 at, float radius, float squash, bool door, System.Random rng, float doorHalf = 0f, float doorTop = 0f)
        {
            var rings = Mathf.Clamp(Mathf.RoundToInt(radius / 0.75f), 3, 7);
            for (int r = 0; r < rings; r++)
            {
                var phi0 = r * 90f / rings;
                var phi = (r + 0.5f) * 90f / rings;
                var ringR = radius * Mathf.Cos(phi * Mathf.Deg2Rad);
                var y = radius * squash * Mathf.Sin(phi * Mathf.Deg2Rad);
                var phi1 = (r + 1) * 90f / rings;
                var tall = radius * squash * (Mathf.Sin(phi1 * Mathf.Deg2Rad) - Mathf.Sin(phi0 * Mathf.Deg2Rad)) + 0.08f;
                // W-100: each block spans the ring's whole slant (the chord from its foot to its head) and leans along it;
                // the old blocks were only as tall as the ring's rise, which near the crown is a fraction of the slope, so
                // the upper rings stood apart with sky between them. (`tall` stays the rise for the door cut.)
                var dr = radius * (Mathf.Cos(phi0 * Mathf.Deg2Rad) - Mathf.Cos(phi1 * Mathf.Deg2Rad));
                var dy = tall - 0.08f;
                var slant = Mathf.Sqrt(dr * dr + dy * dy) + 0.14f;
                var lean = Mathf.Atan2(dr, dy) * Mathf.Rad2Deg;
                var blocks = Mathf.Max(4, Mathf.RoundToInt(2f * Mathf.PI * ringR / 1.25f));
                var offset = (r % 2) * 0.5f;                  // stagger the joints ring on ring
                // Long enough to meet round the ring's foot, its widest girth (the old length met only at mid-height).
                var footR = radius * Mathf.Cos(phi0 * Mathf.Deg2Rad);
                for (int k = 0; k < blocks; k++)
                {
                    var a = (k + offset) * 360f / blocks;
                    var len = 2f * Mathf.PI * footR / blocks + 0.06f;
                    var cutLen = 2f * Mathf.PI * ringR / blocks + 0.06f;
                    if (door && doorHalf > 0f)
                    {
                        var ar = a * Mathf.Deg2Rad;
                        if (Mathf.Cos(ar) > 0f && Mathf.Abs(ringR * Mathf.Sin(ar)) - cutLen * 0.5f < doorHalf && y - tall * 0.5f < doorTop)
                            continue;
                    }
                    else if (door && r < 3 && Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < 18f + r * 2f)
                        continue;
                    var centre = at + Turn(a, new Vector3(0f, y, ringR - 0.25f));
                    Add(parts, KitPiece.Wall, KitMaterial.Ice, centre, new Vector3(len, slant, 0.5f), a, door && r == 0, -lean);
                }
            }
            Add(parts, KitPiece.Roof, KitMaterial.Ice, at + new Vector3(0f, radius * squash - 0.1f, 0f), new Vector3(radius * 0.5f, 0.3f, radius * 0.5f), (float)rng.NextDouble() * 90f, false);
        }

        /// <summary>A cluster of two to four mountaintop details round the origin (#38), each picked from AlpineDetails by the seed.</summary>
        public static List<KitPart> AlpineCluster(uint seed, CultureTrim t, float spread)
        {
            var parts = new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed ^ 0xA1B5u) & 0x7FFFFFFF));
            for (int k = 0, n = 2 + rng.Next(3); k < n; k++)
            {
                var a = (k + (float)rng.NextDouble() * 0.6f) * Mathf.PI * 2f / n;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (k == 0 ? 0f : spread * (0.6f + 0.4f * (float)rng.NextDouble()));
                Alpine(parts, PickAlpine(rng), at, (float)rng.NextDouble() * 360f, rng);
            }
            return parts;
        }

        /// <summary>One mountaintop detail standing at <paramref name="at"/> (its base), turned by <paramref name="yaw"/> degrees.</summary>
        public static void Alpine(List<KitPart> parts, AlpineDetail kind, Vector3 at, float yaw, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            switch (kind)
            {
                case AlpineDetail.PrayerFlags:
                {
                    // Two leaning poles and a sagging cord hung with small flags in five colours.
                    var w = 4f + R() * 3f;
                    var hi = 2.6f + R() * 0.8f;
                    foreach (var side in new[] { -0.5f, 0.5f })
                        Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * w, hi * 0.5f, 0f)), new Vector3(0.12f, hi, 0.12f), yaw, false, 0f, side * -8f);
                    var flags = 6 + rng.Next(5);
                    var mats = new[] { KitMaterial.Rune, KitMaterial.Trim, KitMaterial.Moss, KitMaterial.Roof, KitMaterial.Lamp };
                    for (int f = 0; f < flags; f++)
                    {
                        var u = (f + 0.5f) / flags;
                        var sag = 4f * u * (1f - u) * 0.6f;
                        Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3((u - 0.5f) * w, hi - 0.1f - sag, 0f)), new Vector3(w / flags + 0.02f, 0.03f, 0.03f), yaw, false);
                        Add(parts, KitPiece.Trim, mats[(f + rng.Next(2)) % mats.Length], at + Turn(yaw, new Vector3((u - 0.5f) * w, hi - 0.38f - sag, 0.02f)), new Vector3(0.34f, 0.46f, 0.02f), yaw, false, 0f, (R() - 0.5f) * 14f);
                    }
                    break;
                }
                case AlpineDetail.SnowPot:
                    // Clay pots half sunk in a snow mound, a lid or two askew.
                    Add(parts, KitPiece.Trim, KitMaterial.Wall, at + new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.35f, 1.3f), yaw, false);
                    for (int k = 0, pots = 1 + rng.Next(3); k < pots; k++)
                    {
                        var p = new Vector3((R() - 0.5f) * 0.9f, 0.3f, (R() - 0.5f) * 0.7f);
                        var size = 0.4f + R() * 0.25f;
                        Add(parts, KitPiece.Trim, KitMaterial.Stone, at + Turn(yaw, p), new Vector3(size, size * 1.1f, size), yaw + R() * 45f, false);
                        if (R() < 0.6f)
                            Add(parts, KitPiece.Trim, KitMaterial.Wall, at + Turn(yaw, p + new Vector3(0f, size * 0.6f, 0f)), new Vector3(size * 0.9f, 0.08f, size * 0.9f), yaw, false, (R() - 0.5f) * 20f);
                    }
                    break;
                case AlpineDetail.RuneStone:
                {
                    // A standing stone with a glowing rune cut in its face, and now and then a smaller one fallen beside it.
                    var hi = 1.4f + R() * 0.9f;
                    Add(parts, KitPiece.Column, KitMaterial.Stone, at + new Vector3(0f, hi * 0.5f, 0f), new Vector3(0.7f, hi, 0.45f), yaw, false, (R() - 0.5f) * 6f);
                    Add(parts, KitPiece.Trim, KitMaterial.Rune, at + Turn(yaw, new Vector3(0f, hi * 0.6f, 0.24f)), new Vector3(0.12f, hi * 0.45f, 0.03f), yaw, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Rune, at + Turn(yaw, new Vector3(0f, hi * 0.66f, 0.24f)), new Vector3(0.36f, 0.08f, 0.03f), yaw, false);
                    if (R() < 0.5f)
                        Add(parts, KitPiece.Rubble, KitMaterial.Stone, at + Turn(yaw, new Vector3(0.9f, 0.18f, 0.2f)), new Vector3(0.9f, 0.36f, 0.4f), yaw + 70f, false);
                    break;
                }
                case AlpineDetail.Cairn:
                    // Flat stones stacked smaller and smaller, each a little off.
                    for (int k = 0, n = 4 + rng.Next(4); k < n; k++)
                    {
                        var s = 0.8f * (1f - k / (float)(n + 1));
                        Add(parts, KitPiece.Rubble, KitMaterial.Stone, at + new Vector3((R() - 0.5f) * 0.12f, 0.12f + k * 0.2f, (R() - 0.5f) * 0.12f), new Vector3(s, 0.2f, s * 0.8f), R() * 360f, false, (R() - 0.5f) * 8f);
                    }
                    break;
                case AlpineDetail.IceLantern:
                    // A block-ice post with a rune light in a hollow at the top.
                    Add(parts, KitPiece.Column, KitMaterial.Ice, at + new Vector3(0f, 0.6f, 0f), new Vector3(0.45f, 1.2f, 0.45f), yaw, false);
                    Add(parts, KitPiece.Window, KitMaterial.Rune, at + new Vector3(0f, 1.38f, 0f), new Vector3(0.3f, 0.36f, 0.3f), yaw, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Ice, at + new Vector3(0f, 1.62f, 0f), new Vector3(0.55f, 0.12f, 0.55f), yaw, false);
                    break;
                case AlpineDetail.Sled:
                    // A long sled on two runners, a bundle lashed on it.
                    foreach (var side in new[] { -0.4f, 0.4f })
                        Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, new Vector3(side, 0.08f, 0f)), new Vector3(0.08f, 0.12f, 2.6f), yaw, false);
                    Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, 0.22f, 0f)), new Vector3(0.95f, 0.08f, 2.2f), yaw, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3(0f, 0.48f, -0.2f)), new Vector3(0.7f, 0.45f, 1.1f), yaw, false);
                    break;
            }
        }


        /// <summary>
        /// A house of <paramref name="storeys"/> on a width x depth footprint centred on the origin, door at +Z. Decay 0
        /// is whole; towards 1 more of it has fallen. Every choice comes from the seed, so the same seed is the same house.
        /// </summary>
        public static List<KitPart> House(uint seed, CultureTrim t, float width, float depth, int storeys, float decay, List<KitPart> into = null)
        {
            var parts = into ?? new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * R();
            storeys = Mathf.Max(1, storeys);
            var h = t.Storey;
            var top = storeys * h;
            var ruined = decay > 0f;

            // Ruins: each wall keeps a share of its height, falling away from one end; one wall has nearly all gone.
            var gone = rng.Next(4);
            var keep = new float[4];
            var fromLeft = new bool[4];
            for (int s = 0; s < 4; s++)
            {
                keep[s] = !ruined ? 1f : s == gone ? Range(0.05f, 0.2f) : Range(Mathf.Lerp(1f, 0.25f, decay), 1f);
                fromLeft[s] = R() < 0.5f;
            }

            // Plinth course round the foot.
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 0.25f, 0f), new Vector3(width + 0.16f, 0.5f, depth + 0.16f), 0f, false);

            for (int s = 0; s < 4; s++)
            {
                var along = s % 2 == 0 ? width : depth;
                var out_ = (s % 2 == 0 ? depth : width) * 0.5f;
                var yaw = s * 90f;
                var bays = Mathf.Max(1, Mathf.RoundToInt(along / t.Bay));
                var bw = along / bays;
                var windowChance = s == 0 ? 0.7f : s == 2 ? 0.35f : 0.5f;
                for (int i = 0; i < bays; i++)
                {
                    var x = -along * 0.5f + (i + 0.5f) * bw;
                    var u = (i + 0.5f) / bays;
                    var standing = !ruined ? top : top * keep[s] * Mathf.Lerp(0.35f, 1f, fromLeft[s] ? u : 1f - u) * Range(0.8f, 1.05f);
                    if (ruined && standing < 0.35f)
                    {
                        Rubble(parts, rng, Turn(yaw, new Vector3(x, 0f, out_ + 1.2f)), bw * 0.6f, 3);
                        continue;
                    }
                    for (int k = 0; k < storeys; k++)
                    {
                        var y0 = k * h;
                        if (y0 >= standing)
                            break;
                        var y1 = Mathf.Min(y0 + h, standing);
                        var cut = y1 < y0 + h - 0.01f;
                        var door = k == 0 && s == 0 && i == bays / 2;
                        var window = !door && !cut && R() < windowChance && bw > 1.5f;
                        if (door)
                            DoorBay(parts, t, yaw, x, out_, y0, y1, bw, h, ruined);
                        else if (window)
                            WindowBay(parts, t, rng, yaw, x, out_, y0, y1, bw, h, k, ruined);
                        else
                            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (y0 + y1) * 0.5f, out_)), new Vector3(bw + 0.01f, y1 - y0, Thick), yaw, k == 0);
                    }
                    // Moss on a broken top; ivy down some walls.
                    if (ruined && R() < 0.55f)
                    {
                        // W-64 (#24): a draped sheet, not a 9 cm pad. Same draw as before; how far it hangs follows the stub.
                        var mossLen = bw * Range(0.6f, 1f);
                        Add(parts, KitPiece.MossCap, KitMaterial.Moss, Turn(yaw, new Vector3(x, standing, out_)), new Vector3(mossLen, Mathf.Min(0.55f, standing * 0.35f), Thick), yaw, false);
                    }
                    if (ruined && R() < 0.25f)
                    {
                        // Ivy: a few thin trailing strands of different lengths hugging the face, not one flat panel.
                        var ivy = standing * Range(0.4f, 0.9f);
                        var ix = x + Range(-0.3f, 0.3f) * bw;
                        for (int v = 0, strands = rng.Next(3, 6); v < strands; v++)
                        {
                            var len = ivy * Range(0.35f, 1f);
                            var sx = ix + (v - strands * 0.5f) * 0.13f + Range(-0.04f, 0.04f);
                            var leaf = Range(0.06f, 0.12f) + 0.06f;
                            var face = out_ + Thick * 0.5f + 0.02f + v % 2 * 0.012f;
                            // W-101 (#80): a thin stem with small leaf clumps along it, not a flat ribbon (it read as cloth).
                            // The jitter comes from a hash, not the rng, so the draws (and every later part) stay as they were.
                            Add(parts, KitPiece.Moss, KitMaterial.Moss, Turn(yaw, new Vector3(sx, standing - len * 0.5f, face)), new Vector3(0.025f, len, 0.025f), yaw, false);
                            for (int j = 0, leaves = Mathf.Min(14, Mathf.CeilToInt(len / 0.2f)); j < leaves; j++)
                            {
                                var hsh = Mathf.Abs(Mathf.Sin((v * 31 + j * 7 + x * 3.1f) * 12.9898f) * 43758.55f) % 1f;
                                var tip = 1f - (float)j / leaves * 0.5f;   // smaller towards the trailing end
                                Add(parts, KitPiece.Moss, KitMaterial.Moss,
                                    Turn(yaw, new Vector3(sx + (j % 2 == 0 ? -1f : 1f) * leaf * 0.35f, standing - (j + 0.5f) * len / leaves, face + 0.015f)),
                                    new Vector3(leaf * tip, leaf * 0.8f * tip, 0.02f), yaw + (hsh - 0.5f) * 40f, false, (hsh - 0.5f) * 30f, (j % 2 == 0 ? 1f : -1f) * (25f + hsh * 30f));
                            }
                        }
                    }
                }
                // String courses between storeys and under the eaves.
                for (int k = 1; k <= storeys; k++)
                {
                    if (ruined && k * h > top * keep[s] * 0.6f)
                        break;
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(0f, k * h - 0.08f, out_ + 0.06f)), new Vector3(along + 0.2f, 0.16f, Thick * 0.5f), yaw, false);
                }
            }

            // Quoins up each corner (dressed stones of alternating length).
            if (t.Quoins)
                for (int c = 0; c < 4; c++)
                {
                    var cx = (c & 1) == 0 ? -width * 0.5f : width * 0.5f;
                    var cz = (c & 2) == 0 ? -depth * 0.5f : depth * 0.5f;
                    var corner = !ruined ? top : top * Mathf.Min(keep[c & 1], keep[2 + (c >> 1)]) * 0.7f;
                    for (int k = 0; (k + 0.5f) * 0.45f < corner - 0.1f; k++)
                    {
                        var longX = k % 2 == 0;
                        var size = new Vector3(longX ? 0.75f : 0.4f, 0.41f, longX ? 0.4f : 0.75f);
                        Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(cx - Mathf.Sign(cx) * (size.x * 0.5f - 0.06f), (k + 0.5f) * 0.45f, cz - Mathf.Sign(cz) * (size.z * 0.5f - 0.06f)), size, 0f, false);
                    }
                }

            // Floors show through a ruin's broken walls; a whole house keeps them hidden, so skip them.
            if (ruined)
                for (int k = 1; k < storeys; k++)
                    if (R() < 0.6f)
                        Add(parts, KitPiece.Floor, KitMaterial.Wood, new Vector3(Range(-0.3f, 0.3f) * width, k * h - 0.1f, Range(-0.2f, 0.2f) * depth),
                            new Vector3(width * Range(0.35f, 0.7f), 0.2f, depth * Range(0.3f, 0.6f)), 0f, false);

            if (!ruined)
                Roof(parts, t, rng, width, depth, top);
            else
                for (int k = 0, n = rng.Next(0, 4); k < n; k++)   // a few fallen rafters, one end on the floor
                {
                    // W-101 (#80): the high end leans on the inside of a standing wall (never the door wall), no higher
                    // than that wall's lowest possible stub there and under a window sill, so it can't hang in the air
                    // or in an opening. Too low a stub: it lies on the floor. Same draws as before, so the rest of the
                    // ruin stays put.
                    var len = depth * Range(0.6f, 1f);
                    var lean = Range(0.15f, 0.42f);
                    var xr = Range(-0.3f, 0.3f);
                    var skew = Range(-0.2f, 0.2f) * 20f;
                    var s = 1 + Mathf.Min(2, (int)(R() * 3f));
                    var along = s % 2 == 0 ? width : depth;
                    var inner = (s % 2 == 0 ? depth : width) - Thick;
                    var u = xr + 0.5f;
                    var stub = top * keep[s] * Mathf.Lerp(0.35f, 1f, fromLeft[s] ? u : 1f - u) * 0.8f;
                    var rest = stub < 0.35f ? 0.11f : Mathf.Min(len * lean, stub - 0.05f, h * 0.3f);
                    len = Mathf.Min(len, Mathf.Sqrt(rest * rest + inner * inner * 0.8f));
                    var pitch = Mathf.Asin(Mathf.Clamp01(rest / len)) * Mathf.Rad2Deg;
                    var run = Mathf.Sqrt(Mathf.Max(0f, len * len - rest * rest));
                    var wallSide = (s % 2 == 0 ? depth : width) * 0.5f - Thick * 0.5f - 0.12f;
                    var mid = Turn(s * 90f, new Vector3(xr * along, rest * 0.5f + 0.11f, wallSide - run * 0.5f));
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, mid, new Vector3(0.22f, 0.22f, len), s * 90f + 180f + skew, false, pitch);
                }

            // W-100: a balcony and the landing at a stair's top each get a door in the wall behind them, so neither
            // leads nowhere (no extra draws: the island's other rolls stay where they were).
            if (storeys >= 2 && R() < (ruined ? t.BalconyChance * 0.5f : t.BalconyChance))
            {
                var at = new Vector3(Range(-0.15f, 0.15f) * width, h, depth * 0.5f);
                Balcony(parts, t, at, 0f, Mathf.Min(width * 0.5f, 3.6f), ruined);
                UpperDoor(parts, at.x, depth * 0.5f, 0f, h, ruined);
            }
            if (storeys >= 2 && R() < t.StairChance)
            {
                var steps = Mathf.Max(2, Mathf.CeilToInt(h / 0.28f));
                Stair(parts, new Vector3(width * 0.5f + 0.55f, 0f, -depth * 0.35f), 0f, h, 1f);
                // The landing's centre (Stair: steps * 0.15 + 0.5 past its middle), as a distance along the +X wall
                // (yaw 90 turns +X along a wall to -Z).
                UpperDoor(parts, depth * 0.35f - steps * 0.15f - 0.5f, width * 0.5f, 90f, h, ruined);
            }
            if (R() < t.TowerChance)
                Tower(parts, t, rng, new Vector3(-width * 0.5f, 0f, -depth * 0.5f), Mathf.Min(width, depth) * 0.42f, top + h * Range(1f, 1.8f), ruined ? keep[gone == 1 ? 3 : 1] : 1f);
            if (ruined)
                for (int k = 0, n = 4 + rng.Next(6); k < n; k++)   // rubble round the foot and inside
                    Rubble(parts, rng, new Vector3(Range(-0.6f, 0.6f) * width, 0f, Range(-0.6f, 0.6f) * depth), 0.6f, 2);
            return parts;
        }

        /// <summary>A gate: two piers and a lintel (with a capping course), or an arch of voussoirs when arched.</summary>
        public static void Gate(List<KitPart> parts, CultureTrim t, Vector3 at, float yaw, float width, float height, bool arched)
        {
            const float pier = 0.9f;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Column, KitMaterial.Trim, at + Turn(yaw, new Vector3(side * (width + pier) * 0.5f, height * 0.5f, 0f)), new Vector3(pier, height, pier), yaw, true);
            if (!arched)
            {
                Add(parts, KitPiece.Arch, KitMaterial.Trim, at + Turn(yaw, new Vector3(0f, height + 0.3f, 0f)), new Vector3(width + pier * 2.4f, 0.6f, pier * 1.1f), yaw, false);
                if (t.Crenels)
                    for (int k = -2; k <= 2; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(yaw, new Vector3(k * (width + pier * 2f) / 5f, height + 0.85f, 0f)), new Vector3(0.5f, 0.5f, pier), yaw, false);
                return;
            }
            const int stones = 9;
            for (int k = 0; k < stones; k++)
            {
                var a = Mathf.PI * (k + 0.5f) / stones;
                var p = new Vector3(Mathf.Cos(a) * width * 0.55f, height + Mathf.Sin(a) * width * 0.5f, 0f);
                Add(parts, KitPiece.Arch, KitMaterial.Trim, at + Turn(yaw, p), new Vector3(width * 0.35f, 0.55f, pier), yaw, false, 0f, 90f - a * Mathf.Rad2Deg);
            }
        }

        /// <summary>
        /// A deck on posts (docks-ready: the posts run down <paramref name="deckHeight"/> to the bed or ground), planks
        /// across it, a rail along both long sides.
        /// </summary>
        public static void Platform(List<KitPart> parts, Vector3 at, float yaw, float width, float length, float deckHeight, bool rails)
        {
            var planks = Mathf.Max(2, Mathf.RoundToInt(length / 0.35f));
            for (int k = 0; k < planks; k++)
                Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, deckHeight - 0.06f, -length * 0.5f + (k + 0.5f) * length / planks)),
                    new Vector3(width, 0.12f, length / planks * 0.92f), yaw, false);
            for (int k = 0, posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1); k < posts; k++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var z = -length * 0.5f + k * length / (posts - 1);
                    Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, (deckHeight - 0.1f) * 0.5f, z)), new Vector3(0.24f, deckHeight, 0.24f), yaw, false);
                    if (rails)
                        Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, deckHeight + 0.5f, z)), new Vector3(0.12f, 1f, 0.12f), yaw, false);
                }
            if (rails)
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, deckHeight + 0.95f, 0f)), new Vector3(0.1f, 0.1f, length), yaw, false);
        }

        /// <summary>A straight outside stair along +Z rising <paramref name="rise"/>, its treads solid blocks to the ground.</summary>
        public static void Stair(List<KitPart> parts, Vector3 at, float yaw, float rise, float width)
        {
            var steps = Mathf.Max(2, Mathf.CeilToInt(rise / 0.28f));
            var run = 0.3f;
            for (int k = 0; k < steps; k++)
            {
                var y = (k + 1) * rise / steps;
                Add(parts, KitPiece.Stair, KitMaterial.Stone, at + Turn(yaw, new Vector3(0f, y * 0.5f, (k - steps * 0.5f) * run)), new Vector3(width, y, run), yaw, false);
            }
            Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, rise - 0.08f, steps * 0.5f * run + 0.5f)), new Vector3(width, 0.16f, 1f), yaw, false);
        }

        /// <summary>A balcony: a slab on brackets in front of a wall, posts and a top rail.</summary>
        public static void Balcony(List<KitPart> parts, CultureTrim t, Vector3 at, float yaw, float width, bool ruined)
        {
            const float deep = 1.1f;
            Add(parts, KitPiece.Balcony, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, -0.08f, deep * 0.5f)), new Vector3(width, 0.16f, deep), yaw, false);
            foreach (var side in new[] { -0.4f, 0.4f })
                Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width, -0.45f, deep * 0.35f)), new Vector3(0.16f, 0.16f, deep * 0.9f), yaw, false, -35f);
            var posts = Mathf.Max(3, Mathf.RoundToInt(width / 0.45f));
            for (int k = 0; k < posts; k++)
            {
                if (ruined && k % 3 == 1)
                    continue;
                Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(-width * 0.5f + (k + 0.5f) * width / posts, 0.45f, deep - 0.06f)), new Vector3(0.08f, 0.9f, 0.08f), yaw, false);
            }
            if (!ruined)
                Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, 0.92f, deep - 0.06f)), new Vector3(width, 0.1f, 0.12f), yaw, false);
        }

        /// <summary>A square corner tower with slit windows, a cornice and a pyramid of tiers or a crenellated top.</summary>
        public static void Tower(List<KitPart> parts, CultureTrim t, System.Random rng, Vector3 at, float side, float height, float keep)
        {
            var standing = height * Mathf.Clamp01(keep);
            if (standing < 0.5f)
                return;
            Add(parts, KitPiece.Wall, KitMaterial.Wall, at + new Vector3(0f, standing * 0.5f, 0f), new Vector3(side, standing, side), 0f, true);
            for (float y = t.Storey * 0.6f; y < standing - 1f; y += t.Storey)
                for (int s = 0; s < 4; s++)
                    Add(parts, KitPiece.Window, KitMaterial.Opening, at + Turn(s * 90f, new Vector3(0f, y, side * 0.5f + 0.01f)), new Vector3(0.28f, 0.9f, 0.06f), s * 90f, false);
            if (keep < 1f)
                return;
            Add(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, height + 0.15f, 0f), new Vector3(side + 0.4f, 0.3f, side + 0.4f), 0f, false);
            if (t.Crenels || t.Roof == KitRoof.Flat)
            {
                for (int s = 0; s < 4; s++)
                    for (int k = -1; k <= 1; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(s * 90f, new Vector3(k * side / 3f, height + 0.6f, side * 0.5f + 0.1f)), new Vector3(side / 6f, 0.6f, 0.4f), s * 90f, false);
                return;
            }
            var tiers = 5;
            for (int k = 0; k < tiers; k++)
            {
                var w = (side + 0.6f) * (1f - k / (float)tiers);
                Add(parts, KitPiece.Roof, KitMaterial.Roof, at + new Vector3(0f, height + 0.3f + (k + 0.5f) * side * 0.32f, 0f), new Vector3(w, side * 0.32f, w), 0f, false);
            }
        }

        static void Roof(List<KitPart> parts, CultureTrim t, System.Random rng, float width, float depth, float top)
        {
            const float eave = 0.45f, slab = 0.2f;
            switch (t.Roof)
            {
                case KitRoof.Flat:
                    Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + 0.1f, 0f), new Vector3(width, 0.2f, depth), 0f, false);
                    for (int s = 0; s < 4; s++)
                    {
                        var along = s % 2 == 0 ? width : depth;
                        var out_ = (s % 2 == 0 ? depth : width) * 0.5f;
                        Add(parts, KitPiece.Wall, KitMaterial.Trim, Turn(s * 90f, new Vector3(0f, top + 0.35f, out_)), new Vector3(along + Thick, 0.7f, Thick), s * 90f, false);
                        if (t.Crenels)
                            for (int k = 0, n = Mathf.Max(2, Mathf.RoundToInt(along / 1.2f)); k < n; k++)
                                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(s * 90f, new Vector3(-along * 0.5f + (k + 0.5f) * along / n, top + 0.95f, out_)), new Vector3(along / n * 0.5f, 0.5f, Thick + 0.04f), s * 90f, false);
                    }
                    break;
                case KitRoof.Tiered:
                    for (int k = 0; k < 3; k++)
                    {
                        var shrink = 1f - k * 0.3f;
                        Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + 0.18f + k * 0.85f, 0f), new Vector3(width * shrink + eave * 4f * shrink, 0.32f, depth * shrink + eave * 4f * shrink), 0f, false);
                        Add(parts, KitPiece.Wall, KitMaterial.Trim, new Vector3(0f, top + 0.6f + k * 0.85f, 0f), new Vector3(width * shrink * 0.8f, 0.55f, depth * shrink * 0.8f), 0f, false);
                    }
                    Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(0f, top + 3.1f, 0f), new Vector3(0.18f, 1.2f, 0.18f), 0f, false);
                    break;
                default:
                {
                    // Ridge along the width; slabs pitched down to the front and back, gable ends stepped up to the ridge.
                    var pitch = t.Pitch * Mathf.Deg2Rad;
                    var run = depth * 0.5f + eave;
                    var rise = (depth * 0.5f) * Mathf.Tan(pitch);
                    var length = run / Mathf.Cos(pitch);
                    foreach (var side in new[] { -1f, 1f })
                        Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + rise * 0.5f - eave * Mathf.Tan(pitch) * 0.5f + slab, side * run * 0.5f),
                            new Vector3(width + eave * 2f, slab, length), 0f, false, side * t.Pitch);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, top + rise + slab * 1.2f, 0f), new Vector3(width + eave * 2.2f, 0.22f, 0.3f), 0f, false);
                    const int steps = 5;
                    foreach (var side in new[] { -1f, 1f })
                        for (int k = 0; k < steps; k++)
                        {
                            var w = depth * (1f - (k + 0.5f) / steps);
                            Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(side * (width * 0.5f - Thick * 0.5f), top + (k + 0.5f) * rise / steps, 0f), new Vector3(Thick, rise / steps + 0.01f, w), 0f, false);
                        }
                    // A chimney through one slope.
                    var cx = (rng.NextDouble() < 0.5 ? -1f : 1f) * width * 0.3f;
                    Add(parts, KitPiece.Chimney, KitMaterial.Stone, new Vector3(cx, top + rise * 0.75f + 0.6f, -depth * 0.15f), new Vector3(0.7f, rise * 1.5f + 1.2f, 0.7f), 0f, false);
                    break;
                }
            }
        }

        static void DoorBay(List<KitPart> parts, CultureTrim t, float yaw, float x, float out_, float y0, float y1, float bw, float h, bool ruined)
        {
            var dw = Mathf.Min(1.3f, bw * 0.55f);
            var dh = Mathf.Min(h * 0.78f, 2.3f);
            var side = (bw - dw) * 0.5f;
            foreach (var s in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x + s * (dw + side) * 0.5f, (y0 + y1) * 0.5f, out_)), new Vector3(side + 0.01f, y1 - y0, Thick), yaw, true);
            if (y1 > dh + 0.05f)
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (dh + y1) * 0.5f, out_)), new Vector3(dw + 0.02f, y1 - dh, Thick), yaw, false);
            // W-101 (#80): a ruin's lintel only where the wall still stands up to it (it hung in the air over a broken bay).
            if (!ruined || y1 > dh + 0.24f)
                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, dh + 0.12f, out_ + 0.1f)), new Vector3(dw + 0.4f, 0.24f, Thick * 0.6f), yaw, false);
            if (!ruined)
                Add(parts, KitPiece.Door, KitMaterial.Wood, Turn(yaw, new Vector3(x, dh * 0.5f, out_ - 0.05f)), new Vector3(dw, dh, 0.12f), yaw, false);
            // Step. W-113 (item 10): a ruin's plinth is solid, so its step is half the plinth (0.25 m) and a body climbs both.
            var stepH = ruined ? 0.25f : 0.16f;
            Add(parts, KitPiece.Trim, KitMaterial.Stone, Turn(yaw, new Vector3(x, stepH * 0.5f, out_ + 0.45f)), new Vector3(dw + 0.5f, stepH, 0.6f), yaw, false);
        }

        /// <summary>
        /// W-100: a door on an upper floor (behind a balcony or a stair's landing): a leaf proud of the wall with jambs,
        /// a lintel and a sill. Any window, shutter or flower box already in that spot is taken out first. x runs along
        /// the wall, out_ is the wall's line, both in the wall's frame (yaw, as the house's walls).
        /// </summary>
        static void UpperDoor(List<KitPart> parts, float x, float out_, float yaw, float floor, bool ruined)
        {
            const float dw = 1.0f, dh = 2.1f;
            var face = out_ + Thick * 0.5f;
            var inv = Quaternion.Euler(0f, -yaw, 0f);
            parts.RemoveAll(p =>
            {
                if (p.Piece != KitPiece.Window && p.Piece != KitPiece.Trim || p.Size.x > 2f)   // (string courses stay)
                    return false;
                var l = inv * p.Centre;
                return Mathf.Abs(l.x - x) < dw * 0.5f + 0.5f && l.y > floor - 0.2f && l.y < floor + dh + 0.35f && Mathf.Abs(l.z - out_) < 0.6f;
            });
            if (!ruined)
                Add(parts, KitPiece.Door, KitMaterial.Wood, Turn(yaw, new Vector3(x, floor + dh * 0.5f, face + 0.05f)), new Vector3(dw, dh, 0.1f), yaw, false);
            else   // a ruin's doorway: a dark opening, the leaf long gone
                Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(yaw, new Vector3(x, floor + dh * 0.5f, face + 0.02f)), new Vector3(dw, dh, 0.04f), yaw, false);
            foreach (var s in new[] { -1f, 1f })
                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x + s * (dw * 0.5f + 0.08f), floor + dh * 0.5f, face + 0.06f)), new Vector3(0.16f, dh, 0.12f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, floor + dh + 0.1f, face + 0.07f)), new Vector3(dw + 0.4f, 0.2f, 0.14f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Stone, Turn(yaw, new Vector3(x, floor + 0.04f, face + 0.08f)), new Vector3(dw + 0.2f, 0.08f, 0.16f), yaw, false);
        }

        static void WindowBay(List<KitPart> parts, CultureTrim t, System.Random rng, float yaw, float x, float out_, float y0, float y1, float bw, float h, int storey, bool ruined)
        {
            var ww = Mathf.Min(0.95f, bw * 0.42f);
            var wh = h * (storey == 0 ? 0.4f : 0.45f);
            var sill = y0 + h * 0.34f;
            var head = sill + wh;
            var side = (bw - ww) * 0.5f;
            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (y0 + sill) * 0.5f, out_)), new Vector3(bw + 0.01f, sill - y0, Thick), yaw, storey == 0);
            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (head + y1) * 0.5f, out_)), new Vector3(bw + 0.01f, y1 - head, Thick), yaw, false);
            foreach (var s in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x + s * (ww + side) * 0.5f, (sill + head) * 0.5f, out_)), new Vector3(side + 0.01f, wh, Thick), yaw, false);
            // The opening: dark glass set back (a ruin's is a real hole you can see through), a sill and lintel proud of
            // the wall, sometimes shutters.
            if (!ruined)
                Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(yaw, new Vector3(x, (sill + head) * 0.5f, out_ - 0.08f)), new Vector3(ww, wh, 0.1f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, sill - 0.05f, out_ + 0.08f)), new Vector3(ww + 0.24f, 0.1f, Thick * 0.5f + 0.12f), yaw, false);
            if (!ruined || y1 > head + 0.16f)   // W-101 (#80): likewise a ruin's window head
                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, head + 0.08f, out_ + 0.05f)), new Vector3(ww + 0.3f, 0.16f, Thick * 0.5f + 0.06f), yaw, false);
            if (t.Shutters && !ruined && rng.NextDouble() < 0.5)
                foreach (var s in new[] { -1f, 1f })
                    Add(parts, KitPiece.Window, KitMaterial.Wood, Turn(yaw, new Vector3(x + s * (ww * 0.75f + 0.03f), (sill + head) * 0.5f, out_ + Thick * 0.5f + 0.04f)), new Vector3(ww * 0.48f, wh, 0.06f), yaw, false);
            if (!ruined && storey == 0 && rng.NextDouble() < 0.3)   // a flower box
                Add(parts, KitPiece.Trim, KitMaterial.Moss, Turn(yaw, new Vector3(x, sill + 0.08f, out_ + Thick * 0.5f + 0.18f)), new Vector3(ww, 0.22f, 0.3f), yaw, false);
        }

        static void Rubble(List<KitPart> parts, System.Random rng, Vector3 at, float spread, int stones)
        {
            for (int k = 0; k < stones; k++)
            {
                var size = 0.2f + 0.35f * (float)rng.NextDouble();
                var p = at + new Vector3(((float)rng.NextDouble() - 0.5f) * spread * 2f, size * 0.35f, ((float)rng.NextDouble() - 0.5f) * spread * 2f);
                Add(parts, KitPiece.Rubble, KitMaterial.Stone, p, new Vector3(size * 1.3f, size * 0.8f, size), (float)rng.NextDouble() * 360f, false, ((float)rng.NextDouble() - 0.5f) * 30f);
            }
        }

        /// <summary>
        /// W-128: the inn, built from the kit (it was a flat box with a hand-made roof): a two-storey House whose walls
        /// stand exactly on the old box (faces at x = ±<paramref name="half"/>, z = ±<paramref name="front"/>, eaves at
        /// <paramref name="wall"/>), five bays wide so the door bay sits in the middle of the front, in the folk's trim.
        /// Out front: a plank porch <paramref name="porch"/> deep under a lean-to roof that starts just under the upper
        /// windows' sills, on two posts, with a lantern by the door, barrels and a bench. The kit's own door leaf, lintel
        /// and step are left out (the caller draws the hinged door the folk use); <paramref name="sign"/> is the centre of
        /// the inn sign, hung on rods just under the porch roof's front edge so it rises with the roof.
        /// </summary>
        public static List<KitPart> Inn(uint seed, CultureTrim t, float half, float front, float wall, float porch, out Vector3 sign)
        {
            t.Storey = wall * 0.5f;
            t.Bay = (half * 2f - Thick) / 5f;
            t.TowerChance = t.BalconyChance = t.StairChance = 0f;
            var width = half * 2f - Thick;
            var depth = front * 2f - Thick;
            var parts = House(seed, t, width, depth, 2, 0f);
            var h = t.Storey;
            var out_ = depth * 0.5f;
            // The door bay's leaf, lintel and step: the caller's door (hinged, with its own frame and step) takes their place.
            parts.RemoveAll(p => p.Centre.z > out_ - 0.2f && Mathf.Abs(p.Centre.x) < 0.3f && p.Centre.y < h * 0.9f &&
                                 (p.Piece == KitPiece.Door || p.Piece == KitPiece.Trim && p.Size.x < 2.5f));
            // A flat or tiered roof has no chimney of its own: one stack at the back for the hearth.
            if (!parts.Exists(p => p.Piece == KitPiece.Chimney))
                Add(parts, KitPiece.Chimney, KitMaterial.Stone, new Vector3(half * 0.55f, wall + 0.6f, -front * 0.35f), new Vector3(0.7f, 2f, 0.7f), 0f, false);

            // Porch deck, flush with the plinth, a little narrower than the front, with a nosing along its edge.
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(0f, 0.15f, front + porch * 0.5f), new Vector3(half * 1.8f, 0.3f, porch), 0f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, 0.27f, front + porch - 0.06f), new Vector3(half * 1.8f + 0.04f, 0.08f, 0.14f), 0f, false);
            // Lean-to roof: its top meets the wall under the upper sills' trim (sill at 1.34 h) and falls 0.5 m to the front.
            const float slab = 0.16f, drop = 0.5f;
            var y0 = h * 1.28f;
            var run = porch + 0.3f;
            var length = Mathf.Sqrt(run * run + drop * drop);
            var pitch = Mathf.Atan2(drop, run) * Mathf.Rad2Deg;
            var roofW = half * 1.9f;
            Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, y0 - slab * 0.5f - drop * 0.5f, front + run * 0.5f), new Vector3(roofW, slab, length), 0f, false, pitch);
            var edge = y0 - drop - slab;   // the roof's underside at its front edge
            var edgeZ = front + run;
            // A fascia board along the front edge, a ledger strip where the roof meets the wall.
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, edge + 0.02f, edgeZ - 0.02f), new Vector3(roofW + 0.06f, 0.22f, 0.08f), 0f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(0f, y0 - 0.3f, front + 0.06f), new Vector3(roofW, 0.16f, 0.12f), 0f, false);
            // Two posts at the front corners on stone pads, a beam across under the roof, a knee brace from each post.
            var postZ = front + porch - 0.14f;
            var beamY = edge + (edgeZ - postZ) / run * drop - 0.1f;
            foreach (var x in new[] { -0.85f * half, 0.85f * half })
            {
                var inward = -Mathf.Sign(x);
                Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(x, (0.3f + beamY) * 0.5f, postZ), new Vector3(0.22f, beamY - 0.3f, 0.22f), 0f, false);
                Add(parts, KitPiece.Trim, KitMaterial.Stone, new Vector3(x, 0.36f, postZ), new Vector3(0.34f, 0.12f, 0.34f), 0f, false);
                Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(x + inward * 0.28f, beamY - 0.36f, postZ), new Vector3(0.1f, 0.75f, 0.1f), 0f, false, 0f, inward * 45f);
                // A short rail from the post in toward the door, leaving the middle open.
                Add(parts, KitPiece.Rail, KitMaterial.Wood, new Vector3(x + inward * half * 0.22f, 1.0f, postZ), new Vector3(half * 0.44f, 0.08f, 0.08f), 0f, false);
                Add(parts, KitPiece.Rail, KitMaterial.Wood, new Vector3(x + inward * half * 0.22f, 0.65f, postZ), new Vector3(half * 0.44f, 0.06f, 0.06f), 0f, false);
            }
            Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(0f, beamY + 0.08f, postZ), new Vector3(half * 1.75f, 0.18f, 0.18f), 0f, false);
            // A lantern on a bracket beside the door.
            Add(parts, KitPiece.Trim, KitMaterial.Wood, new Vector3(1.05f, 2.25f, front + 0.2f), new Vector3(0.06f, 0.06f, 0.4f), 0f, false);
            Add(parts, KitPiece.Window, KitMaterial.Lamp, new Vector3(1.05f, 2.02f, front + 0.36f), new Vector3(0.22f, 0.3f, 0.22f), 0f, false);
            // Barrels on one side of the door, a bench on the other (their own rng, so the house's draws are untouched).
            var rng = new System.Random((int)(Scramble(seed ^ 0x1A2B3C4Du) & 0x7FFFFFFF));
            for (int k = 0; k < 2; k++)
                Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(-half * (0.42f + k * 0.17f), 0.3f + 0.42f, front + 0.45f + (float)rng.NextDouble() * 0.2f),
                    new Vector3(0.6f, 0.84f, 0.6f), (float)rng.NextDouble() * 90f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(-half * 0.42f, 0.3f + 0.6f, front + 0.5f), new Vector3(0.64f, 0.06f, 0.64f), 20f, false);   // a hoop
            Add(parts, KitPiece.Platform, KitMaterial.Wood, new Vector3(half * 0.45f, 0.3f + 0.45f, front + 0.4f), new Vector3(1.6f, 0.08f, 0.42f), 0f, false);
            foreach (var e in new[] { -0.7f, 0.7f })
                Add(parts, KitPiece.Column, KitMaterial.Wood, new Vector3(half * 0.45f + e, 0.3f + 0.21f, front + 0.4f), new Vector3(0.08f, 0.42f, 0.36f), 0f, false);

            // The sign: hung on two rods from the roof's front edge, its top 0.25 m under the fascia, so it rises with the roof.
            const float signH = 0.55f;
            sign = new Vector3(half * 0.55f, edge - 0.25f - signH * 0.5f, edgeZ + 0.06f);
            foreach (var e in new[] { -0.32f, 0.32f })
                Add(parts, KitPiece.Rail, KitMaterial.Wood, new Vector3(sign.x + e, (edge + sign.y + signH * 0.5f) * 0.5f, sign.z), new Vector3(0.03f, edge - sign.y - signH * 0.5f + 0.04f, 0.03f), 0f, false);
            Add(parts, KitPiece.Rail, KitMaterial.Wood, new Vector3(sign.x, sign.y + signH * 0.5f + 0.03f, sign.z), new Vector3(1.0f, 0.05f, 0.05f), 0f, false);
            return parts;
        }

        // --------------------------------------------------------------------------------------- marsh folk (W-122)

        /// <summary>W-122: a marsh folk hut's deck height over the pool (m); the walks sit a little under it.</summary>
        public const float MarshDeck = 1.0f;
        /// <summary>W-122: how far the huts stand from the village's middle (ring 0.5 of a 40 m village).</summary>
        public const float MarshRing = 20f;
        /// <summary>W-122: half the side of the smoke hut's square deck (m).</summary>
        public const float MarshHub = 5f;
        /// <summary>W-122: how far past the water line a marsh folk post runs down into the mud (m).</summary>
        public const float MarshPile = 3f;

        /// <summary>
        /// W-122: a marsh folk reed hut, one low storey of House under a steep thatch, on a plank deck
        /// <see cref="MarshDeck"/> over the water (the origin is the water line), its posts run on down as piles. From the
        /// door (+Z) a walk of boardwalk pieces (the fishing villages' W-119 piece) runs on to the smoke hut's deck, which
        /// starts <paramref name="reach"/> - <see cref="MarshHub"/> m in front of the hut's middle.
        /// </summary>
        public static List<KitPart> MarshHut(uint seed, CultureTrim t, float width, float depth, float reach, int theme = 0)
        {
            var parts = House(seed, t, width, depth, 1, 0f);
            // Reed huts keep their fire in the smoke hut: no stone chimney.
            parts.RemoveAll(p => p.Piece == KitPiece.Chimney);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.Centre.y += MarshDeck;
                parts[i] = p;
            }
            Platform(parts, Vector3.zero, 0f, width + 1.6f, depth + 1.6f, MarshDeck, false);
            var doorX = DoorX(t, width);
            var start = depth * 0.5f + 0.8f - 0.3f;
            var walk = reach - MarshHub + 0.5f - start;
            MarshPlanks(parts, seed ^ 0x3A2Fu, t, new Vector3(doorX, 0f, start), 0f, walk, 0.9f);
            // W-122 row 2: each hut dressed a little differently: moss on the thatch, gourds under the eaves, lily pads
            // round its piles, and its trade (eel traps, a reed frame or a carved post) by its variant.
            var rng = new System.Random((int)(Scramble(seed ^ 0x6A11u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            RoofMoss(parts, rng, 1 + rng.Next(2));
            Gourds(parts, rng, width, depth, 1 + rng.Next(3));
            for (int k = 0, n = 2 + rng.Next(2); k < n; k++)
            {
                var side = R() < 0.5f ? -1f : 1f;
                LilyPads(parts, rng, new Vector3(side * (width * 0.5f + 1.2f + R()), 0f, (R() - 0.5f) * depth));
            }
            var trapSide = R() < 0.5f ? -1f : 1f;
            if (theme == 0 || R() < 0.3f)
                EelTrap(parts, new Vector3(doorX + trapSide * 0.95f, 0f, start + walk * (0.3f + 0.4f * R())), trapSide * 90f, 0.9f);
            if (theme == 1 || R() < 0.3f)
                DryingFrame(parts, rng, new Vector3(-trapSide * (width * 0.5f + 0.4f), 0f, 0f), 90f, MarshDeck, Mathf.Min(depth - 0.4f, 2f));
            if (theme == 2)
                Totem(parts, rng, new Vector3(trapSide * (width * 0.5f + 0.6f), 0f, depth * 0.5f + 0.6f), 0f, 2.6f + R() * 0.6f, MarshPaint(theme));
            Piles(parts);
            return parts;
        }

        /// <summary>
        /// W-122: the marsh folk's smoke hut: a squat House under a tall steep thatch on a square plank deck
        /// 2 x <see cref="MarshHub"/> across, its door +Z, a clay smoke stack through the ridge (FloraGenerator puts the
        /// chimney's smoke on it).
        /// </summary>
        public static List<KitPart> SmokeHut(uint seed, CultureTrim t, float width, int theme = 0)
        {
            t.Pitch = 58f;
            var parts = House(seed, t, width, width, 1, 0f);
            var ridge = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.Centre.y += MarshDeck;
                parts[i] = p;
                if (p.Piece == KitPiece.Trim)
                    ridge = Mathf.Max(ridge, p.Centre.y);
            }
            // The steep roof's own chimney runs far past this tall ridge: move it to the ridge's middle (over the village's
            // centre, where its landmark plume rises) and cut it to stand 1 m clear of the ridge, with a cap.
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Piece != KitPiece.Chimney)
                    continue;
                var bottom = p.Centre.y - p.Size.y * 0.5f;
                var stackTop = ridge + 1f;
                p.Size.y = stackTop - bottom;
                p.Centre = new Vector3(0f, (bottom + stackTop) * 0.5f, 0f);
                parts[i] = p;
                Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(p.Centre.x, stackTop + 0.06f, p.Centre.z), new Vector3(0.9f, 0.12f, 0.9f), 0f, false);
                break;
            }
            Platform(parts, Vector3.zero, 0f, MarshHub * 2f, MarshHub * 2f, MarshDeck, false);
            // W-122 row 2: the village's theme (one per village, PerVillage) paints the carved posts flanking the door and
            // picks what crowds the deck: eel traps off its edges, reed frames along its sides, or more posts.
            var rng = new System.Random((int)(Scramble(seed ^ 0x5A0Bu) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var paint = MarshPaint(theme);
            var near = width * 0.5f + 0.9f;
            foreach (var sx in new[] { -1f, 1f })
                Totem(parts, rng, new Vector3(sx * 1.6f, 0f, near), 0f, 3f + R() * 0.5f, paint);
            if (theme == 2)
                foreach (var sx in new[] { -1f, 1f })
                    Totem(parts, rng, new Vector3(sx * (near - 0.3f), 0f, -(near - 0.3f)), sx * 135f, 2.5f + R() * 0.6f, paint);
            if (theme == 1)
                foreach (var sx in new[] { -1f, 1f })
                    DryingFrame(parts, rng, new Vector3(sx * (width * 0.5f + 1.3f), 0f, -0.4f), 90f, MarshDeck, 2.2f);
            for (int k = 0, n = theme == 0 ? 4 : 1; k < n; k++)
            {
                // Square to a deck edge (a corner yaw put the trap back under the planks), at a random spot along it.
                var yaw = k * 90f + rng.Next(4) * 90f;
                EelTrap(parts, Turn(yaw, new Vector3((R() - 0.5f) * 6f, 0f, MarshHub + 0.05f)), yaw, MarshDeck);
            }
            RoofMoss(parts, rng, 1 + rng.Next(2));
            Gourds(parts, rng, width, width, 2 + rng.Next(3));
            for (int k = 0; k < 4; k++)
                LilyPads(parts, rng, Turn(k * 90f + R() * 60f, new Vector3(0f, 0f, MarshHub + 1f + R() * 1.5f)));
            Hatchery(parts, seed);
            Piles(parts);
            return parts;
        }

        /// <summary>
        /// W-122: a plank walk of <paramref name="length"/> m centred on the origin, along X when <paramref name="across"/>
        /// (hut to hut, the prop facing the middle), else along -Z from z = 2.8 out to the bank, where it ends in a step
        /// down onto the mud.
        /// </summary>
        public static List<KitPart> MarshCross(uint seed, CultureTrim t, float length, bool across, int theme = 0)
        {
            var parts = new List<KitPart>();
            // W-122 row 2: dressed by the village's theme (PerVillage): eel traps, reed frames standing in the shallows, or
            // carved posts beside the planks; lily pads either side and a welcome post where the walk meets the bank.
            var rng = new System.Random((int)(Scramble(seed ^ 0x7C05u) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var paint = MarshPaint(theme);
            if (across)
            {
                MarshPlanks(parts, seed, t, new Vector3(-length * 0.5f, 0f, 0f), 90f, length, 0.86f);
                for (int k = 0; k < 2; k++)
                {
                    var side = k == 0 ? -1f : 1f;
                    var x = (R() - 0.5f) * length * 0.6f;
                    if (theme == 0)
                        EelTrap(parts, new Vector3(x, 0f, side * 0.95f), side < 0f ? 180f : 0f, 0.86f);
                    else if (theme == 1)
                        DryingFrame(parts, rng, new Vector3(x, 0f, side * 1.6f), 0f, -0.2f, 2f);
                    else if (k == 0)
                        Totem(parts, rng, new Vector3(x, 0f, side * 1.15f), side < 0f ? 0f : 180f, 2.8f + R() * 0.6f, paint);
                    LilyPads(parts, rng, new Vector3((R() - 0.5f) * length * 0.8f, 0f, side * (1.6f + R())));
                }
            }
            else
            {
                MarshPlanks(parts, seed, t, new Vector3(0f, 0f, 2.8f), 180f, length, 0.82f);
                var end = 2.8f - length - 0.65f - Mathf.CeilToInt(0.82f / 0.28f) * 0.15f;
                Stair(parts, new Vector3(0f, 0f, end), 0f, 0.82f, 1.4f);
                Totem(parts, rng, new Vector3(1.25f, 0f, 2.8f - length + 0.4f), 180f, 3.2f + R() * 0.4f, paint);
                if (theme == 2)
                    Totem(parts, rng, new Vector3(-1.25f, 0f, 2.8f - length + 0.4f), 180f, 3f + R() * 0.4f, paint);
                for (int k = 0; k < 3; k++)
                    LilyPads(parts, rng, new Vector3((k == 1 ? 1f : -1f) * (1.6f + R()), 0f, 2.8f - length * (0.2f + 0.3f * k)));
            }
            Piles(parts);
            return parts;
        }

        /// <summary>W-122 row 2: the paint a village's carved posts take by its theme: ochre, bone or lichen green.</summary>
        static KitMaterial MarshPaint(int theme) => theme == 0 ? KitMaterial.Copper : theme == 1 ? KitMaterial.Net : KitMaterial.Moss;

        /// <summary>
        /// W-122 row 2: a wicker eel trap, a tapering cone hung mouth up off a deck's edge at <paramref name="at"/> to
        /// drain (one floating at the water line was lost in the murk), on a short cord from the planks at
        /// <paramref name="deck"/>; <paramref name="yaw"/> turns it so its rim hoops face along the edge.
        /// </summary>
        static void EelTrap(List<KitPart> parts, Vector3 at, float yaw, float deck)
        {
            var top = deck - 0.2f;
            for (int k = 0; k < 3; k++)
            {
                var s = 0.36f - k * 0.1f;
                var y = top - 0.17f - k * 0.3f;
                Add(parts, KitPiece.Trim, KitMaterial.Roof, at + Turn(yaw, new Vector3(0f, y, 0.25f)), new Vector3(s, s, 0.34f), yaw, false, 90f, 45f);
                Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, y + 0.15f, 0.25f)), new Vector3(s + 0.04f, s + 0.04f, 0.04f), yaw, false, 90f, 45f);
            }
            Add(parts, KitPiece.Trim, KitMaterial.Opening, at + Turn(yaw, new Vector3(0f, top + 0.01f, 0.25f)), new Vector3(0.3f, 0.3f, 0.02f), yaw, false, 90f, 45f);
            Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3(0f, top + 0.1f, 0.25f)), new Vector3(0.025f, 0.2f, 0.025f), yaw, false);
        }

        /// <summary>
        /// W-122 row 2: a reed drying frame, A-legs and a crossbar <paramref name="length"/> m long along local X, standing
        /// at <paramref name="baseY"/> (a deck, or a little under the water line in the shallows), bundles of reeds tied
        /// head down from the bar.
        /// </summary>
        static void DryingFrame(List<KitPart> parts, System.Random rng, Vector3 at, float yaw, float baseY, float length)
        {
            float R() => (float)rng.NextDouble();
            var bar = baseY + 1.75f;
            foreach (var sx in new[] { -0.5f, 0.5f })
                foreach (var lean in new[] { -11f, 11f })
                    Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, new Vector3(sx * length, (baseY + bar) * 0.5f, lean * 0.017f)),
                        new Vector3(0.07f, 1.8f, 0.07f), yaw, false, lean);
            Add(parts, KitPiece.Trim, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, bar, 0f)), new Vector3(length + 0.3f, 0.07f, 0.07f), yaw, false);
            for (int k = 0, n = Mathf.Max(3, Mathf.RoundToInt(length * 2.5f)); k < n; k++)
            {
                var x = (k + 0.5f) / n * (length - 0.3f) - (length - 0.3f) * 0.5f;
                var hang = 0.7f + R() * 0.45f;
                Add(parts, KitPiece.Trim, KitMaterial.Roof, at + Turn(yaw, new Vector3(x, bar - 0.05f - hang * 0.5f, 0f)), new Vector3(0.15f, hang, 0.12f), yaw, false, (R() - 0.5f) * 8f, (R() - 0.5f) * 8f);
                Add(parts, KitPiece.Trim, KitMaterial.Net, at + Turn(yaw, new Vector3(x, bar - 0.12f, 0f)), new Vector3(0.18f, 0.05f, 0.15f), yaw, false);
            }
        }

        /// <summary>
        /// W-122 row 2: a carved post <paramref name="height"/> m from the water line (Piles runs it on into the mud),
        /// stacked with two or three lizard heads facing <paramref name="yaw"/>, bare wood alternating with the village's
        /// <paramref name="paint"/>: dark eyes, a long snout, a painted crest on top.
        /// </summary>
        static void Totem(List<KitPart> parts, System.Random rng, Vector3 at, float yaw, float height, KitMaterial paint)
        {
            Add(parts, KitPiece.Column, KitMaterial.Wood, at + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.3f, height, 0.3f), yaw, false);
            var heads = 2 + rng.Next(2);
            for (int k = 0; k < heads; k++)
            {
                var y = height - 0.3f - k * 0.62f;
                var m = (k & 1) == 0 ? paint : KitMaterial.Wood;
                Add(parts, KitPiece.Trim, m, at + new Vector3(0f, y, 0f), new Vector3(0.48f, 0.52f, 0.48f), yaw, false);
                Add(parts, KitPiece.Trim, m, at + Turn(yaw, new Vector3(0f, y - 0.1f, 0.38f)), new Vector3(0.26f, 0.16f, 0.36f), yaw, false);
                Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(yaw, new Vector3(0f, y - 0.19f, 0.36f)), new Vector3(0.24f, 0.03f, 0.34f), yaw, false);
                foreach (var sx in new[] { -0.13f, 0.13f })
                    Add(parts, KitPiece.Trim, KitMaterial.Opening, at + Turn(yaw, new Vector3(sx, y + 0.09f, 0.245f)), new Vector3(0.09f, 0.07f, 0.02f), yaw, false);
            }
            Add(parts, KitPiece.Trim, paint, at + new Vector3(0f, height + 0.12f, 0f) + Turn(yaw, new Vector3(0f, 0f, -0.05f)), new Vector3(0.07f, 0.4f, 0.46f), yaw, false, -12f);
        }

        /// <summary>W-122 row 2: <paramref name="count"/> hanging gourds on cords from the eave corners of a hut.</summary>
        static void Gourds(List<KitPart> parts, System.Random rng, float width, float depth, int count)
        {
            float R() => (float)rng.NextDouble();
            // Hang from the underside of the thatch slab over each corner (the slab's outer edge is local z = side * half).
            var slabs = new List<KitPart>();
            foreach (var p in parts)
                if (p.Piece == KitPiece.Roof && p.Size.y < 0.3f && p.Size.z > 1f && Quaternion.Angle(p.Rotation, Quaternion.identity) > 5f)
                    slabs.Add(p);
            if (slabs.Count == 0)
                return;
            var start = rng.Next(4);
            for (int k = 0; k < count && k < 4; k++)
            {
                var c = (start + k) & 3;
                var sx = (c & 1) == 0 ? -1f : 1f;
                var slab = slabs[(c >> 1) % slabs.Count];
                var outward = slab.Centre.z >= 0f ? 1f : -1f;
                var local = new Vector3(sx * (width * 0.5f + 0.15f), -slab.Size.y * 0.5f, 0f);
                // The slab's local z that sits 0.3 m outside the wall: walk along it from the centre.
                var flat = slab.Rotation * Vector3.forward;
                var along = Mathf.Abs(flat.z) > 0.1f ? (outward * (depth * 0.5f + 0.3f) - slab.Centre.z) / flat.z : 0f;
                local.z = Mathf.Clamp(along, -slab.Size.z * 0.5f, slab.Size.z * 0.5f);
                var top = slab.Centre + slab.Rotation * local;
                var cord = 0.35f + R() * 0.45f;
                var m = R() < 0.5f ? KitMaterial.Copper : KitMaterial.Sand;
                Add(parts, KitPiece.Trim, KitMaterial.Net, top + new Vector3(0f, -cord * 0.5f, 0f), new Vector3(0.025f, cord, 0.025f), 0f, false);
                Add(parts, KitPiece.Trim, KitMaterial.Wood, top + new Vector3(0f, -cord - 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), 0f, false);
                Add(parts, KitPiece.Trim, m, top + new Vector3(0f, -cord - 0.17f, 0f), new Vector3(0.17f, 0.17f, 0.17f), 45f, false);
                Add(parts, KitPiece.Trim, m, top + new Vector3(0f, -cord - 0.4f, 0f), new Vector3(0.3f, 0.32f, 0.3f), R() * 90f, false);
            }
        }

        /// <summary>W-122 row 2: <paramref name="count"/> ragged moss patches (MossCap sheets) lying on the thatch's slabs.</summary>
        static void RoofMoss(List<KitPart> parts, System.Random rng, int count)
        {
            float R() => (float)rng.NextDouble();
            var slabs = new List<KitPart>();
            foreach (var p in parts)
                if (p.Piece == KitPiece.Roof && p.Size.y < 0.3f && p.Size.z > 1f && Quaternion.Angle(p.Rotation, Quaternion.identity) > 5f)
                    slabs.Add(p);
            for (int k = 0; k < count && slabs.Count > 0; k++)
            {
                var s = slabs[rng.Next(slabs.Count)];
                var w = Mathf.Min(s.Size.x - 0.4f, 1.2f + R() * 1.4f);
                var l = Mathf.Min(s.Size.z - 0.3f, 1f + R() * 1.2f);
                var x = (R() - 0.5f) * (s.Size.x - w);
                var z = (R() - 0.5f) * (s.Size.z - l);
                // Sat clear of the thatch courses (up to ~0.2 m proud of the slab), or they swallow the sheet.
                parts.Add(new KitPart
                {
                    Piece = KitPiece.MossCap, Material = KitMaterial.Moss, Rotation = s.Rotation,
                    Centre = s.Centre + s.Rotation * new Vector3(x, s.Size.y * 0.5f + 0.22f, z), Size = new Vector3(w, 0.1f, l)
                });
            }
        }

        /// <summary>
        /// W-122 row 3: the village's hatchery, a round reed nest raft moored off the back of the smoke hut deck, kept warm
        /// by its fire. A clutch of speckled eggs sits in it, and one has cracked open with a hatchling's head peeking out.
        /// It draws from its own stream, so the deck's other details keep their places.
        /// </summary>
        static void Hatchery(List<KitPart> parts, uint seed)
        {
            var rng = new System.Random((int)(Scramble(seed ^ 0xE66Fu) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            // Straight off the back edge: the hut walks meet the deck 30 deg either side of it, so this gap stays open.
            var at = new Vector3((R() - 0.5f) * 0.8f, 0f, -(MarshHub + 1.7f));
            const float ring = 0.95f;
            // The raft: a woven mat of reed and a bound reed rim, riding a hand above the water.
            Add(parts, KitPiece.Trim, KitMaterial.Wood, at + new Vector3(0f, 0.08f, 0f), new Vector3(2.1f, 0.1f, 2.1f), 0f, false);
            Add(parts, KitPiece.Trim, KitMaterial.Roof, at + new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.06f, 1.9f), 45f, false);
            for (int k = 0; k < 10; k++)
            {
                var yaw = k * 36f;
                Add(parts, KitPiece.Trim, KitMaterial.Roof, at + Turn(yaw, new Vector3(0f, 0.3f, ring)), new Vector3(0.66f, 0.26f, 0.2f), yaw, false, 0f, (R() - 0.5f) * 10f);
            }
            // Mooring cord to the deck's edge.
            var cordFrom = at + new Vector3(0f, 0.3f, ring);
            var cordTo = new Vector3(at.x, MarshDeck - 0.1f, -MarshHub);
            var mid = (cordFrom + cordTo) * 0.5f;
            var run = cordTo - cordFrom;
            Add(parts, KitPiece.Trim, KitMaterial.Net, mid, new Vector3(0.03f, 0.03f, run.magnitude), 0f, false, -Mathf.Atan2(run.y, new Vector2(run.x, run.z).magnitude) * Mathf.Rad2Deg);
            // The clutch: each egg three stacked blocks tapering up, cream with dark speckles.
            var eggs = 5 + rng.Next(3);
            var cracked = rng.Next(eggs);
            for (int k = 0; k < eggs; k++)
            {
                var spot = at + Turn(k * (360f / eggs) + R() * 20f, new Vector3(0f, 0.18f, k == 0 ? 0f : 0.45f + R() * 0.12f));
                if (k == 0)
                    spot = at + new Vector3(0f, 0.18f, 0f);
                var yaw = R() * 90f;
                var size = 0.85f + R() * 0.3f;
                var tilt = (R() - 0.5f) * 30f;
                if (k == cracked)
                {
                    // The lower half of the shell with a jagged rim, and a small green head looking out over it.
                    Add(parts, KitPiece.Trim, KitMaterial.Net, spot + new Vector3(0f, 0.08f * size, 0f), new Vector3(0.2f, 0.16f, 0.2f) * size, yaw, false);
                    for (int j = 0; j < 4; j++)
                        Add(parts, KitPiece.Trim, KitMaterial.Net, spot + Turn(yaw + j * 90f, new Vector3(0f, 0.19f * size, 0.08f * size)), new Vector3(0.09f, 0.08f + (j % 2) * 0.05f, 0.025f) * size, yaw + j * 90f, false, 0f, 12f);
                    // Head a touch big for the shell, as hatchlings are, so it reads from the deck above.
                    var head = spot + Turn(yaw, new Vector3(0f, 0.3f * size, 0.02f));
                    Add(parts, KitPiece.Trim, KitMaterial.Moss, head, new Vector3(0.15f, 0.12f, 0.17f), yaw, false, -20f);
                    Add(parts, KitPiece.Trim, KitMaterial.Moss, head + Turn(yaw, new Vector3(0f, -0.015f, 0.13f)), new Vector3(0.1f, 0.07f, 0.13f), yaw, false, -20f);
                    foreach (var ex in new[] { -1f, 1f })
                        Add(parts, KitPiece.Trim, KitMaterial.Lamp, head + Turn(yaw, new Vector3(ex * 0.075f, 0.035f, 0.05f)), new Vector3(0.025f, 0.035f, 0.035f), yaw, false);
                    continue;
                }
                Add(parts, KitPiece.Trim, KitMaterial.Net, spot + new Vector3(0f, 0.08f, 0f) * size, new Vector3(0.2f, 0.16f, 0.2f) * size, yaw, false, tilt, 0f);
                Add(parts, KitPiece.Trim, KitMaterial.Net, spot + new Vector3(0f, 0.15f, 0f) * size, new Vector3(0.24f, 0.12f, 0.24f) * size, yaw + 45f, false, tilt, 0f);
                Add(parts, KitPiece.Trim, KitMaterial.Net, spot + new Vector3(0f, 0.25f, 0f) * size, new Vector3(0.16f, 0.12f, 0.16f) * size, yaw, false, tilt, 0f);
                for (int j = 0; j < 3; j++)
                    Add(parts, KitPiece.Trim, KitMaterial.Wood, spot + Turn(yaw + j * 120f + 20f, new Vector3(0f, (0.12f + j * 0.05f) * size, 0.125f * size)), new Vector3(0.035f, 0.03f, 0.01f), yaw + j * 120f + 20f, false);
            }
        }

        /// <summary>W-122 row 2: a clump of three to five lily pads on the water near <paramref name="at"/>, one sometimes in flower.</summary>
        static void LilyPads(List<KitPart> parts, System.Random rng, Vector3 at)
        {
            float R() => (float)rng.NextDouble();
            for (int k = 0, n = 3 + rng.Next(3); k < n; k++)
            {
                var spot = at + new Vector3((R() - 0.5f) * 1.6f, 0.03f + k * 0.004f, (R() - 0.5f) * 1.6f);
                var r = 0.22f + R() * 0.2f;
                var yaw = R() * 90f;
                Add(parts, KitPiece.Trim, KitMaterial.Moss, spot, new Vector3(r * 2f, 0.02f, r * 2f), yaw, false);
                Add(parts, KitPiece.Trim, KitMaterial.Moss, spot + new Vector3(0f, 0.002f, 0f), new Vector3(r * 2f, 0.02f, r * 2f), yaw + 45f, false);
                if (R() < 0.25f)
                {
                    Add(parts, KitPiece.Trim, KitMaterial.Net, spot + new Vector3(0f, 0.07f, 0f), new Vector3(0.16f, 0.1f, 0.16f), yaw + 22f, false);
                    Add(parts, KitPiece.Trim, KitMaterial.Sand, spot + new Vector3(0f, 0.12f, 0f), new Vector3(0.07f, 0.05f, 0.07f), yaw, false);
                }
            }
        }

        /// <summary>
        /// W-122: boardwalk pieces (the fishing villages' W-119 piece, <see cref="Boardwalk"/>) end to end from
        /// <paramref name="from"/> along <paramref name="yaw"/> for <paramref name="length"/> m, every other piece 2 cm
        /// higher so their overlaps never fight.
        /// </summary>
        static void MarshPlanks(List<KitPart> parts, uint seed, CultureTrim t, Vector3 from, float yaw, float length, float deck)
        {
            const float piece = 3.8f;
            var n = Mathf.Max(1, Mathf.CeilToInt((length - 0.4f) / (piece - 0.4f)));
            var step = n > 1 ? (length - piece) / (n - 1) : 0f;
            var size = n > 1 ? piece : length;
            for (int k = 0; k < n; k++)
            {
                var along = size * 0.5f + k * step;
                var turn = Quaternion.Euler(0f, yaw, 0f);
                foreach (var p0 in Boardwalk(seed ^ (uint)k * 0x9E3779B9u, t, size, deck + (k & 1) * 0.02f))
                {
                    var p = p0;
                    p.Centre = from + turn * (p.Centre + new Vector3(0f, 0f, along));
                    p.Rotation = turn * p.Rotation;
                    parts.Add(p);
                }
            }
        }

        /// <summary>W-122: posts standing on the water line run on down <see cref="MarshPile"/> m into the mud.</summary>
        static void Piles(List<KitPart> parts)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Piece == KitPiece.Column && p.Centre.y - p.Size.y * 0.5f < 0.1f && p.Centre.y - p.Size.y * 0.5f > -0.1f)
                {
                    p.Centre.y -= MarshPile * 0.5f;
                    p.Size.y += MarshPile;
                }
                parts[i] = p;
            }
        }

        static Vector3 Turn(float yaw, Vector3 p) => Quaternion.Euler(0f, yaw, 0f) * p;

        static void Add(List<KitPart> parts, KitPiece piece, KitMaterial m, Vector3 centre, Vector3 size, float yaw, bool solid, float pitch = 0f, float roll = 0f) =>
            parts.Add(new KitPart { Piece = piece, Material = m, Centre = centre, Size = size, Rotation = Quaternion.Euler(pitch, yaw, roll), Solid = solid });

        /// <summary>An order-sensitive hash of a part list (pieces, materials, centres and sizes to the centimetre).</summary>
        public static uint Fingerprint(List<KitPart> parts)
        {
            var h = 2166136261u;
            void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
            foreach (var p in parts)
            {
                Mix((int)p.Piece | (int)p.Material << 8);
                Mix(Mathf.RoundToInt(p.Centre.x * 100f)); Mix(Mathf.RoundToInt(p.Centre.y * 100f)); Mix(Mathf.RoundToInt(p.Centre.z * 100f));
                Mix(Mathf.RoundToInt(p.Size.x * 100f)); Mix(Mathf.RoundToInt(p.Size.y * 100f)); Mix(Mathf.RoundToInt(p.Size.z * 100f));
            }
            return h;
        }

        /// <summary>A full-avalanche hash (math.hash on uints barely mixes neighbouring seeds).</summary>
        static uint Scramble(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
