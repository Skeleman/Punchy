#ifndef PUNCHY_WOOD_INCLUDED
#define PUNCHY_WOOD_INCLUDED

// W-166: the one wood look. Every plank, post, beam, shingle and wooden prop draws through here, whichever shader
// draws it (Flora.shader's timber, Cave.shader's dressed wood), so a change to the grain reaches every piece made of
// wood. A piece keeps its own colour (its culture's or builder's); its kind (MaterialLibrary.WoodKind: oak, pine,
// driftwood, dark, bleached) picks how the grain looks on it: ring spacing, contrast, knots, weathered grey and the
// hue its late wood darkens towards. Every detail scale drops out as it gets too fine for the pixels, and each is
// centred on 1, so a far piece keeps exactly its vertex colour (rule 25). Needs Sky.hlsl (the noise) included first.

// Pushed by MaterialLibrary (Punchy.Terrain), one row per WoodKind:
//   grain: x ring spacing (m), y contrast, z knots (0..1, how many cells hold one), w weathered grey (0..1)
//   tone:  rgb the hue late wood and knots take (luminance about 1), a streak strength
float4 _PunchyWoodGrainKinds[8];
float4 _PunchyWoodToneKinds[8];
float4 _PunchyWoodFormKinds[8];   // x board scale, y knot spacing (m), z figure, w wear

#define PUNCHY_WOOD_OAK 0
#define PUNCHY_WOOD_PINE 1
#define PUNCHY_WOOD_DRIFTWOOD 2
#define PUNCHY_WOOD_DARK 3
#define PUNCHY_WOOD_BLEACHED 4

struct PunchyWoodKind
{
    float ring;
    half contrast;
    half knots;
    half grey;
    half3 hue;
    half streak;
    half boardScale;
    float knotCell;
    half figure;
    half wear;
};

/// A kind's row; oak until the table has been pushed (a zero row), so nothing divides by zero.
PunchyWoodKind PunchyWoodKindOf(int kind)
{
    int i = clamp(kind, 0, 7);
    float4 g = _PunchyWoodGrainKinds[i];
    float4 t = _PunchyWoodToneKinds[i];
    bool set = g.x > 1e-3;
    PunchyWoodKind k;
    k.ring = set ? g.x : 0.011;
    k.contrast = set ? (half)g.y : 1;
    k.knots = set ? (half)g.z : 0.5;
    k.grey = set ? (half)saturate(g.w) : 0;
    k.hue = set && dot(t.rgb, 1) > 0.1 ? (half3)t.rgb : half3(1.1, 0.96, 0.85);
    k.streak = set ? (half)t.a : 1;
    float4 f = _PunchyWoodFormKinds[i];
    bool formSet = f.x > 1e-3;
    k.boardScale = formSet ? (half)f.x : 1;
    k.knotCell = formSet ? max(f.y, 0.05) : 0.55;
    k.figure = formSet ? (half)f.z : 1;
    k.wear = formSet ? (half)f.w : 0.22;
    return k;
}

/// How much of a detail of this period (m) to keep for a pixel covering `footprint` metres: all of it while the
/// period spans eight pixels or more, none at three (Flora.shader's PunchyOctave; repeated so any shader can use this).
half PunchyWoodFade(float period, float footprint)
{
    return smoothstep(3, 8, period / max(footprint, 1e-4));
}

/// The grain on a piece of timber, as an albedo. `along` runs with the grain (m), `across` is the position in the
/// cross-section (m), `seed` makes each board its own piece of tree. Built from scratch for W-166:
/// - rings: distance from an imaginary pith beside the piece, wandering slowly along it, so a sawn face shows long
///   stripes that open into arches; bent by value noise stretched about 7:1 along the grain and read through a
///   second, coarser noise (a domain warp, W-163), so the bends flow into each other instead of wobbling in place;
///   each ring lightens slowly through the early wood and snaps dark at the late wood;
/// - streaks: fine pores about 100:1 along the grain;
/// - knots: the odd dark oval (per kind), with the rings swirling round it;
/// - dirt: soft darker bands of grime running along the piece (W-163), balanced so the piece's mean tone holds,
///   greying weathered kinds;
/// - colour: a three-tone ramp (early, mid, late) on the piece's own colour, the dark end turning to the kind's hue.
half3 PunchyWoodGrain(half3 albedo, float along, float2 across, float footprint, int kind, float seed)
{
    PunchyWoodKind k = PunchyWoodKindOf(kind);
    float side = across.x + across.y;

    // Domain warp, stretched along the grain: a coarse noise pair shifts where the ring noise is read, mostly along
    // the grain, so its bends stretch and flow rather than sit as separate lumps.
    float2 q = float2(along * 0.14, side);
    float2 qc = q * float2(0.6, 4.0) + seed * 2.3;
    float2 shift = float2(PunchyValueNoise(qc), PunchyValueNoise(qc + float2(5.2, 1.3))) - 0.5;
    q += shift * float2(0.9, 0.05) * k.figure;
    float warp = ((PunchyValueNoise(q * float2(1.3, 9.0) + seed * 3.1) - 0.5) * 0.05
               + (PunchyValueNoise(q * float2(5.3, 31.0) + seed * 1.7 + 4.2) - 0.5) * 0.012) * k.figure;

    // Knots: at most one per cell (the kind's knot spacing, half a metre for oak) along the piece, across its face.
    float cell = floor(along / k.knotCell);
    int cellId = (int)cell + (int)(seed * 977.0);
    half kh = PunchyCellHash(int2(cellId, 71));
    half has = step(kh, k.knots * 0.45);
    float kAlong = (cell + 0.2 + 0.6 * PunchyCellHash(int2(cellId, 72))) * k.knotCell;
    float kSide = (PunchyCellHash(int2(cellId, 73)) - 0.5) * 0.14;
    float kSize = 0.008 + 0.014 * PunchyCellHash(int2(cellId, 74));
    float kd = length(float2((along - kAlong) * 0.55, side - kSide));
    half knot = has * (1 - smoothstep(kSize * 0.55, kSize, kd));
    half swirl = has * exp(-kd * kd / (kSize * kSize * 10));

    // Rings round a pith that drifts beside the piece.
    float2 pith = float2(0.2 + 0.07 * sin(along * 0.41 + seed * 6.28), 0.15 + 0.05 * sin(along * 0.23 + seed * 2.1));
    float radius = length(across + pith) + warp + swirl * 0.025;
    half t = frac(radius / k.ring);
    half late = smoothstep(0.5, 0.93, t) * (1 - smoothstep(0.93, 1.0, t));
    late *= 0.75 + 0.25 * PunchyValueNoise(float2(radius / k.ring * 0.5, along * 0.7 + seed));

    // Pores, 100:1 along the grain.
    half pore = PunchyValueNoise(float2(along * 1.4 + seed * 5.0, side * 140.0));
    // Dirt and wear bands.
    half dirt = PunchyValueNoise(float2(along * 0.7 + seed * 11.0, side * 3.0));

    half fRing = PunchyWoodFade(k.ring, footprint);
    half fPore = PunchyWoodFade(0.008, footprint);
    half fKnot = PunchyWoodFade(0.02, footprint);
    half fDirt = PunchyWoodFade(0.4, footprint);

    // Three tones: early wood a touch light, mid the piece's colour, late wood dark and hued. Centred on the ramp's
    // mean over a ring (late averages about 0.2), so the far tone is the vertex colour.
    half ring = (late - 0.2) * k.contrast * fRing;
    half3 early = albedo * 1.07;
    half3 lateTone = albedo * 0.58 * k.hue;
    half3 c = ring < 0 ? lerp(albedo, early, saturate(-ring * 5)) : lerp(albedo, lateTone, saturate(ring * 1.25));
    c *= 1 + (pore - 0.5) * 0.28 * k.streak * fPore;
    c = lerp(c, albedo * 0.45 * k.hue, knot * 0.85 * fKnot);
    // Grime collects in soft bands: darker where the noise runs high, the rest a touch lighter, netting to nothing
    // over a piece (the band averages 0.21 over value noise).
    half band = smoothstep(0.5, 0.9, dirt);
    c *= 1 + (0.21 - band) * k.wear * 1.1 * fDirt;
    // Weathered kinds go silver where the wear bands are, keeping their brightness.
    half lum = dot(c, half3(0.299, 0.587, 0.114));
    c = lerp(c, lum * half3(0.98, 1.0, 1.04), k.grey * saturate(dirt * 1.6 - 0.2));
    return c;
}

/// The grain on a piece built with its grain along an object axis (FloraMeshBuilder.GrainAlong: 0.25 X, 0.5 Y, 0.75 Z).
/// `seed` makes each piece its own cut of tree (W-163: posts and beams stopped sharing one).
half3 PunchyWoodAlongAxis(half3 albedo, float3 p, half axisCode, float footprint, int kind, float seed)
{
    int axis = (int)round(axisCode * 4);
    float along = axis == 1 ? p.x : axis == 2 ? p.y : p.z;
    float2 across = axis == 1 ? p.yz : axis == 2 ? p.xz : p.xy;
    return PunchyWoodGrain(albedo, along + seed * 17.0, across, footprint, kind, seed);
}

half3 PunchyWoodAlongAxis(half3 albedo, float3 p, half axisCode, float footprint, int kind)
{
    return PunchyWoodAlongAxis(albedo, p, axisCode, footprint, kind, 0);
}

/// A per-piece seed (0..1) from a piece's vertex colour, which its builder varies piece to piece.
float PunchyWoodPieceSeed(half3 key)
{
    return frac(dot(key, half3(97.31, 61.17, 13.73)));
}

/// The plane a built face lies on, in the plant's own frame: `row` climbs the face (measured along a slope, so a course
/// keeps its size at any pitch), `across` runs level along it.
void PunchyWoodFaceCoords(float3 p, float3 nOS, out float across, out float row)
{
    float slope = sqrt(saturate(1 - nOS.y * nOS.y));
    across = slope < 0.3 ? p.x : dot(p.xz, normalize(float2(-nOS.z, nOS.x) + float2(1e-4, 0)));
    row = slope < 0.3 ? p.z : p.y / max(slope, 0.35);
}

/// Lapped boards `height` high laid level (`across` along them, `row` up the face), each its own length, shade and
/// piece of tree, with a shadow under each lap and butt joints. The grain runs along each board.
half3 PunchyWoodBoards(half3 albedo, float across, float row, float footprint, int kind, float height)
{
    height *= PunchyWoodKindOf(kind).boardScale;   // painted boards: the kind's width, not geometry
    float r = row / height;
    int board = (int)floor(r);
    half fr = frac(r);
    half h = PunchyCellHash(int2(board, 31));
    float len = 2.2 + h * 1.6;
    float a = across / len + h * 7.3;
    float piece = floor(a);
    half shade = PunchyCellHash(int2(board, (int)piece + 977));
    half joint = 1 - smoothstep(0.004, 0.014, min(frac(a), 1 - frac(a)) * len);
    half lap = smoothstep(0.78, 1.0, fr) * 0.4 + (1 - smoothstep(0.0, 0.05, fr)) * 0.2;
    half o = PunchyWoodFade(height, footprint);
    half3 c = PunchyWoodGrain(albedo, across + piece * 1.37, float2((fr - 0.5) * height, 0), footprint, kind, shade);
    return c * lerp(1, lerp(0.84, 1.12, shade) * (1 - lap - joint * 0.4), o);
}

/// Shingles about 30 cm wide in 24 cm courses, joints staggered course to course, a shadow under the course above,
/// the grain running down each. `course` >= 0 is a course laid as geometry (it casts its own shadow), else -1.
half3 PunchyWoodShingles(half3 albedo, float across, float row, float footprint, int kind, float course)
{
    float r = row / 0.24;
    half fr = frac(r);
    bool laid = course >= 0;
    float c = laid ? course : floor(r);
    float width = 0.3 * PunchyWoodKindOf(kind).boardScale;   // laid courses keep their height, only width varies
    float t = across / width + frac(c * 0.618);
    half ft = frac(t);
    half h = PunchyCellHash(int2((int)c, (int)floor(t)));
    half joint = 1 - smoothstep(0.02, 0.07, min(ft, 1 - ft));
    half under = laid ? 0 : smoothstep(0.7, 1.0, fr) * 0.4;
    half lip = laid ? 0 : (1 - smoothstep(0.0, 0.08, fr)) * 0.12;
    half m = lerp(0.8, 1.14, h) * (1 - under - joint * 0.45 * (1 - under)) * (1 + lip);
    half3 g = PunchyWoodGrain(albedo, row, float2((ft - 0.5) * width, 0), footprint, kind, h);
    return g * lerp(1, m, PunchyWoodFade(0.24, footprint));
}

/// A sawn log end (its rings are in the vertex colours): end-grain speckle and saw marks.
half3 PunchyWoodEnd(half3 albedo, float3 p, float footprint, int kind)
{
    PunchyWoodKind k = PunchyWoodKindOf(kind);
    half fine = PunchyWoodFade(0.012, footprint);
    half speckle = PunchyValueNoise(p.yz * 160) * 0.6 + PunchyValueNoise(p.yz * 61 + 3.7) * 0.4;
    half saw = PunchyValueNoise(float2(p.y * 7 + p.z * 2, 1.3) * 9);
    half3 c = albedo * (1 + ((speckle - 0.5) * 0.22 * k.contrast + (saw - 0.5) * 0.08) * fine);
    half lum = dot(c, half3(0.299, 0.587, 0.114));
    return lerp(c, lum * half3(0.98, 1.0, 1.04), k.grey * 0.6);
}

/// A timber piece's look (FloraMeshBuilder.Surface): 0 grain along its axis, 1 planks, 2 shingles, 4 a shingle course
/// laid as geometry (`key`: the course's colour, which staggers its joints), 5 a sawn log end. (3, thatch, isn't wood.)
half3 PunchyWoodSurface(half3 albedo, int look, float3 p, half axisCode, float3 nOS, float footprint, half3 key, int kind)
{
    if (look == 0)
        return PunchyWoodAlongAxis(albedo, p, axisCode, footprint, kind, PunchyWoodPieceSeed(key));
    if (look == 5)
        return PunchyWoodEnd(albedo, p, footprint, kind);
    float across, row;
    PunchyWoodFaceCoords(p, nOS, across, row);
    if (look == 1)
        return PunchyWoodBoards(albedo, across, row, footprint, kind, 0.21);
    float course = look == 4 ? floor(PunchyWoodPieceSeed(key) * 64) : -1;
    return PunchyWoodShingles(albedo, across, row, footprint, kind, course);
}

#endif
