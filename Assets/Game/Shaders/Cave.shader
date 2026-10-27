Shader "Punchy/Cave"
{
    // Cave walls (CaveSystem): rough rock in two tones broken up by noise, lit by daylight only near the entrance
    // (vertex colour alpha: 1 at the mouth, falling to 0 deep inside) and by any additional lights, such as the
    // player's lantern, everywhere. At the entrance the walls take the surrounding terrain's colour (vertex rgb) and
    // blend into the rock a few metres in (uv.x). The mesh is in world space and double-sided.
    //
    // Each vertex carries its cave's style slot (uv.z) into _PunchyCaveStyles, the table CaveSystem uploads from the
    // CaveStyle assets: rock colours and banding, a finish (gloss, metal, crystal facets, translucent ice) and glowing
    // veins. Slot 0 is no style, drawn with this material's own colours.
    Properties
    {
        _RockColor ("Rock", Color) = (0.48, 0.44, 0.4, 1)
        _DarkColor ("Crevices", Color) = (0.22, 0.2, 0.18, 1)
        _Darkness ("Light deep inside", Range(0, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockColor;
            half4 _DarkColor;
            half _Darkness;
        CBUFFER_END

        struct CaveAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float4 data : TEXCOORD0;   // x terrain blend, y rock tint, z style slot
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float2 data : TEXCOORD3;
                nointerpolation float style : TEXCOORD4;
            };

            // Five vectors per style slot (CaveStyle.Pack): rock rgb + strata, dark rgb + strata frequency,
            // glow rgb + cover, (gloss, metallic, facets, translucency), (glow scale, glow everywhere).
            float4 _PunchyCaveStyles[32 * 5];

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.data = input.data.xy;
                o.style = input.data.z;
                return o;
            }

            // Integer hash of a lattice cell → three values in -0.5..0.5 (for crystal facets far from the origin).
            float3 FacetHash(int3 c)
            {
                uint h = asuint(c.x) * 0x8da6b343u ^ asuint(c.y) * 0xd8163841u ^ asuint(c.z) * 0xcb1ab31fu;
                h ^= h >> 13; h *= 0x85ebca6bu; h ^= h >> 16;
                uint3 v = uint3(h, h * 0x27d4eb2du, h * 0x165667b1u);
                return float3(v >> 8) * (1.0 / 16777216.0) - 0.5;
            }

            // Distance to the nearest border between random cells (second-nearest minus nearest feature point): 0 on
            // a border, growing towards a cell's middle.
            float CellEdge(float3 q)
            {
                int3 c = int3(floor(q));
                float f1 = 9, f2 = 9;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int3 cell = c + int3(x, y, z);
                    float d = distance(q, float3(cell) + 0.5 + FacetHash(cell) * 0.9);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                return f2 - f1;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Normals come from the rock's shape and always face into the cave, whichever way a triangle winds.
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;
                float openness = saturate(input.color.a);

                // The cave's style (slot 0: this material's colours, plain rock).
                int slot = clamp((int)round(input.style), 0, 31);
                float4 s0 = float4(_RockColor.rgb, 0.3), s1 = float4(_DarkColor.rgb, 0.366), s2 = 0, s3 = 0, s4 = float4(1, 0, 0, 0);
                if (slot > 0)
                {
                    s0 = _PunchyCaveStyles[slot * 5];
                    s1 = _PunchyCaveStyles[slot * 5 + 1];
                    s2 = _PunchyCaveStyles[slot * 5 + 2];
                    s3 = _PunchyCaveStyles[slot * 5 + 3];
                    s4 = _PunchyCaveStyles[slot * 5 + 4];
                }
                float gloss = s3.x, metallic = s3.y, facets = s3.z, translucency = s3.w;

                // Crystal: each lattice cell of the wall tilts its normal its own way, so flat faces flash in turn.
                if (facets > 0)
                    n = normalize(n + facets * 1.4 * FacetHash(int3(floor(p * 1.7))));

                // Rock: two tones in blotches and strata, darker in the hollows (rgb carries a per-vertex tint).
                float blotch = PunchyValueNoise(p.xz * 0.35 + p.y * 0.2) * 0.6 + PunchyValueNoise(p.xy * 1.3 + p.z) * 0.4;
                float strata = 0.5 + 0.5 * sin(p.y * s1.w * 6.2832 + PunchyValueNoise(p.xz * 0.2) * 4);
                half3 rock = lerp(s1.rgb, s0.rgb, saturate(blotch * 0.8 + strata * s0.w)) * input.data.y;
                // Where it stands in for the terrain, speckle it like the terrain's own close-up detail.
                half3 ground = input.color.rgb * (0.7 + 0.45 * blotch);
                float terrain = saturate(input.data.x);
                half3 albedo = lerp(rock, ground, terrain);
                // The finish belongs to the rock, not to the terrain colour near the mouth.
                gloss *= 1 - terrain;
                metallic *= 1 - terrain;
                translucency *= 1 - terrain;
                half3 diffuse = albedo * (1 - 0.7 * metallic);
                half3 specColor = lerp(half3(0.9, 0.9, 0.9), albedo * 1.6, metallic);
                float shininess = lerp(12, 140, saturate(gloss + facets * 0.5));
                float3 view = normalize(GetCameraPositionWS() - p);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Daylight reaches only near the entrance. Openness is per vertex, so this branch is coherent: deep
                // inside, whole wavefronts skip the sun's shadow taps.
                half3 light = SampleSH(n) * lerp(_Darkness, 1, openness);
                if (openness > 0.001)
                {
                    Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                    light += sun.color * saturate(dot(n, sun.direction)) * sun.shadowAttenuation * openness;
                }
                half3 shine = 0;
                half3 through = 0;

                // Lanterns and other lights work everywhere: diffuse, highlights on polished, wet or crystal rock, and
                // light soaking through ice.
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, p, half4(1, 1, 1, 1));
                    half3 lit = l.color * l.distanceAttenuation * l.shadowAttenuation;
                    light += lit * saturate(dot(n, l.direction) * 0.8 + 0.2);
                    float3 h = normalize(l.direction + view);
                    shine += lit * pow(saturate(dot(n, h)), shininess) * saturate(gloss + metallic * 0.6 + facets * 0.8);
                    through += lit * (0.35 + 0.65 * saturate(0.5 - 0.5 * dot(n, l.direction)));
                LIGHT_LOOP_END

                half3 color = diffuse * light + specColor * shine + s0.rgb * through * translucency * 0.6;
                // The atmosphere (fog and weather marches) only where daylight reaches; deep inside it would be lerped away.
                if (openness > 0.001)
                    color = lerp(color, PunchyAtmosphere(color, p), openness);

                // Light the rock gives off: a share everywhere (ice, faintly lit crystal), the rest in cracks. Cracks are
                // the borders between random 3D cells (polygonal, like cooling lava crust or fractured ice), and a slow
                // mask leaves whole patches of wall uncracked.
                if (any(s2.rgb > 0))
                {
                    // The mask first: the 27-cell crack search only runs where the mask leaves any crack to draw.
                    float g = s4.x;
                    float crack = smoothstep(0.45, 0.75, PunchyValueNoise(p.xz * 0.13 * g + p.y * 0.11 + 37));
                    if (crack > 0 && terrain < 1)
                        crack *= 1 - smoothstep(s2.w * 0.4, s2.w * 1.6, CellEdge(p * 0.6 * g));
                    color += s2.rgb * (s4.y + crack * (1 - s4.y)) * (1 - terrain);
                }
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(CaveAttributes input) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(input.positionOS);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
            }

            half4 Frag() : SV_Target { return 0; }
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

            float4 Vert(CaveAttributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS); }
            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // Needed so the camera depth texture (built from the depth-normals prepass) includes caves.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
