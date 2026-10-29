#ifndef PUNCHY_GROUNDING_INCLUDED
#define PUNCHY_GROUNDING_INCLUDED

// Grounding: the occlusion screen-space AO is too weak to give. Generated meshes (plants, rocks, buildings, props)
// darken towards where they meet the ground, and their solid faces darken as they turn downwards (eaves, overhangs,
// the undersides of rocks and eggs). Worked out per vertex from the height above the mesh's own base (its origin
// sits on the ground; hanging things count from their root up the same way), so nothing samples the terrain.
// Multiply it into the ambient occlusion: it then dims the ambient light fully and the direct light in part.
// One function, so the tuning lives in one place.

// Darkest at the very base (a tight contact shadow and a broader rise from the ground), and on downward faces.
static const half PunchyGroundContact = 0.72;   // the tight contact shadow at the base (the first ~12 cm)
static const half PunchyGroundBroad = 0.75;     // the broader darkening at the base, gone by PunchyGroundRamp
static const half PunchyGroundRamp = 0.9;       // metres
static const half PunchyGroundDown = 0.5;       // a face pointing straight down

/// Occlusion (0..1) for a vertex `height` metres above (or below, for hanging things) its mesh's base, with a world
/// normal whose up component is `normalY`; `solid` is 0 for leaves and petals, whose normals don't say which way
/// they face.
half PunchyGrounding(float height, half normalY, half solid)
{
    float h = abs(height);
    half contact = lerp(PunchyGroundContact, 1, smoothstep(0, 0.12, h));
    half broad = lerp(PunchyGroundBroad, 1, smoothstep(0, PunchyGroundRamp, h));
    half down = lerp(1, PunchyGroundDown, saturate(-normalY) * solid);
    return contact * broad * down;
}

// The undersides of roofs, eaves and overhangs on built things: they see the ground, not the sky. Far darker than
// PunchyGroundDown gives (towards ~0.25 of the albedo), most of the sky's ambient and its grazing reflection gone,
// with a faint warm bounce of the ground's colour in their place so they never go pure black.
static const half PunchyUndersideStart = 0.3;   // -normal.y where it begins
static const half PunchyUndersideFull = 0.7;    // and where it's complete
static const half PunchyUndersideAlbedo = 0.6; // albedo kept (with the lost sky light, about 0.25 of a lit wall)
static const half PunchyUndersideSky = 0.18;    // sky ambient (and sky reflection) kept
static const half PunchyUndersideBounce = 1.6;  // ground-coloured bounce, × the sky's light from above

/// How much a face on a built thing is an underside (0..1), from its world normal's up component.
half PunchyUnderside(half normalY)
{
    return smoothstep(PunchyUndersideStart, PunchyUndersideFull, -normalY);
}

/// The ambient an underside gets in place of the sky's: a dim share of it plus the ground's colour bounced up,
/// `skyAbove` being the sky's light on an upward face.
half3 PunchyUndersideAmbient(half3 sky, half3 ground, half3 skyAbove, half under)
{
    return lerp(sky, sky * PunchyUndersideSky + ground * skyAbove * PunchyUndersideBounce * 0.5, under);
}

// The dirt band at a built thing's base: up to the height its instance carries (about 0.9 m on walls, 0.25 m on a
// bench's feet; × its scale), with a ragged top edge, its colour taken towards the ground's (darkened: soil splashed
// and worn in, not paint), a little darker when wet.
static const half PunchyDirtStrength = 0.85;

/// The albedo with the dirt band applied. `ground` is the instance's ground colour, its alpha the band's height in
/// metres / 2.55 (0: none), `height` metres above
/// the base, `scale` the instance's size, `wet` 0..1, `p` the world position (for the ragged edge).
half3 PunchyDirt(half3 albedo, half4 ground, float height, float scale, half wet, float3 p)
{
    if (ground.a <= 0)
        return albedo;
    float ragged = PunchyValueNoise(float2(p.x + p.z, p.y * 0.3) * 3.1) * 0.6 + PunchyValueNoise(float2(p.x - p.z, p.y) * 9.0) * 0.4;
    float top = ground.a * 2.55 * scale * (0.6 + 0.8 * ragged);
    half band = 1 - smoothstep(top * 0.25, top, height);
    half3 dirt = ground.rgb * lerp(0.5, 0.38, wet);
    // Grime as well as colour, so it reads even where the walls match the ground.
    return lerp(albedo, dirt, band * PunchyDirtStrength) * lerp(1, 0.75, band);
}

#endif
