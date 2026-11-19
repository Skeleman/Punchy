#ifndef PUNCHY_SEEP_STREAKS_INCLUDED
#define PUNCHY_SEEP_STREAKS_INCLUDED

// W-142 row 3: the nearest water seeps (LandmarkEffectSystem.WaterSeeps.cs). The rock under each trickle is dark and wet
// down to a damp patch round its foot, with moss along its lower half (TerrainSurface.shader, Cave.shader). Needs
// nothing else included first.

// Each seep's lip (xyz) and half width (w); which way it runs (xy, its length the seep's fade), its drop (z) and how far
// out its foot is from under the lip (w). The rectangle round them all (x min, z min, x max, z max).
#define PUNCHY_SEEP_STREAKS 8
float4 _PunchySeepLip[PUNCHY_SEEP_STREAKS];
float4 _PunchySeepRun[PUNCHY_SEEP_STREAKS];
float _PunchySeepCount;
float4 _PunchySeepRect;

half PunchySeepWet(float3 p, out half moss)
{
    moss = 0;
    // The moss clumps (about a metre across) blend to their mean as they shrink from 4 to 2 pixels (rule 20).
    float clumpSharp = saturate((1.2 / max(length(fwidth(p)), 1e-4) - 2) / 2);
    if (_PunchySeepCount < 0.5 || any(p.xz < _PunchySeepRect.xy) || any(p.xz > _PunchySeepRect.zw))
        return 0;
    half wet = 0;
    [loop] for (int k = 0; k < (int)_PunchySeepCount; k++)
    {
        float4 a = _PunchySeepLip[k];
        float4 b = _PunchySeepRun[k];
        float fade = length(b.xy);
        float2 dir = b.xy / max(fade, 1e-4);
        float t = (a.y - p.y) / b.z;
        float2 d = p.xz - a.xz - dir * saturate(t) * b.w;
        float along = dot(d, dir);
        float across = abs(dot(d, float2(-dir.y, dir.x)));
        // (Two waves down the face move the streak's edges, so it never reads as a painted stripe.)
        float waver = sin(p.y * 2.7 + k * 1.9) * 0.3 + sin(p.y * 6.1 - k * 0.7 + p.x) * 0.15;
        float width = a.w * (1 + 0.9 * saturate(t) + waver);
        float onFace = (1 - smoothstep(0.3, 0.7, abs(along))) * smoothstep(-0.2, 0.05, t) * (1 - smoothstep(1, 1 + 0.5 / b.z, t));
        float streak = (1 - smoothstep(width * 0.6, width, across)) * onFace;
        float level = 1 - smoothstep(0.3, 0.8, abs(p.y - a.y + b.z));
        float patch = (1 - smoothstep(width * 1.2, width * 2.6 + waver * a.w, length(d))) * level;
        float clump = lerp(0.6, smoothstep(0.1, 0.6, sin(p.x * 5.3 + p.y * 4.1 + sin(p.z * 3.1) * 1.5) * sin(p.z * 4.7 - p.y * 3.3 + sin(p.x * 2.9) * 1.5) + 0.35), clumpSharp);
        float mossBand = smoothstep(0.35, 0.75, t) * onFace * (1 - smoothstep(width * 1.4, width * 2.2, across)) * smoothstep(width * 0.5, width * 0.9, across);
        float mossRim = (1 - smoothstep(width * 2.2, width * 3.4, length(d))) * smoothstep(width * 1.6, width * 2.4, length(d)) * level;
        wet = max(wet, max(streak, patch) * fade);
        moss = max(moss, max(mossBand, mossRim) * clump * fade);
    }
    return wet;
}

#endif
