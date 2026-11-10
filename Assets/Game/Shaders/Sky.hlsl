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
#define PUNCHY_DUST_DENSITY 0.028     // extinction per metre in a full sandstorm (~100 m visibility)
#define PUNCHY_DUST_TOP 350.0         // dust thins out above this height
#define PUNCHY_RAIN_DENSITY 0.0016    // extinction per metre in full rain (~1.5 km visibility)

TEXTURE2D(_PunchyWeatherMap);  SAMPLER(sampler_PunchyWeatherMap);
TEXTURE2D(_PunchyWetnessMap);  SAMPLER(sampler_PunchyWetnessMap);
TEXTURE2D(_PunchyCloudMap);    SAMPLER(sampler_PunchyCloudMap);
TEXTURE3D(_PunchyCloudNoise);  SAMPLER(sampler_PunchyCloudNoise);
float4 _PunchyWeatherRect;     // xy world origin of the maps, zw 1 / world size
half3  _PunchyDustColor;
float  _PunchyHazeDensity;     // base exponential-squared haze per metre (hides the edge of the streamed terrain)
float4 _PunchyUnderwater;       // P-20: under water, rgb the water's fog colour and w its density per metre; 0 when dry (Underwater.cs)
float4 _PunchyUnderwaterSurface; // under water, x the surface over the lens (m) and y the lens's depth under it; 0 when dry (Underwater.cs)

float3 _PunchySunDirection;    // towards the sun
float3 _PunchyMoonDirection;   // towards the moon
float _PunchySkyOff;           // test only (-skyoff n, to price the sky's parts): 1 no cloud march, 2 no far clouds, 4 no sky colour, 8 no weather volumes, 16 old even steps, 32 steps-per-pixel overlay, 64 always four sun samples, 128 cloud edges before R-4, 256 cloud light before R-4, 512 one cloud type everywhere (before R-4)
float4 _PunchyMoonPhase;       // phase (0 new, 0.5 full), lit share
float3 _PunchyBlueMoon;        // towards the second, blue moon
static const float PunchyBlueMoonRadius = 0.22;    // radians (about 25 degrees across): 6.5x its v0.11.0 size, a sister planet (Zach, #34)
half3  _PunchySunColor;        // sun colour × brightness (0 once set)
half3  _PunchySkyZenith;
half3  _PunchySkyHorizon;      // also the fog colour, so distant terrain fades seamlessly into the sky
half3  _PunchySkyGround;
half4  _PunchyClouds;          // x coverage at the camera (dims the sun's glow), z noise scale
float2 _PunchyCloudOffset;     // cloud drift with the wind (× CLOUD_FEATURE = metres)
half   _PunchyNight;           // 0 day … 1 night
float4 _PunchyLightning;       // xyz towards the strike, w flash brightness as seen from the camera
float4 _PunchyLightningPos;    // xyz strike position in the cloud, w raw flash brightness

half   _PunchyAurora;           // 0..1: how cold the lands round the camera are (SkyEventsSystem); auroras at night
half   _PunchySkyTest;          // checking only: 1 makes every night a meteor shower
float4 _PunchyClimate;         // map-wide average: coverage, darkness, cloud base / 4000, thickness / 9000

float2 PunchyMapUV(float2 worldXZ) { return (worldXZ - _PunchyWeatherRect.xy) * _PunchyWeatherRect.zw; }

// 1 well inside the weather maps, fading to 0 over their outer 15%. Beyond the maps the weather is the generic
// map-wide average, rather than the edge pixels smeared out to the horizon.
float PunchyMapInside(float2 uv)
{
    float2 edge = min(uv, 1 - uv);
    return smoothstep(0.0, 0.15, min(edge.x, edge.y));
}

half4 PunchyWeatherAt(float2 xz)
{
    float2 uv = PunchyMapUV(xz);
    half4 local = SAMPLE_TEXTURE2D_LOD(_PunchyWeatherMap, sampler_PunchyWeatherMap, uv, 0);
    return lerp(half4(_PunchyClimate.xy, 0, 0), local, PunchyMapInside(uv));
}

half2 PunchyWetnessAt(float2 xz)
{
    float2 uv = PunchyMapUV(xz);
    return SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, uv, 0).rg * PunchyMapInside(uv);
}

// Cloud shape at a world position: base (m), thickness (m), anvil strength (0..1), anvil top (m).
float4 PunchyCloudShapeAt(float2 xz)
{
    float2 uv = PunchyMapUV(xz);
    float4 local = SAMPLE_TEXTURE2D_LOD(_PunchyCloudMap, sampler_PunchyCloudMap, uv, 0);
    return lerp(float4(_PunchyClimate.zw, 0, 0), local, PunchyMapInside(uv)) * float4(4000, 9000, 1, 12000);
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

// Integer hash of a lattice cell → 0..1. (Float-based hashes lose precision thousands of metres from the origin and
// degrade into visible grid patterns; integer mixing doesn't.)
float PunchyCellHash(int2 cell)
{
    uint h = asuint(cell.x) * 0x8da6b343u ^ asuint(cell.y) * 0xd8163841u;
    h ^= h >> 13;
    h *= 0x85ebca6bu;
    h ^= h >> 16;
    return h * (1.0 / 4294967296.0);
}

float PunchyValueNoise(float2 p)
{
    float2 fl = floor(p);
    int2 i = int2(fl);
    float2 f = p - fl;
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(PunchyCellHash(i), PunchyCellHash(i + int2(1, 0)), u.x),
                lerp(PunchyCellHash(i + int2(0, 1)), PunchyCellHash(i + int2(1, 1)), u.x), u.y);
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

// Cloud type (R-4, from data: each weather preset's layering and high cloud, nudged by the biomes' humidity; the
// wetness map's b and a): x 0 heaped cumulus ... 1 a flat stratus sheet, y wispy high cloud. The density below reads
// punchyCloudStyle; the march sets it once per step, so the light samples around a point share its type, and the other
// callers set it themselves. _PunchySkyOff 512 shows one type everywhere, as before R-4.
static float2 punchyCloudStyle = float2(0, 0.3);

float2 PunchyCloudStyleAt(float2 xz)
{
    if (((uint)_PunchySkyOff & 512u) != 0)
        return float2(0, 0.3);
    float2 uv = PunchyMapUV(xz);
    float2 local = SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, uv, 0).ba;
    return lerp(float2(0, 0.3), local, PunchyMapInside(uv));
}

// Cloud density at a point, given the weather and cloud shape there. detail (0..1) scales the edge-eroding detail
// noise: 0 for lighting and shadows (only the broad mass matters) and for distant clouds, where fine detail would
// only shimmer. fine (0..1) adds a second, smaller octave of it, for clouds near the camera only.
float PunchyCloudDensityFine(float3 p, half4 weather, float4 shape, float detail, float fine)
{
    if (!PunchyInCloudLayer(p.y, shape))
        return 0;
    float h = saturate((p.y - shape.x) / max(shape.y, 1));

    float3 drift = float3(_PunchyCloudOffset.x, 0, _PunchyCloudOffset.y) * PUNCHY_CLOUD_FEATURE;
    float3 q = p - drift;
    // Sample the tiling noise off the world axes and at two unrelated scales, so its repeat never lines up into
    // a visible grid (the combined pattern only repeats after hundreds of kilometres).
    float3 uvw = float3(q.x * 0.8 - q.z * 0.6, q.y, q.x * 0.6 + q.z * 0.8) / PUNCHY_CLOUD_NOISE_SCALE;
    uvw.xz += h * 0.06;   // tops lean downwind a little
    float3 uvwLarge = float3(q.x * 0.28 + q.z * 0.96, q.y * 0.6, -q.x * 0.96 + q.z * 0.28) / (PUNCHY_CLOUD_NOISE_SCALE * 2.73) + 0.37;
    float layering = punchyCloudStyle.x;
    // Stratus leans on the large noise, so it spreads into broad, even sheets rather than separate heaps.
    float shapeNoise = lerp(SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvw, 0).r,
                            SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvwLarge, 0).r, lerp(0.4, 0.8, layering));
    float coverage = saturate(weather.r * 1.05 + 0.2 * layering);

    // The cloud body: flat-ish base, rounded top; tall cumulonimbus towers narrow slightly as they climb and are
    // much denser (a solid column rather than a veil).
    float density = 0;
    if (p.y > shape.x && p.y < shape.x + shape.y)
    {
        float tall = saturate((shape.y - 2500) / 3500);
        float profile = saturate(h * 8) * saturate((1 - h) * 3);
        profile = lerp(profile, profile * (1 - 0.3 * h), tall);
        // Stratus: a thin, flat slab in the lower part of the layer, a little denser so the sheet reads grey and even.
        float slabH = h / lerp(1, 0.35, layering);
        profile = lerp(profile, saturate(slabH * 10) * saturate((1 - slabH) * 5), layering);
        density = PunchyRemap(shapeNoise, 1 - coverage, 1) * profile * (1 + 2 * tall) * (1 + 0.3 * layering);
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
        float4 erosion = SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvw * 3.7, 0);
        [branch] if (((uint)_PunchySkyOff & 128u) != 0)
        {
            // (Test only, -skyoff 128: the edges before R-4, one billow noise everywhere.)
            density = PunchyRemap(density, erosion.g * lerp(0.15, 0.45, coverage) * detail, 1);
        }
        else
        {
            // Erode the edges (R-4): cauliflower billows (Worley) on the tops, frayed wisps (Perlin) on the bases and on
            // thin veils. Near the camera a second, smaller octave joins in; it fades out by 6 km, where it would only
            // shimmer on the half-resolution buffer.
            float billowy = saturate(h * 2.5 + 0.2) * saturate(0.4 + coverage) * (1 - layering);   // stratus frays, never billows
            float carve = lerp(erosion.b, erosion.g, billowy);
            [branch] if (fine > 0.01)
            {
                float3 uvwFine = float3(uvw.x * 0.6 - uvw.z * 0.8, uvw.y * 1.3, uvw.x * 0.8 + uvw.z * 0.6) * 11.3 + 0.21;
                float2 small = SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, uvwFine, 0).gb;
                carve = lerp(carve, carve * 0.6 + lerp(small.y, small.x, billowy) * 0.4, fine);
            }
            // Strongest at the edges, where the body is thin; a dense core keeps its mass. Thin, sparse cloud is eroded
            // less overall so it doesn't break into specks, and so is the flat base (holes there would show the sunlit
            // inside as bright specks).
            float edge = 1 - saturate(density * 1.4);
            carve = saturate((carve - 0.3) * 2.2);   // the summed octaves sit mid-grey: stretch them so the edge really breaks up
            density = PunchyRemap(density, carve * lerp(0.25, 0.6, coverage) * lerp(0.45, 1.25, edge) * lerp(0.4, 1, saturate(h * 5)) * detail, 1);
        }
    }
    // Storm clouds are denser (and therefore darker underneath).
    return density * (1.0 + 1.5 * weather.g);
}

float PunchyCloudDensity(float3 p, half4 weather, float4 shape, float detail)
{
    return PunchyCloudDensityFine(p, weather, shape, detail, 0);
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

// ------------------------------------------------------------------------------------------------ twilight
// Sunrise and sunset (W-31), all keyed by the sun's elevation in degrees: a warm band low on the sun's side of the
// sky, violet on the far side and overhead, a bright glow round the sun, warm-lit clouds, then blue hour. Weather
// mutes it through _PunchyTwilightDull (0 clear … 1 overcast/storm; unset reads as clear).
half _PunchyTwilightDull;

float PunchySunElevation() { return degrees(asin(clamp(_PunchySunDirection.y, -1.0, 1.0))); }

// The warm colour of the low sun: gold at +12°, orange at +5°, red-orange at 0°, pink-red at -4°, violet by -8°.
half3 PunchyTwilightColor(float e)
{
    half3 c = lerp(half3(0.42, 0.26, 0.55), half3(0.96, 0.38, 0.46), saturate((e + 8) / 4));
    c = lerp(c, half3(1.00, 0.32, 0.10), saturate((e + 4) / 4));   // deeper red-orange (W-33)
    c = lerp(c, half3(1.00, 0.46, 0.16), saturate(e / 5));
    return lerp(c, half3(1.00, 0.76, 0.46), saturate((e - 5) / 7));
}

// How much twilight shows: none above +12° or below -8°, full from about -3° to +4°, less under cloud.
half PunchyTwilightAmount(float e)
{
    return smoothstep(-8, -3, e) * (1 - smoothstep(4, 12, e)) * (1 - 0.8 * _PunchyTwilightDull);
}

// How bright the twilight light is: full while the sun is up, dimming to a glow as it sinks towards -8°.
half PunchyTwilightLight(float e) { return lerp(0.1, 1, smoothstep(-8, 1, e)); }

// 0 looking away from the sun … 1 looking towards it, round the horizon.
half PunchySunSide(float3 dir)
{
    float2 s = _PunchySunDirection.xz, d = dir.xz;
    return saturate(dot(d, s) * rsqrt(max(dot(d, d) * dot(s, s), 1e-8)) * 0.5 + 0.5);
}

// The blue moon's light as the sky sees it (WeatherSystem; linear, 0 when it's down, new or by day): feedback 11-08
// item 35, so the haze and the clouds read as lit by it, not only the ground.
half3 _PunchyBlueMoonGlow;

// The blue moon's light scattered in the low air towards `dir`: a little all round, much more on its side of the sky.
half3 PunchyBlueMoonHaze(float3 dir)
{
    half toward = saturate(dot(normalize(dir), _PunchyBlueMoon) * 0.5 + 0.5);
    toward *= toward;
    return _PunchyBlueMoonGlow * (0.007 + 0.04 * toward * toward);
}

// The horizon colour in direction `dir` with twilight: warm on the sun's side, grey-violet on the far side. Also
// the haze colour, so land and sea fade into the same band. Under a blue moon it glows blue towards it.
half3 PunchyTwilightHorizon(float3 dir)
{
    half3 horizon = _PunchySkyHorizon + PunchyBlueMoonHaze(dir);
    float e = PunchySunElevation();
    half amount = PunchyTwilightAmount(e);
    if (amount <= 0)
        return horizon;
    half toward = PunchySunSide(dir);
    toward *= toward;
    half lum = dot(horizon, half3(0.3, 0.5, 0.2));
    half3 warm = PunchyTwilightColor(e) * PunchyTwilightLight(e) * 1.05;
    half3 cool = half3(0.62, 0.55, 0.9) * lum;
    return lerp(horizon, lerp(cool, warm, toward), amount * lerp(0.65, 1, toward));
}

// The zenith with twilight: a deeper blue with a touch of purple.
half3 PunchyTwilightZenith()
{
    half amount = PunchyTwilightAmount(PunchySunElevation());
    return lerp(_PunchySkyZenith, _PunchySkyZenith * half3(0.95, 0.72, 1.05) + half3(0.03, 0.0, 0.05), amount);
}

// How strongly clouds glow at sunset (W-33, Zach wanted much more glow): from +12° down to blue hour, and weather
// mutes it by at most half (the sky band is muted by up to 80%).
half PunchyCloudGlowAmount(float e)
{
    return smoothstep(-8, -3, e) * (1 - smoothstep(9, 13, e)) * (1 - 0.5 * _PunchyTwilightDull);
}

// Twilight light on a cloud at height y: rich gold, orange or red at about 2.2x the W-31 strength, and after sunset
// only the high clouds still catch it (low ones lose it at once, the highest about 9° later).
half3 PunchyTwilightCloudLight(float e, float y)
{
    half reach = lerp(0.8, 9.0, saturate((y - 800) / 6500));
    half caught = e >= 0 ? 1 : saturate(1 + e / reach);
    half3 c = PunchyTwilightColor(e);
    c = saturate(lerp(dot(c, half3(0.3, 0.5, 0.2)).xxx, c, 1.35));
    return c * PunchyTwilightLight(e) * caught * 2.2;
}

void PunchyCloudLight(out float3 direction, out half3 color)
{
    // The sun by day, the moon by night; through twilight (down to -8°) the sun still lights the high clouds.
    bool sunUp = _PunchySunDirection.y > -0.14;
    direction = sunUp ? _PunchySunDirection : _PunchyMoonDirection;
    // The moon lights the clouds by its phase: a new moon leaves them a dim grey (Zach, 11-08).
    half3 moonlight = half3(0.16, 0.19, 0.27) * _PunchyNight * lerp(0.15, 1, _PunchyMoonPhase.y);
    color = sunUp ? _PunchySunColor : moonlight;
    // Moonlight takes over gradually through blue hour, so the switch at -8° doesn't pop.
    if (sunUp)
        color += moonlight * (1 - smoothstep(-0.14, -0.05, _PunchySunDirection.y));
    // The blue moon lights the clouds too (item 35), shaded along the main light's path (its own would cost a second
    // march): its glow is zero by day, so this only shows at night.
    color += _PunchyBlueMoonGlow * 0.09;
}

#define PUNCHY_FAR_START 15000.0
#define PUNCHY_FAR_END 22000.0

// The generic far cloud layer: a flat sheet at the average cloud height with the average coverage, shown only
// where the view ray reaches it beyond PUNCHY_FAR_START (it fades in as the volumetric clouds fade out).
half3 PunchyFarClouds(half3 background, float3 dir)
{
    float3 camera = _WorldSpaceCameraPos;
    float height = _PunchyClimate.z * 4000 + _PunchyClimate.w * 9000 * 0.4;
    if (dir.y <= 0.002 || camera.y >= height)
        return background;
    float distance = (height - camera.y) / dir.y;
    float fadeIn = smoothstep(PUNCHY_FAR_START, PUNCHY_FAR_END, distance);
    if (fadeIn <= 0)
        return background;

    float2 hit = camera.xz + dir.xz * min(distance, 120000) - _PunchyCloudOffset * PUNCHY_CLOUD_FEATURE;
    float2 uv = float2(hit.x * 0.8 - hit.y * 0.6, hit.x * 0.6 + hit.y * 0.8) / 3500;   // off-axis, no grid
    float threshold = lerp(0.72, 0.26, _PunchyClimate.x);
    float cover = smoothstep(threshold, threshold + 0.22, PunchyFbm(uv, 4)) * fadeIn;

    half daylight = saturate(dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2)) * 1.6);
    half3 color = lerp(half3(0.92, 0.93, 0.96), half3(0.3, 0.31, 0.34), _PunchyClimate.y) * daylight
                + _PunchySunColor * pow(saturate(dot(dir, _PunchySunDirection)), 6) * 0.3;
    // Twilight: warm on the sun's side, grey-violet away from it.
    float e = PunchySunElevation();
    half twilight = PunchyTwilightAmount(e);
    half3 lum = dot(color, half3(0.3, 0.5, 0.2));
    color = lerp(color, lerp(lum * half3(0.8, 0.72, 1.05), PunchyTwilightCloudLight(e, height) * 0.9, PunchySunSide(dir)),
                 twilight * (1 - 0.6 * _PunchyClimate.y));
    // Very distant: mostly horizon haze.
    color = lerp(color, PunchyTwilightHorizon(dir), saturate(distance / 60000));
    return lerp(background, color, cover);
}

// Marches the view ray through the cloud volume: the light scattered towards the camera, and the fraction of what's behind that
// still shows through. framePhase (0..1, per frame) varies the start jitter between frames for the low-resolution buffer.
void PunchyCloudMarch(float3 dir, float2 screenPos, float framePhase, out half3 scatterOut, out float transmitOut)
{
    scatterOut = 0;
    transmitOut = 1;
    const float bottom = 300, top = 9500;
    float3 camera = _WorldSpaceCameraPos;
    if (dir.y <= 0.003)
        return;
    float t0 = max(0, (bottom - camera.y) / dir.y);
    float t1 = min((top - camera.y) / dir.y, PUNCHY_FAR_END);
    if (t0 >= t1)
        return;

    // Steps grow with distance (~3% of it): fine near the camera, coarse far away where clouds are small on screen.
    // A little per-pixel jitter hides the remaining banding.
    const int maxSteps = 160;
    // (A sine hash: decorrelated on whole-number pixel coordinates, where PunchyHash would show a grid.)
    float jitter = frac(frac(sin(dot(screenPos, float2(12.9898, 78.233))) * 43758.5453) + framePhase);
    float t = t0 + clamp(t0 * 0.02, 30, 500) * jitter;

    float3 lightDir;
    half3 lightColor;
    PunchyCloudLight(lightDir, lightColor);
    float cosTheta = dot(dir, lightDir);
    float phase = max(lerp(PunchyPhase(cosTheta, -0.2), PunchyPhase(cosTheta, 0.65), 0.5), 0.35);
    // Twilight: the low sun's warm light (strength by height, below), and cool grey-violet skylight on shaded sides.
    float sunElevation = PunchySunElevation();
    half twilight = PunchyTwilightAmount(sunElevation);
    half3 cloudHorizon = PunchyTwilightHorizon(dir);
    // Sunset glow: the warm light itself (in the loop), plus a silver-gold lining on thin edges near the sun.
    half glow = PunchyCloudGlowAmount(sunElevation);
    half3 lining = lerp(half3(1, 1, 1), PunchyTwilightColor(sunElevation), 0.5) * PunchyTwilightLight(sunElevation)
                 * pow(saturate(cosTheta), 10) * 4 * glow;

    float transmit = 1;
    half3 scatter = 0;
    float firstHit = -1;
    // Empty air (job 32: the sky was 15 ms of the frame even in clear weather, nearly all of it steps through nothing):
    // where a step can hold no cloud (no cover over this column, or outside its layer) the march takes long strides of up
    // to MaxEmptyStride metres, straight towards the layer when it's under the cloud base; inside the layer, where the
    // samples keep coming back empty, it lengthens its step more cautiously (MaxThinStride, so a small cloud can't be
    // stepped over). Either way, on reaching cloud it backs up and walks in at the fine step.
    const float MaxEmptyStride = 800, MaxThinStride = 240, MaxThinScale = 2.5;
    float lastEmpty = t;
    int emptyRun = 0;
    bool strode = false, farStrode = false;   // the last step was a thin (in the layer) or an empty-air stride
    bool stride = ((uint)_PunchySkyOff & 16u) == 0;
    bool allSunSamples = ((uint)_PunchySkyOff & 64u) != 0;
    bool oldLight = ((uint)_PunchySkyOff & 256u) != 0;
    int stepsTaken = 0;
    float lastOpticalDepth = -1;   // the light samples' last result, reused where a step's light hardly shows
    [loop]
    for (int i = 0; i < maxSteps && t < t1; i++)
    {
        stepsTaken = i + 1;
        float stepLength = clamp(t * 0.02, 30, 500);
        float3 p = camera + dir * t;
        half4 weather = PunchyWeatherAt(p.xz);
        float4 shape = PunchyCloudShapeAt(p.xz);
        bool canHold = (weather.r > 0.03 || shape.z > 0.02) && PunchyInCloudLayer(p.y, shape);
        [branch] if (stride && !canHold)
        {
            // Under this column's base, aim for it (a little short: the base differs from column to column).
            float toBase = p.y < shape.x && dir.y > 0 ? (shape.x - p.y) / dir.y * 0.8 : 0;
            lastEmpty = t;
            emptyRun++;
            farStrode = true;
            // (Varied per pixel by the same jitter as the start, or neighbouring rows land alike and streak the cloud edges.)
            t += max(stepLength, clamp(max(toBase, stepLength * emptyRun), stepLength, MaxEmptyStride) * (0.75 + 0.5 * jitter));
            continue;
        }
        [branch] if (stride && farStrode)
        {
            // Came out of empty air into a step that could hold cloud: back up and come in at the fine step.
            farStrode = false;
            emptyRun = 0;
            if (t - lastEmpty > stepLength * 1.5)
            {
                t = lastEmpty + stepLength;
                continue;
            }
        }
        // Edge detail fades with distance (R-4: full to 3.5 km, gone by 17.5 km; the smaller octave from 2.5 to 6 km),
        // so far clouds don't shimmer.
        bool oldEdges = ((uint)_PunchySkyOff & 128u) != 0;
        float detail = oldEdges ? saturate(1.2 - t / 20000) : saturate(1.25 - t / 14000);
        float fine = oldEdges ? 0 : 1 - smoothstep(2500, 6000, t);
        [branch] if (canHold)
            punchyCloudStyle = PunchyCloudStyleAt(p.xz);
        float density = ((weather.r > 0.03 || shape.z > 0.02) && PunchyInCloudLayer(p.y, shape))
            ? PunchyCloudDensityFine(p, weather, shape, detail, fine) * (1 - smoothstep(PUNCHY_FAR_START, PUNCHY_FAR_END, t)) : 0;

        [branch] if (stride && density <= 0.004)
        {
            // Inside the layer but nothing here: lengthen the step a little after a few empty samples.
            lastEmpty = t;
            emptyRun++;
            strode = emptyRun > 3;
            t += strode ? max(stepLength, min(stepLength * min((emptyRun - 2) * 0.5, MaxThinScale), MaxThinStride) * (0.75 + 0.5 * jitter)) : stepLength;
            continue;
        }
        if (density > 0.004)
        {
            // (A thin stride that landed in cloud backs up too, so a cloud's edge is found at the fine step.)
            if (stride && strode && t - lastEmpty > stepLength * 1.5)
            {
                t = lastEmpty + stepLength;
                strode = false;
                emptyRun = 0;
                continue;
            }
            emptyRun = 0;
            strode = false;
            if (firstHit < 0)
                firstHit = t;

            // Light reaching this point: how much cloud lies between it and the sun (or moon).
            // Four samples up to 3 km, three by 6 km, two past 11 km: far away a step is a few pixels of soft shading, and the
            // nearest sample's and the third's weights merge into their neighbours (fading, so no line shows where the count
            // changes). The weights always sum to 1440 m. (-skyoff 64: always four.)
            float mergeFirst = allSunSamples ? 0 : smoothstep(3000, 6000, t);
            float mergeThird = allSunSamples ? 0 : smoothstep(7000, 11000, t);
            // Fewer samples where the light hardly shows (R-8): thin wisps (little density to shade) and cloud deep behind
            // nearer cloud (little of it reaches the camera) take two, faded in like the distance merge. Below that, a
            // near-empty step or one almost hidden reuses the last step's light. (-skyoff 64 turns this off too.)
            [branch] if (!allSunSamples)
            {
                float few = max(1 - smoothstep(0.02, 0.12, density), 1 - smoothstep(0.15, 0.4, transmit));
                mergeFirst = max(mergeFirst, few);
                mergeThird = max(mergeThird, few);
            }
            float opticalDepth = lastOpticalDepth;
            [branch] if (allSunSamples || lastOpticalDepth < 0 || (density >= 0.015 && transmit >= 0.08))
            {
                opticalDepth = PunchyCloudDensityAt(p + lightDir * 360.0, 0) * (270.0 + 90.0 * mergeFirst + 225.0 * mergeThird)
                             + PunchyCloudDensityAt(p + lightDir * 1440.0, 0) * (630.0 + 225.0 * mergeThird);
                [branch] if (mergeFirst < 1)
                    opticalDepth += PunchyCloudDensityAt(p + lightDir * 90.0, 0) * 90.0 * (1 - mergeFirst);
                [branch] if (mergeThird < 1)
                    opticalDepth += PunchyCloudDensityAt(p + lightDir * 810.0, 0) * 450.0 * (1 - mergeThird);
                lastOpticalDepth = opticalDepth;
            }
            float beer = exp(-opticalDepth * PUNCHY_CLOUD_SIGMA);
            float powder = 1 - exp(-density * 6);   // edges facing the light stay a touch darker
            // Sunlight is far brighter than skylight: sunlit tops glare white, shaded bellies go grey.
            half3 light = lightColor;
            [branch] if (glow > 0)
                light = lerp(light, PunchyTwilightCloudLight(sunElevation, p.y) * 0.9 * (1 - 0.4 * weather.g), glow);
            float height = saturate((p.y - shape.x) / max(shape.y, 1));
            half3 direct;
            half3 ambient = lerp(_PunchySkyHorizon * 0.32, _PunchySkyZenith * 0.75 + _PunchySkyHorizon * 0.25, height)
                          * (1 - 0.6 * weather.g);
            [branch] if (oldLight)
            {
                // (Test only, -skyoff 256: the light before R-4.)
                direct = light * beer * phase * (0.55 + 0.45 * powder) * 3.2 + lining * beer * (1 - powder);
            }
            else
            {
                // Softer light through the body (R-4): light scattered many times inside a cloud gets deeper than a single
                // Beer falloff allows, so a weaker, slower falloff is kept as a floor and bodies glow instead of going flat
                // grey. Powder: thin edges facing the sun, seen from the sun's side, hold too little cloud to have scattered
                // light back yet and stay darker; looking towards the sun that fades and a silver lining takes over, light
                // pushed forward through the thin edges.
                float soft = max(beer, exp(-opticalDepth * PUNCHY_CLOUD_SIGMA * 0.25) * 0.55);
                float towardSun = saturate(cosTheta);
                float powderNew = lerp(1 - exp(-density * 9), 1, towardSun * towardSun);
                half3 silver = light * pow(towardSun, 8) * (1 - powder) * soft * 2.5;
                direct = light * soft * phase * (0.35 + 0.65 * powderNew) * 3.4 + (lining + silver) * soft * (1 - powder);
                // Darker bases: the underside sees little of the bright sky above and the sun's light has crossed the
                // whole body to reach it.
                ambient *= lerp(0.6, 1.1, height);
            }
            ambient = lerp(ambient, dot(ambient, half3(0.3, 0.5, 0.2)) * half3(0.85, 0.75, 1.1), twilight * 0.6);
            half3 flash = half3(0.75, 0.82, 1.0) * _PunchyLightningPos.w * 2.5
                        * exp(-distance(p, _PunchyLightningPos.xyz) / 1200);

            float absorbed = 1 - exp(-density * PUNCHY_CLOUD_SIGMA * stepLength);
            scatter += transmit * absorbed * (direct + ambient + flash);
            transmit *= 1 - absorbed;
            // Past this the cloud is opaque: nothing behind it shows (the last 2% let a shooting star, five times
            // brighter than the sky, shine through a dark night cloud, Z-09).
            if (transmit < 0.02)
            {
                transmit = 0;
                break;
            }
        }
        t += stepLength;
    }

    // (Test only, -skyoff 32: steps per pixel in colour bands, which survive tonemapping: blue under 20, green under 40,
    // yellow under 80, orange under 120, red above.)
    if (((uint)_PunchySkyOff & 32u) != 0)
    {
        scatterOut = stepsTaken < 20 ? half3(0, 0, 1) : stepsTaken < 40 ? half3(0, 1, 0) : stepsTaken < 80 ? half3(1, 1, 0) : stepsTaken < 120 ? half3(1, 0.4, 0) : half3(1, 0, 0);
        transmitOut = 0;
        return;
    }

    // Distant clouds fade into the horizon haze, and right at the horizon (where rays skim through kilometres of
    // cloud layer and alias) they thin out entirely.
    if (firstHit > 0)
        scatter = lerp(scatter, cloudHorizon * (1 - transmit), 1 - exp(-firstHit / 14000));
    float horizonFade = smoothstep(0.003, 0.05, dir.y);
    transmitOut = lerp(1, transmit, horizonFade);
    scatterOut = scatter * horizonFade;
}

// Marches the view ray through the cloud volume and composites the clouds over `background` (with the far layer under them).
half3 PunchyVolumetricClouds(half3 background, float3 dir, float2 screenPos)
{
    if (dir.y <= 0.003)
        return background;
    // Beyond ~15–22 km the clouds hand over to a generic flat layer with the map-wide average weather: far cheaper,
    // and local weather that far away is invisible detail anyway.
    if (((uint)_PunchySkyOff & 2u) == 0)
        background = PunchyFarClouds(background, dir);
    half3 scatter;
    float transmit;
    PunchyCloudMarch(dir, screenPos, 0, scatter, transmit);
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
    punchyCloudStyle = PunchyCloudStyleAt(p.xz);
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
    punchyCloudStyle = PunchyCloudStyleAt(p.xz);
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
    // How much there is comes with the cover and, since R-4, the weather's and biome's high cloud (0.3 = as before).
    half high = PunchyCloudStyleAt(hit).y;
    half amount = saturate(PunchyWeatherAt(hit).r * 0.6 + 0.2 + (high - 0.3) * 1.5);
    float n = PunchyFbm(uv, 4);
    return smoothstep(1 - amount * 0.55, 1.05, n) * 0.55 * (0.85 + 0.5 * high) * smoothstep(0.01, 0.15, dir.y);
}

#include "SharedClock.hlsl"

float PunchyHash1(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

// Shooting stars: the night is cut into 6 s slots on the shared clock, and in a slot a streak may cross the sky (about
// one slot in 3300, about 0.003 a night minute, W-42 cut it to 1/20; on a meteor shower night up to four a slot, each at
// 0.045, about 1.8 a minute). About one in six burns warm red-orange. Where it starts, which way it falls, how long, how
// fast and its colour all come from a hash of its slot, so every player sees the same ones. The shower comes once a
// lunar cycle, a couple of nights after the full moon (the moon's phase is shared too). _PunchySkyTest 1 makes every
// night a shower (-meteors); 2 (-redstar) holds one red-orange streak mid-flight high in the east, for shots.
half3 PunchyShootingStars(float3 dir)
{
    if (_PunchySkyTest > 1.5)
    {
        float3 fa = normalize(float3(1, 0.75, 0.15));
        float3 fr = normalize(cross(float3(0, 1, 0), fa));
        float3 fu = cross(fa, fr);
        float2 fw = normalize(float2(0.5, -0.8));
        float3 fv = dir - fa * dot(dir, fa);
        float2 fq = float2(dot(fv, fr), dot(fv, fu));
        float fAlong = dot(fq, fw);
        float fAcross = abs(fq.x * fw.y - fq.y * fw.x);
        half fTrail = saturate(1 - (0.15 - fAlong) / 0.1) * step(fAlong, 0.15) * step(0.05, fAlong);
        return half3(1.0, 0.42, 0.15) * fTrail * fTrail * (1 - smoothstep(0.0005, 0.0015, fAcross)) * step(0.85, dot(dir, fa)) * 6;
    }
    half shower = max(1 - smoothstep(0.04, 0.06, abs(_PunchyMoonPhase.x - 0.62)), _PunchySkyTest);
    const float slotLength = 6;
    float slot = floor(_PunchyClock / slotLength);
    float local = _PunchyClock - slot * slotLength;
    int count = shower > 0.5 ? 4 : 1;
    half3 sum = 0;
    for (int k = 0; k < 4; k++)
    {
        if (k >= count)
            break;
        float s = fmod(slot, 4096) * 7.13 + k * 31.7;
        if (PunchyHash1(s) > (shower > 0.5 ? 0.045 : 0.0003))
            continue;
        float start = PunchyHash1(s + 1.1) * (slotLength - 2.0);
        float life = lerp(0.5, 1.1, PunchyHash1(s + 2.3));
        float t = (local - start) / life;
        // t 0..1 is the flight; 1..1.5 is the fade-out (the head has stopped, the tail draws in and the light dies away)
        if (t < 0 || t > 1.5)
            continue;
        float az = PunchyHash1(s + 3.7) * 6.2832;
        float el = lerp(0.35, 1.1, PunchyHash1(s + 4.9));
        float3 a = float3(cos(az) * cos(el), sin(el), sin(az) * cos(el));
        if (dot(dir, a) < 0.85)
            continue;
        float3 right = normalize(cross(float3(0, 1, 0), a));
        float3 up = cross(a, right);
        float heading = PunchyHash1(s + 6.1) * 6.2832;
        float2 way = normalize(float2(cos(heading), sin(heading) - 0.7));   // mostly falling
        float len = lerp(0.18, 0.4, PunchyHash1(s + 7.7));
        float3 v = dir - a * dot(dir, a);
        float2 q = float2(dot(v, right), dot(v, up));
        float along = dot(q, way);
        float across = abs(q.x * way.y - q.y * way.x);
        float head = len * min(t, 1);
        float tail = max(len * 0.35 * (1 - smoothstep(1, 1.5, t)), 0.001);
        half trail = saturate(1 - (head - along) / tail) * step(along, head) * step(head - tail, along);
        half streak = 1 - smoothstep(0.0005, 0.0015, across);
        half3 tint = PunchyHash1(s + 8.3) < 1.0 / 6.0 ? half3(1.0, 0.42, 0.15) : half3(1.0, 0.95, 0.85);
        sum += tint * trail * trail * streak * smoothstep(0, 0.12, t) * (1 - smoothstep(0.55, 1.5, t)) * 5;
    }
    return sum;
}

// How strongly the blue moon holds the night: its lit share (from where it stands against the sun) while it's up.
half PunchyBlueMoonHold()
{
    half lit = 0.5 - 0.5 * dot(_PunchyBlueMoon, _PunchySunDirection);
    return lit * smoothstep(0.0, 0.15, _PunchyBlueMoon.y);
}

// The blue moon's pull (Zach, 11-08: it should feel like an insidious influence): on its nights blue streaks leave the
// moon and run outwards and down towards the island, several a minute when it's up and full, none when it's dark or
// down. Like the shooting stars, each comes from a hash of its 12 s slot on the shared clock, so every player sees the
// same ones; each starts just off the moon's disc and runs away from it, longer and slower than a meteor.
// Feedback 11-08 item 9 (they ended abruptly): a streak thins and dims over the outer part of its run, its head has a
// soft front, and it breaks up into twinkling sparkles as it tapers out and again as it fades, the sparkles lingering
// a little past where the line ended. _PunchySkyTest (-meteors) sends all three every slot, for shots.
// Round 2 (item 23): much fainter, in the disc's own deep blue, and half as fast (2.4-4.4 s a run, so 12 s slots, which
// also lets every streak finish its fade inside its slot rather than being cut off at the slot's end).
half3 PunchyBlueMoonStreaks(float3 dir)
{
    half hold = PunchyBlueMoonHold();
    if (hold < 0.35 || dot(dir, _PunchyBlueMoon) < 0.2)
        return 0;
    const float slotLength = 12;
    const float sparkCell = 0.014;   // spacing of the sparkles along a streak (sky units, about 0.8 degrees)
    float slot = floor(_PunchyClock / slotLength);
    float local = _PunchyClock - slot * slotLength;
    half3 sum = 0;
    [loop] for (int k = 0; k < 3; k++)
    {
        float s = fmod(slot, 4096) * 5.31 + k * 23.9 + 911.0;
        if (PunchyHash1(s) > (_PunchySkyTest > 0.5 ? 1.0 : 0.5 * smoothstep(0.35, 0.8, hold)))
            continue;
        float life = lerp(2.4, 4.4, PunchyHash1(s + 2.3));
        float start = PunchyHash1(s + 1.1) * (slotLength - 1.7 * 4.4);
        float t = (local - start) / life;
        // t 0..1 the run; 1..1.7 the fade (the tail draws in and the sparkles twinkle out)
        if (t < 0 || t > 1.7)
            continue;
        // Out from the moon: the anchor is the moon itself; the streak's way is a random direction round it, but
        // turned towards the ground (the island lies below).
        float3 a = _PunchyBlueMoon;
        float3 right = normalize(cross(float3(0, 1, 0), a) + float3(1e-4, 0, 0));
        float3 up = cross(a, right);
        float heading = PunchyHash1(s + 6.1) * 6.2832;
        float2 way = normalize(float2(cos(heading), sin(heading) - 0.9));
        // (At most 0.7 past the rim, so the whole run stays inside the early-out's cone: no cut at its edge.)
        float len = lerp(0.4, 0.7, PunchyHash1(s + 7.7));
        float3 v = dir - a * dot(dir, a);
        float2 q = float2(dot(v, right), dot(v, up));
        float along = dot(q, way) - sin(PunchyBlueMoonRadius) - 0.01;   // from just outside the disc's rim
        float side = q.x * way.y - q.y * way.x;
        // A soft, slightly wavering glow rather than a hard meteor line.
        float wobble = sin(along * 40 + _PunchyClock * 3 + PunchyHash1(s + 9.1) * 6.28) * 0.002;
        float across = abs(side + wobble);
        float head = len * min(t, 1);
        float tail = max(len * 0.6 * (1 - smoothstep(1, 1.6, t)), 0.001);
        half trail = saturate(1 - (head - along) / tail) * step(along, head) * step(head - tail, along) * step(0, along);
        // The taper: over the outer 70% of its run it thins and dims to nothing, and the head's front is soft.
        float run = saturate(along / len);
        half taper = 1 - smoothstep(0.3, 1.0, run);
        half front = smoothstep(0, 0.025, head - along);
        half streak = 1 - smoothstep(0.0006, 0.0012 + 0.003 * taper, across);
        // The break-up: from about a third of the way out, and as the whole streak fades, the line gives way to
        // sparkles: one dot in most cells along it, a little off the line, each twinkling at its own pace.
        half breakUp = saturate(smoothstep(0.3, 0.85, run) + smoothstep(0.85, 1.4, t));
        float cell = floor(along / sparkCell);
        float h = PunchyHash1(s * 0.37 + cell * 1.731);
        float2 spark = float2(along - (cell + 0.5) * sparkCell, side + wobble - (PunchyHash1(h * 97.3) - 0.5) * 0.008);
        half twinkle = pow(0.5 + 0.5 * sin(_PunchyClock * (4 + 7 * h) + h * 40), 3);
        half dots = (1 - smoothstep(0.0007, 0.0024, length(spark))) * step(h, 0.65) * twinkle;
        // Sparkles linger a little behind the tail and past the head, and die out at the very end of the run.
        half sparkTrail = saturate(1 - (head - along) / (tail * 1.4)) * step(head - tail * 1.4, along) * step(along, head + 0.03)
                        * step(0, along) * (1 - smoothstep(0.85, 1.1, run));
        half beam = trail * trail * streak * front * taper * (1 - breakUp);
        sum += half3(0.03, 0.12, 1.5) * (beam + sparkTrail * dots * breakUp * 2.5)
             * smoothstep(0, 0.1, t) * (1 - smoothstep(0.6, 1.7, t)) * 1.4;
    }
    return sum * hold;
}

// Auroras over the cold lands at night (_PunchyAurora): curtains hanging from the sky round the horizon in some
// directions, brightest along a wavering hem low down and fading upwards, green below and violet at their tops,
// folded and shot through with fine vertical rays, all rippling slowly on the shared clock. Worked out round the
// compass (a circle, so there's no seam), so they hang straight up like curtains rather than arching across.
half3 PunchyAurora(float3 dir)
{
    if (_PunchyAurora <= 0.001 || dir.y < 0.02)
        return 0;
    float az = atan2(dir.z, dir.x);
    float t = _PunchyClock * 0.02;
    float2 a2 = float2(cos(az), sin(az)) * 3;
    half hang = smoothstep(0.35, 0.7, PunchyValueNoise(a2 * 0.8 + t));
    float hem = 0.07 + 0.08 * PunchyValueNoise(a2 * 1.7 + t * 2);
    float rise = dir.y - hem;
    half sheet = smoothstep(-0.012, 0.01, rise) * exp(-max(rise, 0) * 9) * (1 - smoothstep(0.18, 0.4, rise));
    half folds = 0.5 + 0.5 * sin(dot(a2, float2(7.3, 5.1)) + PunchyValueNoise(a2 * 2.5 + t * 3) * 6 + t * 4);
    half rays = 0.5 + 0.5 * PunchyValueNoise(a2 * 30 + float2(0, t * 10));
    half3 colour = lerp(half3(0.15, 1.0, 0.45), half3(0.75, 0.3, 1.0), smoothstep(0.04, 0.3, rise));
    return colour * sheet * hang * (0.35 + 0.65 * folds) * rays * _PunchyAurora * 1.1;
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

// A moon's disc where a direction falls on it: a sphere of angular radius `radius` towards `m`, lit from `toLight`
// (in disc space: x right, y up, z towards the viewer), with a soft terminator, a few darker seas and a trace of
// earthshine on its dark side. `cover` (out) is how much of the sky behind it the disc hides. 0 off the disc.
half3 PunchyMoonFace(float3 dir, float3 m, float radius, float3 toLight, half3 tint, out half cover)
{
    float moonDot = dot(dir, m);
    float edgeCos = cos(radius);
    cover = smoothstep(edgeCos - radius * radius * 0.08, edgeCos + radius * radius * 0.08, moonDot);
    if (cover <= 0)
        return 0;
    float3 right = normalize(cross(float3(0, 1, 0), m));
    float3 up = cross(m, right);
    float3 v = dir - m * moonDot;
    float2 uv = float2(dot(v, right), dot(v, up)) / sin(radius);   // -1..1 across the disc
    float3 n = float3(uv, sqrt(saturate(1 - dot(uv, uv))));
    half lit = smoothstep(-0.04, 0.08, dot(n, normalize(toLight)));
    half seas = 1 - 0.22 * smoothstep(0.35, 0.65, PunchyValueNoise(uv * 2.3 + 7.1));
    return tint * cover * (lit * seas + 0.012) * 1.6;
}

// Nearest and second-nearest feature points in a 2D cell pattern (for the blue moon's craters and cracks): x the
// distance to the nearest, y to the second, z the nearest cell's own random 0..1.
float3 PunchyMoonCells(float2 q)
{
    int2 c = int2(floor(q));
    float f1 = 9, f2 = 9, id = 0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 cell = c + int2(x, y);
        float2 at = float2(cell) + float2(PunchyCellHash(cell), PunchyCellHash(cell + int2(37, 91)));
        float d = distance(q, at);
        if (d < f1) { f2 = f1; f1 = d; id = PunchyCellHash(cell + int2(11, 5)); }
        else if (d < f2) f2 = d;
    }
    return float3(f1, f2, id);
}

// The blue moon's disc: always full (Zach, G-34), with a clearly cratered and cracked face: bowls darker with bright
// rims in a share of the cells, fine dark cracks along cell borders in patches, the darker seas, and limb darkening.
half3 PunchyBlueMoonFace(float3 dir, float3 m, float radius, half3 tint, out half cover)
{
    float moonDot = dot(dir, m);
    float edgeCos = cos(radius);
    cover = smoothstep(edgeCos - radius * radius * 0.08, edgeCos + radius * radius * 0.08, moonDot);
    if (cover <= 0)
        return 0;
    float3 right = normalize(cross(float3(0, 1, 0), m));
    float3 up = cross(m, right);
    float3 v = dir - m * moonDot;
    float2 uv = float2(dot(v, right), dot(v, up)) / sin(radius);
    float z = sqrt(saturate(1 - dot(uv, uv)));
    // Craters at three scales, each cell with its own size and depth, soft shaded bowls and soft rims (not discs).
    // The bowl is a smooth dish darkening toward its centre, the rim a broad gentle ridge; a few cells stay empty.
    half craters = 0;
    // W-11: a fourth, fine scale so the face keeps its detail at the larger size. W-12: a fifth, finer still, and
    // crisper crater walls and rims, so the face reads sharp at night (#34).
    for (int k = 0; k < 5; k++)
    {
        float freq = k == 0 ? 2.6 : (k == 1 ? 6.0 : (k == 2 ? 13.0 : (k == 3 ? 29.0 : 61.0)));
        float3 cr = PunchyMoonCells(uv * freq + 4.7 + k * 9.3);
        float pick = frac(cr.z * 7.3);
        float live = step(k == 0 ? 0.3 : 0.45, cr.z);
        float size = lerp(0.12, 0.5, pick * pick);        // many small, a few large
        float d = cr.x / size;
        float depth = lerp(0.35, 1.0, frac(cr.z * 13.7)) * (k == 0 ? 0.9 : (k == 3 ? 0.45 : (k == 4 ? 0.35 : 0.7)));
        // A dish shaded toward its centre inside a steep wall, and a narrow rim just outside it.
        float bowlK = (1 - smoothstep(0.7, 1.0, d)) * (0.55 + 0.45 * saturate(1 - d));
        float rimK = exp(-pow((d - 1.04) / 0.11, 2));
        craters += live * (-depth * 0.5 * bowlK * bowlK * (1 + 0.5 * bowlK) + depth * 0.22 * rimK);
    }
    // Rock: mottled, with a coarse and a fine grain; maria as broad darker patches, warped so their edges wander.
    float2 warp = float2(PunchyValueNoise(uv * 2.1 + 17.0), PunchyValueNoise(uv * 2.1 + 41.0)) - 0.5;
    float basin = PunchyValueNoise(uv * 1.5 + warp * 1.6 + 7.1) * 0.5 + PunchyValueNoise(uv * 3.4 + warp * 1.1 + 1.3) * 0.3
                + PunchyValueNoise(uv * 8.0 + warp * 0.8 + 5.5) * 0.2;
    half seas = 1 - 0.4 * smoothstep(0.38, 0.62, basin);
    half rock = 0.72 + 0.5 * (PunchyValueNoise(uv * 6.5 + 3.3) * 0.35 + PunchyValueNoise(uv * 17 + 8.8) * 0.3 + PunchyValueNoise(uv * 48 + 2.4) * 0.2
                            + PunchyValueNoise(uv * 120 + 6.1) * 0.1 + PunchyValueNoise(uv * 260 + 9.7) * 0.08);
    // A lit sphere: sun from the upper left, a soft terminator and a dark limb; the dark side keeps a faint glow.
    float3 n = float3(uv, z);
    half lit = smoothstep(-0.25, 0.55, dot(n, normalize(float3(-0.55, 0.4, 0.75))));
    half limb = pow(saturate(z), 0.45);
    // Slow shimmer across the face: low-amplitude brightness noise drifting over about a minute (no flicker).
    half shimmer = 1 + 0.12 * (PunchyValueNoise(uv * 4.0 + float2(_Time.y * 0.03, -_Time.y * 0.02)) + PunchyValueNoise(uv * 9.0 - _Time.y * 0.025) - 1);
    half shade = seas * rock * shimmer * saturate(1 + craters) * (0.1 + 0.9 * lit) * (0.2 + 0.8 * limb);
    // W-13 (#34): 1.6 -> 0.7, so the face stays near the bloom threshold instead of clipping and blooming over itself,
    // which washed out the crater edges.
    return tint * cover * shade * 0.7;
}

// The blue moon's corona: a soft glow just outside its disc with a faint ring a little further out, hidden behind it.
half3 PunchyBlueCorona(float3 dir, float radius)
{
    float3 m = _PunchyBlueMoon;
    float a = sqrt(max(2 * (1 - dot(dir, m)), 0));   // angle to it (small angles)
    float outside = max(a - radius, 0) / radius;
    // Angle round the rim; filaments are wavy streaks that reach out a little way, like prominences on the sun.
    float3 right = normalize(cross(float3(0, 1, 0), m));
    float3 up = cross(m, right);
    float3 v = dir - m * dot(dir, m);
    float th = atan2(dot(v, up), dot(v, right));
    float wave = th * 5 + sin(outside * 9 + th * 3 + _Time.y * 0.15) * 0.8;
    float fil = PunchyValueNoise(float2(wave, _Time.y * 0.05)) * 0.7 + PunchyValueNoise(float2(th * 17 + outside * 4 + _Time.y * 0.12, 3.1 + _Time.y * 0.03)) * 0.3;
    half tendrils = smoothstep(0.45, 0.85, fil) * exp(-outside * 3.2) * 0.3;
    half glow = exp(-outside * 6) * 0.07;
    // Starts at the disc's edge (W-13: was 0.98, which laid the glow over the rim of the face).
    return half3(0.0, 0.03, 2.0) * (glow + tendrils) * smoothstep(1.0, 1.02, a / radius);
}

// Both moons where a direction falls on them: the big moon lit by its phase, the small blue one by the sun from
// wherever it stands. `cover` (out) is how much they hide the sky behind.
half3 PunchyMoonDisc(float3 dir, out half cover)
{
    float a = _PunchyMoonPhase.x * 6.2832;
    half bigCover, blueCover;
    half3 moons = PunchyMoonFace(dir, _PunchyMoonDirection, 0.0172, float3(sin(a), 0.15 * sin(a), -cos(a)), half3(0.9, 0.92, 1.0), bigCover);
    // The blue moon: a lit rocky sphere, a sister planet about 13 times the big moon's size (Zach, #34).
    moons += PunchyBlueMoonFace(dir, _PunchyBlueMoon, PunchyBlueMoonRadius, half3(0.0, 0.02, 2.4), blueCover) * (1 - bigCover);
    cover = max(bigCover, blueCover);
    return moons;
}

// The sky without the volumetric clouds: gradient, sun and moon, stars, cirrus, horizon haze. With
// `approximateClouds` (reflections) it adds a cheap one-sample version of the clouds instead.
half3 PunchySkyColor(float3 dir, bool withDiscs, bool approximateClouds)
{
    dir = normalize(dir);
    float h = dir.y;
    // Twilight (W-31): the horizon warms on the sun's side and the zenith deepens; both are the clear colours otherwise.
    half3 horizon = PunchyTwilightHorizon(dir);
    // Around sunset (sun above -3°) the warm band reaches higher on the sun's side, so it reads orange-red rather
    // than thinned to pink by the violet zenith (W-33). Blue hour and midday keep the plain curve.
    float bandElevation = PunchySunElevation();
    half band = PunchyTwilightAmount(bandElevation) * smoothstep(-3, 0, bandElevation) * PunchySunSide(dir);
    half3 sky = lerp(horizon, PunchyTwilightZenith(), pow(saturate(h), lerp(0.45, 0.85, band)));
    sky = h < 0 ? lerp(horizon, _PunchySkyGround, saturate(-h * 3)) : sky;

    float sunDot = saturate(dot(dir, _PunchySunDirection));
    float moonDot = saturate(dot(dir, _PunchyMoonDirection));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    sky += _PunchySunColor * pow(sunDot, 12) * 0.25 * clearness;
    // The low sun's wide glow, lingering a little after it sets.
    float sunElevation = PunchySunElevation();
    sky += PunchyTwilightColor(sunElevation) * PunchyTwilightLight(sunElevation) * PunchyTwilightAmount(sunElevation)
         * (pow(sunDot, 6) * 0.45 + pow(sunDot, 40) * 0.6) * clearness;

    if (h > 0)
    {
        // Stars and night events only by night (R-8: by day they were a tenth of the skybox draw, all multiplied by 0).
        half3 night = 0;
        [branch] if (_PunchyNight > 0)
        {
            night = PunchyStars(dir) * _PunchyNight;
            // Shooting stars and auroras show through clear skies only: dimmed by the cloud cover overall, and by the
            // clouds along this very direction (they're bright enough to show through a thin cloud's last light, Z-09).
            half3 events = (PunchyShootingStars(dir) + PunchyAurora(dir) + PunchyBlueMoonStreaks(dir)) * _PunchyNight * (1 - _PunchyClouds.x);
            [branch] if (any(events > 0))
            {
                half eventsDarkness;
                events *= 1 - PunchyCloudCoverAlong(dir, eventsDarkness);
            }
            night += events;
        }
        if (withDiscs)
        {
            half moonCover;
            night += PunchyMoonDisc(dir, moonCover) * _PunchyNight;
            sky += _PunchySunColor * smoothstep(0.9994, 0.9997, sunDot) * 12 * clearness;
        }
        // The glow round the moon brightens with its lit share (none round a new moon), bluer under a blue moon.
        half3 halo = half3(0.35, 0.42, 0.6) * (0.08 + 0.92 * _PunchyMoonPhase.y);
        // The moon's own face (its dark side too) hides the glow behind it.
        halo *= 1 - smoothstep(0.99982, 0.99987, moonDot) * withDiscs;
        sky += halo * pow(moonDot, 40) * 0.25 * _PunchyNight + night;
        // The blue moon's corona (below the horizon it has none to show).
        sky += PunchyBlueCorona(dir, PunchyBlueMoonRadius) * _PunchyNight * clearness * step(0, _PunchyBlueMoon.y);

        half daylight = saturate(dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2)) * 1.6);
        half3 cirrusColor = (half3(1, 1, 1) * daylight + _PunchySunColor * pow(sunDot, 4) * 0.5) * 0.95;
        // High ice cloud catches the sun longest: pink-gold well after sunset, violet away from the sun.
        half cirrusTwilight = PunchyTwilightAmount(sunElevation);
        half sunSide = PunchySunSide(dir);
        cirrusColor = lerp(cirrusColor, lerp(dot(cirrusColor, half3(0.3, 0.5, 0.2)) * half3(0.8, 0.72, 1.05),
                                             PunchyTwilightCloudLight(sunElevation, PUNCHY_CIRRUS_HEIGHT) * 1.1, sunSide * sunSide),
                           cirrusTwilight);
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
    return lerp(sky, horizon, exp(-max(h, 0) * 10) * 0.85);
}

// ------------------------------------------------------------------------------------------------ atmosphere

// Dust and rain haze along a ray, read from the weather map: a distant sandstorm is a tan wall, rain hangs in
// grey curtains below the local cloud base. Steps are packed towards the camera, where detail matters most.
// Gives the light scattered in and the fraction of what's behind that still shows through. The volumes thin out
// with height (dust above its billowing top, rain above the cloud base), judged at min(ray height, heightCap):
// the haze table caps it at ground level (see PunchyHazeLutHeight), a true ray passes a huge cap.
void PunchyWeatherVolumesAlong(float3 start, float3 dir, float rayLength, int steps, float heightCap, out half3 scatter, out float transmit)
{
    half brightness = dot(_PunchySkyHorizon, half3(0.3, 0.5, 0.2));
    scatter = 0;
    transmit = 1;
    float previous = 0;
    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float s = (float)i / steps;
        float t = rayLength * s * s;
        float segment = t - previous;
        float3 p = start + dir * (previous + segment * 0.5);
        previous = t;
        float height = min(p.y, heightCap);

        half4 w = PunchyWeatherAt(p.xz);
        if (w.a + w.b <= 0.004)
            continue;

        // Drifting noise breaks the volumes up: dust billows with an uneven top, rain falls in streaky curtains.
        float2 drift = _PunchyCloudOffset * PUNCHY_CLOUD_FEATURE;
        float billow = PunchyValueNoise((p.xz - drift) / 260) * 0.65 + PunchyValueNoise((p.xz - drift * 1.6) / 90) * 0.35;
        float dustTop = PUNCHY_DUST_TOP * (0.45 + billow);
        float dust = w.a * PUNCHY_DUST_DENSITY * saturate((dustTop - height) / 180) * (0.4 + billow);
        float streaks = PunchyValueNoise((p.xz - drift) / 110);
        float cloudBase = PunchyCloudShapeAt(p.xz).x;
        // The shaft is densest in the middle of the cell and frays at its edges.
        float rain = w.b * w.b * PUNCHY_RAIN_DENSITY * 1.4 * saturate((cloudBase - height) / 120) * (0.3 + 1.4 * streaks);
        float density = dust + rain;
        if (density <= 1e-6)
            continue;
        half3 rainTint = _PunchySkyHorizon * 0.72 * (1 - 0.5 * w.g);
        half3 tint = (dust * _PunchyDustColor * (brightness * 1.4 + 0.03) + rain * rainTint) / density;
        float absorbed = 1 - exp(-density * segment);
        scatter += tint * absorbed * transmit;
        transmit *= 1 - absorbed;
    }
}

half3 PunchyWeatherVolumes(half3 color, float3 start, float3 dir, float rayLength, int steps)
{
    half3 scatter;
    float transmit;
    PunchyWeatherVolumesAlong(start, dir, rayLength, steps, 1e7, scatter, transmit);
    return color * transmit + scatter;
}

// The weather volumes seen from the camera, marched once a frame into a small table (HazeLut.shader, rendered by
// WeatherSystem) instead of per pixel in every surface shader, which cost weak GPUs nearly half their frame rate
// in stormy weather. Columns are directions round the horizon, rows distances out to PUNCHY_HAZE_RANGE (square-root
// spaced, so near distances get more rows). Rays run almost level from the camera, like the horizon haze target.
// The table is indexed by direction and distance only, but the surfaces it hazes are mostly the ground, below the
// camera. Marching the air at the camera's own height made a camera high near a rain or snow cell (a player on a
// peak) haze the low ground by whatever its eye level held, which flips between directions near the cloud base and
// drew hard vertical edges on screen (A-87 row 2). So the volumes are judged at ground level instead: the full
// shaft below the cloud base, the dust below its top, whatever the camera's height.
// rgb = scattered light, a = 1 - transmittance, so an unset (black) table means no weather haze rather than black.
#define PUNCHY_HAZE_RANGE 8000.0
#define PUNCHY_HAZE_ELEVATION 0.02
#define PUNCHY_HAZE_ROWS 32.0
#define PunchyHazeLutHeight 0.0   // the height the table judges the volumes at (sea level: under every cloud base)
TEXTURE2D(_PunchyHazeLut);     SAMPLER(sampler_PunchyHazeLut);
float3 _PunchyHazeOrigin;      // where the table was marched from

// The ray through table column u (0..1 round the horizon).
float3 PunchyHazeLutDirection(float u)
{
    float angle = (u - 0.5) * 2 * PI;
    return normalize(float3(cos(angle), PUNCHY_HAZE_ELEVATION, sin(angle)));
}

// The distance of table row v (texel centres run from 0 m to PUNCHY_HAZE_RANGE).
float PunchyHazeLutDistance(float v)
{
    float s = saturate((v * PUNCHY_HAZE_ROWS - 0.5) / (PUNCHY_HAZE_ROWS - 1));
    return PUNCHY_HAZE_RANGE * s * s;
}

// Applies the tabled weather volumes for `distance` metres towards `offsetXZ` (from the table's origin).
half3 PunchyHazeFromLut(half3 color, float2 offsetXZ, float distance)
{
    float u = atan2(offsetXZ.y, offsetXZ.x) * (0.5 / PI) + 0.5;
    float s = sqrt(saturate(distance / PUNCHY_HAZE_RANGE));
    float v = (s * (PUNCHY_HAZE_ROWS - 1) + 0.5) / PUNCHY_HAZE_ROWS;
    half4 haze = SAMPLE_TEXTURE2D_LOD(_PunchyHazeLut, sampler_PunchyHazeLut, float2(u, v), 0);
    return color * (1 - haze.a) + haze.rgb;
}

// The weather volumes seen against the sky (R-8): the skybox's own march along each view direction (8 steps out to
// 8 km, the true ray height), done once a frame into a small direction table (HazeLut.shader's second pass, rendered
// by WeatherSystem from the camera) instead of per full-res sky pixel, where it was over half the skybox's cost.
// Columns run round the horizon; rows are spaced by y|y|, so the directions near the horizon, where the volumes are,
// get most of them. Same layout as the haze table: rgb = scattered light, a = 1 - transmittance.
TEXTURE2D(_PunchySkyVolumes);  SAMPLER(sampler_PunchySkyVolumes);

// The view direction through table texel uv (0..1 both ways).
float3 PunchySkyVolumesDirection(float2 uv)
{
    float angle = (uv.x - 0.5) * 2 * PI;
    float r = uv.y * 2 - 1;
    float y = r * abs(r);
    float across = sqrt(saturate(1 - y * y));
    return float3(cos(angle) * across, y, sin(angle) * across);
}

// Applies the tabled weather volumes along the view direction `dir` (unit length).
half3 PunchySkyVolumesFromLut(half3 color, float3 dir)
{
    float u = atan2(dir.z, dir.x) * (0.5 / PI) + 0.5;
    float v = sign(dir.y) * sqrt(abs(dir.y)) * 0.5 + 0.5;
    half4 haze = SAMPLE_TEXTURE2D_LOD(_PunchySkyVolumes, sampler_PunchySkyVolumes, float2(u, v), 0);
    return color * (1 - haze.a) + haze.rgb;
}

// 0..1: how fully clear and sunny the day is round the camera (WeatherSystem): the base haze thins there (item 30).
half _PunchyHazeClear;

// Everything between the camera and a surface point: weather volumes, then the base haze (fog coloured like the
// horizon, thick enough at the edge of the streamed terrain to hide it).
half3 PunchyAtmosphere(half3 color, float3 positionWS)
{
    float3 camera = _WorldSpaceCameraPos;
    float3 toPoint = positionWS - camera;
    float distance = length(toPoint);
    float3 dir = toPoint / max(distance, 1e-3);
    // Under water (P-20, Underwater.cs): the water's own fog by distance instead of the sky's haze, which can't be
    // seen from down here. Everything drawn through PunchyAtmosphere (terrain, flora, rocks) fades alike. Only the part
    // of the path under the surface counts (playtest 11-08 item 8): the shore above water stays clear enough to show
    // through Snell's window as the lens nears the top (Water.shader).
    if (_PunchyUnderwater.w > 0)
    {
        float wet = distance;
        float rise = positionWS.y - camera.y;
        if (positionWS.y > _PunchyUnderwaterSurface.x && rise > 1e-3)
            wet *= saturate(_PunchyUnderwaterSurface.y / rise);
        return lerp(color, (half3)_PunchyUnderwater.rgb, 1 - exp(-pow(wet * _PunchyUnderwater.w, 2)));
    }
    color = PunchyHazeFromLut(color, positionWS.xz - _PunchyHazeOrigin.xz, distance);
    float haze = 1 - exp(-pow(distance * _PunchyHazeDensity, 2));
    if (haze < 0.01)
        return color;
    // Fully clear, sunny days (feedback 11-08 item 30): the haze stays thin further out and closes in near the edge of
    // the streamed terrain (distance cubed instead of squared, scaled to the same ~98% at that edge, so the edge stays
    // hidden), so distant land isn't greyed. Not in cold lands (_PunchyAurora), cloud or weather (WeatherSystem).
    half clearDay = _PunchyHazeClear * (1 - _PunchyAurora);
    if (clearDay > 0)
    {
        float far = distance * _PunchyHazeDensity;
        haze = lerp(haze, 1 - exp(-far * far * far / 1.98), clearDay);
    }

    // Haze towards exactly what the sky shows just above the horizon in this direction: the horizon colour with the
    // sun's glow, seen through the same rain and dust the skybox looks through. Hazing to the bare horizon colour
    // left distant land brighter than the sky behind it whenever rain or cloud darkened the sky.
    float3 flat = normalize(float3(dir.x, 0.02, dir.z));
    float clearness = 1 - _PunchyClouds.x * 0.7;
    // Distant land takes only half the sunset tint (luminance kept), so far hills don't go pink (W-33).
    half3 twilightHaze = PunchyTwilightHorizon(flat);
    float hazeElevation = PunchySunElevation();
    twilightHaze = lerp(twilightHaze, dot(twilightHaze, half3(0.3, 0.5, 0.2)) * half3(1.05, 0.97, 0.92),
                        0.5 * saturate(PunchyTwilightAmount(hazeElevation) * 3) * smoothstep(-3, 0, hazeElevation));
    half3 horizon = twilightHaze + _PunchySunColor * pow(saturate(dot(flat, _PunchySunDirection)), 12) * 0.25 * clearness;
    horizon = PunchyHazeFromLut(horizon, flat.xz, PUNCHY_HAZE_RANGE);
    return lerp(color, horizon, haze);
}

#endif
