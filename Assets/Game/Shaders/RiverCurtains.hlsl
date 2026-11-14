#ifndef PUNCHY_RIVER_CURTAINS_INCLUDED
#define PUNCHY_RIVER_CURTAINS_INCLUDED

// R-21: the open falls' boxes, one per fall, from its description (FallShape.cs, LandmarkEffectSystem.cs). The water
// leaves a river fall's face out (PunchyWaterUnderCurtain, Water.shader), dips and breaks up above every lip and
// heaves and ripples round every foot (PunchyFallWater, the water's vertex and fragment stages), and the ground
// beside them is dark with spray (PunchyFallWet, TerrainSurface.shader). World-owned, like WaterField.hlsl; needs
// nothing else included first.

// Each fall's lip (xyz, top middle) and half width (w, plus a metre); which way it pours (xy), how far out it reaches
// (z, plus 1.5 m) and its pool's level (w); its lip line's skew and bow (xy, m), how far behind the lip the water is
// left out under it (z; 0: not at all) and its seed (w). The rectangle round them all (x min, z min, x max, z max)
// lets anything far from any skip the test.
#define PUNCHY_RIVER_CURTAINS 32
float4 _PunchyRiverCurtainLip[PUNCHY_RIVER_CURTAINS];
float4 _PunchyRiverCurtainPour[PUNCHY_RIVER_CURTAINS];
float4 _PunchyRiverCurtainFoot[PUNCHY_RIVER_CURTAINS];
float _PunchyRiverCurtainCount;
float4 _PunchyRiverCurtainRect;

// How far round a curtain's box the spray wets the rock (m), past its sides and below its lip.
#define PUNCHY_FALL_WET_REACH 3.0
// How far from a fall's foot its ripples reach over the pool (m).
#define PUNCHY_FALL_RIPPLE_REACH 12.0

// How far out the lip line is here, against its middle (m): an oblique lip is further out on one side (skew), a
// bowed one in its middle (the same line the sheet is built on, LandmarkEffects.hlsl). acrossSigned is along
// float2(pour.y, -pour.x).
float PunchyFallLipShift(float4 foot, float acrossSigned, float halfWidth)
{
    float side = clamp(acrossSigned / max(halfWidth, 0.5), -1, 1);
    return foot.x * side * 0.5 - foot.y * side * side;
}

// Whether a point of the water is on a river fall's face, below its lip and above its pool, inside the fall's box.
bool PunchyWaterUnderCurtain(float3 p)
{
    if (_PunchyRiverCurtainCount < 0.5 || any(p.xz < _PunchyRiverCurtainRect.xy) || any(p.xz > _PunchyRiverCurtainRect.zw))
        return false;
    [loop] for (int k = 0; k < (int)_PunchyRiverCurtainCount; k++)
    {
        float4 a = _PunchyRiverCurtainLip[k];
        float4 b = _PunchyRiverCurtainPour[k];
        float4 c = _PunchyRiverCurtainFoot[k];
        if (c.z <= 0)
            continue;
        float2 d = p.xz - a.xz;
        float across = dot(d, float2(b.y, -b.x));
        float along = dot(d, b.xy) - PunchyFallLipShift(c, across, a.w - 1);
        // (Well under the lip's level: the water above the lip dips by up to a quarter of a metre, PunchyFallWater.)
        if (along > -c.z && along < b.z && abs(across) < a.w && p.y < a.y - 0.45 && p.y > b.w + 0.15)
            return true;
    }
    return false;
}

// R-21 row 4: how the water moves round the falls. Above a lip it speeds up: the surface dips towards the edge and
// breaks into quick, short waves in its last metres. At a foot it heaves into a low mound under the sheet and rings
// spread from the impact line, fading with distance. Gives the height to add to a point of the water (m), with the
// slope that adds (dh/dx, dh/dz) and how churned (white) the water is there (0 to 1). p is the water's point at
// rest; fine (0 to 1) is how much of the short waves to give: the vertex stages pass what their mesh can carry,
// the fragment stage what a pixel can show.
float PunchyFallWater(float3 p, float t, float fine, out float churn, out float2 slope)
{
    churn = 0;
    slope = 0;
    float grow = PUNCHY_FALL_RIPPLE_REACH;
    if (_PunchyRiverCurtainCount < 0.5 || any(p.xz < _PunchyRiverCurtainRect.xy - grow) || any(p.xz > _PunchyRiverCurtainRect.zw + grow))
        return 0;
    float height = 0;
    [loop] for (int k = 0; k < (int)_PunchyRiverCurtainCount; k++)
    {
        float4 a = _PunchyRiverCurtainLip[k];
        float4 b = _PunchyRiverCurtainPour[k];
        float4 c = _PunchyRiverCurtainFoot[k];
        float2 d = p.xz - a.xz;
        float halfW = a.w - 1;
        float run = b.z - 1.5;
        float2 o = b.xy;
        float2 side = float2(b.y, -b.x);
        float ac = dot(d, side);
        float al = dot(d, o) - PunchyFallLipShift(c, ac, halfW);
        if (al < -6 || al > run + PUNCHY_FALL_RIPPLE_REACH || abs(ac) > halfW + PUNCHY_FALL_RIPPLE_REACH)
            continue;

        // Above the lip: the last six metres of the water at the lip's level, as wide as the sheet.
        float nearLip = 1 - smoothstep(0.6, 1.4, abs(p.y - a.y));
        if (nearLip > 0 && al < 0.6 && abs(ac) < halfW + 1)
        {
            float w = nearLip * (1 - smoothstep(halfW - 0.8, halfW + 0.6, abs(ac))) * (1 - smoothstep(0.2, 0.6, al));
            float x = saturate((al + 6) / 6);
            float approach = x * x * (3 - 2 * x);
            float phase = al * 2.6 - t * 6 + c.w * 40;
            float turb = 0.05 * approach * fine * w;
            height += w * -0.18 * approach * approach
                    + turb * (0.6 * sin(phase + ac * 1.7) + 0.4 * sin(phase * 1.9 - ac * 3.1 + 1.3));
            float dAlong = w * -0.36 * approach * x * (1 - x)
                         + turb * 2.6 * (0.6 * cos(phase + ac * 1.7) + 0.76 * cos(phase * 1.9 - ac * 3.1 + 1.3));
            float dAcross = turb * (1.02 * cos(phase + ac * 1.7) - 1.24 * cos(phase * 1.9 - ac * 3.1 + 1.3));
            slope += o * dAlong + side * dAcross;
            churn = max(churn, 0.5 * w * approach * approach);
        }

        // At the foot: the pool's water round the line the sheet lands on.
        float nearPool = 1 - smoothstep(0.8, 1.6, abs(p.y - b.w));
        if (nearPool > 0)
        {
            float alF = al - run;
            float beside = max(abs(ac) - halfW, 0);
            float r = length(float2(alF, beside));
            if (r < PUNCHY_FALL_RIPPLE_REACH)
            {
                float amp = clamp(0.06 + 0.012 * (a.y - b.w), 0.08, 0.3) * nearPool;
                float mound = amp * 0.6 * exp(-r * r / 2.25);
                float rings = amp * exp(-r * 0.25) * (1 - smoothstep(PUNCHY_FALL_RIPPLE_REACH - 3, PUNCHY_FALL_RIPPLE_REACH, r)) * fine;
                float p1 = 2.2 * r - 5 * t + c.w * 40;
                float p2 = 4.7 * r - 8.3 * t + 0.8 * ac;
                height += mound + rings * (0.5 * sin(p1) + 0.3 * sin(p2));
                float dr = mound * (-2 * r / 2.25) + rings * (1.1 * cos(p1) + 1.41 * cos(p2));
                // (The way r grows; nan-safe: no direction at the line itself.)
                float2 out2 = float2(alF, beside * (ac < 0 ? -1.0 : 1.0)) / max(r, 0.05);
                slope += (o * out2.x + side * out2.y) * dr;
                churn = max(churn, nearPool * (1 - smoothstep(0.5, 4.5, r)));
            }
        }
    }
    return height;
}

// How wet the ground here is from a fall's spray (0 to 1): a band a few metres either side of the sheet, from
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
