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

// Creatures (Creature.shader): only the tight contact shadow where a foot or a belly meets the ground, and their
// downward faces (bellies, chins, the undersides of limbs) a little less dark than a rock's; no broad rise from the
// ground, which would darken whole legs. `height` is above the ground the creature stands on.
static const half PunchyCreatureDown = 0.7;     // a creature's face pointing straight down

half PunchyCreatureGrounding(float height, half normalY)
{
    half contact = lerp(PunchyGroundContact, 1, smoothstep(0, 0.12, abs(height)));
    half down = lerp(1, PunchyCreatureDown, saturate(-normalY));
    return contact * down;
}

#endif
