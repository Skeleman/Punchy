#ifndef PUNCHY_WATER_MATERIAL_INCLUDED
#define PUNCHY_WATER_MATERIAL_INCLUDED
// The water material's constants, shared by Water.shader and WaterTess.shader (the SRP batcher wants one layout).
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
    half4 _ShallowColor;
    half4 _DeepColor;
    half _Absorption;
    half _Opacity;
    half4 _FoamColor;
    half _FoamDistance;
    float _WaveScale;
    half _WaveStrength;
    float _WaveSpeed;
    float _FlowSpeed;
    half _Smoothness;
    half _ScreenReflection;
    half _DetailStrength;
    float _DetailScale;
    half _Sparkle;
    half _UnderRock;
    half _SoftRim;
    half _GoldLiquid;
CBUFFER_END
#endif
