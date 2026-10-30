# World reports (world lane → supervisor kind-possum)

Newest first.

## 2026-10-30: slot (cave-chunk, gfx settings, P1 checks), zache-73. Unity closed

No Unity, Punchy or bee_backend process running; Projects\Punchy on b/weather-mapjob.
- **Merged cave-chunk** (world-features 0d4e964). **Correction to 256e6a9's commit claim:** "about five times cheaper"
  was the mouth-cut sampling per vertex, not the chunk. At frame level the busiest BuildChunkMeshJob went
  36.8 -> 30.9 ms (-16%); the A/B gain was 1% low 52.5 -> 59.6, worst 27/30 -> 25/21 ms.
- **Merged gfx-settings and z-p1 together: world-features 0d4e964 -> 7f519c0** (fast-forward of a combined branch:
  both merges, plus fountain water on DetailRange and the Z-13 follow-up).
  - Compiles (both branches at once, no errors).
  - Startup line logged: "Graphics: view 1.0 detail 1.0 shadows 1.0 (Medium)". 0 exceptions.
  - Medium = today: the smoke said NOT CLEAN (townwalk cap 0, 1% low 52.6 -> 48.2), but its rounds disagreed (cap 60
    went *up* 36 -> 44). An interleaved ABBA `ab` townwalk came back flat (41.8 -> 41.4, OK).
  - Presets at a village edge, cap 60:
    | Preset | GPU ms | 1% low fps |
    |---|---|---|
    | Low | 6.9 | 58.1 |
    | Medium | 9.0 | 40.8 |
    | Ultra | 15.7 | 31.3 |
    Low hazes and thins the distance, Ultra shows the far forest and more grass, and nothing near the player drops
    out. The profiler's sweep picks this laptop's default.
- **P1 looks:**
  - Z-12 (270,3248): rim capped, basin water drawn as real water. No stretched texture visible from the forum (the
    floor stripes are column shadows); needs Zach's camera if it's still there.
  - Z-11: the elven bench reads as a stone slab. The wet-image look still needs a rainy setup: next slot.
  - **Z-13 was not fixed by the plot clearing.** The cabbage patch is a **hermit's garden** (a HermitHut prop) on a
    road routed after the landmark. b651be6: a landmark's placed props give way to roads and spurs (streets with
    Road set); squares don't count. Re-shot: garden gone, road clear. The plot clearing also removed two houses
    the road ran into at that village's edge.
  - Z-09 and Z-10 weren't looked at (they need night/cloud and fog setups): next slot.
- **Grey shards at cave mouths (item 15 shots, from before cave-chunk): queued, not fixed.** They're flat,
  untextured and grey: the cave material's own colour, style slot 0. In CaveSystem's mesh Look(), a vertex with no
  owning cave (CaveSampling.Distance gives -1) gets style 0. Likely small disconnected surface-net islands where
  the mouth's blended field (surface air smooth-min'd with cave air) dips into a pocket away from the passage. Fix
  options: drop tiny disconnected islands in the mesh job, or give orphan vertices the nearest cave's style and the
  terrain look. Needs a probe build first (count islands at that mouth).
- Reviewed for zache-db: 5198212/4f597bf mask checks OK; the wake-trail shader OK with a distance gate (45 m in
  aa6d715).
- NEXT: the cave-mouth shards (probe, then fix), the Z-09/10/11 looks in a slot, then P2.

## 2026-10-30: Zach's v0.10 P1 rows (W), zache-73

`world-laptop/z-p1` 9124707 (off world-features 3d874dc), not compiled; shots in my slot. Z-01 is on
world-laptop/gfx-settings (a909076).
- **Z-01 Quit: DONE in code.** "Quit game" in the F5 panel, with a confirm. F5 needs a line in the F1 key list
  (zache-db is adding it).
- **Z-09 shooting stars in front of clouds: DONE in code (a4a130a).** The cloud march stopped at 2% transmittance
  but kept that 2% of the sky behind, and a streak (5x the sky) shone through dark night clouds. Now opaque there.
  Streaks and auroras are also dimmed by the cloud cover along their own direction (one cheap sample, only where
  one is lit).
- **Z-10 river over nearer fog: DONE in code (5403f55).** The water shared the Transparent queue with fog wisps, flames,
  rain and falls, and a big water chunk sorted by its middle could draw after mist in front of it. Water is now
  Transparent-10, drawn first (everything else in that queue stands above it). Water.shader is shared: zache-db,
  please note.
- **Z-11 benches: 2 of 3 DONE in code.**
  - d0e176f: built (Founded) forms are meshed with no wind sway. Twelve species swayed like plants, not only the
    benches: halls, lodges, shrines, palisades, fences, signposts, stumps. Gardens keep sway for their plants;
    cloth keeps its own motion.
  - aea3643: elven benches get a carved stone slab seat (new `FloraSpecies.StoneSeat`, set on ElfBench).
  - The view-dependent "wet image": the likely cause is the built-thing weathering. Its octaves fade by screen
    footprint (fwidth) and the survivors are scaled up 2.2x, so a small seat's mottling changes with the camera.
    I'll confirm with a look before changing it, as you said.
- **Z-12 fountain: 2 of 3 DONE in code (9124707).**
  - The rim had no top: wall and rim band are both vertical, and from above the gap showed the ground. It's now
    capped with a flat stone ring.
  - The water is the game's own: `FloraModel.Pools` records basins, and FloraSystem draws them within 80 m as
    landmark pools are drawn (`CaveSystem.MakePoolMesh`, now internal). The old shiny discs stand in further out.
    The 80 m goes on DetailRange when gfx-settings merges.
  - The stretched texture at 270,3248 needs a look first (it's G-22's forum base; suspect the 1.6 m foundation skirt).
- **Z-13 road through a cabbage patch: DONE in code (8f90d47).** Towns are closed to roads only inside 0.85 of their
  radius, but plots reach the edge. A plot a road crosses now becomes an empty green (Landscaping, no building),
  keeping its index. Roads and roadside landmarks don't move (landmark list unchanged), but **the town layout
  changes, so the world fingerprint does too.**
- **Z-14 night howl: handed to zache-db (their files).** It's the wolves' SoundId.Howl: volume 1, reach 160 m,
  with a breathy noise layer.
- **Z-02 crow stuck, cave side: nothing to change.** CaveSystem never touches player control; its only freeze, the
  mouth hitch, was fixed before v0.10. Flight into cliff or cave rock was cancelled whole in movement:
  zache-db's f378a43.
- NEXT: the gfx + P1 slot (compile both branches; Medium = today assert; flat A/B; Low/Ultra shots; looks for Z-09,
  10, 11, 12, 13 and the two "needs a look" items), then P2.

## 2026-10-30: graphics settings steps 1-2 and Z-01, zache-73

`world-laptop/gfx-settings` 9cf4e26 (9 commits off bc89606), not compiled or merged; the checks come in my slot.
- **Step 1 (6a16561):** `Punchy.Settings.GraphicsSettings` (the design said Punchy.Rendering; Settings matches
  FrameRateCap's folder and pattern). View, Detail and Shadows multipliers with Low/Medium/High/Ultra presets,
  PlayerPrefs, and the **F5** panel (F4 is the Options action that steps the frame cap). `-gfx` and `-gfxset` apply to
  one run and are never saved: the panel only applies on a real slider move, so a command-line value off the
  sliders' 0.05 steps isn't rounded and saved on opening. Startup log line: "Graphics: view 1.0 detail 1.0 shadows
  1.0 (Medium)". The URP shadow distance and cascades are restored on quitting. `_PunchyBurrowRange` =
  DetailRange(150). `Punchy.UI.Overlay.AnyOpen` replaces NetMenu.IsOpen in ThirdPersonCamera (1 line +
  comment), PlayerInputSystem (1) and InteractionInputSystem (3): your approved edits. zache-db has the API and waits
  for your OK to wire the creature ranges.
- **Step 2, world ranges, one commit each:**
  - GroundDetail: grass 60, pebbles 45, full density 20 (Detail).
  - LandmarkEffect: 700 (View); flames 150 and cave falls 120 (Detail).
  - Critters 70 (Detail).
  - Ambient particles' Range and others' breath 80 (Detail).
  - Cave plants 60 and drips 25 (Detail).
  - Flora: ground layer at Detail, shrubs and trees at View, radius and LODs, floored at SupportRange + 1.5 tiles;
    bolts 160 (View).
  - Terrain: view radius and LOD distances at View. A change applies after holding 0.3 s by flagging every loaded
    chunk for rebuild (no unload, no holes). The camera far plane (3000 in SampleScene) is pushed out only if the
    terrain reaches past it, which at View 2 it does (3.2 km).
  - Not wired: WeatherSystem reads ViewRadiusChunks for a draw distance (shared file). Left at the baked value; ask if
    you want it scaled.
  - At merge: the TerrainSurface burrow gate becomes `distance < _PunchyBurrowRange` (zache-db asked me to do it).
- **Z-01 DONE in code (a909076):** "Quit game" in the F5 panel, with a Quit/Cancel confirm (stops play mode in the
  editor). Discoverability: F5 isn't listed anywhere on screen. The F1 readout's key list is DebugControls (animal
  lane's file), so it's worth a "F5 graphics / quit" line there.
- NEXT: P1 rows Z-09, Z-10, Z-11, Z-12, Z-13, Z-14, Z-02 (cave side), code first, then shots in a slot.

## 2026-10-30: graphics settings, distance sliders: DESIGN for OK (queue 1), zache-73

**Shape.** `Punchy.Rendering.GraphicsSettings`: static, PlayerPrefs-backed, applied live. Three multipliers on today's
defaults (1.0 = today exactly), plus a preset that sets all three. Systems read `default x multiplier` where they use
the range, never by rewriting serialized fields (so assets and scenes don't change).

| Slider | Range | Scales |
|---|---|---|
| **View distance** | 0.5-2.0 | terrain view radius (TerrainConfig.ViewRadiusChunks, applied in TerrainStreamingSystem; config untouched) and terrain LOD distances with their morph ranges; flora Shrub + Tree layers (Radius, Lod1/Lod2Distance); LandmarkEffectSystem.Range (700); FloraSystem.BoltRange (160). The camera far plane is kept at least the scaled terrain radius. |
| **Detail distance** | 0.5-2.0 | GroundDetail GrassRadius 60 / PebbleRadius 45 / FullDensityRadius 20; flora Ground layer (Radius 70, Lods); CritterSystem.Radius 70 (mine, per your note); LandmarkEffect FlameRange 150 and CaveRange 120; CaveSystem PlantRadius 60 / DripRadius 25; AmbientParticleType.Range; AmbienceSystem.OthersRange 80; burrow marks (below). Animal lane: CreatureRenderSystem FootDetail 40 / TorchFlame 150 / Lantern 60, TraceSystem. |
| **Shadows** | 0.5-2.0 | URP main-light shadow distance (50 m today), with cascades 2 below 0.75, else 4 as now. |

**Presets** (the profiler's sweep may move them; Medium = today until it says otherwise):

| | View | Detail | Shadows |
|---|---|---|---|
| Low | 0.6 | 0.5 | 0.6 (2 cascades) |
| Medium | 1.0 | 1.0 | 1.0 |
| High | 1.3 | 1.5 | 1.4 |
| Ultra | 1.7 | 2.0 | 2.0 |

**Never scaled (simulation, collision, net):** FaunaDirector spawn radius, CreatureRelevancySystem 200 m,
FloraSystem.CollisionRange 24 / SupportRange 40, CaveSystem.LoadRadius 400 (wall meshes), anything seed-derived. One
catch: FloraSystem.UpdateSupports reads the *streamed* flora tiles, so every flora layer's scaled radius is clamped
to at least SupportRange + 1.5 tiles; otherwise Low could stream out a rock the player stands on. Settings only
change what's drawn, so two players with different settings see the same world and stay joinable (no
fingerprint change).

**API** (for the animal lane and anyone else): `GraphicsSettings.View`, `.Detail`, `.Shadows` (floats);
`GraphicsSettings.ViewRange(float baseline)` and `.DetailRange(float baseline)`; `GraphicsSettings.Version` (int,
bumped on every change, for systems that hash their config and rebuild). **Burrow marks:** one source, as you asked.
GraphicsSettings sets the global `_PunchyBurrowRange` = DetailRange(150); the feeder uses DetailRange(150) for its
Range; TerrainSurface's gate reads `distance < _PunchyBurrowRange` instead of the literal (a one-line change to
zache-db's 92a9602 once it lands, or before, if they prefer).

**Applying live.** Cheap ranges apply the next frame. Terrain view or LOD changes mark every loaded chunk for
rebuild (one wave of rebuilds, nearest first), so that slider applies on release, not on drag. Flora layer radius
changes restream tiles beyond the new edge. Shadows apply at once. In the editor the URP asset's shadow values are
restored on exit, so play mode never saves them into the asset.

**UI.** **F4** opens a small panel in the F2 panel's IMGUI style (F1-F3 are taken): a preset row and the three
sliders with their values, plus Reset. It saves to PlayerPrefs on close.

**Flags** (bench runner): `-gfx Low|Medium|High|Ultra` and `-gfxset view=1.3,detail=0.8,shadows=1`, applied after
prefs. **Flags are never saved to prefs** (the -screen lesson): a bench run can't change Zach's settings. Bench
builds have their own prefs identity anyway (PunchyBench).

**Order of work once OK'd:** (1) GraphicsSettings + flags + F4 panel; (2) world ranges, one commit per system; (3)
the API note to zache-db; (4) checks: at Medium every range equals today's (an assert in a debug build, plus an
A/B smoke against bc89606 that should be flat); shots at Low and Ultra from one camera.

## 2026-10-30: Blocked fast path (BENCH item 14), zache-73

- **DONE in code: `world-laptop/blocked-cache` 3d874dc** (off world-features bc89606), not merged, not compiled; the
  profiler's Brain.Blocked A/B build is its check.
- **What made Blocked dear:** for every town plot or counted landmark prop near the animal, it called Place(), which
  builds the full FloraInstance: the plot's FloorHeight and DirtColor, or a prop's SampleHeight and GroundFor, all of
  which sample the ground round the building. Inside() only reads Position.xz, Yaw, Scale and Model.
- **Change:** no cache. `Place(..., bool footprint = false)`; Blocked passes true, and the building/prop/centrepiece
  branches then skip those four (Position.y and Ground come back 0). None of them draws from the cell's Random, so
  xz, Yaw, Scale, Model and Random are exactly as before: same walls, same results, nothing baked, no fingerprint
  change. Walls() and NearPerches() still place in full (they use the heights). Ordinary plants are unaffected.
- Why not a baked cache: FloraBlob is built from the palette with no TerrainSource, so baking every plot's instance
  needs a new bake step and memory. If the A/B shows Blocked still dear (the plot loop, OnBuiltPlot), a per-cell table
  is the next step.
- Brain side unchanged (Walled -> Blocked keeps its signature); zache-db told.

## 2026-10-30: cave-mouth chunk follow-up (v0.11), zache-73

- **DONE in code: `world-laptop/cave-chunk` 256e6a9** (off world-features bc89606), not merged, not compiled; the
  caveapproach A/B build is its check.
- **Where the 39 ms went:** BuildChunkMeshJob.MouthCut ran `Source.MouthCut` five times per LOD 0-1 vertex (the vertex
  and four half-quad neighbours), each a SampleNormal (4 heights) and a CaveDistance, plus a height per neighbour. Its
  `cut >= 100` early-out only fires outside a cave's column footprint. Inside it, MouthCut returns about 0.25 wherever
  the passage lies metres down, so every vertex over the whole footprint paid all five.
- **Change:** `MouthCut(xz, h, out cave)` (a TerrainSource overload, additive) gives the field distance it sampled. The
  job skips the four neighbours when that distance exceeds 2.05 m (MouthCutDepth - 0.1 + MouthBlend + 0.5 spare) plus
  2 x their reach (half a quad, their height difference, twice the sampling depth). Past 1.45 m the smooth min returns
  exactly its rock value, so the skipped neighbours couldn't have changed the min: the cut is unchanged. FieldSlope 2
  is a deliberately loose bound (the field is noise-moved and can overstate by 1.8x). If the A/B shows a cut
  difference at a mouth, raise it.
- **Expectation:** the mouth-cut share of a chunk over a cave drops about 5x. The commit subject says "about five
  times cheaper", which overstates it for the whole chunk (other sampling stays). The A/B will give the real number.
- Earlier entry (worm merge, TroddenGround early-out) was held back by GitHub 500s on push and goes up with this one.

## 2026-10-30: worm marks merged; TroddenGround early-out, zache-73

- **DONE: worm marks merged.** world-features c4d946d -> d81cb06 (merge of world-laptop/worms ad98690, shader only;
  wip/worm-look stays on the animal side, none of its commits came in). Pushed.
- **DONE in code: TroddenGround's hot-path cost.** As first written it did the landmark grid lookup and a full `Hit`
  (distance and frame transform) for every landmark in the cell before finding that most types have no trodden
  props. bc89606 (pushed to world-features) adds two cheap exits: the blob has no trodden spots at all, and the
  instance's type has none, checked before `Hit`. Away from camps with stalls it now costs only the grid lookup,
  which Ground and Shape already do on the same sample. **Not compiled yet** (no slot). Please have the profiler's
  smoke build bc89606 or later rather than c4d946d, so it also serves as the compile check.
- NEXT: idle; the cave-mouth chunk follow-up for v0.11 when you want it.

## 2026-10-30: F-40 and worm marks in the 06:25 slot, zache-73. Unity closed

Bench-runner builds plus shots, no editor. No Unity, Punchy or bee_backend process is running now; Projects\Punchy is
back on b/weather-mapjob. Throwaway branches (tmp/trodden-probe, tmp/worm-test) were local only and are deleted.
- **DONE, merged: F-40 trodden ground.** world-features 6464ce7 -> c4d946d (fast-forward, pushed). The first build of
  the mask (21e7ffd) showed nothing, for two reasons:
  (1) camps are settlements, and their square's surface repainted the ground over the wear. The wear now goes on
  last: `LandmarkSampling.TroddenGround`, called after the settlements in `TerrainSource.SampleSurface` (one added
  call in that shared file).
  (2) At 2.6 m the patch lay under the stall's ~7 m awning and counter. Probe builds showed the data baked (4 trodden
  spots), SampleSurface darker at the stall (albedo 0.069 vs 0.111 at 10 m), and a black 8 m test patch drawing.
  Now: Trodden 7 m on goblin and ogre camp stalls, centred half its reach in front of the counter, x0.5.
  Check (OgreCamp, -355,-4079 from the fire's side, and from above at -362,-4044): a soft, ragged, darker patch in
  front of the counter, the sand's texture showing through. Subtle from eye level: `TroddenDarken` is the dial if
  Zach wants it stronger. GoblinCamp not checked (it sits in fog).
- **PASS, not merged: worm marks.** world-laptop/worms ad98690 with wip/worm-look (c751907, includes 85a1962) merged
  locally, `-place Glacier -spawn SnowWorm:1 -watch SnowWorm -watchfrom 10,20 -timescale 3`, window captured every
  second. The mound and furrow read clearly as a lit ridge and a shaded trough trailing the head. After a breach the
  crater reads as a dark pit with a lit raised rim (the sign fix) and fades out over the next few seconds. No
  exceptions in either run. Per the queue, it merges into world-features after the joint look with the animal lane.
  Say if you want it merged now.
- NEXT: the worm merge on your word; the cave-mouth chunk follow-up for v0.11.

## 2026-10-30: code-only batch (F-40 mask, worm marks, Brain.Sample read), zache-73

None of this is compiled or seen yet: it needs a build (the bench runner's `build` is enough for the code) and shots.
- **F-40 trodden mask: DONE in code, `world-laptop/f40` 21e7ffd, not merged.** The sheet is gone (one revert
  of b336426 + 677bd3b, KeepOutOfSkirt with it); world-features merged in. New `LandmarkProp.Trodden` (m): placed
  props (Count) get worn ground, baked per landmark type at FloraPlacement's own spots (`LandmarkSetBlob.Trodden`,
  like pools) and painted in `LandmarkSampling.Ground`: the ground's colour x0.6, grass gone, pebbles x0.3,
  centred 0.35 x Trodden in front of the prop (towards the middle), ragged edge (snoise at 0.8/m plus the edge noise).
  It's the terrain's vertex colour, so its texture and detail draw over it (the terrain shader itself is unchanged).
  Goblin and ogre camp stalls get 2.6 m. Seed-derived, nothing to sync; landmark placement unchanged, fingerprint
  changes. Side effect: stalls' own dirt band samples the darker ground, so their bases get a little darker too.
  Check: the OgreCamp stall at -355,-4079 (camera -356.6,15.5,-4072 looking at -355,12.3,-4079), before = Bench-d14f556.
- **Worm marks: DONE in code, `world-laptop/worms` ad98690** (world-features + wip/world-worms), not merged. Stronger
  (mound tilt x2, furrow tilt x2.2, darken 0.28 -> 0.4 / 0.12 -> 0.18) and craters fade by (1 - b.y), b.y being the
  feeder's crater age. Bug found: the crater rim's tilt had the wrong sign (sign(r - rim) * -1.2), so the rim shaded
  as a groove: likely why "the crater barely shows". Now it follows the bump's own slope ((r - rim) / width * 2).
  Needs the feeder (wip/worms) or the globals set by hand to see.
- **Brain.Sample 8.7 -> 11.2 us (item 13): the A/B crosses the island change, so it can't attribute this.**
  6cfedaf (twin on e4010f2) lacks 1e53043 (G-18 roads, the v0.10 island), 05f18ec (F-22) and 4c5eafe (F-30);
  7bd0768 has all three. The animals stand in different places on different ground. Code inside the marker is
  byte-identical in both (the marked regions and CaveFloorAt, Crawl, Walled, WalksRoundWalls, SwimDepth). Below it:
  F-22 added one max per lake cell in RiverSampling.Lake plus BayDepthLimit, which only runs after
  NearestLakeAbove succeeds (behind `ShoreLevel > height`, so never far from lakes); F-30 adds one compare to the
  cave SDF, and FloorUnder already skips caves by the baked NearColumn flag. None of that costs 2.5 us a step.
  The likelier source is `Walled` -> `FloraPlacement.Blocked`, which re-runs `Place()` for every town plot or
  counted landmark prop near the animal, every call. Its cost follows what's nearby, and 1e53043 / 7772e01 (Gate
  plots) changed that. Suggestions, no behaviour change: (1) re-run the A/B on the same island (a twin on a commit
  after 1e53043, e.g. 05f18ec^ vs 7bd0768); (2) if Blocked shows up, cache each plot's and placed prop's
  FloraInstance (they're seed-fixed) instead of re-placing per call. That's Flora (shared) and the brain (animal
  lane), so I haven't touched it.
- NEXT: a slot for the F-40 and worm shots; the cave-mouth chunk follow-up stays for v0.11.

## 2026-10-30: F-40 and G-37 in the post-release slot, zache-73. Unity closed

Checked with bench-runner player builds (Bench-<hash>, built with lib.sh `build`, ~5 min each) and framed shots
(`-place <landmark> -benchstops here -cam/-camlook`), not an editor. No Unity is running now; Projects\Punchy is back on
b/weather-mapjob.
- **DONE: G-37 decoration rocks.** world-features 2310533 -> 6464ce7 (fast-forward, pushed): MagneticDrift, a
  FloatingRock species (MagneticMonolith copied: 6-15 m, 4 variants, seed 337) in ElectricWastelands' flora at 0.03
  per 100 m2. At 0.01 a whole wastelands view held one, lost among the small Debris clusters (MagneticDebris is form
  Debris, not FloatingRock). At 0.03 the view at -1096,-3946 shows a big cluster with shards and arcs, a slab and a
  shard over their scorched mounds. Same view, same build otherwise: avg 59.8 / 1% low 57.8 before, 59.8 / 57.9
  after. Only the biome's Flora list changed: landmark list unchanged (101, E128CB1D).
- **NOT MERGED: F-40 stall footprint.** world-laptop/f40 677bd3b (pushed). With the skirt fix alone (b336426) the
  sheet still didn't show: same OgreCamp stall (-355,-4079, camera from the fire's side), before and after looked
  identical. Second cause: the sheet sloped from -0.1 m at its rim to +0.03 m in the middle, so only the middle
  quarter (under the counter) ever cleared the ground. 677bd3b raises it (rim +0.01, middle +0.08). Now it shows,
  but as a flat, untextured brown slab with straight edges in front of the counter, half buried by the dune
  relief. It doesn't read as trodden ground. Proposal for v0.11: darken the ground in the terrain shader instead (a
  trodden mask round stall props, like roads' ground), or a soft-edged decal. Your call or Zach's; the branch stays
  unmerged.
- NEXT: queue item 4 (worm ground ripples, wip/world-worms) once you hand out the next slot, or the F-40 redesign
  if you pick one. The cave-mouth chunk follow-up stays NEXT for v0.11.

## 2026-10-30: G-29 arch NaN fix brought in, zache-73

- **DONE:** cherry-picked rc/v0.10.0 519fb5b (arch bow sin clamped at 0) onto world-features as 2310533, pushed; file
  still CRLF. Scanned for other Pow(sin/cos, fractional): only FloraGenerator :622 (sin at a segment's midpoint,
  always inside (0, pi)), safe; the rest use integer powers or non-negative bases.

## 2026-10-30: caveapproach hitch merged, zache-73

- **DONE: caveapproach hitch.** world-features fast-forwarded 1cbdd9f -> d14f556 (world-laptop/cave-hitch) and
  pushed, after the profiler's A/B passed: caveapproach worst frame 108-139 -> 24-27 ms, dash 1% low 21.5 -> 41.2,
  glide flat, shots clean. In rc/v0.10.0 (c7410cc).
- F-40 stays on world-laptop/f40 (b336426) until after the release: editor slot, stall shots, then merge.

## 2026-10-30: caveapproach hitch (queue 1) and F-40 (queue 2), zache-73

- **DONE, waiting on the bench: caveapproach hitch.** `world-laptop/cave-hitch` d14f556 (off world-features 1cbdd9f,
  not merged). TerrainStreamingSystem MaxBuildFrames 4 -> 60, kept only as a backstop. Cause: a LOD 0-1 terrain
  chunk at a cave mouth samples MouthCut five times per vertex plus the skirt bisection, ~39 ms on one worker
  (BuildChunkMeshJob takes one chunk per work item). The forced Complete at frame 4 can't help with a work item
  that's already running, so the main thread waited it out (item 10's 37.8 ms wait). Dispose paths reviewed:
  OnDestroy and both settings-change paths (re-bake, new source) go through CancelBuild, which Completes before it
  disposes, and world replacement on host/join destroys the system the same way. Side effects: while a slow batch
  runs, OnUpdate returns early (no unloading, no new batch, no HideFarChunks switch) for up to the batch's own
  length, and Meshing stays true longer (WeatherField caps its wait at 0.3 s). Not checked in an editor: the bench
  build is the compile check; caveapproach, glide and dash with shots are the play and fast-travel check.
- **DONE, not for v0.10: F-40 stall footprint.** `world-laptop/f40` b336426 (off 1cbdd9f, not merged). Why the
  sheet never showed: Stall is Founded, and FloraMeshBuilder.Skirt drops every vertex at y <= 0.001 by 1.6 m,
  which took the sheet's rim (y -0.1, or 0.0 "raised 10 cm") into the ground. Fix: KeepOutOfSkirt(first) leaves
  listed vertex ranges unskirted, and the sheet (re-landed from 7de4b48) uses it. Landmark list unchanged. Needs
  an editor slot after the release: before/after stall shots, then merge.
- **NEXT:** (post-v0.10) cheapen or split the cave-mouth chunk (rows as work items, or MouthCut only where the
  centre sample is near), so it doesn't hold the stream. G-37 decoration rocks: still to make. (`MagneticDebris`
  in ElectricWastelands' Flora list at 0.12 is Form 50, not FloatingRock 54, so it isn't them.) Plan: a new species
  copied from MagneticMonolith, Height 6-15, Variants 4, listed at a low density; needs an editor slot.
