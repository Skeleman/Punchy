using Unity.Mathematics;

namespace Punchy.Fauna
{
    // A-128: worn cloth that hangs over a body (a worker's apron, an ogre guard's cape, a sash's tails) kept outside it.
    public partial class CreatureRenderSystem
    {
        partial struct PoseJob
        {
            /// <summary>
            /// A-128: a piece of cloth hung over a posed body clears it, the kit's counterpart of the robe panels'
            /// CreatureAnimator.FollowLegs. The piece from <paramref name="from"/> along <paramref name="along"/> is swung
            /// out towards <paramref name="outward"/> (its face) just far enough that it passes outside every leg and
            /// trunk segment under it, so a stride pushes an apron forward with the thigh, a trailing leg lifts a cape
            /// behind, and a broad chest never shows through. It is never swung in past where it hung. Arms, heads,
            /// tails and wings are left out: they swing past cloth rather than under it. A piece that is
            /// <paramref name="seated"/> (the top of a bib or a cape, not one hung from another piece) is first moved out
            /// to sit on the body near its top, so a bulge just under where it hangs doesn't flare it out like a board.
            /// </summary>
            /// <param name="width">Half the piece's width (its radius across), so a segment off to its side is missed.</param>
            /// <param name="thickness">Half the piece's thickness, kept clear of the body as well.</param>
            /// <returns>The direction to draw the piece along (unit).</returns>
            float3 Drape(ref CreatureBlob b, int offset, ref float3 from, float3 along, float length, float width, float thickness, float3 outward,
                         bool seated)
            {
                // Work in the plane of the hang and the outward face: an angle out from straight down.
                var hang = math.normalizesafe(along - outward * math.dot(along, outward), along);
                var across = math.normalizesafe(math.cross(hang, outward), new float3(1f, 0f, 0f));
                var angle = math.atan2(math.dot(along, outward), math.dot(along, hang));
                var start = angle;
                var seat = 0f;
                // (Pass 0 seats it, pass 1 swings it out. Part -1: the body itself, segment 0.)
                for (int pass = seated ? 0 : 1; pass < 2; pass++)
                for (int i = -1; i < b.Parts.Length; i++)
                {
                    int first = 0, count = 1;
                    if (i >= 0)
                    {
                        ref var part = ref b.Parts[i];
                        // (The legs, and the trunk: a top-level part that's no limb, as the kits find the chest.)
                        var trunk = part.Parent < 0 && (part.Role == LimbRole.Neck || part.Role == LimbRole.Feature);
                        if (part.Role != LimbRole.Leg && !trunk)
                            continue;
                        first = part.FirstSegment;
                        count = part.SegmentCount;
                    }
                    for (int j = 0; j < count; j++)
                    {
                        var seg = offset + first + j;
                        if (seg >= Output.Length)
                            break;
                        var s = Output[seg];
                        if (s.Length <= 0f && s.RadiusStart <= 0f)
                            continue;
                        var axis = math.mul(s.Rotation, new float3(0f, 0f, 1f));
                        var sx = math.mul(s.Rotation, new float3(1f, 0f, 0f));
                        var sy = math.mul(s.Rotation, new float3(0f, 1f, 0f));
                        // (How far a flattened segment reaches out towards the cloth, and to its side, per unit radius.)
                        var reachOut = math.sqrt(math.square(math.dot(sx, outward)) + math.square(s.Flatten * math.dot(sy, outward)));
                        var reachSide = math.sqrt(math.square(math.dot(sx, across)) + math.square(s.Flatten * math.dot(sy, across)));
                        // Its start, middle and end, each a ball of the segment's radius there.
                        for (int n = 0; n < 3; n++)
                        {
                            var t = n * 0.5f;
                            var radius = math.lerp(s.RadiusStart, s.RadiusEnd, t);
                            var d = s.Position + axis * s.Length * t - from;
                            var down = math.dot(d, hang);
                            if (math.abs(math.dot(d, across)) - radius * reachSide > width)
                                continue;
                            var outBy = math.dot(d, outward) + radius * reachOut * 1.08f + thickness - seat;
                            if (pass == 0 && down > -length * 0.05f && down <= length * 0.3f)
                                seat = math.max(seat, outBy);
                            else if (pass == 1 && down > length * 0.05f && down <= length * 1.05f)
                                angle = math.max(angle, math.atan2(outBy, down));
                        }
                    }
                }
                from += outward * seat;
                // (Never so far out it stands off the body like a board.)
                angle = math.min(angle, math.max(start, 1.2f));
                return hang * math.cos(angle) + outward * math.sin(angle);
            }
        }
    }
}
