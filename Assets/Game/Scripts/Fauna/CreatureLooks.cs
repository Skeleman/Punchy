using Unity.Collections;
using Unity.Entities;

namespace Punchy.Fauna
{
    /// <summary>
    /// What each segment of each species looks like (fur, feathers, scales, hoof, eye…, A-97), by species index.
    /// Kept apart from CreatureBlob so a look change never moves the fauna fingerprint; the render system writes a
    /// segment's code into SegmentInstance.Material.w for Creature.shader.
    /// </summary>
    public struct CreatureLookBlob
    {
        /// <summary>Every species' codes, one after another (CreatureBlobBuilder.Looks).</summary>
        public BlobArray<byte> Codes;
        /// <summary>Where each species' codes start in Codes; one more entry than species, so Starts[i + 1] ends them.</summary>
        public BlobArray<int> Starts;
        /// <summary>Whether any of a species' segments has fur (only those need a fur screen-size check).</summary>
        public BlobArray<bool> HasFur;
    }

    /// <summary>The look table, on the fauna render settings entity (made by FaunaDirector).</summary>
    public struct CreatureLooks : IComponentData
    {
        public BlobAssetReference<CreatureLookBlob> Blob;
    }

    /// <summary>
    /// The per-segment look code in SegmentInstance.Material.w: the low nibble is the surface (0 = fur, as every
    /// creature was drawn before A-97), the high one the fur length (FurLength: 0 Medium, 1 Short, 2 Long).
    /// Creature.shader decodes the same numbers.
    /// </summary>
    public static class SurfaceLook
    {
        public const byte Fur = 0, Feathers = 1, Scales = 2, Skin = 3, Chitin = 4, Hoof = 5, Claw = 6, Pad = 7, Eye = 8, Horn = 9;

        public static byte Of(CoatKind coat, SegmentSurface surface, FurLength length)
        {
            byte code = surface switch
            {
                SegmentSurface.Hoof => Hoof,
                SegmentSurface.Claw => Claw,
                SegmentSurface.Pad => Pad,
                SegmentSurface.Eye => Eye,
                SegmentSurface.Horn => Horn,
                SegmentSurface.Skin => Skin,
                _ => coat switch
                {
                    CoatKind.Feathers => Feathers,
                    CoatKind.Scales => Scales,
                    CoatKind.Skin => Skin,
                    CoatKind.Chitin => Chitin,
                    _ => Fur
                }
            };
            return code == Fur ? (byte)((int)length << 4) : code;
        }

        public static FurLength Length(FurLengthOverride o) =>
            o == FurLengthOverride.Short ? FurLength.Short : o == FurLengthOverride.Long ? FurLength.Long : FurLength.Medium;

        public static bool IsFur(byte code) => (code & 15) == Fur;

        /// <summary>A fur code cut back to what the creature's fur tier allows (0 = long fur shown, 1 = up to medium, 2 = short only).</summary>
        public static byte Clamp(byte code, int furTier)
        {
            if ((code & 15) != Fur || furTier <= 0)
                return code;
            var length = (FurLength)(code >> 4);
            if (furTier >= 2)
                return (byte)((int)FurLength.Short << 4);
            return length == FurLength.Long ? (byte)0 : code;
        }

        /// <summary>Builds the table for the director's species list.</summary>
        public static BlobAssetReference<CreatureLookBlob> Build(System.Collections.Generic.IReadOnlyList<CreatureSpecies> species)
        {
            var all = new System.Collections.Generic.List<byte[]>(species.Count);
            var total = 0;
            foreach (var s in species)
            {
                var looks = CreatureBlobBuilder.Looks(s);
                all.Add(looks);
                total += looks.Length;
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CreatureLookBlob>();
            var codes = builder.Allocate(ref root.Codes, total);
            var starts = builder.Allocate(ref root.Starts, all.Count + 1);
            var hasFur = builder.Allocate(ref root.HasFur, all.Count);
            var at = 0;
            for (int i = 0; i < all.Count; i++)
            {
                starts[i] = at;
                var fur = false;
                foreach (var c in all[i])
                {
                    codes[at++] = c;
                    fur |= IsFur(c);
                }
                hasFur[i] = fur;
            }
            starts[all.Count] = at;
            var blob = builder.CreateBlobAssetReference<CreatureLookBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
    }
}
