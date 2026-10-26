#ifndef PUNCHY_PLAYER_REVEAL_INCLUDED
#define PUNCHY_PLAYER_REVEAL_INCLUDED

// Keeps the player visible through things in the way: surfaces inside a cone from the camera to the player's body
// are dithered away (mostly, not entirely, so the occluder still reads as being there). Purely per pixel, so it
// follows the camera and player smoothly with no raycasts. _PunchyPlayerPos is the player's feet (set by
// GroundDetailSystem).

float4 _PunchyPlayerPos;

#define PUNCHY_REVEAL_STRENGTH 0.85   // share of an occluder removed at the centre of the cone

// 0..1: how much of this point to remove to reveal the player.
float PunchyPlayerReveal(float3 positionWS)
{
    float3 camera = GetCameraPositionWS();
    float3 target = _PunchyPlayerPos.xyz + float3(0, 1.0, 0);
    float3 axis = target - camera;
    float lengthSq = max(dot(axis, axis), 1e-4);
    float t = dot(positionWS - camera, axis) / lengthSq;          // 0 at the camera, 1 at the player
    float distance = length(positionWS - (camera + axis * t));
    float radius = lerp(0.35, 1.5, saturate(t));                  // wide enough at the player to show the body
    float inside = 1 - smoothstep(radius * 0.55, radius, distance);
    // Only between the camera and the player; nothing beside or behind them.
    float between = smoothstep(0.0, 0.05, t) * (1 - smoothstep(0.88, 0.97, t));
    return inside * between * PUNCHY_REVEAL_STRENGTH;
}

// Clips this pixel if it's being removed to reveal the player (dither: 0..1 screen-space noise).
void PunchyPlayerRevealClip(float3 positionWS, float dither)
{
    clip(dither - PunchyPlayerReveal(positionWS));
}

#endif
