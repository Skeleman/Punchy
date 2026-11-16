#ifndef PUNCHY_CLOTH_INCLUDED
#define PUNCHY_CLOTH_INCLUDED

// W-166 row 3: the one cloth look, woven linen and tanned hide (MaterialFamily.Cloth and Leather): tents, awnings,
// banners, bedrolls. Each detail fades out with the pixels and is centred on 1, so far cloth keeps its vertex colour
// (rule 25). Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Wood.hlsl's fade).
half PunchyClothFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// W-99: cloth's linen weave, on the facet's plane in the plant's own frame: 3 cm threads over and under in a
/// check, a little uneven along each thread (slubs), and broad soft blotches; fading out with the pixels.
half PunchyLinen(float3 p, float3 nOS, float footprint)
{
    float3 a = abs(nOS);
    float2 uv = a.x > a.y && a.x > a.z ? p.zy : a.y > a.z ? p.xz : p.xy;
    float2 t = uv / 0.03;
    float2 f = frac(t);
    int2 c = (int2)floor(t);
    bool warp = ((c.x + c.y) & 1) == 0;
    half thread = warp ? sin(f.x * 3.14159) : sin(f.y * 3.14159);
    half slub = PunchyValueNoise(warp ? float2(c.x * 1.7, t.y * 0.15) : float2(t.x * 0.15, c.y * 1.7 + 9.3));
    half fine = (thread * 0.7 + slub * 0.3 - 0.55) * 0.6 * PunchyClothFade(0.03, footprint);
    // Sewn panels: a dark seam every ~55 cm with a slight tone change per panel, so it reads at a few metres.
    float pv = uv.y / 0.55;
    half seam = (1 - smoothstep(0.0, 0.035, abs(frac(pv) - 0.5) - 0.465)) * 0.22;
    half panel = (frac(sin(floor(pv) * 12.9898) * 43758.55) - 0.5) * 0.12;
    half mid = (panel - seam) * PunchyClothFade(0.12, footprint);
    half coarse = (PunchyValueNoise(uv * 3.1) - 0.5) * 0.22 * PunchyClothFade(0.3, footprint);
    return 1 + fine + mid + coarse;
}

/// W-113: a tent's hide, as leather, not linen: blotchy tanning, fine creases and pores, so it reads at a few
/// metres. Playtest 11-07: the seams and stitches it had, on a square lattice about 80 cm apart, read as a grid
/// up close, not leather; the panels' own edges between the poles are the seams now, and each hide's tone
/// drifts softly over a metre or two instead of changing square by square.
half PunchyHide(float3 p, float3 nOS, float footprint)
{
    float3 a = abs(nOS);
    float2 uv = a.x > a.y && a.x > a.z ? p.zy : a.y > a.z ? p.xz : p.xy;
    half blotch = (PunchyValueNoise(uv * 1.4) - 0.5) * 0.34 + (PunchyValueNoise(uv * 4.3 + 5.1) - 0.5) * 0.16;
    half crease = (PunchyValueNoise(uv * float2(9, 26)) - 0.5) * 0.12 + (PunchyValueNoise(uv * 55) - 0.5) * 0.08;
    // Tanned unevenly: a slow drift in tone across the hide, and darker worn patches here and there.
    half hides = (PunchyValueNoise(uv * 0.55 + 11.3) - 0.5) * 0.16;
    half worn = smoothstep(0.62, 0.8, PunchyValueNoise(uv * 2.2 + 31.7)) * 0.1;
    return 1 + blotch * PunchyClothFade(0.4, footprint) + crease * PunchyClothFade(0.03, footprint)
             + (hides - worn) * PunchyClothFade(0.1, footprint);
}

#endif
