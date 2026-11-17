#ifndef PUNCHY_METAL_INCLUDED
#define PUNCHY_METAL_INCLUDED

// W-166 row 3: the one metal look (MaterialFamily.Metal), shared by procedural meshes (Flora.shader's metal parts) and creature gear (Creature.shader):
// the colour comes mostly from tinted reflections of the sky, with a sharp sun highlight and glints that twinkle as
// the surface moves and the view changes. Deterministic: the glints are hashed from `glintPos` (world position for
// fixed things, object or segment space for moving ones), no time. Needs Sky.hlsl (PunchySkyColor) included first.

// Pushed by MaterialLibrary (Punchy.Terrain), one row per MetalKind (#336):
//   light: x sky reflection, y sun highlight strength, z highlight power, w diffuse share (the dimming callers draw)
//   glint: x cells per metre, y strength, z share of cells that glint (as a threshold), w sharpness
float4 _PunchyMetalLightKinds[8];
float4 _PunchyMetalGlintKinds[8];

#define PUNCHY_METAL_IRON 0

/// How much of a plain surface's diffuse a metal of this kind keeps (callers dim their diffuse by it).
half PunchyMetalDiffuse(int kind)
{
    return _PunchyMetalLightKinds[kind].w;
}

/// Light to add on top of a metal surface whose diffuse is already drawn dimmed (PunchyMetalDiffuse, 0.45 for iron).
/// `underground` (0..1) fades the sky reflection out under rock. The kind's row (MaterialLibrary.Metal) sets the
/// reflection, highlight and glints.
half3 PunchyMetalShade(half3 albedo, half3 n, half3 v, float3 glintPos, half3 lightColor, half3 lightDir, half shadow,
                       half ao, half underground, int kind)
{
    float4 lightRow = _PunchyMetalLightKinds[kind];
    float4 glintRow = _PunchyMetalGlintKinds[kind];
    half3 h = normalize(lightDir + v);
    float3 r3 = reflect(-v, n);
    r3.y = max(r3.y, 0.05);
    half3 color = albedo * PunchySkyColor(r3, false, true) * lightRow.x * ao * lerp(1, 0.06, underground);
    color += albedo * lightColor * shadow * pow(saturate(dot(n, h)), lightRow.z) * lightRow.y;
    float3 cell = floor(glintPos * glintRow.x);
    float sparkle = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
    float facing = pow(saturate(dot(normalize(n + (sparkle - 0.5) * 0.9), h)), glintRow.w);
    color += lightColor * shadow * facing * glintRow.y * step(glintRow.z, sparkle);
    return color;
}

/// The same, for iron (every metal drawn so far).
half3 PunchyMetalShade(half3 albedo, half3 n, half3 v, float3 glintPos, half3 lightColor, half3 lightDir, half shadow,
                       half ao, half underground)
{
    return PunchyMetalShade(albedo, n, v, glintPos, lightColor, lightDir, shadow, ao, underground, PUNCHY_METAL_IRON);
}

#endif
