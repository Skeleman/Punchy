Shader "Punchy/Flora"
{
    // Procedural plants (FloraSystem): vertex-coloured, wind-blown, with soft wrapped lighting and light shining
    // through leaves and petals, a waxy sheen on cacti, snow on upward faces where the ground is snowy, wetness,
    // cloud shadows and the world atmosphere. Double-sided, since leaves and petals are single triangles.
    Properties
    {
        // Not a real setting: a material property outside UnityPerMaterial keeps these draws off the SRP Batcher,
        // which doesn't pass the per-draw unity_BaseCommandID that Flora.hlsl reads its draw's region by.
        [HideInInspector] _FloraNoBatcher("", Float) = 0
        // 1 on CaveSystem's copy: cave plants are lit as underground wherever the camera is (see Frag).
        [HideInInspector] _FloraInCave("", Float) = 0
    }
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
            #include "SharedClock.hlsl"
            // The shared fine detail CaveSystem makes (rg a normal, b a height): grain on rock and bark up close.
            TEXTURE2D(_PunchyCaveDetail);
            SAMPLER(sampler_PunchyCaveDetail);
            float _PunchyCaveDetailReady;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // rgb albedo, a ambient occlusion
                float4 extra      : TEXCOORD3;   // x part, y fade, z snow, w random
                // A window pane's own lighting-up hour (0..1), worked out per vertex and not interpolated, so every pixel
                // of the pane agrees (hashed per pixel from the interpolated colour, single pixels flickered on and off).
                nointerpolation float windowHour : TEXCOORD4;
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionWS = v.positionWS;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.normalWS = v.normalWS;
                o.color = half4(input.color.rgb, input.data.w);
                o.extra = float4(input.data.y, v.fade, v.plant.snow, v.plant.random);
                // Keyed on the pane (each has a slightly different colour) and the building.
                float3 key = round(input.color.rgb * 255);
                o.windowHour = frac(sin(dot(key, float3(0.917, 0.473, 0.131)) * 10 + v.plant.random * 61.7) * 43758.5453);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.extra.y, input.positionWS);
                int part = (int)round(input.extra.x);
                bool leafy = part == 1 || part == 2 || part == 5;
                bool metal = part == 4;
                // Thin tapered strands (roots, stems) can end in a degenerate normal; a NaN there blooms into a bright fleck.
                half3 n = SafeNormalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Per-plant tint: brightness, and leaves a little yellower or bluer.
                half r = input.extra.w;
                half3 albedo = input.color.rgb * (0.88 + 0.24 * r);

                // Rock and bark up close: grain and fine relief from the shared detail texture (the caves' own), read
                // three ways and blended by the surface's facing, fading out with distance.
                float near = 1 - smoothstep(18, 45, distance(input.positionWS, GetCameraPositionWS()));
                if (part == 0 && near > 0.01 && _PunchyCaveDetailReady > 0)
                {
                    float3 q = input.positionWS * 0.9;
                    float3 w = pow(abs(n), 4);
                    w /= w.x + w.y + w.z;
                    half4 dx = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.zy);
                    half4 dy = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xz);
                    half4 dz = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xy);
                    half4 detail = dx * w.x + dy * w.y + dz * w.z;
                    albedo *= 1 + (detail.b - 0.5) * 0.5 * near;
                    half2 bump = (detail.rg - 0.5) * 0.9 * near;
                    n = normalize(n + float3(0, bump.x, bump.y) * w.x + float3(bump.x, 0, bump.y) * w.y + float3(bump.x, bump.y, 0) * w.z);
                }
                if (part == 1)
                    albedo *= lerp(half3(0.95, 1.0, 1.06), half3(1.1, 1.04, 0.82), r);
                // Windows: dark glass by day (their colour is the light they give at night, below).
                half3 windowLight = albedo;
                if (part == 6)
                    albedo = half3(0.05, 0.06, 0.07) + albedo * 0.06;

                // Snow settles on upward faces where the ground under the plant is snowy.
                half snow = saturate(input.extra.z * 1.5) * smoothstep(0.15, 0.6, n.y);
                albedo = lerp(albedo, half3(0.86, 0.88, 0.92), snow);

                half wet = PunchyWetnessAt(input.positionWS.xz).r * (1 - snow);
                albedo *= lerp(1, part == 0 ? 0.62 : 0.82, wet);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                // Underground (cave plants, seen from a cave) the sun and sky don't reach; the player's lantern does.
                // Cave plants are underground whatever the camera's depth: no direct sun (nor light through leaves, nor
                // sheen, which made roots flare against the sun from near a mouth), and only a little of the sky's
                // ambient (the daylight spilling in at a mouth).
                shadow *= (1 - _PunchyUnderground) * (1 - _FloraInCave);
                half3 sky = SampleSH(n) * lerp(1, 0.06, max(_PunchyUnderground, _FloraInCave * 0.75));
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

                // Glass (crystals): a dark clear body lit faintly from inside, a bright rim where the surface turns
                // away whose hue shifts (light bent through it), the sky reflected at grazing angles, and sharp
                // highlights from the sun and the lantern. Drawn solid: the look of glass without its cost.
                if (part == 7)
                {
                    half facingV = saturate(dot(n, v));
                    half rim = pow(1 - facingV, 3);
                    half3 bent = lerp(albedo, albedo.gbr * 0.6 + albedo * 0.6, 0.45);
                    color = albedo * (light.color * shadow * diffuse * 0.25 + sky * 0.45) * ao;
                    color += albedo * (0.12 + 0.25 * (1 - facingV)) * lerp(1, 2.5, max(_PunchyUnderground, _FloraInCave));
                    color += lerp(bent, 1, 0.35) * rim * (SampleSH(reflect(-v, n)) * lerp(1, 0.15, _PunchyUnderground) + sky * 0.6 + 0.08) * 1.4;
                    half3 hSun = normalize(light.direction + v);
                    color += light.color * shadow * pow(saturate(dot(n, hSun)), 320) * 5;
                    if (_PunchyLantern.w > 0)
                    {
                        float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                        float reachL = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                        half3 hL = normalize(normalize(toLantern) + v);
                        color += _PunchyLanternColor.rgb * _PunchyLantern.w * reachL * pow(saturate(dot(n, hL)), 200) * 4;
                    }
                }

                // Glowing parts shine by themselves (bright enough to bloom at night), with a gentle, slow shimmer of
                // the species' own amount (carried as a fraction on the part; 0 for crystals), out of step plant to plant.
                if (part == 5)
                {
                    float amount = saturate((input.extra.x - 5) / 0.4);
                    float pulse = 1 + amount * sin(_PunchyClock * 0.55 + r * 40 + input.positionWS.y * 0.7);
                    // Brighter in the dark of a cave (eyes adjusted to the dark see the glow stand out).
                    color += albedo * 1.6 * pulse * lerp(1, 2.5, _PunchyUnderground);
                }

                // Windows light up as night falls and go out one by one late at night (the deeper the sun, the later):
                // each window keeps its own hour, and a few stay lit all night.
                if (part == 6)
                {
                    // Its own hour (see Varyings.windowHour).
                    float h = input.windowHour;
                    float late = saturate(-_PunchySunDirection.y * 1.6);
                    float on = smoothstep(0.25, 0.55, _PunchyNight) * (h > 0.85 ? 1 : step(late, 0.3 + 0.7 * h));
                    color += windowLight * on * 2.2;
                }

                // A waxy sheen on cacti and egg shells (and on anything wet), from the sun and, near it, the lantern.
                half gloss = part == 3 ? 0.35 : wet * 0.5;
                if (gloss > 0)
                {
                    half3 h = normalize(light.direction + v);
                    color += light.color * shadow * pow(saturate(dot(n, h)), part == 3 ? 24 : 60) * gloss * 0.3 * ao;
                    if (_PunchyLantern.w > 0)
                    {
                        float3 toLantern = _PunchyLantern.xyz - input.positionWS;
                        float reachL = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                        half3 hL = normalize(normalize(toLantern) + v);
                        color += _PunchyLanternColor.rgb * _PunchyLantern.w * reachL * pow(saturate(dot(n, hL)), 40) * gloss * 1.5 * ao;
                    }
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
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
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
            };

            Varyings Vert(FloraAttributes input)
            {
                FloraVertex v = FloraTransform(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionWS);
                o.fade = v.fade;
                o.positionWS = v.positionWS;
                o.normalWS = v.normalWS;
                return o;
            }

            void Frag(Varyings input, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                FloraDither(input.positionCS, input.fade, input.positionWS);
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
