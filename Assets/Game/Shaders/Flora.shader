Shader "Punchy/Flora"
{
    // Procedural plants (FloraSystem): vertex-coloured, wind-blown, with soft wrapped lighting and light shining
    // through leaves and petals, a waxy sheen on cacti, snow on upward faces where the ground is snowy, wetness,
    // cloud shadows and the world atmosphere. Double-sided, since leaves and petals are single triangles.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Flora.hlsl"
            #include "Glow.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // rgb albedo, a ambient occlusion
                float4 extra      : TEXCOORD3;   // x part, y fade, z snow, w random
                float2 cardUV     : TEXCOORD4;   // leaf cards' atlas UV
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionWS = v.positionWS;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.normalWS = v.normalWS;
                o.color = half4(input.color.rgb, input.data.w);
                float part = input.data.y;
                if (FloraCard(part, o.cardUV))
                    part = 6;
                o.extra = float4(part, v.fade, v.plant.snow, v.plant.random);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.extra.y, input.positionWS);
                int part = (int)round(input.extra.x);
                bool card = part == 6;
                half cardShade = 1;
                if (card)
                {
                    FloraCardClip(input.cardUV);
                    cardShade = SAMPLE_TEXTURE2D(_PunchyLeafAtlas, sampler_PunchyLeafAtlas, input.cardUV).r;
                }
                bool leafy = part == 1 || part == 2 || part == 5 || card;
                bool metal = part == 4;
                half3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Per-plant tint: brightness, and leaves a little yellower or bluer.
                half r = input.extra.w;
                half3 albedo = input.color.rgb * (0.88 + 0.24 * r);
                if (part == 1 || card)
                    albedo *= lerp(half3(0.95, 1.0, 1.06), half3(1.1, 1.04, 0.82), r);
                // Leaf cards: each leaf a little lighter towards its tip, darker along the vein (from the atlas).
                albedo *= lerp(1, cardShade * 1.1, card ? 0.6 : 0);

                // Snow settles on upward faces where the ground under the plant is snowy.
                half snow = saturate(input.extra.z * 1.5) * smoothstep(0.15, 0.6, n.y);
                albedo = lerp(albedo, half3(0.86, 0.88, 0.92), snow);

                half wet = PunchyWetnessAt(input.positionWS.xz).r * (1 - snow);
                albedo *= lerp(1, part == 0 ? 0.62 : 0.82, wet);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                // Underground (cave plants, seen from a cave) the sun and sky don't reach; the player's lantern does.
                shadow *= 1 - _PunchyUnderground;
                half3 sky = SampleSH(n) * lerp(1, 0.06, _PunchyUnderground);
                // Glowing plants and molten pools nearby (their own glow included, softly).
                sky += PunchyGlowLight(input.positionWS, n);
                if (_PunchyLantern.w > 0)
                {
                    float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                    float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                    sky += _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * saturate(dot(n, normalize(toLantern)) * 0.7 + 0.3);
                }
                half ndl = dot(n, light.direction);
                half diffuse = leafy ? saturate(ndl * 0.6 + 0.4) : saturate(ndl * 0.8 + 0.2);
                half through = leafy ? pow(saturate(dot(-v, light.direction)), 4) * 0.55 : 0;
                half ao = input.color.a;

                // Metal leaves: dim diffuse, the colour mostly comes from tinted reflections.
                half3 color = albedo * (light.color * shadow * (diffuse + through) * lerp(0.6, 1, ao) + sky * ao) * (metal ? 0.45 : 1);
                if (metal)
                {
                    // Tinted reflections of the sky, a sharp sun highlight, and glints that twinkle as the leaves move
                    // and the view changes.
                    half3 h = normalize(light.direction + v);
                    float3 r3 = reflect(-v, n);
                    r3.y = max(r3.y, 0.05);
                    color += albedo * PunchySkyColor(r3, false, true) * 0.35 * ao * lerp(1, 0.06, _PunchyUnderground);
                    color += albedo * light.color * shadow * pow(saturate(dot(n, h)), 90) * 3;
                    float3 cell = floor(input.positionWS * 7);
                    float sparkle = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                    float facing = pow(saturate(dot(normalize(n + (sparkle - 0.5) * 0.9), h)), 400);
                    color += light.color * shadow * facing * 14 * step(0.55, sparkle);
                }
                // Bark and rock take a faint reflection of the sky at grazing angles, as the terrain does (its
                // surfaces have a little smoothness), so rocks fade into the same sky-tinted haze as the land. Metal
                // and crystal too: dark ones (obsidian) otherwise stay black specks on a distant hazy slope.
                if (part == 0 || metal)
                {
                    half fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                    color += SampleSH(reflect(-v, n)) * fresnel * 0.35 * ao * lerp(1, 0.06, _PunchyUnderground);
                }

                // Glowing parts shine by themselves with a slow, uneven pulse (bright enough to bloom at night).
                if (part == 5)
                {
                    float pulse = 0.75 + 0.25 * sin(_Time.y * 1.3 + r * 40 + input.positionWS.y * 2);
                    // Brighter in the dark of a cave (eyes adjusted to the dark see the glow stand out).
                    color += albedo * 1.6 * pulse * lerp(1, 2.5, _PunchyUnderground);
                }

                // A waxy sheen on cacti (and on anything wet).
                half gloss = part == 3 ? 0.35 : wet * 0.5;
                if (gloss > 0)
                {
                    half3 h = normalize(light.direction + v);
                    color += light.color * shadow * pow(saturate(dot(n, h)), part == 3 ? 24 : 60) * gloss * 0.3 * ao;
                }

                color = PunchyAtmosphere(color, input.positionWS);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Flora.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                Varyings o;
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, v.normalWS, lightDirectionWS)));
                // Leaf cards cast no shadow (the crown core does): moved outside the clip volume, so the shadow
                // pass never runs their alpha test.
                float2 cardUV;
                if (FloraCard(input.data.y, cardUV))
                    o.positionCS = float4(2, 2, 2, 1);
                o.fade = v.fade;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Shadows don't crossfade (dithered shadows show as dotted patches on the ground): a plant changing
                // LOD keeps casting with its outgoing level until the change is complete.
                clip(input.fade >= 0 && input.fade < 1 ? -1 : 1);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 card : TEXCOORD2;   // xy atlas UV, z 1 on a leaf card
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.card.z = FloraCard(input.data.y, o.card.xy) ? 1 : 0;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
                if (input.card.z > 0.5)
                    FloraCardClip(input.card.xy);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and normals for the depth-normals prepass (SSAO). With it, the camera depth texture comes from this
        // prepass, so without this pass plants would be missing from it (and from SSAO and the heat haze).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Flora.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float3 card : TEXCOORD3;   // xy atlas UV, z 1 on a leaf card
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.normalWS = v.normalWS;
                o.card.z = FloraCard(input.data.y, o.card.xy) ? 1 : 0;
                return o;
            }

            void Frag(Varyings input, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
                if (input.card.z > 0.5)
                    FloraCardClip(input.card.xy);
                // Leaves are double-sided: the back face's normal points the other way.
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(front ? n : -n, 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
