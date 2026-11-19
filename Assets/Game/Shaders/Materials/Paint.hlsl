#ifndef PUNCHY_PAINT_INCLUDED
#define PUNCHY_PAINT_INCLUDED

// The one painted look (MaterialFamily.Paint): trim boards, buoy floats and other painted accents. Rule: a smooth
// coat with a slow drift in tone, faint brush strokes along the piece, and small chips that show a darker coat under
// it. No grain, joints or relief (those belong to wood, brick and stone). Each detail fades out with the pixels and is
// centred near 1, so far paint keeps its vertex colour (rule 25). Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Plaster.hlsl's fade).
half PunchyPaintFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// The paint albedo multiplier at world point p on a face with normal n; r is the plant's own random (0..1).
half3 PunchyPaint(float3 p, half3 n, float footprint, half r)
{
    p += r * 37;
    // Along the face (walls and tops alike) and up it.
    float across = dot(p.xz, normalize(float2(-n.z, n.x) + 1e-4));
    float2 uv = float2(across, abs(n.y) > 0.7 ? p.z : p.y);
    // A slow drift in tone: thicker and thinner coats, faded by the sun in patches.
    half drift = (PunchyValueNoise(uv / 1.3 + 5.3) - 0.5) * PunchyPaintFade(1.3, footprint);
    // Brush strokes: long and thin along the piece.
    half stroke = (PunchyValueNoise(float2(uv.x / 0.35, uv.y / 0.012)) - 0.5) * PunchyPaintFade(0.012, footprint);
    // Chips: small patches where the coat has flaked to a darker, greyer one under it, rarer than not.
    half chipNoise = PunchyValueNoise(uv / 0.07 + 21.7) * 0.7 + PunchyValueNoise(uv / 0.025 + 3.1) * 0.3;
    half chipMask = smoothstep(0.6, 0.8, PunchyValueNoise(uv / 0.9 + 44.9));
    half chip = smoothstep(0.62, 0.68, chipNoise) * chipMask * PunchyPaintFade(0.05, footprint);
    half3 coat = 1 + drift * 0.12 + stroke * 0.05;
    return lerp(coat, half3(0.62, 0.6, 0.58), chip * 0.8);
}

#endif
