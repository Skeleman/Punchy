#ifndef PUNCHY_COAT_INCLUDED
#define PUNCHY_COAT_INCLUDED

// W-166 (#336): the one look for what a creature is covered in (Fauna.CoatKind: fur, feathers, scales, skin, chitin),
// in the material library so a fur rug, a feathered cloak or a scale shield can take a creature's coat later (rule 33).
// The patterns are Creature.shader's A-97 / A-123 coats, moved here unchanged: with the table's default row each
// returns exactly what the inline code drew, so Creature.shader can switch to this include without a visible change.
// The kind's row (MaterialLibrary.Coat) scales its pattern size, contrast and bump, and sets its light. Every detail
// fades by its on-screen size (fwidth) or the given nearness, and is centred near 1, so a far coat keeps its colour.
// Self-contained: carries its own 3D value noise (the same hash as Creature.shader's Noise3). Fragment stage only.

// Pushed by MaterialLibrary (Punchy.Terrain), one row per CoatKind:
//   shape: x pattern size (1 = Creature.shader's), y contrast, z bump, w sky mirrored
//   light: x diffuse wrap, y rim, z highlight strength, w highlight power
float4 _PunchyCoatShapeKinds[8];
float4 _PunchyCoatLightKinds[8];

#define PUNCHY_COAT_FUR 0
#define PUNCHY_COAT_FEATHERS 1
#define PUNCHY_COAT_SCALES 2
#define PUNCHY_COAT_SKIN 3
#define PUNCHY_COAT_CHITIN 4

struct PunchyCoatKind
{
    float size;
    half contrast;
    half bump;
    half sky;
    half wrap;
    half rim;
    half spec;
    half specPow;
};

/// How a coat takes the light (Creature.shader's wrap, rimK, spec, specPow, sky).
struct PunchyCoatLight
{
    half wrap;
    half rim;
    half spec;
    half specPow;
    half sky;
};

float PunchyCoatHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

/// 3D value noise, 0..1: the same pattern as Creature.shader's Noise3.
float PunchyCoatNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(PunchyCoatHash3(i), PunchyCoatHash3(i + float3(1, 0, 0)), f.x),
                     lerp(PunchyCoatHash3(i + float3(0, 1, 0)), PunchyCoatHash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(PunchyCoatHash3(i + float3(0, 0, 1)), PunchyCoatHash3(i + float3(1, 0, 1)), f.x),
                     lerp(PunchyCoatHash3(i + float3(0, 1, 1)), PunchyCoatHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

/// A kind's row; until the table is pushed (a zero row) the inline values Creature.shader used.
PunchyCoatKind PunchyCoatKindOf(int kind)
{
    int i = clamp(kind, 0, 7);
    float4 s = _PunchyCoatShapeKinds[i];
    float4 l = _PunchyCoatLightKinds[i];
    bool set = s.x > 1e-3;
    PunchyCoatKind k;
    k.size = set ? s.x : 1;
    k.contrast = set ? (half)s.y : 1;
    k.bump = set ? (half)s.z : 1;
    k.sky = set ? (half)s.w : (kind == PUNCHY_COAT_CHITIN ? 0.25 : 0);
    // Defaults by kind: fur, feathers, scales, skin, chitin.
    half4 d = kind == PUNCHY_COAT_FEATHERS ? half4(0.35, 0.3, 0.02, 8) : kind == PUNCHY_COAT_SCALES ? half4(0.2, 0.15, 0.35, 30) :
              kind == PUNCHY_COAT_SKIN ? half4(0.4, 0.12, 0.25, 24) : kind == PUNCHY_COAT_CHITIN ? half4(0.15, 0.1, 0.6, 40) :
              half4(0.25, 0.25, 0, 8);
    bool lit = l.w > 1e-3;
    k.wrap = lit ? (half)l.x : d.x;
    k.rim = lit ? (half)l.y : d.y;
    k.spec = lit ? (half)l.z : d.z;
    k.specPow = lit ? (half)l.w : d.w;
    return k;
}

/// Cell coordinates round a segment of radius r (m) and along it: a whole number of cells round (at least minAround),
/// so the pattern closes at the seam. `angle` is 0..1 round the segment. A flat piece passes its own uv / cell instead.
float2 PunchyCoatRound(float angle, float along, float r, float cell, float stretch, float minAround)
{
    float around = max(minAround, round(6.2831853 * max(r, 0.004) / cell));
    return float2(angle * around, along / (cell * stretch));
}

/// Fur: up close fine strands along the piece (z), as a darker fleck and a bump. len 1 short and fine, 2 long and
/// clumped, else medium. Returns the bump height.
float PunchyCoatFur(PunchyCoatKind k, float3 sp, uint len, half near, inout half3 albedo)
{
    if (near <= 0)
        return 0;
    float3 q = sp / k.size;
    half strand;
    float h;
    if (len == 1)
    {
        strand = PunchyCoatNoise3(float3(q.xy * 260, q.z * 40));
        albedo *= 1 - near * 0.09 * k.contrast * (1 - strand);
        h = strand * 0.0018 * near;
    }
    else if (len == 2)
    {
        half clump = PunchyCoatNoise3(float3(q.xy * 45, q.z * 6));
        strand = PunchyCoatNoise3(float3(q.xy * 150, q.z * 9)) * 0.6 + clump * 0.4;
        albedo *= 1 - near * 0.2 * k.contrast * (1 - strand);
        h = strand * 0.005 * near;
    }
    else
    {
        strand = PunchyCoatNoise3(float3(q.xy * 170, q.z * 20));
        albedo *= 1 - near * 0.14 * k.contrast * (1 - strand);
        h = strand * 0.0035 * near;
    }
    return h * k.bump * k.size;
}

/// Feathers (A-123 row 3): staggered rows of rounded vanes, each tip over the base of the row behind. len: 1 tips
/// point down -z (body feathers on a segment running towards the head), 2 flight feathers (long, narrow, a shaft),
/// 0 tips along +z. Sets the light (a faint sheen on dark plumage, a crow's). Returns the bump height.
float PunchyCoatFeathers(PunchyCoatKind k, float3 sp, float angle, float r, uint len, inout half3 albedo, inout PunchyCoatLight light)
{
    uint fl = len;
    float fcell = (fl == 2 ? 0.018 : 0.024) * k.size;
    float fz = (fl & 1) ? -sp.z : sp.z;
    float2 fuv = PunchyCoatRound(angle, fz, r, fcell, fl == 2 ? 6 : 1.4, fl == 2 ? 10.0 : 6.0);
    float frow = floor(fuv.y);
    fuv.x += frac(frow * 0.5);
    float2 ff = frac(fuv) - float2(0.5, 0);
    float fd = length(float2(ff.x * (fl == 2 ? 2.4 : 2), ff.y));
    half fpix = max(fwidth(fuv.x), fwidth(fuv.y));
    half ffine = saturate(1.5 - fpix * 2.5);
    half fbump = saturate(1.5 - fpix * 6);
    half fringe = smoothstep(0.78, 0.95, fd) * (1 - smoothstep(0.95, 1.15, fd)) * ffine;
    half tucked = (1 - smoothstep(0, 0.22, ff.y)) * ffine;
    half fshaft = fl == 2 ? (1 - smoothstep(0, 0.05, abs(ff.x))) * (1 - ff.y) * ffine : 0;
    half barbs = 1 - 0.05 * k.contrast * ffine * (1 - PunchyCoatNoise3(float3(ff.x * 9 + frow, ff.y * 3, 2.9)));
    half dark = 1 - saturate(dot(albedo, half3(0.3, 0.59, 0.11)) * 4);
    albedo *= (1 - 0.14 * k.contrast * fringe) * (1 - 0.08 * k.contrast * tucked) * (1 + 0.08 * k.contrast * fshaft) * barbs *
              lerp(1 - 0.04 * k.contrast, 1 + 0.04 * k.contrast, PunchyCoatNoise3(float3(frow, floor(fuv.x), 1.7)));
    // On near-black plumage a darker edge can't show: each vane's edge and tip catch a faint cool lift instead.
    albedo += k.contrast * dark * (0.014 * fringe + 0.006 * saturate(ff.y - 0.3) * (1 - smoothstep(0.8, 1, fd)) * ffine + 0.01 * fshaft) * half3(0.85, 0.92, 1);
    // The sheen sits on each vane (strongest toward its tip), so it breaks up feather by feather.
    half fsheen = lerp(1, 0.6 + 0.4 * saturate(0.3 + ff.y) * (1 - 0.6 * fringe) * (1 - 0.5 * tucked), ffine);
    light.spec = (k.spec + 0.07 * dark) * fsheen;
    light.specPow = k.specPow + 8 * dark;
    return smoothstep(0, 0.8, ff.y) * (1 - smoothstep(0.8, 1, ff.y)) * (1 - smoothstep(0.7, 1.05, fd)) * 0.0008 * fbump * k.bump * k.size;
}

/// Scales: small domes in staggered rows, a dark groove round each. Returns the bump height.
float PunchyCoatScales(PunchyCoatKind k, float3 sp, float angle, float r, half near, inout half3 albedo)
{
    float2 uv = PunchyCoatRound(angle, sp.z, r, 0.009 * k.size, 0.8, 3);
    float row = floor(uv.y);
    uv.x += frac(row * 0.5);
    float2 f = frac(uv) - float2(0.5, 0);
    float d = length(float2(f.x * 2, f.y));
    float fine = saturate(1.5 - max(fwidth(uv.x), fwidth(uv.y)) * 2.5);
    half groove = smoothstep(0.7, 0.95, d) * fine;
    albedo *= (1 - 0.35 * k.contrast * groove) * lerp(1 - 0.1 * k.contrast, 1 + 0.08 * k.contrast, PunchyCoatNoise3(float3(row, floor(uv.x), 3.3)));
    return (1 - saturate(d * d)) * 0.0012 * near * fine * k.bump * k.size;
}

/// Skin: soft mottling and fine pores. Returns the bump height.
float PunchyCoatSkin(PunchyCoatKind k, float3 sp, half near, inout half3 albedo)
{
    float3 q = sp / k.size;
    albedo *= lerp(1 - 0.12 * k.contrast, 1 + 0.08 * k.contrast, PunchyCoatNoise3(q * 9 + 1.3));
    half pore = PunchyCoatNoise3(q * 140);
    albedo *= 1 - 0.08 * k.contrast * (1 - pore) * near;
    return pore * 0.0005 * near * k.bump * k.size;
}

/// Chitin: hard plates in bands along the piece, a dark seam between. Returns the bump height (seams sunk).
float PunchyCoatChitin(PunchyCoatKind k, float3 sp, half near, inout half3 albedo)
{
    float period = 0.035 * k.size;
    float band = frac(sp.z / period);
    half seam = (1 - smoothstep(0.0, 0.08, band)) * saturate(1.5 - fwidth(sp.z / period) * 2.5);
    albedo *= (1 - 0.3 * k.contrast * seam) * lerp(1 - 0.05 * k.contrast, 1 + 0.05 * k.contrast, PunchyCoatNoise3(sp / k.size * 30));
    return -seam * 0.0008 * near * k.bump * k.size;
}

/// The whole coat for kind (CoatKind) on a piece: sp is the pattern frame in metres (z along the piece, xy round it,
/// riding on the piece), r its radius (m), len the coat's length or feather layout, dist the camera distance, `albedo`
/// the piece's own colour. Returns the albedo; h the bump height for the caller's bump, light how it takes the light.
half3 PunchyCoat(int kind, float3 sp, float r, uint len, float dist, half3 albedo, out float h, out PunchyCoatLight light)
{
    PunchyCoatKind k = PunchyCoatKindOf(kind);
    h = 0;
    light.wrap = k.wrap;
    light.rim = k.rim;
    light.spec = k.spec;
    light.specPow = k.specPow;
    light.sky = k.sky;
    // Broad soft patches in the coat (both shades of it, a little).
    half patch = PunchyCoatNoise3(sp * 2.3 + albedo * 17) * 0.6 + PunchyCoatNoise3(sp * 5.7 + 3.1) * 0.4;
    float angle = atan2(sp.y, sp.x) * 0.15915494 + 0.5;
    half near = 1 - smoothstep(4, 14, dist);
    if (kind == PUNCHY_COAT_FUR)
    {
        albedo *= lerp(0.84, 1.12, patch);
        h = PunchyCoatFur(k, sp, len, 1 - smoothstep(3, 9, dist), albedo);
    }
    else
    {
        albedo *= lerp(0.92, 1.06, patch);
        if (kind == PUNCHY_COAT_FEATHERS)
            h = PunchyCoatFeathers(k, sp, angle, r, len, albedo, light);
        else if (kind == PUNCHY_COAT_SCALES)
            h = PunchyCoatScales(k, sp, angle, r, near, albedo);
        else if (kind == PUNCHY_COAT_SKIN)
            h = PunchyCoatSkin(k, sp, near, albedo);
        else
            h = PunchyCoatChitin(k, sp, near, albedo);
    }
    return albedo;
}

#endif
