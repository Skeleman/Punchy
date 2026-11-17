using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// #550: the kit's one straight outside stair, in stone or wood. Stone treads are solid blocks down to the ground, as
    /// before. Wood treads are 5 cm planks on two sloped stringers, so a pier or stilt house stair reads as joinery rather
    /// than a stack of stone. Wood treads overlap the next by a few centimetres in plan, so the walk support has no gap.
    /// </summary>
    public static partial class StructureKit
    {
        /// <summary>A straight outside stair along +Z rising <paramref name="rise"/>, with a landing at the top.</summary>
        public static void Stair(List<KitPart> parts, Vector3 at, float yaw, float rise, float width, KitMaterial material = KitMaterial.Stone)
        {
            var steps = Mathf.Max(2, Mathf.CeilToInt(rise / 0.28f));
            var run = 0.3f;
            var wood = material == KitMaterial.Wood;
            for (int k = 0; k < steps; k++)
            {
                var y = (k + 1) * rise / steps;
                var z = (k - steps * 0.5f) * run;
                if (wood)
                    Add(parts, KitPiece.Stair, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, y - 0.025f, z)), new Vector3(width, 0.05f, run + 0.04f), yaw, false);
                else
                    Add(parts, KitPiece.Stair, material, at + Turn(yaw, new Vector3(0f, y * 0.5f, z)), new Vector3(width, y, run), yaw, false);
            }
            if (wood)
            {
                // Two stringers from the foot of the first tread to under the top one, just below the tread line.
                var foot = -steps * 0.5f * run - run * 0.5f;
                var top = steps * 0.5f * run;
                var length = top - foot;
                var slope = Mathf.Sqrt(length * length + rise * rise);
                var pitch = -Mathf.Atan2(rise, length) * Mathf.Rad2Deg;
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, rise * 0.5f - 0.15f, (foot + top) * 0.5f)), new Vector3(0.06f, 0.2f, slope), yaw, false, pitch);
            }
            // The landing starts where the top tread ends (it used to leave a 15 cm gap there, a hole in the walk support).
            Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, rise - 0.08f, steps * 0.5f * run + 0.5f - 0.075f)), new Vector3(width, 0.16f, 1.15f), yaw, false);
        }
    }
}
