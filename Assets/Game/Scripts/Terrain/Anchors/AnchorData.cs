using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// What an anchor is for. A byte that travels in creature state and saves, so values are appended only, never
    /// reordered (the same rule as CreatureTask).
    /// </summary>
    public enum AnchorKind : byte
    {
        Door, Work, Stall, Well, Bench, Gate, Altar, Seat,
        /// <summary>A patch of cave ceiling to hang from (bats). Position is on the ceiling.</summary>
        Roost,
        /// <summary>A rooftop, chimney, tower top or signpost to sit on. Position is the top of it.</summary>
        Perch,
        /// <summary>A ledge or eave to build a nest on.</summary>
        Nest,
        /// <summary>Soft ground to dig into: cave floors, banks.</summary>
        Burrow,
        /// <summary>The edge of still or running water to drink at.</summary>
        Pool,
        Hearth, Yard,
        /// <summary>A glowing plant (cave mushrooms, crystals): moths, salamanders and the like gather by its light.</summary>
        Glow
    }

    /// <summary>
    /// A point that world geometry exports for creatures to use: the only contract between places (caves,
    /// settlements) and creature behaviour. Baked from the seed, so every machine has the same anchors and nothing is
    /// synced; immutable (creatures count occupancy themselves). Fields are ordered so nothing is padded.
    /// </summary>
    public struct Anchor
    {
        /// <summary>Absolute, on the surface the creature uses: the ceiling point of a roost, the top of a perch.</summary>
        public float3 Position;
        /// <summary>Unit vector in world XZ: the way a creature at it faces (out of a nest, along a roost's passage).</summary>
        public float2 Facing;
        /// <summary>Radius of the spot (m): creatures using it spread out inside it.</summary>
        public float Size;
        /// <summary>The cave or settlement it belongs to (see OwnerCave / OwnerSettlement).</summary>
        public int Owner;
        public AnchorKind Kind;
        /// <summary>How many creatures it holds; 0 = unlimited.</summary>
        public byte Capacity;
        /// <summary>The biome it's in (palette id).</summary>
        public byte Biome;
        /// <summary>Bit 7: sheltered (out of the rain and wind). Bits 0–6: the cave's style slot or the settlement's folk.</summary>
        public byte Tags;

        public const byte Shelter = 0x80;
        public bool Sheltered => (Tags & Shelter) != 0;
        /// <summary>The cave's CaveStyleTable slot (cave anchors) or the settlement's folk (settlement anchors).</summary>
        public int Variety => Tags & 0x7F;

        const int SettlementBit = 1 << 30;
        public static int OwnerCave(int cave) => cave;
        public static int OwnerSettlement(int settlement) => settlement | SettlementBit;
        public bool InCave => (Owner & SettlementBit) == 0;
        public int OwnerIndex => Owner & ~SettlementBit;
    }

    /// <summary>Every anchor in the world, with a coarse column grid (each anchor is listed in the one cell it is in).</summary>
    public struct AnchorSetBlob
    {
        public BlobArray<Anchor> Anchors;
        public float2 GridOrigin;
        public float CellSize;
        public int2 GridSize;
        public BlobArray<int2> Cells;   // (first item, count)
        public BlobArray<int> Items;    // anchor indices
    }

    /// <summary>
    /// Finding anchors near a point. Allocation-free and Burst-safe, so the brain can call it from jobs while
    /// Deciding: it only visits the grid cells the search radius overlaps.
    /// </summary>
    public static class Anchors
    {
        static void CellsAround(ref AnchorSetBlob set, float2 xz, float radius, out int2 c0, out int2 c1)
        {
            c0 = math.max((int2)math.floor((xz - radius - set.GridOrigin) / set.CellSize), 0);
            c1 = math.min((int2)math.floor((xz + radius - set.GridOrigin) / set.CellSize), set.GridSize - 1);
        }

        /// <summary>Adds the index of every anchor of a kind within radius (3D distance) to a caller-owned list.</summary>
        public static void Near(ref AnchorSetBlob set, float3 position, float radius, AnchorKind kind, ref NativeList<int> into)
        {
            if (set.Cells.Length == 0)
                return;
            CellsAround(ref set, position.xz, radius, out var c0, out var c1);
            var r2 = radius * radius;
            for (int z = c0.y; z <= c1.y; z++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var range = set.Cells[z * set.GridSize.x + x];
                for (int k = range.x; k < range.x + range.y; k++)
                {
                    var i = set.Items[k];
                    ref var a = ref set.Anchors[i];
                    if (a.Kind == kind && math.distancesq(a.Position, position) <= r2)
                        into.Add(i);
                }
            }
        }

        /// <summary>
        /// One anchor of a kind within radius, chosen uniformly at random (reservoir sampling, so it needs no list).
        /// False if there is none.
        /// </summary>
        public static bool Pick(ref AnchorSetBlob set, float3 near, float radius, AnchorKind kind, ref Random rng, out Anchor anchor)
        {
            anchor = default;
            if (set.Cells.Length == 0)
                return false;
            CellsAround(ref set, near.xz, radius, out var c0, out var c1);
            var r2 = radius * radius;
            var seen = 0;
            for (int z = c0.y; z <= c1.y; z++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var range = set.Cells[z * set.GridSize.x + x];
                for (int k = range.x; k < range.x + range.y; k++)
                {
                    ref var a = ref set.Anchors[set.Items[k]];
                    if (a.Kind != kind || math.distancesq(a.Position, near) > r2)
                        continue;
                    seen++;
                    if (rng.NextInt(seen) == 0)
                        anchor = a;
                }
            }
            return seen > 0;
        }
    }
}
