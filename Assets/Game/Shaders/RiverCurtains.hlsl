#ifndef PUNCHY_RIVER_CURTAINS_INCLUDED
#define PUNCHY_RIVER_CURTAINS_INCLUDED

// R-21: the river falls drawn as curtains (LandmarkEffectSystem.cs). The water leaves their faces out
// (PunchyWaterUnderCurtain, Water.shader) and the ground beside them is dark with spray (PunchyFallWet,
// TerrainSurface.shader). World-owned, like WaterField.hlsl; needs nothing else included first.

// Each curtain's lip (xyz, top middle) and half width (w); which way it pours (xy), how far out it reaches (z) and its
// pool's level (w). The rectangle round them all (x min, z min, x max, z max) lets anything far from any skip the test.
#define PUNCHY_RIVER_CURTAINS 32
float4 _PunchyRiverCurtainLip[PUNCHY_RIVER_CURTAINS];
float4 _PunchyRiverCurtainPour[PUNCHY_RIVER_CURTAINS];
float _PunchyRiverCurtainCount;
float4 _PunchyRiverCurtainRect;

// How far round a curtain's box the spray wets the rock (m), past its sides and below its lip.
#define PUNCHY_FALL_WET_REACH 3.0

// Whether a point of the water is on a river fall's face, below its lip and above its pool, inside a curtain's box.
bool PunchyWaterUnderCurtain(float3 p)
{
    if (_PunchyRiverCurtainCount < 0.5 || any(p.xz < _PunchyRiverCurtainRect.xy) || any(p.xz > _PunchyRiverCurtainRect.zw))
        return false;
    [loop] for (int k = 0; k < (int)_PunchyRiverCurtainCount; k++)
    {
        float4 a = _PunchyRiverCurtainLip[k];
        float4 b = _PunchyRiverCurtainPour[k];
        float2 d = p.xz - a.xz;
        float along = dot(d, b.xy);
        float across = abs(dot(d, float2(-b.y, b.x)));
        if (along > -1.5 && along < b.z && across < a.w && p.y < a.y - 0.15 && p.y > b.w + 0.15)
            return true;
    }
    return false;
}

// How wet the ground here is from a river fall's spray (0 to 1): a band a few metres either side of the sheet, from
// just above its lip down to its pool, its edge wavering so it never reads as a painted rectangle.
half PunchyFallWet(float3 p)
{
    float grow = PUNCHY_FALL_WET_REACH + 1;
    if (_PunchyRiverCurtainCount < 0.5 || any(p.xz < _PunchyRiverCurtainRect.xy - grow) || any(p.xz > _PunchyRiverCurtainRect.zw + grow))
        return 0;
    half wet = 0;
    [loop] for (int k = 0; k < (int)_PunchyRiverCurtainCount; k++)
    {
        float4 a = _PunchyRiverCurtainLip[k];
        float4 b = _PunchyRiverCurtainPour[k];
        float2 d = p.xz - a.xz;
        float along = dot(d, b.xy);
        float across = abs(dot(d, float2(-b.y, b.x)));
        // (Two waves along the fall and one down it move the band's edge by up to about a metre.)
        float waver = sin(along * 1.7 + p.y * 0.9 + k * 2.3) * 0.5 + sin(along * 4.1 - p.y * 2.3 + k) * 0.3;
        float side = 1 - smoothstep(0, 1.5, across - a.w - PUNCHY_FALL_WET_REACH + 1.5 + waver);
        float run = smoothstep(-PUNCHY_FALL_WET_REACH - 1, -PUNCHY_FALL_WET_REACH * 0.5, along)
                  * (1 - smoothstep(b.z, b.z + PUNCHY_FALL_WET_REACH, along));
        float height = (1 - smoothstep(a.y + 0.2, a.y + 1.2 + waver * 0.5, p.y)) * smoothstep(b.w - 1.5, b.w - 0.5, p.y);
        wet = max(wet, side * run * height);
    }
    return wet;
}

#endif
