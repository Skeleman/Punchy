#ifndef PUNCHY_WATER_SWELL_INCLUDED
#define PUNCHY_WATER_SWELL_INCLUDED

// Sea swell (prototype, roadmap 3.4, branch water/swells): the sea's surface really rises and falls, in long swells
// rolling in from fixed directions (swell comes from distant storms, so it stays put when the local wind veers, and is
// the same for every player); the wind at the camera sets their height. Gerstner waves: each point also moves a little
// along the wave, which bunches and sharpens the crests. Must match WaterSwells.cs (the swimmer and the camera's
// underwater check use the same heights).

// x 1 on / 0 off (-noswells), y the sea level, z the wind 0..1 (WaterSwells.cs).
float4 _PunchySwell;

static const int PunchySwellWaves = 4;
// Wavelength (m), direction (radians), relative height, phase.
static const float4 PunchySwellTable[PunchySwellWaves] =
{
    float4(38, 0.60, 1.00, 0.0),
    float4(23, 1.05, 0.55, 1.7),
    float4(14, 0.25, 0.32, 4.1),
    float4(9,  1.40, 0.18, 2.6)
};

// How strongly a water vertex swells: only the sea (its surface at sea level), fading out as it shallows to the shore.
float PunchySwellAmount(float surfaceY, float depth)
{
    return _PunchySwell.x * step(abs(surfaceY - _PunchySwell.y), 0.05) * smoothstep(0.1, 2.5, depth);
}

// The swell's height (m) at full strength: calm water still breathes, a storm heaves.
float PunchySwellHeight()
{
    return 0.18 + 0.9 * saturate(_PunchySwell.z);
}

// Offset of the surface point at xz (x, y, z), its slope (dy/dx, dy/dz), and how near a crest it is (-1 trough, 1 crest).
float3 PunchySwell(float2 xz, float t, float amount, out float2 slope, out float crest)
{
    float height = PunchySwellHeight() * amount;
    float3 offset = 0;
    slope = 0;
    float total = 0;
    [unroll]
    for (int i = 0; i < PunchySwellWaves; i++)
    {
        float4 w = PunchySwellTable[i];
        float k = 6.2831853 / w.x;
        float2 dir = float2(cos(w.y), sin(w.y));
        float theta = k * dot(dir, xz) - sqrt(9.81 * k) * t + w.w;
        float a = height * w.z;
        float s, c;
        sincos(theta, s, c);
        offset.y += a * s;
        offset.xz += 0.6 * a * dir * c;
        slope += a * k * dir * c;
        total += w.z;
    }
    crest = height > 1e-4 ? offset.y / (height * total) : 0;
    return offset;
}

#endif
