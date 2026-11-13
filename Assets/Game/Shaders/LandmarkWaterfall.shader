Shader "Punchy/Landmark Waterfall"
{
    // Waterfall curtains: a sheet, solid white water in the open and see-through at its edges, with streaks rushing down it, whitening into foam as it falls.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LandmarkWaterfall"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"
            #include "LandmarkEffects.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fallLength : TEXCOORD3;
                nointerpolation uint emitter : TEXCOORD4;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                FallVertex f = BuildWaterfall(vertex, instance);
                Varyings o;
                o.positionWS = f.positionWS;
                o.positionCS = TransformWorldToHClip(f.positionWS);
                o.normalWS = f.normalWS;
                o.uv = f.uv;
                o.fallLength = f.fallLength;
                o.emitter = instance;
                return o;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                LandmarkEmitter e = _LandmarkEmitters[input.emitter];
                float2 uv = input.uv;
                // A cave's fall is small and seen close: finer, faster streaks with gaps between them.
                float fine = lerp(1.0, 3.5, e.underRock);
                // A landmark's or river's fall runs its whole length in half the effect's Lifetime (Z-35: it used to
                // crawl at a fixed rate whatever its height; W-22 #13: twice as fast again); a cave's keeps its own pace.
                float speed = e.underRock > 0.5 ? 1.62 : 2.0 * input.fallLength / 3.0 / max(e.shape.w, 0.3);
                // Streaks: narrow across, long down, rushing downwards.
                float along = uv.y * input.fallLength / 3.0 * lerp(1.0, 1.3, e.underRock) - _Time.y * speed;
                float streak = PunchyValueNoise(float2(uv.x * e.shape.y * 1.6 * fine, along)) * 0.6 +
                               PunchyValueNoise(float2(uv.x * e.shape.y * 4.0 * fine + 13.0, along * 2.3)) * 0.4;
                streak = lerp(streak, smoothstep(0.3, 0.75, streak), e.underRock);
                float foam = saturate(uv.y * lerp(0.9, 0.6, e.underRock) + (streak - 0.5) * 0.8);

                float edges = smoothstep(0, 0.12, uv.x) * smoothstep(1, 0.88, uv.x);
                // R-21: in the open the sides are cut by noise, in metres, over a band that widens down the fall. Row 3:
                // two octaves, the finer one rushing faster, so no edge runs as a straight line down the sheet.
                float wide = e.shape.y * (1 - 0.12 * uv.y);
                float fromEdge = min(uv.x, 1 - uv.x) * wide;
                float fray = (0.25 + 1.4 * uv.y) * clamp(wide * 0.15, 1, 3);   // (wider falls break up over more)
                float cut = PunchyValueNoise(float2(uv.x * wide * 0.7 + 5.0, along * 0.35)) * 0.65 +
                            PunchyValueNoise(float2(uv.x * wide * 2.1 + 19.0, along * 1.1)) * 0.35;
                // (The outermost few percent always fade out, so a splayed side seen edge-on never shows as a bright line.)
                float openEdges = smoothstep(0, fray, fromEdge - (cut - 0.35) * fray) * smoothstep(0.02, 0.07, min(uv.x, 1 - uv.x));
                edges = lerp(openEdges, edges, e.underRock);
                float ragged = smoothstep(0.25, 0.55, streak + 0.35 - abs(uv.x - 0.5) * 0.6);
                half alpha = e.timing.z * edges * lerp(lerp(0.55, 0.12, e.underRock), 1, streak) * lerp(ragged, 1, lerp(0.8, 0.5, e.underRock)) * smoothstep(0, 0.03, uv.y);
                // The open sheet thins as it falls.
                alpha *= lerp(1, 1 - 0.22 * uv.y, 1 - e.underRock);
                // R-21 row 3: the open sheet's middle is solid white water, not a pane: opaque, streaked white, its
                // border wandering with the edge noise; only the outer band is see-through.
                float core = smoothstep(0.1, 0.2, min(uv.x, 1 - uv.x) + (cut - 0.5) * 0.08) * (1 - e.underRock);
                alpha = lerp(alpha, e.timing.z * lerp(0.85, 1.0, streak) * smoothstep(0, 0.03, uv.y), core);
                foam = lerp(foam, max(foam * 0.6, 0.15 + 0.8 * smoothstep(0.45, 0.85, streak)), core);
                // R-21 row 2: the open sheet's foot turns to white water where it hits its pool or ledge, and its lip
                // throws sparse spray streaks. By screen size (rule 20, the pixels a metre covers here): far away the
                // foot is only a soft whitening, so the fall still reads as one sheet; close up the foam churns and
                // breaks its edge, and the spray shows.
                float open = 1 - e.underRock;
                float perMetre = _ScreenParams.y * 0.5 * UNITY_MATRIX_P._m11 / max(distance(input.positionWS, GetCameraPositionWS()), 0.1);
                float near = smoothstep(4, 10, perMetre) * open;
                float churn = PunchyValueNoise(float2(uv.x * wide * 1.2 + 71.0, along * 0.9 + _Time.y * 0.7));
                // (Row 3: a wider, denser band, near white, so it reads on pale rock; the lip's spray is its own emitter.)
                float footBand = smoothstep(0.7 - 0.14 * churn * near, 0.93, uv.y) * open;
                foam = max(foam, footBand * lerp(0.85, 0.8 + 0.25 * churn, near));
                alpha = max(alpha, e.timing.z * edges * footBand * lerp(0.9, 0.85 + 0.15 * churn, near));
                // Under rock the top frays out of the notch rather than starting on a straight edge.
                alpha *= lerp(1, smoothstep(0, 0.12, uv.y + (streak - 0.5) * 0.1), e.underRock);
                clip(alpha - 0.01);

                float3 n = normalize(front ? input.normalWS : -input.normalWS);
                Light light = GetMainLight();
                half3 albedo = lerp(e.color.rgb, half3(0.92, 0.95, 0.97), foam);
                half diffuse = saturate(dot(n, light.direction)) * 0.6 + 0.4;
                half3 color = albedo * PunchyEffectLight(e, input.positionWS, n, light.color * diffuse * (1 - PunchyCloudShadow(input.positionWS)), SampleSH(n));
                // Bright threads where the water runs thickest.
                color *= 1 + smoothstep(0.6, 0.9, streak) * 0.7 * e.underRock;
                return half4(PunchyAtmosphere(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
