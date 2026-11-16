using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-175 r3b (#415): the structure kit's small standalone parts, each defined once. A crate, a basket and a lantern
    /// head used to be rebuilt box by box at every site (wharf stacks, sunken cargo, raft cargo, six kinds of hung
    /// lantern), so each copy drifted a little. Every site now calls the one part here, so a change to a crate or a
    /// lantern reaches all of them (rule 33). The sites keep their own placement, sizes and seeded draws; a part only
    /// decides what it is made of. Parts are added in a frame (yaw, then pitch and roll) so their pieces turn together.
    /// </summary>
    public static partial class StructureKit
    {
        /// <summary>The lantern head's glass at scale 1 (m): the size most hung lanterns already used.</summary>
        static readonly Vector3 LanternGlass = new Vector3(0.24f, 0.32f, 0.24f);

        /// <summary>One piece of a part, placed in the part's frame.</summary>
        static void Put(List<KitPart> parts, KitPiece piece, KitMaterial m, Vector3 centre, Vector3 size, Quaternion rotation, bool solid) =>
            parts.Add(new KitPart { Piece = piece, Material = m, Centre = centre, Size = size, Rotation = rotation, Solid = solid });

        /// <summary>
        /// A wooden crate: a cube of <paramref name="size"/> centred on <paramref name="centre"/> and a lid band across
        /// its top, the band turning with the crate.
        /// </summary>
        static void Crate(List<KitPart> parts, Vector3 centre, float size, float yaw, bool solid, float pitch = 0f)
        {
            var q = Quaternion.Euler(pitch, yaw, 0f);
            Put(parts, KitPiece.Trim, KitMaterial.Wood, centre, new Vector3(size, size, size), q, solid);
            Put(parts, KitPiece.Trim, KitMaterial.Trim, centre + q * new Vector3(0f, size * 0.5f + 0.01f, 0f), new Vector3(size * 0.9f, 0.03f, 0.1f), q, false);
        }

        /// <summary>
        /// A woven basket: a net body <paramref name="size"/> across and 0.8 of that high, centred on
        /// <paramref name="centre"/>, with a wooden rim round its mouth.
        /// </summary>
        static void Basket(List<KitPart> parts, Vector3 centre, float size, float yaw)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Put(parts, KitPiece.Trim, KitMaterial.Net, centre, new Vector3(size, size * 0.8f, size), q, false);
            Put(parts, KitPiece.Trim, KitMaterial.Wood, centre + q * new Vector3(0f, size * 0.37f, 0f), new Vector3(size * 1.06f, size * 0.08f, size * 1.06f), q, false);
        }

        /// <summary>
        /// A hung lantern's head: a cap set at 45 degrees, the glass (<paramref name="glass"/>: Lamp, or Flame for one
        /// turned low) centred on <paramref name="centre"/> and a drip tray under it. The site supplies what it hangs from.
        /// </summary>
        static void LanternHead(List<KitPart> parts, Vector3 centre, float yaw, KitMaterial glass = KitMaterial.Lamp, float scale = 1f, float roll = 0f)
        {
            var q = Quaternion.Euler(0f, yaw, roll);
            var g = LanternGlass * scale;
            Put(parts, KitPiece.Trim, KitMaterial.Trim, centre + q * new Vector3(0f, g.y * 0.5f + 0.04f * scale, 0f), new Vector3(g.x + 0.05f, 0.055f, g.z + 0.05f),
                q * Quaternion.Euler(0f, 45f, 0f), false);
            Put(parts, KitPiece.Window, glass, centre, g, q, false);
            Put(parts, KitPiece.Trim, KitMaterial.Trim, centre - q * new Vector3(0f, g.y * 0.5f + 0.02f * scale, 0f), new Vector3(g.x + 0.03f, 0.04f, g.z + 0.03f), q, false);
        }
    }
}
