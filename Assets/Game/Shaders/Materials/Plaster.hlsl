#ifndef PUNCHY_PLASTER_INCLUDED
#define PUNCHY_PLASTER_INCLUDED

// W-166 row 3: the one plaster look (MaterialFamily.Plaster): lime and daub walls of huts, inns, smithies and kit
// houses. Rule: mottling and rain streaks, no unit joints (no block courses: those are brick's and stone's), with
// trowel sweeps and hairline cracks up close. Each detail fades out with the pixels and is centred on 1, so far plaster
// keeps its vertex colour (rule 25). Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Wood.hlsl's fade).
half PunchyPlasterFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// The plaster albedo multiplier at world point p on a face with normal n; r is the plant's own random (0..1).
half3 PunchyPlaster(float3 p, half3 n, float footprint, half r)
{
    p += r * 41;
    // Along the face, across the slope (walls and roofs alike), and down it.
    float across = dot(p.xz, normalize(float2(-n.z, n.x) + 1e-4));
    float2 uv = float2(across, p.y);
    // Mottling at three sizes, each kept only while the pixels can hold it, rescaled so what survives keeps its contrast.
    half w0 = PunchyPlasterFade(0.6, footprint), w1 = PunchyPlasterFade(2.2, footprint), w2 = PunchyPlasterFade(8, footprint);
    half m0 = PunchyValueNoise(uv / 0.6 + 3.7) * 0.6 + PunchyValueNoise(uv / 0.31 + 9.1) * 0.4;
    half m1 = PunchyValueNoise(uv / 2.2 + 13.1);
    half m2 = PunchyValueNoise(uv / 8.0 + 27.3);
    half sum = w0 * (m0 - 0.5) + w1 * (m1 - 0.5) * 1.2 + w2 * (m2 - 0.5) * 1.3;
    half weight = w0 + w1 * 1.2 + w2 * 1.3;
    half m = sum / max(weight, 0.5) * 2.0;
    // Lime whitens where it is thick and dries cooler and greyer where thin.
    half3 tint = lerp(half3(0.86, 0.87, 0.9), half3(1.12, 1.09, 1.02), saturate(m + 0.5));
    // Trowel sweeps: shallow arcs laid by hand, each half-metre patch swept round its own centre.
    float2 cell = floor(uv / 0.5);
    float2 centre = (cell + float2(PunchyCellHash(int2(cell)), PunchyCellHash(int2(cell) + int2(17, 3)))) * 0.5;
    half arc = sin(length(uv - centre + 0.35) / 0.09 * 6.2832);
    half sweep = arc * 0.035 * PunchyPlasterFade(0.09, footprint);
    // Hairline cracks: thin wandering lines where the noise crosses its middle, rarer than not.
    half crackNoise = abs(PunchyValueNoise(uv / 0.4 + 51.3) - 0.5) + abs(PunchyValueNoise(uv / 0.13 + 7.7) - 0.5) * 0.35;
    half crackMask = smoothstep(0.55, 0.75, PunchyValueNoise(uv / 1.7 + 33.1));
    half crack = (1 - smoothstep(0.012, 0.035, crackNoise)) * crackMask * 0.28 * PunchyPlasterFade(0.04, footprint);
    // Rain streaks down the walls (not roofs): narrow across, long down.
    half wall = 1 - smoothstep(0.35, 0.7, abs(n.y));
    half streak = smoothstep(0.55, 0.85, PunchyValueNoise(float2(across / 0.45, p.y / 3.5))) * PunchyPlasterFade(0.45, footprint);
    return tint * (1 + sweep - crack) * (1 - streak * wall * 0.2);
}

#define PUNCHY_PLASTER_LIME 0
#define PUNCHY_PLASTER_DAUB 1
#define PUNCHY_PLASTER_ADOBE 2

/// Wattle and daub (PlasterKind.Daub): clay pressed on by hand in lumps, straw flecks in it, shrinkage cracks in a
/// loose net, and the wattle's woven ribs showing faintly through where it is thin.
half3 PunchyDaub(float3 p, half3 n, float footprint, half r)
{
    p += r * 41;
    float across = dot(p.xz, normalize(float2(-n.z, n.x) + 1e-4));
    float2 uv = float2(across, p.y);
    half lump = PunchyValueNoise(uv / 0.11 + 4.1) * 0.6 + PunchyValueNoise(uv / 0.05 + 8.3) * 0.4;
    half press = (lump - 0.5) * 0.45 * PunchyPlasterFade(0.11, footprint);
    half2 fleckCell = frac(uv / 0.03) - 0.5;
    half fleckHash = PunchyCellHash(int2(floor(uv / 0.03)));
    half fleck = step(0.86, fleckHash) * (1 - smoothstep(0.05, 0.12, abs(dot(fleckCell, half2(0.6, 0.8))))) * PunchyPlasterFade(0.03, footprint);
    half wattle = (sin(across / 0.16 * 6.2832) * 0.5 + 0.5) * smoothstep(0.6, 0.85, PunchyValueNoise(uv / 1.3 + 21.7));
    half dry = smoothstep(0.55, 0.75, PunchyValueNoise(uv / 0.9 + 17.3));
    half f1 = abs(PunchyValueNoise(uv / 0.18 + 61.1) - 0.5);
    half f2 = abs(PunchyValueNoise(uv / 0.07 + 23.7) - 0.5);
    half crack = max(1 - smoothstep(0.006, 0.018, f1), (1 - smoothstep(0.008, 0.02, f2)) * 0.6) * dry * 0.4 * PunchyPlasterFade(0.05, footprint);
    half mottle = (PunchyValueNoise(uv / 1.8 + 13.9) - 0.5) * 0.3 * PunchyPlasterFade(1.8, footprint);
    half3 c = (1 + press + mottle - crack - wattle * 0.08 * PunchyPlasterFade(0.16, footprint)) * half3(1, 1, 1);
    return lerp(c, half3(1.3, 1.18, 0.85), fleck * 0.5);
}

/// Adobe (PlasterKind.Adobe): a sun-baked mud render, smooth-rubbed by hand in broad swirls, the bricks' courses
/// ghosting through it, pale where the rain has washed it, eroded rough at the foot of the wall.
half3 PunchyAdobe(float3 p, half3 n, float footprint, half r)
{
    p += r * 41;
    float across = dot(p.xz, normalize(float2(-n.z, n.x) + 1e-4));
    float2 uv = float2(across, p.y);
    float2 cell = floor(uv / 0.7);
    float2 centre = (cell + float2(PunchyCellHash(int2(cell) + int2(5, 9)), PunchyCellHash(int2(cell) + int2(23, 1)))) * 0.7;
    half swirl = sin(length(uv - centre + 0.5) / 0.16 * 6.2832) * 0.04 * PunchyPlasterFade(0.16, footprint);
    float br = uv.y / 0.12;
    float bc = uv.x / 0.36 + frac(floor(br) * 0.5);
    half joint = max(1 - smoothstep(0.0, 0.06, min(frac(br), 1 - frac(br))), 1 - smoothstep(0.0, 0.03, min(frac(bc), 1 - frac(bc))));
    half ghost = joint * smoothstep(0.55, 0.8, PunchyValueNoise(uv / 0.9 + 3.3)) * 0.12 * PunchyPlasterFade(0.12, footprint);
    half wall = 1 - smoothstep(0.35, 0.7, abs(n.y));
    half wash = smoothstep(0.5, 0.9, PunchyValueNoise(float2(across / 0.6, p.y / 2.5) + 9.9)) * wall * 0.12 * PunchyPlasterFade(0.6, footprint);
    half mottle = (PunchyValueNoise(uv / 2.4 + 31.7) - 0.5) * 0.2 * PunchyPlasterFade(2.4, footprint);
    half grit = (PunchyValueNoise(uv / 0.012) - 0.5) * 0.14 * PunchyPlasterFade(0.012, footprint);
    return (1 + swirl + mottle + grit - ghost) * (1 + wash);
}

/// The plaster look of a PlasterKind (MaterialLibrary.PlasterKind): lime is PunchyPlaster.
half3 PunchyPlasterKind(float3 p, half3 n, float footprint, half r, int kind)
{
    if (kind == PUNCHY_PLASTER_DAUB)
        return PunchyDaub(p, n, footprint, r);
    if (kind == PUNCHY_PLASTER_ADOBE)
        return PunchyAdobe(p, n, footprint, r);
    return PunchyPlaster(p, n, footprint, r);
}

#endif
