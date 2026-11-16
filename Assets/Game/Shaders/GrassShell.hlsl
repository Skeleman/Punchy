#ifndef PUNCHY_GRASS_SHELL_INCLUDED
#define PUNCHY_GRASS_SHELL_INCLUDED

// R-31 row 2 (#325) prototype c: a short shell layer under the blades. A few copies of the tile's ground lattice
// (GroundDetailSystem.Prototypes.cs ShellPoint, same 1 m spacing and triangle split as the terrain mesh), each lifted a
// little higher, cut in the fragment shader to thin strands on a fine hashed grid, so the ground between tufts fills
// with short grass. Shells sink into the ground towards the full-density range, where the blades take over alone.
// Every Grass.shader pass calls the same builder and cut, so depth priming keeps them. Needs GroundDetail.hlsl first.

struct ShellPoint
{
    float height;
    float grass;
    float3 albedo;
    float pad;
};

StructuredBuffer<ShellPoint> _ShellPoints;
float4 _ShellGrid;    // origin xz, samples per side, spacing
float _ShellMode;     // 1 for a shell draw (per draw)

#define SHELL_STRAND 0.03

static const uint ShellCorner[6] = { 0, 1, 2,  3, 2, 1 };   // (a, b, c) and (d, c, b): the mesh's diagonal split

// One vertex of shell layer `layer`: position, normal, height fraction t (0..1 up the layer stack), grass density.
void BuildShellVertex(uint layer, uint vertex, out float3 positionWS, out float3 normalWS, out float t, out float grass, out half3 albedo)
{
    uint g = (uint)_ShellGrid.z;
    uint cell = vertex / 6;
    uint corner = ShellCorner[vertex % 6];
    uint cx = cell % (g - 1) + (corner & 1);
    uint cz = cell / (g - 1) + (corner >> 1);
    ShellPoint p = _ShellPoints[cz * g + cx];

    float layers = max(_GrassProtoShape.w, 1);
    t = (layer + 1) / layers;
    float2 xz = _ShellGrid.xy + float2(cx, cz) * _ShellGrid.w;
    float distance = length(float3(xz.x, p.height, xz.y) - GetCameraPositionWS());
    float sink = 1 - smoothstep(_DetailFull * 0.7, _DetailFull, distance);
    positionWS = float3(xz.x, p.height + t * _GrassProtoShape.z * p.grass * sink, xz.y);
    normalWS = float3(0, 1, 0);
    grass = p.grass * sink;
    albedo = p.albedo * half3(0.97, 1.18, 0.88);
}

// Cuts a shell fragment down to tapered strands: each fine cell's strand has its own height, up to the grass density.
void ShellClip(float3 positionWS, float t, float grass)
{
    float2 cell = floor(positionWS.xz / SHELL_STRAND);
    float h = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
    float2 f = frac(positionWS.xz / SHELL_STRAND) - 0.5;
    float strand = h * grass;
    clip(min(strand - t, (1 - t / max(strand, 1e-3)) * 0.5 - length(f)));
}

#endif
