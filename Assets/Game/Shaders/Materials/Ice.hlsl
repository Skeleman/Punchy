#ifndef PUNCHY_ICE_INCLUDED
#define PUNCHY_ICE_INCLUDED

// W-166 row 3: the one ice look (MaterialFamily.Ice): igloos, snow shelters and other built snow and ice. Rule: block
// joints on built ice and depth inside it. Cut blocks laid in staggered courses with packed-snow joints, each block its
// own whiteness, a glassy depth that shifts as the view swings (noise read a little way into the block), and a fine snow
// grain up close. Each detail fades out with the pixels and is centred on 1, so far ice keeps its colour (rule 25).
// Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Wood.hlsl's fade).
half PunchyIceFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// The ice albedo multiplier at world point p on a face with normal n, seen along view direction v; r is the plant's
/// own random (0..1).
half3 PunchyIce(float3 p, half3 n, float3 v, float footprint, half r)
{
    const float course = 0.38, len = 0.62, joint = 0.018;
    p += r * 23;
    float2 alongXZ = normalize(float2(-n.z, n.x) + 1e-4);
    float3 t = float3(alongXZ.x, 0, alongXZ.y);
    float3 bt = normalize(float3(0, 1, 0) - n * n.y + 1e-5);
    float2 uv = float2(dot(p.xz, alongXZ), p.y);
    // Staggered courses: each course slid along by its own amount, so the joints never line up.
    float row = floor(uv.y / course);
    float u = uv.x / len + PunchyCellHash(int2(int(row), 41)) + row * 0.5;
    float2 cell = float2(floor(u), row);
    float2 f = float2(frac(u) * len, frac(uv.y / course) * course);
    float edge = min(min(f.x, len - f.x), min(f.y, course - f.y));
    half fadeJoint = PunchyIceFade(course, footprint);
    half seam = (1 - smoothstep(joint - footprint * 0.5, joint + footprint * 0.5, edge)) * fadeJoint;
    // Each block its own whiteness: some packed denser and bluer, some fresher and whiter.
    half h = PunchyCellHash(int2(cell) + int2(int(r * 89), 13));
    half3 block = lerp(1, lerp(half3(0.9, 0.96, 1.05), half3(1.06, 1.04, 1.0), h), PunchyIceFade(len, footprint));
    // Depth: a cloudy glaze read a few centimetres in along the view, so it drifts against the face as you move.
    float3 vt = float3(dot(v, t), dot(v, bt), max(dot(v, n), 0.25));
    float2 inside = uv - vt.xy / vt.z * 0.05;
    half cloud = PunchyValueNoise(inside / 0.22 + h * 17) * 0.65 + PunchyValueNoise(inside / 0.09 + 5.3) * 0.35;
    half3 depth = lerp(1, lerp(half3(0.88, 0.95, 1.08), half3(1.07, 1.04, 1.0), cloud), PunchyIceFade(0.15, footprint));
    // Packed-snow joints: greyer, a touch darker in the gap.
    half3 packed = half3(0.86, 0.9, 0.96);
    // Snow grain: a fine sugary speckle.
    half grain = (PunchyValueNoise(uv / 0.015 + 3.1) - 0.5) * 0.1 * PunchyIceFade(0.015, footprint);
    return lerp(block * depth * (1 + grain), packed, seam) * (1 + seam * 0.03);
}

#endif
