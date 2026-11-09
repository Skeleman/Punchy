# Shaders and rendering

Read before changing any shader, material or render setting.

- **Every opaque custom shader needs a `DepthNormals` pass** (as well as `DepthOnly` and `ShadowCaster`). SSAO
  uses depth + normals, so URP builds the camera depth texture from the depth-normals prepass; anything without
  the pass is missing from it, and effects that read depth (heat haze, soft particles, SSAO) treat it as the
  ground behind.
- **Never `Shader.Find` at runtime.** Builds strip shaders nothing references; reference shaders or materials from
  a serialized field (see `WeatherSystem.PrecipitationShader`).
- **`Mathf.SmoothStep(a, b, t)` is an interpolation, not shader smoothstep.** Use `FloraMeshBuilder.Smooth` or
  `math.smoothstep`.
- Large world coordinates lose float precision in noise: keep noise offsets small and use integer hashes for
  per-cell randomness (see `PunchyCellHash` in `Sky.hlsl`).
- **Water.shader** draws three liquids from per-vertex data. Look `a` is murk, and from about 0.55 up it turns
  thick and oily (slow swell, bubbles, scum). The glow channel is `float2` (toxic glow, lava). Lava amount comes
  from `WaterLava` on biomes and landmarks (VolcanoCone).
  - Waterfalls are the river mesh's own steep faces. `steep` (from ddx/ddy of the position, so flat per triangle)
    fades out everything laid on flat xz water: streaks, carried foam, flecks, fine detail, caustics, glitter, the
    Fresnel sky. Without that, falls glowed and streaks ran upward (W-111). New flat-water terms need `* (1 - steep)`.
  - Reflections march screen space through the depth texture and sample the opaque texture, so the RP assets
    must keep Opaque Texture on.
  - Inside the march, sample with explicit LOD: implicit derivatives in a loop give a compile warning.
  - Water quality tiers (march steps, refraction, swell waves, fine-detail fade) come from `Settings/WaterQuality.cs`
    by how big a metre of water looks (reference pixels); add new costly water terms to that policy, not as a new
    distance check. Swell numbers live only in `WaterSwells.cs` (uploaded as globals); `WaterSwell.hlsl` is maths only.
  - The water has a depth-only prepass (`WaterDepth`, SRPDefaultUnlit) so displaced waves hide each other properly;
    transparents drawn after the water are depth-tested against its surface.
- `MaterialPropertyBlock.SetInt` stores a float; use `SetInteger` for `int`/`uint` shader properties.

## Learned in October 2026 (from the lane handoffs)

- After editing a `.hlsl` or shader file, run `AssetDatabase.Refresh` before taking shots, or they show the old shader.
- A shader compile error still lets the build report Succeeded: grep the build log for "Shader error" as well as "error CS".
- `line` is an HLSL keyword (geometry shaders): a variable called `line` breaks the Flora shader.
- HLSL `&&` doesn't short-circuit.
- Unity fixes a global array's length the first time it's set in a session; the editor needs a restart after a slot-count change.
- A white bloom blob on a procedural mesh is usually a zero normal: `Vector3.normalized` returns zero below magnitude 1e-5 (tiny triangles, e.g. 4 mm tooth tips), the shader normalizes it to NaN and bloom spreads it. Use `math.normalizesafe` with a fallback (A-10c, #41).
- Blue moon (Sky.hlsl PunchyBlueMoonFace): radius 0.13 rad, one thin wandering crack, maria + craters at two scales;
  its disc/corona tint must stay strongly blue (b channel > 2). Pin with `-bluemoondays 0`, then `-time 21` puts it
  43 degrees up in the east, `-time 19` low in the east (compute with DayNightCycle.Evaluate).
- Flora bark relief (the cave detail texture, albedo and normal bump) fades by distance, not by LOD: 18-45 m for plants, 60-140 m for built things (W-23). Faded by 45 m, hut roofs turned into plain cones whose facets showed at 30-55 m, which read as an LOD bug.
- Tree shadows (FloraShadowOptions, W-107): the cheap far caster and the cached far shadows are on by default, near range 25 m; still far shadows are off. Turn one off with `-cheapfarcaster 0`, `-cachedfarshadows 0` (`-stillfarshadows` turns that one on). The cache starts at the near range less CacheMove and 2 m, so a drifted camera never leaves a gap.
- Local lights go through `LightBudget` (refactor 3.1): request one each frame (`Add`, `AddStill`, or `AddReal` for a
  source that owns a Unity light and reads `RealLevel` back) with its `LightKind`; never pick a range or the nearest N
  yourself. `-lightlog` logs requests, tiers, slots, real lights and drops per kind every 5 s; `-lightbudget N` sets
  the real-light cap (default 4/6/8 by Detail).
- Rain puddles (TerrainSurface.shader) need level ground (vertex normal y > ~0.975) and a true hollow: the mesh's
  hollow depth (BuildChunkMeshJob, Strata.z) is capped by twice the lowest rise round the point, so slopes and
  downhill gullies get none. `TerrainSurface.Porosity` (vertex TEXCOORD4.x) makes sand soak rain up: it darkens,
  barely glazes, and pools only in its deepest spots.
