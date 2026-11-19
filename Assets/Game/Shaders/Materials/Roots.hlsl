#ifndef PUNCHY_ROOTS_INCLUDED
#define PUNCHY_ROOTS_INCLUDED

// Needs PunchyCellHash and PunchyValueNoise (SkyCommon.hlsl).

void PunchyRootColumn(float u, float v, int c, float spacing, float footprint,
                      inout half h, inout half slope, inout half cover, inout half shadow)
{
    float r1 = PunchyCellHash(int2(c, 71)), r2 = PunchyCellHash(int2(c, 72)), r3 = PunchyCellHash(int2(c, 73));
    float radius = r1 < 0.2 ? 0.12 : r1 < 0.55 ? 0.07 : 0.035;
    half fade = saturate((2 * radius / footprint - 1.5) / 1.5);
    if (fade <= 0)
        return;
    half run = smoothstep(0.45, 0.65, PunchyValueNoise(float2(c * 1.37 + 0.5, v * 0.3 + r2 * 40)));
    if (run <= 0)
        return;
    float centre = (c + 0.3 + 0.4 * r3) * spacing + (PunchyValueNoise(float2(v * 0.5, c * 3.1 + 9)) - 0.5) * spacing * 0.35;
    float period = lerp(0.5, 1.6, r2) * sqrt(radius / 0.07);
    float phase = v * 6.2832 / period + r3 * 6.2832 + (PunchyValueNoise(float2(v * 2.3, c * 1.9 + 4)) - 0.5) * 1.2;
    float sweep = radius * 1.2;
    [unroll] for (int s = 0; s < 2; s++)
    {
        float ph = phase + s * 3.1416;
        float rs = radius * (s == 0 ? 1.0 : 0.7) * lerp(0.35, 1.0, run);
        float d = (u - centre - sin(ph) * sweep) / rs;
        half body = saturate(1 - d * d);
        half depth = 0.7 + 0.3 * cos(ph);
        half hs = body * depth * fade;
        shadow = max(shadow, saturate(1 - abs(abs(d) - 1.2) * 2) * fade * run);
        if (hs > h)
        {
            h = hs;
            slope = (half)(-2 * clamp(d, -1, 1) * depth);
            cover = smoothstep(0, 0.15, body) * fade;
        }
    }
}

half PunchyRootStrands(float u, float y, float footprint, out half slope, out half shadow)
{
    const float spacing = 0.9;
    int ci = (int)floor(u / spacing);
    half h = 0, cover = 0;
    slope = 0; shadow = 0;
    [unroll] for (int k = -1; k <= 1; k++)
        PunchyRootColumn(u, y, ci + k, spacing, footprint, h, slope, cover, shadow);
    return cover;
}

// Fixed world axes blended by the normal: a per-facet axis would break strands at every triangle edge.
half PunchyStranglerRelief(float3 p, float3 shapeN, float footprint, half amount, inout half3 albedo, inout float3 n, inout float gloss)
{
    amount *= saturate((1 - abs(shapeN.y)) * 2.5 - 0.25);
    [branch] if (amount <= 0.001)
        return 0;
    half wx = smoothstep(0.3, 0.7, abs(shapeN.x) / (abs(shapeN.x) + abs(shapeN.z) + 1e-4));
    half cover = 0, shadow = 0;
    float3 bend = 0;
    [branch] if (wx < 0.999)
    {
        half sl, sh;
        half c = PunchyRootStrands(p.x, p.y, footprint, sl, sh) * (1 - wx);
        cover += c; shadow += sh * (1 - wx); bend += float3(sl * c, 0, 0);
    }
    [branch] if (wx > 0.001)
    {
        half sl, sh;
        half c = PunchyRootStrands(p.z + 37.0, p.y, footprint, sl, sh) * wx;
        cover += c; shadow += sh * wx; bend += float3(0, 0, sl * c);
    }
    cover *= amount;
    n = normalize(n - bend * 0.9 * amount);
    half3 bark = half3(0.3, 0.22, 0.15) * (0.6 + 0.6 * saturate(cover));
    albedo = lerp(albedo * (1 - shadow * 0.6 * amount * (1 - cover)), bark, cover);
    gloss = lerp(gloss, 0.1, cover);
    return cover;
}

#endif
