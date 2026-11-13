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
- Each tick it renders the cells into a 256 x 256 map (`_PunchyChemMap`, `_PunchyChemRect`, `_PunchyChemOn`;
  r burning, g burnt, b ice, a frost) that the shaders read through `Shaders/ChemistryOverlay.hlsl`:
  - grass blades (`GroundDetail.hlsl`, `Grass.shader`): burnt grass is short charred stubble, burning grass glows,
    frosted grass whitens;
  - terrain (`TerrainSurface.shader`): scorched where grass burned off, glowing where it burns, white with frost;
  - water (`Water.shader`): frozen water is an opaque sheet of pale ice.
- It logs each cast and, once a second while anything has happened, the burning / burnt / ice / frost cell counts
  and its own cost per frame (`[Chemistry]` in the console).

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

F, G, R, T and V are the door key choices, so the bridge stays off them. `-nochemistry` on the command line turns
the bridge off. `-chem-demo` casts a Fireball on the nearest grass (6 m or more from the player) 8 s after
the window is seeded, once, for smoke runs without keys.

## Removing it

1. Delete this folder (`Assets/Game/Scripts/ChemistryBridge/`) and `Assets/StreamingAssets/ChemistryRules/`.
2. Remove the `com.anweather.chemistry-core` line from `Packages/manifest.json`.
3. Delete `Assets/Game/Shaders/ChemistryOverlay.hlsl` and its hooks (each is marked "Spell chemistry"): the include
   and two lines in `GroundDetail.hlsl`, three lines in `Grass.shader`, the include and the chem lines in
   `TerrainSurface.shader`, the include and the ice block in `Water.shader`.

With the bridge gone or off, `_PunchyChemOn` is never set (an unset global float is 0), so the overlay reads 0 and
every shader draws exactly as before.
