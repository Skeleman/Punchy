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
// The atlases (one slice per model that can become an impostor, frames x frames tiles of a few dozen texels):
//   albedo: rgb the vertex colour, a coverage.
//   data:   rg the normal (model space, octahedral, 0..1), b depth along the view (0..1 over the bounding sphere),
//           a occlusion * 0.49 plus 0.51 on leaves and petals (lit the leafy way).
// (R-10 hook: lit windows at night would need a third, emissive atlas: see FloraImpostor.shader ForwardLit.)
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


// ---- Drawing (FloraImpostor.shader's ForwardLit, DepthOnly and DepthNormals; needs Flora.hlsl first) ----
#ifdef FLORA_IMPOSTOR_DRAW

TEXTURE2D_ARRAY(_ImpostorAlbedo);
SAMPLER(sampler_ImpostorAlbedo);
TEXTURE2D_ARRAY(_ImpostorData);
SAMPLER(sampler_ImpostorData);
StructuredBuffer<float3> _ModelBounds;   // radius, centre height (unscaled), thins: FloraCull.compute's
int _ImpostorModelBase;                  // the layer's first model: a plant's local model is its model less this
StructuredBuffer<int> _ImpostorSlice;    // each local model's atlas slice (-1: none; the cull never sends those here)
float _PunchyImpostorOneFrame;           // 1: the nearest frame only (FloraSystem.ImpostorOneFrame, for comparison)

struct ImpostorAttributes
{
    float3 positionOS : POSITION;   // the quad's corner, -1..1
    uint instanceID   : SV_InstanceID;
};

struct ImpostorVaryings
{
    float4 positionCS : SV_POSITION;
    float3 ray : TEXCOORD0;                             // eye to this pixel, in the sphere's frame (see ImpostorVert)
    nointerpolation float3 eye : TEXCOORD1;             // the eye in the sphere's frame
    nointerpolation float3 dir0 : TEXCOORD2;            // the three frames' view directions
    nointerpolation float3 dir1 : TEXCOORD3;
    nointerpolation float3 dir2 : TEXCOORD4;
    nointerpolation float4 frame01 : TEXCOORD5;         // frames 0 and 1 (i, j)
    nointerpolation float3 frame2Slice : TEXCOORD6;     // frame 2 (i, j), atlas slice
    nointerpolation float3 weights : TEXCOORD7;
    nointerpolation float4 centre : TEXCOORD8;          // the sphere's centre (world), its radius (scaled)
    nointerpolation float4 plant : TEXCOORD9;           // yaw, fade, random, snow
    nointerpolation float2 ground : TEXCOORD10;         // the plant's base height, its ground colour (packed, as FloraInstance)
};

// The sphere's frame: model space (the plant's yaw undone) over its bounding sphere, centre 0, radius 1, as baked.
float3 ImpostorUnyaw(float3 v, float c, float s) { return float3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c); }
float3 ImpostorYaw(float3 v, float c, float s) { return float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c); }

// One quad per plant, facing the camera in front of its bounding sphere and just covering it, and the three frames
// nearest the view (the corners of the grid triangle the view falls in), weighted by where it falls.
ImpostorVaryings ImpostorVert(ImpostorAttributes input)
{
    InitIndirectDrawArgs(0);
    uint2 visible = _FloraVisible[GetIndirectInstanceID_Base(input.instanceID)];
    FloraInstance plant = _FloraInstances[visible.x];
    uint local = (uint)(plant.model - _ImpostorModelBase);
    float3 bounds = _ModelBounds[local];
    float slice = max(_ImpostorSlice[local], 0);
    float radius = max(bounds.x, 0.01) * 1.01 * plant.scale;
    float3 centre = plant.position + float3(0, bounds.y * plant.scale, 0);

    float3 camera = GetCameraPositionWS();
    float3 toEye = camera - centre;
    float distance = max(length(toEye), radius * 1.05);
    float3 forward = toEye / distance;
    float3 right = abs(forward.y) > 0.999 ? float3(1, 0, 0) : normalize(cross(float3(0, 1, 0), forward));
    float3 up = cross(forward, right);
    // In front of the sphere (so every point of the plant is behind the quad, as SV_DepthLessEqual wants), as wide as
    // the sphere's silhouette there, a little more for the bilinear rim.
    float near = distance - radius;
    float halfSize = radius * near / sqrt(max(distance * distance - radius * radius, 1e-4)) * 1.03;
    float3 positionWS = centre + forward * radius + (right * input.positionOS.x + up * input.positionOS.y) * halfSize;

    float c = cos(plant.yaw), s = sin(plant.yaw);
    float3 eye = ImpostorUnyaw(toEye, c, s) / radius;
    float frames = _ImpostorFrames;
    bool full = _ImpostorFull > 0.5;
    float2 g = (ImpostorGrid(normalize(eye), full) * 0.5 + 0.5) * (frames - 1);
    float2 base = clamp(floor(g), 0, frames - 2);
    float2 f = saturate(g - base);
    float2 frame0, frame1, frame2;
    float3 w;
    if (f.x + f.y <= 1)
    {
        frame0 = base; frame1 = base + float2(1, 0); frame2 = base + float2(0, 1);
        w = float3(1 - f.x - f.y, f.x, f.y);
    }
    else
    {
        frame0 = base + 1; frame1 = base + float2(1, 0); frame2 = base + float2(0, 1);
        w = float3(f.x + f.y - 1, 1 - f.y, 1 - f.x);
    }
    if (_PunchyImpostorOneFrame > 0.5)
        w = w.x >= w.y && w.x >= w.z ? float3(1, 0, 0) : w.y >= w.z ? float3(0, 1, 0) : float3(0, 0, 1);

    ImpostorVaryings o;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.ray = ImpostorUnyaw(positionWS - camera, c, s) / radius;
    o.eye = eye;
    o.dir0 = ImpostorFrameDirection(frame0, frames, full);
    o.dir1 = ImpostorFrameDirection(frame1, frames, full);
    o.dir2 = ImpostorFrameDirection(frame2, frames, full);
    o.frame01 = float4(frame0, frame1);
    o.frame2Slice = float3(frame2, slice);
    o.weights = w;
    o.centre = float4(centre, radius);
    o.plant = float4(plant.yaw, asfloat(visible.y), plant.random, plant.snow);
    o.ground = float2(plant.position.y, asfloat(plant.ground));
    return o;
}

struct ImpostorSample
{
    float3 positionWS;
    half3 normalWS;
    half3 albedo;
    half occlusion;
    bool leafy;
};

// One frame: where the pixel's ray crosses the frame's picture plane, its texels there, and how far along the ray the
// surface they saw is (from the baked depth).
void ImpostorFrame(float3 eye, float3 ray, float3 d, float2 frame, float slice, float weight,
    inout float4 albedo, inout float4 data, inout float along)
{
    float facing = dot(ray, d);
    facing = abs(facing) < 1e-5 ? -1e-5 : facing;
    float3 p = eye - ray * (dot(eye, d) / facing);
    float3 right, up;
    ImpostorBasis(d, right, up);
    float2 uv = (frame + saturate(float2(dot(p, right), dot(p, up)) * 0.5 + 0.5)) / _ImpostorFrames;
    float4 a = SAMPLE_TEXTURE2D_ARRAY(_ImpostorAlbedo, sampler_ImpostorAlbedo, uv, slice);
    float4 b = SAMPLE_TEXTURE2D_ARRAY(_ImpostorData, sampler_ImpostorData, uv, slice);
    float depth = (b.b / max(a.a, 1e-3)) * 2 - 1;
    albedo += a * weight;
    data += b * weight;
    along += weight * a.a * (depth - dot(eye, d)) / facing;
}

// The plant at this pixel, or clipped where none of the three frames saw it.
ImpostorSample ImpostorSurface(ImpostorVaryings input)
{
    float4 albedo = 0, data = 0;
    float along = 0;
    float slice = input.frame2Slice.z;
    ImpostorFrame(input.eye, input.ray, input.dir0, input.frame01.xy, slice, input.weights.x, albedo, data, along);
    ImpostorFrame(input.eye, input.ray, input.dir1, input.frame01.zw, slice, input.weights.y, albedo, data, along);
    ImpostorFrame(input.eye, input.ray, input.dir2, input.frame2Slice.xy, slice, input.weights.z, albedo, data, along);
    clip(albedo.a - 0.5);
    float coverage = max(albedo.a, 1e-3);
    data /= coverage;
    along /= coverage;
    float c = cos(input.plant.x), s = sin(input.plant.x);

    ImpostorSample o;
    o.positionWS = input.centre.xyz + ImpostorYaw(input.eye + input.ray * along, c, s) * input.centre.w;
    o.normalWS = (half3)ImpostorYaw(ImpostorUnpackNormal(data.rg), c, s);
    o.albedo = (half3)(albedo.rgb / coverage);
    o.leafy = data.a >= 0.5;
    o.occlusion = (half)saturate((data.a - (o.leafy ? 0.51 : 0)) / 0.49);
    return o;
}

// The reconstructed surface's depth (never in front of the quad, so the conservative depth output holds).
float ImpostorDepth(float3 positionWS)
{
    float4 positionCS = TransformWorldToHClip(positionWS);
    return positionCS.z / positionCS.w;
}

#if UNITY_REVERSED_Z
#define IMPOSTOR_DEPTH SV_DepthLessEqual
#else
#define IMPOSTOR_DEPTH SV_DepthGreaterEqual
#endif

#endif // FLORA_IMPOSTOR_DRAW

#endif
