using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    // A-81 row 4: PoseJob's worker and traveller gear (tools, honey, goggles, letters, packs, rods, playthings),
    // moved out of CreatureRenderSystem.cs as it was.
    public partial class CreatureRenderSystem
    {
        /// <summary>A player's hair (P-29): the scalp's, up to four locks (a fall down the back, a braid, a tail, a bun or
        /// a crest), a beard and a moustache.</summary>
        const int HairParts = 7;
        /// <summary>Under this many pixels across (the body), hair is one shape: the scalp's, or a bald head's beard (rule 20).</summary>
        const float HairStrandPixels = 60f;
        /// <summary>A fishing rod's length from the grip to its tip (m, at scale 1; P-33).</summary>
        public const float RodLength = 1.8f;

        /// <summary>
        /// Which way a held weapon points (P-28, shared with P-33's fishing line): held up and a little forward, ready; a
        /// swing tips it back over the shoulder, then down and through, as the arm goes (CreatureAnimator's weapon arm,
        /// the same phases). Swing is CreatureOutfit.Swing (0 at rest).
        /// </summary>
        public static float3 WeaponAim(float yaw, byte swing)
        {
            var forward = new float3(math.sin(yaw), 0f, math.cos(yaw));
            var p = swing == 0 ? 0f : (swing - 1) / 254f;
            var lift = p < 0.55f ? math.smoothstep(0f, 0.55f, p) : 1f - math.smoothstep(0.55f, 0.72f, p);
            var strike = math.smoothstep(0.6f, 0.72f, p) * (1f - math.smoothstep(0.8f, 1f, p));
            var tilt = 0.45f - 1.1f * lift + 1.6f * strike;
            return new float3(0f, math.cos(tilt), 0f) + forward * math.sin(tilt);
        }

        /// <summary>Where a held rod's tip is, from the posed hand's last segment (P-33: the line starts there).</summary>
        public static float3 RodTip(in SegmentInstance hand, float yaw, byte swing, float scale) =>
            hand.Position + math.mul(hand.Rotation, new float3(0f, 0f, hand.Length)) + WeaponAim(yaw, swing) * RodLength * math.max(scale, 0.3f);

        partial struct PoseJob
        {
            /// <summary>
            /// A player's weapon in hand (P-28 row 1; CreatureState.Outfit), out of the right fist along the line its arm
            /// gives it, so it follows the arm through a swing (CreatureAnimator's weapon arm); a bow in the left hand,
            /// its string drawn back to the right as the swing goes. Too small on screen to tell apart, it isn't drawn.
            /// </summary>
            /// <summary>
            /// A player's armour (P-28 row 2; CreatureState.Outfit): each piece a shell a little larger than the body's own
            /// segment under it (the head, the torso's, each shin and foot), so it fits whatever body wears it and moves as
            /// that segment does. Too small on screen to tell apart, it isn't drawn.
            /// </summary>
            void Armour(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var view = Punchy.Settings.DetailPolicy.Current.Data;
                if (view.PixelsPerMetre > 0f &&
                    Punchy.Settings.DetailPolicy.ScreenSize(scale * 1.7f, Unity.Mathematics.math.distance(state.Position, CameraPosition), view.PixelsPerMetre) < 8f)
                    return;
                // (Linear colours, as the shader takes them: a mid brown leather, a darker one, grey iron, darker mail.)
                var leather = new Unity.Mathematics.float4(0.17f, 0.065f, 0.02f, 0f);
                var dark = new Unity.Mathematics.float4(0.07f, 0.027f, 0.01f, 0f);
                var iron = new Unity.Mathematics.float4(0.21f, 0.22f, 0.24f, 0f);
                var chain = new Unity.Mathematics.float4(0.13f, 0.14f, 0.15f, 0f);
                var pad = 0.012f * scale;
                // (Each worn slot's sub-block, only as long as its look needs: ArmourCount.)
                var bodyAt = at + HeadParts(state.Outfit.Head);
                var legsAt = bodyAt + BodyParts(state.Outfit.Body);
                var feetAt = legsAt + PairParts(state.Outfit.Legs);
                var backAt = feetAt + PairParts(state.Outfit.Feet);
                var neckAt = backAt + PairParts(state.Outfit.Back);
                int legs = 0;
                bool head = false, torso = false;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.SegmentCount == 0)
                        continue;
                    var last = part.FirstSegment + part.SegmentCount - 1;
                    if (part.Role == LimbRole.Head && state.Outfit.Head != 0 && !head)
                    {
                        head = true;
                        var h = Output[offset + last];
                        var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                        var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                        var look = state.Outfit.Head;
                        // (Under a helm or a close cap, the hair that grows from the top of the head is hidden, not beards
                        // and moustaches below, as PoseGuard's helmets did; under a brimmed hat it stays. A player's own
                        // hair is the Hair pass's, which hides itself.)
                        if (look == CreatureArmour.Cap || look == CreatureArmour.Helm || look == CreatureArmour.Hood || look == CreatureArmour.Woolcap ||
                            look >= CreatureArmour.KettleHat && look <= CreatureArmour.SkullHelm)
                            for (int j = 0; j < b.Parts.Length; j++)
                            {
                                ref var hair = ref b.Parts[j];
                                if (hair.Socket.y < 0f || hair.SegmentCount == 0 || b.Segments[hair.FirstSegment].Hair == 0)
                                    continue;
                                for (int s = 0; s < hair.SegmentCount; s++)
                                    Output[offset + hair.FirstSegment + s] = default;
                            }
                        if (look >= CreatureArmour.KettleHat)
                        {
                            GuardHead(in state, ref b, h, look, at, faded, scale);
                            continue;
                        }
                        var helm = look == CreatureArmour.Helm;
                        var cloth = look >= CreatureArmour.Hood;
                        var hatColour = cloth ? CreatureArmour.ClothMain(b.GuardKit) : helm ? iron : leather;
                        var hatTrim = cloth ? CreatureArmour.ClothTrim(b.GuardKit) : helm ? new Unity.Mathematics.float4(0.12f, 0.125f, 0.14f, 0f) : dark;
                        var hatMaterial = cloth ? SegmentMaterial.Cloth : helm ? SegmentMaterial.Metal : SegmentMaterial.Leather;
                        // (Over the crown and down to the brow, a helm lower over the ears; then a rim round its edge.)
                        // (A segment is drawn with round ends, so the shell's lower end starts its own radius above the brow.)
                        var brow = h.Length * (helm || look == CreatureArmour.Hood ? 0.4f : 0.55f);
                        var from = h.Position + ax * (brow + r * 0.9f);
                        var over = Shell(h, from, Unity.Mathematics.math.max(h.Length + r * 0.2f - brow - r * 1.8f, 0f), look == CreatureArmour.Hood ? 1.14f : 1.04f, pad, hatColour, faded, hatMaterial);
                        Output[at] = over;
                        // (The rim: a flat disc round the head at the brow, as the straw hat's brim is drawn.)
                        var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                        var face = Unity.Mathematics.math.normalizesafe(forward - ax * Unity.Mathematics.math.dot(forward, ax), forward);
                        var rimR = Unity.Mathematics.math.lerp(h.RadiusStart, h.RadiusEnd, brow / Unity.Mathematics.math.max(h.Length, 1e-4f)) *
                                   (helm ? 1.12f : look == CreatureArmour.Brimhat ? 2.1f : 1.25f) + pad;
                        var rim = Piece(h.Position + ax * brow, face, ax, 0f, rimR, rimR, 0.1f, hatTrim, faded, hatMaterial);
                        if (look == CreatureArmour.Hood)
                            // (The hood's back, down behind the head to the shoulders.)
                            rim = Piece(h.Position + ax * h.Length * 0.6f - face * r * 0.35f, -ax, face, h.Length * 0.8f, r * 1.12f, r * 0.9f, 0.7f, hatColour, faded, hatMaterial);
                        else if (look == CreatureArmour.Woolcap)
                            rim = Piece(h.Position + ax * (h.Length + r * 0.95f), ax, face, 0f, r * 0.32f, r * 0.32f, 1f, hatTrim, faded, hatMaterial);
                        Output[at + 1] = rim;
                    }
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && part.Role != LimbRole.Head && part.Role != LimbRole.Tail &&
                             state.Outfit.Body != 0 && !torso)
                    {
                        // (The torso's last two segments: the chest and the one under it, or one if that's all there is.)
                        torso = true;
                        if (state.Outfit.Body >= CreatureArmour.Breastplate)
                        {
                            GuardBody(in state, ref b, offset, part.FirstSegment, last, bodyAt, faded, scale, pad);
                            continue;
                        }
                        var k = 0;
                        var mail = state.Outfit.Body == CreatureArmour.Chain;
                        var woven = state.Outfit.Body >= CreatureArmour.Tunic;
                        var bodyColour = woven ? CreatureArmour.ClothMain(b.GuardKit) : mail ? chain : leather;
                        var bodyMaterial = woven ? SegmentMaterial.Cloth : mail ? SegmentMaterial.Metal : SegmentMaterial.Leather;
                        for (int j = Unity.Mathematics.math.max(part.FirstSegment, last - 1); j <= last; j++)
                        {
                            var t = Output[offset + j];
                            var ax = Unity.Mathematics.math.mul(t.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                            Output[bodyAt + k++] = Shell(t, t.Position + ax * t.Length * (woven ? 0f : 0.05f), t.Length * (woven ? 0.92f : 0.85f), mail ? 1.08f : 1.05f, pad,
                                                     bodyColour, faded, bodyMaterial);
                        }
                        // A robe: a skirt from the hips flaring down to the shins.
                        if (state.Outfit.Body == CreatureArmour.Robe)
                        {
                            var hips = Output[offset + part.FirstSegment];
                            var drop = Unity.Mathematics.math.max(hips.Position.y - state.Position.y - 0.15f * scale, 0.1f * scale);
                            var down = new Unity.Mathematics.float3(0f, -1f, 0f);
                            var ahead = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                            Output[bodyAt + 2] = Piece(hips.Position, down, ahead, drop, hips.RadiusStart * 1.1f + pad, hips.RadiusStart * 1.45f + pad, hips.Flatten,
                                                   CreatureArmour.ClothMain(b.GuardKit), faded, SegmentMaterial.Cloth);
                        }
                    }
                    else if (part.Role == LimbRole.Leg && part.Parent < 0 && part.SegmentCount >= 2 && legs < 2)
                    {
                        // (The shin is the leg's second-to-last segment with a foot, its last without.)
                        var shin = part.SegmentCount >= 3 ? last - 1 : last;
                        if (state.Outfit.Legs != 0)
                        {
                            var s = Output[offset + shin];
                            var ax = Unity.Mathematics.math.mul(s.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                            Output[legsAt + legs] = Shell(s, s.Position + ax * s.Length * 0.12f, s.Length * 0.72f, 1.15f, pad, iron, faded, SegmentMaterial.Metal);
                        }
                        if (state.Outfit.Feet != 0)
                        {
                            var f = Output[offset + last];
                            var ax = Unity.Mathematics.math.mul(f.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                            var r = Unity.Mathematics.math.max(f.RadiusStart, f.RadiusEnd);
                            Output[feetAt + legs] = part.SegmentCount >= 3
                                ? Shell(f, f.Position - ax * r * 0.4f, f.Length + r * 0.5f, 1.2f, pad, dark, faded, SegmentMaterial.Leather)
                                : Shell(f, f.Position + ax * f.Length * 0.7f, f.Length * 0.35f, 1.25f, pad, dark, faded, SegmentMaterial.Leather);
                        }
                        legs++;
                    }
                }
                if (state.Outfit.Back != 0 || state.Outfit.Neck != 0)
                    Wrap(in state, ref b, offset, backAt, neckAt, faded, scale, pad);
            }

            /// <summary>
            /// Zach 11-12: what guards and workers wear on the head, on any body (the shapes are the folk's own,
            /// PoseGuard.Guard's helmets and Gear's hats): a kettle hat, a dwarf's horned helm, an elf's crested helm (its
            /// crest dyed), a goblin's dented pot, an ogre's beast skull, a baker's cap, a straw hat, a fisher's hat, or
            /// goggles over the eyes. The armour block's head sub-block (HeadParts).
            /// </summary>
            void GuardHead(in CreatureState state, ref CreatureBlob b, in SegmentInstance h, byte look, int at, float faded, float scale)
            {
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                var crown = h.Position + ax * h.Length;
                var face = Unity.Mathematics.math.normalizesafe(forward - ax * Unity.Mathematics.math.dot(forward, ax), forward);
                var side = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(ax, face), new Unity.Mathematics.float3(forward.z, 0f, -forward.x));
                // (One of the folk's helmets is its folk's metal, GuardArmour, as PoseGuard drew them.)
                var iron = state.Mode == CreatureMode.Puppet ? new Unity.Mathematics.float4(0.32f, 0.33f, 0.35f, 0f) : b.GuardArmour;
                var bone = new Unity.Mathematics.float4(0.78f, 0.74f, 0.62f, 0f);
                var cloth = new Unity.Mathematics.float4(0.86f, 0.84f, 0.78f, 0f);
                var dome = Piece(crown + ax * r * 0.12f, face, ax, 0f, r * 1.18f, r * 1.18f, 0.72f, iron, faded, SegmentMaterial.Metal);
                switch (look)
                {
                    case CreatureArmour.KettleHat:
                        // (A dome with a broad brim all round.)
                        Output[at] = dome;
                        Output[at + 1] = Piece(crown - ax * r * 0.3f, face, ax, 0f, r * 1.95f, r * 1.95f, 0.07f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureArmour.HornedHelm:
                        Output[at] = dome;
                        Output[at + 1] = Piece(crown + side * r * 0.85f, Unity.Mathematics.math.normalize(side * 0.8f + ax * 0.7f), face, r * 1.3f, r * 0.32f, r * 0.04f, 1f, bone, faded, SegmentMaterial.Bone);
                        Output[at + 2] = Piece(crown - side * r * 0.85f, Unity.Mathematics.math.normalize(-side * 0.8f + ax * 0.7f), face, r * 1.3f, r * 0.32f, r * 0.04f, 1f, bone, faded, SegmentMaterial.Bone);
                        break;
                    case CreatureArmour.CrestedHelm:
                        // (A tall crest, broad front to back and thin across, swept back like a leaf; dyed.)
                        Output[at] = dome;
                        Output[at + 1] = Piece(crown + ax * r * 0.4f, Unity.Mathematics.math.normalize(ax - face * 0.35f), side, r * 2.6f, r * 0.95f, r * 0.08f, 0.14f, Dye(in state, ref b), faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureArmour.PotHelm:
                    {
                        // (A dented pot, tipped askew.)
                        var tilt = Unity.Mathematics.math.normalize(ax + side * 0.3f - face * 0.1f);
                        Output[at] = Piece(crown - tilt * r * 0.55f, tilt, face, r * 0.95f, r * 1.12f, r * 1.2f, 0.92f, new Unity.Mathematics.float4(0.3f, 0.29f, 0.27f, 0f), faded, SegmentMaterial.Metal);
                        Output[at + 1] = Piece(crown - tilt * r * 0.2f, face, tilt, 0f, r * 1.42f, r * 1.42f, 0.12f, new Unity.Mathematics.float4(0.22f, 0.21f, 0.2f, 0f), faded, SegmentMaterial.Metal);
                        break;
                    }
                    case CreatureArmour.SkullHelm:
                    {
                        // (A great beast's skull: a bone cap, a heavy brow, two short horns.)
                        var skull = dome;
                        skull.Color = bone;
                        skull.Material = new Unity.Mathematics.float4((float)SegmentMaterial.Bone, 0f, 0f, 0f);
                        Output[at] = skull;
                        Output[at + 1] = Piece(crown + face * r * 0.75f - side * r * 0.85f, side, ax, r * 1.7f, r * 0.3f, r * 0.3f, 0.7f, bone, faded, SegmentMaterial.Bone);
                        Output[at + 2] = Piece(crown + side * r * 0.7f + face * r * 0.3f, Unity.Mathematics.math.normalize(side * 0.5f + ax * 0.6f + face * 0.4f), face, r * 0.7f, r * 0.25f, r * 0.05f, 1f, bone, faded, SegmentMaterial.Bone);
                        Output[at + 3] = Piece(crown - side * r * 0.7f + face * r * 0.3f, Unity.Mathematics.math.normalize(-side * 0.5f + ax * 0.6f + face * 0.4f), face, r * 0.7f, r * 0.25f, r * 0.05f, 1f, bone, faded, SegmentMaterial.Bone);
                        break;
                    }
                    case CreatureArmour.BakerCap:
                        // (A soft white crown, no brim.)
                        Output[at] = Piece(h.Position + ax * (h.Length + r * 0.15f), ax, face, r * 0.75f, r * 1.02f, r * 1.15f, 1f, cloth, faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureArmour.StrawHat:
                    case CreatureArmour.FisherHat:
                    {
                        // (A wide straw brim and its crown; a fisher's floppier, narrower, in oilcloth green.)
                        var fisher = look == CreatureArmour.FisherHat;
                        var colour = fisher ? new Unity.Mathematics.float4(0.34f, 0.38f, 0.3f, 0f) : new Unity.Mathematics.float4(0.82f, 0.7f, 0.42f, 0f);
                        var material = fisher ? SegmentMaterial.Cloth : SegmentMaterial.Wood;
                        var brim = (fisher ? 1.6f : 2.1f) * r;
                        Output[at] = Piece(crown - ax * r * 0.3f, face, ax, 0f, brim, brim, 0.07f, colour, faded, material);
                        Output[at + 1] = Piece(crown - ax * r * 0.4f, ax, face, r * (fisher ? 0.55f : 0.7f), r * 1.05f, r * 0.85f, 1f, colour, faded, material);
                        break;
                    }
                    case CreatureArmour.Goggles:
                    {
                        // (Smoked lenses over the eyes, a leather strap round the head.)
                        var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                        var eyes = h.Position + ax * h.Length * 0.55f;
                        var glass = new Unity.Mathematics.float4(0.05f, 0.05f, 0.06f, 0f);
                        Output[at] = Piece(eyes + forward * r * 0.82f + right * r * 0.42f, forward, new Unity.Mathematics.float3(0f, 1f, 0f), r * 0.22f, r * 0.3f, r * 0.3f, 1f, glass, faded, SegmentMaterial.Cloth);
                        Output[at + 1] = Piece(eyes + forward * r * 0.82f - right * r * 0.42f, forward, new Unity.Mathematics.float3(0f, 1f, 0f), r * 0.22f, r * 0.3f, r * 0.3f, 1f, glass, faded, SegmentMaterial.Cloth);
                        Output[at + 2] = Piece(eyes - right * r * 1.02f, right, new Unity.Mathematics.float3(0f, 1f, 0f), r * 2.04f, r * 0.12f, r * 0.12f, 1f, new Unity.Mathematics.float4(0.22f, 0.14f, 0.08f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                    }
                }
            }

            /// <summary>
            /// Zach 11-12: what guards and workers wear on the body, on any body: a breastplate (iron) or a leather
            /// cuirass over the chest, with a pad on each shoulder; or an apron down the front (cloth, a smith's leather,
            /// a baker's white). The armour block's body sub-block (BodyParts), from at.
            /// </summary>
            void GuardBody(in CreatureState state, ref CreatureBlob b, int offset, int first, int last, int at, float faded, float scale, float pad)
            {
                var look = state.Outfit.Body;
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var c = Output[offset + last];
                var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                if (look >= CreatureArmour.Apron)
                {
                    // (An apron down the front: from the chest to below the hips, flat.)
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var colour = look == CreatureArmour.LeatherApron ? new Unity.Mathematics.float4(0.14f, 0.085f, 0.05f, 0f) : look == CreatureArmour.BakerApron ? new Unity.Mathematics.float4(0.86f, 0.84f, 0.78f, 0f) : new Unity.Mathematics.float4(0.62f, 0.58f, 0.5f, 0f);
                    var topAt = c.Position + axis * c.Length * 0.55f + front * r * 0.72f;
                    Output[at] = Piece(topAt, -axis, front, c.Length * 0.6f + r * 1.2f, r * 0.62f, r * 0.7f, 0.035f, colour, faded,
                                           look == CreatureArmour.LeatherApron ? SegmentMaterial.Leather : SegmentMaterial.Cloth);
                    return;
                }
                // (Plate for a breastplate, leather for a cuirass: over the chest and the segment under it.)
                var plate = look == CreatureArmour.Breastplate;
                // (One of the folk's is its folk's armour colour, GuardArmour, as PoseGuard drew it; an ogre's pads iron on
                // its dark hide, so it stands out from its folk at a distance.)
                var folk = state.Mode != CreatureMode.Puppet;
                var colourOf = folk ? b.GuardArmour : plate ? new Unity.Mathematics.float4(0.32f, 0.33f, 0.35f, 0f) : new Unity.Mathematics.float4(0.2f, 0.12f, 0.06f, 0f);
                var material = plate ? SegmentMaterial.Metal : SegmentMaterial.Leather;
                var ogrePads = folk && b.GuardKit == GuardKit.Ogre;
                var k = 0;
                for (int j = Unity.Mathematics.math.max(first, last - 1); j <= last; j++)
                {
                    var t = Output[offset + j];
                    var tAx = Unity.Mathematics.math.mul(t.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    Output[at + k++] = Shell(t, t.Position + tAx * t.Length * 0.05f, t.Length * 0.85f, 1.12f, pad, colourOf, faded, material);
                }
                // (A pad on each shoulder: the first segment of each arm.)
                var pads = 0;
                for (int i = 0; i < b.Parts.Length && pads < 2; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role != LimbRole.Arm || part.SegmentCount == 0)
                        continue;
                    var sh = Output[offset + part.FirstSegment];
                    var outward = part.Side > 0f ? right : -right;
                    Output[at + 2 + pads++] = Piece(sh.Position + up * sh.RadiusStart * 0.4f, outward, up, 0.02f * scale, sh.RadiusStart * 1.9f, sh.RadiusStart * 1.6f, 0.55f,
                                                      ogrePads ? new Unity.Mathematics.float4(0.32f, 0.33f, 0.35f, 0f) : colourOf, faded, ogrePads ? SegmentMaterial.Metal : material);
                }
            }

            /// <summary>
            /// A cloak and a scarf (P-28 row 3), in the wearer's folk's colours: the cloak hangs from behind the shoulders
            /// to the knees in two pieces, swept back as the body goes faster and stirring a little as it walks; the scarf
            /// is a band round the base of the head with one end down the chest. The back's two capsules from backAt, the
            /// neck's from neckAt (Armour's sub-blocks).
            /// </summary>
            void Wrap(in CreatureState state, ref CreatureBlob b, int offset, int backAt, int neckAt, float faded, float scale, float pad)
            {
                int chest = -1, head = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.SegmentCount == 0)
                        continue;
                    if (part.Role == LimbRole.Head && head < 0)
                        head = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && part.Role != LimbRole.Head && part.Role != LimbRole.Tail && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var main = CreatureArmour.ClothMain(b.GuardKit);
                var trim = CreatureArmour.ClothTrim(b.GuardKit);
                if (state.Outfit.Back == CreatureArmour.Cloak && chest >= 0)
                {
                    var c = Output[offset + chest];
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    // (Against the back: a torso is flattened front to back, so its depth there is the flattened radius.)
                    var depth = r * Unity.Mathematics.math.clamp(c.Flatten, 0.3f, 1f);
                    var shoulders = Tip(c) - forward * (depth + 0.015f * scale) - up * 0.04f * scale;
                    var hang = Unity.Mathematics.math.max(shoulders.y - state.Position.y - 0.45f * scale, 0.3f * scale);
                    // (How far it's swept back: with speed, up to a run's, and a slow stir; the hem further than the top.)
                    var going = Unity.Mathematics.math.saturate(state.Speed / 4f);
                    var stir = 0.05f * Unity.Mathematics.math.sin(state.Clock * 2.3f + (state.Individual % 7u)) * (0.3f + going);
                    var top = Unity.Mathematics.math.normalize(-up - forward * (0.12f + 0.35f * going + stir));
                    var hem = Unity.Mathematics.math.normalize(-up - forward * (0.2f + 0.9f * going + stir * 2f));
                    var width = r * 1.25f + pad;
                    Output[backAt] = Piece(shoulders, top, forward, hang * 0.5f, width * 0.85f, width, 0.15f, main, faded, SegmentMaterial.Cloth);
                    Output[backAt + 1] = Piece(shoulders + top * hang * 0.5f, hem, forward, hang * 0.5f, width, width * 1.1f, 0.15f, main, faded, SegmentMaterial.Cloth);
                }
                if ((state.Outfit.Back == CreatureArmour.HideCape || state.Outfit.Back == CreatureArmour.Satchel) && chest >= 0)
                {
                    var c = Output[offset + chest];
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var depth = r * Unity.Mathematics.math.clamp(c.Flatten, 0.3f, 1f);
                    if (state.Outfit.Back == CreatureArmour.HideCape)
                    {
                        // (An ogre guard's hide cape: from across the shoulders down the back, heavy, stirring less than cloth.)
                        var shoulders = Tip(c) - forward * (depth + 0.02f * scale) - up * 0.04f * scale;
                        var hang = Unity.Mathematics.math.max(shoulders.y - state.Position.y - 0.4f * scale, 0.3f * scale);
                        var going = Unity.Mathematics.math.saturate(state.Speed / 4f);
                        var top = Unity.Mathematics.math.normalize(-up - forward * (0.08f + 0.2f * going));
                        var hem = Unity.Mathematics.math.normalize(-up - forward * (0.12f + 0.5f * going));
                        var hide = new Unity.Mathematics.float4(0.36f, 0.27f, 0.18f, 0f);
                        var width = r * 1.3f + pad;
                        Output[backAt] = Piece(shoulders, top, forward, hang * 0.5f, width * 0.9f, width, 0.15f, hide, faded, SegmentMaterial.Leather);
                        Output[backAt + 1] = Piece(shoulders + top * hang * 0.5f, hem, forward, hang * 0.5f, width, width * 1.1f, 0.15f, hide, faded, SegmentMaterial.Leather);
                    }
                    else
                    {
                        // (A healer's satchel at the hip, on a strap across the chest from the other shoulder.)
                        var leather = new Unity.Mathematics.float4(0.4f, 0.25f, 0.13f, 0f);
                        var hip = c.Position + right * r * 1.05f + forward * r * 0.2f;
                        Output[backAt] = Piece(hip - axis * 0.06f * scale, axis, right, 0.2f * scale, 0.09f * scale, 0.1f * scale, 0.5f, leather, faded, SegmentMaterial.Leather);
                        var shoulder = c.Position + axis * c.Length - right * r * 0.6f + forward * r * 0.9f;
                        var strap = hip + axis * 0.12f * scale - shoulder;
                        Output[backAt + 1] = Piece(shoulder,Unity.Mathematics.math.normalize(strap), forward, Unity.Mathematics.math.length(strap), 0.02f * scale, 0.02f * scale, 0.3f, leather, faded, SegmentMaterial.Leather);
                    }
                }
                if (state.Outfit.Neck == CreatureArmour.Sash && chest >= 0)
                {
                    // (A guard's sash, from the shoulder across the chest to the other hip, in its wearer's colour.)
                    var c = Output[offset + chest];
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    // (The chest's own reach forward and to the side: a flattened chest, an ogre's or a dwarf's, is
                    // shallower than it is wide, so the sash is laid on its surface from these rather than its radius, as
                    // PoseGuard laid it; over a breastplate or cuirass, on the plate.)
                    var cx = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(1f, 0f, 0f));
                    var cy = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 1f, 0f));
                    var onPlate = state.Outfit.Body == CreatureArmour.Breastplate || state.Outfit.Body == CreatureArmour.Cuirass ? 1.12f : 1.02f;
                    var deep = r * Unity.Mathematics.math.sqrt(Unity.Mathematics.math.square(Unity.Mathematics.math.dot(cx, front)) + Unity.Mathematics.math.square(c.Flatten * Unity.Mathematics.math.dot(cy, front))) * onPlate;
                    var wide = r * Unity.Mathematics.math.sqrt(Unity.Mathematics.math.square(Unity.Mathematics.math.dot(cx, right)) + Unity.Mathematics.math.square(c.Flatten * Unity.Mathematics.math.dot(cy, right))) * onPlate;
                    var band = Unity.Mathematics.math.min(r * 0.2f, wide * 0.22f);
                    var from = c.Position + axis * c.Length + right * wide * 0.7f + front * deep * 0.35f;
                    var over = c.Position + axis * c.Length * 0.45f + front * (deep * 1.02f + pad);
                    var to = c.Position - axis * r * 0.4f - right * wide * 0.75f + front * deep * 0.6f;
                    var dye = Dye(in state, ref b);
                    Output[neckAt] = Piece(from,Unity.Mathematics.math.normalize(over - from), front, Unity.Mathematics.math.distance(from, over), band, band, 0.25f, dye, faded, SegmentMaterial.Cloth);
                    Output[neckAt + 1] = Piece(over,Unity.Mathematics.math.normalize(to - over), front, Unity.Mathematics.math.distance(over, to), band, band, 0.25f, dye, faded, SegmentMaterial.Cloth);
                }
                if (state.Outfit.Neck == CreatureArmour.Scarf && head >= 0)
                {
                    var h = Output[offset + head];
                    var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.min(h.RadiusStart, h.RadiusEnd);
                    var neck = h.Position - ax * r * 0.15f;
                    Output[neckAt] = Piece(neck,ax, forward, r * 0.35f, r * 1.05f + pad, r * 1.05f + pad, 0.9f, trim, faded, SegmentMaterial.Cloth);
                    Output[neckAt + 1] = Piece(neck + forward * r * 0.9f + right * r * 0.3f, Unity.Mathematics.math.normalize(-up + forward * 0.15f), forward,
                                            0.28f * scale, 0.035f * scale, 0.03f * scale, 0.35f, trim, faded, SegmentMaterial.Cloth);
                }
            }

            /// <summary>
            /// A player's hair (P-29; CreatureState.Outfit.Hair and HairColour): a shell over the scalp down to the brow, as
            /// a cap is drawn, then the style's locks (a fall to the shoulders, a braid, a tail, a bun, a crest) and a beard,
            /// all off the head's last segment so they move with it. A hat or anything on the head hides what's on top of
            /// the head (the scalp, a bun, a crest) and a hood what hangs behind too, so nothing shows through them; what
            /// hangs below a hat's brim still shows. Small on screen, one shape; smaller still, none.
            /// </summary>
            void Hair(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < HairParts; i++)
                    Output[at + i] = default;
                // The species' own hair parts (scalp, beard, moustache: whichever its Individual's style groups picked)
                // are hidden, as a guard's helmet hides them: this hair takes their place, bald included.
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.SegmentCount == 0 || b.Segments[part.FirstSegment].Hair == 0)
                        continue;
                    for (int j = 0; j < part.SegmentCount; j++)
                        Output[offset + part.FirstSegment + j] = default;
                }
                var style = state.Outfit.Hair;
                if (style == CreatureHair.Bald)
                    return;
                int head = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Head && b.Parts[i].SegmentCount > 0)
                    {
                        head = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (head < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var view = Punchy.Settings.DetailPolicy.Current.Data;
                var pixels = view.PixelsPerMetre > 0f
                    ? Punchy.Settings.DetailPolicy.ScreenSize(scale * 1.7f, Unity.Mathematics.math.distance(state.Position, CameraPosition), view.PixelsPerMetre)
                    : float.MaxValue;
                if (pixels < 8f)
                    return;
                var strands = pixels >= HairStrandPixels;
                var covered = (state.Worn & CreatureWorn.Hat) != 0 || state.Outfit.Head != 0 && state.Outfit.Head != CreatureArmour.Goggles;
                var hood = state.Outfit.Head == CreatureArmour.Hood;
                var colour = CreatureHair.Colour(state.Outfit.HairColour);
                const SegmentMaterial hair = SegmentMaterial.Body;

                var h = Output[offset + head];
                var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var face = Unity.Mathematics.math.normalizesafe(forward - ax * Unity.Mathematics.math.dot(forward, ax), forward);
                var side = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(ax, face), new Unity.Mathematics.float3(1f, 0f, 0f));
                var down = new Unity.Mathematics.float3(0f, -1f, 0f);
                var crown = h.Position + ax * h.Length;
                var pad = 0.008f * scale;

                // The scalp: a shell from the brow over the crown (curls fuller and lower, a topknot drawn tight), set
                // back a little so the brow shows.
                if (CreatureHair.Scalp(style) && style != CreatureHair.Crest && !covered)
                {
                    var curls = style == CreatureHair.Curls;
                    var brow = h.Length * (curls ? 0.42f : 0.5f);
                    var from = h.Position + ax * (brow + r * 0.9f) - face * r * 0.06f;
                    var grow = curls ? 1.16f : style == CreatureHair.Topknot || style == CreatureHair.TopknotBeard ? 1.03f : 1.06f;
                    Output[at] = Shell(h, from, Unity.Mathematics.math.max(h.Length + r * 0.15f - brow - r * 1.8f, 0f), grow, pad, colour, faded, hair);
                }
                else if (!strands && CreatureHair.Beard(style))
                    Output[at] = Piece(h.Position + face * r * 0.6f + ax * h.Length * 0.22f, Unity.Mathematics.math.normalize(-ax + face * 0.25f), face, r * 1.2f, r * 0.75f, r * 0.4f, 0.6f, colour, faded, hair);
                if (!strands)
                    return;

                switch (style)
                {
                    case CreatureHair.Long:
                    case CreatureHair.LongAndBeard:
                        // A fall down the back to the shoulders, flat against it (narrower under a helm, hidden by a hood).
                        if (!hood)
                            Output[at + 1] = Piece(h.Position + ax * h.Length * 0.75f - face * r * 0.45f, Unity.Mathematics.math.normalize(down - face * 0.15f), face,
                                                   r * 2.4f, r * (covered ? 0.85f : 0.95f), r * 0.82f, 0.4f, colour, faded, hair);
                        break;
                    case CreatureHair.Braid:
                        // A braid of four knots from the back of the head, thinning.
                        if (!hood)
                        {
                            var p = h.Position + ax * h.Length * 0.35f - face * r * 0.95f;
                            var along = Unity.Mathematics.math.normalize(down - face * 0.2f);
                            for (int k = 0; k < 4; k++)
                            {
                                Output[at + 1 + k] = Piece(p, along, face, r * 0.4f, r * (0.27f - 0.03f * k), r * (0.24f - 0.03f * k), 1f, colour, faded, hair);
                                p += along * r * 0.62f;
                            }
                        }
                        break;
                    case CreatureHair.Ponytail:
                        // Tied high at the back: the tie, then the tail swinging down behind.
                        if (!hood && !covered)
                        {
                            var tie = h.Position + ax * h.Length * 0.8f - face * r * 1.0f;
                            Output[at + 1] = Piece(tie, -face, ax, 0f, r * 0.22f, r * 0.22f, 1f, colour, faded, hair);
                            Output[at + 2] = Piece(tie, Unity.Mathematics.math.normalize(down - face * 0.45f), face, r * 1.9f, r * 0.32f, r * 0.12f, 0.8f, colour, faded, hair);
                        }
                        else if (!hood)
                            // (Under a hat: tied low, below the brim.)
                            Output[at + 2] = Piece(h.Position + ax * h.Length * 0.3f - face * r * 1.0f, Unity.Mathematics.math.normalize(down - face * 0.2f), face,
                                                   r * 1.6f, r * 0.3f, r * 0.12f, 0.8f, colour, faded, hair);
                        break;
                    case CreatureHair.Topknot:
                    case CreatureHair.TopknotBeard:
                        // A bun on the crown.
                        // (Clear of the scalp's shell, which reaches the head's end radius and a little more.)
                        if (!covered)
                            Output[at + 1] = Piece(crown + ax * (h.RadiusEnd * 1.03f + r * 0.28f), ax, face, r * 0.25f, r * 0.45f, r * 0.38f, 1f, colour, faded, hair);
                        break;
                    case CreatureHair.Crest:
                        // A ridge from the brow over the crown to the back of the head, thin side to side, in two pieces
                        // that follow the head's curve.
                        if (!covered)
                        {
                            // (Its ends sunk into the head, so it grows out of it rather than sitting on it.)
                            var front = crown + face * r * 0.6f + ax * h.RadiusEnd * 0.5f;
                            var top = crown + ax * h.RadiusEnd * 0.85f;
                            var back = crown - face * r * 0.7f + ax * h.RadiusEnd * 0.3f;
                            Output[at + 1] = Piece(front, Unity.Mathematics.math.normalize(top - front), side, Unity.Mathematics.math.distance(front, top), r * 0.36f, r * 0.42f, 0.45f, colour, faded, hair);
                            Output[at + 2] = Piece(top, Unity.Mathematics.math.normalize(back - top), side, Unity.Mathematics.math.distance(top, back), r * 0.42f, r * 0.32f, 0.45f, colour, faded, hair);
                        }
                        break;
                }

                // The beard, from the jaw forward and down (short, or long to the chest), and a moustache over the mouth.
                if (CreatureHair.Beard(style))
                {
                    // (From the cheeks under the mouth, so it covers the chin rather than hanging off it.)
                    var full = style != CreatureHair.ShortBeard;
                    var jaw = h.Position + face * r * 0.6f + ax * h.Length * 0.22f;
                    Output[at + 5] = Piece(jaw, Unity.Mathematics.math.normalize(-ax + face * 0.25f), face, r * (full ? 1.7f : 0.7f), r * 0.75f, r * (full ? 0.3f : 0.55f), 0.6f,
                                           colour, faded, hair);
                    Output[at + 6] = Piece(h.Position + ax * h.Length * 0.36f + face * r * 1.02f - side * r * 0.42f, side, ax, r * 0.84f, r * 0.17f, r * 0.17f, 0.6f,
                                           colour, faded, hair);
                }
            }

            /// <summary>A shell round a segment: the same turn, flattening and taper (its radii where the shell starts and
            /// ends along it, grown by this much and padded), from here along it, this long.</summary>
            static SegmentInstance Shell(in SegmentInstance under, Unity.Mathematics.float3 from, float length, float grow, float pad,
                                         Unity.Mathematics.float4 color, float faded, SegmentMaterial material)
            {
                var shell = under;
                var start = Unity.Mathematics.math.saturate(Unity.Mathematics.math.dot(from - under.Position, Unity.Mathematics.math.mul(under.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f))) / Unity.Mathematics.math.max(under.Length, 1e-4f));
                var end = Unity.Mathematics.math.saturate(start + length / Unity.Mathematics.math.max(under.Length, 1e-4f));
                shell.Position = from;
                shell.Length = length;
                shell.RadiusStart = Unity.Mathematics.math.lerp(under.RadiusStart, under.RadiusEnd, start) * grow + pad;
                shell.RadiusEnd = Unity.Mathematics.math.lerp(under.RadiusStart, under.RadiusEnd, end) * grow + pad;
                shell.Color = color;
                shell.Pad = faded;
                shell.Material = new Unity.Mathematics.float4((float)material, under.Material.y, 0f, 0f);
                return shell;
            }

            void Weapon(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded, int sheathed = -1)
            {
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var view = Punchy.Settings.DetailPolicy.Current.Data;
                if (view.PixelsPerMetre > 0f &&
                    Punchy.Settings.DetailPolicy.ScreenSize(scale, Unity.Mathematics.math.distance(state.Position, CameraPosition), view.PixelsPerMetre) < 6f)
                    return;
                int hand = -1, offHand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Role == LimbRole.Arm && part.Side < 0f && offHand < 0)
                        offHand = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0)
                    return;
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wood = new Unity.Mathematics.float4(0.45f, 0.3f, 0.17f, 0f);
                var iron = new Unity.Mathematics.float4(0.62f, 0.64f, 0.67f, 0f);
                var leather = new Unity.Mathematics.float4(0.36f, 0.22f, 0.12f, 0f);
                var darkWood = new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f);
                var straw = new Unity.Mathematics.float4(0.76f, 0.62f, 0.33f, 0f);
                var stick = new Unity.Mathematics.float4(0.24f, 0.16f, 0.08f, 0f);
                var h = Output[offset + hand];
                var grip = Tip(h);
                // (The fist's tip, and the point between both hands, for what's held in the fist or in both arms.)
                var tip = grip;
                var between = offHand >= 0 ? (tip + Tip(Output[offset + offHand])) * 0.5f : tip;
                var p = state.Outfit.Swing == 0 ? 0f : (state.Outfit.Swing - 1) / 254f;
                // (Held up and a little forward, ready; a swing tips it back over the shoulder, then down and through,
                // as the arm goes: CreatureAnimator's weapon arm, the same phases.)
                var dir = WeaponAim(state.Yaw, state.Outfit.Swing);
                // (One of the folk at work, Zach 11-12 row 3: its tool out of the fist crossing the hand's own line, so
                // it swings with the arm as the job's pose goes (TaskPose), as Gear held them.)
                var folk = state.Mode != CreatureMode.Puppet;
                if (folk)
                {
                    var along = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    grip = h.Position + along * h.Length * 0.6f;
                    dir = Unity.Mathematics.math.normalize(Unity.Mathematics.math.cross(along, right) + along * 0.2f);
                }
                // Sheathed (Zach 11-12 row 3, the sheath block: Punchy.Items.FolkKits.Sheathed): the same shape worn, as
                // a guard wears its weapon (PoseGuard.Guard): across the back from low on the left to high on the right if
                // it's long for its bearer, else hung from the belt at the left hip by its grip, its head down along the
                // thigh and a little back.
                var look = state.Outfit.Weapon;
                var sheathing = sheathed >= 0;
                if (sheathing)
                {
                    look = Punchy.Items.FolkKits.HandLook(sheathed);
                    var chest = -1;
                    for (int i = 0; i < b.Parts.Length && chest < 0; i++)
                        if (b.Parts[i].Parent < 0 && b.Parts[i].Role != LimbRole.Leg && b.Parts[i].Role != LimbRole.Arm && b.Parts[i].SegmentCount > 0)
                            chest = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                    if (chest < 0)
                        return;
                    var torso = Output[offset + chest];
                    var torsoAxis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var torsoR = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                    var length = Punchy.Items.FolkKits.Length(sheathed) * scale;
                    if (Punchy.Items.FolkKits.OnBack(sheathed, b.StandHeight))
                    {
                        dir = Unity.Mathematics.math.normalize(torsoAxis + right * 0.75f);
                        grip = torso.Position + torsoAxis * torso.Length * 0.5f - forward * torsoR * 1.3f - dir * length * 0.35f;
                    }
                    else
                    {
                        dir = Unity.Mathematics.math.normalize(-up - forward * 0.3f - right * 0.08f);
                        grip = torso.Position - torsoAxis * torsoR * 0.5f - right * torsoR * 1.15f;
                    }
                }
                var face = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(dir, right), forward);
                var k = 0;
                switch (look)
                {
                    case CreatureWeapon.Spear when sheathing:
                        // (Its shaft and head along the back.)
                        Output[at + k++] = Piece(grip - dir * 0.3f * scale, dir, face, 1.9f * scale, 0.017f * scale, 0.014f * scale, 1f, darkWood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 1.6f * scale, dir, face, 0.2f * scale, 0.03f * scale, 0.003f * scale, 0.4f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Bow when sheathing:
                    {
                        // (An elf's bow across its back, as PoseGuard drew it: an arc from low on one side to high on the
                        // other, and its string.)
                        var bowLength = 1.1f * scale;
                        var centre = grip + dir * bowLength * 0.35f;
                        var lower = centre - dir * bowLength * 0.5f;
                        var mid = centre - forward * 0.08f * scale;
                        var upper = centre + dir * bowLength * 0.5f;
                        var thread = new Unity.Mathematics.float4(0.78f, 0.74f, 0.62f, 0f);
                        Output[at + k++] = Piece(lower, Unity.Mathematics.math.normalize(mid - lower), forward, Unity.Mathematics.math.distance(lower, mid), 0.012f * scale, 0.02f * scale, 1f, darkWood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(mid, Unity.Mathematics.math.normalize(upper - mid), forward, Unity.Mathematics.math.distance(upper, mid), 0.02f * scale, 0.012f * scale, 1f, darkWood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(lower, dir, forward, bowLength, 0.003f * scale, 0.003f * scale, 1f, thread, faded, SegmentMaterial.Cloth);
                        break;
                    }
                    case CreatureWeapon.Club:
                        Output[at + k++] = Piece(grip - dir * 0.08f * scale, dir, face, 0.55f * scale, 0.024f * scale, 0.05f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.4f * scale, dir, face, 0.12f * scale, 0.06f * scale, 0.045f * scale, 0.85f, wood, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Sword:
                        Output[at + k++] = Piece(grip - dir * 0.08f * scale, dir, face, 0.13f * scale, 0.017f * scale, 0.017f * scale, 1f, leather, faded, SegmentMaterial.Leather);
                        Output[at + k++] = Piece(grip + dir * 0.06f * scale - face * 0.09f * scale, face, dir, 0.18f * scale, 0.014f * scale, 0.014f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        Output[at + k++] = Piece(grip + dir * 0.06f * scale, dir, face, 0.66f * scale, 0.032f * scale, 0.006f * scale, 0.25f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Axe:
                        // (A haft, and at its head a flat blade facing the way the swing strikes, widening to the edge,
                        // with a short poll behind; flattened across, side to side.)
                        Output[at + k++] = Piece(grip - dir * 0.1f * scale, dir, face, 0.62f * scale, 0.02f * scale, 0.024f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.44f * scale, -face, right, 0.15f * scale, 0.035f * scale, 0.075f * scale, 0.22f, iron, faded, SegmentMaterial.Metal);
                        Output[at + k++] = Piece(grip + dir * 0.44f * scale, face, right, 0.04f * scale, 0.03f * scale, 0.026f * scale, 0.6f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Rod:
                        // (P-33: a long pole tapering to the tip, a cork grip; the line and bobber are the Items side's,
                        // from RodTip.)
                        Output[at + k++] = Piece(grip - dir * 0.2f * scale, dir, face, 0.32f * scale, 0.02f * scale, 0.018f * scale, 1f, leather, faded, SegmentMaterial.Leather);
                        Output[at + k++] = Piece(grip + dir * 0.1f * scale, dir, face, (RodLength - 0.1f) * scale, 0.013f * scale, 0.004f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Hammer:
                        // (A haft, and across its top a squat iron head, square-ish, both faces flat.)
                        Output[at + k++] = Piece(grip - dir * 0.1f * scale, dir, face, 0.6f * scale, 0.02f * scale, 0.024f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.47f * scale + face * 0.1f * scale, -face, right, 0.2f * scale, 0.05f * scale, 0.05f * scale, 0.9f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Pickaxe:
                        // (A haft, and across its top an iron head: a long point tapering forward, the way the swing
                        // strikes, and a short adze behind, flat along the haft, wide side to side.)
                        Output[at + k++] = Piece(grip - dir * 0.1f * scale, dir, face, 0.65f * scale, 0.02f * scale, 0.024f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.49f * scale, face, right, 0.24f * scale, 0.03f * scale, 0.007f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        Output[at + k++] = Piece(grip + dir * 0.49f * scale, -face, dir, 0.15f * scale, 0.03f * scale, 0.028f * scale, 0.35f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Shovel:
                        // (A long haft with a short cross grip at its butt, and at its head a flat iron blade, wide side
                        // to side, thin front to back.)
                        Output[at + k++] = Piece(grip - dir * 0.3f * scale, dir, face, 0.95f * scale, 0.02f * scale, 0.022f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip - dir * 0.3f * scale - right * 0.06f * scale, right, face, 0.12f * scale, 0.016f * scale, 0.016f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.62f * scale, dir, face, 0.26f * scale, 0.1f * scale, 0.085f * scale, 0.12f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Spear when folk:
                    {
                        // (A gatekeeper's: upright in its fist, butt near the ground.)
                        var butt = tip - up * 0.95f * scale + forward * 0.02f * scale;
                        Output[at + k++] = Piece(butt, up, forward, 2f * scale, 0.017f * scale, 0.015f * scale, 1f, darkWood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(butt + up * 2f * scale, up, forward, 0.2f * scale, 0.035f * scale, 0.004f * scale, 0.3f, iron, faded, SegmentMaterial.Metal);
                        break;
                    }
                    case CreatureWeapon.Spear:
                    {
                        // (Both hands: levelled forward, the right hand back along the shaft, thrust out through a swing.)
                        var level = Unity.Mathematics.math.normalizesafe(forward + up * 0.15f, forward);
                        var thrust = Unity.Mathematics.math.sin(p * Unity.Mathematics.math.PI) * 0.35f * scale;
                        var butt = grip - level * (0.6f * scale - thrust);
                        Output[at + k++] = Piece(butt, level, up, 1.9f * scale, 0.018f * scale, 0.016f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(butt + level * 1.9f * scale, level, up, 0.24f * scale, 0.036f * scale, 0.003f * scale, 0.35f, iron, faded, SegmentMaterial.Metal);
                        break;
                    }
                    case CreatureWeapon.Bow:
                    {
                        // (In the left hand, upright, its limbs curving back; the string drawn to the right hand as the
                        // swing goes and loosed at its end, an arrow on it while drawn.)
                        var o = offHand >= 0 ? Output[offset + offHand] : h;
                        var bowAt = o.Position + Unity.Mathematics.math.mul(o.Rotation, new Unity.Mathematics.float3(0f, 0f, o.Length * 0.75f));
                        // (Each limb in two pieces: out from the grip, then curving back to its tip; drawn, the tips
                        // come back further.)
                        var draw = p < 0.75f ? Unity.Mathematics.math.smoothstep(0f, 0.6f, p) : 0f;
                        var limb = 0.31f * scale;
                        var bend = (0.14f + 0.08f * draw) * scale;
                        var topMid = bowAt + up * limb - forward * bend * 0.3f;
                        var top = bowAt + up * limb * 1.9f - forward * bend;
                        var bottomMid = bowAt - up * limb - forward * bend * 0.3f;
                        var bottom = bowAt - up * limb * 1.9f - forward * bend;
                        Output[at + k++] = Piece(bowAt, Unity.Mathematics.math.normalize(topMid - bowAt), right, Unity.Mathematics.math.length(topMid - bowAt), 0.026f * scale, 0.02f * scale, 0.6f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(topMid, Unity.Mathematics.math.normalize(top - topMid), right, Unity.Mathematics.math.length(top - topMid), 0.02f * scale, 0.01f * scale, 0.6f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(bowAt, Unity.Mathematics.math.normalize(bottomMid - bowAt), right, Unity.Mathematics.math.length(bottomMid - bowAt), 0.026f * scale, 0.02f * scale, 0.6f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(bottomMid, Unity.Mathematics.math.normalize(bottom - bottomMid), right, Unity.Mathematics.math.length(bottom - bottomMid), 0.02f * scale, 0.01f * scale, 0.6f, wood, faded, SegmentMaterial.Wood);
                        var rest = (top + bottom) * 0.5f;
                        var nock = Unity.Mathematics.math.lerp(rest, Tip(h), draw);
                        var thread = new Unity.Mathematics.float4(0.88f, 0.85f, 0.75f, 0f);
                        Output[at + k++] = Piece(top, Unity.Mathematics.math.normalizesafe(nock - top, -up), right, Unity.Mathematics.math.length(nock - top), 0.006f * scale, 0.006f * scale, 1f, thread, faded, SegmentMaterial.Cloth);
                        Output[at + k++] = Piece(nock, Unity.Mathematics.math.normalizesafe(bottom - nock, -up), right, Unity.Mathematics.math.length(bottom - nock), 0.006f * scale, 0.006f * scale, 1f, thread, faded, SegmentMaterial.Cloth);
                        if (draw > 0.05f)
                        {
                            var shaft = Unity.Mathematics.math.normalizesafe(bowAt - nock, forward);
                            Output[at + k++] = Piece(nock, shaft, up, Unity.Mathematics.math.distance(bowAt, nock) + 0.12f * scale, 0.008f * scale, 0.008f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        }
                        break;
                    }
                    // Zach 11-12, one item system: what folk carry, in any body's hand (the shapes are the folk's own).
                    case CreatureWeapon.GreatClub:
                        // (An ogre's great club: thick at the end, both hands on it.)
                        Output[at + k++] = Piece(grip - dir * 0.1f * scale, dir, face, 1.15f * scale, 0.045f * scale, 0.13f * scale, 1f, darkWood, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Hoe:
                        // (A long haft, a flat blade set across its top, facing the strike.)
                        Output[at + k++] = Piece(grip - dir * 0.38f * scale, dir, face, 1.1f * scale, 0.016f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.7f * scale, face, dir, 0.16f * scale, 0.06f * scale, 0.07f * scale, 0.1f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Broom:
                        // (A long haft, and its bristles on from the top, flaring.)
                        Output[at + k++] = Piece(grip - dir * 0.45f * scale, dir, face, 1.0f * scale, 0.016f * scale, 0.018f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.55f * scale, dir, face, 0.3f * scale, 0.05f * scale, 0.14f * scale, 0.7f, straw, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.SmithHammer:
                        // (A short haft, a small square head across its top.)
                        Output[at + k++] = Piece(grip - dir * 0.05f * scale, dir, face, 0.32f * scale, 0.016f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.27f * scale + face * 0.045f * scale, -face, right, 0.09f * scale, 0.024f * scale, 0.024f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        break;
                    case CreatureWeapon.Stick:
                        // (A crooked stick held out ahead.)
                        Output[at + k++] = Piece(grip - dir * 0.06f * scale, Unity.Mathematics.math.normalizesafe(dir + forward * 0.6f, dir), face, 0.6f * scale, 0.014f * scale, 0.009f * scale, 0.85f, stick, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Stone:
                        Output[at + k++] = Piece(tip - up * 0.02f * scale, up, forward, 0.03f * scale, 0.06f * scale, 0.05f * scale, 0.8f, new Unity.Mathematics.float4(0.45f, 0.44f, 0.42f, 0f), faded, SegmentMaterial.Body);
                        break;
                    case CreatureWeapon.Letter:
                        // (A rolled letter across the fist, its red seal.)
                        Output[at + k++] = Piece(tip - right * 0.06f * scale, right, up, 0.12f * scale, 0.012f * scale, 0.012f * scale, 1f, new Unity.Mathematics.float4(0.86f, 0.82f, 0.7f, 0f), faded, SegmentMaterial.Cloth);
                        Output[at + k++] = Piece(tip + up * 0.012f * scale, up, forward, 0f, 0.008f * scale, 0.008f * scale, 0.6f, new Unity.Mathematics.float4(0.45f, 0.06f, 0.05f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureWeapon.Jar:
                        Output[at + k++] = Piece(tip - up * 0.04f * scale, up, forward, 0.12f * scale, 0.05f * scale, 0.04f * scale, 1f, new Unity.Mathematics.float4(0.55f, 0.32f, 0.2f, 0f), faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Fruit:
                        Output[at + k++] = Piece(tip - up * 0.03f * scale, up, forward, 0.02f * scale, 0.045f * scale, 0.045f * scale, 1f, new Unity.Mathematics.float4(0.78f, 0.18f, 0.12f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureWeapon.Loaf:
                        Output[at + k++] = Piece(tip - right * 0.09f * scale, right, up, 0.18f * scale, 0.065f * scale, 0.06f * scale, 0.7f, new Unity.Mathematics.float4(0.62f, 0.38f, 0.16f, 0f), faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.OreSack:
                        // (A lumpy sack held to the chest in both arms, low.)
                        Output[at + k++] = Piece(between - up * 0.12f * scale, up, forward, 0.14f * scale, 0.16f * scale, 0.12f * scale, 0.85f, new Unity.Mathematics.float4(0.36f, 0.3f, 0.2f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureWeapon.Bowl:
                        // (Between the hands: a wooden bowl, herbs in it.)
                        Output[at + k++] = Piece(between - up * 0.07f * scale, up, forward, 0.07f * scale, 0.05f * scale, 0.1f * scale, 1f, new Unity.Mathematics.float4(0.5f, 0.36f, 0.22f, 0f), faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(between - up * 0.005f * scale, forward, up, 0f, 0.085f * scale, 0.085f * scale, 0.05f, new Unity.Mathematics.float4(0.32f, 0.42f, 0.2f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                    case CreatureWeapon.Dough:
                        Output[at + k++] = Piece(between - right * 0.11f * scale - up * 0.05f * scale, right, up, 0.22f * scale, 0.07f * scale, 0.07f * scale, 0.55f, new Unity.Mathematics.float4(0.9f, 0.82f, 0.62f, 0f), faded, SegmentMaterial.Cloth);
                        break;
                }

                // The other hand (Zach 11-12), after the right's (HandsCount): a shield on the forearm, tongs, a bucket.
                // (Only with something one-handed in the right: a two-handed thing frees it, Items.Equip.)
                if (sheathing || state.Outfit.OffHand == CreatureWeapon.None || offHand < 0 || k > 4)
                    return;
                k = LookParts(look, false);
                var otherHand = Output[offset + offHand];
                var oTip = Tip(otherHand);
                switch (state.Outfit.OffHand)
                {
                    case CreatureWeapon.Shield:
                    {
                        // (A goblin's scrap shield, strapped to the forearm, facing out.)
                        var fore = Output[offset + offHand - 1];
                        var on = fore.Position + Unity.Mathematics.math.mul(fore.Rotation, new Unity.Mathematics.float3(0f, 0f, fore.Length * 0.5f)) - right * 0.06f * scale;
                        Output[at + k++] = Piece(on, forward, -right, 0.01f * scale, 0.17f * scale, 0.155f * scale, 0.12f, new Unity.Mathematics.float4(0.36f, 0.3f, 0.24f, 0f), faded, SegmentMaterial.Metal);
                        break;
                    }
                    case CreatureWeapon.Tongs:
                    {
                        // (Two iron arms out from the fist, held forward and down.)
                        var hold = Unity.Mathematics.math.normalize(forward - up * 0.6f);
                        Output[at + k++] = Piece(oTip + right * 0.012f * scale, hold, right, 0.32f * scale, 0.009f * scale, 0.008f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        Output[at + k++] = Piece(oTip - right * 0.012f * scale, hold, right, 0.32f * scale, 0.009f * scale, 0.008f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        break;
                    }
                    case CreatureWeapon.Bucket:
                    {
                        // (Hanging from the hand by its handle.)
                        var hang = otherHand.Position + Unity.Mathematics.math.mul(otherHand.Rotation, new Unity.Mathematics.float3(0f, 0f, otherHand.Length * 0.8f));
                        Output[at + k++] = Piece(hang - up * 0.27f * scale, up, forward, 0.2f * scale, 0.08f * scale, 0.095f * scale, 1f, new Unity.Mathematics.float4(0.42f, 0.3f, 0.18f, 0f), faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(hang - up * 0.08f * scale - right * 0.09f * scale, right, up, 0.18f * scale, 0.007f * scale, 0.007f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        break;
                    }
                }
            }

            /// <summary>
            /// What a body has on and in its hands (P-23, P-28; the folk's kits since Zach 11-12 row 3, Dressed): a hat on
            /// the crown of the head and a pack on the back, then the hands' looks, then the armour and clothes, each
            /// only if it has any (WornCount).
            /// </summary>
            void Worn(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < WornCount(in state); i++)
                    Output[at + i] = default;
                var hands = at + HatPackCount(in state);
                if (HandsCount(in state) != 0)
                    Weapon(in state, ref b, offset, hands, faded);
                if (state.Outfit.Armoured)
                    Armour(in state, ref b, offset, hands + HandsCount(in state), faded);
                if (state.Worn == 0)
                    return;
                int head = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Head && head < 0)
                        head = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                if ((state.Worn & CreatureWorn.Hat) != 0 && head >= 0)
                {
                    var h = Output[offset + head];
                    var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                    var crown = h.Position + ax * h.Length;
                    var face = Unity.Mathematics.math.normalize(forward - ax * Unity.Mathematics.math.dot(forward, ax));
                    var straw = new Unity.Mathematics.float4(0.62f, 0.5f, 0.3f, 0f);
                    var band = new Unity.Mathematics.float4(0.36f, 0.24f, 0.16f, 0f);
                    Output[at] = Piece(crown - ax * r * 0.1f, face, ax, 0f, r * 1.9f, r * 1.9f, 0.1f, straw, faded, SegmentMaterial.Cloth);
                    Output[at + 1] = Piece(crown + ax * r * 0.25f, face, ax, 0f, r * 0.95f, r * 0.95f, 0.8f, band, faded, SegmentMaterial.Cloth);
                }
                if ((state.Worn & CreatureWorn.Pack) != 0 && chest >= 0)
                {
                    var c = Output[offset + chest];
                    var mid = c.Position + Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, c.Length * 0.5f));
                    var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                    var at0 = mid - forward * (Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd) + 0.08f * scale) - up * 0.2f * scale;
                    Output[at + HatPackCount(in state) - 1] = Piece(at0, up, forward, 0.42f * scale, 0.16f * scale, 0.14f * scale, 0.7f, new Unity.Mathematics.float4(0.42f, 0.33f, 0.2f, 0f), faded, SegmentMaterial.Leather);
                }
            }

            /// <summary>
            /// What stands at a worker's post (see GearCount; A-33): a baker's kneading table, floured, with a loaf at its
            /// far end; a smith's bar glowing in its tongs; a woodcutter's chopping block with a log on it and a split
            /// piece beside; a well keeper's rope down into the well and the bucket coming up on it.
            /// </summary>
            void Gear(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < GearCount(in state); i++)
                    Output[at + i] = default;
                if (!b.Upright || !b.Hands)
                    return;
                int hand = -1, offHand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Role == LimbRole.Arm && part.Side < 0f && offHand < 0)
                        offHand = part.FirstSegment + part.SegmentCount - 1;
                }
                var k = 0;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wood = new Unity.Mathematics.float4(0.36f, 0.24f, 0.13f, 0f);
                var job = FolkJobs.Of(in state);
                // (A hand's tip, and the point between both hands.)
                var tip = hand >= 0 ? Tip(Output[offset + hand]) : state.Position;
                var offTip = offHand >= 0 ? Tip(Output[offset + offHand]) : tip;
                var between = (tip + offTip) * 0.5f;
                var ground = state.Position;
                if (state.Task == CreatureTask.Tend && job == FolkJob.Baker)
                {
                    // The kneading table under the dough, floured: a flat top on a stout leg.
                    var tableTop = new Unity.Mathematics.float3(between.x, between.y - 0.1f * scale, between.z) - forward * 0.05f * scale;
                    var topLevel = Unity.Mathematics.math.max(tableTop.y, ground.y + 0.4f * scale);
                    var centre = new Unity.Mathematics.float3(tableTop.x, topLevel, tableTop.z);
                    Output[at + k++] = Piece(centre - forward * 0.05f * scale, forward, up, 0.5f * scale, 0.4f * scale, 0.4f * scale, 0.1f, new Unity.Mathematics.float4(0.6f, 0.48f, 0.32f, 0f), faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(new Unity.Mathematics.float3(centre.x, ground.y, centre.z) + forward * 0.2f * scale, up, forward, topLevel - ground.y, 0.07f * scale, 0.07f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    // A finished loaf at the table's far end.
                    Output[at + k++] = Piece(centre + forward * 0.25f * scale + right * 0.2f * scale + up * 0.05f * scale, right, up, 0.18f * scale, 0.065f * scale, 0.06f * scale, 0.7f,
                                             new Unity.Mathematics.float4(0.62f, 0.38f, 0.16f, 0f), faded, SegmentMaterial.Wood);
                }
                else if (state.Task == CreatureTask.Tend && job == FolkJob.WellKeeper && hand >= 0)
                {
                    // The well's rope, from the higher hand down into the well in front, and the bucket coming up on it.
                    var top = tip.y > offTip.y ? tip : offTip;
                    var foot = state.Position + forward * 0.75f * scale;
                    var drop = top - foot;
                    var ropeLength = Unity.Mathematics.math.length(drop) + 0.4f * scale;
                    var down = -Unity.Mathematics.math.normalize(drop);
                    Output[at + k++] = Piece(top + up * 0.3f * scale, down, forward, ropeLength, 0.011f * scale, 0.011f * scale, 1f, new Unity.Mathematics.float4(0.6f, 0.52f, 0.36f, 0f), faded, SegmentMaterial.Cloth);
                    var bucketAt = state.Position + forward * 0.6f * scale + up * (0.25f + 0.2f * Unity.Mathematics.math.sin(state.Clock * 0.4f)) * scale;
                    Output[at + k++] = Piece(bucketAt, up, forward, 0.2f * scale, 0.08f * scale, 0.095f * scale, 1f, new Unity.Mathematics.float4(0.42f, 0.3f, 0.18f, 0f), faded, SegmentMaterial.Wood);
                }
                else if (state.Task == CreatureTask.Hammer && offHand >= 0)
                {
                    // The glowing bar in its tongs (the tongs are its kit's, in the other hand: the Weapon pass's), on the anvil.
                    var hold = Unity.Mathematics.math.normalize(forward - up * 0.6f);
                    Output[at + k++] = Piece(offTip + hold * 0.3f * scale, right, up, 0.2f * scale, 0.018f * scale, 0.018f * scale, 1f, new Unity.Mathematics.float4(1f, 0.42f, 0.08f, 0f), faded, SegmentMaterial.Metal);
                }
                else if (state.Task == CreatureTask.Chop && job != FolkJob.Farmer)
                {
                    // A chopping block in front with a log stood on it, and a split piece beside.
                    var block = ground + forward * 0.75f * scale;
                    var bark = new Unity.Mathematics.float4(0.32f, 0.22f, 0.14f, 0f);
                    Output[at + k++] = Piece(block, up, forward, 0.35f * scale, 0.2f * scale, 0.2f * scale, 1f, bark, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(block + up * 0.35f * scale, up, forward, 0.3f * scale, 0.1f * scale, 0.1f * scale, 1f, new Unity.Mathematics.float4(0.7f, 0.56f, 0.36f, 0f), faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(block + right * 0.35f * scale + up * 0.05f * scale, Unity.Mathematics.math.normalize(forward + right * 0.5f), up, 0.3f * scale, 0.06f * scale, 0.06f * scale, 0.6f, bark, faded, SegmentMaterial.Wood);
                }
            }

            /// <summary>A dyed item's colour (ItemDef.Dyed: a sash, an elf's crest): one of the folk's in its folk's guard
            /// colour (GuardSash), a player's in its folk's cloth (P-28 row 3).</summary>
            static Unity.Mathematics.float4 Dye(in CreatureState state, ref CreatureBlob b) =>
                state.Mode == CreatureMode.Puppet ? CreatureArmour.ClothMain(b.GuardKit) : b.GuardSash;

            /// <summary>The end of a limb segment (a hand's fingertips).</summary>
            static Unity.Mathematics.float3 Tip(in SegmentInstance s) =>
                s.Position + Unity.Mathematics.math.mul(s.Rotation, new Unity.Mathematics.float3(0f, 0f, s.Length));

            /// <summary>
            /// The chunk of honeycomb a raiding bear knocked down: falling from up at the comb in its first half second,
            /// then lying on the ground at its nose while it licks at it.
            /// </summary>
            void Honey(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                var head = -1;
                for (int i = 0; i < b.Parts.Length && head < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Head)
                        head = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                if (head < 0)
                    return;
                var h = Output[offset + head];
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var nose = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length)) + forward * 0.08f;
                var ground = Terrain.SampleHeight(nose.xz);
                var fall = Unity.Mathematics.math.saturate(state.StageTime / 0.5f);
                var y = Unity.Mathematics.math.lerp(ground + 2.5f, ground + 0.05f, fall * fall);
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                Output[at] = Piece(new Unity.Mathematics.float3(nose.x, y, nose.z) - right * 0.1f, right, new Unity.Mathematics.float3(0f, 1f, 0f),
                                   0.2f, 0.11f, 0.09f, 0.55f, new Unity.Mathematics.float4(0.75f, 0.45f, 0.06f, 0.15f), faded, SegmentMaterial.Body);
            }

            /// <summary>
            /// A rolled message: a little roll on a messenger bird's leg, under its body; in the hands of one reading it,
            /// the roll across between the hands and the sheet hanging open from it, towards their face.
            /// </summary>
            void Letter(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                Output[at + 1] = default;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.1f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var paper = new Unity.Mathematics.float4(0.62f, 0.55f, 0.4f, 0f);
                if (!b.Upright)
                {
                    var body = Output[offset];
                    var leg = body.Position + Unity.Mathematics.math.mul(body.Rotation, new Unity.Mathematics.float3(0f, 0f, body.Length * 0.5f)) -
                              up * Unity.Mathematics.math.max(body.RadiusStart, body.RadiusEnd) * 0.9f;
                    Output[at] = Piece(leg - right * 0.025f, right, up, 0.05f, 0.012f, 0.012f, 1f, paper, faded, SegmentMaterial.Cloth);
                    return;
                }
                int hand = -1;
                for (int i = 0; i < b.Parts.Length && hand < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                if (hand < 0)
                    return;
                var h = Output[offset + hand];
                var grip = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.6f));
                // (The roll from this hand across to the other; the sheet down from it, tipped back towards the reader.)
                var rollFrom = grip + up * 0.02f * scale;
                Output[at] = Piece(rollFrom, -right, up, 0.2f * scale, 0.018f * scale, 0.018f * scale, 1f, paper, faded, SegmentMaterial.Cloth);
                var down = Unity.Mathematics.math.normalize(-up + forward * 0.35f);
                Output[at + 1] = Piece(rollFrom - right * 0.1f * scale, down, forward, 0.16f * scale, 0.09f * scale, 0.09f * scale, 0.08f,
                                       paper * 1.1f, faded, SegmentMaterial.Cloth);
            }

            /// <summary>A traveller's walking staff, planted ahead in the right hand, and a pack on its back.</summary>
            void Traveller(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                Output[at + 1] = default;
                int hand = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0 || chest < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var upward = new Unity.Mathematics.float3(0f, 1f, 0f);
                var h = Output[offset + hand];
                var grip = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.5f));
                var dir = Unity.Mathematics.math.normalize(upward + forward * 0.18f);
                var length = 1.7f * scale;
                Output[at] = Piece(grip - dir * length * 0.65f, dir, forward, length, 0.02f * scale, 0.016f * scale, 1f,
                                   new Unity.Mathematics.float4(0.24f, 0.17f, 0.09f, 0f), faded, SegmentMaterial.Wood);
                var torso = Output[offset + chest];
                var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                var back = -Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                Output[at + 1] = Piece(torso.Position + back * r * 1.15f, axis, back, torso.Length * 0.9f, r * 0.7f, r * 0.62f, 0.7f,
                                       new Unity.Mathematics.float4(0.3f, 0.2f, 0.11f, 0f), faded, SegmentMaterial.Leather);
            }

            /// <summary>
            /// A miner's gear: its pick across its back on the way in and out, in its hands swinging at the ore face
            /// (the head's two points out either side of the haft's end); and carrying ore out, a full sack hugged to its
            /// chest.
            /// </summary>
            void Miner(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < MinerParts; i++)
                    Output[at + i] = default;
                int hand = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                if (hand < 0 || chest < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var wood = new Unity.Mathematics.float4(0.18f, 0.12f, 0.06f, 0f);
                var iron = new Unity.Mathematics.float4(0.3f, 0.3f, 0.32f, 0f);
                var torso = Output[offset + chest];
                var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                var length = 0.8f * scale;
                float3 butt, dir;
                if (state.Mode == CreatureMode.Work && state.Stage == CreatureAnimator.MinerAtFace && state.Held == HeldItem.Pickaxe)
                {
                    // In both hands, held at the end of the haft, along the arms' swing.
                    var h = Output[offset + hand];
                    dir = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    butt = h.Position + dir * h.Length * 0.4f - dir * 0.08f * scale;
                }
                else
                {
                    // Across the back, from low on the left to high on the right.
                    dir = Unity.Mathematics.math.normalize(axis + right * 0.75f);
                    butt = torso.Position + axis * torso.Length * 0.5f - forward * r * 1.3f - dir * length * 0.5f;
                }
                var tip = butt + dir * length;
                // (In the hands the head is in the swing's plane; worn on the back it lies across, pointing to either side.)
                var swinging = state.Mode == CreatureMode.Work && state.Stage == CreatureAnimator.MinerAtFace && state.Held == HeldItem.Pickaxe;
                var across = swinging ? Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(dir, right), forward)
                                      : Unity.Mathematics.math.normalizesafe(right - dir * Unity.Mathematics.math.dot(right, dir), right);
                // (Only a miner has the pick on its back: a bat folk bringing goods home carries just the sack, #35.)
                if (state.Task == CreatureTask.Mine || state.Held == HeldItem.Pickaxe)
                {
                    Output[at] = Piece(butt, dir, right, length, 0.02f * scale, 0.022f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    // (A pick's head: a bar across the haft's top, its two points curving a little back towards the handle.)
                    Output[at + 1] = Piece(tip, Unity.Mathematics.math.normalize(across - dir * 0.28f), right, 0.24f * scale, 0.03f * scale, 0.004f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                    Output[at + 2] = Piece(tip, Unity.Mathematics.math.normalize(-across - dir * 0.28f), right, 0.2f * scale, 0.03f * scale, 0.006f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                }
                if (state.Held == HeldItem.OreSack)
                {
                    // A lumpy sack of ore held to the chest, low.
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var at3 = torso.Position + front * r * 1.25f;
                    Output[at + 3] = state.Task == CreatureTask.Fish
                        // (A fisher's fish crate carried out along the wharf, A-73: wider, squarer, weathered plank.)
                        ? Piece(at3 - axis * 0.1f * scale, axis, front, 0.22f * scale, 0.2f * scale, 0.2f * scale, 0.35f,
                                new Unity.Mathematics.float4(0.5f, 0.37f, 0.22f, 0f), faded, SegmentMaterial.Wood)
                        : Piece(at3 - axis * 0.06f * scale, axis, front, 0.14f * scale, 0.16f * scale, 0.12f * scale, 0.85f,
                                new Unity.Mathematics.float4(0.36f, 0.3f, 0.2f, 0f), faded, SegmentMaterial.Cloth);
                }
            }

            /// <summary>
            /// A child's plaything in its right hand: a crooked stick held out ahead and down, or a stone in the fist
            /// (gone from the hand from the moment it's thrown until another's picked up).
            /// </summary>
            void Plaything(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                var hand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                    {
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (hand < 0)
                    return;
                var segment = Output[offset + hand];
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length * 0.6f));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                if (state.Held == HeldItem.Stone)
                {
                    if (state.Action == IdleAction.Throw && state.ActionTime > ThrowRelease && state.ActionTime < 0.9f)
                        return;
                    Output[at] = new SegmentInstance { Position = grip, Rotation = segment.Rotation, Length = 0.03f * scale, RadiusStart = 0.06f * scale, RadiusEnd = 0.05f * scale, Flatten = 0.8f, Color = new Unity.Mathematics.float4(0.45f, 0.44f, 0.42f, 0f), Pad = faded };
                    return;
                }
                // The stick goes on from the hand the way the forearm points, dipping a little towards the ground.
                var along = Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                var dir = Unity.Mathematics.math.normalizesafe(along + new Unity.Mathematics.float3(0f, -0.25f, 0f));
                Output[at] = new SegmentInstance
                {
                    Position = grip - dir * 0.06f * scale,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = 0.8f * scale,
                    RadiusStart = 0.014f * scale,
                    RadiusEnd = 0.009f * scale,
                    Flatten = 0.85f,
                    Color = new Unity.Mathematics.float4(0.24f, 0.16f, 0.08f, 0f), Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
            }

            /// <summary>
            /// A fisher's gear: the rod held out over the water (swung back over the shoulder and through in a cast,
            /// lifted high landing a fish), its line down to a float bobbing on the water where it fishes (WorkFace) or,
            /// landing one, to a fish wriggling on the hook; and a basket on the ground beside it.
            /// </summary>
            void Rod(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded, int id)
            {
                for (int i = 0; i < RodParts; i++)
                    Output[at + i] = default;
                var hand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                    {
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (hand < 0)
                    return;
                var segment = Output[offset + hand];
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length * 0.55f));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                // The rod's rise: out over the water; back over the shoulder in a cast; high, landing a fish.
                var casting = state.Stage == 2 ? CreatureAnimator.CastBack(state.ActionTime) : 0f;
                var rise = state.Stage == 3 ? 1.05f : Unity.Mathematics.math.lerp(0.55f, 2.3f, casting);
                var dir = forward * Unity.Mathematics.math.cos(rise) + new Unity.Mathematics.float3(0f, Unity.Mathematics.math.sin(rise), 0f);
                var length = 2.6f * scale;
                var butt = grip - dir * 0.35f * scale;
                var tip = butt + dir * length;
                var dark = new Unity.Mathematics.float4(0.18f, 0.12f, 0.06f, 0f);
                Output[at] = new SegmentInstance { Position = butt, Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, forward), Length = length, RadiusStart = 0.013f * scale, RadiusEnd = 0.004f * scale, Flatten = 1f, Color = dark, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f), Pad = faded };
                // The line: to the float on the water (not while the cast is in the air), or to a fish on the hook.
                var line = new Unity.Mathematics.float4(0.85f, 0.85f, 0.8f, 0f);
                Unity.Mathematics.float3 end;
                if (state.Stage == 3)
                {
                    end = tip - new Unity.Mathematics.float3(0f, 0.7f * scale, 0f);
                    var wriggle = Unity.Mathematics.math.sin(Time * 14f + id) * 0.6f;
                    var along = Unity.Mathematics.math.mul(Unity.Mathematics.quaternion.RotateY(wriggle), forward);
                    Output[at + 3] = new SegmentInstance
                    {
                        Position = end - along * 0.12f * scale,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, new Unity.Mathematics.float3(0f, 1f, 0f)),
                        Length = 0.24f * scale,
                        RadiusStart = 0.035f * scale,
                        RadiusEnd = 0.012f * scale,
                        Flatten = 0.55f,
                        Color = new Unity.Mathematics.float4(0.55f, 0.6f, 0.62f, 0.1f),
                        Pad = faded
                    };
                }
                else if (state.Stage == 2 && state.ActionTime < 0.75f)
                    end = tip;
                else
                {
                    var water = Terrain.SampleGround(state.WorkFace);
                    end = new Unity.Mathematics.float3(state.WorkFace.x, water.WaterSurface + 0.02f + 0.015f * Unity.Mathematics.math.sin(Time * 2.1f + id), state.WorkFace.y);
                    Output[at + 2] = new SegmentInstance
                    {
                        Position = end - new Unity.Mathematics.float3(0f, 0.03f, 0f),
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(new Unity.Mathematics.float3(0f, 1f, 0f), forward),
                        Length = 0.06f,
                        RadiusStart = 0.025f,
                        RadiusEnd = 0.025f,
                        Flatten = 1f,
                        Color = new Unity.Mathematics.float4(0.85f, 0.12f, 0.08f, 0f),
                        Pad = faded
                    };
                }
                var span = end - tip;
                var reach = Unity.Mathematics.math.length(span);
                if (reach > 0.02f)
                    Output[at + 1] = new SegmentInstance { Position = tip, Rotation = Unity.Mathematics.quaternion.LookRotationSafe(span / reach, forward), Length = reach, RadiusStart = 0.0035f, RadiusEnd = 0.0035f, Flatten = 1f, Color = line, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Cloth, 0f, 0f, 0f), Pad = faded };
                // The basket, on the ground at its side: an open wicker basket, a ring of staves flaring a little to a
                // rim, dark inside.
                var spot = state.Position.xz + right.xz * 0.45f * scale - forward.xz * 0.15f * scale;
                var foot = new Unity.Mathematics.float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                var radius = 0.14f * scale;
                var height = 0.2f * scale;
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wicker = new Unity.Mathematics.float4(0.42f, 0.3f, 0.14f, 0f);
                var weave = new Unity.Mathematics.float4(0.5f, 0.37f, 0.18f, 0f);
                for (int k = 0; k < BasketStaves; k++)
                {
                    var a = (k + 0.5f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var outward = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var bottom = foot + outward * radius * 0.85f;
                    var top = foot + outward * radius * 1.12f + up * height;
                    Output[at + 4 + k] = new SegmentInstance
                    {
                        Position = bottom,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(Unity.Mathematics.math.normalize(top - bottom), outward),
                        Length = Unity.Mathematics.math.distance(top, bottom),
                        RadiusStart = 0.045f * scale,
                        RadiusEnd = 0.05f * scale,
                        Flatten = 0.45f,
                        Color = wicker, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                        Pad = faded
                    };
                    var b0 = (k + 0f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var b1 = (k + 1f) * 2f * Unity.Mathematics.math.PI / BasketStaves;
                    var r0 = foot + up * height + new Unity.Mathematics.float3(Unity.Mathematics.math.cos(b0), 0f, Unity.Mathematics.math.sin(b0)) * radius * 1.14f;
                    var r1 = foot + up * height + new Unity.Mathematics.float3(Unity.Mathematics.math.cos(b1), 0f, Unity.Mathematics.math.sin(b1)) * radius * 1.14f;
                    Output[at + 4 + BasketStaves + k] = new SegmentInstance
                    {
                        Position = r0,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(Unity.Mathematics.math.normalize(r1 - r0), up),
                        Length = Unity.Mathematics.math.distance(r0, r1),
                        RadiusStart = 0.016f * scale,
                        RadiusEnd = 0.016f * scale,
                        Flatten = 1f,
                        Color = weave, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                        Pad = faded
                    };
                }
                // The dark inside, just under the rim, and the base it stands on.
                Output[at + 4 + BasketStaves * 2] = new SegmentInstance
                {
                    Position = foot + up * (height * 0.85f) - forward * radius,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(forward, up),
                    Length = radius * 2f,
                    RadiusStart = radius,
                    RadiusEnd = radius,
                    Flatten = 0.05f,
                    Color = new Unity.Mathematics.float4(0.1f, 0.07f, 0.03f, 0f), Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
                Output[at + 4 + BasketStaves * 2 + 1] = new SegmentInstance
                {
                    Position = foot + up * 0.01f - forward * radius,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(forward, up),
                    Length = radius * 2f,
                    RadiusStart = radius * 0.9f,
                    RadiusEnd = radius * 0.9f,
                    Flatten = 0.08f,
                    Color = wicker, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Wood, 0f, 0f, 0f),
                    Pad = faded
                };
            }
        }
    }
}
