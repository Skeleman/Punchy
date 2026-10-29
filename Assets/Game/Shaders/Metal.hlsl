#ifndef PUNCHY_METAL_INCLUDED
#define PUNCHY_METAL_INCLUDED

// The metal look shared by procedural meshes (Flora.shader's metal parts) and creature gear (Creature.shader):
// the colour comes mostly from tinted reflections of the sky, with a sharp sun highlight and glints that twinkle as
// the surface moves and the view changes. Deterministic: the glints are hashed from `glintPos` (world position for
// fixed things, object or segment space for moving ones), no time. Needs Sky.hlsl (PunchySkyColor) included first.

/// Light to add on top of a metal surface whose diffuse is already drawn dimmed (about 0.45 of a plain surface).
/// `underground` (0..1) fades the sky reflection out under rock.
half3 PunchyMetalShade(half3 albedo, half3 n, half3 v, float3 glintPos, half3 lightColor, half3 lightDir, half shadow,
                       half ao, half underground)
{
    half3 h = normalize(lightDir + v);
    float3 r3 = reflect(-v, n);
    r3.y = max(r3.y, 0.05);
    half3 color = albedo * PunchySkyColor(r3, false, true) * 0.35 * ao * lerp(1, 0.06, underground);
    color += albedo * lightColor * shadow * pow(saturate(dot(n, h)), 90) * 3;
    float3 cell = floor(glintPos * 7);
    float sparkle = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
    float facing = pow(saturate(dot(normalize(n + (sparkle - 0.5) * 0.9), h)), 400);
    color += lightColor * shadow * facing * 14 * step(0.55, sparkle);
    return color;
}

#endif
