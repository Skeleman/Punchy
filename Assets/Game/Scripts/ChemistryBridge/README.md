# Chemistry bridge (experiment)

A proof of concept: the spell-chemistry core from Spell Chemistry Valley (the `com.anweather.chemistry-core`
package, its rules in `Assets/StreamingAssets/ChemistryRules/`) running inside Punchy's world. Local and cosmetic
only: nothing is networked or saved.

## What it does

- Once the player has spawned, `ChemistryBridge` (created by itself at scene load, no scene edits) builds a
  256 x 256 cell world, 1 m cells, in a window centred on the player. It seeds it from `TerrainSource` on the 1 m
  grid in a Burst job: grass where blades grow (as `GroundDetailSystem` decides), water where there's water, rock
  on steep slopes, dirt elsewhere. Cell (x, y) is Punchy's (x, z) plus the window's corner. Walk more than 96 m
  from the window's centre and it's rebuilt round you (what burned there is forgotten).
- It ticks the chemistry at the rules' rate (6 a second), at most 2 ms a frame (a tick can span frames).
- Each tick it renders the cells into two 256 x 256 maps (`_PunchyChemMap`: r burning, g burnt, b ice, a frost;
  `_PunchyChemMap2`: r water standing on land, g steam, b smoke; with `_PunchyChemRect`, `_PunchyChemOn`) that the
  shaders read through `Shaders/ChemistryOverlay.hlsl`. The maps are bilinear and `PunchyChemSoft` thresholds them
  near 0.5 with a noise-wandered edge, so ice, scorch, frost and water show soft, organic edges rather than 1 m squares:
  - grass blades (`GroundDetail.hlsl`, `Grass.shader`): burnt grass is short charred stubble, burning grass glows,
    frosted grass whitens;
  - terrain (`TerrainSurface.shader`): scorched where grass burned off, glowing where it burns, white with frost;
    water poured on land is a sheet drawn like a rain puddle (dark film, sky reflection) with damp ground round it,
    and frozen water on land is ice;
  - water (`Water.shader`): frozen water is a sheet of pale ice with a bright, thinner lip at its edge. When fire melts
    it, the map's ice goes and Punchy's water shows again.
- `ChemistryEffects` draws a flame on every burning cell through Punchy's standard fire (`FireSystem`: flame, sparks,
  glow and a little smoke, far flames beyond 70 m, a second lower tongue per cell within 35 m) plus one fire light,
  so the flames follow the fire front and go out with the cells. Gas cells puff soft billboards from two pooled
  ParticleSystems (at most 48 puffs a frame): white steam that rises and thins as the gas does, on the skunk's
  gas-cloud material (`Resources/SkunkGas`, unlit, not faded at the ground), with a burst off each cell the water puts
  out; grey smoke on the breath-puff material (`AmbienceSystem.PuffMaterial`, lit); and a wisp of vapour off ice as it
  melts (cosmetic: the melt itself makes no steam).
- It logs each cast and, once a second while anything has happened, the cell counts (burning, burnt, ice, frost,
  water on land, steam, smoke), the reactions it has seen change the cells since the window was seeded (ice melted,
  fires doused, water boiled to steam, new steam, steam condensed) and its own cost per frame (`[Chemistry]` in the
  console).

## Where the core comes from

`Packages/manifest.json` points at the package by a local `file:` path into a checkout of Spell Chemistry Valley
(`packages/com.anweather.chemistry-core`, with its `dev` up to date). The package declares
`com.unity.nuget.newtonsoft-json` 3.2.2, which Punchy already resolves. Its asmdef also names a
`Prototype.Analyzers` reference (Spell Chemistry Valley's own Roslyn analyzers); Unity drops the missing
reference silently here (no error, no warning).

## First run (2026-11-12, Editor, 1280 x 720)

- Fireball on a lake-side meadow: 19 cells burning at once; after 6 s 130 burning / 370 burnt, after 26 s 405
  burning / 7143 burnt, the ring still spreading through the grass and leaving scorched ground behind it.
- Frost (charged) on the lake: 15 cells of ice; Sleet Storm: 132.
- Cost: about 0.75 ms a frame on average (a tick about 5 ms, sliced to 2 ms a frame; the map about 2 ms, 6
  times a second). No console errors or warnings.

## Keys

| Key | Spell |
|---|---|
| H | Fireball at the ground under the screen centre (a burst: grass round it catches and the fire spreads) |
| Shift+H | Fire Bolt (one cell) |
| J | Frost, charged: a line of cold through the aimed cell (water freezes, ground frosts over) |
| Shift+J | Sleet Storm (a burst of cold, radius 5: freezes a pond) |
| L | Create Water, charged once (5 x 5): douses fire in a hiss of steam, stands on land as a sheet of water |
| Shift+L | Wave (a line of water rolling out through the aimed cell) |

F, G, R, T and V are the door key choices and K is the weather key, so the bridge stays off them (hence L for water). `-nochemistry` on the command line turns
the bridge off. `-chem-demo` casts a Fireball on the nearest grass (6 m or more from the player) 8 s after
the window is seeded, once, for smoke runs without keys.

## Second round (2026-11-13, Editor, 1280 x 720, a lake shore)

- Sleet Storm twice on the lake: 157 cells of ice, drawn as one wavy, rounded floe (the maps are read at a
  noise-warped point, up to about 1.5 m, then thresholded with a wandering edge).
- Fireball on the meadow above it: a wall of flames along the front, a smoke haze over it; Create Water three times
  on the front: gaps in the flame line, blue-green sheets of water with damp edges, steam rising off them.
- Fireball twice on the ice: a hole melted through the floe each time, open water again.
- Reactions the map saw change cells: 50 melted, 24 doused, 27 boiled to steam, 88 new steam, 12 condensed (Frost
  and Sleet Storm through steam).
- Cost: about 1.05 ms a frame on average (max 1.17) with a 200-cell fire front and 1000 puffs; no console errors.

## Removing it

1. Delete this folder (`Assets/Game/Scripts/ChemistryBridge/`) and `Assets/StreamingAssets/ChemistryRules/`.
2. Remove the `com.anweather.chemistry-core` line from `Packages/manifest.json`.
3. Delete `Assets/Game/Shaders/ChemistryOverlay.hlsl` and its hooks (each is marked "Spell chemistry"): the include
   and two lines in `GroundDetail.hlsl`, three lines in `Grass.shader`, the include and the chem lines in
   `TerrainSurface.shader`, the include and the ice block in `Water.shader`.

With the bridge gone or off, `_PunchyChemOn` is never set (an unset global float is 0), so the overlay reads 0 and
every shader draws exactly as before.
