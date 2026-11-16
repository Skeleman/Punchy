#ifndef PUNCHY_LIBRARY_INCLUDED
#define PUNCHY_LIBRARY_INCLUDED

// W-166 row 4: one call into the material library for shaders that carry a family per vertex instead of their own
// decode (Colossus.shader: colossi, the salt tower; later the items on the ground). The family numbers are
// MaterialFamily's (MaterialLibrary.cs); the kind is the family's own row (WoodKind for wood; for stone 0 natural rock,
// 1 cut courses, 2 rubble; for cloth 0 linen, 1 hide). Each family's look comes from its include, so an upgrade there
// reaches these callers too (rule 33). Every family fades its detail by its own on-screen size (rules 20, 25).
// Needs Sky.hlsl (the noise) first, and _PunchyCaveDetail / sampler_PunchyCaveDetail declared (stone reads them).
// p, n and v are in the caller's pattern frame: for a body that moves, its own scaled object space, so the pattern
// rides on it; n comes back leaned where a family adds relief (brick, stone, ice), in that same frame.
#include "Assets/Game/Shaders/Materials/Stone.hlsl"
#include "Assets/Game/Shaders/Materials/Wood.hlsl"
#include "Assets/Game/Shaders/Materials/Thatch.hlsl"
#include "Assets/Game/Shaders/Materials/Cloth.hlsl"
#include "Assets/Game/Shaders/Materials/Plaster.hlsl"
#include "Assets/Game/Shaders/Materials/Brick.hlsl"
#include "Assets/Game/Shaders/Materials/Ice.hlsl"
#include "Assets/Game/Shaders/Materials/Bone.hlsl"

#define PUNCHY_FAMILY_WOOD 1
#define PUNCHY_FAMILY_BRICK 2
#define PUNCHY_FAMILY_STONE 3
#define PUNCHY_FAMILY_THATCH 4
#define PUNCHY_FAMILY_PLASTER 5
#define PUNCHY_FAMILY_METAL 6
#define PUNCHY_FAMILY_CLOTH 7
#define PUNCHY_FAMILY_LEATHER 8
#define PUNCHY_FAMILY_ICE 9
#define PUNCHY_FAMILY_BONE 10

/// The (across, along) plane a surface lies on: along is up the piece, across runs level round it, so bone's growth
/// lines and wood's grain run up a leg or a trunk.
float2 PunchyLibraryPlane(float3 p, half3 n)
{
    float across = abs(n.x) > abs(n.z) ? p.z : p.x;
    return abs(n.y) > 0.8 ? p.xz : float2(across, p.y);
}

/// The family's albedo for a surface of base colour `albedo` (the vertex colour); n is leaned in place.
/// Metal shades through the light (PunchyMetalShade) and stays with the caller; families with no look keep the colour.
half3 PunchyLibraryAlbedo(int family, int kind, float3 p, inout half3 n, float3 v, float footprint, half r, half3 albedo)
{
    // One exit: FXC rejects a switch whose cases all return ("not all control paths return a value").
    half3 result = albedo;
    [branch] switch (family)
    {
        case PUNCHY_FAMILY_WOOD:
            result = PunchyWoodAlongAxis(albedo, p, 0.5, footprint, kind);
            break;
        case PUNCHY_FAMILY_BRICK:
            result = albedo * PunchyBrick(p, n, v, footprint, r);
            break;
        case PUNCHY_FAMILY_STONE:
            if (kind > 0)
            {
                result = albedo * PunchyStoneBlocks(p, n, footprint, kind == 2, r);
            }
            else
            {
                half crevice;
                PunchyStoneSurface(p, footprint, 0, 0, result, n, crevice);
            }
            break;
        case PUNCHY_FAMILY_THATCH:
            result = albedo * PunchyThatch(p, n, footprint);
            break;
        case PUNCHY_FAMILY_PLASTER:
            result = albedo * PunchyPlaster(p, n, footprint, r);
            break;
        case PUNCHY_FAMILY_CLOTH:
        case PUNCHY_FAMILY_LEATHER:
            result = albedo * (family == PUNCHY_FAMILY_LEATHER || kind == 1 ? PunchyHide(p, n, footprint) : PunchyLinen(p, n, footprint));
            break;
        case PUNCHY_FAMILY_ICE:
            result = albedo * PunchyIce(p, n, v, footprint, r);
            break;
        case PUNCHY_FAMILY_BONE:
            result = PunchyBone(albedo, PunchyLibraryPlane(p, n), footprint);
            break;
        default:
            break;
    }
    return result;
}

#endif
