#ifndef PUNCHY_GLOW_INCLUDED
#define PUNCHY_GLOW_INCLUDED

// Light from glowing things near the camera (molten pools, glowing plants on the surface and in caves): the nearest 16,
// uploaded by CaveSystem, with no real lights. Each falls off over its reach (squared), brightest on faces turned to it.
// Out of reach, a light costs only its distance test.
float4 _PunchyCaveHeat[16];        // xyz position
float4 _PunchyCaveHeatColor[16];   // rgb light (linear), w reach (m)
int _PunchyCaveHeatCount;

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
    return light;
}

#endif
