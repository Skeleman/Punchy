Shader "Punchy/Creature"
{
    // Animals (CreatureRenderSystem): every body segment is an instance of one unit capsule mesh, stretched to the
    // segment's length, tapered from its start radius to its end radius, flattened, then placed and rotated.
    // Soft wrapped lighting, shadows, cloud shadows, wetness and the atmosphere. A creature appearing or leaving
    // dithers in or out (the segment's pad). Gear (helmets, armour, sashes, weapons) is shaded for what it's made of
    // (the segment's material): metal, leather, cloth, wood, bone.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Must match Punchy.Fauna.SegmentInstance.
        struct Segment
        {
            float3 position;
            float length;
            float4 rotation;   // quaternion xyzw
            float radiusStart;
            float radiusEnd;
            float flatten;
            float pad;         // how far faded out (0 shown, 1 gone): a creature appearing or leaving
            float4 color;
            float4 material;   // x: what it's made of (SegmentMaterial: body, cloth, leather, metal, wood, bone)
        };

        StructuredBuffer<Segment> _Segments;
        float _PunchyPlainGear;
        // Where this draw's instances start in _Segments (the shadowless draw of colonies and traces follows the bodies).
        uint _SegmentOffset;

        struct CreatureAttributes
        {
            float3 shape : POSITION;      // cos, sin around the axis; t along it
            float2 cap : TEXCOORD0;       // cap offset along the axis, radius factor (× local radius)
            uint instanceID : SV_InstanceID;
        };

        float3 Rotate(float4 q, float3 v)
        {
            float3 t = 2 * cross(q.xyz, v);
            return v + q.w * t + cross(q.xyz, t);
        }

        // Fading in or out, as a screen-door dither (a 4x4 ordered pattern of dropped pixels): no blending or sorting,
        // the same in every pass, so shadows fade with the body.
        void FadeClip(float fade, float2 pixel)
        {
            if (fade <= 0)
                return;
            static const float pattern[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = uint2(pixel) % 4;
            clip(1 - fade - (pattern[p.y * 4 + p.x] + 0.5) / 16.0);
        }

        float SegmentFade(CreatureAttributes input) { return _Segments[input.instanceID + _SegmentOffset].pad; }

        // Also where on the segment the point is, in the segment's own frame (metres: across, across, along; so a pattern on
        // it stays put as it moves), how far along it (0 to 1), and what it's made of.
        void SegmentVertexMaterial(CreatureAttributes input, out float3 positionWS, out float3 normalWS, out float4 color,
                                   out float3 surface, out float along, out float material)
        {
            Segment s = _Segments[input.instanceID + _SegmentOffset];
            float t = input.shape.z;
            float r = lerp(s.radiusStart, s.radiusEnd, t);
            float2 around = input.shape.xy;
            float3 local = float3(around.x * r * input.cap.y, around.y * r * input.cap.y * s.flatten, t * s.length + input.cap.x * r);
            float slope = (s.radiusStart - s.radiusEnd) / max(s.length + r, 1e-3);
            float3 n = float3(around.x * input.cap.y, around.y * input.cap.y / max(s.flatten, 0.05), input.cap.x + slope * input.cap.y);
            positionWS = s.position + Rotate(s.rotation, local);
            normalWS = Rotate(s.rotation, normalize(n));
            color = s.color;
            surface = local;
            along = saturate(local.z / max(s.length + 2 * r, 1e-3) + r / max(s.length + 2 * r, 1e-3));
            material = s.material.x;
        }

        void SegmentVertex(CreatureAttributes input, out float3 positionWS, out float3 normalWS, out float4 color)
        {
            Segment s = _Segments[input.instanceID + _SegmentOffset];
            float t = input.shape.z;
            float r = lerp(s.radiusStart, s.radiusEnd, t);
            float2 around = input.shape.xy;
            float3 local = float3(around.x * r * input.cap.y, around.y * r * input.cap.y * s.flatten, t * s.length + input.cap.x * r);
            // Normal of the (flattened, tapered) surface, approximately.
            float slope = (s.radiusStart - s.radiusEnd) / max(s.length + r, 1e-3);
            float3 n = float3(around.x * input.cap.y, around.y * input.cap.y / max(s.flatten, 0.05), input.cap.x + slope * input.cap.y);
            positionWS = s.position + Rotate(s.rotation, local);
            normalWS = Rotate(s.rotation, normalize(n));
            color = s.color;
        }
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"
            #include "Glow.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float fade : TEXCOORD3;
                float3 surface : TEXCOORD4;
                float along : TEXCOORD5;
                nointerpolation float material : TEXCOORD6;
            };

            Varyings Vert(CreatureAttributes input)
            {
                Varyings o;
                float3 n;
                float4 c;
                SegmentVertexMaterial(input, o.positionWS, n, c, o.surface, o.along, o.material);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = n;
                o.color = c;
                o.fade = SegmentFade(input);
                return o;
            }

            // Value noise in 3D (hashed lattice, smoothly blended): fixed patterns on a segment's surface, the same on
            // every machine and every frame.
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // What gear is made of, shaded so it reads for what it is from across a town, not just close to: metal
            // mirrors the sky and the ground and flashes in the sun; leather has a soft sheen, cloth none; wood is
            // grained along its length; bone is ivory and waxy. (Bodies keep their own soft light, in Frag.)
            half3 Material(uint material, half3 albedo, half3 n, float3 v, Light light, half shadow, float3 surface, float along, float3 positionWS)
            {
                half3 sun = light.color * shadow;
                half ndl = dot(n, light.direction);
                half ndv = saturate(dot(n, v));
                half3 ambient = SampleSH(n) + PunchyGlowLight(positionWS, n);
                float angle = atan2(surface.y, surface.x);
                if (material == 3)
                {
                    // Metal: a hammered face (the normal nudged a little, cell by cell), the sky and the ground in it,
                    // a hot highlight in the sun's direction, brighter at glancing angles, darker where it turns away.
                    float3 dent = float3(Noise3(surface * 38), Noise3(surface * 38 + 7.1), Noise3(surface * 38 + 13.7)) - 0.5;
                    half3 m = normalize(n + (half3)dent * 0.5);
                    float3 r = reflect(-v, m);
                    // (Polished metal reads by its banding: the bright sky above the horizon, a dark band at it, the
                    // ground dark below. That shows even from across a town, and even in shade.)
                    half3 sky = SampleSH(half3(0, 1, 0)) * 1.3 + PunchyGlowLight(positionWS, r);
                    half3 ground = SampleSH(half3(0, -1, 0)) * 0.22;
                    // (Its banding follows where the surface faces as much as what it reflects, so a plate facing the
                    // viewer still shows its bright top, dark band and darker underside.)
                    float key = r.y * 0.45 + m.y * 0.75;
                    half3 env = key > 0 ? lerp(sky * 0.22, sky, smoothstep(0.0, 0.5, key)) : lerp(sky * 0.22, ground, smoothstep(0.0, 0.3, -key));
                    half fresnel = 0.6 + 0.4 * pow(1 - ndv, 4);
                    half highlight = pow(saturate(dot(r, light.direction)), 48) * 5 + pow(saturate(dot(r, light.direction)), 6) * 0.4;
                    half3 diffuse = albedo * 0.12 * (sun * saturate(ndl) + ambient);
                    half3 color = diffuse + albedo * (env * fresnel + sun * highlight);
                    return color * 0.8 * lerp(0.5, 1, saturate(ndv * 2.5));
                }
                if (material == 2)
                {
                    // Leather: fine grain, worn darker towards its ends, a line of stitches down one side, a soft sheen.
                    half grain = 0.88 + 0.24 * Noise3(surface * 55);
                    half worn = lerp(0.72, 1, saturate(min(along, 1 - along) * 5));
                    half stitch = abs(angle - 1.5708) < 0.06 && frac(surface.z * 28) < 0.55 ? 0.55 : 1;
                    half sheen = pow(1 - ndv, 2.5) * 0.22;
                    half gloss = pow(saturate(dot(reflect(-v, n), light.direction)), 10) * 0.18;
                    half diffuse = saturate(ndl * 0.8 + 0.2);
                    return albedo * grain * worn * stitch * (sun * diffuse + ambient) + (sun * gloss + ambient * sheen) * 0.6;
                }
                if (material == 1)
                {
                    // Cloth: matte, a weave too fine to see far off but a soft rise and fall of folds that is.
                    half weave = 0.93 + 0.07 * sin(surface.x * 140) * sin(surface.z * 140);
                    half folds = 0.86 + 0.14 * sin(surface.z * 16 + angle * 2.5 + Noise3(surface * 4) * 3);
                    half diffuse = saturate(ndl * 0.75 + 0.25);
                    return albedo * weave * folds * (sun * diffuse + ambient * 1.05);
                }
                if (material == 4)
                {
                    // Wood: grain running along its length, wavering, with darker streaks.
                    half grain = sin(angle * 7 + surface.z * 1.3 + Noise3(float3(surface.xy * 6, surface.z * 0.8)) * 5);
                    half streak = 0.8 + 0.2 * grain + 0.1 * (Noise3(float3(surface.xy * 30, surface.z * 2)) - 0.5);
                    half diffuse = saturate(ndl * 0.75 + 0.25);
                    half gloss = pow(saturate(dot(reflect(-v, n), light.direction)), 16) * 0.08;
                    return albedo * streak * (sun * diffuse + ambient) + sun * gloss;
                }
                // Bone: towards ivory, a waxy sheen, small dark pits.
                half3 ivory = lerp(albedo, half3(0.86, 0.81, 0.68), 0.35);
                half pits = Noise3(surface * 95) > 0.78 ? 0.7 : 1;
                half sheen = pow(1 - ndv, 2) * 0.2;
                half gloss = pow(saturate(dot(reflect(-v, n), light.direction)), 20) * 0.3;
                half diffuse = saturate(ndl * 0.7 + 0.3);
                return ivory * pits * (sun * diffuse + ambient) + sun * gloss + ambient * sheen * 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FadeClip(input.fade, input.positionCS.xy);
                half3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo = input.color.rgb * lerp(1, 0.75, PunchyWetnessAt(input.positionWS.xz).r);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                half3 color;
                // (_PunchyPlainGear, set by a debug probe: everything shaded as a body, for before/after shots.)
                uint material = _PunchyPlainGear > 0.5 ? 0 : (uint)(input.material + 0.5);
                if (material == 0)
                {
                    half diffuse = saturate(dot(n, light.direction) * 0.75 + 0.25);
                    // A little soft sheen at the silhouette, like fur or feathers catching the light.
                    half rim = pow(1 - saturate(dot(n, v)), 3) * 0.25;
                    // (Plus the light of glowing plants and hot rock near it: W-18.)
                    color = albedo * (light.color * shadow * diffuse + SampleSH(n) * (1 + rim) + PunchyGlowLight(input.positionWS, n));
                }
                else
                    color = Material(material, albedo, n, v, light, shadow, input.surface, input.along, input.positionWS);
                // Glowing parts (alpha): embers, toxic markings, eye sockets. Bright enough to bloom.
                color += input.color.rgb * input.color.a * (2.2 + 0.4 * sin(_Time.y * 2.1 + input.positionWS.x * 3.0));
                return half4(PunchyAtmosphere(color, input.positionWS), 1);
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
            };

            Varyings Vert(CreatureAttributes input)
            {
                float3 p, n;
                float4 c;
                SegmentVertex(input, p, n, c);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                Varyings o;
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
                o.fade = SegmentFade(input);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                FadeClip(input.fade, input.positionCS.xy);
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fade : TEXCOORD0;
            };

            Varyings Vert(CreatureAttributes input)
            {
                float3 p, n;
                float4 c;
                SegmentVertex(input, p, n, c);
                Varyings o;
                o.positionCS = TransformWorldToHClip(p);
                o.fade = SegmentFade(input);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FadeClip(input.fade, input.positionCS.xy);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and normals for the depth-normals prepass (SSAO), which is also where the camera depth texture comes
        // from; without it creatures would be missing from both.
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
                float fade : TEXCOORD1;
            };

            Varyings Vert(CreatureAttributes input)
            {
                float3 p, n;
                float4 c;
                SegmentVertex(input, p, n, c);
                Varyings o;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = n;
                o.fade = SegmentFade(input);
                return o;
            }

            void Frag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                FadeClip(input.fade, input.positionCS.xy);
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
