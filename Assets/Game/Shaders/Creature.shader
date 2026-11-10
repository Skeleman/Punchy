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
            float4 material;   // x: what it's made of (SegmentMaterial: body, cloth, leather, metal, wood, bone); y: the
                               // height of the ground the creature stands on (for grounding: a body's segments only)
                               // z: seams only ("-noseams" leaves it 0), how many segments back the one this hangs from is (0: none)
                               // w: the look (A-97, Punchy.Fauna.SurfaceLook): surface in the low 4 bits (0 fur, 1 feathers,
                               // 2 scales, 3 skin, 4 chitin, 5 hoof, 6 claw, 7 pad, 8 eye, 9 horn), fur length x 16 (0 medium,
                               // 1 short, 2 long); 0 = fur as every body was before
        };

        StructuredBuffer<Segment> _Segments;
        float _PunchyPlainGear;
        float _PunchyNoGrounding;   // (debug: bodies ungrounded, for before/after shots)
        // Where this draw's instances start in _Segments (the shadowless draw of colonies and traces follows the bodies).
        uint _SegmentOffset;

        struct CreatureAttributes
        {
            float3 shape : POSITION;      // cos, sin around the axis; t along it
            float2 cap : TEXCOORD0;       // cap offset along the axis, radius factor (× local radius)
            uint instanceID : SV_InstanceID;
        };

        // A-96: a normal from a vector that can come out zero or not a number (two near-opposite normals blended, the fur
        // bump on a pixel whose screen derivatives are parallel), the fallback then: normalize gives NaN there, and bloom
        // spreads one such pixel into a white sparkle.
        float3 SafeNormal(float3 v, float3 fallback)
        {
            float l = dot(v, v);
            return l > 1e-20 && l < 1e20 ? v * rsqrt(l) : fallback;
        }

        float3 Rotate(float4 q, float3 v)
        {
            float3 t = 2 * cross(q.xyz, v);
            return v + q.w * t + cross(q.xyz, t);
        }

        // A point of the segment, turned into the world (offset from the segment's start) with its normal. With seams
        // (material.z: the segment this hangs from, that many back) the start of the segment is blended into that one's
        // frame over about a radius, so a bent joint reads as one bent tube rather than two capsules crossing.
        void Joint(Segment s, uint index, float r, float3 local, float3 n, out float3 offsetWS, out float3 normalWS)
        {
            offsetWS = Rotate(s.rotation, local);
            normalWS = Rotate(s.rotation, n);
            int back = (int)round(s.material.z);
            if (back == 0)
                return;
            float band = max(min(2 * r, 0.6 * s.length), 1e-3);
            float w = 1 - smoothstep(0, band, local.z);
            if (w <= 0)
                return;
            float4 q = _Segments[(uint)((int)index - back)].rotation;
            offsetWS = lerp(offsetWS, Rotate(q, local), w);
            normalWS = SafeNormal(lerp(normalWS, Rotate(q, n), w), normalWS);
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

        // The look's surface and fur length (material.w, see Segment).
        uint LookSurface(float look) { return (uint)(look + 0.5) & 15u; }
        uint LookLength(float look) { return (uint)(look + 0.5) >> 4; }

        // Shells (A-92, #230; "-noshells" turns them off): a near body is drawn again as one or two shells a few millimetres out
        // (_Shell: the layer, 0 for the body itself), each kept only where a fur strand stands, thinner further out, so
        // the outline goes soft. Gear, thin parts (eyes, ear tips), anything that isn't fur (hooves, eyes, horns, scales,
        // feathers: A-97), short fur and bodies further than ShellRange collapse to a point (nothing drawn).
        float _Shell;
        static const float ShellStep = 0.0045;
        static const float ShellRange = 7;

        void ShellVertex(inout float3 positionWS, float3 normalWS, Segment s)
        {
            if (_Shell <= 0)
                return;
            if (s.material.x > 0.5 || LookSurface(s.material.w) != 0 || LookLength(s.material.w) == 1 ||
                max(s.radiusStart, s.radiusEnd) * s.flatten < 0.03 || distance(s.position, _WorldSpaceCameraPos) > ShellRange)
                positionWS = s.position;
            else
                positionWS += normalWS * (_Shell * ShellStep);
        }

        void ShellClip(float3 surface)
        {
            if (_Shell > 0)
                clip(Noise3(float3(surface.xy * 170, surface.z * 20)) - (0.3 + 0.2 * _Shell));
        }

        float SegmentFade(CreatureAttributes input) { return _Segments[input.instanceID + _SegmentOffset].pad; }

        // Also where on the segment the point is, in the segment's own frame (metres: across, across, along; so a pattern on
        // it stays put as it moves), how far along it (0 to 1), and what it's made of.
        void SegmentVertexMaterial(CreatureAttributes input, out float3 positionWS, out float3 normalWS, out float4 color,
                                   out float3 surface, out float along, out float material, out float ground)
        {
            Segment s = _Segments[input.instanceID + _SegmentOffset];
            float t = input.shape.z;
            float r = lerp(s.radiusStart, s.radiusEnd, t);
            float2 around = input.shape.xy;
            float3 local = float3(around.x * r * input.cap.y, around.y * r * input.cap.y * s.flatten, t * s.length + input.cap.x * r);
            float slope = (s.radiusStart - s.radiusEnd) / max(s.length + r, 1e-3);
            float3 n = float3(around.x * input.cap.y, around.y * input.cap.y / max(s.flatten, 0.05), input.cap.x + slope * input.cap.y);
            float3 offsetWS;
            Joint(s, input.instanceID + _SegmentOffset, r, local, SafeNormal(n, float3(0, 0, 1)), offsetWS, normalWS);
            positionWS = s.position + offsetWS;
            color = s.color;
            surface = local;
            along = saturate(local.z / max(s.length + 2 * r, 1e-3) + r / max(s.length + 2 * r, 1e-3));
            material = s.material.x;
            ground = s.material.y;
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
            float3 offsetWS;
            Joint(s, input.instanceID + _SegmentOffset, r, local, SafeNormal(n, float3(0, 0, 1)), offsetWS, normalWS);
            positionWS = s.position + offsetWS;
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
            #include "Metal.hlsl"
            #include "Grounding.hlsl"

            float _PunchyNoFur; // A-92, #230: 0 gives bodies fur strands and coat patches, 1 ("-nofur") plain

            // A-96 (debug, SparkleProbe "-sparkleprobe", 0 in play): above 0, the brightness cap. A pixel that comes out not a
            // number, infinite or brighter than the cap is drawn as a marker instead (r 60000, g which steps went bad, b the
            // shell layer + 1), so the probe camera can count bloom's white sparkles before bloom and say where they start.
            float _PunchySparkleProbe;

            bool SparkleBad(float3 v) { return any((asuint(v) & 0x7f800000u) == 0x7f800000u); }

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
                nointerpolation float ground : TEXCOORD7;
                nointerpolation float look : TEXCOORD8;
                nointerpolation float radius : TEXCOORD9; // (the segment's mean radius: feather and scale rows round it, A-97)
            };

            Varyings Vert(CreatureAttributes input)
            {
                Varyings o;
                float3 n;
                float4 c;
                SegmentVertexMaterial(input, o.positionWS, n, c, o.surface, o.along, o.material, o.ground);
                ShellVertex(o.positionWS, n, _Segments[input.instanceID + _SegmentOffset]);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = n;
                o.color = c;
                o.fade = SegmentFade(input);
                o.look = _Segments[input.instanceID + _SegmentOffset].material.w;
                o.radius = 0.5 * (_Segments[input.instanceID + _SegmentOffset].radiusStart + _Segments[input.instanceID + _SegmentOffset].radiusEnd);
                return o;
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
                    // Metal: the shared look (Metal.hlsl, as Flora's metal parts): tinted reflections of the sky, a sharp
                    // highlight and glints, on a diffuse dimmed to under half, here on a hammered face (the normal
                    // nudged a little, cell by cell).
                    float3 dent = float3(Noise3(surface * 38), Noise3(surface * 38 + 7.1), Noise3(surface * 38 + 13.7)) - 0.5;
                    half3 m = SafeNormal(n + (half3)dent * 0.5, n);
                    return albedo * 0.45 * (sun * saturate(ndl * 0.75 + 0.25) + ambient) +
                           PunchyMetalShade(albedo, m, (half3)v, surface, light.color, light.direction, shadow, 1, 0);
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
                ShellClip(input.surface);
                // (Probe steps, see _PunchySparkleProbe: 1 the vertex normal, 2 the normal, 4 after the fur bump, 8 the lit
                // colour, 16 the final colour bad; 32 the final colour above the cap.)
                uint probe = SparkleBad((float3)input.normalWS) ? 1u : 0u;
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 n = SafeNormal(input.normalWS, v);
                probe |= SparkleBad((float3)n) ? 2u : 0u;
                half3 albedo = input.color.rgb * lerp(1, 0.75, PunchyWetnessAt(input.positionWS.xz).r) * (1 + 0.07 * _Shell);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                half3 color;
                // (_PunchyPlainGear, set by a debug probe: everything shaded as a body, for before/after shots.)
                uint material = _PunchyPlainGear > 0.5 ? 0 : (uint)(input.material + 0.5);
                if (material == 0)
                {
                    // A-97: what the body is covered in (Segment, material.w): fur as before, or feathers, scales, skin,
                    // chitin, hoof, claw, pad, eye or horn. Each gives the albedo a pattern, a bump height h for the shared
                    // bump below, and its own light: how far the diffuse wraps round, the rim, and a highlight.
                    uint surf = LookSurface(input.look);
                    uint len = LookLength(input.look);
                    float3 sp = input.surface;
                    float h = 0;
                    half wrap = 0.25, rimK = 0.25, spec = 0, specPow = 8, sky = 0;
                    if (_PunchyNoFur < 0.5)
                    {
                        float dist = distance(input.positionWS, _WorldSpaceCameraPos);
                        half patch = Noise3(sp * 2.3 + albedo * 17) * 0.6 + Noise3(sp * 5.7 + 3.1) * 0.4;
                        if (surf == 0)
                        {
                            // Fur: broad soft patches in the coat (both shades of it, a little), and up close fine strands
                            // running along the segment, as a bump on the normal (screen-space derivatives: no tangents) and
                            // a darker fleck between them. Patterns sit in the segment's own frame, so they move with it.
                            // Short fur (len 1) has finer, flatter strands; long fur (2) longer ones gathered in clumps.
                            albedo *= lerp(0.84, 1.12, patch);
                            float near = 1 - smoothstep(3, 9, dist);
                            if (near > 0)
                            {
                                half strand;
                                if (len == 1)
                                {
                                    strand = Noise3(float3(sp.xy * 260, sp.z * 40));
                                    albedo *= 1 - near * 0.09 * (1 - strand);
                                    h = strand * 0.0018 * near;
                                }
                                else if (len == 2)
                                {
                                    half clump = Noise3(float3(sp.xy * 45, sp.z * 6));
                                    strand = Noise3(float3(sp.xy * 150, sp.z * 9)) * 0.6 + clump * 0.4;
                                    albedo *= 1 - near * 0.2 * (1 - strand);
                                    h = strand * 0.005 * near;
                                }
                                else
                                {
                                    strand = Noise3(float3(sp.xy * 170, sp.z * 20));
                                    albedo *= 1 - near * 0.14 * (1 - strand);
                                    h = strand * 0.0035 * near;
                                }
                            }
                        }
                        else
                        {
                            // Round the segment and along it, in metres: u counts cells round (a whole number of them, so
                            // the pattern closes up at the seam), v along. Detail too fine for the pixel fades out.
                            float angle = atan2(sp.y, sp.x) * 0.15915494 + 0.5;
                            float r = max(input.radius, 0.004);
                            float near = 1 - smoothstep(4, 14, dist);
                            if (surf <= 4)
                                albedo *= lerp(0.92, 1.06, patch);
                            if (surf == 1 || surf == 2)
                            {
                                // Feathers (1): overlapping rounded vanes in staggered rows, a darker edge where each
                                // overlaps the next, a faint sheen. Scales (2): the same layout, smaller and tighter, each
                                // a small dome with a dark groove round it, and a little gloss.
                                float cell = surf == 1 ? 0.022 : 0.009;
                                float around = max(3, round(6.2831853 * r / cell));
                                float2 uv = float2(angle * around, sp.z / (cell * (surf == 1 ? 1.3 : 0.8)));
                                float row = floor(uv.y);
                                uv.x += frac(row * 0.5) * 1.0;
                                float2 f = frac(uv) - float2(0.5, 0);
                                float d = length(float2(f.x * 2, f.y));
                                float fine = saturate(1.5 - max(fwidth(uv.x), fwidth(uv.y)) * 2.5);
                                if (surf == 1)
                                {
                                    half edge = smoothstep(0.75, 1.0, d) * fine;
                                    half shaft = (1 - smoothstep(0, 0.06, abs(f.x))) * (1 - f.y) * fine;
                                    albedo *= (1 - 0.28 * edge) * (1 + 0.1 * shaft) * lerp(0.95, 1.05, Noise3(float3(row, floor(uv.x), 1.7)));
                                    h = (1 - saturate(d)) * 0.0015 * near * fine;
                                    rimK = 0.3; spec = 0.12; specPow = 12;
                                }
                                else
                                {
                                    half groove = smoothstep(0.7, 0.95, d) * fine;
                                    albedo *= (1 - 0.35 * groove) * lerp(0.9, 1.08, Noise3(float3(row, floor(uv.x), 3.3)));
                                    h = (1 - saturate(d * d)) * 0.0012 * near * fine;
                                    wrap = 0.2; rimK = 0.15; spec = 0.35; specPow = 30;
                                }
                            }
                            else if (surf == 3)
                            {
                                // Skin: fine pores and soft mottling, the light wrapping further round, a moist gloss.
                                albedo *= lerp(0.88, 1.08, Noise3(sp * 9 + 1.3));
                                half pore = Noise3(sp * 140);
                                albedo *= 1 - 0.08 * (1 - pore) * near;
                                h = pore * 0.0005 * near;
                                wrap = 0.4; rimK = 0.12; spec = 0.25; specPow = 24;
                            }
                            else if (surf == 4)
                            {
                                // Chitin: hard plates in bands along the body, a sharp gloss and a bright edge.
                                float band = frac(sp.z / 0.035);
                                half seam = (1 - smoothstep(0.0, 0.08, band)) * saturate(1.5 - fwidth(sp.z / 0.035) * 2.5);
                                albedo *= (1 - 0.3 * seam) * lerp(0.95, 1.05, Noise3(sp * 30));
                                h = -seam * 0.0008 * near;
                                wrap = 0.15; rimK = 0.1; spec = 0.6; specPow = 40; sky = 0.25;
                            }
                            else if (surf == 5 || surf == 6 || surf == 9)
                            {
                                // Hoof (5), claw (6), horn, antler, tusk, beak (9): matte keratin and bone, faint growth
                                // rings round it and streaks along it, the light flatter (less contrast) than on fur.
                                half rings = 0.94 + 0.06 * sin(sp.z * 380 + Noise3(sp * 20) * 3) * near;
                                half streak = 0.92 + 0.08 * Noise3(float3(sp.xy * 120, sp.z * 6));
                                albedo *= rings * streak;
                                h = (streak - 0.92) * 0.004 * near;
                                wrap = 0.4; rimK = 0.08; spec = 0.08; specPow = 10;
                            }
                            else if (surf == 7)
                            {
                                // Pad: dark, pebbled, a low sheen.
                                half pebble = Noise3(sp * 260);
                                albedo *= 0.8 - 0.1 * (1 - pebble);
                                h = pebble * 0.0005 * near;
                                wrap = 0.3; rimK = 0.05; spec = 0.1; specPow = 12;
                            }
                            else if (surf == 8)
                            {
                                // Eye: wet and hard, a sharp highlight from the sun, the sky mirrored at a glancing angle
                                // and a small catchlight, so it reads alive from a few metres. No bump.
                                wrap = 0.2; rimK = 0; spec = 2.2; specPow = 220; sky = 0.6;
                            }
                        }
                    }
                    // (A-96: the derivatives are taken after the branch, where every pixel of the quad has its h; inside
                    // it a neighbour that skipped the branch gave an undefined one. Where the screen derivatives are
                    // parallel, as on the capsule's pinched ends, det is 0 and the normal is kept as it was.)
                    if (_PunchyNoFur < 0.5)
                    {
                        float3 dpdx = ddx(input.positionWS), dpdy = ddy(input.positionWS);
                        float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                        float det = dot(dpdx, r1);
                        float3 grad = sign(det) * (ddx(h) * r1 + ddy(h) * r2);
                        n = SafeNormal(abs(det) * n - grad, n);
                    }
                    probe |= SparkleBad((float3)n) ? 4u : 0u;
                    half diffuse = saturate(dot(n, light.direction) * (1 - wrap) + wrap);
                    // A little soft sheen at the silhouette, like fur or feathers catching the light.
                    half ndv = saturate(dot(n, v));
                    half rim = pow(1 - ndv, 3) * rimK;
                    // (Plus the light of glowing plants and hot rock near it: W-18.)
                    // Grounded (Grounding.hlsl): darker underneath (belly, chin, the undersides of limbs) and where it
                    // meets the ground; the ambient light dimmed fully, the sun's in part.
                    half ground = _PunchyNoGrounding > 0.5 ? 1 : PunchyCreatureGrounding(input.positionWS.y - input.ground, n.y);
                    color = albedo * (light.color * shadow * diffuse * lerp(1, ground, 0.5) + SampleSH(n) * (1 + rim) * ground + PunchyGlowLight(input.positionWS, n));
                    if (spec > 0)
                    {
                        // The highlight (A-97), white rather than the albedo's colour, kept within a few times the sun.
                        float3 rv = reflect(-v, (float3)n);
                        half hl = pow(saturate(dot(rv, light.direction)), specPow) * spec;
                        half fresnel = 0.04 + 0.96 * pow(1 - ndv, 5);
                        color += min(light.color * shadow * hl, 6) + SampleSH(rv) * sky * fresnel * ground;
                        if (surf == 8)
                            color += SampleSH(float3(0, 1, 0)) * pow(saturate(dot((float3)n, normalize(v + float3(0.25, 0.45, 0)))), 600) * 1.5;
                    }
                }
                else
                    // (Gear is grounded by its downward faces only: it carries no ground height.)
                    color = Material(material, albedo, n, v, light, shadow, input.surface, input.along, input.positionWS) *
                            lerp(1, PunchyCreatureGrounding(100, n.y), 0.75);
                // Glowing parts (alpha): embers, toxic markings, eye sockets. Bright enough to bloom.
                color += input.color.rgb * input.color.a * (2.2 + 0.4 * sin(_Time.y * 2.1 + input.positionWS.x * 3.0));
                half3 final = PunchyAtmosphere(color, input.positionWS);
                [branch] if (_PunchySparkleProbe > 0)
                {
                    probe |= SparkleBad((float3)color) ? 8u : 0u;
                    probe |= SparkleBad((float3)final) ? 16u : 0u;
                    probe |= (probe & 16u) == 0 && max(final.r, max(final.g, final.b)) > _PunchySparkleProbe ? 32u : 0u;
                    if ((probe & 48u) != 0)
                        return half4(60000, probe, _Shell + 1, 1);
                }
                return half4(final, 1);
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
                float3 surface : TEXCOORD1;
            };

            Varyings Vert(CreatureAttributes input)
            {
                float3 p, n;
                float4 c;
                float along, material, ground;
                Varyings o;
                SegmentVertexMaterial(input, p, n, c, o.surface, along, material, ground);
                ShellVertex(p, n, _Segments[input.instanceID + _SegmentOffset]);
                o.positionCS = TransformWorldToHClip(p);
                o.fade = SegmentFade(input);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                FadeClip(input.fade, input.positionCS.xy);
                ShellClip(input.surface);
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
                float3 surface : TEXCOORD2;
            };

            Varyings Vert(CreatureAttributes input)
            {
                float3 p, n;
                float4 c;
                float along, material, ground;
                Varyings o;
                SegmentVertexMaterial(input, p, n, c, o.surface, along, material, ground);
                ShellVertex(p, n, _Segments[input.instanceID + _SegmentOffset]);
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
                ShellClip(input.surface);
                outNormalWS = half4(SafeNormal(input.normalWS, float3(0, 1, 0)), 0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
