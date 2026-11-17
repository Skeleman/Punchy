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

    // One path's geometry: the spine, or one fork off it.
    struct ArcPath
    {
        float3 from, to, sideA, sideB;
        float jag;
        uint pathKey;
        bool fork;
        float3 root, tip, forkSideA;
        float forkJag;
        uint forkKey;
    };

    // Point i (0..segments) along the path.
    // Both are worked out and one is picked (no branch: FXC's D3D11 backend fails on the branchy form).
    float3 PathPoint(ArcPath P, uint i)
    {
        float3 spine = SpinePoint(P.from, P.to, P.sideA, P.sideB, P.jag, i / (float)SPINE_SEGMENTS, P.pathKey);
        float s = i / (float)FORK_SEGMENTS;
        float3 fork = lerp(P.root, P.tip, s) + P.forkSideA * Noise1(s, 7.0, P.forkKey) * P.forkJag * sin(s * PI);
        return P.fork ? fork : spine;
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
        float3 across : TEXCOORD0;      // x -1..1 across the ribbon; y, z how far past the path's start / end (half widths)
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
        uint piece = quad % QUADS_PER_PATH;

        float t = _PunchyClock;
        float rate = max(_PunchyArcTiming.x, 0.1);
        int k;
        float start;
        PathAt(seed, t, rate, k, start);
        float brightness;
        if (copy == 0u)
        {
            // Live: its own brightness, and dropouts in some 1/30 s slots.
            uint flickerSlot = (uint)floor(t * 30.0);
            bool dropped = Hash01(seed ^ PunchyArcHash(flickerSlot ^ 0x2545F491u)) < _PunchyArcTiming.z;
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

        // This quad's two ends (a, b): points i0 and i0 + 1 of the spine or a fork.
        ArcPath P = (ArcPath)0;
        P.from = from; P.to = to; P.sideA = sideA; P.sideB = sideB; P.jag = jag; P.pathKey = pathKey;
        uint i0, segments;
        bool miss = false;
        float thin = 1.0;
        if (piece < SPINE_SEGMENTS)
        {
            i0 = piece;
            segments = SPINE_SEGMENTS;
        }
        else
        {
            uint f = (piece - SPINE_SEGMENTS) / FORK_SEGMENTS;
            i0 = (piece - SPINE_SEGMENTS) % FORK_SEGMENTS;
            segments = FORK_SEGMENTS;
            uint forkKey = PunchyArcHash(pathKey ^ (0x9E37u + f * 0x85EBu));
            if (Hash01(forkKey) >= _PunchyArcForks.x)
                return o; // no fork in this slot on this path: a degenerate quad (clipped)
            float forkBase = 0.15 + 0.7 * Hash01(forkKey ^ 0x11u);
            miss = Hash01(forkKey ^ 0x22u) < _PunchyArcForks.z;
            float3 root = SpinePoint(from, to, sideA, sideB, jag, forkBase, pathKey);
            float angle = Hash01(forkKey ^ 0x33u) * 2.0 * PI;
            float3 outward = normalize(dir * 0.8 + (sideA * cos(angle) + sideB * sin(angle)));
            float forkLength = _PunchyArcForks.y * arcLength;
            float3 tip = miss ? root + outward * forkLength
                              : SpinePoint(from, to, sideA, sideB, jag, min(forkBase + _PunchyArcForks.y, 0.98), pathKey);
            P.fork = true;
            P.root = root; P.tip = tip;
            P.forkSideA = normalize(cross(normalize(tip - root + 1e-4), sideB));
            P.forkJag = jag * 0.5;
            P.forkKey = forkKey;
            thin = 0.6;
        }
        float3 a = PathPoint(P, i0);
        float3 b = PathPoint(P, i0 + 1u);

        // Camera-facing ribbon, as wide as the glow. A joint's two vertices are shared with the next quad (the side
        // comes from the tangent through the joint, not this segment alone), so neighbouring quads never fan out and
        // overlap into streaks. The path's own two ends get round caps: the quad runs on half a width past them and
        // the fragment fades by the distance to the end point.
        bool atB = corner == 1u || corner == 2u || corner == 4u;
        bool right = corner == 2u || corner == 4u || corner == 5u;
        uint i = atB ? i0 + 1u : i0;
        float3 p = atB ? b : a;
        // The neighbour past this joint (clamped: at the path's ends it is the joint itself).
        float3 beyond = PathPoint(P, atB ? min(i + 1u, segments) : max(i, 1u) - 1u);
        // Selects below are arithmetic: FXC's D3D11 backend emits an invalid negated movc for "c ? x : -y".
        float fb = atB ? 1.0 : 0.0;
        float3 tangent = lerp(b - beyond, beyond - a, fb);
        tangent = normalize(tangent + 1e-5);
        // A ribbon much wider than its segments (a colossus's short fat arcs) would swing a tall quad at every jag
        // and fan into streaks again, so its side follows the path's straight axis instead, more so the shorter
        // the segments are against the half width.
        float halfWidth = _PunchyArcShape.x * _PunchyArcShape.y * width * thin;
        // (By the path's mean segment, so both quads at a joint agree on its side.)
        float3 axis2 = P.fork ? normalize(P.tip - P.root + 1e-4) : dir;
        float meanSegment = (P.fork ? length(P.tip - P.root) : arcLength) / (float)segments;
        tangent = normalize(lerp(axis2, tangent, saturate(meanSegment / max(halfWidth, 1e-5))) + 1e-5);
        float3 toCamera = GetCameraPositionWS() - p;
        float3 side = normalize(cross(tangent, toCamera) + 1e-6);
        float across = (right ? 2.0 : 0.0) - 1.0;
        float segment = length(b - a) / max(halfWidth, 1e-5);
        float capStart = -1.0, capEnd = -1.0;
        float startQuad = i0 == 0u ? 1.0 : 0.0, endQuad = i0 + 1u == segments ? 1.0 : 0.0;
        capStart = lerp(-1.0, lerp(1.0, -segment, fb), startQuad);
        capEnd = lerp(-1.0, lerp(-segment, 1.0, fb), endQuad);
        p += tangent * (halfWidth * ((i == segments ? 1.0 : 0.0) - (i == 0u ? 1.0 : 0.0)));
        p += side * (across * halfWidth);

        o.positionCS = TransformWorldToHClip(p);
        o.across = float3(across, capStart, capEnd);
        float4 coreColour = _PunchyArcColours[colour * 2];
        float4 glowColour = _PunchyArcColours[colour * 2 + 1];
        float strength = brightness * fade;
        // The afterglow is all glow: the core has gone.
        o.core = float4(coreColour.rgb * strength * (copy == 0u ? 1.0 : 0.0), _PunchyArcShape.z);
        o.glow = glowColour.rgb * strength;
        o.alpha = (miss && P.fork) ? 1.0 - i / (float)FORK_SEGMENTS : 1.0;
        o.fogFactor = ComputeFogFactor(o.positionCS.z);
        return o;
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float d = length(float2(input.across.x, max(max(input.across.y, input.across.z), 0.0)));
        float coreEdge = 1.0 / max(_PunchyArcShape.y, 1.0);
        float core = 1.0 - smoothstep(coreEdge * 0.5, coreEdge, d);
        float glow = input.core.a * saturate(1.0 - d) * saturate(1.0 - d); // soft halo out to the ribbon edge, past the core
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
