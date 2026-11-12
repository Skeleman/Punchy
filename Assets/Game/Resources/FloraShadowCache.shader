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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "../Shaders/WideShadow.hlsl"

            TEXTURE2D_FLOAT(_FloraCacheTex);
            SAMPLER(sampler_FloraCacheTex);
            float4 _FloraCacheTex_TexelSize;
            float4x4 _FloraCacheMatrix;   // world to the cache's uv
            float4 _FloraReceiveSun;      // xyz the way the sunlight travelled when the cache was laid
            float4 _FloraCacheParams;     // xyz the camera, w how much of the direct sunlight a cached shadow takes
            float4 _FloraCacheFade;       // fades in over x..y and out over z..w (m from the camera)
            // R-16 row 3: the cache before the last redraw, blended out over about a second so shadows slide instead
            // of jumping each time the cache is laid again. _FloraCacheBlend.x: 1 = done, the old cache isn't read.
            TEXTURE2D_FLOAT(_FloraCacheOldTex);
            float4x4 _FloraCacheOldMatrix;
            float4 _FloraReceiveOldSun;
            float4 _FloraCacheBlend;

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

            // 0..1: how much of one cache's texels round c stand between this point and the sun (four taps). Four taps
            // a texel apart soften the edge (the cache's texels are about 26 cm at the 120 m range): the farthest a tap
            // reads from the pixel is about a third of a metre sideways, hence 0.35 m per unit of slope.
            float CacheTaps(TEXTURE2D_PARAM(map, mapSampler), float2 c, float3 sun, float3 positionWS, float3 normalWS)
            {
                float facing = dot(normalWS, -sun);
                if (facing < 0.05)
                    return 0;
                float slope = min(sqrt(saturate(1 - facing * facing)) / facing, 8);
                float along = dot(positionWS, sun) - (0.4 + 0.35 * slope);
                float t = _FloraCacheTex_TexelSize.x * 0.75;
                return (SAMPLE_TEXTURE2D_LOD(map, mapSampler, c + float2(-t, -t), 0).r < along ? 0.25 : 0)
                     + (SAMPLE_TEXTURE2D_LOD(map, mapSampler, c + float2(t, -t), 0).r < along ? 0.25 : 0)
                     + (SAMPLE_TEXTURE2D_LOD(map, mapSampler, c + float2(-t, t), 0).r < along ? 0.25 : 0)
                     + (SAMPLE_TEXTURE2D_LOD(map, mapSampler, c + float2(t, t), 0).r < along ? 0.25 : 0);
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
                float2 o = mul(_FloraCacheOldMatrix, float4(positionWS, 1)).xy;

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
                float shade = CacheTaps(TEXTURE2D_ARGS(_FloraCacheTex, sampler_FloraCacheTex), c, _FloraReceiveSun.xyz, positionWS, normalWS);
                if (_FloraCacheBlend.x < 1)
                {
                    float old = all(o > 0) && all(o < 1)
                        ? CacheTaps(TEXTURE2D_ARGS(_FloraCacheOldTex, sampler_FloraCacheTex), o, _FloraReceiveOldSun.xyz, positionWS, normalWS) : 0;
                    shade = lerp(old, shade, _FloraCacheBlend.x);
                }
                if (shade <= 0)
                    return 1;
                // R-16 (shadows darker in the cache band than near or far): the shade takes only the share of the light
                // that comes straight from the sun, as the real-time and the wide shadows do, so all three tiers keep
                // the sky's blue in them instead of going brown. Where the real-time or the wide shadow already dims
                // this point the darker one wins (they never stack). The real-time term fades out as URP fades its own
                // shadows near their distance, or the band where both had let go was left lit.
                float realtime = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
                realtime = lerp(realtime, 1, GetMainLightShadowFade(positionWS));
                half wide = PunchyWideShadow(positionWS, normalWS);
                half cached = shade * fade * _FloraCacheParams.w;
                half sunlit = Luminance(_MainLightColor.rgb) * saturate(facing);
                half ambient = Luminance(SampleSH(normalWS));
                half kept = min(realtime, 1 - wide);
                half before = ambient + sunlit * kept;
                half after = ambient + sunlit * min(kept, 1 - cached);
                return half4((after / max(before, 1e-3)).xxx, 1);
            }
            ENDHLSL
        }
    }
}
