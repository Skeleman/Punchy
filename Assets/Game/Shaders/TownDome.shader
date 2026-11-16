Shader "Punchy/Town Dome"
{
    // R-10 row 4: the light dome over a lit town at night (SurfaceGlow.Dome.cs), its windows' and street lights' warm
    // light caught in the air. Each town is a box (36 vertices from SV_VertexID, read from a structured buffer) round a
    // flattened half-ellipsoid on the ground; every pixel adds how much of the ellipsoid its view ray crosses in front
    // of the scene (from the depth texture), thickest through the middle and soft to its edge, so a hill hides it and it
    // shows against the sky. Thinned by the haze like the land behind it, but much less (it's lit air), and faded out as the
    // camera nears the town, where its own lights take over.
    SubShader
    {
        // After the opaque scene (it reads its depth) and after the water (Transparent-10), so a sea or lake behind
        // the town doesn't cover it; the water isn't in the depth texture, so it lights the air over water too.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Front

        Pass
        {
            Name "TownDome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Dome
            {
                float4 at;       // xyz its middle on the ground, w its radius (m)
                float4 light;    // rgb its light, w its height (m)
            };
            StructuredBuffer<Dome> _Domes;
            float4 _DomeParams;          // x brightness (windows on, weather folded in)
            float _PunchyHazeDensity;    // (Sky.hlsl) the haze per metre

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation uint dome : TEXCOORD0;
            };

            Varyings Vert(uint vertex : SV_VertexID)
            {
                static const uint Faces[36] = { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,1,4, 1,5,4, 2,6,3, 3,6,7, 0,4,2, 2,4,6, 1,3,5, 3,7,5 };
                uint index = vertex / 36;
                uint corner = Faces[vertex % 36];
                Dome d = _Domes[index];
                float3 c = float3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
                // (A little below the ground too, for a town on a slope.)
                float3 local = float3(lerp(-1, 1, c.x) * d.at.w, lerp(-0.3, 1, c.y) * d.light.w, lerp(-1, 1, c.z) * d.at.w);
                Varyings o;
                o.positionCS = TransformWorldToHClip(d.at.xyz + local);
                // Its far side can lie past the far plane (3 km) when the town is 2-3 km off: pinned onto it instead of
                // clipped, so the dome still shows (only x, y and w place the pixel; the depth test is off).
                #if UNITY_REVERSED_Z
                    o.positionCS.z = max(o.positionCS.z, 0);
                #else
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w);
                #endif
                o.dome = index;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                Dome d = _Domes[input.dome];
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float raw = SampleSceneDepth(uv);
                float3 camera = _WorldSpaceCameraPos;
                // The view ray through this pixel, and how far along it the scene is (none: the sky).
                #if UNITY_REVERSED_Z
                    float clipZ = raw;
                #else
                    float clipZ = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 scene = ComputeWorldSpacePosition(uv, clipZ, UNITY_MATRIX_I_VP);
                float3 dir = normalize(scene - camera);
                float sceneT = raw == UNITY_RAW_FAR_CLIP_VALUE ? 1e7 : distance(scene, camera);
                // In the ellipsoid's own frame it's the unit sphere: |o + t v| = 1.
                float3 size = float3(d.at.w, d.light.w, d.at.w);
                float3 o = (camera - d.at.xyz) / size;
                float3 v = dir / size;
                float a = dot(v, v), b = dot(o, v), c = dot(o, o) - 1;
                float disc = b * b - a * c;
                if (disc <= 0)
                    return 0;
                float root = sqrt(disc);
                float t0 = max((-b - root) / a, 0), t1 = min((-b + root) / a, sceneT);
                if (t1 <= t0)
                    return 0;
                // How much light the ray gathers: the integral of (1 - r^2) along it, over that of a ray through the
                // middle (4 / (3 sqrt a)), so the dome is 1 at its thickest seen from any side and soft to its edge.
                float3 k = float3(a / 3, b, c);   // 1 - r^2 = -(a t^2 + 2 b t + c)
                float i1 = -(k.x * t1 * t1 * t1 + k.y * t1 * t1 + k.z * t1);
                float i0 = -(k.x * t0 * t0 * t0 + k.y * t0 * t0 + k.z * t0);
                float thick = saturate((i1 - i0) * 0.75 * sqrt(a));
                thick *= thick * thick;
                // Fades in from inside its edge to a little way out, so it's still there while the town's own lights
                // are a thin line at a low angle (400-600 m), and gone where they take over.
                float dist = distance(camera.xz, d.at.xz);
                float near = smoothstep(d.at.w * 0.5, d.at.w * 1.5 + 100, dist);
                // Thinned by the haze, but only a twentieth as much as the land (the haze itself is lit by it), so it
                // still shows from 3 km where the land has gone.
                float h = dist * _PunchyHazeDensity;
                float haze = exp(-h * h * 0.05);
                return half4(d.light.rgb * (thick * near * haze * _DomeParams.x), 0);
            }
            ENDHLSL
        }
    }
}
