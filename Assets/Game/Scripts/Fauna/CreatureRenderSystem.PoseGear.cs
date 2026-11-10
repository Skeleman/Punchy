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
        /// <summary>A player's weapon (P-28): at most a haft, a head or blade, a guard and, for a bow, its string and arrow.</summary>
        const int WeaponParts = 7;

        partial struct PoseJob
        {
            /// <summary>
            /// A player's weapon in hand (P-28 row 1; CreatureState.Outfit), out of the right fist along the line its arm
            /// gives it, so it follows the arm through a swing (CreatureAnimator's weapon arm); a bow in the left hand,
            /// its string drawn back to the right as the swing goes. Too small on screen to tell apart, it isn't drawn.
            /// </summary>
            void Weapon(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
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
                var h = Output[offset + hand];
                var grip = Tip(h);
                var p = state.Outfit.Swing == 0 ? 0f : (state.Outfit.Swing - 1) / 254f;
                // (Held up and a little forward, ready; a swing tips it back over the shoulder, then down and through,
                // as the arm goes: CreatureAnimator's weapon arm, the same phases.)
                var lift = p < 0.55f ? Unity.Mathematics.math.smoothstep(0f, 0.55f, p) : 1f - Unity.Mathematics.math.smoothstep(0.55f, 0.72f, p);
                var strike = Unity.Mathematics.math.smoothstep(0.6f, 0.72f, p) * (1f - Unity.Mathematics.math.smoothstep(0.8f, 1f, p));
                var tilt = 0.45f - 1.1f * lift + 1.6f * strike;
                var dir = up * Unity.Mathematics.math.cos(tilt) + forward * Unity.Mathematics.math.sin(tilt);
                var face = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.cross(dir, right), forward);
                var k = 0;
                switch (state.Outfit.Weapon)
                {
                    case CreatureWeapon.Club:
                        Output[at + k++] = Piece(grip - dir * 0.08f * scale, dir, face, 0.55f * scale, 0.024f * scale, 0.05f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(grip + dir * 0.4f * scale, dir, face, 0.12f * scale, 0.06f * scale, 0.045f * scale, 0.85f, wood, faded, SegmentMaterial.Wood);
                        break;
                    case CreatureWeapon.Sword:
                        Output[at + k++] = Piece(grip - dir * 0.08f * scale, dir, face, 0.13f * scale, 0.017f * scale, 0.017f * scale, 1f, leather, faded, SegmentMaterial.Leather);
                        Output[at + k++] = Piece(grip + dir * 0.06f * scale - face * 0.09f * scale, face, dir, 0.18f * scale, 0.014f * scale, 0.014f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                        Output[at + k++] = Piece(grip + dir * 0.06f * scale, dir, face, 0.66f * scale, 0.032f * scale, 0.006f * scale, 0.25f, iron, faded, SegmentMaterial.Metal);
                        break;
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
                }
            }

            /// <summary>A worker's gear (see GearParts): its tool while at work, and its apron or hat.</summary>
            /// <summary>What a player's body has on (P-23): a hat on the crown of the head, a pack on the back.</summary>
            void Worn(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < WornParts + WeaponParts; i++)
                    Output[at + i] = default;
                if (state.Outfit.Weapon != CreatureWeapon.None)
                    Weapon(in state, ref b, offset, at + WornParts, faded);
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
                    Output[at + 2] = Piece(at0, up, forward, 0.42f * scale, 0.16f * scale, 0.14f * scale, 0.7f, new Unity.Mathematics.float4(0.42f, 0.33f, 0.2f, 0f), faded, SegmentMaterial.Leather);
                }
            }

            void Gear(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < GearParts; i++)
                    Output[at + i] = default;
                if (!b.Upright || !b.Hands)
                    return;
                int hand = -1, offHand = -1, head = -1, chest = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role == LimbRole.Arm && part.Side > 0f && hand < 0)
                        hand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Role == LimbRole.Arm && part.Side < 0f && offHand < 0)
                        offHand = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Role == LimbRole.Head && head < 0)
                        head = part.FirstSegment + part.SegmentCount - 1;
                    else if (part.Parent < 0 && part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && chest < 0)
                        chest = part.FirstSegment + part.SegmentCount - 1;
                }
                var k = 0;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wood = new Unity.Mathematics.float4(0.36f, 0.24f, 0.13f, 0f);
                var iron = new Unity.Mathematics.float4(0.3f, 0.31f, 0.33f, 0f);
                var atWork = state.Mode == CreatureMode.Work && state.Stage != 0;
                var job = FolkJobs.Of(in state);
                var tend = state.Task == CreatureTask.Tend;
                var baker = tend && job == FolkJob.Baker;
                var healer = tend && job == FolkJob.Healer;
                var wellKeeper = tend && job == FolkJob.WellKeeper;
                var cloth = new Unity.Mathematics.float4(0.86f, 0.84f, 0.78f, 0f);
                // (A hand's tip, and the point between both hands.)
                var tip = hand >= 0 ? Tip(Output[offset + hand]) : state.Position;
                var offTip = offHand >= 0 ? Tip(Output[offset + offHand]) : tip;
                var between = (tip + offTip) * 0.5f;

                // The tool in hand, at work: along the hand's own line, so it swings with the arm (TaskPose).
                if (atWork && state.Task == CreatureTask.Watch && hand >= 0 && state.Held == HeldItem.None)
                {
                    // A gatekeeper's spear, upright in its fist, butt near the ground.
                    var butt = tip - up * 0.95f * scale + forward * 0.02f * scale;
                    Output[at + k++] = Piece(butt, up, forward, 2f * scale, 0.017f * scale, 0.015f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(butt + up * 2f * scale, up, forward, 0.2f * scale, 0.035f * scale, 0.004f * scale, 0.3f, iron, faded, SegmentMaterial.Metal);
                }
                else if (atWork && (baker || healer) && hand >= 0 && offHand >= 0)
                {
                    // Under the hands: a baker's lump of dough, a healer's bowl.
                    if (baker)
                        Output[at + k++] = Piece(between - right * 0.11f * scale - up * 0.05f * scale, right, up, 0.22f * scale, 0.07f * scale, 0.07f * scale, 0.55f,
                                                 new Unity.Mathematics.float4(0.9f, 0.82f, 0.62f, 0f), faded, SegmentMaterial.Cloth);
                    else
                    {
                        var bowl = new Unity.Mathematics.float4(0.5f, 0.36f, 0.22f, 0f);
                        Output[at + k++] = Piece(between - up * 0.07f * scale, up, forward, 0.07f * scale, 0.05f * scale, 0.1f * scale, 1f, bowl, faded, SegmentMaterial.Wood);
                        Output[at + k++] = Piece(between - up * 0.005f * scale, forward, up, 0f, 0.085f * scale, 0.085f * scale, 0.05f,
                                                 new Unity.Mathematics.float4(0.32f, 0.42f, 0.2f, 0f), faded, SegmentMaterial.Cloth);
                    }
                }
                else if (atWork && wellKeeper && hand >= 0)
                {
                    // The well's rope, from the higher hand down into the well in front, and the bucket coming up on it.
                    var top = tip.y > offTip.y ? tip : offTip;
                    var foot = state.Position + forward * 0.75f * scale;
                    var drop = top - foot;
                    var ropeLength = Unity.Mathematics.math.length(drop) + 0.4f * scale;
                    var down = -Unity.Mathematics.math.normalize(drop);
                    var rope = new Unity.Mathematics.float4(0.6f, 0.52f, 0.36f, 0f);
                    Output[at + k++] = Piece(top + up * 0.3f * scale, down, forward, ropeLength, 0.011f * scale, 0.011f * scale, 1f, rope, faded, SegmentMaterial.Cloth);
                    var bucketAt = state.Position + forward * 0.6f * scale + up * (0.25f + 0.2f * Unity.Mathematics.math.sin(state.Clock * 0.4f)) * scale;
                    Output[at + k++] = Piece(bucketAt, up, forward, 0.2f * scale, 0.08f * scale, 0.095f * scale, 1f, new Unity.Mathematics.float4(0.42f, 0.3f, 0.18f, 0f), faded, SegmentMaterial.Wood);
                }
                else if (atWork && hand >= 0 && (state.Task == CreatureTask.Hammer || state.Task == CreatureTask.Chop || state.Task == CreatureTask.Hermit && state.Stage != 2))
                {
                    var h = Output[offset + hand];
                    var along = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var grip = h.Position + along * h.Length * 0.6f;
                    // (The haft out of the fist, crossing the hand's line: down from the grip and on beyond it.)
                    var side = Unity.Mathematics.math.normalize(Unity.Mathematics.math.cross(along, right) + along * 0.2f);
                    // (W-57: a hermit's tool is the landmark's Work, in Stage: 1 axe, 3 broom, 4 shovel, 5 hammer.)
                    var hermitStage = state.Task == CreatureTask.Hermit ? state.Stage : 0;
                    var broom = hermitStage == 3;
                    var spade = hermitStage == 4;
                    var axe = state.Task == CreatureTask.Chop || hermitStage == 1;
                    // (A farmer's is a hoe: a longer haft, a flat blade set across it.)
                    var hoe = state.Task == CreatureTask.Chop && job == FolkJob.Farmer || broom || spade;
                    var haft = (broom ? 1.25f : spade ? 1f : hoe ? 1.1f : axe ? 0.75f : 0.32f) * scale;
                    var butt = grip - side * haft * (hoe ? 0.35f : 0.15f);
                    Output[at + k++] = Piece(butt, side, along, haft, 0.016f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    var top = butt + side * haft;
                    Output[at + k++] = broom
                        ? Piece(top - side * 0.02f * scale, along, side, 0.3f * scale, 0.05f * scale, 0.14f * scale, 0.7f, new Unity.Mathematics.float4(0.76f, 0.62f, 0.33f, 0f), faded, SegmentMaterial.Wood)
                        : spade
                        ? Piece(top, side, along, 0.26f * scale, 0.12f * scale, 0.02f * scale, 0.8f, iron, faded, SegmentMaterial.Metal)
                        : hoe
                        ? Piece(top - side * 0.02f * scale, along, side, 0.16f * scale, 0.06f * scale, 0.07f * scale, 0.1f, iron, faded, SegmentMaterial.Metal)
                        : axe
                        ? Piece(top - side * 0.12f * scale, along, side, 0.14f * scale, 0.05f * scale, 0.08f * scale, 0.12f, iron, faded, SegmentMaterial.Metal)
                        : Piece(top - along * 0.045f * scale, along, side, 0.09f * scale, 0.024f * scale, 0.024f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                }
                else if (atWork && offHand >= 0 && state.Task == CreatureTask.Tend)
                {
                    // A bucket hanging from the other hand.
                    var h = Output[offset + offHand];
                    var grip = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.8f));
                    var bucket = new Unity.Mathematics.float4(0.42f, 0.3f, 0.18f, 0f);
                    Output[at + k++] = Piece(grip - up * 0.27f * scale, up, forward, 0.2f * scale, 0.08f * scale, 0.095f * scale, 1f, bucket, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(grip - up * 0.08f * scale - right * 0.09f * scale, right, up, 0.18f * scale, 0.007f * scale, 0.007f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                }
                k = 2;

                // An apron down the front: smiths (leather) and stallholders (cloth).
                if (chest >= 0 && (state.Task == CreatureTask.Hammer || state.Task == CreatureTask.Gesture || baker))
                {
                    var c = Output[offset + chest];
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var smith = state.Task == CreatureTask.Hammer;
                    // (Leather for a smith, floury white for a baker, plain cloth for a stallholder.)
                    var colour = smith ? new Unity.Mathematics.float4(0.14f, 0.085f, 0.05f, 0f) : baker ? cloth : new Unity.Mathematics.float4(0.62f, 0.58f, 0.5f, 0f);
                    var topAt = c.Position + axis * c.Length * 0.55f + front * r * 0.72f;
                    var length = c.Length * 0.6f + r * 1.2f;
                    Output[at + k++] = Piece(topAt, -axis, front, length, r * 0.62f, r * 0.7f, 0.035f, colour, faded, smith ? SegmentMaterial.Leather : SegmentMaterial.Cloth);
                }
                else
                    k++;

                // A hat: a straw hat, wide-brimmed, for outdoor work; a fisher's floppy one. (Under it, the hair stays.)
                // (A-77: never on cave folk, pygmies and bat folk, who go bareheaded.)
                if (b.CaveDweller)
                    k += 2;
                else if (head >= 0 && baker)
                {
                    // A baker's cap: a soft white crown, no brim.
                    var h = Output[offset + head];
                    var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                    var face = Unity.Mathematics.math.normalize(forward - ax * Unity.Mathematics.math.dot(forward, ax));
                    Output[at + k++] = Piece(h.Position + ax * (h.Length + r * 0.15f), ax, face, r * 0.75f, r * 1.02f, r * 1.15f, 1f, cloth, faded, SegmentMaterial.Cloth);
                    k++;
                }
                else if (head >= 0 && !healer && (state.Task == CreatureTask.Chop || state.Task == CreatureTask.Tend || state.Task == CreatureTask.Fish || state.Task == CreatureTask.Hermit))
                {
                    var h = Output[offset + head];
                    var ax = Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                    var crown = h.Position + ax * h.Length;
                    var face = Unity.Mathematics.math.normalize(forward - ax * Unity.Mathematics.math.dot(forward, ax));
                    var fisher = state.Task == CreatureTask.Fish;
                    var colour = fisher ? new Unity.Mathematics.float4(0.34f, 0.38f, 0.3f, 0f) : new Unity.Mathematics.float4(0.82f, 0.7f, 0.42f, 0f);
                    var material = fisher ? SegmentMaterial.Cloth : SegmentMaterial.Wood;
                    var brim = (fisher ? 1.6f : 2.1f) * r;
                    Output[at + k++] = Piece(crown - ax * r * 0.3f, face, ax, 0f, brim, brim, 0.07f, colour, faded, material);
                    Output[at + k++] = Piece(crown - ax * r * 0.4f, ax, face, r * (fisher ? 0.55f : 0.7f), r * 1.05f, r * 0.85f, 1f, colour, faded, material);
                }
                k = 5;

                // Extras at the post (A-33).
                var ground = state.Position;
                if (atWork && baker)
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
                else if (healer && chest >= 0)
                {
                    // A satchel at the hip on a strap across the chest (worn on and off the post).
                    var c = Output[offset + chest];
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var leather = new Unity.Mathematics.float4(0.4f, 0.25f, 0.13f, 0f);
                    var hip = c.Position + right * r * 1.05f + forward * r * 0.2f;
                    Output[at + k++] = Piece(hip - axis * 0.06f * scale, axis, right, 0.2f * scale, 0.09f * scale, 0.1f * scale, 0.5f, leather, faded, SegmentMaterial.Leather);
                    var shoulder = c.Position + axis * c.Length - right * r * 0.6f + forward * r * 0.9f;
                    var strap = hip + axis * 0.12f * scale - shoulder;
                    Output[at + k++] = Piece(shoulder, Unity.Mathematics.math.normalize(strap), forward, Unity.Mathematics.math.length(strap), 0.02f * scale, 0.02f * scale, 0.3f, leather, faded, SegmentMaterial.Leather);
                }
                else if (atWork && state.Task == CreatureTask.Hammer && offHand >= 0)
                {
                    // Tongs in the other hand holding a glowing bar on the anvil.
                    var hold = Unity.Mathematics.math.normalize(forward - up * 0.6f);
                    Output[at + k++] = Piece(offTip + right * 0.012f * scale, hold, right, 0.32f * scale, 0.009f * scale, 0.008f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                    Output[at + k++] = Piece(offTip - right * 0.012f * scale, hold, right, 0.32f * scale, 0.009f * scale, 0.008f * scale, 1f, iron, faded, SegmentMaterial.Metal);
                    Output[at + k++] = Piece(offTip + hold * 0.3f * scale, right, up, 0.2f * scale, 0.018f * scale, 0.018f * scale, 1f, new Unity.Mathematics.float4(1f, 0.42f, 0.08f, 0f), faded, SegmentMaterial.Metal);
                }
                else if (atWork && state.Task == CreatureTask.Chop && job != FolkJob.Farmer)
                {
                    // A chopping block in front with a log stood on it, and a split piece beside.
                    var block = ground + forward * 0.75f * scale;
                    var bark = new Unity.Mathematics.float4(0.32f, 0.22f, 0.14f, 0f);
                    Output[at + k++] = Piece(block, up, forward, 0.35f * scale, 0.2f * scale, 0.2f * scale, 1f, bark, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(block + up * 0.35f * scale, up, forward, 0.3f * scale, 0.1f * scale, 0.1f * scale, 1f, new Unity.Mathematics.float4(0.7f, 0.56f, 0.36f, 0f), faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(block + right * 0.35f * scale + up * 0.05f * scale, Unity.Mathematics.math.normalize(forward + right * 0.5f), up, 0.3f * scale, 0.06f * scale, 0.06f * scale, 0.6f, bark, faded, SegmentMaterial.Wood);
                }
                else if (atWork && state.Task == CreatureTask.Gesture && hand >= 0)
                {
                    // A trader's wares in hand: a round fruit or a jar, by individual, shown to passers-by.
                    var jar = (state.Individual & 1u) != 0u;
                    Output[at + k++] = jar
                        ? Piece(tip - up * 0.04f * scale, up, forward, 0.12f * scale, 0.05f * scale, 0.04f * scale, 1f, new Unity.Mathematics.float4(0.55f, 0.32f, 0.2f, 0f), faded, SegmentMaterial.Wood)
                        : Piece(tip - up * 0.03f * scale, up, forward, 0.02f * scale, 0.045f * scale, 0.045f * scale, 1f, new Unity.Mathematics.float4(0.78f, 0.18f, 0.12f, 0f), faded, SegmentMaterial.Cloth);
                }
            }

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
            /// <summary>Bat folk's smoked goggles (#35): two dark lenses on the front of the head, a strap round it.</summary>
            void Goggles(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                Output[at] = default;
                Output[at + 1] = default;
                Output[at + 2] = default;
                int head = -1;
                for (int i = 0; i < b.Parts.Length && head < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Head)
                        head = b.Parts[i].FirstSegment;
                if (head < 0)
                    return;
                var h = Output[offset + head];
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var r = Unity.Mathematics.math.max(h.RadiusStart, h.RadiusEnd);
                // (The eye line: a little above the head segment's middle.)
                var eyes = h.Position + Unity.Mathematics.math.mul(h.Rotation, new Unity.Mathematics.float3(0f, 0f, h.Length * 0.55f));
                var glass = new Unity.Mathematics.float4(0.05f, 0.05f, 0.06f, 0f);
                var strap = new Unity.Mathematics.float4(0.22f, 0.14f, 0.08f, 0f);
                for (int k = 0; k < 2; k++)
                    Output[at + k] = Piece(eyes + forward * r * 0.82f + right * r * (k == 0 ? 0.42f : -0.42f), forward, new Unity.Mathematics.float3(0f, 1f, 0f),
                                           r * 0.22f, r * 0.3f, r * 0.3f, 1f, glass, faded, SegmentMaterial.Cloth);
                Output[at + 2] = Piece(eyes - right * r * 1.02f, right, new Unity.Mathematics.float3(0f, 1f, 0f), r * 2.04f, r * 0.12f, r * 0.12f, 1f, strap, faded, SegmentMaterial.Cloth);
            }

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
