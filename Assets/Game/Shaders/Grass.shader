Shader "Punchy/Grass Blades"
{
    // Grass blades for GroundDetailSystem: procedural, double-sided, wind-blown, lit with soft wrap lighting,
    // light shining through the blades, cloud shadows, wetness and the world atmosphere.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // R-32 row 2: marks blade pixels (a URP user stencil bit) so the flora cache receiver lights them with the
            // ground's up normal instead of a normal rebuilt from the blades' noisy depth.
            Stencil { Ref 8 WriteMask 8 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // R-25: no Forward+ variant here (its cost, profiler job 45j); real lights come from LightBudget's list.
            #define PUNCHY_NEAR_REAL_LIST

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Atmosphere.hlsl"
            #include "Glow.hlsl"
            #include "WideShadow.hlsl"
            #define PUNCHY_SURFACE_REAL_LIGHTS
            #define PUNCHY_SURFACE_SHADOW_FADE
            #include "Surface.hlsl"
            #include "GroundDetail.hlsl"
            #include "GrassShell.hlsl"

            StructuredBuffer<Blade> _Blades;
            // R-31 row 1: blades drawn flat magenta for GrassCoverageProbe's coverage count.
            float _PunchyGrassProbe;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // rgb albedo, a = height fraction along the blade
                float  grass      : TEXCOORD3;   // R-31 shells (prototype c): grass density for the strand cut
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Varyings o;
                float t;
                float3 normal;
                if (_ShellMode > 0)
                {
                    half3 albedo;
                    BuildShellVertex(instance, vertex, o.positionWS, normal, t, o.grass, albedo);
                    o.color = half4(albedo, t);
                }
                else
                {
                    Blade blade = _Blades[instance];
                    BuildBladeVertex(blade, instance, vertex, o.positionWS, normal, t);
                    half3 albedo = blade.color.rgb;
                    if (_GrassProto == 4)
                        albedo *= GrassClumpTint(blade.position.xz);
                    o.color = half4(albedo, t);
                    o.grass = 1;
                }
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = normal;
                return o;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                if (_ShellMode > 0)
                    ShellClip(input.positionWS, input.color.a, input.grass);
                if (_PunchyGrassProbe > 0)
                    return half4(1, 0, 1, 1);
                half3 n = normalize(frontFace ? input.normalWS : -input.normalWS);
                n = normalize(n + half3(0, 0.6, 0));   // soften: blades read as a canopy, not as flat cards
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half t = input.color.a;

                // Darker in the shaded base of the tuft, lighter at the tips. R-31 prototypes: roots towards the
                // ground's own tone (_GrassProtoShape.y; shells barely darken), tips lighter.
                half rootMatch = _ShellMode > 0 ? 0.8 : _GrassProtoShape.y;
                half3 albedo = input.color.rgb * lerp(lerp(0.45, 0.95, rootMatch), lerp(1.15, 1.3, rootMatch), t);
                // Undo the blades' green lift at the root, so it meets the ground's colour.
                albedo = lerp(albedo, albedo * half3(1.03, 0.85, 1.14), rootMatch * (1 - t));
                albedo *= lerp(1, 0.8, PunchyWetnessAt(input.positionWS.xz).r);

                // R-32 row 2: the same shadow tiers as the ground under the blades. URP's shadow fade (the ground's
                // UniversalFragmentPBR fades its real-time shadow the same way), then the wide shadow past it, the darker
                // of the two winning as on the terrain. The flora cache tier is laid on top by its receiver.
                // (Surface.hlsl: PUNCHY_SURFACE_SHADOW_FADE for the fade, the wide map biased straight up like plants.)
                PunchySurface ps = PunchySurfaceAt(input.positionWS, n);
                ps.wideNormalWS = half3(0, 1, 0);
                ps.positionCS = input.positionCS;
                PunchyLit lit = PunchySurfaceLight(ps);
                Light light = lit.sun;
                half shadow = lit.shadow;
                half wrap = saturate(dot(n, light.direction) * 0.6 + 0.4);
                half through = pow(saturate(dot(-v, light.direction)), 4) * t * 0.6;   // sunlight through the blade
                through *= _GrassProto == 4 ? 1.8 * t : 1;                              // d: backlit tips glow
                // Glowing plants and molten pools nearby light the grass round them too.
                half3 color = albedo * (light.color * shadow * (wrap + through) + lit.ambient + lit.glow + lit.lights);

                color = PunchySurfaceFinish(color, ps);
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GroundDetail.hlsl"
            #include "GrassShell.hlsl"

            StructuredBuffer<Blade> _Blades;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 shell      : TEXCOORD1;   // R-31 shells: height fraction, grass density
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Varyings o;
                float3 normal;
                float t, grass = 1;
                half3 albedo;
                if (_ShellMode > 0)
                    BuildShellVertex(instance, vertex, o.positionWS, normal, t, grass, albedo);
                else
                    BuildBladeVertex(_Blades[instance], instance, vertex, o.positionWS, normal, t);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.shell = float2(t, grass);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                if (_ShellMode > 0)
                    ShellClip(input.positionWS, input.shell.x, input.shell.y);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and normals for the depth-normals prepass (SSAO). With depth priming the opaque pass only draws what
        // this prepass drew, so without it the grass would vanish.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "GroundDetail.hlsl"
            #include "GrassShell.hlsl"

            StructuredBuffer<Blade> _Blades;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 shell : TEXCOORD2;   // R-31 shells: height fraction, grass density
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Varyings o;
                float3 normal;
                float t, grass = 1;
                half3 albedo;
                if (_ShellMode > 0)
                    BuildShellVertex(instance, vertex, o.positionWS, normal, t, grass, albedo);
                else
                    BuildBladeVertex(_Blades[instance], instance, vertex, o.positionWS, normal, t);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = normal;
                o.shell = float2(t, grass);
                return o;
            }

            void Frag(Varyings input, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                if (_ShellMode > 0)
                    ShellClip(input.positionWS, input.shell.x, input.shell.y);
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
