#ifndef PUNCHY_WATER_SWELL_INCLUDED
#define PUNCHY_WATER_SWELL_INCLUDED

// Sea swell (prototype, roadmap 3.4, branch water/swells): the sea's surface really rises and falls, in long swells
// rolling in from fixed directions (swell comes from distant storms, so it stays put when the local wind veers, and is
// the same for every player); the wind at the camera sets their height. Gerstner waves: each point also moves a little
// along the wave, which bunches and sharpens the crests. The wave table and the swell's shape come from WaterSwells.cs
// as globals (refactor 3.4: one table, so the drawn sea and the swimmer can't drift apart); only the maths is here, and
// it must stay the same as WaterSwells.Height.

// x 1 on / 0 off (-noswells), y the sea level, z the wind 0..1 (WaterSwells.cs).
float4 _PunchySwell;
// x the height (m) in a calm, y what a full wind adds, z how far a point moves along a wave (fraction of its height),
// w how many waves there are.
float4 _PunchySwellShape;
// The room for waves: WaterSwells.MaxWaves (the array's size only; unused entries have no height).
#define PunchySwellMaxWaves 12
// Each wave: wavelength (m), direction (radians), relative height, phase.
float4 _PunchySwellWaves[PunchySwellMaxWaves];

// How strongly a water vertex swells: only the sea (its surface at sea level), fading out as it shallows to the shore.
float PunchySwellAmount(float surfaceY, float depth)
{
    return _PunchySwell.x * step(abs(surfaceY - _PunchySwell.y), 0.05) * smoothstep(0.1, 2.5, depth);
}

// The swell's height (m) at full strength: calm water still breathes, a storm heaves.
float PunchySwellHeight()
{
    return _PunchySwellShape.x + _PunchySwellShape.y * saturate(_PunchySwell.z);
}

// Offset of the surface point at xz (x, y, z), its slope (dy/dx, dy/dz), and how near a crest it is (-1 trough, 1 crest).
float3 PunchySwell(float2 xz, float t, float amount, out float2 slope, out float crest)
{
    float height = PunchySwellHeight() * amount;
    float3 offset = 0;
    slope = 0;
    float total = 0;
    [loop]
    for (int i = 0; i < (int)_PunchySwellShape.w; i++)
    {
        float4 w = _PunchySwellWaves[i];
        float k = 6.2831853 / w.x;
        float2 dir = float2(cos(w.y), sin(w.y));
        float theta = k * dot(dir, xz) - sqrt(9.81 * k) * t + w.w;
        float a = height * w.z;
        float s, c;
        sincos(theta, s, c);
        offset.y += a * s;
        offset.xz += _PunchySwellShape.z * a * dir * c;
        slope += a * k * dir * c;
        total += w.z;
    }
    crest = height > 1e-4 ? offset.y / (height * total) : 0;
    return offset;
}

#endif
