#ifndef PUNCHY_FLORA_IMPOSTOR_INCLUDED
#define PUNCHY_FLORA_IMPOSTOR_INCLUDED

// R-7: octahedral impostors (after shaderbits.com/blog/octahedral-impostors). Each model is baked at load
// (FloraSystem.Impostors.cs) from a grid of view directions, one frame per direction, into a slice of two atlases;
// far away a plant is drawn as one quad that blends the frames nearest its view direction. The grid is laid out on
// an octahedron: a direction maps to a point in [-1, 1]^2 and back, so neighbouring frames are neighbouring views.
// Hemi-octahedral (the default) covers the upper half only, all of its frames, since plants are not seen from below;
// the full octahedron covers every direction. This file is the one place the mapping lives, so the bake and the draw
// agree on it.
//
// The atlases (one slice per model, frames x frames tiles of a few dozen texels):
//   albedo: rgb the vertex colour, a coverage.
//   data:   rg the normal (model space, octahedral, 0..1), b depth along the view (0..1 over the bounding sphere),
//           a occlusion * 0.49 plus 0.51 on leaves and petals (lit the leafy way).
// Empty texels are all zero, and coverage is 1 wherever the model was drawn, so a texel averaged with empty ones
// (mipmaps, bilinear filtering at the outline) divides back by its coverage: no dark fringe.

/// Frames per side of the grid, and 1 for the full octahedron (0 hemi). Set per layer (FloraSystem.Impostors.cs).
float _ImpostorFrames, _ImpostorFull;

/// The view direction (from the model towards the eye, model space, y up) of a point of the grid square [-1, 1]^2.
float3 ImpostorDirection(float2 g, bool full)
{
    if (full)
    {
        float3 d = float3(g.x, 1 - abs(g.x) - abs(g.y), g.y);
        if (d.y < 0)
            d.xz = (1 - abs(d.zx)) * float2(d.x >= 0 ? 1 : -1, d.z >= 0 ? 1 : -1);
        return normalize(d);
    }
    // Hemi: the square turned 45 degrees onto the upper half's diamond.
    float2 p = float2(g.x + g.y, g.x - g.y) * 0.5;
    return normalize(float3(p.x, 1 - abs(p.x) - abs(p.y), p.y));
}

/// The grid point (in [-1, 1]^2) of a view direction: ImpostorDirection undone. Below the horizon, hemi grids clamp.
float2 ImpostorGrid(float3 d, bool full)
{
    d /= abs(d.x) + abs(d.y) + abs(d.z);
    if (full)
    {
        float2 g = d.xz;
        if (d.y < 0)
            g = (1 - abs(d.zx)) * float2(d.x >= 0 ? 1 : -1, d.z >= 0 ? 1 : -1);
        return g;
    }
    float2 p = d.xz / max(abs(d.x) + abs(d.z), 1e-5) * (1 - max(d.y, 0));
    return float2(p.x + p.y, p.x - p.y);
}

/// The view direction of frame (i, j), each 0 .. frames - 1.
float3 ImpostorFrameDirection(float2 frame, float frames, bool full)
{
    return ImpostorDirection(frame / max(frames - 1, 1) * 2 - 1, full);
}

/// A frame's picture plane: right and up across it (model space, unit), for the view direction d. Straight down and
/// straight up use a fixed right, so every frame has one.
void ImpostorBasis(float3 d, out float3 right, out float3 up)
{
    float3 ref = abs(d.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
    right = normalize(cross(ref, d));
    up = cross(d, right);
}

/// Octahedral packing of a unit normal into 0..1 (and back), for the data atlas.
float2 ImpostorPackNormal(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 e = n.y >= 0 ? n.xz : (1 - abs(n.zx)) * float2(n.x >= 0 ? 1 : -1, n.z >= 0 ? 1 : -1);
    return e * 0.5 + 0.5;
}

float3 ImpostorUnpackNormal(float2 e)
{
    e = e * 2 - 1;
    float3 n = float3(e.x, 1 - abs(e.x) - abs(e.y), e.y);
    float t = saturate(-n.y);
    n.xz += float2(n.x >= 0 ? -t : t, n.z >= 0 ? -t : t);
    return normalize(n);
}

#endif
