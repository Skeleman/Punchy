#ifndef PUNCHY_CANOPY_INCLUDED
#define PUNCHY_CANOPY_INCLUDED

// R-22 row 3 (#298, far forest): past where the trees themselves are drawn, the ground carries the forest as a rough,
// dark crown cover, read from the island-wide map FloraSystem.Canopy bakes at load from the tree placement rules
// (every tree cell, so it's the same forest the streaming places). It fades in exactly where its trees fade out: a
// texel's trees end where FloraCull.compute ends an always-drawn tree (its size reach x pixels per metre, kept between
// the layer's radius and its far radius), so the hand-over goes by size on screen (rule 20), never fixed metres.
// Inside the layer's radius it also stands in for the trees the cull thins out, so the cover reads the same density
// on both sides of the hand-over. Crowns break into ~8 m clumps (self-shaded) while those are big enough on screen,
// the cover thickens at grazing angles as real crowns would hide the ground between them, the canopy's height edges
// shade the ground beside a forest on the side away from the sun, and its height steps tilt the normal so slopes of
// forest catch the light. Needs Glow.hlsl (PunchySampleSmooth) and Sky.hlsl (PunchyValueNoise) included first.
TEXTURE2D(_PunchyCanopyData);      // r cover (crown area / ground area), g mean size reach / scale.x, b snow, a bare share
SAMPLER(sampler_PunchyCanopyData);
TEXTURE2D(_PunchyCanopyColour);    // rgb mean crown colour (sRGB texture, so linear here), a mean tree height / scale.y
float4 _PunchyCanopyRect;          // xy the map's corner (m), z 1 / its width (1/m; 0: no map yet), w a texel's width (m)
float4 _PunchyCanopyReach;         // x pixels per metre the trees' reach goes by (0: none past the radius), y the tree layer's radius, z its far radius, w the share kept past it
float4 _PunchyCanopyScale;         // x reach scale, y height scale (m), z strength (0: off), w pixels per metre now (Detail in)
#define PUNCHY_CANOPY_SIZE 1024.0

// Smooth (B-spline) close up, where a texel spans several pixels; the mips further out, cross-faded between.
float4 PunchyCanopyRead(TEXTURE2D_PARAM(map, mapSampler), float2 uv, float lod)
{
    float4 far = 0;
    [branch] if (lod > 0.5)
        far = SAMPLE_TEXTURE2D_LOD(map, mapSampler, uv, max(lod - 0.5, 0));
    [branch] if (lod < 1)
    {
        float4 nearV = PunchySampleSmooth(TEXTURE2D_ARGS(map, mapSampler), uv, PUNCHY_CANOPY_SIZE);
        return lerp(nearV, far, saturate(lod * 2 - 1));
    }
    return far;
}

// The far forest on the ground at p (distance from the camera, view direction): darkens and tints albedo and tilts
// normalWS. Returns how much of the ground the crowns cover (0 where there's no far forest), for the caller to dry
// out puddles and gloss under it and move the sky light onto the new normal.
half PunchyCanopyApply(float3 p, float distance, half3 viewDir, inout half3 albedo, inout half3 normalWS)
{
    float radius = _PunchyCanopyReach.y;
    [branch] if (_PunchyCanopyRect.z <= 0 || _PunchyCanopyScale.z <= 0 || distance < radius * 0.85)
        return 0;
    float2 uv = (p.xz - _PunchyCanopyRect.xy) * _PunchyCanopyRect.z;
    [branch] if (any(uv < 0) || any(uv > 1))
        return 0;
    half ndv = saturate(dot(viewDir, normalWS));
    float mpp = distance / max(_PunchyCanopyScale.w, 1e-3);   // metres a pixel spans here
    float lod = log2(max(mpp / max(ndv, 0.25) / _PunchyCanopyRect.w, 1e-3));
    float4 d = PunchyCanopyRead(TEXTURE2D_ARGS(_PunchyCanopyData, sampler_PunchyCanopyData), uv, lod);
    [branch] if (d.r < 0.004)
        return 0;
    float4 c = PunchyCanopyRead(TEXTURE2D_ARGS(_PunchyCanopyColour, sampler_PunchyCanopyData), uv, lod);

    // The hand-over: these trees' end (as the cull works it out) and its last 12%, where they fade; inside the radius
    // the share the cull thins out of them.
    float end = clamp(d.g * _PunchyCanopyScale.x * _PunchyCanopyReach.x, radius, max(_PunchyCanopyReach.z, radius));
    half handoff = smoothstep(end * 0.88, end, distance);
    half thinned = (1 - _PunchyCanopyReach.w) * smoothstep(radius * 0.85, radius, distance);
    half fadeIn = max(handoff, thinned) * _PunchyCanopyScale.z;

    // Crowns in ~8 m clumps while a clump is a couple of pixels or more; seen low, the gaps between them close up.
    half clumpFade = saturate((8 / mpp - 1.5) / 3);
    half n = PunchyValueNoise(p.xz * 0.125) * 0.65 + PunchyValueNoise(p.xz * 0.31 + 17.3) * 0.35;
    half cover = saturate(d.r * (1 + (n - 0.5) * 0.9 * clumpFade));
    cover = 1 - pow(1 - min(cover, 0.999), 1 / max(ndv, 0.2));

    half3 crown = c.rgb * lerp(0.72, lerp(0.55, 0.95, n), clumpFade);   // (a crown's underside and the gaps go dark)
    crown = lerp(crown, half3(0.78, 0.8, 0.85), min(saturate(d.b * 1.5), 0.6));   // as the flora shader's snow

    // Height steps of the canopy (cover x height): the normal tilts down them, and the ground beside a forest lies in
    // its shade on the side away from the sun (the shadow's length from the trees' height and the sun's elevation).
    float stepM = _PunchyCanopyRect.w * exp2(max(lod, 0));
    float du = stepM * _PunchyCanopyRect.z;
    float lodT = max(lod, 0) + 0.5;
    float h0 = d.r * c.a;
    float hx = SAMPLE_TEXTURE2D_LOD(_PunchyCanopyData, sampler_PunchyCanopyData, uv + float2(du, 0), lodT).r
             * SAMPLE_TEXTURE2D_LOD(_PunchyCanopyColour, sampler_PunchyCanopyData, uv + float2(du, 0), lodT).a;
    float hz = SAMPLE_TEXTURE2D_LOD(_PunchyCanopyData, sampler_PunchyCanopyData, uv + float2(0, du), lodT).r
             * SAMPLE_TEXTURE2D_LOD(_PunchyCanopyColour, sampler_PunchyCanopyData, uv + float2(0, du), lodT).a;
    float2 slope = float2(hx - h0, hz - h0) * _PunchyCanopyScale.y / stepM;
    half3 crownN = normalize(normalWS + half3(-slope.x, 0, -slope.y) * 0.6);

    half shade = 0;
    float3 sun = _MainLightPosition.xyz;
    [branch] if (sun.y > 0.03 && length(sun.xz) > 0.01)
    {
        float reach = min(c.a * _PunchyCanopyScale.y * length(sun.xz) / sun.y, _PunchyCanopyRect.w * 4);
        float2 toSun = normalize(sun.xz) * reach * _PunchyCanopyRect.z;
        half there = SAMPLE_TEXTURE2D_LOD(_PunchyCanopyData, sampler_PunchyCanopyData, uv + toSun, lodT).r;
        shade = saturate((there - d.r) * 1.5) * saturate(sun.y * 6);
    }

    half alpha = saturate(cover * fadeIn);
    albedo *= 1 - 0.45 * shade * fadeIn;
    albedo = lerp(albedo, crown, alpha);
    normalWS = normalize(lerp(normalWS, crownN, alpha));
    return alpha;
}

#endif
