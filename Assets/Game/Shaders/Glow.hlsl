#ifndef PUNCHY_GLOW_INCLUDED
#define PUNCHY_GLOW_INCLUDED

// Light from glowing things (molten pools, glowing plants on the surface and in caves, lit windows), with no real
// lights, in two tiers (W-101):
// - Near: the 16 that matter most on screen, picked and uploaded by LightBudget (refactor 3.1; it was the nearest 16). Each falls off over its reach (squared), brightest on faces turned
//   to it. Out of reach, a light costs only its distance test. Surface ones fade out by their distance from the camera.
// - Far: every surface glow source within about 320 m, baked by SurfaceGlow into a camera-centred light map (the light
//   each casts on the ground, softened; rgb, a the light-weighted height of its sources) with lit windows in a second
//   map for night. One or two texture reads a pixel, however many lights; it fades in as the near tier fades out, so
//   towns and glowing woods glow from afar and sharpen into single lights as you come close.
float4 _PunchyCaveHeat[16];        // xyz position
float4 _PunchyCaveHeatColor[16];   // rgb light (linear), w reach (m)
int _PunchyCaveHeatCount;

TEXTURE2D(_PunchyGlowMap);         // rgb light of plants (linear), a sum of source height x light
SAMPLER(sampler_PunchyGlowMap);
TEXTURE2D(_PunchyGlowMapNight);    // rgb light of lit windows, a as above
float4 _PunchyGlowMapRect;         // xy the map's corner (m), z 1 / its width (1/m)
float4 _PunchyGlowMapParams;       // x strength (0: off), y lit windows on, zw camera distance the far tier fades in over (m)

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
        float4 a = SAMPLE_TEXTURE2D_LOD(_PunchyGlowMap, sampler_PunchyGlowMap, uv, 0);
        float4 b = SAMPLE_TEXTURE2D_LOD(_PunchyGlowMapNight, sampler_PunchyGlowMap, uv, 0) * _PunchyGlowMapParams.y;
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
    return light;
}

#endif
