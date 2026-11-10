Shader "Punchy/FloraShadowCache"
{
    // W-107 (FloraShadowOptions.CachedFarShadows): the receiving side of FloraSystem's cached far-tree shadows. FloraSystem
    // draws it as one full-screen triangle after the opaques: each pixel's world position comes back from the depth
    // texture, is looked up in the cache (laid from the sun; each texel keeps how far along the sunlight the nearest tree
    // is) and darkened where a cached tree stands between it and the sun. Pixels the real-time shadows already shade are
    // left alone, so the near range never gets a double shadow. Loaded with Resources.Load (never Shader.Find).
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FloraShadowCache"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_FLOAT(_FloraCacheTex);
            SAMPLER(sampler_FloraCacheTex);
            float4 _FloraCacheTex_TexelSize;
            float4x4 _FloraCacheMatrix;   // world to the cache's uv
            float4 _FloraReceiveSun;      // xyz the way the sunlight travelled when the cache was laid
            float4 _FloraCacheParams;     // xyz the camera, w how dark a cached shadow is
            float4 _FloraCacheFade;       // fades in over x..y and out over z..w (m from the camera)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                return o;
            }

            float Cached(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_FloraCacheTex, sampler_FloraCacheTex, uv, 0).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw <= 1e-6)
                    return 1;
            #else
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                if (raw >= 1 - 1e-6)
                    return 1;
            #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float d = distance(positionWS, _FloraCacheParams.xyz);   // 3D, as FloraCull hands trees over (R-6 row 0)
                float fade = smoothstep(_FloraCacheFade.x, _FloraCacheFade.y, d) * (1 - smoothstep(_FloraCacheFade.z, _FloraCacheFade.w, d));
                if (fade <= 0)
                    return 1;
                float2 c = mul(_FloraCacheMatrix, float4(positionWS, 1)).xy;
                if (any(c < 0) || any(c > 1))
                    return 1;

                // Playtest 11-07 (tents striped at mid range): a fixed bias let steep faces, nearly edge-on to the sun,
                // shade themselves in bands (shadow acne): within a texel and the taps' spread the cached depth changes by
                // more than 0.4 m there. The surface's normal, rebuilt from the depth across neighbouring pixels, scales the
                // bias by the slope against the sun; a face turned away from the sun is dark from its own lighting already.
                float3 normalWS = normalize(cross(ddy(positionWS), ddx(positionWS)));
                if (dot(normalWS, GetCameraPositionWS() - positionWS) < 0)
                    normalWS = -normalWS;
                float facing = dot(normalWS, -_FloraReceiveSun.xyz);
                if (facing < 0.05)
                    return 1;
                float slope = min(sqrt(saturate(1 - facing * facing)) / facing, 8);
                // Four taps a texel apart soften the edge (the cache's texels are about 26 cm at the 120 m range): the
                // farthest a tap reads from the pixel is about a third of a metre sideways, hence 0.35 m per unit of slope.
                float along = dot(positionWS, _FloraReceiveSun.xyz) - (0.4 + 0.35 * slope);
                float t = _FloraCacheTex_TexelSize.x * 0.75;
                float shade = (Cached(c + float2(-t, -t)) < along ? 0.25 : 0) + (Cached(c + float2(t, -t)) < along ? 0.25 : 0)
                            + (Cached(c + float2(-t, t)) < along ? 0.25 : 0) + (Cached(c + float2(t, t)) < along ? 0.25 : 0);
                if (shade <= 0)
                    return 1;
                float realtime = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
                float k = shade * fade * realtime * _FloraCacheParams.w;
                return half4((1 - k).xxx, 1);
            }
            ENDHLSL
        }
    }
}
