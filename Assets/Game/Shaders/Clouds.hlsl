#ifndef PUNCHY_CLOUDS_INCLUDED
#define PUNCHY_CLOUDS_INCLUDED

// Cloud density, where the cloud volume reads its maps, and the clouds' shadow on the ground (split from Sky.hlsl,
// R-23 row 3). The march that draws the clouds stays in Sky.hlsl.

#include "SkyCommon.hlsl"

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
    float2 local = SAMPLE_TEXTURE2D_LOD(_PunchyWetnessMap, sampler_PunchyWetnessMap, PunchyMapSmoothUV(uv), 0).ba;
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

// Where the cloud volume reads the weather and cloud maps (R-12): a few hundred metres off the point, wandering with
// position and height. A storm cell is a smooth disc in the 64 m maps, and inside it the cover saturates so the noise
// no longer carves the cloud; read straight, its rim stood as a smooth pillar with straight sides (#241). Wandered, the
// rim bulges and leans like a heaped tower. The march sets punchyCloudWarp once per step and its light samples share it.
// (-skyoff 1024: read straight, before R-12.)
static float2 punchyCloudWarp = 0;

float2 PunchyCloudWarpAt(float3 p)
{
    if (((uint)_PunchySkyOff & 1024u) != 0)
        return 0;
    float3 q = p - float3(_PunchyCloudOffset.x, 0, _PunchyCloudOffset.y) * PUNCHY_CLOUD_FEATURE;
    float2 n = SAMPLE_TEXTURE3D_LOD(_PunchyCloudNoise, sampler_PunchyCloudNoise, float3(q.x * 0.6 + q.z * 0.8, q.y * 1.4, q.z * 0.6 - q.x * 0.8) / 3100.0 + 0.53, 0).gb;
    return (n - 0.5) * 800;
}

float PunchyCloudDensityAt(float3 p, float detail)
{
    float2 xz = p.xz + punchyCloudWarp;
    return PunchyCloudDensity(p, PunchyWeatherAt(xz), PunchyCloudShapeAt(xz), detail);
}

float PunchyPhase(float cosTheta, float g)
{
    float g2 = g * g;
    return (1 - g2) / pow(max(1 + g2 - 2 * g * cosTheta, 1e-4), 1.5);   // Henyey–Greenstein × 4π (1 = isotropic)
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
    float2 mapXZ = p.xz + PunchyCloudWarpAt(p);   // where the volume reads its maps (R-12), so the shadow sits under it
    half4 weather = PunchyWeatherAt(mapXZ);
    punchyCloudStyle = PunchyCloudStyleAt(p.xz);
    float density = PunchyCloudDensity(p, weather, PunchyCloudShapeAt(mapXZ), 0);
    // R-20 row 4: the edge eases in and out rather than starting at a hard line, and widens with the cloud's height
    // above the ground: the sun is half a degree across, so a cloud's penumbra grows about 9 m per km of height.
    float edge = ((uint)_PunchySkyOff & 2048u) != 0 ? 1.0 / 3.0 : (1.0 / 3.0) * (1 + max(height - positionWS.y, 0) * 0.00025);
    float dark = ((uint)_PunchySkyOff & 2048u) != 0 ? saturate(density * 3) : smoothstep(0, edge, density);
    return dark * lerp(0.5, 0.95, saturate(shape.y / 3000));
}

// R-36: what a lit colour keeps of the sun's share once the sky over it has its say. skyOverhead is how much open sky
// stands over the point (1 out in the open, 0 under a cave roof): the sun's share is kept only there, and cloud shadow
// dims what is kept. direct and ambient are the luminances of the sun's and the ambient share; multiply the colour by
// the result. With skyOverhead 1 it is the plain cloud-shadow dimming the surfaces had before.
half PunchySkyShade(half cloudShadow, half skyOverhead, half direct, half ambient)
{
    half kept = saturate(skyOverhead) * (1 - cloudShadow);
    return 1 - (1 - kept) * direct / max(direct + ambient, 1e-3);
}

#endif
