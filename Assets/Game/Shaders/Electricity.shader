// R-29 row 2 (#320, rule 30): the standard electricity, drawn for every arc ElectricitySystem gathered this frame in
// one procedural draw (no mesh). Each arc is QuadsPerArc camera-facing quads: a 16-segment spine and two 6-segment
// forks for the live path, and the same again for the path before it (the afterglow). Everything moves from the
// shared clock and the arc's seed, so every player sees the same bolt:
//  - path k starts at (k + 0.85 h_k) / rate, so re-jags come on irregular gaps, never a beat (ArcBolt's 8/s one);
//  - each path has its own jag (three octaves of hashed noise under a sin envelope), brightness and forks;
//  - forks come and go per path, and most miss: they leave the spine and fade out in the air;
//  - the old path glows on for Afterglow s; the live one drops out in some 1/30 s slots (flicker).
// Additive, unlit, fogged to black like Flame. The PCG hash matches ElectricitySystem.Hash so the CPU's light and
// crack land on these paths.
// SKETCH (11-16, uncompiled): to compile and tune at the first editor slot.
Shader "Punchy/Electricity"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "SkyCommon.hlsl"
    #include "SharedClock.hlsl"

    #define SPINE_SEGMENTS 16
    #define FORK_SEGMENTS 6
    #define QUADS_PER_PATH (SPINE_SEGMENTS + 2 * FORK_SEGMENTS)
    #define MAX_ARC_COLOURS 8

    struct ArcInstance
    {
        float4 fromSeed;   // xyz one end, w seed (uint bits)
        float4 toWidth;    // xyz the other end, w width
        float4 params;     // x colour, y fade, z jag (m), w struck (TODO: strike start)
    };
    StructuredBuffer<ArcInstance> _ArcInstances;

    float4 _PunchyArcColours[MAX_ARC_COLOURS * 2]; // core, glow per colour
    float4 _PunchyArcShape;   // x core half width (m), y glow width (x core), z glow strength
    float4 _PunchyArcForks;   // x chance, y length share, z miss share
    float4 _PunchyArcTiming;  // x re-jag rate, y afterglow (s), z dropout, w brightness jitter

    uint PunchyArcHash(uint v)
    {
        uint state = v * 747796405u + 2891336453u;
        uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
        return (word >> 22u) ^ word;
    }
    float Hash01(uint v) { return (PunchyArcHash(v) & 0xFFFFFFu) / 16777216.0; }

    // Hashed value noise in [-1, 1] along u at frequency f, for one path.
    float Noise1(float u, float f, uint key)
    {
        float x = u * f;
        float i = floor(x);
        float r = x - i;
        r = r * r * (3.0 - 2.0 * r);
        float a = Hash01(key ^ PunchyArcHash((uint)i));
        float b = Hash01(key ^ PunchyArcHash((uint)(i + 1.0)));
        return lerp(a, b, r) * 2.0 - 1.0;
    }

    // A point on path k's spine at u (0..1): the straight line plus jag in the two directions across it, pinned at the ends.
    float3 SpinePoint(float3 from, float3 to, float3 sideA, float3 sideB, float jag, float u, uint pathKey)
    {
        float env = sin(u * PI);
        float a = Noise1(u, 4.0, pathKey) * 0.6 + Noise1(u, 9.0, pathKey ^ 0x51u) * 0.3 + Noise1(u, 19.0, pathKey ^ 0xA7u) * 0.1;
        float b = Noise1(u, 4.0, pathKey ^ 0x3Cu) * 0.6 + Noise1(u, 9.0, pathKey ^ 0x8Du) * 0.3 + Noise1(u, 19.0, pathKey ^ 0xE1u) * 0.1;
        return lerp(from, to, u) + (sideA * a + sideB * b) * (jag * env);
    }

    // The live path's index and start time at clock t: path k starts at (k + 0.85 h_k) / rate (ElectricitySystem.PathAt).
    void PathAt(uint seed, float t, float rate, out int k, out float start)
    {
        float x = t * rate;
        k = (int)floor(x);
        start = (k + 0.85 * Hash01(seed ^ ((uint)k * 0x632BE5ABu))) / rate;
        if (t < start)
        {
            k--;
            start = (k + 0.85 * Hash01(seed ^ ((uint)k * 0x632BE5ABu))) / rate;
        }
    }

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 across : TEXCOORD0;      // x -1..1 across the ribbon, y unused
        nointerpolation float4 core : TEXCOORD1; // rgb core x brightness, a glow strength
        nointerpolation float3 glow : TEXCOORD2;
        float alpha : TEXCOORD3;        // fades a missing fork's tail
        float fogFactor : TEXCOORD4;
    };

    Varyings Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
    {
        Varyings o = (Varyings)0;
        ArcInstance arc = _ArcInstances[iid];
        uint seed = asuint(arc.fromSeed.w);
        float3 from = arc.fromSeed.xyz;
        float3 to = arc.toWidth.xyz;
        float width = arc.toWidth.w;
        int colour = (int)arc.params.x;
        float fade = arc.params.y;
        float jag = arc.params.z;

        uint quad = vid / 6u;
        uint corner = vid % 6u;
        uint copy = quad / QUADS_PER_PATH;          // 0 live path, 1 the one before (afterglow)
        uint local = quad % QUADS_PER_PATH;

        float t = _PunchyClock;
        float rate = max(_PunchyArcTiming.x, 0.1);
        int k;
        float start;
        PathAt(seed, t, rate, k, start);
        float brightness;
        if (copy == 0u)
        {
            // Live: its own brightness, and dropouts in some 1/30 s slots.
            uint slot = (uint)floor(t * 30.0);
            bool dropped = Hash01(seed ^ PunchyArcHash(slot ^ 0x2545F491u)) < _PunchyArcTiming.z;
            brightness = dropped ? 0.0 : 1.0 - _PunchyArcTiming.w * Hash01(seed ^ ((uint)k * 0xC2B2AE35u));
        }
        else
        {
            // Afterglow: the path before, dying away since the live one struck.
            k--;
            brightness = (1.0 - _PunchyArcTiming.w * Hash01(seed ^ ((uint)k * 0xC2B2AE35u)))
                       * exp(-(t - start) / max(_PunchyArcTiming.y, 0.001));
        }
        uint pathKey = PunchyArcHash(seed ^ ((uint)k * 0x27D4EB2Fu));

        float3 axis = to - from;
        float arcLength = max(sqrt(dot(axis, axis)), 0.001);
        float3 dir = axis / arcLength;
        float3 helper = abs(dir.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
        float3 sideA = normalize(cross(dir, helper));
        float3 sideB = cross(dir, sideA);

        // This quad's two ends (a, b) along the spine or a fork, and how bright each end is.
        float3 a, b;
        float alphaA = 1.0, alphaB = 1.0;
        float thin = 1.0;
        if (local < SPINE_SEGMENTS)
        {
            float u0 = local / (float)SPINE_SEGMENTS;
            float u1 = (local + 1u) / (float)SPINE_SEGMENTS;
            a = SpinePoint(from, to, sideA, sideB, jag, u0, pathKey);
            b = SpinePoint(from, to, sideA, sideB, jag, u1, pathKey);
        }
        else
        {
            uint f = (local - SPINE_SEGMENTS) / FORK_SEGMENTS;
            uint j = (local - SPINE_SEGMENTS) % FORK_SEGMENTS;
            uint forkKey = PunchyArcHash(pathKey ^ (0x9E37u + f * 0x85EBu));
            if (Hash01(forkKey) >= _PunchyArcForks.x)
                return o; // no fork in this slot on this path: a degenerate quad (clipped)
            float base = 0.15 + 0.7 * Hash01(forkKey ^ 0x11u);
            bool miss = Hash01(forkKey ^ 0x22u) < _PunchyArcForks.z;
            float3 root = SpinePoint(from, to, sideA, sideB, jag, base, pathKey);
            float angle = Hash01(forkKey ^ 0x33u) * 2.0 * PI;
            float3 outward = normalize(dir * 0.8 + (sideA * cos(angle) + sideB * sin(angle)));
            float forkLength = _PunchyArcForks.y * arcLength;
            float3 tip = miss ? root + outward * forkLength
                              : SpinePoint(from, to, sideA, sideB, jag, min(base + _PunchyArcForks.y, 0.98), pathKey);
            float3 forkSideA = normalize(cross(normalize(tip - root + 1e-4), sideB));
            float s0 = j / (float)FORK_SEGMENTS;
            float s1 = (j + 1u) / (float)FORK_SEGMENTS;
            float fj = jag * 0.5;
            a = lerp(root, tip, s0) + forkSideA * Noise1(s0, 7.0, forkKey) * fj * sin(s0 * PI);
            b = lerp(root, tip, s1) + forkSideA * Noise1(s1, 7.0, forkKey) * fj * sin(s1 * PI);
            alphaA = miss ? 1.0 - s0 : 1.0;
            alphaB = miss ? 1.0 - s1 : 1.0;
            thin = 0.6;
        }

        // Camera-facing ribbon round segment a-b, as wide as the glow.
        bool atB = corner == 1u || corner == 2u || corner == 4u;
        bool right = corner == 2u || corner == 4u || corner == 5u;
        float3 p = atB ? b : a;
        float3 toCamera = GetCameraPositionWS() - p;
        float3 side = normalize(cross(b - a + 1e-5, toCamera));
        float halfWidth = _PunchyArcShape.x * _PunchyArcShape.y * width * thin;
        float across = right ? 1.0 : -1.0;
        p += side * (across * halfWidth);

        o.positionCS = TransformWorldToHClip(p);
        o.across = float2(across, 0);
        float4 coreColour = _PunchyArcColours[colour * 2];
        float4 glowColour = _PunchyArcColours[colour * 2 + 1];
        float strength = brightness * fade;
        // The afterglow is all glow: the core has gone.
        o.core = float4(coreColour.rgb * strength * (copy == 0u ? 1.0 : 0.0), _PunchyArcShape.z);
        o.glow = glowColour.rgb * strength;
        o.alpha = atB ? alphaB : alphaA;
        o.fogFactor = ComputeFogFactor(o.positionCS.z);
        return o;
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float d = abs(input.across.x);
        float coreEdge = 1.0 / max(_PunchyArcShape.y, 1.0);
        float core = 1.0 - smoothstep(coreEdge * 0.5, coreEdge, d);
        float glow = input.core.a * exp(-d * 4.0) * (1.0 - d);
        half3 color = (half3)(input.core.rgb * core + input.glow * glow) * input.alpha;
        color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
        return half4(color, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            ENDHLSL
        }
    }
}
