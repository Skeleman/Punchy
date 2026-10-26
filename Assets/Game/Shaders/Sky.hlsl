#ifndef PUNCHY_SKY_INCLUDED
#define PUNCHY_SKY_INCLUDED

// The sky, its clouds, and the atmosphere between the camera and anything it sees. Shared by the skybox, terrain
// and water, so they always agree. Weather is anchored in the world: WeatherSystem renders it into world-covering
// maps that everything here reads:
//   weather map  rgba = cloud coverage, cloud darkness, rain/mist haze, dust
//   wetness map  rg   = ground wetness, precipitation
//   cloud map    rg   = cloud base / 4000 m, cloud thickness / 9000 m
// Clouds are volumetric, carved from a tiling 3D noise texture (CloudNoise) between each place's cloud base and
// top: puffy cumulus in fair weather, flat stratus when overcast, cumulonimbus towers with anvils in storms.
// Rain falls from the local cloud base. A thin cirrus layer sits above everything.

#define PUNCHY_CLOUD_FEATURE 1800.0   // drift units of _PunchyCloudOffset, in metres
#define PUNCHY_CLOUD_NOISE_SCALE 4000.0
#define PUNCHY_CLOUD_SIGMA 0.012      // cloud extinction per metre at density 1
#define PUNCHY_CIRRUS_HEIGHT 8500.0
#define PUNCHY_DUST_DENSITY 0.02      // extinction per metre in a full sandstorm (~150 m visibility)
#define PUNCHY_DUST_TOP 350.0         // dust thins out above this height
#define PUNCHY_RAIN_DENSITY 0.0016    // extinction per metre in full rain (~1.5 km visibility)

TEXTURE2D(_PunchyWeatherMap);  SAMPLER(sampler_PunchyWeatherMap);
TEXTURE2D(_PunchyWetnessMap);  SAMPLER(sampler_PunchyWetnessMap);
TEXTURE2D(_PunchyCloudMap);    SAMPLER(sampler_PunchyCloudMap);
TEXTURE3D(_PunchyCloudNoise);  SAMPLER(sampler_PunchyCloudNoise);
float4 _PunchyWeatherRect;     // xy world origin of the maps, zw 1 / world size
half3  _PunchyDustColor;
float  _PunchyHazeDensity;     // base exponential-squared haze per metre (hides the edge of the streamed terrain)

float3 _PunchySunDirection;    // towards the sun
float3 _PunchyMoonDirection;   // towards the moon
half3  _PunchySunColor;        // sun colour × brightness (0 once set)
half3  _PunchySkyZenith;
half3  _PunchySkyHorizon;      // also the fog colour, so distant terrain fades seamlessly into the sky
half3  _PunchySkyGround;
half4  _PunchyClouds;          // x coverage at the camera (dims the sun's glow), z noise scale
float2 _PunchyCloudOffset;     // cloud drift with the wind (× CLOUD_FEATURE = metres)
half   _PunchyNight;           // 0 day … 1 night
float4 _PunchyLightning;       // xyz towards the strike, w flash brightness as seen from the camera
float4 _PunchyLightningPos;    // xyz strike position in the cloud, w raw flash brightness

float2 PunchyMapUV(float2 worldXZ) { return (worldXZ - _PunchyWeatherRect.xy) * _PunchyWeatherRect.zw; }
half4 PunchyWeatherAt(float2 xz) { return SAMPLE_TEXTURE2D_LOD(_PunchyWeatherMap, sampler_PunchyWeatherMap, PunchyMapUV(xz), 0); }
half2 PunchyWetnessAt(float2 xz) { return SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, PunchyMapUV(xz), 0).rg; }

// Cloud shape at a world position: base (m), thickness (m), anvil strength (0..1), anvil top (m).
float4 PunchyCloudShapeAt(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_PunchyCloudMap, sampler_PunchyCloudMap, PunchyMapUV(xz), 0) * float4(4000, 9000, 1, 12000);
}

#define PUNCHY_ANVIL_DEPTH 900.0

bool PunchyInCloudLayer(float y, float4 shape)
{
    return (y > shape.x && y < shape.x + shape.y) ||
           (shape.z > 0.02 && y > shape.w - PUNCHY_ANVIL_DEPTH && y < shape.w);
}

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

float PunchyRemap(float v, float a, float b) { return saturate((v - a) / max(b - a, 1e-4)); }

// ------------------------------------------------------------------------------------------------ volumetric clouds

// Cloud density at a point, given the weather and cloud shape there. detail (0..1) scales the edge-eroding detail
// noise: 0 for lighting and shadows (only the broad mass matters) and for distant clouds, where fine detail would
// only shimmer.
float PunchyCloudDensity(float3 p, half4 weather, float4 shape, float detail)
{
    if (!PunchyInCloudLayer(p.y, shape))
        return 0;
    float h = saturate((p.y - shape.x) / max(shape.y, 1));

    float3 drift = float3(_PunchyCloudOffset.x, 0, _PunchyCloudOffset.y) * PUNCHY_CLOUD_FEATURE;
    float3 uvw = (p - drift) / PUNCHY_CLOUD_NOISE_SCALE;
    uvw.xz += h * 0.06;   // tops lean downwind a little
    float shapeNoise = SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvw, 0).r;
    float coverage = saturate(weather.r * 1.05);

    // The cloud body: flat-ish base, rounded top; tall cumulonimbus towers narrow slightly as they climb and are
    // much denser (a solid column rather than a veil).
    float density = 0;
    if (p.y > shape.x && p.y < shape.x + shape.y)
    {
        float tall = saturate((shape.y - 2500) / 3500);
        float profile = saturate(h * 8) * saturate((1 - h) * 3);
        profile = lerp(profile, profile * (1 - 0.3 * h), tall);
        density = PunchyRemap(shapeNoise, 1 - coverage, 1) * profile * (1 + 2 * tall);
    }

    // Storm anvils: a flat, spreading layer just below the tower top, reaching well beyond the tower.
    float anvilDepth = (shape.w - p.y) / PUNCHY_ANVIL_DEPTH;   // 0 at the top … 1 at the bottom of the anvil
    if (shape.z > 0.02 && anvilDepth > 0 && anvilDepth < 1)
    {
        float anvilProfile = saturate(anvilDepth * 6) * saturate((1 - anvilDepth) * 2.5);
        density = max(density, PunchyRemap(shapeNoise, 1 - shape.z * 0.95, 1) * anvilProfile * 1.1);
        coverage = max(coverage, shape.z);
    }

    if (density <= 0)
        return 0;
    if (detail > 0.01)
    {
        // Erode the edges into cauliflower-like billows; thin, sparse cloud is eroded less so it doesn't break into specks.
        float erosion = SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvw * 3.7, 0).g;
        density = PunchyRemap(density, erosion * lerp(0.15, 0.45, coverage) * detail, 1);
    }
    // Storm clouds are denser (and therefore darker underneath).
    return density * (1.0 + 1.5 * weather.g);
}

float PunchyCloudDensityAt(float3 p, float detail)
{
    return PunchyCloudDensity(p, PunchyWeatherAt(p.xz), PunchyCloudShapeAt(p.xz), detail);
}

float PunchyPhase(float cosTheta, float g)
{
    float g2 = g * g;
    return (1 - g2) / pow(max(1 + g2 - 2 * g * cosTheta, 1e-4), 1.5);   // Henyey–Greenstein × 4π (1 = isotropic)
}

void PunchyCloudLight(out float3 direction, out half3 color)
{
    // The sun by day, the moon by night.
    bool sunUp = _PunchySunDirection.y > -0.05;
    direction = sunUp ? _PunchySunDirection : _PunchyMoonDirection;
    color = sunUp ? _PunchySunColor : half3(0.16, 0.19, 0.27) * _PunchyNight;
}

// Marches the view ray through the cloud volume and composites the clouds over `background`.
half3 PunchyVolumetricClouds(half3 background, float3 dir, float2 screenPos)
{
    const float bottom = 300, top = 9500;
    float3 camera = _WorldSpaceCameraPos;
    if (dir.y <= 0.003)
        return background;
    float t0 = max(0, (bottom - camera.y) / dir.y);
    float t1 = min((top - camera.y) / dir.y, 40000);
    if (t0 >= t1)
        return background;

    // Steps grow with distance (~3% of it): fine near the camera, coarse far away where clouds are small on screen.
    // A little per-pixel jitter hides the remaining banding.
    const int maxSteps = 160;
    // (A sine hash: decorrelated on whole-number pixel coordinates, where PunchyHash would show a grid.)
    float jitter = frac(sin(dot(screenPos, float2(12.9898, 78.233))) * 43758.5453);
    float t = t0 + clamp(t0 * 0.02, 30, 500) * jitter;

    float3 lightDir;
    half3 lightColor;
    PunchyCloudLight(lightDir, lightColor);
    float cosTheta = dot(dir, lightDir);
    float phase = max(lerp(PunchyPhase(cosTheta, -0.2), PunchyPhase(cosTheta, 0.65), 0.5), 0.35);

    float transmit = 1;
    half3 scatter = 0;
    float firstHit = -1;
    [loop]
    for (int i = 0; i < maxSteps && t < t1; i++)
    {
        float stepLength = clamp(t * 0.02, 30, 500);
        float3 p = camera + dir * t;
        half4 weather = PunchyWeatherAt(p.xz);
        float4 shape = PunchyCloudShapeAt(p.xz);
        float detail = saturate(1.2 - t / 20000);
        float density = ((weather.r > 0.03 || shape.z > 0.02) && PunchyInCloudLayer(p.y, shape))
            ? PunchyCloudDensity(p, weather, shape, detail) : 0;

        if (density > 0.004)
        {
            if (firstHit < 0)
                firstHit = t;

            // Light reaching this point: how much cloud lies between it and the sun (or moon).
            float opticalDepth = 0;
            [unroll]
            for (int j = 1; j <= 4; j++)
            {
                float3 q = p + lightDir * (j * j * 90.0);
                opticalDepth += PunchyCloudDensityAt(q, 0) * (2 * j - 1) * 90.0;
            }
            float beer = exp(-opticalDepth * PUNCHY_CLOUD_SIGMA);
            float powder = 1 - exp(-density * 6);   // edges facing the light stay a touch darker
            // Sunlight is far brighter than skylight: sunlit tops glare white, shaded bellies go grey.
            half3 direct = lightColor * beer * phase * (0.55 + 0.45 * powder) * 3.2;

            float height = saturate((p.y - shape.x) / max(shape.y, 1));
            half3 ambient = lerp(_PunchySkyHorizon * 0.32, _PunchySkyZenith * 0.75 + _PunchySkyHorizon * 0.25, height)
                          * (1 - 0.6 * weather.g);
            half3 flash = half3(0.75, 0.82, 1.0) * _PunchyLightningPos.w * 2.5
                        * exp(-distance(p, _PunchyLightningPos.xyz) / 1200);

            float absorbed = 1 - exp(-density * PUNCHY_CLOUD_SIGMA * stepLength);
            scatter += transmit * absorbed * (direct + ambient + flash);
            transmit *= 1 - absorbed;
            if (transmit < 0.02)
                break;
        }
        t += stepLength;
    }

    // Distant clouds fade into the horizon haze, and right at the horizon (where rays skim through kilometres of
    // cloud layer and alias) they thin out entirely.
    if (firstHit > 0)
        scatter = lerp(scatter, _PunchySkyHorizon * (1 - transmit), 1 - exp(-firstHit / 14000));
    float horizonFade = smoothstep(0.003, 0.05, dir.y);
    transmit = lerp(1, transmit, horizonFade);
    scatter *= horizonFade;
    return background * transmit + scatter;
}

// Cheap stand-in for the volumetric clouds (reflections): one density sample at the cloud layer's middle.
half PunchyCloudCoverAlong(float3 dir, out half darkness)
{
    darkness = 0;
    float3 camera = _WorldSpaceCameraPos;
    if (dir.y <= 0.02)
        return 0;
    float2 guess = camera.xz + dir.xz * min((1500 - camera.y) / dir.y, 40000);
    float4 shape = PunchyCloudShapeAt(guess);
    float middle = shape.x + shape.y * 0.35;
    float3 p = camera + dir * min((middle - camera.y) / dir.y, 40000);
    half4 weather = PunchyWeatherAt(p.xz);
    darkness = weather.g;
    return saturate(PunchyCloudDensity(p, weather, PunchyCloudShapeAt(p.xz), 0) * 3) * smoothstep(0.02, 0.12, dir.y);
}

// 0..1: how much a point on the ground is shaded from the sun by the clouds above it (one sample at the clouds'
// lower middle, along the sun direction — matches the volumetric clouds' shapes).
half PunchyCloudShadow(float3 positionWS)
{
    float3 toSun = _PunchySunDirection;
    if (toSun.y < 0.05)
        return 0;
    float4 shape = PunchyCloudShapeAt(positionWS.xz);
    float height = shape.x + shape.y * 0.3;
    float3 p = float3(positionWS.xz + toSun.xz * (height - positionWS.y) / toSun.y, height).xzy;
    half4 weather = PunchyWeatherAt(p.xz);
    float density = PunchyCloudDensity(p, weather, PunchyCloudShapeAt(p.xz), 0);
    return saturate(density * 3) * lerp(0.5, 0.95, saturate(shape.y / 3000));
}

// ------------------------------------------------------------------------------------------------ cirrus, sky

// Thin, wispy high cloud, stretched along the wind.
half PunchyCirrus(float3 dir)
{
    if (dir.y <= 0.01)
        return 0;
    float3 camera = _WorldSpaceCameraPos;
    float2 hit = camera.xz + dir.xz * min((PUNCHY_CIRRUS_HEIGHT - camera.y) / dir.y, 80000);
    float2 wind = normalize(_PunchyCloudOffset + 1e-4);
    float2 uv = (hit - _PunchyCloudOffset * PUNCHY_CLOUD_FEATURE * 2) / 9000;
    uv = float2(dot(uv, wind), dot(uv, float2(-wind.y, wind.x)) * 4);   // streaks along the wind
    half amount = saturate(PunchyWeatherAt(hit).r * 0.6 + 0.2);
    float n = PunchyFbm(uv, 4);
    return smoothstep(1 - amount * 0.55, 1.05, n) * 0.55 * smoothstep(0.01, 0.15, dir.y);
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

// The sky without the volumetric clouds: gradient, sun and moon, stars, cirrus, horizon haze. With
// `approximateClouds` (reflections) it adds a cheap one-sample version of the clouds instead.
half3 PunchySkyColor(float3 dir, bool withDiscs, bool approximateClouds)
{
    dir = normalize(dir);
    float h = dir.y;
    half3 sky = lerp(_PunchySkyHorizon, _PunchySkyZenith, pow(saturate(h), 0.45));
    sky = h < 0 ? lerp(_PunchySkyHorizon, _PunchySkyGround, saturate(-h * 3)) : sky;

    float sunDot = saturate(dot(dir, _PunchySunDirection));
    float moonDot = saturate(dot(dir, _PunchyMoonDirection));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    sky += _PunchySunColor * pow(sunDot, 12) * 0.25 * clearness;

    if (h > 0)
    {
        half3 night = PunchyStars(dir) * _PunchyNight;
        if (withDiscs)
        {
            night += half3(0.9, 0.92, 1.0) * smoothstep(0.99982, 0.99987, moonDot) * 1.6 * _PunchyNight;
            sky += _PunchySunColor * smoothstep(0.9994, 0.9997, sunDot) * 12 * clearness;
        }
        sky += half3(0.35, 0.42, 0.6) * pow(moonDot, 40) * 0.25 * _PunchyNight + night;

        half daylight = saturate(dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2)) * 1.6);
        half3 cirrusColor = (half3(1, 1, 1) * daylight + _PunchySunColor * pow(sunDot, 4) * 0.5) * 0.95;
        sky = lerp(sky, cirrusColor, PunchyCirrus(dir));

        if (approximateClouds)
        {
            half darkness;
            half cover = PunchyCloudCoverAlong(dir, darkness);
            half3 cloudColor = lerp(half3(0.9, 0.92, 0.95), half3(0.25, 0.26, 0.29), darkness) * daylight
                             + _PunchySunColor * pow(sunDot, 6) * 0.3 * (1 - darkness);
            sky = lerp(sky, cloudColor, cover);
        }
    }
    float lightningGlow = _PunchyLightning.w * (0.1 + 0.5 * pow(saturate(dot(dir, _PunchyLightning.xyz)), 3));
    sky += half3(0.5, 0.55, 0.7) * lightningGlow * 0.3;

    // Haze: the horizon band is exactly the fog colour.
    return lerp(sky, _PunchySkyHorizon, exp(-max(h, 0) * 10) * 0.85);
}

// ------------------------------------------------------------------------------------------------ atmosphere

// Dust and rain haze along a ray, read from the weather map: a distant sandstorm is a tan wall, rain hangs in
// grey curtains below the local cloud base. Steps are packed towards the camera, where detail matters most.
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
        float cloudBase = PunchyCloudShapeAt(p.xz).x;
        // The shaft is densest in the middle of the cell and frays at its edges.
        float rain = w.b * w.b * PUNCHY_RAIN_DENSITY * 1.4 * saturate((cloudBase - p.y) / 120) * (0.3 + 1.4 * streaks);
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

#endif
