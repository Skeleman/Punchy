# Handoff: world feature manager (after v0.8.0)

Written 2026-10-29 by the outgoing world session for the fresh one. The worktree is `D:\Unity\Punchy-world` (Main PC).
Read COORDINATION.md first; this note covers only what's specific to the world lane right now.

## Branches

| Branch | Where | State |
|---|---|---|
| `world-features` | origin | **5268dc5**: v0.8.0 (ba87e14) merged in, plus all the post-cut-off world work below. Pushed. |
| `world-next` | local only | Same as `world-features` (5268dc5). It was the holding branch while the RC was frozen; work on `world-features` again. |
| `rc-world` | local only | Old copy of `rc/v0.8.0`. Can be deleted. |
| `rc-test/*` | origin | Throwaway branches from the quit-crash hunt (`priming-off`, `priming-no-passes`, `priming-auto`, `priming-off-at-quit`, `forward-priming`, `d3d11-first`, `d3d11-args`, `args-fix`). All merged or abandoned; delete them once the supervisor agrees. |

There's also a scratch git worktree in the old session's temp folder (`quit-test`). `git worktree prune` cleans it up once that folder is gone.

## What's in world-features since the v0.8.0 cut-off (all checked in game unless noted)

- **Sky events:** shooting stars, meteor showers, auroras in cold biomes (Sky.hlsl, the supervisor's loan; SkyEventsSystem).
- **Hinged doors:** door leaves swing. `FloraDoors.SetOpen(doorAnchor.Position, 0..1)` is the contract sent to the animal session. Door anchors for huts, stone houses, towers and inns now stand 0.6 m out from the real door (`FloraGenerator.DoorFront`).
- **Ruins:** the `Ruin` form (51), `Ruins` landmark in temperate/grassland/marsh/tundra; the animal session adds ghosts.
- **Perches:** `FloraPlacement.NearPerches` (roof peaks, ridges, chimneys, posts, ruin wall tops). The animal session's songbirds use it.
- **Camp and hermit anchors:** `AnchorKind` Camp, Bed, Home, Garden; `LandmarkType.Anchors` (WildAnchors); `AnchorBaking.AddWildAnchors`.
  - A **test HermitHut** is hand-placed at (140, -545) in `World.unity`'s PlacedLandmarks. Real hermit placement is Phase C.
- **Campfires and bedrolls:** night-only flames (`LandmarkEffect.NightOnly`), and the `Bedroll` form (52), soft cloth.
- **Small touches:** glacier cold mist (`ColdMist`), and salt-flat landmarks `SaltPillars` and `SaltCairns`.
- **Phase A roads, first slice:**
  - `RoadsideBaking`: landmarks beside the roads (Campsite, LoneInn, RoadShrine, Monument, BurntCampfire, Ruins, CaveEntrance), seeded per 120 m spot, appended to the landmark set so nothing else moves.
  - Spurs to each, fork signposts.
  - Rail fences outside human villages and dwarf holds (`RailFence` form 53, `PlotRole.Fence`, `ExtraSlots` = 5).
  - Roads route round cliffs inside grid cells.
- The island has 104 landmarks: the 83 from v0.8.0, unchanged, plus 21 new. The coordinates went to kind-possum and the animal session on 2026-10-29.

## Unfinished in the world lane

1. **Phase A roads, rest of it:**
   - The roadside mix is uneven: Ruins and caves rarely win a spot.
   - **F-43:** signposts only at real forks, none near towns. That conflicts with the current gate signposts and fork posts, so rework it.
   - Cliff roads: 50 of about 2,700 road stretches still run over ground steeper than 45° (sheer terrace walls in mesas; roads leaving towns on rough ground). Two attempts, cliff-safe smoothing and verify-and-reroute, didn't help and were reverted. Try a finer routing grid, or switchbacks.
   - Forest stretches already look right; nothing to do there.
2. **Phase C, hermits:** the real HermitHut, CaveCamp and the four biome shelters, using the anchor contract already sent. Replace the test hut.
3. **Phase B, rivers:** start with Zach's smoothing request, since everything else builds on the path:
   - smooth the drainage centreline and resample it;
   - meanders on flat ground (straighter in gorges), seeded;
   - carve beds and banks from the curve;
   - re-check bridges, fords, lake joins and towns;
   - measure the max turn per 10 m and the count of segments at 0, 45 and 90°, with an 800 px before/after.
   - Then canyons and deltas.
4. **Fruit trees, orchards and the Harvest anchor contract** (after B).
5. **The flora naming cleanup** (Zach approved): a `Category` on FloraSpecies, filled from Form; a category-aware inspector; per-form colour labels; Flexibility forced to 0 for rocks and structures. One commit; tell the animal session first (shared file).
6. **The glow-count bug:** CaveSystem sets `_PunchyCaveHeatCount` with `SetGlobalInt`, a float, but the shaders declare it `int`, so they read 0. Glow lighting from caps, crystals and lava may never reach any shader. The fix is `SetGlobalInteger`. First check in game what's visibly missing, then measure before and after: it adds per-pixel cost where glow is dense.
7. **A profiler marker and counter for terrain chunks started per frame** (kind-possum asked).

## Queue: Zach's v0.8.0 feedback (FEEDBACK-2026-10-29.md)

The supervisor's proposed v0.9 minimum for this lane:
- F-22: river oddity at (1090, 1530).
- F-24: lily pads in rivers; white water pulses too much; corner oddities at (-2083, -1040).
- F-30: disconnected cave stream at (-615, -838); streams end in a pool.
- F-31: the entrance fix only, at (-2766, -624).
- F-34: mine trusses on downward slopes.
- F-36: hut roof collision.
- F-42: the stall awning flapping.
- F-43: signposts.
- F-44: mesa arches: seated, rarer, no icicles.
- F-48: the waterfall landmark at (623, 2519).
- F-49: floating rocks at (1909, 2331).
- F-52: crystals pulsing; they should be static glass.

The rest of the W items in that file follow, after the minimum.

## Rendering and performance state (v0.8.0)

- **Depth priming is Forced on PC_Renderer.** It cut town and glide GPU time by a third to a half. Every opaque shader must have a `DepthNormals` pass, or it vanishes under priming. Grass and Pebbles got theirs; Butterflies draws at Transparent-50.
- **Windows starts on D3D11 (D3D12 is the fallback)** because D3D12 plus priming crashes on every quit: an access violation after "CodeReloadManager destroyed".
  - It reproduces on the main PC and the laptop.
  - Not fixed by Unity 6000.5.11f1 (tested; still installed, unused). Priming Auto and turning priming off at quit don't help; plain Forward doesn't either.
  - To get D3D12 back (async compute, VRS): try Unity 6.6, or file a bug with a minimal repro (URP Forward+, Forced priming, D3D12, quit).
- **D3D11 gotcha:** a buffer can't be `IndirectArguments | Raw` there; the Raw is silently dropped. FloraSystem now counts into a structured copy and copies it with `Graphics.CopyBuffer`.
- **Ideas parked for later:**
  - Forward instead of Forward+: one run gave much better 1% lows (town 47 vs about 35). Needs a proper A/B.
  - Variable rate shading: available in our URP 17.5, but D3D12/Vulkan only.
  - Impostors for far trees: an estimate is in the outgoing session's notes. Since we're pixel-bound, there's little gain until vertex cost matters. Roughly 4 side views plus 1 top view, albedo plus normal, about 0.12 MB per variant with BC7, about 18 MB in total, under a second to bake.

## Gotchas worth keeping

- **Stable seeds:**
  - Landmark spots (5b) and biome picks (5c) are keyed by position and by name hash, not by list order.
  - `math.hash(uintN)` has no avalanche, so `BiomeSampling.Scramble` before comparing per-name draws.
  - A new landmark type only takes the spots it wins, and roadside landmarks are appended.
  - Before any push that changes placement or the fingerprint, diff the full landmark list and send coordinates.
- **Line endings** are per file (some CRLF, some LF). Check `git ls-files --eol` before every add, and patch files in their own ending.
- **Rebakes:** wait about 100 to 130 s after a force bake before entering play mode, or you get stale bakes. After .hlsl or .shader edits, run `AssetDatabase.Refresh`.
- **Shots:**
  - 16:9 at true aspect (set `camera.aspect`); noon and clear weather; avatar out of frame.
  - Review your own shots at about 480 px.
  - Disable the camera's MonoBehaviours before moving the camera from a script, or it snaps back.
- **Player test runs:** don't pass `-screen-fullscreen` or `-screen-width`; Unity saves them as the user's preference (F-01 came from that). If you did, clear the non-"Default" `Screenmanager` values under `HKCU\Software\DefaultCompany\Punchy`.
- **Builds from this lane:** only to `D:\Unity\Punchy-world\Builds\Dev\Punchy.exe`. Never write in another session's tree (`D:\Unity\Punchy` is the animal session's). Never trigger UAC: extra editors go in through Unity Hub or by Zach.
- **Editor turns:** two editors at once have run the PC out of memory. Hand the editor back and forth with the animal session (close, ping) and kill idle AssetImportWorkers after an import.
- **The old session's scratch tools** (BuildRig shot rig, probes such as LmList, WildCheck, RoadSlopes, GpuTime, DepthPrime, ForceBake, Refresh) lived in its temp scratchpad and are gone. Recreate the ones you need; LmList (a sorted landmark type + position list) is the most useful for placement diffs.

## Shots

- Milestone finals and galleries: `D:\Unity\PunchyMilestones\world\<feature>\gallery\` (COORDINATION.md rule 12).
- Working captures from the editor land in `Assets/Temp/shots` in the world worktree. Untracked; never commit them.
