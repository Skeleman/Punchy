#ifndef PUNCHY_BONE_INCLUDED
#define PUNCHY_BONE_INCLUDED

// W-166 row 3: the one bone look (MaterialFamily.Bone): gear bone and horn, totems, skull trophies. Ivory towards a
// shared tone, faint growth lines along the piece, small dark pits and soft stains; each detail fades out with the
// pixels and is centred on 1, so far bone keeps its tone (rule 25). Needs Sky.hlsl (the noise) included first.

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres (as Wood.hlsl's fade).
half PunchyBoneFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// Bone's albedo on its own surface: `uv` is (across, along) in metres on the piece.
half3 PunchyBone(half3 albedo, float2 uv, float footprint)
{
    half3 ivory = lerp(albedo, half3(0.86, 0.81, 0.68), 0.35);
    half lines = (PunchyValueNoise(float2(uv.x * 3.0, uv.y * 70.0)) - 0.5) * 0.1 * PunchyBoneFade(0.015, footprint);
    // Pits: about one cell in eight dark; minus their mean so the far tone holds.
    half pit = smoothstep(0.72, 0.8, PunchyValueNoise(uv * 95.0 + 3.7));
    half pits = (pit - 0.12) * 0.3 * PunchyBoneFade(0.012, footprint);
    half stain = (PunchyValueNoise(uv * 2.3 + 7.1) - 0.5) * 0.16 * PunchyBoneFade(0.3, footprint);
    return ivory * (1 + lines + stain - pits);
}

#endif
