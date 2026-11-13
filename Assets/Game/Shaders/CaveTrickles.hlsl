#ifndef PUNCHY_CAVE_TRICKLES_INCLUDED
#define PUNCHY_CAVE_TRICKLES_INCLUDED

// W-141: what a wall trickle does to the rock (the water itself is the Cave Trickle shader): the slit it comes from, a
// narrow dark crack with a pale lip; the darker, glossier wet streak running down below it (short under an overhang,
// where the water drips off instead), widening as a fan spreads; and a puddle at the foot. Painted into the rock's own
// colour, so they hold at any distance. CaveSystem uploads the nearest 16 (a slit, out of the wall and a puddle each).
// Needs PunchyValueNoise (Sky.hlsl).
float4 _PunchyTrickles[48];   // per trickle: (slit xyz, width), (out x, out z, drop, kind), (foot x, landing height, foot z, puddle radius)
int _PunchyTrickleCount;

void PunchyTrickleWet(float3 p, float3 shape, inout half3 rock, inout float gloss)
{
    for (int k = 0; k < _PunchyTrickleCount; k++)
    {
        float4 a = _PunchyTrickles[k * 3];
        float4 b = _PunchyTrickles[k * 3 + 1];
        float4 c = _PunchyTrickles[k * 3 + 2];
        float3 rel = p - a.xyz;
        float2 outward = b.xy;
        float across = dot(rel.xz, float2(-outward.y, outward.x));
        float depth = dot(rel.xz, outward);
        float below = -rel.y;
        float width = max(a.w, 0.05);
        float drop = max(b.z, 0.1);
        int kind = (int)round(b.w);
        [branch] if (abs(across) < width * 1.6 + 1.0 && below > -0.3 && below < drop + 0.3 && abs(depth) < 1.5)
        {
            float facing = smoothstep(0.1, 0.3, dot(shape.xz, outward)) * step(abs(depth), 0.6);
            // The slit: a dark lens across the wall, its edges a little ragged, with a paler lip over it.
            float ragged = 0.05 + 0.025 * PunchyValueNoise(float2(across * 9 + k * 13, 3));
            float2 lens = float2(across / (width * 0.5), rel.y / ragged);
            float slit = smoothstep(1, 0.6, dot(lens, lens)) * facing;
            float2 lipLens = float2(across / (width * 0.6), (rel.y - ragged - 0.03) / 0.035);
            float lip = smoothstep(1, 0.4, dot(lipLens, lipLens)) * facing * (1 - slit);
            rock *= lerp(1, 0.12, slit) * (1 + 0.22 * lip);
            gloss = lerp(gloss, 0.7, slit * 0.5);

            // The wet streak below it, spreading as the kind does, ragged at the edges.
            float down = saturate(below / drop);
            float spread = kind == 1 ? lerp(1.0, 2.2, down) : kind == 2 ? lerp(1.0, 1.3, down) : lerp(0.8, 1.2, down);
            float halfWidth = width * 0.5 * spread + 0.05;
            halfWidth *= 0.8 + 0.4 * PunchyValueNoise(float2(across * 3 + k * 7, p.y * 2.3));
            float wet = smoothstep(halfWidth, halfWidth * 0.6, abs(across)) * step(0.02, below) * smoothstep(drop + 0.2, drop - 0.1, below)
                      * facing * (1 - slit);
            if (kind == 3)
                wet *= smoothstep(0.3, 0.1, below);
            rock *= lerp(1, 0.55, wet);
            gloss = lerp(gloss, 0.92, wet);

            // The puddle at the foot, on floor only (none where it lands in a pool).
            float2 fromFoot = p.xz - c.xz;
            float radius = c.w * (0.85 + 0.3 * PunchyValueNoise(fromFoot * 3 + k * 5));
            float onFloor = smoothstep(0.6, 0.8, shape.y) * step(abs(p.y - c.y), 0.15) * step(0.01, c.w);
            float puddle = smoothstep(radius, radius * 0.75, length(fromFoot)) * onFloor;
            rock *= lerp(1, 0.5, puddle);
            gloss = lerp(gloss, 0.95, puddle);
        }
    }
}

#endif
