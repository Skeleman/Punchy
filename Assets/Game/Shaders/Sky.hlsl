#ifndef PUNCHY_SKY_INCLUDED
#define PUNCHY_SKY_INCLUDED

// The sky, as a function of view direction, and the atmosphere between the camera and anything it sees. Shared by
// the skybox, terrain and water, so they always agree. Weather is anchored in the world: WeatherSystem renders it
// into a world-covering weather map (cloud coverage, cloud darkness, rain/mist haze, dust) that the clouds, cloud
// shadows and atmosphere all read. Clouds live on a horizontal layer CLOUD_HEIGHT above sea level.

#define PUNCHY_CLOUD_HEIGHT 1500.0
#define PUNCHY_CLOUD_FEATURE 1800.0
#define PUNCHY_DUST_DENSITY 0.02      // extinction per metre in a full sandstorm (~150 m visibility)
#define PUNCHY_DUST_TOP 350.0         // dust thins out above this height
#define PUNCHY_RAIN_DENSITY 0.0016    // extinction per metre in full rain (~1.5 km visibility)

TEXTURE2D(_PunchyWeatherMap);  SAMPLER(sampler_PunchyWeatherMap);   // rgba: coverage, darkness, haze, dust
TEXTURE2D(_PunchyWetnessMap);  SAMPLER(sampler_PunchyWetnessMap);   // rg: ground wetness, precipitation
float4 _PunchyWeatherRect;     // xy world origin of the maps, zw 1 / world size
half3  _PunchyDustColor;
float  _PunchyHazeDensity;     // base exponential-squared haze per metre (hides the edge of the streamed terrain)

half4 PunchyWeatherAt(float2 worldXZ)
{
    return SAMPLE_TEXTURE2D_LOD(_PunchyWeatherMap, sampler_PunchyWeatherMap, (worldXZ - _PunchyWeatherRect.xy) * _PunchyWeatherRect.zw, 0);
}

half2 PunchyWetnessAt(float2 worldXZ)
{
    return SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, (worldXZ - _PunchyWeatherRect.xy) * _PunchyWeatherRect.zw, 0).rg;
}

float3 _PunchySunDirection;   // towards the sun
float3 _PunchyMoonDirection;  // towards the moon
half3  _PunchySunColor;       // sun colour × brightness (0 when set)
half3  _PunchySkyZenith;
half3  _PunchySkyHorizon;     // also the fog colour, so distant terrain fades seamlessly into the sky
half3  _PunchySkyGround;
half4  _PunchyClouds;         // x coverage at the camera (dims the sun's glow), unused, z scale, unused
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

float PunchyFbm(float2 p, int octaves)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll(5)]
    for (int i = 0; i < octaves; i++)
    {
        sum += amp * PunchyValueNoise(p);
        norm += amp;
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return sum / norm;
}

// Cloud density (0..1) at a world XZ position on the cloud layer, with the local coverage and darkness.
// Fewer octaves are fine where only the broad shape matters (shadows on the ground).
float PunchyCloudDensityAt(float2 worldXZ, out float noiseValue, out half darkness, int octaves = 5)
{
    half4 weather = PunchyWeatherAt(worldXZ);
    darkness = weather.g;
    float2 uv = worldXZ / PUNCHY_CLOUD_FEATURE * _PunchyClouds.z + _PunchyCloudOffset;
    noiseValue = PunchyFbm(uv, octaves);
    // The noise mostly spans ~0.2–0.8, so coverage moves the threshold across that range.
    float threshold = lerp(0.72, 0.26, weather.r);
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
    half darkness;
    float density = PunchyCloudDensityAt(hit, n, darkness, 3);
    return density * lerp(0.65, 0.92, darkness);
}

// Dust and rain haze along a ray, read from the weather map: a distant sandstorm is a tan wall, rain under a
// distant storm hangs as grey curtains. Steps are packed towards the camera, where detail matters most.
half3 PunchyWeatherVolumes(half3 color, float3 start, float3 dir, float rayLength, int steps)
{
    half brightness = dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2));
    half3 scatter = 0;
    float transmit = 1;
    float previous = 0;
    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float s = (float)i / steps;
        float t = rayLength * s * s;
        float segment = t - previous;
        float3 p = start + dir * (previous + segment * 0.5);
        previous = t;

        half4 w = PunchyWeatherAt(p.xz);
        if (w.a + w.b <= 0.004)
            continue;

        // Drifting noise breaks the volumes up: dust billows with an uneven top, rain falls in streaky curtains.
        float2 drift = _PunchyCloudOffset * PUNCHY_CLOUD_FEATURE;
        float billow = PunchyValueNoise((p.xz - drift) / 260) * 0.65 + PunchyValueNoise((p.xz - drift * 1.6) / 90) * 0.35;
        float dustTop = PUNCHY_DUST_TOP * (0.45 + billow);
        float dust = w.a * PUNCHY_DUST_DENSITY * saturate((dustTop - p.y) / 180) * (0.4 + billow);
        float streaks = PunchyValueNoise((p.xz - drift) / 110);
        float rain = w.b * PUNCHY_RAIN_DENSITY * saturate((PUNCHY_CLOUD_HEIGHT - p.y) / 400) * (0.3 + 1.4 * streaks);
        float density = dust + rain;
        if (density <= 1e-6)
            continue;
        half3 rainTint = _PunchySkyHorizon * 0.72 * (1 - 0.5 * w.g);
        half3 tint = (dust * _PunchyDustColor * (brightness * 1.4 + 0.03) + rain * rainTint) / density;
        float absorbed = 1 - exp(-density * segment);
        scatter += tint * absorbed * transmit;
        transmit *= 1 - absorbed;
    }
    return color * transmit + scatter;
}

// Everything between the camera and a surface point: weather volumes, then the base haze (fog coloured like the
// horizon, thick enough at the edge of the streamed terrain to hide it).
half3 PunchyAtmosphere(half3 color, float3 positionWS)
{
    float3 camera = _WorldSpaceCameraPos;
    float3 toPoint = positionWS - camera;
    float distance = length(toPoint);
    float3 dir = toPoint / max(distance, 1e-3);
    color = PunchyWeatherVolumes(color, camera, dir, min(distance, 8000), 8);
    float haze = 1 - exp(-pow(distance * _PunchyHazeDensity, 2));
    return lerp(color, _PunchySkyHorizon, haze);
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
        half darkness;
        float cloud = PunchyCloudDensityAt(hit, n, darkness) * smoothstep(0.0, 0.12, h);

        // Cheap self-shadowing: denser cloud towards the sun means a darker underside.
        float nTowardSun;
        half unused;
        PunchyCloudDensityAt(hit + _PunchySunDirection.xz * 120, nTowardSun, unused);
        float shade = saturate(0.65 + (n - nTowardSun) * 4);

        half daylight = saturate(dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2)) * 1.6);
        half3 lit = (lerp(half3(1, 1, 1), _PunchySunColor * 0.9 + 0.1, 0.3) * (1 - darkness * 0.55)) * daylight;
        half3 dark = lerp(half3(0.55, 0.58, 0.64), half3(0.22, 0.23, 0.26), darkness) * daylight;
        half3 cloudColor = lerp(dark, lit, shade) + _PunchySunColor * pow(sunDot, 6) * 0.3 * (1 - darkness);
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
