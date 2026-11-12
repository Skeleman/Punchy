#ifndef PUNCHY_WATER_TESS_INCLUDED
#define PUNCHY_WATER_TESS_INCLUDED
// Hardware tessellation for the fine sheet (profiler prototype, Zach 11-11; WaterTess.shader). Instead of WaterPatchSystem
// cutting each chunk water quad into Cuts x Cuts on the CPU, the sheet is cut into 2 x 2 and the GPU divides each
// triangle further where the wave field shows, by its size on screen: up to _PunchyWaterTess.x times, aiming at
// _PunchyWaterTess.y pixels a segment. Each edge's factor depends only on its two ends (the field's weight there and
// their distance on screen), so the two triangles sharing an edge always agree and no crack opens. An edge with an end
// where the field weighs nothing (the sheet's border, where it meets the chunk water) stays whole, as the chunk water's.
//
// The including pass defines PUNCHY_TESS_INPUT (its vertex input), PUNCHY_TESS_OUTPUT and PUNCHY_TESS_VERT (its vertex
// function, run in the domain stage on each new point) and PUNCHY_TESS_EXTRA (the lerps of any further fields).

float4 _PunchyWaterTess;   // x the most a triangle's edge is divided, y the pixels a segment aims at (WaterPatchSystem)

struct PunchyTessFactors
{
    float edge[3] : SV_TessFactor;
    float inside  : SV_InsideTessFactor;
};

PUNCHY_TESS_INPUT TessVert(PUNCHY_TESS_INPUT input)
{
    return input;
}

float PunchyTessEdge(float3 a, float3 b)
{
    [branch] if (min(PunchyWaterFieldWeight(a.xz), PunchyWaterFieldWeight(b.xz)) <= 0.01)
        return 1;
    float4 ca = TransformWorldToHClip(a);
    float4 cb = TransformWorldToHClip(b);
    float2 sa = ca.xy / max(ca.w, 1e-2);
    float2 sb = cb.xy / max(cb.w, 1e-2);
    float pixels = length((sa - sb) * 0.5 * _ScreenParams.xy);
    return clamp(pixels / max(_PunchyWaterTess.y, 1), 1, max(_PunchyWaterTess.x, 1));
}

PunchyTessFactors PunchyTessPatch(InputPatch<PUNCHY_TESS_INPUT, 3> p)
{
    float3 a = TransformObjectToWorld(p[0].positionOS);
    float3 b = TransformObjectToWorld(p[1].positionOS);
    float3 c = TransformObjectToWorld(p[2].positionOS);
    PunchyTessFactors f;
    // (Edge i is the one facing corner i.)
    f.edge[0] = PunchyTessEdge(b, c);
    f.edge[1] = PunchyTessEdge(c, a);
    f.edge[2] = PunchyTessEdge(a, b);
    f.inside = max(f.edge[0], max(f.edge[1], f.edge[2]));
    return f;
}

[domain("tri")]
[partitioning("integer")]
[outputtopology("triangle_cw")]
[patchconstantfunc("PunchyTessPatch")]
[outputcontrolpoints(3)]
PUNCHY_TESS_INPUT Hull(InputPatch<PUNCHY_TESS_INPUT, 3> p, uint id : SV_OutputControlPointID)
{
    return p[id];
}

[domain("tri")]
PUNCHY_TESS_OUTPUT Domain(PunchyTessFactors f, OutputPatch<PUNCHY_TESS_INPUT, 3> p, float3 bary : SV_DomainLocation)
{
    PUNCHY_TESS_INPUT v = p[0];
    #define PUNCHY_TESS_LERP(F) v.F = p[0].F * bary.x + p[1].F * bary.y + p[2].F * bary.z   // (not "x": bary.x would expand too)
    PUNCHY_TESS_LERP(positionOS);
    PUNCHY_TESS_LERP(data);
    PUNCHY_TESS_EXTRA
    // (The edge-weld data, PunchyWaterPatchVertex's parent, means something only at the vertex it was made for: the
    // sheet's border edges stay whole, so a new point never lies on one; it takes none.)
    float most = max(bary.x, max(bary.y, bary.z));
    uint k = bary.x >= most ? 0 : (bary.y >= most ? 1 : 2);
    v.parent = most > 0.9999 ? p[k].parent : 0;
    v.parentY = most > 0.9999 ? p[k].parentY : 0;
    return PUNCHY_TESS_VERT(v);
}

#endif
