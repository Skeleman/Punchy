# Handoff: world feature manager (v0.10 in progress)

Refreshed 2026-10-30 for whoever carries the world lane on next (J-042). Worktree `D:\Unity\Punchy-world` (main PC),
branch `world-features`. Read COORDINATION.md first; this note is only what's specific to the world lane now.

## State

- **v0.9.0 is published** (rc/v0.9.0 39f5558). Everything on `world-features` since then counts toward v0.10.
- **`world-features` 79b4221** (pushed). Since v0.9: G-17 Work anchors at anvils and chopping blocks, kind-possum's audit
  items (cached queries in WorldSync/NetMenu, batched DisableRendering), G-18 + G-23 roads, G-28 white water, G-34 big
  full cratered blue moon with corona, G-35 cave detail fades at mouths, G-15/F-25 swimmer rings and wakes (Water.shader
  reads the animal session's `_PunchySwimmers`), G-36 + G-30 rocks take the biome's rock on dry ground, lakes cut
  through by a gorge are lowered to the breach, G-29 ice arch variety. animal-behaviours 69d04db is merged in.
- **Landmark baseline: 100 landmarks, hash 7AFF6C9F** (since 1e53043, the planned v0.10 island change: roads reroute,
  roadside landmarks reroll). The supervisor wants no other landmark changes in v0.10; any new landmark type must take
  only its own spots, and the hash diff goes in the batch report.

## Unfinished (in the supervisor's order)

1. **caveapproach hitch** (~110 ms, every round, at -2773,-726): likely `TerrainStreamingSystem.HideFarChunks`, batched
   since 000ef06. Waiting on kind-possum's re-bench of 807ee9f+; if it's still there, get that frame's markers.
2. **G-31/F-46** mushroom caps: mostly not glowing, glowing gill veins underneath; drier ground, short orange grass.
3. **G-33/F-47** ember forest: glowing slivers in leaves and needles.
4. **G-32** mushroom-biome tree LODs turn more colourful with distance (fog differs per LOD).
5. **G-22** forum/fountain base: the foundation stretches the stone texture and lacks the dark band at the bottom.
6. **F-40** darker ground under market stalls: the footprint sheet in the Stall model never shows, even raised 10 cm.
   Commit cd90cf0 is on `wip/world-v09-v010`; find out why (dirt band? culling? LOD?) before re-landing.
7. **F-50** geyser/hot-spring bubbles: plume flecks are too faint. Zach's idea: a reusable BubbleSurface, small 2D
   bubble-pop flipbook sprites on the surface near the camera, usable by mud pits and lava too.
8. **G-37** (Zach): enormous floating, rotating magnetic rocks in the electric wastelands, a few shapes and sizes, a
   faint crackle like the spires. A biome decoration or a new landmark type taking only its own spots.
9. Tier 1 left: F-33 limestone stalactites, F-27 lava fills the crater, F-39 street lamps/torches per culture.
10. **Phase B rivers** (later): smooth the river centreline and its per-segment data. That's also where the zigzag
    shading at sharp river corners (F-24's "corner oddities") gets fixed.
- WIP branches: `wip/world-v09-v010` (F-40, F-50 attempts, the plume draw-after-water change), `wip/world-g18-roads`
  (superseded by 1e53043; delete when convenient).

## Open questions

- None waiting on Zach. kind-possum: bench the swimmer wakes on a lake with ducks (up to 16 swimmers per water pixel),
  and the new lake look stop (stand at 1328, 1730, yaw -90).

## Gotchas

- **Probe:** an untracked `Assets/Temp/Editor/WorldProbe.cs` (never commit Assets/Temp) does landmark lists and hashes
  (`LmList`), water/rock/cave numbers and framed shots via `unity command eval`. Play flags go in `Temp/PlayArgs.txt`
  (`-time 12 -nohud`, `-place <Biome>`).
- **Editors:** Zach allows both editors at once; keep 4 GB free. Each refresh spawns two AssetImportWorkers (~1.5 GB
  each): kill this tree's own afterwards. Never stop the animal session's processes.
- **Close the editor before switching commits** (an RC build, a merge that touches a scene): otherwise a modal "scene
  modified externally" dialog blocks it, and after a kill "Recovering Scene Backups" appears (delete this tree's
  `Temp/__Backupscenes` before reopening).
- **Builds:** `EditorApplication.delayCall` never fires in an idle editor. Call `BuildPipeline.BuildPlayer` directly
  in an eval, which times out at 30 s while the build carries on; write the summary to `Temp/build-result.txt` and poll
  it. Player builds go to `Builds/Dev/Punchy.exe` (one fixed path, rule 14), D3D11 first. Smoke test without -screen
  flags.
- **Shots:** offscreen `Shot` renders miss some live state; for particles or plumes use `ScreenCapture` with the camera
  posed (camera MonoBehaviours disabled first). Shader edits need play mode stopped, then Refresh, or play resets.
- **Line endings** are per file. Python/sed edits must keep CRLF files CRLF (`git ls-files --eol` before every add).
- **Stable seeds:** landmark spots and roadside spots are keyed by position. Rerouting roads rerolls roadside landmarks.
- The flora `Ground` channel: alpha 255 = a rock tint (`FloraPlacement.RockTintMark`), any other non-zero alpha = a
  built thing's dirt band.
- The friend's repo (anweather/5e-compatible-game) has no licence: ideas only, never code or assets.
