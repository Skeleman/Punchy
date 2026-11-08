#ifndef PUNCHY_WATER_SWELL_INCLUDED
#define PUNCHY_WATER_SWELL_INCLUDED

// Sea swell (roadmap 3.4): the sea's surface really rises and falls, in swells running towards the nearest shore (the
// sea breeze pushes them in, playtest 11-08 item 2), the same for every player (seed and shared clock); the wind at the
// camera sets their height. Gerstner waves: each point also moves a little along the wave, which bunches and sharpens
// the crests. Two sets of waves turned to the nearest two of eight headings round a point's onshore heading are
// cross-faded, with slow wave groups raising and lowering them (item 1: no repeating pattern), and they grow and steepen
// over the shelf (item 3). The wave table, the shape and the shore field come from WaterSwells.cs as globals (one
// source, so the drawn sea and the swimmer can't drift apart); only the maths is here, and it must stay the same as
// WaterSwells.Height.

// x 1 on / 0 off (-noswells), y the sea level, z the wind 0..1 (WaterSwells.cs).
float4 _PunchySwell;
// x the height (m) in a calm, y what a full wind adds, z how far a point moves along a wave (fraction of its height),
// w how many waves there are in a set.
float4 _PunchySwellShape;
// x headings the sets turn to, y how much the wave groups raise and lower the height, z, w how much higher and steeper
// the waves get fully shoaled.
float4 _PunchySwellMix;
// x, y the swell fades in between these depths (m); z, w it shoals fully at z, not at all from w.
float4 _PunchySwellDepths;
// The two wave groups: heading x z, wavenumber, angular speed.
float4 _PunchySwellGroups[2];
// The room for waves: WaterSwells.MaxWaves (the array's size only; unused entries have no height).
#define PunchySwellMaxWaves 12
// Each wave: wavelength (m), heading from its set's onshore heading (radians), relative height, phase.
float4 _PunchySwellWaves[PunchySwellMaxWaves];
// The shore field: onshore headings (rg) per cell, and (x, z of its corner, cells per metre, cells across; 0 until built).
TEXTURE2D(_PunchySwellShore);
float4 _PunchySwellShoreAt;

// The water's quality policy (refactor 3.4, WaterQuality.cs): tiers by how big a metre of water looks on screen.
// x reference pixels a metre spans 1 m from the camera (0 until set: everything at full), yzw the near, mid and far tier
// boundaries (pixels a metre spans).
float4 _PunchyWaterTiers;
// The reflection march: xyz its steps in the near, mid and far tiers (none past far, none at all on Low), w the
// refinement steps once it hits.
float4 _PunchyWaterSsr;
// xyz how much each step grows in those tiers (so every tier reaches as far), w the first step (m).
float4 _PunchyWaterSsrGrowth;
// x the least a swell wave's length may look (reference pixels) to be summed, y the pixels a metre spans where the fine
// detail has faded out.
float4 _PunchyWaterDetailAt;

// How many reference pixels a metre of water spans this far from the camera.
float PunchyWaterPixelsPerMetre(float distance)
{
    return _PunchyWaterTiers.x > 0 ? _PunchyWaterTiers.x / max(distance, 0.01) : 1e6;
}

// How many swell waves to sum where a metre spans this many pixels: each wave while its length looks at least
// _PunchyWaterDetailAt.x pixels, fading in over half as many again (the table runs longest first).
float PunchySwellWaveCount(float pixelsPerMetre)
{
    float least = max(_PunchyWaterDetailAt.x, 1e-3);
    float count = 0;
    [loop] for (int i = 0; i < (int)_PunchySwellShape.w; i++)
        count += saturate((_PunchySwellWaves[i].x * pixelsPerMetre - least) / (0.5 * least));
    return count;
}

// How strongly a water vertex swells: only the sea (its surface at sea level), fading out as it shallows to the shore.
float PunchySwellAmount(float surfaceY, float depth)
{
    return _PunchySwell.x * step(abs(surfaceY - _PunchySwell.y), 0.05) * smoothstep(_PunchySwellDepths.x, _PunchySwellDepths.y, depth);
}

// The swell's height (m) at full strength: calm water still breathes, a storm heaves.
float PunchySwellHeight()
{
    return _PunchySwellShape.x + _PunchySwellShape.y * saturate(_PunchySwell.z);
}

// The way the swell runs at xz (not normalised): towards the nearest shore, from the shore field (bilinear by hand, so
// it is exactly WaterSwells.Onshore), or towards the world's middle until that is built.
float2 PunchySwellOnshore(float2 xz)
{
    float4 at = _PunchySwellShoreAt;
    if (at.w < 1)
        return -xz / max(length(xz), 1);
    float2 u = (xz - at.xy) * at.z - 0.5;
    float2 i0 = floor(u);
    float2 f = u - i0;
    int n = (int)at.w - 1;
    int2 a = clamp((int2)i0, 0, n);
    int2 b = clamp((int2)i0 + 1, 0, n);
    float2 h00 = LOAD_TEXTURE2D_LOD(_PunchySwellShore, a, 0).rg;
    float2 h10 = LOAD_TEXTURE2D_LOD(_PunchySwellShore, int2(b.x, a.y), 0).rg;
    float2 h01 = LOAD_TEXTURE2D_LOD(_PunchySwellShore, int2(a.x, b.y), 0).rg;
    float2 h11 = LOAD_TEXTURE2D_LOD(_PunchySwellShore, b, 0).rg;
    return lerp(lerp(h00, h10, f.x), lerp(h01, h11, f.x), f.y);
}

struct PunchySwellResult
{
    float3 offset;   // how far the surface point moves (m)
    float2 slope;    // dy/dx, dy/dz
    float crest;     // -1 in a trough, 1 on a crest (of the waves summed)
    float squeeze;   // how hard the waves bunch the surface together here (0 none; whitecaps and breakers past ~0.15)
    float2 phasor;   // (sum of a sin, sum of a cos) of the waves summed: atan2 of it is where in its cycle the swell is
};

// The swell at xz: amount from PunchySwellAmount, depth the water's (for shoaling), waves how many waves of each set to
// sum, longest first (a fraction fades the last one in: the detail policy's tier, by how big each looks on screen).
PunchySwellResult PunchySwell(float2 xz, float t, float amount, float depth, float waves)
{
    PunchySwellResult r = (PunchySwellResult)0;
    float shoal = 1 - smoothstep(_PunchySwellDepths.z, _PunchySwellDepths.w, depth);
    float group = 0.5;
    [unroll] for (int g = 0; g < 2; g++)
        group += 0.25 * sin(dot(_PunchySwellGroups[g].xy, xz) * _PunchySwellGroups[g].z - _PunchySwellGroups[g].w * t);
    float height = PunchySwellHeight() * amount * (1 + _PunchySwellMix.z * shoal) * (1 + _PunchySwellMix.y * (2 * group - 1));
    float q = _PunchySwellShape.z * (1 + _PunchySwellMix.w * shoal);
    float2 on = PunchySwellOnshore(xz);
    float sectors = _PunchySwellMix.x;
    float f = atan2(on.y, on.x) * sectors / 6.2831853;
    float s0 = floor(f);
    float blend = smoothstep(0, 1, f - s0);
    float norm = rsqrt(blend * blend + (1 - blend) * (1 - blend));
    int count = min((int)ceil(waves), (int)_PunchySwellShape.w);
    float most = 0;
    [unroll] for (int set = 0; set < 2; set++)
    {
        float weight = (set == 0 ? 1 - blend : blend) * norm;
        [branch] if (weight >= 1e-3)
        {
            // (Wrapped to 0..sectors-1, so the phases match on either side of due west.)
            float sector = s0 + set;
            sector -= sectors * floor(sector / sectors);
            float heading = sector * 6.2831853 / sectors;
            [loop] for (int i = 0; i < count; i++)
            {
                float4 w = _PunchySwellWaves[i];
                float k = 6.2831853 / w.x;
                float2 dir = float2(cos(heading + w.y), sin(heading + w.y));
                float theta = k * dot(dir, xz) - sqrt(9.81 * k) * t + w.w + sector * 2.39996 * (i + 1);
                float a = height * w.z * weight * saturate(waves - i);
                float s, c;
                sincos(theta, s, c);
                r.offset.y += a * s;
                r.offset.xz += q * a * dir * c;
                r.slope += a * k * dir * c;
                r.squeeze += q * a * k * s;
                r.phasor += a * float2(s, c);
                most += a;
            }
        }
    }
    r.crest = most > 1e-4 ? r.offset.y / most : 0;
    return r;
}

#endif
