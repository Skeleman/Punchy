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

            /// <summary>
            /// A-128: a loose length of cloth or hide hanging from a point in two pieces (at, at + 1): down, pushed by the
            /// wind's rolling gusts (the trees' and hair's, CreatureAnimator.Wind) and swung back by the wearer's speed,
            /// the lower piece more than the upper. Stiff (0..1) holds it closer to hanging straight. Each piece is kept
            /// outside the wearer's legs and trunk (Drape), so a stride swings it out rather than walking through it.
            /// </summary>
            void Hanging(ref CreatureBlob b, int offset, float3 from, float3 face, float width, float widthEnd, float length, in CreatureState state,
                         float3 forward, float4 color, float faded, SegmentMaterial material, int at, float stiff = 0f)
            {
                var w = CreatureAnimator.Wind.Data;
                var dir = math.normalizesafe(new float2(w.y, w.z), new float2(1f, 0f));
                var phase = w.w * (0.7f + w.x * 1.1f) - math.dot(state.Position.xz, dir) * 0.04f + (state.Individual % 628u) * 0.01f;
                var gust = (0.6f + 0.4f * math.sin(phase) * math.sin(phase * 0.37f + 1.3f)) * (0.08f + 0.92f * w.x);
                var flutter = math.sin(w.w * 6f + state.Individual * 0.1f) * 0.15f * gust;
                var down = new float3(0f, -1f, 0f);
                var push = (new float3(dir.x, 0f, dir.y) * (gust * 0.9f + flutter) - forward * math.min(state.Speed * 0.12f, 0.8f)) * (1f - stiff);
                var widthMid = (width + widthEnd) * 0.5f;
                var upper = Drape(ref b, offset, ref from, math.normalize(down + push * 0.6f), length * 0.5f, width, width * 0.12f, face, true);
                var mid = from + upper * length * 0.5f;
                var lower = Drape(ref b, offset, ref mid, math.normalize(down + push * 1.2f), length * 0.5f, widthMid, widthMid * 0.12f, face, false);
                Output[at] = Piece(from, upper, face, length * 0.5f, width, widthMid, 0.12f, color, faded, material);
                Output[at + 1] = Piece(mid, lower, face, length * 0.5f, widthMid, widthEnd, 0.12f, color, faded, material);
            }
        }
    }
}
