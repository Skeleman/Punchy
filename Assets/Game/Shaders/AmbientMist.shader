Shader "Punchy/Ambient Mist"
{
    // Soft ambient particles (fog wisps, drifting ash): translucent puffs lit by the sun and sky, faded where they
    // touch the ground, and seen through the atmosphere. Types with Volume > 0 (dune dust) draw as soft volumes:
    // a billowing noise shape, lit as a sphere from the sun with a darker underside, and faded softly into the ground.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "AmbientMist"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Sky.hlsl"
            #include "AmbientParticles.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float visibility : TEXCOORD2;
                float4 screen : TEXCOORD3;
                float3 tint : TEXCOORD4;
                float2 sizeRandom : TEXCOORD5;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                ParticleVertex p = BuildParticle(vertex, instance);
                Varyings o;
                o.positionWS = p.positionWS;
                o.positionCS = TransformWorldToHClip(p.positionWS);
                o.uv = p.uv;
                o.visibility = p.visibility;
                o.screen = ComputeScreenPos(o.positionCS);
                o.tint = p.tint;
                o.sizeRandom = float2(p.size, p.random);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                half volume = _ParticleShape.w;
                // Wispy edges: value noise over the puff.
                float wisp = PunchyValueNoise(input.uv * 2.2 + input.positionWS.xz * 0.05) * 0.6 + 0.4;
                half shapeFlat = pow(saturate(1 - r), 1.6) * wisp;

                // Soft volume: a rounded body whose edge is eaten by slowly drifting billows, so it reads as a cloud
                // of dust rather than a disc.
                float2 q = input.uv * 2.0 + input.sizeRandom.y * 37.0 + _PunchyClock * float2(0.03, 0.018);
                float billow = PunchyFbm(q, 4);
                float body = saturate(1 - r * r);
                half shapeVolume = smoothstep(0.0, 0.55, body - (1 - billow) * 0.75);
                half alpha = lerp(shapeFlat, shapeVolume, volume) * _ParticleColor.a * input.visibility;

                // Soft where it meets the ground or anything else (a volume over a wider depth, so it sinks in).
                float2 screenUV = input.screen.xy / input.screen.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.positionCS.w) / lerp(1.5, max(1.5, input.sizeRandom.x * 0.6), volume));
                clip(alpha - 0.002);

                Light light = GetMainLight();
                half sun = 1 - PunchyCloudShadow(input.positionWS);
                // Flat wisps: half the sunlight. Volumes: a sphere normal from the quad (plus a tilt from the billows),
                // wrapped sunlight and a darker, self-shadowed core on the side away from the sun.
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, toCam = UNITY_MATRIX_V[2].xyz;
                float2 bend = input.uv + (billow - 0.5) * 0.6;
                float3 n = normalize(right * bend.x + up * bend.y + toCam * sqrt(saturate(1 - dot(bend, bend)) + 0.05));
                half lit = saturate(dot(n, light.direction) * 0.5 + 0.5);
                half selfShadow = lerp(1, 0.6, shapeVolume * (1 - lit));
                half direct = lerp(0.5, (0.3 + 0.7 * lit) * selfShadow, volume);
                half3 color = input.tint * (light.color * direct * sun + SampleSH(half3(0, 1, 0)));
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
