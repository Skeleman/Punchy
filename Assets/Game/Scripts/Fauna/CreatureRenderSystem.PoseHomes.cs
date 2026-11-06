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
    // A-81 row 4: PoseJob's homes and marks (nests, mounds, webs, threads, fear marks), moved out of
    // CreatureRenderSystem.cs as they were.
    public partial class CreatureRenderSystem
    {
        partial struct PoseJob
        {
            /// <summary>
            /// A nest, where the bird's nest is (if it has one): a shallow bowl in a ring of twigs against a trunk, a
            /// bigger, untidier one of sticks on a treetop, or a pale ring of grass on the ground.
            /// </summary>
            void Nest(in CreatureState state, NestSite site, int offset)
            {
                if (state.AnchorSize <= 0f)
                {
                    for (int i = 0; i < NestCount; i++)
                        Output[offset + i] = default;
                    return;
                }
                var r = state.AnchorSize;
                var c = state.Anchor;
                var ground = site == NestSite.Ground;
                var top = site == NestSite.Treetop;
                // (Linear colours: straw; grey-brown mud; dark twigs.)
                var twigs = ground ? new Unity.Mathematics.float4(0.4f, 0.31f, 0.12f, 0f)
                          : site == NestSite.Ledge ? new Unity.Mathematics.float4(0.16f, 0.12f, 0.08f, 0f)
                          : new Unity.Mathematics.float4(0.1f, 0.06f, 0.025f, 0f);
                var x = new Unity.Mathematics.float3(1f, 0f, 0f);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                // The floor: a flat disc just under the bird's feet.
                Output[offset] = new SegmentInstance
                {
                    // (On the ground a round disc, its edge under the rim; in a tree the long shape it always had.)
                    Position = c + new Unity.Mathematics.float3(ground ? -r * 0.05f : -r * 0.45f, ground ? 0.01f : -r * 0.05f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = ground ? r * 0.1f : r * 0.9f,
                    RadiusStart = ground ? r * 0.68f : r * 0.55f,
                    RadiusEnd = ground ? r * 0.68f : r * 0.55f,
                    Flatten = ground ? 0.1f : 0.3f,
                    // (On the ground, the hollow: dark, trodden straw.)
                    Color = ground ? new Unity.Mathematics.float4(0.12f, 0.09f, 0.04f, 0f) : twigs * 0.8f
                };
                // The rim: twigs round it, each a little askew (sticks on a treetop more so).
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                var rimHeight = ground ? r * 0.16f : r * 0.15f;
                var thick = r * (ground ? 0.045f : top ? 0.16f : 0.18f);
                // Contact darkening: a dark, flat disc a little wider than the nest just under it (the ground in its
                // shade, or the shadowed underside of a nest in a tree).
                Output[offset + NestCount - 1] = new SegmentInstance
                {
                    Position = c + new Unity.Mathematics.float3(ground ? -r * 0.05f : -r * 0.5f, ground ? 0.004f : -r * 0.06f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(x, up),
                    Length = ground ? r * 0.1f : r,
                    RadiusStart = ground ? r * 0.95f : r * 0.7f,
                    RadiusEnd = ground ? r * 0.95f : r * 0.7f,
                    Flatten = 0.05f,
                    Color = ground ? new Unity.Mathematics.float4(0.05f, 0.04f, 0.02f, 0f) : new Unity.Mathematics.float4(0.02f, 0.018f, 0.012f, 0f)
                };
                for (int k = 1; k <= NestRim; k++)
                {
                    var a = k / (float)NestRim * Unity.Mathematics.math.PI * 2f + rng.NextFloat(-0.25f, 0.25f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    // (On the ground, short tufts of straw heaped up raggedly: shorter, more askew, in and out.)
                    var along = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(-dir.z, rng.NextFloat(-0.15f, 0.15f) * (top ? 2f : ground ? 2.5f : 1f), dir.x));
                    var length = r * (top ? rng.NextFloat(1.1f, 1.6f) : ground ? rng.NextFloat(0.55f, 0.85f) : 0.95f);
                    var layer = ground && (k & 1) == 0 ? 1f : 0f;
                    var centre = c + dir * r * (ground ? rng.NextFloat(0.82f, 1.0f) - 0.1f * layer : 0.9f) +
                                 up * rimHeight * (ground ? rng.NextFloat(0.5f, 0.9f) + 0.7f * layer : 1f);
                    Output[offset + k] = new SegmentInstance
                    {
                        Position = centre - along * length * 0.5f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, up),
                        Length = length,
                        RadiusStart = thick,
                        RadiusEnd = thick * 0.8f,
                        Flatten = 1f,
                        Color = twigs * rng.NextFloat(0.75f, 1.15f)
                    };
                }
                // A scrape on the ground has a few straws sticking out of the rim at angles, and eggs in the hollow when
                // the hen is off it.
                for (int k = NestRim + 1; k < NestCount - 1; k++)
                    Output[offset + k] = default;
                if (!ground)
                    return;
                for (int k = 0; k < 2; k++)
                {
                    var a = rng.NextFloat(0f, Unity.Mathematics.math.PI * 2f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var outward = Unity.Mathematics.math.normalize(dir + up * rng.NextFloat(0.3f, 0.8f) + new Unity.Mathematics.float3(-dir.z, 0f, dir.x) * rng.NextFloat(-0.6f, 0.6f));
                    Output[offset + NestRim + 1 + k] = new SegmentInstance
                    {
                        Position = c + dir * r * 0.75f + up * rimHeight,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(outward, up),
                        Length = r * rng.NextFloat(0.6f, 0.9f),
                        RadiusStart = r * 0.035f,
                        RadiusEnd = r * 0.02f,
                        Flatten = 1f,
                        Color = twigs * 1.1f
                    };
                }
                if (Unity.Mathematics.math.distance(state.Position, c) < r * 1.5f)
                    return;
                for (int k = 0; k < 2; k++)
                {
                    var egg = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(rng.NextFloat(-1f, 1f), 0.15f, rng.NextFloat(-1f, 1f)));
                    var at = c + new Unity.Mathematics.float3(k == 0 ? -r * 0.28f : r * 0.25f, r * 0.12f, k == 0 ? r * 0.14f : -r * 0.2f);
                    Output[offset + NestRim + 3 + k] = new SegmentInstance
                    {
                        Position = at - egg * r * 0.05f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(egg, up),
                        Length = r * 0.1f,
                        RadiusStart = r * 0.13f,
                        RadiusEnd = r * 0.1f,
                        Flatten = 1f,
                        Color = new Unity.Mathematics.float4(0.72f, 0.62f, 0.46f, 0f)
                    };
                }
            }

            /// <summary>A burrow: a dark hole in a ring of dug-out earth, where the animal's burrow is (if it has one).</summary>
            void Mound(in CreatureState state, int offset)
            {
                if (state.AnchorSize <= 0f)
                {
                    for (int i = 0; i < MoundCount; i++)
                        Output[offset + i] = default;
                    return;
                }
                var r = state.AnchorSize;
                var mouth = state.Anchor;
                var earth = new Unity.Mathematics.float4(0.11f, 0.06f, 0.027f, 0f);   // (linear)
                // The hole: a flat dark disc just above the ground.
                Output[offset] = new SegmentInstance
                {
                    Position = mouth + new Unity.Mathematics.float3(-r * 0.45f, 0.02f, 0f),
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(new Unity.Mathematics.float3(1f, 0f, 0f), new Unity.Mathematics.float3(0f, 1f, 0f)),
                    Length = r * 0.9f,
                    RadiusStart = r * 0.55f,
                    RadiusEnd = r * 0.55f,
                    Flatten = 0.08f,
                    Color = new Unity.Mathematics.float4(0.05f, 0.04f, 0.03f, 0f)
                };
                // Heaped earth round it, thicker on one side (where it was thrown).
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                for (int k = 1; k < MoundCount; k++)
                {
                    var a = k / (float)(MoundCount - 1) * Unity.Mathematics.math.PI * 2f + rng.NextFloat(-0.3f, 0.3f);
                    var dir = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(a), 0f, Unity.Mathematics.math.sin(a));
                    var along = new Unity.Mathematics.float3(-dir.z, 0f, dir.x);
                    var heap = k <= 2 ? 1.5f : 1f;
                    var centre = mouth + dir * r * 1.1f;
                    Output[offset + k] = new SegmentInstance
                    {
                        Position = centre - along * r * 0.5f,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(along, new Unity.Mathematics.float3(0f, 1f, 0f)),
                        Length = r,
                        RadiusStart = r * 0.35f * heap,
                        RadiusEnd = r * 0.3f * heap,
                        Flatten = 0.45f,
                        Color = earth
                    };
                }
            }

            /// <summary>
            /// An orb web round the spider, in the plane facing its heading: spokes, a spiral (as rings of chords
            /// between the spokes), and anchor threads: down to the ground and across to the tree it hangs by, or out to
            /// both things it's strung between (see CreatureMode.Web). Only for a spider sitting in its web (room is
            /// made for it then only).
            /// </summary>
            void Web(in CreatureState state, ref CreatureBlob b, int offset)
            {
                var radius = b.WebSize * state.Scale;
                var centre = state.Position;
                var right = new Unity.Mathematics.float3(Unity.Mathematics.math.cos(state.Yaw), 0f, -Unity.Mathematics.math.sin(state.Yaw));
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var thick = Unity.Mathematics.math.clamp(radius * 0.006f, 0.003f, 0.012f);
                var colour = new Unity.Mathematics.float4(0.93f, 0.95f, 0.98f, 0.18f);
                // Every web is a little irregular, its own way.
                var rng = new Unity.Mathematics.Random(state.Individual | 1u);
                var spoke = new Unity.Collections.FixedList512Bytes<float>();
                for (int k = 0; k < Spokes; k++)
                    spoke.Add((k + rng.NextFloat(-0.2f, 0.2f)) / Spokes * Unity.Mathematics.math.PI * 2f);
                Unity.Mathematics.float3 Point(float angle, float r) =>
                    centre + (right * Unity.Mathematics.math.cos(angle) + up * Unity.Mathematics.math.sin(angle)) * r;

                var n = 0;
                for (int k = 0; k < Spokes; k++)
                    Output[offset + n++] = Thread(centre, Point(spoke[k], radius * rng.NextFloat(0.95f, 1.05f)), thick, colour);
                for (int ring = 0; ring < Rings; ring++)
                {
                    var r = radius * (0.22f + 0.13f * ring);
                    for (int k = 0; k < Spokes; k++)
                    {
                        var next = spoke[(k + 1) % Spokes] + (k == Spokes - 1 ? Unity.Mathematics.math.PI * 2f : 0f);
                        Output[offset + n++] = Thread(Point(spoke[k], r), Point(next, r), thick * 0.8f, colour);
                    }
                }
                if (state.Stage == 4)
                {
                    // Strung between two things: from above and below the middle of each side, out to each one.
                    var span = Unity.Mathematics.math.max(state.AnchorSize, radius);
                    for (int k = 0; k < 4; k++)
                    {
                        var side = k < 2 ? 1f : -1f;
                        var tilt = (k % 2 == 0 ? 0.3f : -0.3f);
                        var start = Point(side > 0f ? tilt : Unity.Mathematics.math.PI - tilt, radius);
                        var end = centre + right * side * span + up * Unity.Mathematics.math.sin(tilt) * radius * 1.6f;
                        Output[offset + n++] = Thread(start, end, thick, colour);
                    }
                    return;
                }
                // Down to the ground from the lower edge, either side.
                for (int k = 0; k < 2; k++)
                {
                    var start = Point(Unity.Mathematics.math.PI * (k == 0 ? 1.3f : 1.7f), radius);
                    var foot = start + right * (k == 0 ? -0.3f : 0.3f) * radius;
                    foot.y = Terrain.FloorUnder(start, out _);
                    Output[offset + n++] = Thread(start, foot, thick, colour);
                }
                // Across to the trunk, from the top and the near side (stage 2: tree to the spider's right, 3: left).
                var trunkSide = state.Stage == 2 ? 1f : -1f;
                for (int k = 0; k < 2; k++)
                {
                    var start = k == 0 ? Point(Unity.Mathematics.math.PI * 0.5f, radius) : Point(trunkSide >= 0f ? 0f : Unity.Mathematics.math.PI, radius);
                    var end = centre + right * trunkSide * radius * 1.05f + up * (k == 0 ? radius * 1.4f : 0f);
                    Output[offset + n++] = Thread(start, end, thick, colour);
                }
            }

            static SegmentInstance Thread(Unity.Mathematics.float3 from, Unity.Mathematics.float3 to, float radius, Unity.Mathematics.float4 colour)
            {
                var d = to - from;
                var length = Unity.Mathematics.math.length(d);
                return new SegmentInstance
                {
                    Position = from,
                    Rotation = Unity.Mathematics.quaternion.LookRotationSafe(d / Unity.Mathematics.math.max(length, 1e-5f), new Unity.Mathematics.float3(0f, 1f, 0.01f)),
                    Length = length,
                    RadiusStart = radius,
                    RadiusEnd = radius,
                    Flatten = 1f,
                    Color = colour
                };
            }

            /// <summary>
            /// Three short lines fanning out above the creature's highest point, facing the camera, flashing on and
            /// off while it flees. Sized to the creature, but never too small to see. When it isn't fleeing, the same
            /// three capsules show its strongest pressing need (#64) for a moment on its own beat (each creature's cycle
            /// and phase come from its Individual, so a herd never shows them all at once): tired, pale bubbles drifting
            /// up; hungry, dust puffed up where its nose sniffs the ground; thirsty, a drop falling over its head;
            /// lonely, a small pink heart. Drawn only: nothing in the simulation reads them.
            /// </summary>
            /// <param name="id">Fixed per creature, so each flashes on its own steady beat (its random seed changes
            /// every frame, which made the marks flicker).</param>
            void FearMarks(in CreatureState state, int offset, int segments, int id)
            {
                var top = float.MinValue;
                var fwd = new Unity.Mathematics.float3(Unity.Mathematics.math.sin(state.Yaw), 0f, Unity.Mathematics.math.cos(state.Yaw));
                var nose = state.Position;
                var reach = float.MinValue;
                for (int i = 0; i < segments; i++)
                {
                    var seg = Output[offset + i];
                    var end = seg.Position + Unity.Mathematics.math.mul(seg.Rotation, new Unity.Mathematics.float3(0f, 0f, seg.Length));
                    top = Unity.Mathematics.math.max(top, Unity.Mathematics.math.max(seg.Position.y + seg.RadiusStart, end.y + seg.RadiusEnd));
                    var ahead = Unity.Mathematics.math.dot(end - state.Position, fwd);
                    if (ahead > reach) { reach = ahead; nose = end; }
                    ahead = Unity.Mathematics.math.dot(seg.Position - state.Position, fwd);
                    if (ahead > reach) { reach = ahead; nose = seg.Position; }
                }
                var size = Unity.Mathematics.math.clamp(state.Scale * 0.3f, 0.25f, 0.5f);
                var up = new Unity.Mathematics.float3(0f, 1f, 0f);
                var right = Unity.Mathematics.math.normalizesafe(new Unity.Mathematics.float3(CameraRight.x, 0f, CameraRight.z), new Unity.Mathematics.float3(1f, 0f, 0f));
                if (state.Mode == CreatureMode.Flee)
                {
                    var visible = Unity.Mathematics.math.frac(Time * 3f + (id % 97) * 0.01f) < 0.6f;
                    for (int k = 0; k < MarkCount; k++)
                    {
                        var angle = (k - 1) * 0.6f;
                        var dir = up * Unity.Mathematics.math.cos(angle) + right * Unity.Mathematics.math.sin(angle);
                        var start = new Unity.Mathematics.float3(state.Position.x, top, state.Position.z) + dir * size * 0.7f;
                        Output[offset + segments + k] = new SegmentInstance
                        {
                            Position = start,
                            Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, right),
                            Length = visible ? size : 0f,
                            RadiusStart = visible ? size * 0.16f : 0f,
                            RadiusEnd = visible ? size * 0.1f : 0f,
                            Flatten = 1f,
                            Color = new Unity.Mathematics.float4(1f, 0.92f, 0.55f, 0.6f)
                        };
                    }
                    return;
                }
                // Which need shows, if any: the strongest past 0.6, during the first 2.6 s of this creature's own
                // 8-15 s cycle (from a scrambled Individual, since neighbouring ids would otherwise keep in step).
                var h = state.Individual * 0x9E3779B1u;
                h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; h *= 0xC2B2AE3Du; h ^= h >> 16;
                var period = 8f + (h & 1023) / 1023f * 7f;
                var t = Unity.Mathematics.math.frac(Time / period + ((h >> 10) & 1023) / 1023f) * period;
                const float Show = 2.6f;
                int cue = 0;
                if (ForceCues)
                {
                    t = Unity.Mathematics.math.frac(Time / Show + ((h >> 10) & 1023) / 1023f) * Show;
                    cue = 1 + (int)((h >> 20) % 4);
                }
                else if (t < Show && state.Mode != CreatureMode.Sleep && state.Holder == 0)
                    cue = state.Cue;   // (set by the brain on the host, replicated to clients: #64)
                var swell = Unity.Mathematics.math.sin(Unity.Mathematics.math.PI * Unity.Mathematics.math.saturate(t / Show));
                // (At the fear marks' size the cues were 0.2-2.6 cm across: drawn, but too small to see past a metre or
                // two, A-60. Everything about a cue is scaled up from here.)
                size *= CueScale;
                var head =new Unity.Mathematics.float3(state.Position.x, top, state.Position.z);
                for (int k = 0; k < MarkCount; k++)
                {
                    var at = head;
                    var dir = up;
                    float length = 0f, radius = 0f, tip = 0f;
                    var color = new Unity.Mathematics.float4(0f);
                    switch (cue)
                    {
                        case 1: // Tired: bubbles rising one after another, swaying a little.
                        {
                            var rise = Unity.Mathematics.math.frac(t / 1.3f + k / 3f);
                            at = head + up * size * (0.25f + rise * 1.1f) + right * size * (0.25f + 0.12f * Unity.Mathematics.math.sin((t + k) * 2.4f));
                            radius = tip = size * (0.05f + 0.05f * rise) * Unity.Mathematics.math.saturate((1f - rise) * 4f);
                            length = radius * 0.2f;
                            color = new Unity.Mathematics.float4(0.78f, 0.86f, 1f, 0.45f);
                            break;
                        }
                        case 2: // Hungry: little puffs of dust kicked up round its nose, as if sniffing the ground.
                        {
                            var puff = Unity.Mathematics.math.frac(t / 0.9f + k * 0.37f);
                            var side = (k - 1) * 0.5f;
                            at = new Unity.Mathematics.float3(nose.x, state.Position.y, nose.z) + (right * side + fwd * 0.3f) * size * (0.3f + puff) + up * size * puff * 0.35f;
                            radius = tip = size * 0.1f * Unity.Mathematics.math.sin(Unity.Mathematics.math.PI * puff) * swell;
                            length = radius * 0.3f;
                            color = new Unity.Mathematics.float4(0.74f, 0.64f, 0.48f, 0.15f);
                            break;
                        }
                        case 3: // Thirsty: one drop falling over its head, again and again.
                            if (k == 0)
                            {
                                var fall = Unity.Mathematics.math.frac(t / 1.1f);
                                dir = -up;
                                at = head + up * size * (1.1f - fall * 0.6f);
                                length = size * 0.22f;
                                radius = size * 0.03f;
                                tip = size * 0.11f * Unity.Mathematics.math.saturate((1f - fall) * 5f);
                                color = new Unity.Mathematics.float4(0.4f, 0.65f, 1f, 0.6f);
                            }
                            break;
                        case 4: // Lonely: a small heart (two strokes meeting at its point), bobbing.
                            if (k < 2)
                            {
                                var s2 = k == 0 ? -1f : 1f;
                                dir = Unity.Mathematics.math.normalize(up * 1.1f + right * s2);
                                at = head + up * size * (0.45f + 0.08f * Unity.Mathematics.math.sin(t * 3f));
                                length = size * 0.42f * swell;
                                radius = size * 0.07f * swell;
                                tip = size * 0.15f * swell;
                                color = new Unity.Mathematics.float4(1f, 0.45f, 0.62f, 0.6f);
                            }
                            break;
                    }
                    Output[offset + segments + k] = new SegmentInstance
                    {
                        Position = at,
                        Rotation = Unity.Mathematics.quaternion.LookRotationSafe(dir, Unity.Mathematics.math.abs(dir.y) > 0.99f ? right : up),
                        Length = length,
                        RadiusStart = radius,
                        RadiusEnd = tip,
                        Flatten = 1f,
                        Color = color
                    };
                }
            }
        }
    }
}
