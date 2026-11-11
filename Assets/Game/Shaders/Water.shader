Shader "Punchy/Water"
{
    // Sea, lake and river surfaces. Colour comes from how much water the view ray passes through (read from the
    // camera depth texture): clear and tinted in the shallows, dark further down. Reflections rise at grazing angles
    // (Fresnel): the scene itself where a short screen-space march finds it (banks, trees), the live sky elsewhere.
    // The sun glints, ripples are sums of directional waves, rivers carry their ripples and streaks downstream
    // (faster in the deep middle), and foam lines the shore and the rapids.
    // Two other liquids share the sheet: very murky water (swamps) turns thick and oily, slowly heaving and bubbling,
    // with drifting scum; lava (the lava amount in the vertex) is a black crust churning slowly in flow-noise swirls, orange
    // veins glowing through it, closing up at the edges, never reflective (_GoldLiquid draws it as molten gold instead).
    // (Cave pools use the same path, drawn by CaveSystem.)
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
        // Drawn before the other see-through things (fog wisps, flames, rain, waterfalls): they all stand above the water,
        // and in one queue a big water chunk, sorted by its middle, could be drawn after mist in front of it (Z-10).
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        // (Both passes share the material's constants: the SRP batcher wants them laid out the same in every pass.)
        HLSLINCLUDE
        #include "WaterMaterial.hlsl"
        ENDHLSL

        // The water's depth first (playtest 11-08 item 4): the swell lifts the sea into waves that hide each other at low
        // angles, and see-through water drawn without depth layered the hidden faces over the near ones in mesh order
        // (and the far side of a crest over its face). This pass lays down only the nearest surface's depth, so the
        // colour pass below shades just that layer per pixel (and skips the hidden ones). It writes the depth buffer
        // only: the camera depth texture the colour pass reads (reflections, refraction, thickness) was copied before.
        Pass
        {
            Name "WaterDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Off
            // (A hair further than the colour pass's own depth, so that pass always passes its own test.)
            Offset 1, 1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            // R-9: on for the fine sheet near the player (WaterPatchSystem.cs, WaterField.hlsl).
            #pragma multi_compile_local _ _WATER_FIELD
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "WaterDepthPass.hlsl"
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // R-9: on for the fine sheet near the player (WaterPatchSystem.cs, WaterField.hlsl).
            #pragma multi_compile_local _ _WATER_FIELD
            #include "WaterForwardPass.hlsl"
            ENDHLSL
        }
    }
}
