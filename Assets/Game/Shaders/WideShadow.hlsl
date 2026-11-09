#ifndef PUNCHY_WIDE_SHADOW_INCLUDED
#define PUNCHY_WIDE_SHADOW_INCLUDED

// R-5: reading the wide cached sun-shadow map (WideShadow.cs) on the terrain and the flora, past the real-time
// shadows. The map keeps, per texel, how far along the sunlight (0-1 over its view's depth) the nearest huge caster
// is: the coarse terrain, the colossi and the huge landmarks. Globals set by WideShadow every frame; while the map is
// off or not laid yet _WideShadowParams.x is 0 and nothing is read.

TEXTURE2D_FLOAT(_WideShadowTex);
SAMPLER(sampler_WideShadowTex);
float4 _WideShadowTex_TexelSize;
float4x4 _WideShadowMatrix;   // world to the map's uv
float4 _WideShadowFrontEye;   // xyz the map's near plane centre, w one over its depth
float4 _WideShadowFrontSun;   // xyz the way the sunlight travelled when the map was laid
float4 _WideShadowParams;     // x how much of the direct sunlight a wide shadow takes (0 = off)
float4 _WideShadowFade;       // fades in over x..y (m from the camera): the real shadows' last quarter

// 0..1: how much of the direct sunlight a cached caster takes from this point. normalWS scales the bias by the
// slope against the sun (the map's texels are about 1.6 m and its terrain is lowered 1.5 m, so the bias is metres).
half PunchyWideShadow(float3 positionWS, half3 normalWS)
{
    if (_WideShadowParams.x <= 0)
        return 0;
    float d = distance(positionWS, GetCameraPositionWS());
    float fade = smoothstep(_WideShadowFade.x, _WideShadowFade.y, d);
    if (fade <= 0)
        return 0;
    float2 c = mul(_WideShadowMatrix, float4(positionWS, 1)).xy;
    // Out towards the map's edges the shadow fades rather than stopping on a line.
    float2 edge = min(c, 1 - c);
    fade *= smoothstep(0, 0.06, min(edge.x, edge.y));
    if (fade <= 0)
        return 0;
    float facing = dot(normalWS, -_WideShadowFrontSun.xyz);
    if (facing < 0.05)
        return 0;   // turned from the sun: dark from its own lighting already
    float slope = min(sqrt(saturate(1 - facing * facing)) / facing, 8);
    float along = (dot(positionWS - _WideShadowFrontEye.xyz, _WideShadowFrontSun.xyz) - (2 + 2 * slope)) * _WideShadowFrontEye.w;
    float t = _WideShadowTex_TexelSize.x;
    half shade = 0;
    shade += SAMPLE_TEXTURE2D_LOD(_WideShadowTex, sampler_WideShadowTex, c + float2(-t, -t), 0).r < along ? 0.25 : 0;
    shade += SAMPLE_TEXTURE2D_LOD(_WideShadowTex, sampler_WideShadowTex, c + float2(t, -t), 0).r < along ? 0.25 : 0;
    shade += SAMPLE_TEXTURE2D_LOD(_WideShadowTex, sampler_WideShadowTex, c + float2(-t, t), 0).r < along ? 0.25 : 0;
    shade += SAMPLE_TEXTURE2D_LOD(_WideShadowTex, sampler_WideShadowTex, c + float2(t, t), 0).r < along ? 0.25 : 0;
    return shade * fade * _WideShadowParams.x;
}

#endif
