#ifndef PUNCHY_SURFACE_INCLUDED
#define PUNCHY_SURFACE_INCLUDED

// R-23: the one surface lighting path. Every surface shader used to put its sun, shadow tiers, ambient, glow lights and
// lantern together by hand, so a fix to the blend between shadow tiers landed in one shader and not the next (#272).
// Each now fills a PunchySurface, asks PunchySurfaceLight for the terms, lights with its own diffuse (or URP's PBR),
// and ends with PunchySurfaceFinish. Where shaders differ on purpose they set it in the struct (sun and sky reach,
// the wide shadow's normal, the shadow point); where they still differ by drift, a compile-time flag set before this
// include keeps today's look until each fix is shown and agreed (no keywords: the variant count does not grow).
//
// Flags (define before including):
// - PUNCHY_SURFACE_GLOW_WORLD: the far glow map too (PunchyGlowWorld), not only the 16 near glow lights.
// - PUNCHY_SURFACE_LANTERN: the player's lantern as a soft diffuse term in lit.lantern. The shader declares
//   _PunchyLantern and _PunchyLanternColor itself (Flora.hlsl, Water.shader); without the flag lit.lantern is 0.
// - PUNCHY_SURFACE_REAL_LIGHTS: real lights (PunchyRealLights, the glow's falloff) in lit.lights; the shader sets
//   s.positionCS. Under Forward+ the lantern is one of them, so lit.lantern stays 0 there.
//
// Needs URP's Lighting.hlsl, Sky.hlsl, WideShadow.hlsl and Glow.hlsl included first.

/// What the shader knows about the point it lights.
struct PunchySurface
{
    float3 positionWS;
    float3 shadowPositionWS;   // where shadows are looked up: positionWS, but the terrain skirt's lifted point
    half3 normalWS;
    half3 wideNormalWS;        // the normal the wide map's bias uses: terrain its own, plants straight up
    half sunReach;             // how much of the sun gets here: 1 outside, 0 for cave plants and underground
    half skyReach;             // how much of the sky's ambient gets here: 1 outside, a little in caves
    half glowWorldReach;       // how much of the far glow map: 1, plants 0 in caves (0 skips the lookup)
    float4 positionCS;         // the clip position, for real lights' Forward+ clusters (PUNCHY_SURFACE_REAL_LIGHTS)
};

/// The light terms at that point, shared by every surface.
struct PunchyLit
{
    Light sun;          // the main light, its shadowAttenuation the real-time (cascade) shadow
    half wide;          // the wide cached shadow of the huge things (R-5)
    half cloud;         // the cloud shadow
    half shadow;        // the one tier blend: min(real-time, 1 - wide) x (1 - cloud) x sunReach
    half3 ambient;      // the sky's ambient x skyReach
    half3 glow;         // glow lights (and the far glow map under PUNCHY_SURFACE_GLOW_WORLD)
    half3 lantern;      // the lantern's soft diffuse light (PUNCHY_SURFACE_LANTERN, not under Forward+), else 0
    half3 lights;       // real lights (PUNCHY_SURFACE_REAL_LIGHTS), else 0
};

/// A surface's defaults: shadows looked up where it is, the wide map biased along its normal, sun and sky in full.
PunchySurface PunchySurfaceAt(float3 positionWS, half3 normalWS)
{
    PunchySurface s;
    s.positionWS = positionWS;
    s.shadowPositionWS = positionWS;
    s.normalWS = normalWS;
    s.wideNormalWS = normalWS;
    s.sunReach = 1;
    s.skyReach = 1;
    s.glowWorldReach = 1;
    s.positionCS = 0;
    return s;
}

/// The real-time shadow and the wide one where they overlap: the darker, so they never double.
half PunchyShadowTiers(half realtime, half wide)
{
    return min(realtime, 1 - wide);
}

PunchyLit PunchySurfaceLight(PunchySurface s)
{
    PunchyLit lit;
    lit.sun = GetMainLight(TransformWorldToShadowCoord(s.shadowPositionWS));
    lit.wide = PunchyWideShadow(s.shadowPositionWS, s.wideNormalWS);
    lit.cloud = PunchyCloudShadow(s.shadowPositionWS);
    lit.shadow = PunchyShadowTiers(lit.sun.shadowAttenuation, lit.wide) * (1 - lit.cloud);
    lit.shadow *= s.sunReach;
    lit.ambient = SampleSH(s.normalWS) * s.skyReach;
    lit.glow = PunchyGlowLight(s.positionWS, s.normalWS);
    #ifdef PUNCHY_SURFACE_GLOW_WORLD
    [branch] if (s.glowWorldReach > 0)
        lit.glow += PunchyGlowWorld(s.positionWS, s.normalWS) * s.glowWorldReach;
    #endif
    lit.lights = 0;
    #ifdef PUNCHY_SURFACE_REAL_LIGHTS
    lit.lights = PunchyRealLights(s.positionWS, s.normalWS, s.positionCS);
    #endif
    lit.lantern = 0;
    #if defined(PUNCHY_SURFACE_LANTERN) && !(defined(PUNCHY_SURFACE_REAL_LIGHTS) && PUNCHY_REAL_LIGHTS)
    if (_PunchyLantern.w > 0)
    {
        float3 toLantern = _PunchyLantern.xyz - s.positionWS;
        float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
        lit.lantern = _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * saturate(dot(s.normalWS, normalize(toLantern)) * 0.7 + 0.3);
    }
    #endif
    return lit;
}

/// For URP-PBR surfaces, after UniversalFragmentPBR (which knows nothing of the wide or cloud shadow): dims exactly the
/// share of the light that came straight from the sun, by how much of it the wide and cloud shadows take away.
/// bakedGI is the ambient the PBR call was given.
half3 PunchySurfaceSunDim(half3 color, PunchySurface s, PunchyLit lit, half3 bakedGI)
{
    if (lit.cloud > 0 || lit.wide > 0)
    {
        half sunlit = Luminance(lit.sun.color) * saturate(dot(s.normalWS, lit.sun.direction));
        half direct = sunlit * lit.sun.shadowAttenuation;
        half kept = sunlit * PunchyShadowTiers(lit.sun.shadowAttenuation, lit.wide) * (1 - lit.cloud);
        half ambient = Luminance(bakedGI);
        color *= 1 - (direct - kept) / max(direct + ambient, 1e-3);
    }
    return color;
}

/// The last step for every surface: the haze and atmosphere.
half3 PunchySurfaceFinish(half3 color, PunchySurface s)
{
    return PunchyAtmosphere(color, s.positionWS);
}

#endif
