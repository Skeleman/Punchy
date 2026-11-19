using Unity.Mathematics;

namespace Punchy.Terrain
{
    public partial class CaveSystem
    {
        /// <summary>
        /// The look baked into each wall vertex (moved out of CaveSystem.cs, which is over its size cap): the terrain's
        /// colour, daylight, style, ribs and a road tunnel's floor, and W-162's country rock round a mouth.
        /// </summary>
        partial struct MeshJob
        {
            /// <summary>W-162: the country rock's colour shows fully within RockNear metres of a mouth, gone by RockReach.</summary>
            const float RockNear = 4f, RockReach = 30f;

            /// <summary>
            /// A road tunnel's floor (its road's level) under p: along the nearest of its passages; axis is the point
            /// on that passage's centre line (xz) nearest p.
            /// </summary>
            static float TunnelFloor(ref CaveSetBlob set, in CaveData c, float3 p, out float2 axis)
            {
                var best = 1e9f;
                var floor = p.y;
                axis = p.xz;
                for (int s = c.FirstSegment; s < c.FirstSegment + c.SegmentCount; s++)
                {
                    var a = set.Nodes[set.Segments[s].A];
                    var b = set.Nodes[set.Segments[s].B];
                    var ab = b.Position.xz - a.Position.xz;
                    var t = math.saturate(math.dot(p.xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                    var d = math.distancesq(p.xz, a.Position.xz + ab * t);
                    if (d < best)
                    {
                        best = d;
                        floor = math.lerp(a.Position.y, b.Position.y, t) - math.lerp(a.Radius, b.Radius, t) * c.Profile.y * 0.45f;
                        axis = a.Position.xz + ab * t;
                    }
                }
                return floor;
            }

            /// <summary>
            /// rgb the terrain's colour at this spot; alpha how much daylight reaches (the entrance and near the
            /// surface). Data: x how much to look like the terrain (1 at the surface, fading out a few metres
            /// underground), y a slight brightness tint for the cave rock, z the cave's style slot, w how much darker
            /// (negative: lighter) ribs make it here. In a road tunnel z also carries 32 x a tunnel code (1 + paved +
            /// 2 if built, see Cave.shader) and w the road's share of the floor here (negative: a fallen stone's share).
            /// Rock (W-162): rgb the country rock round a cave's mouth (the ground's own steep rock, as the cliffs outside
            /// draw it), a its share, fading to 0 by RockReach in (the shader fades to the style's own colours); none in
            /// a road tunnel or deeper in, so the extra sample costs only the walls near a mouth.
            /// </summary>
            internal float4 Look(float3 p, out float4 data, out float open, out float4 rock)
            {
                var surface = Source.SampleHeight(p.xz);
                // Daylight from the mouth of whichever cave this wall belongs to.
                ref var set = ref Source.Caves.Value;
                CaveSampling.Distance(ref set, p, out var cave);
                var fromMouth = cave >= 0 ? math.saturate(1f - (math.distance(p, set.Caves[cave].Mouth) - 3f) / 14f) : 0f;
                // A road tunnel (W-110) has a portal at each end: daylight comes in at both.
                if (cave >= 0 && set.Caves[cave].Tunnel != 0)
                {
                    var far = FarPortal(ref set, set.Caves[cave]);
                    fromMouth = math.max(fromMouth, math.saturate(1f - (math.distance(p, far) - 3f) / 14f));
                }
                var nearSurface = math.saturate(1f - (surface - p.y - 1f) / 3f);
                // Open to the sky (the terrain is cut away above: a trench or crater at a mouth): it's outside ground,
                // so it takes the land's look and daylight however far below the uncut surface height it lies. Generous
                // (up to 2 m of clearance): the terrain's cut is interpolated between its vertices, and wherever the
                // terrain stays it hides this skin anyway.
                open = math.saturate((2f - Source.CaveClearance(p.xz, surface)) / 1.5f);
                var ground = Source.SampleGround(p.xz);
                var groundNormal = Source.SampleNormal(p.xz, 1f);
                var albedo = Source.SampleSurface(p.xz, ground, groundNormal).Albedo;
                rock = float4.zero;
                if (cave >= 0 && set.Caves[cave].Tunnel == 0)
                {
                    var share = 1f - math.smoothstep(RockNear, RockReach, math.distance(p, set.Caves[cave].Mouth));
                    if (share > 0f)
                    {
                        // The rock a cliff here would show: the surface layer for a 70 degree face down the slope.
                        var down = math.normalizesafe(groundNormal.xz, new float2(1f, 0f));
                        var steep = new float3(down.x * 0.94f, 0.342f, down.y * 0.94f);
                        rock = new float4(Source.SampleSurface(p.xz, ground, steep).Albedo, share);
                    }
                }
                // A road tunnel's floor carries its road through (W-110): the road's own colour, as painted on the
                // centre line outside, so it reads as the same road from portal to portal. W-138 row 4b: no longer
                // taken as the terrain look (that shaded it as daylit ground, a pale smear deep inside); the shader
                // draws it as its own surface (cobbles or packed grit) in the cave's light, and fallen stones in their
                // own pale grey.
                var tunnelCode = 0;
                var tunnelFloor = 0f;
                if (cave >= 0 && set.Caves[cave].Tunnel != 0)
                {
                    var floorY = TunnelFloor(ref set, set.Caves[cave], p, out var axis);
                    if (p.y < floorY + 1.2f)
                    {
                        var props = new SurfaceProps { Albedo = albedo };
                        if (Source.TunnelRoadSurface(axis, ref props) > 0f)
                        {
                            // Never brighter than worn stone: a pale road paint would glare under a lamp.
                            var lum = math.dot(props.Albedo, new float3(0.3f, 0.59f, 0.11f));
                            albedo = props.Albedo * math.min(1f, 0.32f / math.max(lum, 1e-3f));
                            tunnelFloor = 1f - math.smoothstep(floorY + 0.35f, floorY + 0.8f, p.y);
                        }
                        tunnelCode = 1 + (props.Paving > 0.4f ? 1 : 0);
                    }
                    else
                        tunnelCode = 1;
                    if (set.Caves[cave].Tunnel >= 2)
                        tunnelCode += 2;
                    var stone = 1f - math.smoothstep(0.05f, 0.25f, CaveSampling.StoneDistance(ref set, p));
                    tunnelFloor = tunnelFloor * (1f - stone) - stone;
                }
                // Ribbed caves: how much darker the hollow between two ribs is (crests a touch lighter), applied to
                // every look the wall takes, so the ribs read even in the flat light of a shallow ice cave; none where
                // the cave opens to the sky, so mouths still match the terrain.
                var ribShade = 0f;
                if (cave >= 0 && set.Caves[cave].Wall.z > 0f)
                {
                    var rib = CaveSampling.Rib(set.Caves[cave], p);
                    ribShade = (0.55f - 0.75f * rib * rib) * set.Caves[cave].Wall.z * (1f - open);
                }
                data = new float4(math.max(math.saturate(1f - (surface - p.y - 0.3f) / 2.5f), open),
                                  0.85f + 0.3f * (noise.snoise(p * 0.15f) * 0.5f + 0.5f),
                                  (cave >= 0 ? set.Caves[cave].Style : 0) + 32 * tunnelCode, tunnelCode > 0 ? tunnelFloor : ribShade);
                if (ShardProbe && cave < 0)
                {
                    data.x = 1f;
                    return new float4(1f, 0f, 1f, 1f);
                }
                return new float4(albedo, math.max(math.max(fromMouth, nearSurface), open));
            }
        }
    }
}
