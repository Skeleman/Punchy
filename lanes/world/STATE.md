# World lane state (after W-91, 2026-11-04)

Branch world-features, head 6d9b5ff (W-91, pushed). Worktree D:/Unity/Punchy-world.

## Hashes (v3, editor play mode, WorldProbe.LmList)

- v3 landmarks 108 / 4D2643D7 (unchanged by W-91), caves 5307D41E3A2AE83C, ground CBD3E62DCF42728C, rivers F46CF58544C3E072.
- v2 landmarks 136 / 4E61F1CF, caves C49E8E3036C741FC, ground F37805C9C70E3822, rivers 835B1404BEBDF563 (W-76).

## W-91

- #36: flora leaves house/stilt house/dock plots on river water empty (LandmarkSampling.OnRiver); villages log plots dropped (v3: 0/6, 2/6, 0/6).
- #88: GrandFalls.BuildShelf scores the land behind each bay (biomes 100/200 m behind, rise) and logs before/after. On v3 the pick stays at bearing 120 (SaltFlat behind). Open: raise the land weight or loosen the bay test.
- #40: carnivorous plant 1/4 size, head 1.7x, vines 0.5x. Renderers stay visible at 50/150/300 m in MarshFog; Colossus shader haze fade not checked by eye.

## Open

- #88 shelf still faces salt flat on v3.
- #40 eye check of w91-plant-fog-*.png.
- Lake backwater over low banks; unexplained v2 water steps.

## Tools and gotchas

- Untracked Assets/Temp/Editor/WorldProbe.cs and W91Probe.cs (never commit): LmList, Shot, H, plant probes.
- The lake shelf needs PUNCHY_LAKESHELF=1 in the editor env (SetEnvironmentVariable via eval, then re-enter play); -lakeshelf in PlayArgs alone was not seen.
- Island pick: PUNCHY_ISLAND env (v3 for current cards).
