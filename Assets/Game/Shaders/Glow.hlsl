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
TEXTURE2D(_PunchyGlowWorld);       // the whole island: r fire amount, gba plants' and lava's light (linear)
float4 _PunchyGlowWorldRect;       // xy its corner (m), z 1 / its width (1/m)
float4 _PunchyGlowWorldParams;     // x strength (0: off), y night (plants and lava), zw camera distance they fade in over (m)
float4 _PunchyGlowWorldFire;       // rgb tonight's fire light (0 by day or off), w camera distance it starts fading in at (full at twice it)
float4 _PunchyBlueLightDirection;  // xyz towards the blue moon
float4 _PunchyBlueLightColor;      // rgb its light (linear; 0 when it gives none)
// R-32 row 5: the main light's moon while it hands the shadows over (WeatherSystem), shaded like the blue moon's
// term; rgb 0 outside the few seconds of a hand-over, so it costs one uniform branch.
float4 _PunchyHandOverLightDirection;
float4 _PunchyHandOverLightColor;

// R-38 row 3: a smooth (cubic B-spline) read of a coarse light map, from four bilinear reads. The glow maps' texels
// are many metres wide, and one bilinear read shows them as squares and diamonds across a lit town from afar.
// size is the map's width in texels, passed in (no GetDimensions, which broke some variants): keep it in step with
// SurfaceGlow.MapSize and SurfaceGlow.WorldMapSize.
#define PUNCHY_GLOW_MAP_SIZE 160.0
#define PUNCHY_GLOW_WORLD_SIZE 512.0
float4 PunchySampleSmooth(TEXTURE2D_PARAM(map, mapSampler), float2 uv, float size)
{
    float2 st = uv * size - 0.5;
    float2 i = floor(st);
    float2 f = st - i;
    float2 f2 = f * f, f3 = f2 * f;
    float2 w0 = (1 - 3 * f + 3 * f2 - f3) / 6, w1 = (4 - 6 * f2 + 3 * f3) / 6;
    float2 w2 = (1 + 3 * f + 3 * f2 - 3 * f3) / 6, w3 = f3 / 6;
    float2 g0 = w0 + w1, g1 = w2 + w3;
    // Each pair of weights as one bilinear read placed between its two texels.
    float2 h0 = (i - 0.5 + w1 / g0) / size, h1 = (i + 1.5 + w3 / g1) / size;
    return g0.y * (g0.x * SAMPLE_TEXTURE2D_LOD(map, mapSampler, float2(h0.x, h0.y), 0) + g1.x * SAMPLE_TEXTURE2D_LOD(map, mapSampler, float2(h1.x, h0.y), 0))
         + g1.y * (g0.x * SAMPLE_TEXTURE2D_LOD(map, mapSampler, float2(h0.x, h1.y), 0) + g1.x * SAMPLE_TEXTURE2D_LOD(map, mapSampler, float2(h1.x, h1.y), 0));
}

// The far tier's light on a surface at p with normal n.
half3 PunchyGlowFar(float3 p, half3 n)
{
    float2 uv = (p.xz - _PunchyGlowMapRect.xy) * _PunchyGlowMapRect.z;
    float2 edge = min(uv, 1 - uv);
    float fade = saturate(min(edge.x, edge.y) * 8)
               * smoothstep(_PunchyGlowMapParams.z, _PunchyGlowMapParams.w, distance(p, GetCameraPositionWS()));
    half3 light = 0;
    [branch] if (fade > 0)
    {
        float4 a = PunchySampleSmooth(TEXTURE2D_ARGS(_PunchyGlowMap, sampler_PunchyGlowMap), uv, PUNCHY_GLOW_MAP_SIZE);
        // (Lit windows' layer: off since their light is indoor, P-21 item 31; CaveSystem sends y = 0.)
        float4 b = 0;
        [branch] if (_PunchyGlowMapParams.y > 0)
            b = PunchySampleSmooth(TEXTURE2D_ARGS(_PunchyGlowMapNight, sampler_PunchyGlowMap), uv, PUNCHY_GLOW_MAP_SIZE) * _PunchyGlowMapParams.y;
        float3 rgb = a.rgb + b.rgb;
        float weight = rgb.r + rgb.g + rgb.b;
        // Its sources' height, weighted by their light (interpolates cleanly where lit and dark texels meet): a cliff
        // top or a cave floor well away from it gets none.
        float height = (a.a + b.a) / max(weight, 1e-4);
        float vertical = saturate(1 - abs(p.y - height) / 14);
        // A slow, soft shimmer drifting across a cluster.
        float shimmer = 1 + 0.07 * sin(_Time.y * 0.7 + p.x * 0.05) * sin(_Time.y * 0.53 + p.z * 0.043 + 1.7);
        // Soft cap: a dense cluster sums to far more than the 16 near lights ever show, so it levels off at about
        // one near light's brightness instead of floodlighting the floor.
        rgb /= 1 + weight * 2.5;
        light = rgb * (fade * vertical * shimmer * _PunchyGlowMapParams.x * saturate(0.6 + 0.4 * n.y));
    }
    return light;
}

// The whole island's static glow (R-2, SurfaceGlow.World), for terrain only: r how much fire burns there (tinted with
// tonight's fire), gba the fixed light of plants and lava. One smooth read a pixel, past where the nearer tiers end.
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
            float4 t = PunchySampleSmooth(TEXTURE2D_ARGS(_PunchyGlowWorld, sampler_PunchyGlowMap), uv, PUNCHY_GLOW_WORLD_SIZE);
            // Soft cap: a dense forest sums high, and should level off rather than floodlight the ground.
            float3 rgb = _PunchyGlowWorldFire.rgb * (t.r / (1 + t.r * 2)) * fireFade
                       + t.gba / (1 + (t.g + t.b + t.a) * 2.5) * _PunchyGlowWorldParams.y * plantFade;
            light = rgb * (_PunchyGlowWorldParams.x * saturate(0.6 + 0.4 * n.y));
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
    [branch] if (_PunchyHandOverLightColor.r + _PunchyHandOverLightColor.g + _PunchyHandOverLightColor.b > 0)
        light += _PunchyHandOverLightColor.rgb * saturate(dot(n, _PunchyHandOverLightDirection.xyz) * 0.85 + 0.15);
    return light;
}

#endif
