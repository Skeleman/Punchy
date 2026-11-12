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
    // A-81 row 4: PoseJob's torches, guards, lanterns and spears, moved out of CreatureRenderSystem.cs as they were.
    public partial class CreatureRenderSystem
    {
        partial struct PoseJob
        {
            /// <summary>
            /// A town guard's weapon, by its folk (GuardKit): in a closed fist (a spear, an axe, a club, an ogre's great
            /// club over its shoulder; an elf's bow on its back instead), sheathed on its back or at its hip while it isn't
            /// drawn, between the two through the half-second Draw and Sheathe; or after dark a torch held up and forward
            /// (the handle: its flame is FireSystem's). Running at a beast, the weapon is levelled at it, or raised. (What
            /// it wears, its helmet, armour, sash and an ogre's cape, and a goblin's shield, are its kit's since Zach 11-12
            /// row 3, FolkKits: the Armour and Weapon passes draw them, as on a player.)
            /// </summary>
            /// <summary>
            /// A torch carried by one of the folk out after dark (goblins, ogres): the brand in the right fist, held up and a
            /// little forward, as a guard holds one (its flame is the world's, lit on the main thread from the top).
            /// </summary>
            void Torch(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < TorchParts; i++)
                    Output[at + i] = default;
                var hand = -1;
                for (int i = 0; i < b.Parts.Length && hand < 0; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side > 0f)
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                if (hand < 0)
                    return;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var fist = Output[offset + hand];
                fist.Length *= 0.5f;
                fist.RadiusStart *= 1.25f;
                fist.RadiusEnd *= 1.35f;
                Output[offset + hand] = fist;
                var grip = fist.Position + Unity.Mathematics.math.mul(fist.Rotation, new Unity.Mathematics.float3(0f, 0f, fist.Length * 0.6f));
                var up = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(0f, 1f, 0f) + forward * 0.45f);
                var handle = 0.5f * scale;
                Output[at] = Piece(grip - up * handle * 0.35f, up, forward, handle, 0.02f * scale, 0.028f * scale, 1f,
                                   new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f), faded, SegmentMaterial.Wood);
            }

            void Guard(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded, int id)
            {
                for (int i = 0; i < GuardParts; i++)
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
                if (hand < 0)
                    return;
                var k = 0;
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                var upward = new Unity.Mathematics.float3(0f, 1f, 0f);
                var wood = new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f);
                var iron = new Unity.Mathematics.float4(0.32f, 0.33f, 0.35f, 0f);
                var bone = new Unity.Mathematics.float4(0.78f, 0.74f, 0.62f, 0f);
                var levelled = state.Mode == CreatureMode.Patrol && state.Stage == 2;

                // How far the weapon is out of its sheath and in hand: drawn, all the way; worn (or a torch in hand
                // instead), not at all; drawing or sheathing, from its sheath to the hand at the middle of the reach.
                var u = state.ActionTime;
                var inHand = state.Held == HeldItem.SpearDrawn ? (state.Action == IdleAction.Draw ? Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 1f)
                           : state.Held == HeldItem.Spear && state.Action == IdleAction.Sheathe ? 1f - Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 0f;

                // The hand closed round what it holds (a fist, shorter and rounder than the open hand), if anything.
                var fist = Output[offset + hand];
                var holding = state.Held == HeldItem.Torch || inHand > 0.5f;
                if (holding)
                {
                    fist.Length *= 0.5f;
                    fist.RadiusStart *= 1.25f;
                    fist.RadiusEnd *= 1.35f;
                    Output[offset + hand] = fist;
                }
                var grip = fist.Position + Unity.Mathematics.math.mul(fist.Rotation, new Unity.Mathematics.float3(0f, 0f, fist.Length * 0.6f));

                if (state.Held == HeldItem.Torch)
                {
                    var up = Unity.Mathematics.math.normalize(upward + forward * 0.45f);
                    var handle = 0.5f * scale;
                    var bottom = grip - up * handle * 0.35f;
                    Output[at + k++] = Piece(bottom, up, forward, handle, 0.02f * scale, 0.028f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    // (Its flame is lit on the main thread, from the top of this handle: FireSystem's, like any other.)
                }

                // The weapon (an elf's is the bow on its back, below): in hand as it's carried drawn, or worn in its
                // sheath, on the back if it's long for its bearer (GuardWeapons.OnBack), else at the hip; between the
                // two as it's drawn or put away.
                if (b.GuardKit != GuardKit.Elf && chest >= 0)
                {
                    var length = GuardWeapons.Length(b.GuardKit) * scale;
                    float3 handDir;
                    float handFrom;   // (how far along the weapon the hand holds it, from the butt)
                    switch (b.GuardKit)
                    {
                        case GuardKit.Dwarf:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.7f) : Unity.Mathematics.math.normalize(upward + forward * 0.15f);
                            handFrom = 0.3f;
                            break;
                        case GuardKit.Goblin:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.8f) : Unity.Mathematics.math.normalize(upward * 0.6f + forward * 0.5f);
                            handFrom = 0.11f;
                            break;
                        case GuardKit.Ogre:
                            handDir = levelled ? Unity.Mathematics.math.normalize(upward + forward * 0.25f) : Unity.Mathematics.math.normalize(upward * 0.55f - forward * 0.75f);
                            handFrom = 0.07f;
                            break;
                        default:
                            // (Levelled: tilted up a little and held near its middle, so its butt doesn't trail low behind.)
                            handDir = levelled ? Unity.Mathematics.math.normalize(forward + upward * 0.28f) : Unity.Mathematics.math.normalize(upward + forward * 0.12f);
                            handFrom = levelled ? 0.42f : 0.45f;
                            break;
                    }
                    // Worn: across the back, from low on the left to high on the right, or hanging from the belt at the left
                    // hip, head down along the thigh.
                    var torso = Output[offset + chest];
                    var torsoAxis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var torsoR = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                    float3 wornAt, wornDir;
                    if (GuardWeapons.OnBack(b.GuardKit, scale, b.StandHeight))
                    {
                        wornDir = Unity.Mathematics.math.normalize(torsoAxis + right * 0.75f);
                        wornAt = torso.Position + torsoAxis * torso.Length * 0.5f - forward * torsoR * 1.3f - wornDir * length * (0.5f - handFrom);
                    }
                    else
                    {
                        // At the hip: hung from the belt by its grip, the heavy end down along the thigh and a little back.
                        wornDir = Unity.Mathematics.math.normalize(-upward - forward * 0.3f - right * 0.08f);
                        wornAt = torso.Position - torsoAxis * torsoR * 0.5f - right * torsoR * 1.15f;
                    }
                    var dir = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.lerp(wornDir, handDir, inHand), handDir);
                    var held = Unity.Mathematics.math.lerp(wornAt, grip, inHand);
                    var butt = held - dir * length * handFrom;
                    var face = Unity.Mathematics.math.normalizesafe(forward - dir * Unity.Mathematics.math.dot(forward, dir), right);
                    switch (b.GuardKit)
                    {
                        case GuardKit.Dwarf:
                            // An axe: a short haft, a broad blade at its head facing out from it.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.022f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            Output[at + k++] = Piece(butt + dir * length * 0.86f, Unity.Mathematics.math.normalize(face - dir * 0.1f), Unity.Mathematics.math.cross(dir, face), 0.13f * scale, 0.05f * scale, 0.1f * scale, 0.18f, iron, faded, SegmentMaterial.Metal);
                            break;
                        case GuardKit.Goblin:
                            // A crude club, thick at the end.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.025f * scale, 0.07f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            break;
                        case GuardKit.Ogre:
                            // A great club (over the shoulder in hand; raised overhead running at a beast).
                            Output[at + k++] = Piece(butt, dir, face, length, 0.045f * scale, 0.13f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            break;
                        default:
                            // A spear: upright at its side in hand, its butt near the ground; levelled at a beast.
                            Output[at + k++] = Piece(butt, dir, face, length, 0.017f * scale, 0.014f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                            Output[at + k++] = Piece(butt + dir * length, dir, face, 0.2f * scale, 0.03f * scale, 0.003f * scale, 0.4f, iron, faded, SegmentMaterial.Metal);
                            break;
                    }
                }

                // An elf's bow across its back: an arc from low on one side to high on the other, and its string.
                if (b.GuardKit == GuardKit.Elf && chest >= 0)
                {
                    var c = Output[offset + chest];
                    var axis = Unity.Mathematics.math.mul(c.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                    var r = Unity.Mathematics.math.max(c.RadiusStart, c.RadiusEnd);
                    var front = Unity.Mathematics.math.normalize(forward - axis * Unity.Mathematics.math.dot(forward, axis));
                    var centre = c.Position + axis * c.Length * 0.5f - front * r * 1.25f;
                    var across = Unity.Mathematics.math.normalize(axis + right * 0.9f);
                    var bowLength = 1.1f * scale;
                    var lower = centre - across * bowLength * 0.5f;
                    var mid = centre - front * 0.08f * scale;
                    var upper = centre + across * bowLength * 0.5f;
                    Output[at + k++] = Piece(lower, Unity.Mathematics.math.normalize(mid - lower), front, Unity.Mathematics.math.distance(lower, mid), 0.012f * scale, 0.02f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(mid, Unity.Mathematics.math.normalize(upper - mid), front, Unity.Mathematics.math.distance(upper, mid), 0.02f * scale, 0.012f * scale, 1f, wood, faded, SegmentMaterial.Wood);
                    Output[at + k++] = Piece(lower, across, front, bowLength, 0.003f * scale, 0.003f * scale, 1f, bone, faded, SegmentMaterial.Cloth);
                }
            }

            /// <summary>One capsule of a kit: from a point along a direction, its flattening across "flat" (local y), and what it's made of.</summary>
            static SegmentInstance Piece(Unity.Mathematics.float3 from, Unity.Mathematics.float3 along, Unity.Mathematics.float3 flat, float length,
                                         float radiusStart, float radiusEnd, float flatten, Unity.Mathematics.float4 color, float faded,
                                         SegmentMaterial material) =>
                new SegmentInstance
                {
                    Position = from,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, flat),
                    Length = length,
                    RadiusStart = radiusStart,
                    RadiusEnd = radiusEnd,
                    Flatten = flatten,
                    Color = color,
                    Pad = faded,
                    Material = new Unity.Mathematics.float4((float)material, 0f, 0f, 0f)
                };

            /// <summary>
            /// A lantern hanging from the off hand by its bail: a dark iron cap and base, and between them a warm glass
            /// core that glows. It hangs plumb, swinging gently fore and aft with the walk (and still when standing).
            /// </summary>
            void Lantern(in CreatureState state, ref CreatureBlob b, int offset, int at, float faded)
            {
                for (int i = 0; i < LanternParts; i++)
                    Output[at + i] = default;
                var hand = -1;
                for (int i = 0; i < b.Parts.Length; i++)
                    if (b.Parts[i].Role == LimbRole.Arm && b.Parts[i].Side < 0f)
                    {
                        hand = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                        break;
                    }
                if (hand < 0)
                    return;
                var segment = Output[offset + hand];
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length));
                var scale = Unity.Mathematics.math.max(state.Scale, 0.3f);
                // Swinging with the stride: back and forth along the heading, more the faster it goes.
                var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var swing = Unity.Mathematics.math.sin(state.Phase * 2f * Unity.Mathematics.math.PI) * 0.35f * Unity.Mathematics.math.saturate(state.Speed / 2f);
                var down = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(0f, -1f, 0f) + forward * swing);
                var hang = Unity.Mathematics.quaternion.LookRotationSafe(down, forward);
                var iron = new Unity.Mathematics.float4(0.05f, 0.045f, 0.04f, 0f);
                var glass = new Unity.Mathematics.float4(1f, 0.62f, 0.22f, 1f);
                var bail = 0.07f * scale;
                var cap = grip + down * bail;
                var core = 0.11f * scale;
                // The bail (handle), the cap, the glass, the base.
                Output[at] = new SegmentInstance { Position = grip, Rotation = hang, Length = bail, RadiusStart = 0.006f * scale, RadiusEnd = 0.006f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
                Output[at + 1] = new SegmentInstance { Position = cap, Rotation = hang, Length = 0.025f * scale, RadiusStart = 0.012f * scale, RadiusEnd = 0.045f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
                Output[at + 2] = new SegmentInstance { Position = cap + down * 0.02f * scale, Rotation = hang, Length = core, RadiusStart = 0.04f * scale, RadiusEnd = 0.04f * scale, Flatten = 1f, Color = glass, Pad = faded };
                Output[at + 3] = new SegmentInstance { Position = cap + down * (0.02f * scale + core), Rotation = hang, Length = 0.02f * scale, RadiusStart = 0.048f * scale, RadiusEnd = 0.048f * scale, Flatten = 1f, Color = iron, Pad = faded, Material = new Unity.Mathematics.float4((float)SegmentMaterial.Metal, 0f, 0f, 0f) };
            }

            /// <summary>
            /// A hunter's spear: worn across its back about the town (and after dark, a lantern in hand), drawn when it
            /// goes after game (the same half-second reach as a guard's, Draw and Sheathe), then carried pointing ahead
            /// and a little down while creeping, held level over the shoulder at the game while aiming; gone at the
            /// throw (it's been thrown), back in hand after. (Drawn for every hunter: the Hunt task is replicated, so
            /// each machine draws it.)
            /// </summary>
            void Spear(in CreatureState state, ref CreatureBlob b, int offset, int at)
            {
                Output[at] = default;
                if (state.Mode == CreatureMode.Hunt && state.Stage == FolkHunting.Throw)
                    return;
                // The right hand: the end of the right arm's last segment, as just posed.
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
                var grip = segment.Position + Unity.Mathematics.math.mul(segment.Rotation, new Unity.Mathematics.float3(0f, 0f, segment.Length));
                var yaw = state.Yaw;
                var aiming = state.Mode == CreatureMode.Hunt && state.Stage == FolkHunting.Aim;
                var pitch = aiming ? -0.08f : 0.25f;   // (positive: tip down)
                var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(yaw) * Unity.Mathematics.math.cos(pitch),
                                                       -Unity.Mathematics.math.sin(pitch),
                                                       Unity.Mathematics.math.cos(yaw) * Unity.Mathematics.math.cos(pitch));
                var length = 1.8f * state.Scale;
                var colour = new Unity.Mathematics.float4(0.16f, 0.1f, 0.05f, 0f);   // (linear) dark wood
                var held = grip - dir * length * 0.4f;
                // Worn, or on its way between back and hand: across the back, from low on the left to high on the right.
                var u = state.ActionTime;
                var inHand = state.Held == HeldItem.SpearDrawn ? (state.Action == IdleAction.Draw ? Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u) : 1f)
                           : state.Held == HeldItem.Spear && state.Action == IdleAction.Sheathe ? 1f - Unity.Mathematics.math.smoothstep(0.35f, 0.65f, u)
                           : state.Held == HeldItem.Spear || state.Held == HeldItem.Lantern ? 0f : 1f;
                if (inHand < 1f)
                {
                    var forward = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(yaw), 0f, Unity.Mathematics.math.cos(yaw));
                    var right = new Unity.Mathematics.float3(forward.z, 0f, -forward.x);
                    var chest = -1;
                    for (int i = 0; i < b.Parts.Length; i++)
                        if (b.Parts[i].Parent < 0 && b.Parts[i].Role != LimbRole.Leg && b.Parts[i].Role != LimbRole.Arm)
                        {
                            chest = b.Parts[i].FirstSegment + b.Parts[i].SegmentCount - 1;
                            break;
                        }
                    if (chest >= 0)
                    {
                        var torso = Output[offset + chest];
                        var axis = Unity.Mathematics.math.mul(torso.Rotation, new Unity.Mathematics.float3(0f, 0f, 1f));
                        var r = Unity.Mathematics.math.max(torso.RadiusStart, torso.RadiusEnd);
                        var wornDir = Unity.Mathematics.math.normalize(axis + right * 0.75f);
                        var wornStart = torso.Position + axis * torso.Length * 0.5f - forward * r * 1.3f - wornDir * length * 0.5f;
                        dir = Unity.Mathematics.math.normalizesafe(Unity.Mathematics.math.lerp(wornDir, dir, inHand), dir);
                        held = Unity.Mathematics.math.lerp(wornStart, held, inHand);
                    }
                }
                Output[at] = new SegmentInstance
                {
                    Position = held,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = length,
                    RadiusStart = 0.016f * state.Scale,
                    RadiusEnd = 0.009f * state.Scale,
                    Flatten = 1f,
                    Color = colour
                };
            }
        }
    }
}
