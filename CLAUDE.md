# Punchy

An open-world exploration prototype: a procedurally generated island with biomes, rivers, weather, a day/night
cycle, procedural plants and procedurally animated animals. Unity 6000.5.8f1, URP 17.5, Entities 6.5 (ECS + Burst),
Input System. Windows is the only build target so far.

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
