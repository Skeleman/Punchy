# Rendering rules (20, 24, 25, 26)

The render lane owns `Shaders/*.hlsl`, the surface lighting path and `DetailPolicy`: ask before editing them.

## LOD by screen size (rule 20)
- Every visual detail choice (fade, cull, tier, shells, seams, effects, impostor hand-over, light reach) uses
  `DetailPolicy.ScreenSize` (object size over distance in reference pixels; FOV and resolution aware) with a
  per-kind threshold in DetailPolicy, plus hysteresis. A fixed-metre visual range in new code goes back.
- Gameplay and simulation ranges (senses, AI, collision, streaming) may stay in metres: mark them `// gameplay:`
  (or sim, streaming, collision).
- A file you touch that still has a metre visual range: convert it in that branch, or list it in your handoff.
- The supervisor runs `tools/lod-lint.sh <branch>` before merge.

## Far view first (rule 25, #298)
- Anything big enough to see from a ridge or the air (landforms, forest, water, buildings, towns, large
  creatures, herds, lights, smoke, weather) ships with a far form: a stand-in, an impostor, a tint in the far
  ground or canopy map, a light or a plume. It is picked by screen size, with no pop at the hand-over.
- Done-when includes a far shot from R-22's fixed vantage points, next to the close shot.
- Nothing visual is cut at the edge of the streamed terrain or tied to draw distance. Haze is atmosphere, not a
  curtain.
- Far forms are drawn only (no entities, colliders or network cost) and built from the same placement records as
  the real thing (rule 22).
- A far view is judged on landform, forest, water, sky and light before anything built.

## One lighting path, one atmosphere (rule 26, R-23)
- Surface shaders don't put lighting together by hand. Shadows (cascades, shadow cache, wide map), cloud shadow,
  glow lights, wetness and haze come from the shared includes, in the shared order.
- Until R-23 lands: no new surface shader, and no lighting or haze term added to one shader only. Ask the
  supervisor.
- After R-23: a forward pass that calls URP lighting or PunchyAtmosphere outside the shared function goes back.

## NaN-safe (rule 24, #282)
White bloom circles are NaN pixels spread by bloom. URP Stop NaN on the main camera is a net, not a fix.
- `SafeNormal` or a guarded normalize for anything that can be zero length; `max(d, 1e-5)` under divisions;
  saturated or non-negative input to `pow` and `sqrt`; no `ddx`, `ddy` or `fwidth` inside a branch.
- Every row that touches a shader, material or generated mesh: `-sparkleprobe` NaN count 0 on its views with
  `-stopnan 0`.

## Also
- Never `Shader.Find` at runtime.
- Nights are dark enough for now (Zach 11-08). Individual billboards (octahedral if possible) come before any
  cluster or HLOD work (Zach 11-09).
