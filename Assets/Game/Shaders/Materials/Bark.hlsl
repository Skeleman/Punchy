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

// Value noise that wraps x every wrap cells, so it meets itself round the trunk.
half PunchyBarkNoise(float2 p, int wrap)
{
    int2 i = (int2)floor(p);
    float2 f = p - i;
    float2 u = f * f * (3 - 2 * f);
    int x0 = (int)((uint)(i.x + 64 * wrap) % (uint)wrap), x1 = (int)((uint)(i.x + 1 + 64 * wrap) % (uint)wrap);
    return lerp(lerp(PunchyCellHash(int2(x0, i.y)), PunchyCellHash(int2(x1, i.y)), u.x),
                lerp(PunchyCellHash(int2(x0, i.y + 1)), PunchyCellHash(int2(x1, i.y + 1)), u.x), u.y);
}

// age is the instance's scale, rise the height above the plant's base (m), lush how green the ground is (moss
// and lichen). crevice: how deep in a furrow or crack the pixel sits (0-1), for the caller's occlusion and sheen.
void PunchyBark(float3 gp, float3 positionWS, half r, int kind, float age, float rise, half lush, inout half3 albedo, inout half3 n, out half crevice)
{
    float ang = atan2(gp.z, gp.x);
    float2 ring = float2(cos(ang), sin(ang));
    float ringStep = length(fwidth(ring));

    half furrowAmt = 1, width = 0.07, depth = 0.4;
    int round_ = 5 + (int)floor(r * 4);
    float tall = 0.45;
    if (kind == PUNCHY_BARK_PLATED)
    {
        furrowAmt = 0.15; round_ = 21 + (int)floor(r * 6); tall = 0.24; width = 0.13;
        depth = 0.38 * lerp(0.8, 1.25, saturate((age - 0.8) / 0.6));
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
    crevice = 0;
    half3 n0 = n;
    float3 around = normalize(float3(-n.z, 0, n.x) + 1e-5);
    if (furrowAmt > 0)
    {
        float furrows = 5 + floor(r * 4);
        half wander = PunchyValueNoise(ring * 1.7 + float2(gp.y * 0.8, r * 17.0)) - 0.5;
        float phase = ang * furrows + wander * 3.0 + gp.y * 0.12;
        half furrow = 1 - smoothstep(0.0, 0.5, abs(sin(phase)));
        half fibre = 0.5 + 0.5 * sin(phase * 4 + PunchyValueNoise(float2(gp.y * 6.0, ang * 0.5 + r * 9.0)) * 4.0);
        half fade = (1 - smoothstep(0.35, 0.9, ringStep * furrows)) * furrowAmt;
        shade *= lerp(1, (1 - furrow * 0.3) * (0.92 + 0.16 * fibre) + 0.07, fade);
        n = normalize(n + around * cos(phase) * sign(sin(phase)) * 0.5 * fade);
        crevice = furrow * furrow * fade;
    }

    float2 uv = float2((ang * 0.159155 + 0.5) * round_, gp.y / tall);
    uv.x += (PunchyValueNoise(float2(gp.y * 0.9, r * 23.0)) - 0.5) * 0.6;
    if (kind == PUNCHY_BARK_CHECKED)
        uv.y += uv.x * 0.15;
    float px = max(ringStep * round_ * 0.159155, fwidth(gp.y) / tall);
    half cellFade = 1 - smoothstep(width * 0.5, width * 1.6, px);
    half scaleFade = kind == PUNCHY_BARK_PLATED ? 1 - smoothstep(width * 0.5, width * 1.6, px * 2.6) : 0;
    half tone = 0.5, weather = 0, rust = 0;
    [branch] if (cellFade > 0.01)
    {
        float cellTone;
        float2 cuv = uv;
        if (kind == PUNCHY_BARK_PLATED)
            cuv += (float2(PunchyBarkNoise(uv * 3, round_ * 3), PunchyBarkNoise(uv * 3 + float2(0, 17.3), round_ * 3)) - 0.5) * 0.3
                 + (float2(PunchyBarkNoise(uv * 8, round_ * 8), PunchyBarkNoise(uv * 8 + float2(0, 41.7), round_ * 8)) - 0.5) * 0.1;
        half edge = PunchyBarkCells(cuv, round_, cellTone);
        tone = cellTone;
        half wide = width * lerp(0.55, 1.5, PunchyValueNoise(uv * float2(0.6, 0.35) + r * 7.0));
        if (kind == PUNCHY_BARK_PLATED)
            wide *= lerp(0.75, 1.5, PunchyBarkNoise(uv * 6, round_ * 6));
        half crack = 1 - smoothstep(wide * 0.1, wide * 1.1, edge);
        // Checks open only on some cell walls, so the trunk isn't caged.
        if (kind != PUNCHY_BARK_PLATED)
            crack *= smoothstep(0.35, 0.6, PunchyValueNoise(uv * float2(1.3, 0.7) + r * 31.0));
        else
        {
            half rim = smoothstep(wide * 0.1, wide * 1.1, edge) * (1 - smoothstep(wide * 1.1, wide * 2.6, edge));
            half scale = 0, flake = 0;
            [branch] if (scaleFade > 0.01)
            {
                float scaleTone;
                half e2 = PunchyBarkCells(cuv * float2(2, 2.6), round_ * 2, scaleTone);
                scale = (1 - smoothstep(0.02, 0.09, e2)) * smoothstep(0.4, 0.6, PunchyBarkNoise(cuv * float2(4, 3), round_ * 8)) * scaleFade;
                flake = smoothstep(0.8, 0.86, scaleTone) * scaleFade;
            }
            scale *= 1 - crack;
            flake *= 1 - crack;
            h += scale * 0.35 + flake * 0.25;
            shade *= 1 - scale * 0.25;
            weather = (1 - crack) * (1 - flake) * (0.65 + 0.35 * tone) * cellFade;
            rust = max(rim * 0.45, flake * 0.8) * cellFade;
        }
        if (kind == PUNCHY_BARK_CHECKED)
            shade *= lerp(1, 0.55, (1 - smoothstep(0.0, 0.05, abs(frac(uv.y * 0.5 + tone) - 0.5) - 0.42)) * step(0.7, tone) * cellFade);
        h = h * cellFade + crack * cellFade;
        half plate = 1 + (tone - 0.5) * (kind == PUNCHY_BARK_PLATED ? 0.3 : 0.1);
        shade *= lerp(1, (1 - crack * depth * 0.55) * plate + depth * width * 0.5, cellFade);
        crevice = max(crevice, crack * cellFade * saturate(depth * 1.6));
    }
    // Outside the branch so the screen derivatives stay defined.
    float3 dpx = ddx(positionWS), dpy = ddy(positionWS);
    float3 r1 = cross(dpy, n), r2 = cross(n, dpx);
    float det = dot(dpx, r1);
    float3 grad = sign(det) * (ddx(h) * r1 + ddy(h) * r2);
    n = normalize(abs(det) * n + grad * depth * 0.12);

    albedo *= shade * lerp(half3(0.9, 0.94, 0.98), half3(1.08, 1.02, 0.92), PunchyValueNoise(float2(rise * 0.6, r * 41.0)));
    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
    albedo = lerp(albedo, lum * 2.2 * half3(1.08, 1, 0.88), weather);
    albedo = lerp(albedo, albedo * half3(1.25, 0.92, 0.7) + half3(0.05, 0.025, 0.01), rust);
    // Moss and lichen keep to the shaded (north) side, moss low on the trunk and down in the cracks.
    half north = saturate(n0.z * 0.8 + 0.3);
    half lichenPatch = PunchyValueNoise(uv * float2(1.6, 1.1) + r * 13.0) * 0.6 + PunchyValueNoise(uv * 5.3 - r * 5.0) * 0.4;
    half lichen = smoothstep(0.7, 0.84, lichenPatch + north * 0.08 + tone * 0.04) * (1 - crevice) * cellFade * (kind == PUNCHY_BARK_CHECKED ? 0.2 : 0.4) * (0.4 + 0.6 * lush);
    albedo = lerp(albedo, albedo * half3(1.35, 1.45, 1.2) + half3(0.05, 0.06, 0.04) * (0.85 + 0.3 * tone), lichen);
    half mossPatch = PunchyValueNoise(uv * float2(0.5, 0.3) - r * 19.0) + crevice * 0.35;
    half moss = smoothstep(0.5, 0.62, mossPatch * north) * (1 - smoothstep(0.15, 0.6 + north * 0.9, rise / max(age, 0.3))) * cellFade * lush;
    albedo = lerp(albedo, half3(0.2, 0.3, 0.09) * (0.8 + 0.4 * lichenPatch), moss * 0.85);
}

#endif
