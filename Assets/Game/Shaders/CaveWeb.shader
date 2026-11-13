Shader "Punchy/Cave Web"
{
    // Dangling cobwebs (W-142 row 3b, CaveSystem.Webs): a sheet stretched between three anchors on the rock, with
    // spokes from a hub and rings sagging between them, drawn as thin anti-aliased threads (so they thin to nothing as
    // they shrink below a pixel), and loose strands hanging as camera-facing ribbons at least a pixel or so wide (fainter
    // in step, so a far strand doesn't thicken). A torn web has a sector missing; an old dusty one is grey, fuzzy and
    // has a dead leaf caught in it. Each sways on the shared clock, a slow small swing (more in a draught), its phase
    // from its seed so neighbours never move together, and catches the light faintly, most when lit from behind. No
    // meshes: 216 vertices per web from SV_VertexID (36 triangles of sheet, 3 strands of 6 segments), from a buffer.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Webs"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SharedClock.hlsl"
            #include "Glow.hlsl"

            // Per web 4 float4s: (A, kind), (B, seed), (C, sway), (strand length, fade, 0, 0). Kinds: 0 corner, 1 torn,
            // 2 strand, 3 cluster, 4 dusty. A strand or cluster hangs from A.
            StructuredBuffer<float4> _Webs;
            float4 _PunchyLantern;        // xyz position, w strength
            float4 _PunchyLanternColor;   // rgb colour, w reach (m)
            float _PunchyUnderground;     // 1 while the camera is in a cave (sky light mostly shut out)

            static const uint Rows = 6;             // the sheet's triangle split 6 ways a side: 36 triangles
            static const uint SheetVertices = 108;
            static const float Silk = 0.0018;       // a thread's width (m)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 bary       : TEXCOORD1;   // in the sheet (A, B, C)
                float2 plane      : TEXCOORD2;   // metres from the hub in the sheet's plane
                nointerpolation float4 info : TEXCOORD3;   // x part (0 sheet, 1 strand), y kind, z seed, w fade
                nointerpolation float3 hub  : TEXCOORD4;   // the hub's barycentrics
                float3 normalWS   : TEXCOORD5;
                float2 strand     : TEXCOORD6;   // x down the strand 0..1, y its alpha for being drawn wider than it is
            };

            float3 SafeNormal(float3 v, float3 fallback)
            {
                float l = length(v);
                return l > 1e-6 ? v / l : fallback;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            Varyings Culled()
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);   // outside the clip box: dropped
                return o;
            }

            // A lattice point of the split triangle (row i from A, j across towards C) as barycentrics.
            float3 Lattice(uint i, uint j)
            {
                return float3(1 - i / (float)Rows, (i - j) / (float)Rows, j / (float)Rows);
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                uint b = instanceID * 4;
                float4 h0 = _Webs[b], h1 = _Webs[b + 1], h2 = _Webs[b + 2], h3 = _Webs[b + 3];
                int kind = (int)round(h0.w);
                float seed = h1.w, sway = h2.w, hang = h3.x, fade = h3.y;
                float3 A = h0.xyz, B = h1.xyz, C = h2.xyz;
                // The swing: two slow sines that never line up, each web its own phase; a draught makes it bigger.
                float t = _PunchyClock;
                float phase = seed * 43.7;
                float swing = sin(t * 0.83 + phase) * 0.6 + sin(t * 1.37 + phase * 1.9) * 0.4;
                float swing2 = sin(t * 0.61 + phase * 2.3) * 0.5 + sin(t * 1.13 + phase * 0.7) * 0.5;
                float stir = 0.3 + 0.7 * saturate(sway);

                Varyings o = (Varyings)0;
                o.info = float4(0, kind, seed, fade);
                float3 cameraWS = GetCameraPositionWS();
                bool sheet = kind != 2 && kind != 3;
                if (vertexID < SheetVertices)
                {
                    if (!sheet)
                        return Culled();
                    uint tri = vertexID / 3, corner = vertexID % 3;
                    uint r = (uint)floor(sqrt((float)tri) + 0.001);
                    uint k = tri - r * r;
                    uint j = k / 2;
                    float3 bc;
                    if ((k & 1) == 0)
                        bc = corner == 0 ? Lattice(r, j) : corner == 1 ? Lattice(r + 1, j) : Lattice(r + 1, j + 1);
                    else
                        bc = corner == 0 ? Lattice(r, j) : corner == 1 ? Lattice(r + 1, j + 1) : Lattice(r, j + 1);
                    float3 flat = bc.x * A + bc.y * B + bc.z * C;
                    float3 n = SafeNormal(cross(B - A, C - A), float3(0, 1, 0));
                    float size = sqrt(length(cross(B - A, C - A)) * 0.5);
                    // The hub, near the middle (each web its own), and the plane's own axes for the threads.
                    float3 hub = float3(1, 1, 1) / 3 + (float3(Hash(seed * 7.1), Hash(seed * 3.3), Hash(seed * 5.7)) - 0.5) * 0.12;
                    hub /= max(hub.x + hub.y + hub.z, 1e-3);
                    float3 hubWS = hub.x * A + hub.y * B + hub.z * C;
                    float3 e1 = SafeNormal(B - A, float3(1, 0, 0));
                    float3 e2 = cross(n, e1);
                    o.plane = float2(dot(flat - hubWS, e1), dot(flat - hubWS, e2));
                    // Billowing out of its plane, most in the middle (the anchors and edges hold), and sagging a little.
                    float belly = 27 * bc.x * bc.y * bc.z;
                    float3 p = flat + n * belly * size * (0.04 * stir) * swing + float3(0, -1, 0) * belly * size * 0.03;
                    o.positionWS = p;
                    o.bary = bc;
                    o.hub = hub;
                    o.normalWS = n;
                    o.positionCS = TransformWorldToHClip(p);
                    return o;
                }

                // Strands: one for a strand, a torn or dusty web; three for a cluster; none for a whole corner web.
                uint id = vertexID - SheetVertices;
                uint s = id / 36, seg = (id % 36) / 6, v = id % 6;
                uint count = kind == 3 ? 3 : kind == 0 ? 0 : 1;
                if (s >= count || hang <= 0)
                    return Culled();
                float2 corner = float2(v == 1 || v == 2 || v == 4 ? 1 : 0, v == 2 || v == 4 || v == 5 ? 1 : 0);
                float3 anchor = A;
                float hangLength = hang;
                if (kind == 3)
                {
                    float2 off = float2(Hash(seed * 11 + s), Hash(seed * 17 + s * 3)) - 0.5;
                    anchor += float3(off.x, 0, off.y) * 0.3;
                    hangLength *= 0.55 + 0.45 * Hash(seed * 23 + s * 5);
                }
                else if (sheet)
                {
                    // From the sheet's lowest anchor, a little in towards its middle.
                    float3 low = A.y < B.y ? (A.y < C.y ? A : C) : (B.y < C.y ? B : C);
                    anchor = lerp(low, (A + B + C) / 3, 0.3);
                }
                float u = (seg + corner.y) / 6.0;
                // A pendulum: the tip swings most, each strand of a cluster a little out of step.
                float lag = s * 1.7;
                float2 sw = float2(swing * cos(lag) - swing2 * sin(lag), swing2 * cos(lag) + swing * sin(lag));
                float reach = hangLength * (0.04 + 0.1 * stir) * pow(u, 1.3);
                float3 p = anchor + float3(0, -hangLength * u, 0) + float3(sw.x, 0, sw.y) * reach;
                float3 toCamera = cameraWS - p;
                float3 side = SafeNormal(cross(float3(0, 1, 0), toCamera), float3(1, 0, 0));
                // At least about a pixel and a half wide, fainter in step (rule 24: no divide by zero).
                float pixel = 2 * max(length(toCamera), 0.01) / max(abs(UNITY_MATRIX_P._m11) * _ScreenParams.y, 1);
                float silk = Silk * (kind == 4 ? 1.6 : 1);
                float width = max(silk, pixel * 1.5);
                p += side * (corner.x - 0.5) * width;
                o.positionWS = p;
                o.info.x = 1;
                o.strand = float2(u, silk / width);
                o.normalWS = SafeNormal(toCamera, float3(0, 1, 0));
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }

            // How much of a thread of the given width (m) at distance d (m) covers this pixel (pixel: metres a pixel).
            float Thread(float d, float width, float pixel)
            {
                float drawn = max(width, pixel);
                return saturate(1 - (d - (drawn - pixel) * 0.5) / pixel) * (width / drawn);
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                int kind = (int)round(i.info.y);
                float seed = i.info.z;
                float3 p = i.positionWS;
                float3 toCamera = SafeNormal(GetCameraPositionWS() - p, float3(0, 1, 0));
                float alpha;
                half3 tint = kind == 4 ? half3(0.62, 0.6, 0.56) : half3(0.82, 0.83, 0.8);
                half leaf = 0;
                if (i.info.x < 0.5)
                {
                    // The sheet: how far out from the hub (0 there, 1 on the edge threads) and which way round.
                    float3 h = max(i.hub, 0.05);
                    float s = max(max(1 - i.bary.x / h.x, 1 - i.bary.y / h.y), 1 - i.bary.z / h.z);
                    float r = length(i.plane);
                    float angle = atan2(i.plane.y, i.plane.x + 1e-6);
                    float spokes = 10 + floor(Hash(seed * 31) * 7);
                    float sector = angle / (2 * PI) * spokes + Hash(seed * 13);
                    float within = frac(sector);
                    float pixel = max(length(fwidth(i.plane)), 1e-5) * 0.8;
                    float silk = Silk * (kind == 4 ? 1.8 : 1);
                    float spoke = Thread(min(within, 1 - within) * (2 * PI / spokes) * r, silk, pixel);
                    // Rings: a spiral that sags inwards between spokes, from a small hub out to the edge.
                    float rings = 9 + floor(Hash(seed * 47) * 6);
                    float sag = 1 + 0.08 * (1 - cos(2 * PI * within));
                    float q = s * sag * rings + within / spokes * 0.8;
                    float metres = r / max(s, 0.02) / rings;   // a ring step in metres along this direction
                    float ring = s > 0.1 ? Thread(abs(frac(q + 0.5) - 0.5) * metres, silk, pixel) : 0;
                    float edge = Thread((1 - s) * r / max(s, 0.02), silk * 1.4, pixel);
                    float hubMat = (1 - smoothstep(0.05, 0.12, s)) * 0.35;
                    alpha = max(max(spoke, ring), max(edge, hubMat));
                    // A torn web: a sector gone (just the odd spoke left), ragged at its edge.
                    if (kind == 1)
                    {
                        float tear = Hash(seed * 61) * 2 * PI;
                        float off = abs(frac((angle - tear) / (2 * PI) + 0.5) - 0.5) * 2 * PI;
                        float ragged = 0.55 + 0.25 * sin(s * 23 + seed * 9);
                        if (off < ragged && s > 0.18)
                            alpha = spoke * step(0.7, Hash(floor(sector) + seed * 5)) * 0.6;
                    }
                    if (kind == 4)
                    {
                        // Old and dusty: a faint grey film between the threads, and a dead leaf caught in it.
                        float dust = frac(sin(dot(floor(i.plane * 90), float2(12.9898, 78.233))) * 43758.5453);
                        alpha = max(alpha, 0.07 + 0.08 * dust * (1 - s));
                        float2 at = float2(cos(seed * 20), sin(seed * 20)) * 0.12;
                        float turn = seed * 9;
                        float2 l = i.plane - at;
                        l = float2(l.x * cos(turn) - l.y * sin(turn), l.x * sin(turn) + l.y * cos(turn));
                        float2 e = l / float2(0.045, 0.022);
                        float inLeaf = dot(e, e) * (1 + 0.3 * sin(l.x * 120));
                        leaf = saturate((1 - inLeaf) / max(length(fwidth(e)) * 2, 1e-4));
                    }
                }
                else
                {
                    // A strand: a little thinner and fainter towards its loose tip.
                    alpha = i.strand.y * (1 - 0.5 * smoothstep(0.7, 1, i.strand.x));
                }

                // Light: the sky's ambient (mostly shut out underground, as flora does), the lantern, torches and glows,
                // the last three most from behind the silk.
                float3 n = SafeNormal(i.normalWS, float3(0, 1, 0));
                n = dot(n, toCamera) < 0 ? -n : n;
                float3 toLantern = _PunchyLantern.xyz - p;
                float lanternDistance = length(toLantern);
                float lanternReach = _PunchyLanternColor.w > 0 ? saturate(1 - lanternDistance / max(_PunchyLanternColor.w, 1e-3)) : 0;
                float3 lanternDir = toLantern / max(lanternDistance, 1e-4);
                float behind = pow(saturate(dot(-toCamera, lanternDir)), 3);
                half3 lantern = _PunchyLanternColor.rgb * _PunchyLantern.w * lanternReach * lanternReach;
                half3 sky = max(SampleSH((half3)n), SampleSH((half3)-n)) * lerp(0.8, 0.06, _PunchyUnderground);
                half3 light = 0.06 + sky + lantern * (1.0 + 2.5 * behind) + PunchyGlowLight(p, (half3)n) * 0.6 + PunchyGlowLight(p, (half3)-n) * 1.6;
                half3 color = tint * min(light, 2);
                if (leaf > 0)
                {
                    half3 brown = half3(0.32, 0.2, 0.09) * min(0.08 + lantern + PunchyGlowLight(p, (half3)n), 1.5);
                    color = lerp(color, brown, leaf);
                    alpha = max(alpha, leaf);
                }
                alpha = saturate(alpha * 0.9) * i.info.w;
                clip(alpha - 0.003);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
