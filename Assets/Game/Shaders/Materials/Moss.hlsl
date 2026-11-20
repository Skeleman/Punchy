#ifndef PUNCHY_MOSS_INCLUDED
#define PUNCHY_MOSS_INCLUDED

// One moss look for moss sheets and moss on wet stone (rule 33). p in metres, footprint in metres per pixel.

float PunchyMossHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float PunchyMossNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(PunchyMossHash(i), PunchyMossHash(i + float3(1, 0, 0)), f.x),
                     lerp(PunchyMossHash(i + float3(0, 1, 0)), PunchyMossHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(PunchyMossHash(i + float3(0, 0, 1)), PunchyMossHash(i + float3(1, 0, 1)), f.x),
                     lerp(PunchyMossHash(i + float3(0, 1, 1)), PunchyMossHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

half PunchyMossClump(float3 p)
{
    return PunchyMossNoise(p * 9) * 0.6 + PunchyMossNoise(p * 31 + 3.1) * 0.4;
}

half PunchyMossHem(float3 p, half cover)
{
    return cover * 1.4 + PunchyMossClump(p) + (PunchyMossNoise(p * 70 + 1.3) - 0.5) * 0.4 - 1.0;
}

// Each scale fades to its mean under a few pixels, so the mat never shimmers (rule 20).
half3 PunchyMossMat(float3 p, half cover, float footprint)
{
    half fClump = smoothstep(3, 8, 0.11 / max(footprint, 1e-5));
    half fTuft = smoothstep(3, 8, 0.038 / max(footprint, 1e-5));
    half fFine = smoothstep(3, 8, 0.0125 / max(footprint, 1e-5));
    half fCushion = smoothstep(3, 8, 0.3 / max(footprint, 1e-5));
    half cushion = lerp(0.5, PunchyMossNoise(p * 3.3 + 4.2), fCushion);
    half clump = lerp(0.5, PunchyMossClump(p), fClump);
    half fine = lerp(0.5, PunchyMossNoise(p * 80), fFine);
    half tuft = lerp(0.62, smoothstep(0.25, 0.75, PunchyMossNoise(p * 26 + 9.7)), fTuft);
    return lerp(half3(0.42, 0.56, 0.36), half3(1.0, 0.98, 0.62), clump) * (0.8 + 0.4 * fine)
         * lerp(0.5, 1.15, tuft) * lerp(0.75, 1, saturate(cover * 2)) * lerp(0.6, 1.25, smoothstep(0.2, 0.8, cushion));
}

// A torn edge only where it spans enough pixels; far, the painted cover stays soft.
half PunchyMossOnStone(float3 p, half cover, float footprint)
{
    half hem = PunchyMossHem(p, cover);
    half edge = lerp(0.6, 0.06, smoothstep(3, 8, 0.11 / max(footprint, 1e-5)));
    return smoothstep(-edge, edge, hem) * saturate(cover * 3);
}

#endif
