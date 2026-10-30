Shader "Punchy/Cave"
{
    // Cave walls (CaveSystem): rough rock in two tones broken up by noise, lit by daylight only near the entrance
    // (vertex colour alpha: 1 at the mouth, falling to 0 deep inside) and by any additional lights, such as the
    // player's lantern, everywhere. At the entrance the walls take the surrounding terrain's colour (vertex rgb) and
    // blend into the rock a few metres in (uv.x). The mesh is in world space and double-sided.
    //
    // Each vertex carries its cave's style slot (uv.z) into _PunchyCaveStyles, the table CaveSystem uploads from the
    // CaveStyle assets: rock colours and banding, a finish (gloss, metal, crystal facets, translucent ice, frost sheen),
    // fine surface detail from one shared texture, a floor material (ground facing up: sand, gravel, ash, frost crust)
    // and glowing cracks in sparse patches. One shader and one material for every style. Slot 0 is no style, drawn
    // with this material's own colours.
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
                float3 data : TEXCOORD3;   // x terrain blend, y rock tint, z rib shade
                nointerpolation float style : TEXCOORD4;
            };

            // Nine vectors per style slot (CaveStyle.Pack): rock rgb + strata, dark rgb + strata frequency,
            // glow rgb + crack width, (gloss, metallic, facets, translucency), (glow scale, glow everywhere, cracks),
            // floor rgb + grit, floor dark rgb + floor gloss, (detail, detail scale, sheen, moss), moss rgb.
            float4 _PunchyCaveStyles[32 * 9];
            // The nearest glowing things (lava pools, glowing plants): they light the walls round them in the shader
            // instead of with real lights (the list is declared in Glow.hlsl, shared with the terrain and plants).
            #include "Glow.hlsl"
            // Shared tiling detail (CaveSystem makes it once): rg a fine normal, b a height, a a sparkle.
            TEXTURE2D(_PunchyCaveDetail);
            SAMPLER(sampler_PunchyCaveDetail);
            float _PunchyCaveDetailReady;

            Varyings Vert(CaveAttributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.data = input.data.xyw;
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

            // The shared detail texture on three planes, blended by the normal: rg a tangent-space normal in -1..1 per
            // plane (folded into a world-space nudge), b a height, a a sparkle.
            float4 Detail(float3 p, float3 n, float scale, out float3 nudge)
            {
                float3 w = pow(abs(n), 4);
                w /= w.x + w.y + w.z;
                float4 x = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.zy * scale);
                float4 y = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.xz * scale);
                float4 z = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, p.xy * scale);
                float2 nx = x.rg * 2 - 1, ny = y.rg * 2 - 1, nz = z.rg * 2 - 1;
                nudge = float3(0, nx.y, nx.x) * w.x + float3(ny.x, 0, ny.y) * w.y + float3(nz.x, nz.y, 0) * w.z;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Normals come from the rock's shape and always face into the cave, whichever way a triangle winds.
                float3 shape = normalize(input.normalWS);
                float3 n = shape;
                float3 p = input.positionWS;
                float openness = saturate(input.color.a);

                // The cave's style (slot 0: this material's colours, plain rock).
                int slot = clamp((int)round(input.style), 0, 31);
                float4 s0 = float4(_RockColor.rgb, 0.3), s1 = float4(_DarkColor.rgb, 0.366), s2 = 0, s3 = 0, s4 = float4(1, 0, 0, 0);
                float4 s5 = float4(_RockColor.rgb * 0.8, 0.3), s6 = float4(_DarkColor.rgb, 0), s7 = float4(0.3, 1, 0, 0), s8 = 0;
                if (slot > 0)
                {
                    int i = slot * 9;
                    s0 = _PunchyCaveStyles[i];     s1 = _PunchyCaveStyles[i + 1];
                    s2 = _PunchyCaveStyles[i + 2]; s3 = _PunchyCaveStyles[i + 3];
                    s4 = _PunchyCaveStyles[i + 4]; s5 = _PunchyCaveStyles[i + 5];
                    s6 = _PunchyCaveStyles[i + 6]; s7 = _PunchyCaveStyles[i + 7];
                    s8 = _PunchyCaveStyles[i + 8];
                }
                float terrain = saturate(input.data.x);
                // Floor: ground that faces up, by its shape (not its fine detail), away from the terrain-coloured mouth.
                float floorShare = smoothstep(0.55, 0.8, shape.y) * (1 - terrain);
                float gloss = lerp(s3.x, s6.w, floorShare);
                float metallic = s3.y * (1 - floorShare), facets = s3.z * (1 - floorShare), translucency = s3.w * (1 - floorShare);
                gloss *= 1 - terrain;
                metallic *= 1 - terrain;
                translucency *= 1 - terrain;

                // Translucent rock (ice): slick, glassy patches on the walls where meltwater ran and froze smooth, and sheets
                // of refrozen meltwater on the powder floor. Smooth (no frost or grit), clear and glossy.
                float glassMask = PunchyValueNoise(p.xz * 0.18 + p.y * 0.11 + 13) * 0.65 + PunchyValueNoise(p.zy * 0.4 - 5) * 0.35;
                float sheetMask = PunchyValueNoise(p.xz * 0.25 + 31) * 0.7 + PunchyValueNoise(p.xz * 0.9 - 7) * 0.3;
                float slick = s3.w * saturate(smoothstep(0.55, 0.62, glassMask) * (1 - floorShare) + smoothstep(0.5, 0.57, sheetMask) * floorShare) * (1 - terrain);

                // Fine surface detail: frost and grain on walls, grit on floors; one texture, three reads.
                float3 nudge;
                float4 detail = Detail(p, shape, s7.y, nudge);
                n = normalize(n + nudge * s7.x * (1 - terrain) * (1 - slick));

                // Faceted rock (ore, crystal): each triangle shaded with its own flat face normal (from screen
                // derivatives), so the angular walls read as planes meeting at sharp edges, each catching the light
                // on its own.
                if (facets > 0)
                {
                    float3 face = normalize(cross(ddy(p), ddx(p)));
                    face *= dot(face, shape) < 0 ? -1 : 1;
                    n = normalize(lerp(n, face, facets));
                }

                // Rock: two tones in blotches and strata, darker in the hollows (rgb carries a per-vertex tint). Raised
                // detail takes the light tone (frost on ice), sunken detail the dark one (deep blue in its recesses).
                float blotch = PunchyValueNoise(p.xz * 0.35 + p.y * 0.2) * 0.6 + PunchyValueNoise(p.xy * 1.3 + p.z) * 0.4;
                float strata = 0.5 + 0.5 * sin(p.y * s1.w * 6.2832 + PunchyValueNoise(p.xz * 0.2) * 4);
                float tone = saturate(blotch * 0.8 + strata * s0.w + (detail.b - 0.5) * s7.x);
                half3 rock = lerp(s1.rgb, s0.rgb, tone) * input.data.y;
                // The floor: its own two tones with grit (sand, gravel, ash, frost crust).
                half3 floorTone = lerp(s6.rgb, s5.rgb, saturate(blotch * 0.7 + detail.b * 0.5));
                floorTone *= lerp(1, 0.6 + 0.8 * detail.a, s5.w);
                rock = lerp(rock, floorTone, floorShare);
                // Moss: patches on anything facing up (floors, ledges, the tops of fallen rocks), thickest where daylight
                // comes in, a little on the lower walls; matte, so it also dulls the rock's gloss.
                if (s7.w > 0)
                {
                    float clump = PunchyValueNoise(p.xz * 0.6 + p.y * 0.3 + 71) * 0.7 + detail.b * 0.3;
                    float moss = smoothstep(0.55, 0.7, clump + s7.w * 0.35 + openness * 0.25) * saturate(shape.y * 1.6 + 0.2) * s7.w;
                    rock = lerp(rock, s8.rgb * (0.7 + 0.6 * detail.b), moss);
                    gloss *= 1 - moss;
                }
                // Slick ice: the deep, clear tone of the rock and a mirror gloss.
                rock = lerp(rock, s1.rgb * 0.8, slick * 0.7);
                gloss = lerp(gloss, 0.97, slick);
                // Where it stands in for the terrain, speckle it like the terrain's own close-up detail.
                half3 ground = input.color.rgb * (0.7 + 0.45 * blotch);
                half3 albedo = lerp(rock, ground, terrain);
                half3 diffuse = albedo * (1 - 0.7 * metallic);
                half3 specColor = lerp(half3(0.9, 0.9, 0.9), albedo * 1.6, metallic);
                float shininess = lerp(12, 256, saturate(gloss + facets * 0.5));
                // Frost and grit glint: the odd detail texel catches the lantern far brighter than the rest.
                float glint = 1 + 8 * smoothstep(0.93, 0.99, detail.a) * s7.x * (1 - slick);
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
                    shine += lit * pow(saturate(dot(n, h)), shininess) * saturate(gloss + metallic * 0.6 + facets * 0.8) * glint;
                    through += lit * (0.35 + 0.65 * saturate(0.5 - 0.5 * dot(n, l.direction)));
                LIGHT_LOOP_END

                // Glowing things nearby (molten pools, glowing plants): their light falling off over its reach, brightest
                // on walls facing them, with a glint on glossy rock.
                for (int k = 0; k < _PunchyCaveHeatCount; k++)
                {
                    float3 toPool = _PunchyCaveHeat[k].xyz - p;
                    float4 hc = _PunchyCaveHeatColor[k];
                    float dist = length(toPool);
                    // Out of its reach it adds nothing: skip the rest (the slots are usually full of distant plants).
                    [branch] if (dist < hc.w)
                    {
                        float fall = saturate(1 - dist / hc.w);
                        fall *= fall;
                        float3 dir = toPool / max(dist, 1e-3);
                        half3 warm = hc.rgb * fall;
                        light += warm * saturate(dot(n, dir) * 0.7 + 0.3);
                        shine += warm * pow(saturate(dot(n, normalize(dir + view))), shininess) * saturate(gloss + metallic * 0.6);
                    }
                }

                half3 color = diffuse * light + specColor * shine + s0.rgb * through * translucency * 0.6;
                // Ribs: their hollows darker and crests lighter, whatever look the wall has (rock, ice or the terrain's
                // skin in a shallow cave), so they read in flat light.
                half ribs = 1 - clamp(input.data.z * 2.4, -0.3, 0.75);
                color *= ribs;
                // Sheen: a soft rim where the wall turns away, lit by whatever light reaches it, with a faint floor of
                // its own so even near-black rock (obsidian) keeps its shape out of the lantern's reach.
                color += lerp(s0.rgb, 1, 0.3) * pow(1 - saturate(dot(n, view)), 3) * s7.z * (light * 0.5 + 0.035);
                // The atmosphere (fog and weather marches) only where daylight reaches; deep inside it would be lerped away.
                if (openness > 0.001)
                    color = lerp(color, PunchyAtmosphere(color, p), openness);

                // Where the walls stand in for the ground (round a mouth), shade that share as TerrainSurface shades the
                // ground beside it: its plain colour, darkened and glossy when the weather has wet it, lit the same way
                // (PBR, glow lights, cloud shadows) under the same atmosphere, so the seam doesn't show (F-29). Ribs only
                // further in, where there's no ground outside to match.
                [branch] if (terrain > 0.001)
                {
                    half wet = PunchyWetnessAt(p.xz).r * openness;
                    // The terrain's rock grain on steep faces (TerrainSurface: the shared detail at 0.6, three ways).
                    float3 groundNormal = shape;
                    half rockShade = 1;
                    half steep = 1 - smoothstep(0.55, 0.8, shape.y);
                    if (steep > 0.01 && _PunchyCaveDetailReady > 0)
                    {
                        float3 q = p * 0.6;
                        float3 tw = pow(abs(shape), 4);
                        tw /= tw.x + tw.y + tw.z;
                        half4 rd = SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.zy) * tw.x
                                 + SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xz) * tw.y
                                 + SAMPLE_TEXTURE2D(_PunchyCaveDetail, sampler_PunchyCaveDetail, q.xy) * tw.z;
                        half2 rb = (rd.rg - 0.5) * 0.8 * steep;
                        groundNormal = normalize(shape + float3(0, rb.x, rb.y) * tw.x + float3(rb.x, 0, rb.y) * tw.y + float3(rb.x, rb.y, 0) * tw.z);
                        rockShade = 1 + (rd.b - 0.5) * 0.6 * steep;
                    }
                    InputData ground = (InputData)0;
                    ground.positionWS = p;
                    ground.positionCS = input.positionCS;
                    ground.normalWS = groundNormal;
                    ground.viewDirectionWS = view;
                    ground.shadowCoord = TransformWorldToShadowCoord(p);
                    ground.bakedGI = SampleSH(shape) * lerp(_Darkness, 1, openness) + PunchyGlowLight(p, shape);
                    ground.normalizedScreenSpaceUV = inputData.normalizedScreenSpaceUV;
                    ground.shadowMask = half4(1, 1, 1, 1);
                    SurfaceData groundSurface = (SurfaceData)0;
                    groundSurface.albedo = input.color.rgb * rockShade * lerp(1, 0.72, wet);
                    groundSurface.smoothness = lerp(0.15, 0.45, wet);
                    groundSurface.occlusion = 1;
                    groundSurface.alpha = 1;
                    half3 groundColor = UniversalFragmentPBR(ground, groundSurface).rgb;
                    half cloudShadow = PunchyCloudShadow(p);
                    if (cloudShadow > 0)
                    {
                        Light sun = GetMainLight(ground.shadowCoord);
                        half direct = Luminance(sun.color) * saturate(dot(shape, sun.direction)) * sun.shadowAttenuation;
                        groundColor *= 1 - cloudShadow * direct / max(direct + Luminance(ground.bakedGI), 1e-3);
                    }
                    groundColor *= lerp(ribs, 1, openness);
                    groundColor = lerp(groundColor, PunchyAtmosphere(groundColor, p), openness);
                    color = lerp(color, groundColor, terrain);
                }

                // Light the rock gives off: a share everywhere (ice, faintly lit crystal), the rest in cracks. Cracks are
                // borders between random 3D cells, warped so they wander, small, and only in sparse patches (the style's
                // Cracks share of the wall), never on the floor. They may also darken the rock (dark fissures in ice).
                if (any(s2.rgb > 0) || s4.w > 0)
                {
                    // The mask first: the 27-cell crack search only runs where the mask leaves any crack to draw.
                    float g = s4.x;
                    float patch = PunchyValueNoise(p.xz * 0.21 * g + p.y * 0.17 + 37) * 0.7 + PunchyValueNoise(p.zy * 0.5 * g - 11) * 0.3;
                    // Only on walls and ceilings: anything facing even half up (sloped floor) stays clear.
                    float crack = smoothstep(1 - s4.z, 1 - s4.z + 0.12, patch) * (1 - smoothstep(0.2, 0.45, shape.y)) * (1 - terrain);
                    if (crack > 0)
                    {
                        float3 warp = float3(PunchyValueNoise(p.xy * 0.9 + 3), PunchyValueNoise(p.yz * 0.9 - 5), PunchyValueNoise(p.zx * 0.9 + 9));
                        crack *= 1 - smoothstep(s2.w * 0.4, s2.w * 1.6, CellEdge(p * 1.3 * g + warp * 1.2));
                        // Broken cracks: the lines fade in and out along their length in short fragments, and the
                        // patch fades towards its edge instead of stopping on a line.
                        if (s8.w > 0)
                        {
                            float pieces = PunchyValueNoise(p.xz * 2.3 * g + p.y * 1.9) * 0.6 + PunchyValueNoise(p.zy * 4.1 * g - 7) * 0.4;
                            crack *= lerp(1, smoothstep(0.42, 0.62, pieces) * smoothstep(1 - s4.z, 1 - s4.z + 0.3, patch), s8.w);
                        }
                    }
                    color *= 1 - crack * s4.w * 0.85;
                    color += s2.rgb * (s4.y * (1 - floorShare * 0.5) + crack * (1 - s4.y) * (1 - s4.w)) * (1 - terrain);
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
