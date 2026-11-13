#ifndef PUNCHY_CHEMISTRY_OVERLAY_INCLUDED
#define PUNCHY_CHEMISTRY_OVERLAY_INCLUDED

// The spell-chemistry overlay (experimental, Assets/Game/Scripts/ChemistryBridge): ChemistryBridge runs a cell world in
// a 256 m window round the player and renders it into a map, one texel per 1 m cell:
//   r = burning (0..1, how fierce), g = burnt (grass burned off), b = ice (frozen water), a = frost on the ground
// Shaders read it through PunchyChemAt. With no bridge running nothing is set: _PunchyChemOn stays 0 and every
// shader looks exactly as it did before.

TEXTURE2D(_PunchyChemMap);  SAMPLER(sampler_PunchyChemMap);
float4 _PunchyChemRect;     // xy world XZ of the window's corner, zw 1 / its size in metres
float  _PunchyChemOn;       // 1 while the bridge runs (an unset global float is 0)

// The overlay at a world XZ: 0 outside the window or with the bridge off. (A texture read in a loop or vertex shader
// needs an explicit LOD, so this always reads LOD 0.)
half4 PunchyChemAt(float2 xz)
{
    float2 uv = (xz - _PunchyChemRect.xy) * _PunchyChemRect.zw;
    float inside = step(0.0, uv.x) * step(0.0, uv.y) * step(uv.x, 1.0) * step(uv.y, 1.0);
    return SAMPLE_TEXTURE2D_LOD(_PunchyChemMap, sampler_PunchyChemMap, uv, 0) * (inside * saturate(_PunchyChemOn));
}

// Emission for burning ground and blades: deep orange flickering to yellow (HDR, so bloom picks it up).
half3 PunchyChemFireGlow(half burning, float2 xz, float time)
{
    half flicker = 0.75 + 0.25 * sin(time * 11.0 + xz.x * 1.7 + xz.y * 2.3) * sin(time * 7.3 + xz.y * 1.1);
    return lerp(half3(1.6, 0.35, 0.04), half3(3.2, 1.4, 0.25), burning * flicker) * burning * flicker;
}

#endif
