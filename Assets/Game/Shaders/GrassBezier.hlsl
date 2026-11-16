#ifndef PUNCHY_GRASS_BEZIER_INCLUDED
#define PUNCHY_GRASS_BEZIER_INCLUDED

// SPDX-License-Identifier: MIT
// Blade shape (cubic Bezier spine, width across the spine's tangent, rounded normal, taper) adapted from
// "Cubic Bezier Grass" by altunenes (https://www.shadertoy.com/view/7fSSRc), itself after "Procedural Grass in
// Ghost of Tsushima". The reference ray-marches blades per pixel; here the spine is extruded in the vertex shader.
//
// Copyright (c) 2026 @altunenes
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
// persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
// Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

// R-31 row 2 (#325) prototype d: Bezier blades in Voronoi clumps. Each blade belongs to the nearest of a jittered grid of
// clump centres (GRASS_CLUMP_SIZE apart), which gives its neighbours a shared lean, height band and colour, so the
// field reads as overlapping clumps rather than identical tufts or noise. Included by GroundDetail.hlsl (needs Blade,
// the blade tables, DetailVisibility and GrassBladeDisplace).

#define GRASS_CLUMP_SIZE 0.35

float2 GrassClumpHash(float2 c)
{
    return frac(sin(float2(dot(c, float2(127.1, 311.7)), dot(c, float2(269.5, 183.3)))) * 43758.5453);
}

// The clump a ground point belongs to: its hash (xy, 0..1) and its centre in world xz.
float2 GrassClumpCell(float2 xz, out float2 centre)
{
    float2 g = xz / GRASS_CLUMP_SIZE;
    float2 i = floor(g);
    float best = 1e9;
    float2 bestHash = 0;
    centre = xz;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 c = i + float2(x, y);
        float2 h = GrassClumpHash(c);
        float2 p = c + h;
        float d = dot(p - g, p - g);
        if (d < best)
        {
            best = d;
            bestHash = h;
            centre = p * GRASS_CLUMP_SIZE;
        }
    }
    return bestHash;
}

// The clump's colour on top of the blade's own: some clumps yellower and lighter, some deeper green.
half3 GrassClumpTint(float2 xz)
{
    float2 centre;
    float2 h = GrassClumpCell(xz, centre);
    return lerp(half3(0.88, 0.95, 0.9), half3(1.12, 1.06, 0.82), h.y) * lerp(0.9, 1.08, h.x);
}

float3 CubicBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1 - t;
    return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
}

float3 CubicBezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1 - t;
    return 3 * u * u * (p1 - p0) + 6 * u * t * (p2 - p1) + 3 * t * t * (p3 - p2);
}

// One vertex of a Bezier blade: 15 vertices (rows at t = 0, 1/3, 2/3 and the tip) or 9 (0, 1/2, tip) by screen size.
void BuildBezierBladeVertex(Blade blade, uint instance, uint vertex, uint verts, out float3 positionWS, out float3 normalWS, out float t)
{
    float distance = length(blade.position - GetCameraPositionWS());
    float keep;
    float visibility = DetailVisibility(instance, distance, keep);

    bool cheap = verts == 9;
    uint row = cheap ? BladeRow9[vertex] : BladeRow[vertex];
    float side = cheap ? BladeSide9[vertex] : BladeSide[vertex];
    t = row / (cheap ? 2.0 : 3.0);

    // The clump's shared lean, with each blade turned a little off it and splayed out from the clump's centre.
    float2 centre;
    float2 clump = GrassClumpCell(blade.position.xz, centre);
    float clumpTurn = clump.x * 6.2832;
    float2 out2 = blade.position.xz - centre;
    float2 dir2 = lerp(float2(cos(blade.facing), sin(blade.facing)), float2(cos(clumpTurn), sin(clumpTurn)), 0.7)
                + out2 / GRASS_CLUMP_SIZE * 0.6;
    dir2 = normalize(dir2 + 1e-4);
    float3 forward = float3(dir2.x, 0, dir2.y);

    float height = blade.height * lerp(0.75, 1.25, clump.y) * visibility;
    float tilt = blade.bend * height;          // how far the tip leans out
    float bend = blade.bend * 0.6 * height;    // how far the middle bows past the straight line

    float push;
    float3 displace = GrassBladeDisplace(blade, height, push);
    float3 up = float3(0, height * (1 - 0.35 * push), 0);
    float3 p0 = blade.position;
    float3 p1 = p0 + up * 0.35 + forward * tilt * 0.05;
    float3 p2 = p0 + up * 0.72 + forward * (tilt * 0.55 + bend) + displace * 0.6;
    float3 p3 = p0 + up + forward * tilt + displace;

    float3 spine = CubicBezier(p0, p1, p2, p3, t);
    // (NaN-safe as in BuildBladeVertex: a faded blade has no height, so the tangent gets a floor.)
    float3 tangent = CubicBezierTangent(p0, p1, p2, p3, t) + float3(0, 1e-4, 0);
    float3 across = cross(tangent, forward);
    across = dot(across, across) > 1e-10 ? normalize(across) : float3(-forward.z, 0, forward.x);

    // Taper: full near the root (a little pinched right at it), narrowing to a point.
    float taper = pow(1 - t, 1.6) * smoothstep(1, 0.92, t) * (smoothstep(0, 0.05, t) * 0.3 + 0.7);
    // Thinned-out blades grow wider so distant grass keeps its coverage.
    float width = blade.width * taper * side * min(rsqrt(keep), 2.5);
    positionWS = spine + across * width;

    // Rounded: the normal turns towards each edge, so a flat strip shades like a curved blade.
    float3 face = normalize(cross(across, normalize(tangent)));
    normalWS = normalize(face + across * side * 0.8);
}

#endif
