Shader "Hidden/Punchy/HazeLut"
{
    // Marches the rain and dust volumes once a frame into the haze table that PunchyAtmosphere reads (see
    // PunchyHazeFromLut in Sky.hlsl): one texel per direction round the horizon and distance from the camera.
    // Rendered by WeatherSystem with Graphics.Blit.
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"

            float2 _HazeLutSize;   // texels across, down

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                // Blit's quad spans 0..1; cover the whole target whichever way up the API draws it.
                Varyings o;
                o.positionCS = float4(input.positionOS.xy * 2 - 1, 0.5, 1);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Pixel coordinates rather than interpolated UVs, so texel (i, j) is always sampled back at uv
                // ((i + 0.5) / width, (j + 0.5) / height) on every graphics API.
                float2 uv = input.positionCS.xy / _HazeLutSize;
                half3 scatter;
                float transmit;
                // Many more steps than the old per-pixel march (8): it's cheap at this size, and neighbouring
                // rows then agree closely enough not to band.
                PunchyWeatherVolumesAlong(_PunchyHazeOrigin, PunchyHazeLutDirection(uv.x), PunchyHazeLutDistance(uv.y), 32, scatter, transmit);
                return half4(scatter, 1 - transmit);
            }
            ENDHLSL
        }
    }
}
