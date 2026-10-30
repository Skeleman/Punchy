# Handoff: world feature manager (v0.10 in progress)

Refreshed 2026-10-30 (last update before the main account ran out) for whoever carries the world lane on next (J-042). Worktree `D:\Unity\Punchy-world` (main PC),
branch `world-features`. Read COORDINATION.md first; this note is only what's specific to the world lane now.

## State

- **v0.9.0 is published** (rc/v0.9.0 39f5558). Everything on `world-features` since then counts toward v0.10.
- **`world-features` 1cbdd9f** (pushed). Since v0.9: G-17 Work anchors at anvils and chopping blocks, kind-possum's audit
  items (cached queries in WorldSync/NetMenu, batched DisableRendering), G-18 + G-23 roads, G-28 white water, G-34 big
  full cratered blue moon with corona, G-35 cave detail fades at mouths, G-15/F-25 swimmer rings and wakes (Water.shader
  reads the animal session's `_PunchySwimmers`), G-36 + G-30 rocks take the biome's rock on dry ground, lakes cut
  through by a gorge are lowered to the breach, G-29 ice arch variety, G-31/F-46 fungal forest (matte giant caps with
  glowing gills, drier ground, orange grass, less fog), G-33/F-47 ember slivers in leaves, F-50 Bubbles landmark effect
  (pop rings on geysers and hot springs; plumes draw after water), G-37 MagneticRock landmark (FloatingRock form). animal-behaviours 69d04db is merged in.
- **Landmark baseline: 101 landmarks, hash E128CB1D** (1e53043 rerouted roads and rerolled roadside landmarks to 100 /
  7AFF6C9F; 1cbdd9f added one MagneticRock at -1068,-3840). The supervisor wants no other landmark changes in v0.10; any new landmark type must take
  only its own spots, and the hash diff goes in the batch report.

## Unfinished (next, in order)

1. **G-37 decoration rocks** (supervisor's ask, after the worms): besides the one MagneticRock showpiece, scatter smaller
   floating rocks (6-15 m, 3-4 shapes) through the electric wastelands as biome *flora* (not landmarks, so the list
   stays 101 / E128CB1D): a second species on the FloatingRock form (`MagneticMonolith` is the 22-40 m one; copy it,
   Height 6-15, Variants 4) added to ElectricWastelands' Flora list at a low density. The form's variants already
   give shard / slab / cluster shapes.
2. **Worm marks** (`wip/world-worms`, world half of the animal session's `wip/worms`): TerrainSurface.shader reads
   `_PunchyBurrowers[8]` (x, yaw, z, w = radius, negative while breaching). Works but faint: the furrow shows, the crater
   barely. Strengthen the tilt and darkening, test (set the globals by hand in play or merge wip/worms), then land it
   and tell the animal session, who keeps its feeder off animal-behaviours until then.
3. **F-40** darker ground under market stalls: the footprint sheet in the Stall model never shows, even raised 10 cm
   (cd90cf0 on `wip/world-v09-v010`). Find why (dirt band? culling? LOD?) before re-landing.
4. **caveapproach hitch** (~110 ms at -2773,-726, every round): probably HideFarChunks, batched since 000ef06; needs a
   re-bench on 1cbdd9f and, if still there, that frame's markers.
5. Tier 1 left: F-33 limestone stalactites, F-27 lava fills the crater, F-39 street lamps/torches per culture.
6. **Phase B rivers** (later): smooth the river centreline and per-segment data; that also fixes the zigzag shading
   at sharp river corners (F-24's "corner oddities").

Waiting on Zach (need his spot or a screenshot):
- **G-32** mushroom-biome trees "more colourful at a distance": not reproduced (210 m vs 235 m across the LOD switch
  look the same in fog; fog is applied after the glow).
- **G-22** forum/fountain base stretching the stone, no dark band at the bottom: not visible on flat ground. Likely
  FloraMeshBuilder.Skirt (every ground-level vertex drops 1.6 m) where a slope exposes it.
- **Ember leak**: gold highlights in an ordinary forest after G-33/F-47: not reproduced (temperate crowns show none).
  The gate is tightened to the exact ember mark (98a61db). Only EmberOak and EmberPine carry it.

WIP branches: `wip/world-worms` (worm marks), `wip/world-v09-v010` (F-40 attempt, earlier F-50 attempt; F-50 itself is
landed), `wip/world-g18-roads` (superseded by 1e53043; delete).

## Open questions

- kind-possum: bench the swimmer wakes on a lake with ducks (up to 16 swimmers per water pixel), the F-50 bubbles at
  a geyser, and the lake look stop at 1328, 1730, yaw -90.

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
