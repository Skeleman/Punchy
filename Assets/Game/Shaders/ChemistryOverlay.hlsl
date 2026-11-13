#ifndef PUNCHY_CHEMISTRY_OVERLAY_INCLUDED
#define PUNCHY_CHEMISTRY_OVERLAY_INCLUDED

// The spell-chemistry overlay (experimental, Assets/Game/Scripts/ChemistryBridge): ChemistryBridge runs a cell world in
// a 256 m window round the player and renders it into two maps, one texel per 1 m cell:
//   _PunchyChemMap:  r = burning (0..1, how fierce), g = burnt (grass burned off), b = ice (frozen water), a = frost
//   _PunchyChemMap2: r = water standing on land, g = steam, b = smoke (a unused)
// Shaders read them through PunchyChemAt / PunchyChem2At (at a noise-warped point), and PunchyChemSoft turns a
// bilinear read into a soft, wandering edge, so cells never show as squares. With no bridge running nothing is set:
// _PunchyChemOn stays 0 and every shader looks exactly as it did before.

TEXTURE2D(_PunchyChemMap);  SAMPLER(sampler_PunchyChemMap);
TEXTURE2D(_PunchyChemMap2); SAMPLER(sampler_PunchyChemMap2);
float4 _PunchyChemRect;     // xy world XZ of the window's corner, zw 1 / its size in metres
float  _PunchyChemOn;       // 1 while the bridge runs (an unset global float is 0)

// Value noise on an integer lattice hash (stays clean thousands of metres out; see PunchyCellHash in Sky.hlsl).
float PunchyChemHash(int2 cell)
{
    uint h = asuint(cell.x) * 0x8da6b343u ^ asuint(cell.y) * 0xd8163841u;
    h ^= h >> 13;
    h *= 0x85ebca6bu;
    h ^= h >> 16;
    return h * (1.0 / 4294967296.0);
}

float PunchyChemNoise(float2 p)
{
    float2 fl = floor(p);
    int2 i = int2(fl);
    float2 f = p - fl;
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(PunchyChemHash(i), PunchyChemHash(i + int2(1, 0)), u.x),
                lerp(PunchyChemHash(i + int2(0, 1)), PunchyChemHash(i + int2(1, 1)), u.x), u.y);
}

// Where the maps are read for a point: pushed about by two octaves of noise (up to about 1.5 m), so the 1 m cells and
// the spells' square bursts come out as wavy, rounded shapes (a domain warp; the same everywhere, so grass, ground and
// water agree).
float2 PunchyChemWarp(float2 xz)
{
    float2 q = xz * 0.21;
    float2 w = float2(PunchyChemNoise(q), PunchyChemNoise(q + 31.7)) - 0.5;
    float2 q2 = xz * 0.83;
    w = w * 2.4 + (float2(PunchyChemNoise(q2 + 11.3), PunchyChemNoise(q2 + 57.1)) - 0.5) * 0.9;
    return xz + w;
}

float2 PunchyChemUV(float2 xz, out float inside)
{
    float2 uv = (PunchyChemWarp(xz) - _PunchyChemRect.xy) * _PunchyChemRect.zw;
    inside = step(0.0, uv.x) * step(0.0, uv.y) * step(uv.x, 1.0) * step(uv.y, 1.0) * saturate(_PunchyChemOn);
    return uv;
}

// The overlay at a world XZ: 0 outside the window or with the bridge off. (A texture read in a loop or vertex shader
// needs an explicit LOD, so this always reads LOD 0.)
half4 PunchyChemAt(float2 xz)
{
    half4 v = 0;
    [branch] if (_PunchyChemOn > 0)   // (bridge off: skip the warp's noise too)
    {
        float inside;
        float2 uv = PunchyChemUV(xz, inside);
        v = SAMPLE_TEXTURE2D_LOD(_PunchyChemMap, sampler_PunchyChemMap, uv, 0) * inside;
    }
    return v;
}

half4 PunchyChem2At(float2 xz)
{
    half4 v = 0;
    [branch] if (_PunchyChemOn > 0)   // (bridge off: skip the warp's noise too)
    {
        float inside;
        float2 uv = PunchyChemUV(xz, inside);
        v = SAMPLE_TEXTURE2D_LOD(_PunchyChemMap2, sampler_PunchyChemMap2, uv, 0) * inside;
    }
    return v;
}

// A soft, organic edge for a channel read bilinearly off the 1 m grid: thresholds it near 0.5, the threshold wandering
// with two octaves of noise (about a cell either way), and fades over a narrow band. rim is 1 right at the edge and
// falls to 0 a little inside it (for an ice lip or a wet margin).
half PunchyChemSoft(half v, float2 xz, out half rim)
{
    half n = PunchyChemNoise(xz * 0.55) * 0.65 + PunchyChemNoise(xz * 1.9 + 7.3) * 0.35;
    half t = 0.5 + (n - 0.5) * 0.55;
    half soft = smoothstep(t - 0.07, t + 0.07, v) * step(0.002, v);
    rim = soft * (1 - smoothstep(t + 0.02, t + 0.22, v));
    return soft;
}

half PunchyChemSoft(half v, float2 xz)
{
    half rim;
    return PunchyChemSoft(v, xz, rim);
}

// Emission for burning ground and blades: deep orange flickering to yellow (HDR, so bloom picks it up).
half3 PunchyChemFireGlow(half burning, float2 xz, float time)
{
    half flicker = 0.75 + 0.25 * sin(time * 11.0 + xz.x * 1.7 + xz.y * 2.3) * sin(time * 7.3 + xz.y * 1.1);
    return lerp(half3(1.6, 0.35, 0.04), half3(3.2, 1.4, 0.25), burning * flicker) * burning * flicker;
}

#endif
