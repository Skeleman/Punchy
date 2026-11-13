# Punchy

An open-world exploration prototype: a procedurally generated island with biomes, rivers, weather, a day/night
cycle, procedural plants and procedurally animated animals. Unity 6000.5.8f1, URP 17.5, Entities 6.5 (ECS + Burst),
Input System. Windows is the only build target so far.

## What we are building

Zach, 2026-11-13: "I am looking for a world that feels expansive, yet full of life and detail. Fidelity of distant
views is very important, wanting to make sure the whole island feels grounded in beautiful nature."

Weigh every change against that, in this order: the far view (the whole island in sight, nature first), life and
small details near and far, shared foundations that let both scale, then new feature lines.

This file is kept short on purpose. Detail lives in `docs/agents/`: read only the files your task touches.

## Read before you touch

| If your task touches | Read |
|---|---|
| Terrain, biomes, rivers, landmarks, settlements, roads, caves | `docs/agents/world.md` |
| Creatures, behaviour, poses, the player's body, residents, workers | `docs/agents/fauna.md` |
| Plant species, rocks, props, flora forms | `docs/agents/flora.md` |
| Any shader, material or render setting | `docs/agents/rendering.md` |
| Jobs, per-frame work, anything that samples the terrain | `docs/agents/performance.md` |
| Input, network tables, world state, anything other players see | `docs/agents/multiplayer.md` |
| Assets, scenes, editor CLI, commits | `docs/agents/editor-and-assets.md` |
| Player builds, benches, releases | `docs/agents/build-release.md` |
| Finding a system's folder, debug keys | `docs/agents/systems.md` |

When you learn a lasting gotcha, add one or two lines to the matching `docs/agents/` file, not here.

## Rules that apply everywhere

- **`TerrainSource` is the single source of truth** for the ground (height, biome, water, surface). Never compute
  heights another way. Anything that can go underground uses `FloorUnder(float3)`, not `SampleHeight`.
- **Content is data:** biomes, species, landmarks and particles are assets. Most new content is a new asset or a
  biome list entry. Editing a biome, surface or landmark asset re-bakes the terrain and can reshuffle the island;
  plant, animal and particle assets apply live in Play mode.
- **Never `Shader.Find` at runtime, and never `Schedule(...).Complete()` heavy work in the same frame.**
- **Every feature must work solo, as host and as a joined client.** Decide which kind of state it is (seed-derived,
  shared world state, simulated, local player, cosmetic) using `docs/agents/multiplayer.md`.
- **Far view first.** Anything big enough to see from a ridge or from the air (landforms, forest, water, buildings,
  large creatures, herds, lights, smoke) ships with a far form picked by on-screen size (`DetailPolicy.ScreenSize`,
  never fixed metres), with no pop at the hand-over. Far forms are drawn only and built from the same placement data
  as the real thing. Nothing visual is tied to the edge of the streamed terrain. See `docs/agents/rendering.md`.
- **Share, don't repeat.** Surface shaders take lighting, shadows and haze from the shared includes, not their own
  copy. Items, worn things, ownership and saved state use the shared definitions once they exist: don't add a
  one-off stand-in. Don't add code to a file over 2500 lines: add a partial or a new file beside it.
- **Line endings are mixed per file.** Don't use `sed -i`; check `git ls-files --eol <file>` before `git add`.

## Working in this repo

- Use the terms in `GLOSSARY.md` in code, assets, commits, docs and messages between sessions.
- Keep code commented the way it is: a summary comment on each class saying what it is and why, short comments on
  non-obvious steps. Match the surrounding naming and idioms.
- Collaborators: work on a branch or fork rather than committing to `main` directly.
- The repo is public: everything pushed (code, assets, commit messages, docs, coordination files) can be read by
  anyone. Never commit secrets (keys, tokens, passwords), personal information, or machine-specific paths such as
  `C:\Users\<name>\...`.

## Roadmap Ideas
When Zach shares long-term or 'someday' feature ideas, append them to ROADMAP-IDEAS.md under the right category with the date. Do not implement them unless he explicitly asks. When he asks for a change meant for another agent, add it to that agent's card queue and confirm which card it went on.
