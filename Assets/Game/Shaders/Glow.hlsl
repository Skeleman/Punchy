#ifndef PUNCHY_GLOW_INCLUDED
#define PUNCHY_GLOW_INCLUDED

// Light from glowing things (molten pools, glowing plants on the surface and in caves, lit windows), with no real
// lights, in two tiers (W-101):
// - Near: the 16 that matter most on screen, picked and uploaded by LightBudget (refactor 3.1; it was the nearest 16).
//   Each falls off over its reach (squared), brightest on faces turned to it. Out of reach, a light costs only its
//   distance test. Each fades out as it shrinks on screen.
// - Far: every surface glow source within about 320 m, baked by SurfaceGlow into a camera-centred light map (the light
//   each casts on the ground, softened; rgb, a the light-weighted height of its sources) with lit windows in a second
//   map for night. One or two texture reads a pixel, however many lights; it fades in as the near tier fades out, so
//   towns and glowing woods glow from afar and sharpen into single lights as you come close.
// Also the blue moon's own light (feedback 11-08 item 10): a second, unshadowed moonlight from the blue moon's
// direction, added on top of the big moon's (the main light), set by WeatherSystem (black when it's down, new or by day).
float4 _PunchyCaveHeat[16];        // xyz position
float4 _PunchyCaveHeatColor[16];   // rgb light (linear), w reach (m)
int _PunchyCaveHeatCount;

TEXTURE2D(_PunchyGlowMap);         // rgb light of plants (linear), a sum of source height x light
SAMPLER(sampler_PunchyGlowMap);
TEXTURE2D(_PunchyGlowMapNight);    // rgb light of lit windows, a as above
float4 _PunchyGlowMapRect;         // xy the map's corner (m), z 1 / its width (1/m)
float4 _PunchyGlowMapParams;       // x strength (0: off), y lit windows on, zw camera distance the far tier fades in over (m)
float4 _PunchyGlowMapNightFade;    // xy camera distance the night map (street lights) fades in over (m): where they stop being real lights (R-10)
TEXTURE2D(_PunchyGlowWorld);       // the whole island: r fire amount, gba plants' and lava's light (linear)
float4 _PunchyGlowWorldRect;       // xy its corner (m), z 1 / its width (1/m)
float4 _PunchyGlowWorldParams;     // x strength (0: off), y night (plants and lava), zw camera distance they fade in over (m)
float4 _PunchyGlowWorldFire;       // rgb tonight's fire light (0 by day or off), w camera distance it starts fading in at (full at twice it)
TEXTURE2D(_PunchyGlowTown);        // R-10 row 4: the island's towns, lit windows and street lights summed (same rect as above)
float4 _PunchyGlowTownParams;      // x strength x windows on (0 by day or off), y how hard dense towns level off, zw camera distance it fades in over (m)
float4 _PunchyGlowTownColor;       // rgb the warm light of a lit town
float4 _PunchyBlueLightDirection;  // xyz towards the blue moon
float4 _PunchyBlueLightColor;      // rgb its light (linear; 0 when it gives none)

// The far tier's light on a surface at p with normal n.
half3 PunchyGlowFar(float3 p, half3 n)
{
    float2 uv = (p.xz - _PunchyGlowMapRect.xy) * _PunchyGlowMapRect.z;
    float2 edge = min(uv, 1 - uv);
    float fade = saturate(min(edge.x, edge.y) * 8);
    float dist = distance(p, GetCameraPositionWS());
    // Plants and street lights hand over at different distances: a street light stays a real light much further out.
    float plantFade = smoothstep(_PunchyGlowMapParams.z, _PunchyGlowMapParams.w, dist);
    float nightFade = _PunchyGlowMapParams.y > 0 ? smoothstep(_PunchyGlowMapNightFade.x, _PunchyGlowMapNightFade.y, dist) : 0;
    half3 light = 0;
    [branch] if (fade > 0 && plantFade + nightFade > 0)
    {
        float4 a = SAMPLE_TEXTURE2D_LOD(_PunchyGlowMap, sampler_PunchyGlowMap, uv, 0);
        // The night layer: street lights only (W-127); lit windows' light is indoor (P-21 item 31).
        float4 b = 0;
        [branch] if (nightFade > 0)
            b = SAMPLE_TEXTURE2D_LOD(_PunchyGlowMapNight, sampler_PunchyGlowMap, uv, 0) * _PunchyGlowMapParams.y;
        float wa = a.r + a.g + a.b, wb = b.r + b.g + b.b;
        // Its sources' height, weighted by their light (interpolates cleanly where lit and dark texels meet): a cliff
        // top or a cave floor well away from it gets none.
        float height = (a.a + b.a) / max(wa + wb, 1e-4);
        float vertical = saturate(1 - abs(p.y - height) / 14);
        // A slow, soft shimmer drifting across a cluster.
        float shimmer = 1 + 0.07 * sin(_Time.y * 0.7 + p.x * 0.05) * sin(_Time.y * 0.53 + p.z * 0.043 + 1.7);
        // Soft caps. Plants: a dense cluster sums to far more than the 16 near lights ever show, so it levels off at
        // about one near light's brightness instead of floodlighting the floor. Street lights (R-10): one or two
        // overlapping are left as bright as their glow slots, so the hand-over doesn't dim them; only a dense square
        // levels off.
        float3 rgb = a.rgb * (plantFade / (1 + wa * 2.5)) + b.rgb * (nightFade / (1 + max(wb - 2.5, 0) * 0.6));
        light = rgb * (fade * vertical * shimmer * _PunchyGlowMapParams.x * saturate(0.6 + 0.4 * n.y));
    }
    return light;
}

// The whole island's static glow (R-2, SurfaceGlow.World), for terrain, plants and built things (R-10 row 4): r how much fire burns there (tinted with
// tonight's fire), gba the fixed light of plants and lava. One read a pixel, past where the nearer tiers end.
half3 PunchyGlowWorld(float3 p, half3 n)
{
    half3 light = 0;
    [branch] if (_PunchyGlowWorldParams.x > 0)
    {
        float2 uv = (p.xz - _PunchyGlowWorldRect.xy) * _PunchyGlowWorldRect.z;
        float d = distance(p, GetCameraPositionWS());
        float fireFade = smoothstep(_PunchyGlowWorldFire.w, _PunchyGlowWorldFire.w * 2, d);
        float plantFade = smoothstep(_PunchyGlowWorldParams.z, _PunchyGlowWorldParams.w, d);
        [branch] if (fireFade + plantFade > 0 && uv.x > 0 && uv.x < 1 && uv.y > 0 && uv.y < 1)
        {
            float4 t = SAMPLE_TEXTURE2D_LOD(_PunchyGlowWorld, sampler_PunchyGlowMap, uv, 0);
            // Soft cap: a dense forest sums high, and should level off rather than floodlight the ground.
            float3 rgb = _PunchyGlowWorldFire.rgb * (t.r / (1 + t.r * 2)) * fireFade
                       + t.gba / (1 + (t.g + t.b + t.a) * 2.5) * _PunchyGlowWorldParams.y * plantFade;
            light = rgb * (_PunchyGlowWorldParams.x * saturate(0.6 + 0.4 * n.y));
        }
    }
    // R-10 row 4: a town's windows and street lights, past where the far map ends, so a distant town still glows. Its
    // own map: it follows the windows (dark by day), and fades in where the far map's street lights fade out.
    [branch] if (_PunchyGlowTownParams.x > 0)
    {
        float2 uv = (p.xz - _PunchyGlowWorldRect.xy) * _PunchyGlowWorldRect.z;
        float townFade = smoothstep(_PunchyGlowTownParams.z, _PunchyGlowTownParams.w, distance(p, GetCameraPositionWS()));
        [branch] if (townFade > 0 && uv.x > 0 && uv.x < 1 && uv.y > 0 && uv.y < 1)
        {
            float t = SAMPLE_TEXTURE2D_LOD(_PunchyGlowTown, sampler_PunchyGlowMap, uv, 0).r;
            light += _PunchyGlowTownColor.rgb * (t / (1 + t * _PunchyGlowTownParams.y) * townFade * _PunchyGlowTownParams.x
                     * saturate(0.6 + 0.4 * n.y));
        }
    }
    return light;
}

// The glow light falling on a surface at p with normal n (add it to the diffuse light, times albedo).
half3 PunchyGlowLight(float3 p, half3 n)
{
    half3 light = 0;
    for (int k = 0; k < _PunchyCaveHeatCount; k++)
    {
        float4 hc = _PunchyCaveHeatColor[k];
        float3 to = _PunchyCaveHeat[k].xyz - p;
        float dist = length(to);
        [branch] if (dist < hc.w)
        {
            float fall = saturate(1 - dist / hc.w);
            light += hc.rgb * fall * fall * saturate(dot(n, to / max(dist, 1e-3)) * 0.7 + 0.3);
        }
    }
    [branch] if (_PunchyGlowMapParams.x > 0)
        light += PunchyGlowFar(p, n);
    // The blue moon: a little wrap, so faces turned just past it aren't cut off hard.
    [branch] if (_PunchyBlueLightColor.b > 0)
        light += _PunchyBlueLightColor.rgb * saturate(dot(n, _PunchyBlueLightDirection.xyz) * 0.85 + 0.15);
    return light;
}

// Real lights (R-10): the Unity lights LightBudget turns on (lanterns, room lamps, street lights), read through URP's
// Forward+ clusters, so a pixel pays only for the lights whose clusters it is in. Every shader lights them the same way,
// with the glow slots' fall-off: a real light of colour c and range r lights like a glow of c x PunchyRealToGlow and
// reach r x PunchyRealToGlowReach (LightBudget.RealToGlow, which sizes a pooled light from its glow the other way round),
// so a light handed between a real light and a glow slot looks the same in both. Without Forward+ (the cluster keyword
// off) there are none: the glow slots light everything, as before.
#if defined(USE_CLUSTER_LIGHT_LOOP) && defined(LIGHT_LOOP_BEGIN)
    #if USE_CLUSTER_LIGHT_LOOP
        #define PUNCHY_REAL_LIGHTS 1
    #endif
#endif
#ifndef PUNCHY_REAL_LIGHTS
    #define PUNCHY_REAL_LIGHTS 0
#endif
float _PunchyFloraGlowWorldOff;  // profiler pricing (-floraglowworld 0): plants skip PunchyGlowWorld
float _PunchyRealLightReach;   // how far from the camera (m) any real light reaches (LightBudget): past it, none
static const float PunchyRealToGlow = 0.28;
static const float PunchyRealToGlowReach = 0.75;

#if PUNCHY_REAL_LIGHTS
// Real light i (a Forward+ light index) at p: its light as a glow (falling off over its reach, before the facing term)
// and the direction to it.
half3 PunchyRealLight(uint i, float3 p, out half3 direction)
{
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 position = _AdditionalLightsBuffer[i].position;
    half3 color = _AdditionalLightsBuffer[i].color.rgb;
    half4 attenuation = _AdditionalLightsBuffer[i].attenuation;
    half4 spot = _AdditionalLightsBuffer[i].spotDirection;
#else
    float4 position = _AdditionalLightsPosition[i];
    half3 color = _AdditionalLightsColor[i].rgb;
    half4 attenuation = _AdditionalLightsAttenuation[i];
    half4 spot = _AdditionalLightsSpotDir[i];
#endif
    float3 to = position.xyz - p * position.w;
    float dist = max(length(to), 1e-3);
    direction = half3(to / dist);
    // URP keeps 1 / range^2 in attenuation.x.
    float reach = rsqrt(max(attenuation.x, 1e-6)) * PunchyRealToGlowReach;
    float fall = saturate(1 - dist / reach);
    return color * (PunchyRealToGlow * fall * fall * AngleAttenuation(spot.xyz, direction, attenuation.zw));
}
#endif

// All real lights' diffuse light on a surface at p with normal n, seen at screen position positionCS (SV_POSITION):
// add it next to PunchyGlowLight.
half3 PunchyRealLights(float3 p, half3 n, float4 positionCS)
{
    half3 light = 0;
#if PUNCHY_REAL_LIGHTS
    // (No real light reaches past this: skip the cluster lookup and loop. Uniform per frame, so whole far draws skip it.)
    [branch] if (distance(p, GetCameraPositionWS()) > _PunchyRealLightReach)
        return light;
    InputData inputData = (InputData)0;
    inputData.positionWS = p;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    LIGHT_LOOP_BEGIN(0)
        half3 direction;
        half3 lit = PunchyRealLight(lightIndex, p, direction);
        // Shadowed only in shaders with the additional-light shadow keyword (the few lights LightBudget lets cast them).
        light += lit * (saturate(dot(n, direction) * 0.7 + 0.3) * AdditionalLightRealtimeShadow(lightIndex, p, direction));
    LIGHT_LOOP_END
#endif
    return light;
}

#endif
