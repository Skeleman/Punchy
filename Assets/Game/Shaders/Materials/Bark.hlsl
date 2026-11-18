#ifndef PUNCHY_BARK_INCLUDED
#define PUNCHY_BARK_INCLUDED

// Needs Sky.hlsl (the noise) included first.

#define PUNCHY_BARK_FURROWED 0
#define PUNCHY_BARK_PLATED 1
#define PUNCHY_BARK_CHECKED 2
#define PUNCHY_BARK_SMOOTH 3

half PunchyBarkCells(float2 uv, int wrap, out float cellTone)
{
    int2 c = (int2)floor(uv);
    float2 f = uv - c;
    float d1 = 9, d2 = 9;
    cellTone = 0.5;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        int2 k = int2((uint)(c.x + i + 4 * wrap) % (uint)wrap, c.y + j);
        float2 o = float2(i, j) + 0.5 + (float2(PunchyCellHash(k), PunchyCellHash(k + int2(57, 113))) - 0.5) * 0.8 - f;
        float d = dot(o, o);
        if (d < d1) { d2 = d1; d1 = d; cellTone = PunchyCellHash(k + int2(11, 5)); }
        else if (d < d2) d2 = d;
    }
    return (half)(sqrt(d2) - sqrt(d1));
}

// age is the instance's scale, rise the height above the plant's base (m).
half PunchyBark(float3 gp, float3 positionWS, half r, int kind, float age, float rise, inout half3 n)
{
    float ang = atan2(gp.z, gp.x);
    float2 ring = float2(cos(ang), sin(ang));
    float ringStep = length(fwidth(ring));

    half furrowAmt = 1, width = 0.07, depth = 0.4;
    int round_ = 5 + (int)floor(r * 4);
    float tall = 0.45;
    if (kind == PUNCHY_BARK_PLATED)
    {
        furrowAmt = 0.25; round_ = 4 + (int)floor(r * 3); tall = 0.4; width = 0.17;
        depth = 0.62 * lerp(0.8, 1.25, saturate((age - 0.8) / 0.6));
    }
    else if (kind == PUNCHY_BARK_CHECKED)
    {
        furrowAmt = 0; round_ = 11 + (int)floor(r * 4); tall = 0.09; width = 0.14;
        // Birch-like: fissured and dark low down, smooth with fine checks higher up.
        depth = lerp(0.75, 0.3, smoothstep(0.4, 2.2, rise / max(age, 0.3)));
    }
    else if (kind == PUNCHY_BARK_SMOOTH)
    {
        furrowAmt = 0; round_ = 14; tall = 0.07; width = 0.05; depth = 0.18;
    }

    half shade = 1, h = 0;
    float3 around = normalize(float3(-n.z, 0, n.x) + 1e-5);
    if (furrowAmt > 0)
    {
        float furrows = 5 + floor(r * 4);
        half wander = PunchyValueNoise(ring * 1.7 + float2(gp.y * 0.8, r * 17.0)) - 0.5;
        float phase = ang * furrows + wander * 3.0 + gp.y * 0.12;
        half furrow = 1 - smoothstep(0.0, 0.5, abs(sin(phase)));
        half fibre = 0.5 + 0.5 * sin(phase * 4 + PunchyValueNoise(float2(gp.y * 6.0, ang * 0.5 + r * 9.0)) * 4.0);
        half fade = (1 - smoothstep(0.35, 0.9, ringStep * furrows)) * furrowAmt;
        shade *= lerp(1, (1 - furrow * 0.5) * (0.92 + 0.16 * fibre) + 0.12, fade);
        n = normalize(n + around * cos(phase) * sign(sin(phase)) * 0.35 * fade);
    }

    float2 uv = float2((ang * 0.159155 + 0.5) * round_, gp.y / tall);
    uv.x += (PunchyValueNoise(float2(gp.y * 0.9, r * 23.0)) - 0.5) * 0.6;
    if (kind == PUNCHY_BARK_CHECKED)
        uv.y += uv.x * 0.15;
    float px = max(ringStep * round_ * 0.159155, fwidth(gp.y) / tall);
    half cellFade = 1 - smoothstep(width * 0.5, width * 1.6, px);
    [branch] if (cellFade > 0.01)
    {
        float tone;
        half edge = PunchyBarkCells(uv, round_, tone);
        half crack = 1 - smoothstep(width * 0.35, width, edge);
        // Plates break cleanly; checks open only on some cell walls, so the trunk isn't caged.
        if (kind != PUNCHY_BARK_PLATED)
            crack *= smoothstep(0.35, 0.6, PunchyValueNoise(uv * float2(1.3, 0.7) + r * 31.0));
        if (kind == PUNCHY_BARK_CHECKED)
            shade *= lerp(1, 0.55, (1 - smoothstep(0.0, 0.05, abs(frac(uv.y * 0.5 + tone) - 0.5) - 0.42)) * step(0.7, tone) * cellFade);
        h = crack * cellFade;
        half plate = 1 + (tone - 0.5) * (kind == PUNCHY_BARK_PLATED ? 0.22 : 0.08);
        shade *= lerp(1, (1 - crack * depth) * plate + depth * width * 0.8, cellFade);
    }
    // Outside the branch so the screen derivatives stay defined.
    float3 dpx = ddx(positionWS), dpy = ddy(positionWS);
    float3 r1 = cross(dpy, n), r2 = cross(n, dpx);
    float det = dot(dpx, r1);
    float3 grad = sign(det) * (ddx(h) * r1 + ddy(h) * r2);
    n = normalize(abs(det) * n + grad * depth * 0.03);
    return shade;
}

#endif
