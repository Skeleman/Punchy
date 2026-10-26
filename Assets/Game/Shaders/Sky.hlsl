#ifndef PUNCHY_SKY_INCLUDED
#define PUNCHY_SKY_INCLUDED

// The sky, as a function of view direction. Shared by the skybox and anything that reflects the sky (water), so
// reflections always match the current weather. All inputs are globals set every frame by WeatherSystem.

float3 _PunchySunDirection;   // towards the sun
half3  _PunchySunColor;
half3  _PunchySkyZenith;
half3  _PunchySkyHorizon;     // also the fog colour, so distant terrain fades seamlessly into the sky
half3  _PunchySkyGround;
half4  _PunchyClouds;         // coverage, darkness, scale, unused
float2 _PunchyCloudOffset;    // drifts with the wind

float PunchyHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float PunchyValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(PunchyHash(i), PunchyHash(i + float2(1, 0)), u.x),
                lerp(PunchyHash(i + float2(0, 1)), PunchyHash(i + 1), u.x), u.y);
}

float PunchyFbm(float2 p)
{
    float sum = 0, amp = 0.5;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        sum += amp * PunchyValueNoise(p);
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return sum / 0.97;
}

// 0..1 cloud density in direction dir (dir.y > 0), plus a lit/shadowed shade term.
float PunchyCloud(float3 dir, out float shade)
{
    float2 uv = dir.xz / (dir.y + 0.12) * 0.35 * _PunchyClouds.z + _PunchyCloudOffset;
    float n = PunchyFbm(uv);
    // The noise mostly spans ~0.2–0.8, so coverage moves the threshold across that range.
    float threshold = lerp(0.72, 0.26, _PunchyClouds.x);
    float density = smoothstep(threshold, threshold + 0.22, n) * smoothstep(0.0, 0.12, dir.y);
    // Cheap self-shadowing: denser cloud between this point and the sun means a darker underside.
    float towardSun = PunchyFbm(uv + _PunchySunDirection.xz * 0.06);
    shade = saturate(0.65 + (n - towardSun) * 4);
    return density;
}

half3 PunchySkyColor(float3 dir, bool withSunDisc)
{
    dir = normalize(dir);
    float h = dir.y;
    half3 sky = lerp(_PunchySkyHorizon, _PunchySkyZenith, pow(saturate(h), 0.45));
    sky = h < 0 ? lerp(_PunchySkyHorizon, _PunchySkyGround, saturate(-h * 3)) : sky;

    float sunDot = saturate(dot(dir, _PunchySunDirection));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    sky += _PunchySunColor * pow(sunDot, 12) * 0.25 * clearness;

    if (h > 0)
    {
        float shade;
        float cloud = PunchyCloud(dir, shade);
        half3 lit = lerp(half3(1, 1, 1), _PunchySunColor * 0.9 + 0.1, 0.3) * (1 - _PunchyClouds.y * 0.55);
        half3 dark = lerp(half3(0.55, 0.58, 0.64), half3(0.22, 0.23, 0.26), _PunchyClouds.y);
        half3 cloudColor = lerp(dark, lit, shade) + _PunchySunColor * pow(sunDot, 6) * 0.3 * (1 - _PunchyClouds.y);
        sky = lerp(sky, cloudColor, cloud);
        if (withSunDisc)
            sky += _PunchySunColor * smoothstep(0.9994, 0.9997, sunDot) * 12 * (1 - cloud) * clearness;
    }

    // Haze: the horizon band is exactly the fog colour.
    return lerp(sky, _PunchySkyHorizon, exp(-max(h, 0) * 10) * 0.85);
}

#endif
