# Handoff: world lane (laptop, from zache-73), 2026-10-30 evening

Supervisor: the laptop session "Unity build analytics supervisor setup" (kind-possum); tasks by message and in
QUEUE-world.md; report per batch in REPORTS-world.md (LF, newest first) plus one message. One Unity at a time on the
laptop: open Unity (or bench builds) only in a slot the supervisor gives you, and say "Unity closed" after.
Worktree: C:\Users\zache\Projects\Punchy-world. Zach's feedback list: FEEDBACK-v0.10.md (W rows are this lane).

## Branches
- **world-features 0c637a9** (pushed): cave hitch, F-40 trodden mask, G-37 rocks, worm marks, cave-chunk, gfx settings
  (F5 panel + Quit), P1 rows Z-09/10/11/12a/13, zache-db's gfx-creatures.
- **world-laptop/z-p2 e2f1c97** (13 commits on world-features, UNCOMPILED): Z-33 banners + cloth LOD, Z-37 fireflies,
  Z-38 brown caps (incl. FungalGlowCap/FungalGlimmerCap, fungal forest only), Z-39 ember slivers, Z-36 bubble ripples,
  Z-41 ridge tiles, Z-42 IceSlab/IceCrag + FloraSpecies.KeepOwnColour, Z-40 forge ember pit, Z-30 wood grain
  (FloraMeshBuilder.Wood), Z-12 per-pixel grounding (Flora.shader).
- **world-laptop/blocked-top e58eed2** (2 commits, UNCOMPILED): FloraPlacement.Blocked(..., out top) and RockAt(...).
  zache-db merged it into wip/worm-arc; when they report it builds clean, land it on world-features.

## Next slot (one combined local branch, built once)
1. Merge z-p2 + blocked-top locally onto world-features, build (bench runner `build`), fix compile errors.
2. `bench.sh smoke <new> <wf>`; if NOT CLEAN, confirm with `bench.sh ab <wf> <new> townwalk` (ABBA) before chasing.
3. Looks (contact sheets): Z-33 banners, Z-36 geyser pops, Z-37 great tree at night, Z-38 fungal forest, Z-39 ember
   forest, Z-40 smithy forge, Z-41 stone house roof, Z-42 glacier, Z-30 wood before (Bench-7f519c0) / after,
   Z-12 a fountain/forum on a slope from below the base, Z-09 night with clouds, Z-10 river + fog, Z-11 bench in rain.
4. Probe the grey shards at cave mouths (style-0 cave mesh: orphan surface-net islands?), then fix.
5. Merge what passes to world-features; push; report.

## Open rows
- Z-31 vertical lines in close-up detail on buildings: look first (suspect triplanar detail texture seam).
- Z-32 stone house lit/dark bands: look first (normals or the built double-sided flip).
- Z-34 cave stream meets pool / striping; Z-35 waterfall top gap + faster fall (effect Lifetime) + spray: look first.
- Z-30: after the look, mark more timber with `b.Wood = true` (smithy, inn, hall, pen, bridge, woodpile block).
- Cave-mouth grey shards (above). P3 rows (Z-54, Z-60..64) need a design note first.

## Key files
- Flora models: Scripts/Flora/FloraGenerator.cs (form builders: Stall, Bench, Fountain, Smithy, RailFence, Signpost,
  mushrooms ~1150, gabled roofs ~3150/3220; Founded(); ToMesh flexibility), FloraMeshBuilder.cs (Vertex marks: Bark +
  0.08 strata, 0.25 seams, 0.4 veins, 0.02 wood; Skirt; Sheet/Box/Beam/Tube/Blob; Cloth).
- Placement: Flora/FloraPlacement.cs (Place(..., footprint), Blocked, RockAt, OnBuiltPlot, OnRoad, RockTint, FloraRules).
- Flora draw: Flora/FloraSystem.cs (layers, ScaleRanges, basins = fountain water, bolts); Shaders/Flora.shader
  (Frag: weathering, wood grain, ember slivers, grounding) and Flora.hlsl (wind, cloth).
- Landmarks: Terrain/Landmarks/LandmarkData.cs (Ground, TroddenGround, pools), LandmarkBaking.cs, LandmarkType.cs.
- Settlements/roads: Terrain/Settlements/RoadBaking.cs (ClearPlots), RoadsideBaking.cs, SettlementData.cs.
- Terrain: TerrainSource.cs (SampleSurface, MouthCut), TerrainStreamingSystem.cs (ApplyViewDistance, MaxBuildFrames),
  BuildChunkMeshJob.cs; Caves/CaveSystem.cs (Look(), MakePoolMesh, plants/drips).
- Settings: Scripts/Settings/GraphicsSettings.cs (View/Detail/Shadows, F5 panel, -gfx/-gfxset), Overlay.cs.
- Sky/water: Shaders/Sky.hlsl (cloud march, shooting stars), Water.shader (queue Transparent-10), LandmarkPlume.shader +
  LandmarkEffects.hlsl (bubbles).

## How to test without the editor
- Build: from Punchy-wt/Tools/bench, `B=$(pwd) bash -c ". ./lib.sh; build <commit> Build Bench-<commit>"` (~5 min; builds
  in Projects\Punchy and restores it). Local commits work (shared object store).
- Shots: run the build from Builds/Bench (`fixed` in lib.sh) with `-benchroute -benchshots <dir>` (mkdir first) and
  `-place <Landmark> -benchstops here`, or `-benchstops spot -benchspot x,z,yaw`, plus `-cam x,y,z,yaw,pitch` /
  `-camlook x,y,z,tx,ty,tz` and `-gfx Low|Ultra`. Timed frames: grab the window with System.Drawing CopyFromScreen,
  tile into a contact sheet, kill the game by exact PID.
- OgreCamp is clear (fire ~ -359,-4061; stall ~ -355,-4079); GoblinCamp sits in fog; elf grove forum at 270,3248.

## Gotchas learned today
- Git Bash `sed -i` turns CRLF files LF: edit with Python in binary/newline='' and check `git ls-files --eol`.
- Settlement streets/squares repaint ground after landmarks: anything that must show in a town goes after them.
- Probe builds (Debug.Log blob data, exaggerated values) settle "why doesn't it show" faster than tuning.
- Unity fixes a global array's length at first set (an editor needs a restart after a slot-count change).
- `git commit -p` with piped input does commit; don't chain git after a Python assert; check `git log` before push.
- GitHub pushes sometimes fail with 500s: retry. Branch switches can take >2 min (hooks): use long timeouts.
- Benches: smoke can say NOT CLEAN from round order alone; ABBA `ab` decides.
