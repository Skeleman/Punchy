#ifndef PUNCHY_SKY_COMMON_INCLUDED
#define PUNCHY_SKY_COMMON_INCLUDED

// The sky, its clouds, and the atmosphere between the camera and anything it sees. Shared by the skybox, terrain
// and water, so they always agree. Weather is anchored in the world: WeatherSystem renders it into world-covering
// maps that everything here reads:
//   weather map  rgba = cloud coverage, cloud darkness, rain/mist haze, dust
//   wetness map  rg   = ground wetness, precipitation
//   cloud map    rg   = cloud base / 4000 m, cloud thickness / 9000 m
// Clouds are volumetric, carved from a tiling 3D noise texture (CloudNoise) between each place's cloud base and
// top: puffy cumulus in fair weather, flat stratus when overcast, cumulonimbus towers with anvils in storms.
// Rain falls from the local cloud base. A thin cirrus layer sits above everything.

//
// R-23 row 3: Sky.hlsl is split by what a shader needs. SkyCommon.hlsl (this file): the weather maps, the sky's
// globals and the shared noise. Clouds.hlsl: cloud density and the cloud shadow on the ground. Atmosphere.hlsl:
// twilight colours and the haze between the camera and a surface (PunchyAtmosphere). Sky.hlsl: the sky itself
// (cloud march, far clouds, cirrus, stars, moons, PunchySkyColor) on top of all three. Surfaces that only need
// haze and cloud shadow include Atmosphere.hlsl; anything that reads the sky colour includes Sky.hlsl.

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
float4 _PunchyUnderwaterShafts;  // under water, x light shaft march steps (0 off), y brightness, z fade per metre under the surface (Underwater.cs)

float3 _PunchySunDirection;    // towards the sun
float3 _PunchyMoonDirection;   // towards the moon
float _PunchySkyOff;           // test only (-skyoff n, to price the sky's parts): 1 no cloud march, 2 no far clouds, 4 no sky colour, 8 no weather volumes, 16 old even steps, 32 steps-per-pixel overlay, 64 always four sun samples, 128 cloud edges before R-4, 256 cloud light before R-4, 512 one cloud type everywhere (before R-4), 1024 cloud maps read straight (before R-12), 2048 weather, cloud and wetness maps read plain bilinear, hard cloud shadow edge (before R-20 row 4); 65536 cloud edge detail fading in fixed metres (before R-35 pick E)
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

// R-20 row 4: the weather, cloud and wetness maps are coarse (64 m texels) and read bilinear, which bends every contour at the
// texel lines; sharpened into cloud edges and shadow edges those bends showed as big square and diamond pixels. Still
// one tap: the read point is moved inside its texel so the blend between texels eases in and out (quintic), smooth
// across the texel lines. Keep the size in step with WeatherField.Resolution. (-skyoff 2048: plain bilinear.)
#define PUNCHY_WEATHER_MAP_SIZE 256.0
float2 PunchyMapSmoothUV(float2 uv)
{
    if (((uint)_PunchySkyOff & 2048u) != 0)
        return uv;
    float2 st = uv * PUNCHY_WEATHER_MAP_SIZE - 0.5;
    float2 i = floor(st);
    float2 f = st - i;
    f = f * f * f * (f * (f * 6 - 15) + 10);
    return (i + f + 0.5) / PUNCHY_WEATHER_MAP_SIZE;
}

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
    half4 local = SAMPLE_TEXTURE2D_LOD(_PunchyWeatherMap, sampler_PunchyWeatherMap, PunchyMapSmoothUV(uv), 0);
    return lerp(half4(_PunchyClimate.xy, 0, 0), local, PunchyMapInside(uv));
}

half2 PunchyWetnessAt(float2 xz)
{
    float2 uv = PunchyMapUV(xz);
    return SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, PunchyMapSmoothUV(uv), 0).rg * PunchyMapInside(uv);
}

// Cloud shape at a world position: base (m), thickness (m), anvil strength (0..1), anvil top (m).
float4 PunchyCloudShapeAt(float2 xz)
{
    float2 uv = PunchyMapUV(xz);
    float4 local = SAMPLE_TEXTURE2D_LOD(_PunchyCloudMap, sampler_PunchyCloudMap, PunchyMapSmoothUV(uv), 0);
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

#endif
