#ifndef PUNCHY_BRICK_INCLUDED
#define PUNCHY_BRICK_INCLUDED

// W-166 row 3: the one brick look (MaterialFamily.Brick): chimney stacks and anything else laid in fired brick.
// Rule: courses with offset joints and recessed mortar. Bricks are too small for real geometry, so the recess is drawn:
// the brick edges lean their normals into the joints and a one-step parallax slides the mortar behind the brick faces as
// the view swings. The warm fired colour stays at every range (it is the material's colour, luminance kept at 1); the
// joints, the per-brick tints and the relief fade out with the pixels (rule 20), so far brick keeps its tone (rule 25).
// Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Wood.hlsl's fade).
half PunchyBrickFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// Distance (m) from face point uv to the nearest mortar joint centre, split into (to a head joint, to a bed joint),
/// and which way each lies (-1 or +1); cell is the brick's own index.
float2 PunchyBrickJoints(float2 uv, out float2 side, out float2 cell)
{
    const float course = 0.075, len = 0.225;
    float row = floor(uv.y / course);
    float u = uv.x / len + row * 0.5;   // running bond: each course half a brick along
    cell = float2(floor(u), row);
    float2 f = float2(frac(u) * len, frac(uv.y / course) * course);
    side = float2(f.x < len * 0.5 ? -1 : 1, f.y < course * 0.5 ? -1 : 1);
    return float2(min(f.x, len - f.x), min(f.y, course - f.y));
}

/// The brick albedo multiplier at world point p on a face with normal n (leaned in place for the joints), seen along
/// view direction v; r is the plant's own random (0..1).
half3 PunchyBrick(float3 p, inout half3 n, float3 v, float footprint, half r)
{
    const half mortar = 0.0055;   // half a joint's width (m)
    p += r * 37;
    // The face's frame: along it (walls and stacks), up it.
    float2 alongXZ = normalize(float2(-n.z, n.x) + 1e-4);
    float3 t = float3(alongXZ.x, 0, alongXZ.y);
    float3 bt = normalize(float3(0, 1, 0) - n * n.y + 1e-5);
    float2 uv = float2(dot(p.xz, alongXZ), p.y);
    half fadeJoint = PunchyBrickFade(0.075, footprint);
    half fadeBrick = PunchyBrickFade(0.225, footprint);
    // One-step parallax: where the first look lands in mortar, look again 6 mm further in along the view.
    float3 vt = float3(dot(v, t), dot(v, bt), max(dot(v, n), 0.3));
    float2 side, cell;
    float2 d = PunchyBrickJoints(uv, side, cell);
    half sunk = 1 - smoothstep(mortar, mortar + footprint, min(d.x, d.y));
    d = PunchyBrickJoints(uv - vt.xy / vt.z * 0.006 * sunk * fadeJoint, side, cell);
    float edge = min(d.x, d.y);
    half joint = (1 - smoothstep(mortar - footprint * 0.5, mortar + footprint * 0.5, edge)) * fadeJoint;
    // Each brick fired a little differently: darker clinkers, paler soft ones, with a fine sandy speckle.
    half h = PunchyCellHash(int2(cell) + int2(int(r * 97), 5));
    half3 fired = lerp(half3(0.84, 0.86, 0.9), half3(1.12, 1.08, 1.02), h);
    half speckle = (PunchyValueNoise(uv / 0.018 + 11.3) - 0.5) * 0.12 * PunchyBrickFade(0.018, footprint);
    half3 brick = lerp(1, fired * (1 + speckle) * 0.96, fadeBrick);
    // Lime mortar: paler and greyer, a touch darker deep in the groove where it sits back.
    half3 lime = half3(1.04, 1.12, 1.22) * lerp(0.86, 1, saturate((mortar - edge) / mortar));
    // The fired colour, kept at every range: warm, with its luminance at 1 so a stone-grey vertex colour reads as brick.
    half3 warm = half3(1.22, 0.95, 0.83);
    // The rounded brick arrises lean their light into the joints.
    half2 lean = (1 - smoothstep(mortar, mortar + 0.012, d)) * step(mortar, d) * side * 0.55 * fadeJoint;
    n = normalize(n + t * lean.x + bt * lean.y);
    return warm * lerp(brick, lime, joint);
}

#endif
