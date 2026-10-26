#ifndef PUNCHY_SKY_INCLUDED
#define PUNCHY_SKY_INCLUDED

// The sky, as a function of view direction. Shared by the skybox and anything that reflects the sky (water), so
// reflections always match the current weather and time of day. Clouds live on a horizontal layer CLOUD_HEIGHT
// above sea level, so the same function also gives cloud shadows on the ground. All inputs are globals set every
// frame by WeatherSystem.

#define PUNCHY_CLOUD_HEIGHT 1500.0
#define PUNCHY_CLOUD_FEATURE 1800.0

float3 _PunchySunDirection;   // towards the sun
float3 _PunchyMoonDirection;  // towards the moon
half3  _PunchySunColor;       // sun colour × brightness (0 when set)
half3  _PunchySkyZenith;
half3  _PunchySkyHorizon;     // also the fog colour, so distant terrain fades seamlessly into the sky
half3  _PunchySkyGround;
half4  _PunchyClouds;         // coverage, darkness, scale, unused
float2 _PunchyCloudOffset;    // cloud-layer drift with the wind (in layer UV units)
half   _PunchyNight;          // 0 day … 1 night
float4 _PunchyLightning;      // xyz towards the strike, w flash brightness

float PunchyHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float PunchyHash3(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
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

// Cloud density (0..1) at a world XZ position on the cloud layer.
float PunchyCloudDensityAt(float2 worldXZ, out float noiseValue)
{
    float2 uv = worldXZ / PUNCHY_CLOUD_FEATURE * _PunchyClouds.z + _PunchyCloudOffset;
    noiseValue = PunchyFbm(uv);
    // The noise mostly spans ~0.2–0.8, so coverage moves the threshold across that range.
    float threshold = lerp(0.72, 0.26, _PunchyClouds.x);
    return smoothstep(threshold, threshold + 0.22, noiseValue);
}

// 0..1: how much a point on the ground is shaded from the sun by the cloud layer above it.
half PunchyCloudShadow(float3 positionWS)
{
    float3 toSun = _PunchySunDirection;
    if (toSun.y < 0.05)
        return 0;
    float2 hit = positionWS.xz + toSun.xz * (PUNCHY_CLOUD_HEIGHT - positionWS.y) / toSun.y;
    float n;
    return PunchyCloudDensityAt(hit, n) * 0.8;
}

half3 PunchyStars(float3 dir)
{
    float3 p = dir * 260;
    float3 cell = floor(p);
    float h = PunchyHash3(cell);
    if (h < 0.9965)
        return 0;
    float3 centre = cell + 0.5 + (float3(PunchyHash3(cell + 1.7), PunchyHash3(cell + 3.1), PunchyHash3(cell + 5.3)) - 0.5) * 0.6;
    float d = length(p - centre);
    float twinkle = 0.6 + 0.4 * sin(_Time.y * (2 + h * 30) + h * 100);
    return smoothstep(0.25, 0, d) * twinkle * lerp(half3(0.8, 0.85, 1.0), half3(1.0, 0.9, 0.75), frac(h * 731));
}

half3 PunchySkyColor(float3 dir, bool withDiscs)
{
    dir = normalize(dir);
    float h = dir.y;
    half3 sky = lerp(_PunchySkyHorizon, _PunchySkyZenith, pow(saturate(h), 0.45));
    sky = h < 0 ? lerp(_PunchySkyHorizon, _PunchySkyGround, saturate(-h * 3)) : sky;

    float sunDot = saturate(dot(dir, _PunchySunDirection));
    float moonDot = saturate(dot(dir, _PunchyMoonDirection));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    sky += _PunchySunColor * pow(sunDot, 12) * 0.25 * clearness;
    float lightningGlow = _PunchyLightning.w * (0.25 + 1.5 * pow(saturate(dot(dir, _PunchyLightning.xyz)), 3));

    if (h > 0)
    {
        // Where the view ray meets the cloud layer.
        float3 camera = _WorldSpaceCameraPos;
        float distanceToLayer = min((PUNCHY_CLOUD_HEIGHT - camera.y) / max(h, 0.02), 60000);
        float2 hit = camera.xz + dir.xz * distanceToLayer;
        float n;
        float cloud = PunchyCloudDensityAt(hit, n) * smoothstep(0.0, 0.12, h);

        // Cheap self-shadowing: denser cloud towards the sun means a darker underside.
        float nTowardSun;
        PunchyCloudDensityAt(hit + _PunchySunDirection.xz * 120, nTowardSun);
        float shade = saturate(0.65 + (n - nTowardSun) * 4);

        half daylight = saturate(dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2)) * 1.6);
        half3 lit = (lerp(half3(1, 1, 1), _PunchySunColor * 0.9 + 0.1, 0.3) * (1 - _PunchyClouds.y * 0.55)) * daylight;
        half3 dark = lerp(half3(0.55, 0.58, 0.64), half3(0.22, 0.23, 0.26), _PunchyClouds.y) * daylight;
        half3 cloudColor = lerp(dark, lit, shade) + _PunchySunColor * pow(sunDot, 6) * 0.3 * (1 - _PunchyClouds.y);
        cloudColor += half3(0.75, 0.8, 1.0) * lightningGlow * 2.5;   // lit from inside by lightning
        cloudColor += half3(0.6, 0.7, 1.0) * pow(moonDot, 8) * 0.08 * _PunchyNight;

        // Night sky: stars and the moon, hidden behind cloud.
        half3 night = PunchyStars(dir) * _PunchyNight;
        if (withDiscs)
        {
            night += half3(0.9, 0.92, 1.0) * smoothstep(0.99982, 0.99987, moonDot) * 1.6 * _PunchyNight;
            sky += _PunchySunColor * smoothstep(0.9994, 0.9997, sunDot) * 12 * clearness;
        }
        sky += half3(0.35, 0.42, 0.6) * pow(moonDot, 40) * 0.25 * _PunchyNight;
        sky = lerp(sky + night, cloudColor, cloud);
    }
    sky += half3(0.5, 0.55, 0.7) * lightningGlow * 0.3;

    // Haze: the horizon band is exactly the fog colour.
    return lerp(sky, _PunchySkyHorizon, exp(-max(h, 0) * 10) * 0.85);
}

#endif
