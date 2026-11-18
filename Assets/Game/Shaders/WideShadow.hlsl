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
float4 _WideShadowParams;     // x how much of the direct sunlight a wide shadow takes (0 = off), y how far the
                              // last redraw has blended in over the one before (1 = done, the old map isn't read)
float4 _WideShadowFade;       // R-40: the real shadows' fade-out as URP lays it, saturate(distance^2 * x + y): the
                              // wide map's weight, so where one fades out the other fades in (x 0, y 1 when the
                              // real shadows are off and the wide map stands alone)
// R-16 row 3: the map before the last redraw, blended out over a second or two so far shadows slide instead of
// jumping each time the map is laid again.
TEXTURE2D_FLOAT(_WideShadowOldTex);
float4x4 _WideShadowOldMatrix;
float4 _WideShadowOldEye;
float4 _WideShadowOldSun;

// One bilinear compare: the four texels round uv (at their centres) each tested against along, the results lerped by
// where uv falls between them, so a shadow edge slides across a texel instead of stepping.
half PunchyWideBilinear(TEXTURE2D_PARAM(map, mapSampler), float2 uv, float along)
{
    float2 size = _WideShadowTex_TexelSize.zw;
    float2 tc = uv * size - 0.5;
    float2 base = floor(tc);
    float2 f = tc - base;
    // Gather order: w (u0, v0), z (u1, v0), x (u0, v1), y (u1, v1).
    float4 g = GATHER_RED_TEXTURE2D(map, mapSampler, (base + 1) / size);
    half4 c = half4(g < along);
    return lerp(lerp(c.w, c.z, f.x), lerp(c.x, c.y, f.x), f.y);
}

// 0..1: how much of one map's texels round c stand between this point and the sun. R-40: four bilinear compares half
// a texel apart (a 3x3-texel tent, 16 texels in four gathers), so the 1.6 m texels give a soft edge, not a staircase.
half PunchyWideTaps(TEXTURE2D_PARAM(map, mapSampler), float2 c, float3 positionWS, half3 normalWS, float4 eye, float3 sun)
{
    float facing = dot(normalWS, -sun);
    if (facing < 0.05)
        return 0;   // turned from the sun: dark from its own lighting already
    float slope = min(sqrt(saturate(1 - facing * facing)) / facing, 8);
    float along = (dot(positionWS - eye.xyz, sun) - (2 + 2 * slope)) * eye.w;
    float h = 0.5 * _WideShadowTex_TexelSize.x;
    half shade = PunchyWideBilinear(TEXTURE2D_ARGS(map, mapSampler), c + float2(-h, -h), along);
    shade += PunchyWideBilinear(TEXTURE2D_ARGS(map, mapSampler), c + float2(h, -h), along);
    shade += PunchyWideBilinear(TEXTURE2D_ARGS(map, mapSampler), c + float2(-h, h), along);
    shade += PunchyWideBilinear(TEXTURE2D_ARGS(map, mapSampler), c + float2(h, h), along);
    return shade * 0.25;
}

/// 0..1: the wide map's share of the shadow here, 1 minus the real shadows' (URP's own distance fade, same numbers).
float PunchyWideShadowFade(float3 positionWS)
{
    float3 v = positionWS - GetCameraPositionWS();
    return saturate(dot(v, v) * _WideShadowFade.x + _WideShadowFade.y);
}

// 0..1: how much of the direct sunlight a cached caster takes from this point, already times its share of the
// hand-off (PunchyWideShadowFade). normalWS scales the bias by the
// slope against the sun (the map's texels are about 1.6 m and its terrain is lowered 1.5 m, so the bias is metres).
half PunchyWideShadow(float3 positionWS, half3 normalWS)
{
    if (_WideShadowParams.x <= 0)
        return 0;
    float fade = PunchyWideShadowFade(positionWS);
    if (fade <= 0)
        return 0;
    float2 c = mul(_WideShadowMatrix, float4(positionWS, 1)).xy;
    // Out towards the map's edges the shadow fades rather than stopping on a line.
    float2 edge = min(c, 1 - c);
    fade *= smoothstep(0, 0.06, min(edge.x, edge.y));
    if (fade <= 0)
        return 0;
    half shade = PunchyWideTaps(TEXTURE2D_ARGS(_WideShadowTex, sampler_WideShadowTex), c, positionWS, normalWS, _WideShadowFrontEye, _WideShadowFrontSun.xyz);
    if (_WideShadowParams.y < 1)
    {
        float2 o = mul(_WideShadowOldMatrix, float4(positionWS, 1)).xy;
        half old = all(o > 0) && all(o < 1) ? PunchyWideTaps(TEXTURE2D_ARGS(_WideShadowOldTex, sampler_WideShadowTex), o, positionWS, normalWS, _WideShadowOldEye, _WideShadowOldSun.xyz) : 0;
        shade = lerp(old, shade, _WideShadowParams.y);
    }
    return shade * fade * _WideShadowParams.x;
}

/// R-40: the sunlight kept (0-1) where the real-time and the wide shadow hand over, so each point shows one shadow.
/// realShade is the real shadow times its share, (1 - attenuation) x (1 - fade), which is exactly what URP's own fade
/// leaves; wide is PunchyWideShadow (times the wide share). The larger of the two, over the larger share, so a point
/// both maps shade stays as dark as either alone (a plain blend or sum washes out halfway), and a shadow only one map
/// has keeps full strength until the other map takes over.
half PunchyShadowHandOff(half realShade, half wide, float fade)
{
    return 1 - max(realShade, wide) / max(1 - fade, fade);
}

#endif
