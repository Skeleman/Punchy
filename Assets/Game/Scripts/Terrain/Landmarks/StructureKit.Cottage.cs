using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>W-173 (#399): town houses that used to be one-off meshes, rebuilt from the kit's house parts.</summary>
    public static partial class StructureKit
    {
        /// <summary>A one-storey stone cottage, <paramref name="half"/> to the wall's outer face; the caller hangs a <paramref name="door"/> (width, height) in the gap.</summary>
        public static List<KitPart> StoneHouse(uint seed, CultureTrim t, float half, float wall, bool ladder, out Vector2 door)
        {
            var width = half * 2f - Thick;
            t.Storey = wall;
            t.Bay = width / 3f;
            t.Quoins = true;
            t.TowerChance = t.BalconyChance = t.StairChance = 0f;
            var parts = House(seed, t, width, width, 1, 0f);
            var out_ = width * 0.5f;
            parts.RemoveAll(p => p.Centre.z > out_ - 0.2f && Mathf.Abs(p.Centre.x) < 0.3f && p.Centre.y < wall * 0.9f &&
                                 (p.Piece == KitPiece.Door || p.Piece == KitPiece.Trim && p.Size.x < 2.5f));
            if (ladder)
                RoofLadder(parts, seed, t, width, width, 1, 1);
            door = new Vector2(Mathf.Min(1.3f, t.Bay * 0.55f), Mathf.Min(wall * 0.78f, 2.3f));
            return parts;
        }
    }
}
