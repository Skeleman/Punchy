using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// #550: hung fish, shared by drying racks (and market stalls once they exist). Each fish is a short Net cord from
    /// its rail and a KitMaterial.Fish part below it, which FloraGenerator.HungFish draws as a real fish species
    /// (FloraSpecies.HungFish) head down inside the part's box.
    /// </summary>
    public static partial class StructureKit
    {
        /// <summary>A drying rack: an A-frame each end, two rails, a row of fish hung on cords from each.</summary>
        static void FishRack(List<KitPart> parts, Vector3 at, float yaw, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var w = 2f + R();
            foreach (var side in new[] { -0.5f, 0.5f })
                foreach (var lean in new[] { -12f, 12f })
                    Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * w, 0.75f, 0f)), new Vector3(0.1f, 1.6f, 0.1f), yaw, false, lean);
            for (int r = 0; r < 2; r++)
            {
                var h = 0.9f + r * 0.5f;
                Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, h, 0f)), new Vector3(w + 0.2f, 0.06f, 0.06f), yaw, false);
                for (int f = 0, fish = 4 + rng.Next(4); f < fish; f++)
                    HangFish(parts, at + Turn(yaw, new Vector3((f + 0.5f) / fish * w - w * 0.5f, h - 0.03f, 0f)), yaw, (R() - 0.5f) * 10f);
            }
        }

        /// <summary>One fish hung head down under <paramref name="hook"/> (the underside of its rail): a 9 cm cord, then
        /// the fish's 30 cm box, both swung by <paramref name="swing"/> degrees about the hook.</summary>
        static void HangFish(List<KitPart> parts, Vector3 hook, float yaw, float swing)
        {
            var down = Quaternion.Euler(0f, yaw, swing) * Vector3.down;
            Add(parts, KitPiece.Trim, KitMaterial.Net, hook + down * 0.045f, new Vector3(0.012f, 0.09f, 0.012f), yaw, false, 0f, swing);
            Add(parts, KitPiece.Trim, KitMaterial.Fish, hook + down * 0.24f, new Vector3(0.1f, 0.3f, 0.05f), yaw, false, 0f, swing);
        }
    }
}
