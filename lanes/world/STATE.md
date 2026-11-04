# World lane state (after W-93, 2026-11-04)

Branch world-features, head 34a0924 (W-93, pushed). Worktree D:/Unity/Punchy-world.

## Hashes (v3, editor play mode, WorldProbe.LmList)

- v3 landmarks 108 / 4D2643D7 (unchanged by W-93), caves 5307D41E3A2AE83C, ground CBD3E62DCF42728C, rivers F46CF58544C3E072.
- v2 landmarks 136 / 4E61F1CF, caves C49E8E3036C741FC, ground F37805C9C70E3822, rivers 835B1404BEBDF563 (W-76).

## W-91

- #36: flora leaves house/stilt house/dock plots on river water empty (LandmarkSampling.OnRiver); villages log plots dropped (v3: 0/6, 2/6, 0/6).
- #88: GrandFalls.BuildShelf scores the land behind each bay (biomes 100/200 m behind, rise) and logs before/after. On v3 the pick stays at bearing 120 (SaltFlat behind). Open: raise the land weight or loosen the bay test.
- #40: carnivorous plant 1/4 size, head 1.7x, vines 0.5x. Renderers stay visible at 50/150/300 m in MarshFog; Colossus shader haze fade not checked by eye.

## W-92

- #88: BuildShelf rules out bays with arid land 50/100/200 m behind (land weight 4). On v3 all 11 high-ground bays have arid land behind, so the logged fallback keeps bearing 120.
- #40: plant leafy skirt (9-13 drooping leaves, purple veins, red tips/spots). SaltCrystal colossus (slot 7, SaltFlat): 13 m ball rolls a 36 m circle round (3702, -917), 90 m fading dirt trail (SaltCrystalRoll).

## W-93

- #70: wasteland Debris/FloatingRock mounds get terraced kit boxes (0.35 m tiers); fallen chunks under 0.45 m are flat decals, no boxes.
- #40: plant skirt darker, rooted high on the bulb and draping down. Salt ball lumpy (own rng), furrow trail with lips; roll on _PunchyClock, absolute rotation, trail rebuilt per 0.5 m.

## Open

- #69 row 2 of W-93 (kit-built hazard houses per harsh biome) not started: W-57b HazardShelter domes + PlaceShelters swap already exist; needs a kit shelter shape.
- #70 caveat: on slopes a drift mound's lowest tier edge reads as a wall from the downhill side.

- #88 shelf still faces salt flat on v3: no non-arid bay with 10 m high ground behind exists (Zach: move or terraform the shelf?).
- #40 eye check of w91-plant-fog-*.png.
- Lake backwater over low banks; unexplained v2 water steps.

## Tools and gotchas

- Untracked Assets/Temp/Editor/WorldProbe.cs and W91Probe.cs (never commit): LmList, Shot, H, plant probes.
- Unity wipes Temp/PlayArgs.txt at editor start: write it after the editor opens, then enter play; -island/-lakeshelf there now work. Scripts don't recompile while in play mode: stop play first.
- Island pick: PUNCHY_ISLAND env (v3 for current cards).
