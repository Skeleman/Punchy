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
// - PUNCHY_SURFACE_SHADOW_FADE: the real-time shadow fades out at the shadow distance as UniversalFragmentPBR fades it
//   (URP's GetMainLight with the position), for diffuse shaders that sit on PBR ground (grass).
//
// Needs URP's Lighting.hlsl, Atmosphere.hlsl (or Sky.hlsl, which includes it), WideShadow.hlsl and Glow.hlsl included first.

// W-179: the one cave haze. The everywhere light of the cave the camera is in (CaveStyle.GlowEverywhere: Crystal's
// purple, Ice's blue), already faded out as the camera leaves the cave (CaveSystem.CameraCaveAir). PunchySurfaceFinish
// adds it to every surface, so fungi, creatures, items and props sit in the same air as the cave walls (which add the
// same term per cave from the style table in Cave.shader).
float4 _PunchyCaveAir;

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
    half caveAir;              // how much of the camera's cave air: 1, 0 for what only stands outside (terrain, grass)
    float4 positionCS;         // the clip position, for real lights' Forward+ clusters (PUNCHY_SURFACE_REAL_LIGHTS)
};

/// The light terms at that point, shared by every surface.
struct PunchyLit
{
    Light sun;          // the main light, its shadowAttenuation the real-time (cascade) shadow
    half wide;          // the wide cached shadow of the huge things (R-5), times its share of the hand-off
    half realShade;     // the real-time shadow times its share, as URP's PBR fade leaves it (R-40)
    half tiers;         // the sunlight the two shadow tiers keep, one shadow per point (PunchyShadowHandOff)
    half cloud;         // the cloud shadow
    half shadow;        // tiers x (1 - cloud) x sunReach
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
    s.caveAir = 1;
    s.positionCS = 0;
    return s;
}

PunchyLit PunchySurfaceLight(PunchySurface s)
{
    PunchyLit lit;
    #ifdef PUNCHY_SURFACE_SHADOW_FADE
    lit.sun = GetMainLight(TransformWorldToShadowCoord(s.shadowPositionWS), s.shadowPositionWS, half4(1, 1, 1, 1));
    #else
    lit.sun = GetMainLight(TransformWorldToShadowCoord(s.shadowPositionWS));
    #endif
    // R-40: the real shadow's share is 1 minus the wide map's; with the fade flag URP has already applied it.
    float handOff = PunchyWideShadowFade(s.shadowPositionWS);
    #ifdef PUNCHY_SURFACE_SHADOW_FADE
    lit.realShade = 1 - lit.sun.shadowAttenuation;
    #else
    lit.realShade = (1 - lit.sun.shadowAttenuation) * (1 - handOff);
    #endif
    lit.wide = PunchyWideShadow(s.shadowPositionWS, s.wideNormalWS);
    lit.tiers = PunchyShadowHandOff(lit.realShade, lit.wide, handOff);
    lit.cloud = PunchyCloudShadow(s.shadowPositionWS);
    lit.shadow = lit.tiers * (1 - lit.cloud);
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

/// For URP-PBR surfaces, after UniversalFragmentPBR (which knows nothing of the wide or cloud shadow): sets the share of
/// the light that came straight from the sun to what the shadow hand-off and the cloud shadow keep. URP lit it with its
/// own faded real shadow (lit.realShade), so the hand-off band is darkened back to one full shadow, not left washed out.
/// bakedGI is the ambient the PBR call was given.
half3 PunchySurfaceSunDim(half3 color, PunchySurface s, PunchyLit lit, half3 bakedGI)
{
    half direct01 = 1 - lit.realShade;
    if (lit.cloud > 0 || lit.tiers != direct01)
    {
        half sunlit = Luminance(lit.sun.color) * saturate(dot(s.normalWS, lit.sun.direction));
        half direct = sunlit * direct01;
        half kept = sunlit * lit.tiers * (1 - lit.cloud);
        half ambient = Luminance(bakedGI);
        color *= 1 - (direct - kept) / max(direct + ambient, 1e-3);
    }
    return color;
}

/// The last step for every surface: the cave haze, then the atmosphere.
half3 PunchySurfaceFinish(half3 color, PunchySurface s)
{
    color += _PunchyCaveAir.rgb * s.caveAir;
    return PunchyAtmosphere(color, s.positionWS);
}

#endif
