Shader "Hidden/Punchy/WaterTess"
{
    // The fine sheet near the player drawn with hardware tessellation (profiler prototype, Zach 11-11): Water.shader's
    // two passes (shared through WaterDepthPass.hlsl and WaterForwardPass.hlsl) with the wave field on and hull and
    // domain stages between (WaterTess.hlsl). WaterPatchSystem swaps the sheet's material to it with -waterfieldtess 1.
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.10, 0.42, 0.45, 1)
        _DeepColor("Deep Color", Color) = (0.01, 0.07, 0.15, 1)
        _Absorption("Colour Absorption (per metre)", Float) = 0.18
        _Opacity("Opacity (per metre)", Float) = 0.45
        _FoamColor("Foam Color", Color) = (0.9, 0.95, 1, 1)
        _FoamDistance("Shore Foam Distance (m)", Float) = 0.8
        _WaveScale("Wave Frequency (per metre)", Float) = 0.35
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.25
        _WaveSpeed("Wave Speed", Float) = 0.8
        _FlowSpeed("River Flow Speed (m/s)", Float) = 1.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        _ScreenReflection("Screen-Space Reflection", Range(0, 1)) = 1
        _DetailStrength("Fine Ripples", Range(0, 1)) = 0.5
        _DetailScale("Fine Ripple Repeats (per metre)", Float) = 0.4
        _Sparkle("Sun Sparkle", Range(0, 1)) = 0.6
        // 1 on CaveSystem's copy for cave pools and streams: always under rock, whatever the camera's depth.
        [HideInInspector] _UnderRock("Under Rock", Float) = 0
        // 1 for white pools (CaveSystem, per draw): the pool fades out towards its rim (the disc mesh's depth) instead of
        // ending in a hard edge, and its glow with it.
        [HideInInspector] _SoftRim("Soft Rim", Float) = 0
        // Lava drawn as molten gold instead (the 11-08 lava look, kept for a gold liquid later: playtest 11-08 item 37).
        // Nothing sets it yet; a material copy with it on draws its lava gold.
        [ToggleUI] _GoldLiquid("Gold Liquid (lava draws as molten gold)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Assets/Game/Shaders/WaterMaterial.hlsl"
        ENDHLSL

        Pass
        {
            Name "WaterDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Off
            Offset 1, 1

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment DepthFrag
            #define _WATER_FIELD 1
            #include "Assets/Game/Shaders/WaterDepthPass.hlsl"
            #define PUNCHY_TESS_INPUT DepthAttributes
            #define PUNCHY_TESS_OUTPUT DepthVaryings
            #define PUNCHY_TESS_VERT DepthVert
            #define PUNCHY_TESS_EXTRA
            #include "Assets/Game/Shaders/WaterTess.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define _WATER_FIELD 1
            #include "Assets/Game/Shaders/WaterForwardPass.hlsl"
            #define PUNCHY_TESS_INPUT WaterAttributes
            #define PUNCHY_TESS_OUTPUT Varyings
            #define PUNCHY_TESS_VERT Vert
            #define PUNCHY_TESS_EXTRA PUNCHY_TESS_LERP(look); PUNCHY_TESS_LERP(glow);
            #include "Assets/Game/Shaders/WaterTess.hlsl"
            ENDHLSL
        }
    }
}
