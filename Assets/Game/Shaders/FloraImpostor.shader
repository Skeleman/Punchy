Shader "Punchy/FloraImpostor"
{
    // R-7: octahedral impostors for far flora (see FloraImpostor.hlsl and FloraSystem.Impostors.cs). The Bake pass
    // draws a model once per frame of the grid (instanced, one instance per frame) into its tile of the atlases.
    Properties
    {
        // Not a real setting: keeps these draws off the SRP Batcher, as Flora.shader's.
        [HideInInspector] _FloraNoBatcher("", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // Only drawn by FloraSystem's bake (a command buffer naming this pass), never by a camera.
            Name "ImpostorBake"
            Tags { "LightMode" = "PunchyImpostorBake" }
            Cull Off
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"
            #include "FloraImpostor.hlsl"

            /// The model's bounding sphere: centre height, radius (FloraModel.CentreY, Radius).
            float2 _ImpostorSphere;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalOS : TEXCOORD0;
                half4 color : TEXCOORD1;    // rgb albedo, a occlusion
                float2 depth : TEXCOORD2;   // x depth along the view (-1..1), y leafy
                float4 moss : TEXCOORD3;    // FloraMossData
                nointerpolation float3 view : TEXCOORD4;
            };

            Varyings Vert(FloraAttributes input)
            {
                // Instance i draws frame (i % frames, i / frames), looking at the model from that frame's direction,
                // orthographic over the bounding sphere, into that frame's tile.
                float frames = _ImpostorFrames;
                float2 frame = float2(input.instanceID % (uint)frames, input.instanceID / (uint)frames);
                float3 d = ImpostorFrameDirection(frame, frames, _ImpostorFull > 0.5);
                float3 right, up;
                ImpostorBasis(d, right, up);
                float3 p = (input.positionOS - float3(0, _ImpostorSphere.x, 0)) / _ImpostorSphere.y;
                float2 local = float2(dot(p, right), dot(p, up)) * 0.5 + 0.5;
                float2 uv = (frame + local) / frames;
                float depth = dot(p, d);   // towards the eye is nearer

                Varyings o;
                float2 xy = uv * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                xy.y = -xy.y;   // so the atlas is sampled at the same uv it was drawn at
                #endif
                #if UNITY_REVERSED_Z
                o.positionCS = float4(xy, saturate(depth * 0.5 + 0.5), 1);
                #else
                o.positionCS = float4(xy, -depth, 1);
                #endif
                o.normalOS = input.normalOS;
                o.color = half4(input.color.rgb, input.data.w);
                int part = (int)round(input.data.y);
                o.depth = float2(depth, part == 1 || part == 2 || part == 5 ? 1 : 0);
                o.moss = FloraMossData(input);
                o.view = d;
                return o;
            }

            void Frag(Varyings input, out half4 albedo : SV_Target0, out half4 data : SV_Target1)
            {
                FloraMossClip(input.moss);
                // Double-sided like the mesh: the side facing this frame's eye.
                float3 n = SafeNormalize((float3)input.normalOS);
                if (dot(n, input.view) < 0)
                    n = -n;
                albedo = half4(input.color.rgb, 1);
                data = half4(ImpostorPackNormal(n), input.depth.x * 0.5 + 0.5, saturate(input.color.a) * 0.49 + input.depth.y * 0.51);
            }
            ENDHLSL
        }

        // Far plants (FloraSystem's impostor draw, after LOD2): one camera-facing quad per plant that blends the three
        // frames nearest its view, reconstructs where on the plant each pixel is from the baked depth (so it sorts and
        // takes the sun's shadow like the mesh) and lights it as Flora.shader lights the mesh, from the baked normal.
        // No shadow caster: at this range the plants' shadows come from the wide shadow cache (R-5/R-6) or not at all.
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ImpostorVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "WideShadow.hlsl"
            #include "Flora.hlsl"
            #include "Grounding.hlsl"
            #include "Glow.hlsl"
            #define FLORA_IMPOSTOR_DRAW
            #include "FloraImpostor.hlsl"

            half4 Frag(ImpostorVaryings input, out float depth : IMPOSTOR_DEPTH) : SV_Target
            {
                ImpostorSample surface = ImpostorSurface(input);
                float3 positionWS = surface.positionWS;
                depth = ImpostorDepth(positionWS);
                FloraDither(input.positionCS, input.plant.y, positionWS);

                // As Flora.shader's Frag, for what an impostor knows: leaves or not, no parts, moss or windows.
                // (R-10 hook: buildings' lit windows would add a third atlas, an emissive mask baked from the window
                // part, sampled with the other two, and Flora.shader's window hour here.)
                bool leafy = surface.leafy;
                half3 n = SafeNormalize(surface.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
                half r = input.plant.z;
                uint g = asuint(input.ground.y);
                half4 ground = half4(g & 255, (g >> 8) & 255, (g >> 16) & 255, g >> 24) / 255.0;
                bool rockTint = ground.a > 0.998;
                bool greenTint = ground.a > 0.99 && !rockTint;
                half3 albedo = surface.albedo * (0.88 + 0.24 * r);
                if (leafy)
                    albedo *= lerp(half3(0.95, 1.0, 1.06), half3(1.1, 1.04, 0.82), r);
                half snow = min(saturate(input.plant.w * 1.5) * smoothstep(0.3, 0.7, n.y) * 0.85, 0.7);
                albedo = lerp(albedo, half3(0.78, 0.8, 0.85), snow);
                half wet = PunchyWetnessAt(positionWS.xz).r * (1 - snow);
                if (!leafy && rockTint)
                {
                    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
                    half rockLum = max(dot(ground.rgb, half3(0.3, 0.59, 0.11)), 0.02);
                    albedo = lerp(albedo, ground.rgb * clamp(lum / rockLum, 0.45, 1.6), 0.85);
                }
                if (leafy && greenTint)
                {
                    half green = saturate((albedo.g - max(albedo.r, albedo.b)) / max(albedo.g, 0.02) * 4);
                    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
                    half groundLum = max(dot(ground.rgb, half3(0.3, 0.59, 0.11)), 0.02);
                    albedo = lerp(albedo, ground.rgb * clamp(lum / groundLum, 0.2, 1.6), 0.6 * green);
                }
                albedo *= lerp(1, leafy ? 0.82 : 0.62, wet);

                Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = min(light.shadowAttenuation, 1 - PunchyWideShadow(positionWS, half3(0, 1, 0))) * (1 - PunchyCloudShadow(positionWS));
                shadow *= (1 - _PunchyUnderground) * (1 - _FloraInCave);
                half3 sky = SampleSH(n) * lerp(1, 0.06, max(_PunchyUnderground, _FloraInCave * 0.75));
                sky += PunchyGlowLight(positionWS, n);
                if (_PunchyLantern.w > 0)
                {
                    float3 toLantern = _PunchyLantern.xyz - positionWS;
                    float reach = saturate(1 - length(toLantern) / _PunchyLanternColor.w);
                    sky += _PunchyLanternColor.rgb * _PunchyLantern.w * reach * reach * saturate(dot(n, normalize(toLantern)) * 0.7 + 0.3);
                }
                half ndl = dot(n, light.direction);
                half diffuse = leafy ? saturate(ndl * 0.6 + 0.4) : saturate(ndl * 0.8 + 0.2);
                half through = leafy ? pow(saturate(dot(-v, light.direction)), 4) * 0.55 : 0;
                half ao = surface.occlusion * PunchyGrounding(positionWS.y - input.ground.x, n.y, leafy ? 0 : 1);
                half3 color = albedo * (light.color * shadow * (diffuse + through) * lerp(0.6, 1, ao) + sky * ao);
                // Bark and rock: the mesh's faint sky reflection at grazing angles (Flora.shader), at its far strength, so
                // a far trunk or rock fades into the haze as the mesh beside it does and the hand-off keeps its tone (R-11).
                if (!leafy)
                {
                    half fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                    half sheenFar = lerp(0.25, 1, smoothstep(30, 120, distance(positionWS, GetCameraPositionWS())));
                    color += SampleSH(reflect(-v, n)) * fresnel * 0.35 * sheenFar * ao * lerp(1, 0.06, _PunchyUnderground);
                }
                color = PunchyAtmosphere(color, positionWS);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ImpostorVert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Flora.hlsl"
            #define FLORA_IMPOSTOR_DRAW
            #include "FloraImpostor.hlsl"

            half Frag(ImpostorVaryings input, out float depth : IMPOSTOR_DEPTH) : SV_Target
            {
                ImpostorSample surface = ImpostorSurface(input);
                depth = ImpostorDepth(surface.positionWS);
                FloraDither(input.positionCS, input.plant.y, surface.positionWS);
                return depth;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ImpostorVert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Flora.hlsl"
            #define FLORA_IMPOSTOR_DRAW
            #include "FloraImpostor.hlsl"

            void Frag(ImpostorVaryings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                , out float depth : IMPOSTOR_DEPTH
            )
            {
                ImpostorSample surface = ImpostorSurface(input);
                depth = ImpostorDepth(surface.positionWS);
                FloraDither(input.positionCS, input.plant.y, surface.positionWS);
                outNormalWS = half4(SafeNormalize(surface.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
