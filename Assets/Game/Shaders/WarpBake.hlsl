// One pass of R-12's warp bake (HazeLut.shader passes 2 and 3): the weather or cloud map, re-read where the cloud warp
// points. PUNCHY_WARP_PASS_MAP names the source map; PUNCHY_WARP_PASS_CLOUD marks the cloud map (its climate default
// differs). Each texel's warp is taken at its column's cloud lower middle, from the unwarped cloud map.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Sky.hlsl"

TEXTURE2D(_SrcWeatherMap); SAMPLER(sampler_SrcWeatherMap);
TEXTURE2D(_SrcCloudMap);   SAMPLER(sampler_SrcCloudMap);
float2 _WarpMapSize;   // texels across, down

struct Attributes { float4 positionOS : POSITION; };
struct Varyings { float4 positionCS : SV_POSITION; };

Varyings Vert(Attributes input)
{
    Varyings o;
    o.positionCS = float4(input.positionOS.xy * 2 - 1, 0.5, 1);
    return o;
}

half4 Frag(Varyings input) : SV_Target
{
    // (Texel centres: uv (i + 0.5) / width, as PunchyMapUV reads them back.)
    float2 uv = input.positionCS.xy / _WarpMapSize;
    float2 xz = _PunchyWeatherRect.xy + uv / _PunchyWeatherRect.zw;
    float4 shape = lerp(float4(_PunchyClimate.zw, 0, 0), SAMPLE_TEXTURE2D_LOD(_SrcCloudMap, sampler_SrcCloudMap, uv, 0), PunchyMapInside(uv))
                   * float4(4000, 9000, 1, 12000);
    float2 warp = PunchyCloudWarpRaw(float3(xz.x, shape.x + shape.y * 0.3, xz.y));
    float2 at = PunchyMapUV(xz + warp);
#ifdef PUNCHY_WARP_PASS_CLOUD
    float4 fallback = float4(_PunchyClimate.zw, 0, 0);
#else
    float4 fallback = float4(_PunchyClimate.xy, 0, 0);
#endif
    // (Past the map's edge the source fades to the climate as PunchyWeatherAt / PunchyCloudShapeAt do.)
    return lerp(fallback, SAMPLE_TEXTURE2D_LOD(PUNCHY_WARP_PASS_MAP, sampler_SrcWeatherMap, at, 0), PunchyMapInside(at));
}
