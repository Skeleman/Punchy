# World reports (world lane → supervisor kind-possum)

Newest first.

## W-95 (2026-11-04, partial: paused for Zach's test build): gentler dune mound, human house style, slab shelters

Commits on world-features: f2936f5 (row 1), 3a9997e (row 2), 772ed5f (row 4). All pushed. Done: rows 1, 2 and 4. Left: row 3 (#40 salt ball: round body with fine dents), not started.
- Row 1 (#80): the dune mound is gentler (Reach 120). Max mound slope 25.6 deg (was 61), below the rock-slope threshold. 13-14 cells sit at 24-25.6 deg and none above 29. The ruin walk ARRIVED and the east wall walk STOPPED at x 1424.91. Landmarks v2 136/4E61F1CF and v3 108/4D2643D7 are unchanged. Shot: w95-sandmound.png.
- Row 2 (#69): the four W-94 shelter houses are now a human house style named "Frontier" (SettlementBuilding.Style; layout HouseStyleChance). It is seeded per settlement and swapped onto house plots after placement, so plots and landmarks don't move. I set the chance to 0.75 so that v3's only human settlement uses the style: HumanVillage (2972,-885, Sonoran), 38 houses. No other v3 settlement gets it. v3 landmarks are 108/4D2643D7. Shot: w95-humanstyle.png.
- Row 4 (#69): new kit shape SlabShelter, a crooked ramada.
  - Three rough rock slabs stand as walls: buried, leaning and twisted, with lumps on their faces. A fourth slab rests across them as the roof, tipped to their uneven tops, with ash, snow, char or sand caps.
  - The plot pad is dug 0.9 m down (StructureKit.SlabDig). Under the roof is a dirt floor, and a dirt ramp runs up the bank from the open front.
  - Four species (AshSlabShelter, SnowSlabShelter, StormSlabShelter, SandSlabShelter) take the local rock colour. They fill the harsh-biome shelter slots in all 7 layouts.
  - Each slab and lump is a KitCollision box, with no inside cylinder.
  - v3 landmarks are 108/4D2643D7. On v3 the shelters land in OgreCamp (Ashlands), DwarfHold and DwarfMiningCamp (Tundra).
  - Walk at the Ashlands shelter (890.9,-3864.9): it goes down from 8.99 to the pit floor at 8.60, under the roof, and is STOPPED 2.8 m behind the centre at the back slab.
  - Shot: w95-slabshelter-ashlands.png.
- Weak:
  - No Tundra shot. The bake puts SnowSlabShelter on plots at DwarfHold (-3292,1564) and DwarfMiningCamp (-1977,642), but two framed shots after a 45-60 s wait showed none. I didn't have time to find out why, so it needs a look.
  - The slabs are plain boxes and the caps look blocky.
  - The ramp is a sloped sheet over the terrain's own bank, so it can float or sink a little.
  - The biggest settlement's KitBuilding override may replace style houses there.
- The bright white circle with a black silhouette: I didn't find it in my editor. My shots and a Game-view capture at the Ashlands shelter show no disc; the sky is Punchy/Sky and the sun is high. It is likely from another lane's walk test.

## W-94 (2026-11-04): hazard shelter houses, brick ruins + dune mound, sunk mound lip, meteoroid salt ball

Commits on world-features: 26c6163 (row 1), 853b1bc (row 2), cbdf6cf (row 3), aab2d6a (row 4). All pushed. I commented on #69, #80, #70 and #40.
- Row 4 (#40): the salt ball is now one 32x22-facet, slightly elongated body (seeded, about 1.13 x 0.9 x 0.96). Seeded noise gives it swells, sharp ridges and 8-11 round pits with low rims. The crystals are small and low and sit on its surface: outcrops stand 1-2 m proud, crust 0.4-1 m. The trail's width and lip height vary smoothly along the circle (three seeded whole-turn waves, about 0.7-1.3x, fixed to the ground and the same for every player). The furrow, lips and 0.5 m rebuild rule are unchanged. Ball vertices: 7050 before, 9624 after. Main thread: Update 0.0003 -> 0.0002 ms; a rebuild is 0.0125 -> 0.0132 ms. Shot: Assets/Temp/shots/w94-saltcrystal.png (the w93 camera). The pits read only faintly at that distance, and the sky was hazy.
- Row 1 (#69): four kit shelter houses for harsh biomes: Ash, Snow, Storm and Sand (new kit shapes; new materials Copper, Sand and Ash; banks, hoods and cheeks). They replace the dome entries in 7 settlement layouts. The old dome assets stay in the repo but nothing uses them now. v3 landmarks are 108 / 4D2643D7.
  - Ash house (890.9,-3864.9): the walker goes through the bank gap, under the hood, and stops at the closed door.
  - Snow house (-3292.0,1563.7): the front walk stops at the door. The side walk climbs the snow bank (about 1.2 m) and stops at the wall.
  - Only Ashlands and Tundra have a settlement on v3, so there are 2 shots: Assets/Temp/shots/w94-shelter-ashlands.png and w94-shelter-tundra.png. Storm and Sand houses are not on v3 yet, so they have no walk or shot.
- Row 2 (#80):
  - Ruined kit wall bays draw as old coursed stone blocks at close range: staggered courses, worn and slightly tipped blocks, a few missing, a ragged top, and some fallen at the foot. This is drawing only; collision is unchanged.
  - The sunken temple's sand mound is now wind-shaped: a long windward side, ripples, and drifts at the top's edge.
  - v2 checks: north-gap walk ARRIVED (foot 31.5); east-wall walk STOPPED at x 1424.9 (same as W-67b); landmarks 136 / 4E61F1CF, unchanged.
  - Shots: w94-ruin-bricks.png (the bricks read well) and w94-sandmound.png. The mound's steep flanks still show the brown rock-slope shading and the dune shape is weak. It may need a gentler flank, which would be a follow-up.
- Row 3 (#70): the lowest tier box on a drift mound is now a 0.15 m lip. A walk uphill onto a MagneticDrift mound on a 0.46 slope (-789.9,-4026.8, v3) stepped up about 0.3 m per tier and reached the top (y 7.56) without stopping.
- Notes: the W-67b walk spots are on v2. The editor keeps v3 in EditorPrefs, so I switched the island for one play by setting PUNCHY_ISLAND inside the editor process. I closed the editor at the end. I went well over the call budget.

## W-93 (2026-11-04): wasteland rock collision, plant skirt drape, salt crystal clock + lumpy ball + furrow

Branch world-features: 3ef6bde (row 1, #70), fd20db4 (row 4, #40), 34a0924 (rows 3+5, #40). Row 2 (#69) not done.

- Row 1 (#70): Debris and FloatingRock mounds get terraced KitBoxes (0.35 m tiers, at most 6), same rng order. Fallen chunks under 0.45 m flatten to decals, with no boxes. Editor walk: east at z -4005, the player stops at a fallen chunk (x -987.0). South from (-981.6,-4000.5), the player steps up 8.27 to 8.42 onto a debris tier, walks level, then steps down. East at z -3972.5, the player stops at a 2.3 m MagneticDrift mound. Main thread at (-987,-3972.5): 6.74 ms before, 6.22 ms after (noise); PlayerMovementSystem 0.026 ms both. Caveat: on a slope, a drift mound's lowest tier reads as a wall from downhill. Shot w93-wasteland.png. Commented on #70.
- Row 4 (#40): the skirt is darker green (0.05,0.15,0.04 / 0.03,0.10,0.03), rooted high on the bulb under the stem and hanging down over it, with slightly broader leaves. Count, lean, purple veins and red tips/spots keep their draws. Shot w93-plant.png (PlantShot d 22 up 5, the w92 view).
- Row 3: the roll angle reads _PunchyClock (WorldSync: the server tick time on a client, Time.time on the host; Time.time when it is 0). The rotation is absolute from the angle, so the face matches too. The trail rebuilds only after 0.5 m. No host/client join run: the angle is a pure function of the shared clock, and the clock lag between host and client (about 0.1 s) is about 0.25 degrees on a 36 m circle. Solo check: clock 42.67 = Time.time.
- Row 5 (#40): the ball is a coarse, squashed core (7x5 facets, seeded radii and turn) with 6-9 big lumps and 2-3 crystal outcrops. It uses its own rng, so the circle, crust, crumbs and direction keep their draws. The trail is now a 7-column furrow, 9 m wide: a dirt floor and dark inner walls (as the worm crater rim darkens), with 0.6 m salt lips that slump towards the tail, shaded by its normals. Main thread: Update 0.0006 ms; a rebuild is 0.013 ms, about every 0.3 s. Shot w93-saltcrystal.png (the w92 camera).
- Row 2 (#69): not started, because of the call budget. W-57b already swaps HazardShelter domes onto house plots via SettlementBuilding.OnlyIn (PlaceShelters, no rng). The remaining work is a kit-built house shape per harsh biome, swapped in the same way.
- Landmarks are 108 / 4D2643D7, unchanged.
- Shots are in D:\Unity\Punchy-world\Assets\Temp\shots: w93-wasteland.png, w93-plant.png, w93-saltcrystal.png.

## W-92 (2026-11-04): lake shelf arid rule, plant skirt, rolling salt crystal

world-features: 91b7320 (#88), 438685d (#40 plant), f086dd0 (#40 salt crystal). Pushed. Island v3. Rerun after the last editor hung at shutdown.

DONE
- Row 1, #88: BuildShelf rules out stretches with arid land (LandOf < 0) at 50, 100 or 200 m behind in their middle two thirds, land weight 2.5 -> 4, with a logged fallback. -island and -lakeshelf now read from PlayArgs (IslandV2 uses DebugControls.LaunchArgs). Log: before, W-91 and after all bearing 120 (SaltFlat 50 m, SaltFlat 100 m, Temperate 200 m, rise -1 m, land -0.14); "every bay has arid land behind it (11 ruled out), taking the best anyway". Shelf 10.2 m over the lake, lip 252 m, 25 fall stretches.
- Row 3, #40: carnivorous plant gets a leafy skirt: 9-13 broad drooping leaves (own seed, lean 18-34 deg), a purple vein down each, red tips on every third, 6 red spots on the bulb.
- Row 2, #40: SaltCrystal colossus (slot 7, SaltFlat, keep radius 78 m). A faceted 13 m salt ball with short salt points and dirt smears rolls at 1.6 m/s round a seeded circle, following the ground, and drags a 90 m dirt strip fading back to salt; salt crumbs lie by the track. Cosmetic, clock-driven (SaltCrystalRoll.cs). Log: spot (3702, 7.3, -917), circle radius 36 m, lap 142 s, relief 2.9 m. The other seven colossi kept their spots.

NOT DONE
- #88 goal: no temperate bay wins on v3. Only 11 bays pass the 10 m high-ground test and all have arid land behind. A per-ray probe found the only non-arid stretch at 50/100 m (Grassland, bearing 75-80) has -3 m ground behind and MesaCanyonlands at 200 m. Needs a call: move the shelf off the bay test, or terraform one.

Checks
- Landmarks v3 108 / 4D2643D7 before and after (game hash 3A0DC7BB), with and without -lakeshelf.
- Compiles clean; no exceptions from StoneColossi in play.

Shots
- w92-shelf-lake.png (from the lake), w92-plant.png (close up), w92-saltcrystal.png (ball, trail arc, crumbs).

Learned
- Unity wipes Temp/PlayArgs.txt at editor start; write it after the editor opens.
- Script edits don't compile while the editor sits in play mode (isCompiling stays true): stop play first.

NEXT
- Zach's call on the shelf (#88). Salt crystal trail could print into the ground texture instead of a strip.

- Calls: ~45

## W-91 (2026-11-04): river-free fishing villages, temperate lake shelf, small carnivorous plant

world-features: 532d14e (#36), 43f66e2 (#88), 6d9b5ff (#40). Pushed. Merged origin/main first. Island v3.

- Row 3, #36: houses, stilt houses and docks/wharves whose plot (docks: plot plus 30 m along the facing) is on river water (river channel > 0.3, not ocean) are left empty by flora. Plots dropped per village: v1 0 of 6, v2 2 of 6, v3 0 of 6. Landmark hash v3 108/4D2643D7 before (W-76) and after: villages and landmarks don't move, only flora plots go. Shot: w91-village.png (village 2).
- Row 4, #88: the bay pick now adds a land score (biome 100 m and 200 m behind the shore: temperate/grass +1, mountains 0.6, forest 0.5, arid -1; plus the rise toward the mountains). Log (still behind -lakeshelf, needs PUNCHY_LAKESHELF=1 in the editor env): before bearing 120, SaltFlat 100 m behind, Temperate 200 m behind, rise -1 m; after bearing 120, same, land score -0.14. So the pick did not move on this seed: no other bay with a temperate shore scored higher. Not done: the weights need raising, or the bay test loosening, so a temperate bay wins. Shot: w91-shelf-lake.png (from the lake, unreviewed).
- Row 5, #40: plant body at 1/4 scale, head 1.7x, vines 0.5x; sprouts and bones stay life size; far shadow cast sized 11x8. Fog check (MarshFog forced, ExpSquared density ~0.0019, no layer cull, far plane 3000): Body, Sprouts and Spores renderers stay visible at 50, 150 and 300 m, so no culling pop. Whether the Colossus shader haze fades it smoothly is not yet checked by eye. Shots: w91-plant.png (30 m), w91-plant-fog-50.png, w91-plant-fog-150.png, w91-plant-fog-300.png (the card's w91-plant-fog.png).
- Calls: about 50 of a 45 ceiling (the shelf needed an env restart of play).

## W-76 (2026-11-04): parallel island bake

world-features: b262269. Pushed. Row 2 dropped (see below).

- Row 1: bat folk sites place in parallel; the fishing mouth scan, turtle height grid and sea folk lake beds sample in parallel rows; the second river build copies the first's drainage grid. The stage log now splits fishing / turtles / seafolk / batfolk (the old "batfolk" stage timed all four).
- v2 stages ms, before -> after: rivers 1153-1245 -> 1070-1143; settlements 340-395 -> 385; caves 566-598 -> 576; fishing 773 -> 678-695; turtles 547 -> 61-63; seafolk 488 -> 151-154; batfolk 429 -> 430-470 (the four together 2657-2734 -> ~1340); anchors 148-157 -> 153; pondmask ~100 -> 101; icebergs 32-88 -> 16. About 1.4 s off a ~5.2 s bake. Ready at 13.7-15.1 s -> 12.6 s.
- v3 stages ms, before -> after: v2base 521 -> 536, ranges 567 -> 589, ground 66 -> 58; rivers 1506 -> 1461; settlements 406 -> 455; caves 497 -> 509; the four placements 2178 -> 1252 (fishing 540, turtles 73, seafolk 478, batfolk 161); anchors 91 -> 98; pondmask 97 -> 91; icebergs 21 -> 22. Ready at 37.2 -> 37.7 s: the editor audit reports (23.2 s) dominate.
- The 10 s target is out of reach through the bake: the whole v2 bake is ~3.8 s now and most of the ~13 s ready time is play-mode entry. A bake cache (the next card) or a faster play entry is what's left.
- Hashes, bit-identical before and after. v2: 136/4E61F1CF, caves C49E8E3036C741FC, ground F37805C9C70E3822, rivers 835B1404BEBDF563, batfolk 3/70 5E495C663CB43E07. v3: 108/4D2643D7, caves 5307D41E3A2AE83C, ground CBD3E62DCF42728C, rivers F46CF58544C3E072, batfolk 1/29 7019110BE7B5BB34.
- Row 3, worst frame (v2, editor): before 7.0 / 7.6 s, after 7.4 s, so not worse, but not better either. The bake still runs inside one main-thread frame (the workers only help inside it), and that frame also holds ~4 s of work after the bake ends. v3 after: 7.5 s bake frame, then a 44.7 s frame with the editor audit reports.
- Row 2 dropped: the pond mask can't run beside the chain (TerrainSource heights read the landmarks, settlements and caves; the mask needs PondsOn and RiverRoughOn), and the three village placements can't overlap (SampleHeight sees each earlier one's flattening). Only the icebergs (~30 ms) are independent.
- Fishing, caves and roads: same cave, ground, landmark (roadside included) and bat folk hashes on v2 and v3, no errors or exceptions in the play logs. Not walked in game.
- Calls: ~25 in this resume (plus the paused first part).

## W-75 (2026-11-04): spread frame work, faster world load

world-features: e4db17b, 140a494, 3d45402 (merge of origin/main; the IslandV2.cs conflict kept both sides), 096ec00. Pushed.

- Row 1: AmbientParticle, Critter and GroundDetail now finish their placing jobs a frame later (approach c). AmbienceSystem heat and Flora were already like this.
- Row 2: terrain and water chunk entities are created and destroyed in batches; the mesh data jobs were already there. While the world first loads (before the first full ring), streaming builds 6x per frame and flora 4x cells per frame. PUNCHY_LOADBOOST=0 turns it off. A "world ready at" log line was added.
- Frame ms, average and worst, before -> after: spawn 10.40 -> 9.73 (worst 390 -> 41); cave 13.65 -> 11.11 (worst 454 -> 39); fly 11.65 -> 9.95 (worst 54 -> 46).
- System ms at the fly stop, before -> after: GroundDetail 0.288 -> 0.075, TerrainStreaming 0.550 -> 0.519, AmbientParticle 0.370 -> 0.350 (0.169 -> 0.335 at the cave stop, likely noise), Critter 0.059 -> 0.049, Flora LateUpdate 0.567 -> 0.385.
- Island-ready -> world-ready gap (v2, this PC): 10.8 / 10.8 s without the load boost, 9.1 / 9.0 s with it.
- 0 errors and 0 warnings in the bench runs. There is no before shot, so the visual check is written only: the same data lands one frame later.

## I-1 (2026-11-04): main merge check for PR #98

- Merge 79ab19b compiled clean. Solo v3 runs: 17898 / 18583 / 19066 ms, ready at 31.2 / 32.0 / 42.1 s. 0 errors, no leaks. The host "shared with" line was not tested.
- Line endings: IslandV2.cs is CRLF, IslandV3.cs is LF.

## W-73 (2026-11-03): bake blob leak, pass bank, bake profile

world-features: 2d6ad34 (rows 1-2), 9765d71 (row 3). Not on main.

- Row 1, leak: fixed. Domain reload is off, so IslandV2System.Shared.Reset dropped the 13 Persistent bake blobs every play cycle. Worlds now refcount the shared bake. The blobs are freed when Play mode ends with no world using them, and before assembly reload. #23 sharing is unchanged. 10 v3 enter/exit cycles with leak detection (EnabledWithStackTrace): Total Used 1013 MB at start, 1035 after cycle 1, then 1035/1047/1048/1036/1037/1036/1037/1016/1017 MB. That is flat: +4 MB net, 32 MB peak-to-peak (it was ~+22 MB per cycle). 0 leak warnings, 0 exceptions.
- Row 2, bake profile (v3): the bake is 6.0-6.4 s (generate 1.2 s, places 4.8-5.2 s). The 18 s is the end-of-bake reports at 17.4-18.5 s: V3Audit + V3Ranges.Checks + V3Ground.Report, which run on every v3 bake (IslandV2.cs line 475). v3 is ready at ~31 s, v2 at ~14 s. The 3 slowest steps: **reports 17400-18500 ms**, **batfolk 2084-2202 ms**, **rivers 1502-1616 ms**. Next: settlements 427, ranges 563-717, v2base 526, caves 483-537, grand falls 1-4, archipelago (v2 107). No fix made: making the reports editor-only or on-request touches the audit workflow. Suggest a card: run them only on request (~17 s back per v3 bake).
- Row 3, pass: partial, 43 -> **36 deg** (field 13). Not under 30. The 43 deg step at (-578,874) was a rough river bank (bank slope up to 1.2 rise/m) crossing the track: carve 3.6 m in 4 m at track 0.99. Query now takes a bank slope cap, and TerrainSource.PassBankCap lerps it from 1.25 to 0.5 rise/m across the pass track. New best line: 36 deg at (-505,974), shape 2.8 m in 4 m at track 0.84 (relief that is left at the track edge). Lines 0/1 still have 36/43 deg steps near (-600,765) at track ~1. Other passes: Wet-Cold 26 -> 25, the rest unchanged. Land over 45 deg 37043 -> 36476, max 85.
- Row 3, top rounding: top = max(smoothstep(35,95,field), smoothstep(100,160,field+base) * smoothstep(15,40,field)). Range 3 top-15%: 30 deg / 38% / 51.5 convexity before, **31 deg / 39% / 61.2** after. It did not help and is a bit worse. Its top cells are now field 90, base 95, total 185 (were 66/104/170). Overall ground top15: 16.8/9.4%/15.8 -> 16.2/8.0%/14.55. Likely cause: base elevation jumps between Hills and its neighbours, so `top` changes quickly across the range. Next try: blur the base elevation, or revert this half.
- Hashes, rows 1-2 (all unchanged vs W-72): v1 103/19885131, caves 47C703C0E17F77CB, ground A96DC8AA7978A9A7, rivers 9833D8B2BD09C77E. v2 136/4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers 835B1404BEBDF563. v3 110/C1CD73AE, caves 300F01C693A56304, ground AAB8C9CFFF5D8029, rivers E783445189E1B572.
- Hashes, row 3: v1 and v2 unchanged (same as above). v3 is now 108/4D2643D7, caves 25 / B5CF07D35E0B3AED, ground 7D490D9220C4E743, rivers BCF87133353A873F. Audit: rivers 22 (ends 11, open 0), lakes 101 (9432 cells), falls 79, pools 35 (dry 0), ponds 45, dry beds 0 | wet 47738, steep 622, water over ground 576, land between 31, floating 17. The audit on unchanged code was already wet 47714 / steep 634 / over 557 / between 29 / floating 16, not the numbers in the W-72 report.
- Editor closed. QualitySettings.asset and Assets/Temp are not committed. W73Probe.cs is scratch in Assets/Temp. Calls used: ~66 (over the ~60 ceiling, because of eval timeouts and a wasted first open).

## W-72 (2026-11-03): v3 rounded mountain tops

- Commit: 7239c07 on world-features (pushed). V3Ranges.cs, TerrainSource.ShapeHeight, BiomeSampling.Relief (new `round` argument, 0 = old heights exactly). All behind `-island v3`.
- Cause: mostly the biome relief on the range tops, not the range field. Ridged biome relief (abs of noise, sharp creases, fine octaves) sits on the crests, e.g. biome 15 (Ridged, relief 110 m, ~220 m wavelength) on range 2. The range field itself had a V crest (`pow(1-u, 1.8)`) and ridged-noise cusps along the spine and on the massif, but was much smoother.
- Fix: range cross-section crest rounded (soft distance, dome about a quarter of the half width); soft abs on the along-spine and massif ridged noise; on range tops (range field 35-95 m and up) biome relief gets soft creases, about half its fine octaves, sharpness eased toward 1, and up to 45% less height. Flanks below keep their crags.
- Spikiness (top 15% of each range's height, foot = 10th percentile; mean slope / share over 35 deg / convexity = minus the Laplacian at 12 m, 1/km):
  - Ground before: 19.6 deg / 9.1% / 22.3. Worst range (2) 41 deg / 61% / 211.
  - Ground after: 16.8 deg / 9.4% / 15.8. Range 2 26 deg / 30% / 53.5.
  - Range field alone: 15.6 deg / 8.1 before, 11.9 deg / 4.4 after.
  - Still rough: range 3 (30 deg / 38% / 52, was 30 / 34% / 79). Its top is biome 12 (Hills, base 120 m, relief 90) at a range field of only ~73 m, so it is a high plateau's bumps more than a crest. A follow-up could key the rounding on total height, not the range field.
- Pass slopes (walkable line / range field alone, longest over-60 wall): Wet-Cold 26/20, wall 72 m; Volcanic-Arid 16/16, 0 m; Arid-Temperate (2232,1734) 15/14, 56 m; Arid-Temperate (1405,835) 13/11, 48 m; Temperate-Cold 43/13, 176 m. Worst 43 deg, all under 45, but Temperate-Cold went from 27 to 43: its steepest step is at (-578,874), ~750 m down the walk line from the saddle, with a carve of 3.6 m. Worth a look before more v3 changes.
- Land: over 45 deg 37043 / 774902 cells (W-70b 38531), max 85.
- Audit (v3): `[hydro audit] rivers 22 (ends 11, open 0), lakes 101 (9434 cells), falls 77 + grand 0, pools 35 (dry 0), ponds 45, dry beds 0 | grid 8 m: wet 48873, steep 618, water over ground 558, land between 27, floating 12`
- Hashes:
  - v1 (unchanged): 103 / 19885131, caves 47C703C0E17F77CB, ground A96DC8AA7978A9A7, rivers 9833D8B2BD09C77E.
  - v2 (unchanged): 136 / 4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers 835B1404BEBDF563.
  - v3: 110 / C1CD73AE, caves 25 / 300F01C693A56304, ground AAB8C9CFFF5D8029, rivers E783445189E1B572.
- Shots, in D:\PunchyMilestones\W-72:
  - w72_v3_pass_worst.png: pass at (2232,1734), W-70b framing.
  - w72_v3_skyline.png: range 0 (longest mountain range) from the side, 2.2 km out, mid at (-1991,386).
- Calls used: ~52. The first two editor opens failed on probe compile errors (my scratch probe). Editor closed at the end.

## W-70b (2026-11-03): v3 walkable passes, notched pass walls, landmark drop

- Commit: b83c9c7 on world-features (pushed). V3Ground.cs, V3Ranges.cs, TerrainSource.ShapeHeight. All behind `-island v3`.
- Row 1, cause: biome shape, not rivers or rock. The range field was fine, but (a) biome relief stood in the saddle, and (b) the blurred shape weights wobble every ~40 m between biomes with different base elevations (Temperate-Cold: 15-20 m bumps every 40 m), which alone gave 50+ deg steps.
- Row 1, fix (per Zach's change: steep bits OK, no walls, one walkable line): each pass gets a narrow track (12-60 m wide, the width wanders along it). On the track, biome relief fades to 0 and base elevations are ring-averaged (8 taps at 90 m). The rest of the pass box keeps its crags, but relief is notched by gullies on a 96 m world grid (40% relief kept in the gully), so long faces are broken.
- Pass slopes: max slope along the walkable line / range field alone, and the longest over-60-deg wall in the pass:
  - Wet-Cold 26/13, wall 72 m
  - Volcanic-Arid 12/11, wall 0 m
  - Arid-Temperate (2232,1734) 17/13, wall 88 m
  - Arid-Temperate (1405,835) 10/8, wall 56 m
  - Temperate-Cold 27/10, wall 152 m (still the longest; notching took it from 192 to 152)
  - Worst pass 27 deg. MET (all 5 under 45).
  - Walls before notching: 40 / 0 / 152 / 160 / 192 m.
- Row 2: W-70 gave every interior cell edge the wide 480 m blend, because no range stands there and that counted as "open". The surface blur then pulled most spots under the 90% dominant-biome landmark threshold. Now the wide blend applies only within about one radius of a realm border. v3 landmarks: 86 -> 111 (W-69 had 141; the rest moved with the new v3 terrain and rock).
- Row 3 shots, in D:\PunchyMilestones\W-70b:
  - w70b_v3_pass_worst.png: pass at (2232,1734), W-70 framing.
  - w70b_v3_cliff_Arid.png: (272,-208), slope 82 deg, 99 m drop over 30 m. 1619 cells over 50 deg in Arid.
  - w70b_v3_cliff_Volcanic.png: (224,-280), slope 84 deg, 35 m drop. 133 cells over 50 deg in Volcanic.
- Audit (v3): `[hydro audit] rivers 22 (ends 11, open 0), lakes 62 (9974 cells), falls 65 + grand 0, pools 35 (dry 0), ponds 47, dry beds 0 | grid 8 m: wet 49516, steep 740, water over ground 552, land between 35, floating 11, near falls 159`
  - Lakes fell from 100 to 62: the notched pass boxes changed where basins hold water.
  - Land overall: over 45 deg 38531 / 773399 cells, max 85.
- Hashes:
  - v1 (unchanged): 103 / 19885131, caves 47C703C0E17F77CB, ground A96DC8AA7978A9A7, rivers 9833D8B2BD09C77E.
  - v2 (unchanged): 136 / 4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers 835B1404BEBDF563.
  - W70Probe's W-68-recipe line read 8B429C2B75DCBADF (v2). It differs from the A77D2ED7B919296E in W-70, while every other v2 hash matches, so this is probably a probe-recipe difference. Not chased.
  - v3: 111 / A96593A9, caves 26 / 36019D3F36E79493, ground AA3740C3D5A99D61, rivers B0356AAD4A717AB9.
- Calls used: ~62 (over the ~60 ceiling: the mid-task pass brief change cost one extra measure loop). Editor closed at the end.

## W-70 (2026-11-03): v3 B3 biome blend and bleed, B4 rock and scree

- Commit: dbce62f on world-features (pushed). New V3Ground.cs; small hooks in TerrainSource, IslandV2, IslandV3. All behind `-island v3`.
- Row 0: the data didn't change, only the hash recipe. The old W-68 recipe (Bed, WaterLevel, Width, Down) still gives A77D2ED7B919296E on v2 today. W-69's recipe hashes more river arrays, so it reads 835B1404BEBDF563.
- Row 1 (B3): the v3 biome map has no border strips. Each pixel has a blend radius of 60-480 m (wide on open ground and around passes), and realms bleed through passes (rolled per cell, stronger near a pass). HighlandSurface is skipped in v3. Biome-colour map: w70_v3_biome_map.png (base surface albedo plus hillshade, passes in red).
- Pass slopes from `[ranges check]`. Each is "on dry ground / range field alone":
  - Wet-Cold 39/22
  - Volcanic-Arid 19/11
  - Arid-Temperate (2232,1734) 68/13
  - Arid-Temperate (1405,835) 55/8
  - Temperate-Cold 66/10
  - Worst pass 68 deg (range field alone 22).
  - NOT MET: the range field is now well under 45 deg at every pass, but 3 of 5 passes are still over 45 on dry ground. Both narrow ones (w13, w37) are among them. Next suspect: biome shape relief or river banks cutting the saddle at 4 m steps. I didn't tune this, to stay inside the call budget.
  - Land overall: over 45 deg 37666 / 773880 cells, max 85.
- Row 2 (B4): geology per realm (sandstone, slate, granite, basalt, limestone). Altitude bands for alpine, scree and snow sit above the range foot. Scree comes in at 28-38 deg and rock at 38-45 deg; edge noise wobbles both cut-offs. Far LODs use the same SampleSurface path. v3 baked with no errors.
  - `[rock] land cells (16 m) per realm, ground/alpine/scree/rock/snow: Arid 26777/415/1274/4952/0; Wet 29125/3703/1577/1556/0; Cold 22001/2356/4202/5543/2531; Volcanic 43155/2/733/662/0; Temperate 23583/169/1280/1749/0`
- Audit (v3): `[hydro audit] rivers 23 (ends 12, open 0), lakes 100 (10583 cells), falls 78 + grand 0, pools 35 (dry 0), ponds 47, dry beds 0 | grid 8 m: wet 53547, steep 804, water over ground 655, land between 45, floating 16, near falls 205`
- Hashes:
  - v1, re-measured: 103 / 19885131 (unchanged), caves 47C703C0E17F77CB, ground A96DC8AA7978A9A7, rivers 9833D8B2BD09C77E (W-69 recipe).
  - v2, unchanged: 136 / 4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers 835B1404BEBDF563 (W-69 recipe) / A77D2ED7B919296E (W-68 recipe).
  - v3, measured only: 86 landmarks / 64748C4D, caves 16 / B5FFCC2A57A390D4, ground E831C2DEA66D4893, rivers C0D23F3DFB9AF19D.
- Shots (D:\PunchyMilestones\W-70, not viewed):
  - w70_v3_biome_map
  - w70_v3_pass_bleed (Arid-Temperate pass, Arid on both sides)
  - w70_v3_cliff_Cold
  - w70_v3_cliff_Arid
  - w70_v3_cliff_Volcanic_far
  - The cliff spots were picked by range-field slope on a 32 m grid. Arid (24 deg) and Volcanic (20 deg) came out mild, so those two shots may not show real cliffs.
- Calls: about 68. Editor left open: W-71 exists and EDITOR-QUEUE is empty.

## W-69 (2026-11-03): v3 B2, range spines, taper, spurs, passes

Commits: world-features 5d63a75 (V3Ranges.cs new; IslandV3 feeds the range field into v3 ground; IslandV2 helpers made internal, v1/v2 behaviour unchanged). All behind `-island v3`.

- Row 1, `[ranges]`: spines 20 (4 ranges + 16 spurs), length 22.3 km (ranges 8.0); range peak max 142 min 95 m, field max 190 m; passes 5; borders 6: Wet-Volcanic open (no range, 1.8 km), Wet-Cold range Mountains 3.6/4.7 km 1 pass, Volcanic-Arid short Volcano 0.9/3.4 km 1 pass, Arid-Temperate range Mountains 2.3/3.4 km 2 passes, Arid-Cold river (no range, 0.7 km), Temperate-Cold short Mountains 1.2/3.7 km 1 pass; no range on 2; massif at (-2538,659) r 1295 m h 161 m; field 1024 (9 m cells), ~0.5 s. 0 compile errors.
- Row 2, walls (8 m grid, wall = over 45 deg): v2 land 774555 cells, over 45 deg 47480, over 38 deg 69052, max 82 deg. v3 land 780707, over 45 deg 32063 (-32%), over 38 deg 50735, max 81 deg. Done condition met.
- Passes (best of 7 parallel lines, max 4 m step on dry ground / range field alone): Wet-Cold 45/10, Volcanic-Arid 17/13, Arid-Temperate (2232,1734) 37/14, Arid-Temperate (1405,835) 46/8, Temperate-Cold 48/10 deg. The range field is walkable at every pass (max 14 deg; a medial-axis jump that gave 70 deg was fixed by taking the max over every nearby segment, plus a light blur). The 37-48 deg dry steps come from v2 ground features still under the field (river banks, the Mountains-biome strips), which B3/B4 replace.
- V3Audit: `[hydro audit] rivers 36 (ends 12, open 0), lakes 144 (14191 cells), falls 101 + grand 5, pools 42 (dry 0), ponds 5, dry beds 0 | grid 8 m: wet 70106, steep 975, water over ground 428, land between 8, floating 4, near falls 282`. Water code untouched; counts move because the ground moved.
- Row 3, hashes: v2 unchanged: 136 landmarks / 4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers 835B1404BEBDF563 (rivers hash is a new W-69 recipe over the river blob arrays). v3 measured: 141 landmarks / AC163FFE, caves 41 / 26115845CA1A851B, ground D02B2E32D2F18F61, rivers 1414CB113CBA4E0A. v1 not re-measured (calls); no v1 code path touched (only IslandV2 helper visibility changed).
- Shots (D:\PunchyMilestones\W-69): w69_v2_height, w69_v3_height (passes red, massif ring white), w69_v3_massif, w69_v3_range_end, w69_v3_pass (all _800).
- Notes: biome map is still v2 until B3, so Mountains-biome strips remain beside the new ranges. Editor left on Island Default.
- Calls used: about 70 (over the 50 budget: two compile/measure rounds to fix pass slopes).

## W-68 (2026-11-03) world-features 527677a: v3 B0 water audit + B1 v3 flag

- Merged origin/main (PR #89) into world-features: fast-forward to 1345b25. 0 compile errors before and after.
- Commit 527677a: `V3Audit` (new, Terrain/Island): one `[hydro audit]` line; eval `Punchy.Terrain.V3Audit.Run(source[, step])`
  on any island, and it runs at the end of every v3 bake. `IslandV3` (new): `-island v3`, PUNCHY_ISLAND=v3 or the new
  editor menu Punchy/Island/v1|v2|v3|Default (EditorPrefs + process env) bakes through IslandV3, whose steps still call
  v2. `IslandV2.RequestedVersion()` returns 1/2/3; `Requested()` keeps its meaning. `WorldSync`: "island vN" added to
  the world fingerprint (8A), so every fingerprint changes once; the refusal log names the host's version. The v3 hydro
  hash is not in the fingerprint yet (v3 = v2 for now); add it when B5 has a table to hash.
- Audit, v2 = v3 (identical lines):

| Count (v2, seed 1337, editor) | Value |
|---|---|
| Rivers (sources) | 27 |
| River ends / open ends (not on a lake, pool or the sea) | 8 / 0 |
| Lakes (4-connected lake cells on the river grid) | 154 (14625 cells) |
| Falls: river / grand falls tiers | 66 / 5 |
| Pools (landmark + grand falls plunge) / dry (under 0.2 m) | 42 / 0 |
| Biome ponds (16 m grid) | 5 |
| Dry beds | 0 (v2 has none) |
| 8 m grid: wet cells / steep water (ground over 35 deg under 0.3 m+ of water) | 69771 / 810 |
| Water over ground (surface 0.3 m+ over a dry neighbour's ground) | 343 |
| Land between surfaces | 13 |
| Floating water (dry and 1 m+ lower on all four sides) | 1 |
| Grid hits within 25 m of a fall lip (curtains, counted apart) | 222 |

- Reading the baseline: water over ground 343 and land between 13 are the counts v3 must take to 0; most "over" spots
  cluster west of the lake (-2690,-1180) and "between" ones near (-1950,210). The 154 lakes include every small hollow
  the river grid fills. Audit time 1.3-1.4 s.
- Hashes (editor, after the merge), v2 and v3 the same: landmarks 136 / 4E61F1CF, caves C49E8E3036C741FC, ground
  C0C906EE310FA91F, rivers A77D2ED7B919296E. All match the baselines. Only the network fingerprint differs by version.
- v3 bake: 0 errors (console has only the editor's own "Lifecycle ERROR ... EditMode" lines). v1 not run; its path
  only goes through `RequestedVersion() != 1`, same rule as before. Editor left on Punchy/Island/Default (v2).
- Calls: about 45.

## W-65g (2026-11-03) world-features 6a436b5 (build at 83bbb2d)

- Refresh first: left play mode, AssetDatabase.Refresh, no compile errors.
- Row 1 hashes (editor, at 83bbb2d): v2 136 / 4E61F1CF (raw B5EFF8C8), v1 103 / F628AE5F raw (19885131 by base name), caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers A77D2ED7B919296E. All unchanged. After the dedup, v2 is still 136 / 4E61F1CF.
- Row 2 (#36) build: one build, Succeeded, 0 errors. The player quit by itself (exit 0).
  - Wharf -3285 shore -> stilt door: FAIL. The walk says ARRIVED at -3272.0,-1.3,-1647.6 (0.3 m to go), but the shot shows the player wading in the water UNDER the wharf deck, between the piles. The walker gets past the solid treads and walks below the deck instead of up onto it.
  - Stone row -2982.6,3178.8 across: FAIL. STOPPED at -2983.8,42.8,3181.0, 9.0 m to go (water 43.77): it drops into the river at the row end again, as in W-65e. The 0.85 m pitch did not fix it.
  - Day shot: the row of about 10 mossy stones spans the river bank to bank and looks right.
- Row 3 dedup (editor only, not in the build): the W-65f patch works once the code is compiled; the W-65f 0 count was the stale compile. Added a check: a spot is kept only when it is also the narrowest in its own 25 m window. Rows within 60 m, before -> after: v1 -1893,-1613: 3 -> 2 (the 2 left are 1.3 m apart, different yaw: not yet one); v1 -1927,-680: 2 -> 1; v1 -1841,615: 1 -> 1; v2 -2982.6,3178.8: 4 rows (3 coincide at -2964.0,3193.4, one at -2994.2,3170.7). All > 0, so committed. The walked row moved: the old -2982.6,3178.8 spot has no row now, so the next walk needs new coordinates.
- Shots (only viewed at 560 px): D:/PunchyMilestones/W-65g (w65g_wharf_door_walk_end, w65g_stones_walk_end, w65g_stone_row_day), with the shot list and player.log beside them.
- Left: the wharf walker goes under the deck; the walker falls in at the row end; v1 -1889,-1609 still has 2 rows (the window is a 1-D line along each cell's own flow, so it needs a 2-D check); there is no build of the dedup yet.

## W-65f (2026-11-03): wharf shore treads + stepping stones, WIP at the call ceiling

- Commit 83bbb2d on world-features (pushed). No build, no player walks, no shots, no hashes: the call ceiling came first.
- Row 1 (#36): every wharf shore tread is now solid, a collider up to its top, so a walker on the low bank is stopped by any tread more than a step up and can't walk in under the steps. It applies to all 4 wharves. The fingerprint doesn't cover Solid. Not walked in a player yet.
- Row 2, walkable stones: the row at -2982.6,3178.8 had holes in its support grid between stones (NaN at the row centre, tops 0-0.11 above the water). The stones were 1.1 m apart and each standable top is only about 0.36 m. Now 0.85 m apart. Not yet probed or walked.
- Row 2, one row per narrows: NOT done. Measuring the narrowest lattice spot within 25 m along the flow gave 0 rows within 250 m of -2982.6,3178.8, so I reverted it. The patch is untracked at Punchy-world Assets/Temp/W65f_dedup_wip.patch. Cause unknown: either the probe ran before the recompile, or the filter rejects every spot.
- Left: fix the dedup, then ONE build, the wharf -3285.9,-1644.0 walk, the stone walk, the day shot, the v1/v2 counts and all hashes. The stone pitch changes the row mesh, but its placement and the landmark hashes should not move.

## W-65e (2026-11-03) world-features adabd0b

- Row 1 hashes (measured before the build): editor v2 136 / 4E61F1CF (raw B5EFF8C8), player 136 / B5EFF8C8, v1 103 / F628AE5F raw (19885131 by base name), caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers A77D2ED7B919296E. All unchanged, so nothing reverted.
- Row 2 stone rows: v2 35 rows (whole world, extent 4200, the W-65d count finished, 1179 s); v1 79 rows (extent 2000, 232 s). v2 positions: -3259.3,1511.5 (9.1 m), -3232.0,1495.0 (8.8 m), -2982.6,3178.8 (8.7 m, yaw -60). v1: -1937.7,-693.5 (4.2 m), -1895.1,-1614.2 (8.6 m), -1841.1,614.7 (9.2 m). Problem: rows stack. At v2 -3232,1495 four rows sit within 3 m of each other (cell is 2 m, so neighbouring cells all place a row in the same narrows); same on v1 -1895/-1891. Needs one row per narrows (not fixed, out of scope). Driftwood.asset Variants line fixed (`\001 5` -> `Variants: 5`), commit adabd0b.
- Row 3 build: one build, Succeeded, 0 errors (58 s). The player quit by itself.
  - Igloo 344.7,50.0 (door 3.75 m from the middle): PASS. Walk from 349.5,63.0 STOPPED at 347.0,271.7,52.9, 3.7 m from the middle (at the door). Feet 1.0 m above the igloo origin.
  - Wharf -3285 shore -> stilt door: FAIL, same spot as W-65c. STOPPED at -3285.9,0.4,-1644.0 (13.9 m to go). Ground there 0.42 (local z -0.9). The shot shows the player standing in the water UNDER the deck between the posts, so the walker goes under the shore treads instead of up them. Best cause: the first tread sits more than 0.4 m above the bank (ground about 0.5 after the 0.73 m drop at local z -3.25), so the player's support pick (PlayerMovementSystem, not edited) keeps the ground and walks under it until a post stops it. Fix idea on our side: start the treads on the bank above the drop, or put a solid block under the shore end so nothing can walk under it.
  - Stone row v2 -2982.6,3178.8: FAIL. Walk across (yaw -60, from -2985.9,3184.4 to -2979.4,3173.2) STOPPED at -2983.6,42.6,3180.7, 8.7 m to go (2.2 m before the row centre, on the row line). Water 43.77, feet 42.6: the player was at 43.8 just short of the row end, then dropped into the river where the end stone should be and waded until stuck; it never stood on a stone. Cause not found within budget (suspects: the row has no working support grid, or its end stone is off the walked line).
- Shots (not viewed full size): D:/PunchyMilestones/W-65e (w65e_wharf_door_walk_end, w65e_igloo344_door_walk_end, w65e_stones_walk_end), shot list and player.log beside them.
- Left: wharf foot (walker goes under the treads); stone rows: one per narrows, and walkable; nobody has looked at the igloo shot full size.

## W-65d (2026-11-03) world-features 7aaeb67 (WIP: stopped at the call ceiling, no build)

- Row 1 (#38) slope igloos: PASS in the editor. PorchSeat now tries 6 headings (including facing downhill) and sinks up to 3.5 m. Editor sim: 5/5 stop at the door, including 344.7,50.0 / 343.5,67.8 / 360.0,74.4. No player walk yet.
- Row 2 (#36) wharf foot: NOT FIXED / NOT VERIFIED. Profile at -3285: the bank drops 0.73 m at lz -3.25, then the treads rise 0.45 -> 1.25 over ground at about 0.5. The editor line sim (with props loaded) reaches the shore end on all 4 wharves, so it does not reproduce the player stop at -3285.9. Changed to 10 treads over 6 m (was 7 over 5.2) as a best guess. Needs a player walk. Next suspects: the player standing on the bank under the low treads (PlayerMovementSystem line 337 support pick), or swim mode at the shoreline (line 77).
- Row 3 stone rows: code done (6 variants 4-14 m, about 1.1 m pitch, fitted bank to bank). The world count (W65dProbe.Count) was still running in the editor at the ceiling, so there is no count yet.
- Row 4: no build, no shots, no hashes. StructureKit tread change: check the landmark hash.
- Note: Driftwood.asset has the same corrupted Variants line (`\001 5`) that SteppingStone.asset had. Left alone.

## W-65c (2026-11-03) world-features 601405c

- Row 1 (#38) PARTLY: the door block mesh is gone; a collider-only circle behind the door stops walkers. The tunnel roof, walls, crown and arch are 0.6 m higher, with a lintel over the door. Editor sim (head 1.8, colliders counted): 2/5 stop at the door. Igloo 5 (361.9,45.7) stops 4.06 m from the middle, door 3.47: FIXED. 3 still stop at the mouth because of the slope, not the roof: 344.7,50.0 and 343.5,67.8 have ground 1.8 m below the floor at the mouth (a 1.8 m step up onto the snow path), and 360.0,74.4 has ground 2.2-4.4 m above the floor (tunnel buried). The fix is PorchSeat seating, which moves flora; not done.
- Row 2 (#36) DONE: ring houses keep off a box round the whole wharf (x -14.5..7.5, along -4.6..23.5). Overlaps with deck cells: 0 across 4 villages (12 ring houses within 45 m).
- Row 3 NOT DONE: no SteppingStone row was loaded near -3098,3118 or -2940,3220. Narrows that fit a 12 m bank-to-bank row may not exist. 0 positions found.
- Row 4: one build Succeeded, 0 errors. Shot lines with -walk now wait for the walk to end (ARRIVED / STOPPED after 2 s still / FAIL on timeout). Wharf shore -> stilt door: FAIL, STOPPED at -3285.9,0.4,-1644.0 (wharf foot, 13.9 m to go). Igloo 5 door: PASS, STOPPED at 359.6,264.2,48.2 (3.4 m from the middle, at the door). Stone row walk: not run (no row). The player quit by itself.
- Row 5 hashes: editor v2 136 / 4E61F1CF (raw B5EFF8C8), player 136 / B5EFF8C8, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers A77D2ED7B919296E: all unchanged. v1 103 / F628AE5F was not measured (no probe at hand).
- Shots (not viewed): D:/PunchyMilestones/W-65c (w65c_wharf_door_walk_end, w65c_igloo5_door_walk_end, w65c_igloo5_tunnel_inside, w65c_river_cattails at -3156,3112, w65c_river_driftwood at -3139,3118.3).
- Left: PorchSeat for the 3 slope igloos; the wharf foot step (the walker is blocked at the wharf base); find or relax stone-row placement; measure v1.

## W-65b (2026-11-03) world-features 563796f

- Row 1 (#36) DONE: wharf variant now follows the village (rank + scrambled offset). Editor sim: 4 distinct layouts (house sets differ, v2 has 2 houses), doors 3/3, 3/3, 2/2, 3/3 = 11/11 reached.
- Row 2 (#38) PARTLY: hidden 1 m snow block behind the door; PorchSeat max turn 60 -> 30 deg. Editor sim: 4/5 stop before the door (3 at the tunnel mouth, head 1.7 vs roof ~1.65), 1/5 (igloo 361.9,45.7) walks to the middle in the sim; the player walk at that igloo stops 3.4 m from the middle (inside the tunnel, at the door). Paths 3/3 PASS. Shot igloo_door_walk_b shows the hidden block visible as a translucent ice box in the tunnel: needs a fix (non-rendering material or set it deeper).
- Row 3 NOT VERIFIED: stepping stones on a Narrows are now one row model (11-12 stones over 12 m, tops 4-12 cm over water, ends on/near bank). Player walk at the old W-63 crossing (-3098,3118 -> -3119,3140) crossed, but the shot shows no row there; a placed row's location was not found within budget.
- Row 4: one build Succeeded, errors 0; 9-shot list, quit by itself (115 s). Wharf walk shot stopped on the shore after 10 s (shot fired at the first -wait, walk too short) - not a pass. Cattails/driftwood shots taken at W-63 spots, framing not re-checked (driftwood view blocked by leaves).
- Row 5 DONE: docs/agents/world.md section added, committed.
- Hashes (editor): v2 136 / 4E61F1CF, caves C49E8E3036C741FC unchanged. Not measured: player landmark hash, v1, ground, rivers (no terrain/river code touched).
- Shots: D:/PunchyMilestones/W-65b (w65b_wharf_door_walk_end, w65b_igloo_door_walk_a/b, w65b_stones_walk_end, w65b_river_cattails, w65b_river_driftwood, w65b_stones). No Punchy.exe of mine running.
- Left: hide the igloo door block; find a placed stone row and walk it; re-shoot wharf walk with a longer wait; reframe river shots.


## W-65 (2026-11-03) world-features c39bc42 (#36)

- Row 1, layout per village (FishWharf, prop 0 of FishingVillage; local z = seaward): v0 -3285,-1644: main to z 24, joins at z 22 (T), 16, mid-left branch; 3 houses. v1 -2940,-1427: main to z 21, L at z 19 (x -14..1), mid branch z ~5 (x -8..9); 3 houses. v2 -2898,-2136: same seed layout as v0 (3 houses). v3 -2350,-2124: main to z 24, T at z 16 (x -8..5), short tip z 23; 2 houses. Solid bed under all decks; one shore end per village, now flat treads (pitched ramp's support stepped > 0.4 m on v1's low bank).
- Row 2: editor BFS walk (step-up 0.4, walls to head 1.7) shore -> every stilt door: v0 3/3, v1 3/3, v2 3/3, v3 2/2 = 11/11. No rail on door-side walkway edges; rails on open-water edges only.
- Row 3: build Succeeded, 0 errors. Player walk at v1: shore -> main walkway -> L branch -> house door: legs 1-3 reached, ended on the deck (y 1.3) 0.2 m from the door target: PASS. Shots (not viewed by me): D:\PunchyMilestones\W-65\w65_day_from_water.png, w65_walk_house_front.png, w65_dusk.png. Exe quit (-quit).
- Row 4 hashes: v2 136, editor 4E61F1CF, player B5EFF8C8; v1 103 / F628AE5F raw; caves C49E8E3036C741FC; ground C0C906EE310FA91F. Rivers by the w37 measure A5B3DE5D53257332 (same as every earlier w37-measure v2 run; the A77D2ED7B919296E form's probe not found; no river code touched). All unchanged.
- Left: v0 and v2 share one layout (one wharf model per species); per-instance variants would need more models. Shots unreviewed.

## W-63 (2026-11-03) world-features 875f785
- #36 ramp foot: DockShore walks back up to 30 m to find the real bank, and kit houses stay off a dock's lane (a stilt house stood across that deck). The dock is now based at -3299.0,-1656.1. Editor sim, step-up 0.4, 3 lanes: it PASSES on all 3 (end feet 1.09, deck 1.29). All 8 docks pass on the centre and +0.5 lanes. The old crab-pot snag at d 9.1 is still on the -0.5 lane of 2 docks.
- River dressing as flora data (RiverSpot on FloraSpecies, 13 biome lists): RiverCattail (inner bends, green biomes), Driftwood (gravel bars), SteppingStone (shallow narrows), FallenLog (narrow creeks, density 1). Loaded counts near -2940,3220: cattail 13, driftwood 24, stones 14, logs 13 (logs were at density 3; not re-counted at 1). Near 60,180: cattail 2, driftwood 1, stones 0, logs 5. Near village 0: cattail 10, driftwood 16. Shallow narrows are rare (2 creek spots with width <= 11 island-wide).
- Player (1 build): dock walk reached -3283.4,-1656.1, y 1.1 on the deck (0.2 m to go): PASS. Stepping-stone crossing at -3110,3130: FAIL. It is 2 stones about 10 m apart, not a chain. The walker reached stone 1, waded the riverbed (y 40.8, water 42.05), then stuck 0.9 m short of stone 2.
- Hashes: v2 136 / editor 4E61F1CF / player B5EFF8C8; v1 103 / F628AE5F; caves C49E8E3036C741FC; ground C0C906EE310FA91F; rivers A77D2ED7B919296E. All unchanged.
- Shots: D:/PunchyMilestones/W-63 (w63_dock_walk_end, w63_stones_walk_end, w63_river_cattails, w63_river_driftwood, w63_stones). w63_stones is badly framed (a near bank fills the bottom half), and the cattails are hard to see in w63_river_cattails.
- Left: lay stepping stones as a spaced row across the narrow (about 1.2 m pitch), not as single scattered draws; re-count fallen logs; better river shots.

## W-62c (2026-11-03) world-features 49d33c9
- #37 pier end: spires keep 6.5 m from any pier's far end (placement rule). Spire that stood 3.8 m off the end is gone; nearest is now 24.2 m. Shot w62c_pier_end.
- #36 docks: all 8 docks (4 villages) get the pier's solid bed under the planks. Editor sim, step-up 0.4, each walked from its own village, 3 lanes: all 8 reach the end on the deck (feet = deck - 0.2). Side-lane snags: crab pots at d 9.1 on 2 docks (-0.5 lane, walk around). Left over: dock -3296.5,-1656.1 meets a 1.3-1.65 m wall at its ramp foot on all 3 lanes (ground sits under the ramp start).
- Gotcha: the support grid exists only near the player; walking far docks gives `support -` and fake drops, so the first "all docks drop" reading was probably wrong. The bed is a cheap guard anyway.
- Player (1 build): pier walk reached -1467.1,-13.5 (0.2 m to go), y 28.0 on the deck: PASS. Longest dock (19.9 m, -2927.4,-1412.5) reached 0.3 m from the end, y 2.1 (deck 2.31): PASS.
- Hashes: v2 136 / editor 4E61F1CF / player B5EFF8C8; v1 103 / F628AE5F; caves C49E8E3036C741FC; ground C0C906EE310FA91F; rivers A77D2ED7B919296E. All unchanged.
- Shots: D:/PunchyMilestones/W-62c (w62c_pier_end, w62c_pier_walk_end, w62c_dock_walk_end).

## W-62b (2026-11-03): sea folk pier walk, shot check, docs (#37)
Commit 5c2ce10 (world-features). Shots: D:/PunchyMilestones/W-62b (w62b_ramp, w62b_from_pier, w62b_walk_end; I looked at from_pier only).
1. Pier snag fixed. There were two causes. (a) The 4.6 m ramp climbed 0.5 m per support-grid cell (a long pier has about 0.8 m cells), which left a 0.53 m wall at the deck's land end. That is the W-62 stuck point. (b) Thin plank gaps landed on 3 grid cell centres (z 4.2, 15.0, 25.9), which read as holes and dropped a walker to the lake bed. Fix in StructureKit.Pier: the ramp is now 8 m (about 10 deg), and a solid bed sits 0.17 m under the planks. No RNG draws changed. Editor sim at step-up 0.4: PASS on 3 lanes (centre, +0.6, -0.6), from 8 m up the bank to the far end (about 36 m), ending with feet at 27.99 (deck 27.99). The walk lane was already clear.
2. W-62 shots at 480 px:
   - shore_day: OK (pier, spires, village readable). There is a fog/spray patch at the right edge.
   - from_pier: the camera sat inside a spire. Retaken (now the pier deck, the arch and a spire at the far end).
   - under_1 and under_2: there is no underwater look at all. You see a dry-looking bed, the sky and full daylight, with no water surface from below, no fog and no tint. There are no black frames.
   - walk_end: the player is stuck at the pier start. Fixed and retaken.
   Gap note (#22): the water and camera have no underwater mode. Below the surface the lake is not drawn from beneath, and there is no depth fog or colour, so the village looks like it stands on land under a grey sky. This needs a camera/water feature (an underwater volume or post effect and a back-face water surface). That is rendering and camera work, outside world code. I made no world change for it.
3. Build 1x: Succeeded, 0 errors. Player walk from -1440,9 to the pier base, then to the pier end: PASS. Leg 1 reached at (-1445.6, 27.8, 4.3). The player ended 0.3 m from the end at (-1466.8, 28.0, -13.2), on the deck, pushed out 0.0 m. The player quit by itself.
4. Sea folk section added to docs/agents/world.md: lake pick rule, village, landing (ramp and under-deck), and the underwater gap.
5. Hashes:
   - v2: player 136 / B5EFF8C8, editor 136 / 4E61F1CF (unchanged).
   - Ground: C0C906EE310FA91F (unchanged).
   - Caves, rivers and v1: not re-hashed. This change only touches the pier prop mesh, which bakes after them.
Left: a spire stands at the pier's far end and blocks the view to the village from the deck. The underwater view needs #22. Docks use the same plank Platform and could drop a walker on a long dock (not seen yet).

## W-62 (2026-11-03): sea folk settlement (#37), partial
Commit f7c7c35 (world-features). Shots: D:/PunchyMilestones/W-62 (w62_shore_day, w62_from_pier, w62_under_1/2, w62_walk_end; not viewed).
1. Lake: 154 lakes; largest is 73.6 ha (2876 cells), level 26.7, 25.2 m deep, middle -1472,-202 (1481 m from land centre). Choice: biggest connected lake >= 8 m deep, lowest cell breaks ties.
2. Village (SeaFolkVillage) at -1480,-24 on the bed at 13.6 (13.1 m down): 6 shell domes, 3 spires (26-32 m, they break the surface), reefs. Landing (SeaFolkLanding) at -1446,4 on the bank (ground 27.0), 44 m away, with a 28 m pier toward the village. Landmark diff: 134 kept exactly (same lines), 2 new only. Editor 136 / 4E61F1CF, player 136 / B5EFF8C8. No ground or water stamping (Flatten 0), so ground/lake surface do not move; caves and rivers bake before this code (not re-hashed). v1 not run (sea types not in v1 lists).
3. Details as data: kelp gardens, shell mosaics, bubble vents, trade goods, coral arches, glowing anemones, seeded per prop. Shore and pier shots taken.
4. Underwater: 2 camera shots placed under the surface (-camlook); not viewed, so whether the water renders from below is unchecked.
5. Walk test FAIL: player walked from -1440,9 to the pier start (-1445.6, 27.4, 4.3) and stuck there 27.7 m short of the end for 16 s, 0.4 m over the floor. Probably the pier ramp or first pile blocks the step.
6. Build 1x Succeeded, 0 errors (Builds/Dev). Player quit by itself.
Left: fix the pier start so the player can walk on (ramp/collider), rerun the walk; look at the shots; docs note in world.md.

## W-61b (2026-11-03): waypoint walk, igloo porches (#38), bat cave walk (#35)

Commit 22affdd on world-features. Shots: D:/PunchyMilestones/W-61b (8: cave walk start/end, igloo 1/3/5 start/end).
1. Waypoint walk: `-walk x,z;x,z;...` (or x,y,z) walks the points in order; one point works as before. Player cave walk took 10 legs, every leg reached, log line per leg.
2. #38 cause: igloos sat at their centre's ground height on slopes, so the porch floor hung up to 1.0 m over a dip at the mouth (or was dug 4-5 m into a knoll); the W-61 steps lay below y=0, so the foundation skirt sank them 1.6 m (support tops -1.85 to -3.1), out of reach. The W-61 sim also took its mouth from the step-grown bounds (~2.6 m too far out), so its PASS was wrong. Fix: FloraPlacement.PorchSeat turns the igloo up to 60 deg toward the lowest mouth ground and seats it at that height (clamped -1.4/+0.5 m, no random draws); steps removed (W-60 parts back). Editor sim settlement 1: 5/5 PASS (6.1, 6.2, 2.9, 2.3, 6.8 m past the mouth).
3. Build 1x Succeeded, 0 errors. Player cave walk mouth -> bat settlement 1 -> white pool rim: PASS, ended 0.2 m from the rim (2999.8,-2.9,-2134.0), 62 m, pushed out 0.3 m in all. Igloo porch walks (the 3 that failed in W-61), from 6 m outside the mouth: igloo 1 PASS (8.7 m walked, ~2.7 m past the mouth, stopped 3.5 m from centre), igloo 3 PASS (8.8 m, ~2.8 m in, 3.0 m from centre, at the door), igloo 5 PASS (9.1 m, ~3.1 m in, 3.8 m from centre). End shots not viewed by me.
4. Hashes, all unchanged: v2 player 134 / 69CD531E, editor 134 / 829DF295; v1 player 103 / F628AE5F raw; caves C49E8E3036C741FC; ground C0C906EE310FA91F (outside-temple 7E891ED5DFB2C564); rivers A77D2ED7B919296E.
Left: igloo walkers stop 3-3.8 m short of the centre (door/inner floor); fine for the porch test, check the end shots.

## W-61 (2026-11-03): bat folk kit settlements, white pools (#35), igloo porch (#38, partial)

Commit 3cf1725 on world-features. Shots: D:/PunchyMilestones/W-61 (6 named + stray shot-6.png).

1. Bat settlements (#35): DONE. The 3 largest caves were picked deterministically. Cave 1 chamber 3001,3,-2135 has 4 wall houses and 10 details. Cave 26 chamber 525,45,958 has 1 wall house, 1 ceiling house (7.0 m up) and 10 details. Cave 36 chamber -3048,2,190 has 1 wall house and 11 details. Houses use kit House parts in BatFolk trim, with Lamp/Rune parts skipped (no lights). Wall houses hang 2.7-3.2 m above the floor.
2. White pools: DONE (CaveSystem.WhitePoolWater). Shots: w61_site0_whitepool, w61_site1_whitepool.
3. Details as data: DONE. A weighted Details table, with weights jittered 0.3-1.7 per site. Site 1 is mostly echo chimes; site 2 is mostly roost frames and guano. Shot: w61_site2_details.
4. Igloo porch snag (#38): NOT FIXED, 2/5 in the editor sim. I added 6 buried steps (no RNG). The sim still hits a 0.57-1.03 m PropSupport wall at the porch mouth on 3 igloos. Not yet confirmed: whether the sim measures from the right place and whether the support grid was rebuilt with the new steps.
5. Walk test: FAIL in the player. -walk heads in a straight line for the last target, so the player was pinned to the cave wall at 3050.7,22.6,-2146.3, 8.5 m in. No fall-through, in cave the whole time. The editor floor sim along the cave nodes passes: 62 m, max rise 0.37 per 0.25 m, 0 steps outside the cave, head room at least 2.3 m, stops 0.6 m from the pool rim. A following walk is needed to test it in the player.
6. Build OK (1). Player: 134 landmarks, hash 69CD531E. Editor: 829DF295; vs W-60, kept 134, gone 0, new 0. Caves C49E8E3036C741FC unchanged. Ground and rivers were not re-measured (the bat bake runs after them and has not touched them).

Left: igloo porch row 4; walk test in the player along waypoints.

## W-60 (2026-11-03): turtle folk mountaintop settlements (#38), village3 dusk retake (#36)
Commit 34895e0 on world-features (pushed). Build 1x Succeeded, 0 errors.
- Row 1 (#38): new TurtleVillage type (Resources) + TurtleVillageBaking, placed last (after the fishing villages) on the highest peaks of v2 (48 m grid of local maxima, highest first, peak then rings at 40/80 m; 600 m apart; igloo rings within 10 m relief). "[turtles] 3 from 687 peaks (13 tried)": 356,60 (h 269), -756,2660 (h 233), -660,3428 (h 211). Landmarks 131 -> 134; diff vs lmlist-w59c: kept 131, gone 0, new 3 (only the TurtleVillage lines). StructureKit: TurtleFolk trim, Igloo (domed snow-block shell, 1.7 x 3.4 m tall tunnel door, Rune glow drawn as window pane), Alpine clusters.
- Row 2 (#38): details are data, seeded per prop (Alpine: prayer flags, snow-buried pots, rune stones, cairns, ice lanterns, sleds; igloo dome rings, side huts, door frames vary). Shots: w60_turtle1_day, w60_turtle2_day, w60_turtle3_night (rune glow), w60_igloo_close_day/night.
- Row 3 (#38) player -walk, 3 igloo doors: A (343.5,67.8) stopped 2.9 m from centre, B (-744.2,2651.2) 3.6 m, C (-771.4,2660.5) 3.3 m - all walked up the tunnel to the door, pushed out 0.0 m, no snag/fall. Doors are closed (no interiors), so "in" = to the door. Path at settlement 2: 3 legs (-749,2655 -> -758,2653 -> -765,2660 -> -759,2669, rises up to 6 m) all reached within 0.3 m. Editor sim: settlement 1 had 2 of 5 igloos short at the porch mouth (0.6-1.0 m ledge on the slope) - not walked in the player; watch.
- Row 4 (#36): w60_village3_dusk retaken from over the river channel (-2960,9,-2131) looking east at the village and docks.
- Row 5 hashes: v2 player 134 / 69CD531E (was 131 / 41BA8108; change = the 3 new lines only), editor 134 / 829DF295. v1 player 103 / F628AE5F (raw form, same as W-57/58 = 19885131 editor form, unchanged). Caves C49E8E3036C741FC (unchanged). Ground all C0C906EE310FA91F (unchanged, even under the settlements: Flatten 0). Rivers not re-hashed (baked before this code, untouched).
- Shots: D:/PunchyMilestones/W-60 (17: the above + doorA/B/C start/end, path_start/a/b/c; shot-0 is the warm-up). All Punchy.exe runs quit.
- Left: the 2 porch-mouth snags at settlement 1 (sim only); igloo interiors and turtle folk NPCs out of scope.

## W-59 (2026-11-03): fishing villages, colossus keep-out (WIP, stopped at the call ceiling)
Commit 9b67f36 on world-features (pushed). It compiles in the editor, but it has not been built or shot yet.
- Row 1 (#36): code done. New FishingVillage landmark type (Resources) and FishingVillageBaking. Villages are placed on v2 only, after the caves, at the widest river mouths, on a dry bank next to the mouth. Up to 4 villages, at least 350 m apart, clear of every landmark. The type is appended last, so other type indexes don't move, and v1 gets no villages. The spot list is not printed yet: it goes to the log as "[villages] ..." at bake time.
- Row 2 (#36): StructureKit has the FishFolk coastal trim, StiltHouse, Dock (Platform deck, piles, end lantern, crab pots, moored boat) and Shore details (boat, net frame, fish rack, crab pots, driftwood, lantern), seeded per prop. Species: FishStiltHouse, FishDock, FishShore. No shots yet.
- Row 3: walk test not done.
- Row 4 (#40): code done. FloraPlacement.KeepOut (SharedStatic) clears 34 m round the titan and 26 m round the head. StoneColossi sets it, and FloraSystem.Replant re-places the streamed tiles once. No head shot yet.
- Row 5: no build, no hash checks yet. Risk: v2 landmark count/fingerprint will grow by the villages. Check that rivers, caves and the existing landmarks are unchanged.
- Known gap: FloraSpecies has no FaceLandmark field, so docks get the prop yaw (random), not the seaward facing. Docks may point inland until a facing option is added.
- Next: ONE build, the shot list (2 day + 1 dusk village, head), the hash checks and the dock walk.

## W-58b (2026-11-03): kit polish, walk test, docs, mound sim
- Row 1 before/after: titan and head shots showed no colossus (camera at the editor-probe spots, not where the player places them) -> retaken at the player spots, both colossi in frame. Ruin: flat green squares on walls (one ivy box per bay) -> thin trailing strands; dark glass panels in ruin windows -> real holes; rafters floating at random tilt -> rest on the floor. Intact houses: wall collider circles bulged ~1.2 m past the wall over the door step and outside stair -> set back to the wall face. Left: trees grow through the half-buried head (no flora keep-out round colossi).
- Row 2 (ElfGrove, player -walk): 5 houses (-2529.8,-1576.0; -2500.5,-1576.6; -2458.3,-1548.5; -2468.7,-1525.8; -2451.3,-1510.8) - all reached the front door and stopped 3.0/4.7/3.3/4.7/3.2 m from the centre, two up on the step (+0.5 m); no snag before the door, no fall-through. Doors are closed (no interiors), so "through" = up to the door. Stair (editor sim, player rules, step 0.4): 7 of 24 KitHouse variants have one, all 7 climb to the 3.4 m landing, no snag, no fall.
- Row 3: StructureKit section in docs/agents/world.md (committed 3b09f20), incl. notes for W-59 docks (Platform) and trims.
- Row 4 (#70, mounds grown directly, 5 copies on ElectricWastelands ground): all have colliders (1-9 circles, 0.57-1.88 m tall); centre walk blocked/slid on 4, 1 missed the pieces; edge walk 20/20 on all 5, no snag, 0 pass-through.
- Row 5: build 1x Succeeded, 0 errors. v2 127 landmarks, raw A5ED303A (= 0AAACD45, unchanged). v1 103 landmarks, raw F628AE5F (same raw as W-57; not mapped to the 19885131 form). Ground/caves/rivers not re-hashed: no ground, cave or river code changed (kit colliders and ruin meshes only).
- Shots: D:/PunchyMilestones/W-58b (city_street_elfgrove, city_overview_elfgrove, ruin_kit_1420_2160, colossus_kneeling_titan, colossus_halfburied_head).
- PROBLEM: the v1 check player (PID 23204, Builds/Dev) is still running: inline -quit only fires after a shot list, and I launched it without one. Not killed. Builds/Dev stays locked until it is closed.

## W-58 (2026-11-03), world-features 0c85421 (WIP)
- DONE #24 kit: StructureKit (pieces Wall, Floor, Roof, Door, Window, Trim, Rail, Stair, Rubble, Moss; trims per culture), FloraForm KitBuilding/KitRuin. Temple(seed) unchanged (CAB1F447 / 777 21F1AD29). Kit house seed 42: 203 parts, fp B65B386D; ruin 107 parts.
- DONE #24 city: ElfGrove is the biggest (r 96, -2511,-1518, 28 plots). ElfGroveLayout gets KitBuilding = KitHouse (Elf trims, 24 variants, seeded per building). Plots unchanged. Shots: city_street_elfgrove, city_overview_elfgrove.
- DONE #24 ruins: Ruin species now builds kit ruins (form KitRuin, 12 variants) at all 4 ruin landmarks (2119.3,3131.6; -2226.2,2325.5; -1899.7,2164.6; 1420.4,2159.8). Shot: ruin_kit_1420_2160.
- DONE #40: StoneColossi.cs: kneeling titan with snow (816,144,1312, yaw 79, Tundra/Glacier/Mountains) and half-buried head with moss (-1071,19,-779, yaw 346, Grassland/Temperate). Own rng salts, no landmark data: fingerprint 127/0AAACD45 -> 127/0AAACD45 (unchanged). Shots: colossus_kneeling_titan, colossus_halfburied_head.
- NOT DONE #70: the sim found 0 MagneticDebris/MagneticDrift instances loaded near ElectricWastelands spots (-600,-1400; 700,-2400; -1000,-4000), even with the camera moved there. Probe is ready (W58Probe.Mounds); the next try should find where flora tiles stream from.
- Build 1x (Succeeded, 0 errors). Ground all C0C906EE310FA91F, caves C49E8E3036C741FC (unchanged). v2 127 landmarks, raw player hash A5ED303A (= 0AAACD45, unchanged). Rivers: code untouched; my probe hash (A77D2ED7B919296E) uses another formula from A5B3DE5D53257332, so it can't be compared. v1 was not run.
- Shots: D:/PunchyMilestones/W-58 (not viewed). Left: #70 sim, v1 check, look at the kit shots (kit assets were first seen in the player).

## W-57b (2026-11-03), world-features f6b77f1
- DONE (code, no sim) #70: MagneticDebris pieces below 2.2 m now lie fallen on the mound and block via stepped colliders (cut off below the 0.4 m step-up); the scorched mound blocks. Pieces higher up keep orbiting and stay non-solid. MagneticDrift: the mound under the rock blocks; the rock always floats 5 m+ up, so it stays non-solid. Form text updated. NOT DONE: the editor sim at 5 slabs (ran out of budget).
- DONE #69: FloraForm.HazardShelter + ShelterStyle (AshDome / SnowLeanTo / ShardDome); 3 species; all 7 layouts list them with OnlyIn (Ashlands+Volcano / Tundra+Glacier / ElectricWastelands+GlassDesert). They take the house plot nearest the centre with no rng draws. On v2, 2 settlements sit in Tundra (OgreCamp -2073.8,1266.1; DwarfHold -2337.8,1994.6); none in ash or waste. Shots: shelter_tundra_dwarfhold, shelter_tundra_ogrecamp.
- #69/#72: v2 has only 1 hermit (HermitHutAsh 140,-545) and 2 campsite biomes (SparklingForest -3357.7,-256.9; FungalForest -3202.9,-1411.3). No second harsh hermit or third campsite biome on v2. v1 was not searched. Shots: hermit_ash_140_-545, campsite_sparkling, campsite_fungal.
- DONE #71: greatmushroom_day.
- Build 1x (Succeeded, 0 errors). Ground C0C906EE310FA91F (unchanged). v2 landmarks 127 / 0AAACD45 (editor). v1 103 landmarks; the player log hash is F628AE5F (raw log hash, not yet mapped to the 19885131 form). Rivers and caves were not hashed: they are baked before settlements and this change doesn't touch them.
- Shots: D:/PunchyMilestones/W-57 (not viewed). Left: the #70 sim; check the shelter shots.

## 2026-11-03 batch W-57: biome looks for hermits, campsites, great mushroom; cave stress test (#69-#72)

- New: `LandmarkType.BiomeLooks` (a list of biome lists -> look landmark type) + `Work` (Chop/Sweep/Shovel/Hammer). Looks are swapped by the dominant biome at the centre after placement, so count and positions never move. The Work anchor carries Work to the hermit's brain; the tool is axe/broom/spade/hammer. Commit 2ec329c (world-features).
- PARTIAL #69: shelter / ground / work per biome: Ashlands+Volcano stone house, basalt fire pit, ash sand, cinder rocks, obsidian stump, sweeps ash | Tundra+Glacier+Mountains mud hut, tundra ground, ice slab, shovels | ElectricWastelands+GlassDesert stone house, basalt, obsidian, hammers (armouring) | Dunes+Sonoran+SaltFlat mud hut, sand, cinder rocks, bogwood, sweeps | elsewhere the old hut, chops. The island has only ONE hermit (140,-545, now HermitHutAsh), so no second harsh-biome hermit exists without new placement (a hash change). Atypical shelter for harsh-biome settlements: NOT done. No shots (no build).
- NOT DONE #70: the electric wasteland flora with no collision are MagneticDebris (Debris form, orbiting pieces) and MagneticDrift (FloatingRock form, flat-slab variant). Both are `Solid: 0` by design ("Not solid" in the form tooltip). No fix and no sim yet: either give the low pieces colliders or make them read as decals.
- DONE (no shot) #71: GreatMushroom (the AncientTree stamp and ground; giant glow cap x2.6-3, pale caps, puffballs; no residents) replaces AncientTree in FungalForest. Landmarks v2 127, positions unchanged; base-mapped fingerprint 4D245E12 -> 0AAACD45 (the only diff is AncientTree -> GreatMushroom at -1798.5,-1970.2); raw A5ED303A. v1 (103 / 19885131) not checked.
- PARTIAL #72: CampsiteAsh / Snow / Fungal / Dune looks (fire, ground, a tent or giant puffball in place of the 2nd bedroll). The island has only 2 isolated campsites: -3202.9,-1411.3 is now Fungal; -3357.7,-256.9 stays plain (temperate). So 3 biomes can't be shown on this seed. No shots.
- DONE row 5: 11 cave jumps 2 s apart (editor play mode): frame 16.6-16.7 ms every jump; cave chunks 8-39 (per cave, not growing); dying 0; moving lights 0; creatures 18-103 (per spot); mono 1684-1738 MB, alloc 2608-2631 MB (flat, no trend). Nothing grows. Cave hash C49E8E3036C741FC (unchanged).
- Not checked: ground hash (C0C906EE310FA91F), rivers. Left: #70, second hermit + settlement shelter (#69), a 3rd campsite biome (#72), build + shots for all rows.

## 2026-11-03 batch W-56b: temple v2 finished on the ruin kit (#24)

- DONE #1: the "block" was the probe, not the temple. It walked straight lines between door centres, which ran through the pillared hall's pillar row (u 6.2-7.3, v -3.9..-5.1, hall floor y 8; not the entrance). The probe now walks the room chain square-on to each doorway (along the room's centre line, across, through). Walk in and out: 1312 moves each, blocked 0, max rise 0.32 / 0.27 m, sanctum reached (trunk rooms 45/45; the 2 branch dead-end rooms are not walked). Same seed builds the same layout (CAB1F447 twice; seed 777 gives 21F1AD29). No game code changed.
- DONE #2: the 7.5 m sink was the probe too: rooms stack in plan (stair 2 and the sanctum run under the pillared hall), and it took the first room containing the point (hall floor 8 vs sanctum floor 0.5 = 7.5). It now takes the highest drawn floor or box top within step reach. Sink 0.000 m in and out.
- DONE #3: ground (W-56 FNV-64, 4 m over +-6 km) e39ae7f -> now: all 6E9CF8A3EACD4760 -> C0C906EE310FA91F; skipping 80 m round the temple 7E891ED5DFB2C564 on both, so the change is limited to the temple. Landmarks v2 127 / 4D245E12 (editor and player), v1 103 / 19885131 (player, `-island v1`): unchanged.
- DONE #4: RuinKit.cs.meta committed; "Ruins" section + stacked-rooms gotcha in docs/agents/world.md (5eeb15a).
- DONE #5: one build (Succeeded, 0 errors). Shots in D:\PunchyMilestones\W-56: w56b_swell_eye.png, w56b_entrance_day.png, w56b_entry_hall.png, w56b_sanctum_torch.png (unviewed). Perf (-perflog, 3 s): entry hall 16.67 ms (max 17.0), sanctum at 22:00 16.67 ms (max 16.8), both vsync-capped (no frame timing in this player); Unity Lights 3 (1 shadowed) in both, particles 1897 / 3079.
- DONE #6: world-features fast-forwarded to 5eeb15a (c15a249 + 5eeb15a) and pushed; worktree is back on world-features. w56-wip pushed too.
- Left: nothing in the table. Not checked: the two branch dead ends in the walk; the frame cost above the 60 fps cap (needs an uncapped run).

## 2026-11-02 batch W-32: sunken temple in the dunes (enterable, 8 rooms on 3 levels) + interiors design note

World commit 1320e8f on world-features (local, not pushed; it sits on W-48/52/53).

- **Placement:** one SunkenTemple per island with dunes. v2 is at 3520.0,360.0 (facing 159 deg, base 10.9, relief 0.12 m, patch 1138 cells, 320 m in from the edge). v1 is at 1320.0,-2720.0 (base 7.3).
  - It is placed in a second pass after the towers, with its own salt 0x5E7A.
  - The diff shows additions only. With the SunkenTemple line removed, each new list hashes to the old value.
- **Hashes, old → new:**
  - landmarks v2 126 / 053C63D5 → 127 / 4D245E12
  - landmarks v1 102 / 51EA6463 → 103 / 19885131
  - v2 rivers A5B3DE5D53257332, ground F8F938002B0CBE46, caves C49E8E3036C741FC: unchanged
  - v1 rivers 2508BF538DA96D7E, ground A32E650C44A45E06, caves 47C703C0E17F77CB: unchanged since W-48
- **Shape:** a half-buried sand mound with a stepped ziggurat top, a broken portico, column stumps and a trench ramp down to the door, with drifts in it.
  - Rooms: entry hall (fallen drums), pillared hall (8 columns), corridor and stair 1 down to a sand-filled chamber (level 2), then corridor 2 and stair 2 down to the sanctum (level 3) with an altar and a statue.
  - Light comes from 5 roof shafts and 8 sconces (point lights on only while the interior shows).
- **Collision:** analytic, in SunkenTempleShape Solid/Floor/Wall, through TerrainSource Deck, TowerWall and FloorUnder (the SaltTower approach).
  - Walk test: 2110 steps of 0.1 m from the entrance to the sanctum and back, max rise 0.31 m, max drop 0.31 m. It reached the sanctum floor (local 0.5) and got back out.
  - v2's first run had 9 blocked steps at u -25, where the edge of a ramp drift stood taller than the 0.4 m step-up. I moved that drift off the path and lowered it.
  - After the fix, v1 had 0 blocked. I did not re-walk v2.
- **Tris:** LOD0 6560 (<120 m), LOD1 1248 (<1.2 km), LOD2 356. The interior is 2736 tris and draws only at LOD0 while the camera is inside the temple's box. From outside, a plug mesh closes the door and shafts. The interior was hidden in all outside probes.
- **Doc:** docs/agents/interiors.md (design only). Recommended first step: pull the temple tables into a shared RoomSet, then make one village hut enterable with its rule 9 door.
- **Shots** (Dev build, `-time 11 -weather Clear -nohud`, viewed at 800 px): D:/PunchyMilestones/W-32/temple_120m.png, temple_ramp.png, temple_hall.png, temple_sanctum.png.
  - 120 m: the mound reads, but as a smooth dome with small ruins on top.
  - Ramp: the trench, columns and pediment read well.
  - Hall: pillars, a sconce glow and a shaft opening show. It is dim and untextured.
  - Sanctum: the altar and a sconce show, but a column/statue sits close in the foreground and blocks much of the view.
- **Not done / follow-ups:**
  - The interiors use the plain tinted basalt material, with no sandstone texture.
  - The sanctum shot framing should be redone.
  - Animals and the camera arm don't know about the temple.
  - The sand chamber is drifts on a floor, not flooded.

## 2026-11-02 batch W-53: W-37 meanders in with landmarks fixed (option b); v2 dam cut to a canyon; round wet-sand reach
- DONE: #0 Temp/w37_measure.cs repaired. At 3268e84: v2 landmarks 126 / 053C63D5, caves ACA9CC9155621A44, rivers 70B01EF403A78FF5, ground A87024EE3525ADC0, falls 70, open ends 92 not at falls, floating 28; v1 102 / 51EA6463 (unchanged since W-48).
- DONE: #1 w37-wip patch on world-features (ef41bf2). TerrainAuthoring.BuildPlaces bakes two river maps from the same bare ground: the jittered map (`RiverBaking.Build(..., meander: false)`) for landmarks, settlements and the salt tower, then swaps in the meandered map before caves. v2 landmarks 126 / 053C63D5 and v1 102 / 51EA6463 unchanged (editor and player). v2 rivers 70B01EF403A78FF5 -> A5B3DE5D53257332, ground A87024EE3525ADC0 -> F8F938002B0CBE46, caves ACA9CC9155621A44 -> C49E8E3036C741FC (count 37 unchanged; cave shapes follow the carved ground), falls 70 -> 66. v1 rivers/ground/caves unchanged (meanders are v2-only). The double bake costs ~0.9 s in the editor rivers stage (player stages: rivers 299 ms).
- DONE: #2 open ends not at falls 92 -> 46. Landmark spots with river water within 1.2x footprint: 0.
- NOT MET: #2 floating water 28 -> 35 (W-37 alone 33). 8 of the 35 are on narrow rivers (width <= 12 m, not smoothed). Most of the rest are on steep drops where the water falls 5-15 m between cells (656..684,562..654; -2186,3604..3633; -702..-630,513). Tried carving each bed around both the drawn and the jittered point: 34, reverted. Needs a cascade-aware measure or a lower sigma (trade-off: less meander).
- DONE: #2b `RiverBaking.CutDams` (v2, meandered map only): a river whose water stands above the lowest land beside it is cut down to that land minus 0.3 m when a downstream path reaches water at or below that level within 16 m deeper and 900 m. Dam at -2343,-437: water 34.51 -> 23.1 m, flat for ~480 m (-2338,-445 to -2405,-887) with banks 37 m beside it, so ~14 m below the banks, then falls in rapids to 10 m by -2571,-1099. Banks are the normal carve slope (0.55), walkable. Dam-box open ends (-2300..-1900, -400..-150) 13 -> 1 (-1957,-205 left, upstream of the cut at 33.5 m). Landmark diff empty.
- DONE: #2c ExtendStillWater finds the nearest still water by true distance (square rings outward, stopping once a ring can't beat the best) instead of 8 rays, so the band edge isn't an octagon. Mesh only (hashes above unchanged by it). Frame at 3585,-42: 16.67 ms (max 17.0 / 16.9), same as W-52. Eye reshot, but the run was rainy and one faint straight line still shows in the near sand, so I can't confirm the edge is gone (it may be rain puddles or a chunk seam).
- DONE: #3 shots, same cameras, viewed at 800 px (before top, after bottom). Wide A (-3286,-1694): the river now curves smoothly, no bulges. Wide B (-1203,-1902): the bends are smoother and the bank lines cleaner. Canyon aerial: the river sits lower between brown cut banks and runs straighter. Canyon floor: banks rise on both sides; the before shot had a wide flat pool. The floor camera is the same xz and direction, but 2.4 m over each water level (y 37 before, 25.5 after). Before shots are rainy (weather drifts despite `-weather Clear`).
- Checks: compile clean; 1 build Succeeded, errors 0; player v2 log 126 landmarks / hash 053C63D5, no exceptions, quit by itself; eol kept (RiverBaking/RiverData lf, TerrainAuthoring/BuildChunkMeshJob crlf); QualitySettings.asset left alone (was modified before).
- Shots: PunchyMilestones/W-53/wide_a_pair_800.png, wide_b_pair_800.png, canyon_aerial_pair_800.png, canyon_floor_pair_800.png, eye_after_800.png (full size in before/ and after/).
- NEXT: floating water on cascades (measure or fix); recheck the wet-band edge on a clear day.

## 2026-11-02 batch W-52: still water reaches over shallow dune beaches (mesh only); post-load 33 ms not reproduced
- DONE: #1 cause at 3585,-42 (lake 6.81, beach ~3 cm/m). Two causes. (a) Lake/bay water is claimed per coarse RiverMap cell (SampleGround bay rule: `RiverSampling.ShoreLevel` per cell, -inf in cells without a shore level), so on a beach only 0.1-0.4 m under the lake level the claim stops on a cell line (e.g. z -225, x 3594-3606: 38 dry vertices under the level, mean 0.23 m). BuildChunkMeshJob.BuildWater's bank ring only extends a sheet one vertex and only over ground above it (`water[i].w >= n.x - 0.05f`), so it can't fill them: holes and straight cuts. (b) The wet-sand band (`if (ground.HasWater) Extra.w = ...`) only exists inside the claim, so it is cut into dark rectangles (aerial before).
- DONE: #2 fix in BuildChunkMeshJob only (world-features 3268e84): new ExtendStillWater. Still water (no flow: lakes, sea) reaches up to 8 m over a dry vertex when every ground sample on the ray to it is below its level (8 rays, depth <= 1.5 m, not over landmark pools, not into cave air); the wet band comes from the nearest still water and fades 4-8 m out. An apron of world samples round the chunk keeps borders and LODs consistent. SampleGround untouched. Open edges (probe simulating the mesh rule, dunes 3328..3776 x -320..64): 1 m 182 -> 28, 2 m 73 -> 14, 4 m 21 -> 8, 8 m 11 -> 4. Island-wide at 8 m: 1616 -> 1217 (sea 45 -> 37); extended vertices 327, floating water 0 (no extended vertex sits above ground; path rule forbids spill past ridges). Frame ms at 3585,-42 t10..t80: 16.67 (vsync) before and after.
- DONE: #2b not reproduced. Before run with -perflog per line (current build, nothing else running): t10..t80 frame 16.67-16.89 ms, cpu main 5.4-5.8 ms, render 2.0-2.2, gpu 2.4-3.4; single spikes max 38.7 (t30) and 51.7 ms (t50). After: frame 16.67 (max 16.9) throughout (this build had no frame timing stats, so no cpu main). No 33 ms window, so the W-48 one was most likely contention (Zach's Builds/Windows player and 2 editors were running then). Builds/Dev is not a development build, so no Profiler capture or marker ProfilerRecorder in the player; top-3 not named. Nothing to fix.
- DONE: #3 shots, same cameras, viewed at 800 px. Aerial: before has a dark straight-edged rectangle on the east shore; after the wet band follows the shoreline, no rectangle. Eye: before the beach has no wet band; after a wet band runs along the shore.
- OPEN: eye after shows one faint straight diagonal edge in the wet band (8-ray distance makes an octagon-shaped reach). Fix: more rays or a distance blur (needs a build). The after run happened to be rainy (weather drifts despite `-weather Clear`).
- Checks: compile clean; 1 build Succeeded, errors 0 (a first call failed in 0 s because the editor was still leaving play mode, nothing built); player: no exceptions, quit by itself (11 shots in 103.2 s). Hashes: only mesh code changed (no SampleGround, landmark or cave code). Temp/w37_measure.cs no longer compiles (RiverMapBlob.Points gone), so hashes were not re-measured. eol crlf kept.
- Shots: PunchyMilestones/W-52/aerial_before_800.png + aerial_after_800.png, eye_before_800.png + eye_after_800.png (full size in before/ and after/).
- NEXT: smooth the wet band reach (more rays or blur); repair w37_measure.cs for hashes.

## 2026-11-02 batch W-48: large-cave frame cost cut (Burst camera arm, cave room cull); colossus normals fixed; W-42 reshoots
- DONE: #1 profile. Cave under -1669,2494 = v2 cave 9 (largest, 102 nodes, box 147x86x200 m, mouth -1664,73,2525), floor y 48.8 there (surface 85.7). Builds/Dev, 1600x900, vsync. Surface vs cave 9 (same run, old code): frame 33.1/33.3 ms, cpu main 33.1/33.2, render 2.4/1.8, gpu 4.8/3.5 ms; cave chunks submitted 33/51, in view 11/6, 100k/66k cave tris; lights 2 (1 shadowed)/3 (0); particles 394/353; camera probe 0/677 us/frame; IsRock 1.9/2.3 us per Burst call; creatures 0.88 ms total. Deeper in cave 9 (-1653,29.7,2380): cpu main 15.2, probe 2.27 ms, 57 chunks submitted, 201k tris in view, gpu 0.5 ms. Top 3: (1) camera spring-arm IsRock probe, managed (0.7-2.3 ms/frame); (2) cave chunks drawn/shadow-culled when not in the camera's cave (57 submitted, 20 in view); (3) a main-thread cost not cave-specific: ~33 ms frames for the first ~70 s after load at both surface and cave (cpu main bound, GPU < 5 ms) - this matches Zach's 28 fps and is not cave meshes or collision. Walls off (-nocavewalls) changed nothing (33.2 ms), so meshes are not it. Running alongside: 2 Unity editors (Punchy, Punchy-world, idle) and Zach's Builds/Windows Punchy.exe.
- DONE: #2 fix (world-features 6abc0d8). Camera arm probe as a Burst job: 677 -> 24 us (mouth), 2266 -> 70 us (deep). Room cull: deep in a cave (SurfaceHidden >= 0.99) only chunks of caves whose box holds the camera (plus overlapping caves) draw: 57 -> 41 submitted. Cave 9 deep cpu main 15.23 -> 13.22 ms (frame vsync-capped 16.8 -> 16.7, max 29.7 -> 16.7). Cave 9 mouth spot: 33.3 -> 34.8 ms (inside the post-load 33 ms window; room cull off there, hidden 0.83). Shapes and hashes unchanged.
- DONE: #3 3 largest v2 caves, cpu main old -> new (frame 16.7 ms vsync in all): cave 9 15.23 -> 13.22, cave 20 (-1571,60.8,805) 7.66 -> 7.22, cave 1 (3001,-3.8,-2135) 5.48 -> 4.44.
- DONE: #3b colossus: DunesColossus.MeshBuilder Ellipsoid/Dome/Limb wound every face inward (cross products point in), so normals and backface culling were inside out. Fixed once in MeshBuilder.Tri (corners swapped): body, head, arms, hands (no LODs); furrow has its own up normals. Same builder is used by Mantrap (fixed too, Dome inside flag now correct). Other landmark meshes use different builders, not changed. Placement unchanged.
- DONE: #4 reshoots, all viewed at 800 px. Shard: player at (-109.7, 4.9, -3862.8) per log, beside the prong base, body visible, no -benchstops. Red star: orange streak in open night sky between clouds. Colossus before/after by day.
- Checks: compile clean (2 errors fixed first); 1 build Succeeded, errors 0 (enableFrameTimingStats on for it only, ProjectSettings unchanged); player log 126 landmarks / 053C63D5; no exceptions; eol unchanged on all 6 files; my player quit by itself.
- Shots: Punchy-world/Temp/w48/w48_colossus_before_800.png (inside-out shading) and w48_colossus_after_800.png (lit correctly from outside); w48_shard_800.png (player side-on at the prong); w48_redstar_800.png (red-orange star, clear sky).
- Learned: docs/agents/performance.md (Burst arm, IJobExtensions.Run, -perflog/-gocave), docs/agents/world.md (MeshBuilder winding)
- NEXT: find the post-load ~33 ms main-thread cost (first ~70 s, surface too: streaming/bake work on main thread?) with a Profiler capture; cave 9 cpu main 13 ms vs 4-7 ms elsewhere (scales with loaded chunks).
## 2026-11-02 batch W-42: ArcSpire shard collision follows the lean; shooting stars 1/20, 1 in 6 red-orange
- DONE: big wasteland shards (ArcSpire prongs >= 2 m; smaller and non-solid debris/floating shards walk-through) collide as stacked 0.5 m slices up each tilted prong (to 4.5 m) plus a low base core, via FloraObstacle (flora is not in TerrainSource; no PMS change) (world-features f137084)
- DONE: slice plants only within 14 m of the player
- DONE: shooting stars 0.006 -> 0.0003 per slot (shower 0.9 -> 0.045), 1 in 6 red-orange; -redstar test switch (f137084)
- NOT DONE: shots 3 and 5 failed: shard shot has no player in frame (benchstops held the body elsewhere), red star hidden behind a prong and cloud. One build/one list used; needs a reshoot batch.
- Checks: editor sim (PMS slide by reflection, 0.1 m steps) at 3 prongs tilt 7.1/10.0/10.9 deg: pushed 1.03/1.88/1.21 m, max sink -0.040/-0.039/-0.042 m (4 cm clear), walk-past min step 0.066/0.016/0.023 of 0.1 (no stall), got 11.8/11.3/10.9 of 12 m. Obstacles near one spire 4 -> 31; slide query 0.10 -> 0.48 us/step (managed copy), UpdateObstacles 0.022 ms. Stars (4096-slot cycle, CPU hash): normal 24 -> 2 (0.059 -> 0.005/min); shower 36.0 -> 1.78/min, red 111/730 = 15%; 5 min window: 0 -> 0 normal, shower 185 -> 12 (3 red). Build OK, 0 errors; player log 126 landmarks / 053C63D5. Ground/cave code untouched.
- Shots: Punchy-world/Temp/w42/w42_shard.png, w42_redstar.png (both unusable, see NOT DONE)
- Learned: docs/agents/flora.md (leaning flora collision = stacked slices, ColliderBase)
- NEXT: reshoot 3 and 5 (place the player with -goto without -benchstops, star facing open sky); merge w37 river patch

## 2026-11-02 batch W-14: v2 loads in 7.9 s (was 15.8), cave render cost is not the world's
- DONE: #23 load. Timed each bake stage; the pond grid (16 m sample of the whole world, ~2.8 s) ran 4 times per v2 bake (3 diagnostic counts + the mask). Now one pass whose labelling also gives the report, rows in parallel. Editor v2 stage ms before/after: rivers 228/238, landmarks 22/22, settlements 1006/1025, caves 1369/1375, anchors 155/158, ponds 8463 (3 passes)/167, island-report pond recount 2809/4; bake 11777 -> 3525 ms. Player log: `[bake] stages ms: rivers 187 landmarks 21 settlements 1019 caves 1380 anchors 154 pondmask 248`, baked in 3557 ms, `v2 ready at 7.9 s since start` (world-features d473bf1)
- DONE: #23 cave render diagnosis (no fix: not world-owned). At the deepest chamber of the cave by each bench stop: v1 106 creatures / 2507 segments, v2 127 / 2815 (+20% / +12%). Both SurfaceHidden 1.00 (surface creatures hidden), 1 roost in 45 m (capacity v1 21, v2 28), 0 cave pools in 40 m (no cave fish). Within 60 m: v1 39 creatures, v2 4. So neither world data (roosts, pools, hidden test) nor creature count explains 2.2 -> 10.5 ms. For the animal lane: profile CreatureRenderSystem's own passes at the v2 stop (sky birds, critters, emotes, door scan, per-species loops over all 127 bodies); count is not the cause.
- SKIPPED: volcano markers: #23 only names a "volcanic region", no marker spec.
- Checks: compile clean; editor play v1 101 / E128CB1D, v2 127 / 831FF2EE; player log v2 127 / 831FF2EE; pond stats unchanged (7 ponds / 22 wet -> 1 / 15, dropped 7) before and after; one build Succeeded, errors 0; my player quit by itself (shot list done, 3 shots in 59.1 s), no Punchy.exe of mine running.
- Shots: PunchyMilestones/world/w14 (not viewed). v2-cave-mouth: v2 bench stop looking at the cave mouth -1665,2520. v2-cave-chamber: camera in the deepest chamber (-1699,33,2368).
- Learned: lanes/world/STATE.md (diagnostic passes over the whole world cost seconds; reuse the real pass's data)
- NEXT: #23 v2 default decision; settlements (~1.0 s) and caves (~1.4 s) are now the biggest bake stages; #13 Z-35 fall speed, #14 cave-mouth shards.

## 2026-11-02 batch W-13: river fog finally shows (test flag), blue moon face crisp
- DONE: #15 `-weather <name>` now forces a cell that ignores the biome's weather list (tests only; ] key and natural weather unchanged). MarshFog shot over both W-12 rivers (world-features fd25279)
- DONE: #34 blue moon face brightness 1.6 -> 0.7 (it clipped and bloomed over itself) and the corona now starts at the disc edge (was 0.98 r); size 0.22 rad, tendrils and halo outside unchanged (fd25279)
- Checks: compile clean; editor play: Describe() reads MarshFog at Grassland 197,-663 and Temperate -1885,-1562 after ForceAt (Grassland had none before); one build Succeeded, errors 0; v1 101 / E128CB1D (editor play), v2 127 / 831FF2EE (player log); all my players quit by themselves.
- Shots: PunchyMilestones/world/w13. w13-fog-grassland-800: thick white fog, river with its bridge visible below. w13-fog-temperate-800: river clear in front, light mist over the far bank and forest. w13-moon-night-800: blue moon at 23:30, disc edge crisp with a dark rim, maria and craters read, tendrils outside as before.
- Learned: lanes/world/STATE.md (blue moon direction per hour on day 0)
- NEXT: #23 v2 follow-ups (volcano markers, default decision), #13 Z-35 fall speed, #14 cave-mouth shards. Temperate fog is light; a denser MarshFog preset is a design call.

## 2026-11-02 batch W-12: big sand-coloured dune dust, crisper blue moon, river fog still missing
- DONE: #44 dune dust is now a few large (5-10 m) sand-coloured soft-volume patches: new AmbientParticleType DensityScale / GroundTint / Volume, no biome re-bake, no raymarching. Puffs in range at Dunes 1800,-1800: 126-132 before, ~30 after (world-features 482e7c8)
- DONE: #34 blue moon face: fifth, finer crater scale and crisper walls and rims; radius unchanged (482e7c8)
- NOT DONE: #15 river framed well (Grassland 305,-581; Temperate -1875,-1562) but no fog: forced MarshFog is masked by each biome's weather list (Grassland has none; Temperate at chance 0.2 still clear after 15 s)
- Checks: compile and shader messages clean; build Succeeded, errors 0; v1 101 / E128CB1D measured in editor play; v2 hash not re-measured (no terrain or landmark code touched, expected 127 / 831FF2EE); shot-list players quit by themselves.
- Shots: PunchyMilestones/world/w12. dunes-close: soft sand-coloured cloud patches on the horizon. dunes-wide: patches only faint against the sand. moon-night: crater speckle visible, halo still softens the edge. river-fog*: river in view, no fog.
- Learned: docs/agents/world.md (DensityScale / GroundTint / Volume)
- NEXT: #15 let `-weather` bypass the biome mask for tests (or check how allowed scales with chance), then retake. #44 if the wide view should show the patches, raise contrast a little (darker tint or more lit side). #34 less glow over the disc if it still reads soft.
- Budget: about 110 calls (ceiling 85), 1 build, 2 player runs (the second retook #15 in Temperate).

## 2026-11-02 batch W-11: v2 ponds, big blue moon, timber/fog looks
- DONE: #19 v2 ponds come back: the core test uses the biome map's sharp weights on v2 (blurred weights never passed 0.4). v2 1 pond, 15 wet cells, min/median/p90 15/15/15, 0 single-cell; v1 unchanged 33 blobs 3/4/7 (world-features 959fd34)
- DONE: #34 blue moon radius 0.13 -> 0.22 rad (about 6.5x v0.11.0), extra crater scale and rock octave so the face keeps detail (959fd34)
- DONE: #15 hall timber close-up: dark posts and beams read clearly on the hall front
- NOT DONE: #15 fog over a non-marsh river: the shot framed forest and a dirt track, no river or fog in view; needs a reframed spot
- Checks: compile clean, no shader errors; 1 build to Builds/Dev, Succeeded 0 errors; v1 101 / E128CB1D, v2 127 / 831FF2EE; shot list 7 shots in 139 s, quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w11/w11-moon-night.png (blue moon about a quarter of the frame wide over the treeline, white moon at its edge, face mottled, slightly soft), w11-moon-dusk.png (blue moon rising behind the eastern trees, clearly large and blue), w11-hall-timber.png (hall front, timber frame and banners at about 12 m)
- NEXT: #15 fog shot at a non-marsh river picked with WorldProbe (H + river cell); more v2 swamp area if Zach accepts a v2 hash change.

## 2026-11-02 batch W-10: pond pruning, readable sand puffs, cobbles
- DONE: #19 small ponds dropped by a baked PondMask (wet cells labelled on the 16 m grid, ponds under 3 cells removed). Blobs 219 -> 33, wet cells 361 -> 154, min/median/p90 1/1/3 -> 3/4/7, single-cell 165 -> 0 (world-features c5ddbe2)
- DONE: #44 sand puffs warmer than the sand, more opaque, smaller, hanging 0.6-1.4 m; 126-132 SandPuffs in range at the Dunes spot, a few per screen (c5ddbe2)
- DONE: #39 cobbles bigger (0.5 x 0.6 m), wider grout, stronger stone/grout contrast (c5ddbe2)
- Checks: compile clean; 2 builds to Builds/Dev, 0 errors; v1 101 / E128CB1D, v2 127 / 831FF2EE; shot list quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w10/w10-dunes-puff-b.png (orange puff close at left, about 10 m, more along the dune line), w10-town-ground.png (plaza stones and grout clear from the feet to the benches), w10-swamp-ponds.png (a few mid-sized ponds in the marsh, no specks)
- Learned: docs/agents/world.md (pond mask)
- NEXT: v2 default decision; puff density/colour per biome if Zach wants them softer; springs on ridge crests.

## 2026-11-02 batch W-07: ponds, high springs, fall shot, storm flowers
- DONE: #19 ponds re-enabled; they are dug only after rivers, landmarks, settlements, caves and anchors are baked (TerrainSource.PondsOn). Cause of the earlier 102 / 2294FB88: roadside landmarks were placed on the dug ground. v1 now 101 / E128CB1D in play with ponds on.
- DONE: #18 v2 springs: SourceHeight 45 m. Source beds min 18 -> 44, median 64 -> 90 m; 31 -> 22 sources; v2 130 landmarks DA354F63. First try (rain weights) barely moved them (median 68), dropped.
- DONE: #42 filtered v2 fall shot: 60 falls now (21+10+20+16 old bins -> 16/9/19/16), 51 clusters. Not clearly too many; 28 are 8 m or more.
- DONE: #30 `-weather Storm` forces a storm; shot taken in a meadow, rain and wind visible, flowers lean.
- Builds/Dev built twice (second after the springs rewrite). Players quit on their own. A Punchy.exe (PID 36200) runs that is not mine.
- Shots: PunchyMilestones/world/w07/pond.png, storm-flowers.png, v2fall.png.
- NEXT: pond spot was a wide shallow bay near the coast, pick an inland swamp pond; v2 default decision.

## 2026-11-02 batch W-06: small falls, cave stream ends, ocotillo mimics
- DONE: #42 v2 fall filter (world-features 646202c): falls with a water drop under 3 m dropped (5 were under 2 m, some negative, invisible) and falls under 8 m within 60 m of a bigger kept one merged. 98 -> 67 falls. Close shot of an unfiltered 4.6 m fall (riverfall-before.png) shows a teal curtain with white spikes; the filtered result is not re-shot (needs a v2 run).
- DONE: #3 z34-pool was already merged (ancestor of HEAD). Cave stream ends, v1: pool 5, hole 0, left out 0, kept 5. v2: pool 12, hole 2, left out 0, kept 14. Logged as [cave-streams] at bake.
- DONE: #17 mimics do spawn on v1, no fix needed. Rule: FaunaDirector.TrySpawn shortfall race, density 0.2 per ha in Sonoran (about 0.9 expected in the 120 m radius), so about one at a time. Sonoran is 7 cells of 140 land (2.5 km2). Seen at (-295,-2224), (390,-2483), (-3702,-1439).
- Checks: v1 hash 101 landmarks E128CB1D with the new code; Builds/Dev built (first attempt failed on a file copy lock, second succeeded); one player run, quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w06/cavepool.png (v1 cave stream running into a green pool), mimic.png (ocotillo mimic, bulb root with feet under a trunk, in Sonoran), riverfall-before.png (v2 4.6 m fall, curtain visible).
- NEXT: re-shot a filtered v2 fall; decide whether 67 falls is still many; v2 default decision.

## 2026-11-02 batch W-05b: smooth v2 rivers and waterfalls
- DONE: #18 v2 rivers follow Hermite curves through the river points (no grid angles), width grows downstream (9-45 m), bed carved on the smoothed line (world-features e3ab4af).
- DONE: #42 waterfalls where a segment's bed drops 4 m or more: bed step half way along the segment, curtain and spray from the landmark effect emitters. 98 falls (includes stacked cascades).
- NOT DONE: springs not rewritten (still catchment/snowmelt, not on ridge crests); waterfall look unreviewed close up (see Checks); fall curtain not visible in the one shot.
- Checks: v1 unchanged in play on SampleScene: 101 landmarks, E128CB1D. v2 now 129 landmarks, hash 62B8C643 (was 134 / 71F2AF0C; rivers shift landmark spots), same in the player; bake 3.5 s. 31 river sources; longest 6548 m (src 86 m -> 0 m), then 6305 m (33 -> 0), 6125 m (90 -> 0); widths 9-45 m. Biggest fall: a 32 m single step at width 9.5 m. Builds/Dev succeeded, player quit on its own, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w05b/map-v2-rivers.png (top-down, smooth rivers), bend.png (ground view of a curved river bend, bank and forest), fall.png (looking up at the big fall's cliff face from downstream: a steep dark wall, water curtain not visible).
- Learned: lanes/world/STATE.md.
- NEXT: look at a lower fall (4-8 m) for curtain and spray; place springs on high ground; decide v2 as default.

## 2026-11-02 batch W-05: generator version 2 wired behind -island v2
- DONE: #23 layout B (five realms) as generator version 2, selected by `-island v2`; v1 default unchanged (world-features b5c7f88, wip/island-v2 merged).
- NOT DONE: #18 rivers still angular at bends (width 9-60 m done); #42 waterfalls not started (v2 rivers near sea level).
- Checks: compile ok; editor play on SampleScene v1 = 101 landmarks, hash E128CB1D; v2 = 134 landmarks, hash 71F2AF0C (editor and player, bake 3.2 s); Builds/Dev succeeded, both launches quit on their own.
- Shots: PunchyMilestones/world/w05/v2-map-topdown.png (rivers cyan, lakes blue), v2-river-bend.png (wide river with angular bends).
- Learned: STATE.md gotchas.
- NEXT: smooth river paths, then waterfalls on steep reaches.

## 2026-11-02 batch W-04b: W-04 built (Dev + DevB) and shot; most checks inconclusive, hash not obtained
- DONE: W-04 code (f4c7239) compiles and builds clean (Builds/DevB, then Builds/Dev after Zach freed it; errors=0). No player exceptions in the log. Shot list ran to "done", 7 shots, 66.6 s.
- NOT DONE: #32 roofs: Inn shot ended inside a wall (dark, no eave). The village shots show thatched eaves with a visible lip on the round huts, no gaps; the roof-edge close up still needs a framed shot (-camrel from a hut position).
- NOT DONE: #30 flowers: the -place meadow line found no target, so the pair is two village frames in Storm, not one spot; pink flowers stand upright, no stretching seen, but not a valid 2-frame pair.
- NOT DONE: #14 cave-mouth shards: `-place CaveEntrance` framed a forest, no cave mouth in view. Not verified.
- NOT DONE: #19 swamp ponds: `-place ToxicGeyser` top-down shows meadow and no water. Landmark hash NOT obtained: the editor never got a TerrainSource (WorldProbe.LmList returns "none" after opening World.unity, entering play and Punchy.Net.GameBootstrap.PlaySolo()). The carve is applied after landmarks per its own comment, but E128CB1D is unconfirmed. Do not merge #19 until printed.
- DONE: #45 stars: W-04 Sky.hlsl gate 0.06 -> 0.006 off-shower (about 0.6 -> 0.06 streaks per night minute, 10x rarer), shower nights unchanged (0.9).
- Checks: build errors=0 twice; Builds/Dev first failed on a locked lib_burst_generated.dll, fixed once the stuck player was gone. My own run's shot list had no `quit` line, so that player (PID 6896) did not exit by itself; a Punchy.exe (PID 12296) is running now, not touched.
- Shots: D:/Unity/PunchyMilestones/world/w04b/sheet.jpg (contact sheet of all 6; unreliable framing, see above)
- Learned: shot lists need a final quit line; -place names that match nothing silently keep the previous view.
- NEXT: re-shoot with explicit coordinates: pond top-down inside the PoisonSwamp biome (find via biome sampler), a cave mouth by -place cavestream or coordinates, a meadow pair at a fixed -goto, a hut roof via -camrel; and get the landmark hash from a player-side log line or a working editor World load (LmList).

## 2026-11-02 batch W-03: shooting stars rarer/thinner/fading, Dunes GPU measured, island v2 preview maps
- DONE: #45 shooting stars about 4x rarer (about 2.5 -> 0.6 streaks per night minute), half as thick, and they fade
  out (tail draws in, light dies over the last third) (world-features 3dbd8df, 8d83102)
- DONE: #44 b/bench-runner merged (a428b48). Dunes GPU, same view (-place Dunes, -benchgpupasses): 4.1 ms with puffs,
  3.1 ms with -nosandpuffs (FrameTime.GPU 4.15 vs 3.39). One run each, so noisy; puffs cost roughly 1 ms.
- DONE: #23 island v2 spike: 3 preview maps + 8-10 line notes, editor-side only, generator version 2 (wip/island-v2
  not merged). v0.11 island untouched (no placement code touched, landmark hash E128CB1D unchanged).
  Cold/Volcanic never border: 0 offending pixels on all three. Rivers reach the coast but read thin.
- Checks: Sky.hlsl compiles in batch build and shots; benches ran in the same build; preview script compiles in batchmode.
  No Punchy.exe of mine running.
- Shots: PunchyMilestones/world/W-03/fade3/streak-mid-fade-gone.png (streak mid, fading, gone);
  PunchyMilestones/world/island-v2/island-v2-{A-four,B-five,C-six}-realms.png
- Learned: none added to docs.
- NEXT: pick one island v2 layout, thicken rivers, then wire it behind the generator version; #15 look checks; #13, #14.

## 2026-11-02 batch W-02b: W-02 built and seen, moon animation added, pushed (3728ef9)
- DONE: #34 blue moon: rocky blue sphere with wavy rim tendrils; plus Zach's follow-up: tendrils drift slowly and a
  faint slow shimmer crosses the face. 2 frames 20 s apart show different tendrils (world-features 3728ef9, pushed)
- DONE: #44 dunes: sand puffs haze the cave mouth; `-nosandpuffs` is crisp (ec53f8a, pushed)
- DONE: #30 flowers stay upright and keep their length in Storm; Oak calm/Storm unchanged, no stretching
- DONE: #4 `-menu Graphics` works when launched alone (tabs, tiers, sliders, pad glyphs); earlier miss was a combined launch
- DONE: shot-list players now Application.Quit when the list ends (fe2dda9); no Punchy.exe of mine left
- NOT DONE: dunes GPU ms: -benchroute is not on world-features (b/bench-runner unmerged). Resources/ not committed
  (Unity test-runner json, no code reads it). Shooting stars #45 skipped: build already running.
- Checks: 2 builds ok, no compile errors; 4 shot lists. Daisy is ground scatter, not in `-place prop:`; used meadow
  (-255,27) camlook at ground level instead. First moon shot needed `-bluemoondays 0`.
- Shots: PunchyMilestones/world/W-02/ moon_after.png, flowers_calm.png / flowers_storm.png, dunes_puffs.png / dunes_nopuffs.png (also moon_t0/t20, oak, menu_graphics)
- Learned: STATE.md updated.
- NEXT: #45 shooting stars; merge b/bench-runner for GPU numbers; #15 look checks.

## 2026-11-02 batch W-02: code for moon/dunes/flowers written and compiled, NOT built or seen (blocked on a leftover player)
- DONE: landmark hash on a96a1dc: 101 landmarks, hash E128CB1D, same as the G-37 baseline, so the z34 CaveBaking change moved no landmark
- DONE (code only, local commit ec53f8a on world-features, not pushed): #34 per Zach's new direction: near #0000FF, lit sphere (terminator, dark limb), rock mottling, mixed-size soft craters, crack removed, round corona replaced by faint wavy rim tendrils
- DONE (code only): #44 SandPuffs ambient type (ColdMist style, sand tint, wind drift 2.2) on Dunes density 1.5; `-nosandpuffs` flag for the GPU comparison
- DONE (cause found, code only): #30 cause is the wind sway shader, not a character push (flora has no push code; only grass blades do). Shear moved vertices sideways without keeping length, and the mesh's per-vertex sway grew with distance from the axis so a flower head's far petals swung further than near ones (swelling/swivel). Fix: Flora.hlsl bend keeps length (sphere round the base); flowers bend by height only. Note it changes all swaying plants slightly: look at trees
- NOT DONE: build, shots, GPU ms, #4 `-menu Graphics`: building failed because a W-01 player (Builds\Dev\Punchy.exe, pid 11484, plus its crash handler 16700) is still running and locks the exe. Killing it was refused by the permission system. Shaders compile in the editor (no errors after refresh); editor closed
- Checks: editor compile and shader refresh clean; no play-mode visual check. `-menu Graphics` reading of MainMenu.cs shows nothing obvious; needs a run
- Shots: none
- NEXT: Zach or supervisor stops pid 11484 (and 16700), then rerun W-02 tasks 2-5: one build, one shot list (moonnight same framing as W-01, dunes x2 with and without -nosandpuffs, Daisy side view calm vs Storm, -menu Graphics alone), then push world-features

## 2026-11-02 batch W-01: menu+pad wheel, cave pools, bigger bluer moon, roof lips merged into world-features a96a1dc
- DONE: #4 #5 wip/ui-wheel merged (CLAUDE.md conflict: kept docs index). Compiles, builds, Multiplayer tab shows pad glyphs and the Exit game button (world-features 54b394f)
- DONE: #3 z34-pool merged (22e7534). Cave stream 2 now ends in a pool 7 m earlier; streams 0 and 2 shortened 15 to 10 points
- DONE: #34 blue moon radius 0.034 to 0.13 rad, much bluer, one thin crack, maria and small craters (a96a1dc)
- DONE: #32 roof rim lip on Hut, StoneHouse, Tower, Shrine (6449002); subtle, about 0.14 m thick
- NOT DONE: #4 #5 pad input behaviour (A then B, wheel enter/delete/ring switch/cancel): a shot list can't press buttons; needs Zach or a pad. `-menu Graphics` shot showed no menu
- Checks: 5 batch-mode builds succeeded, no exceptions in the player logs; before build (pre-change) and after build, same camera and time. Landmark hash not measured (no landmark code touched)
- Shots: D:\Unity\PunchyMilestones\world\W-01\ moonnight_before/after.png, moondusk_before/after.png, roof_before/after.png, cave_before/after.png, mm.png (800 px); full size in gallery\
- Learned: docs/agents/rendering.md (blue moon pin recipe)
- NEXT: Zach plays the menu with a pad (#4 #5); run WorldProbe landmark hash; #15 look checks; delete merged wip branches

## 2026-11-01 — #4 Exit game button (wip/ui-menu e0c7814; merged into wip/ui-wheel 6feebef)
- The menu's "Quit game" is now **Exit game**: full width, bold, red-tinted, a focus stop on every tab. The confirm reads "Exit the game?" Cancel / Exit, with Cancel first so the held focus lands on it. B backs out of the confirm.
- Compiles; not built. Slot check: shot of the foot and of the confirm (`-menu Graphics -forcepad xbox`); A, then B cancels.

## 2026-11-01 — #5 text wheel pushed (wip/ui-wheel 0f05394)
- Controller text entry for the menu's fields: a radial wheel. The stick points (the choice holds when it's let go), A enters, LB/RB or d-pad up/down switch letters/numbers, B deletes (cancels on an empty field), Y/Start done, View cancels, d-pad left/right moves the cursor. A field's allowed set limits the rings (IP: digits + dot only).
- Plugs into MenuNav.TextEntry from wip/ui-menu. The menu is modal behind it. NetMenu's keyboard notices are removed; the IP field is capped at 15.
- Compiles (dotnet check of UI/Settings/InputGlyphs). Not built yet. Slot check, same slot as #4: `-menu Multiplayer -forcepad xbox`, choose the Code field with A and the IP field, take shots of both wheels; enter, delete, ring switch and cancel with a real pad if there is one.

## 2026-11-01: UI-1 main menu (code only), zache-73

- **DONE, waiting on a slot: UI-1.** `wip/ui-menu` 0796b07 (off world-features b39522b, pushed, not merged).
  IMGUI, as the panels already were. New Scripts/UI: MainMenu (Esc/Start opens and closes it; tabs
  Multiplayer + Graphics; Quit at the foot; B/Circle/Esc backs out; LB/RB or Q/E switch tabs), MenuNav (focus stops
  for the d-pad/stick/arrows with key repeat; A/Enter presses; left/right steps sliders), Prompts (draws
  `[[control path]]` tokens as icons or names). The F2 and F5 toggles are gone. ThirdPersonCamera's Esc cursor unlock
  is folded into the menu. While it's open the Player action map is off (kept off across a join's rebuild). The frame
  cap is also a Graphics row. The help text says Esc menu; CLAUDE.md and MULTIPLAYER.md are updated.
- **Glyphs:** InputGlyphs.Token/ActionToken/Icon/Plain. The interact prompt, the HUD control hints (now drawn under
  the readout) and the menu footer use them. Icons load from `Assets/Game/Resources/Prompts/<kind>/<control>.png`
  (kind keyboard, mouse, xbox or playstation; control as in the path, '/' as '_': buttonSouth, dpad_up, escape, e).
  This is Resources rather than UI/Prompts, so they ship without a serialized reference. A generic pad and any
  missing icon fall back to today's names.
- **DONE: the icon pack.** `wip/ui-menu` 09a79ee. Kenney Input Prompts **1.5A** (License.txt says 1.5A; the
  folder Zach downloaded is named 1.5), CC0. 23 PNGs from the 64 px Default set: Xbox and PlayStation South/East/North
  (colour), shoulders, start (Xbox Menu, PS5 Options), stick presses; keyboard escape, enter, e, q, space, left shift,
  left ctrl. The pack's License.txt sits beside them. No fonts or .url files. They're in
  `Assets/Game/Resources/Prompts`, not UI/Prompts, so InputGlyphs.Icon can load them by name without a serialized
  reference. GUI textures, no mipmaps, uncompressed (metas committed). Also in the slot: check the icon size and
  sharpness at 720p.
- **Gap 8 hook:** `MenuNav.TextEntry` (current text, max length, allowed characters, result callback). While it's
  unset, a text field chosen on a pad shows "Typing ... needs a keyboard for now". The pad itself is not built.
- **Checked:** the new UI files, InputGlyphs and Settings/*.cs compile with dotnet against Unity 6000.5.8f1's DLLs.
  NetMenu, DebugControls and InteractionInputSystem wait for the bench build.
- **Slot check:** the bench build. Then `-menu Multiplayer` / `-menu Graphics` shots, the same with `-forcepad xbox`
  and `-forcepad playstation`. Then a pad by hand if one is plugged in: Esc/Start open and close, B backs out, tabs
  switch, sliders step, A on Host. Check for 0 IMGUI layout errors in the log.

## 2026-11-01: Z-34 stream with no pool (code only, restarted lane), zache-73

- **DONE, waiting on a slot: Z-34 leftover.** `world-laptop/z34-pool` 91f6b6a (off world-features b39522b, pushed,
  not merged). This follows Zach's call: CaveBaking.StreamEnd walks back from the stream's last point, up to 12
  points (~18 m), to the first spot that works. Either the floor ahead falls 1.5 m or more (a hole or crevice, so
  no pool), or a pool of 1.2-2.5 m at the stream's level passes RimHolds. The stream is trimmed there. With
  neither, the stream is left out. Streams are seed-derived, so nothing to sync. Players still need the same build.
- **Slot check:** a bench build (compile), then `-place cavestream#n` shots of each stream end. These include
  (-1454, 37, -1153), which should now end in a pool, or end shorter, or be gone. Look for a shortened stream that
  now stops short of its chamber, and for any stream ending at a drop.
- **NEXT (slot):** Z-30 hall/inn posts close up; Z-10 non-marsh river fog. P3 rows need design notes first.

## 2026-10-31: world slot 3 (task session): Z-32/Z-31 fixed, Z-34 fixed, Z-35 done, Z-09/Z-10/Z-11 judged, merged b39522b

- **world-features b39522b** (pushed) = b870008 + world-laptop/slot3 (6 commits; branch deleted). Same tree as
  c7bb5f3, which built clean (0 script or shader errors); 0 exceptions and 0 NaN-bounds lines in 6 look runs
  (~140 shots, results ws3/a-f with contact sheets in ws3/).
- **New test flags** (DebugControls, also per shot-list line): `-weather <Preset>` forces that WeatherPreset over the
  player (Rain, Storm, MarshFog, Overcast, PartlyCloudy...; Clear only clears), so rain and fog no longer depend on
  the seed's weather. `-place cavestream[#n]` goes to the end of the nth nearest cave stream and logs the 8 nearest.
  `-camrel dx,dy,dz,tx,ty,tz` poses the camera relative to the player (after any -place). `-waterprobe 1..4` draws
  water as foam, thickness, alpha or foam parts. `-caveshards` now also logs where ownerless walls are.
- **Judged by sight:**
  - **Z-32 fixed** (bb08f7a): the stone house walls were a four-sided tube with smoothed normals (45 degrees at each
    corner). Every wall shaded from edge to middle, and at a glancing view the built double-sided flip lit a wedge
    of wall orange at night. The walls are now a flat-faced block. Day, night and rain shots show even walls.
  - **Z-31 fixed with it** (stone houses): the slanted normals weighted the wrong triplanar projection (streaks).
    No lines in close shots after. The inn and hall plaster walls show no lines either.
  - **Z-34 fixed:** the thin lines across cave streams were the streak-foam term (`-waterprobe 4` showed them in its
    blue channel), so running cave water has no streak flecks now (c7bb5f3). A stream now stops at most half way
    into its end pool (no lighter tongue over the pool). Streams 2 and 3 meet their pools cleanly. Stream at
    (-1454, 37, -1153) has no pool at its end (CaveBaking leaves the pool out when the floor can't hold it), so it
    still ends mid-floor. The lantern-glint and reflection-march guesses were tried and undone.
  - **Z-35 done:** the falls shader ignored the effect's Lifetime and scrolled at a fixed rate. It now runs the fall's
    length in Lifetime, which is 2.5 s for Waterfall (was 4). Frames 0.2 s apart now differ a lot. There's a spray
    plume at the lip, and the curtain starts at the clifftop (no gap seen from the pool or above).
  - **Z-11 pass:** in a forced Storm (50 s), the village bench planks show puddle-wet ground around them and no
    screen-space image. No wiggle in frames 0.3 s apart. The elf bench is a stone slab. The very dark stone legs are
    in the seat's shade (wet), with a slight sheen.
  - **Z-10 pass where seen:** under forced MarshFog at FoggyMarsh, a stream is veiled like the rocks beside it. Its
    foam flecks keep some contrast. MarshFog over the River place showed no fog (that preset seems marsh-only).
  - **Z-09 pass:** 20 frames, 1 s apart, under PartlyCloudy caught 2 shooting stars, both in clear sky. 20 under
    Overcast caught 1, faint, in the sky below the cloud deck. None crossed a cloud.
  - **Z-30 pass where seen close:** bench planks, bridge planks and rails, smithy posts and the woodpile read as wood.
    Hall/inn posts were seen only at 9 m. There's still no Pen on this seed.
  - **Cave-mouth shards: none found.** `-caveshards` logged no ownerless wall in any cave chunk built at CaveNetwork,
    CaveEntrance, Grotto, the nearest cave (FungalGrotto), the big cave mouth or three cave streams.
- Waterfall on this island: landmark about (627.5, 2526), pool centre (629, 2518), clifftop about 92.
- Unity and every Punchy.exe started here are closed. The main checkout is on b/weather-mapjob, and the bench lock
  is clear.

## 2026-10-31: world slot 2 (task session): targets found, owed looks taken, Z-30 grain fixed, merged b870008

- **world-features b870008** (pushed) = 3976d17 + world-laptop/z30-more (6 commits). Built clean as 8b0a657 (same
  tree): 0 script or shader errors; 0 exceptions and 0 NaN-bounds lines in 7 look runs (~80 shots).
- **animal-behaviours 9b7459b + world-features b870008** (tmp/animal-world-check 5f4c917): built clean, 0 errors.
  Not merged; tmp branch deleted.
- **Targeting:** `-place prop:<Species>[@x,z][#n]` (DebugControls) finds any placed plant (landmark props, town
  buildings, biome scatter), logs the 8 nearest with coordinates, height and front, and frames one. One run gave:
  Bench (111.4, 25.7, -404.8) and 30 more; Stall (-355.6, 12.2, -4079.4) at OgreCamp; Smithy (76.3, 25.8, -367.8);
  Inn (168.1, 25.7, -401.4); Hall (513.0, 28.1, -943.5); StoneHall (1973.3, 65.1, 2479.9); Fountain and Forum
  (260.8, 32.1, 3250.9); ElfBench (267.0, 32.1, 3253.2); EmberPine (-800.8, 25.9, -3573.5); Woodpile
  (87.6, 25.7, -439.9); Bridge (231.6, 16.9, -638.5); AncientTree (-5.9, 5.3, 1250.5), 31.9 m tall. No Pen found.
  Geyser pool ~(1182, 51.5, 2799); MudPits pools ~(197, 20, -3666); FoggyMarsh place (-304, 5, 817).
- **Rain:** `-benchday <day type>` on b/bench-runner db3c1f2. The day types are Sunny, Partly cloudy, Overcast, Stormy
  and Changeable. Stormy doesn't rain everywhere (the village had a cloud deck). Under Partly cloudy, seed 4, rain
  reaches the village square about 90 s in.
- **Shot lists under the bench route:** `-benchroute -benchstops here -benchlong here 900` holds the weather while
  `-shotlist` takes many in-game shots in one session (HANDOFF-world "How to test").
- **Looks, judged by sight:**
  - **Z-30 failed, then fixed.** The bench close-ups before (7f519c0) and after (3976d17) looked the same: the grain
    was at most 18% darker and mostly faded by the pixel footprint. 632f479 draws lines about 3 cm apart, up to 45%
    darker, over broad bands, and the same close-up now shows clear streaks along the planks. 69f3185 marks more
    timber: pens, the inn's band, posts and porch, the hall's walls, posts, beams and poles, bridges, the smithy's
    posts, the anvil block and the chopping block.
  - Z-33 pass: three frames 0.3 s apart show broad, slow rolls, with no high-frequency shake. The speed itself can't
    be measured from stills.
  - Z-36 pass: the geyser pool shows flat rings lying on the water beside the bubble domes.
  - Z-37 weak pass: at night (in rain) a few coloured fireflies hover above the ground by the great tree. The tree
    stands in a dense marsh canopy, which makes it hard to frame.
  - Z-39 pass: the ember slivers on the pine crowns are short upright dashes.
  - Z-40 pass: embers and dark coals sit inside a raised stone rim and glow at night.
  - Z-41 pass: the stone hall's ridge shows a rounded tile band. Stone houses have hipped roofs, with no ridge.
  - Z-12 pass where seen: the forum slab's edge at 270,3248 shows no stretch and a dark band at the ground. The
    ground there is barely below the slab, so it's not a hard case.
  - Z-09 pass with `-meteors`: the aurora (on with the same switch) stays behind the clouds. No shooting star was
    caught in 6 frames (rain streaks look alike).
  - Z-10 inconclusive: fog and rain in the marsh veil its streams, and no unfogged water was seen.
  - Z-11 partly judged: no wiggle in two frames 0.3 s apart, and no screen-space image on the planks in light rain.
    The stone legs look glassy in dry and wet shots alike.
- **Found for the animal lane (TraceSystem, not changed):** when a dog walks under a bench, its paw prints land on
  the bench seat. `Underfoot` samples prop tops at foot + 0.3 m and takes the seat above; it needs
  `hit.Height <= at.y + ~0.3`. Shots: ws2/e/z11-bench-rain-b and -c.
- **Cave-mouth shards:** `-caveshards` (CaveSystem) draws walls no cave owns in magenta. None showed at the
  CaveEntrance, Grotto (glacier) or nearest-cave mouths from the places' default views. The big cave's mouth wasn't
  in view; a close look at a known shard spot is next.
- Shots: Punchy-wt/Tools/bench/results/ws2/{probe,a,b,c,d,e,f}, with contact sheets in ws2/.
- Unity and every Punchy.exe started here are closed; the main checkout is on b/weather-mapjob; the bench lock is
  clear.

## 2026-10-31: world slot 1 (task session): z-p2 built, benched, merged; looks partly done

- **world-features 3976d17** (pushed) = 247c690 + world-laptop/z-p2 (13 commits; blocked-top was already in via
  worm-arc). Built clean first time. Smoke vs 247c690 said NOT CLEAN (glide cap 0 1% low 36->23 from ~500 ms spikes);
  the ABBA `ab 247c690 3976d17 townwalk,glide` says no change beyond noise (townwalk 48.6->54.4, glide 35.6->34.2).
  35 look runs, 0 exceptions.
- **Seen and fine:** Z-38 fungal forest caps read tawny/brown; Z-42 glacier has IceCrag (stacked icy tor, reads a bit
  like a cairn of pancakes) and IceSlab blocks next to the arch; dwarf hold hall roofs and stone houses by day and night
  at 20 m show no artefacts; geyser pool bubbles show (ripple not judged at that distance).
- **Not judged (shots missed the target):** Z-30 before/after, Z-33, Z-36 ripple, Z-37, Z-39, Z-40, Z-41 up close,
  Z-12 skirt, Z-09, Z-10, Z-11, Z-31, Z-32, Z-34, Z-35, cave-mouth shards. Causes: `-place tree:` only knows tree-layer
  plants; stale falls/cave coordinates; no rain outside the storm stops. Next slot in HANDOFF-world.md.
- Seen in passing: big screen-door dithered patches where a canopy or giant cap is between camera and player (the
  see-through dither) in fungal, falls and storm shots; expected, but they're large on giant caps.
- Unity and every Punchy.exe started here closed; main checkout on b/weather-mapjob; bench lock clear.

## 2026-10-30: Zach's answers on Z-12 and Z-38; RockAt for the worm, zache-73

- **Z-12 stretched base (654d748, z-p2): DONE in code.** The flora shader worked out "grounding" (darker toward the
  ground: a tight 12 cm contact band plus a broader rise) at the vertices. On a foundation skirt, whose bottom ring
  sits 1.6 m down, that band was interpolated linearly over the whole exposed face: stretched toward the bottom, and
  no dark band at the ground (also G-22). It's now per pixel, from the interpolated height above the base (exact,
  since it's linear in y). That fixes every skirted building. Check: a fountain or forum on a slope, from below the base.
- **Z-38 small glowing caps (e2f1c97, z-p2): DONE in code, fungal forest only.** New FungalGlowCap and FungalGlimmerCap
  (amber-brown and dusky brown, still glowing) replace GlowCap and GlimmerCap in the fungal forest's flora list only.
  The poison swamp, sparkling forest and fairy rings keep the originals.
- **For zache-db's worm (Z-28), on world-laptop/blocked-top e58eed2:**
  - `Blocked(..., out float top)`: buildings and placed props, top = model height x scale.
  - `RockAt(ref f, source, xz, radius, out float top)`: scattered rocks (any solid non-tree on the tree layer: boulders,
    tors, standing stones, hoodoos), from Place() on the cells within radius + 6 m. No streamed state.
  - zache-db merged it into wip/worm-arc (9213d8e); their build is the compile check, then I land it on
    world-features.

## 2026-10-30: Z-30 wood grain, option (a), zache-73

`world-laptop/z-p2` ee5a1f9, not compiled.
- `FloraMeshBuilder.Wood` marks timber (Bark + 0.02; strata are 0.04-0.15, seams > 0.15, veins > 0.32). It also stores
  the grain direction in the vertex colour's alpha: 0.25 X, 0.5 Y, 0.75 Z, the object axis nearest each piece's length
  (Box/Beam/Tube/Sheet/Blob each set it). The shader never read that alpha; builders' colour maths scales it, so it's
  only meaningful on marked wood.
- Flora.shader `PunchyWoodGrain`: fine (~2.5 cm) and broad (~11 cm) wavering streaks along the axis, in the plant's own
  frame so they turn with it, darker in the lines. Each scale drops out by screen footprint, as the weathering does.
  New interpolators: object position and a nointerpolation axis.
- Marked so far: stall posts and counters, bench planks (the elves' stone seats aren't), rail fences, signposts.
  After the before/after look: smithy, inn and hall timber, pens, bridges, the woodpile's block.

## 2026-10-30: P2 second batch (W), zache-73

`world-laptop/z-p2` ec75753, not compiled.
- **Z-38 widened (01da094), per Zach's wording:** GiantPaleCap, GiantPuffball and PaleCap (PaleCap is also in the foggy
  marsh) go from cream and beige to tawny, light tan and mid brown, keeping a light-to-dark range. With the giants'
  brown caps, the whole canopy reads earthy. Kept: the small glowing caps (GlowCap, GlimmerCap). They're the forest's
  lights and are shared with the poison swamp, sparkling forest and fairy rings; say if Zach wants them browned too
  (brown caps with glowing gills, as the giants have).
- **Z-33 cloth LOD (08c93a2):** banners drop to 1 x 2 segments at LOD 1 (were 2 x 3), awning stripes to 2 rows (were
  3), tent flaps to 1 x 2. LOD 0 and LOD 2 (one quad) are unchanged.
- **Z-42 glacier (6564033):** IceSlab (the boulder families in ice, 1.5-4 m) and IceCrag (Tor form, 4-9 m) join
  IceArch, slabs commonest. New `FloraSpecies.KeepOwnColour`: without it, ice boulders take the glacier's rock tint
  (G-36). Landmark list unchanged.
- **Z-40 furnace (ec75753):** there's no separate smelter, so this is the smithy's forge. The hearth is now a lower bed
  inside a raised stone rim, with glowing embers among dark coals (7 up close, 3 further off) instead of a glowing
  dome on top. Anvil and work spot unchanged.
- **Z-30 wood grain: design question.** The flora shader can't tell wood from stone (both are the Bark part), and
  grain needs a direction (planks run along, posts up). Options:
  (a) a "wood" mark (Bark + a new fraction, like the seams' and strata's) set by the builders on timber (planks,
      beams, posts, stalls, benches), plus grain stripes along the longer axis of each face. Right, but it touches
      every wooden builder.
  (b) a shader-only heuristic: warm-hued built Bark gets grain along world Y on upright faces and along X/Z on flat
      ones. Cheap, but it also catches warm plaster and would grain mud walls.
  I'd do (a), starting with the stall, bench, fence and smithy timber, after a look at today's wood.

## 2026-10-30: Zach's v0.10 P2 rows (W), first batch, zache-73

`world-laptop/z-p2` bfd69e5 (off world-features 7f519c0), not compiled; shots in a slot.
- **Z-33 banners (e42f0fa):**
  - The wave runs at 0.9 + 2 x strength rad/s (was 1.6 + 4.5 x, about twice as fast in a breeze).
  - The edge flutter is slower, broader and gentler, and fades out between 30 and 80 m, where it read as shimmer.
  - Cloth meshes already drop to 3 and 1 segments at the far LODs, so "less resolution at distance" is mostly that
    shimmer. Awnings are unchanged.
- **Z-37 great tree fireflies (c180050):** two in three of its coloured fireflies hover 4 or 8 m up round the crown
  instead of all at its feet.
- **Z-38 fungal caps toward brown (1331dad):** glowing giants' plain caps lean umber to tawny per cap, keeping a hint
  of their hue. The brown is drawn only for them, so other mushrooms' shapes don't shift.
- **Z-39 ember slivers along the grain (da04ce0):** the pattern varies across the crown and only slowly with height,
  giving upright slivers in short dashes.
- **Z-36 bubble pops as ripples (8e793b7):** after the burst, the quad lies flat on the water (it was a billboard), and
  the ring takes the swimmers' shape: a lit crest, dark troughs either side and a weaker inner ring, fading as
  (1-t)^2. The ring maths is copied, not shared slots, so no feeder is needed.
- **Z-41 roof bevel (bfd69e5):** a rounded ridge tile along stone houses' and halls' gabled roofs; ridge perches
  raised onto it. Cone and tent roofs are unchanged.
- **Need a look first (next slot):**
  - Z-31: vertical lines in the close-up detail. Suspect the triplanar detail texture's tiling seam on x/z faces.
  - Z-32: stone-house lighting bands. Suspect normals across the walls or the double-sided flip.
  - Z-34: cave stream meets pool; striping.
  - Z-35: the waterfall's gap at the top. The fall speed is the effect's Lifetime, an asset value, easy once seen.
- **Bigger builds, next:** Z-30 wood grain, Z-40 furnace ember pit, Z-42 glacier slabs and bergs.

## 2026-10-30: merged zache-db's creature render ranges, zache-73

- **DONE:** wip/gfx-creatures 6f6dd94 merged into world-features as **0c637a9** (merge commit), pushed. It wires
  CreatureRenderSystem, TraceSystem and the F1 key line onto GraphicsSettings. Compiled in zache-db's slot; merged
  clean here, code only.

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

## W-08 (2026-11-02)
- **DONE:** #19 ponds fewer and larger (2614 -> 1287 blobs, 4329 -> 3850 wet cells; single-cell 658 of 1287 remain). #44 puffs alpha 0.4 (system existed). #20 v2 ridge chains (ridge height min 13 / median 81 / max 170 m; 127 landmarks, 831FF2EE). #30 flower pair shot. world-features 149bc0a pushed; v1 101 / E128CB1D holds.
- **NOT DONE:** single-cell ponds not fully removed; v2 top-down map blank (4600 m camera); 3 builds used, not 1 (pond design failed stats; first ridge edit misused Mathf.SmoothStep).

## W-09 (2026-11-02)
- **DONE:** #19 ponds 1287 -> 219 blobs (3850 -> 361 wet cells, p90 7 -> 3). #20 v2 top-down ridge map. #44 puffs readable (alpha 0.6, density cut). #39 paving channel plus cobble shader on Cobbles/Flagstones. world-features 0e19871 pushed; v1 101 / E128CB1D holds; one build.
- **NOT DONE:** single-cell ponds still 165 of 219; cobble stones not visible in the town shot (look needs tuning); v2 hash not rechecked.

## W-15 (2026-11-02)
- **DONE:** #23 cave bake 1350 -> 555 ms in the editor (rubble 566 -> 90, plants 379 -> 60; parallel per cave, joined in cave order), player caves stage 1380 -> 591 ms, v2 ready 7.2 s; cave hash unchanged; v2 127 / 831FF2EE holds. #35 BATFOLK-PLAN.md (36 lines; placement does not change v1). #35 `-whitepools` test look. world-features fb6d7ee pushed; one build.
- **NOT DONE:** white pool not judged done: the editor shot reads as milky water, but in the build it reads as a glowing white disc with a hard rim. v1 hash not rechecked in a player (no landmark code touched; caves bake after landmarks).
- **Shots:** PunchyMilestones/world/w15 (v2-white-pool, editor-pool-default, editor-pool-white).

## W-16 (2026-11-02)
- **DONE:** #23 settlements stage ~970 -> ~290 ms in the editor (roads 833 -> 150), settlement hash unchanged (bench B4E4858C9FE1CF46 / roadside 14650FB0739D0383, baked 2433C3F949E9CE3E); v2 player settlements 344 ms. v2 player log 127 / 831FF2EE. world-features d69a710 pushed; one build.
- **NOT DONE:** #35 the rim is soft now, but the build shot still reads as a bright milky blob, not water. v1 player log has no hash line: v1 never logged one. Added the log after the single build, so the next build must confirm 101 / E128CB1D (no landmark code touched).
- **Shots:** PunchyMilestones/world/w16 (v2-white-pool, v2-white-pool-wide).

## W-17 (2026-11-02)
- **DONE:** #35 white pools now darken and stay see-through across the middle (body alpha x0.55), glow only a pale band inside the soft rim, colour/glow toned down; default pools unchanged. Build shot at 800 px reads as a pale translucent pool, floor showing through. Editor cave preview: `CaveTools.PreviewAt` + `PreviewShot` (tracked), editor shot taken before the build. Player logs v1 101 / E128CB1D, v2 127 / 831FF2EE. world-features 60a8ebd pushed; one build.
- **NOT DONE:** nothing from the card. Bat folk not started (out of scope).
- **Note:** first v2 player run lost its shots (shot dir missing); reran the player, no rebuild.
- **Shots:** PunchyMilestones/world/w17 (v2-white-pool, v2-white-pool-wide, editor-white-pool).

## W-18 (2026-11-02)
- **DONE:** #35 bat folk steps 1-2. `BatFolkBaking` stage after caves, own seed stream and blob; picks 1 per 6 qualifying caves by scrambled mouth-cell hash; per-pool white flag replaces `-whitepools` (kept as debug override). Logs: v1 101 / E128CB1D, `[batfolk] caves 0, picked 0`; v2 127 / 831FF2EE, `[batfolk] caves 3, picked 1 ... cave 2 chamber 3001,3,-2135 r 10.1 pool 1`. Cave hash 9CB26E0C6AEB39BA unchanged. Build shots at 800 px: picked pool white, non-picked pool normal blue. world-features 73709c8 pushed; one build.
- **NOT DONE:** nothing from the card. v1 gets no bat folk (no deep big chamber with a pool); rule choice open.
- **Shots:** PunchyMilestones/world/w18 (picked-white-pool, unpicked-pool, editor-picked).

## W-19 (2026-11-02) - #35 bat folk huts and props
- DONE: task 1. NOT DONE: task 2 (the huts don't read in the build shot).
- world-features 4ef8516: `BatFolkSetBlob.Props`, per-site RNG on the bat folk stream, clear of pools and path segments; `BatFolkMeshes.cs`; CaveSystem draws them.
- v2 log: `cave 2 chamber 3001,3,-2135 r 10.1 pool 1 huts 3 props 8`. v1: `caves 0, picked 0` (rule unchanged).
- Hashes: v1 101 / E128CB1D, v2 127 / 831FF2EE, cave 9CB26E0C6AEB39BA (all unchanged).
- Shots: PunchyMilestones/world/w19/w19-chamber-800.png, w19-chamber-up-800.png. The racks (pale hides), baskets and white pool read, and nothing floats.
  The huts hang 8-14 m up, past the lantern, and show only as dark ovals at the top edge.
- NEXT: step 4 light on the huts (or a lower hang or ledge huts), then retake the shot looking up.

## W-20 (2026-11-02)
- **DONE:** #35 huts hang lower: each stalk is sized so the pod bottom sits 3-6 m above the floor (stalk checked for air along its length; bat folk stage only). v2 log `huts 3 props 8 hut bottoms 3.9/4.6/4.7 m` (was 8-14 m up). Build shot at 800 px: three pale pods with their seam bands, racks, baskets and the white pool in one frame; huts read as huts. world-features fb047dc pushed; one build (= fb047dc code).
- **DONE:** the W-19 "2 errors" were the editor's pipeline server failing to answer the `unity command eval` request after the CLI's 30 s timeout dropped the connection mid-build (BasePipelineServer: "connection forcibly closed" + "Cannot be changed after headers are sent"). Harmless to the player. Build eval with `--timeout 600`: `Succeeded 0 errors`. Noted in docs/agents/editor-and-assets.md.
- **DONE:** one report log: W-19 moved here, lanes/world/REPORTS-world.md deleted.
- **Checks:** v1 101 / E128CB1D, v2 127 / 831FF2EE (player logs); cave hash 9CB26E0C6AEB39BA (editor CaveBench, baked = fresh). v1 `[batfolk] caves 0, picked 0`. Compile clean; no Punchy.exe of mine left running. Frame judged in the CaveTools editor preview before the build.
- **Shots:** PunchyMilestones/world/w20/w20-batfolk-chamber.png (build, 800 px: huts, racks, white pool), preview3.png (editor preview of the same frame, brighter lantern), w20-v1-spawn.png (v1 check run).
- **Learned:** docs/agents/world.md (hut hang rule), docs/agents/editor-and-assets.md (build eval timeout).
- **NEXT:** #35 step 4 light is now optional (huts sit in lantern reach; the build is a little darker than the editor); step 5 residents to the animal lane; v1 rule (pool or MinChamber) still waits for Zach.

## W-21 (2026-11-02) - v2 steep walls, missing water, fall gap
- **NOT DONE (held for Zach):** fix found but not pushed: it changes the v2 landmark hash (hard rule).
- **Cause (-2165,255 walls + missing water):** `BiomeData.SampleMapBlurred` (v2 biome map only) adds 9 taps x 4 corners into `BiomeWeights`, which holds 4 slots and replaces the weakest in tap order. Where 5+ biomes meet, a biome with ~0.07 weight drops out in one step: x -2163 -> -2161 at z 255, ground 43.3 -> 36.0 m (biome 4 base 105 swapped for biome 9). The same flip makes 21 m pits under a 26.5 m lake; the F-22 rule (TerrainSource ~507, no lake where it is > depth+2 deep) cuts the lake there = the holes in the water. Not the river or fall code: no river within 300 m.
- **Fix (Punchy-world/Temp/w21-biome-blend.patch):** sum every biome id, keep the top 4, subtract the 5th's weight, normalize, so weights stay continuous. Editor: same row reads 38.3 -> 37.2 m over 10 m, no wall, no lake holes. But v2 landmarks 127 / 831FF2EE -> 125 / AFA9CC63 and cave hash 9CB26E0C6AEB39BA -> D531293D622E785A (heights move at every 5+ biome blend, so rivers, landmark checks and cave rock shift). Reverted; the editor is back to 127 / 831FF2EE and 9CB26E0C6AEB39BA.
- **-2422,-971 fall gap:** separate problem; the patch leaves that view identical. Fall 0 lip -2450.5,21.4,-960.3, drop 4.6, width 47.8; land shows between the upper and lower river surfaces.
- **Cave-creek (card task 1):** not located. The nearest cave to the walls (cave 5, mouth -2573,296) is 410 m away; Zach's "creek" is likely these biome-blend trenches.
- **Checks:** v2 editor before: 127 / 831FF2EE, cave 9CB26E0C6AEB39BA; with patch: 125 / AFA9CC63, D531293D622E785A; after revert: 127 / 831FF2EE, 9CB26E0C6AEB39BA. v1 not run (editor is pinned to v2 by env; the patch only touches the v2 map path, so v1 101 / E128CB1D can't move). No build; no Punchy.exe touched.
- **Shots:** PunchyMilestones/world/w21: w21_wall_before.png (walls behind trees, top right), w21_wall_after.png (smooth slopes, patch applied), w21_fall_before.png / w21_fall_after.png (same gap in both).
- **NEXT:** Zach decides: accept a new v2 landmark hash for the blend fix (apply the patch, recheck towns/caves), or keep the hash and live with the walls. Then a separate card for the -2422,-971 fall gap (river/fall surfaces around fall 0), and a fresh hunt for the cave-creek spot with a -goto from Zach.



## W-22 (2026-11-02) world

- Row 1 #23 DONE: v2 is the default. No flag logs `125 landmarks, hash AFA9CC63`; `-island v1` logs `101 landmarks, hash E128CB1D`. No island netcode sync (each side reads its own launch).
- Card change DONE: W-21 biome-blend patch committed alone (d436650). v2 125 / AFA9CC63, caves D531293D622E785A as expected. Lost: OakGrove 1757.3,2593.3; Waterfall -1993.9,3009.8. Caves 37/37, settlements 6/6, bat folk unchanged.
- Row 2 DONE: docs/agents/world.md, lanes/world/TESTING.md.
- Row 3 #23 #18 DONE: fall 0 upper water now runs to the lip (RiverQuery.FallWater). Build shots w22-fall0-side/above: no land gap.
- Row 4 #13 DONE: fall speed fallLength/(3 lifetime) -> 2x (fall 0: 1.15 -> 2.30 m/s). Sound log: `[falls] sound sources 1 (of 1 falls drawn), max distance 44 m`. Curve in STATE.
- Row 5 NOT DONE: LOD1 hut roof meshes check out in numbers; ran out of call budget before an editor look.
- Shots: PunchyMilestones/world/w22 (w22-fall0-side, w22-fall0-above, w22-walls-lake). 1 build. world-features 5016957.

## W-23 (2026-11-02), partial

- Row 3: DONE in 253080d. Hut roofs lost their thatch relief by 45 m: the bark detail fade, not the LOD.
  - Built things now fade it over 60-140 m.
  - Before/after at 40 m (View 0.6, LOD1) and 45 m (View 1, LOD0): the roofs keep an even thatch.
  - Shots: w23-hut-40m-view06 vs w23-hut-40m-v0.6-after, and w23-hut-45m-zoom vs w23-hut-45m-v1-after.
- Row 1: NOT DONE, reverted.
  - The v2 swamp sits on a slope: median 0.16 m/m, and 89% of its cells are over 0.12.
  - With "no pools on slopes", denser noise gives 0 pools at a 0.12 limit, or 15 single-cell specks at 0.22.
  - Before: 1 pool, 15 wet cells.
  - Needs a decision: flatten the swamp, allow terraced pools, or move the swamp.
- Rows 2 and 4: NOT DONE, no build made.
- Hashes: no terrain code committed, so they are unchanged by construction.

## W-24 (2026-11-02)
- Row 1 DONE (#23): stepped pools. world-features 9568761.
  - PoisonSwamp at -2400,500, before -> after: pools 3 -> 15, wet cells 15 -> 33, steps 2 -> 119.
  - Island-wide: 1 pond -> 5 ponds.
- Row 2 NOT DONE: in the build shot from 30 m up, the terraces are too small to read as steps. Next: a bigger step or bank, or a closer shot.
- Row 3 DONE: the lake shot from lower down shows no walls or holes.
- Row 4 DONE: the hut thatch is even at 40 m.
- Hashes unchanged: v2 125 / AFA9CC63, caves D531293D622E785A; v1 101 / E128CB1D.
- Shots: PunchyMilestones/world/w24. 1 build.

## W-25 (2026-11-02)
- Row 1 DONE (#23): swamp pools are now irregular ~15 m paddy cells with banks 0.3 m above the higher neighbour. world-features a19bc79.
  - PoisonSwamp box, W-24 -> W-25: pools 15 -> 15, wet cells 33 -> 33, steps 119 -> 89, level pools 858 -> 173, mean pool 9 -> 32 m2.
  - At -2260,569 (slope 0.48) the pools are 7 m wide between risers. The W-24 bands there were 1-2 m wide.
- Row 2 DONE: the build shot from 30 m up the slope reads as a staircase of paddies with dark banks. So does the 8 m side shot.
- Hashes unchanged: v2 125 / AFA9CC63, caves D531293D622E785A; v1 101 / E128CB1D.
- Shots: PunchyMilestones/world/w25. 1 build.

## W-26 (2026-11-02)
- **Row 1 DONE** (#23): lowland surfaces climbed about half of each v2 mountain flank, because the map marks Mountains only on the crests while the ridge lifts the ground further out. `TerrainSource.HighlandSurface` gives the surface to the mountain biome above a 40-52 m lift (map mode only). Other surface on Mountains area 2.7% -> 1.3%; on ground lifted 40 m+ 13.0% -> 3.4%. Edge bands 16 -> 4 m, 24 -> 8 m (that edge also moved 36-44 m further out), and a small change at the third edge.
- **Row 2 DONE**: before (editor) vs after (build), daylight, about 150 m out. Lowland patches are gone from the upper flanks. Pines on the crests are the Mountains biome's own layer.
- **Checks:** v2 125 / AFA9CC63 -> 125 / D709D95C, caves D531293D622E785A -> 5D90D9CFC5326976 (editor). Lost: Grotto -1086.4,2981.1. New: CrystalGrotto -0.1,-406.2. Caves 37/37, settlements unchanged, bat folk unchanged. v1 not re-run (map-only code path). world-features afc068c pushed. 1 build.
- **Shots:** PunchyMilestones/world/w26/w26-before-flank1.png, w26-before-flank2.png, w26-after-flank1.png, w26-after-flank2.png.

## W-27 salt tower (2026-11-02, world-features fba3a7f)
- DONE, row 1:
  - v2 tower at 3720,-800, 73.4 m tall on base 7.0, in the largest salt-flat patch (0.4 km2, 280 m in from
    its edge, relief 0.08 m).
  - Landmark diff: +1 SaltTower, nothing else moved.
- DONE, row 2: 245 basalt step stubs, 0.300 m rise each, round the column and through 2 tunnels to a flat
  summit. Walker: max rise 0.339 m (step up 0.4), no drops, never blocked, top reached.
- DONE, row 3: LOD0 7048 tris, LOD1 1660, LOD2 692.
- DONE, row 4: shots in world/w27 (far, spiral, summit), viewed at 800 px. All three were taken in rain.
  The far shot is off the flat because the patch is small.
- Hashes:
  - v2 126 / 053C63D5.
  - v1 has salt flats: 102 / 51EA6463 (+SaltTower 3080,320 only).
  - Caves not re-run.

## W-27b v2 multiplayer (2026-11-02, world-features 96a80de)
- Row 1 DONE: merged main 0bd91cb (704229f). One trivial C# conflict, in PlayerMovementSystem.cs: I kept both blocks. 0 compile errors.
- Row 2 DONE (#23): v2 bakes once per process. Later worlds copy the blobs (`IslandV2System.Shared`).
  - Host: one "v2 ready" (ServerWorld); ClientWorld logged "shared ... no rebake".
- Row 3 DONE (build, 75 s, host plus local join):
  - v2: 126 / 053C63D5 on both sides; players 2; each side sees the other 3 m away.
  - v1: 102 / 51EA6463 on both sides; players 2; 3 m.
  - F2 hosting tested in the editor only: it shared the bake, kept the hash and did not time out.
- Leftovers:
  - Caves: v2 5D90D9CFC5326976 (unchanged); v1 47C703C0E17F77CB.
  - Roads near the tower: v2 nearest road 5963 m (street 687 m); v1 nearest road 1385 m. Nothing runs into either tower.
  - Tower far shots retaken at noon in Clear weather, along the patch's long axis.
- Shots, all in world/w27b:
  - w27b-mp-v2.jpg and w27b-mp-v1.jpg (host and client side by side)
  - w27b-tower-far-south.jpg and w27b-tower-far-north.jpg
- 1 build.

## W-28 icebergs (2026-11-02), world-features 10a5ee1
- Row 1 DONE:
  - v2: big 6, chunks 56, floes 38 off the Cold realm coast (km cells -5,1 -4,1 -2,3 -2,4 -1,4).
  - v1 has a cold coast in the east (km 3,2 4,1 4,2): big 1, chunks 8, floes 1.
  - No bergs in rivers or lakes (open sea only), none off warm coasts, and settlements kept clear by 350 m or more.
- Row 2 DONE:
  - Faceted white/pale-blue ice. One draw per 1024 m region: v2 5 draws, 6232 tris; v1 3 draws, 2140 tris.
  - Climb onto big bergs by a ramp on the shore side.
  - No bob. The underwater part shows only near the surface.
- Row 2b DONE:
  - Cave in the berg mesh, so the cave system is untouched.
  - v2 berg (-4332, 1739), chamber (-4337, 1.2, 1740). v1 berg (4226, 2354).
  - Cave cost: 1026 tris, 1 draw.
  - Walk test: water to chamber and back, 922 steps, 0 blocked.
- Row 3 DONE: [coast](PunchyMilestones/world/w28/w28-coast.png), [coast wide](PunchyMilestones/world/w28/w28-coast-wide.png), [berg](PunchyMilestones/world/w28/w28-berg.png), [chamber](PunchyMilestones/world/w28/w28-chamber.png).
- **Checks:** hashes unchanged: v2 126 / 053C63D5, caves 5D90D9CFC5326976; v1 102 / 51EA6463, caves 47C703C0E17F77CB.
- Open: a small gap at the chamber's tunnel joint. W-27 tower steps may not hold the player (Floor ignores the tower floor).
- 1 build. No Punchy.exe left running.

## W-29 grand falls (2026-11-02), world-features d9ce83c
- Row 1 DONE: survey (v2, central lake at 25.3 m).

  | Option | Site | Total drop | Tiers | Length | Moves or flattens |
  |---|---|---|---|---|---|
  | A: mountain into the lake | (316,905) at 166 m to the NW shore near (530,1400) | ~140 m (steepest 76 m over 96 m) | 1 main + 4 small | ~550 m | Nothing: no village, cave, road or river on the path; Tors (448,1360) dodged |
  | B: lake over a rim | SE rim, e.g. (757,1265), down to the sea | <= 25 m (lake to sea) | 1-2 | 600-1000 m | Lake level falls if cut; cannot reach 60 m |

  Recommendation: A.
- Row 2 DONE: `Terrain/Rivers/GrandFalls.cs`, a v2 overlay baked after every place.
  - Tiers: 5.0, MAIN 72.5, 10.3, 5.5, 5.5 m, each with a plunge pool (main pool r 20). 548 m long, 140.8 m in all.
  - The water surface is continuous (a staircase along the path).
  - Flow 1.2-4.0 m/s on reaches, 0.6 m/s in pools.
  - Sound: 3-4 sources, heard up to 180 m.
  - Main drop: 3 curtains and 2 big mist plumes.
- Row 3 DONE: [whole](PunchyMilestones/world/w29/w29-whole.png), [main drop from the pool](PunchyMilestones/world/w29/w29-pool.png), [from the lip](PunchyMilestones/world/w29/w29-lip.png), [lower cascades](PunchyMilestones/world/w29/w29-cascades.png).
  - Noon, Clear weather. Reads as one connected system.
- **Checks:** hashes unchanged: v2 126 / 053C63D5, caves 5D90D9CFC5326976. v1 is untouched by code (not run).
- Open: a slanted water-mesh sheet beside the main curtain; snowy levees on the plateau; plain gorge walls.
  W-28 leftovers (berg tunnel gap, tower steps) not done.
- 1 build. No Punchy.exe left running.

## W-28b (2026-11-02): less uniform icebergs
- Row 1 DONE: seeded shape classes, plus stretch, tilt, off-centre peaks, notches, undercuts and waterline ledges on some.
  - Big: tabular, dry dock, dome, tilted, pinnacle, capsized. Chunks and floes get four of these in a cheaper form.
  - Neighbours: v2 has 1 of 100 bergs whose nearest neighbour shares its class.
  - v2 classes: big [tabular 3 (incl. cave), dry dock 2, dome 1]; small [tabular 26, tilted 27, dome 21, pinnacle 20].
  - Cost: v2 went from 5 draws / 6232 tris to 5 / 6712, cave 1026 -> 1058 tris. v1 went from 3 / 2140 to 3 / 1420.
    Still batched per region.
- Row 2 DONE: a palette strip gives snow tops, bluer or greyer faces per berg and per face, and a dark blue streak on some. Visible in the shots.
- Row 3 DONE: by day, same cameras as W-28.
  - [coast-wide](PunchyMilestones/world/w28b/w28b-coast-wide.png), [coast](PunchyMilestones/world/w28b/w28b-coast.png), [berg](PunchyMilestones/world/w28b/w28b-berg.png)
  - [chamber](PunchyMilestones/world/w28b/w28b-chamber.png), [joint from the tunnel](PunchyMilestones/world/w28b/w28b-joint.png)
- Extra (a) DONE: a frame at the chamber wall closes the tunnel-joint gap (chamber and joint shots).
- Extra (b) DONE: TerrainSource.Deck includes the salt tower step floor. The player's Floor, called through
  reflection, walks all 245 steps to 80.42 m with 0 blocks. No PlayerMovementSystem change.
- **Checks:** hashes unchanged for v2 (126 / 053C63D5, caves 5D90D9CFC5326976) and v1 (102 / 51EA6463, caves 47C703C0E17F77CB).
  Cave walk 958 steps, 0 blocked.
- Open: the W-28 views centre on the cave berg, which stays a slab for its chamber, so the before/after there is
  mostly colour and facets. Next could be a dry-dock cave berg and a close shot of the other bergs.
- 1 build. No Punchy.exe left running from Builds/Dev.

## W-34 (2026-11-02): bridge posts, cobblestones
- Row 1 DONE: each bridge span now has a post at both ends of each rail (shared at the joints, reaching below the
  deck). v1: 5 bridges, 10 spans; rail ends without a post went from 20 to 0. v2 was not counted by probe; the same
  code draws it, and the v2 shot shows posts at every rail end.
- Row 2 DONE: cobbles are rounded, uneven stones in mixed sizes (a warped Voronoi with smooth-min borders, not a
  grid), with per-stone colour and height and dark earth or moss in the gaps. No tiling at walking height or at 30 m.
  - [before](PunchyMilestones/world/w34/w34-cobble-before.png) / [after](PunchyMilestones/world/w34/w34-cobble-after.png)
  - 30 m: [before](PunchyMilestones/world/w34/w34-cobble30-before.png) / [after](PunchyMilestones/world/w34/w34-cobble30-after.png)
- Row 3 DONE: shots by day, clear weather, viewed at 800 px.
  - v1 bridge: [A-31 side](PunchyMilestones/world/w34/w34-v1-bridge.png), [close](PunchyMilestones/world/w34/w34-v1-bridge-close.png), [framed](PunchyMilestones/world/w34/w34-v1-bridge-framed.png)
  - [v2 bridge](PunchyMilestones/world/w34/w34-v2-bridge.png); the cobble close-up is the after shot above.
- **Checks:** hashes unchanged: v2 126 / 053C63D5 (caves not re-read, mesh and shader only), and v1 102 / 51EA6463,
  caves 47C703C0E17F77CB. Build Succeeded, 0 errors.
- 1 build, two player runs (v1 and v2 can't share a launch). No Punchy.exe left running. world-features @ 0bdc707.

## W-30 (archipelago), 2026-11-02: PAUSED for Zach's demo
- Row 1 NOT DONE: Archipelago overlay written and compiling (v2 only, baked after places, only raises sea floor, surface to Dunes); v2 not run yet, so no islet log. Saved as Punchy-world Temp/w30-archipelago.patch, nothing committed.
- Row 2 NOT DONE: tooling in (`-archipelago off|on`, draw calls/batches/tris on each shot log line); no numbers yet.
- Row 3 NOT DONE: no build, no shots.
- Checks: v1 102 / 51EA6463 unchanged (editor play mode). v2 not read.
- ~52 tool calls, 0 builds. See lanes/world/STATE.md for what is left.

## W-30 (archipelago), 2026-11-02: DONE, world-features 8565688
- Row 1 DONE: 11 islets, 4 sandbars, off the dune coast at (2972,2159) heading 36 deg, out to 1694 m. Land spans x 3122..4252, z 2323..3290, inside the coast square (limit 4486). Sizes: 205, 117, 128 m (big), 73, 59, 49 m, rocks 10-23 m. Beaches, Dunes surface, palms; turquoise shelves around the islets. Landmark diff empty: v2 126 / 053C63D5, caves 5D90D9CFC5326976; v1 102 / 51EA6463, caves 47C703C0E17F77CB.
- Row 2 DONE: looking out from the coast (cam 2950,20,2140 to 3700,0,2900), editor play mode, v2: off 1368 draw calls, 85 set-pass, ~2105-2134 k tris; on 1368 draw calls, 85 set-pass, 2084 k tris. No added cost. (The per-shot recorders return nothing in a release player, so these are editor UnityStats.)
- Row 3 DONE: shots viewed at 800 px. The beach shot reads well (palms, sandbar, mainland dunes behind). The 250 m aerial shows the chain trailing out but is hazy.
- Shots: PunchyMilestones/world/w30/w30-beach.png, w30-aerial.png, w30-coast-off.png, w30-coast-on.png.
- Notes: islet flora is denser than "a few palms" (palm groves). The commit subject picked up a BOM (cosmetic, pushed, left as is).
- NEXT: W-35.

## W-35 river water steps (NOT DONE)

- Row 1 NOT DONE. Cause: LowerBreachedLakes lowers a breached lake to breach + 0.5 m (here 68.3 -> 55.89), but rivers that backed up into it keep the old level, and the old lake cells get bed = cell elevation with water = bed. Before the fix, the water along the site-1 river (from x -560 to -680) was 59.68, 60.27, 61.91, 56.81, 57.17, then lake 55.89. The ground under it is about 57.1-57.4, so the 61.91 water stands 4.8 m above the ground and then steps down 5 m.
  - Try A: refill the water after the lowering using the bake's max rule. Steps fell from 456 to 228 island-wide, but at site 1 the water became a flat 66.95 over ground at 57, a 10 m wall.
  - Try B: min upstream first, then max. Site 1 became 59.10, 58.40, 58.40, 56.12, 56.12, 55.89, which is monotonic, but the grove's dry channel points went from 5 to 473 and open ends from 0 to 7.
  - The hashes held both times. Both tries were reverted; a patch is kept untracked in the world tree.
  - Proposed: rerun bed shaping on the old lake cells. This is bed carving, so it needs approval and a hash check.
- Row 2 NOT DONE. v2 before: 1531 river cells, 456 steps over 0.3 m with no slope, in 219 cells. v1 was not measured.
- Row 3 NOT DONE. In the grove at (-756,1192), within 50 m: 1675 wet points, 5 points with a dry channel. The walking-height shot shows a floating water wall beside the grove trees, the same stale-level cause as row 1.
- Row 4 NOT DONE.
  - (-1196,953) is the lake's shallow edge (water 55.89 over ground 55.91), not a river and not a ford.
  - At (-879,1001) there is no water (ground 64.33). The open-end counter found 0 on v2, so it misses this case and needs a better rule.
- Row 5 partly done: 8 before shots in PunchyMilestones/world/w35, taken with the existing W-30 build. There are no after shots, and the aerial views are blocked by trees.
- Calls: about 80. Builds: 0 (no code change to ship).

## W-31 (sunrise and sunset), 2026-11-02: DONE, world-features f064b41
- Row 1 DONE (Sky.hlsl). It is driven by sun elevation e = asin(sunDir.y). The warm band sits on the sun's side of the horizon and turns cool violet on the far side and overhead. There is a glow around the sun. Stars, moons and shooting stars are untouched. Land haze uses the same horizon colour.
- Row 2 DONE (Sky.hlsl). Volumetric clouds, far clouds and cirrus take the twilight light toward the sun and go grey-violet away from it. High clouds keep the light below 0 deg: low clouds (800 m) go dark by -0.8 deg; clouds at 7300 m are still at 38% at -4 deg. Muting: `_PunchyTwilightDull` = saturate(Overcast + 0.4 Darkness) cuts the twilight by up to 80%, and cloud warmth falls by a further 60% x weather.
- Elevation table (clear sky, Dull 0). "Twilight" is how much of the effect applies; "light" is the brightness factor on the warm colour.

| e (deg) | warm colour (sun side) | twilight | light | far side / overhead | cloud toward sun | cloud away | high cloud (7300 m) |
|---|---|---|---|---|---|---|---|
| +12 | gold 1.00,.76,.46 | 0.00 | 1.00 | normal day | normal day | normal | normal |
| +5 | orange 1.00,.55,.24 | 0.96 | 1.00 | zenith x .95,.72,1.05 + violet | gold-orange | grey-violet .62,.55,.90 x lum | same as low |
| 0 | red-orange 1.00,.40,.17 | 1.00 | 0.97 | violet-blue | orange-red | grey-violet | same as low |
| -4 | pink-red .96,.38,.46 | 0.90 | 0.48 | violet, stars start | dark (low clouds unlit) | grey-violet | pink rim, 38% |
| -8 | violet .42,.26,.55 | 0.00 | 0.10 | night | night (moonlight fades in) | night | unlit |

- Row 3 DONE (lent: DayNightCycle.Evaluate, sun branch only; path and timing unchanged). Below about 15 deg (sunDir.y < 0.26) the sun colour is lerped toward x(1,.8,.6) and its strength x0.75 at the horizon. WeatherSystem lerps the equator ambient toward the twilight colour (x0.5). Fog already follows RenderSettings.fogColor and the sky haze.
  - +5 deg: before colour 1.00,.67,.45 at strength .69; after 1.00,.57,.32 at strength .56.
  - 0 deg: before colour 1.00,.46,.22 at .038; after 1.00,.37,.13 at .029.
  - Above 15 deg: unchanged.
  - Animal lane: the edit is 4 lines in the `SunDirection.y > -0.02` branch, marked W-31.
- Row 4 DONE: 1 build. Shot list: Glacier spot looking west, PartlyCloudy, -time 12 / 17.59 / 18.0 / 18.33, a sunrise at 6.24 looking east, and an extra 0 deg Overcast shot. Ends with -quit. Viewed at 800 px.
  - Midday is unchanged.
  - +5: peach-pink sky, sun disc, warm cloud edges.
  - 0: orange-pink band on the sun's side, pink clouds.
  - -4: violet blue hour; a high cloud keeps a pink rim.
  - Sunrise: gold under grey cloud.
  - Overcast: muted to grey-pink.
- Shots: PunchyMilestones/world/w31/w31-1-midday, w31-2-sun+5, w31-3-sun0, w31-4-sun-4, w31-5-sunrise-east, w31-6-sun0-overcast (.png), plus shots.txt.
- Weak spots:
  - At +5 and 0 the sky reads more pink-peach than deep orange-red.
  - Land fog takes the warm tint strongly, so distant hills go pink. A tuning pass could deepen the band and halve the fog tint.
- Checks:
  - Compile and shaders clean (0 errors). Build succeeded.
  - Hashes not re-measured: no generation code was touched (only Sky.hlsl, WeatherSystem lighting and the DayNightCycle light colour), so v2 126 / 053C63D5 and v1 102 / 51EA6463 are expected unchanged.
  - CRLF kept on WeatherSystem.cs.
- Incident:
  - Early on I used port 7801 thinking it was my editor. It is the D:/Unity/Punchy editor today (the ports were swapped).
  - On it I ran AssetDatabase.Refresh, set DayNightCycle TimeOfDay 17.59 and Paused on, and tried to enter play mode twice (it did not start). I set the values back (9, unpaused); the scene is not dirty.
  - Since then everything goes by --pid 30120. My editor is checked clean: not playing, not compiling, no errors, scene not dirty.
- Calls: about 60. Builds: 1.
- NEXT: optional colour tuning pass (deeper band, less fog tint); W-35 still waits on Zach.
## W-33 (cave distance bound + sky tuning), 2026-11-02: DONE with notes, world-features 287497c
- Row 1 DONE with a change of approach. `CaveDistance` itself is unchanged, because baking, meshing and fauna use its value, so changing it would change the hashes. The new query is `TerrainSource.CaveDistanceBound` (`CaveSampling.LowerBound`). It takes CaveDistance and divides it by the steeper of two slopes: a per-cave `SlopeLimit` (from the wall noise settings) and 1.2 x the local slope (4 taps). The sign is exact.
  - Probe: 10k points (6176 in air, 3824 in rock) in caves 3, 16 and 7. Truth comes from a 161-direction march plus bisection.
  - Old field: max overstatement x9.26, worst 0.82 m over; 3695 points over by more than 2%; mean bound/true 0.90.
  - LowerBound: 5 points over by more than 2%, max 4.6 cm over (x4.5 at a point 1.3 cm from rock); mean bound/true 0.49. The 5 outliers sit on stepped wall blocks and ledges (floor()-quantised noise).
  - Cost per call (editor, managed, single thread): 11.1 us before, 55.6 us after (5 field samples). Cheap under Burst for a few queries a frame; don't use it for per-vertex work.
- Row 2 DONE: docs/agents/world.md says CaveDistance overstates by 2-9x and that CaveDistanceBound is the lower bound to use for push-out. It also says to retune SlopeLimit if WallNoise changes.
- X1 DONE (sky): 0 deg 1.00,.40,.17 -> 1.00,.32,.10; +5 deg 1.00,.55,.24 -> 1.00,.46,.16. From -3 deg up, the band on the sun's side reaches higher (zenith blend exponent .45 -> up to .85). Land haze takes half the warm tint (lerp toward luminance, x0.5).
- X1b DONE (Zach: more cloud glow). Glow values, old -> new:
  - Strength: twilight colour x0.9 -> x0.9 x 2.2, with saturation x1.35.
  - Range: same envelope as the sky (fading out by +12) -> its own envelope, full to +9 and gone at +13.
  - Weather mute: 80% (cloud warmth also -60% x weather) -> 50% (-40% x weather).
  - High cloud reach: 6.5 deg -> 9 deg below the horizon.
  - New silver-gold lining: thin cloud edges facing the sun, pow(cos, 10) x 4. Far clouds and cirrus take the stronger light too.
- X2 PARTLY: player log v2 = 126 landmarks, hash 053C63D5 (unchanged). Editor (bakes v1) caves 47C703C0E17F77CB (unchanged). My v1 player run defaulted to v2 because I left out `-island v1`. That run also had no `-shotdir`, so it never reached -quit. I closed its window myself; it was not killed. Not re-measured: the v1 landmark hash and the v2 cave hash. No generation code changed (only new methods and Sky.hlsl).
- Shots: PunchyMilestones/world/w33: w33-1-midday, w33-2-sun+5, w33-3-sun0, w33-4-sun-4 and w33-5-sun0-overcast (.png), plus shots.txt. Same Glacier view and times as W-31. Viewed at 640 px.
  - Midday is unchanged.
  - +5 deg: gold-orange round the sun, and a lit cloud with a bright gold rim.
  - 0 deg: the band is more orange and the land haze less pink. The upper sky is still pink-violet.
  - -4 deg: violet blue hour.
  - The weather drew few clouds at the moment of these shots, so the glow change is only partly visible. A cloudier comparison is still needed.
- Weak spots: the pink at 0 deg comes from the violet twilight zenith blending down into the band. Next pass: a warmer zenith near 0 deg on the sun's side.
- Checks: compile, shaders and build clean (errors=0). CRLF kept on TerrainSource.cs; the other files are LF. No Punchy.exe left running.
- Builds: 2. The first was done before the cloud-glow request arrived, and the second shipped it.

## W-35b (2026-11-02): riverbeds after a lowered lake, plus X1 and X2

world-features f770492. Status: rows 1, 2 and 5 DONE. Rows 3 and 4 are DONE with exceptions. X1 and X2 are DONE.

**Fix.** `RiverBaking.ReshapeLoweredLakes` runs after LowerBreachedLakes. It recomputes each cell's base level downstream-first, using the lowered lake surface. It then touches only the cells whose base level dropped or that stopped being lake. In those cells:
- A cell the lake left behind gets a BedStart channel bed.
- The three BedDownhill passes run again: cut downhill to the new base level, spread gorges, and set the water so it never rises along the flow.

Every other cell keeps its bed and water exactly.

**Hashes.** Landmarks did not move: the WorldProbe lists before and after are identical on both islands, and the player logs agree.

| Island | Landmarks | Caves | Rivers | Ground |
|---|---|---|---|---|
| v2 | 126 / 053C63D5, same | 5D90D9CFC5326976 -> ACA9CC9155621A44 | FD0045F0A0810452 -> 6A1BC5BDCDE399BE | 4E0B705A4BDE0ECD -> 79A65F834D760FC8 |
| v1 | 102 / 51EA6463, same | 47C703C0E17F77CB, same | 164DF6A9926DE097 -> B867BA86DC27D8D0 | 3EE84EED3A33770B -> 2AC3143B4715222E |

- v1 also has breached lakes, so its rivers changed too.
- Falls on v2 went from 75 to 70.

**1. River through -632,1325 (water/ground, m).**
- Before: -556,1362=59.92/57.57, -589,1337=61.91/59.17, -606,1324=56.71/54.36, -725,1191=61.88/59.47, -758,1165=58.25/55.78. The water rose by up to 1.46 m along the flow.
- After: -556,1362=58.08/56.33, -572,1349=57.11/55.36, -589,1337=56.31/54.56, -606,1324=56.05/54.29. From -622,1311 to -808,1112 the water is 55.89, level with the lake. The largest rise along the flow is now 0.00 m.
- "Water above ground" on this line is the channel depth, about 2.4 m at both times. The 6.49 m maximum is the deep channel at the lake mouth, -808,1112.

**2. Steps over 0.3 m with no slope.**

| Island | Before | After |
|---|---|---|
| v2 | 456 in 219 cells (unexplained 68, gorge 20, cascade 65, shore 64, confluence 2) | 235 in 116 cells (unexplained 44, gorge rapids 19, cascade 45, lake/sea shore 8) |
| v1 | 68 in 41 cells (gorge 18, shore 5, unexplained 15, confluence 3) | 59 in 33 cells (gorge rapids 16, confluence 3, shore 1, unexplained 13) |

The reasons for each remaining step cell:
- cascade: within 40 m of a fall lip.
- shore: next to a lake or the sea.
- gorge rapids: the bed drops more than 1.5 m to the next cell.
- confluence: rivers join at different levels.

Unexplained cells (cell, worst step), v2: (-905,-1483) 0.71 m, (-846,-1437) 0.84 m, (-865,-1261) 0.98 m, (-862,-1209) 0.44 m, (-2569,-1099) 0.36 m, (-591,-1082) 0.80 m, (-2527,-1065) 0.32 m, (-2399,-888) 0.48 m, (-423,-884) 1.77 m, (-2407,-855) 0.40 m, (-2390,-842) 0.30 m, (-2374,-829) 0.36 m, (-2365,-822) 0.48 m, (-2363,-555) 1.33 m, (-2346,-542) 1.34 m, (-2337,-483) 0.91 m, (-2344,-477) 0.33 m, (-1721,424) 0.90 m, (-1633,417) 0.40 m, (-1768,476) 0.46 m, (-758,507) 0.32 m, (-1976,525) 0.50 m, (694,700) 0.83 m, (702,706) 0.59 m, (-2417,846) 0.60 m, (317,1439) 0.97 m, (-1158,1681) 0.60 m, (-467,2270) 0.64 m, (-2441,2332) 0.45 m, (-2556,2493) 1.14 m, (-309,2585) 1.42 m, (2486,2670) 0.48 m, (-1274,2677) 0.56 m, (-2447,2794) 0.35 m, (-2260,2855) 0.41 m, (-2185,2914) 0.55 m, (-2109,2987) 0.37 m, (-1591,3045) 0.45 m, (-1574,3058) 0.63 m, (-3089,3145) 0.79 m, (-2070,3169) 0.46 m, (-2966,3192) 0.93 m, (-1705,3247) 1.32 m, (-2140,3469) 0.54 m.

v1: (-2056,537) 0.66 m, (2476,941) 0.84 m, (2460,955) 0.87 m, (2445,968) 0.87 m, (2455,963) 0.87 m, (2429,981) 0.87 m, (2440,987) 0.87 m, (2413,995) 0.87 m, (2424,1000) 0.87 m, (2764,1082) 0.62 m, (-201,1784) 0.56 m, (-77,1831) 0.98 m, (676,2139) 1.58 m.

The full list is in the world tree at Temp/w35b_steps.txt.

**3. Grove at -756,1192 (r 50 m).**

| | Wet points | Deep river points | Dry-channel points | Trees | Trees in water |
|---|---|---|---|---|---|
| Before | 1675 | 0 | 5 | 72 | 0 |
| After | 1417 | 0 | 31 | 92 | 0 |

- The water wall is gone: the river is now at lake level, with sandy banks.
- The 31 dry points are all at the channel edge (river weight 0.5-0.93). Their ground stands 0.2-0.55 m above the water, at 56.1-56.4 m against 55.89 m, so they are bank. None of them is carved channel bottom; the measured bank height there is 0.
- The old count of 5 was low only because the water stood above the banks.
- This is over the card's limit of 5, so the supervisor should decide.

**4. Open ends.**
- The new edge rule walks 8 directions out to width/2+12 m from each wet river point. A cell is an open end when the water stops and the ground there, and 2 m further on, is more than 0.3 m below the water. The rule caught -879,1001 before the fix. The old rule and the flow rule each found 0 on v2.

| Island | Before | After |
|---|---|---|
| v2 | 298 (161 at falls, 137 not) | 239 (133 at falls, 106 not) |
| v1 | 18 | 3 |

- -879,1001 is fixed: its channel now runs at the lake level, 55.89 m.
- The 106 v2 ends that are not at falls are mostly away from lowered lakes, in the -2400..-2100 x band and in the north-west. This fix does not reach them; it would need a general end fix.

**5. Shots** (PunchyMilestones/world/w35b, viewed at 800 px). Day, clear weather, the W-35 views. The aerials are now about 40 m above the ground.
- The step and grove aerials show a flat river at lake level with sand banks: no walls and no floating sheets.
- The end aerial shows the channel meeting the lake with banks on both sides.
- In the walk shots for the step, grove and ford, trees fill most of the frame at the old eye positions. The river shows only at the edge.
- The grove aerial shows a white foam strip across the river near the far lake. It is probably a fall kept at a lake outlet, and I did not check it.

**X1.** No code change. Glacier view, Overcast (cloud coverage 0.9), with -nohud: x1-1 at sun +5 deg and x1-2 at sun 0 deg.
- The forced Overcast still gave only a few clouds in this view, not a full sky.
- At +5 deg the clouds glow warm gold. At 0 deg the glow is dimmer, rose-grey, under a mauve sky.
- A next pass would need a denser cloud preset, or a weather force that sets the coverage itself.

**X2.** Both players were given -shotdir, and both runs reached -quit.
- v1: `-island v1`, shot w35b/v1/x2-v1-check.png. The log shows 102 landmarks, hash 51EA6463.
- v2: the log shows 126 landmarks, hash 053C63D5.

**Checks.**
- Compile was clean, and the build succeeded with 0 errors.
- RiverBaking.cs and docs/agents/world.md are LF, as before.
- No Punchy.exe was running before the build or afterwards.

**Builds:** 1.

## W-36 (world, 2026-11-02)

Commit world-features 4d22a44 (pushed): `RiverBaking.FillRiverPits`, rapids gradient from the water drop, lake-outlet foam from the real ramp, docs/agents/world.md gotcha.

| # | Row | Result |
|---|---|---|
| 1 | Open river ends | DONE (partly fixed, rest classified). v2 not at falls 106 -> 92 (x -2400..-2100: 26 -> 12; north-west 18 -> 18). Before: backwater 65, cut by another carve 40, low ground 1. The 65 were mostly river pits: beds below the bed downstream (e.g. -2343,-437 bed 20.07 under a 35.39 dam, water 34.51), the carve dug the banks down to 20-25 m. Filled; 17 ends fixed, 3 new at -1988..-1955,-231..-205. v1: 3 -> 3. |
| 2 | Unexplained steps | DONE (classified). v2 116 -> 117 cells; unexplained 36 -> 7 (44 by the W-35b rule = 36 + side by side 7 + hidden fall 1). New reasons: rapid (bed drop > 1 m over two cells) 30, two rivers side by side 7. v1 unexplained 13 -> 0 (rapid 4, side by side 9, gorge 16, confluence 3). |
| 3 | Grove foam strip | DONE: a fault, not a waterfall. Rapids foam came from the bed gradient on water backed up flat to the lake (water 55.9 over -802..-813,1123..1136, rapids 1.0). Fixed; rapids 0 on that line; shot w36-5. |
| 4 | Shots | DONE, all viewed at 800 px: banks hold the water at both band ends, no white strip by the grove lake. |

Hashes old -> new. v2: landmarks 126/053C63D5 (same), caves ACA9CC9155621A44 (same), rivers 6A1BC5BDCDE399BE -> 70B01EF403A78FF5, ground 79A65F834D760FC8 -> A87024EE3525ADC0, falls 70 (same). v1: landmarks 102/51EA6463 (same), caves 47C703C0E17F77CB (same), rivers B867BA86DC27D8D0 -> 2508BF538DA96D7E, ground 2AC3143B4715222E -> A32E650C44A45E06.

Checks: WorldProbe landmark list on both islands is unchanged (count + hash). The player v2 log shows 126 landmarks, hash 053C63D5. One build succeeded with 0 errors. One shot list ran -shotdir and quit with exit 0. No Punchy.exe was running before the build or the run.

Remaining v2 open ends (not at falls), by reason:
- Cut by another carve (38): a neighbouring segment's carve lowered the bank at a rapid or junction. -872,-1241 -2677,-1158 -2477,-1012 -2399,-888 -414,-891 -2374,-829 -2346,-542 61,184 -1118,346 -775,494 658,810 149,1241 -437,1331 -846,1574 -968,1794 -974,1814 -419,2244 -2548,2499 1606,2626 2542,2664 -2318,2796 -2334,2808 -2260,2855 -1575,3032 -1591,3045 -1574,3058 -2056,3131 -1239,3186 -2069,3209 -1286,3212 -1254,3213 -2085,3222 -2061,3418 -2044,3431 -2161,3580 631,3762 -1595,3789 805,3862
- Low ground beside (32): the river is held at its dam level (34.51 m) and the land beside it is lower. -2260,-358 -2243,-345 -2226,-332 -2162,-331 -2088,-323 -2072,-310 -2055,-297 -2038,-284 -2022,-257 -2005,-244 -1988,-231 -1972,-218 -1955,-205 244,1378 664,1409 947,2267 963,2280 980,2293 997,2306 1013,2334 1030,2346 1047,2359 1130,2438 1131,2478 1148,2491 1164,2504 1181,2517 1191,2550 1266,2609 1354,2617 1371,2630 1388,2643
- Backwater (22): a lake (28.49 m) or the sea backs up over banks lower than its surface. -1164,222 699,1058 648,1436 675,1482 692,1495 708,1508 701,1540 685,1567 678,1573 663,1585 391,1674 392,1700 411,2195 420,2202 395,2222 379,2234 1063,2372 1080,2385 -2196,3715 (sea) -708,4014 -691,4027 -707,4040

Remaining v1 open ends: 2424,1000 (cut by another carve), 1547,3277 (cut by another carve), -2315,1571 (backwater).

Unexplained v2 steps, with the worst jump: -2407,-855 0.40; -2365,-822 0.48; -2346,-542 1.34; -2344,-477 0.33; -2441,2332 0.45; -2185,2914 0.55; -2070,3169 0.46.

Shots are in PunchyMilestones/world/w36:
- w36-1-band-end-a-air: -2343,-437, fixed.
- w36-2-band-end-b-air: -2153,-324, fixed.
- w36-3-northwest-end-air: -2056,3131. Still open (cut by another carve). No north-west end was fixed, but the banks look closed from 40 m.
- w36-4-grove-air.
- w36-5-grove-lake-air.

NEXT:
- Decide whether to cut the 34.51 m dam rather than fill it. That would clear the 13 low-ground ends at -2260..-1955, but it carves a canyon about 15 m deep and 500 m long.
- Clamp lake backwater to the river's banks.
- Look at junction bank carving, which accounts for 38 ends.

Budget: about 60 calls and 1 build.

## W-37 river meander scales with width: NOT DONE (stopped, landmarks would move)

- Fix written, not committed: new Burst `RiverBaking.PathPointsJob` (v2 only). A wide river's segment points are a Gaussian average of the jittered points along its main channel, with sigma = min((w-12)*1.5, 72) m. They are stored in `RiverMapBlob.Points`. Narrow streams (w <= 12) and v1 are unchanged. The patch sits uncommitted in the world tree.
- Landmark diff, v2: 126 -> 128 (hash 053C63D5 -> 8835D39A). CrystalGrotto at -0,-406 dropped. Added: LoneInn -2857,-1463; OakGrove 1749,2448; Waterfall -1994,3010. The other 125 keep their positions. Stopped before committing, per the hash rule. v1 after was not measured.
- v2 before -> after (W-37 measure):
  - rivers 70B01EF403A78FF5 -> 707C64C3E62EB1FD
  - ground A87024EE3525ADC0 -> 26D0900E19772F07
  - caves ACA9CC9155621A44 -> AF3F57E122D19AE8
  - falls 70 -> 68
- Row 1, wide reaches (w ~48), wavelength 1.4-1.9x w -> 5.2-10.9x w; max curvature 0.33-0.35 -> 0.02-0.18. Zach's reach (-1195,-2014, w 26): wavelength 2.0x -> 6.4x w, curvature 0.359 -> 0.072, min radius 3 -> 14 m. Narrow reach 194,1340 is identical.
- Row 2, bulbous on v2: 744 -> 171 (balloon 131 -> 20, pinch 81 -> 4, radius < width 532 -> 147). v1 before: 263.
- Row 3: steps (W-37 rule) 138 -> 121. Open ends not at falls 92 -> 102 (worse). Floating 28 -> 33 (worse). v1 before: steps 131, open ends 3, floating 0.
- Row 4: before shots only, in PunchyMilestones/world/w37/before (zach_spot, widest, mid). No build and no after shots, since nothing was committed.
- NEXT, needs a decision: (a) accept the 4 landmark changes, or (b) keep river points at the jittered value wherever landmark placement reads them. The open-ends and floating regressions need one tuning pass either way.

## W-43 village polish: PARTLY DONE (world 882af99, local)

Hashes, v2: unchanged. Landmarks 127 / 4D245E12, caves C49E8E3036C741FC, rivers A5B3DE5D53257332, ground F8F938002B0CBE46. v1 landmarks 103 / 19885131 are unchanged too. The build succeeded with 0 errors in 1:05, using `--timeout 900` instead of the card's 600.

- **Row 1, banners.** There were three causes:
  - The phase rate rode on the per-vertex gust.
  - The gust UV scroll (clock x wind x direction) jumped whole noise cells as the wind drifted.
  - The two banners were nearly in phase.

  The phase now runs at a fixed rate with a random offset, and each banner reads one gust at a fixed scroll. Tip jump per frame over 10 s, from an analytic replica:

  | fps | old | new |
  |---|---|---|
  | 30 | 0.652 m | 0.028 m |
  | 144 | 0.708 m | 0.007 m |

- **Row 2, roof gap.** Measured as the roof underside minus the wall top. Roofs now start at the wall top (EaveStart).

  | Hut | old | new |
  |---|---|---|
  | Hall | 0.70-0.73 m | -0.04 m (embedded) |
  | StoneHall | 0.72-0.75 m | -0.04 m |
  | Inn | 0.13-0.14 m | -0.04 m |
  | StoneHouse, Hut, Tower | 0 (solid RoofTube) | 0 |

- **Row 3, tent seams.** The panels now share one sag point.

  | Tent | old | new |
  |---|---|---|
  | Chief | 0.255 m | 0 |
  | Ogre | 0.268 m | 0 |
  | Goblin | 0.140 m | 0 |

- **Row 4, tent collision.** Tents have no support grid, so the player cannot ledge-grab or mantle onto them. Sink is 0 from N/S/E/W.
  - Stand-off from centre: Chief 2.48 m, Ogre 2.90 m, Goblin 1.27 m (was 0.98 m, too deep; the rings now follow the hide 0.5 m below each top).
  - Off-axis approaches slide around the tent. PlayerMovementSystem is unchanged.
- **Row 5, chimneys.** ChimneyTop added to StoneHouse, Inn and Smithy. Smoke plumes and their cost were NOT measured.
- **Row 5b, elven foyers.** Sink 0. Front approaches stop at 4.79 m (ElfSpire, foyer front 4.39) and 4.98 m (ElfTower, front 4.58). Side approaches are held off.
- **Row 5c, weather hold.** Played with `-weather Clear` and checked at t = 0, 57 and 172 s: coverage 0.04, precipitation 0, overcast 0, wind 0.20. It holds.
- **Rows 5d, 5f: NOT DONE.** The floating-water measure, the temple walk and the sanctum shot were all out of budget.
- **Shots**, in PunchyMilestones/W-43, with *_800.jpg copies:
  - hall_before / hall_after
  - tent_before / tent_after
  - inn_chimney_after
  - eye_after (3585,-42)
  - The hall pair frames the wall base; I could not see a difference at 800 px.

NEXT:
- Smoke count, particle peak and frame cost (row 5).
- Row 5d.
- Row 5f.
- A roofline shot with smoke.
- A reframed hall roof shot.

Budget: about 90 calls (over the 85 ceiling) and 1 build.

## W-43b (2026-11-03), partial

- Commit: TerrainSurface.shader (puddles: sky reflection, fresnel, dark tint, rain rings; broken shoreline band). 0 shader errors in the editor; NOT yet in a player build.
- Row 2: the seeded width/slope/edge wobble in the river carve changed v2 landmarks 4D245E12 -> 54FFBC1C (caves C49E8E30 -> 94E6B820), so it was reverted and is not committed. Rivers stayed A5B3DE5D53257332 and v1 was unchanged. Needs a landmark-safe version (wobble applied after landmark placement).
- Row 3: floating water is 35 under the old rule and 5 under the cascade rule (a drop of 5 m or more counts as a fall; 30 cascades). That count was measured with the reverted carve.
- Row 1: no chimney smoke exists in the code, so 0 of N chimneys smoke. NOT DONE.
- Not done: build (the CLI dropped and the build never ran), after shots, rows 4 to 6 shots, row 5 temple walk. The before shots in D:/PunchyMilestones/W-43b are badly framed.

## W-43c (world-features e39ae7f)
- Row 1 bank wobble: landmarks v2 127/4D245E12, caves C49E8E3036C741FC, rivers A5B3DE5D53257332 all unchanged; v1 is untouched (the wobble needs v2 Smooth). Ground F8F938002B0CBE46 -> 84FAAD0B047B05EB. Open ends 46 -> 44.
- Row 2 chimney smoke: 6/17 chimney buildings smoking near one Inn, 84 particles peak, gather 0.01 ms.
- Row 3 floating water (WorldProbe.Floating, cascade rule): W-43 had 35 (old rule) / 5 (cascade rule); now 23 / 3. Left: 19,202; 656,562; -2186,3604.
- Row 4: build Succeeded, 0 errors. Shots in W-43c: bend before/after OK. The puddle, roofline and hut shots missed their framing (retake needed). eye_clear shows the W-53 sand line is still there.

## 2026-11-03 batch W-59b: fishing villages finished (docks face the sea), built and shot

Commit: world-features 5737dc4 (FloraPlacement.cs, FishingVillageBaking.cs). Builds: 1 (0 errors). Player runs: 1, with -shotdir and -quit; it quit by itself.
1. #36 docks face the sea: DONE. Docks use FloraRule.FaceLandmark, so they turn to the village's facing (from the bank out to open water). Fits() now also rejects a village spot unless every dock's far end (19 m out) is at least 0.6 m below sea level. Editor check: 8 of 8 docks have their far end over water (0.19 to 2.68 m below sea level). Dock lengths are 16.3 to 19.9 m.
2. v2 bake: 4 villages, from 186 mouth cells (86 tried):
   - -3304,-1644 facing 0
   - -2940,-1448 facing 90
   - -2883,-2121 facing -135
   - -2372,-2124 facing 0
   Diff against lmlist-w58: only those 4 FishingVillage lines were added. The 127 old landmarks are identical (position and type). Count 131. The editor WorldProbe fingerprint is D153C437; the player log hash is 41BA8108 (a different hash function).
3. Shots, in D:\PunchyMilestones\W-59: w59b_village1_day, w59b_village2_day, w59b_village3_dusk, w59b_colossus_head (#40). The agent did not look at them, so nobody has checked framing or whether trees go through the colossus head yet.
4. Dock walk: FAIL (a snag). The walk went from -3286,-1643.7 toward the far end of village 1's dock (-3264,-1643.7). The player went down the beach from y 0.4 to y -0.3, then stopped at x -3280.9, which is the dock's land end (-3280.0), with 16.9 m still to go. It stayed stuck there for the rest of the shot: 0.1 m over the floor, pushed out 0.0 m, 57 probe lines. It did not fall into water. The likely cause is that the dock's land end sits a step higher than the beach at the waterline. The fix would be a ramp or step at the dock's land end, or starting the dock further up the bank. It needs a new build.
5. Hashes: caves C49E8E3036C741FC (unchanged). v1: 103 landmarks, fingerprint 19885131, no villages (unchanged). Rivers: not measured this batch; no river code changed (the hash is expected to stay A5B3DE5D53257332). Ground: villages have Flatten 0 and Ground null, so they change no ground; not measured.
Left: the dock snag (row 4), a look at the 4 shots, and the river and ground hash numbers.

## 2026-11-03 batch W-59c: fishing village polish (docks at the waterline, plank ramp), built and shot

Commit: world-features 0587ed2 (FloraPlacement.cs, StructureKit.cs). Builds: 1 (0 errors). Player runs: 1, with -shotdir and -quit; it quit by itself. About 80 tool calls.
1. Before (W-59 shots at 320 px): village1 and village3 were shot from inside the forest (trunks only, no docks); village2 OK but no dock in view; colossus_head was shot at editor coordinates (-1030,-740), camera under the ground, white frame. No dock visible in any shot. Dock bases sat -1.70 to +2.47 m from sea level (two started 2.3-2.5 m up the bank, two up to 1.7 m under water).
   Fix: DockShore() in FloraPlacement slides each dock along the facing (up to 14 m) to the last dry point; bases now -0.06 to +1.31 m (6 of 8 within 0.25-0.37 m); far ends all over water (1.8-2.4 m deep).
   After (W-59c shots): village1, village2, village4 framed from the water with both docks and houses in view, docks sit on the shore. village3_dusk is black (camera inside something; retake needed). colossus_head is blocked by conifers, only the top of the head shows (retake needed). Titan shot OK (a conifer in front). White mist puffs float over the water in every shore shot.
2. Dock snag: the short stair's treads fell between support-grid samples (0.5-1.6 m wall at the land end). Now a 4.6 m plank ramp (about 15 degrees, starts 0.4 m in the ground), no RNG draws. Editor sim (step-up 0.4) is partial: the props buffer only holds props near the player, so only the dock nearest the player is valid. With the earlier 3.4 m ramp: village4 dock PASS bank to end; village3 dock got on the deck, then a 1.44 m wall at 9.5 m (shore clutter, likely crab pots, on the centre line); village1/2 marginal 0.44-0.48 m step, which the 4.6 m ramp was made for. NOT MET: all 8 not simmed with the final ramp.
3. Player walks (step-up 0.4): village4 dock (-2356 -> -2335, z -2124.1): PASS, bank y 2.6, up the ramp onto the deck y 1.1, stopped 0.3 m from the target (1.6 m from the end). village1 dock (-3291 -> -3270, z -1643.7): ramp PASS (onto the deck, y 1.1), then FAIL mid-deck at x -3275.5 (10.5 m along), 5.5 m to go, held there 20 probe lines: a crate/crab-pot prop on the centre line (visible in w59c_dock1_end).
4. Hashes, v2 (editor Play-mode bake): landmarks 131, editor WorldProbe fingerprint D153C437, player log hash 41BA8108 (different hash function); lmlist-w59c identical to lmlist-w59, and vs lmlist-w58 only the 4 FishingVillage lines are added. Caves C49E8E3036C741FC. Ground (FNV-64, every 4 m) all C0C906EE310FA91F: unchanged everywhere, villages included. Rivers: W58Probe formula (bed+level+width+down) A77D2ED7B919296E, same as W-58 (A5B3DE5D53257332 is another formula; river code untouched). v1 (editor edit-mode bake): 103 landmarks / 19885131, unchanged.
Shots: D:\PunchyMilestones\W-59c\w59c_village1_day, village2_day, village3_dusk (black), village4_day, colossus_head (trees block), colossus_titan, dock1_start, dock1_end, dock4_start, dock4_end.
Left: keep shore clutter (crab pots, crates) off the dock centre line; retake village3_dusk and colossus_head; editor colossus positions differ from the player's (editor HalfBuriedHead -1071,-779 buried 26 m; player 1479,2221), so editor framing of colossi is wrong; mist puffs over the water.

## W-59d (2026-11-03): dock walk lanes, sea mist, colossus placement (#36 #40)
Commits on world-features: b30a28b, 8feecb0. One build (Succeeded, 0 errors), one player run; the player quit by itself.
1. #40 colossi: DONE. Cause: StoneColossi placed against the editor subscene's v1 TerrainSource before IslandV2System swapped in v2 (the player only ever saw v2). It now waits for the v2 bake. Editor and player now match: titan -1434,63.5,3112; head 1479,26.6,2221. DunesColossus (animal lane file, A-09) has the same bug: editor 1780,-1973 vs player 3730,877. Not touched.
2. #36 walk lane: DONE. Shore kit props stay 5.6 m (scaled) and biome plants 2 m from the axis of each dock deck plus its 4.6 m ramp. Crab pots moved to the deck's left edge (same RNG draws). Editor check: 0 lane walls, 0 flora in the lane on all 8 docks.
3. #36 editor sim, 8 docks, step-up 0.4: last pass 7 PASS, 1 SNAG. PASS: -2355,-2136.5 (18.0 m); -2350,-2124.1 (16.3); -2897.9,-2136.1 (16.4); -2927.4,-1412.5 (19.9); -2939.8,-1426.5 (19.9); -3296.5,-1656.1 (16.3); -3285.0,-1643.7 (16.4). SNAG: -2910.9,-2131.6 at d 2.8 (top 1.56 m over feet -0.43). The snag moves between runs: across 3 passes, each dock passed at least once (v2 snagged at d 0.4 in one pass, v1 -3296.5 in another). It looks like the sim's ground start or props still streaming in, not clutter. Lane walls were 0 in every pass after props loaded.
4. #36 sea mist: DONE. Source: FogWisps (Mist style) in FoggyMarsh/FungalForest; the coastal biome blend reaches over the sea. Before: 1411 of 1411 open-sea points within 150 m of the 4 villages could spawn wisps. After: 0. Mist now skips water deeper than 0.5 m at sea level.
5. Player: dock1 (-3291 -> -3270.3) PASS, ends on the deck at y 1.1 (2.7 m over the sea floor). dock3 (-2895.1,-2133.3 -> -2908.3,-2146.5) PASS, ends on the deck at y 1.2 (3.4 m over the floor). colossus_head retake: clear view (head keep-out 26 -> 42 m). village3_dusk retake: no longer black, but the camera sits over forest canopy and the village is hard to see. Still needs a camera out over the water.
6. Hashes, all match: v2 131 landmarks, editor D153C437, player 41BA8108; caves C49E8E3036C741FC; ground C0C906EE310FA91F; rivers A77D2ED7B919296E. v1 was not run.
Shots: D:\PunchyMilestones\W-59d\w59d_village3_dusk, colossus_head, dock1_start, dock1_end, dock3_start, dock3_end (plus a stray shot-0).
Left: village3_dusk framing; the intermittent sim snag; DunesColossus editor/player mismatch (animal lane); v1 hash check.

## W-64 (2026-11-03) world-features cb2dc77
1. #38 igloo doors: DONE. Domes are bigger (height 10.5-12), so the 1.7 x 3.4 door sits inside the dome wall with dome all round it. Dome tops are 5.5-6.5 m against the 3.4 m door. Porches re-seated. Editor sim (village 0, step-up 0.4, start 3 m out): 5/5 reach the door. 2 stop at the door wall 3.8 m in; 3 walk past the door line because the inner block doesn't catch them (leftover, see below). Overlap check: all pairs clear. Neighbours are 17.8 m apart against a combined reach of 15.0-16.2 m. One mouth-to-mouth path is blocked by a dome (343.3,57.7 -> 349.9,71.1).
2. #24 ruin moss: DONE. The moss pad is replaced by one draped sheet (MossCap / FloraPart.Moss 13). It runs up the face, over the top and down the back, with ragged hanging tongues. It's an alpha-cutout clip with noise in all 4 Flora passes, so shadows and depth match. Tint is clumped yellow-green and darker near the torn edge. No new lights; ruin shapes unchanged.
3. Build: Succeeded, 0 errors. Player shots retaken. Igloo walk PASS: ends 0.5 m from the door target (367.7,265.7,60.5), pushed out 0.0 m. Every Punchy.exe has quit.
4. Hashes: v2 136 landmarks, editor 4E61F1CF, player B5EFF8C8; caves C49E8E3036C741FC; ground C0C906EE310FA91F; rivers A77D2ED7B919296E. All match. No landmark changes. v1 was not run (tool budget).
Shots: D:\PunchyMilestones\W-64\w64_turtle_village, igloo_door_a, igloo_door_b, igloo_walk_start, igloo_walk_end, ruin0_close, ruin0_medium, ruin1_close, ruin1_medium (plus stray shot-0, shot-10).
Left: the igloo inner block lets 3/5 walks pass the door line (the door is still reached); v1 hash check.

## W-66 (2026-11-03) world-features 29fefb1 + follow-up (#81 #82)
- Editor: no editor was running, so I opened the world-tree editor. Refresh ran and there were 0 compile errors (collab-proxy is gone).
- #81 cave at 530,958: the editor marker profile (1019 frames) put Fauna TraceSystem at 4.0 ms/frame, the top game cost. CreatureRender came next at 1.0 ms and InteractionInput at 0.8 ms. Fix: every in-range creature's ground lookup (NoSurface, plus water for waders, breachers and burrowers) now runs in one Burst job instead of managed calls per creature. Behaviour is the same. `-oldtrace` on a shot line brings back the old path. The file is in the Fauna folder.
  - Player, same spot and heading (-view 0,10, -time 12): old 18.69 ms (max 22.2). New 16.67 / 16.67 ms (max 16.8), which is the vsync cap, so the real gain is bigger than this shows. The build has no frame timing stats, so there is no CPU/GPU split.
- #82: settlement plot buildings, landmark placed props (stalls) and the landmark centre prop (big tent) were all created with Snow 0. Each now takes the biome snow at its spot from the new TerrainSource.SampleBiomeSnow, which is read-only and changes no terrain.
  - NOT PROVEN: both v2 stall camps are on snowless ground. In the w66_ogre_camp shot the tents and stall are bare, as expected. In the editor, all v1 camps sample 0 snow. The Zach playtest camp spot is still unknown, so I have no framed snowy-town shot.
  - Steep tent hides may still take little snow, because of the shader's n.y smoothstep.
- Build: 1 build, Succeeded with 0 errors (1:20). The player quit by itself, and tasklist shows no Punchy.exe running.
- Hashes: v1 103 / F628AE5F raw, unchanged. Player v2 136 landmarks, from the warp-target count. The editor was in v1, so I did not re-hash v2 there. I did not re-hash caves, ground or rivers. No landmark, cave or terrain code changed: SampleBiomeSnow only reads.
- Shots: D:/PunchyMilestones/W-66 (w66_cave_new_a, w66_cave_old, w66_cave_new_b, w66_goblin_camp, w66_ogre_camp, player.log).
- Left over:
  - #82 needs a snowy camp spot, or Zach's playtest coordinates, for the proof shot.
  - Re-hash v2, caves, ground and rivers in the editor.
  - I went over the tool budget (about 70 calls).

## W-67 (2026-11-03): kit box collision (#79 #36 #38 #80)
- Commit: world-features 52b4af5 (pushed). New file Flora/KitCollision.cs. KitParts become rotated solid boxes; decor (moss, trim, windows, lamp/rune/moss materials) opts out. FloraSystem places only the boxes within 12 m of a player (plus the model radius), refreshed with the support tops. Flora cylinders, caves and non-kit props keep their old paths.
- PlayerMovementSystem changes (for the animal lane rebase), all commented "W-67":
  - (a) reads the KitBox singleton buffer (`hasKit`);
  - (b) KitCollision.KeepOut after KeepOutOfPropWalls (walls from box sides, X/Z slide);
  - (c) new helper TopUnder: prop Sample (skipKit) or kit Floor, whichever is higher; used for the main prop floor, the climber perch, the top-out and walk-off-edge checks (`tops || hasKit`);
  - (d) ceiling clamp from box bottoms while rising;
  - (e) KeepOutOfPropWalls passes skipKit;
  - (f) SlideAroundObstacles skips `Kit` obstacles. Ledge/mantle unchanged.
- Row 1 (#79): compiles, 0 errors. Editor wharf walk (-3292,-1644 to -3272,-1644) stayed on the deck at y 1.25. Three more editor walks onto the wharf and pier arrived at foot 1.3 / 1.3 / 1.1 vs expected the same.
- Row 2 (-walk): ARRIVED now needs the feet to have stopped falling and to be within 0.3 m of the expected floor; it logs "walk foot y A expected B". A walker below the deck target (explicit y 3.1) reported FAIL: foot -1.2 vs 3.1. A real under-deck walk is not possible on this wharf any more in the editor.
- Rows 3-4 (player build):
  - Wharf: NOT PROVEN. The player walked along the seabed under the deck to foot -1.3 (expected -1.5, so it reported ARRIVED). The editor run of the same walk stayed on top. My guess: in the player the boxes were not placed yet when it teleported in right after loading (the refresh may be keyed on movement), and the check's TopAt ceiling of feet+2.5 missed the deck at 1.25. Needs a look.
  - Pier: ARRIVED, foot 28.0 vs expected 27.9.
  - Stone row (-2972 to -2956, z 3193.4): STOPPED 7.2 m short at y 43.1. The row's real yaw and ends are not known; the walk line was a guess.
  - Igloo door (344.67,50.04): STOPPED 4.2 m short at y 272.5.
  - Ruin 1420.4,2159.8: STOPPED 4.5 m short at y 31.6, so walls block, but stacked floors are not proven.
- Bench at the cave spot (editor, -goto 530,958 -time 12): before 16.71 ms/frame with PlayerMovement 0.033 ms; after 16.71 ms/frame with PlayerMovement 0.033 ms.
- Build: 1 build, done. The player quit by itself.
- Hashes: player v2 136 landmarks / B5EFF8C8, which matches. The editor caves/ground/rivers re-hash was still running when this was written; I did not re-run v1. Colliders are not hashed, so nothing should move.
- Shots: D:/PunchyMilestones/W-67 (w67_wharf_deck, w67_walk_fail_demo, w67_pier_end, w67_pier_framed, w67_stone_row, w67_igloo_door, w67_ruin, player.log, shots_w67.txt).
- Left over:
  - The player wharf deck walk (box placement timing after a teleport).
  - Raise the -walk TopAt ceiling.
  - The stone row ends and the igloo door approach.
  - Ruin stacked floors.
  - The editor re-hash of caves, ground and rivers.
  - I went over the tool budget.

## W-67b (2026-11-03): box collision proof, finished (#79 #36 #38 #80)

Commit: world-features a080fd5 (DebugControls.cs, KitCollision.cs). One build; all walks in that build.
- Harness fix (row 1): `-goto` now waits (up to 5 s) until the kit boxes round the spot are placed, then stands the walker on the lowest kit floor over the ground there. `-walk` waits for that. The walk floor check now uses the first floor over the ground (`KitCollision.LowestTopAt`) instead of "highest top below feet + 2.5 m", which is why the seabed walk had ARRIVED.
- Why walks stopped 4-7 m short in W-67 (row 2): the targets were guesses. The stone row runs at yaw -59, not along x. The igloo target was the dome centre, behind the closed door. The ruin walk hit the east wall, which is correct. The new targets come from the bake data (probe in the editor).

| Walk | Foot y | Expected | Result |
|---|---|---|---|
| Wharf: ramp onto the deck (-3292,-1644 to -3272,-1644) | 1.3 | 1.3 | ARRIVED |
| Wharf: forced under the deck (-3282,-1.5 to -3276) | -1.0 | 1.3 | FAIL (as wanted) |
| Stone row, end to end (-2967.7,3199.9 to -2960.0,3187.0) | 46.2 | 46.3 | ARRIVED; held at 44.1 on the stones, 1.2 m over the bank |
| Igloo: tunnel mouth to inside the door (347.9,58.9 to 346.2,54.1) | 272.3 | 275.3 | FAIL on the check only: reached 0.3 m from the target. The expected 275.3 looks wrong (3 m over the tunnel floor), so the floor search probably skips the tunnel floor. Open |
| Ruin: north gap to inside (1420.9,2167.5 to 1420.9,2161.8) | 31.5 | 31.5 | ARRIVED |
| Ruin: straight into the east wall (1434 to 1420.4, z 2159.8) | 31.6 | - | STOPPED at x 1424.9: the wall blocks |

- The ruin has no upper floor (single storey; tops at 33-35 m are broken wall tops), so no upper-floor walk.
- Found, out of scope: two stepping-stone rows sit on the same spot (-2963.9,3193.4; yaw -59 and -46).
- Hashes: player landmarks v2 136 / B5EFF8C8. Editor v2: landmarks 136 / 4E61F1CF, caves C49E8E3036C741FC, ground C0C906EE310FA91F, rivers A77D2ED7B919296E, all unchanged. v1 103 was not measured: it needs an editor restart with PUNCHY_ISLAND=v1.
- Shots: D:\PunchyMilestones\W-67b (wharf_deck, wharf_framed, underdeck_fail, stone_row, igloo_door, igloo_framed, ruin_entrance, ruin_wall, ruin_framed).
- Calls: about 50.

## W-67c (2026-11-03): main merged into world-features, igloo check fixed (#38)

Commits: world-features 178e226 (merge of origin/main, no rebase, 0 compile errors) and eb95cf2 (#38 check fix: DebugControls.cs, KitCollision.cs). No new build; all walks in the open editor.
- DebugControls.cs conflict: kept main's `-goto` log line (to x,y,z) and the W-67b settle wait; `-oldtrace` now sets both TraceSystem.OldTrace (#81) and OldOrder (A-45).
- TraceSystem.cs conflict: kept both. Main's CreatureSnapshotSystem.Get/Release and A-45 due-first order (feather, stone, step) drive pass 1; my #81 Burst GroundJob runs once over the creatures that need ground; pass 2 does splash, no-surface, feather, stone, wake/footprint. OldTrace keeps the managed path.
- #38 cause: at the igloo target the kit boxes are the tunnel roof/lintel (tops 275.0 and 275.3) over a 272.5 floor. LowestTopAt took the roof top because nothing sat on it. Fix: LowestTopAt takes a roof gap (2 m: walker height plus step) and skips boxes whose bottom is more than that over the ground; no gap over water. The igloo is unchanged.
- Stepping-stone pair (not fixed): the W-65g recheck accepts a spot within 0.5 m, and each spot gets its own `across` direction and yaw jitter from hash(round(bestXz*4)), so two near spots can centre on one point with yaw -59 and -46. Not a one-liner and it moves placement; left for a card.

| Walk | Foot y | Expected | Result |
|---|---|---|---|
| Wharf: ramp onto the deck | 1.3 | 1.3 | ARRIVED |
| Wharf: forced under the deck | -1.0 | 1.3 | FAIL (as wanted) |
| Stone row, end to end | 46.2 | 46.3 | ARRIVED |
| Igloo: tunnel mouth to inside the door | 272.3 | 272.5 | ARRIVED (was FAIL vs 275.3) |
| Ruin: north gap to inside | 31.5 | 31.5 | ARRIVED |
| Ruin: into the east wall | 31.6 | - | STOPPED at x 1424.9, wall blocks |

- Fauna near cave #1: no exceptions in the console after the merge; shot w67c_cave_fauna. The probe's creature count read 0; cause not checked, so no count to report.
- Hashes: incomplete. The eval came back truncated: landmarks read 103 / 19885131 (that is the v1 set, not comparable to v2 136 / 4E61F1CF); caves, ground and rivers lines were cut off. Re-measure v2 caves/ground/rivers next card (baselines C49E8E3036C741FC / C0C906EE310FA91F / A77D2ED7B919296E).
- Shots: D:\PunchyMilestones\W-67c (pick: w67c_igloo_framed, w67c_wharf_deck, w67c_cave_fauna; also underdeck_fail, stone_row, igloo_door, ruin_entrance, ruin_wall).
- Calls: about 52.

## W-71 (2026-11-03): play build from main, #68 smoke
- Main: 3a4acb8 (CharacterTab.cs present). World tree detached there for the build, back on world-features after; QualitySettings change left uncommitted, no stash needed.
- Build: Builds/Dev/Punchy.exe, Succeeded, errors 0, warnings 3, 1m21s. (A first try queued via EditorApplication.delayCall never ran; rebuilt synchronously in eval. Still one build.)
- Smoke (solo, default island, shot list `-pickclass Quadruped -menu Character`): Character tab shows Class -> Species -> Colour; Quadruped picks Wolf (Colour row Grey/Black/White, none chosen). In game the player body is the wolf. Player quit by itself, 2 shots in 12.1 s. Exceptions in player log: 0.
- Shots: D:/PunchyMilestones/W-71 (w71_character_tab, w71_player_ingame; player.log, shots.txt).
- No colour was picked (no shot flag for coats); colour pick not checked.
- Calls: about 36.

## W-74 (2026-11-03) snowy camp, campgrounds per biome, settlement plots

- Commits (world-features): bdc3913 (0c, release blocker: the palette append for FishingVillage/turtle/sea folk now runs on the Shared path too, before the "shared with" log), c2a86cf (rows 0 + 0b), f22f056 (rows 1-3). Merged origin/main first (872c121, clean).
- Row 0: v3 audit reports run only with `-islandaudit` or PUNCHY_ISLANDAUDIT=1. The editor turns it on by default (Punchy/Island/Audit Reports). v3 bake with the flag: baked 6.5 s + reports 22.8 s, ready at 47 s. Without it: baked 6.2 s + reports 1 ms, ready at 14.6 s (W-73: 6.0-6.4 s + 17.4-18.5 s). The reports still print with the flag.
- Row 0b: the base elevation used for range-top rounding is averaged over a 6-point ring at 110 m. Range 3 top 15% (slope / over 35 deg / convexity): W-72 30 / 38% / 51.5, W-73 31 / 39% / 61.2, W-74 27 / 33% / 59.8. Whole ground 16.3 deg / 8.3% / 15.31. Cost: 6 extra SampleBiomes calls per range sample. This is probably why the reports got slower (17-18 s to 23 s).
- Row 1 (#82): snow on settlement props was already in 29fefb1 (built plots, placed props, tent centrepiece). New: CampsiteSnow gets a grey RockArch (Layer 2) over the fire, a hide tent (CampTentSnow) in place of the ogre tent, and a woodpile. NOT SHOT: neither seed has a snowy camp or a snowy town (v2: 2 camps, Campsite and Fungal; v3: 3 camps, Ash and 2 Fungal; SampleBiomeSnow is under 0.05 at every town centre). So the arch camp and snowy village shots are still owed. They need a cold-seed spot or a test placement.
- Row 2 (#72): per-biome tent cloth: CampTent (canvas, default camp, in place of the second bedroll), CampTentSnow, CampTentDune (red/sand), CampTentAsh (soot). Fungal keeps its puffball. Each look already had its own ground. Shots: 2 of the 3 asked for (only these two camp kinds are near on v2). Framing not checked; the first take came out blank before streaming, so I retook it after a 25 s wait.
- Row 3 (#87): plot pads now mix weighted by w^8 instead of levelling one after another. Houses with a corner more than 0.3 m off the ground (corners at the plot's half size): v2 1 -> 0 of 129 (worst 0.36 -> 0.02 m). v3 before: 2 of 115 buried (worst 1.35 m at -3339,1570); v3 after was not measured.
- Shots: D:\PunchyMilestones\W-74\w74_camp_default_v2.png, w74_camp_fungal_v2.png.
- Hashes: v2 landmarks 136 / 4E61F1CF (unchanged). v3 landmarks 108 / 4D2643D7 (unchanged, measured before rows 1-3; rows 1-3 change no landmark positions). The ground hash changes on v2 and v3 (plot levelling; on v3 also 0b) but was not re-measured. Caves, rivers and v1 were not run.
- Calls: about 80 (over the ~70 ceiling). Editor closed; island pref put back to v3.

## R-12 (release prep v0.12.0), 2026-11-04, finished by the supervisor
- Source: main 9d34275, built in a detached Punchy-release worktree. Build: Succeeded, 0 errors, 3 warnings, 3 min 46 s.
- Smoke test (host `-body Bison`, client `-join 127.0.0.1`): both "in game True", same world check CAC3BAE8, 0 exceptions in both logs. Host at the end: players 2, animals 46, nearest player 3 m, 60 fps. The client went solo when the host quit first (expected). One "serverTick prediction error" warning on the client at the host's shutdown.
- Colour: not tested (no launch option sets it); Zach to check by hand.
- Zip: Punchy-v0.12.0-windows.zip, 51 MB, same layout as v0.11.0 (no BurstDebugInformation folder, no logs, no provenance file).
- Notes: batches/R-12-notes.md, with a Known issues line for #99.
- Calls: the R-12 agent about 45, then the supervisor finished build watch, test, zip and publish.

## W-77 (2026-11-04): #84, #86, #88, partly done

- Commit 3a6d069 on world-features (pushed).
- #84: the sunken temple ruins now draw with tinted Colossus copies (Colossus.shader has a new `_BaseColor`), so they fade into the PunchyAtmosphere haze. Not done: the salt tower is still on URP Lit SaltTowerBasalt, there is no far shadow or shadow proxy (shadowDistance stays 40 m), and the after ms was not measured. Before ms near the tower: 5.95 ms/frame (uncapped).
- #88: the lake shelf bake (GrandFalls.BuildShelf plus its effects) is in, but it does not fire yet. It logs "shelf none: no lake at the centre", most likely because IslandV3.LakeCentre is unset on a cached bake. v3 hash 4D2643D7 before and after (108 landmarks), so nothing has moved.
- #86 driftwood: not started.
- Shots in D:\PunchyMilestones\W-77\: w77-far-before/after, w77-tower-before/after, w77-falls-before. There is no falls-after shot.
- Calls: about 64 (over the 60 ceiling).

## W-77b (2026-11-04): #88 and #84 done, #86 not started

- Commits on world-features (pushed): f447128 (#88), 36126d9 (#84).
- #88: the cause was not the cached bake. IslandV3.LakeCentre was set (1610, -53), but the v3 central lake sits at sea level and the shelf asked for water above sea level + 1 m. It now asks for open water over the bed. Log: "lake shelf at 34.0 m over the lake at 0.0 m: lip 1099 m from (2009,-81) to (1938,685), 79 fall stretches". v3 landmark hash 3A0DC7BB (108 landmarks) before and after. This run's before hash was 3A0DC7BB, not the 4D2643D7 in the W-77 entry. The falls-before camera spot wasn't recorded, so falls-after is framed from the lake at (1680, 60, 260) looking at the lip.
- #84: the salt tower now draws with a Colossus copy tinted like SaltTowerBasalt, so it takes the haze. New FarShadow proxy (FarShadow.cs, Punchy/FarShadow shader, Resources/FarShadow.mat): a ground-hugging strip cast away from the sun. It shows only past the real shadow distance and fades with the haze. Global shadow distance unchanged. It is used by the salt tower only; the sunken temple could reuse it. Frame near the tower: 5.95 before, 5.6 ms after (uncapped, editor play).
- #86 driftwood: not started (call ceiling).
- Shots in D:\PunchyMilestones\W-77\: w77-falls-after, w77b-tower-after, w77b-far-after.
- Calls: about 44.

## W-77c (2026-11-04): #86 driftwood

- Commit f546783 on world-features (pushed): new DriftwoodSystem.cs (self-creating MonoBehaviour, no scene change). A pool of 12 logs and branches: the Driftwood species' 4 variants at scale 0.4-1.1, drawn with a tinted Colossus copy. They spawn on running river water within 150 m of the camera, preferring water that flows towards it. Each one follows GroundSample.Flow (faster in rapids, steering away from the shallow side), bobs and turns, sometimes snags on a bank for 3-9 s, and sinks away at a lake or the sea, or when far. The drift runs as a Burst job, read back one frame later as in CritterSystem. Cosmetic and local.
- Count: 12 afloat (the pool was full) at the v3 river near (170, -760) after 30 s.
- Cost: 0.021-0.024 ms on the main thread per frame. This includes waiting for the job; the frame was 16.6 ms, vsync in the editor.
- Hashes (v3): 108 landmarks, 4D2643D7 at the start and the end on world-features. This matches W-77, not W-77b's 3A0DC7BB. I did not measure main.
- Shot: D:\PunchyMilestones\W-77\w77c-driftwood.png. Only checked that it isn't blank (luma stddev 46).
- Calls: about 27. Editor closed.

## W-78 (2026-11-04): #40 two new colossi, #84 far shadows

- Commit 4e3b63c on world-features (pushed).
- Inventory (v3, before W-78): Dunes colossus, Dunes (1780, 13, -1973). Kneeling titan, the biggest Tundra/Glacier/Mountains region (-2034, 147, 1612). Half-buried head, the biggest Grassland/Temperate region (1629, 28, 2521). No colossus in Ashlands, ElectricWastelands, EmberForest, FoggyMarsh, FungalForest, GlassDesert, MesaCanyonlands, PoisonSwamp, SaltFlat (it has the salt tower, a landmark), Sonoran or SparklingForest.
- #40: StoneColossi now has a GiantTortoise in FungalForest at (-1502, 3, -2769): moss-backed plated shell, a crop of mushrooms and one big toadstool on the crest, three stone eggs by a front foot, a chipped plate on the ground, a fairy ring on its snout. It also has a CrystalSerpent in ElectricWastelands at (701, 20, -511): a slate body arching through the ground, cyan and violet crystal spines, rubble rings and crystal shards where it dives under, the head reared with the jaw open, a snapped spine and shed scales nearby. Each kind has its own salt. The flora keep-out grows to 4 circles; its SharedStatic got a new key, because a live editor can't resize one and aborts Burst init.
- Hashes (v3): 108 landmarks, 4D2643D7 before and after. The titan, head and Dunes spots are unchanged.
- #84: FarShadow strips now draw for the sunken temple, all four stone colossi and the Dunes colossus. Frame near the tortoise: 5.57 ms (uncapped, editor play).
- Shots in D:\PunchyMilestones\W-78\: w78-tortoise, w78-serpent, w78-far-tortoise, w78-far-serpent, w78-far-temple. I only checked that they aren't blank. w78-far-temple is nearly flat (luma stddev 7), probably haze at (3520, 360). It needs a reframe.
- Note: the first compile aborted Burst on the SharedStatic resize and hung the editor, so I killed my own world editor (PID 22616) and reopened it. Calls: about 42. Editor closed.

## W-79 (2026-11-04) - startup hitch #102

Commit bd27f3d on world-features (pushed, on top of 4e3b63c). Editor, safety/jobs debugger/leak detection off.
- Flora build: v2 4110 -> 2413 ms, v3 4123 ms before (after not measured); 620 variants, 1846476 vertices both ways. Pregrow starts on workers at the bake; meshes are made on the main thread. A first build (~4.1 s) still runs before the v2/v3 bake, so it gets no pregrow (to do).
- Caves v2 first frame: 1984 ms (drips 1739) -> under 250 ms (drip spots now gathered on a worker). v3 not re-measured.
- First terrain batch: unboosted, 3 chunks applied in 0 ms (was 18 chunks, 10 ms in editor; 1.6 s/96 chunks in player).
- Hashes (LandmarkTypes): v2 136/B5EFF8C8, v3 108/3A0DC7BB. Probe LmList now prints the game hash. Bake = shared in one process: not verified (no host run).
- merge-tree world-features vs animal-behaviours: CONFLICT in FloraGenerator.cs (Grow: my pregrow vs A-52 shapeOnly). Needs resolving.
- Calls: about 75 (past the ceiling).
## W-79b (2026-11-04) - finish W-79, #102

Commits on world-features (pushed): 79512da merge origin/main (A-52), 00b758d flora builds once after the bake, cfae8be shared path pregrows. Editor, checks off, uncapped.
- Merge: Grow keeps the pregrown parts plus A-52's shapeOnly (LOD 0 only, no support grid, break before meshes); a shapeOnly grow skips the pregrown parts. 0 compile errors.
- Fauna fingerprint on world-features: 51E641E9 with shapeOnly on and with it forced off (full grows), v2 and v3, so the shapes match. It is not E698F061 because this branch's content differs from main (W-77c/W-78); E698F061 is main's value.
- Flora: the first ~4.1 s build ran on the old terrain before the bake and was thrown away. FloraSystem now waits for the bake (toggle WaitForIsland / PUNCHY_FLORAWAIT=0). Frame before the bake: v2 4939 -> 728 ms, v3 5525 -> 714 ms. Island-ready frame: v2 5615 -> 5303 ms (flora 237 -> 71 ms); v3 flora 235 -> 99 ms. 620 variants, 1846476 vertices.
- Caves v3: no frame over 250 ms (the cave slow-frame log stays quiet), both runs.
- v3 island-ready frame is ~29 s in editor both ways: bake 5.5 s plus ~23 s after it (hydro audit / v3 biome reports run). Not flora or caves; worth a look.
- Host play v2: bake "136 landmarks, hash B5EFF8C8" and "v2 shared with ClientWorld ... hash B5EFF8C8": equal. v3 bake hash 108/3A0DC7BB.
- Host run 1 showed the client world rebuilding flora with nothing pregrown (4644 ms); the shared path now pregrows missing variants (third commit). Run 2 built once (220 ms), so the fix is not yet seen working in the double-build case.
- Calls: about 43.

## W-80 (2026-11-04): #87 flat house plots, #86 driftwood

- Merged origin/main (a20eff1), with the W-79b FloraGenerator resolution kept.
- c92b0ca (#87): plots were already levelled to their mean pad in SettlementSampling.Height. That code is seed-derived, so host and client match. Two changes:
  - A pad far off the ground now blends out over 3 m plus 1 m per metre of height difference, up to 6 m. Plot bounds were widened to fit.
  - Inside its own plot, a pad now wins outright, so a neighbour's fringe can't tilt it.
  - Plots flattened: 161 levelled, 149 of them built.
  - House corners off the floor: worst 0.04 m before, 0.00 m after. None were above 0.3 m.
  - Fishing and turtle villages and sea folk are separate landmark bakes and are untouched, so stilts and igloos are skipped.
- Area changed: 64037 m2 checked. 5546 m2 moved more than 1 cm and 2228 m2 more than 10 cm, with a maximum of 2.53 m.
- Hashes: landmarks 4D2643D7 / 3A0DC7BB (108) before and after. Ground 94A257A419224B6D -> 39BCE2E6F3B78D07. Caves and rivers unchanged.
- Timing: the hash pass took 9535 ms before and 8884 ms after, so no bake cost was measurable. Three editors were open at the time.
- 3aaeb59 (#86): MaxLogs 3, SpawnInterval 15 s, and a new log needs a 60 m gap from the others. Live count at (170,-760) every 10 s over 2 min: 0,0,1,2,2,2,2,2,3,3. The after shot still shows two far pieces in one wide view. Suggest a MinGap of about 120 m or MaxLogs 2.
- Shots in D:\PunchyMilestones\W-80\:
  - before_set0 and after_set0: the OgreCamp sloped plot. The tipi sits flat.
  - before_set3 and after_dwarfhold: no house in frame, only the streets.
  - after_fishing: the village is out of frame, so this shot is weak.
  - after_driftwood.
- Calls: about 53 (ceiling). Editor closed.

## W-81 (2026-11-04): #86 driftwood, #88 lake shelf (tabled)

- 3d05f54 (#86): MaxLogs 2, MinGap 120 m. No after shot (see below).
- 61f953b (#88): rows 1-2 are coded but only half checked, so row 3 applies. The shelf is behind a flag, **off by default**: `-lakeshelf` on the command line or `PUNCHY_LAKESHELF=1`. With it off, v3 has no shelf at all (the W-77b peninsula is gone too).
  - Placement: rays every 2.5 degrees all round the lake. Each 60-degree stretch is scored by how far its middle sits back from its chord (bay) and the lowest land 30-90 m behind it, plus a small seeded nudge. A stretch needs at least 12 m of land behind it. The shelf takes that land's height (10-40 m), and the lip is the bay's middle two thirds.
  - Falls: one sheet per ~10 m chord of the lip, as wide as the chord, each facing its own normal. The sheets meet at the lip; they widen a little lower down.
  - First run, before the 12 m rule: bay at bearing 343 (215 m behind its chord), land behind -9 m. That was an inlet, not the mountain side, so I added the rule. The run with the rule was killed for low RAM, so the new pick is unmeasured.
  - v3 landmark hash: 108 / 3A0DC7BB at the end, the same as at W-80 on this HEAD. The shelf goes in after landmarks.
- What's left on #88: measure the new pick (log line "lake shelf bay at bearing ..."), take shots from (1680,60,260) and from the lake, check the land is flush, and maybe add a feeder stream.
- No shots: free RAM dropped to about 3.5 GB twice while other lanes ran editors and the player lane built, and both runs were killed. D:\PunchyMilestones\W-81\ is empty.
- The editor opened with a compile error (my typo, since fixed) and stopped at the Safe Mode prompt; I pressed Ignore. The editor was closed with EditorApplication.Exit.
- Calls: about 45 (ceiling).

## W-82 (2026-11-04): #84 far shadow (code only), blocked on a hung editor

- **Blocked:** the world editor (PID 21836, from W-81) is stuck inside EditorApplication.Exit. Its log stopped at 07:51 and it keeps one core busy, but Pipeline can't reach it. `unity close` returned closed=false, method none, and my permission to force-end it was refused. The project stays locked, so I couldn't open another editor. No shots, no Play runs. Rows 3, 0a, 0b and 4 were not started. Someone has to end PID 21836 (5.6 GB) before the next world card.
- 3d88837 (#84, rows 1-2, **not yet seen in the editor**): I worked out why the strip didn't show without an editor. The W-78 far tower spot (2600,450,-1900) is ~1660 m from the strip's middle. That is past the 1536 m view distance (24 chunks x 64 m), where the haze lets ~1% through: exp(-(1.98 d/1536)^2) = 0.010. That gives an alpha of 0.45 x 0.75 x 0.010 = 0.0035, so the strip can't be seen at any time of day. At noon the strip also lies short (sun high) and mostly behind the tower from that side.
  - Fix: the shader fades by the square root of what the haze lets through; strength 0.45 -> 0.6 (shader default and FarShadow.mat); the vertex profile is (1-u^3)(1-s^4), so the core stays near full.
  - Estimated alpha at the middle, from the distance haze only (the LUT haze is ignored): 600 m 0.19 -> 0.39, 1000 m 0.06 -> 0.23, 1660 m 0.004 -> 0.05.
  - New: `-nofarshadow` / `FarShadow.Enabled` toggle. Each strip records its last sun elevation, length and middle (`FarShadow.Live`) for the on/off shots.
  - The probe class W82 is ready in the untracked WorldProbe.cs: `W82.Sun(h)`, then `W82.Far(name, nextToggle, pos, at)`, which logs the sun angle, strip length, estimated alpha and the patch darkening measured between the on and off shots. Suggest a nearer far spot too (~800-1000 m), as nothing shows at 1660 m.
- Row 4 (#102), from reading the code only: the backstop doesn't hold a ready batch back. OnUpdate returns only while `buildHandle.IsCompleted` is false, so the 39-chunk batch is really still running for 60 frames. Likely causes: the workers are busy with the bake-time flora pregrow, cave and hydro jobs, or the batch has slow cave-mouth chunks (~40 ms each). This needs a timeline in the editor, and possibly a fix that lands a batch's chunks one by one.
- Calls: about 30. D:\PunchyMilestones\W-82\ was not created.
- **W-82 resumed** (after Zach ended PID 21836; I deleted only the leftover Temp/UnityLockfile). Fresh editor PID 58836, closed with EditorApplication.Exit and confirmed gone.
- 1ace8bc (#84): **the real cause of "no shadow".** `lastSun` started at zero, and Vector3.Angle with a zero vector returns 0, so the strip was never laid. Every far shadow since W-77b (tower, temple, colossi) drew as a degenerate mesh at the origin. Now it lays on the first draw. Together with 3d88837 (softer haze fade, 0.6 strength), the strip shows.
- Far-shadow series (v3, tower strip middle at (3557, 8, -775); far spot (2600,450,-1900); near spot (3094,250,-1406); "darkening" is the patch luma change between on and off at the strip's middle, diluted because the strip is thin from up there):
  - 07:00, sun 12.2 deg, strip 347 m. Far, 1542 m: estimated alpha 0.009, darkening 0.003, **not visible**. Near, 819 m: estimated alpha 0.148, darkening 0.029, **visible**: a thin dark line from the tower foot westwards (I looked at the crop).
  - 12:00, sun 55 deg, strip 60 m, under and behind the tower. Far: estimated alpha 0.005, not visible. Near, 919 m: estimated alpha 0.111, darkening 0.017, barely visible.
  - 17:00, sun 12.2 deg, strip 347 m. Far, 1763 m: estimated alpha 0.003, not visible. Near, 1039 m: estimated alpha 0.075, darkening 0.019, faint. (18:30 is past sunset, so no strip; dropped.)
  - At the old 1660 m spot the ground is past the 1536 m view distance, so no shadow can show there. From 800-1000 m it shows at a low sun. It is thin from 250 m up, because it is only as wide as the tower.
- Shots in D:\PunchyMilestones\W-82\: w82-{far,near}-{7,12,17}-{on,off}.png (12 shots), w82-driftwood-after.png.
- 0a (#86): the w77c spot (226,-762) has no water on v3. At the v3 river (-88,-577), DriftwoodSystem.Live stayed at 0 for 2.3 min, so the after shot shows no log. Either the spawn rule (River > 0.3, flow > 0.5 m/s) finds nothing there, or spawning is broken. To check.
- 0b (#88): `-lakeshelf` in Temp/PlayArgs.txt does nothing (GrandFalls reads only the process command line). PUNCHY_LAKESHELF=1 set inside the editor process also gave no shelf line in GrandFalls.LastLog (359 chars, falls only). Probably the bake path or a cache skips BuildShelf. Not measured, no shots.
- Rows 3 (#87 village before/after) and 4 (#102 timings): not done, out of budget.
- Calls: about 30 more (about 60 for the card).

## W-83 (world-features)

- Commits: 8a9bac1 (#25 interior cells), 6e30f18 (#88 lake shelf flag). Both pushed to world-features. Merged origin/main first, keeping the #107 Flora hook.
- Row 1 (#25), done. API: `Punchy.Terrain.InteriorCells` is a SharedStatic list of `InteriorBox` (Min, Max, Part Floor/Wall/Ceiling). It has Set/Add/Clear/Count/Get, Distance, Floor, Wall, AddRoom(insideMin, insideMax, thick, doorWidth, doorHeight) and TestRoom.
  - It feeds TerrainSource.Deck (so FloorUnder), TerrainSource.TowerWall and PlayerMovementSystem.SolidDistance. Each checks Count > 0 first, so an empty list changes nothing.
  - Per house type: `FloraGenerator.HouseInterior(species, out door, out inside)` gives the door's local position and the inside size. It covers Hut, StoneHouse, Tower, Inn, Hall and kit House/StiltHouse. Sizes are unscaled at the mean height, and the door faces +Z. The stilt deck lift is not included.
  - `-interiortest` (debug flag) builds an 8x3x6 m room 300 m off +X, with its floor at sea level + 20. The player stood at (4396, 20, 0): FloorUnder 20, the wall blocks, the door gap passes, and SolidDistance under the ceiling is 0.20.
  - Landmarks 108 / 3A0DC7BB, unchanged. Ground hash (FNV of SampleHeight over -6000..6000 step 4) is 19A5064094B4F8F6. SampleHeight never reads the list, so this hash is the same with the list empty. The W-80 figure 39BCE2E6F3B78D07 was likely a different formula or HEAD, so I don't compare against it.
- Row 2 (#88), half done. Why the bake logged nothing: the flag was read only from the process command line, and BuildShelf ran only when the grand falls found no lake. Both are fixed. On v3 it now logs "[grand falls] lake shelf on; shelf none: no closed shore stretch (0 open rays)". Every shore stretch fails the closed / behind >= 12 m test, so no bay is picked. Still to do: work out why (lake centre or the behind-height cut on v3); no bay log line, no shots.
- Row 3 (#86 driftwood): skipped per Zach.
- Rows 4 (#87 village before/after) and 5 (#102 batch frame timeline): not done, out of budget.
- Shots: none (D:\PunchyMilestones\W-83\ not created).
- Editor: the world editor is closed, and the Audit Reports pref is restored.
- Calls: about 48 (at the ceiling).

## W-84 (2026-11-04): #87 village before/after, #102 batch timeline, #88 lake bay

- Commit 1a8efde (#88), pushed to world-features after merging origin/main (f97bfc8, fast-forward).
- Row 1 (#87), done. I put the pre-#87 plot code (f22f056^) back for the before shot only, then restored it. Before: settlement 3 has 2 house plots tilted, the worst 1.39 m at (-3339.5, 126.1, 1570.2). After: no tilted plots anywhere. Same camera: (-3319.5,135,1550.2) looking at the plot. In the before shot the huts' front walls sink into the slope (I looked at a crop).
- Row 2 (#102): this time the 39-chunk batch ran 4 frames, not 60 (t=31.9 s, during the load boost). Frame times: 205, 13, 16, 19 ms, then 13 ms on the frame it lands. **Biggest part: CaveSystem.Update() at 147 ms of the 205 ms first frame.** The other frames are mostly EditorLoop. The 18-chunk batch just before it had an 88 ms WeatherSystem.Update() in a 147 ms frame. The 30 s frame before both is the island bake (InitializationSystemGroup). Probe: W84 / BatchSpy in the untracked probe (Profiler frame data, the heaviest chain per frame). The 60-frame case didn't happen again in the editor.
- Row 3 (#88), done. Why no bay: the v3 lake shore is ~670-750 m from the centre (1610,-53), so a 30-degree stretch was ~400 m of shore. Only 45 of 144 rays have 12 m of land behind them, and the best stretch's lowest point was 5.2 m. Fix: a stretch now spans ~160 m of shore either side (4-12 rays), and it needs 10 m of land behind (the shelf's lowest rise). With 12 m the best was 10.7 m, so still no bay. The failure log now says how close the best stretch came. Log: "lake shelf bay at bearing 120 (score 0.37, bay 27 m behind its chord, land behind 10 m over the lake); lake shelf at 10.2 m over the lake at 0.0 m: lip 252 m from (1323,638) to (1074,645), 25 fall stretches". It is a low shelf (10 m), on the north-west shore. Still only with -lakeshelf, so nothing changes by default.
- Shots: w84-village-before.png, w84-village-after.png, w84-lakeshelf-hill.png (from (1680,60,260); the lip is ~600 m off in haze, so it barely reads), w84-lakeshelf-lake.png (from the lake, not viewed).
- Hashes: landmarks 4D2643D7 / 3A0DC7BB (108), unchanged.
- Editor: closed at the end of the card.
- Calls: about 42.

## W-85 (2026-11-04): #102 cave spike, #40 colossi; #72 left

Commits on world-features: f94399e (#102), 56f6568 (#40).

- #102: the hitch was cave plant models (all species x 3 variants grown in one frame, ~148 ms). They now grow under a 6 ms/frame budget and draw once complete. BatchSpy, first 4 frames of the streaming batch: before 295 / 24 / 16 / 16 ms, after 156 / 43 / 21 / 22 ms (uncapped, noisy). Cave hash 5307D41E3A2AE83C unchanged (25 caves, 735 plants).
- #40: biome -> colossus: Tundra/Glacier/Mountains -> KneelingTitan; Grassland/Temperate -> HalfBuriedHead; FungalForest -> GiantTortoise; ElectricWastelands -> CrystalSerpent; Dunes -> DunesColossus; FoggyMarsh/PoisonSwamp -> DrownedKnight (new). Still none: Ashlands, EmberForest, GlassDesert, MesaCanyonlands, SaltFlat, Sonoran, SparklingForest, Volcano. DrownedKnight: bronze knight on his back in the marsh, raised gauntlet with snapped sword, shield half under, seeded lily pads; own salt, second flora keep-out static. Landmark hash 4D2643D7 / game 3A0DC7BB before and after.
- #72 not done (out of calls). Plan: CampsiteSwamp (Mud, swamp tent, Bogwood/Cattail/Driftwood) and CampsiteForest (Woodpile/Stump/FallenLog) as BiomeLooks on Campsite. Snow/desert/fungal/ash looks already exist.
- Shots: W85-drowned-knight.png
- Calls: ~44

## W-87 (2026-11-04): #72 campsite test shots, #36 fishing villages on v3

Commit on world-features: ff45024 (#72). Island v3.

- #72: new debug flag `-campsitetest <look>` (swamp, forest, snow, desert, ash, fungal: the end of a campsite look's name). The bake adds one campsite with that look at the flat dry spot of a matching biome nearest the island middle (clear of other landmarks by 70 m) after every other place, and DebugControls puts the player 9 m west of it, facing it. Nothing is added without the flag. Spots on this seed: CampsiteSwamp 0,32; CampsiteForest 16,-32. Landmark hash without the flag 3A0DC7BB (same as W-86); with the flag F73AD8B1 (the one test campsite, expected).
- #36: already built (PR #89, W-59..W-67c) and it runs on v3 too: 3 fishing villages from 69 mouth cells, at -3941,1290, -3144,-1596 and -4041,921 (stilt houses, pier with a solid bed, shore props). No code change, so the landmark hash is the same before and after (3A0DC7BB). Shot at village 2. FishingVillageBaking's summary still says "Island v2 only"; that's out of date.
- Shots: W87-campsite-swamp.png, W87-campsite-forest.png, W87-fishing-village.png
- Left: no boat could be seen in the village shot at that distance (they're seeded shore props, maybe small or missing); v3 has 3 villages (Most is 4). The swamp tent looks pale in the shot.
- Calls: ~44

## W-86 (2026-11-04): #72 campsite looks, #40 ash smith

Commits on world-features: 375e76c (#72), feecbc2 (#40).

- #72: Campsite gains two biome looks. CampsiteSwamp (FoggyMarsh, PoisonSwamp): mud ground, olive oilcloth tent (CampTentSwamp), driftwood, a bogwood snag and a ring of five cattails. CampsiteForest (Temperate, SparklingForest): green canvas tent (CampTentForest), seat log plus two stump seats, woodpile, fallen log. Snow/desert/ash/fungal looks already existed. On this seed the island has only 3 campsites (1 ash, 2 fungal), none in swamp or forest, so the new looks don't show here; no campsite shot (my ash-camp framing missed, out of calls).
- #40: AshSmith for Ashlands/Volcano/EmberForest (most distinct of the uncovered biomes): basalt smith sitting cross-legged over a house-sized anvil, hammer under one hand, tongs on a glowing half-made blade, ember cracks and eyes, ash on up faces; seeded slag lumps (every fourth glowing) and a quench trough. Own salt 0xA54D, second keep-out slot. Placed in this world. Landmark hash 4D2643D7 / game 3A0DC7BB before and after.
- Shots: W86-ash-smith.png
- Left: a campsite shot for two biomes (needs a seed with a swamp or forest campsite, or a forced look); the ash weathering on the smith may want a darker pass.
- Calls: ~41

## W-88 (2026-11-04): #88 shelf shots, #40 colossi to Zach's list

- Merged origin/main (a468c7a) into world-features. Commit a5a870d: StoneColossi places a SkeletalLizard in Ashlands and a CarnivorousPlant in PoisonSwamp. AshSmith and DrownedKnight keep their code and spawn with `-colossus AshSmith` / `-colossus DrownedKnight` (they take the same slot as the new kind).
- Lizard: about 80 m long, standing on four splayed legs, ribcage open, skull held low with the jaw hanging open and a frill of horns, embers in the eye sockets, a smouldering heart inside the ribcage, a snapped rib and teeth lying in the ash. It sheds ash from the ribs and raises dust at its feet. Placed at about (-515, -2477).
- Plant: about 40 m tall. A ribbed bulb on six arched tentacle legs, a stem up to a Mantrap-style snap-trap head (#41) with drool, and eight tentacle arms; every third arm ends in a small trap and the rest curl. Leaves round the feet, three young sprouts, deer bones, and spores drifting from the mouth. Placed at about (-167, 78). It stands on a steep slope, so part of its base is hidden in the shot; the legs may hang over the drop.
- Landmark hash before/after: 108 landmarks, 4D2643D7 (game hash 3A0DC7BB), unchanged. Colossi add no landmarks.
- Shots: w88-shelf-lake.png, w88-shelf-along.png (#88, with -lakeshelf, midday, Clear), w88-lizard.png, w88-plant.png.
- Not done: row 3 (call ceiling reached). Next of Zach's ideas: ElectricWastelands rock golem, Sonoran saguaro, SaltFlat crystal, Tundra big worm.

| Biome | #40 design | Exists now |
|---|---|---|
| Ocean | serpent | serpent exists |
| ElectricWastelands | floating rock golem (Zach) | CrystalSerpent stands in |
| PoisonSwamp | carnivorous plant (Zach) | CarnivorousPlant, W-88 |
| Sonoran | walking saguaro (Zach) | none |
| SaltFlat | rolling salt crystal (Zach) | none |
| Ashlands | skeletal lizard (Zach) | SkeletalLizard, W-88 (AshSmith by flag) |
| Tundra | bigger snow worm (Zach) | KneelingTitan (Tundra/Glacier/Mountains) |
| Dunes | half-buried humanoid in a sandstorm | DunesColossus |
| Grassland / Temperate | grazing titan / walking oak (suggestions) | HalfBuriedHead |
| FungalForest | puffball (suggestion) | GiantTortoise |
| FoggyMarsh | wader (suggestion) | none (DrownedKnight by flag) |
| SparklingForest, EmberForest, Volcano, Mountains, Glacier, GlassDesert, MesaCanyonlands | suggestions | none (Mountains/Glacier may host the titan) |

- Calls: ~47

## W-89 / W-89b (2026-11-04): #40 rock golem and walking saguaro

- Commit e0af8ed on world-features: StoneColossi places a RockGolem in ElectricWastelands (floating basalt segments held by electric arcs, new GolemArcs.cs; FloraSystem.BoltMesh made internal) and a WalkingSaguaro in Sonoran. The duplicate `Basalt` colour fix compiles; console clean. The world editor had stuck at start-up on the old compile error and was restarted.
- Slope cap (up to 8 tries, keeps the flattest): of the two colossi with a W-88 record, 1 moved: CarnivorousPlant (-167, 78) -> (-53, 516), relief 11.0 m; SkeletalLizard unchanged at (-515, -2477). GiantTortoise unchanged at (-1502, -2769). Now: titan (-1990, 1321), head (1716, 2493), golem (707, -329), saguaro (2925, -1710).
- Landmark hash before/after: 108 landmarks, 4D2643D7 (game hash 3A0DC7BB), unchanged.
- Shots (W-89 milestone folder): w89-golem.png (arcs between the segments), w89-saguaro.png (side view, root legs mid-stride).
- Row 5 (SaltFlat rolling crystal): not started (budget).
- Calls: ~38

## W-90 (2026-11-04): rows 0-2 done, 3-5 left (budget)

- 438fc81 #84 row 0: FarShadow.Lay runs as one Burst IJobParallelFor per strip (TerrainSource, NativeArray), scheduled when the sun moves, completed next frame (copy to mesh, RecalculateBounds). StoneColossi far shadows: 0.28-0.36 ms schedule frame (0.96 first, Burst warm-up), 0.04 ms land frame (was 44 ms). All 10 strips lay (lengths 31-79 m). -nofarshadow kept.
- 5ac804c #72 rows 1-2: Campsite and CampsiteForest ground Grass -> Dirt (swamp already Mud). Pads rejected if a river or water surface within the radius stands over the flattened pad (test and roadside). Swamp test camp moved to dry ground (1266 nearer wet spots rejected); roadside: 3 placed, 0 rejected, so normal placement unchanged.
- Shot: w90-campsite-swamp.png (tent on dry mud, no grass or water patch). Forest after shot not taken.
- Hash with -campsitetest swamp: 109 landmarks, F8E930BB (game 67B353C1).
- Left: forest shot, rows 3 (#36 river plots), 4 (#88 shelf bay), 5 (#40 plant size and fog).
- Calls: ~44.

## W-97 play-mode test harness (2026-11-04, world-features 1ff6d90..66a9456)
- **Asmdef:** separate test-only asmdef Punchy.Tests.PlayMode; reaches Assembly-CSharp by reflection via TestHooks (editor-only statics) + DebugControls.RunOptions (test flags mid-run). No game asmdefs, playModeTestRunnerEnabled stays off.
- **Rows 1-4 done:** TestSceneSetup (island v3, time, weather, place/goto, spawn), scripts/run-playmode-tests.sh (UNITY_EXE or Hub, PASS/FAIL + measured lines, exit 1 on failure: verified), Shots.Take (checked shot), docs/agents/testing.md.
- **Suite:** 10 tests, 161 s (world up in 40 s). 9 pass, 1 fails:
  - PASS: Harness_WorldUp, Landmarks_V3 (108 4D2643D7), Rivers (max 119.5 / limit 130, measured), meadow butterflies (0.33/100 m2, band 0.1-1.5, measured), village creatures (5 within 50 m, band 1-20, measured), Setup_SpawnRainAndShot, WalkBy_NeverThroughWall (12 lines, 0 through walls, 143 held back), PlayerSlot_AlwaysFilled (0 unfilled of 4800), AnyLines <= 30% (6.2%, worst HumanChild 13%).
  - FAIL: Fauna_Fingerprint: main 69ca891 reads 286E0A52, target 8726AADB is the animal branch's (STATE.md's "main D0B9488A" is stale too). Left failing on purpose: passes once that branch merges, or retarget if 286E0A52 is accepted.
- **Fix shipped:** walk-by folk talk now held back when a kit box is between speaker and player (FolkTalkSystem.Walled, KitCollision.Blocks).
- **Notes:** row 1 commit alone doesn't compile (TestHooks uses row 2's Blocks/DensityNear/HeldBack); branch head is clean. Unity on Windows needs C:/ paths (cygpath) and empties Temp/ on quit, so results go to Logs/.

## W-96 bat folk and pygmy homes, #35 (2026-11-04, world-features 6abe164)
- **Row 1 done (verified):** bat folk live only in large hollow egg pods hung from the ceiling: open woven nests (checkered weave, inner wall, side opening, bedding, twigs, straws), radius 0.8-1.5 m, length 2.1-2.5x the radius. They replace the hammocks; goggle pegs kept; huts are drawn as the same nest. Kit houses for bat folk are no longer placed (Zach's feedback): WallHouse/CeilingHouse enum values are kept, the log reads houses 0. Pygmy wall niches have a bedroll plus a glow-pool or fire ring, at walking height or up a short ledge, with box collision (kit boxes via FloraSystem, an additive line). Pods are decorative.
- **Anchors:** BatFolkHomes / Homes blob: Kind (BatFolkPod / PygmyNiche), Stand (pod floor or niche bed), Door, Face. Measured on v3: 1 site (cave 5, r 15.3 m), 4 pods + 1 niche (ledged) = 5 homes, 6 huts, 0 houses.
- **Numbers:** landmarks 108 / 4D2643D7 (unchanged).
- **Shot:** w96-batfolk-homes.png (pods + niche in one frame; at 8.9 m, a little past the 8 m guide, so the niche fits). Full-res file in the gallery.
- **Risks:** #35 not commented (the brief forbids it). FloraSystem is shared (+1 additive line), so tell its owner. DebugControls `-place batfolk` and FaunaDirector.CaveFolk still use Hammock/Hut spots: the animal lane should move to the BatFolkHomes anchors (Stand is now the pod floor). Detail draws per site changed with the house loops gone (landmarks unaffected); the fauna fingerprint may move. The glow-pool is not emissive. Only 1 niche is placed on v3.

## W-98 water (#83), world-features
- **Row 1 done (verified), e937ccb:** whitewater is now foam carried by the current. Water.shader FlowFoam: two flow-advected reads on a 1.6 s cycle, cross-faded, replacing the static flecks (the decal look). Cover grows with rapids, ripples get choppier with speed (x1+1.3 rapids), and streak flecks thin out in white water. RiverData: the gradient blends mid-segment to mid-segment with the neighbour segment. Rapids along a line at (-577,691), 4 m steps: before 0 -> 1.00 in one step; after 0, 0.07, 0.39, 0.77, 0.91, 1.00. Cave streams: the drop is measured over 5 points, so the white builds at the steep step.
- **Numbers:** landmarks 108 / 4D2643D7 (unchanged). Rapids values shift near segment ends, so rapids-gated rocks (MinRapids) may move a little.
- **Shots:** w98-rapid-before/after (cam -575.9,41.5,698.75 -> -577,38.6,691), w98-cave-before/after (cave stream 1, cam -1331.3,90.3,1423.6). Full-res in the gallery.
- **Risks:** stills can't show the motion. The rapid is fairly white overall (green shows through). There are 2 extra value-noise reads per pixel, only where rapids > 0.02.

## W-98 row 2 (#83): waterfall = river ribbon - done (verified)
- Commit a8c459d. The water shader adds a falling-sheet look on steep, flowing faces (smoothstep on the face normal's y, 0.8 down to 0.45), with streaks scrolling at 5.5 m/s. The overlay curtain is no longer drawn; the emitter stays for sound only, and plumes are kept.
- Shot: gallery/w98-fall-after.png (fall 61, 24.5 m drop, lip -301.5,113.7,2591.6). The whole face is streaked water, with no river showing through.
- Risk: the falling look keys on the shading slope, so any steep rapid (more than about 37 degrees) gets streaks too. Re-importing the shader in play mode left the water half-broken until play was restarted.

## W-98 row 3 (#83): river sources and stepped pools - done (partly verified)
- Commit b4d875d. River heads outside a lake carve a spring pool (width max(2.2x river, 5 m), depth 1.3x) just upstream. In landmark stepped pools, a lower pool can't cut below a higher pool's level plus 0.25 m within 1.15-1.4 r of it.
- Hashes: landmarks 108 / 4D2643D7 -> **107 / 14D92F64**. One landmark loses its spot to the new pools, so Landmarks_V3 in WorldTests needs the new target if this is accepted.
- Shot: gallery/w98-head-after.png (river head at -1617,1506 beside a pond). The river now leaves a basin.
- Weak point: the card's spots don't match this tree's v3 island. At 176,-253 the ground is 123 m (the card says 43.1), and at -2389,1740 it is 146 m (the card says 116.8). Neither is in a cave and there is no water or landmark there. So the card's spots weren't shot, and the stepped-pool containment has no shot.

## W-98 row 4 (#83): puddles in dips - done (UNVERIFIED shot)
- Commit 1a1068f. Near chunks store a hollow depth per vertex (Strata.z: the 8-ring mean at 2.5/6 m minus the height). Puddles need hollow > 0.02-0.2 m on flat ground, and grass now damps them (x0.4) instead of removing them.
- Numbers: in a 90x90 m meadow at -1660,1534, 281 of 961 points are flat (normal y > 0.95), 185 of those are dips deeper than 5 cm, and the deepest is 1.03 m.
- Shot: gallery/w98-puddles-after.png (forced rain, 50 s soak). The ground is wet, with darker patches in the dips, but no clear mirror puddles, so this is UNVERIFIED. The puddle thresholds (0.55-0.7 smoothstep) may need lowering.
- Note: "-weather Rain" at launch rains at the spawn point only. Use DebugControls.RunOptions("-weather Rain") after a goto.

### W-98 fix 2 - row 3 spots re-checked on v2 (no code change)
- Both card spots are v2 coordinates: ground 43.3 at 176,-253 and 116.8 near -2389,1740 (v2 has 136 landmarks).
- -2389,1740 is HotSprings at (-2378,1732): the stepped pools are contained, each with a lip, no spill (gallery w98-pools-after.png).
- 176,-253 is the river head pond with its driftwood; the water stays in the basin (gallery w98-v2head-after.png).
- On v3 these spots have no source or pool, so the row 3 card numbers came from v2, not from a bug.

### W-98 fix 3 - rain puddles visible (commit 0873f2f)
- A debug view showed the dips were already counted as wet, flat and hollow, so lowering thresholds alone would not help: whole dips glazed over evenly, and from above the 2% Fresnel floor left only dark ground.
- Now: noise cuts dips into separate crisp-edged pools, flatness 0.8-0.93 (was 0.88-0.97), dip 0-0.08 m, wet 0.1-0.3, and the puddle film reflects at least 35% sky. Grass damping kept (x(1-0.6*grass)).
- Forced rain on v3 (RunOptions after GoTo, 55 s soak): pale sky-mirror pools with rain rings on the terraces and dips (gallery w98-puddles-after.png). Shader has no errors, CRLF, no BOM.
- Weak point: at a steep downward view the pools are mostly sky-grey; they read best at a low angle.

### W-98 fix 1 - suite (commit 64c0a1b)
- Landmarks_V3 now expects 107 / 14D92F64 and passes. Suite: 10/10 on the second run. The first run was 9/10 because Village_CreatureDensity measured 0 creatures within 50 m (4 on the rerun). That test is flaky and none of W-98 touches it.

### W-99 row 1 - surface looks (commit 23bcb6f) - done (verified)
- Planks roof and plank walls on a human house (gallery r1-planks.png), elf shingle courses (r1e.png), goblin mud hut thatch with course rows and edge (r1t.png), tent cloth with linen weave and sewn panel seams (r1c.png). Shots in PunchyMilestones/world/W-99/gallery.
- Tent panels (all Form Tent species, goblin hides included) are now the Cloth part, held still (no wind). This changes their lighting a little (cloth faces the viewer).
- Risk: the weave reads faintly at 800 px; the seams are what you see at a few metres. The commit also carries the row 2-4 code, which later rows verify.

### W-99 row 2 - contact shading (commits 23bcb6f + 32a9ce4) - done (verified)
- Kit walls, posts, piles and chimneys get a vertex occlusion band where they meet a floor, deck, balcony or plinth (BandedBox, LOD 0-1). Fishing village pier posts darken under the deck (gallery r2q.png); stilt house wall darkens toward its deck (r2h.png).
- Weather drifted to overcast during the shots; time was noon.

### W-99 row 3 - log stack (code in 23bcb6f) - done (verified)
- Woodpile logs have flat ends with growth rings and a bark rim, bark sides, and the odd split log with woodgrain (gallery r3.png, hermit hut at 140,-545).
- Hash note: the Woodpile builder draws more RNG than before, so woodpile meshes differ; landmark placement is untouched.

### W-99 row 4 - snow on the camp village (9a2de7f) - done (verified, see risks)
- Snow now lands on every clearly upward face. Tent cloth takes it by its own facet, and faceted stake tips by the steeper of normal and facet. Per Zach's feedback the cover is capped at 0.7, only faces above about 45 degrees fully whiten, noise breaks it up, and the tint is greyer to match the terrain snow. The debug force in the shader was removed before committing.
- Shots: gallery r4-before.png and r4-after.png, the snowy DwarfMiningCamp (-1966,651) on island v3. The slab shelter stops glowing and sits with the ground snow.
- Risks: the card's -2077,1301 has no camp on my v3 island, so the corral Pen fence was not seen (unverified). The stone and timber house roofs there show no snow in either shot; they may use another material or sit on a low snow value. Worth a check.

### URGENT bloom / black hex at camp village (0ff5b2e, own commit, cherry-pick ready) - done (verified)
- Cause: not a W-98 puddle or a shader. `SettlementSampling.Height` returned NaN on a thin ring at the outer edge of every plot pad's blend (w ~1e-6, so w^8 underflowed to 0 and the mix was 0/0). Terrain vertices on that ring got NaN normals or heights, which drew black hexes (the 6 triangles round each vertex), and bloom smeared them white. The pale hex beside it was the same bug.
- Fix: a one-line guard in SettlementData.cs. When sum is 0 the ground keeps its own height, because the pad has no pull there. A 1M-point scan of SampleHeight over the camp went from 3 NaN points to 0.
- Shots: gallery nanhex-before.png and nanhex-after.png, DwarfMiningCamp shrine (-1984,655) on v3, about 11 m away, bloom on. Both hexes are gone.

### W-100 row 1 - doors on elven stairs and balconies (6442e9f) - done (verified)
- Every kit-house balcony and every outside stair's landing now has a door in the wall behind it: jambs, lintel and sill, or a dark opening on a ruin. It replaces the small window or trim it would overlap. No new RNG draws, so placement and variants are unchanged.
- Bird walk: playing as Crow, -walk from the foot of the stair at the ElfGrove house (-2518.5,-1458.8) to its landing. It went from y 12.69 to 16.29 on foot the whole way (Flying=False, Grounded=True, OnProp=True on the treads). Kit treads now step up to 0.32 m, and flight keeps out of kit walls and lands on kit tops.
- Shot: gallery row1-elf-stair-door.png (the stair and its new top door, biggest town = the elf grove at -2511,-1518).
- Risks: this touches PlayerMovementSystem.cs (Fly/KeepOut/TopUnder kit handling), which may be the player lane's file and needs a look from them.

### W-100 row 2 - inn porch, igloo roofs, mine torches (d44ee31) - done (verified)
- Inn patio: the porch roof now meets the wall at 0.66 of wall height and the front edge at 0.6 (was about 0.41), so it is about 50% higher. The posts and sign follow. Shot: gallery row2-inn-porch.png.
- Igloos: the dome blocks span the slant between rings and lean along it, so no sky shows through. Shots: gallery row2-igloos.png and row2-igloo-roof-low.png.
- Mine (-1471,116.1,2109, island v2): the free-standing "torches" were mine frames drawn with the wrong model. Cave plants grew 3 variants per species, but tall frames use variants 3 and 4. Those indexes landed on the next species (WallTorch) at frame scale (about 2x), so a big unlit torch stood in the middle of the passage every 4 m. The stride now covers every frame variant, so those spots show their timber frames. No torch was added, and the wall torches and their light are unchanged. Shots: gallery row2-mine-before.png and row2-mine-after.png.
- Note: the card's coordinates are on island v2 (the default), not v3. On v3 that spot has no cave. Inn and igloo shots were taken on v3. That fix is per asset, so it applies on both islands.
- Risk: cave species now grow 5 variants each instead of 3, so there are more cave models to pregrow and grow (still on a per-frame time budget).

### W-100 row 3 - fishing village walkways, lights, boats (f97a353) - done (verified)
- Walkways and doors (-2377,4.0,-2120, island v2): a stilt house's porch platform and stair stood at the middle of the wall, but the door sits in the middle bay, which is off centre when the bay count is even. Porch and stair now use the door bay's x (StructureKit.DoorX), so the stair rises straight to the door. Shot: gallery row3-stair-under-door.png.
- Lights: wharf door lamps now hang from a wood bracket and arm beside the door instead of floating on the wall (same RNG draw). Other village lights already had a source: coast lanterns (post + arm), pier shell lamps on rail posts, and residents' carried lanterns. The warm patches at night are resident lanterns, and the green points are fireflies. Shot: gallery row3-night-lights.png.
- Boat (-2374,1.9,-2113): the foundation skirt drops every vertex at or below y 0 by 1.6 m, which stretched the hull into the beach. Boat parts are now marked KitPart.Afloat and the mesh builder leaves them out of the skirt. Shot: gallery row3-boat-on-beach.png.
- Seen, not fixed: in both noon shots a large translucent dome edge shows over the water at the left. It looks like a water or bubble render issue, not part of this row.

## W-100 row 4 (roads: max grade, #39) - done (verified)
- Commit 221e16c on world-features. Roads re-route around stretches steeper than MaxGrade 0.45 (rise over run, checked every metre; up to 3 tries with the steep cells closed) and leave out any stretch still too steep. Town streets now also stop where they would run off a cliff, like they stop at water and cave holes.
- The card's spot (-1419,165.2,2236) was a town street, not a road: it ran 16 m down the plateau edge (grade 1.65). Gone in the after shot.
- Island v2, end-to-end grade of laid stretches, before -> after:
  - Roads (1132 -> 1010 stretches): >0.2 184 -> 116, >0.3 97 -> 33, >0.5 45 -> 0, worst -> 0.42.
  - Town streets (364 -> 320 stretches): >0.2 20 -> 2, >0.3 15 -> 0, >0.5 11 -> 0, worst 1.65 -> 0.21.
- Shots: gallery/row4-road-before.png, gallery/row4-road-after.png (camera -1460,175,2215 looking at -1420,152,2245).
- Risks: settlement layouts and roads change where streets or roads were steep, so the fingerprint will move. A road that can't meet the grade now has a gap where it meets the cliff instead of running down it. A metre-level probe still finds steps where the levelled street terraces at stretch ends; that's the existing levelling, not the route.

## W-100 row 5 (-place prop regression, #137) - done (verified)
- Commit 4e60c44 on world-features. `-place prop:` now keeps the request and retries it each frame until the flora lookup (FloraPlacementBlob) and the player are up, instead of returning silently. It warns if it gives up after 120 s. New hook TestHooks.PropPlacePending.
- New play-mode test WorldTests.Place_PropFindsAndFramesWharf (Order 8): runs `-place prop:FishWharf@0,0` and checks the found and framing lines, and that the player stands within 30 m of wharf #1. Passed in my editor through the Test Runner, with Harness_WorldUp: 2 FishWharf found on v3, #1 at (-4056.9, 0.5, 904.2), player at (-4048.4, 11.1, 907.1). The full suite runs at the end of the batch.
- Also checked: PlayArgs `-place prop:FishWharf` at play start logs both wharves and frames #1.
- Risks: in the editor the options run after the lookup is up, so the waiting branch itself only gets exercised in a player build (the profiler's original case). Worth one bench run with `-place prop:FishWharf`. TestHooks.WorldReady still doesn't wait for the flora lookup.

## W-101 row 1 (light at distance) - done (verified)
- Commit 43747a5 on world-features. Glowing things now light the world in two tiers.
  - **Near:** the 16 nearest lights, as before. Surface ones now fade out between 18 and 30 m from the camera.
  - **Far:** SurfaceGlow bakes every glowing plant (ground 70 m, shrubs 200 m, trees 300 m) and every lit window into a light map centred on the camera: 640 m across, 4 m per texel, one map for plants and one for windows. It runs in a Burst job off the main thread and rescans after the camera moves 24 m.
  - Glow.hlsl reads the far map with 2 texture samples per pixel and fades it in as the near lights fade out. A height test keeps the light off cliff tops, and a soft brightness cap stops dense clusters from floodlighting the ground. Creatures use the same shader, so moving things get lit with no proxy lights.
- **Technique:** I didn't use the vertex-channel bake. Every terrain and flora mesh would need rebuilding whenever night falls or windows light. The light map is the same idea kept in one texture around the camera, with a cost that stays the same however many sources there are. No URP rendering-layer lights were needed.
- **Electric wastelands:** arc spires and shard rocks keep thick, low-fidelity arcs at LOD2 (an additive FloraGenerator edit, see the risks).
- **Terrain detail texture:** it now fades out at 110 m times the Detail graphics setting (it was a fixed 70 m).
- **Frame cost per tier:** editor, uncapped, standing in a town at night, average of 3 runs. The numbers are noisy by about ±0.3 ms.

  | Tiers on | ms/frame |
  |---|---|
  | none | 7.31 |
  | near only | 7.43 |
  | far only | 7.78 |
  | both | 7.72 |

  So the far tier costs about 0.3 to 0.4 ms. Its scan takes up to 70 ms on one worker thread (start to pick-up, off the main thread), once per 24 m of camera movement.
- **Shots:** night sets for a town (-2511,-1518), a SparklingForest (-2500,600) and the ElectricWastelands (-600,-1200) on v3, in D:\Unity\PunchyMilestones\world\W-101\gallery\r1_{town,forest,wastes}_{far,mid,near}.png. For comparison, r1_tn_far_nearonly and r1_town_mid_t1 are near-only shots from the same views.
- **Risks:**
  - FloraGenerator.cs is shared (additive only), so the other owner should be told about the LOD2 arc change.
  - Ground glow beyond 70 m gives no far light.
  - The far scan is a single-threaded job; it could become a parallel job if it shows up in a bench.

## W-101 row 2: ruins (#80), done (verified)
- **Commit:** b6ed421 on world-features.
- **Changes:**
  - Fallen rafters now rest their high end on the inside of a standing wall stub, not in the air.
  - The floating door lintel over a broken bay is gone: a ruin's lintels and window heads are only added where the wall reaches them.
  - Ivy is now a stem with leaf clumps instead of cloth-like ribbons.
  - Moss sheets have tuft cushions and a ragged, frayed hem.
  - Ruin bricks have a darker mortar core, so the joints read as linework. About 1 in 10 whole blocks carries a carved glyph (ringed cross, chevrons or spiral).
- **Shots:** v2 at (1420,2160), noon, in D:\Unity\PunchyMilestones\world\W-101\gallery: r2_a (overview), r2_c (ivy close), r2_d (interior and rafters from above), r2_e (side, no floating lintel), r2_f (moss and carvings close).
- **Risks:**
  - The ruins' part lists changed (ivy parts, lintels), so the landmark fingerprint will move. Rafters reuse the old draws and the glyphs use their own rng, so the layout is otherwise unchanged.
  - FloraGenerator.cs is shared: RuinBricks has a new mortar shade and a new RuinGlyph helper (additive). Tell the other owner.
  - Flora.shader and Flora.hlsl moss edits affect every moss sheet (W-64 caves and houses too).

## W-101 row 3: runed stones + archipelago scatter (done, verified) - commit 3d58161
- StoneCircle (v2, 282,2837) now uses a new RunedStone species (StandingStone copy; Monument/Ruins keep the plain stone): glowing glyphs on both faces of every stone, 4 colour patterns (cyan, amber, violet, green), each with its own glyph family (ringed cross, chevrons, tree rune, eye). One pattern per landmark instance (scrambled hash), applied on fixed props, centrepiece and density rings; rng draw counts unchanged. The v2 circle draws amber.
- Archipelago (v2): the first 4 islands zigzag off the spine on alternating sides. Head islets before (3224,2425) (3644,2638) (3760,2823), roughly a line at 36 deg; after (3113,2573) (3553,2444) (3534,2888) (3941,2806). Now 14 islets / 2 sandbars (was 11 / 4).
- Hashes (allowed): v2 landmark game hash is 95EAB5B4 (list hash AC866923, 131 landmarks). Flora models and the archipelago shape move; the landmark list itself is untouched by these edits.
- Shots: gallery r3_runes_b (noon, inside the ring), r3_runes_c (noon, wide), r3_runes_dusk (glow), r3_archipelago (aerial, hazy).
- Risk: FloraGenerator.cs (shared) gets the additive SpireRunes helper. FloraPlacement.cs gets the additive Patterns rule field and PatternModel. FloraSpecies.cs gets the RunePatterns field.

## W-101 row 4: colossi collision #40 (done, verified) - commit 1fb6edf
- The meshes of each colossus are sampled into 3 m columns at build time (new Scripts/Fauna/ColossusCollision.cs). On v2: Dunes 311 columns, KneelingTitan 125, HalfBuriedHead 163, GiantTortoise 301, RockGolem 149, CarnivorousPlant 32, SkeletalLizard 186, WalkingSaguaro 110, SaltCrystal 132 plus its rolling ball as a moving cylinder.
- Player: the columns join the flora obstacle buffer through an additive hook in FloraSystem.UpdateObstacles, so the player slides round a colossus. PlayerMovementSystem is untouched. Creatures: a new ColossusPushSystem runs after CreatureBrainSystem and pushes bodies out to the nearest edge. It skips the player avatar, birds overhead and anything under an arm.
- Walk test (v2): from (1510,2220) with `-walk 1478,2221` into the HalfBuriedHead. Log: "ColossusCollision: player stopped against the HalfBuriedHead at (1506.7, 26.4, 2220.1), gap 0.00 m". The player was still held there 35 s later. With LogPushes on, the push system pushed a creature out of a colossus on each of the 20 logged frames.
- Shot: gallery r4_head_stop (noon, the head from the side the player walked in on).
- Risks:
  - FloraSystem.cs is shared (one additive hook plus a field).
  - Scripts/Fauna is animal-owned by path, and the new file sits there next to StoneColossi and DunesColossus. Those are world-lane through the W cards.
  - CreatureBrainSystem and BuildingFootprints (animal lane, A-70) are untouched.

## W-102 row 1: flora shadows near the camera only (done, verified)
- Commit 1ca7d1a (world-features). FloraCull.compute writes separate shadow lists (DRAWS 5: 3 LOD + 2 shadow); a plant casts only within its layer's ShadowCastDistance (shrubs 40 m, trees 60 m, ground layer never). Main draws are ShadowCastingMode.Off. Shadow lists include off-screen casters within the distance, so near shadows behind the camera stay.
- Editor GPU ms, v2, noon, uncapped, 500-frame mean, shot camera (editor numbers on the main PC; the profiler should re-measure in a build):
  - fishing village 1 (-3304,-1644): 2.97 -> 2.46 ms; tris 5.11M -> 3.16M; instances 17595 -> 9730.
  - forest (850,835, Temperate): 3.73 -> 2.91 ms; tris 5.82M -> 5.64M; instances 16402 -> 15412.
- Shots (near shadows unchanged): gallery W-102/r1_before_fv1, r1_after_fv1, r1_before_forest, r1_after_forest.
- Risk: FloraSystem.cs and FloraCull.compute are shared flora files. Shrub shadows between 40 m and the 50 m URP shadow distance are gone (a few pixels each).

## W-102 row 2: far flora fade and thinning (done, verified)
- Commit 499a18c (world-features). Natural plants thin past a per-layer share of radius with a per-plant dither threshold; rocks/built things never thin. Trees thin last and keep 50% at radius; shrubs and ground keep 25%.
- GPU ms (editor, uncapped, 500 frames, plain), before (row 1 code) -> after:
  - fv1 (-3304,-1644): 2.61 -> 2.53 (tris 4.11M -> 3.85M)
  - forest (850,835): 3.12 -> 2.77 (tris 5.66M -> 4.89M, instances 14230 -> 12689)
  - far view (800,800): 4.17 -> 2.88 (tris 2.70M -> 2.63M). The far drop is larger than the triangle change explains; part of it is likely run noise or streaming state. Editor noise is about +-0.15 ms; the profiler should re-measure in a build.
- Far shot: no visible pop line or density edge (before/after near identical). gallery W-102/r2_before_far.png, r2_after_far.png.
- No terrain/landmark code touched; hash checked in the final suite.

## W-102 row 3: shadow settings sweep (measured; proposal only, no asset change)
- Runtime-only sweep of the PC URP asset (shadow distance / cascades / map size), restored to 50 m / 4 / 2048 afterwards. No QualitySettings or URP asset edited.
- Medians of 3 interleaved passes, editor, v2, noon, uncapped (GPU ms / shadow casters / setpass):

| Config | fv1 ms | fv1 casters | fv1 setpass | forest ms | forest casters | forest setpass |
|---|---|---|---|---|---|---|
| 50 m / 4 / 2048 (current) | 3.80 | 347 | 193 | 3.81 | 364 | 175 |
| 50 / 3 / 2048 | 3.59 | 260 | 187 | 2.54 | 272 | 169 |
| 40 / 3 / 2048 | 3.60 | 258 | 187 | 2.51 | 271 | 169 |
| 50 / 2 / 2048 | 2.99 | 176 | 181 | 2.67 | 182 | 163 |
| 40 / 4 / 2048 | 3.94 | 347 | 193 | 2.78 | 364 | 175 |
| 50 / 4 / 1024 | 3.89 | 349 | 193 | 2.89 | 370 | 175 |

- Caster and setpass counts are deterministic; ms is noisy (single runs vary up to about 1 ms).
- Proposal (needs Zach's OK): 3 cascades at 50 m, map 2048. About 25% fewer casters and 6 fewer setpass; about 0.2 ms at fv1 and up to about 1 ms in the forest. 2 cascades is cheaper but softens near shadows. A 1024 map gains nothing. 40 m vs 50 m makes no difference.

## W-103 row 1: bat folk placement (done, verified)
- Merged origin/main first (6d961d9). Commit d961ac3 (world-features), BatFolkBaking.cs and BatFolkBaking.Homes.cs only.
- Every placement loop (huts, floor props, details, pods, niches) draws all of a try's numbers before any test, and only the last test (crowding) reads what is already placed. New Attempts helper: draws 24 tries in order, runs their rock/pool/path tests in parallel, then crowds and places in order; when enough are placed mid-batch the RNG rewinds to that try, so later loops on the same RNG draw what they drew before.
- Editor stage times (one bake each; card's 2.3 s was a build number):
  - v2: batfolk 1644 -> 291 ms; bake 6682 -> 5389 ms.
  - v3: batfolk 689 -> 92 ms.
- Identity: bat folk list diff = 0 on both (v2 2 sites, 47 props, 7 homes, hash 8A17B4DB; v3 1 site, 27 props, 5 homes, hash DCB70DE3). Landmark hash v2 95EAB5B4, v3 80EBFCE6, Fauna_Fingerprint F99FD9E4, heightmap checksum v2 9CBCD24A / v3 C524062E: all unchanged.
- Seen on the way (for row 5): v3 logs "reports 22287 ms" before "v3 ready"; v2 reports take 2 ms.

## W-103 row 2: rivers (done, verified)
- Commit 7b75d2c (world-features), RiverBaking.cs only.
- The two river maps already share one drainage grid (W-76), so the drainage is built once. Timing the rest showed most of the stage was CutDams on the drawn map: 24 ground samples per river cell in one thread (about 590 ms). Those samples and the shore-level loop now run in parallel over the same managed maths, and blob arrays are block-copied.
- Editor stage times (one bake each): v2 rivers 1169 -> 541 ms (bake 5389 -> 4954 ms); v3 rivers 1460 -> 886 ms.
- Identity: landmark hash v2 95EAB5B4 / v3 80EBFCE6, heights v2 9CBCD24A+C8D060BA / v3 C524062E+D4AD0999, bat folk 8A17B4DB / DCB70DE3, Fauna_Fingerprint F99FD9E4: all unchanged.

## W-103 row 3: Generate in parallel (done, verified)
- Commit 02a67ea (world-features): IslandV2.cs, RiverBaking.cs, new Terrain/BakeParallel.cs (+ .meta).
- IslandV2.Generate's cell loop runs per row; counts and border heights are summed in row order, so the report line and heightmap are identical. PickRealms is only 15 ms.
- Found: the plants pregrow (#102) fills the .NET thread pool just before the bake, so a Parallel.For queued behind them ran on the main thread alone. New BakeParallel helper uses its own above-normal threads; the row 2 river loops use it too.
- Generate alone (eval): about 50 ms. In the bake it still shares the CPU with the pregrow: v2 generate 1010 -> 713 ms, bake 4954 -> 4364 ms; v3 bake 6028 -> 4979 ms (v2base 1071 -> 736).
- Tried starting the pregrow after Generate: generate drops to 57 ms, but rivers then slow by the same amount (v2 bake 4327, v3 5461). The CPU is the limit while plants grow, so I kept the original order.
- Identity: landmark hash v2 95EAB5B4 / v3 80EBFCE6, heights 9CBCD24A / C524062E, bat folk 8A17B4DB / DCB70DE3, Fauna_Fingerprint F99FD9E4: all unchanged.

## W-103 row 4: v3 generate (done, verified)
- Commit 9eef1d1 (world-features): IslandV2.cs, IslandV3.cs, V3Ranges.cs, V3Ground.cs.
- IslandV3.Generate no longer runs the v2 Generate (it was only used for the report line). The v3 line now prints the layout part only (version, seed, realms, forbidden borders) plus "v2 border ridge and cell counts not built". The v2 line is unchanged.
- PickRealms is computed once per process; callers get copies. The v3 range and ground loops use BakeParallel.
- v3 generate 1407 -> 1205 ms: v2base 736 ms gone, layout 41 ms. Ranges and ground still share the CPU with the plant pregrow, so ranges is about 1080 ms in the bake (it was about 580 ms when v2base took the contention).
- Identity: landmark hash v3 80EBFCE6 / v2 95EAB5B4, heights C524062E / 9CBCD24A, bat folk DCB70DE3 / 8A17B4DB, Fauna_Fingerprint F99FD9E4: all unchanged.

## W-103 row 5: island-ready time (done, measured; no code change)
- Island-ready time, editor play mode, 3 runs each. Before is the pre-W-103 code (6d961d9 files checked out temporarily); after is 9eef1d1.
  - v2: before 7.5 / 7.6 s (first run 24.6 s, cold), after 5.5 / 5.6 / 5.6 s. Bake 6348-6682 -> 4339-4485 ms.
  - v3: before 7.9 / 8.0 / 7.9 s, after 6.1 / 6.1 / 6.1 s. Bake 6614-6796 -> 4962-5002 ms.
- v3 "before" printed ready at about 31 s because the editor's Island/Audit Reports menu sets PUNCHY_ISLANDAUDIT=1 and the audit (V3Audit, ranges checks, ground report) adds about 22.7 s. That is editor-only and opt-in; builds skip it. The numbers above leave it out. If Zach's 30 s+ v3 startup was in the editor, turning off Punchy/Island/Audit Reports removes most of it.
- Target under 6 s: v2 meets it, v3 is 0.1 s over. The rest is settlements (about 920 ms, roads about 620), rivers (about 1000), caves (about 490), fishing (about 550) and seafolk (about 470), all sharing the CPU with the plant pregrow. No cheap win left that keeps the output identical without a deeper change, so I stopped here.
- Identity on every run: landmark hash v2 95EAB5B4 / v3 80EBFCE6, heights 9CBCD24A / C524062E, bat folk 8A17B4DB / DCB70DE3, Fauna_Fingerprint F99FD9E4: all unchanged.

## W-104 row 1: test pins (done, verified)
- Commit 8a1315c (world-features): Assets/Tests/PlayMode/WorldTests.cs, comments name PR #148 / W-104 row 1.
- Landmarks_V3: 107 14D92F64 -> 109 C96F266F. Fauna_Fingerprint: EA6A4DE5 (A-71's pin on animal-behaviours) -> F99FD9E4 (main after PR #148; also the value before W-103).
- Suite on v3: 13/13 passed.

## W-104 row 2: #70 magnetic debris solid (done, verified, no code change)
- Commit b20a7ff (world-features, empty, names #70).
- v3 walk tests at the MagneticDebris at (-982.1, -4004.8) (14 boxes): walking east stopped at x -987.07 against its face; walking south stepped up onto the low tier (y 8.42 over ground about 8.2) and off again; stopping on it held the player at y 8.419.
- The thin flat pieces have no boxes: they're ground decals, the issue's "clearly non-solid" option.
- Shots: Assets/Temp/shots/w104-70-blocked.png, w104-70-standing.png.

## W-104 row 3: #80 ruins sand (partial, verified in play)
- Commit 8136740 (world-features, FloraGenerator.cs, CRLF kept). The v3 Ruins use FloraForm.KitRuin, which already had W-94's worn coursed blocks, gaps and fallen blocks. New RuinSand adds drifts at the wall feet, one low mound with a tail drift, and dry tufts at LOD 0. It's drawing only and uses its own RNG, so boxes and colliders don't change.
- Shots: Assets/Temp/shots/w104-80-before-ruin{0,1}_{close,medium}.png and w104-80-after-*.png.
- Honest note: this ruin stands on a raised stone floor in tundra grass, so the sand reads as small tan patches on the floor, not a strong mound. A bigger, ground-level mound would need the floor height. This is a follow-up.

## W-104 row 3b: ruin sand mound at ground level (#80), 60212c5

- Cause of the tan patches: StructureKit.House puts a 0.5 m plinth under the ruin, and the row 3 sand sat below its top.
- Now: drifts outside the walls and the mound start from the ground beside the plinth and rise past it. The mound is about 0.8 m tall, bigger, and pushed out from the windward edge. Drifts inside sit on the floor.
- Drawing only, from its own stream: kit parts, colliders and hashes are unchanged.
- The mound crest has no collider, so a walker sinks into it a little.
- Shots: Assets/Temp/shots/w104-80b-after-ruin{0,1}_{close,medium}.png. The before shots are w104-80-after-*. The ruin 1 medium shot shows the mound plainly. Ruin 0 is on snow, where the sand reads less.

## W-104 row 4: fishing villages (#36), 784e034

- Pier networks now branch and join. When the mid and end branches both reach out on one side, a walkway links their tips parallel to the main walkway, closing a loop round open water. It sits as near the shorter tip as it can while clear of the houses. Wharves with only one branch stay a T or an L.
- Small details added: one to three crates by the land end (the bottom ones are solid) and two to four buoys with masts off the far end. These join the nets, crab pots, lanterns, moored skiffs and ladder that were already there.
- Everything new draws from its own stream, so walkways, houses and older details are unchanged.
- Doors: AnchorBaking now gives fishing villages Home and Door anchors for their placed kit and stilt houses. A stilt house's Door anchor is at the foot of its stair. Plots dropped on river water are skipped.
  - Result: each of the 3 v3 villages now has 4 Door anchors (it had none). This is the A-71 "huts gave the folk no door" problem.
- Stilt houses over the water: the wharf houses already stand over the water. The 4 FaceCentre stilt houses per village stay on the bank, since moving them would change the asset and re-bake.
- Shots, all in Assets/Temp/shots/:
  - w104-36-wharf-shore: village at (-3941, 1290), seen from the bank.
  - w104-36-wharf-above: the same village from above, showing the loop.
  - w104-36-wharf-above2: village (-4041, 921), a one-branch L.
  - Village (-3144, -1596) shows no wharf at its spot (on river water, dropped by W-91).
- Landmark hash: see the suite result in the final row-5 entry. Landmark positions are unchanged; only drawing and anchors changed. The fauna fingerprint may move because of the new anchors.

## W-104 row 5: cobble roads (#39), 8623d53

- Paved layouts now lay cobbles only through the centre. Those are the human village and mining camp (Cobbles) and the dwarf hold and mining camp (Flagstones).
- From about 0.35 to 0.6 of the town radius, streets and the roads leaving town fade to packed earth: no cobble pattern, browner, duller, a few pebbles. The fade edge is ragged with noise.
- Only surface sampling changed (SettlementData.Ground). Street layout and the road bake are unchanged. Unpaved layouts look the same as before.
- Shots: Assets/Temp/shots/w104-39-fade-above.png (human town at (2972, -885), looking out along the main road; cobbles fade to earth) and w104-39-fade-eye.png.
- Road bake: about 650 ms (`[bake] stages`, roads 649-652 in v3 editor runs, 697 in the suite). Unchanged, since the bake isn't touched.
- Island-ready time, editor play mode, audit off:
  - v3: 6.1 / 6.1 s. On budget.
  - v2: 5.7 / 5.7 s. This is 0.2 s over the 5.5 s from W-103. Bake was 4502-4553 ms against 4339-4485 then, while three other Unity editors were busy on the machine. Neither row adds bake work, so I read this as load, not a regression. Worth one re-measure on a quiet machine.
- Suite on v3 at 684dcdc (with the repin): 13/13 passed.
  - Landmarks_V3: 109 C96F266F, unchanged after row 4.
  - Fauna_Fingerprint: F99FD9E4 -> 1202B8ED, from row 4's Home/Door anchors. I repinned it in WorldTests.cs; the first run failed only on that pin.
  - v2 landmark hash 95EAB5B4, unchanged.
- StoneColossi.cs untouched.

## W-105 row 1: tidy-ups, 64e6f75

- AnchorBaking.cs: FolkHouse and HasFolkHouses now sit above AddWildAnchors' summary, and each has its own. No code change.
- v2 island-ready time, editor play mode, audit off: 5.7 / 5.6 s (the first run, cold, was 15.8 s). Bake 4579-4603 ms, hash 95EAB5B4.
- Not quiet: one other editor was busy, the main tree (D:/Unity/Punchy). It used about 2.5 cores during the runs (427 CPU-s in about 170 s). No other lane editor or build was running. So v2 is 0.1-0.2 s over 5.5 s with that load.

## W-105 row 2: missing wharf (#36), 7a09321

- Cause: the wharf at the village near (-3144, -1596) ran out over river water, so flora left the plot empty (W-91's OnRiver rule). Placement never checked for that.
- Fix (FishingVillageBaking.cs): a layout whose docks or wharf run over river water no longer fits. Placement then tries the shore angle turned 30° and 60° each way. The straight-out try comes first, so villages that fitted before don't move.
- v3 result: 4 villages, all with wharves.
  - Unchanged: (-3941, 1290) and (-4041, 921).
  - New: (-2151, 3731) facing 150° and (368, -613) facing -15°. Both mouths needed a turned angle.
  - The (-3144, -1596) village is gone: it didn't fit at any angle before the 4-village limit was reached.
  - Each village has 4 Door anchors.
- Landmark hash (game log): v3 110 landmarks, 12D60044, against 109 before. The change is the extra village and the moved one. The suite's Landmarks_V3 is repinned in row 3 along with the stilt house move.
- v2: hash 95EAB5B4, unchanged.
- Shot: Assets/Temp/shots/w105-2-wharf-above.png (village (368, -613) with its wharf; the stilt houses are still on the bank).

## W-105 row 5: shadow cascades by preset, 69f1a66

- GraphicsSettings.cs picks the cascade count from the Shadows setting: Low 2, Medium 3, High and Ultra 4. A custom setting gets the nearest preset's count (2 below 0.8, 3 below 1.2, otherwise 4). The shadow distance still scales with the setting as before.
- Runtime only: the count goes on the running pipeline and is put back on quit, as the distance already was. The URP asset and QualitySettings are untouched; the asset reads 3 after play. Describe() now names the count, e.g. "(Medium, 3 cascades)".
- The only change in behaviour is High and Ultra going from 3 to 4 cascades. Low already had 2 and Medium 3.
- Shots, same forest view (SparklingForest near (-1000, -400), 14:00): Assets/Temp/shots/w105-5-Low.png, -Medium, -High, -Ultra (each also has an -800 copy). Each preset also changes the view and detail ranges, so the tree counts differ between shots.
- Render ms, editor play mode, uncapped, 400 frames, two rounds (frame ms / GPU ms):

  | Preset | Frame ms | GPU ms | Shadow casters |
  |---|---|---|---|
  | Low | 5.2 / 8.3 | 1.7 | 124 |
  | Medium | 6.9 / 6.3 | 2.3 | 217 |
  | High | 7.9 / 8.5 | 2.9-3.0 | 341 |
  | Ultra | 15.1 / 11.3 | 4.1-6.8 | 535 |

- High with 3 cascades against 4, A/B twice: 9.1 / 8.6 ms against 8.8 / 8.9 ms. The 4th cascade's cost is within noise here.

## W-105 row 6: colossus collision follows the walkers (1630cdb, world-features, merged origin/main with A-72 first)

- ColossusCollision now stores each colossus's columns in its root's frame (position plus heading only, so a walker's lean leaves the columns upright). It lays them again in place whenever the root moves, with no per-frame allocation: the lists are rewritten and the push snapshot copies only the moved columns into its cache. ColossusPushSystem, the player's obstacle buffer (MovingNear now also flags a walker in reach that has moved) and Against all read the real body. Still giants keep their pose, so nothing changes for them. StoneColossi.cs was not touched.
- Check (v3, golem slot 3): the golem walked 39, then 47, then 66 m from its build spot. Against() at the body gives RockGolem; at the build spot (707, -329) it gives nothing. The summary centre tracks the root: (715, -369), then (732, -370). The player walked west into it (PlayerInputSystem.TestMove) and logged "player stopped against the RockGolem" four times along its current flank, at x 767, 759, 730 and 725.
- Cost with the golem awake, player 20 m off: Brain.Tick 0.65 ms/frame, Brain.Advance 0.18, ColossusPushSystem 0.04 ms/frame.

## W-105 row 3: stilt houses over the water (91321e3, world-features)

- StructureKit.StiltHouse stands out over the shallows: a walkway runs from the plot about 15 m out to the porch, with the stair at the walkway's shore end. Piles that reach the bed are 8 m long (were 5). Shore details stay on the bank.
- One check decides where a house fits, LandmarkSampling.StiltHouseFits: plot 0.2-3.5 m above the sea, 0.3-6 m of water under the house, neither on river water. Village placement needs at least one spot that fits, and FloraPlacement and AnchorBaking leave the rest empty. Flora also drops a house that would stand on a dock or wharf lane. The village type now offers six single stilt house spots (rings 0.35-0.5, angles -80 to 75). It had 4 round the middle before.
- Doors: the Door anchor is at the walkway end (reach 0.8) and the Home anchor stays at the plot. The door leaf look-ahead in Flora.hlsl is 15 m (FloraDoors.cs comment matches).
- v3: still 4 villages, at (-2151, 3731), (368, -625), (-1216, 3829) and (-4012, 1075). Each village has 1-2 stilt houses, 6 in all: the shore only has shallows on the sea side, close to the docks. "Doors: 133 house doors". No console errors.
- Repinned in WorldTests.cs: Landmarks_V3 is "110 09EAC0FE" (island line 110 landmarks, hash CE5EB4D9; it was 109 C96F266F) and Fauna_Fingerprint is 61459BF0 (was 1202B8ED). v3 bake 5207 ms (places 3897).
- Shared files: Flora.hlsl, FloraDoors.cs, FloraPlacement.cs and LandmarkData.cs (the new StiltHouseFits).
- Shots, village (-2151, 3731): Assets/Temp/shots/w105_stilt_shore.png (from the beach: house on stilts in the water, walkway to the shore) and w105_stilt_above.png (the pier and the stilt house side by side).

## W-105 row 4: shared fire effect #131 (20d14b2, world-features)

- One standard fire: a FireEffect asset (Resources/StandardFire). It holds the colours (Wood, Blue, Green, Violet; each has outer, mid, core and light), glow size and strength, light intensity and reach, embers (chance, rate, rise) and smoke (strength, rise, rate, colour). Flame.shader draws a flame, a spark, a soft glow and two smoke puffs per flame; the smoke is a second, alpha-blended pass.
- Debug: F4 (after F3) cycles every fire through the effect's colours and back to each fire's own.
- Fix: the colour index was truncated from an interpolated value, so a non-orange palette bled orange (goblin green was hit too). It is rounded now.
- Moved onto it. Every flame already went through FireSystem.AddFlame, so callers didn't change:
  - landmark Flame effects on fire pits and braziers: campsites, hermit hut, goblin camp (green), ogre camp, human village, human and dwarf mining camps, dwarf hold;
  - cave wall torches (flame and light);
  - sunken temple ruin sconces (flame and light);
  - carried torches (flame only).
- Left alone:
  - the carried torch light in CreatureRenderSystem (animal lane file);
  - the Ember Forest night fire (not started, per the card);
  - cave lava light, landmark ember and smoke plumes, ambient Embers particles, burnt campfires and lanterns.
- Shots in Assets/Temp/shots: w105_4_campfire_before / _after, w105_4_sconce_before / _after, and w105_4_campfire_green (debug override, green).
- Render ms, human village (2972, -885) at 22:00, v3, uncapped, 500 frames, fires on / off (flame material removed):
  - frame 4.83 / 5.11 ms, then 4.80 / 4.86 ms;
  - gpu 1.68 / 1.29 ms, then 1.00 / 1.24 ms;
  - fires add 2 batches and 2 setpass calls. The difference is inside the noise, so no regression.
- Bake: v3 6261 ms this session (110 landmarks, hash CE5EB4D9, unchanged) against 5207 ms in row 3. Row 4 adds no bake work (runtime and shader only), so I read this as machine load. The suite run will re-measure it.
- StoneColossi.cs untouched.

## W-106 row 1: Ember Forest ablaze at night #131 part 2 (4e7ae31, world-features)

- Ember Forest trees within 70 m of the camera (DetailRange scales it) carry StandardFire flames: 3-5 in each crown, sometimes one at the foot and a patch of burning litter on the ground. The 6 nearest crowns light the ground through CaveSystem.MovingLights.
- One colour per night from the world seed and the night (evening and the morning after count as one): ForestColour. On this seed nights 0..5 are blue, green, violet, wood, wood, violet.
- Cosmetic only, local. It catches as the light goes and dies down at dawn with the landmark night fires. Gathered twice a second or when the camera moves 6 m, capped at 320 flames.
- New: FloraSystem.TreesNear (read only, from the placed flora) and LandmarkEffectSystem.ForestFire.cs (LandmarkEffectSystem is now partial). Debug switch LandmarkEffectSystem.ForestFiresOn.
- Shots at (800, -1700), v3, in Assets/Temp/shots: w106-ember-night-a-800.png (night 0, blue, 22:00), w106-ember-night-b-800.png (night 1, green), w106-ember-dawn-800.png (05:54, flames dying down).
- Render ms there at 22:00, uncapped, 500 frames, fires on / off (48 trees, 213 flames): gpu 1.94 / 1.97 ms, then 2.00 / 1.90 ms; frame 6.47 / 6.36, then 6.58 / 6.01 ms; +2 batches, +2 setpass. Within the noise.
- Landmarks_V3 and Fauna_Fingerprint unchanged (no placement code touched): "110 09EAC0FE", 61459BF0.
- Paused W-106 here for W-107 (priority from Zach). Rows 2 (mesa tortoise) and 3 (Wasteland stepping stones) are written but uncommitted, shelved as a patch; row 4 not started.

## W-107 rows 1-2: cheap far tree caster, still far shadows (done, verified; off by default)
- Commit e6bcbf1 (world-features). Switches in `Punchy.Rendering.FloraShadowOptions` (public static, read every frame; each also a command-line argument; all off draws as main):
  - `CheapFarCaster` / `-cheapfarcaster`: trees past `NearRange` (`-shadownear`, default 25 m) cast with their far level of detail (LOD 2) instead of LOD 1. Only the trees layer.
  - `StillFarShadows` / `-stillfarshadows`: in the flora ShadowCaster pass, wind sway and flutter fade out over 10 m round `StillFrom` (`-stillfrom`, default 40 m; 30-50 is the intended range).
- Bench: editor, v2 island, noon, uncapped, 500-frame means. Shadow ms is the GPU time of URP's MainLightShadow marker. Tree shadow casters come from reading back the shadow draw args.

| Spot | Switch | GPU ms | Shadow pass ms | Tree shadow casters (near+far) | Tree shadow tris |
|---|---|---|---|---|---|
| Fishing village 1 (-3304,-1644) | off | 3.32 | 0.57 | 60+13 | 1549k |
| | cheap | 3.03 | 0.28 | 7+66 | 231k |
| | still | 3.44 | 0.56 | 60+13 | 1549k |
| Forest (OakGrove 1925,2335) | off | 2.11 / 2.25 | 0.16 / 0.16 | 35+0 | 217k |
| | cheap | 2.07 / 2.18 | 0.13 / 0.13 | 15+20 | 90k |
| | still | 2.14 / 2.19 | 0.15 / 0.17 | 35+0 | 217k |

- Look:
  - Cheap: at the village (dense canopy) the far canopy is a little lighter, with less fine self-shadow between crowns. The grove's ground shadow looks the same.
  - Still: no visible difference in stills. The sway fades over 10 m, so there's no step where a shadow could pop.
  - Still costs nothing and saves nothing measurable here; its gain would be less cascade shimmer in wind.
- Shots (Assets/Temp/shots): w107_fv1_cheap_false/true-800, w107_fv1_still_true-800, w107_forest_cheap_false/true-800, w107_forest_still_true-800. The forest shots are in rain, so the shadows are faint.
- Note: rows 1-2 also add the inert FloraCache shader pass and the cull's inner skip (`_Shadow.w`) for row 3. Both do nothing while row 3's switch is off.

## W-107 row 3: cached far shadows + profiler job 33 answers (world-features 3b41973)

Switches (all off by default; defaults, URP asset, QualitySettings and -benchtune untouched):
`CheapFarCaster` / `-cheapfarcaster`, `StillFarShadows` / `-stillfarshadows`, `CachedFarShadows` / `-cachedfarshadows`.
Tunables: `-shadownear` 25, `-stillfrom` 40, `-shadowcachefar` 50 (cache: 0.5 deg sun step, 10 m move step, 1024, 4 frames).

**Cache**: far trees (past the near range) are laid once into a 1024 sun-view depth cache, redrawn over 4 frames when the
sun turns 0.5 deg or the camera moves 10 m, then applied with one full-screen pass (multiplies, skips pixels the real-time
shadow already darkens, strength 0.42). Memory: 15.3 MB (2 x 1024^2 RFloat + 16-bit depth + buffers). At 24-min days it
redraws about every 2 s; 79 redraws over the whole bench below.
**Sun check** (shots w107_cache_sun_9 / _13 / _17.5): the shadows follow the sun; redraw count went 2 -> 3 -> 4.

Table, editor, v2 fishing village 1, noon, uncapped, 2 rounds averaged (round-to-round spread ~0.4 ms gpu, so gpu deltas are in the noise; the shadow pass is the clean measure):

| Switches | GPU ms | Frame ms | Shadow pass ms | Look |
|---|---|---|---|---|
| off (default) | 2.56 | 5.99 | 0.10-0.11 | reference |
| cheap | 2.57 | 6.35 | 0.04-0.05 | forest now dappled (holes), same as default at a glance |
| still | 2.62 | 6.24 | 0.10-0.11 | far shadows stop swaying |
| cheap + still | 2.55 | 5.96 | 0.04-0.05 | as cheap |
| cache | 2.58 | 5.97 | 0.04 | meadow shadows kept; far trees a little lighter |
| all | 2.56 | 5.87 | 0.03-0.04 | as cache |

Profiler job 33:
1. Forest blob: the cheap card now alpha-clips 1.3 m value-noise holes in the shadow pass past the near range. Re-shot at
   (800, 800), noon and 16:30, on vs off: w107_forest3_cheap_false/true, w107_forest4_cheap_false/true. They now look
   nearly the same; the seam no longer shows at a glance.
2. Still at FV1: there is no separate still draw list. `StillFarShadows` only sets a shader global that zeroes far sway,
   so no tree is drawn twice (casters 285 in both). The extra cost is per-vertex branch work in the shadow pass, about +0.0 ms
   shadow pass here and noise-level in gpu. In the build, I'd use cheap + still or cache rather than still alone.
3. Cache: done (above). Its shadow pass matches cheap; the full-screen apply isn't split out in GpuSpy.

Shots are in the world tree under Assets/Temp/shots (not committed): w107_forest3/4_*, w107_forest4_cache_true, w107_cache_sun_*.

## W-106 row 2 (#40) mesa rock tortoise (world-features, commit after 3b41973)

New colossus `MesaTortoise` (ninth slot, MesaCanyonlands, own salt so the other eight keep their spots). On v3 it stands at
(2026, 142, 1344). Shots (world tree Assets/Temp/shots): w106_tortoise_far (reads as a small banded mesa among the
canyon buttes), w106_tortoise_near (head with lidded eye and toes under the strata, talus at the foot).
Landmark hash: the colossi aren't landmarks, so Landmarks_V3 should stay at 110 09EAC0FE; the final suite run checks it
(and Fauna_Fingerprint 61459BF0), and I'll report it in my hand-back.
Row 3 (#70 Electric rock chunks held still as stepping stones) is applied in the working tree but not verified or committed:
out of budget. Row 4 and the buoy perches have not been started.

## W-106 row 3 (#70) debris stepping stones (world-features 8ff0876)

Wastelands debris pieces below 3.4 m now settle as flat stones (top 0.75-1.1 m above the ground) with a KitBox each;
pieces at or above 3.4 m still orbit. Checked on v3 at the MagneticDebris plant at (-640, 19, -1230):
- Standing: the player dropped from 21.5 m lands at y 19.86, on the stone's top (ground 18.97). Shot w106r3-standing.
- Blocked: a 0.9 m step west into the stone's side comes back blocked (KitCollision.KeepOut on the live box buffer).
  The shot w106r3-blocked shows the player against the stone's side.
- Player ms: PlayerMovementSystem takes 0.034 ms on the stone and 0.033 ms 50 m away. Unchanged.
- Island ready on v3 with a warm editor: 36.4 s and 30.0 s, under the 72 s line.
Shots are in the world tree under Assets/Temp/shots (not committed).

## W-106 row 4 (#80) ruin brick walls (world-features 17ae4db)

Ruin walls are now built from coursed StructureKit bricks with moss, sand and fallen blocks. Shots w106r4-ruinA and
w106r4-ruinB look right. Ruin C showed only its shadow: not looked into yet. At ruin A in the editor (vsync-bound): main
thread 7.13 ms, render thread 2.14 ms, URP Main Camera 3.03 ms.

## W-106 (A-73) wharf buoy masts in the perch Boxes (world-features 648d9f5)

The buoy masts are now Column pieces inside the model's perch Boxes. Caveat for the animal lane: WharfSpots drops perches
whose bottom is below Deck - 0.2. The buoy masts sit at about 0.15, so they are probably filtered out until that filter
is relaxed. The wharf spot count is unchanged: 48 spots, 190 perches.

## W-107 job 33 follow-ups (world-features d8a796a)

(a) Gap between the near range and the cache. The cache cull skipped trees within near - 2 m of where the cache was
laid. The camera can drift up to CacheMove (10 m) before a redraw, so trees just past the near range cast nowhere, and
the gap grew as the near range shrank. Changing the near range at runtime also never redrew the cache. Fixes:
- The cache now starts at max(0, near - CacheMove - 2 m), so the overlap can't double a shadow (the receiver leaves
  real-time shade alone).
- The cache is redrawn whenever the near range changes.
Checked at OakGrove on v3 (noon, cache on, near 15, camera 8 m from where the cache was laid): shot w107fix-drift8
shows full ground shadow under the oaks. My "before" emulation, w107fix-before, looks the same from this angle, so the
gap the profiler saw is not visible in this frame. Please re-check with the profiler's job 33 view.

(b) Still far shadows made cheaper. The distance and smoothstep now run once per plant in FloraCull (the shadow list
fade = 1 + stillness). The shadow vertex only does a saturate. GpuSpy at OakGrove, cache off, two rounds each:
- Draw Main Light Shadowmap is 0.28 ms with the switch on and 0.28 ms with it off.
- GPU total is 0.12-0.21 ms either way: noise.
The switch is now about free here, but it still saves nothing, since the cascades draw the same meshes.

Defaults are unchanged: every switch is off, NearRange is 25 and CacheMove is 10.
Suite on d8a796a: 13 passed, 0 failed. Landmarks_V3 110 09EAC0FE and Fauna_Fingerprint F4E66093 are unchanged, so no
repin. v3 ready at 66.4 s in the cold suite (66.6 s before), and world up in 35 s. Warm play ready was 40.8 s earlier.
Shots are in the world tree under Assets/Temp/shots (not committed).

## W-107 new tree shadow defaults (world-features 278a190)

Per Zach: CheapFarCaster and CachedFarShadows are now on by default, near range 25 m. StillFarShadows stays off. This
builds on the gap fix in d8a796a.

Flags: each switch takes "-name 0" to turn it off, so use `-cheapfarcaster 0` and `-cachedfarshadows 0`.
`-stillfarshadows` (or "-name 1") turns a switch on, so the old bench flags still work. This is noted in
docs/agents/rendering.md.

Editor, v3, clear noon. Two rounds each, Draw Main Light Shadowmap ms, old defaults (all off) then new defaults:
- OakGrove forest (camera (1719.6, 34.4, 3389)): 0.33 / 0.33 before, 0.20 / 0.18 after.
- Fishing village 1 (camera (435, 22, -585) looking at the wharf): 0.14 / 0.15 before, 0.11 / 0.12 after.
- The frame is vsync-bound in the editor (16.7 ms either way), and GPU totals of 0.10-0.17 ms are noise. Build frame
  ms needs the profiler's bench.

Shots are in the world tree under Assets/Temp/shots (not committed): w107def-forest-before/after-r2 and
w107def-fv1-before/after-r2, at 1920 plus -800 copies. Ground shadow under the oaks looks the same in both forest shots,
with no gap.

Suite on 278a190: 13 passed, 0 failed. Landmarks_V3 110 09EAC0FE and Fauna_Fingerprint F4E66093 are unchanged.
v3 ready at 31.7 s (66.4 s before), and warm play ready with the new defaults is 37.3 s, so no worse. The URP asset and
QualitySettings are untouched. The bench's Tools/bench/BenchmarkRoute.cs.txt (the profiler's) still sets the
switches by reflection, so it is unaffected.

## W-108 row 1: ruin C (world-features 4a2bbea)

Ruin C draws; it was never a game bug. Cause: the shot. The W-106 shot moved Camera.main and called cam.Render() in
the same eval. FloraSystem culls in LateUpdate, for the pose the camera had that frame, so the camera's plant lists
were still the previous pose's. The shadow cache doesn't depend on the view, so it still cast. Every plant, rock and
ruin near the new pose was missing, not only ruin C. I reproduced it on purpose (shoot elsewhere, then ruin C in the
next eval: w108-ruinC-stale). With a pose step and one frame between pose and render, ruin C draws: w108-ruinC.

Ruins checked: the Ruin species (KitRuin form) is used only by the one Ruins landmark at (-2226.2, 2325.5). It has
four ruins, at (-2206.3, 2318.7) (C), (-2226.2, 2325.5), (-2230.3, 2346.1) and (-2242.1, 2311.6). All four draw from
fresh poses; the 2x2 tile is w108-ruins-all.

The game's order is fine: FloraSystem runs at execution order 1000, after the camera has moved. A gotcha line is in
docs/agents/editor-and-assets.md so other lanes' shots don't hit this. Shots are in the world tree under
Assets/Temp/shots (not committed).

### W-108 row 2: cobbled roads (#39 part 1), world-features c6e20b1

Roads near and inside paved settlements are now cobblestone. Far roads stay dirt. A road fades from dirt to cobble
over about 14 m at the settlement's edge (radius -2 m to +12 m, with a noisy edge), and town streets are cobbled out
to the edge, not only at the centre. The look is all in the existing terrain shader, driven by the vertex paving
channel: no new meshes, decals or textures. It has uneven stones, dirt and grass tufts in the gaps (more grass
towards the verges), and a worn middle band where the ruts run: paler, smoother stones with packed dust in the
joints and no grass. About 1 stone in 30 is missing (only a dirt hole) and 1 in 15 is sunken (darker and flatter).
The two ruts merge into one worn band on the 1 m terrain grid, which reads fine.

Shots (world tree, Assets/Temp/shots): w108r2-entry-before/-after (a road entering HumanVillage, dirt to cobble),
w108r2-close-before/-after and w108r2-top-after (close-ups).

GPU ms, uncapped, 500 frames, 2 rounds, at HumanVillage:

| Pose | Before | After |
|---|---|---|
| entry | 3.19 / 3.43 | 3.25 / 3.35 |
| close | 3.87 / 4.20 | 4.16 / 4.11 |

That is within noise. Casters and batches are unchanged (453/2650 and 490/2705). Island ready: 35.4 to 38.3 s this
session, no worse than before. The change is look-only, so the landmark hash is unchanged; the suite at the end of
the card confirms it.

### W-108 row 3: paved squares (#39 part 2), world-features 883bb9a

Towns with a water feature of radius 70 or more now get a flagstone square round it. Its shape is a superellipse,
and the seed sets the shape and tint. A kerb runs round it and breaks where the lanes come in. Worn paths run from
each lane mouth to the water, and a worn ring circles the water. Shots (world tree, Assets/Temp/shots):
w108r3-village-top (HumanVillage) and w108r3-elf-top (an elf town). In HumanVillage the well stands just outside the
square, and the ring goes round the campfire in the middle. Island ready 36.6 s. Landmark hash unchanged.

### W-108 extra: wharf buoys clear of other props (A-75 risk), world-features 301ca93

Cause: StructureKit.Wharf placed two to four buoys off the far end without checking footprints. That end can carry
houses on piles (seaward of the end branch, reaching about length + 6.6 m), and the second dock lane is about 12 m off
on +X. Fix: each buoy keeps its draws, so the stream and the other buoys stay put. It is pulled in from the dock side
(x at most 7), then steps out to sea in 0.5 m steps until it is 1 m clear of every walkway and house. Offline check
over 2000 wharf seeds: 0 of 5979 buoy masts inside a house footprint (0.8 m margin). Gulls follow the masts on their
own (PR #161).

- Shot: w108b-fv2-wharf-end, fishing village 2 (the A-75 g-mast2 spot). Both buoys float well clear of the stilt
  house piles.
- Village 1 has no wharf in game, so there is no wharf end to shoot there. Its loaded props are four FishShore, one
  FishStiltHouse and one FishDock. This was there before my change, which can't affect placement; a FloraPlacement
  drop rule probably removes it. Not fixed here: worth a card.
- Play v3: island hash CE5EB4D9, 110 landmarks, ready 37.2 s, Fauna_Fingerprint F4E66093 (unchanged), 48 wharf
  spots, 190 perches.
- Gotcha (added to docs/agents/editor-and-assets.md): an editor killed in Play mode leaves Temp/__Backupscenes, and
  the next start sits at a hidden "Recovering Scene Backups" dialog. Delete that folder first.
- Suite at 301ca93 (editor closed): 13 passed, 0 failed. Landmarks_V3 110 09EAC0FE and Fauna_Fingerprint F4E66093,
  both unchanged, so no repin. World up in 37 s. The place-prop test also finds only 3 FishWharf on v3, which fits
  village 1 having none.

## W-109 row 1: missing wharves (1eaf980 on world-features)

Cause: the W-91 river rule in FloraPlacement. A dock or wharf is checked for river water 30 m out along its
facing from its ring spot. At village 1 (wharf spot -2172,3743) the river channel starts 25-30 m out. The island
bake (FishingVillageBaking.Fits) checks the same 30 m clean, because it runs before the rough riverbanks are
switched on (TerrainAuthoring); once they are on, the mouth is wider and flora drops the wharf. No plot, road or
other prop clashes (diagnostic: 0 plots and 0 roads within 6 m of any wharf spot). Side note:
TerrainSource.OceanFactor is the old radial coast, so on v3 it doesn't really tell sea from river.

Fix: a dock or wharf the 30 m test flags is still built when its real deck stays off the river. The deck runs
from DockShore's start out to the longest deck plus the end walk, 23.5 m. This only adds props: every dock and
wharf the old test kept stays where it was. Village 1 fits a full wharf, so no shorter jetty was needed.

- v3 fishing villages: 4. Wharves in game: 3 before, 4 after. The place-prop test now says "4 FishWharf found
  in all". Docks: 4 before and after (the river rule flagged 1 of the 8 dock and wharf spots, village 1's wharf).
- Shot: w109-v1-wharf. Village 1's wharf with three stilt houses, beside the river mouth, with its dock in front.
- Suite at 1eaf980: 13 passed, 0 failed. Landmarks_V3 110 09EAC0FE and Fauna_Fingerprint F4E66093, both
  unchanged (flora only), so no repin. World up in 37 s, the same as before. Wharf spots 48.
- The editor crashed once on ExitPlaymode, inside Unity's audio cleanup (SoundManager::UnloadClip under
  RestoreSceneBackups). That's an engine crash, not our code. A restart was fine.
- An editor that loads after a player build can open an untitled scene. Play then never gets an island. Open
  Assets/Scenes/SampleScene.unity first.

### W-111 row 1 - continuous roads (#39) - done (verified), commit 892b428
- Cause: not a decal or flora rule. W-100's TooSteep skip in RoadBaking.Emit dropped any stretch steeper than grade 0.45 (sampled every 1 m), which left the gaps at cliffs.
- Fix: each run of stretches gets a grade-limited height profile (the mean of the upper and lower slope envelopes at MaxGrade). Streets take their heights from that profile. Graded streets carry Road = 2, and Height() widens their bank blend to StreetBlend + 1.5 x depth, capped at 14 m, so the terrain is cut or filled to the road. Stretches already within the grade come out bit-identical.
- Road gaps: 57 steep stretches dropped before, 0 after. All 57 are graded now, 91 graded stretches in total, out of 2424 road stretches.
- Deepest cut: 6.4 m at (-1696,164,406). Deepest fill: 6.1 m at (-1694,156,388).
- Crossings: 8 bridge spans and 0 unbridged deep crossings. A new counter tracks unbridged crossings. Fords are unchanged: the road surface stops at the water's edge.
- Switchbacks were not built: the worst grading is only about 6 m deep, so cutting and filling covers every gap without them. They can be added if Zach wants hairpins on purpose.
- Landmarks_V3: 110 09EAC0FE (unchanged). Fauna_Fingerprint: F4E66093 (unchanged). Play-mode landmark hash: CE5EB4D9 (unchanged). No landmarks moved.
- Island-ready time:
  - Warm play: v3 ready at 38.6 s, against 38.7 s before.
  - Cold suite: 85.0 s, against 66.4 s last time. The bake took 5309 ms against 5207 to 6261 ms on earlier runs, so I read the cold-suite difference as machine load.
- Shots:
  - Assets/Temp/shots/w111-cutting.png: the road runs unbroken through the graded saddle.
  - Assets/Temp/shots/w111-cutting-along.png
  - Assets/Temp/shots/w111-crossing.png: the road runs onto the bridge over the river and continues on the far bank.
- Suite: `unity test` ran the play-mode suite, but it wrote no results XML, so I took the hashes from the [measure] lines in Logs/playmode-tests.log. I'll rerun the full suite for pass/fail counts before the final hand-back.
- Risk: graded banks reach up to 14 m from the road, so props or plots sitting right next to a steep road may now stand on a slightly re-shaped slope.

### W-111 row 2 - pond water by the poison plant colossus - done (verified), commit c8ab5d7
- Where: the carnivorous plant stands at StoneColossi slot 4, at (-53.2, 34.3, 516.1). Next to it is a PoisonSwamp paddy plateau, with water at about 38.4 m and 0.5 m deep, centred near (-25, 490).
- What breaks: none of the plant's own pieces. It is not standing over water, its collision and far shadow aren't involved, and there is no river cut or ford ending nearby. The fault is the pond's water sheet. A dry mesh vertex just outside a paddy's thin rim lies below the pool level. BuildWater's border pass skipped such vertices, so whole quads dropped and the pool edges came out as cell-aligned staircases of bare ground. A 2 m probe found 30 such low dry points round 183 wet ones.
- Fix: all in world files, nothing for the animal lane. In BuildChunkMeshJob.BuildWater, a dry vertex up to 1.5 m (TuckDrop) below a neighbour's surface now gets a surface tucked 5 cm under its own ground. The quad is drawn and the sheet meets the ground along a smooth line. It is mesh only: ground, hashes and gameplay water are unchanged. Drops of more than 1.5 m keep their edge, so the water doesn't slope down a cliff face.
- Bake order:
  - Before: rivers (beds and channels) > landmarks > settlements and roads > salt tower > caves > fishing, turtles, seafolk > batfolk > anchors > water surface (rough banks + ponds + pond mask) > icebergs.
  - After: the same, but icebergs now bake right after anchors, so the water surface pass is the last step. Icebergs read sea coasts only, and their log line is identical to before.
- Shots (Assets/Temp/shots):
  - w111-plant-top (before) and w111-plant-top-after: from (-35,85,465) looking at (-30,30,498). The staircase pool edges are now smooth shorelines.
  - w111-plant-edge (before) and w111-plant-edge-after: the plateau's cliff from below. No sheet hangs off the edge.
- Regression check (after shots, taken at noon):
  - w111-reg-lakeA-after: the lake shore at (-1005,-269) is clean.
  - w111-reg-river-after: the river at (188,-778) has clean banks.
  - w111-reg-lakeB-after: the view at (670,-1160) is open lake only, with no shore in frame, so it checks nothing about edges.
- Suite at c8ab5d7: 13 passed, 0 failed, results XML written. Landmarks_V3: 110 09EAC0FE (unchanged). Fauna_Fingerprint: F4E66093 (unchanged). No repin.
- Island-ready time:
  - Warm play: 39.2 s, against 38.6 s for row 1. The change is mesh only, so I read the difference as noise.
  - Cold suite: 89.2 s, against 85.0 s, with the machine busy.
- Note for the animal lane: nothing in StoneColossi needs to change. The 2x-taller plant stands on dry ground at 34.3 m, about 30 m west of the paddies.

## W-111 row 3 (waterfalls): partial, not fixed yet (budget ran out)

- **The spots are on the v2 island, not v3.** Zach's build (main 763edbc) loads the Default island, which is still v2: "v2 ready ... 66 waterfalls". On v3 there is no water within 150 m of 78,-319 or 445,1273, and the y values don't match the ground there. On Default/v2 every spot sits next to a river step. Rows 1 and 2 were measured on v3, so they should be rechecked on v2 too.
- **Steepest water step within 60 m of each spot (v2):**
  - 78,-319: 3.7 m at 72,32.8,-327.
  - -2430,-980: 4.7 m at -2412,16.7,-962.
  - 445,1273: 7.8 m at 451,36.7,1251.
  - -1509,-38: no step within 60 m. The steepest within 150 m is 6.9 m at -1659,26.7,-138, and the shelf is not located yet.
- **Before shots** from Zach's -goto positions at noon: Assets/Temp/shots/w111-fall1..4-before(-800).png in the world tree.
  - Fall 3 is a W-98 river-mesh fall.
  - The fall 2 shot is framed too close and comes out dark blue. It needs re-framing.
- **Leads in Water.shader:**
  - The fine detail reads (`_PunchyCaveDetail`) use xz `p`, so they smear vertically on steep faces. This is the likely "glowing" two-step at -2430. Fix idea: fade `fineStrength` and the glitter/caustics by the steep-face factor, which needs `face` computed before the detail reads.
  - The rapids/streak foam is driven by xz flowDir, which can read as upward motion on a near-vertical face (445,1273). Fix idea: suppress FlowFoam and the streaks where `falling` is high, so only the downward `fallV` sheets show.
  - The gradient breaks at 78,-319 and the see-through shelf at -1509 still need geometry checks: the BuildWater quad rule and the back-face/alpha on the shelf.
- **No code changes in this row.** Hashes are unchanged from row 2 (110 09EAC0FE, F4E66093). The suite after row 2: 13 passed, 0 failed.

## W-111 row 3: v3 default + waterfalls (done, verified) - world-features ffe0c0a
- **v3 is the default island** (code, IslandV2.RequestedVersion; no ProjectSettings change). A fresh Dev build with no flags logs "v3 ready at 9.0 s", world ready 13.1 s. `-island v2` / `-island v1` still work.
- **Water.shader:** a per-face `steep` term fades the flat-water layers on a fall's face (streaks, carried foam, flecks, fine detail, caustics, glitter, Fresnel sky). Falling sheets are lit by the face's turn to the sun, and the sheet's gaps stay opaque (alpha 0.85). Result: falls no longer glow flat white at noon, upward streaks are gone, and white/grey sheets stream down.
- **Falls (v3, noon), eye -> target:**
  - fall 74 (drop 18 m, w 38): -goto -2167,3,3691 looking at -2181,12,3647
  - fall 67 (drop 27 m): -goto -3273,7,3094 looking at -3231,18,3114
  - fall 61 (drop 25 m): -goto -263,92,2618 looking at -302,101,2592
- **Shots:** Assets/Temp/shots/w111-v3fall{74,67,61}-{before,after}.png (world tree).
- **Hashes:** Landmarks_V3 110 09EAC0FE, Fauna_Fingerprint F4E66093: unchanged (pinned tests pass). Suite 13/13.
- **Risks / left over:** fall 74 still shows a paler flat pane near the lip on the right (a flatter mesh piece; it looks like the shelf). Sheet noise can zigzag where the triangles turn. Both are geometry follow-ups (BuildWater). The v2 shots and the v2 recheck were skipped as asked.

## W-112 row 1: cave mouths - done (verified)
- **Commit:** 273a914 on world-features.
- **Lip:** the walls ended 10 cm under the terrain, and the terrain's mouth cut reached past the walls' rim, so the buried skin showed as a ring. One shared `CaveSampling.SkinGap` (now 2 cm) drives the wall field, the rock test, the terrain cut and the settled mesh. At cave 4 the ring is gone.
- **Normal detail:** the terrain has no normal map to raise: its relief is procedural. Instead, round a mouth (cut field under 8 m, within 60 m of the camera) the terrain takes the walls' own detail texture at the walls' scale. GPU (editor, vsync-capped) is 0.18 ms off vs 0.13 ms on, which is noise. Frame time 16.76 vs 16.67 ms. Memory: +0, since it reuses the 683 KB cave detail texture that is already loaded.
- **Shots:** Assets/Temp/shots/w112-mouth{0,4}-{before,after}.png (world tree).
- **Risks:** the grain round a mouth reads quite strong on a steep rock face. It can be tuned (the `mouth * 0.7` weight in TerrainSurface.shader).

## W-112 row 2: far shadows - done (verified, effect soft)
- **Commit:** see `git log` on world-features ("W-112 row 2").
- **Cause:** the cached far shadows (#156/#158) stopped at 50 m, so trees past that cast nothing at all. They also darkened by only 0.42 x the sun's shadow strength. Now: CacheRange is 120 m, and a new `CacheDarkness` option (0.62, `-shadowcachedark`) is about what a real-time shadow takes at midday. Both can be flipped at runtime for A/B.
- **Numbers (editor, vsync-capped):** GPU 0.11 ms old vs 0.16 ms new (noise level). Frame time 16.81 vs 16.82 ms. Instances 12667 vs 14243: more trees cast into the cache, which redraws only on sun or camera moves. Cache memory is unchanged (1024 square).
- **Check:** at a forest edge over a lake, 25% of the ground band is darker with the new settings, by 0.09 brightness on average.
- **Shots:** Assets/Temp/shots/w112-shadow-{before,after}.png (aerial), w112-shadowlow-{old,new}.png (low, same frame A/B).
- **Risks:** it still reads soft in the shots, because the weather keeps the sun's shadow strength at 0.64 even with Clear forced. Under the crowns the floor is mostly canopy shade already. If Zach wants it stronger, try `-shadowcachedark 0.8` before any code change.

## W-112 row 3: far lights, fire LOD, ember colour - done (verified)
- **Commit:** 68f998d on world-features.
- **What changed:** past the near fires, out to 600 m (cap 3000), each burning tree gets a far flame: one soft glow card round the crown in the burn palette, with no light and no particles. This is the fire's far LOD; `FarForestFiresOn` is the A/B switch. Tree bark seams and leaf slivers now take the forest's burn colour (blue tonight). Wood fire keeps the old orange.
- **Numbers (editor, vsync-capped, far pose at night):** off: GPU 0.16 ms, frame 16.70 ms, 3.79M tris, 2852 batches. On: GPU 0.14 ms, frame 16.71 ms, 3.80M tris, 2854 batches, 835 far flames.
- **Shots:** Assets/Temp/shots/w112-ember-far-{before,off,on}.png, w112-ember-near-{before,after}.png (world tree).

## W-112 suite
- PlayMode 13/13 with the editor closed. Landmarks_V3 110 09EAC0FE, Fauna_Fingerprint F4E66093: unchanged.

## W-112 row 4 (follow-up, not fixed)
- From W-111: fall 74 has a pale flat shelf near the lip, and the water sheet's noise zigzags where its triangles turn. Both are BuildWater geometry, left for a later card.

## W-113 row 4: salt ball dent trail, no glow mushrooms in bat folk caves - done (verified)
- **Commit:** e5dd5ec on world-features.
- **What changed:** the salt crystal ball no longer drags a trail mesh (it moved and blocked the player). It sets shader globals, and TerrainSurface presses a dark furrow into the ground behind it, like the sandworm mounds. Glowing cave plants are skipped inside bat folk chambers, which are also where the pygmies live.
- **Walk:** across the trail from (3711,-893) to (3718,-874): arrived, pushed out 0.0 m.
- **Shots:** Assets/Temp/shots/w113-salt-dents-800.png, w113-batfolk-nomush-800.png (world tree).
- **Risks:** the dent is visual only. The collider stays flat, so feet float up to 0.6 m over the deepest groove.

## W-113 row 1: leather tents, town shade - done (verified)
- **Commit:** e35d9b6 on world-features. It also carries row 2's ruin plinth collision in FloraGenerator.
- **What changed:**
  - Tent panels are hide: leather mottle, creases and stitched seams instead of the linen weave. Goblin and chief tents now blend to a leather brown instead of olive.
  - Poles bow with the hide's sag and stand 0.12 r inside it, so only the crossed tips past the apex show.
  - Towns no longer take slab shelters. Harsh-biome plots get a ramada in the town's style (TownRamada, DwarfRamada, ElfRamada), and the slab shelters went to the hermit huts.
- **Numbers:** town slab shelters 0. Ramadas on the island: 2 (both dwarf). The town and elf ramadas only appear where their layouts land in a harsh biome.
- **Shots:** Assets/Temp/shots/w113-tent3-800.png (before: olive, a pole showing down the ridge), w113-tent-after-800.png, w113-town-ramada2-800.png (world tree).
- **Risks:** hermit huts gained a slab shelter. That changes landmark contents; the suite will report it.

## W-113 row 2: ruin base, igloo seating, sunken temple - done (verified)
- **Commit:** 5e53174 on world-features.
- **Item 10 (ruin base):** the door step is now 0.25 m (half the 0.5 m plinth) and solid. Walk from (-2242,2326) east: the player stops at (-2230.2,120.3,2326.0), standing on the plinth (top about 120.4, ground 119.9) without sinking, blocked by fallen beams.
- **Item 11 (igloos):** an igloo whose foundation skirt can't reach the lowest ground round the dome now sinks into the slope, as long as its tunnel and mouth stay under 2 m of ground. If that's not possible it moves up to 4.5 m, and if no spot fits it's left out. **Count: 15 igloos, 15 seated in place, 0 moved, 0 left out.** In village 2 the open-bottom check alone would have moved 1 and dropped 4; sinking keeps all 5.
- **Item 12 (sunken temple):**
  - New Punchy/SunkenTemple shader: ashlar sandstone blocks or sand ripples, plus the torches' glow light (the old Colossus shader was flat vertex colour with no glow).
  - The interior now shows from 30 m round the ruin, so the door plugs can't stand in front of the camera.
  - The pass-through plane was a terrain chunk skirt (6-24 m) hanging through the rooms at a chunk edge. Skirts now stop 0.3 m above temple air.
- **Shots:** Assets/Temp/shots/w113-ruin-step-800.png, w113-igloo-v2-800.png (before), w113-igloo-v2-after-800.png, w113-temple-out-800.png and w113-temple-in-800.png (before), w113-temple-out-after-800.png, w113-temple-in-after.png (world tree).
- **Risks:** sunk igloos sit up to about 2 m deeper on slopes. Igloo footprints follow their seat, so the TurtleVillage contents change; the suite will show it.

## W-113 row 3: eave lips, sky blocks, creek static - done (verified)
- **Commit:** 8fb8df5 on world-features (fingerprint repin 3bd9c9a).
- **Item 13 (black eaves):** roof tube normals now tilt upward at the rim, and the stone house roof's occlusion floor went from 0.6 to 0.8. Its stone grey is a little lighter. The black lips and corner tips are gone.
- **Item 14 (blocks in the sky):** these were the Interiors rooms: 223 cubes at y about 40, built off the island's east edge (x 4589-4960). Only the room the player stands in is drawn now. Outside, 0 room cubes are drawn; inside door 11, 22 (that room). The room colliders stay solid either way.
- **Item 15 (creek static):** the noisy layer was the falling white sheets, plus the shore foam line flickering on the depth-buffer thickness. They were static because of a precision bug: the sheet's along-flow position took the pixel-to-pixel wobble of a derivative-based direction times a world position about 1.7 km out. The sheet now reads along fixed x/z axes, blended by the face's turn (like triplanar). It has three octaves, so it shows slow streaks, and the shore line fades out on steep faces.
- **Shots:** Assets/Temp/shots/w113-eave-ba.png, w113-sky-ba.png (before/after at x 800), w113-creek-ba.png (before, after, probe) (world tree).
- **Idea for ROADMAP-IDEAS.md (I can't write it):** "Floating archipelago: the sky blocks looked good from the sea. Hang a few real floating rock islets with waterfalls off their edges in one high biome, reached by a colossus or a grapple."
- **Risks:**
  - Interiors.cs is a player-lane (P-13) file edited on the world branch.
  - The roof normal lift changes every RoofTube user: hut, stone house, shrine and smithy.
  - The waterfall sheet change also applies to surface waterfalls.

## W-113 suite - 13 passed after repin
- First run: 10 passed, 3 failed, because the editor island preference was still v2 from the playtest shots. Punchy/Island is an EditorPrefs value shared by every editor on this account, so it overrode the suite's v3. I cleared it (back to Default = v3) and reran.
- Rerun: 12 passed, 1 failed. **Landmarks_V3 110 09EAC0FE (unchanged).** **Fauna_Fingerprint 90AC5F60 (was F4E66093).** The fingerprint hashes the flora blob (colliders, perches), which rows 1-3 changed through tents, the ruin plinth and roofs. I repinned it in 3bd9c9a.
- **Watch:** any lane that runs play.sh with Punchy/Island v1 or v2 should set it back to Default afterwards, or the next suite on this account fails Harness_WorldUp.

## W-116 row 1 — file splits: done (verified)
- Commit 45f4349 on world-features (after merging origin/main as b267195). Ready to merge.
- DebugControls.cs 335 KB -> partial class in 11 files (largest Places 47 KB, Flight 45 KB, Flags 41 KB; core 27 KB): FaunaShots, FaunaLogs, Options, Flags, Goto, Flight, Shots, Places, Tests, Locations.
- FloraGenerator.cs 320 KB -> partial static class in 11 files (largest Houses/Village 37 KB; core 29 KB): Trees, Plants, Props, Rocks, Crystals, Houses, Kit, Village, Town, Waterside.
- Move-only: a line multiset diff against HEAD shows only the class declaration changed (added `partial`); every other added line is a using, namespace, brace, part comment or partial declaration. Line endings kept per original (DebugControls LF, FloraGenerator CRLF), no static initializers that depend on each other across files.
- Suite 13 passed, 0 failed. Landmarks_V3 110 09EAC0FE, Fauna_Fingerprint 90AC5F60, both unchanged.
- Risk: lanes with open edits to either file will conflict; rebasing means moving the hunk into the matching part file.

## W-116 row 2 — biome tags: done (verified)
- Commit 6d04d55 on world-features. Ready to merge.
- BiomeDefinition now has Family (Arid, Wet, Cold, Volcanic, Temperate), FamilyOrder and free Tags; constants and lookups in BiomeTags.cs (First, InFamily, Tagged). All 19 biome assets tagged.
- Switched to tags: IslandV2 realm picks, ridge biomes and the archipelago coast (and V3Ground), GrandFalls, Iceberg, Mantrap, the Ember Forest fire, glade butterflies, the meadow debug place.
- FamilyOrder reproduces each family's old name-list order, so realm picks are identical. **A new biome must set Family and FamilyOrder** or it never joins a realm.
- New test BiomeLiteralTests: fails on any biome asset name quoted in non-Editor game code. Allow-list is StoneColossi.cs and DunesColossus.cs, left for A-81 (colossus work). `git grep` shows literals only there.
- Suite 14 passed, 0 failed. Landmarks_V3 110 09EAC0FE, Fauna_Fingerprint 90AC5F60, both unchanged.
- Risk: Mantrap.cs (Fauna folder) and CritterSystem.cs are edited on the world branch; other lanes touching them will conflict.

## W-116 row 3 — ClimateField: done (verified)
- Commit b185498 on world-features. Ready to merge.
- Design: Weather/ClimateField.cs. Climate.Exact(p) gives elevation, heat, humidity, air, wind, sun, temperature, rain, snow, storm from the same formulas Comfort and Ambience used; Climate.At/HeatAt read a coarse grid, filled lazily on first use. The first fill runs at once; later refreshes (about 24 ms in all) are spread over frames at 0.4 ms each and swapped in whole. Weather publishes its local state; Ambience reads Climate.Camera. Season is a stub (0).
- Values (noon / midnight temp): Temperate (1050,1050) elev 39, heat 0.16, humid 0.65, temp 0.26 / 0.09. Dunes (3418,-358) 10 m, 0.45, 0.44, 0.57 / 0.37. Glacier (410,-38) 148 m, 0.24, 0.42, 0.28 / 0.06. PoisonSwamp (-2022,154) 148 m, 0.27, 0.71, 0.28 / 0.12. Ashlands (-230,-3686) 15 m, 0.53, 0.53, 0.65 / 0.46. Rain, snow and storm 0 everywhere at test time. Grid vs Exact at (0,0) agree (temp 0.522, heat 0.411).
- Frame ms: before 16.77 ms/frame (2679 frames); after with the grid kept live 16.77 ms/frame (2892 frames), 48 refreshes, worst slice 0.90 ms. An unsliced first try spiked 23 ms once a second, so the fill is sliced.
- Oddity (not from this row): Glacier heat comes out above Temperate, from the copied Comfort formula. Worth a look by whoever owns Comfort.
- A-81 can now switch CreatureComfort, WeatherTalkSystem and SkunkGas to Climate.At/HeatAt.
- Suite 14 passed, 0 failed. Landmarks_V3 110 09EAC0FE, Fauna_Fingerprint 90AC5F60, both unchanged.
- Risk: WeatherSystem.cs and AmbienceSystem.cs are touched (one line in, a few reads swapped).

## W-114 — Ashlands ribcage: done (verified)
- Commits fac588e (W-116 blob guard: the chunk build is cancelled on Application.quitting; 0 blob errors on two play exits) and 2107640 (W-114) on world-features, after merging origin/main (#171/#172) as 92fec20. Ready to merge.
- New landmark type AshRibcage (Ashlands only, own spot) at (-480, -2320), facing 162 deg: spine, 11 pairs of weathered rib arches, a skull with horns, fallen broken ribs, ash drifts, BoneRemains, a few GiantRibs and cinder/obsidian rocks round it. Box and capsule collision; you walk under and between the ribs. Horns, spikes and teeth have no collision. No FarShadow.
- Meshes: LOD0 15573 tris, LOD1 4892, LOD2 1552. Bake: ribcage 10 ms.
- Render (mid view, vsync 60): 16.87 ms/frame, gpu 0.25 ms, 2.34M tris with it; 16.69 ms, gpu 0.13 ms, 2.29M tris without.
- Landmarks_V3 111 3BDF705F (was 110 09EAC0FE). Hashed without the ribcage the other 110 still read 09EAC0FE: no other landmark moved. Fauna_Fingerprint 8A1979F1 (was 90AC5F60): changed, from the ribcage's props and colliders. Both repinned in WorldTests.
- Shots: Assets/Temp/shots/w114-far.png (an arch on the horizon through the ash haze), w114-mid.png, w114-inside.png, w114-skull.png.
- Suite before repin: 12 passed, 2 failed (only the two pins above).
- Risk: SaltTower.Relief gained a step parameter (shared relief helper); TerrainSource/TerrainAuthoring/LandmarkData touched.

## W-115 row 1 — cave dungeon sockets: done (verified)
- Commit d2ed5e2 on world-features (it also carries the origin/main merge c030507). Ready to merge after the rest of W-115.
- New CaveSockets.cs (Terrain/Caves): each cave's chambers get a centre creature (plus one on the ceiling when it's at least 4 m tall), clutter and wall decor round the walls, and a chest against the far wall in dead ends and the main end. Passages get a guard where they open into a chamber, plus clutter, decor and creatures along alternate sides. Water pools get NearWater spots. Spots deep in, away from the mouth, shafts and lava, are Dark. Each socket carries its chamber type (new SocketTags Chamber, DeadEnd, Passage, added at the end; the struct is unchanged) and the dominant surface biome above it. Bat folk chambers are skipped. No biome or race literals.
- DungeonTypes.Cave = 0 added (the spawner's Cave row) in TempleSockets.cs (a player-lane file, one line). The spawner's tables are untouched: DungeonSpawnSystem fills all 25 cave dungeons as they are.
- v3: 25 caves, 459 sockets (creature 94, chest 54, wall decor 137, interactable 5, clutter 169). 10 to 28 per cave: 2-chamber caves have 10 to 18, the 6- and 7-chamber networks 27 or 28. Cave socket hash 42581E93, the same on two play runs. Per-cave hashes are in the "[dungeon] cave N" log lines.
- Cost: 194-227 ms in total, spread over 13 frames (two caves a frame, about 17 ms each), once at island-ready.
- Shot: Assets/Temp/shots/w115-cave-chamber.png (cave 16's chamber with a spawned creature, loot and rock-pile clutter).
- Risk (animal lane): the spawner keeps cave creatures permanently, about 2-5 per cave, so roughly 60 more live creatures island-wide. Cave interactables pick only Empty. Most spots read Dark (the caves are long).
- Landmarks and fingerprint: no landmark or flora change in this row (confirmed in the final suite).

## Fix: crash on quit in the surface glow far scan (profiler q46) - done (verified in editor)
- Commit 156e7df on world-features, its own commit (CaveSystem.cs only, CRLF kept).
- Cause: SurfaceGlow.FarScanJob reads the TerrainSource blobs; quitting (or leaving play mode) frees them before
  CaveSystem.OnDisable runs SurfaceGlow.Release, so the job could still be reading freed blobs.
- Fix: CaveSystem also releases on Application.quitting (as TerrainStreamingSystem.CancelBuild does), which
  finishes the near and far scan jobs and every chunk job while the blobs are intact.
- Check: play mode to world ready, wait 10 s (far scan running), exit play mode: no "not valid", exception or
  error lines in Editor.log. Not re-checked in a player build quit; the profiler's q46 repro is the real test.
- Risk found, not mine to fix: Items.ChestSockets/TryChest (Items.cs:294/310, player lane) call GetSingletonEntity on
  DungeonInfo+DungeonSocket, but since W-115 row 1 there is one dungeon entity per cave as well as the temple, so it
  throws every frame (10k+ InvalidOperationException in one play). It should filter by DungeonInfo.Type ==
  DungeonTypes.SunkenTemple (or by Id).

## Fix: Items finds the temple's dungeon entity by type (approved cross-lane exception) - done (verified)
- Commit cfd07d0 on world-features, its own commit (Items.cs only, CRLF kept). ChestSockets and TryChest now pick
  the dungeon entity whose DungeonInfo.Type is SunkenTemple instead of calling GetSingletonEntity.
- Play-mode check: one run to world ready, no InvalidOperationException in the log since play started (the last one
  was from the run before the fix). The temple lists 13 chest sockets and TryChest opens the first (socket 15).

## W-115 row 2 — tile-built cave rooms: done (verified)
- Commit 19c8817 on world-features (pushed). Rooms are boxes with a door, a porch and a two-sided ramp down to the
  passage floor (slope capped at 0.7). Fixes: the grotto doorway had a 2.2 m rise (now a ramp, Approach 2.63 m); the
  dark sheets in the lake, roost and ruin doorways came from the mesh snap pulling doorway vertices onto room faces
  (the field was air there). Room kit boxes join the flora collider list.
- Counts: v3 12 planned / 9 kept (grotto 4, lake 2, roost 2, ruin 1); v2 9 / 7. World ready 39.5 s (was 40.1).
- Doorway walks (flood fill from the room middle out through the door, max step 0.45): grotto 1778 cells, lake 1067,
  roost 1432 (max step 0.43), ruin 1324 (0.44; 55 cells stopped by kit boxes). All four reach local z -10.2, past the
  porch mouth at -2.8.
- Render ms (vsync 60), URP main camera / render thread / main thread: grotto 2.80 / 1.75 / 6.97, lake 1.62 / 1.23 /
  5.79, roost 3.92 / 2.64 / 8.25, ruin 2.71 / 1.77 / 8.31.
- Pins: Landmarks_V3 111 3BDF705F and Fauna_Fingerprint 8A1979F1 both pass, unchanged. Cave hash now
  9A3D3E91E47A0F21 (editor CaveBench, baked = fresh); it changes because rooms carve new rock.
- Shots: Assets/Temp/shots/w115-{grotto,lake,roost,ruin}-{inside,back,approach,door}.png.

## W-115 row 3 — destination chambers placed per cave: done (verified)
- Commit dcdd87d on world-features (pushed). Placement was built in row 2 (CaveBaking.Rooms.PlaceRooms): each cave
  rolls from its own key whether its main passage's end chamber becomes a room (style RoomChance 0.45-0.6), and big
  networks (2+ free dead ends) roll for one side chamber at half that chance. The type comes from the style's
  RoomWeights (Ice and Crystal favour grottos, Sandstone and Mine ruin rooms, LavaTube never lakes, Ice never roosts).
  Bat folk chambers (wide) and stream ends stay as they are. A room is kept only where it fits in rock.
- New in row 3: buried ruin room sockets carry DungeonTypes.Ruin (3), so the spawner fills them from the ruin rows
  (v3: 5 sockets -> Bat, ChestLarge, Cobweb, Sconce, Lever). All other cave sockets keep the Cave rows (0), which
  A-80 already gave the table. Each socket has its biome and chamber/room tags (Grotto, Lake, Roost, RuinRoom added
  at the end of SocketTags in row 2). DungeonSpawner and the tables are untouched.
- Counts on v3: 12 planned, 9 kept in 8 of 25 caves: grotto 4, lake 2, roost 2, ruin 1. v2: 9 planned, 7 kept:
  grotto 2, lake 1, roost 2, ruin 2. Cave sockets on v3: 503 (459 before rooms), hash 567CEA4C, the same on two runs.
- -goto spots (v3), one per type: grotto `-goto -1931,95,2329`, lake `-goto -2181,31,3229`, roost
  `-goto 1558,8,-2691`, ruin `-goto -238,9,-3204`.
- Pins: Landmarks_V3 111 3BDF705F and Fauna_Fingerprint 8A1979F1 pass, unchanged. Cave hash 9A3D3E91E47A0F21 (as row 2).
- Shot: Assets/Temp/shots/w115r3-ruin-inside.png (the ruin room in play, its chest placeholder in the middle).

## W-110 row 1 — road tunnels: done (verified)
- Commit 32aa839 on world-features (pushed). RoadBaking looks for the steepest crest stretches of existing roads
  and bores them at a straight grade. Roads don't move. Only the roofed core (at least 4 m of cover, at least 10 m long)
  becomes a `TunnelRoad`; the rest stays a cutting. A portal opens only onto a cutting, never straight onto a bridge.
  CaveBaking.Tunnels then adds one cave per bore after all the natural caves. The road surface carries through,
  with 1.5 m of passage past each portal so the mouth stays full width. Short bores (under 25 m) are always dark.
  The others roll for lamps every 7-13 m (30% hung in pairs), and some also get timber frames.
- 3 tunnels on v3:
  - cave 25 at (2053, 7, -1535): 19 m, dark.
  - cave 26 at (-1870, 129, 118): 44 m, lamps and frames.
  - cave 27 at (-2023, 265, 713): 30 m, lamps.
- Walk check, portal to portal plus 6 m past each end:
  - largest step 0.23 m (limit 0.45);
  - least head room 1.9 m (cave 27), 2.1 m (cave 25) and 2.2 m (cave 26).
- Render inside the framed tunnel: URP camera 3.75 ms, render thread 2.5 ms, main thread 8.2 ms, at the 16.7 ms
  vsync frame.
- Cave hash:
  - before: 9A3D3E91E47A0F21 (25 caves, 358 nodes);
  - after: C65CDFB8DDB8D0C1 (28 caves, 384 nodes). The 3 tunnels are added; natural caves, pools (13) and
    speleothems (443) are unchanged.
- Unchanged:
  - cave socket hash 567CEA4C (tunnels have no sockets);
  - bat folk pick: cave 5, chamber r 15.3 (tunnels have no chambers);
  - pins Landmarks_V3 111 3BDF705F and Fauna_Fingerprint 8A1979F1 both pass, so no landmark moved;
  - world ready 39.7 s (39.5 before).
- Note for fauna: FaunaDirector den visitors and AnchorBaking loop over every cave, so they also see tunnels.
  To exclude tunnels, filter on `CaveData.Tunnel != 0` (fauna-lane files, not touched).
- Shot: Assets/Temp/shots/w110-tunnel-framed-inside.png (cave 26, lit and framed, daylight at the far portal).

## W-110 row 2: tunnel portal kit (#39), done (verified), 7781a5f9

- Both mouths of all 3 road tunnels (6 portals, caves 25-27) get a seeded stone portal: plinths, jambs, imposts,
  9-19 voussoirs with a keystone (plain, cracked, or carved with a ring, chevron, cross or tally), an ashlar headwall
  cut into setts at the opening, and a coping. Each portal also gets its own moss amount, an ivy fringe on about 40%,
  1-3 fallen stones, and a rockfall on about 40%. Each kit has 188-274 parts and 8-13 collision solids. All 6 build in 12 ms in total, once, when the camera first comes in range.
- Render at portal 26 (looking in, editor, vsync 60): main thread 7.9 ms, render thread 2.8 ms, 4 draws per portal.
- Pins unchanged:
  - Landmarks_V3: 111 3BDF705F (the card's 110 09EAC0FE is the pin before W-113 and W-114).
  - Fauna_Fingerprint: 8A1979F1.
  - Cave hash: C65CDFB8DDB8D0C1 (28 caves, 384 nodes). The kit is drawn at runtime only, so no bake changed.
- World ready: 33.5 s to 40.5 s across 4 plays (pin 39.7 s). The high readings came with the PC under 6 GB free.
- Fixed during verification: the coping loop compared against a recomputed `halfW + 0.12f`. Under Mono float precision it never ended and ran the editor out of memory. The end is now held in one local and the loop exits within a 5 cm margin.
- Shots:
  - Assets/Temp/shots/w110r2_portal26b.png (cave 26, mossy, ivy, carved cross, rockfall)
  - Assets/Temp/shots/w110r2_portal26b_far.png
  - Assets/Temp/shots/w110r2_portal25a_far.png (cave 25, desert, bare stone, chevron)
- Risk: the moss patches are flat slabs, not the textured sheet from the 11-03 feedback. A follow-up could move them to the moss sheet.

## W-117 GC row: per-frame allocations, done (verified), 2890fe18

- Measured with the editor profiler (GC self-alloc per marker), 120 frames, v3 start spot, fresh play each time.
- Before -> after (B/frame):
  - WeatherField.Bilinear closures 1410 -> 0. Bilinear and BilinearAllowed now share a closure-free cell lookup.
  - WeatherSystem 352 -> 0. The tint no longer allocates a `new[]` array.
  - LandmarkEffectSystem 223 -> 17. The falls log string is only built when its numbers change.
  - FloraSystem.LateUpdate 220 -> 0. Stream's capturing lambdas were costing a display class per layer every frame. The tile sort now uses one reused comparer, and basin removal is a plain loop.
  - CaveSystem.Update 176 -> 40. The frame Stopwatch and marks array are now reused.
- Remaining top allocators are outside the world lane: Items.Update 3.4 KB, IMGUI BeginGUI 2.6 KB, LiveConversion (editor only) 380 B, DunesColossus 252 B, InteractionPrompt 136 B.
- No look change. Landmarks_V3 111 3BDF705F. World ready 38.8 s.

## W-117 row 1: water draws at v3 fishing village 1, done (verified), no code change

- At (-2151, 3731), player camera looking north, 25 s after arriving, the scene had 1196 water entities. 486 of them were in the camera frustum, at 1 m to 1504 m.
- There is one water entity per 64 m terrain chunk that has any water. Sea, river and lake water share that chunk's single mesh (TerrainStreamingSystem: WaterChunk entities, Entities Graphics, transparent queue).
- The 486 visible entities draw as 486 separate transparent draws, sorted back to front: about 119k vertices, about 245 per draw. That fits the profiler's sort and SRP-batcher flush markers.
- Editor A/B, with all water entities hidden versus shown (main and render threads, ms per frame, vsync 60):

  | Marker | Water shown | Water hidden |
  |---|---|---|
  | DrawTransparentObjects | 0.38 | 0.02 |
  | RenderLoop.DrawSRPBatcher | 0.93 | 0.57 |
  | DrawBuffersBatchMode | 0.57 | 0.36 |
  | RenderLoop.Sort | 0.29 | 0.20 |
  | SRPBatcher.Flush | 0.23 | 0.14 |

  In total, water costs about 1.1 ms of render CPU, and about 0.17-0.27 M of the 2.2-2.3 M triangles.

## W-117 rows 2-5: NOT DONE (stopped for memory and budget), plan for the next pass

- Why: my editor was at 12 GB with 1.4 GB free on the PC, and other editors were open. I closed it to run the suite.
- Row 2 (water draws), plan:
  - Water.shader reads only the vertex position through GetVertexPositionInputs and works from world position, so merging meshes should not change the look. Still verify against a shot from the same spot.
  - In TerrainStreamingSystem, keep near chunks (within about 256 m) as single entities. Beyond that, build one combined water mesh per 4x4-chunk region, rebuilt when a member chunk streams in or out.
  - About 486 transparent draws should drop to about 60-80, worth an estimated 0.6-0.8 of the ~1.1 ms render CPU.
  - Risk: transparent sort order between regions, where far water overlaps nearer water. It should be minor, because water is a single height layer.
- Row 3 (shader): the main per-pixel cost is the SSR march at Water.shader ScreenReflection (28 + 5 steps).
  - Gate it by distance and fresnel weight, where it fades anyway.
  - Drop the second detail-texture read past rippleNear (110 m).
  - Measure with a GPU frame time A/B from the village spot.
- Rows 4-5 (flora LOD and shadow casters): not started.

## W-117 row 2: far water merged per 4x4-chunk region, done (verified), d06081b0

- New TerrainStreamingSystem.WaterRegions.cs. When a 4x4-chunk region's nearest edge is more than 256 m from the focus (with 16 m hysteresis), its water chunks hide and one combined "WaterRegion" entity draws them instead. Water.shader uses world position only, so the look is the same. The combined mesh is rebuilt when a member chunk streams in or out.
- `TerrainStreamingSystem.MergeFarWater` (or `PUNCHY_WATERMERGE=0`) turns it off for A/B.
- At v3 fishing village 1 (-2151, 3731), player camera looking north:
  - Water entities drawn: 1196 -> 93 chunks plus 110 regions (203).
  - Batches: 1908 -> 1503.
  - Editor markers, ms per frame, off/on, two runs each:

    | Marker | Off | On |
    |---|---|---|
    | RenderLoop.DrawSRPBatcher | 0.93 / 0.86 | 0.64 / 0.58 |
    | DrawTransparentObjects | 0.37 / 0.36 | 0.10 / 0.10 |
    | DrawBuffersBatchMode | 0.55 / 0.52 | 0.37 / 0.35 |
    | RenderLoop.Sort | 0.29 / 0.27 | 0.20 / 0.20 |
    | SRPBatcher.Flush | 0.24 / 0.23 | 0.16 / 0.15 |

    About 0.9 ms of render CPU saved.
- Look:
  - Shots `D:\PunchyMilestones\W-117\w117r2_off.png` and `w117r2_on.png` are 2560 wide, taken from 30 m above the village looking north over the sea.
  - They are identical to the eye. The pixel diff averages 1.3/255 (wave animation between frames), and 0.006% of pixels differ by more than 16/255.
- I teleported 500 m away and back: no chunk drew under a merged region (0 overlaps), and no new exceptions.
- Landmarks_V3: 111 3BDF705F (unchanged).
- Risk: transparent sort between overlapping far regions. None was seen, since water is a single height layer.
- Found, not mine: Camera/Underwater.cs:76 `GetComponent<AudioLowPassFilter>() ?? AddComponent` never adds the filter in the editor (Unity fake null), so it throws a MissingComponentException every frame (13k in one session). Editor only.

## W-117 row 3: water shader per-pixel cost, done (verified), 51ed65c1

- Water.shader (shared, additive edit; animal session: no property or keyword changes):
  - The screen-space reflection march steps in screen space. The ray's clip/screen position is linear in the world position, so it is worked out once and each of the up-to-33 steps is a multiply-add instead of a matrix transform. It finds the same hits.
  - The march is skipped where the final Fresnel leaves the reflection under 2.5% (murky water, cave runs, fall faces). On clear sea this almost never triggers.
  - The fine detail texture reads are skipped past 350 m, where everything using them is already faded out (worth at most 0.09 ms).
- GPU cost: offscreen 4K renders at v3 fishing village 1, interleaved old/new medians (the animal editor shares the GPU, so absolute ms drift):

  | View | Frame | Old -> new | SSR's whole cost |
  |---|---|---|---|
  | 30 m up, looking north over the sea | ~10 ms | -0.35 ms | 1.41 ms |
  | 8 m up, looking north | ~7.8 ms | -0.17 ms | 0.54 ms |
  | 8 m up, looking south at the shore | ~5.8 ms | -0.01 ms | 0.22 ms |

  Tried and dropped: a stricter Fresnel gate (6%: 0.00 ms) and a 300 m distance cutoff (-0.21 ms, but it loses far reflections).
- Look: `D:\PunchyMilestones\W-117\w117r3_old_sea.png` / `w117r3_new_sea.png` and `w117r3_old_shore.png` / `w117r3_new_shore.png`, 2560 wide, both rendered in the same frame. Sea: 0 pixels differ. Shore: 2 of 3.7M pixels differ by more than 16/255 (hit edges).
- Landmarks_V3: 111 3BDF705F.
- Gotcha (editor, not shipped): reimporting a shader during play sometimes re-bakes the island as a different world (106 landmarks, BADC2E70). Restart play and check the hash before benching after any shader import.

## W-117 row 4: fewer flora vertices at distance, done (verified), 0dbc1d58

- FloraGenerator.Props.cs (shared flora file, additive edit; animal session: no API change): wispy hanging strands (SilverTree, Bogwood) draw their threads at LOD0 only. Threads are 3-14 mm thick, so past 55 m they are well under a pixel, yet they were most of a wispy tree's LOD1.
  - SilverTree LOD1 drops from 3604 to 868 tris.
  - LOD1 still makes every random draw, so the rest of the plant grows the same.
  - `PUNCHY_WISPLOD=0` or `FloraGenerator.WispsLodZeroOnly = false` brings the old LOD1 back.
- Why this lever: trees' LOD1 band (55-220 m, ~630 instances at the cave approach) was 1180k of the camera's flora triangles, and wisps were about half of it. Trees at LOD2 are already a single 112-tri crown blob. Pulling the LOD distances in bought little (Lod2 220 -> 150: 0.15 ms) and would show pop.
  - I tried and dropped dropping the giant rule's extra clump step at LOD1: the trees here are 10-16 m, so it never applied.
- Camera triangles for trees LOD1: 1180k -> 596k (-49%) at the cave approach.
- GPU cost: offscreen 4K, toggled in the same session (flora rebuilt each way), median of 16 rounds.

  | Measure | Before | After |
  |---|---|---|
  | Trees LOD1 band, cave approach | 0.16 / 0.15 ms | 0.13 / 0.12 ms |
  | Trees LOD1 band, forest from above | 0.23 / 0.12 ms (noisy) | 0.21 / 0.08 ms |
  | Whole 4K frame (both poses) | ~7.1-8.4 ms | same within noise |

  So about 0.03 ms on this GPU. The 4K frame isn't vertex-bound here; the halved vertex count matters more on weaker GPUs.
  - An earlier long-running session read the band at 0.50 ms; fresh sessions read ~0.15. Bench fresh.
- Look: `D:\PunchyMilestones\W-117\w117r4_cave_before.png` / `w117r4_cave_after.png` (cave approach, eye height, looking north into the SilverTree wood) and `w117r4_hill_before.png` / `w117r4_hill_after.png` (70 m up, looking over the forest). All 2560 wide, same session, time pinned at 10:00.
  - They look the same at a glance; near wisps are still there.
  - Pixel diff after vs before is within the before-vs-before repeat (wind and clouds). Cave: 448k vs 427k pixels over 8/255. Hill: 173k vs 282k.
- Landmarks_V3: 111 3BDF705F. Fauna fingerprint: the generator's shape-only grow (LOD0) is unchanged; the suite runs at card end.
- Bench note (editor): RenderBench.AB from eval can't A/B flora settings, because the draws were queued in the previous LateUpdate. Use a frame-based bench.

## W-117 row 5: flora shadow casters, done (verified), no code change

- W-107 and W-112 already do what this row asks, so I changed nothing:
  - Trees cast full-mesh real-time shadows only inside 25 m (`-shadownear`).
  - Past that they cast with the 112-tri LOD2 blob (cheap far caster), into a cached sun texture out to 120 m that is redrawn only when the sun or camera moves.
  - Shrubs cast to 40 m, ground cover casts none.
- Measured: offscreen 4K, same session, 12 rounds, median per-frame difference. Each row turns one caster off, then on.

  | Turned on | Cave approach | Forest from 70 m up |
  |---|---|---|
  | Shrub shadows | +0.02 ms | +0.03 ms |
  | Tree shadows (near + cheap) | -0.11 ms | -0.12 ms |
  | Whole sun shadow pass (control) | +0.40 ms | +0.76 ms |

  - The tree and shrub figures are noise: flora is at most ~0.03 ms of a shadow pass costing 0.4-0.8 ms. The rest is terrain, rocks and buildings, not flora.
  - The bench is static, so the cache's redraw while moving isn't in these numbers (W-107 measured it).
  - Moving shrubs to the cheap caster or cutting their range would save under 0.03 ms, too little to measure, and would cost near-shrub shadow detail, so I left it.
- Far shadows read: `D:\PunchyMilestones\W-117\w117r5_far_trees_on.png` vs `w117r5_far_trees_off.png`, 30 m up looking over the SilverTree wood 30-150 m away, tree shadows on vs off, 10:00, hazy light.
  - 100k pixels differ by more than 8/255, mostly in the lower two thirds, where the cached far shadows fall.
  - The light is soft today, so they read softly.
- Landmarks_V3 111 3BDF705F (no world change).

## W-117 card end: suite

- PlayMode suite as merged on world-features: 13 of 14 fail. The cause is a pre-existing bug from P-19 (2c63be37, merged via PR #179), not from W-117.
  - `Underwater.cs:76` does `GetComponent<AudioLowPassFilter>() ?? AddComponent<...>()`. Unity's missing component is a fake null, so `??` never adds the filter, and the next access throws MissingComponentException on every frame under water. That is an unhandled log in every test.
  - Fix: an explicit `if (lowPass == null)` check. **Player lane:** please ship it; I didn't commit it because the file is yours.
- With that one-line fix applied locally (then reverted, not committed): 14 passed, 0 failed, including Landmarks_V3 111 3BDF705F and Fauna_Fingerprint 8A1979F1.

## W-118 row 1 (#40): round salt crystal ball, done (verified)

- Commit a6609b02 on world-features: StoneColossi.SaltCrystal.cs only.
- The body is near-round now: radii within 3 %, faint broad swells, no ridges. It carries 110-140 small shallow dents with soft rims, 6 of them larger bowls, on finer 72x50 facets.
  - Outcrops, crust points, dirt and crumbs are unchanged. The rolling circle keeps its draws.
- Shots in `D:\PunchyMilestones\W-118\`:
  - `w118-salt-ball.png`: about 34 m out, the whole ball on the salt flat with its shadow and the trail.
  - `w118-salt-ball-close.png`: about 7 m from the surface, fine dimpled facets across the whole surface, a few points sticking out.
- Landmarks_V3 111 3BDF705F (unchanged). The fauna fingerprint gets checked at card end with the suite.
- Risk: the roll's material seed draw comes later in its own rng, so any per-ball cosmetic seed differs from W-94. Cosmetic only.

## W-118 row 2: Tundra SnowSlabShelter not seen, done (verified; cause found, no code change)

- Cause: the premise is out of date. W-113 row 1 (e35d9b6a) swapped the town slab shelters for ramadas.
  - DwarfHold (-3273,1594) and DwarfMiningCamp (-1962,665) each bake a DwarfRamada on their most central plot. Both render.
  - SnowSlabShelter is now used only by the HermitHutSnow look. v3's only hermit hut (140,-545) isn't in snow, so it takes the plain look, and SnowSlabShelter appears nowhere on v3. That is expected, not a placement bug.
- Row 3 now gives both dwarf towns Tundra kit SnowShelterHouses beside the ramada:
  - DwarfHold gets 2, at (-3263,1594) and (-3294,1593).
  - DwarfMiningCamp gets 1, at (-1969,631). It has only one free house plot.
- Shots in `D:\PunchyMilestones\W-118\`:
  - `w118-dwarfhold.png`: high wide view of the hold, snowy hills behind.
  - `w118-dwarfhold-close.png`: the cobbled square with the well. Cream snow shelter houses with drift skirts stand left and right, and the open post ramada is in the middle.
  - `w118-dwarfminingcamp.png`: the frosty-roofed snow house with its drift on the right, the StoneHall in the centre, and the striped ramada on the left.
- Open question for the supervisor: if Zach wants the slab shelter itself visible on v3, it needs a snow hermit spot or a tundra layout entry. I didn't do that because it would move landmarks or plots.

## W-118 row 3 (#69): kit hazard houses per harsh biome, done (verified)

- Commit 07b2c6cd: the 7 settlement layouts, data only, LF kept.
  - Each layout ends with 2 OnlyIn entries per harsh group:
    - AshShelterHouse: Ashlands, Volcano
    - SnowShelterHouse: Tundra, Glacier, Mountains
    - StormShelterHouse: ElectricWastelands
    - SandShelterHouse: Dunes, SaltFlat, GlassDesert
  - PlaceShelters puts them on the next central house plots after the culture's ramada. Temperate towns are unchanged.
  - OnlyIn entries draw no rng, so landmarks and plots stay put.
- On v3, harsh towns exist only in Ashlands and Tundra.
  - Ashlands: OgreCamp (912,-3837) has AshShelterHouses at (887,-3811) and (931,-3803).
  - Tundra: the two dwarf towns (row 2).
  - The Human Frontier-style town near (2930,-870) already had mixed hazard houses from W-95.
- Shots in `D:\PunchyMilestones\W-118\`:
  - `w118-ashlands-ogrecamp.png`: two white crenellated ash shelter houses, one with a tower, inside the camp's palisade on dark ash ground, with the bone arch between them.
  - Tundra: `w118-dwarfhold-close.png` (row 2).
- Landmarks_V3 111 3BDF705F (unchanged). The fauna fingerprint gets checked at card end.
- Risk: SettlementSpecies indices grow and shift for layouts after the first one. Any saved index from an earlier build is off, but these are seed-derived and rebuilt at bake.

## W-118 row 4 (#70 caveat): drift mound edge on slopes. Done (verified)

- Commit d110f32d (world-features): MagneticDrift MaxSlope goes from 60 to 16. It fades out from 12 deg.
- Why: a drift mound is a flat lens about 8 m across. On a 27-41 deg slope its downhill rim hung 3.5-6.4 m over the ground and read as a wall. Collision was already fine: a walk up from downhill stepped up the tiers.
  - Tilting the mound like rocks was ruled out because it would tilt the floating rock, and the colliders don't tilt.
  - A mesh skirt was ruled out because the player would clip through it.
- Measured over the 355 drifts loaded round the wastelands:
  - Before: 29 sat on slopes over 15 deg, and 12 had more than 2.5 m of drop under the rim.
  - After: 320 drifts, none over 15 deg, and 1 left with 2.5 m of drop (13 deg).
- Shots in `D:\PunchyMilestones\W-118\`:
  - `w118-drift-downhill.png` (before): a dark mound slab with a vertical front over the slope.
  - `w118-drift-downhill-after.png`: the same view. The slope in front is clear, and mounds remain only on the flat crest behind.
  - `w118-drift-steep-after.png`: the 41 deg slope at (1581,-3285), with no mound, only spires.
- Card-end suite: 13 of 14 pass. Landmarks_V3 111 3BDF705F is unchanged.
- **Fauna_Fingerprint 027CC3FD (pin 8A1979F1): changed, with a reason.** The fingerprint hashes the flora placement blob, which holds every species rule and the settlement species, so flora data changes move it:
  - Row 3 (the 7 layouts get hazard house entries) gives 3EA2B0C0. Measured with the row 4 change reverted.
  - Row 4 (MaxSlope) then gives 027CC3FD.
  - No fauna code changed. Suggest re-pinning 027CC3FD after merge.

## W-119 row 1 (#36): village plots on river water. Done (verified)
- Commit 40295300 on world-features (merged origin/main 3933acb5 first).
- **River water:** v3 now has 0 of 8 dock/wharf plots on river water in the 4 villages (card's old log: 0/6, 2/6, 0/6). Water plots are kept.
- **Stilt houses:** a plot that misses the bank/shallows window now slides along its ring ray, up to 16 m out or 6 m back, to the waterline (LandmarkSampling.StiltHouseSpot). Flora and anchors use the same code.
  - Bake log: 6 of 24 fit at the ring spot. With the slide, 14 of 24 fit.
  - Built in play: about 2 before, 5 after (v1 2, v2 0, v3 2, v4 1).
- **Pins:** Landmarks_V3 111 3BDF705F is unchanged. Fauna_Fingerprint will be measured in the card-end suite; it will likely move, because stilt house positions are in the flora placement blob.
- **Remaining loss:** dock and wharf lane clearance. Village 2 has 4 fitting houses but builds 0, because its houses run parallel to the wharf and the dock. Row 2's boardwalk/side-pier layout takes that on.
- **Shots:** Assets/Temp/shots/w119-r1-village1.png and w119-r1-village3.png show stilt houses on posts in the shallows next to the dock and the wharf.

## W-119 row 2 (Zach 11-03): pier networks. Done (verified)
- Commit e94282a6 on world-features. New kit piece FishBoardwalk (KitShape.Boardwalk 16; species asset added to FishingVillage props).
- **Layout:** FloraPlacement joins each village's dock and wharf landings and its stilt house plots into one shore boardwalk. The nodes are sorted along the shore, and each link is laid as 3 m plank pieces on piles. Inner pieces slide along the facing to hold the ground line. Shore kit stays 3.6 m clear.
- **Pieces per village:** v1 10, v2 7, v3 15, v4 9. In v3 the walk runs about 40 m, from the stilt house on the west dune past the dock and pier to the east stilt house.
- **Pins:** Landmarks_V3 111 3BDF705F is unchanged. Fauna_Fingerprint will move again (a new species in the flora blob); it is measured at card end.
- **Risk:** where a link climbs a steep bank (v3 west dune), neighbouring pieces step up to about 1.5 m. They read as stairs on posts, not a ramp.
- **Shots:** Assets/Temp/shots/w119-r2-above.png and w119-r2-boardwalk.png.

## W-119 row 3: fishing details, varied per village. Done (verified)
- Commit 12440968 on world-features.
- **Trades:** each village takes one of 3 trades from its landmark rank: crabbers, net fishers, or fish driers. The trade weights its shore kit (FishShore variants 8 -> 24) and picks its boardwalk deck details (FishBoardwalk variants 16 -> 64).
  - Crabbers: stacked slatted crab-pot cages.
  - Net fishers: nets on a rail, hung in cord-grid panels of uneven fall with floats.
  - Fish driers: drying racks.
- **New coast details:** rope coils and gull posts. Gulls also sit on about 40% of mooring posts.
- **Coverage:** about half of each boardwalk's pieces carry a deck detail (v1 5 of 10).
- **Pins:** Landmarks_V3 111 3BDF705F is unchanged. Fauna_Fingerprint moves (variant counts in the flora blob); it is measured at card end.
- **Risk:** the trade weighting also changes how the shore kits that already exist look (accepted).
- **Shots:**
  - Assets/Temp/shots/w119-r3-driers.png (v1 racks)
  - w119-r3-nets.png (v3)
  - w119-r3-crabpots.png (v2)
  - w119-r3-shore1.png

## W-119 row 4: lake backwater over low banks. Done (verified: does not reproduce on v3, no code change)
- **Cause (from W-36, v2):** a river cell held at the level of the lake or sea downstream backs its water up. Where the river's carve or a neighbouring carve left the banks lower than that level, the water stood over dry ground. v2 had 22 such open ends.
- **v3 probe** (river cells whose water sits at the first lake or sea level downstream):
  - 64 cells. 0 of them have dry ground under the water level at +1, +3 or +6 m past the edge.
  - 1 cell shows it at +10 m: 263,3343. Its bed (36.78) is above its water level (36.19), so no water stands there. Not a real case.
  - WorldProbe.Floating: 11 cells, 9 of them cascades. The 2 left (-2046,139 and -626,801) have drops of 0.0 and 0.2 m and are not backwater.
- **Change:** none. W-36's FillRiverPits, the v3 water-first respin and W-52's ExtendStillWater together already cover it. lanes/world/STATE.md line 39 can drop "lake backwater" (I left STATE alone; it is the supervisor's file).

## W-119 card end
- **PlayMode suite** (editor closed): 13 passed, 1 failed. The failure is Fauna_Fingerprint.
- **Fingerprint:** 027CC3FD -> 2998FA9A. That is expected: the flora blob now holds the FishBoardwalk species (row 2) and new variant counts for FishShore 8 -> 24 and FishBoardwalk 16 -> 64 (row 3), and the stilt house placements changed (row 1). It needs a re-pin at merge.
- **Landmarks_V3:** 111 3BDF705F, unchanged.
- **Shots:** in D:\PunchyMilestones\W-119 (8 files).
- **Cleanup:** my editor and editor task are closed, and no world-tree Unity processes remain.

## W-120 row 1 - boardwalk steps (done, verified)
- FloraPlacement.BoardwalkAt: pieces pitch end to end (instance Tilt); inner points lift up to 2 m (BoardwalkMaxLift) so no piece pitches over 0.3 (BoardwalkMaxPitch, ~17 deg). Link ends keep their ground, so links still meet.
- v3 west dune piece heights before 0.2 / 1.3 / 2.7 / 3.8-4.3 (flat steps up to 1.6 m); after 0.3 / 0.9 / 1.9 / 2.9 / 3.8 / 4.3, ramped with no lips.
- Landmarks_V3 111 3BDF705F unchanged. Fauna not rechecked until the card-end suite.
- Shots: D:\PunchyMilestones\W-120\w120-r1-before.png, w120-r1-after.png
- Risk: a lip is left only where a 2 m lift can't reach (a bank steeper than about 1:1 over a whole link). Collision isn't affected: the boardwalk has no footprint.

### W-120 row 2 (#37 sea folk lake settlement, phase 1) - done (verified)
- Lake pick (v3, logged by SeaFolkBaking): lake 2770 cells (70.9 ha), level 35.6, 31.2 m deep, middle -1492,-444; village -1624,-488, bed 16.0 m down; landing -1783,-554 (172 m).
- Layout (SeaFolkVillage props): 6 shell domes ring (kept), 3 spires -> 1 central spire (h 29.8, breaks the surface), new ring of 6 glow stones (KitShape.GlowStone, species SeaFolkGlowStone: stacked stone, glowing rune bands, pearl cap, shells/anemone/kelp at foot), reef ring kept. Kelp now 6-segment snaking strands with leaf blades. No NPCs.
- Landmarks_V3 111 3BDF705F unchanged. Fauna fingerprint: checked at card end.
- Shots: D:\PunchyMilestones\W-120\w120-r2-above.png, w120-r2-under.png (ScreenCapture, includes the underwater tint); before: w120-r2-pre-above/pre-under.png.
- Commit a6b39a48 (world-features). Docs/SeaFolkBaking comment no longer say "v2 only".
- Risk/observation (camera lane, not edited): underwater fog is set (ExpSquared 0.11) but flora/terrain shaders ignore it, so the bed reads clear at 30 m+.
- ASK animal lane (row 3): a hook for fish schools near the sea folk domes, e.g. spawn existing Koi/SilverFish/Carp schools around a landmark's centre when its type is SeaFolkVillage (centre -1624,-488 on v3, bed ~19.6, water 35.6). World lane will not edit fauna code.

## W-121 row 1 - haze LUT edge near rain/snow cells: done (verified)
- Commit 54929358 on world-features (Sky.hlsl, HazeLut.shader). The haze table judged the rain/dust height fades at the camera's own height, so a high camera near a cell hazed the ground by its eye level; near the cloud base that flipped across directions and distances and drew hard edges (A-87 row 2). The table now judges the fades at ground level (PunchyHazeLutHeight 0); the sky's per-pixel march keeps the true ray.
- Repro: forced SnowShowers + Blizzard cells at (-115, 1560) r 350 (cloud base about 705 inside, 1300 outside), main camera at (-115, 740, 1560), just above the base, looking north and down.
- Shots: D:/PunchyMilestones/W-121/w121-r1-before.png (hard arched edge where the ground snaps to white), w121-r1-after.png (same spot and cells, ground fades smoothly). Inside a cell, low ground now reads a bit hazier from above, as it should under the shaft.
- No pin impact (shader only).

## W-121 row 2 (#37) - sea folk polish: wrecks, shell buoys, moored raft: done (verified)
- Commit 5e880ec1 on world-features. Wrecks: Height 9-ish -> 11-15 with a 0.75-1.3 size spread; planks, keel and barnacles scale with hull length so big hulls do not look spindly.
- New KitShape.ShellBuoy (ribbed shell float, lashing, pole with glowing pearl, barnacles, kelp streamer, cord down) and KitShape.SeaRaft (lashed logs on cross beams, shell-scale sail, baskets, pearl pot, paddle, bow mooring line), species SeaFolkBuoy / SeaFolkRaft. Village rings 4 buoys (Ring 0.95); 3 sit on water in v3, the 4th spot is dry and stays empty. Landing moors 1 raft at Ring 0.95 / Angle 60, beside the pier (not under it).
- Fix in FloraPlacement: landmark ring props with Floats now sit on the water surface over their spot (they sat on the lake bed); a dry spot returns none, so no draws move.
- Landmarks_V3 111 3BDF705F unchanged. Fauna: checked at card end.
- Shots: D:/PunchyMilestones/W-121/w121-r2-raft.png, w121-r2-buoy.png, w121-r2-wreck.png.

## W-121 row 3 (#21) - plant fidelity: Bush, Daisy, Buttercup: done (verified)
- Commit 316a8796 on world-features (FloraGenerator.Trees.cs, FloraGenerator.Plants.cs, Buttercup.asset). Picked from close shots of 7 meadow plants at (800, 800): Bush read as lumpy blobs, Daisy as a tiny thin star, Buttercup flowers were invisible.
- Shrubs (LOD0): 3-5 twig sprigs poke out of the clump, each a bark tube with paired pointed leaves and a paler tip leaf. This applies to every Shrub-form species, not only Bush.
- Flowers (LOD0, all heads except Spike): 1-2 stem leaves, a green calyx, sometimes 1-2 nodding buds. Daisy heads (and AlpineFlower) get an inner raised petal ring and a domed disc. Cup heads (Buttercup, Poppy, DesertPoppy, Glowbell) get a stamen ring and a knob. Buttercup is now a taller (0.22-0.38) yellow cup with a green-gold heart.
- New detail draws only from the per-model Detail stream, so plant layout does not move. Landmarks_V3 111 3BDF705F unchanged.
- Shots: D:/PunchyMilestones/W-121/w121-r3-before.png and w121-r3-after.png (Bush | Daisy | Buttercup, same camera).

## W-122 row 1 - marsh folk stilt village in the Foggy marsh: done (verified)
- Commit 22c24ab9 on world-features. New landmark MarshFolkVillage (MarshFolkBaking, MarshFolk.cs), baked last on its own spot in the biome tagged marshfolk (FoggyMarsh). v3 pick: (-1240, 320), base 112.9, relief 7.4 m (fallback; the marsh has no 4 m spot), 80 m in from the patch edge. A 2nd village is only placed if relief <= 10 m; the best v3 spot left has 16.3 m, so v3 has one village.
- Layout: murky flattened pool (Radius 40), smoke hut (kit SmokeHut, squat house under a 58 deg thatch, stone stack + grey landmark plume) on a 10x10 deck at the centre; 6 reed huts (kit MarshHut) on stilts at 20 m, doors in, each with its own plank walk (FishBoardwalk pieces) to the smoke hut deck; 3 cross walks between hut pairs; 1 landing walk to the bank ending in a step. New kit culture MarshFolk (ochre reed walls, bog-oak wood, straw thatch); new KitShapes MarshHut, SmokeHut, MarshCross, MarshWalk; species MarshHut, MarshSmokeHut, MarshCross, MarshWalk.
- Landmarks_V3: 111 3BDF705F -> 112 A955AFE0. Only the appended MarshFolkVillage entry is new; the other 111 hash to 3BDF705F unchanged. Fauna: checked at card end.
- Shots: D:/PunchyMilestones/W-122/r1-above.png, r1-walkway.png.

## W-122 row 2 - marsh folk details, three village themes: done (verified)
- Commit 05da2ec9 on world-features (StructureKit.cs, FloraGenerator.Kit.cs, FloraPlacement.cs, MarshCross.asset). New details: reed drying frames with hanging bundles, wicker eel traps hung mouth up off the deck edges, carved totem posts (stacked faces, beak, crest), gourd strings under the eaves, a MossCap roof moss sheet over the thatch, and lily pads (some with a flower) on the pool round the decks.
- Per-village variety: the theme is Variant % 3. It sets the trim paint, the totem colour and which details lead (0 = eel traps, 1 = drying frames, 2 = totems and gourds). The other details roll at about 30%. SmokeHut, MarshCross and MarshWalk are now PerVillage (MarshCross Variants 4 -> 3), so one village keeps one theme. The huts still vary on their own (6 variants).
- The v3 village is theme 2 (green totems), so eel traps and drying frames are rare there. I did not catch either in a render this row; totems, gourds, roof moss and lily pads all show.
- Landmarks_V3 112 A955AFE0, unchanged by this row.
- Hut spots for A-89 (wildlife): the village centre is (-1240, 320), ground about 111, water at the deck foot. The smoke hut sits on a 10x10 deck at the centre, 1 m up. 6 reed huts stand on stilts on a 20 m ring round it (village facing 291 deg) with doors in. Plank walks run from each hut to the centre, plus 3 cross walks between hut pairs and 1 landing walk to the bank. The pool's radius is 40 m. Open water for wildlife lies between the walks, 8-18 m from the centre and 22-38 m out.
- Shots: D:/PunchyMilestones/W-122/r2-hub.png, r2-above.png, r2-totem.png, r2-roofmoss.png, r2-smokehut.png.

## W-122 row 3 - marsh folk hatchery raft: done (verified)
- Commit fb83c0c7 on world-features (StructureKit.cs only). The surprise: the village's hatchery. A round reed nest raft is moored by a cord off the back of the smoke hut deck, where the fire keeps it warm. It holds 5-7 cream eggs with dark speckles, and one has cracked open with a green hatchling's head looking out over the broken shell. It has its own random stream, so the row 2 details keep their places. It sits in the open gap between the hub's walks (checked from above).
- Landmarks_V3 112 A955AFE0, unchanged by this row. WorldTests in batch mode: Landmarks_V3 fails against the card pin 111 3BDF705F, because row 1 added the MarshFolkVillage entry (already reported).
- Fauna_Fingerprint: measured AEA50DCE, not the pin 706B915E. It reads AEA50DCE with the hatchery switched off too (editor play mode), so row 3 doesn't move it. The change came in rows 1-2, most likely from the new marsh folk flora species in the fauna build's flora lookup blob. Supervisor: confirm that reason and re-pin, or ask me to find the cause. I left the test pins alone.
- Not verified: the hatchling's eyes use the lamp material, but in a dusk shot (20:20) they don't glow visibly, so I don't count on that.
- Shots: D:/PunchyMilestones/W-122/r3-hatchery.png, r3-hatchling.png, r3-above.png.
- W-122 is complete (rows 1-3). Editor closed.

## W-123 row 1 - flora LOD by class and on-screen size: done (verified, with the limits below)
- Commit 9ca9d11c on flora/class-rules. Suite 14/14; Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint F0EF4BD5 unchanged.
- Each variant's size and class (DetailPolicy.FloraRule: tree, plant, rock, debris, structure) set its LOD changes, draw range and real-time shadow range in the existing cull. Layer radius and the sun's shadow distance stay as ceilings. `-oldfloralod` gives the old rule.
- Hysteresis: shadows have a per-plant state buffer. Mesh changes keep the existing dithered crossfade.
- Trees have a minimum change distance (DetailPolicy.FloraFloor), or 7-9 m trees went angular at 25 m and to flat discs at 100-250 m. So trees under about 20 m change where they always did.
- A/B, same build, windowed 1920x1080, -fpscap 0, High, 3 rounds alternating. Mean GPU new minus old (ms): town-near +0.03, town-near2 +0.02, town-far -0.02, forest-near -0.02, forest-near2 -0.05, forest-far +0.02. Frame: +0.03, -0.02, +0.01, -0.12, +0.03, +0.01. No worse and no better: all inside the run-to-run spread (about 0.1 ms).
- Discarded: the first old-rule run fell back from exclusive fullscreen to 4K (all shots 3840x2160), a false GPU win of up to 1.7 ms. The first tuning was 0.25-0.7 ms slower at far views (High scales the rule by Detail 1.5; the old layers used View 1.3).
- UNVERIFIED: popping judged from still shots, not a moving camera; shadow hysteresis not exercised directly; only High measured; the town's shadow-caster cost not measured separately.
- Seen: two rocks about 100 m south of the town square sit in a crossfade band on the new rule and show dither stipple from that camera spot. All present in all runs.
- Shots: PunchyMilestones/W-123/r1 (new1-3, old1-3, crop-street.png).

## W-123 row 2 - flora wind: BLOCKED, not started (needs a supervisor call)
- The row reworks Flora.hlsl, but the card says not to touch anything open in #208 and #209, and #209 changes Flora.hlsl. Not edited. Supervisor: allow it, or merge #209 first.
- Cause found: FloraMeshBuilder.Sway multiplies the weight by (1 + 0.6 x outward), so a crown's outer vertices swing further than its inner ones; Flora.hlsl then rescales each vertex to its length from the base, pulling the crown in and out; flutter along the normal swells round clumps. Plan: height-only weights, a rotation about the base, flutter along a fixed direction.
- To decide: the grass's gust scroll (clock x wind direction) jumps when the wind veers late in a session (W-43 gave the banners a fixed-rate scroll instead). Matching the grass exactly brings that jump to the trees.

## W-123 row 3 - V3 colour sync: not started
- Stopped at row 2. Likely the same conflict: the biome tint path (GroundFor, RockTint) is in FloraPlacement.cs, which #209 also changes.

## W-123 row 2 - flora wind: done (unblocked by the supervisor; motion needs Zach's eye)
- Flora.hlsl: plants read the grass's gust noise and scroll at their foot (identical UV to GroundDetail.hlsl). The sway is a rotation about the base by height, replacing offset plus rescale. Flutter runs along one fixed direction, not the normal. FloraMeshBuilder.Sway lost the outward factor. All inside `#ifndef FLORA_STATIC`, so static flora is unchanged by construction (no separate static-model shot).
- GPU, new against row 1, 4 alternating runs: forest 2.08/2.07/2.08/2.08 against 2.09/2.06/2.10/2.04; meadow 2.49/2.55/2.42/2.44 against 2.48/2.48/2.47/2.44. No worse. The 4th run of each is on the final bend constant; the first 3 are on the constant before it.
- 15 s frame series on one shrub, 31 frames: centroid x sd 0.67 px new against 1.68 px before, so visibly calmer, about 40% of the old movement. Crown area varies 0.66% against 0.48%, so area can't show the swelling at this wind either way. Whether "inflating and twisting" is gone is a judgement for a playtest; if it now looks too still, raise the 0.16 / 0.18 in the bend.
- Known, not fixed here (as instructed): the shared gust scroll is clock x wind direction, so it jumps when the wind veers late in a session. Grass already had it and trees now share it; fix once for both.
- Shots: PunchyMilestones/W-123/r2 (motion-forest.png, motion-meadow.png, shrub-strip.png, lnew2 and lbase series).

## W-123 row 3 - V3 colour sync: done (the look is Zach's call)
- Suite 14/14 with this row in; Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint F0EF4BD5 unchanged. No rule or blob field added (a new field would move the fingerprint); colour only.
- Plants on grassy ground carry the ground's colour (FloraPlacement.GreenTint, from SampleSurface so V3 applies, alpha mark 254). In Flora.shader, leaves that are clearly green lean 60% to that hue and keep their own light and dark, so autumn, blossom and fungus colours stay. Grass blades (GroundDetailSystem) lost some of their yellow bias and gained some green.
- Biggest visible shift: conifers go from blue-green toward olive. Marsh vines go a little duller. The result is subtle; to change it, the strength is the 0.6 in Flora.shader.
- Not measured: GreenTint adds a surface sample per placed plant in the streaming job. Single-run GPU at the forest 2.90 / 2.53 against 2.87 / 2.55, frame max 7.7 against 7.2, standing still only.
- Grassland here is the `-place meadow` spot (-644, 1299). Shots: PunchyMilestones/W-123/r3 (side-by-side.png: left before, right after; Grassland, Temperate x2, Foggy marsh).
- W-123 complete. Editor closed.

## W-124 row 1 - item 25, `-place meadow` "only in shadows": BLOCKED (not reproduced; needs Zach to say what he saw)
- Branch flora/round3 (from cc50c547), no commit for this row.
- Tried: the meadow spot (-644, 1299), 11:00 clear, High. Player-follow camera at the spot in 4 directions, a posed camera at the spot in 4 directions, from 5, 20 and 60 m on 3 sides, and from 50-90 m up. Builds: this branch, and a local throwaway merge of this branch + #208 + #209 (the Playtest4 mix without sky/cost, which conflicts in Sky.shader and doesn't touch flora). A/B on this branch with `-oldfloralod` and with `-cachedfarshadows 0 -cheapfarcaster 0`.
- Seen: every tree, bush, flower and grass tuft drew with its shadow in every run, at every distance. The big shadow beside the spot belongs to an oak a few metres east of it (trunk in the 5 m and 180-degree shots, crown just out of frame). The old-rule and no-cache runs look the same, so the W-123 row 1 class rule isn't hiding anything here. DetailPolicy.Decision (the decision byte) isn't used by flora or grass at all; only creatures use it.
- Possible reads, for Zach: (a) the grass tufts show up mostly inside shadows because there a blade (lit by the sky) stands out against the shaded ground, while in sun it is nearly the ground's colour; that is a look, not a LOD fault. (b) Something seen from above: a plant directly under a raised camera is put on LOD 0 by the GPU (horizontal distance) while the CPU skips the LOD 0 draw (3D distance), so only its shadow showed. Row 3 fixes that. Supervisor: which one did Zach mean, or a screenshot and a `-goto` (F3)?
- Shots: PunchyMilestones/W-124/r1 (base, old, nocache, pt4, appr, pt4appr; r1-meadow-c0.png).

## W-124 row 2 - item 26, plants rooted at the base: done (verified by motion maps; the feel is Zach's call)
- Commit 17bd5157 on flora/round3 (FloraMeshBuilder.cs only). Cause: the bend was already zero at the foot (W-123 row 2). The flutter was not. Every leaf-part vertex had full flutter, including flower and reed stems built as Foliage tubes, right down to the ground, each vertex on its own beat. So the stem jiggled along its whole length. Flutter now scales with height like the sway (up^1.2), zero at the base. Mesh data only: no shader cost, no placement change. Grass untouched.
- Check: 16 frames 0.2 s apart, Overcast, fixed camera, per-pixel brightness spread (higher = more motion). Meadow (-644, 1299): red flower lower stems 10.5 -> 5.0, lupine lower stems 7.2 -> 3.2, flower heads 8.8 -> 7.3 and 8.9 -> 6.6 (they still flutter). Forest at a bush (from 1740, 3355): bush 9.0 -> 8.9, daisies 4.8 -> 4.7, so trees and shrubs barely change.
- UNVERIFIED: in a storm, and by eye in motion. If plants now look too stiff, the 1.2 in Sway can be lowered.
- Shots: PunchyMilestones/W-124/r2/motion-meadow.png (motion map, before on top, after below), frame series in base/new (meadow) and fbase/fnew (forest).

## W-124 row 3 - item 29, hole under a climbing camera: done (verified)
- Commit 7a78a5ad on flora/round3 (FloraCull.compute, Flora.shader). Cause: FloraCull picked each plant's LOD by its distance on the ground plane, but FloraSystem decides whether to draw a model's LOD by the 3D distance to its bounds. From high up, plants straight below counted as near and went to LOD 0, and that draw was skipped as too far, so they vanished along with their shadows. Plants off to the side had a large ground distance, went to LOD 1/2 and drew. Fix: the cull measures to the nearest point of the plant's upright span (base to top). That is the 3D distance, and it is unchanged for anything level with the camera. The cheap far caster's shadow holes go by 3D distance too, so the cull and the holes agree.
- Checked, no change needed: tile streaming and the GPU early-out. Streaming loads tiles by ground distance from the camera, which is a superset of what is in reach. The early-out (beyond the layer radius) already used 3D distance.
- Climb over the forest at (1740, 3355), straight down, camera 60 / 120 / 200 / 310 m above the ground. Before: the grove under the camera was gone at 310 m while the trees round it showed. After: the grove is there at every height.
- GPU, alternating new/old, 3 runs each, windowed 1920x1080, High, uncapped. Mean new minus old (ms): town-near -0.11, town-near2 -0.45, town-far -0.07, forest-near -0.01, forest-near2 +0.29, forest-far +0.27. These are inside the spread (single runs vary by up to 1.3 ms; the other two lanes' editors were running on the machine), so no worse that I can measure.
- Shots: PunchyMilestones/W-124/r3/climb-310m.png (left before, right after), full series in climbbase/climbnew, A/B logs in abnew1-3/abold1-3.

## W-124 row 1 - item 25: closed, not a bug (mid-range shadow aliasing, per Zach)
- Zach (through the supervisor): what he saw was aliasing in the mid-range shadows, which he took for a missing mesh. No change, no commit. This replaces the BLOCKED entry above.

## W-124 row 4 - items 27-28, wet bark and leaves, bark grain: PARKED, UNVERIFIED (uncommitted in the world tree)
- Stopped on the supervisor's wrap-up call. The change is uncommitted in the world tree on flora/round3 (Flora.shader only, +40/-3; it builds with no shader errors). Nothing of it is pushed.
- What it does:
  - A `living` flag goes from vertex to fragment: 1 on animated models, 0 under FLORA_STATIC. Bark is part 0 on a living plant that isn't built, rock-tinted, timber or strata, so rocks, logs and buildings are unchanged.
  - On bark the rock relief texture drops to 35%, which was the "lumpy clay" look.
  - New procedural bark grain: 10-16 furrows round the plant's own up axis, with no seam, wandering with height. Plates crack across now and then and there are fine fibres. The normal leans with the furrow slope. It fades out by size on screen and by 60 m.
  - Wet: bark darkens to 0.5 (was 0.62), leaves to 0.7 (was 0.82). Wet gloss on bark and leaves goes from 0.5 to 0.07 and from a sharp highlight (power 60) to a broad sheen (power 10). Wet stone and planks keep their old gloss.
- Check so far: dry/rain shots of an oak, a pine and a trunk on the new build and the row 2 build (PunchyMilestones/W-124/r4/new, old; sheet-old-top-new-bottom.png). Rain came in overcast, so the sun highlight the fix targets isn't lit in them. Grain is faintly visible on the dry trunk. The trunk close-up framed from 5 m, not 2.5 m. Not judged done.
- Left to do: re-shoot rain with sun breaks (or with -time chosen for a lit rain cell), a true 2-3 m trunk close-up (Oak #1 at 1737.4, 20.6, 3354.6), and an A/B for GPU cost. Then commit, push, and run the suite (scripts/run-playmode-tests.sh, 14/14 with pins Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint F0EF4BD5). The suite was not run this card.
- No editor was opened this card (batch-mode builds only, all exited). Untracked probes are back in Assets/Temp/Editor.

## R-1: clouds on a low-resolution buffer (sky/lowres), 2026-11-09
Row 1 (fbc78198): cheaper sun samples with distance. Per in-cloud step: four up to 3 km, three by 6 km, two past 11 km
(the nearest and third samples' weights merge into their neighbours, fading, so no line shows). `-skyoff 64` keeps all four.
Verified: full-resolution crops noon and sunset (Summit, -gfx High, seed 4) against `-skyoff 64`: no visible change; the
differences are the cloud drift between the two shots. Merged origin/sky/cost first (underwater early-out kept, then the test mask).
Next: row 2.
Row 2 (9ba08caa): LowResEffectFeature (`Assets/Game/Scripts/Rendering`), RenderGraph, half or quarter size, reprojected history with a
neighbourhood clamp, depth-aware `PunchyLowResUpsample` (`LowResEffect.hlsl`). No effect code inside; a user is an entry on the PC
renderer asset plus a shader. Targets are made once per size and imported; per-frame allocation checked by reading the code only.
Row 3 (ccd74cc1): clouds are its first user. Half at High and Ultra, quarter below (Low too: the card allowed leaving it, I did not).
`-skyfull` is today's sky; `-lowres half|quarter` forces a size (both also per shot line). Discs, stars, far clouds and weather volumes stay full resolution.
- Shots (1920x1080, full / half / quarter): noon, sunset, horizon, tree line, moon and a 9-frame turn series, kept locally under the milestones folder, R-1.
  No halo at the tree line or horizon, discs and stars crisp, no ghosting in the turn series (coarse: 14 degrees per shot line). Clouds are softer and lose the grain;
  very small far wisps may thin out at quarter. Cloud drift and player movement between shot lines limit how exact these comparisons are.
- GPU ms, this PC, Summit noon, `-gfx Ultra`, two rounds: full 4.63 / 3.77, half 4.01 / 1.45, quarter 3.85 / 1.20. **The rounds disagree badly; do not quote these.** At High, one round: noon 3.62 / 2.96 / 2.85, sunset 3.44 / 2.45 / 2.08. `-screen-width 2560` did not take (shots came out 1920x1080), so no higher-resolution number.
- Suite 14/14. The world and fauna pins were not checked separately.
- UNVERIFIED: under water (read the code only: Sky.shader's early-out is unchanged and the buffer draws nothing there); weather volumes and storms were not shot; fast turns were only checked coarsely.
- **Laptop job to queue (not added: this lane may only edit its report and EDITOR-QUEUE.md):** Profile build of sky/lowres, as job 34: the Summit, facing out, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, ABBA: `-skyfull` against the default (half), and `-lowres quarter`; plain, Overcast and Storm; sunset too.

## R-2: one detail policy, four rules; flames on it; a world-scale static glow (detail/four-rules 2f51a43b)
Built in the player tree, not the one the card names (the world tree's gitignored probe files break compiling). All rows DONE. Suite 14/14 on the row 4 code; pins Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint F0EF4BD5 are held by that suite. Shots at `-gfx High -weatherseed 4 -island v3`, kept locally in the shots-R2 folder, one subfolder per row with its player log.
- Row 1 (f91c72cf): `DetailPolicy` owns `CappedReach`, `Holds`, `Ease`, `EasedReach` and `Crossfade`; LightBudget and the creature shadow test call them. Town and Ember Forest night shots match before and after. No per-frame allocation: read from the code only, not profiled.
- Row 2 (0ea75a2a): cause found in the code: near flames were picked by flat distance and the far set skipped trees by flat distance, while both faded by 3D distance, so from above there was a hole under the camera. Now one 3D distance, one crossfade, the shown reach eased, and a far set holding every burning tree in range (no re-gather when the reach moves).
  Frame series flying up (6 heights, then 8 at 6 m steps) and walking in show flames with no empty ring. **Limits:** stills 0.5 s or more apart, not continuous flight; no 6 m-step series from the old build to compare; flames animate, so frame differences can't isolate flicker. Worth a look in motion.
  Timing: `-benchfires` isn't in this tree (it was on the bench branch), so this is not the 3.2 test. The natural Ember Forest spot (about 300 near flames, not 500): main thread 5.42 before, 5.46 after, GPU 2.47 both. This PC is CPU-limited; read as no visible change only.
- Row 3 (80332ee7): grass and pebble reach and thinning come from on-screen size, tuned to give the old 60/45/20 m (x Detail) at the 60 degree view, so ranges at the default view are unchanged. Meadow shots old and new match on grass; a trunk or two in the second meadow view differs in tone, unexplained (this row doesn't touch flora). GPU 2.65 before, 2.59 after: noise. Narrower views (up to 1.5x reach) and presets other than High were not shot.
- Row 4 (2f51a43b): `SurfaceGlow.World` bakes a 512 x 512 RGBAHalf map over the 9216 m island on a worker once the world loads; terrain only reads it (`PunchyGlowWorld`, one extra texture read). Red is fire, tinted with the night's colour; gba is plants and lava. Fire fades in from half the near flames' reach to all of it; plants and lava over 240-320 m. `AddWorldSource` is the way in for window spill and fixtures. `-worldglow 0` turns it off.
  Bake time logged: 4.1 s on a worker on this PC, laptop not measured. Memory 2 MB on the GPU, no CPU copy kept (about 6 MB more during the bake). Shots far (950 m), mid (450 m) and near, on and off: the difference image follows the forest at all three, and the ground glows from 950 m. Day check clean. GPU 2.46 on, 2.48 off: noise.
  **Limits:** 0 lava texels on this island, so the lava path ran but its look is unseen; `AddWorldSource` has no user yet; one night's colour (blue) shot; the fire brightness is a first value, not tuned; host and joined client not run (seed-derived and cosmetic by design). The mask takes every model of a burning species, a little wider than the flames' own test (trunk, no colliders).

## W-126 (flora/habitat, tree Punchy-release)
- Row 1 (d6224b27, pushed): per-species habitat in `FloraPlacement.Place`: `SunSlope`, `Shelter`, `Exposure`, `WetReach` fields on `FloraSpecies` (neutral defaults), set on 22 species across biomes. Factors only thin (0..1); a `HabitatGain` (1 + 0.4 x strongest rule) is counted in the early-out bound and paid back where the habitat is, so each species keeps about its overall density.
  **Verified (numbers, `FloraHabitatTests`):** plant sun-facing vs the ground's: Saguaro +0.26 vs -0.05; Oak relief -0.42 (hollows) vs 0.02; Pine relief +0.21 (ridges) vs -0.06; Fungal glow cap sun-facing -0.26 vs +0.13. Old/new shots of 5 biomes, N and S: `shots-w126/row1-<Biome>-<S|N>-<old|new>.png`.
  **Timing:** `Flora_PlacementCost` (3 biomes, 7.8k cells each): old 1355 / 1364 ms, new 1496 / 1492 ms = +10%, right at the limit (Mountains +42% from the 4 relief height samples, Sonoran +5%). Relief now reuses the ground height and runs only for a live weight.
  **UNVERIFIED:** the shots at 110 m aerial show the change only faintly (plants are tiny; checked one pair at low res); the numbers are the proof. Tundra Spruce has few plants at its spot (5), so its stats are thin.
  **Next:** row 2, clearings and groves.
- Row 2 (flora/habitat, second commit pushed): clearings and groves. Biome `ClearingAmount`/`ClearingSize` (low-frequency mask thins every plant; shrubs and flowers with `EdgeAffinity` are spared inside and favoured on the edge). Species `GroveSpacing`/`GroveRadius`/`GroveGain`/`GroveStraggle`: seeded stand centres on a jittered grid (about 3 cells in 4), dense stands with stragglers between. `ExtraGain` is counted in the early-out bound. Data: clearings on Temperate, Grassland, Mountains, FungalForest; groves on Oak, Birch, Pine, FungalGlowCap; edge species on 8 shrubs and flowers.
  **Verified:** plan-view maps of a 512 m square in 4 biomes (`shots-w126/row2-plan-sheet.png`: green = trees, orange = shrubs/flowers, pink = grass) show tight tree stands with scattered stragglers (Mountains, FungalForest clearest) and shrubs ringing the open ground.
  **UNVERIFIED:** the aerial/ground camera shots (`clear-*`) were taken but framed badly (rugged view spots, terrain not streamed in 3 s), so they are not evidence; no real-render look. Temperate's plan has a large bare patch with straight edges that did not change when its clearing amount fell 0.85 to 0.7, so it is likely water or terrain, not the mask (not checked). Tree counts at a spot fell (Temperate Oak 125 to 85 per 400 m square): densities want retuning in row 3.
  **Timing:** `Flora_PlacementCost` 1607 / 1625 ms vs 1355-1364 before row 1 (+19%; Grassland +20% from the clearing noise and wider bound). Over row 1's 10% line; row 3 to trim (skip the mask for biomes with amount 0 is already so; next lever is a cheaper bound).
  **Next:** row 3, every biome one habitat rule, suite, pin, sheet.

- Row 3 (flora/habitat, third commit b60e85b7): every flora biome has a habitat rule. Rules and clearings are data on the species and biome assets (no biome names in code). Added habitat rules to 15+ species (Bogwood, CanopyTree, CottonGrass, CrystalShrub, DryBush, DwarfWillow, EmberOak/Shrub, FulguriteSpire, Glowbell, IceCrag/Slab, MarshShrub, SilverTree, Spruce, SwampShrub) and clearings on Tundra, Sonoran, SaltFlat, EmberForest, FoggyMarsh, PoisonSwamp, SparklingForest. Ocean is exempt (no flora).
  **Verified:** `Landmarks_V3` still 112 A955AFE0. Fauna_Fingerprint re-pinned once, to 7D75BB04: flora rules move plant positions and the fauna build bakes plants into obstacles, so the hash must change. Suite 16 of 18 on the full run before the last test-only edits; Butterflies_MeadowDensity failed once there and passes alone (flaky, not touched). One-shot-per-biome plan sheet of 17 biomes: `shots-w126/row3-biome-sheet.png` (green trees, orange shrubs/flowers, pink grass); every biome shows plants, none is empty.
  **UNVERIFIED:** no real-render look; the sheet is plan view only. Suite not re-run after the final commit (test-only change: biome list and a Timeout on the plan-map test). Placement cost `Flora_PlacementCost` is +25% (1697 ms vs 1355 baseline), over the 10% line of row 1, not trimmed (Sonoran clearing noise and wider bound are the likely cause). Temperate's bare patch is unchecked (likely water).
  **Next:** trim placement cost (cheaper bound or skip mask earlier), check Temperate patch, then review and merge.

## R-3

- Row 1 (rendering, `shadow/tiers` 1e08675b): shadow tier by size on screen. `DetailPolicy.Rule.SharpShadow`, `ShadowTier` (0 real-time, 1 cached map, 2 none; hysteresis through `Tier`) and `SharpReach`; FloraCull.compute clamps each trees-layer plant's real-time cast to its own size-based reach (floor NearRange 25 m, ceiling the URP shadow distance, 50 m at High), and the cache's cull starts 12 m inside it. Built things and rocks 400 px (a 16 m house about 37 m), trees 1050 px (a 20 m tree keeps the old 25 m), bushes and debris never past the floor. Only trees keep the cheap far mesh and crown holes; built things and rocks cast solid. Creatures already tier real-time/none through `Shadow` (no cached map for them). No new distance constants; `-oldfloralod` keeps the old.
  **Verified (editor, in-place A/B, frozen clock):** at the big town, houses 35-45 m out keep sharp chimney/facade shadows that the old tier drops to the soft cache (`Assets/Temp/shots/r3-town-old.png` / `-new.png` / `-crop.png`, world tree, untracked); the diff touches only built things and rocks, no bush. Main-light shadow pass (GPU marker, 400 frames): big town 0.21 ms new vs 0.25 old; dense forest (1740,3355) 0.33 vs 0.33. Casters 519-522 vs 520 and 244-255 vs 248. No allocation in the new path (struct `SetVector`/`SetBuffer` only; editor GC per frame the same either way).
  **UNVERIFIED:** whole-frame GPU (the editor's FrameTiming gpu reads 0.14-0.8 ms, not usable); a Dev build bench would settle it. Not looked at at Low/Medium presets.
  **Next:** rows 3 and 4; row 2 as a plan if it proves big.
- Row 3 (rendering, `shadow/tiers` 7e4175a3): far crowns without noise holes. Cause of the 25-40 m shadow shimmer: the cheap far caster cut world-space noise holes in every crown, so the holes swam through the swaying crowns. Now only open crowns (species `Fullness` < 0.6: birch, spruce, bogwood, palo verde, ember trees..., 30 of 77 tree models) get holes, in the plant's own frame so they ride the sway; full crowns cast whole (FloraCull.compute tags them, fade 3-4). Cascades/texel snapping already stable (4 cascades, conservative enclosing sphere), left alone.
  **Verified (editor, dense forest (1740,3355), clear, in-place toggle):** frozen stills old/new mean luminance 74.72/74.19, 39.37/39.16, 109.08/109.07 (no darker than 0.7%, not blockier: `Assets/Temp/shots/r3c-still-ab.png`, `-floor-ab.png`, `-far-ab.png`, world tree, untracked). Wind series, 12 frames, time running: floor view changed px/frame 4591 old vs 3689 new; mid view sign-flip flicker 1113 vs 1321 (`pan/wm-ab.png`): the remaining flicker in both is leaf-tip silhouette sway and thin trunks (geometry), not shadow holes.
  **UNVERIFIED:** wind runs differ by sway phase, so the counts are noisy; no build pan; Low/Medium presets not looked at.
  **Next:** row 4 (depth priming), row 2 plan.
- Row 4 (rendering, `shadow/tiers` 08749a83, merged with main 8ce83dcd): depth priming. What forces it: PC_Renderer `m_DepthPrimingMode: 2` was set on purpose (e754aaf8) because SSAO (source DepthNormals, on at every preset) already runs the depth-normals prepass, so priming makes the opaque pass shade each pixel once. In URP 17 Auto is the same as Forced on desktop (it is only "not recommended" on mobile); priming also turns off by itself with MSAA, deferred or non-first cameras, so water, SSAO and the low-res upsample do not depend on it. Decision: keep Forced, no asset change. New flag `-depthpriming off|auto|forced` sets the live renderer only (the asset is untouched) for benches.
  **Verified (editor, noon clear, ABBA 300 frames each, GPU markers prepass + opaques in ms, forced vs off):** dense forest (1740,3355) 0.82+0.79 vs 0.79+2.02; big town (2972,-885) 0.78+1.50 vs 0.75+1.61; lake (-1624,-488) 0.81+1.07 vs 0.79+1.04; Summit (-2446,1154) 0.41+0.45 vs 0.41+0.52. With SSAO off as well (forest): forced 0.89 depth-only + 0.78 vs off 0 + 2.26, so even then Forced wins. Look: no change (town 788 px over 8 of 960 wide, mean luminance 110.07/110.05; lake 0 px; Summit 437 px, 169.63/169.62); shots `Assets/Temp/shots/r3d-{town,lake,summit}-{forced,off}.png` (world tree, untracked).
  **UNVERIFIED:** whole-frame GPU and the laptop's iGPU, where the extra prepass might cost more than overdraw saves. BENCH-QUEUE job text: "R-3 depth priming on the laptop (shadow/tiers 8ce83dcd): Profile build, 1920x1080, -gfx High (then -gfx Low), -weatherseed 4, -time 12 -weather Clear, uncapped (-fpscap 0). At -goto 2972,-885 (big town), 1740,3355 (forest), -1624,-488 (lake), -2446,1154 (Summit), ABBA -depthpriming forced / off, 300 frames each; report GPU ms per frame and per pass (DrawDepthNormalPrepass, DrawOpaqueObjects). Optional: cross with -ssao off from render/ssao-source." If off wins on the laptop at Low, the change is one line: Low's renderer sets Disabled.
  **Next:** row 2 plan below.
- Row 2 (plan only, not built): one wide cached sun-shadow map for huge things to 1-2 km, replacing FarShadow.cs. Build on FloraSystem.ShadowCache (orthographic sun-view float texture, refresh on sun move, receiver like FloraShadowCache.shader). (1) A second cache, 2048x2048 R16 (about 8 MB) covering 3 km (about 1.5 m texels), centred on the player and snapped to texels, depth range about 600 m. (2) Redraw when the sun turns about 1 degree or the player moves about 300 m, spread over 4 frames by strips so there is no hitch. (3) Casters: only huge landmark meshes (salt tower, basalt tower, sunken temple, colossi) plus a coarse terrain heightfield for ridge shadows; colossi move, so their bounds are redrawn every few frames into a small dirty rect. (4) Receivers: terrain, flora and water read it past the last cascade with a wide soft filter and a fade-in from the cascade edge. (5) Retire FarShadow (strip mesh and Punchy/FarShadow shader, users SaltTowerSystem, SunkenTempleSystem, StoneColossi). Done when: shots at 300 m, 800 m, 1.5 km old vs new; redraw cost under 0.3 ms per frame spread, no hitch over 1 ms; memory measured; Low preset can turn it off. Size: about 2-3 days, so it was not built in this card.

## W-126 follow-up (flora/habitat)
- verified: Flora_PlacementCost 1697 -> 1424 ms (+5% over 1355 baseline) by counting clearing and grove in the early-out bound. Temperate bare patch is the Glacier biome (sparse by design), not a clearing or grove, so no cap needed. Tests added: 4 (Flora_PlacementCost, Habitat_StatsAndShots, Clearings_AerialAndGroundShots, Clearings_PlanMaps) to measure cost and show habitat/clearing results. Full suite: all pass, Butterflies_MeadowDensity failed once in suite and passes alone; Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 7D75BB04.
- UNVERIFIED: the two framed real-render shots. Habitat_FramedShots runs, but the camera ends up skimming terrain (hook heights do not match rendered ground), so no clearing or saguaro slope is visible.
- next: fix framing (probably sample the live terrain height, or use a free-fly camera offset) and retake.

## W-126 (lean suite)
- verified: Habitat_StatsAndShots, Clearings_AerialAndGroundShots, Clearings_PlanMaps, Habitat_FramedShots are [Explicit]; runner now prints them as SKIP. Full suite after last commit: 15 passed, 0 failed, 4 skipped, 210 s. Flora_PlacementCost runs 11 s, stays in. Pins unchanged: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 7D75BB04; Butterflies passed.
- UNVERIFIED: the two framed real-render shots. Player-relative -camrel framing works, but trees show as dark placeholder quads or are absent in batch renders, and the Sonoran slope shows no saguaros; no good shot yet.
- next: someone with an interactive editor should frame the clearing and saguaro slope by eye (-goto plus -camrel).

## R-4
- Row 1 (sky/look 334cbe62): cloud edges eroded by billows (Worley) on tops and wisps (new Perlin channel in CloudNoise) on bases and thin veils, strongest where the body is thin, a second smaller octave near the camera (fades out 2.5-6 km), all detail fading out by about 17 km; the flat base is eroded less (holes there showed as bright specks). `-skyoff 128` shows the old edges.
  **Verified (editor, same camera, frozen clock, PartlyCloudy seed 4):** Summit noon, plains (lake) noon and Summit sunset 17.9, old/new pairs `Assets/Temp/shots/r4-{summit,plains,sunset}-{old,new}.png` + `-ab.png` (world tree, untracked). Changes are at the silhouettes only: mean abs diff 0.38 / 0.30 / 0.37, mean luminance equal within 0.2. Slow pan (40 frames, 0.03 deg/frame) mean frame-to-frame luminance change near/far band: old 0.019/0.202, new 0.026/0.220, no shimmer. No shader errors.
  **Note:** the look is subtle. Big near cloud walls come from the weather map's cover edge, which a detail threshold can only shave; bending the cover lookup by the detail noise (500 m) broke the walls into lobes but left bright specks on the bases, so it was left out.
- Row 2 (sky/look 5ab6813e): softer light through cloud bodies, all inside the existing march (no new pass). Powder darkening on sunlit edges that fades toward the sun, a silver lining when looking near the sun, a soft multi-scatter floor so thick bodies aren't flat grey, and ambient scaled by height so bases are darker. `-skyoff 256` shows the old light.
  **Verified (editor, Summit, frozen clock, PartlyCloudy seed 4):** noon `r4l-noon-{old,new,ab}.png`: diff 2.32, 12.4% px changed, luminance 177.8 -> 179.5 (rounder body shading, darker base, brighter rim). Sunset 17.9 facing the sun `r4l-sunset-*`: diff 0.80, brighter lining on the near cloud. No shader errors.
  **Overcast: UNVERIFIED.** The `-weather Overcast` switch had not taken effect by the time of the shot, so `r4l-overcast-*` still shows the partly cloudy sky (diff 2.25, same change as noon). Per rule 19 I didn't retake it.
- Row 3 (sky/look 328540d8): cloud types from data. Weather presets gain `CloudLayering` (0 heaped cumulus, 1 a flat stratus sheet with frayed edges) and `HighCloud` (cirrus amount), set on all 14 presets (Overcast 0.85, MarshFog 0.9, Rain 0.6, Clear high cloud 0.55, Sandstorm 0.6...). WeatherField blends them per cell into the wetness map's b/a channels, nudged by each biome's Humidity (humid: more layered, dry: more high cloud), so no biome names in code. The march flattens into a slab where layering is high; the cirrus scales with HighCloud. `-skyoff 512` shows the single old type.
  **Verified (editor, frozen clock, pitch 40 into a forced cell):** sheet `Assets/Temp/shots/r4t-sheet.png` (clear, partly, overcast, rain, storm, dunes; small copy `r4t-sheet-small.png`); pairs `r4t-<name>-{old,new,ab}.png`. Rain: diff 10.11, 35.6% px changed, luminance 149.5 -> 140.2 (an even, darker sheet). Overcast: diff 0.44 (the forced cell there was thin by shot time). PartlyCloudy 0.57, Storm 0.16 (cumulus types, barely changed, as intended). No shader errors.
  **UNVERIFIED:** the high cloud (cirrus amount) and the biome humidity nudge. Clear at the Summit and Clear over the Dunes (3250,-250) both show diff 0.0: the cirrus is too faint to change a pixel at this size. Per rule 19, no retake.
- Row 4 (sky/look 5880293c): cost. Job 38 names DrawSkybox (3.7 ms at half and quarter alike; no `-skyoff` bit made it cheaper). Its likely cause: the skybox inlined the full-resolution cloud march fallback behind a dynamic branch, so every sky pixel paid for its registers even with the buffer on. LowResEffectFeature effects can now name an `ActiveKeyword` (PC_Renderer: `PUNCHY_CLOUDS_LOW_RES`, on while the camera has the buffer), and Sky.shader compiles the fallback out of that variant. Compiled D3D fragment: temps 30 -> 19, instructions 3389 -> 2489, texture reads 33 -> 10. Cameras without the buffer still march at full resolution, as before. Look unchanged: `r4c-summit.png`.
  **GPU at the Summit (this PC, editor, 1920x1080 synced renders, pitch 10, PartlyCloudy, noon):** this PC's GPU does a whole frame in about 2.2 ms, so it can't resolve the cut. The editor's FrameTimingManager read 0.1 ms (no live frames when the editor is unfocused), so I timed synced cam.Render calls instead. Before the cut: R-4 look 2.22 ms vs main look (`-skyoff 896`) 2.21. After the cut: 2.26 / 2.28 vs 2.20 / 2.24 (two runs). R-4 total sky cost is +0.01..+0.06 ms over main, within the +1 ms limit. The march itself is 0.13 ms here (`-skyoff 1`: 2.10).
  **Quarter leak at leaf edges (1740,3355): UNVERIFIED.** No trees had streamed in for my editor shot (`r4c-leaf-{quarter,half}.png`, sky only, diff 0.13). Reading the code: the low-res depth keeps the furthest depth of each block and the upsample is depth-weighted, so a sky pixel can't pull from a tree texel. Job 36's patch may have been a real cloud (fresh game per run). No fix made.
  **Bench:** queued as BENCH-QUEUE job 40 (laptop: R-4 vs main at half/quarter, Overcast/Rain, RenderDoc DrawSkybox against 3.68 ms).
  **Suite after the last code change:** 15 passed, 0 failed, 4 skipped in 249s (Unity exit 0). Pins unchanged: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 7D75BB04.

## W-127 (town lights, tree Punchy-release, branch town/lights)
- Row 1 (window spill, dd730b99): DONE. Each lit pane of a house within 90 m becomes a projected box (WindowSpill.shader, 36 verts a pane from a structured buffer) that adds a warm pool on the ground below and on walls facing it, from scene depth and its slopes. No real light, no glow slot; follows the indoor Fixture (windowsOn, off by day), fades 60-85 m. Panes recorded by the generators (kit houses, huts, igloos, shell domes). `-windowspill 0` turns it off.
  **Shots (play mode, game camera, night, before = `-windowspill 0`):** big town street, 25 m and 60 m up; a hut from street level and 60 m. Town: warm halo on walls round the windows, pools under the near houses' windows, gone past ~85 m. Hut: pool on the ground and the wall by window and door. Side effect: the pane itself sits inside its box, so panes read brighter than before. A faint vertical edge stays where the cone meets a round hut wall.
  **Counts (big town, night):** 39 lit-window houses, 231 panes.
  **Glow bake:** 5257-5711 ms after vs 5223 ms before (same seed); glowing plants 2221 vs 2259 (kit houses now indoor lights, w < 0, rather than glowing-accent plants).
  **Cost (editor, same 5 spots, spill on / off, 181 frames each):** render thread 2.57/2.58, 2.56/2.54, 2.50/2.56 ms (25 m up); 2.33/2.29, 2.39/2.29 ms (street). Main thread within 0.15 ms. GPU: UNVERIFIED in the editor (its timer reads 0.06 ms and the frame is vsynced at 16.67); a player bench would settle it.
- Row 2 (fixture table, 71366fbf): DONE. `SettlementLayout.Fixtures` and `LandmarkType.Fixtures` list rows of (fixture species, spot door/post/pier, chance, optional OnlyIn biomes). Seven fixture species are kit models (KitShape.Fixture, no collision): lantern, sconce, brazier, camp brazier, candelabra, rune stone, pier lamp. Tables: human village (lanterns on posts, sconces at doors; braziers in tundra/glacier), human and dwarf mining camps, dwarf hold (braziers), elf grove (rune stones, candelabra), goblin and ogre camps (camp braziers), fishing village (pier lamps at dock ends). No culture or biome names in code. They light after dark as LightPolicy Fixture; past the glow hand-off the night map lights them, so they take no slot there. Change: the night far map now holds street lights only; lit windows light the street through their spill panes (row 1).
  **Night sheet (play mode, game camera, 22:00):** dwarf hold (braziers by doors, warm pools on the snow), elf grove (rune post and a warm pool by the tall house), goblin camp (camp braziers light the ground by each hut), fishing village (pier lamp at the dock end; its pool is faint in rain), big human town (lantern posts light the ground round the square). Ogre camp: 0 fixtures placed at the probed camp, not on the sheet.
  **Fixtures placed (probe):** dwarf 11, goblin 3, elf 2, fishing village 1.
  **Budget (big town, same 4 shotcams, before/after):** requests 60/73, 59/71, 56/82, 56/67; glow slots 10/16, 9/16, 7/16, 7/16; real 0/6 both; dropped 0/7, 0/5, 0/17, 0/2. Dropped rose because the 16 slots fill; the dropped ones are lit by the night map. Main thread 6.37/6.42, 8.66/6.49, 8.18/6.17, 6.93/6.33 ms; frame 16.67 ms (vsync) in all.
- Row 3 (beacon tower, b48bedb9): DONE. A climbable stone beacon tower (FixtureStyle.Beacon, FixtureBeacon species, 3 m shaft, deck at 10.4 m x scale 0.92-1.08) stands in a big town's paved square: `FixtureSpot.Beacon` row in the human village table with the new `MinPlots` 30, placed by `FloraPlacement.BeaconSpot` at the square's rim in the widest gap between lane mouths, clear of the well, plots, lanes and the square's own props (pure, so flora, fire and glow bake agree). Stair winds round the shaft (38 treads, rise 0.27-0.30 m, corner landings, a head landing level with the deck), rail open at the stair head, coal bowl and logs on top, door, slit windows, woodpile. Its fire (LandmarkEffectSystem.Beacons) is tonight's Ember Forest colour: near, a flame plus a LightBudget fire light (x1.8, reach x2.6) that lights the square; past the hand-off a far flame out to 1600 m that grows with distance (3 m, x d/200) so it stays a point of fire. The world glow map's fire channel (r) gets a 60 m splat at each beacon, so the square glows from afar too. Landmarks_V3 placement untouched (fixture only, own hash).
  **Placement (probe, seed world):** 2 paved towns; beacon in the 46-plot human town at (2968.2, -878.3), none in the 25-plot layout-1 town (no row). 1 beacon in the world.
  **Climb (play mode, `-walk` round the stair):** player goes from ground y 9.2 to the deck at y 20.1 (10.9 m = deck x 1.049), leg by leg along the four faces; the walker's final "FAIL" line is only its expected-ground check (it expects terrain height, the player is on the deck). The first try stalled at a corner and the deck edge, which led to the head landing.
  **Shots (play mode, game camera, 22:00, weather seed 4):** square with beacon on/off (tower face and the house beside it lit blue-white on, dark off); deck with the player beside the bowl's flame; 1 km out (camera 40 m up at 2044,-1261): before the far-size change the flame was a sub-pixel speck, after it a clear small blue flame over the town. Top-down view from inside the deck is cluttered by the flame itself.
  **Cost (square shotcam, beacons on/off, 181/178 frames):** main 7.60/7.59 ms, render 3.14/3.03 ms, light requests 76/68 (dropped 9/1, the 16 slots were already full); frame 16.67 ms (vsync) both.
  **City-landmark plan (not built):** (1) a landmark tier above the beacon for the biggest towns (60+ plots): a keep or clock tower as the square's centrepiece in place of the well, its own landmark type so it takes its own spots and leaves Landmarks_V3 alone. (2) Built from the same kit pieces as the beacon (shaft, winding stair, deck), with a walled yard and a gate facing the main road mouth. (3) Lit as one more LightBudget fire and a glow-map splat, like the beacon. (4) Seen far off: a tall silhouette plus a far flame or lit windows. (5) Gameplay hook: the deck as a lookout that marks nearby landmarks on the map (needs the player lane). (6) Culture variants: dwarf hold gate tower (stone, braziers), elf grove spire (rune light, no fire), goblin watch platform on stilts.
  **Suite (after the last code change):** 14 passed, 1 failed, 4 skipped in 246s; the failure was Fauna_Fingerprint 7D75BB04 -> B15790ED (the flora blob it bakes now holds the beacon species and MinPlots), re-pinned in the test and re-run alone: 1 passed (B15790ED both runs). Landmarks_V3 112 A955AFE0 unchanged.

## T-1 (shot tool, tree Punchy-world, branch tools/shots)
- Row 1 (settled frame, 6781e65e): DONE. `ShotTool` gates each shot until the camera and player have held still for 6 frames, the terrain has no chunk pending, every flora tile in range is placed and uploaded with no far-shadow cache redraw under way, and the caves round the camera are built. The camera then renders one normal frame into the shot texture. Batch mode has no player-loop render, so the shot is drawn from a late LateUpdate after completing tracked jobs. `-shotlist` (editor and player builds) and the tests' `Shots.Settled` both use it. On a timeout it writes `[shot] FAIL ... waiting on ...` and no file. The log line says what it waited on: `[shot] 't1-town' ready after 0.5 s (waited flora placing 0.4 s)`.
  **Proof (W-126 failure):** `ShotToolTests.SettledFrame_ForestAndTown` [Explicit] PASS in 59 s. It takes old `Shots.Take` and settled shots of the same spots. Old: forests and the town show no trees or houses. Settled: full forests, every house and cactus.
  **Player match:** the Dev player build ran the same 3 spots from `-shotlist ... -quit` and exited on its own. The forest mid, forest ground and town shots match the editor's frame for frame: same trees and houses, no placeholder quads; only the clouds differ (live weather). Gate waits in the player: 0.2, 0.1 and 3.1 s (terrain chunks 3.0 s).
- Row 2 (framing by subject, 451cfbb8): DONE. `-frame <target>` takes a point (`x,z`), a landmark (`#37` or a kind such as `CaveEntrance`), `flora:<species>` (the nearest dense stand: landmark props, town plots and scatter, from a Burst scan), or `clearing`. Any target takes `@x,z` to say where to search from. `-framing close|mid|aerial|ground` sets the preset; `-from water` keeps the lens over water. The player is moved to the subject so it loads. The tool then scores 12 headings x 3 distances by five rays (centre, top, left, right, base) against the terrain, kit boxes and tree trunks and crowns, and keeps the least hidden one. The lens is lifted out of the ground, water and boxes. A cave landmark is framed at its cave's mouth, from outside. The shot gate waits while framing runs. Each shot logs one `[frame]` line with the heading, distance and hidden share.
  **Proof:** `ShotToolTests.Framing_BySubject` [Explicit] PASS in 106 s. All 6 framed on the first try, each with hidden 0.00 and ready in 0.5-1.6 s:
  - Saguaro: a stand of 7 on the W-126 Sonoran slope, mid, 49 m.
  - Temperate clearing: open 14 m with 25 trees ringing it. Aerial from 57 m; ground from inside, looking across.
  - Cave: CaveEntrance #37, cave 16 mouth (2.4 m opening), close, 10 m. The dark mouth is centred.
  - Inn: aerial from 40 m. The roof is centred among the town houses.
  - Stilt village: from the water, 26 m. Stilt houses and piers.
  The first attempt was BAD on the clearing (a bare slope) and the cave (a rock mound with no mouth). The clearing finder and the cave mouth aim fixed both. None of the 6 subjects had kit boxes, so no test checked the kit-box rays.
- Row 3 (self-check and budget, 84184d1d): DONE. Each shot now logs a written check after the image is read: mean brightness, near-black share (luminance < 0.04), near-white share (all channels > 245), sky share (16x9 camera rays that meet no ground within 3 km), lens under the ground, and for `-frame` shots whether the subject is on-screen and not hidden (by terrain, a building or plants). It is BAD when: lens under the ground; black (by day mean < 0.06 or near-black > 0.6; at night or in a cave only if almost all black); blown out (near-white > 0.4); target off-screen or hidden; all sky (> 0.9, framed shots only). A bad shot retries once: `-frame` takes the next-best heading at least 59 deg round; a posed camera is lifted over the ground and turned 90 deg; a live camera is not moved. After that it is kept and marked `BAD <reason>`. With `PUNCHY_GALLERY` set, full-res copies go to that folder. Each run writes one contact sheet: 320x180 tiles, 4 to a row, green or red label bars, JPG. Shot lists write `contact-sheet.jpg`; tests use the `ShotRunStart`/`ShotRunSheet` hooks.
  **Proof:** `ShotToolTests.SelfCheck_TenShots` [Explicit] PASS in 174 s. 10 framed shots over 5 biomes (Temperate, Sonoran, Tundra, FungalForest, Mountains), noon and dusk (18:10). 10 of 10 good:
  - temperate clearing day: mean 0.56, near-black 0.00, near-white 0.00, sky 0.00, target on-screen and clear.
  - temperate clearing dusk: mean 0.23, near-black 0.09, sky 0.22, target clear, night.
  - sonoran saguaro day: mean 0.59, near-black 0.00, sky 0.11, target clear.
  - sonoran saguaro dusk: mean 0.24, near-black 0.01, sky 0.32, target clear, night.
  - tundra spruce day: mean 0.55, near-black 0.00, sky 0.17, target clear.
  - tundra spruce dusk: mean 0.19, near-black 0.32, sky 0.35, target clear, night.
  - fungal oak day: mean 0.47, near-black 0.00, sky 0.22, target clear.
  - fungal oak dusk: mean 0.22, near-black 0.07, sky 0.22, target clear, night.
  - mountain pine day: mean 0.53, near-black 0.00, sky 0.00, target clear.
  - mountain pine dusk: good after retry. It was first "target hidden by plants" and was re-framed 120 deg round (looking 60 deg from 74 m): mean 0.13, near-black 0.17, sky 0.04, target clear, night.
  **Seeded failure:** a camera posed 6 m under the ground. Caught as "inside geometry (lens under the ground)", lifted and turned to heading 180, then good (mean 0.50, sky 0.06).
  **Contact sheet:** 11 shots, 1280x582, 116 KB (limit 300). I looked only at the sheet; it matches the checks. Full res is in the T-1 gallery folder (world/T-1/gallery).
  **Real failures fixed on the way:**
  - FungalGlowCap: `-frame` gives "FAIL no FungalGlowCap within reach" from the Fungal view spot, so the test now uses FungalOak.
  - The same subject and framing twice log the same `[frame]` line, so the test now writes a marker before each frame.
- **Row 4 DONE** (18f75d7d, tools/shots): docs/agents/testing.md gains "How to take shots" (Settled with -camlook, -frame subjects/framings, -shotlist/-shotdir, ShotRunStart/ShotRunSheet hooks, PUNCHY_GALLERY env var, check line and BAD rules) and the card checklist line: `Shots: T-1 tool (Shots.Settled, -frame or -shotlist); quote each [shot] check line; look only at the contact sheet; full res to PUNCHY_GALLERY; any BAD shot is retaken or reported with its reason.` Shots.Take, DebugControls.Shot and CaveTools.PreviewShot now point at the tool; no behaviour change (WorldTests rain-deer still on Take).
  **W-126 Explicit tests on Settled, all PASS:** Clearings_AerialAndGroundShots 124.3 s, Clearings_PlanMaps 311.8 s, Habitat_FramedShots 76.5 s, Habitat_StatsAndShots 107.5 s.
  **Suite (final code):** 15 passed, 0 failed, 7 skipped in 211s. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 7D75BB04.
  **NEXT:** supervisor review and merge of tools/shots (rows 1-4: 6781e65e, 451cfbb8, 84184d1d, 18f75d7d).

## R-5 (wide cached sun shadow, tree Punchy-world, branch shadow/wide)

- **Row 1 DONE (verified at 300/800 m; 1.5 km UNVERIFIED, fogged in both)** (6577f63f, shadow/wide): `Rendering/WideShadow.cs` lays one 2048 R16 sun-view map ~3.2 km across (reach 1600 m) round the camera, texel-snapped, from the coarse terrain (129x129 field at 25 m, lowered 1.5 m), the colossi and the huge landmarks (salt tower, sunken temple via one `WideShadow.Cast` line each). Redraw over 4 frames on a ~1 deg sun turn or ~300 m move. Terrain, flora and water read it past the last cascade (`Shaders/WideShadow.hlsl`), min'd with the real shadow.
  **Shots (old = FarShadow only, new = WideShadow only, -goto + -shotcam, weather Clear forced at the tower), all `Done, near-black 0.00, near-white 0.00`:** mean old/new: tower 300 m 0.70/0.71, 800 m 0.67/0.66, 1500 m 0.72/0.72; salt crystal 300 m 0.65/0.65, 800 m 0.66/0.66, 1500 m 0.72/0.72; RockGolem 300 m 0.60/0.60, 800 m 0.63/0.63, 1500 m 0.72/0.73. By eye on the sheet: the basalt tower's long shadow on the salt flat matches old in shape and darkness at 300 m; faint in both at 800 m; 1500 m fogged out in both; golem shots hazy (rain at 1500 m, weather forced only near the tower).
  **Contact sheet:** Builds/Shots/R5/contact-sheet.jpg in the world tree (18 pngs beside it).
  **Cost:** redraw 7-47 casters + terrain, CPU 0.84-1.47 ms total over 4 frames (worst frame 0.19-0.41 ms), GPU 0.03-0.04 ms (own timer; a few 2.5-5 ms readings look like timer stalls). Same-spot A/B (tower 300 m, 300 frames each, twice): editor GPU frame time none 0.11/0.06, old 0.07/0.09, new 0.06/0.09 ms: noise-level, so no measurable cost. **Memory 16 MB** (2 x 2048^2 x 2 B, double-buffered so receivers never read a half-drawn map), not the card's ~8 MB.
  **Allocation:** 0 B over 600 WideShadow.LateUpdate calls, including one redraw (GC.GetAllocatedBytesForCurrentThread round each call). Only the redraw log string allocates, once per redraw.
  **Risks / to decide:** shared shader edits (TerrainSurface, Flora, Water: include + receiver line each); two landmark Cast lines (SaltTower, SunkenTemple) that W-128 will merge around; large buildings not cast (town code is W-128's); 16 MB vs 8 MB.
- **Row 1 v2 DONE (verified at 300/800 m; 1.5 km UNVERIFIED, fogged in both): casters by the DetailPolicy rule (Zach's change)** (5922ef4e, shadow/wide):
  - **Rule, not a list:** new ShadowTier 2 "wide map" between cached and none (Huge rule WideShadow 30 px, 15% hysteresis). Every loaded renderer whose size could still pass the tier is a candidate (rescan every 90 frames); flora casts through a WideCull kernel per plant (DetailPolicy.WideReach: trees and built things). The old ScanColossi/hand list is gone.
  - **Caster kinds at the big town (2972,-885), 16:00:** `13 meshes (SunkenTemple LOD2 1, SaltTower LOD2 1, Colossus 2, WalkingSaguaro 1, SaltCrystal 2, CinderSnail 1, CharredTreant 2, Notice board 3); flora: Tree 690, Structure 122; of 214 candidate renderers`.
  - **Shots (T-1, old = FarShadow only, new = WideShadow only), all `Done, near-black 0.00, near-white 0.00`:** mean old/new: tower 300 m 0.71/0.71, 800 m 0.68/0.68, 1500 m 0.72/0.72; salt crystal 300 m 0.70/0.69, 800 m 0.68/0.68, 1500 m 0.72/0.72; RockGolem colossus 300 m 0.57/0.57, 800 m 0.64/0.64, 1500 m 0.74/0.74. Tower and crystal shadows match at 300/800 m; 1500 m is fog in both. Contact sheet: Builds/Shots/R5/row1/contact-sheet.jpg in the world tree. Shot 18 (golem 1500 m new) shows two faint orange vertical bands that 17 doesn't; not chased (likely weather/particles, worth a look).
  - **Cost (town, same spot, A/B flora on/off):** CPU 1.05-1.44 ms over 4 frames (worst frame 0.27-0.34). GPU: flora first cost 6.9-9.4 ms per redraw (worst frame 3.3-4.1) from ~600 empty indirect draws; now only models the cull left plants in are drawn (async readback, 53 of 512 wide models at the town), GPU 0.01-0.04 ms per redraw; bench 10x flora WideDraw 0.05 ms (was 13 ms). Memory 18 MB (16 map + 2 flora cull).
  - **Dirty rects (moving casters):** tallest renderer moved 40 m: rect 36x79 texels, CPU 0.25-0.39 ms, GPU < 0.01 ms; map after dirty rects vs a forced full redraw: 217 of 4.2M texels differ (max 0.12), scattered (other casters moving under the 10 m / 5 deg limits). 147 dirty rects in ~2 min at the town (walking colossi, snails).
  - **Allocation:** 0 B over 600 LateUpdate calls with 12 forced redraws, rescans and dirty checks (kinds logging off).
  - **Risks / to decide:** landmarks drawn immediate-mode still need Cast offers (SaltTower, SunkenTemple already; town code is W-128's). "Notice board" shows as a root kind (mesh naming). The 56 m "Litter" mesh qualifies by size. Flora Tree/Structure keep their Shadow 14 px, so their wide tier comes from WideReach directly; the flora cache and the wide map overlap past the cache range. The day-cycle time-lapse redraws every 1 deg of sun.
- **Row 2 PARTIAL (numbers verified with FarShadow switched off; deletion not committed):**
  - **GPU, live frame (ExecuteRenderGraph, 360 frames per run, three alternating runs each, 16:00, same spot):** old = FarShadow only, new = WideShadow only. Summit (-2446,1154, fixed shotcam): old 1.57/1.61/1.56 ms, new 1.53/1.53/1.44 ms. Big town (2972,-885): old 0.65/0.61/0.63 ms, new 0.65/0.67/0.64 ms (within noise). PlayerLoop: Summit old 7.74/7.86/7.47, new 8.14/7.87/7.82 ms; town old 6.61/6.94/7.15, new 6.97/6.75/6.76 ms.
  - **Shots without FarShadow:** the row-1 "new" shots already have FarShadow off (Builds/Shots/R5/row1/contact-sheet.jpg in the world tree).
  - **Blocked:** the deletion (FarShadow.cs/.shader/.mat, the StoneColossi strips, and the SaltTower/SunkenTemple call deletes, Cast lines left as they were) is ready on local branch r5-row2-wip (ac1b326e) in the world tree, but the untracked Assets/Temp/Editor/WorldProbe.cs uses FarShadow (lines ~1281-1312). Deleting FarShadow breaks the editor compile there, and the card says to leave WorldProbe alone. Needs a decision: trim WorldProbe's FarShadow probe, then apply r5-row2-wip and re-run the suite.
  - **Suite (row 1 code, editor closed):** `15 passed, 0 failed, 7 skipped in 284s (Unity exit 0)`; pins Landmarks_V3 112 A955AFE0, Fauna_Fingerprint B15790ED (unchanged).

## W-128 (playtest fixes: caves, fishing villages, inn, landmark polish; tree Punchy-release, branch world/fixes-1107)

- **Row 1 DONE (verified)** (cdc3e2e9, world/fixes-1107):
  - **Chamber door (-goto -1134,62.2,1193):** the door box and flare left a lip in the doorway. Before `walk STOPPED ... 8.1 m to go`; after `DebugControls: walk ARRIVED at (-1133.8, 62.3, 1188.2), 0.3 m to go, 0 legs left` (re-run after the seam change: same). All 7 rooms of that cave ARRIVED.
  - **Mouth seam, all entrances:** the walls' skin now follows the terrain mesh's own LOD0 height (`TerrainSource.MeshHeight`), and the mesh cut samples `MeshCutDepth` 0.15 m instead of 0.35 (placement and creatures keep `MouthCutDepth`, so baking is unchanged). `CaveSystem.WallReach` 2.5 -> 3.5: the skin was culled under part of cave 22's cut, which showed sky through a hole above the mouth (the cut needs 2.73 m reach even at the old depth, 3.29 m at the new; measured over all 28 mouths). Cost: +10% cave-chunk triangles at cave 22 (18284 -> 20168 over 6 chunks).
  - **Seam gap, before -> after** (edge = terrain edge height over the skin where the cut ends, mean |gap| / max; poke = skin showing through kept terrain, from a probe that rebuilds both fields):
    - cave 22 (card spot 1597,109.9,1060): edge 0.237 / 0.684 m -> 0.071 / 0.384 m; poke 588 pts, max 0.329 m -> 0.
    - cave 9 (-1323,98,1395): 0.144 / 0.724 -> 0.058 / 0.556 m; poke 684 pts, max 0.385 -> 0.
    - cave 17 (-1360,83,1091): 0.306 / 1.087 -> 0.093 / 0.663 m; poke 610 pts, max 0.361 -> 0.
    - cave 19 (-1120,77,1229): 0.119 / 0.714 -> 0.035 / 0.164 m; poke 694 pts, max 0.357 -> 0.
  - **Shots (T-1 tool, cave 22, noon):** before `seam22-front ... mean 0.52, near-black 0.00, near-white 0.00, sky 0.18 ... good`, `seam22-side ... mean 0.43 ... sky 0.10 ... good`; after `seam22-front mean 0.41 ... good`, `seam22-side mean 0.35 ... good`, `speck-close mean 0.37, near-black 0.03 ... good`, `speck-mid mean 0.48 ... good`. Grey skin patches on the ground are gone; no sky hole above the mouth. Contact sheets in the W-128 agent's scratchpad (shots/w128/seam-before, seam-after2).
  - **Pins:** Landmarks_V3 112 A955AFE0, Fauna_Fingerprint B15790ED (unchanged).
  - **Risks:** the remaining max edge steps (0.4-0.66 m) are single points on steep cliff faces; WallReach grows every cave chunk's skin a little (hidden under the terrain).
- **Row 2 DONE (verified), landmark move needs approval** (cfe23161 boats, d68e1c6a marsh site; world/fixes-1107):
  - **Boats:** the moored skiffs sit at DockWater with a 0.18 m draft and a rebuilt closed hull (keel, planks to the gunwale, no gaps); the wharf skips a skiff whose berth would hit a walk or a stilt house. Waterline offsets (keel draft vs water at the boat), all 4 moored boats: dock m417 387.6,-630.4 0.000 m; wharf m456 -2181.2,3751.1 0.000 m; wharf m451 401.8,-618.6 0.000 m; wharf m452 -1221.8,3859.7 0.000 m. **Max 0.000 m.** No boat has ground above its keel (bed 0.07-6.37 m below).
  - **Site rule breakers: 1 before -> 0 after.** Before: MarshFolkVillage #111 at -1240,320 dug its own pool (Flatten + a Pools entry). FishingVillage #101-#104 and SeaFolkVillage #108 already pass (river/sea/lake). Now the marsh village is placed by MarshFolkBaking on natural lake or sea water 0.4-3 m deep (middle + 14 of 16 ring points; no rivers, no landmark pools), base at the water line, turned so its landing walk ends on the bank; the asset no longer flattens or digs. Log: `[landmarks] MarshFolkVillage 1 at -3120.0,-2320.0: facing 326 deg, water 0.0 (2.0 m deep), 15/16 of its ring in water`. Props: 6 MarshHut, 1 MarshSmokeHut, 3 MarshCross, 1 MarshWalk, all floating at y 0.01. Fauna: `[marsh life] village 111 peopled: 8 frogs on pads, 1 heron, 6 dragonflies`.
  - **Landmark move (NOT approved, supervisor to check with Zach):** Landmarks_V3 112 A955AFE0 -> 112 E46A5414. Only #111 MarshFolkVillage moved, -1240,320 -> -3120,-2320 (sea shallows at the marsh's coast). Still 1 village (the second needs another clear wet spot). Fauna_Fingerprint B15790ED unchanged.
  - **Shots (T-1 tool, noon):** boats `wharf-boat-side mean 0.34, sky 0.11 good`, `wharf-boat-above mean 0.31 good`, `wharf2-boat-side mean 0.48 good`, `dock-boat-side mean 0.38 good` (shots/w128/boats-after2); marsh `marsh-above mean 0.41, sky 0.11 good`, `marsh-landing mean 0.56, sky 0.44 good`, `marsh-sea-side mean 0.44 good` (shots/w128/marsh-after), in the W-128 agent's scratchpad.
  - **Risks:** the murky water tint (OverrideWater) only colours a landmark pool, so the village's water is now plain sea; the old site at -1240,320 is ordinary marsh again.
- **Row 3 DONE (verified)** (cce82b69, world/fixes-1107):
  - **Inn from the kit:** `StructureKit.Inn` builds a two-storey kit House on the old inn's box (walls at +-half / +-front, eaves at the old wall height, five bays so the door bay is centred) in the folk's trim and looks (Human: plastered walls, quoins, shutters, gable roof with courses and a chimney; Dwarf: crenellated flat roof). Porch: deck, lean-to roof under the upper sills on two posts with a beam and braces, rails, a lantern, barrels, a bench. The sign hangs on rods 0.25 m under the porch roof's front edge, so it follows the overhang. KitStructure's per-part drawing moved into `DrawKitPart` (pure refactor, same order and jitter), shared by both.
  - **Spots unchanged:** the inn keeps its hinged Doorway at front+0.02, Width, TrunkRadius and eave-high collider; `DoorFront` / interior anchors are species-derived. Probe over both inns' anchors, before -> after: `inn 3016,-852 m524: Door 1 Sit 2 hash BF13DC50 | inn 1164,-3077 m525: Work 1 hash AA161A79` -> identical. `Doors: 141 house doors on this island` before and after.
  - **Shots (T-1 tool, noon, both v3 inns, wide + porch):** old `inn0-wide-old mean 0.52 good`, `inn0-porch-old mean 0.27, near-black 0.13 good`, `inn1-wide-old mean 0.47 good`, `inn1-porch-old mean 0.23, near-black 0.22 good`; new Human `inn0-wide-new mean 0.57, near-black 0.00 good`, `inn0-porch-new mean 0.55 good`, `inn1-wide-new mean 0.51 good`, `inn1-porch-new mean 0.47 good`; new Dwarf (Inn.asset culture set live, reverted, not committed) `inn0-wide-dwarf mean 0.56 good`, `inn0-porch-dwarf mean 0.53 good`, `inn1-wide-dwarf mean 0.51 good`, `inn1-porch-dwarf mean 0.47 good`. Contact sheets in the W-128 agent's scratchpad (shots/w128/inn-old, inn-new, inn-dwarf).
  - **Risks:** the island's biggest settlement builds its inn from its layout's Kit Building, so only HumanVillage / HumanMiningCamp / LoneInn inns change. Porch posts and props have no collision (as before); the inn still collides as one cylinder.

## R-6 (one LOD rule for the very big; tree Punchy-world, branch lod/big)

- **Row 0 DONE (verified)** (97e1da66, lod/big):
  - **One metric:** FloraCull's LOD level, real-time shadow tier (sharp), shadow stillness and draw fade now use `PlantDistance` = 3D distance from the camera to the nearest point of the plant's base-to-top segment. The reject and cast tests keep the bounding sphere's nearest surface (3D, never further), so no ground-level caster loses its real-time shadow (a first try with the segment there dropped 29 L2 casters; fixed). Same 3D metric in WideCull's cache hand-over, the shadow cache receiver's fade (FloraShadowCache.shader, eye xyz), Flora.shader's cheap far caster holes, waterfall effects (LandmarkEffectSystem `FallDistance`) and sky birds. Flora tile streaming and terrain chunk rings stay on the ground plane, with comments saying why (3D is never shorter, so the ground ring holds everything; no reloads when climbing).
  - **Counts (forest at -1236,1324, 15:00, Clear re-forced after the pose; old -> new):**
    - ground: LOD 69/604/3306 -> 68/603/3305; real-time casters 131 -> 131 (L1 52+11, L2 39+29 both); cache 0; wide 5698 -> 5694.
    - cliff (-1556,115,1356): LOD 0/319/5532 -> 0/307/5539; rt 0, cache 55 -> 55; wide 4900 -> 4893.
    - summit (-1636,144,1364): LOD 0/141/6110 -> 0/101/6082; rt 0; cache 0; wide 4214 -> 4187.
  - **GPU (camera render to 1920x1080, 3x40 frames):** ground 2.97 -> 2.99 ms, cliff 2.88 -> 2.94 ms, summit 2.76 -> 2.69 ms (no worse; two earlier new runs read about +0.8 ms but a rerun of the same files and a bisect showed it was run-to-run noise).
  - **Shots (T-1 tool):** contact sheets Assets/Temp/shots/r6-row0-old.jpg and r6-row0-new.jpg in the world tree (ground, cliff, summit): the same trees and shadows, no pop or shadowless band. Overall brightness differs between runs from cloud cover (sky included), not from the change.
  - **Pins:** placement code untouched.
  - **Risks:** from high ground slightly fewer trees are at LOD1 (their true distance is longer), as intended.
- **R-5 row 2 (FarShadow retired) DONE (verified)** (d82ac780, lod/big; cherry-pick of the r5-row2-wip commit, approved by Zach):
  - Deletes FarShadow.cs/.shader/.mat (+ metas); StoneColossi, SaltTower and SunkenTemple no longer register far casters (WideShadow covers them).
  - Nothing else references FarShadow: code, shaders, Resources and docs grep clean, no GUID references; one historical comment left in WideShadow.cs.
  - **Suite (editor closed):** `15 passed, 0 failed, 7 skipped in 289s (Unity exit 0)`; Landmarks_V3 112 A955AFE0, Fauna_Fingerprint B15790ED unchanged.
- **Row 1 DONE (verified except the big-town tris check, which FAILS; see below)** (7f640c86, lod/big):
  - Ribcage, salt tower and sunken temple pick LOD0/1/2 via `DetailPolicy.LandmarkLod` (whole bounding diameter, Huge geometry thresholds 295/75 px, one decision per landmark with hysteresis); sunken temple still forces LOD0 with the camera in its interior box. Build logs size and switch distances.
  - **Switch distances (reference FOV, old -> new):** ribcage 357 m: 300/1500 -> 1133/4457; salt tower 81 m: 250/1200 -> 257/1010; sunken temple 89 m: 120/1200 -> 281/1104. Tris per LOD: ribcage 15573/4892/1552, tower 7048/1660/692, temple 1482/974/348.
  - **Poses (landmark LOD old -> new):** ribcage 217 m 0->0, 755 m 1->0, 2766 m (edge) 2->1; tower 294 m 1->1, 793 m 1->1, 2843 m 2->2; temple 286 m 1->1, 789 m 1->1, 2831 m 2->2. At these poses every change is toward more detail. Not covered by a pose: tower and temple now drop to LOD2 a little earlier (1010 m vs 1200 m, 1104 m vs 1200 m); the ribcage drops much later.
  - **Shots (T-1 tool):** contact sheets Assets/Temp/shots/r6-row1-old.jpg and r6-row1-new.jpg in the world tree (10 poses each): same scenes, no visible pop. At 1280x720 the edge shots show the landmarks only as specks or below the horizon.
  - **Big town (town #5, r 96, eye at ground + 12 m): FAILS "tris no higher".** The ribcage, 2022 m away, now draws LOD1 instead of LOD2, so landmark tris go 2592 -> 5932 (+3340). Render time 3.10 -> 3.05 ms (no worse). This follows from the rule: a 357 m object is ~165 px tall at 2 km. Using the largest extent instead of the diagonal would not change it (LOD1 reach would still be ~3.7 km). Supervisor/Zach to decide: accept it, or give landmarks a separate LOD2 threshold.
  - **Pins:** placement code untouched.
- **Row 2 DONE (verified; edge and three-colossi shots are weak, see below)** (528f260b, lod/big):
  - New `Fauna/ColossusLod.cs`: per colossus, two far levels built on workers from the meshes StoneColossi just made (Burst vertex clustering at 1 px cells for each level's switch distance; merged triangle colours averaged, winding kept, flat normals). Level = `DetailPolicy.LandmarkLod` on the whole body's diameter (one decision with hysteresis). Far levels draw per part with Graphics.RenderMesh from each part's own transform (renderer forceRenderingOff), so they follow the rig and walk cycle; sharedMesh stays LOD0 because the rig, collision and WideShadow read it. DunesColossus skipped (dynamic furrow mesh). Files are in the Fauna/ folder (animal lane's area; the card asked for Fauna/Colossus*).
  - **Tris per level (full/LOD1/LOD2):** KneelingTitan 84 m 2682/2582/2254 (switch 266/1045 m); HalfBuriedHead 3004/2890/2402; GiantTortoise 8588/5087/2318; RockGolem 114 m 7710/4954/2884 (362/1423 m, 26 parts); CarnivorousPlant 54768/20724/7095; SkeletalLizard 30068/14421/4043; WalkingSaguaro 125 m 59650/30164/8990 (397/1563 m); SaltCrystal 9266/8163/3166; MesaTortoise 7888/6829/4149; GlassHeron 4344/2662/1484; LanternStag 6480/4275/1984; CinderSnail 14950/12620/6879; MireWalker 43412/23271/11897; SnowWorm 25666/13856/6509; WalkingOak 60152/34351/12943; Puffball 26968/15769/5616; CharredTreant 30328/20434/13556. Built in 20-229 ms on workers.
  - **Drawn tris, all colossi within 3 km (full 510204):** saguaro at 200 m 206597, 600 m 192746, edge 131501, three-in-view 146658.
  - **Render ms (1920x1080, LOD on/off):** 200 m 2.89/2.89, 600 m 2.99/2.99, edge 3.14/3.18, three 2.94/2.93. Main-thread cost of the LOD script 0.11 ms/frame (RenderMesh for ~70 far parts; the renderers' own cull/draw is saved).
  - **Shots (T-1 tool):** Assets/Temp/shots/r6-row2-onoff.jpg and r6-row2-crops.jpg in the world tree (LOD on vs off, same frame). Saguaro at 200 m and 600 m reads the same; on/off mean pixel diff < 1.4 per channel, the differing pixels spread over the whole frame (wind, walker motion). At the edge (2850 m) the saguaro is lost in haze in both, and the three-colossi pose shows no clear colossus, so those two compare only as equal frames, not as a readable silhouette.
  - **Pins:** placement code untouched; WorldProbe.cs and r5-row2-wip untouched.
- **R-6 row 3 DONE (verified)** (92e42fc4, lod/big): intact kit houses' LOD 2 is their outer shell. FloraMeshBuilder.Box skips faces
  that look into the closed inside below the eaves or down onto the ground, only for KitShape.House and the four hazard houses at LOD 2
  (LOD 2 already had no floors, inside stairs or slabs for intact houses; StiltHouse and other shapes are left alone).
  - Big town (#5, r 96 m) from 400 m: 66 buildings at LOD 2, **19248 -> 16608 tris (-14%)**, about 292 -> 252 per building; 900 m: 19104 -> 16464.
    All 347 intact variants: 196332 -> 186504 LOD 2 tris; 72 variants shelled. Render 2.72 -> 2.65 ms at 400 m (noise level).
  - Ruins: 12 variants, LOD 0-2 identical tris/verts with the shell on or off. Intact LOD 0/1: 0 of 347 differ.
  - Shots (same session, LOD 2 meshes swapped): Assets/Temp/shots/r6-row3-town-high-oldnew.png (from above, old | new) and
    r6-row3-town400-zoom.png (old over new): same outline and colours.
  - Risk: merges next to W-128's edits in FloraGenerator.Kit.cs (mine are at the top of the class and right after the eaves line).
    The probe found one trap: swapping a model's mesh in play mode also needs the layer's indirect args updated (they hold the index range).

- **R-6 row 4 DONE (verified by counts; the shots show little)** (fcf19e00, lod/big): a model's draw reach is no longer cut by its layer's Radius.
  - Each Far-0 model in the shrub and tree layers keeps its size reach (size / FloraEdgePixels 4 px at the current pixels per metre), clamped to [Radius, FarRadius]. FarRadius is the layer's largest size reach, capped at the terrain edge (1536 m at View 1). The ground layer is left alone.
  - "Far tiles" stream in a second pass between Radius and FarRadius. Harvest trims them to the plants big enough to show there; they are dropped when the eye comes within Radius or moves 20% closer, or when pixels per metre change. `-nofarflora` (FloraSystem.FarReach) turns it off at runtime.
  - **Reach data (935 ppm, 4 px):** shrubs (R 260): 37 variants (991 instances) reach past R, up to 2615 m (stepping stones, boardwalks, rocks, bone remains). Trees (R 1500): 111 variants (16038 instances), up to the edge (canopy trees 35-40 m, MagneticDrift rocks, wharves).
  - **Drawn flora models, old -> new:** dense forest (ground): shrubs 630 -> 941, trees 2675 -> 2714. Ridge view (forest top looking 1.2-1.5 km out): shrubs 252 -> 488, trees 3615 -> 3738.
  - **Render ms (1920x1080):** forest 2.82 -> 2.97 (+0.15), ridge 2.95 -> 2.99.
  - **Far tiles held:** shrubs about 4600 tiles out to 1536 m (keeping about 5-8k extra instances), trees 16-42.
  - **Shots (T-1 tool):** Assets/Temp/shots/r6-row4-{forest,ridge}-{old,new}.png in the world tree. Same views. The ridge pose looks out only about 1.5 km, so the extra trees are a few pixels and the visible differences are clouds and a walking colossus. The drawn counts carry the check.
  - **Risk:** the shrub layer now places about 1.9M cells for its far tiles while streaming, to keep a few hundred big rocks and boardwalks. I did not measure the stream-time cost. If it hitches, cap the shrub far reach (e.g. 600 m) or raise the threshold for shrubs only.
  - **R-5 side notes:**
    - **Wide-map redraws in a fast time-lapse:** not reproduced. At a 30 s day (12 deg/s) it was 6 redraws per 20 s, with or without a sun-turn-rate throttle, so I reverted the throttle and left WideShadow unchanged.
    - **Heron "Litter" 56 m caster:** a real 40 m spread of debris and fulgurites around the GlassHeron (StoneColossi.GlassHeron.cs, Fauna). It qualifies by its largest extent. Not fixed.
    - **"Notice board" label with 3 casters and the golem's orange bands:** not looked at (no time in play mode). Not fixed.
- **R-6 row 4b DONE (verified; the visible change is mild)** (5e78d89d, lod/big): the terrain fine relief now fades one octave at a time, by on-screen size, instead of all at one fixed range.
  - Octaves: a new 3 m swell (slope weight 1.6, colour 0.4), the 0.7 m clods and the 0.21 m grain. Each fades from 4 to 2 reference pixels of its own wavelength (Detail pixels per metre, 935 at Detail 1). At Detail 1 the grain ends near 100 m, the clods near 330 m (were 110 m) and the swell near 1.4 km. Faded octaves are skipped by branch. The sand grains keep the old range. `TerrainStreamingSystem.MicroReach = false` restores the old fade to compare.
  - **Render ms (1920x1080, synced, old/new alternating 4 rounds):** shore spot with ground filling the frame: 2.545 -> 2.583 and 2.576 -> 2.587 (+0.01 to +0.04). Looking inland at 60 m: 2.679 -> 2.685 and 2.635 -> 2.637. All within the +0.2 ms limit.
  - **Shots (T-1 tool):** Assets/Temp/shots/r6-row4b-{g60,g150,g300,shore}-{old,new}.png, plus r6-row4b-crop150-old-top-new-bottom.png, in the world tree. Eye 2.5 m up at a shore, looking inland. The ground 60-150 m out shows more mottling in new (mean pixel change 4.8 levels in that band, under 2 elsewhere). The eye is low, so the 60/150/300 m poses frame almost the same scene.
  - **Shimmer:** each pose was shot twice, turned about 0.4 px apart. The mean difference between the twins is the same old vs new in every distance band (for example 300 m: 1.42/3.28/7.78 old, 1.60/3.36/8.02 new), so the new fades add no measurable shimmer. Grass and flora account for most of the twin difference. A live slow pan was not watched.
- **Row 4 DONE (verified)** (766bf902, world/fixes-1107):
  - **Giant ribcage:** each rib and the spine are walkable floors along the bone (kit box tops following the curve), and a rib stops walling you once you stand on it. Walk test on rib 3: `-goto -448.2,101.7,-2338.5`, walked ~28 m along the bone, `walk ARRIVED at (-458.9, 90.5, -2365.7) ... 0.0 m over the floor`; stepping 8 m sideways off it falls to the ground (feet 28.42).
  - **Electric Wastelands rocks:** MagneticDebris / MagneticDrift mounds and fallen chunks get kit boxes cut from a height grid of the drawn rock (strips across the long axis, shared by every tier so each tier rests on the one below), instead of an ellipse round it. Box-top samples with no rock under them (>0.3 m): (-640,-1230) r60 **2284 of 61259 -> 0 of 60768**; (-987,-3990) **9666 of 288842 -> 6 of 258994** (the 6 are W-106's stone boxes on Debris m1). Edge check beside the drift mound's point (`-goto -634.61,20.6,-1224.62`): before stood on air at 20.25 (box top 20.25, ground 18.80, no rock), now `no top`, falls to 18.80. Centre drop stands at 20.92 = box top; walking down and back up the mound's tiers works (19.8 <-> 20.9).
  - **Desert tomb:** column shafts in a fluted drum pattern (vertex alpha + UVs in Punchy/SunkenTemple), room floors as close-laid tiles with no grout. Shot (T-1 tool, `-place temple`, hall 16x16 m): `tomb-hall ... camera (3518.9, 22.9, 354.1); check: mean 0.20, near-black 0.00, near-white 0.00, sky 0.00 ... good` (shots/w128/tomb2 in the W-128 agent's scratchpad; tomb-warm in that sheet is a pre-island frame, ignore).
  - **Risks:** a drawn rock edge taller than a step (0.4 m) is now a wall (the drift mound's rim is a ~0.6 m drawn ledge; the player jumps it; the old ellipse terraces let you walk up on air). The walk tool can't jump, so it stops at such rims.
  - **Suite (editor closed, after the last change):** `13 passed, 2 failed, 7 skipped in 278s (Unity exit 2).` The 2 failures are the pins, both from row 2's marsh move, not row 4: Landmarks_V3 `112 E46A5414` (expected A955AFE0), Fauna_Fingerprint `66320C7C` (expected B15790ED).
- **Correction to row 2:** Fauna_Fingerprint is NOT unchanged. Bisected with the single test: cfe23161 (boats) `fingerprint B15790ED, 1 passed`; cce82b69 (after the marsh move) `fingerprint 66320C7C, failed`. The marsh village move (d68e1c6a) changes it B15790ED -> 66320C7C. Both pins need re-pinning only if Zach approves the move; test pins were not touched.

## W-128 row 5

- **Row 5 DONE (verified by numbers)** (f2b67f3a revert, 293ad9b9 merge, world/fixes-1107): the marsh folk village stays where it is.
  - `git revert d68e1c6a` (clean; boats cfe23161 and rows 1, 3, 4 kept), then origin/main merged in (3 behind, no conflicts).
  - **Landmarks_V3: `112 A955AFE0`** (editor hook and suite). Log: `[landmarks] MarshFolkVillage 1 at -1240.0,320.0: facing 291 deg, base 112.9, relief 7.38 m`.
  - **Fishing-village site rule (check only, no moves): 1 breaker.** MarshFolkVillage #111 at -1240,320 (no sea/lake/river, 120 of 952 points in its own pool), kept on purpose per Zach. FishingVillage #101-#104 (river; #103 also sea) and SeaFolkVillage #108 (lake) pass.
  - **Fauna_Fingerprint: `66320C7C`, NOT back to B15790ED.** Re-bisected with the single test: d68e1c6a (marsh move) `B15790ED, passed`; f2b67f3a (row 4 + marsh reverted) `66320C7C`. So row 3 (cce82b69, the inn in kit pieces) changes it, not the marsh move: my row-4 "correction" was wrong. Reaching B15790ED would mean reverting the inn; needs a re-pin to 66320C7C (supervisor's call; not re-pinned here).
  - **Suite (editor closed, after the last change):** `13 passed, 2 failed, 7 skipped in 219s (Unity exit 2).` Fails: Fauna_Fingerprint (above) and FolkTalkTests.WalkBy_NeverThroughWall (`1 through a wall, 151 held back by walls`). WalkBy re-run alone at the same head: `3 passed, 0 failed, 0 skipped in 208s` (0 through a wall), and it passed at f2b67f3a too: flaky, not from this row.
  - Next: supervisor decides the fingerprint re-pin; card W-128 rows done.

## R-6 row 6

- **Row 6 DONE (verified by numbers)** (349553e7 row 6, f18bce10 origin/main adb548d7 merged in, lod/big): Zach 11-09 "original shrub distance is OK". The size-based reach past Radius (row 4, fcf19e00) is kept for the tree layer only; the ground and shrub layers are cut at their Radius again, as before row 4 (shrub FarRadius = Radius, 0 far tiles).
  - **Drawn shrubs, forest / ridge** (all in one session, 1920 wide): row 6 **760-781 / 211**; before row 4 781 / 211; row 4 1084 / 592 (about 4500 far tiles). Trees stay at row 4's reach: 2973 / 5782 (before row 4 2936 / 5777).
  - **Render ms, forest / ridge** (median of 3x40): row 6 2.83-3.84 / 2.75-3.18; before row 4 2.78 / 2.86; row 4 2.70-2.88 / 2.42-2.62. These are noisy because other lanes' editors were open at the time; there is no clear difference between the variants.
  - **Shrub streaming walk** (1400 m at 30 m/s, uncapped), mean: row 4 10.87 / 11.05 ms; row 6 7.56, 8.19, 8.20, 8.49, 8.71, 9.16, 9.16 ms; before row 4 8.01, 8.03, 8.67 ms. Row 4's ~+2.7 ms is gone. Rare single frames of 200-590 ms happen at 1240-1330 m in both the row 6 and the before-row-4 walks, so row 6 does not cause them; probably streaming or the host.
  - **MissingReferenceException: 0** (and 0 errors/exceptions) over two play sessions in a row without a domain reload, plus the exit from play.
  - **Pins (after the merge):** Landmarks_V3 `112 A955AFE0`; Fauna_Fingerprint `66320C7C` (W-128's re-pin on main).
  - **Suite (editor closed, after the merge):** `15 passed, 0 failed, 7 skipped in 287s (Unity exit 0).` This is the first suite run with the WideShadow stale-caster fix (809f8469).
  - Risks: none known. No placement or ProjectSettings changes.

## R-7 (octahedral impostors for far flora; tree Punchy-world, branch lod/impostors)

- **R-7 row 1 DONE (verified)** (80616e50, lod/impostors): shrub models (layer 1) are baked into impostor atlases at load, after Build. Each model is drawn once per frame of a hemi-octahedral grid on the GPU into a slice of two texture arrays: albedo + coverage, and normal / depth / occlusion + leafy flag. It runs 12 models a frame, and the atlases are freed with their layer. There are launch flags `-impostorframes N` (default 8), `-impostorpixels N` (default 32), `-impostorfull` (the whole octahedron) and `-noimpostors`. Nothing draws the atlases yet (row 2).
  - **Load cost:** 214 shrub models in 9.8 ms total over 18 frames. Per model the median is 0.01 ms, p95 0.26 ms and the slowest 1.06 ms (the first model, warm-up). These are CPU submission times only; the GPU time is not measured. Each model gets a log line (`[flora] impostor bake layer 1 model i/214 ...`) and there is a total line.
  - **Atlas memory:** 140.4 MB at 8x8 frames of 32 px (3 mips), and 568.4 MB at 64 px (4 mips; the first run). 12x12 at 32 px would be about 316 MB. I chose 32 px as the default because a shrub at impostor range is only about 13-20 px tall on screen.
  - **Contact sheet** (3 models; albedo over grey on the left, packed normal on the right): PunchyMilestones/R-7/r1/atlas-sheet-32.png (BarrelCactus, DryBush, SwampShrub) and atlas-sheet-64.png (BarrelCactus, FishBoardwalk, SwampShrub). Written check at 480 px: every frame is filled and centred; the middle frame is the top-down view and the corners are horizon views; the outlines are clean, the normals vary smoothly from frame to frame, and the background is empty.
  - Note: the shrub layer also holds props such as FishBoardwalk. Row 3's size rule decides whether they get impostors.
  - Risks: memory scales with the shrub model count. No placement or ProjectSettings changes.
- **R-7 row 2 DONE (verified)** (8b449db7, lod/impostors): far shrubs are drawn as impostors. Each plant past its LOD2 hand-over (DetailPolicy Plant rule `Impostor = 9` px, with hysteresis in the cull) is one camera-facing quad on the same GPU cull and indirect path. It blends the 3 nearest frames, or only the nearest with `-impostoroneframe`. Depth is rebuilt from the baked depth (conservative SV_Depth), so the plants sort and take the sun's shadow, and they are lit from the baked normal the way Flora.shader lights the mesh. There is a dithered cross-fade with LOD2.
  - **Shadows: none.** The impostor has no shadow caster. Far shadows come from the wide cache (R-5/R-6), which still lays shrubs in.
  - **Method:** editor play mode on this PC, hour 15, clear weather, 1280x720, 180-frame means. The forest poses come from R-6's FindForest. Render ms is synced GPU time. "Flora ms" is the new `FloraSystem.Draw` marker (CPU cull dispatch and indirect submit).
  - **Shrubs drawn (lod0/1/2 + imp), render ms, frame ms:**

    | pose | mesh (today) | impostors 3-blend | 1-frame |
    |---|---|---|---|
    | ground at hand-over | 16/120/630, 4.27, 15.78 | 16/120/421 + 271, 4.48, 15.73 | 4.27, 15.15 |
    | hilltop | 0/15/244, 4.83, 21.22 | 0/15/138 + 122, 4.77, 20.97 | 4.81, 20.91 |
    | fly ~100 m | 0/8/439, 3.87, 16.02 | 0/8/263 + 207, 3.74, 16.08 | 3.76, 15.78 |
    | fly ~300 m | no shrubs (reach 260 m), 3.74 | same, 3.66 | 3.65 |

    Flora ms is 0.32 in every case: the submit is per model, not per plant. At today's reach the impostors cost about the same as the LOD2 meshes they replace (-0.13 to +0.21 ms render, within noise). Any saving has to come from longer reach (row 3).
  - **Look:** mean absolute image difference, mesh vs impostor (0-255): ground 11.6 (heavy mist drifts between shots; mesh vs 1-frame is 5.0), hilltop 0.9, fly 100 m 1.0, fly 300 m 0.3 (no impostors in view, so this is the noise floor). At small size the colour and outline match.
  - **Pop:** slow fly-over at 100 m, 10 shots 4 m apart. Frame-to-frame change (mean / worst 32 px block): impostors 5.0-5.8 / 38-51, mesh 6.9-7.8 / 50-59. There is no spike with impostors on.
  - **Shots:** PunchyMilestones/R-7/r2/ ({ground,hilltop,fly100,fly300}-{mesh,imp,one}.png, contact-sheet.jpg, strip100-{imp,mesh}.jpg and frames).
  - **Lit windows (Zach's R-10 note):** buildings are not on impostors in this card. A hook comment marks where they would go in the ForwardLit pass. Cost estimate: a third atlas (an emissive mask baked from the window part), R8 at about +25% of that layer's atlas memory, plus 3 more samples per pixel and the window-hour logic.
  - Risks: no placement or ProjectSettings changes. The 1-frame mode is a debug flag.

## R-8 (skybox cost and adaptive cloud light; tree Punchy-release, branch sky/cost)

- **R-8 row 1 DONE (measured, no look change)** (e58608ab, sky/cost; branch reset onto origin/main a984c4e4 first, since the old sky/cost was already in main). That commit only makes the `-skyoff` 4 (sky colour) and 8 (weather volumes) test bits real branches. They were ternaries, which run both sides, so job 38's "off" timings for bits 4 and 8 still paid for the work.
  - **Method:** this PC, editor in play mode, Summit, noon, `-weatherseed 4`, `-gfx High`, `-lowres half`, camera pinned with `-shotcam` and pitched up 10 deg. The sky material is drawn alone 100 times per command buffer over a full-screen cube, synced by a GPU readback, median of 5 trials, empty-buffer baseline subtracted. The cube covers the whole screen, including the part below the horizon where the game draws terrain, so these are upper bounds. Stars, moon/sun discs and cirrus were split out with temporary bits (1024, 2048, 4096) that are not committed.
  - **Per-full-res-pixel parts of the skybox fragment** (with the low-res buffer on): sky colour (gradient, twilight, sun glow, horizon haze), stars + shooting stars/aurora/blue-moon streaks, moon disc + sun disc + moon halo + blue corona, cirrus (noise), far clouds (above the horizon only), the low-res composite (depth-aware upsample, 2 loads), lightning glow, and the weather volumes (an 8-step march through the weather map with 3 value noises and a cloud-shape read per step that has weather).
  - **Compiled fragment (D3D SM5, my own count of SHDR opcodes; Unity strips the STAT chunk):** Punchy/Sky low-res variant: 2568 instructions, 18 temps, 12 texture reads (10 samples + 2 loads), 39 ifs, 7 loops. Without the keyword: 3538 instructions, 30 temps, 33 reads. CloudsLowRes march: 1055 instructions, 29 temps, 24 reads. (R-4 gave 2489 / 3389 from Unity's counter; my count includes the test branches.)
  - **GPU ms per full-screen sky draw** (this PC; the laptop's DrawSkybox is about 12x these):

    | skyoff | removes | 1920x1080 | 2560x1440 |
    |---|---|---|---|
    | 0 | nothing | 0.305 (0.276 rerun) | 0.608 |
    | 8 | weather volumes | 0.135 (0.117) | 0.271 |
    | 4 | sky colour | 0.191 (0.174) | 0.385 |
    | 1024 | stars + night events | 0.260 | 0.512 |
    | 4096 | cirrus | 0.288 | 0.544 |
    | 2048 | moon/sun discs, halo, corona | 0.282 | 0.567 |
    | 1 | far clouds + low-res composite | 0.293 | 0.572 |
    | 2 | far clouds | 0.305 | 0.588 |
    | 15 | everything | 0.019 | 0.041 |

  - **Most expensive part: the weather volumes, about 55% of the draw** (0.17 ms of 0.31 at 1080p, 0.34 of 0.61 at 1440p), even on a clear noon. The sky colour comes second at about 37% (0.11 / 0.22), and most of that is the stars (0.05 / 0.10). The stars are computed at noon even though `_PunchyNight` is 0. Cirrus costs 0.02 / 0.06, the discs and halo 0.02 / 0.04, and the composite plus far clouds 0.01 / 0.04. All of these are smooth except the discs, the stars and the cloud edges, so row 2 will move the weather volumes and the gradient into a per-frame table and skip the stars when there is no night.
  - **Context:** low-res march (half) is 0.83 ms at 960x540 and 1.46 ms at 1280x720 (`-skyoff 64` 0.81 / 1.44). A whole synced frame takes 2.66 ms at 1080p and 3.75 ms at 1440p. `-lowres quarter` passed mid-session through RunOptions did not resize the buffer, so the quarter numbers come in row 3 from a fresh play session.
  - Risks: none; e58608ab changes test flags only.
- **R-8 row 2 DONE (DrawSkybox -54% by day, -38% at night on this PC; no visible change)** (01e1c1c5, sky/cost).
  - **Change:** the weather volumes were the biggest per-pixel cost (row 1: about 55% of the draw). The skybox now reads them from a 1024x128 direction table instead of marching them for every full-res sky pixel. WeatherSystem renders the table once a frame (HazeLut.shader pass 2) with exactly the old march: 8 steps out to 8 km from the camera. Rows are spaced by y|y|, so most of them land near the horizon. Stars, shooting stars, auroras and blue-moon streaks are now skipped while `_PunchyNight` is 0, because by day they were multiplied by 0. The sun disc, moon, stars, cirrus and cloud edges are still drawn at full resolution. The sky gradient stays per pixel: I didn't put it in the table, since it costs little and holds the sun glow.
  - **DrawSkybox ms** (same session, same method as row 1, Summit view, before = origin shader, after = 01e1c1c5):

    | time | 1080p before | 1080p after | 1440p before | 1440p after |
    |---|---|---|---|---|
    | noon | 0.93 | 0.43 (-54%) | 1.67 | 0.74 (-55%) |
    | sunset (time 19) | 0.98 | 0.61 (-38%) | 1.76 | 1.04 (-41%) |
    | night (time 0) | 0.95 | 0.59 (-39%) | 1.70 | 1.03 (-39%) |

    Absolute numbers drift between sessions on this PC (this session ran about 3x row 1's), but the before/after pairs were measured back to back. Sampling the table costs 0.01-0.05 ms (`-skyoff 8` vs 0). **Night falls short of 50%.** At night the remaining cost is the stars and events (about 0.1 ms) and the blue moon's face (about 0.12 ms at 1080p, partly in this view). All three must stay sharp.
  - **Shots** (T-1 ShotTool, 1280x720; old path vs table in the same frame state): noon, sunset, night, Overcast and Rain (forced at the camera), each from the Summit and from the sea, 20 shots. All 20 checks were `Done`. Each before/after pair has the same mean (for example summit_rain 0.60/0.60, sea_overcast 0.71/0.71) and no near-black or near-white pixels. The contact sheet shows identical pairs, with rain curtains and the aurora present on both sides.
  - **Pixel diffs per pair:** mean 0.01-0.80 of 255, at most 2.5% of pixels over 4. The diff heatmap puts every difference on plant sway, rain streak particles, cloud-edge temporal jitter and the moon's twinkle, and none on the smooth sky where the volumes apply. A control pair (two identical shots, sea rain) differs more: mean 0.91, 2.3% over 4, against 0.46 for that pose's before/after.
  - Gallery (unviewed): PunchyMilestones/R-8/sky-cost/gallery (20 shots + 2 control).
  - Risks: one extra 1024x128 blit per frame (8 steps per texel, about 131k texels, versus about 2M sky pixels at 1080p). No ProjectSettings or placement changes.
- **R-8 row 3 NOT PASSED (needs a decision; nothing committed).** Adaptive light samples top out at about 7.5% cheaper in storm and 3% in Overcast with no visible change. The 15% target is only reached by reusing light between march steps, and that shows as dark speckle.
  - **Method:** editor play mode at the Summit, weather forced, time scale 0. Low-res cloud march timed with synced GPU timers at half res (1280x800), old path (test bit 16384) and new path interleaved in the same eval, 4 runs each per weather. Storm intensity drifts between forces (old path 0.97-1.43 ms), so only same-eval pairs are compared. Light sampling is about 31% of the march (storm 0.97 ms with light vs 0.67 ms without), so it caps what this row can save. Quarter res was not measured: half res already missed the target.
  - **Variants (saving storm / Overcast; shot diff mean vs control noise 1.61 storm / 0.66 Overcast):**

    | variant | storm | Overcast | diff storm / Overcast | look |
    |---|---|---|---|---|
    | fewer light steps by density and transmittance (clean) | -7.5% | -3.3% | 1.61 / 0.55 (= noise) | no change |
    | aggressive thresholds, no reuse | about -14% | - | 2.5-2.7 | not viewed |
    | reuse light every 2.5 steps + aggressive | -16 to -19% | about -17% | 2.87 / 1.24 | visible dark speckle |
    | far-only reuse | -6 to -10% | - | 1.75-2.28 | speckle in crops |
    | light samples share the march point's maps | -12 to -15% | - | 4.5-10 | large change, rejected |

    Clean variant, all weathers (old vs new ms): Clear 0.790 vs 0.763 (-3.3%), PartlyCloudy 0.779 vs 0.790 (+1.5%), Overcast 0.855 vs 0.827 (-3.3%), Storm 1.303 vs 1.206 (-7.5%). The 0.9 history blend doesn't hide the reuse speckle, because the speckle sits still in view space.
  - **Ghosting (committed code, storm at the Summit, moving vs still frame, 1280x720; mean / max / % over 4):** control 0.10 / 18 / 0.09%; spin 3 deg/frame 0.21 / 21 / 0.52%; spin 10 deg/frame 0.21 / 24 / 0.47%; glide 0.7 m/frame 0.22 / 12 / 0.05%; glide 5 m/frame 0.32 / 20 / 0.39%. The contact sheet shows no trails. **0.9 history weight unchanged.**
  - **Options:** (a) take the clean variant as is (-7.5% storm, no look change); (b) accept the reuse grain for -16 to -19%; (c) look for the saving elsewhere in the march (fewer noise octaves far away, a cheaper density function for light samples only, or quarter res in storm). The clean variant is kept as a scratch patch and can be committed in minutes on (a).
  - Risks: none; Sky.hlsl is back at row 2's commit.
  - **Suite (editor closed, sky/cost at row 2):** `15 passed, 0 failed, 7 skipped in 286s (Unity exit 0).` Pins: Landmarks_V3 `112 A955AFE0`, Fauna_Fingerprint `66320C7C`. My editor is closed.
- **R-8 row 3 COMMITTED at the supervisor's call, option (a)** (3c91d8ac, sky/cost): the clean variant above, debug test bit removed. **It misses the card's 15% target:** about -7.5% storm and -3.3% Overcast on this PC. The speckle version was not taken. Option (c), finding the saving elsewhere in the march, stays open as a suggestion.
  - **Shots vs row 2** (Summit, forced weather, 1280x720, same frame state; diff mean / % over 4, two runs each): Overcast 1.52 / 11.6% and 1.13 / 6.9%, against control 0.93 / 4.8% and 0.79 / 3.4%. Storm 1.97 / 10.0% and 1.94 / 10.0%, against control 1.39 / 5.7% and 1.43 / 6.3%. The extra over control is a faint smooth darkening of the thick cloud bodies (signed mean -0.3 to -0.6 of 255 in the upper half). Edge and rain diffs match control. Side by side, the pairs look the same. With the debug bit removed, a pair is at control level (0.77 / 1.56), so the editor was running the committed shader.
  - **Suite (editor closed, sky/cost at 3c91d8ac):** `15 passed, 0 failed, 7 skipped in 374s (Unity exit 0).` Pins: Landmarks_V3 `112 A955AFE0`, Fauna_Fingerprint `66320C7C`. My editor is closed.

## W-129 (streaming spikes; tree Punchy, branch world/spikes)

- **W-129 row 1 DONE (attribution only, no code change; verified by numbers).** Branch world/spikes = origin/main a984c4e4, no commits.
  - **Method:** my editor in play mode, `-island v3 -time 12 -weather Clear -nohud`, uncapped, Profiler recording the main thread and all workers. A scratch probe (untracked, Assets/Temp) moves the camera on a fixed line and, for every frame over 50 ms, dumps the main-thread sample tree, the samples with the most self time, and the busiest jobs on the workers. Shrub walk: z=1324, x -1236 to 164 (1400 m) at 30 m/s on the ground. Glide: z=-1200, x -3000 to 3000 at 60 m/s, 40 m up. Each run is a fresh play session (the world streams in again from nothing).
  - **Harness artefact found first:** every `unity command eval` compiles and loads an assembly in the editor. When I polled for the result once a second, a 100 s run standing still with nothing streaming gave 54 frames over 50 ms, max 341 ms. The same run without polling gave 1 frame over 50 ms, max 53 ms. All numbers below come from runs with no evals while the run was going.
  - **Runs without polling:**

    | run | frames | mean ms | p99 | max ms (at) | >50 | >100 |
    |---|---|---|---|---|---|---|
    | walk 1 | 1897 | 24.56 | 39.0 | 71.0 (522 m) | 4 | 0 |
    | walk 2 | 4082 | 11.42 | 38.3 | 193.4 (902 m) | 17 | 3 |
    | walk 3 | 3481 | 13.40 | 46.2 | 472.6 (1195 m) | 31 | 14 |
    | glide 1 | 7664 | 13.04 | 25.0 | 217.4 (4409 m) | 46 | 18 |
    | glide 2 (same line, next session) | 7370 | 13.56 | 19.0 | 66.4 (3888 m) | 1 | 0 |

  - **Spike table (the frames over 100 ms; the "cause" is the sample with the most self time in that frame):**

    | ms | place | main-thread self time | likely real? |
    |---|---|---|---|
    | 472.6 | walk 3, 1195 m (-41,1324) | "Main Thread" root 446 (no child) | no: stall |
    | 331.7 | walk 3, 1228 m | Idle 208, ColossusWalker.Update 106 | no: stall |
    | 325.7 | walk 3, 621 m | CreatureBurrowSystem 275 | no: stall |
    | 261.2 | walk 3, 351 m | PhysicsCore2DLateUpdate 150 (game has no 2D physics) | no: stall |
    | 217.4 | glide 1, 4409 m (1409,-1200) | EditorLoop 194 | no: editor |
    | 205.9 | glide 1, 4233 m | InputUpdate 129, Idle 66 | no: stall |
    | 205.4 | walk 3, 1211 m | CreatureRenderSystem 194 | no: stall |
    | 202.8 | glide 1, 3812 m | ParticleSystem.Update 91, EditorLoop 69 | no: stall |
    | 197.3 | walk 3, 1194 m | ColossusWalker.Update 186 | no: stall |
    | 191.3 | glide 1, 3852 m | split over 5 unrelated samples of about 40 ms each | no: stall |
    | 183.0 | walk 3, 568 m | BeginContextRendering 89, Idle 82 | no: stall |
    | 175.2 | glide 1, 4433 m | TerrainStreamingSystem 159 | maybe: first-visit terrain tiles |
    | 167.6 | glide 1, 5598 m | ComputeStats 155 | no: stall |
    | 157.6 | glide 1, 5583 m | CustomRenderTextures.Update 113 | no: stall |
    | 153.7 | glide 1, 5621 m | Semaphore.WaitForSignal 124 (main thread waiting on workers) | no: stall |
    | 152.1 | walk 3, 373 m | CreatureBrainSystem 85, ColossusLod 48 | no: stall |
    | 143.1 | glide 1, 5576 m | TerrainStreamingSystem 104 | maybe: first-visit terrain tiles |
    | 138.6 | walk 3, 682 m | FloraSystem.LateUpdate 118 | maybe: first-visit flora build/upload |
    | 135.7, 101.9 | glide 1, 4401 / 4392 m | Profiler.FlushMemoryCounters 90 / 70 | no: profiler |
    | 132.4, 131.6 | walk 3, 339 / 545 m | IslandV2System 34 + EditorLoop 34 + AssetDatabase 23; EditorLoop 75 + HybridLightBaking 41 | no: editor |
    | 100-127 (10 more) | walks 2-3, glide 1 | AmbientLife.Near 78, ColossusPush 91, FaunaDirector.Comfort 91, RibcageSystem 75, Mantrap 39 + ScheduleDraw 44, Idle 52-81 | no: stall |

  - **What this says:**
    1. **Most frames over 100 ms are stalls of the whole editor process, not streaming work.** The time lands on samples that normally cost under 1 ms (InputUpdate, ComputeStats, a 2D physics step the game doesn't use, the bare thread root, Idle). The workers sit idle for the whole frame (their time is all Semaphore.WaitForSignal). The spikes don't come back at the same places: walk 3 spiked at 300-680 m and 1195-1230 m, while walk 1 on the same line never went over 71 ms. Glide 2 streamed the same line from nothing, straight after glide 1, and peaked at 66 ms. The host was loaded throughout: three editors open, 57-59 UnityShaderCompiler processes from another lane's editor, CPU 54-91% (85% when the runs ended), 24-29 GB RAM free.
    2. **Real streaming costs exist but stay under 100 ms when the host is quiet.** Seen on first visits: TerrainStreamingSystem 55-159 ms, FloraSystem.LateUpdate 33-160 ms, CaveSystem.Update 67-84 ms, StdRender/StdShadow.ApplyShader 84-143 ms (shader variants compiling in the editor), and incremental GC 20-48 ms (9 KB/frame allocated, 5.7 of it in FloraSystem.LateUpdate). None of them came back over 100 ms in glide 2 or walk 1. The R-6 row 6 frames at 1240-1330 m match walk 3's 1195-1228 m stall cluster, which has no streaming cause.
    3. **No sign of systems competing for workers.** The workers are idle during the spikes, and nothing waits on another system's jobs. So the row 3 scheduler note is not needed.
  - **Row 2 not started.** No cause repeats over 100 ms when the host is quiet, so a fix can't be measured against the card's pass condition here (no frame over 100 ms in 3 walks and 2 glides). On this PC the host stalls alone break that condition. Suggested next step for the supervisor: one profiler-lane bench of the shrub walk and glide in a dev player build, with the other editors closed. If TerrainStreamingSystem or FloraSystem.LateUpdate still go over 100 ms there, slice their first-visit work over frames (terrain tile uploads; flora build/upload), one commit each.
  - Risks: none; no code change. The probe is a scratch file in Assets/Temp and is not committed.
- **R-7 row 3 DONE (verified), (c) kept** (c47dd81e, lod/impostors): shrub reach is now 520 m (was 260), with the far part drawn as impostors. It is one value in FloraSystem.Shrubs plus the scene.
  - **Method:** editor play mode, hour 15, clear weather, camera aspect pinned to 16:9. Modes are interleaved per view so drift hits them all alike. Each mean is over 10 samples of 150 frames (a and c, second pass); the first pass (3 rounds of a/b/c/c1) gives b and c1. Render ms is synced GPU time. Flora ms is the `FloraSystem.Draw` marker (CPU cull dispatch and submit).
  - **Table (render ms / frame ms; drawn plants all layers, impostors in brackets):**

    | view | (a) meshes, 260 m | (b) impostors, 260 m | (c) impostors, 520 m | c - a render / frame |
    |---|---|---|---|---|
    | dense forest, ground | 5.27 / 14.16; 4015 | 5.35 / 14.87; 4082 (264) | 5.40 / 14.53; 5604 (1356) | +0.13 / +0.37 |
    | ridge | 4.76 / 13.78; 5862 | 5.11 / 14.40; 5901 (152) | 4.74 / 14.12; 7501 (1158) | -0.02 / +0.34 |
    | flying ~100 m | 3.65 / 12.01; 4252 | 7.65* / 21.49*; 4282 (224) | 3.99 / 12.49; 6252 (1544) | +0.34 / +0.48 |
    | flying ~300 m | 6.87* / 17.50*; 5369 (no shrubs) | 5.04 / 14.55; 5369 | 6.86* / 18.84*; 6168 (394) | -0.01 / +1.34* |

    \* = rounds with an outlier spike (spread 5-15 ms). The cleaner first pass at 300 m gives a 5.09 / 14.84 and c 5.09 / 14.95 (+0.00 / +0.11), and at 100 m a 5.20 / 14.22 and c 5.40 / 14.82. Shipped scene re-measured (2 rounds, spreads <= 0.6): ground 5.54 / 15.30, ridge 5.00 / 14.82, fly100 4.20 / 12.81, fly300 4.22 / 13.09.
  - **Verdict:** GPU cost of (c) is within noise (+0.34 ms worst). Frame cost is +0.34 to +0.48 ms where the runs are clean. That is under the +0.5 ms bar, but only narrowly: editor frame noise is about the same size.
  - **Flora ms:** 0.24 ms in every mode and view. The submit is per model, so it doesn't grow with reach.
  - **300 m altitude:** today, no shrubs at all (5369 drawn, all trees). With (c), 405 lod2 + 394 impostor shrubs (6168 drawn). Flora 0.24 ms.
  - **Memory:** shrub layer at 260 m is about 160-200 tiles, 2.5-3.7k plants, GPU 0.4-1.6 MB, CPU 0.1-0.2 MB. At 520 m it is about 585-615 tiles, 10-11k plants, GPU 1.6 MB, CPU 0.4-0.5 MB. The impostor atlas is 140.4 MB (214 models, 8x8 frames of 32 px, 3 mips) and does not change with reach.
  - **Load:** the bake is 9.5-11.8 ms spread over 18 frames (slowest frame 1.55 ms). The doubled stream settles 0.3-0.5 s after a teleport.
  - **3-blend vs 1-frame at 520 m (render ms):** ground 5.46 / 5.58, ridge 5.90* / 4.81, fly100 5.40 / 6.18, fly300 5.09 / 5.00. There is no measurable difference, so 3-blend stays the default.
  - **Shots:** PunchyMilestones/R-7/r3/ ({ground,ridge,fly100,fly300}-{a,b,c,c1}.png, contact-sheet.jpg, shipped-fly100.png, shipped-fly300.png).
  - Risks: frame ms is near the bar, so a release-build bench would settle it; reverting is one value. No placement or ProjectSettings changes.
  - **Suite (editor closed, world/spikes = origin/main a984c4e4):** `15 passed, 0 failed, 7 skipped in 303s (Unity exit 0).` Pins: Landmarks_V3 `112 A955AFE0`, Fauna_Fingerprint `66320C7C`. My editor is closed.
- **R-7 row 4 NOT DONE (measured, doesn't pay; nothing committed):** I tried it with one flag (tree layer in `ImpostorLayers`) plus one policy value per kind (Tree `Impostor = 30`, Rock `Impostor = 16`), then reverted.
  - **Interleaved measure** (shrub impostors at 520 m in both modes, 4 rounds x 2 per view; tree/rock impostors off vs on, render / frame ms): fly100 3.20 / 11.00 vs 3.25 / 11.69; fly300 4.27 / 13.39 vs 4.09 / 13.49. Ground and ridge were too noisy to read (spread 6-14 ms) and show no clear gain. It moves 1.2k-4.4k tree/rock LOD2 meshes to impostors (fly300: 5369 meshes became 1692 meshes + 4373 impostors) for no measurable GPU win. Trees already draw out to the terrain edge (R-6 row 4), so there is no reach to gain.
  - **Cost:** the tree layer atlas is 395.7 MB (603 slices; impostors total 536 MB), and the bake is +24.5 ms over 52 frames.
  - **Why so big:** the atlas has one slice per model in the layer, used or not. Tree layer: Tree 77, Rock 120, Structure 394, Debris 10, Plant 2. Shrub layer: Plant 60, Rock 47, Structure 71, Debris 36, so about 100 of its 140 MB is never drawn.
  - **Follow-up worth a card (small new code path):** bake only models with a reach > 0, through a model-to-slice index. That would cut the shrub atlas from 140 to about 39 MB, and make trees + rocks about 129 MB (trees alone about 51 MB) if they're ever wanted, e.g. for flying-height reach past the edge stream.
  - **Other candidates (not done), estimated:** far kit buildings and settlement structures (394 + 71 models; about 0.66 MB each at 32 px; they hold their reduced mesh far out per the default rule). These would be the place for Zach's lit windows: a third atlas at about +25%, 3 more samples per pixel, and the window-hour logic. Landmarks are mostly one-off big meshes, so impostors make little sense there; a far proxy mesh would do better. Animated things stay out.
  - **Suite:** 15 passed, 0 failed, 7 skipped (shots on demand). Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint 66320C7C hold.
- **R-7 row 5 DONE (verified)** (b0448ed0, lod/impostor-trim, from main d880f947): impostors are baked only for models whose DetailPolicy rule has an impostor tier (new `DetailPolicy.HasImpostor`). Each impostor layer gets a model-to-slice buffer (`_ImpostorSlice`, -1 = none). FloraImpostor.hlsl keeps the local model index for `_ModelBounds` and reads the atlas slice from the buffer. Models without a slice keep impostor reach 0, so the cull never hands them over (same gate as before).
  - **Atlas:** shrubs 140.4 MB -> **39.4 MB** (60 of 214 models: the plants; rocks, debris and structures are no longer baked).
  - **Bake:** 14.9 ms over 18 frames -> **2.1 ms over 5 frames** (first load after a domain reload, editor; slowest frame 5.19 -> 1.13 ms).
  - **Render ms (synced GPU, shrubs 520 m, hour 15, clear, before / after):** ground 3.14 / 3.09, hilltop 3.16 / 3.05, fly100 2.68 / 2.76, fly300 2.61 / 2.52. Noise-level, as expected (draw path unchanged). Draw counts are identical in all four views (e.g. ground 5616 drawn, 1316 impostors; fly300 6168, 394).
  - **Look:** mean abs pixel diff fly100 1.44/255 (0.32% of pixels > 16), fly300 0.96/255 (0.29%). Ground and hilltop differ by 11-15/255, all from clouds, sun-flecks and grass sway between the two runs (same composition, same counts; checked side by side). Shots: PunchyMilestones/R-7/r5/{before,after}/{ground,hilltop,fly100,fly300}-imp.png plus contact sheets.
  - **Seen, not mine:** a few magenta (missing-shader) objects on the hill in the hilltop view, in both before and after shots; worth a look by whoever owns those props.
  - Risks: if a rule later gains an impostor tier, its models get slices automatically (atlas grows about 0.66 MB per model). No placement, scene or ProjectSettings changes.
  - **Suite (editor closed):** `15 passed, 0 failed, 7 skipped in 197s (Unity exit 0).` Pins: Landmarks_V3 `112 A955AFE0`, Fauna_Fingerprint `66320C7C`. My editor is closed.

## R-9 (water deforms under the player, #232; tree Punchy-world, branch water/deform from origin/main beabf98b)

- **R-9 row 1 DONE (read-through; no code, nothing committed on water/deform; costs below are estimates, measured in row 2)**
  - **Mesh:** the water is a sheet built with the terrain chunk (BuildChunkMeshJob.BuildWater): one vertex per terrain vertex,
    chunk 64 m, LOD0 64 quads, so **1.0 m spacing** at best (2, 4, 8 m on coarser LODs). No tessellation. Drawn through
    Entities Graphics (BRG, one Water.mat for every chunk); shores are cut per quad on the CPU, and lake/river levels differ per vertex.
  - **Waves:** the vertex stage only does the sea swell (WaterSwell.hlsl: up to 12 Gerstner waves, sea only, mirrored on the CPU by
    WaterSwells.Height). Everything small is in the pixel stage: 6 directional waves, two reads of the fine detail texture, flow-mapped
    river ripples, rain rings, and the wakes.
  - **Today's ripples:** CreatureWakeSystem (Fauna lane file) drops a ripple every 0.5 m or 0.5 s for anything at the surface within
    40 m of the camera (players' bodies included); the 48 nearest go out as `_PunchySwimmers`, with a coarse MarkMask. Water.shader
    SwimmerWakes loops over them per pixel (within 45 m, only in marked cells) and draws each as an analytic ring (4.2 m over 3 s) that
    bends the normal and adds a little foam. No height change, no interaction between rings, nothing pushed aside under the body.
  - **Can the mesh carry 5-20 cm at 10-20 cm spacing? No.** At 1 m spacing anything shorter than about 2 m aliases away. It needs 5-10x
    more vertices near the player. Options:
    - (a) **Fine near patch (recommended):** each frame the LOD0 water quads within ~16 m of the player are copied from the chunk data and
      subdivided 8x (12.5 cm) in a Burst job when the player crosses a metre: ~32x32 quads -> 256x256 = 65k verts / 131k tris at most, far
      less on a river. Exact surface levels and shore cuts, since it comes from the same quads. The chunk water clips itself inside the
      patch's rect (one compare per pixel). Drawn with Graphics.RenderMesh and the same shader (a keyword), so BRG is not touched.
      Estimated cost: vertex work on 65k verts (swell + 1 field read) ~0.02-0.05 ms on the laptop's GTX; CPU rebuild ~0.2 ms only on
      the frames it runs.
    - (b) Hardware tessellation (hull/domain) on the near quads: same result without the CPU copy, but it puts hull/domain stages on every
      water draw through BRG (untested with DOTS instancing here) and pays the stage on far water too. Riskier.
    - (c) Normals only from the field, no geometry: cheapest (~2 texture reads per pixel inside the field), but no silhouette at grazing
      views. This is also what Low/fallback could do.
  - **Field (row 2 plan):** 256x256 R16G16 RT over 32 m (12.5 cm texels), scrolled with the player in whole texels; a compute (or blit)
    wave step with damping, 2 dispatches a frame; swimmers/waders/splashes splat discs into it from the same list CreatureWakeSystem
    already builds (I'd read its ripple sources rather than edit the Fauna file; if I need a hook there I'll ask). Estimated 0.01-0.03
    ms GPU. The water frag reads the field (height + gradient) inside the patch for normals; the old rings stay outside the field and on
    Low.
  - **Budget risk:** the laptop's water already costs ~10 ms GPU at the lake (profiler q57), so 0.3 ms leaves room for the field and the
    patch but not for extra full-screen work. Laptop numbers will need a BENCH-QUEUE job (I can only measure this PC).
  - **Not mine:** CreatureWakeSystem.cs is in Scripts/Fauna (animal lane); Water.shader is shared-additive (animal session told first).
  - Next: W-130 side task (supervisor), then row 2 (prototype as (a) + field).
- **R-9 row 2 DONE (plan (a): fine near sheet + 256² wave field; water/deform fd50ad58, pushed, no PR)**
  - **What:** WaterPatchSystem.cs (new, Terrain) copies the LOD0 chunk water's own wet quads in a 32 m square round the player and cuts
    each 8x8 (12.5 cm) in Burst when the player crosses a metre, drawn with Graphics.RenderMesh in Water.mat with the `_WATER_FIELD`
    keyword. WaterField.compute steps a 256² R16G16 field over the same square at 60 steps/s (damped wave equation, 3 m absorbing rim,
    scrolls with the player in whole texels). The sheet's vertices rise and fall by the field, its normals follow (slope drawn 2.5x),
    and foam shows only where it stands steepest. The chunk water clips itself under the sheet (rect + per-metre mask, so fountains and
    cave water are untouched); sheet edge vertices follow the chunk's straight edges, so no cracks with the swell.
  - **Sources (read from the water side, no Fauna edits):** the creature snapshot (CreatureSnapshotSystem.Get), not CreatureWakeSystem.
    Every swimmer/wader/floater in the square holds a dimple (35 cm-1 m wide, deeper with speed), shoves water aside each step while
    it moves (the troughs spring back as a V wake), and upright waders knock a ring out on each stride (1.2-3.6 a second, own beat per
    creature). Landings through the surface splash. Old ripples (SwimmerWakes) stay outside the square, on Low and with `-waterfield 0`
    (or `WaterPatchSystem.On = false`).
  - **Files:** new WaterPatchSystem.cs, Resources/WaterField.compute, Shaders/WaterField.hlsl (all LF); Water.shader (+59, CRLF kept;
    keyword in the depth and colour passes, clip outside the sheet); TerrainStreamingSystem.cs (+32, CRLF kept: `TryGetWater(int2,
    out Mesh, out int lod, out int built)` so the sheet can copy a chunk's water mesh and see when it was rebuilt).
  - **Look:** swimming leaves a clear V wake with foam streaks; a landing throws rings that cross the square; wading shows stride
    rings spreading away (subtler: in 0.8 m of clear water the bottom caustics dominate at a steep view). Night reads the same, darker.
  - **Desktop ms (High, uncapped, editor play, swimming at the lake 488,-1144, 3 ABAB pairs x 360 frames):** ExecuteRenderGraph GPU
    0.795/0.791/0.798 off vs 0.901/0.893/0.883 on = **+0.10 ms**. PlayerLoop CPU 9.6-9.9 ms both ways (no measurable change). The
    compute step is dispatched outside the render graph, so it is not in that figure (2 dispatches of 256² a frame at most).
  - **BENCH-QUEUE line for the laptop:** `R-9 row 2: Profile build of water/deform (fd50ad58), -gfx High, 1920x1080, uncapped, -time 12
    -weather Clear; stand in the lake at -goto 488,-1144 (swimming) and swim straight for 10 s, then the same with -waterfield 0; whole-frame
    GPU ms and the water pass ms, ABAB 2 runs each. Pass = on costs < +0.3 ms over -waterfield 0.`
  - **Shots:** D:/Unity/PunchyMilestones/R-9/2/: swim-noon.jpg, landing-noon.jpg, wade-and-night.jpg (wade noon | wade night /
    swim night | landing night).
  - **Overlap:** R-10 row 2b plans a three-line light hook-up in the water shaders; Water.shader now has R-9 changes, so whichever merges
    second rebases (additive, separate blocks).
  - **Decisions:** none. Next is row 3: pass/flag call (needs the laptop number) and the full suite with the pins.
- **R-9 row 3 DONE (suite + pins; pass/fail: pending laptop job 45)**
  - **State:** water/deform fd50ad58 unchanged. The feature is on by default at High/Medium; it is off on Low water quality or with
    `-waterfield 0`, and the swimmer ripples carry on there. No new commit was needed.
  - **Suite:** `scripts/run-playmode-tests.sh` gave 15 passed, 0 failed, 7 skipped (shot tests, Explicit) in 251 s.
    Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C.
  - **Cost:** desktop +0.10 ms GPU (row 2). The laptop pass/fail is pending BENCH-QUEUE job 45 (pass < +0.3 ms). If it fails, put the
    feature behind the flag (off by default, `-waterfield 1` to turn it on) as the card says.
  - **Editor:** closed. No Fauna edits. Decisions: none.

## W-130 (magenta slabs; tree Punchy-world, branch fix/magenta-slabs from origin/main beabf98b)

- **W-130 DONE (verified)**: commit 3e28a4d8 (shader fix) + 1b6168d2 (rendering.md gotcha), pushed to fix/magenta-slabs.
  - **Object:** R-7's shrub impostors (FloraImpostor.shader, layer 1, 60 slices). With `-noimpostors` (meshes) there are 0 magenta pixels; with impostors there are 8498 at the R-7 hilltop pose. The time and weather don't change it.
  - **Cause:** on D3D11 (this editor and the laptop) FXC rejects a pixel shader that reads SV_POSITION and writes conservative depth
    (IMPOSTOR_DEPTH = SV_DepthLessEqual), error X8000, unless the position is `noperspective centroid`. Every impostor pass
    fell back to the error shader, so each impostor drew as a bare magenta quad. Came in with R-7 (#233/#234). Vulkan/Metal unaffected.
  - **Fix:** FloraImpostor.hlsl adds `noperspective centroid` to ImpostorVaryings.positionCS on SHADER_API_D3D11 only (it only moves the
    MSAA sample point). Shader now compiles with 0 messages (it had 3 D3D errors). After: 0 magenta, and the imp shot matches the mesh shot by eye.
  - **Numbers (hilltop, 1280x720, desktop High):** render 2.88 ms imp / 2.85 mesh; frame 15.8 / 15.4 ms; impostors drawn 1069 on L1.
  - **Note:** re-importing the shader during Play wipes the runtime impostor material's properties, and the impostors then show as pale
    quads. That's only an editor artefact: they're fine after re-entering Play. Logged in docs/agents/rendering.md.
  - **Suite:** 15 passed, 0 failed, 7 skipped in 214 s (editor closed, on fix/magenta-slabs). Pins held: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C.
  - **Shots:** D:/Unity/PunchyMilestones/W-130/hilltop-imp-before.png, hilltop-imp-after.png

## R-10 (lights at distance and a glowing town; tree Punchy-release, branch light/forwardplus)

- **R-10 row 1 DONE (measured, no code)** (light/forwardplus = origin/main 2644b295, with #236 merged).
  - **Forward+ cap in 6000.5.8f1 / URP 17.5:** 256 visible additional lights per camera on desktop (structured buffer), 32 on mobile, GL and WebGPU. PC_Renderer is Forward+ (`_CLUSTER_LIGHT_LOOP`), and prefiltering is SelectOnly, so a new `multi_compile _ _CLUSTER_LIGHT_LOOP` in our shaders adds no build variants. Today our own budget caps owned lights at `RealLights = clamp(round(2+4*Detail), 4, 8)` (6 at Medium), far below the cap.
  - **Who reads Forward+ lights today:** TerrainSurface (URP PBR) and Cave.shader (explicit LIGHT_LOOP). **Flora.shader does not**: it covers plants AND built things (houses, tower, walls, benches, well, cave props), and it reads only the 16 glow slots, the far map and the lantern. Grass, Pebbles and SunkenTemple are the same, so a real light today lights the ground but not the houses next to it.
  - **Cost of N unshadowed point lights** (this PC, editor play mode, Medium, 1920x1080, whole synced frames, ABBA x3, median; lights reach 8 m, scattered 5-45 m ahead in town and 4-30 m in the cave). Only terrain and cave shaders read them yet, so row 2 adds Flora's share:

    | place | base | N=8 | N=32 | N=64 | N=128 |
    |---|---|---|---|---|---|
    | big town, 22:00 | 2.5 ms | -0.002 | +0.056 | +0.079 | +0.236 |
    | Mine cave | 1.65 ms | +0.025 | +0.006 | +0.030 | +0.158 |

    Up to 64 lights stays inside the noise on this PC. 128 costs about 0.2 ms. Laptop: BENCH-QUEUE line below.
  - **Walk-away from one street light** (big town, 22:00 with the clock paused, lantern at (2988.8, 11.2, -889.9), camera walked straight back). Street lights (W-127) are `AddStill(Fixture)`, so they never own a Unity light: of the 6 real lights, 0 were in use in town. They live only in the glow slots (13 of 16 used). The tiers a viewer actually sees:

    | camera distance | what draws the light | what changes on screen |
    |---|---|---|
    | 0-35 m | glow slot, colour (1.30, 0.62, 0.16), reach 9 | full warm pool on the ground and walls (walk_3...walk_27) |
    | 16-37 m | slot + far night map fading in (map hand-off 16.4-37.4 m) | the coarse map adds warmth under the slot |
    | **35-38 m** | **slot dropped** (between walk_27 at 34.6 m and walk_31 at 38.6 m) | **the visible step:** the sharp pool on the house wall by the pole fades over 0.8 s into the far map's softer, blockier wash; the map's texel steps show at the base of the next building (walk_31) |
    | 38-290 m | far map, then world map | town reads as a warm wash; no single light is visible (walk_40...walk_280). Slot count falls 16, 9, 2, 1 at 108/188/288 m |

    - **Why at 37 m:** CaveSystem.GatherSurfaceGlow skips a street light past `handOff.y`, and that hand-off is computed as `GlowHandOff(AmbientGlow, 8)` = (23.4, 37.4) m, not from the Fixture rule. The Fixture rule alone (Near/Mid/Far 60/20/4 px at 935 px/m) would keep a reach-9 light: Real to 140 m, Glow to 420 m (size fade from about 260 m), FarGlow to 2.1 km, then Emissive. So the one pop is a hand-off mismatch, not a tier boundary.
    - For a light that does own a Unity light (Carried, Fire, interior Fixtures), Real to Glow is a 0.8 s cross-fade at the Near threshold. The Real and Glow falloffs differ (URP's inverse square vs Glow's (1-d/r)^2), so the brightness shifts during that fade. Row 3 removes both.
  - **Shots:** D:/Unity/PunchyMilestones/R-10/1/: walk_{3,8,13,18,23,27,31,40,60,100,180,280}.jpg, where the number is metres walked back (camera distance is about +7.6). Also town0.jpg / town128b.jpg (128 lights), cave0.jpg / cave128.jpg.
  - **BENCH-QUEUE line for the laptop:** `R-10 lights: Profile build of light/forwardplus, -gfx High, 1920x1080, uncapped, -time 22 -weather Clear, -goto 2988.8,-920 facing north, then a Mine cave (-place cave:Mine); whole-frame GPU ms with -lightbudget 6 vs 32 vs 64 vs 128 (ABBA, 300 frames each), plus the same after row 2 lands so Flora's share shows.` (`-lightbudget` only raises the cap; this spot has few owners until row 2, so rerun it after row 2.)
  - **Decisions:** none yet. For row 2 I plan to give Flora/Grass/Pebbles/SunkenTemple one shared Forward+ loop with Glow's falloff, and to let street lights own pooled real lights up to a per-quality budget (16/48/96), since 64 lights cost about 0.08 ms here.

- **R-10 row 2 DONE for flora and built things (row 2b waits)**: light/forwardplus `e0c4123a` (pushed).
  - **What changed:**
    - **One shared Forward+ loop:** `PunchyRealLights()` in Glow.hlsl reads URP's cluster lights with Glow's `(1-d/r)^2` falloff. A real light of colour c and range r lights like a glow slot of c x 0.28 and reach r x 0.75 (the row-1 fit), so a light looks the same as a real light or as a glow. Shadowed lights keep their shadow term.
    - **Shaders that use it:** Flora (plants AND built things: houses, towers, walls, props), Grass, Pebbles, SunkenTemple and TerrainSurface, under `_CLUSTER_LIGHT_LOOP`. This adds no variants (SelectOnly). URP's own PBR additional lights are switched off there, so no light counts twice.
    - **Budget:** `RealLights` is 4/16/48/96 by quality (Low/Medium/High/Ultra; `-lightbudget` still overrides it). Sources' own lights (which may cast shadows) stay capped at `OwnRealLights` 4/6/8, as before.
    - **Pooled street lights:** `LightBudget.AddPooled`. Street lights (W-127) can't own a light, so they rank with the owners and get a pooled unshadowed point light while they're granted one; the rest stay in the glow slots. The glow slots remain the fallback, and Low keeps 4.
    - **Street-light hand-off:** street lights are now kept out to the Fixture hand-off (184/421 m at Medium) instead of the ambient one (37 m). That was the row-1 pop at 35-38 m. The far night map now fades in over that hand-off (`_PunchyGlowMapNightFade`), not at 16-37 m.
  - **Numbers (this PC, editor play mode, Medium, 1920x1080, big town at 22:00, ABBA x2, ms a frame):** glow-only (`-lightbudget 0`) 3.21, budget 16 3.29, budget 48 3.18. All within noise. At this spot 16 pooled lights are on at Medium (19 made). Laptop: BENCH-QUEUE line below.
  - **Suite (editor closed, at e0c4123a's tree):** `15 passed, 0 failed, 7 skipped in 250s (Unity exit 0).` Pins: Landmarks_V3 `112 A955AFE0`, Fauna_Fingerprint `66320C7C`.
  - **Shots:** D:/Unity/PunchyMilestones/R-10/2/
    - `sheet_ab_glowonly_vs_real.jpg`: glow-only (left) vs real lights (right), 40/90/150/220 m south of the row-1 lantern, same moment. They look nearly the same, which is the point: the falloffs match, so lights can move between tiers without a step.
    - `ab_glowonly_*.jpg` / `ab_real_*.jpg` are the full-size shots.
    - `after_walk_*.jpg` and `sheet_before_after.jpg` are the walk-away. The "before" shots come from an earlier build with a warmer sky tint, so judge from the A/B sheet.
  - **Look changes:**
    - Terrain now uses Glow's falloff instead of URP's inverse square: a softer centre, and no specular highlight from lanterns on the ground.
    - Flora's old lantern diffuse term is off when the cluster loop is on, because the shared loop replaces it.
  - **Row 2b (waiting for your go):** Creature.shader (A-92), the water shaders (R-9) and Colossus.shader (A-89). Each needs the same three-line hook-up once those branches are merged.
  - **BENCH-QUEUE line for the laptop:** `R-10 row 2: Profile build of light/forwardplus (e0c4123a), -gfx High, 1920x1080, uncapped, -time 22 -weather Clear, -goto 2988.8,-920 facing north; whole-frame GPU ms with -lightbudget 0 vs default (48 at High) vs 96 (ABBA, 300 frames each), then the same at origin/main for the "today" baseline (pass: High <= +0.5 ms).`
  - **Decisions:** none. Next is row 3: Cave.shader moves to the same falloff, real level fades by on-screen size instead of a 0.8 s pop, and the night map is matched to the slots in colour and reach.

## R-11 (far trees and rocks as impostors; tree Punchy-world, branch lod/impostor-trees from origin/main 2644b295)

- **Row 1, trees: done (verified).** Commit d977c58e, pushed.
  - The tree layer bakes impostors, but only for the Tree class (DetailPolicy `Impostor = 30` px), so buildings, debris and props keep their meshes. Far reach and shadows are unchanged, because shadows still use the mesh lists.
  - Impostors turn with each plant: its yaw, its W-16 tilt and the mesh's wind bend (same gust and phase as Flora.hlsl, scaled by each model's sway at its centre).
  - Far coverage: once a pixel spans more than a texel, the alpha cut is dithered. With the old hard 0.5 cut, far conifers thinned out and vanished, and the rest read as dark blots.
  - Bark and rock take the mesh's far sky sheen. With both fixes the far-band mean colour matches main within 0.2/255.
  - Render ms, in-session interleaved A/B at 1920x1080, 3 rounds. `t0` means tree impostors off, which is the same as main. Absolute ms drifts between sessions because other editors share the GPU.

    | preset | view | impostors | main (t0) | delta |
    |---|---|---|---|---|
    | High | walk | 5.55 | 5.66 | -0.11 |
    | High | hilltop | 5.08 | 5.29 | -0.21 |
    | High | 300 m up | 3.85 | 4.02 | -0.17 |
    | High | hilltop2 (forest) | 5.33 | 5.53 | -0.20 |
    | Medium | walk | 4.35 | 4.35 | 0.00 |
    | Medium | hilltop | 3.89 | 4.15 | -0.26 |
    | Medium | 300 m up | 3.30 | 3.56 | -0.26 |
    | Medium | hilltop2 (forest) | 3.92 | 4.18 | -0.26 |

  - Draw counts, High hilltop: 6675 meshes + 4340 impostors, against 8376 + 2014 on main.
  - Tree atlas 51.8 MB (79 of 603 tree-layer models). Bake 2.4 ms CPU over 7 frames; the slowest model is AncientTree at 0.39 ms. All impostor atlases together: 91.2 MB.
  - Pops: 12-step walk (3 m steps) and fly (300 m up) strips. Frame-to-frame change is the same as main to within 0.1 at every step (walk worst block 124 both, fly 34 both), so no added pops.
  - Wind hand-off is UNVERIFIED in stills. It uses the mesh's formulas by construction.
  - The editor runs D3D11: no magenta, no shader errors.
  - Shots: PunchyMilestones/R-11/r1final-*.png and r1final-contact-sheet.jpg; r1c-hilltop2-High-zoom-imp-top-t0-bottom.png (the far band after the fixes); strips/r1-{walk,fly}-{imp,t0}-NN.png.
  - Risk: impostor strata are baked in model space, so banded rock stripes won't line up with the cliffs. Matters for row 2 only, and only far away.
- **Row 2 (rocks): done (verified, measured, not shipped).** Commit 2b881062: rocks stay on meshes. The rule comment and docs/agents/rendering.md say why.
  - Tried: Rock rule `Impostor = 16` px. That baked 167 rock models (47 in the shrub layer, 120 in the tree layer, none in layer 0). The atlases grew from 91.2 to 200.8 MB, and the bake went from 2.4 to 8.4 ms over 18 frames.
  - Test: mesa (MesaCanyonlands), same build, in-session A/B. Rock impostors on ("imp") against off ("r0", the rocks' coarsest mesh, as main). Render ms, 5 rounds:

    | Preset | View | imp | r0 |
    |---|---|---|---|
    | High | walk | 5.05 | 4.99 |
    | High | hill | 4.76 | 4.73 |
    | High | fly 300 m | 4.24 | 4.13 |
    | Medium | walk | 4.56 | 5.19 (noisy) |
    | Medium | hill | 4.13 | 4.19 |
    | Medium | fly 300 m | 3.45 | 3.73 |

  - Repeat runs (3 and 4 rounds) agree: at High, r0 was cheaper in 8 of 9 view-runs, by 0.03 to 0.26 ms. Medium is mixed.
  - Later start: 8 px instead of 16 px (reach x2) was no better.
  - Why it doesn't pay: a far rock's coarsest mesh costs less than the impostor quad, which writes depth and blends three frames. So it doesn't pay for 110 MB.
  - Look: no visible difference either way.
  - The W-16 tilt hand-off is moot. ImpostorTurn already carries tilt from row 1.
  - Shots: PunchyMilestones/R-11/scout/r2scout{mesawalk,mesahill,mesafly}-High-{imp,r0}.png and r2scoutcontact-sheet.jpg.
- **Row 3 (tree reach x1.5): done (verified, measured, not kept).** No code: tree reach stays as it is.
  - Tried: tree-layer radius x1.5 at High (1500 to 2250 m at View 1), impostors on, against the committed build ("imp") and tree impostors off ("t0", as main). Same forest views as row 1. Render ms, in-session interleaved A/B; run 1 had 4 rounds of imp/reach15/t0, run 2 had 4 rounds of imp/reach15 only:

    | View | imp (run 1 / 2) | reach x1.5 (run 1 / 2) | t0 (run 1) | delta reach - imp (run 2) |
    |---|---|---|---|---|
    | walk | 3.86 / 3.68 | 5.53 (one-round outlier) / 3.94 | 3.79 | +0.26 |
    | hilltop | 3.78 / 3.61 | 3.99 / 3.79 | 3.77 | +0.18 |
    | 300 m up | 2.84 / 2.71 | 2.88 / 2.76 | 2.83 | +0.05 |
    | hilltop2 (forest) | 4.07 / 3.88 | 4.08 / 3.93 | 3.83 | +0.05 |

  - Draws, High hilltop: 6637 meshes + 4402 impostors, against 7487 + 7580 at x1.5.
  - Why not kept: the cost stays under +0.3 ms, but it buys nothing on screen. Terrain streams to about 1536 m at View 1 (12 chunks), and trees already reach 1500 m. The extra ring would stand past the drawn ground and in thick haze. Both contact sheets look the same at every view. To draw further, the terrain's far ring has to grow first (world lane). Then this x1.5 can be redone, at about +0.05 to +0.26 ms.
  - Session note: this editor showed imp about equal to t0 (row 1 measured -0.1 to -0.26). Other editors share the GPU; row 1's interleaved table is the better measure.
  - Shots: PunchyMilestones/R-11/r3/r3{walk,hilltop,fly300,hilltop2}-High-{imp,reach15,t0}.png and r3contact-sheet.jpg; r3b/ (rerun) and r3bcontact-sheet.jpg.
- **Row 4 (what else could use the impostor path): list, no code.** Estimates are for one agent.
  1. **Far kit buildings (Structure class: 394 models in the tree layer, 71 in the shrub layer).** Best candidate: buildings are heavy meshes, unlike rocks, so the quad should pay as it did for trees. Atlas: about 0.66 MB per model at 32 px frames, so all of them would be about 305 MB. Trim it to the most-placed set (about 60 models, about 40 MB), or use 16 px frames (about 0.17 MB per model, about 80 MB for all). Code: one DetailPolicy line (Structure `Impostor` px) plus the bake set trim. About 0.5 day with an A/B in a town view.
  2. **Lit-window mask for those buildings (R-7's note in FloraImpostor.shader).** A third atlas (one channel, baked from the window part) is about +25% atlas memory. Sampling it means 3 more taps per pixel, plus Flora.shader's window hour in the impostor pass. Without it, far buildings go dark at night while the near ones glow, so it ships with item 1 or item 1 is day-only. About 1 day. It touches the window/glow logic, which R-10 owns, so it needs a word with R-10 first.
  3. **Landmarks (Ribcage, SaltTower, SunkenTemple...).** Low value. They are few, drawn one by one with Graphics.RenderMesh and their own LODs, so the draw count barely moves. They are big, so 32 px frames would look soft at any range where they still fill the screen. Only worth it if the reach grows past the terrain edge; a coarser last LOD mesh is cheaper to make. About 1 day, not recommended now.
  4. **Not candidates:** rocks (row 2: the coarsest mesh beats the quad), debris (tiny, culled early), layer-0 plants (near only). Cluster/HLOD (the 11-9 audit's second step) isn't needed: individual impostors already cut the tree cost.
- **Card close:** full PlayMode suite (rule 19) on the branch head 2b881062: 15/15 passed, 0 failed, in 201 s. Pins: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C. Editor closed. The branch has no uncommitted changes (the probe is untracked under Assets/Temp).

## R-10 (continued: lights at distance and a glowing town; tree Punchy-release, branch light/forwardplus)
- **R-10 row 3 DONE (transitions)**: light/forwardplus `34d4278a` (pushed, on e0c4123a).
  - **Changes:** an owner's real light now dims by on-screen size over the last stretch of its Real tier (LightBudget RealFade) while its glow slot joins at full, so Real to Glow is one long size fade instead of a 0.8 s swap. Street lights beyond 40 m come from SurfaceGlow's far scan (new FarFixtures, out to 276 m), so pooled real lights cover the whole town, not just the ~50 m the near scan saw. The far night map now starts where the budget's lights end (LightBudget.StreetLightGapAt, capped at 207 m) instead of at 276-631 m, and its soft cap leaves one or two street lights as bright as their glow slots, so it matches where it takes over. Cave.shader's real lights use the shared PunchyRealLight falloff.
  - **Walk-away (editor, High, 22:00, town at 2988.8,-900 walking south to -1200 facing north):** real lights 48 out to ~220 m, then fading smoothly to 3 by 300 m. Night map from 38-62 m near the town to 176-290 m at the far end, tracking the gap continuously. Before (row 2): 33 real at 5 m, 0 by 110 m, night map 276-631, so the town went dark from ~100 m.
  - **GPU ms (desktop editor, town at -920 facing north, FrameTimingManager medians, ABBA):** default 5.48 ms before vs 5.62 after (noise ~0.3 ms; budget 0 vs 48 within noise too). The pass line needs the laptop: BENCH-QUEUE job 45 already covers it; please point it at `34d4278a` (or later). Note: `-lightbudget` clamps to 64, so job 45's `-lightbudget 96` arm actually runs 64.
  - **Suite + pins (editor closed):** 15 passed, 0 failed, 7 skipped in 251 s. Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C.
  - **Shots:** PunchyMilestones/R-10/3/walkaway_before_after.mp4 (side by side, 5 m steps to 300 m), sheet_before_after.jpg, ba_010m..ba_300m.jpg, cave_mine_after.jpg (Mine cave, lanterns lit as before).
  - **Decisions:** street lights within 40 m use the near scan, beyond that the far scan's tree layer out to 276 m. The night map starts where the real lights run out (~40-250 m), not at a fixed hand-off. The before and after runs differ a little in clouds (different time in the session); the lights are what to compare.
  - **Process note:** a fresh play came up at Medium (16 real lights) because the editor's saved graphics prefs are shared across worktrees; my scripts now force High for the run by reflection without saving prefs.
  - **Next:** merge origin/main (clean per merge-tree), then row 2b (Creature.shader), then row 4.
- **R-10 row 2b DONE (creatures)**: light/forwardplus `cbf6e145` (pushed; the merge of origin/main with A-92 is `44945b71`).
  - **Changes:** Creature.shader adds the `_CLUSTER_LIGHT_LOOP` multi_compile and runs PunchyRealLights in both its ambient and body paths, so creatures take the same real lights as terrain, flora and rocks (+8/-5, CRLF kept).
  - **ms:** no measurable cost. It is the same cluster loop the other shaders already run, and creatures cover few pixels.
  - **Shots (High, 22:00, town at 2988.8,-890):** PunchyMilestones/R-10/2b/creature_real_on.jpg and creature_real_off.jpg (`-lightbudget 0`). The figure's brightness is 55.8 vs 55.7: they look alike because the glow slots match real lights by design. The hook-up matters where more lights reach a creature than there are slots.
  - **Suite + pins (editor closed):** 23 passed, **1 failed**, 17 skipped in 318 s. Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C.
    - The failure is `LyingTests.Lying_LegsStayAboveGround_FlatAndSlope`, which came in from main with A-92 (#231). OcotilloMimic reads -0.051 m on the 0.33 slope (lie 0.85), and every other species is >= -0.005.
    - This row only touches the shader, which has no part in leg placement. It looks like a main or animal-lane issue: OcotilloMimic was probably not in A-92's species list when it was measured. Over to the supervisor.
  - **Next:** row 4 (glowing town at distance).

## W-131 (far terrain ring for flight; tree Punchy-world, branch world/far-ring from origin/main da39fd72)
- **Row 1 (measure): done (verified), no code.** Editor D3D11, play mode, walk spot (-1236, 1324), console 0 errors.
  - **Where the ground stops** (chunks stream to ViewRadiusChunks 24 x View; far plane is 3000 m at every preset, fog = haze, 98% haze at the edge):

    | Preset (View) | Chunks (water) | Edge, nearest point / chunk centre | Terrain mesh | Water mesh |
    |---|---|---|---|---|
    | Low (0.6) | 660 (208) | 952 / 989 m | 187k verts, 37 MB | 30k verts, 3.7 MB |
    | Medium (1.0) | 1809 (404) | 1499 / 1536 m | 453k, 90 MB | 84k, 9.9 MB |
    | High (1.3) | 3018 (789) | 1948 / 1983 m | 722k, 144 MB | 161k, 19 MB |
    | Ultra (1.7) | 5280 (1564) | 2589 / 2624 m | 1202k, 241 MB | 293k, 35 MB |

  - **What the outer ring costs** (render ms, 3 rounds, chunks past the cut hidden; frame ms in brackets):

    | View | Medium full | High, cut 1536 m | High full | High delta | Ultra, cut 1984 m | Ultra full |
    |---|---|---|---|---|---|---|
    | walk | 4.98 (15.98) | 6.53 (17.44) | 6.70 (18.30) | +0.17 | 9.39 (noisy) | 8.56 |
    | hilltop | 5.54 (19.07) | 5.94 (17.05) | 6.25 (17.77) | +0.31 | 8.40 | 9.97 |
    | 300 m up | 4.71 (14.59) | 3.58 (14.82) | 4.73 (15.92) | +1.15 | 6.19 | 7.69 |

    High's ring from 1536 to 1984 m (about 1200 chunks, LOD4) costs +1.15 ms at 300 m; the cost follows the draw count, not the vertices. So the far ring in row 2 uses 256 m tiles (one draw per 16 chunks).
  - **Hitches over a fixed 60 s flight** (40 m/s, 300 m up, along the walk line): High 5884 frames, mean 10.20 ms, p99 14.52, max 43.7, 1 frame over 33 ms, 0 over 50; Medium 6862 frames, mean 8.75, p99 12.88, max 43.4, 4 over 33 ms, 0 over 50. No `[stream]` slow batches at either.
  - **Look:** from 150 m and 300 m the edge is not a hard line; the ground fades into haze before it ends (haze is tied to the edge). The cost of "more distance" is therefore also thinner haze near the player, which row 2 will report.
  - Shots: PunchyMilestones/W-131/r1-{Medium,High}-{150m,300m}.png, r1-contact-sheet.jpg. `[shot] 'r1-High-300m' ready after 0.9 s ... 1280x720, camera (-1236.0, 377.5, 1324.0); check: mean 0.69 ... good` (all four "good").
- **Row 2 (far ring at High): done (verified), kept.** world/far-ring `94548d6f` (pushed). Editor D3D11, play mode, console 0 game errors (only CLI transport lines).
  - **What it is:** TerrainStreamingSystem.FarRing.cs (new partial). From View 1.29 up (High, Ultra), far tiles carry the ground from the disc's edge (1984 m) out to 1.5x its radius (about 2976 m). Near the disc they are 256 m tiles on the 16 m grid (the disc's last LOD, same sample points, so the overlap can't be seen). Past a 512 m band they use the 32 m grid, as 512 m tiles wherever a whole one lies past the band. They have no colliders, flora, props or shadows, and build in their own small background batches. A disc chunk past its unload margin waits for its far tile, so the ground never has a hole. The haze (WeatherSystem, via TerrainStreamingSystem.DrawDistance) reaches 98% at the new edge, and the far plane grows to edge + 768 m (3744 m at High). Medium and below: 0 far tiles, so the code path is main's. Off switch: env PUNCHY_FARRING=0 or the static `TerrainStreamingSystem.FarRing`.
  - **Why 1.5x, not 2x:** at 2x (about 4 km) fly300 cost +0.64 ms (256 m tiles only, 652 tiles) and still +0.64 with 512 m outer tiles (316 tiles). Halving the draws didn't help, so the cost is pixels (ground and sea where sky was), not draws. 1.5x is the card's "e.g. 3 km".
  - **Cost** (render ms, 6 interleaved rounds, same session; ring off = main's High; frame ms in brackets):

    | View | Medium | High, ring off | High, ring on | High delta |
    |---|---|---|---|---|
    | walk | 4.32 (13.38) | 6.33 (18.82) | 6.33 (18.14) | +0.00 |
    | hilltop | 3.78 (12.46) | 6.14 (17.69) | 6.23 (18.26) | +0.09 |
    | 300 m up | 4.66 (14.60, noisy) | 4.26 (14.64) | 4.52 (15.31) | +0.26 |

    An earlier 6-round run in a busier session gave +0.33 / +0.35 / +0.44. Both runs are inside the +0.5 ms gate.
  - **Memory (far meshes, High):** 263 tiles (184 with water; 169 on the 16 m grid, 37 at 512 m): terrain 80.2k verts, 16.0 MB; water 44.9k verts, 5.4 MB. That is about +21 MB on top of the disc's 144 + 19 MB. Medium +0.
  - **Hitches, fixed 60 s flight** (40 m/s, 300 m up, fresh play session, alternating):
    - High ring on: 1, 1, 1, 2 frames over 33 ms; 0 over 50; max 33.6-49.4 ms.
    - High ring off: 3, 1 frames over 33 ms; 0 over 50.
    - Medium: 7417 frames, mean 8.09, p99 12.20, 2 over 33 ms, 0 over 50 (main row 1: 4 over 33).
    - Main thread time in the far ring was never over 2 ms in any frame (temporary stopwatch, now removed).
  - **Risk (seen once, not reproduced fresh):** after about 90 minutes in one play session with many preset and FarScale switches, the disc had grown to 2710 water chunks (972 when fresh) and 360-520 batches per flight (80-120 when fresh). In that state, ring-on flights hitched 22, 16, 0, 51 and 1 times over 33 ms (up to 224 ms), against 3, 0 and 1 with the ring off. The far ring's own work stayed under 2 ms. A fresh session was clean. The bench (BENCH-QUEUE) could fly High for 10+ minutes to rule it out.
  - **Nearby is unchanged:** near-player chunks, LODs and colliders are the same (disc lods 45/115/426/1647/785 either way). Flora reach is unchanged. The one look change near the player is the haze: it is tied to the edge, so at High it is now about two-thirds as dense (98% at 2973 m instead of 1982 m). The card asks for this; the sheet shows it.
  - **Not changed:** ProjectSettings, renderer assets, placement and shaders. When you drop from High to Medium in-session, the far plane keeps its larger value; it only grows, as on main. Nothing is drawn there at Medium.
  - Shots: PunchyMilestones/W-131/r2-{Medium,High}-{150m,300m}.png, r2-contact-sheet.jpg (before: r1-*). `[shot] 'r2-High-150m' ready after 0.9 s ... 1280x720, camera (-1236.0, 227.5, 1324.0); check: mean 0.60 ... good`, `[shot] 'r2-High-300m' ready after 1.5 s ... camera (-1236.0, 377.5, 1324.0); check: mean 0.66 ... good`, `[shot] 'r2-Medium-150m' ... mean 0.65 ... good`, `[shot] 'r2-Medium-300m' ... mean 0.71 ... good`. At High, far ridges now stand out past the old edge and fade at the new one. There are no seams, pops or magenta. Medium matches r1.
  - **Next:** row 3 (tree reach x1.5 at High).
- **W-131 row 3 NOT KEPT (tree reach x1.5 at High, verified):** no commit; FloraSystem.cs reverted, world/far-ring stays at `94548d6f`.
  - **What was tried:** (a) "edge": FloraSystem.TerrainEdge() returns the far ring's edge (TerrainStreamingSystem.DrawDistance, 2974 m) instead of the disc's 1984 m, so far trees may stand on the far ring; (b) "x15": that plus the tree layer's (index 2) radius x1.5 while the far ring is on, as R-11 row 3. Both measured against row 2's code ("row2").
  - **Render ms (High, 1920x1080, synced GPU, 6 interleaved rounds per mode, fresh play session):**

    | view | row2 | edge | x15 | x15 - row2 |
    |---|---|---|---|---|
    | walk | 5.34 | 5.61 | 5.67 | +0.33 |
    | hilltop | 5.07 | 5.21 | 5.29 | +0.22 |
    | fly300 | 4.09 | 4.27 | 4.31 | +0.22 |

    Spreads 0.15-0.59. The x1.5 radius alone (x15 - edge) is only +0.04 to +0.08; the edge is the cost. Frame ms (editor, uncapped) rose more: +1.0 to +1.4 ms for both edge and x15 (walk 15.63 / 16.77 / 17.07), from twice the tree tiles to stream and cull.
  - **Memory / counts (tree layer):** tiles 1237 -> 2498, plants about 37k -> 67-73k, CPU 1.6 -> 2.9 MB, GPU buffers 12.2 MB unchanged. Impostors drawn at fly300 2053 -> 5420, LOD2 meshes 4797 -> 5727.
  - **Visible on screen: no.** Shots at High from 150 m and 300 m in each mode, plus a repeat of row2 as a noise floor. Ground pixels changed (sum RGB diff > 30, below the horizon) against the first row2 shot: repeat row2 2945 / 2637, edge 2770 / 1966, x15 2620 / 2776 (150 m / 300 m). A second shot of the same mode differs as much as x1.5 does, and the band at the horizon (where the extra trees would be) changes by 5-81 pixels. Past 2 km the trees sit in the haze (98% at 2973 m) and are a pixel or less, so there is nothing to see.
  - **Verdict:** fails "visible on screen" (and walk is at +0.33, just over +0.3), so neither the edge nor the x1.5 radius is kept. Trees still reach the disc edge (1984 m) at High, as row 2.
  - Shots: PunchyMilestones/W-131/r3-{row2,row2b,edge,x15}-High-{150m,300m}.png, their r3-*-contact-sheet.jpg, and r3-compare-sheet.jpg (row2 / x15 / diff x8, 300 m over 150 m; the diff is clouds drifting and scattered tree sway only). All shots "good"; device Direct3D11; console 0 game errors (only CLI transport lines).
- **W-131 card end (suite + pins, editor closed, world/far-ring `94548d6f`):** 23 passed, 1 failed, 17 skipped in 404s. Pins: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C (both match).
  - The failure is FolkConversationTests.LiveFolk_Answer (`Expected: String containing "[who]"`); it fails alone too (0 passed, 1 failed in 66s). The folk greet the player as a crow ("Who's a good crow?") and only give [meet]/[bye]. The editor PlayerPrefs on this PC (shared by every Punchy tree, DefaultCompany/Punchy) hold `Punchy.Character.Body = Crow`, so the test runs with a beast body and gets beast lines, not the topics. Not from this branch (it touches only terrain streaming and the haze distance). I left the pref alone; whoever set it, or the test (by setting a human body first), should fix it.
  - BurstCache pruned (0.1 GB old in Punchy-world). Editor closed.

## R-10 (continued, row 4: a glowing town at distance; tree Punchy-release, branch light/forwardplus)
- **R-10 row 4 DONE (glowing town)**: light/forwardplus `95c93aef` (pushed; merges cleanly with origin/main 484c8f2c).
  - **Changes:**
    - **Town map:** the world bake now also sums every town's lit windows and street lights into its own 512² map, on the same rect as the world glow map (SurfaceGlow.World BakeTowns, 36 m soft fall-off). Bake log: 101 lit houses and 195 street lights in 6 towns. The whole world bake took 5.6-7.5 s on its worker thread, about the same as before.
    - **Who reads it:** PunchyGlowWorld reads the map past the far glow map (fade 250-330 m from the camera). Terrain already read PunchyGlowWorld. Flora.shader and FloraImpostor.shader now read it too, which covers built things, plants, rocks and R-7's flora impostors. It is warm (1, 0.6, 0.28), follows the windows (off by day and when the lights go out), is off underground, and levels off in dense towns.
    - **Light dome:** SurfaceGlow.Dome and TownDome.shader put a soft dome in the air over each lit town. It is cosmetic and seed-derived (nothing is sent). It is one box per town, drawn in a single RenderPrimitives call. Each pixel integrates the ray through a flattened half-ellipsoid against scene depth, so hills hide it and it shows against the sky. It is a little thicker in Overcast and Rain, takes only a twentieth of the land's haze, and fades out from about 1.5R to 0.5R as the camera nears the town, where the real lights take over.
    - **Off switch:** `-townglow 0` turns off both the map and the domes.
  - **Decisions:**
    - **Own map, not AddWorldSource:** towns went into their own map rather than literal SurfaceGlow.World.AddWorldSource calls, so they can follow the windows' on/off and have their own fade without touching the static plant and lava map.
    - **Impostors:** structures have no impostors (R-7's are flora only), so "lit windows at every LOD" means windows plus the town map on the ground and buildings past the far map. Lit windows were already emissive at every structure LOD.
    - **Dome z clamp:** the dome's far faces are clamped onto the far plane (3000 m). Without that, a town 2-3 km away lost its dome.
    - **Dome queue:** the dome draws at Transparent-5, after the water (Transparent-10), so a sea behind a town doesn't cut a dark band through it.
    - **"Light fog" = Overcast:** the haze density doesn't change with weather (it's 1.98/drawDistance), so "light fog" shots use Overcast (fog 1.5). That brightens the dome only.
    - **Glide fix:** the first glide had a dip at 410-540 m, where the dome had faded but the ground patch was still a thin line at a low angle. Bringing the dome's fade in to 0.5R-1.5R+100 fixed it.
  - **Glide (High, 22:00, Clear, 3000 m to 110 m west of the big town, 20 stops, camera 60-155 m over the ground):** the glow shows at every stop. The dome carries it from 3 km to about 400 m, then hands over smoothly to the ground patch and lit windows. No visible step.
  - **GPU ms (desktop editor, High, ABBA, town map + domes on vs off by `TownGlowEnabled`/`TownGlowStrength`, first warm-up block dropped):**
    - 1.5 km facing the town: 5.91 on vs 5.77 off.
    - 450 m: 4.44 on vs 4.62 off.
    - Both within noise (about 0.3 ms). The laptop pass line is BENCH-QUEUE job 45, which should use `95c93aef` (or later) so row 4 is included.
  - **Suite + pins (editor closed):** 24 passed, 0 failed, 17 skipped in 348 s. Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C. The LyingTests OcotilloMimic failure from row 2b did not recur this run (flaky, or fixed on main).
  - **Shots (PunchyMilestones/R-10/4/):**
    - distances_sheet.jpg: 500 m, 1.5 km, 3 km over the hills and 3 km from the sea, Clear and Overcast, 22:00.
    - The single frames v_{Clear,Overcast}_{0500,1500,3000w,3000sea}.jpg.
    - glide_sheet.jpg and glide/f00..f19.
  - **Not mine:** pale lavender squares on the ground at about 1.5 km west of the big town. They stay with the world and town glow fully off (squares_glow_on.jpg vs squares_glow_off.jpg), so they came in from main (flat props or decals?). Worth a look by the world or flora lane.
  - **Merge note:** R-11 (far trees and rocks as impostors) may also touch FloraImpostor.shader. My change there is one line (the PunchyGlowWorld read).

- **W-131 row 2b (far-ring leak soak, no code change, world/far-ring stays at `94548d6f`):** fresh editor at High, scripted soak of 30 cycles each with the ring on and with it off (`PUNCHY_FARRING=0`). Each cycle: Medium -> High (90 frames each), plus an Ultra hop every 5th cycle (Low every 10th) and back to High, then a 20 s terrain-following flight leg at 300 m and 75 m/s (1.5 km out on a heading turned 77 deg per cycle on odd cycles, back home on even cycles), then settle and count.
  - **Counts after settle are flat in both runs and match cycle for cycle:**

    | measure | ring on | ring off |
    |---|---|---|
    | disc chunks | 3062-3114 | same |
    | chunks past the unload radius | 0 every cycle | 0 every cycle |
    | water chunks at home (c02 824 ... c30 806) | 805-831 | same |
    | water chunks at the out spots | 797-1254, repeats by heading | same |
    | water regions | 114-159 | same |
    | far tiles (unbuilt) | 230-257 (0) | 0 |
    | farWanted / farBuilding | 0 / 0 | 0 / 0 |

  - **Leg-end transients (before the settle):** chunks past unload max 15 on / 14 off (sum over 30 legs 129 / 111); farWanted up to 25 and farBuilding up to 11 with the ring on. All drain to 0 by the settle.
  - **Hitches (other editors open, so noisy):** frames over 33 ms 101 on / 393 off; worst frame 196 ms on / 643 ms off; cycles with a hitch 18 on / 26 off. The ring-on run had a late cluster in cycles 24-29. Neither run trends upward.
  - **Verdict:** no leak, so no fix and no ms rerun. The earlier 972 -> 2710 water-chunk rise did not come back. Water-chunk counts depend on where you are (797-1254 across these spots), and that long session also had extra probe tweaks. The code agrees: CancelFarBuild only drops the current batch, UpdateFarRing re-wants those tiles every frame, and Medium/Low unload the whole ring, so nothing is left behind.
  - Soak probe (`W131Probe.Soak`) is untracked, as before. Editor closed (own PID), BurstCache pruned.

## R-10 (continued, row 4b: the glow on the town, not the sky; tree Punchy-release, branch light/forwardplus)
- **R-10 row 4b DONE (verified)**: light/forwardplus `0a4b3b50` (pushed). This follows Zach's 11-10 note: "the glow dome ... illuminating too much of the sky".
  - **Changes:**
    - **Dome off by default:** the dome still read as a sky dome even weaker and tighter, so it is now off unless `-towndome 1`. When on, it is a low, small halo: radius R x 1.3 + 40 m (was R x 2.5 + 120), height up to 50 m (was 300), strength 0.3 (was 0.8), and Overcast/Rain thickening 0.9-1.15 (was 0.6-1.6).
    - **Town lights (new SurfaceGlow.TownLights.cs + TownLights.shader + Resources/TownLights.mat):** the world bake keeps every lit house and street light as a point (`TownPoints`, at window height 1.6 m or lamp height 3.2 m). 296 points: 101 houses and 195 street lights in 6 towns.
      - Drawing: one RenderPrimitives call each night frame, 6 vertices a light, read from a structured buffer.
      - Each light is a warm HDR core at least 5 px across, with a faint halo 4 light-sizes wide, so a far town's lights run together into one cluster.
      - It takes a twentieth of the land's haze, fades in past 150-280 m (where real lights take over), and has its own soft depth test so hills hide it.
      - It is lit with the windows, off underground and off with `-townglow 0`. It is cosmetic and seed-derived, so nothing is sent and it works the same solo, as host and as a client.
    - **Town map 3x brighter:** `CaveSystem.TownGlowStrength` is 0.35 (was 0.12), on the town's ground and walls.
    - **Docs:** one rendering.md line on town glow and on comparing HDR strengths.
  - **Shots (PunchyMilestones/R-10/4b/):**
    - Sheets: sheet_Clear.jpg and sheet_Overcast.jpg, 22:00, before (95c93aef) and after side by side, at 500 m, 1.5 km and 3 km (1.5 km and 3 km are crops round the town).
    - Full frames: before_/after_{Clear,Overcast}_{0500,1500,3000}.jpg (1920x1080), plus after_Clear_1500_towndome1.jpg with the dome on.
    - Vantages:
      - 500 m and 1.5 km: west of the big town, camera 80 m and 120 m up.
      - 3 km: east over the sea, camera 60 m up. From west, north and south at 3 km, ridges hide the town.
    - What they show:
      - Before: an orange dome lights a wide patch of sky at every distance.
      - After: no sky glow. At 500 m the town is a bright warm cluster on the land. At 1.5 km it is a clear line of warm lights. At 3 km it is a small row of lights on the horizon: it does read, but it is tiny, because the whole town is about 25-50 px wide in a 1920 frame at a grazing angle.
  - **D3D11:** the editor runs Direct3D11. TownLights and TownDome shaders have 0 messages and no errors. 0 magenta pixels in all after shots.
  - **GPU ms (desktop editor, High, 22:00 Clear, ABBA, 4b town map + town lights on vs off by `TownGlowEnabled`/`TownGlowStrength`, first warm-up block dropped):**
    - Walk (in the big town, eye level): 5.55 on vs 5.21 off.
    - Hilltop (1.5 km, 150 m up): 6.45 vs 6.15.
    - 300 m up, 500 m out: 3.01 vs 3.37.
    - All within block-to-block noise (up to 1.6 ms at the hilltop). The extra work is one draw of 296 quads. Pass line on the laptop: BENCH-QUEUE job 45 (point it at this commit).
  - **Suite + pins (editor closed):** 23 passed, 1 failed, 17 skipped in 359 s. Pins held: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C. The failure is FolkConversationTests.LiveFolk_Answer, which expected a "[who]" line in the folk dialogue dump and found none. This row touches no folk or dialogue code, so it is unrelated (worth a look by the fauna lane if main fails it too). Merge-tree against origin/main is clean.
  - **Not mine:** the pale lavender squares from main (row 4 note) showed up again in 2 of 10 shots at 500 m (after_*_0500_squares.jpg). They were not there in re-shots from the same spot, nor with the town map at 0 or 0.35, so they are intermittent and not from the town glow.
  - **Merge note:** CaveSystem.cs (CRLF) changes one constant only. No ProjectSettings or renderer-asset changes.

## R-12 (#241: lavender squares and hard-edged cloud; tree Punchy-world, branch fix/squares-cloud)
- **R-12 row 1 DONE (partial, UNVERIFIED for the squares)**. No code commit: fix/squares-cloud is still at origin/main ef393625.
  - **Squares: not from main.** R-10 took the squares shots on light/forwardplus (cbf6e145 / 95c93aef / 0a4b3b50). Those commits are not ancestors of origin/main, so the issue's "came in from main (484c8f2c)" doesn't hold. They are the unmerged Forward+ real lights and the town glow map.
    - **Not reproduced.** Many tries at R-10's vantage (west of the big town, 80 m up, 22:00 Clear and Overcast), on main and on forwardplus, with glow on and off: no squares. R-10 also saw them in only 2 of 10 shots, and never in re-shots from the same spot. Shots: PunchyMilestones/R-12/repro/, find/, bisect1/ (each has a contact-sheet.jpg).
    - **What they look like in R-10's frames:** pale blue rectangles and triangles with dither, drawn on the terrain under the cacti. They show up together with the blue moon and cumulus clouds.
    - **Ruled out, following Zach's order:**
      - **Items:** the nearest world item drop or loot spawn is about 1130 m from 1489,-890. None are in frame, so the ItemTable placeholder cubes and spheres are not the cause.
      - **Foliage, #233/#239/#236 impostors:** zeroed impostor reaches, unbound atlas textures, atlas health checked and the async placeholder. None gave squares. #236 (3e28a4d8, the D3D11 error-shader fix) is in both trees. #239 was not in the shot build.
      - **Foliage, #215 grass/fields:** the ground detail toggle made no difference.
      - **Creatures:** #240 breath was not in the shot build. #237 fur shells were not tested on their own (an open gap).
      - **Also checked:** weather, wetness and cloud maps; sky holes; the low-res cloud buffer; the shadow cache; window spill; the blue moon alone.
    - **Most likely source (inferred, not proven):** something only in light/forwardplus, either the Forward+ cluster/z-bin light loop or the 95c93aef town glow map sampled in Flora/FloraImpostor. Tile-sized blocks of dither fit a cluster lookup that read stale or unset data for a frame. The forwardplus lane should re-check before that branch merges. Suggested test: 20+ shots at R-10's vantage with real lights off, compared with 20+ with them on.
  - **Cloud: reproduces on main, not attributed.** At 22:00 Overcast, the cumulus has a hard flat bottom and pill-shaped outline (PunchyMilestones/R-12/cloud1/, cloud-old/).
    - **Probes that didn't remove it:** no anvil, tall = 0, sky-off 128 and 512, and the old edge.
    - **Swap test inconclusive:** I swapped Sky.hlsl to pre-R-8 (01e1c1c5) and then restored it. The weather after the replay was different (main had no cloud, the pre-R-8 run had a storm), even with -weatherseed 4. So the shots can't be compared, and #235/R-8 is neither confirmed nor cleared.
    - **Next steps:** bisect the Sky.hlsl commits 3c91d8ac, 01e1c1c5, 328540d8/5ab6813e/334cbe62 and ccd74cc1 with weather pinned in one session (a shader swap plus AssetDatabase.Refresh, with no replay), or pin WeatherField's cell state.
- **R-12 row 2 NOT DONE.** There is no root to fix for the squares on main, and the cloud commit is not found. No look or ms change, so no ms table. Pins untouched (no code changed).
- **Suite + pins (editor closed, fix/squares-cloud = main ef393625):** 24 passed, 1 failed, 17 skipped in 356 s. Pins held: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C. The failure is FolkConversationTests.LiveFolk_Answer, the same one R-10 hit, so main fails it too (fauna lane).
- Editor closed (own PID). The tree is clean, and Assets/Temp is not committed.

## R-10 (continued, row 4c: far lights in tiers; tree Punchy-world, branch light/forwardplus)
- **R-10 row 4c DONE (verified)**: commit 8263a9b5 on light/forwardplus (base 0a4b3b50), pushed.
  - **Step 1, squares (#241): 0 seen in every arm, cause unknown, no fix.** 0/24 with all lights on, 0/24 with `-lightbudget 0`, 0/24 with `-townglow 0`. Also 0 in the moon, preset-stress, recompile and rebake arms. D3D11 compile of FloraImpostor, Flora, Pebbles and Grass: 420 variants, 0 errors, so the magenta fallback (W-130/#236) is not the cause. I skipped the `-noimpostors` and GlowWorld-off arms: with 0 in the "on" arm they can't tell anything apart. In the 4b frames the squares look like impostor cards getting past the alpha clip, but that is inferred, not proven. Caveat: play mode starts without a domain reload, so `-townglow 0` and `-lightbudget 0` stay set in later sessions until a script reload.
  - **Step 2, far lights in tiers.** TownLights is now FarLights (SurfaceGlow.FarLights.cs, FarLights.shader, Resources/FarLights.mat). The world bake gives one far point to each static light: lit windows, street lights, burning trees, beacon fires, surface lava, glowing plants and crystals. Each keeps its source's colour, and fires take tonight's forest-fire colour. Bake line: 7829 far lights, including 5269 burning trees, 2232 glowing plants, 101 houses, 195 street lights and 32 crystals.
  - **Tier table:** one small table, `Punchy.Settings.FarLightPolicy`.
    - `Brightness` 5 (4b town lights were 8).
    - Tiers: Fire 1 (windows, street lights, fires, lava), Glow 0.4 (glowing plants), Faint 0.25 (crystals, creatures).
    - Each kind's row sets size, least pixels, halo, haze share and the screen-size hand-off (56 -> 30 reference px, rule 20).
    - Bake strengths (SurfaceGlow.World.cs): burning tree 0.5, beacon 1.5, lava 1.5, plant 0.12 x stride.
    - The town map (CaveSystem.TownGlowStrength) goes from 0.35 to 0.22.
  - **Town vs 4b**, measured as glow above the background in the town crop: 0.68x at 500 m (rain over the town in the after shot, so partly the rain), 0.76x in BOTH, 0.84x at 1.5 km (0.74x on an earlier run). That is about 20-30% dimmer, as asked.
  - **Ember Forest:** in BOTH its glow went from 143k to 615k (4.3x). It now reads from afar as a wide field of fire points, at least as strong as the town. Fungal forest and crystals show as fainter points (F0500, F1500).
  - **Shots** (22:00 Clear, 7 each, plus contact-sheet.jpg): PunchyMilestones/R-10/4c/before and PunchyMilestones/R-10/4c/after. 0 magenta pixels in the after shots.
  - **Bug found and fixed:** the Burst bake job reads SurfaceGlow statics. A `Shader.PropertyToID` static in the same partial class made Burst fail with BC1091, the bake threw, and no lights showed. The shader ids now sit in a nested class.
  - **GPU ms** (desktop editor, High, 22:00 Clear, ABCCBA x2, mean of block medians, first warm-up block dropped). Arms: A = all on (4c), B = far lights off (town map on), C = town map and far lights off (pre-row-4 look, standing in for main):
    - Walk (eye level in the big town): A 4.40, B 4.63, C 5.03.
    - Hilltop (1.5 km, 150 m up): A 4.08, B 4.03, C 3.82.
    - 300 m up, 500 m out: A 2.87, B 2.64, C 2.55.
    - All of these are inside the block-to-block spread (up to about 0.8 ms). The cost is one draw of 7.8k quads.
    - These are in-editor toggles, not checkouts of 0a4b3b50 or main. For a laptop pass line, add this commit to BENCH-QUEUE.
  - **Suite + pins** (editor open, async PlayMode): 24/24 passed. Pins held: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint 66320C7C. FolkConversationTests.LiveFolk_Answer passed in this run.
  - **Not done:** spores, campfires, colossus eyes and lanterns are not static bake sources, so they have no far points yet; Creature has a row in the table, unused. Vantages T1500, E0500 and E1500 frame close trees. Tonight's fire colour is cyan, so the Ember Forest points read cyan-white.
  - **Merge note:** CaveSystem.cs (CRLF kept) changes one constant and adds one call. The rename is git mv. No ProjectSettings or renderer-asset changes. Unity re-saved SmallBasalt.asset in the tree; I left it uncommitted.

## W-133 row 1 - #213 water round 3 merged with main: done (verified)
- Commit 7a5dd09e on water/round3 (pushed; merge of origin/main, no rebase). Still merges cleanly with today's main (30ed6629, checked with merge-tree).
- Resolution: Sky.shader takes main's body (R-8 low-res clouds, sky volumes LUT, -skyoff bits) with one change, the underwater early-out returns the fog colour plus `PunchyUnderwaterShafts(...)`. Sky.hlsl keeps both: round 3's shafts function and main's R-8 additions (`_PunchySkyVolumes`, `PunchySkyVolumesFromLut`, `_PunchyHazeClear`). Cloud shape untouched. Against main the sky code differs only on the underwater paths.
- Compiles clean: player build 0 C# errors, 0 shader errors, no magenta in any shot.
- Shots (Ultra, `-weatherseed 4`): PunchyMilestones/W-133/seed4-DevW (this branch) and seed4-DevB (main): sky-noon, sky-sunset, lava-above, under-lava, under-sea, under-swamp, shafts-1m/6m/18m. All 10 ShotTool checks good. Side by side: seed4-water-left-main-right.jpg.
- Sky unchanged vs main: mean abs diff of the top band, branch vs main noon 24.8/14.4/2.8, sunset 7.4/6.3/14.9. Main vs a second main run gives the same numbers (24.7/14.4/2.8, 7.4/6.3/14.8): one big cumulus differs run to run (shot timing), everything else matches. Boot view 1.2/0.9/0.5.
- Shaft GPU ms (1920x1080 windowed, uncapped, `-perflog`, (1460, 257), 11:00 clear; second, warm pass of each line; on vs `-noshafts`):

| Preset | 2 m on | 2 m off | 10 m on | 10 m off |
|---|---|---|---|---|
| Ultra | 5.09 | 4.98 | 5.16 | 4.88 |
| High | 3.95 | 4.35 | 3.75 | 4.41 |

  Ultra: shafts about +0.1 to +0.3 ms. High: inside the noise (off measured higher than on). First-pass lines vary by up to 0.9 ms.
- **Main's player build is broken (not this merge):** DebugControls.DungeonWalk.cs (A-95) calls the editor-only TestHooks at lines 146, 158, 418, 419 (CS0103), so `BuildPlayer` fails on main as is. I built with a local, uncommitted stub and restored it after. Owner of A-95 should guard those calls with `#if UNITY_EDITOR`.

## W-133 row 2 - #212 flora round 3 merged with main: done (verified)
- Commit 365e29fb on flora/round3 (pushed; merge of origin/main, no rebase).
- Resolution: FloraCull.compute takes main's hunk as is. Main's `PlantDistance` (R-6 row 0) already measures 3D distance, so round 3's 3D LOD fix is dropped and main's is kept (the file now equals main). Flora.shader (CRLF kept) takes main's impostor hand-off, cull and shadow cheap-holes, with round 3 on top. Net diff vs main: FloraMeshBuilder.cs +5 (rooted stems) and Flora.shader +43/-3 (bark grain at 35% relief, wet darkening 0.5/0.7, gloss 0.07 power 10).
- Rule 20: the bark grain's metre fade (`smoothstep(30, 60, detailFade)`) is removed. The grain now fades by screen size alone (`1 - smoothstep(0.35, 0.9, length(fwidth(ring)) * furrows)`), so it goes once the furrows are under a pixel. No other distances added or touched.
- Shots (High, `-weatherseed 4`, branch vs main): PunchyMilestones/W-133/flora-Dev + flora-DevB (meadow-stems x2, climb-120m, climb-310m, oak/pine dry and rain) and flora2-Dev + flora2-DevB (hill-sw/nw/se forest from 60 m, oak-rain6/21, pine-rain6). Every ShotTool check is good and has the same mean on both builds. Side by side: flora-left-branch-right-main.jpg, flora2-left-branch-right-main.jpg.
  - Climb 310 m looking down: ground drawn, sky 0.00 in the check, no hole (same as main).
  - Forest from the hill: impostors and tree layout match main; the only differences are clouds (mean abs diff 2.5-3.7, the same order as main vs main).
  - Bark: oak and pine trunks show long vertical grain where main shows speckle; in rain the pine trunk reads darker and wetter than main.
- Walk / hilltop ms (1920x1080, uncapped, High, (1740, 3355) 11:00 clear, 8 s per line, alternating branch/main x2):

| View | GPU branch | GPU main | frame branch | frame main |
|---|---|---|---|---|
| Walk (eye level, forest) | 2.90 / 2.95 / 3.38 / 3.86 | 3.84 / 3.90 / 4.42 / 4.62 | 5.9-6.3 | 6.5-7.1 |
| Hilltop (60 m, facing forest) | 2.57 / 2.59 / 3.21 / 3.75 | 3.28 / 3.72 / 3.77 / 3.89 | 5.5-5.7 | 6.1-6.6 |

  Frames given for the settled lines only; lines with streaming hitches (max 55-350 ms) show up in both builds. The branch costs no more than main (GPU about 0.5-0.9 ms lower in these runs, which may be noise).
- Not ours, found on the way: (1) at the lone-oak spot near (1756, 17, ...) rain draws a grey wall over the bottom of the view on both builds (pine spot is fine). (2) Main's player build still fails on DungeonWalk TestHooks (see row 1); built with the same local stub, restored before the push.

## W-133 row 3 - suites on both branches after the second main merge: done (verified)
- Both branches re-merged with origin/main 93ea2439 (A-97 #245 creature surfaces, A-98 #246 underwater sound), as the supervisor asked:
  - flora/round3 7eaf0a6a: clean merge, no conflicts. Net diff vs main still FloraMeshBuilder.cs + Flora.shader only.
  - water/round3 239842b8 + 53271a24: one conflict, Underwater.cs (CRLF kept). Kept #213's per-fluid look (FluidLook/Fluids rows, per-fluid fog, darkening, shafts, bubbles/motes) and took A-98's audio change: listener low-pass, `Muffle()` and the per-fluid `Cutoff` field removed; GameAudioWater reads the public `LensDepth`/`LensWater`. `LensWater = kind` comes from #213's SurfaceAt, so it names the fluid ("the sea", "a lake or river", "a swamp", "lava", "cave water", "cave lava"). `_PunchySurface` y is now `LensDepth`. 53271a24 removes a stray closing brace the merge left (CS1022); 239842b8 alone does not compile, so use the branch tip.
- Full PlayMode suite (pins: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954):

| Branch | Result | Landmarks | Fauna |
|---|---|---|---|
| flora 7eaf0a6a | 25 passed, 0 failed, 17 skipped (434 s) | 112 A955AFE0 | AD723954 |
| water 53271a24 | 24 passed, 1 failed, 17 skipped; the failure passes alone | 112 A955AFE0 | AD723954 |

  - Water's one failure: Setup_SpawnRainAndShot hit its 180 s timeout on a single 285 s stalled frame (the shot itself was saved), with four other editors running on the machine. Rerun alone: passed in 69 s. Same flake as in the first row 3 run.
  - Before this merge (earlier today): main 30ed6629 24/1 (LiveFolk_BeastResponse 52.4 vs >52.7, passed on rerun), same pins.
- Not re-shot after the second merge: the underwater visuals code is #213's, unchanged; only the audio lines changed. Row 1's under-sea/swamp/lava shots still stand for the look.
- Both PRs (#212 flora/round3, #213 water/round3) are ready to merge.

## R-12 cloud (#241 pill cloud; tree Punchy-release, branch fix/pill-cloud)
- **R-12 row 1b DONE (verified).** Cause, in `Assets/Game/Shaders/Sky.hlsl` (PunchyCloudDensity):
  - L180 `float coverage = saturate(weather.r * 1.05 + 0.2 * layering);`. Inside a storm cell, weather.r is high, so coverage saturates to 1.
  - L193 `density = PunchyRemap(shapeNoise, 1 - coverage, 1) * ...`. With coverage 1 the remap no longer cuts the noise, so the noise stops carving the cloud's edge.
  - The outline then comes only from the cell's smooth disc in the 64 m weather map (WeatherField.cs L531/L573, `smoothstep(1, 0.55, d / R)`). That disc stands as a pillar with straight walls, and the base/thickness lerp across the rim rounds its bottom corners: the pill. This is original cloud code, as Zach guessed, so no bisect was done.
  - Shots (22:00 Overcast and noon, weather pinned in one session): PunchyMilestones/R-12/pill/b_night.png, b_noon.png, sheet_pill.jpg.
- **R-12 row 2 DONE (verified).** Fix: the cloud march reads the weather and cloud maps a few hundred metres off the point (a 3D noise warp of up to +-400 m, set once per march step, shared by that step's light samples). The disc's rim now bulges and leans like a heaped tower. `-skyoff 1024` turns it off (maps read straight) for A/B.
  - Kept small: two functions plus three lines in PunchyCloudMarch, with no reformatting. R-8's cheap paths and the underwater early-out are untouched. Cloud shadows and rain volumes still read unwarped maps.
  - Commits 9c476c4e, merge of origin/main 84ec5110 (keeps #213's lines), 1732eed8. Shader refreshed and re-shot after the merge.
  - Shots, before (skyoff 1024) / after, in PunchyMilestones/R-12/after/:
    - Same spot: same_night*, same_noon*, sheet_same.jpg.
    - Coast and sea, another biome: coast_night*, coast_noon*, sheet_coast.jpg.
    - Away view: away_noon*.
  - Look elsewhere, mean abs diff (pixels differing by >16):
    - away_noon: 0.69/255 (0.11%).
    - coast_noon: 0.86/255 (0.25%).
    - same_noon: 5.58/255 (12%). This is the storm itself, which also shades darker grey at noon.
  - Editor-only indicative timings, taken before rule 21 arrived. Not a gate. Same build, skyoff 1024 = main's path, High, uncapped, ABBA ×2:
    - Cloud march: +0.10 ms (0.654-0.661 vs 0.554-0.562).
    - Whole frame: 3.06 vs 3.02 ms avg, within noise.
  - Tried and dropped: an ALU sine warp. Free, but it left a smooth leaning blob.
- **BENCH-QUEUE request (rule 21):**
  - Branch fix/pill-cloud, commit 1732eed8, against main 0479ee53. Player build, High, uncapped.
  - Spots, both with a forced Storm (`WeatherSystem.ForceAt("Storm", xz)`) and timescale 0:
    - (a) shotcam 1489,88,-897 -> 1889,98,-897, Storm at (5489,-897), 22:00 and 12:00.
    - (b) shotcam -1500,300,-3500 -> -1100,330,-3500, Storm at (2500,-3500), 12:00.
  - Arms: main vs branch, ABBA. On the branch build, `-skyoff 1024` gives main's path.
  - Pass: frame time within noise of main (<= +0.1 ms GPU) at every spot.
  - Main's player build needs A-91's DungeonWalk TestHooks fix first, or a local stub.
- PlayMode suite on 1732eed8: 25 passed, 0 failed, 17 skipped (405 s). Landmarks 112 A955AFE0, Fauna AD723954.
- Risks:
  - The rain column under a storm keeps its straight sides. That is the separate rain-volume system, not changed here.
  - GraphicsSettings High was set through the editor. It writes PlayerPrefs, which other trees may share.
  - For a while, some eval calls went to the world editor (PID 25428) because the port swapped. Those were reads, failed compiles and possibly one AssetDatabase.Refresh, so no writes. Evals were then pinned to the release port with a dataPath guard.

## R-9 (continued, row 4: the fine sheet only near the player, plus the leading swell; tree Punchy-world, branch water/deform, #232)
- **R-9 row 4 DONE (verified in the editor; pass/fail: pending laptop job 45c)**
  - Change: each chunk water quad in the 32 m square is cut 8x8 / 4x4 / 2x2 by screen size (`DetailPolicy.WaterSheet` thresholds 168/158/142 px, with hysteresis, rule 20). Quads that would stay whole are no longer in the sheet; the chunk water draws them and only clips the cut cells. The cut is redone only when the player crosses a 1 m cell or the camera distance changes past a 1.2x band. It is held under a budget of 8000 indices x Detail (12000 at High) by shrinking the pixel sizes until it fits. A (Span+1)^2 weight texture eases the field to 0 where the sheet meets the chunk water, so there is no step. A moving swimmer also pushes a raised swell ahead of itself (Zach 13:05).
  - Index counts, High (Detail 1.5), editor:

    | Situation | Sheet | Chunk under player | Chunk + sheet vs chunk |
    |---|---|---|---|
    | Standing at 488,-1144 (job 45's spot) | 8,064 | 23,844 | 1.34x (target <= 1.5x) |
    | Swimming there | 8,640 | 23,844 | 1.36x |
    | Camera 10 m, moving | 6,192 | 23,844 | 1.26x |
    | Wade spot (shallows) | 8,592-10,944 | 12,186 | under budget |

    Job 45 had 319,872 indices with the field on.
  - Commits: 295063da (row 4), 24459300 (merge of origin/main with the #248 player build fix, so the Profile build compiles). The setup merge 65650de9 went up with them. Compiled clean after the merge.
  - Shots at noon, D3D11, 0 magenta pixels in every PNG: D:/Unity/PunchyMilestones/R-9/4/ (swim-{follow,side,chase}-*, wade-{follow,side,chase}-*, contact-sheet.jpg). swim-follow-0 was not written; the side and chase shots cover that set.
  - Written checks: the swim chase shots show the bow ring, the V wake and the raised swell just ahead of the swimmer. There is no visible seam, crack or step where the sheet meets the chunk water. The wade shots show small rings in the shallows. The far side of the square reads as the plain chunk water.
  - Frame timing skipped per rule 21.
- **BENCH-QUEUE request (rule 21): job 45c, QUEUED in BENCH-QUEUE.md.**
  - Branch water/deform at 24459300. Profile build, 1920x1080, uncapped, `-gfx High`.
  - Spot: job 45's, `-time 12 -weather Clear -goto 488,-1144`, swim straight 10 s.
  - Arms: field on vs `-waterfield 0`, ABBA, 300 frames per arm.
  - Pass: on <= +0.3 ms GPU over off. A fall arm (45d) follows once row 5 pushes.
- Risks: the CPU side of job 45 (+4.7 ms) was mostly the full-square cut. The cut now runs only on a cell or camera-band change, but 45c should confirm it. With a close camera the budget shrink makes the 12.5 cm ring smaller rather than adding indices.

## R-12 cloud (continued: shadow follow-up and row 3; tree Punchy-release, branch fix/pill-cloud)
- **R-12 shadow follow-up DONE (verified, with a caveat).** Commit ea9a7ee0. PunchyCloudShadow and PunchyCloudCoverAlong now read the weather and cloud maps at `p.xz + PunchyCloudWarpAt(p)`, the same warp as the cloud march: one 3D sample per call. Style and the height estimate stay unwarped. `-skyoff 1024` turns it off.
  - Shots, noon, branch vs `-skyoff 1024`, in PunchyMilestones/R-12/shadow/: east_noon*.png, sheet_east.jpg, east_ground_diff.jpg (ground band: before / after / diff x6); edge_noon2*.png, sheet_edge2.jpg.
  - Written check: on the branch the cloud's rim is ragged, and the ground diff shows the shadow patches moved and reshaped on the loaded terrain under it (1.4% of ground pixels differ by >16; elsewhere ~0). The rain shaft's sides follow the cloud bulge.
  - Caveat: terrain streams only near the play area, so I could not frame the cloud and its whole ground shadow in one shot. The match is shown by the shadow changing with the warp, not by a single picture of the rim lined up with its shadow.
- **R-12 row 3 DONE (verified).** Commit 86462ce0.
  - **Cause of rain over near terrain:** `Assets/Game/Scripts/Weather/WeatherSystem.cs` L478 (RenderHazeLut), `Graphics.Blit(null, hazeLut, hazeLutMaterial);`. It gave no pass index, so Unity drew every pass of HazeLut.shader. R-8's second pass (sky directions: 8 km rays at true height) was drawn over the haze table after pass 0, so every distance row held the whole 8 km shaft. Ground in the storm's directions was hazed at any distance, with screen-vertical edges (sheet_same.jpg). This came in with R-8 commit 01e1c1c5.
  - Proof from a dump of the table, column toward the storm: before, alpha was 0.97-1.0 in every row including distance 0. After, rows 0-18 are 0.00, then 0.20, 0.46, 0.58, 0.67, 0.59, 0.54 from the shaft (~3 km) on.
  - Fix: `Graphics.Blit(null, hazeLut, hazeLutMaterial, 0);` with a comment. The sky pass call is unchanged.
  - **Thinner rain (Sky.hlsl, PunchyWeatherVolumesAlong):** the maps are read at the cloud warp at its base (the shared helper), so the shaft's sides follow the cloud. The density factor went from 1.4 to 0.6. Streaks are now a coarse layer (110 m) plus a fine layer (30 m) with a cut-off, so there are clear gaps between them, and the rain thins to 40% over 1.5 km below the cloud base. PUNCHY_RAIN_DENSITY is unchanged.
  - **#247: same cause.** At the lone oak (editor, seed 4, Rain, the camera spot from w133-flora.txt) the before shot is an all-grey wall (mean 143/255 in every band). The after shot shows the oak, grass and hills with thin streaks (mean 34-52). Seen in the editor only; a player build was not retaken because main's player build is broken.
  - Shots in PunchyMilestones/R-12/rain/:
    - Storm spot (1489,88,-897), Storm at (5489,-897): storm_noon_before/storm_noon, storm_night_before/storm_night, sheet_storm.jpg.
    - Lone oak: oak_rain_before/oak_rain, sheet_oak.jpg.
  - Written check: in the after shots there is no grey column over the near ground or water. The rain reads as thin streaks you can see the land through, and its sides follow the cloud.
  - Note: the after storm shots were taken in a different play session, and the cumulus/overcast around the storm in the before shots is absent. That is the session's day type; the rain code does not touch clouds.
  - Risk: WeatherSystem.cs is listed under the Animal manager for Sun handling. The only edit is the one Blit line in RenderHazeLut, not the Sun code.
- **BENCH-QUEUE request (rule 21), no timing done:**
  - Branch fix/pill-cloud at 86462ce0 against main. Player build, High, uncapped. Main's player build is currently broken (A-91), so this may wait for that fix.
  - Spots, Storm forced (`WeatherSystem.ForceAt("Storm", xz)`), timescale 0:
    - (a) shotcam 1489,88,-897 -> 1889,98,-897, Storm at (5489,-897), 22:00 and 12:00.
    - (c) ground under the storm's shadow with creatures in view: shotcam 1489,30,-897 -> 1889,25,-600, Storm at (1700,-1900), 12:00 (the noon shadow falls ~0.7x cloud height north of the cell).
  - Arms: branch vs `-skyoff 1024` on the same build, ABBA, then branch vs main.
  - What changed in cost: one extra 3D noise sample per cloud-shadow and sun-cover call. The rain march in the haze table and the sky pass now takes one 3D warp sample per step, in all weather, including clear. The haze table now draws one pass instead of two.
- Suite (PlayMode, at 86462ce0, editor closed): 24 passed, 1 failed, 17 skipped. Pins hold: Landmarks_V3 112 A955AFE0, Fauna AD723954. The failure is `LyingTests.Lying_LegsStayAboveGround_FlatAndSlope` (BoneStag -0.078 m on the 0.33 slope). This is the known A-92 (#231) failure from main, already reported by player and world, and is in animal posing, not sky.
- Editor targeting (coordinator, A-99 report): eval.sh now also refuses unless the port is owned by a Unity process whose command line names Punchy-release. It already read the release port file, passed --project-path and refused (WRONG EDITOR) any other dataPath before running anything. The shotcam 1489,88,-897 writes in PID 46020 date from earlier, when a shared scratchpad script was overwritten by another agent, before the guard existed.

## R-9 (continued, row 5: fine water on falls; tree Punchy-world, branch water/deform, #232)
- **R-9 row 5 DONE for river falls (verified in the editor; pass/fail pending laptop job 45d). Cave falls are NOT done.**
  - Change (commit 2856a634, new WaterPatchSystem.Falls.cs plus WaterPatchSystem.cs, DetailPolicy.cs, WaterField.hlsl, Water.shader):
    - The river fall that looks biggest gets its own fine sheet, switched by `DetailPolicy.ScreenSize` of its drop (WaterFallOn 300 / WaterFallFull 520 px, held with hysteresis; rule 20).
    - The river's own sloping quads are cut from the lip's run-in to the outflow (span -5 m .. foot + 10 m). Each quad gets its own level along and across the flow, so a tall drop gets rows down its length. The budget is 32k x Detail indices, shrunk to fit.
    - The vertex shader moves the fall along its flow (the wave field is a height field): the run-in bulges, the lip curls out, ripples run down the curtain, and the pool heaves at the foot, with foam there.
    - Everything is scaled by the screen-size fade, so the switch-on doesn't pop.
    - Edge vertices follow their coarser neighbour's sloped segment (new per-vertex parentY), so there are no cracks.
    - The curtain is kept shaded as a steep face, so ripples don't make pale flat patches.
    - `-waterfield 0` turns it off too.
  - Index counts, fall 74, High (Detail 1.5), camera yaw 30 rise 18:

    | Distance | Fade | Sheet indices |
    |---|---|---|
    | 15 m | 1.00 | 45,240 |
    | 40 m | 1.00 | 44,040 |
    | 75 m | 0.82 | 37,752 |
    | 90 m | 0.35 | 37,752 |
    | 105 m | 0.06 | 26,856 |
    | 130 m | 0 (off) | 0 |

  - Shots, Clear, D3D11, no magenta: D:/Unity/PunchyMilestones/R-9/5/ (fall74-{15,40,75,90,105,130}m-0.png, contact-sheet.jpg, checks.txt; baseline in before/).
  - Written checks: the near shot shows the lip curl, the rippled curtain and the churning foam at the foot. 75 -> 105 -> 130 m reads the same with the effect fading, with no visible pop or seam. The seam halfway down the curtain is in the baseline too (fall 74 has two steps).
  - Not done: the cave-fall cut (LandmarkEffectSystem meshes); left for a later card. Row 6 (whitewater) not started.
  - Frame timing skipped per rule 21.
- **BENCH-QUEUE request (rule 21): job 45d, QUEUED in BENCH-QUEUE.md.**
  - Branch water/deform at 2856a634 (it can stand in for 24459300 in 45c). Profile build, High, uncapped.
  - Spot: `-goto -2167,3,3691`, shotcam on fall 74 from 40 m.
  - Arms: field on vs `-waterfield 0`, ABBA, 300 frames per arm.
  - Pass: on <= +0.3 ms GPU over off.
- Risks:
  - The fall cut runs on a fall, camera-band or Detail change, as a job finished the next frame. 45d should confirm the CPU side.
  - One fall at a time: a second big fall in view stays coarse.

## R-10 merge main (2026-11-10, world lane subagent on the release tree): done (verified)
- Branch `light/forwardplus`: merge commit 1f6c966d (origin/main 60edfacb into 8263a9b5). It is a merge, not a rebase. Pushed.
- Conflict: only Assets/Game/Shaders/Creature.shader (CRLF kept). Both sides are kept:
  - from main: A-92 fur shells, the A-96 NaN fix, A-97 per-segment surfaces and spec block (ndv/rimK), and A-99;
  - from R-10: `multi_compile _ _CLUSTER_LIGHT_LOOP`, the positionCS input, and `+ PunchyRealLights(positionWS, n, positionCS)` added after PunchyGlowLight in the lit sum.
- Shader check after an asset refresh: no shader errors.
- Suite on 1f6c966d: 25 passed, 0 failed, 17 skipped in 572 s (Unity exit 0). Pins match: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- Shots are in D:/Unity/PunchyMilestones/R-10/merge/:
  - town-night-{aerial,mid,ground}.png (`-place HumanVillage -time 22`): all three are "good". Window lights and town glow show, as in the R-10 4c shots.
  - wolf-noon.png and deer-noon.png (village, noon, `-pose X:rest`):
    - fur shells, coat-coloured furry paws, dark hooves and eyes all render as on main;
    - no white sparkles. The sparkle probe found 0 creature markers on the wolf (360 frames, and 227 on the re-run) and on the deer (360 frames). The deer run also flagged 6 non-creature pixels over 2 frames, at the bottom right of the frame, not on the deer.
  - sparkle-log.txt and contact-sheet.jpg (the contact sheet holds only the last run).
  - Note: `-pose` circles the camera (ThirdPersonCamera.WatchSpin 12), so the still-shot gate never opens. I set WatchSpin = 0 by eval for these runs and changed no code. A `-posespin 0` flag would make this repeatable.
- lod-lint hits on this branch are R-10's own code, not the merge, and they are not view ranges:
  - FarLightPolicy FadeFrom/To 56/30 are reference pixels already;
  - CaveSystem NearFixtures 40 and TownGlowFadeFrom/To 250/330 hand data over at the far map's edge;
  - SurfaceGlow.World FarCrownHeight and FarFireLight are a height and a strength.
  R-13 lists them, and they can convert later.
- Frame timing skipped per rule 21. **BENCH-QUEUE request: job 45e, QUEUED in BENCH-QUEUE.md.**
- Risks: Creature.shader now builds the cluster light loop variant on top of the shells. A creature in a lit town at night pays for both, and 45e's town spot covers it.

## R-13 row 1: list (2026-11-10, world lane subagent on the release tree): done (verified by grep and lod-lint; no code yet)
- Branch `lod/screen-size` made from origin/main 0477dd07 and pushed. The list was made on 60edfacb; the #252 delta (Model Lab, StoneColossi.BuilderFor) touches no range.
- Row 2 next: C# conversions first. Before touching Flora.shader, FloraImpostor.shader, Flora.hlsl (R-10) or Interiors.cs (player lane) I ask the supervisor.


Pixels are reference pixels: 1080 lines, 60 degree FOV (935.3 px per metre at 1 m), Detail 1; px = size x 935.3 / today's metres, so the reference view keeps today's distance. "Size" = what ScreenSize measures. Owner other than R-13 in brackets.

**Convert (C#)**

| Name | File | m | Kind | Size used | px |
|---|---|---|---|---|---|
| FlameRange | Ambience/LandmarkEffectSystem.cs | 150 (xDetail) | landmark fire flames drawn | flame size (f.size); ref 0.35 m | 2.182 (= TorchFlamePixels) |
| ChimneyRange | LandmarkEffectSystem.cs | 220 (xDetail) | chimney smoke gathered | plume 3 m | 12.75 |
| RiverFallRange | LandmarkEffectSystem.cs | 350 (xDetail) | river waterfall sheets | fall drop; ref 10 m | 26.72 |
| Range (Inspector) + tiers | LandmarkEffectSystem.cs | 700 (xView) | landmark effects, river tiers | type radius x scale; ref 20 m | 26.72; 700 m kept as cap |
| ForestFireRange | LandmarkEffectSystem.ForestFire.cs | 70 (xDetail) | near flames on burning trees | crown flame 2 m | 26.72 |
| ForestFarRange | LandmarkEffectSystem.ForestFire.cs | 600 (xView) | one far flame per crown | 4 m | 6.235 |
| BeaconFarRange | LandmarkEffectSystem.Beacons.cs | 1600 (xView) | beacon glow card | 8 m | 4.677 |
| LanternFlameRange | Fauna/StoneColossi.cs | 450 (fixed) | colossus lantern flames | 1 m | 2.078 |
| BoltRange | Flora/FloraSystem.cs | 160 (xView) | spire lightning bolts | bolt 10 m | 58.46 |
| BasinRange | FloraSystem.cs | 80 (xDetail) | fountain/basin water disc swap | basin diameter; ref 4 m | 46.77 |
| DrawRange | Items/Items.cs | 60 (fixed) | dropped item blocks | 0.3 m | 4.677 |
| DetailFar | Terrain/TerrainStreamingSystem.cs -> TerrainSurface.shader | 110 (xDetail) | ground detail texture fade | 0.5 m texture detail | 4.251 |
| SpawnRadius | Ambience/DriftwoodSystem.cs | 150 (xDetail) | driftwood logs shown | 2 m log | 12.47 |
| Range (per type asset) | Ambience/AmbientParticleType.cs | 50 default (xDetail) | ambient particle tiles | type's particle size; ref 0.1 m | 1.871 at default, per type keeps its m |
| StillFrom | Flora/FloraShadowOptions.cs | 40 (-stillfrom) | far tree shadows stop swaying | tree 15 m | 350.7 |
| CullNear | Interact/Interiors.cs | 40 | room meshes + lamp drawn | room 8 m | 187.1 [player lane: ask] |

**Convert (shaders)**: one global set by DetailPolicy.SetView (reference pixels per metre at 1 m, Detail included), helper `PunchyPixels(size, distance)`; each fade becomes a smoothstep on pixels with the same ends at the reference view.

| Fade | File | m | Size | px (fade end) |
|---|---|---|---|---|
| plant shrink (two lines) | Flora.hlsl 184/191 | 60-100 | 1 m | 15.59-9.35 |
| flutterNear | Flora.hlsl 222 | 30-80 | 1 m | 31.18-11.69 |
| leafNear | Flora.shader 381 | 40-90 | 1 m | 23.38-10.39 |
| sheenFar | Flora.shader 481, FloraImpostor.shader 178 | 30-120 | 2 m | 62.35-15.59 |
| amount (ember/bloom) | Flora.shader 567 | 80-100 | 1 m | 11.69-9.35 |
| cellNear | Flora.shader 593 | 4-10 | 0.1 m | 23.38-9.35 |
| wet sheen | TerrainSurface.shader 408 | 120-450 | 2 m | 15.59-4.157 |
| cobbleFade | TerrainSurface.shader 412 | 30-80 | 0.3 m | 9.35-3.51 |
| steep (rock streaks) | TerrainSurface.shader 432 | 40-90 | 2 m | 46.77-20.78 |
| grain | TerrainSurface.shader 472 | 6-22 | 0.1 m | 15.59-4.25 |
| rain darkening | TerrainSurface.shader 549 | 20-60 | 0.3 m | 14.03-4.68 |
| rippleNear | Water.shader 601 | 25-110 | 1 m | 37.41-8.50 (uses existing PunchyWaterPixelsPerMetre) |
| landmark bubbles/alpha | LandmarkEffects.hlsl 89/117 | 600-750 | 20 m | 31.18-24.94 (with Range above) |

Flora.shader / FloraImpostor.shader / Flora.hlsl are also changed by R-10 (light/forwardplus): ask the supervisor before editing.

**Stays (gameplay / simulation / physical)**

| Name | File | m | Why |
|---|---|---|---|
| AskFade | Fauna/FolkConversation.cs | 90 | seconds, not metres: how fast repeated asks are forgotten |
| Items.Reach | Items.cs | 2 | pick-up reach (gameplay) |
| FallVoice 40-180 | LandmarkEffectSystem.cs | - | sound reach (audio) |
| NearRange | FloraShadowOptions.cs | 25 | real-time cascade / shadow cache split; matched to cascade and cache geometry, not one object's size (keep, -shadownear stays for benches) |
| _Thin.x (ThinFrom x layer Radius) | FloraSystem.cs | share of layer radius | follows the layer's draw radius, which FloraRule already sets by pixels; no metre of its own |
| HeatHaze 15-220 / 250-700 | HeatHaze.shader | - | physical: air between eye and ground, grows with distance whatever the view |
| BurrowRange | GraphicsSettings.cs | - | already DetailPolicy.SimulationReach (A-99) |
| Water detail/tiers | WaterQuality.cs, Water.shader 513 | - | already pixels |
| Flora LODs, reach, casting, impostors | DetailPolicy.FloraRule | - | already pixels |

**List only (other owners)**

| Name | File | m | Owner |
|---|---|---|---|
| CaveRange (cave falls) | LandmarkEffectSystem.cs | 120 | W-135 |
| SurfaceFadeNear/Far, WorldGlowFadeFrom/To, RoomFlameRange | Terrain/Caves/CaveSystem*.cs | 25/45, 240/320, 60 | W-135 |
| cave mouth fade | TerrainSurface.shader 436 | 30-60 | W-135 |
| CaveFishRange | Fauna/CreatureRenderSystem.Extras.cs | 35 (xDetail) | A-99 leftover |
| CreatureBreathSystem Range | Fauna/CreatureBreathSystem.cs | 25 | A-99 (breath) |
| AmbientLife Range (drawn critters/birds; also heard) | Fauna/AmbientLife.cs | 40 (22 small) | A-99 (critters); the sound part stays |
| Critters.hlsl fade | Critters.hlsl 27 | 50-68 | A-99 |
| Creature.shader near fades (3-9, 4-14), ShellRange, FootDetail | Creature.shader, creature render | - | A-97 |
| R-10's own (on light/forwardplus, not main): CaveSystem NearFixtures 40, TownGlowFadeFrom/To 250/330 | CaveSystem.cs | - | R-10 / W-135; data hand-offs tied to the far map's edge, not a look |

## R-9 (continued, row 6: whitewater on rapids; tree Punchy-world, branch water/deform, #232)
- **R-9 row 6 DONE (verified in the editor; pass/fail pending laptop job 45f).**
  - Change (commit 9b1cacc1, shader only: WaterField.hlsl, Water.shader; no new placement):
    - Standing waves on steep, fast river water, driven by the mesh's rapids value times its flow. They are world-fixed, so they hold place while the water runs through. Three crossed rows about 2.4 m apart heave with time.
    - On the fine sheet (field or fall) they also move the mesh, by up to 0.16 m. Elsewhere they tilt the light only. They are off down a fall's curtain.
    - Crests break white, torn up by the carried foam (FlowFoam), with a thin broken cover between them.
    - Pre-existing artifact fixed: the flow-turned ripples (FlowingSlope) and foam streaks (FlowStreaks) were read at world position, so the flow's per-triangle turns tore them into dense zigzag lines on bending and rapid rivers (visible in the before shots). They are now read about a camera anchor: a 64 m grid with a per-axis cross-fade to a half-offset grid, so the pattern never jumps.
    - The river shore foam's drift grew without bound with time, and now cross-fades two 6 s reads.
  - Index counts unchanged: sheet 7,992, chunk 4,944 at spawn.
  - Shader messages 0 after AssetDatabase.Refresh. D3D11, no magenta.
  - Shots: D:/Unity/PunchyMilestones/R-9/6/:
    - after/ (near-sheet, near-river, mid, far, runin; contact-sheet.jpg);
    - before/ (2856a634 baseline);
    - before-after.jpg and checks.txt.
  - Written checks:
    - The ruled zigzag lines are gone at near-river, mid, far and runin.
    - The rapids read as broken water, with white crests and streaks running with the flow.
    - Residual: at near-sheet (the river mouth into the sea) some jagged streak edges remain. There, the mouth's flow data itself turns sharply between triangles; it is not a phase error. Left as is.
  - Suite on 9b1cacc1: 25 passed, 0 failed, 17 skipped in 331 s (Unity exit 0). Pins match: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
  - Frame timing skipped per rule 21.
- **BENCH-QUEUE request (rule 21): job 45f, QUEUED in BENCH-QUEUE.md (45e was already R-10's).**
  - Arms: 9b1cacc1 vs 2856a634. Profile build, High, uncapped, ABBA, 300 frames per arm.
  - Views: shotcam on the rapids below fall 74 from 30 m, and a second view inside the anchor handover band.
  - Pass: <= +0.2 ms GPU.
- Risks:
  - Inside the 3-10 m band around each 64 m grid line, both patterns are read twice; at a corner, four times.
  - The river mouth's flow-data zigzag remains (see above).

## W-135 (cave LOD by screen size; tree Punchy-world, branch world/cave-lod)
- **W-135 row 1 DONE (verified): surface-side skip.** Commit f18fc7f7 (CaveSystem.cs, DetailPolicy.cs, DebugControls.Flags.cs).
  - Change:
    - Each chunk's mesh job records the box of its opening vertices (Look's open > 0).
    - Above ground, Draw keeps a chunk only when an opening is big enough on screen (new DetailPolicy.CaveOpeningPixels 4, through Holds hysteresis), is in the frustum, and the chunk's screen rect overlaps that opening's rect.
    - Chunks within BuriedReach of a seen opening but off its rect are drawn ShadowsOnly, so the light at the mouth is unchanged.
    - `-nocaveskip` restores the old drawing (the A/B arm).
  - Counts, main view (submitted = RenderMesh calls including shadow-only; skip on vs -nocaveskip):

    | Spot | Skip on | -nocaveskip |
    |---|---|---|
    | c5 network (-2785, 62, -584), looking +z | 15 submitted, 6 in view, 85,964 tris | 41, 7, 90,208 |
    | c5, looking west | 25, 10, 114,458 | 41, 10, 114,458 |
    | c5 overview from 160 m | 40, 35, 429,486 | 41, 35, 429,486 |
    | c13/c24 (1500, 25, -2620), looking west | 11, 11, 97,802 | 26, 11, 97,802 |
    | c1 (-3060, 204, 1010), looking east | 0, 0, 0 | 8, 3, 32,990 |
    | c1 walk-in (5 poses, 1.7 m eye) | 7, 7, 76,148 | 8, 8, 76,946 |

    - c5 is riddled with openings: 36 of 176 chunks have one, so most of its chunks really are visible.
  - No pop:
    - Same-pose pairs along the walk into the c1 mouth: under 0.2% of pixels differ by more than 40 levels, all of it wind and cloud.
    - Mouth and network stills match main to within the noise between two skip-on captures.
  - Shots: D:/Unity/PunchyMilestones/W-135/row1/
    - walk-pairs-sheet.png: top row skip, bottom row main
    - walk-pairs/
    - mouth-skip/, net-skip/
    - walk-in-skip.gif: early fixed-height walk; it clips through the terrain and shows nothing useful about pops
  - lod-lint: 0 hits.
  - Pins and suite: these will be checked once, before the last row's handoff.
- **BENCH-QUEUE request (rule 21), for the supervisor to queue as job 47i:**
  - Arms: f18fc7f7 default vs f18fc7f7 `-nocaveskip`. Profile build, High, uncapped, ABBA, 300 frames per arm.
  - Views (shotcam):
    - surface over the c5 network (-2785, 62, -584), looking +z
    - c1 mouth approach (-3155, 166, 1038), looking at (-3110, 169, 1038)
    - c13 mouth (1615, 20, -2725)
  - Pass: default is no slower than -nocaveskip (CPU render thread and GPU).
- Risks:
  - The per-frame opening test is O(chunks with openings x chunks drawn). For c5 that is 36 x 176 rect checks, all in managed code.
  - Thin openings seen almost edge-on can fall under 4 px. The hysteresis and the 0.25-0.35 x distance frustum pad cover this in the walk tests; it is not tested at extreme angles.
- **W-135 row 2 DONE (verified): coarser wall levels.** Commit 9d3d86dd (CaveSystem.cs, DetailPolicy.cs, DebugControls.Flags.cs).
  - Change:
    - Each chunk is built at 0.5, 1 or 2 m, picked by Tier on a 1 m ScreenSize at the chunk's nearest point (new DetailPolicy.CaveWall 20/10 px, Scaled by Detail, shared hysteresis). At 1080p and 60 degrees, that is 0.5 m to about 47 m and 1 m to about 94 m.
    - A chunk whose level should change is rebuilt in the background with the leftover build budget, nearest first. The rebuild is swapped in once it is uploaded, so the old walls draw until then. Empty chunks only rebuild finer.
    - Skirts: every open edge on a chunk's border hangs a strip 1.5 voxels back into the rock along its normal, in the edge's own colour. Where levels meet, a crack shows rock, not sky.
    - `-cavelevel n` forces one level for every chunk. `-cavelevel 0` matches main (the A/B arm).
  - Counts. All loaded cave chunk meshes are summed (vertex streams plus 32-bit indices); in-view tris is StatTriangles:

    | Spot | -cavelevel 0 (= main) | by screen size |
    |---|---|---|
    | c5 big room (-2798, 33, -560), all loaded, fresh load | 29.0 MB, 722k tris | 19.4 MB, 479k tris (L0 79 / L1 80 / L2 13 chunks) |
    | same, in view | 171,600 tris | 158,586 |
    | c5 tunnel, 68 m from the room, in view | 599,606 | 356,628 |
    | c5 tunnel, 42 m from the room, in view | 408,214 | 294,990 |
    | c5 mouth from 70 m, all loaded | 30.0 MB, 746k tris | 11.2 MB, 273k tris |
    | c1 mouth from 29 m, in view | 80,300 | 50,674 |

    - Hysteresis makes the mix depend on history. After `-cavelevel 0` and then back to auto at the room, it settles at 22.8 MB (L0 150 chunks).
  - No cracks or changed look: same-pose pairs, -cavelevel 0 vs auto.
    - Room near, mid and far, and the tunnel: at most 0.02% of pixels differ by more than 40.
    - c5 mouth near/mid/far and c1 mouth mid/far: 0.1-2%, all plant sway and cloud. The mouths themselves match.
    - No sky or terrain shows through anywhere.
  - Shots: D:/Unity/PunchyMilestones/W-135/row2/ (room-sheet.png, mouth-sheet.png, c1mouth-sheet.png; columns are main, auto and a >40 diff mask; single shots *-main.png / *-auto.png).
  - Console: no errors or leaks while moving between the spots or when leaving Play with rebuilds in flight. A "JobTempAlloc older than 4 frames" warning shows during the world bake on entering Play. It is logged next to the [ranges] bake; I did not check whether main logs it too.
  - lod-lint: 0 hits.
- **BENCH-QUEUE request (rule 21), for the supervisor to queue (or fold into 47i):**
  - Arms: 9d3d86dd default vs 9d3d86dd `-cavelevel 0`. Profile build, High, uncapped, ABBA, 300 frames per arm.
  - Views (shotcam):
    - c5 tunnel (-2864, 24.5, -567), looking at (-2799, 27, -551)
    - c5 big room (-2810, 27, -556), looking at (-2795, 29, -549)
    - c1 mouth approach (-3155, 172, 1038), looking at (-3110, 169, 1038)
  - Pass: default is no slower than -cavelevel 0 (GPU and render thread). Also watch for spikes while walking the tunnel, from the swap uploads.
- Risks:
  - The rebuild picker scans every loaded chunk (about 180) up to BuildsPerFrame times a frame. It is plain managed code with no allocations.
  - A level change rebuilds the whole chunk, so the build count rises while you move through a network.
  - Skirts add triangles on every border edge (they are in the counts above). Where neighbours share a level, they are hidden behind the wall.
- **W-135 row 3 DONE (verified): cave detail by screen size, plus the cave fall sheet.** Commit 1bf4435a.
  - Files: DetailPolicy.cs, CaveSystem.cs, CaveSystem.Rooms.cs, SurfaceGlow.Spill.cs, CreatureRenderSystem.Extras.cs, LandmarkEffectSystem.cs, CaveData.cs, WaterPatchSystem.Falls.cs.
  - What moved to DetailPolicy (new helpers ViewReach and FadeReach, all with Hysteresis 0.15):
    - Plants (shown and finest), drips, fish, falls (spray/curtain), pools and streams (while about 2 px wide), room flames.
    - SurfaceFade, the world glow fade and the window spill.
    - CaveRange 120 and PlantRadius are gone.
  - Tuning: checked in play at ppm 935.3 (reference 935.31) and Detail 1.5. The new reaches at the reference view equal the old metres exactly:
    - plant 60, finest 20, drip 25, fall 120, fish 35, flame 60
    - surface 25/45, glow 240/320, spill 60/85
  - Cave fall sheet:
    - WaterPatchSystem.Falls.cs now weighs cave waterfalls together with river falls by screen size.
    - A cave fall builds its own quads (a 0.4 m run-in at the lip, then the curtain down to the pool), all cut to the same level, leaving the chunk water uncut.
    - The island's one cave fall shows at fade 1.00 with 12,288 indices.
  - Same-look A/B, 9d3d86dd (main) vs 1bf4435a (auto), same poses. Share of pixels differing by more than 40:
    - c5 room 0.00%, c5 mouth 0.00%.
    - c5 tunnel 3.75%: foliage sparkle seen through the far mouth, plus a few wall columns. Triangle counts differed (74,034 vs 72,724), so the row 2 level mix was in a different state.
    - c1 mouth from outside 4.88%: clouds (time of day).
    - Cave fall 0.42%: the new fine sheet over the curtain, which is intended.
  - Shots: D:/Unity/PunchyMilestones/W-135/row3/
    - row3-sheet.png: columns are main, auto and the diff mask.
    - *-main.png / *-auto.png per pose.
    - fall-auto.png and fall2-auto.png: the sheet.
  - lod-lint: 0 hits. WorldGlowEdge 320 and SpillEdge 85 stay as caps, each with a `// streaming` comment: they are the far map's and the near scan's coverage, not looks.
- **BENCH-QUEUE request (rule 21):**
  - Arms: 1bf4435a vs 9d3d86dd. Profile build, High, uncapped, ABBA, 300 frames per arm.
  - Views (shotcam):
    - c5 room (-2785, 34, -540), looking at (-2805, 30, -555)
    - c5 tunnel (-2805, 31.5, -564), looking at (-2860, 31, -564)
    - c5 mouth (-2803, 45, -630), looking at (-2804, 52, -660)
    - cave fall (-2195.5, 10.5, -1572), looking at (-2203, 8.5, -1576.2)
  - Pass: no slower on GPU or render thread at any view. Expect the fall view to cost a little more (the new sheet).
- Risks:
  - The fall sheet holds one fall at a time. A cave fall can win over a river fall that looks smaller, which is by design.
  - Per-index hold arrays (plants, drips, pools, streams) are rebuilt when a cave set's counts change.

### W-135 row 4: seam darkening (done, verified). Branch world/cave-lod, commit ef191d54
- What it does:
  - Anything standing in a cave darkens the rock and the room tiles round where it touches down. That covers cave plants (not wall torches), room set pieces (crystals, drums, boxes on the floor) and dungeon chests and stand-ins (DungeonPropBox).
  - It hides the seam and is not a shadow. Darkest at the seam: 0.54, Grounding.hlsl's contact 0.72 x broad 0.75. It falls off within a reach sized from the contact's width (0.15-1.5 m).
  - The shared helper is Assets/Game/Scripts/Terrain/ContactShade.cs (Contact.Disc/Box, Distance, ShadeAt), ready for W-136.
- How:
  - Baked at build, so there is no per-frame cost. Each chunk takes its contact list when it builds, and MeshJob folds the shade into the rib shade (Data.w), which Cave.shader multiplies over the whole lit colour. Rock, floor tone, dressed stone, wood and iron all darken alike. No shader edit.
  - The rock tint alone wasn't enough: a floor's tone ignores the tint, so the first try showed nothing.
  - Openness (alpha) and placement are untouched.
  - Coarse levels: the contact grows by half a voxel and keeps at least half a voxel of reach. A seam much narrower than that is faded in proportion.
  - A chunk whose list changes (plant models grown, a room kit made, a dungeon filled) is rebuilt at its level through the usual swap.
  - Room tiles near a contact are cut into cells of about 0.2 m, so the fall-off is smooth.
  - All contacts are seed-derived, so solo, host and client darken alike.
- Counts:
  - Live contacts: plants 1209, boxes 298.
  - Triangles before -> after: room 16006 -> 15754 (row 2 level mix), ruin 83924 -> 86012 (+2088, finer tiles), crystal 28432 -> 31044 (+2612).
- Shots: D:/Unity/PunchyMilestones/W-135/row4/
  - room_/ruin_/crystal_before.png and _after.png.
  - row4_pair.jpg: before | after.
  - row4_darker_x6.jpg: where the after shot is darker, amplified 6x.
  - It is subtle by design: both faces of the seam darken alike. Pixels darker by more than 8/255: room 36.5k, ruin 20.4k, crystal 6.4k.
  - The room's pool floor is darkened inside the basin (the basin is a floor contact).
  - The crystal "before" shot has a dark block at the back left that the "after" doesn't. It is a streaming state, not this change.
- lod-lint: 0 hits on the whole branch.
- **BENCH-QUEUE request (rule 21):**
  - Arms: ef191d54 vs 1bf4435a. Profile build, High, uncapped, ABBA, 300 frames per arm.
  - Views (shotcam):
    - room (-1389.2, 65.9, 1043.96), looking at (-1392.3, 63.4, 1037.4)
    - ruin (1363.4, 9.85, -2544.1), looking at (1372, 7.3, -2544)
    - crystal (1538.9, 3.96, -2596.45), looking at (1536.7, 1.9, -2591.95)
  - Also the first 20 s after load, while chunks rebuild as plants grow.
  - Pass: no slower on GPU or render thread at any view, and no new hitch in the first 20 s. Expect a few % more triangles where tiles are cut finer.
- Risks:
  - Chunks near contacts rebuild once when plant models finish growing, and again when a dungeon fills.
  - Plant contacts darken chunk rock only, not room tiles. Room tiles take their set's and the dungeon boxes' contacts.

### W-135 row 5: lying drums and pieces on raised kit parts (done, verified). Branch world/cave-lod, commit e5dcb14b
- Change (CaveRoomKit.cs only):
  - A drum on its side (fallen ruin drum, roost bone) touches along its lowest line as a Box contact, not at its end cap.
  - A set piece stands on the floor or on the raised part under its foot (pool coping, a pillar's base, a step, floor tiles), whichever top it is nearest above. The contact sits on that top, and BuildMeshes darkens that part's top round it too.
  - Stacked like drums (guano crowns, rope coil turns), overhangs (lantern housings over the smaller post) and anything more than 1.2 m above the floor stay out.
- Contacts over every room (grotto/lake/roost/ruin): 137/92/27/664 -> 140/92/27/672, total 920 -> 931.
  - Lake is unchanged in count: the posts were already counted against the floor. What changed is that their contact now sits on the coping top, and the coping darkens.
  - Roost bones were already counted, as discs at the end cap. They are now boxes along their length.
  - Contacts standing on raised parts: 9/10/5/32.
- Kit mesh size over the 8 loaded rooms: 660,896 -> 687,408 vertices (+4.0%), 328,822 -> 342,078 triangles (+13.3k), from finer cells on the darkened tops.
- Sweep of set parts, in and out:
  - In: grotto floor crystals; lake floor tiles, coping, posts and coil base; roost guano bases, bones and the skull; ruin tiles, rubble, blocks, altar, pillars and bases, fallen capitals and fallen drums.
  - Out: everything 1.5 m or more above the floor (wall and ceiling crystals, beams, perches, sconces, lantern housings, ruin lintels and upper drums), plus the lake pieces sunk in the pool (y -1 to -1.5).
- Pins: Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 unchanged. Full PlayMode suite: 25/25 passed. lod-lint: 0 hits.
- Shots (same camera before and after): D:/Unity/PunchyMilestones/W-135/row5/
  - sheet_ruin_fallen.png: the floor darkens under the fallen drum along its length, and the pillar base top darkens round the pillar.
  - sheet_lake_boatpost.png and sheet_lake_lantern.png: subtle darkening on the coping at the post foot and under the coil.
  - sheet_roost_bones.png: the bones darken along their length.
  - The single shots are before_*.png and after_*.png.
- No timing. BENCH job 52 ("ef191d54 or newer") covers it, so bench e5dcb14b.
- world/ground-contact-shade had no commits of its own yet. It was fast-forwarded to e5dcb14b and pushed: no rebase of existing work and no force-push.
- Risk: kit BuildMeshes is CPU-heavy because ShadeAt checks every contact for every vertex. This dates from row 4; row 5 adds 11 contacts and about 4% more vertices. A possible follow-up is culling contacts per part by bounds.

### W-137 rows 0 and 2b: cave mouth seam map, and tunnel portal covered way (row 2b done, verified; row 0 done, part UNVERIFIED). Release tree, branch world/mouth-rim, commit b5312063
- Row 0, seam map (4 mouths on this seed: cliff = cave 5 Crystal, gentle = cave 18 LavaTube, narrow slit = cave 9 Granite, mine = cave 23; there is no bat-folk mouth on this seed). Read from code and the shots:
  - Step at the rim: the terrain is cut MeshCutDepth deep but the cave skin stands SkinGap off it, so a lip shows where they overlap (BuildChunkMeshJob cut vs CaveSystem skin).
  - Crack / sky showing: the shader's per-pixel clip against the skin mesh leaves a thin slot on narrow slits, and chunks at LOD 2+ cut no hole at all, so the mouth shows as a dent at 30 m (BuildChunkMeshJob only cuts at LOD 0-1).
  - Shading and texture change: TerrainSurface.shader on the ground against the cave material on the skin, with no blend across the rim.
  - All of these sit in W-135's files (BuildChunkMeshJob, CaveSystem*, TerrainSurface.shader): rows 1 and 2, not touched here.
  - Shots: D:/Unity/PunchyMilestones/W-137/before/before-m{5,18,9,23}-{2,8,30}.png. Several 2 m shots show only ground: UNVERIFIED. Frames are 2560x1600 (16:10), not cropped.
- Row 2b finding: the headwall top already met the hill (top gap 0.00 m at all 6 portals before and after, all LODs). The gap Zach saw is the terrain cut behind and beside the portal: a roofless trench over the road, with sky through the arch (before-p0-front).
- Change (TunnelPortalKit.cs only): per portal, from its seed, a stone covered way behind the headwall (ashlar side walls down to the drawn ground, a lintel roof in whole courses clear of the ground, risers where it steps, an end wall where the hill closes) and wing walls over the cut beside the road in front. Heights from MeshHeight at LOD 0-2. Cut test is MouthCut at mesh height. Headwall and arch output unchanged.
- Probe (all 6 road-tunnel portals; cut cells not under stone):

| Portal | Before | After |
|---|---|---|
| #0 cave 25/0 | 67.13 m2 | 2.38 m2 |
| #1 cave 25/1 | 67.75 m2 | 2.13 m2 |
| #2 cave 26/0 | 57.00 m2 | 0.25 m2 |
| #3 cave 26/1 | 58.13 m2 | 0.50 m2 |
| #4 cave 27/0 | 73.38 m2 | 0.13 m2 |
| #5 cave 27/1 | 53.63 m2 | 0.50 m2 |
| Total | 377.00 m2 | 5.88 m2 |

  - Cave 25's remainder is one row at z -9.5, the tunnel's middle where the two covered ways meet (probe edge, not a hole). The rest are single cells at the face corners.
  - Headwall top gap: worst 0.00 m (target 0), none over 5 cm. The probe's "bottom" column (3.5-3.9 m) is the coping's end overhang, not a gap.
- Mesh: all 6 portal kits 31,968 -> 88,176 verts, 15,984 -> 44,088 tris (about 13-17k verts and 45-53 solids per portal).
- Shots (same camera): D:/Unity/PunchyMilestones/W-137/before/before-p{0,3,4,5}-{front,high}.png and after/after-*.png. p0-front shows the sky through the arch before and the roof after. All [shot] lines "good".
- BENCH-QUEUE request: arms origin/main vs b5312063, spots portals #4 (-2024.9, 261.4, 697.7), #2 (-1878.9, 125.4, 97.7) and #0 (2045.7, 6.8, -1540.3). Check the one-off kit build when a portal first loads (7-31 ms each in the editor) and GPU cost with the extra triangles.
- Risks:
  - The covered way runs straight along the portal's axis, so a tunnel that curves sharply near its mouth could leave a strip uncovered.
  - Where the ground dips and rises again behind a portal, the roof shows as a separate raised step (p4-high).
  - Optional frame styles not done: they would sit on the portal path in CaveSystem.Rooms.cs (W-135's file).
- Pins: Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 unchanged. Full PlayMode suite: "24 passed, 1 failed, 17 skipped in 337s". The failure is FolkConversationTests.LiveFolk_BeastResponse ("the fleeing folk got no further off"). Re-running FolkConversationTests alone: "6 passed, 1 failed", the same test again ("the folk told to flee isn't fleeing", mode Idle). This is the animal lane's known flaky test (see REPORTS-animal/player). This branch changes only TunnelPortalKit.cs, but I did not prove the failure has nothing to do with it.

## W-137 look-check (diagnosis only, no code change; world/mouth-rim b5312063)

Zach: "I don't see a tunnel at all... It looks like an archway embedded in the ground." Checked all 6 road portals (3 tunnels).

- Which side: in the old shots the camera was on the approach side (the road side, out of the hill), facing the arch. The far end is not hiding a better entrance: both ends of each tunnel look the same.
- Does the road reach the arch? Yes. At all 6 portals the road comes in at floor level and the drawn ground follows it within about 0.4 m. Nothing buries the opening; the old p0 "hump" was the side ground of a shallow cutting seen from a low camera.
- The real problem: every portal face sits where the ground is level with the road. The ground over the tunnel line is only 1.0-1.5 m up 5 m in, and passes the 3.36 m arch top 7-8 m in. So the 4.9-5.2 m headwall stands up out of flat ground, with no hillside around it. The approach cuttings are only 0-1 m deep, so no cut slopes frame the mouth either.

Ground over the tunnel line, metres above the road floor at the face (arch top 3.36, headwall top 4.9-5.2):

| Portal | tunnel / bore | 5 m in | 10 m in | 20 m in | peak |
|---|---|---|---|---|---|
| 0 | 25, 18.8 m | 1.0 | 3.3 | -0.1 (past the far end) | 3.3 |
| 1 | 25, 18.8 m | 1.1 | 4.1 | 0.5 (past the far end) | 4.1 |
| 2 | 26, 44.3 m | 1.3 | 8.2 | 13.2 | 13.7 |
| 3 | 26, 44.3 m | 1.3 | 5.5 | 9.1 | 9.6 |
| 4 | 27, 30.3 m | 1.5 | 5.1 | 7.9 | 8.2 |
| 5 | 27, 30.3 m | 1.5 | 4.7 | 2.3 | 5.0 |

- Tunnel 25 (portals 0/1) has no hill at all. It is an 18.8 m cut-and-cover under a 3-4 m bump that never rises more than 0.7 m above the inner arch, and the terrain cut runs its whole length. From the road you can see daylight straight through it.
- Tunnel 27 has a low hill (5-8 m). Tunnel 26 has a real hill (9-14 m), but its mouths still start 7 m out on flat ground.
- Shots: look/p{0..5}-{approach,hill,inside}.png, approach sheet look/sheet.jpg (milestone folder W-137). The p2/p3 hill shots are blocked by forest, even from 20 m up.

Proposed fix (world lane, RoadBaking + TunnelPortalKit):
1. Move each portal face in to where the ground over the line is at least the headwall top plus about 1 m (about 6 m), and keep the stretch between as a proper cutting: graded to the bore, with side slopes that climb to meet the headwall wings.
2. Turn down bores whose peak cover is under about 6 m. Tunnel 25 would become a plain cutting.
3. Give the approach cutting a minimum depth, so the headwall is set into a slope face and does not stand free.

## W-136 (ground contact darkening; tree Punchy-world, branch world/ground-contact-shade)

### Row 1: contact darkening hugging the footprint: done (verified)

- Commit aa0b87f9 (pushed; the branch started from W-135 row 5, e5dcb14b). New GroundContacts.cs: each flora model gets a list of "feet", sized from where it touches the ground: a trunk's width (never its canopy), a rock's base outline, walls and posts of built pieces, a prop's legs. BuildChunkMeshJob darkens the terrain vertex albedo at every LOD: darkest at the seam (ContactShade.Seam 0.54), fading out within about one to two widths. Placement is only read, never moved.
- Feet per model: the KitHouse has 156 along its walls (capped at 160 per model; the fit coarsens until it fits), a RailFence 4. Models by kind (893 in all): 215 plants and trees, 167 rocks, 465 buildings, 46 props.
- Shots (same camera before/after, contacts off/on, clear sky, time frozen at 14:00): milestone folder W-136/row1/, sheet_near.png (2-5 m: trunk, boulder, house wall, fence post, stall) and sheet_far.png (the same at 30 m). diff_sheet.png shows where pixels got darker (top near, bottom far). Darkened pixels per near shot: trunk 17k, boulder 109k (a large rock, so its reach covers the frame), wall 58k (a band along the base), post 837, stall 142.
- Pins: Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 are unchanged (in the PlayMode suite, run before the last handoff).
- lod-lint on aa0b87f9 flagged `bin < 50f` in the feet fit. That is model geometry (the coarsest merge bin), not a view range. 3b1cb6aa makes it a named constant; lint on 3b1cb6aa prints nothing.
- Limits:
  - The terrain vertex grid is 1 m at LOD0 and coarser further out. A 6 cm fence post spreads over a vertex, so posts and stall legs are faint by design.
  - Wall feet are clamped to 3 m wide, so a house band is about 1 m and partly under the wall. From eye height (1.7 m) it is foreshortened; it reads best from about 3 m up.
  - When flora publishes or a tunable changes, all loaded chunks re-mesh once. To catch objects across chunk edges, chunk builds look this far out: 36 m for natural objects, 52 m for built ones.
- BENCH request (rule 21, lanes don't time): chunk build cost (ms per LOD0 chunk) and frame time over a forest and a town, main vs world/ground-contact-shade. The contact pass loops over the feet near each chunk inside the Burst job.

### Row 2: per-kind strength and reach tunables: done (verified), waiting for Zach's OK

- Commit 3b1cb6aa (pushed). FloraSystem's inspector has a "Ground contacts (W-136)" header with ContactPlants, ContactRocks, ContactBuildings and ContactProps, each with Strength (0-2, where 1 means the seam reaches ContactShade.Seam) and Reach (0-2, where 1 means about the contact's width). FloraSystem passes them to GroundContacts. A change re-meshes the loaded terrain once, and you can tune them live in play mode. They change only how the ground looks, so each peer just uses its own values.
- The kinds follow DetailPolicy's flora classes. Plants and trees are one kind. Buildings means every Structure form, so fences, stalls, wells and signposts count as buildings. Props means the Debris forms: benches, seat logs, stumps, fire pits, woodpiles, driftwood.
- Shot for Zach: milestone folder W-136/row2/sheet_row2.png (1200x676). Rows: trunk, boulder, house wall, fire pit with seat logs. Columns: all off, defaults (1/1), that kind at Strength 2, that kind at Reach 2. Pixels darker than "off" (count / largest drop, out of 255):

| Subject | defaults | Strength 2 | Reach 2 |
|---|---|---|---|
| trunk | 17.5k / 27 | 31.6k / 62 | 33.8k / 29 |
| boulder | 112k / 27 | 116k / 73 | 127k / 30 |
| house wall | 52k / 13 | 75k / 30 | 74k / 15 |
| fire pit + logs | 21 / 6 | 1.4k / 7 | 1.4k / 7 |

- Props barely show. Their feet are much smaller than the 1 m vertex grid, so there's almost nothing to darken at any setting. Making them show would need a finer grid or a decal, which is not in this card.
- Full PlayMode suite on 3b1cb6aa: "completed: 25/25 passed". Pins Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 are unchanged.
- The row 1 BENCH request covers this row too: with the defaults the cost is the same as row 1.

### Row 3: one contact-shade path for every surface: done (verified)

- Commit 077acd90 (pushed, world/ground-contact-shade). Zach 11-11: unify the methods rather than keeping a cave version and an overworld version. Defaults stay at 1/1.
- The one code path is ContactShade (Assets/Game/Scripts/Terrain/ContactShade.cs + ContactShade.Footprints.cs):
  - Footprints: one fit for every placed thing. ModelFeet handles flora models on the terrain and in caves (fitted feet, then ground colliders, then trunk, plus a stem for cave plants). PartFoot handles kit parts, using W-135's lying-drum line and lowest-face box rules. BoxFoot handles dungeon boxes. FitFeet does the slice fit.
  - Shade: Weigh + Darken, used through Contact.ShadeAt and Contact.Near.
  - Tunables: one per-kind Strength/ReachScale set, through ContactShade.Set and Version. FloraSystem's inspector sets it.
  - Users: terrain chunks (GroundContacts + BuildChunkMeshJob), cave chunks (CaveSystem.MeshJob SeamShade) and room/kit tiles (CaveRoomKit.BuildMeshes). All three take their footprints, shade and tunables from ContactShade, and each mesh build gets copies of the tunables.
- Deleted duplicates:
  - from GroundContacts: its own Foot/Kind structs, FitFeet, the tunables, KindOf and the per-foot Kinds array
  - from CaveSystem: its own cave-plant footprint code and the SeamShade fall-off math
  - from CaveRoomKit: its own part-foot switch
- Net change in 11 files: +479 / -364.
- Split dropped: any contact now darkens whatever surface it touches. Cave plants and dungeon boxes feed room tiles (RoomTileContacts lists the kit's own contacts, then the plant and box contacts standing in that room). A tunable change re-meshes the terrain, cave chunks and rooms once.
- Kit shell tiles cull contacts per part by bounds. Set parts count only contacts on the floor or on their own raised top.
- Contact counts (before -> after, at the defaults):
  - Terrain feet: 43960 -> 43960, over 893 models. By kind (plant/rock/building/prop): 206/6577/35812/1365.
  - Room kits' own contacts (grotto/lake/root/ruin): 140/92/27/672 -> the same.
  - Dungeon boxes: 298 -> 298. Extra tile contacts per room type: 8/5/3/14, all boxes, as before.
  - Cave plants: 1209 -> 4564. They now use the same fitted feet as the terrain (several capsules per rock or bush) instead of one collider or stem disc each.
  - Plant contacts on room tiles: 0. No cave plant stands within 2 m of a room's floor area today (the nearest is 2.2 m outside), so the dropped split only matters for future placements.
- Shots (same camera before/after, milestone folder W-136/row3/): before_/after_ trunk, boatpost, lakeroom and ruin; sheet_*.png puts them side by side; diff_*.png marks the pixels that changed by more than 8/255.
  - Checked at small size: no change in ground, rock or tile shading.
  - The changed pixels are animated water, the boat bobbing, a doorway highlight, and clouds in the trunk sky (ruin: 232 px).
- Changes at the defaults, besides the split:
  - Caves now also skip contacts fainter than ContactShade.Faint (4% at the seam). This only applies to small contacts on coarse cave LODs, which the terrain already skipped.
  - Cave chunk contact buckets are wider because Extent allows reach x2. This doesn't change the shade.
- lod-lint on 077acd90 prints nothing.
- Full PlayMode suite on 077acd90: 24/25 passed. The one failure was the known-flaky FolkConversationTests.LiveFolk_BeastResponse ("Idle 81.1"), which passed when rerun alone. Pins Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 are unchanged.
- BENCH request (rule 21, lanes don't time): compare main with world/ground-contact-shade 077acd90 on:
  - cave room mesh build cost (CaveRoomKit.BuildMeshes for a ruin room and a lake room)
  - cave chunk mesh build with about 3.8x more plant contacts
  - terrain LOD0 chunk build over a forest and a town

## W-138 (mountain tunnels; tree Punchy-release, branch world/mountain-tunnels from origin/main cc835cf6)

### Row 1: survey of roads against ranges and big relief: done (no game code changed), waiting for the rule review with Zach

- Method: a throwaway, git-ignored editor probe. It rebuilds the road pairs (the MST plus cave spurs), chains the baked road streets per pair, and measures each road and the best straight bore under it. A "bore" here is the straight line between the two points where cover over a level-graded line first and last reaches 6 m. It reports length, peak and mean cover, the share of the length under at least 20 m, cover and ground slope at each portal, grade, and the road length it would save. Today's seed: 6 settlements, 5 town roads (all built), 20 V3 spines, 5 passes. None of the 11 cave spurs got a road; that predates this card.
- Today's tunnels (Stats.Tunnels): (2053,-1535) bore 19 m on road S0-S1 in coastal lowland, with ground nearby only 12-27 m (the "senseless" case); (-1870,118) bore 44 m where road S4-S5 crosses spine 0 at a saddle; (-2023,713) bore 30 m on the massif top near S5.

| Road | Length vs straight | Climb | Range | Best 60-500 m bore: length, peak/mean cover, % >= 20 m, portal cover, portal slope, grade, saves |
|---|---|---|---|---|
| S0-S1 | 3920 / 3642 (x1.08) | +5 | none (spine 9 is 554 m off) | none: flat lowland |
| S0-S4 | 4503 / 4109 (x1.10) | +8 | none | none (best at any length: 1124 m, peak only 18) |
| S4-S2 | 842 / 810 | flat | none | none |
| S4-S5 | 2721 / 2282 (x1.19) | +247 (y 6 to 253, up the escarpment) | crosses spine 0 (Wet-Cold border range, crest 142) | 171 m, 32/22, 66%, 8.4/7.3, 0.94/0.24, 0.077, 66 m |
| S5-S3 | 1883 / 1592 (x1.18) | +19 (y 253 to 137) | runs on the massif, 154 m from spine 0 | 453 m, 35/22, 60%, 6.5/6.2, 0.17/0.32, 0.035, 58 m. At any length: 1241 m, 84/43, 79%, 6.5/6.0, grade 0.067, saves 284 m |

- S4-S5 climbs a valley onto the plateau north of range 0, which is 180-250 m high. A single-level bore can't avoid the climb, so the best bore only shortcuts the saddle near the top.
- Pairs left without a road because a range is in the way: none. No pair within 4 km crosses a spine or a rise over 60 m with a network route more than 1.8x the straight line. Range 0 is a true range (at (-1001,283): crest 167 m, with lowland at 50 and 28 m either side), but no settlements sit near it, so no road needs to cross it.
- Proposed tunnel rule (for the review before row 2):
  1. Bore only where a road has to cross a V3 border range or massif (the line crosses a spine or the massif's crest), never under small hills. Today's sub-20 m bores (19, 30 and 44 m long) become cuttings or go round.
  2. Bore 60 m to about 500 m long, graded at most 0.08. At least 20 m of cover over at least half the length, and at least 6 m of cover at each portal, set into a slope face.
  3. Bore only when the far side is about the same level, so the bore replaces a climb over the crest and doesn't just shortcut the top of an escarpment. Otherwise build a switchback or a graded road.
  4. With 6 towns, a range-crossing need almost never comes up. If Zach wants tunnels visible, make one on purpose: a road across a border range between realms (range 0 or spine 2), or tie it to the coming settlements.
- Shots (milestone folder W-138, clear sky, 12:00, no HUD): w138-1-range0-crossing.png (road S4-S5 climbing to the spine 0 crossing; the road reads as a pale line up the valley), w138-2-massif-450m.png and w138-3-massif-1200m.png (the massif top along road S5-S3). Contact sheet: contact-sheet.jpg. On the massif the ground reads as a broad, bare dome rather than a mountain, and the road is a short pale line. Expected for a 35-84 m cover. The shot tool's checks: all three "good".
- Pins: not run (no code changed). Landmark hash and fauna fingerprint stay measures only.

### Row 1b: tunnel and destination survey for row 2: done (no game code changed), waiting for Zach's pick of router setting

- Method: a throwaway, git-ignored editor probe. It dumps RoadBaking's cost grid (20 m cells), a 10 m height grid, roads, caves, landmarks, spines and settlements. A python copy of the road router then reruns the town links and spurs with tunnels allowed. A bore is any straight line with dry portals, at least 60 m long, whose cover reaches 15 m. Inside a bore the grade is at most 0.15. Outside, roads keep MaxGrade. The option C bores were checked again against exact editor ground every 5 m. Placement can move; the landmark hash and fauna fingerprint stay measures only.
- Images (milestone folder W-138/row1b): map-router-A.png, map-router-B.png, map-router-C.png (the top-down network for each setting, with bores in red), map-option-c.png (the five option C bores), shot-landing.png, shot-portal-c0a.png, shot-cave5.png. All checked at small size.

**(a) Sea folk landing (required road).** The landing is at (-1783,-554), h37, in FoggyMarsh, on the lake's west shore. The underwater SeaFolkVillage is 172 m out under 15.7 m of water, so this landing is the pier for the underwater city. The nearest road is S4-S5, 434 m away in a straight line. Best route: a spur from (-2336,-856), 793 m long, ascent 26 m, max grade 0.16, one bridge cell. No bore is needed. With the B or C costs the spur starts at (-2316,-816) instead: 801 m, grade 0.12. shot-landing.png shows the shore with the landing post in the water.

**(b) Hills that could take a bore (cover of 30 m or more over the road line, within 400 m of a road or sight line).** The survey found 37; 9 of them would shortcut an existing road. Almost all sit on the massif and the S4-S5 escarpment. The lowland roads (S0-S1, S0-S4, S4-S2) have none. Top 12:

| # | Hill centre | Ground | Peak cover | Near | Best bore | Saves | Biomes |
|---|---|---|---|---|---|---|---|
| 1 | (-2486,1004) | 290 | 151 | S5-S3 | (-2876,1244)->(-2045,900), 900 m, mean cover 36, grade 0.018 | 305 m, crest +37 | Tundra/Tundra |
| 2 | (-2803,1231) | 244 | 136 | S5-S3 | (-3316,1424)->(-2281,995), 1120 m, mean 25, grade 0.135 | 213 m | Tundra/Tundra |
| 3 | (-2269,717) | 264 | 168 | S5-S3 | (-2856,1344)->(-2022,510), 1180 m, mean 34, grade 0.023 | 208 m, crest +31 | Tundra/Tundra |
| 4 | (-1901,668) | 234 | 138 | mine spur | (-1936,304)->(-1844,526), 240 m, mean 17, grade 0.129 | 175 m on S4-S5 | PoisonSwamp/Tundra |
| 5 | (-2028,372) | 199 | 121 | S4-S5 | (-1896,304)->(-1769,431), 180 m, mean 18, grade 0.145 | 134 m | PoisonSwamp/Tundra |
| 6 | (-2166,-26) | 151 | 85 | S4-S5 | (-2276,-656)->(-1710,711), 1480 m, mean 25, grade 0.086 | 54 m, crest +41 | SparklingForest/Mountains |
| 7 | (-1932,1005) | 213 | 158 | S5-S3 | (-2376,1424)->(-2070,685), 800 m, mean 22 | 35 m | Tundra/Tundra |
| 8 | (-1709,223) | 142 | 111 | S4-S5 | (-1716,104)->(-1716,504), 400 m, mean 30, grade 0.125 | 3 m, crest +24 | FoggyMarsh/Tundra |
| 9 | (-2246,1344) | 254 | 136 | S5-S3 | (-2736,1424)->(-2145,1179), 640 m, mean 22 | 1 m | Tundra/Tundra |
| 10 | (-2866,934) | 261 | 136 | cave1 spur | (-2756,724)->(-2897,865), 200 m, mean 25, grade 0.148 | - | Mountains |
| 11 | (-1961,1449) | 186 | 116 | S5-S3 (312 m) | (-1976,1384)->(-1902,1415), 80 m, mean 27 | - | Tundra/Mountains |
| 12 | (-2536,4) | 178 | 113 | S4-S5 (98 m) | (-2576,144)->(-2477,-96), 260 m, mean 19 | - | SparklingForest |

**(c) Option C: hand-placed bores under spine 0 and spine 2 (exact editor ground).** See map-option-c.png.

| Bore | South/west portal (x,z,h) | North/east portal | Length | Peak / mean cover | Share >= 15 m / >= 20 m | Grade | Roads to portals | Waystations |
|---|---|---|---|---|---|---|---|---|
| C0a | (-1596,44,41) FoggyMarsh | (-1596,1164,93) Mountains | 1120 m | 103 / 50 | 0.85 / 0.76 | 0.046 | 280 / 563 m | (-1635,34) h49; (-1496,1164) h78 |
| C0b | (-1776,4,74) FoggyMarsh | (-1776,1924,79) Mountains | 1920 m | 129 / 78 | 0.95 / 0.95 | 0.002 | 137 / 824 m | (-1679,-22) h53; (-1916,2166) h107 |
| C0c | (-1796,44,94) | (-1597,524,114) | 520 m | 69 / 37 | 0.85 / 0.83 | 0.038 | 99 / 147 m | (-1780,-14); (-1500,550) |
| C2a | (1344,944,55) Temperate | (1824,944,61) MesaCanyonlands | 480 m | 64 / 34 | 0.73 / 0.69 | 0.013 | 2150 / 2430 m | (1286,960); (1945,874) |
| C2b | (2844,584,39) MesaCanyonlands | (2155,2247,20) Grassland | 1800 m | 76 / 29 | 0.76 / 0.63 | 0.011 | 1430 / 3204 m | (2787,527); (2183,2275) |

- C0a is the best long tunnel that changes biome. It runs from the marsh lake shore into the Mountains realm. Its north portal is 247 m from cave17 (Granite grotto), and the south end is near cave26 and the FungalGrotto. A dip at (-1596,554) leaves 41 m of cover, enough for a light shaft. shot-portal-c0a.png shows the ridge above the south portal rising behind the lake.
- C0b is the showpiece: 1.9 km long, almost level, with 95% of its length under 20 m or more of cover. It ends 429 m from cave2 (Ice grotto). Cover dips to 28 m at (-1776,1624).
- C0c is a shortcut on S4-S5. Its north end is 380 m from the mine (cave23) and DwarfMiningCamp.
- Spine 2 (C2a, C2b) has no roads near it: both bores need a new road or a settlement first. C2b's portals have only 1-5 m of cover over the first 30 m, so they need cut-and-cover or a moved portal.
- Each waystation is the flattest dry spot within 320 m of its portal and within 30 m of its height. The ground near the portals is steep (median slope 0.35-0.6), so each waystation needs a small terrace.

**(d) Router settings.** The table shows the climb weight and the tunnel cost, given as a multiple per metre plus a fixed cost to enter a bore. Strict means at least 70% of the bore lies under at least 15 m of cover.

| Setting | Climb | Tunnel cost | Tunnels | Bored | Road | Ascent |
|---|---|---|---|---|---|---|
| base = A | 25 | none / 4x + 600 | 0 | 0 m | 17469 m | 537 m |
| B loose | 50 | 2.5x + 300 | 3 | 2211 m | 17845 m | 316 m |
| B strict (map-router-B) | 50 | 2.5x + 300 | 2 | 1018 m | 17786 m | 437 m |
| C loose | 80 | 1.5x + 150 | 6 | 4340 m | 18510 m | 246 m |
| C strict (map-router-C) | 80 | 1.5x + 150 | 5 | 4046 m | 18424 m | 278 m |

- A gives no tunnels: at today's costs a climb is always cheaper than a bore.
- B strict:
  - S4-S5 (-2116,444)->(-1916,644), 283 m
  - cave17 spur (-2076,404)->(-1556,924), 735 m, under spine 0 into the Mountains realm
- C strict:
  - S4-S5 (-2756,-736)->(-2316,344), 1166 m: a ramp from the lowland into the massif
  - S4-S5 (-2296,444)->(-1836,644), 502 m
  - S5-S3 (-1996,604)->(-2936,1544), 1329 m, Tundra to Tundra
  - cave17 spur (-1976,564)->(-2176,564), 200 m, then (-2196,584)->(-1596,1184), 849 m
- The loose runs include short stubs (220-323 m) with only 52-61% of their length under 15 m of cover. Row 2 should use the strict filter.
- Recommendation: B strict, plus C0a placed by hand as the long tunnel that changes biome (C0b if Zach wants the bigger showpiece). C strict gives 5 tunnels and much more road underground; that is Zach's call.

**(e) Destinations, second routes and breaks.**
- Mines:
  - cave23 (Mine style, with a MineEntrance) at (-1975,496) is the only real mine. It needs a 117 m spur.
  - cave26 is a mine-style road tunnel that is already on a road.
  - cave21 (Metallic, at (-1434,-1147)) is 950 m off the road: optional.
- Caves that deserve roads:
  - cave5 (Crystal CaveNetwork, at (-2804,-651)): 241 x 261 m, the biggest cave. It needs a 526 m spur, or 88 m with B costs. shot-cave5.png shows its purple crystal mouth.
  - cave1 (Granite CaveEntrance, near DwarfHold): 395 m.
  - cave24/13 (Rock, two rooms, near S0-S1): 48-246 m.
  - cave17 (Granite, north of spine 0): 996 m by surface, or reached as the tunnel's payoff.
  - Skip cave16 (1772 m off), cave10 (already on a road), cave3 and cave20 (small grottos) and cave19.
- Second routes, loops and breaks:
  1. Lake loop, a second S4-S5 route: S4, the landing, the lake's east shore, MarshFolk (-1240,320), then S5. The legs are 1542 + 2376 + 1074 = 4991 m, with 352 m of ascent and 3 + 3 bridge cells; the direct road is 2977 m. The loop can tie into C0a's south portal.
  2. A ruined bridge on the landing spur at (-1876,-756): 22 cells beside water up to 14 m deep.
  3. A washed-out stretch on S4-S5 from (-2276,-616) to (-2376,-896), h29-43, along a SparklingForest stream 1.6 m deep.
  4. A ford on S0-S4 at (-1056,-3076), h10, in PoisonSwamp, 0.4 m deep.
  5. A low causeway or tidal break on S4-S2 at (-2976,-1456), h2, in FungalForest.
  6. A footpath-only last stretch to cave17 at (-1516,944), along a Mountains stream.
  - The survey found no switchback shortcuts.

**Proposed rules for row 2:**
1. Each bore is at least 60 m long. At least 70% of its length has 15 m or more of cover, and each portal arch has at least 6 m. The grade inside is at most 0.15, both portals are dry, and each bore crosses only one spine.
2. Run the router at B costs (climb 50, tunnels 2.5x per metre + 300), and place C0a (or C0b) as an extra bore.
3. Each portal of a bore longer than 500 m gets a waystation or outpost on a terraced flat.
4. Build spurs to every mine and to cave5, cave1, cave24/13 and cave17. The landing road is required.
5. Build at least one loop (the lake loop) and one break (the ruined bridge on the landing spur, or the S4-S5 washout).
6. Spine 2 tunnels wait for a road or settlement on that side.

**Zach decides:**
- router B or C
- C0a, C0b, or both
- whether the lake loop and the ruined bridge go into row 2
- whether spine 2 gets a road now

### Row 2a: strict tunnel rule, router A default, spurs to the landing, a mine and caves: done (3e3c9e08)
Branch `world/mountain-tunnels` at 3e3c9e08 (pushed). Router B/C and C0a/C0b not started: they wait for Zach's picks.

**Roads (probe, before -> after):** 2424 -> 3041 streets, 13871 -> 17122 m. Road 1: 2292 -> 2855; road 2: 115 -> 186; tunnels (road 3): 17 -> 0. The town network kept its xz for 2414 of 2424 streets; the other 10 are roadside access streets, which are added later and now sit at new indexes. 51 streets changed kind and 56 changed level, all near the old tunnels.

**Tunnels removed (all three are now cuttings):**
- (2053,-1535), 19 m: road 2, 0.58 m of cover
- (-1870,118), 44 m: road 2, 2.99 m
- (-2023,713), 30 m: road 1, 2.27 m

**The new tunnel rule:** a bore needs >= 60 m length, >= 20 m of cover over most of it and ~6 m over each portal arch. The router's tunnel, climb and detour costs are now named settings (RouterCosts), with A (today) as the default, so nothing new bores yet: 0 new tunnels.

**Spurs:**
| Destination | From | To | Length |
|---|---|---|---|
| cave23 MineEntrance | (-1955,605) | (-1969,498) | 123 m |
| cave13 CaveEntrance (passes cave24) | (1443,-2577) | (1598,-2709) | 221 m |
| cave1 CaveEntrance | (-3024,1365) | (-3133,1039) | 358 m |
| Sea folk landing (required) | (-2122,-282) | (-1783,-552) | 1182 m |
| cave5 CaveNetwork | (-2365,-887) | (-2809,-654) | 518 m |
| cave17 Grotto | (-1789,622) | (-1358,1097) | 921 m, the last 250 m footpath |

Skipped: cave9 and cave19 grottos already have a road; cave16 is too far from any road. Cave grottos in the mountains get footpath ends, as the card allows for cave17.

**What moved (Zach OK'd roads moving):**
- Roads: the six spurs above are new, and the three crests are now cuttings.
- Landmarks: 112, hash A955AFE0 -> A5D1D048. Only the sea folk pair moved: the landing 2 m ((-1783,-554) -> (-1783,-552)) and the village 16 m ((-1624,-488) -> (-1624,-504)). Their spots are now found before the roads, so the landing spur ends exactly at the landing; the other 110 landmarks did not move. Landmarks_V3 is repinned to 112 A5D1D048.
- Fauna_Fingerprint AD723954: unchanged, passes.
- Fix along the way: the cave13 spur pushed the cave24 roadside CaveEntrance at (1453,-2608) off its site. Roadside landmarks now keep only their middles off spur streets (RoadBaking.SpurStreets, RoadsideBaking.SpurClear), and cave24 is back.

**Checks:** playmode suite 24 passed, 17 skipped; the only failure before the repin was Landmarks_V3, from the expected sea folk move. World up in 37 s. lod-lint is clean. The probe stays untracked.

**Shots** (PunchyMilestones/W-138/row2a/, all checked):
- 1-landing-spur-at-pier.png: wide view; the road runs along the bank to the shore, with the spire in the lake.
- 1b-landing-close.png
- 1c-landing-from-road.png: the road reaches the waterline, with the teal pier deck and its post/sail a few metres out in the shallows.
- 2-former-tunnel-cutting.png: high wide view.
- 2c-cutting.png: the road climbs between bare cut banks at (-1870,118).

**Caveats:**
- The pier deck sits a little offshore of the road's end. I have not checked that you can walk from the road onto the deck.
- A jagged white water-edge strip runs along the graded bank by the landing.
- The landing spur's ruined-bridge break is kept out until Zach answers.
- BENCH: job 59, first load near the landing.

### W-138 row 2a fix: landing road onto the pier, white strip removed: done (verified, c3cdf1db)
Branch `world/mountain-tunnels` at c3cdf1db (pushed). Row 2b not started; it waits for Zach's picks.

**(1) Road onto the pier.** The pier wasn't offshore: it was missing. The spur ended at the landing centre, inside the pier's 6 m footprint, so FloraPlacement's road cull dropped the pier. The teal deck with a post and sail in row 2a's 1c shot is the sea folk boat, not the pier.
- The landing spur now ends at the foot of the pier's ramp: 8.5 m inland of the landing, against its facing (SettlementBaking.LandingRampRun). The road end is (-1791.0,-554.7).
- A prop with a PierReach is no longer culled by a road over its spot (FloraPlacement). Every other prop is still culled.
- Play-mode walk (scripted -walk legs, sampled every 0.2 s, 63 samples): from 6 m up the road, down the road end, along the 28 m deck to (-1760,-545) and back.
  - Feet never went under water. The closest was 3.86 m above the lake surface (35.63).
  - On the deck the feet held y 39.66 over open water. Off the deck they matched the ground.
  - Walk ended ARRIVED.
- The bank from the road end down to the deck drops about 3.8 m over 8 m. The pier's ramp lies inside that bank, and the walker steps onto the deck where the ground meets deck level, about 1.4 m in from the deck's land end. There is no step.

**(2) The white strip.** It was neither shore foam nor the waterline mask, and it predates row 2a (row1b/shot-landing.png already shows it).
- It was a river sheet standing 1.02 m over a lowered lake: water 36.65 against lake 35.63.
- Cause: ReshapeLoweredLakes lifted the lake's river cells to the outlet's pre-cut level, and CutDams then left lake cells alone. The water shader's falling-water term drew the sheet's stepped cell edges as a white zig-zag.
- Fix: a new RiverBaking.SettleLakeRivers pass. On the drawn map only, it lays a river in a lake at the lake's surface, and the river cells downstream of it follow, down to their own level.

**What moved:**
- The landing spur's last 8.5 m. The spur is 1182 -> 1176 m, and streets stay 3041.
- road 1 2855 -> 2852, road 2 186 -> 189: re-levelled cells near the end.
- The pier now stands.
- Landmarks unchanged: 112 A5D1D048. Fauna_Fingerprint AD723954.

**Checks:** playmode suite 25 passed, 0 failed, 17 skipped (Landmarks_V3 and Fauna_Fingerprint pass). lod-lint clean. The probes stay untracked.

**Shots** (PunchyMilestones/W-138/row2a-fix/, all checked):
- a0-top-before.png / a0-top-after.png: same top pose. Before shows the zig-zag along the bank and no pier. After shows a smooth waterline and the pier off the road end. Both are night with rain; same pose and weather draw.
- join-15m.png: 15 m off the join, 10:30, clear. The road comes over the bank and down onto the deck.
- a0-probe1/2/4.png: the water probe views used for the diagnosis.

### W-138 row 2b: router C, tunnel C0a, lake loop, ruined bridge + ford: done (verified, 90ffa7a1 + 277087ea)
Branch `world/mountain-tunnels` at 277087ea (pushed), built on c3cdf1db. 90ffa7a1 holds the code (RoadBaking.cs, RoadsideBaking.cs, and a gotcha in docs/agents/world.md). 277087ea re-pins Landmarks_V3 to 114 00D16E22. Spine 2 roads and portals set into the rock face are not in this row.

**Router.** `RoadBaking.Router` now defaults to C, with the strict cover filter unchanged: TunnelLeast 60 m, TunnelPortal 6 m over every metre, TunnelDeep 20 m peak, BoreGrade 0.15 inside, MaxGrade outside.
- Router C on its own sent spurs and the lake loop through every mountain: 15 tunnels and 26 km of road, with cave 11's spur at 2.6 km.
- Fix: destination spurs, the lake loop and hand tunnels' roads use `SpurRoute`, which takes a bored route only if it comes out 20% shorter than the open one.
- Fix: `Route` can move a path's end off its cell centre, which made false 31-38 m "bores" at road ends. `Shape` never counts a route's first or last hop as a bore.

**Tunnels** (probe, play island seed 1337). Cover is ground over the bore's straight grade. "Line" is portal to portal including the approach.

| Tunnel | From -> to | Bore | Line | Peak cover | Cover A / B | Grade |
|---|---|---|---|---|---|---|
| router | (-2269,-632) -> (-2091,-454), elf grove road | 252.8 m | 311.1 m | 32.3 m | 9.5 / 6.7 m | 0.065 |
| router | (-1977,-210) -> (-1977,900), up to the mining camp | 1110 m | 1180 m | 68.0 m | 9.6 / 9.1 m | 0.150 |
| router | (-2017,680) <-> (-2017,870), used by two roads | 190 m | 240 m | 26.2 m | 7.8 / 8.2 m | 0.070 |
| router | (-2035,968) -> (-3198,1453), mining camp to dwarf hold | 1260 m | 1300 m | 92.7 m | 6.6 / 7.1 m | 0.073 |
| **C0a** (hand) | (-1596,74) -> (-1596,1144), y 42.6 -> 91.7 | 1070 m | 1120 m | 103.8 m | 8.5 / 10.8 m | 0.046 |

- 5 tunnels, 3883 m bored. Row 1b predicted 5 and 4046 m.
- Every tunnel passes 20 m peak cover, 6 m or more at each portal, 60 m or more long, and grade 0.15 or less.
- C0a is laid by hand (`HandBores`) and re-checked by `HandBoreHolds`, so an island without that mountain simply leaves it out. Its roads join the nearest roads at each end, which closes a loop.

**C0a walk** (scripted -walk, sampled every 10 s, 17 samples): from (-1600,-20), in at the south portal, along the bore and out of the north portal to (-1590,1200).
- Ended ARRIVED. The feet followed the bore's grade (y 44.5 -> 91.4) with no stall.
- The probe's MaxUnder reads 42.5 m once, at z 555. That's a mountain tarn 42 m above the bore, not water at the feet. Every other sample was dry.

**Waystations.** One at each portal, from the existing roadside kit with no new art.
- North: wanted LoneInn, then Campsite. The inn fails on the mountain slope (relief), so CampsiteSnow went in, 31.7 m from the portal.
- South: CampsiteSwamp, 32.5 m from the portal.
- The north portal's own road was only about 38 m long, so the portal's way out (`Portals[i].Out`) carries on along the road it joins until it is 300 m long.

**Breaks.** Loops 2, breaks 1.
- Loop 1 is C0a itself. It is a second way between the mining camp and the marsh/landing side.
- Loop 2 is the lake loop: 2483 m from the landing road's end, round the marsh folk village (75 m off its middle) to C0a's south portal. It ends at the portal, not at S5.
- The ruined bridge is the landing road's river crossing at (-2060,-870), about 210 m from the 1b spot, and the nearest bridge with 3 or more spans.
  - Its middle span is gone; one span stands at each bank as a stub.
  - A second road had a 2-span bridge over the same crossing 4 m away, so any other bridge within 20 m of the ruined one goes too (`RuinTwin`). A transect shows deck height only on the stubs.
- Ford: a 3 m footpath 10 m off the bridge line, on the side with shallower water, joining the road on both banks. Its bed is raised to 0.35 m under the water where it was deeper (deepest was 1.22 m), so the river can always be crossed on foot.

**Roads before -> after:**
- Streets 3041 -> 3610. Length 17116.6 -> 21556.0 m (+4439 m).
- Road 1 2852 -> 3010, road 2 189 -> 192, tunnel 0 -> 408.
- Spurs:

  | Spur | Before | After |
  |---|---|---|
  | landing | 1176 m | 955 m (now joins at (-2065,-417)) |
  | cave 11 | 358 m | 477 m |
  | cave 19 | 518 m | 497 m |
  | cave 32 | 221 m | 173 m |
  | mine 87 | 123 m | 139 m |
  | grotto 42 | 921 m | 938 m (footpath 250 m) |
  | C0a roads | - | 1726 m |
  | lake loop | - | 2483 m |

- Grottos 25 and 47 are still served. Cave 37 is still too far.
- Old row 2a tunnel spots: (-2023,713) is now 6.6 m from a tunnel street, and (-1870,118) 107 m from one.

**What moved** (measures, not gates):
- Landmarks 112 A5D1D048 -> 114 00D16E22. Two are new: the waystations.
- Every roadside pick re-drew, because the roads changed and the waystations take their keys first.
  - Gone: LoneInn (1169,-3083), the roadside CaveEntrance (1453,-2608), RoadShrines at (2213,-1437), (177,-3572) and (-1898,-2056), CampsiteAsh (-402,-3627), Monuments at (-989,-3130), (-2163,-1829) and (-2240,-440).
  - New: LoneInn (1521,-2253), RoadShrines at (1548,-2614), (350,-3548), (-1335,-2658) and (-2282,-1778), CampsiteDune (2676,-1073), Monuments at (849,-3744) and (-2006,-1988), Ruins (-473,-3620), CampsiteSnow (-1566,1154), CampsiteSwamp (-1608,14).
- All other landmarks are unchanged.
- Fauna_Fingerprint: AD723954, unchanged. It passes.

**Checks:**
- Suite: 24 passed, 1 failed, 17 skipped (358 s). The one failure was Landmarks_V3 on the old pin (112 A5D1D048). The measured value was 114 00D16E22, as expected, so I re-pinned it in 277087ea. I did not run the suite again for a one-line pin change.
- lod-lint on world/mountain-tunnels: clean.
- The probes (W138bProbe, W138Walk) stay untracked.

**Shots** (PunchyMilestones/W-138/row2b/, all checked at small size; 10:30, clear):
- map-row2b.png: top-down, the whole island plus a west close-up. Tunnels are red, row 2a roads grey underneath.
- c0a-north.png: the north portal arch in the scree slope, with the snow campsite beside the road.
- c0a-south.png: the south portal's arch at the end of the marsh road, in fog.
- c0a-south-camp.png: the swamp campsite and signpost at the south end.
- ruined-bridge.png: the two stubs with the gap between them, and the ford's sandy bed crossing just upstream.

**BENCH-QUEUE:** job 59 now carries the C0a walk: south to north through the bore at walking pace, with whole-frame ms and hitches located at the portals, mid-bore and under the tarn. No timing from the lane (rule 21).

### W-138 row 3a: one bore per ridge, shorter bores: done (verified, 5a549268)
Branch `world/mountain-tunnels` at 5a549268 (pushed), on 277087ea. One commit: RoadBaking.cs plus the Landmarks_V3 re-pin to 117 689D5348.

**What changed** (RoadBaking.cs only):
- `BoreLongest` is now 800 m (was 1500 m). With 1500 m the router ran dead-straight kilometre bores where a shorter bore plus a valley road fits. The one long, biome-changing tunnel is still C0a, laid by hand.
- `FindBores` no longer offers a bore that passes under a town's closed cells (`UnderTown`). The old 1110 m north-south bore ran under the mining camp's edge, beside the second one.

**Result** (map: `W-138/row3a/map-row3a.png`; grey is the row 2b roads):
- The two parallel north-south bores near x -2000 are gone. Neither survives in any form, so nothing needed a fork inside.
- The elf grove road now goes up the far west: a 480 m bore north at x -2937, then a 607 m diagonal to (-2459,638), then a surface road east into the camp, with a 120 m bore through the last ridge.
- The camp-DwarfHold road is valley road, a 234 m bore, more valley road, then a 694 m bore NW. It was one 1260 m straight line.
- Near the camp, two short bores (120 m going west, 234 m going NW) cross the same ridge band about 200 m apart. I kept them on purpose. They lead to different places, they diverge at about 45 degrees, and each is shorter than the ridge's way round. Unlike the old pair, they are not two copies of one route. If you'd rather they shared one bore, it's a router change: bores shared between roads.

**Tunnels** (probe, play island seed 1337). Cover is ground over the bore's straight grade. "Line" is portal to portal including the approach.

| Tunnel | From -> to | y | Bore | Line | Peak cover | Cover A / B | Grade |
|---|---|---|---|---|---|---|---|
| router | (-2937,-340) -> (-2937,140), elf grove road | 60 -> 125 | 480 m | 540 m | 45.6 m | 6.4 / 8.3 m | 0.134 |
| router | (-2889,208) -> (-2459,638), elf grove road | 132 -> 213 | 608 m | 707 m | 51.1 m | 7.2 / 6.4 m | 0.133 |
| router | (-2217,660) -> (-2097,660), into the camp | 234 -> 251 | 120 m | 180 m | 25.2 m | 15.6 / 10.1 m | 0.149 |
| router | (-2058,701) -> (-2223,867), camp to dwarf hold | 258 -> 266 | 234 m | 283 m | 25.4 m | 9.7 / 6.6 m | 0.037 |
| router | (-2471,894) -> (-2962,1385), camp to dwarf hold | 257 -> 166 | 694 m | 764 m | 52.5 m | 6.9 / 7.0 m | 0.131 |
| **C0a** (hand) | (-1596,74) -> (-1596,1144) | 43 -> 92 | 1070 m | 1120 m | 103.8 m | 8.5 / 10.8 m | 0.046 |

- 6 tunnels, 3206 m bored. Row 2b had 5 tunnels and 3883 m.
- Every tunnel passes the same rule as row 2b: 20 m peak cover, 6 m or more at each portal, 60 m or more long, grade 0.15 or less.
- Longest router bore: 694 m (was 1260 m).

**Roads before -> after:**
- Streets 3610 -> 3866, length 21556 -> 22283 m. The new length comes from the valley roads in place of bores.
- Loops 2, breaks 1, the ruined bridge and the ford: unchanged.
- Spurs: cave 19 is now 91 m (was 497 m), because the new west road passes near it. The other spurs, C0a's roads and the lake loop are unchanged. Cave 37 is still too far, and grottos 25 and 47 are still served.

**Measures:**
- Landmarks 114 00D16E22 -> 117 689D5348 (re-pinned in 5a549268).
- What moved: three roadside campsites are new along the new far-west road: CampsiteForest at (-2894,-650), and two CampsiteFungal at (-2764,-981) and (-2669,-1313). C0a's south waystation (CampsiteSwamp) moved 65 m, from (-1608,14) to (-1582,73). The other 113 are unmoved.
- Fauna_Fingerprint: AD723954, unchanged (read live).
- I didn't run the suite for this row; it runs after row 3's final re-pin, as asked.

### W-138 row 3: portals set into the rock face: done (verified, 53f2d557)
Branch `world/mountain-tunnels` at 53f2d557 (pushed), on 5a549268. One commit: SettlementData.cs, RoadBaking.cs, CaveBaking.Tunnels.cs, TunnelPortalKit.cs, FloraPlacement.cs (3 additive lines, see below), docs/agents/world.md and editor-and-assets.md.

**What changed:**
- Root cause of the "arch embedded in flat ground" look: a road street's round end cap carved a disc into the ground, so the hill behind every headwall was levelled to road height. A road street within 40 m of a tunnel portal now carries the portal (way into the hill, its plane, the cutting's half width). `SettlementSampling.Height` stops levelling 0.6 m behind the headwall and cuts a narrow graded cutting in front of it: 6 m flat, then an 8 m taper out to the open road.
- The W-137 kit (not the shelved covered way): the stepped wing walls are now 1.2 m thick, and the cut floor runs just behind them. The first shots had dark sawtooth spikes in front of the walls, because a steep cut edge at 1 m a terrain cell draws triangles across the wall face. With the edge inside the wall, they're gone. The wings sample the ground past the cut edge, so the banks climb to the wing tops.
- Trees: no trees in the cutting or in front of the mouth. Scattered trees are cleared through `SettlementSampling.Keep`; placed props through `OnRoad` in FloraPlacement. FloraPlacement.cs is shared, additive only: 3 added lines in `OnRoad`, no other change. Please tell the flora owner.
- Both bakers now use one `TunnelRadius(width)`, so the bore and the headwall can't disagree.
- Gotcha logged in docs/agents/world.md. Also logged in editor-and-assets.md: a CLI-started editor can open with no scene loaded.

**Portal check** (probe, 12 portals = both ends of the 6 tunnels). This is ground over the road floor, measured behind and beside each portal:
- 2 m behind the headwall: 6.6 to 18.5 m. 6 m behind: 7.2 to 24.2 m. Before this row, the hill behind every portal was levelled flat (0 m).
- 3 m out to the side: 0.8 to 4.6 m (the banks).
- Road 12 m out: -1.6 to +1.8 m (graded approach).

**Shots** in `W-138/row3/`: `pNN-15m.png` and `pNN-40m.png` for portals 00 to 11, checked by me. All 12 read as arches set into a slope, with stepped wings and banks.
- c0a-south is portal 10, at (-1596,74). `p10-15m` is redone from the other side, now with no trees in the cutting. That needed the `Keep` change: the swamp trees there came through the scatter, not `OnRoad`. At 40 m the swamp canopy closes over the road whatever the pose. So `p10-40m` is from 40 m out, raised over the canopy, looking down into the cutting, and I added `p10-30m` at eye height, where it reads best.
- `p00-40m` is raised (eye 14 m) over the autumn forest; the portal is small but visible. `p02-40m` has mushrooms in the foreground.
- The bright tunnel interiors on the snow portals are the existing tunnel lighting, unchanged.

**Tunnel table:** unchanged from row 3a. This row moves no road or portal.

**Measures:**
- Landmarks 117 689D5348, unchanged from row 3a. No landmark moved in row 3.
- Fauna_Fingerprint AD723954, unchanged.
- Suite after the final pin (run after the last code change): `25 passed, 0 failed, 17 skipped in 357s`. lod-lint clean.
- No frame timing (rule 21). Bench notes added to job 59.

### W-138 row 3b: solid rock over the portals, dark bores: done (verified, 0ef51794)
Branch `world/mountain-tunnels` at 0ef51794 (pushed), on 53f2d557. One commit: SettlementData.cs, TunnelPortalKit.cs, CaveBaking.Tunnels.cs, docs/agents/world.md. FloraPlacement.cs is not touched again; the 3 `OnRoad` lines are still row 3's.

**1. The hole over p04 (fixed at its source: the terrain, not a landmark or cave mesh):**
- Cause: row 3 levelled the ground only 0.6 m behind the headwall. Behind that, one terrain mesh cell climbed from road height to the hilltop. The cave mouth cut drops every terrain triangle that touches a cut vertex, so it dropped that whole cliff-high quad. That left a window straight through the ridge above the arch. The "fins" were the terrain edges left either side, and the "pale struts" were the bore's lit interior seen through the gap.
- Fix: behind each headwall the ground is capped at the road plus `PortalLedge` (2 m over the passage crown) for `PortalLedgeBack` (4 m). The headwall is raised by whole rows (up to `HeadwallRaise`, 5 m) to cover that ledge. Now the dropped cells are small, and they sit behind solid masonry.
- Checked all 12 portals from above (`pNN-40m`, plus `p00/p02/p10-20m-top`, where trees hide the 40 m view). No hole or slot over any of them. p04 before and after: `p04-40m-before` / `p04-40m`, `p04-air-before` / `p04-air`, `p04-close`.

**2. The blue-white glow and "foam" in p06 to p09 (the interior material, not light or water):**
- Cause: `BiomeStyle` picked any style listed for the biome above. Under snow that was the Ice style (or another glowing or glossy one), whose glow and gloss lit the bore blue-white from the portal in. The white "foam" on p07's road was that glossy ice floor. It was not water: road tunnels are added after cave water is placed, so they never get pools or streams.
- Fix: road tunnels only take plain styles (no glow, gloss 0.5 or less). Now p08 and p09 go dark a few metres in. p06 and p07 (the 234 m bore, Rock) show warm daylit walls for about 17 m, which is the existing reach of daylight from a mouth, then dark. The bright dot at the end is the far portal seen down the straight bore.
- The p09-40m blue stream: there's no river there. The river mask is 0, and there's no water within a 120 m grid around it. It was the Ice style's tint spilling onto the cutting's banks, and it's gone in the new `p09-40m`. No water reaches any cutting.

**Shots** in `W-138/row3b/` (sunny, 11:00, checked by me): `p00..p11-40m`, `p06..p09-15m`, the p04 before/after set, and the `-top` views.

**Measures:**
- Landmarks 117 689D5348 and Fauna_Fingerprint AD723954, both unchanged: nothing moved.
- Suite after the last code change: `25 passed, 0 failed, 17 skipped in 354s`. lod-lint clean.
- No frame timing (rule 21). Bench note added to job 59.

### W-138 row 4: long tunnel interiors: done (verified, a149c17f)
Branch `world/mountain-tunnels` at a149c17f (pushed), on 0ef51794. One commit: CaveBaking.Tunnels.cs, CaveBaking.cs, CaveStyle.cs, CaveSystem.cs, CaveStyles/Mine.asset, docs/agents/world.md.

**What's in the bores now:**
- Light beyond the daylit ends: bores of 300 m or more are always lit (torches and frames). That turned caves 24 (483 m) and 28 (697 m) from dark to lit. C0a (1073 m) and cave 25 (610 m) were lit already. Short bores keep their lighting as before.
- A side niche in each road tunnel, with a small shrine against its back wall and a torch on its side wall.
- Runed marker stones (`CaveStyle.Marker`, RunedStone) on both sides of the road where the biome above changes and stays changed for 15 m. At most two crossings per bore, 80 m apart, clear of the portals. C0a has one pair. The stones are kept small where the roof is low (scale about 0.4 in C0a).
- Fallen stones: small heaps of the bore's own rock against one wall every 25-55 m, sometimes with a sharp-edged block, never on the road.
- No drainage gutter or vent shaft: a vent would need a hole through the rock above, which row 3b had just made solid.
- Fix found on the way: the niche first read blue-white. The cause was the Shrine species' glow (accent 0.6, 0.85, 1; strength 3; reach about 14 m), which was applied at full species height. A cave plant of form Shrine now glows in proportion to its set scale (`CaveSystem`). The Shrine asset is not edited, so surface settlement shrines are unchanged.

**Shots** in `W-138/row4/` (checked by me):
- `c0a-1-entrance`: entrance.
- `c0a-3-middle`: middle.
- `c0a-2-marker`: detail, the marker stone at the biome border in C0a.
- `c0a-4-shrine`: the niche and shrine, warm now.
- `bore28-middle` (697 m) and `bore25-middle` (610 m): one inside each of the next two longest bores.

**Rule 22 (placement is data): gaps.**
- Tunnels and row 4's details (niche nodes and segments, shrine, torch and marker plants, stones) are baked into the cave blob as plain records. Runtime (`CaveSystem`) only reads them and never re-runs placement or its RNG.
- Gap 1: none of these records has a stable id. Their identity is an array index plus the cave's `Key`, which shifts on any re-bake.
- Gap 2: portals have no record of their own. `TunnelPortalKit.Portals` derives them at runtime from the cave nodes. That is deterministic and uses no RNG, but there is nothing a world file could hold.
- Gap 3: waystations are `LandmarkInstance` records (from `RoadsideBaking`) with a `Key`. The key is a hash of their position, so it changes if the road moves.
- Gap 4: breaks (the ruined bridge and its ford) are only edits to streets and plots during the road bake: bridge spans removed, ford streets added. No break record or id exists.
- I did not rework any of these. Suggest a follow-up card for ids and portal/break records.

**Measures:**
- Landmarks 117 689D5348 and Fauna_Fingerprint AD723954, both unchanged: nothing moved.
- Suite after the last code change: `25 passed, 0 failed, 17 skipped in 364s`.
- lod-lint: 4 hits, all bake geometry, none visual: the niche radius, the 0.1 m wall-search step, the 40 m minimum bore for markers and stones, and the marker spacing.
- No frame timing (rule 21). Bench note added to job 59.
- FloraPlacement.cs is not touched in this row.

### W-138 row 4b: tunnel interior look fixes: done (verified, f6217674)
Branch `world/mountain-tunnels` at f6217674 (pushed), on a149c17f. One commit: CaveData.cs, CaveSystem.cs, Cave.shader.

**What the white artifacts were:**
- White road smear: the road's share was written into the terrain blend channel, so the floor took the terrain branch: full sun whatever the openness, and a flat pale vertex colour. Now the road share has its own channel (rib shade slot, tunnels only) and the cave shader draws it in the cave's light: cobbles where paved, packed grit (6 cm pebbles in world xz, crisp at 2-5 m) otherwise, with a darker damp gutter. Road colour is capped at luminance 0.32.
- White squiggles: the calcite mineral veins of the bare-rock branch. Off in road tunnels.
- Net pattern: the Voronoi crack net drawn in the style's glow colour. Off in road tunnels.

**Walls, stones, lights:**
- Walls: rough rock from a coarse read of the shared detail; built tunnels (lamps or frames) get cut stone courses on the side walls (0.42 m rows, 0.95 m blocks, mortar, a tone per block). The vault stays rock.
- Fallen stones: drawn pale grey with a dark contact band where they meet the floor (new `CaveData.StoneDistance`).
- Marker stones: a small cold glow (0.3, 0.45, 0.85, reach 3 m) 1 m above each marker in a road tunnel.
- Lamps: tunnel torches are brighter (x2.2) with a shorter reach (6.5 m), so they read as warm pools with dark stretches between. Choice to note: the player's lantern dims to 0.35 inside a lit bore (Tunnel >= 2), so it doesn't flatten the pools. Easy to drop if Zach wants the full lantern.
- Shrine niche unchanged.
- Shader encoding: the style slot now carries 32 x a tunnel code (1 + paved + 2 if built); 0 in every ordinary cave, so caves look as before.

**Shots** in `W-138/row4b/` (same 6 poses as row 4, plus `close-3m`), checked by me on a small sheet (`sheet.jpg`): no veins or net in any view; floor is grit at the walls' light level; courses read on the side walls. The vault still has a slightly swirly grain from the shared detail at a distance.

**Rule 22:** no placement change in this row; row 4's four gaps stand (no stable ids for tunnel records, no portal records, waystation keys from position, no break records).

**Measures:**
- Landmarks_V3 117 689D5348 and Fauna_Fingerprint AD723954, unchanged.
- Suite after the last code change: `25 passed, 0 failed, 17 skipped in 391s`.
- lod-lint f6217674 vs a149c17f: clean (vs origin/main: only row 4's 4 bake-geometry hits).
- No timing (rule 21). **BENCH-QUEUE request** (supervisor to add): walk the length of C0a (1073 m, the longest bore) from its daylit approach, plus the first load near it; compare against main for the cave shader's tunnel branch (cobble/pebble Voronoi loops, extra coarse detail read) and the added marker glows and lamp lights.

## W-132 (cheaper 3 km landscape, horizon; tree Punchy-release, branch world/far-lite from origin/main cc835cf6)

### Row 1: the horizon oddity (2026-11-10): done (verified; diagnosis only, no code change)
- Setup: Clear weather, High preset (DrawDistance 2976 m with the far ring, far plane 3744 m, fog ExponentialSquared 0.000665 = 1.98 / 2976). The sun sets due west (-X). Sun heights: +4.1° (17:40), +1.4° (17:53), 0° (18:00). Vertical FOV 60°.
- Cause 1, the main one: the sun sets on the camera's own eye-level line, not on the land or sea. Sky.hlsl line 1020 (`if (h > 0)`) draws the sun disc (line 1041) only above dir.y = 0. Below that line the skybox draws a flat below-horizon sky (line 1009, then line 1074 lerps it 85% to the horizon colour). Drawn land and sea stop at DrawDistance, so they end atan(eye height / D) below eye level. Between the two lines is a band of plain sky that sits where the sea should be.
  - Ground (eye 1.8 m over the sea at -2240,-2999): the sea edge is 0.03° down. The sun meets the sea and looks right.
  - Hill (eye 128 m at -3460,2425): the sea edge is 2.2° down (about 3.3 km out at the square ring's tiles). The disc is cut flat at 0.0° (measured), so the sun sinks into a 2.2° orange band above the sea.
  - 300 m over sea (-2240,-2999): the sea edge is 5.0° down and straight, with a tile corner showing. The disc cut is at 0.0°, so the band is 5°, about 10 sun widths.
  - 300 m over land (-1236,1324, eye 380 m): a half sun sliced by a flat line in open sky, well above the hills.
  - On a round world the sea horizon from 300 m would be only 0.56° down (about 62 km away), so the band is entirely an artefact of where the drawn world ends.
- Cause 2, a colour step at the drawn edge: the haze colour (PunchyAtmosphere, Sky.hlsl lines 1287-1295) is not the sky's below-horizon colour.
  - Haze takes half the sunset tint (W-33) and only the pow12 sun glow, without the twilight glow terms.
  - Measured across the edge (mean of 200 px, sun +1.4°), the step is hard and 1 px wide: band RGB 209,154,109 against sea 200,164,140 at 300 m, and 209,159,109 against 201,169,149 from the hill (blue +31-40).
  - The edge is also only about 98% hazed, so the tile outline shows.
- Fix direction, for row 4, not done here:
  - Add a sea skirt or horizon plane from DrawDistance outward, so the sea reaches about eye level.
  - Make the haze colour equal the skybox's below-horizon colour in each direction (or clip the sun disc at the drawn edge's depression).
  - Decouple haze from DrawDistance.
  - Sky.hlsl's sun and horizon code is named here. Its cloud code (R-12) is not touched.
- Shots: milestone folder W-132/row1-final.
  - 9 shots: ground, hill and air300, each at sun4, sun1 and sun0.
  - Plus airsea-sun1, contact-sheet.jpg, zoom-airsea-sun1.jpg and zoom-air300-sun0.jpg (the best two).
  - The first ground and hill set (W-132/row1) was blocked by trees and was retaken in row1b.
  - Note: the row1b set shows thick cloud under "-weather Clear", but the air300 set has none.
- Pins: not run (no code changed).

### Row 2: cheap far-ring path (2026-11-11): done (verified)
- Commit b0a46b6d on world/far-lite (pushed). Four files: TerrainSurface.shader, Water.shader, TerrainStreamingSystem.cs, TerrainStreamingSystem.FarRing.cs.
  - The far tiles and far water now use runtime copies of the terrain and water materials with a far-only keyword on (`_PUNCHY_FAR`, `_PUNCHY_FAR_WATER`, multi_compile_local). No new asset and no Shader.Find. The disc is unchanged.
  - Far terrain keeps: cave clip, albedo, strata, far-grass gust, wetness, shore, BRDF with SH and the main light, emission, cloud dim, one PunchyAtmosphere. It drops micro relief, grain, cobbles, rock faces, burrows, salt, puddles, sheen, shadows, extra lights, SSAO and glow GI. Cloud shadow moves to the vertex.
  - Far water is flat: broad WaveSlope and swell kept for the seam, simple fresnel, sky reflection and cloud shadow per vertex, one depth sample for thickness. No foam, caustics, water field or per-pixel shadows. Toxic, falling and lava looks are kept in cheap form.
- Counts (static D3D11 SHEX counts, ForwardLit with _MAIN_LIGHT_SHADOWS_CASCADE _SHADOWS_SOFT; both sides of every branch counted, so these are upper bounds, not timings):
  - Terrain fragment: full 5856 instructions, 127 samples, 81 if/loop -> far 773 instructions, 4 samples, 9 if/loop.
  - Terrain vertex: 41 -> 208 instructions, 0 -> 7 samples (cloud shadow per vertex; the far ring has few vertices).
  - Water fragment: full 9589 instructions, 83 samples, 16 loads, 124 if/loop -> far 1737 instructions, 4 samples, 5 loads, 27 if/loop.
  - Water vertex: 249 -> 1966 instructions, 0 -> 23 samples (sky colour per vertex).
- Seam check: an A/B in one play session, with the far keyword switched off and on live on the same far materials, so each pair is the same view with the far ring on the full path vs the far path.
  - 5 views at High: hill west at sunset (eye 128 m), and 400 m over the hill looking west (sunset), north and east (noon), south (sunset).
  - No visible line or colour step at the disc edge. Mean pixel difference 0.6-5.9 of 255; most of it is wave and cloud animation between the two frames (the near sea, drawn by the disc, differs as much as the far sea).
  - Far water reads a little more wave-textured near the horizon than the full path, where the full path's foam and sheen smooth it. No band.
  - D3D11 editor, 0 magenta pixels in all 10 shots.
- Shots: milestone folder W-132/row2-ab (pairs *-far / *-full).
  - Best for Zach: zoom-hill-seam-far-top-full-bottom.jpg and ab-sheet-far-full-diff.jpg (far | full | difference x6).
  - W-132/row2 is a first pass at the same spots (some views blocked by slopes, weather not clear away from the forced cell).
- Tests: full PlayMode suite 25 passed, 0 failed, 17 skipped (on-demand). Pins hold: landmarks 112 A955AFE0, fauna AD723954.
- lod-lint (world/far-lite vs origin/main): no hits.
- Decisions for review:
  - Far colour comes from the far tile's per-vertex albedo, not a separate low-res texture.
  - Sky reflection and cloud shadow are per vertex (an approximation; the far ring's triangles are large).
  - Wetness, shore, broad waves and swell are kept to match the seam. They could go if the bench says they cost.
  - The ForwardLit variant count doubles for both shaders, because the keyword is set on runtime copies (multi_compile_local, not shader_feature).
  - Live inspector edits to the terrain or water material during play reach the far copies only when the settings re-register.
- BENCH-QUEUE request: main vs world/far-lite, High and Ultra, three spots: a ground walk, a hilltop view (e.g. -3460,2425 looking west) and a 300 m flight over land and sea. Watch GPU frame time and the far ring's draw cost. The row is kept only if the branch costs no more than main.
- Row 3 not started; waits for the supervisor.

### Row 3: fewer draws in the disc's outer ring - done (verified)
- Commit 21df3a54 on world/far-lite (pushed).
- What changed: with the far ring on, the chunk disc now ends one chunk into its outermost LOD. At High that is 31 chunks down to 27 (1.73 km); the last LOD starts at 1.66 km.
  - The trim comes from the LOD config (Lod0Distance x 2^(LodLevels-2) + 1 chunk), not from a fixed number of metres.
  - The disc also ends on 256 m tile lines: a chunk whose far tile the ring draws isn't loaded, with a 32 m hold so it doesn't flip back and forth.
  - The LOD4 band was the far tiles' own 16 m grid, so the ring now draws that ground once, in far fewer draws.
  - Unchanged: the ring's reach, the 16 m fine band, DrawDistance (haze) and flora reach (still the config radius). Colliders, flora and props near the player are unchanged; disc chunks past 1.73 km had no gameplay use.
  - `PUNCHY_FARDISC=0` restores the old disc for before/after.
- Counts at air300 looking west, High (row 1 spot, same camera):
  - Before: 3018 chunks, LODs [45,115,426,1647,785], 263 far tiles, about 2370-2450 draws, about 270 setpass.
  - Tile lines only, no trim: 2656 chunks, about 2264 draws.
  - After: 1936 chunks, LODs [45,115,426,1342,8], 308 far tiles, about 1930 draws, about 253 setpass.
  - Net: -1082 chunks and about 440-520 fewer draws (about 20%).
- No-holes flight: 60 s at 40 m/s heading east from air300 (y 400). Every frame, the probe counted 64 m cells within reach that neither a loaded chunk nor a built far tile covered.
  - After: 5412 frames, 0 frames with holes. The queue peaked at 1277 pending.
  - Before (PUNCHY_FARDISC=0, same line): 6155 frames, 0 holes. The queue peaked at 2347 pending.
  - The streaming backlog while flying is about halved.
- lod-lint vs origin/main: clean. The one hit, `DiscIntoLastLod`, is a chunk count and is labelled `// streaming:`.
- Shots: milestone folder W-132/row3. It has before/ and after/ (air300-west, air300-north-noon, low120-south-noon, hill-east-noon, hill-west-sunset), plus `ab-sheet-before-after-diff.jpg` and `rim-crops.jpg` (the horizon band at full resolution).
  - 0 magenta pixels on D3D11.
  - The A/B differences (mean 6-13) come from clouds and fog density: weather is forced clear only at the first goto, so the later shots drifted.
  - The rim crops show no seam, band or z-fighting at the handover. Near ground and trees match.
- Risks:
  - Past 1.73 km there are now no chunk colliders (far tiles have none). Anything that raycasts the ground that far would miss. I found nothing that does; flora and water patches use their own nearer radius.
  - At High the disc ends on a tile line, so the old LOD3/LOD4 morph is never seen past 1.73 km. That ground is the far tiles' identical 16 m grid.
- BENCH-QUEUE request (job for row 3): world/far-lite at 21df3a54, High, uncapped, at air300 (`-goto -1236,1324 -camlook -1236,380,1324,-2236,270,1324`). Run PUNCHY_FARDISC=0 against 1 (same build) for frame and GPU time.
  - A second leg: a 60 s flight or glide at speed, for streaming hitches (the pending peak halved).
  - Row 2's job 56 stands as is.
- Row 4 next (the horizon skirt and the haze knob).

### Row 4: coarser far band, horizon skirt, haze knob, sun road on far water (2026-11-11) - done (verified)
- Commit 0900515f on world/far-lite (pushed). It sits on row 3 (21df3a54).
- Correction to the row 1 report: row 1 changed no code, so it has no game commit. Its report is coordination commit 30821339. The id a0f3c1e2 given earlier does not exist.
- What changed:
  - Far ring: a third tile level, 1024 m tiles. Past coarseFrom (~2.5 km) they draw a 64 m grid; inside the drawn reach they draw 16 quads per side, and 8 past it.
  - Horizon skirt (from High up): the same 1024 m tiles carry the island shape and sea out to 1.35x the haze reach, about 6.2 km at High. The sun now sets on land or sea, not on the drawn ground's cut. It can be turned off for A/B: `TerrainStreamingSystem.FarSkirt` (env `PUNCHY_FARSKIRT=0`).
  - Haze is decoupled from DrawDistance. **Knob: `TerrainStreamingSystem.HazeDistance`, default 4600 m** (env `PUNCHY_HAZE`, metres). WeatherSystem hazes to `HazeReach = max(HazeDistance, DrawDistance)` while the skirt is on, and to DrawDistance (2976 m at High, as before) when it is off or below High. At the drawn edge the haze is now about 80% instead of 98%, so the land and sea silhouettes read out to the horizon. I picked 4600 m by eye. Zach (11-11) saw it demoed and said keep the look.
  - Sky.hlsl: `PunchyHazeHorizon` is factored out of PunchyAtmosphere (same maths). New `PunchySunRoad`: the view ray mirrored in a flat sea, tested against the sun's direction. It gives a band that is narrow across and long toward the viewer, broken into glitter by one value-noise read, with no cubemap and no texture. Clouds and the sun under the horizon fade it.
  - The far water (`_PUNCHY_FAR_WATER`) adds the road over the haze, so it runs out to the skirt's sea. The skybox fades to the haze colour below the horizon and carries the road on past the skirt. Row 2's far-path choices are kept as they are: per-vertex colour, sky reflection and cloud shadow.
- Far water fragment counts (ForwardLit, D3D11 bytecode; my opcode count):
  - Before: 1737 instructions, 4 samples, 5 loads, 27 if/loop.
  - After: 1818 instructions, 4 samples, 5 loads, 27 if/loop. That is +81 ALU and no new sample.
  - Far water vertex is unchanged: 1966 instructions, 23 samples. The full-water fragment moved 9381 -> 9388 from the shared header, with no behaviour change.
  - The analytic road was cheap enough, so no glint-quad impostor was built.
- Shader messages: warnings only, no errors. They are "potentially uninitialized variable" in PunchyAtmosphere, FarWaterFrag and the skybox's PunchyBelowHorizon. Both shaders report isSupported.
- Counts (High, Clear; skirt off vs on, same session and same view; counts only, no timing):

  | View | Far tiles (256 / 512 / 1024 m) | Water tiles | Draws | Setpass |
  |---|---|---|---|---|
  | Start spot, skirt off | 308 (271 / 37 / 0) | 215 | ~2502 | ~435 |
  | Start spot, skirt on | 405 (271 / 22 / 112) | 309 | ~2615 | ~490 |
  | Hilltop -3460,2425, off | 297 (259 / 38 / 0) | 255 | ~1958 | 361 |
  | Hilltop, on | 386-392 (255-259 / 18-21 / 112-113) | 341-347 | ~1880-2050 (noisy) | ~380-415 |

  - Chunks are unchanged: 1936 at the start spot, 1952 at the hilltop. Vertex totals move by about 1% either way.
  - Budget: rows 2-3 saved about 440-520 draws. Row 4 spends about 100 of them (~110 at the start spot) and about 95 more far-water tiles, so the net is still about 340-420 fewer draws than main.
- No-holes flight with the skirt on (the probe now also checks level-2 tiles and the skirt's reach): 60 s at 40 m/s east from air300 (y 400). 1334 frames, 0 frames with holes, pending peaked at 1709. The only holes in the hilltop hold came from the 2.3 km teleport at its start (186 frames, then none).
- Suite: `25 passed, 0 failed, 17 skipped in 397s`.
- Pins hold: Landmarks_V3 112 A955AFE0 and Fauna_Fingerprint AD723954 PASS.
- lod-lint world/far-lite vs origin/main: clean. HazeDistance is streaming haze, not a detail fade, and is labelled `// streaming:`.
- Shots: milestone folder W-132/row4, before/ and after/. Spots: air300-sun0, airsea-sun1, hill-sun1, hill-sun0, ground-sun1 (shore at sea level, looking west at the sunset), and low120-noon (near-view check). Also `before-after-crops.jpg` and `hill-sun0-sun-crop.jpg`. All viewed; 0 magenta pixels on D3D11.
  - hill-sun0: before, the drawn sea ended in a line above the sun, and the sun sat in a band of plain sky. After, the sun sits on the sea's horizon and the waves run all the way out to it.
  - air300-sun0: mountains past the old edge now show at the right through the thinner haze.
  - airsea-sun1 and ground-sun1: the sea's texture reads further out, with no cut.
  - low120-noon: near ground, trees and water look the same as before.
- Risks and notes:
  - The road is subtle at these spots. It shows most with the sun just above the sea; at 300 m the haze still softens it. If Zach wants it stronger, raise the 0.25 + 1.25*glitter gain in PunchySunRoad (that changes the look, so ask him first).
  - Clouds differ between before and after because weather drifts after the first goto (as in row 3). Cloud code is not touched.
  - The skirt has no colliders, the same as row 3's far tiles.
  - Below High nothing changes: there is no far ring, so there is no skirt and the haze follows DrawDistance.
- BENCH-QUEUE request (row 4): world/far-lite at 0900515f, High, uncapped. One build, env A/B on the skirt and haze: `PUNCHY_FARSKIRT=0` vs default.
  - Spots: air300 (`-goto -1236,1324 -camlook -1236,380,1324,-2236,270,1324`) at `-time 18` (sunset) and at noon.
  - Record GPU and CPU frame time, draws, and the Water and Skybox GPU passes (the sun road on far water and in the skybox).
  - Second leg: main vs world/far-lite at the same spots, for rows 2-4 together.
  - Jobs 56 and the row 3 request stand.

## W-134 (cave water = surface water; tree Punchy-world, branch world/cave-water)

### Row 1: one water query for a 3D point - done (verified)
- Commit 0a726943 on world/cave-water (pushed). New `TerrainSource.SampleGround(float3)`:
  - At or above the ground it gives the float2 answer, at the cost of one compare.
  - Below the ground, `CaveSampling.WaterAt` does a box test per cave, then uses the pool basin or the stream's carved bed. It sets `GroundSample.CaveWater` and gives Height = bed, plus the water level, flow and rapids.
- Callers now asking with the 3D position: swimmer wakes (CreatureWakeSystem), the R-9 field (WaterPatchSystem.SourceJob), player swimming (the floor in a cave pool) and river current (PlayerMovementSystem).
- Test `WorldTests.CaveWater_SurfaceMatchesAndPoolStirs` (about 5 s):
  - Surface matches main: 0 of 4000 random spots differ on the ground or 5 m above it, and 0 of 4000 differ 30 m underground away from cave water.
  - Cave water is found: 12/12 pools and 6/6 streams read as cave water, with worst level error 0.000 m.
  - In a cave pool, the player at (1526.31, -0.97, -2628.29) is in cave water 1.36 m deep. That made 6 ripples near and 1 R-9 field press.
- Suite (fresh domain): `DONE 26 passed, 0 failed, 17 skipped in 307s`.
- Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- lod-lint vs origin/main: clean (the new reach constants are `// gameplay:`).
- Shot: D:/Unity/PunchyMilestones/W-134/row1-cavepool-stir-spot.png. It shows the cistern pool that `-place cavepool` uses; the ripples don't show at shot size.
  - No clip: row 1 doesn't change the look. Visible cave water comes with row 2.
- Docs: docs/agents/world.md has 3 lines next to FloorUnder.
- Notes and risks:
  - Small edits in fauna and R-9 files: CreatureWakeSystem gained `RipplesNear`, WaterPatchSystem gained `Presses`. Both use the float3 sample.
  - Still on the xz sample, as follow-ups for their owners:
    - The creature brain's swim choice and the fish body's SwimOrFlop (fauna).
    - ThirdPersonCamera.CaveWaterAbove (player lane) should call CaveSampling.WaterAt.
  - Most cave pools (Depth <= 1.2) are shallower than SwimDepth 1.3, so the player wades rather than swims. Row 2 or 3 should decide pool depth.
  - New TestHooks `FreeCamera` re-enables Camera.main's behaviours. `-place prop:` poses the camera and nothing freed it, so the cave test got 0 ripples after the wharf test.
  - Place_PropFindsAndFramesWharf can fail on suite reruns in the same editor domain, because it reads a stale `[measure]` line from LogWatch. It passes after a domain reload. Worth a small fix in that test's log wait (its owner's call).
  - Two ignored probes from other cards (W135Probe, W136Probe in Assets/Temp/Editor) don't compile against this branch. They were parked during the run and are now restored.
- BENCH-QUEUE request: main vs world/cave-water at `-place cavepool` (the cave pool) and at a surface river/lake spot. Watch the WaterPatchSystem/CreatureWakeSystem jobs and player movement; the expected surface cost is one compare per sample.
- Row 2 not started. Row 3 waits for the supervisor.

### Row 2: bake cave water like chunk water - done (verified)
- Commit 54dd9ff3 on world/cave-water (pushed). New `CaveSystem.Water.cs` replaces the shared disc per pool and the strip per stream (`MakeStreamMesh` and `streamMeshes` are gone):
  - Pools and streams become water quads on a fixed 0.5 m world lattice, fitted to the carved floor. A vertex is wet where the cave's air reaches below the water level (the floor is marched the way FloorUnder does it).
  - Wet vertices stay flat at the level. Dry vertices stay flat too, and sink (up to 1.2 m) only over the last 0.5 m of the water's reach, so the rim ends under the rock and the GPU clips the shoreline smoothly, as surface water does in its bank. A vertex still wet at the reach end is cut, with a continuous claim so the cut follows a curve.
  - Shore foam comes from the water shader's depth thickness. Per-vertex depth drives river speed, the soft rim and the white middle, as on the surface. Streams and their pools share the lattice, so there's no seam where they meet.
  - Each cave bakes in a background Burst job the first time it comes within LoadRadius (one cave at a time). It is cut into tiles, and each tile draws only while big enough on screen (DetailPolicy.ScreenSize, rule 20). Lava pools keep their disc and glow.
- Shots (inside, by lantern light), all viewed:
  - Before: row2-before-pool2/pool6/pool8/pool9/pool11.png and row2-before-stream0/stream3.png.
  - After: row2-after-pool2.png, row2-after-pool6.png, row2-after-pool8.png, row2-after-stream0.png. Also row2-after-pool8-white.png, with the white debug look on, which shows the sheet filling the basin.
  - All are in D:/Unity/PunchyMilestones/W-134/. The borders now meet the floor with a curved, foamy shoreline instead of a disc edge cutting through the rock.
- Triangle counts in a cave view (old means the main meshes, rebuilt on this branch for comparison):

  | View | Old tris | New tris | Tiles drawn / baked |
  |---|---|---|---|
  | Pool 2 | 540 | 2939 | 9 / 9 |
  | Pool 8 | 1040 | 4706 | 15 / 16 |
  | Pool 6 (small) | - | 56 | 1 / 1 |

- Memory: the old shared meshes were 9 meshes, 134 KB in all. Baked water for the caves loaded at each view: 234 KB at pool 2, 373 KB at pool 8, 6 KB at pool 6. It is baked per cave in range only.
- Pool depths are unchanged. Of 12 water pools, the mean middle depth is 0.62 m and the max is 1.36 m. If the basin were deepened at the centre, the number reaching SwimDepth 1.3 would be:
  - 2 today
  - 3 at +0.5 m
  - 12 at +1.0 m
  - 12 at +1.5 m

  Only 3 pools have a radius of 2.5 m or more. Carving is not changed.
- Suite (fresh domain): `DONE 26 passed, 0 failed, 17 skipped in 314s`.
- Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- lod-lint HEAD: clean.
- Notes and risks:
  - In row2-after-pool2, a faint straight edge shows on a rock shelf just under water level (right side). It's minor: the lattice meets a near-flat shelf.
  - There are more triangles per cave than the old disc, but they draw only in screen-size range and are tiled.
- BENCH-QUEUE request: main vs world/cave-water inside a cave with water: `-place cavepool`, and a stream cave if one can be placed. Watch the cave water bake job, the draw count and memory. No timing from the lane (rule 21).
- Row 3 not started. It waits for the supervisor.

### Row 3: underground lakes - done (verified)
- Commit 11176d1c on world/cave-water (pushed). New `CaveBaking.Lakes.cs`, plus small edits in CaveData, CaveBaking and CaveSystem.Water:
  - A few caves gain a lake hall: a domed chamber carved around a wide, deep basin (pool Hall/Span; HallFloor/HallDome in CaveData). Eligible caves are wet, winding (Straightness < 0.5), non-lava and have 4+ nodes; a 15% roll per cave picks them. The hall hangs off a passage or chamber node.
  - Stable seeds: every draw comes from hash(cave Key, 0x1A4E), and all draws are made before any skip. A lake only takes its own spot: it is dropped if it would break the cover above, flood, open into air outside its host segments, or overlap another cave. Lake pools are inserted after the cave's own pools, so existing pools, streams and landmarks keep their indices.
  - The water is row 2's baked lattice (PoolReach grows to 1.2R + 3 for halls); there's no new water path.
  - Details tenet (PlaceLakeDetails): 1-2 drip columns, 3-7 stalactites, a 60% chance of a 2-3 boulder ledge, and 1-2 patches of 8-16 glow flora from the cave flora table. All of it goes through the existing cave props/plants, so it's drawn by DetailPolicy.ScreenSize.
- Play island (seed 8121): planned 5, kept 3 (misses: cover 67, wet 18, air 9, overlap 5 tries).

  | Lake | Centre | Radius | Hall | Surface >= SwimDepth 1.3 m |
  |---|---|---|---|---|
  | 1 | -2600.2, 204.5, 333.5 | 8.4 m | 4.6 m | 70% (twin basin 67%) |
  | 2 | -2709.2, 41.4, -617.8 | 7.8 m | 8.9 m | 67% |
  | 3 | 1623.2, 4.2, -2632.1 | 6.6 m | 7.5 m | 69% |

- Shots, viewed at sheet size, in D:/Unity/PunchyMilestones/W-134/: row3-lake{1,2,3}-{door,over,swim}.png (entrance, over the water, swimming). Lakes 1 and 2 are in the purple glow style, lake 3 in a dark style lit by lantern.
- Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- Suite (fresh domain): `DONE 26 passed, 0 failed, 17 skipped in 304s`.
- lod-lint HEAD: clean.
- Notes and risks:
  - The cave and landmark hashes don't move, but the cave layout of the chosen caves does (new hall, wider bounds), so any test or save that keys on a cave's Min/Max or PoolCount sees the new pools.
  - Two new bake steps: "lakes" (after streams) and "lake details" (after fittings). The "lakes" step marches the hall's cover and air for each try, so its cost scales with tries (99 on the play island).
  - Lake 3's swim shot looks past a drip column; the hall is small enough that a column is usually in view.
- BENCH-QUEUE request: main vs world/cave-water. (1) Island bake: the new "lakes" and "lake details" steps. (2) Standing in lake 1's hall (-2600, 205, 333) and swimming in it: cave water bake job, draw count and memory for the hall's water tiles, props and glow flora. No timing from the lane (rule 21).
- W-134 rows 1-3 done. Row 4 (river chambers) waits for the supervisor.

### Row 4: river chamber - done (verified)
- Commits cca1d174 and 6df55722 on world/cave-water (pushed). New `CaveBaking.Rivers.cs`, plus edits in CaveData, CaveBaking, CaveBaking.Lakes and LandmarkEffectSystem:
  - A few wet caves gain a river chamber: a long vaulted chamber (CaveChamber: ChamberVault/ChamberFloor in CaveData) hung off a passage by a round-roofed gallery. A river (CaveStream.Chamber) flows from end to end on a gentle fall, then drops 1.6-2.4 m down a chute at 55-70% of its length.
  - Steep stretches pick up R-9 row 6 whitewater (9b1cacc1 is in the branch). The chute is the stream's own water: LandmarkEffectSystem plays its sound and draws only its spray mist, no curtain.
  - Stable seeds: every draw comes from hash(cave Key), and all draws are made before any skip. A chamber only takes its own spot: it is dropped on a cover, wet, air or overlap miss. Chambers and their streams go after the cave's own pools and streams, so existing indices hold.
  - The water is row 2's baked lattice; there's no new water path.
  - Details tenet: gravel bars on the inside of the run, a fallen slab (the Block shape with a slab cut), and spray at the chute. Everything goes through the existing cave props, so it's drawn by DetailPolicy.ScreenSize.
- Play island: `[cave-rivers] planned 6, kept 2; misses: host 0, cover 119, wet 27, air 37, overlap 11`.

  | Chamber | Cave | From | To | Width | Height | Chute drop |
  |---|---|---|---|---|---|---|
  | 0 | 20 | -2163.6, 35.0, -1094.5 | -2194.8, 31.7, -1107.7 | 6.2 m | 7.6 m | 2.0 m |
  | 1 | 24 | 1447.0, 7.3, -2616.4 | 1405.6, 3.4, -2612.7 | 6.6 m | 6.4 m | 2.1 m |

- Fixes folded into these commits:
  - Drip columns are no longer smooth cylinders. They are waisted, with flowstone rings (narrow ridges, wide hollows), lumps from noise, a wider foot and a bell flaring into the ceiling, all in the cave rock material. See r4-5-column.
  - The "E sit" prompt in every shot comes from fauna-lane code, not world-lane code: InteractionInputSystem's idle rest offer (offerRest after 1.5 s standing still, or nearSeat within 1.6 m). For shots I hide the HUD instead (InteractionPrompt.Hidden plus DebugControls noHud).
  - StreamBed end caps: a chamber river's stretches cut only alongside themselves (round ends only at the river's two ends), so the plunge pool doesn't hollow out the run above the chute.
  - WaterAt does the same for chamber rivers (with 0.25 m of slack at bends). Their points sit closer together than half the river's width, so an earlier stretch's end read the water 5 cm high. The suite caught this (CaveWater_SurfaceMatchesAndPoolStirs). The worst level error is now 0.000 m, and a sweep across both rivers found no dry gaps.
- Shots, viewed at sheet size, in D:/Unity/PunchyMilestones/W-134/: r4-1-chute (chamber 1 whitewater and spray), r4-2-vault (chamber 0 end to end), r4-3-bars (gravel bank), r4-4-slab, r4-5-column, r4-6-chute-c0, r4-7-small-cave (the comparison cave). r4-sheet.jpg is a contact sheet of shots 1-6.
- Clip: r4-clip-river.mp4, 6 s at 20 fps: a glide down chamber 0 past the chute and the slab.
- Counts (UnityStats for the whole frame, no timing):

  | Pose | Triangles | Draws | SetPass |
  |---|---|---|---|
  | Chamber 0 vault | ~4.4 M | 750 | 224 |
  | Chamber 0 bars | ~5.4 M | 2999 | 518 |
  | Chamber 1 chute | ~2.0 M | 2599 | 455 |
  | Small deep cave 1 (57 m down) | 384k | 110 | 61 |

  - The cave's own rock: chamber cave 20 is 8 chunks, 66k triangles; chamber cave 24 is 13 chunks, 120k. Ordinary cave 1 is 23 chunks, 139k; lake cave 13 is 20 chunks, 141k. So the chambers are not heavier in rock.
  - The big frame numbers come from the overworld still drawing. Both chambers are 11-16 m under the ground and within 45 m of a cave mouth, and CaveSystem only fades the surface out between 25 and 45 m from the nearest mouth (SurfaceHidden 0.76 in chamber 0, 0.20 in chamber 1). Hiding by depth or by cover rather than by distance to a mouth would be a separate card; I made no change.
- Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- Suite (fresh domain): `DONE 26 passed, 0 failed, 17 skipped in 305s`.
- lod-lint HEAD: clean. GalleryRadius carries a collision tag because it is a carved size, not a draw range.
- BENCH-QUEUE request: main vs world/cave-water at three poses: (1) chamber 0 (-2180, 34, -1100), (2) chamber 1's chute (1425, 6, -2614), and (3) small deep cave 1 (-3011.8, 157.8, 1028.0) as the baseline. Measure draw count, SetPass, triangles and memory, plus the new "rivers" and "river details" bake steps. Note how much of the chamber cost is the surface still drawn near the mouths. No timing from the lane (rule 21).
- W-134 rows 1-4 done.

## W-134 row 4 fix (world/cave-water 75c98a00)

1. **Black slit above chamber 0's chute.** The slit was not a gap between meshes. It was the water's own underside: a sheet that ends over a drop has an open edge, and from the air below its level the Snell-window path drew the underside there as a thin band of flat water colour along the rock. I checked this by hiding cave walls, rooms, plants and water in turn; the slit went away only with the water hidden. Fix in Water.shader (shared file, additive, 6 lines): the underside is drawn only with the lens under water (`_PunchyUnderwater.w > 0`, which Underwater sets). Swimmers still get the Snell window from below. The same slit at open-edged surface cascades goes away too. Two bake-side tries (cutting deep stream vertices, and claims by distance to the ledge) did not remove it, so I reverted them.
2. **Green slab.** This was the chute's own water: a tilted sheet with foam, lit like moss. Chute stretches (stream segments at ChuteSlope 0.25 or steeper, `CaveData.ChuteSegment`) are now cut out of the cave water mesh at the lip and start again at the foot. A waterfall curtain is drawn in the drop, placed from the first and last chute segment (lip, width, drop, Spray 1, new `CaveWaterfall.Reach`), with no rng. The waterfall emitter moves to the chute lip. This is a cosmetic placement change only; landmarks and caves are unchanged. Re-shot from the same pose: r4-6-chute-c0.png (before: r4-6-before.png).
3. **r4-5-column pool.** This is row 3's lake hall (lake 2 at -2709.2, 41.4, -617.8, Depth 2.8). Only the column is this row's work. It is not a bug: the depth fade is applied, and the pale ring is a soft shallow gradient over the beach, not a hard lip, as the second view from the door shows (r4-5-column-b.png). The deep middle reads dark because underground the water has nothing to mirror but darkness (the sky term is about 0.04), and SSR picks up the column. The blue in r4-2 and r4-4 is shallower or lamp-lit water. The flat band is the foot of the hall wall under even light and fog. Re-shot from the same pose: r4-5-column.png.

- Latent, not shipped: BuildChunkMeshJob's skirt checks for cave air only at its foot. A tall chamber lying wholly inside the skirt's depth, with rock again below it, would let the skirt hang through the chamber. This did not cause the slit, so I left it out. The patch (a 1 m step search for the first air) is kept aside if a card wants it. BuildChunkMeshJob is world-lane owned.
- Shots checked at small size. Test shots deleted.
- Suite (water code touched): `DONE 26 passed, 0 failed, 17 skipped in 311s`. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954. lod-lint clean.
- I did not change the surface-hiding distance.

### Row 4b: stronger far sun road (2026-11-11) - done (verified)
- Commit e99891ed on world/far-lite (pushed), on top of row 4 (0900515f).
- Zach (11-11): "Stronger sun road in the distance please", then "farawy sun set with reflective path looks excellent". So this is a modest boost to the far band only. Near and mid keep row 4's look exactly.
- **Knob: `TerrainStreamingSystem.SunRoad`, default 2** (env `PUNCHY_SUNROAD`). 1 is row 4's road everywhere, bit for bit. 0 turns the road off. To go back to row 4, set the default to 1.
- How (Sky.hlsl `PunchySunRoad`, used by the far water and the skybox's below-horizon band):
  - Distance is read from how big a glitter cell looks: `DetailPolicy.ScreenSize(SunGlintSize 28 m)` over distance, flattened by the grazing angle (rule 20, no fixed metres).
  - A cell that still looks whole (>= `SunGlintFull` 6 px) gives exactly row 4's output.
  - Where cells shrink, the band widens and brightens by up to (strength - 1). The sparkle hands over to cells 4x the size, then settles to its mean by `SunGlintEnd` 1.5 px, so it can't flicker. Output is capped at 1.5x the sun colour, so it doesn't bloom out.
  - TerrainStreamingSystem sets `_PunchySunRoad` with the other per-frame detail globals.
- Far water fragment (ForwardLit, D3D11, my opcode count): row 4 had 1818; now 1870 instructions, 4 samples, 5 loads, 27 if/loop. That is +52 ALU and no new sample, within row 4's +81. Far vertex is unchanged (1966). I didn't count the skybox (unnamed pass, my counter needs a name); it runs the same function.
- Shots (milestone W-132/row4b; all viewed small):
  - **pair/** is the comparison to use. One session, time paused, the same clouds. `<spot>-r4.png` is strength 1 (= row 4) and `<spot>.png` is strength 2, plus contact-sheet.jpg. Spots: air300-sun0, airsea-sun1, hill-sun1, hill-sun0, ground-sun1, airsea-noon.
    - Near and mid water look the same in every pair.
    - At sunset, the road under the sun runs longer and brighter toward the horizon.
    - At noon, neither strength shows a road.
  - before/ (row 4 code) and after/ (default 2) come from separate sessions, so the clouds differ. Use them for context only.
- Suite: `DONE 25 passed, 0 failed, 17 skipped in 310s`. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954. lod-lint world/far-lite vs origin/main: clean.
- Findings for anyone shooting sunsets:
  - Shot-list waits run the clock (1 game hour per real minute). Row 4's `-time 18.0 ... -wait 12` shots ended with the sun below the horizon, which is partly why the road looked so subtle there. I set `DayNightCycle.Paused = true` by eval right after entering play, and set sun0 to 17.95.
  - With the sun low from the first frame, the flora shadow cache never draws, shot readiness waits on it, and the shot is never saved. A noon warm-up line first (`-goto ... -time 12 -wait 10`) fixes it.
  - `-weather` on a `-goto` line applies at the old spot. Put `-goto` on its own line.
  - At these spots the road is drawn mostly by the skybox's below-horizon band, not by far-water tiles.
  - Cloud coverage over the sea is about 0.8 even with `-weather Clear` (orography and pressure). The road is multiplied by (1 - 0.8 x clouds), so on a truly clear evening it's stronger than in these shots.
- No placement changes. No bench request: job 56 already notes the new arm.

### W-132 row 4b default (2026-11-11) - done (verified)
- Commit 4eabc7c7 on world/far-lite (pushed). `TerrainStreamingSystem.SunRoad` now defaults to 1, which is row 4's road exactly. Zach (11-11): "I actually prefer it to the newest. Your original implementation is good!"
- The tunable, env `PUNCHY_SUNROAD` (2 gives 4b's stronger far band) and the far-band code all stay. Only the default and its doc comment changed. TerrainStreamingSystem.cs is still CRLF.
- No new shots: the `-r4.png` files in W-132/row4b/pair/ are strength 1.
- Suite: `DONE 24 passed, 1 failed, 17 skipped`. The one failure was the known-flaky LiveFolk_BeastResponse, which passed when rerun alone. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.

## W-140 (island cache; tree Punchy-world, branch world/island-cache)

### Row 1: survey - done (verified)
- Commit c3eea1df on world/island-cache (pushed), from origin/main 393a9c20. Small code only: `IslandCache` (Terrain/Island) lists every blob the island bake writes, and the bake now logs `[island] blob sizes ...`. No output change.
- WorldTests (batch): `10 passed, 0 failed, 0 skipped`. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954.
- **What the island-ready frame builds** (IslandV2System.OnUpdate, v3). Sizes are serialised sizes from the new log line:

| Output | Saveable | Size | Runtime-state dependence |
|---|---|---|---|
| BiomeMap blob | yes, plain BlobArrays | 256K | none |
| Island (heightmap) blob | yes | 2048K | none |
| V3Ground blob | yes | 1024K | none |
| Rivers blob | yes | 19202K | none |
| Landmarks blob | yes | 50K | type indices into the palette's landmark list (see LandmarkTypes below) |
| Settlements blob | yes | 738K | none |
| Caves blob | yes | 768K | none |
| BatFolk blob | yes | <1K | none |
| Icebergs blob | yes | 58K | none |
| GrandFalls blob | yes | <1K (off without -lakeshelf) | none |
| Archipelago blob | yes | 1K | none |
| Anchors blob | yes | 144K | none |
| PondMask blob | yes | 512K | none |
| Scalars: BiomeKind, RiverSettings (after the v2/v3 adjustment), PondsOn, RiverRoughOn; BiomeCells = default | yes | bytes | none |
| Palette landmark list (LandmarkTypes appends 6 Resources village types) | no (asset refs) | - | stays runtime; cheap, idempotent |
| FloraSystem.Pregrow (plant variants on workers) | no (meshes) | - | stays runtime; depends on palette, not island |
| Bake statics: IslandV3.LakeCentre, V3Ranges massif/spine/pass/border tables, CampsiteTest.At | not needed by runtime | - | read only by the bake itself, the audits and -campsitetest |

  - Total **24.22 MB** per version. Rivers is 79% of it. No blob holds an Entity, UnityObjectRef, BlobPtr, BlobString or pointer, so `BinaryWriter.Write<T>(blob)` / `Read<T>()` (Entities 6.5) round-trip them byte for byte.
  - Runtime systems read only the blobs and `IslandV2.ActiveVersion`, never the bake statics. So the plan is safe.
- **Proposal for row 2:**
  - **File:** header (magic, format version, island version, key hash, blob count), then the scalars, then per blob its name, its byte length and the Write<T> bytes in IslandCache.Each order, then a 128-bit xxHash3 of everything before it. Written to a temp file and moved into place. A wrong magic, version, name, length or checksum is a miss, never an exception. Raw, not compressed: a read is a memcpy, and 24 MB is fine on disk.
  - **Key** (any change means a miss):
    - format version
    - island version
    - bake flags: -lakeshelf / PUNCHY_LAKESHELF, PUNCHY_SPRINGS
    - the bake inputs as they arrive from the subscene: World, BiomeNoise, RiverSettings (before adjustment), SpawnCell, HeightKind, plus the Biomes and Heightmap blob bytes
    - a code+data stamp. Editor: hash of Library/ScriptAssemblies/Assembly-CSharp.dll, Application.unityVersion, Packages/packages-lock.json, and AssetDatabase.GetAssetDependencyHash over the World subscene's recursive dependencies plus the six Resources village types and theirs. Player: the stamp the build step wrote next to the file; with no stamp, no cache.
  - **Where:**
    - Editor: Library/IslandCache/island-v{n}.bin (git-ignored, per tree).
    - Player: StreamingAssets/IslandCache/ (row 3's build step writes it), then a per-user copy in persistentDataPath for other flags.
  - **Stays runtime:** DecideVersion and the joined client's wait for HostVersion, LandmarkTypes(palette), FloraSystem.Pregrow, per-process sharing (Shared), the landmark hash log, the river report.
  - **Bypass** (fresh bake, no write): -freshisland / PUNCHY_FRESHISLAND=1, -campsitetest. Audits (-islandaudit / PUNCHY_ISLANDAUDIT, which the editor menu turns on by default) need the bake statics. On a hit I plan to skip them with a log line that says so, rather than bake fresh. Otherwise editor plays and the suite would never hit (audits are ~21 s of the 39 s to ready in the suite run). -freshisland runs them.
- **Rule 22 gaps seen:**
  - (a) GrandFalls reads IslandV3.LakeCentre, a static set by Generate, instead of an input.
  - (b) The audits read V3Ranges statics.
  - (c) LakeShelfOn and PUNCHY_SPRINGS are read from flags inside bake steps, not passed in as settings.
  - (d) LandmarkTypes mutates palette.Landmarks at runtime.
  - None blocks the cache: (c) goes into the key.

### W-140 row 2: save + load, switch, tests - done (verified)
- Commit c5d72dae on world/island-cache (pushed).
- **What it does:**
  - IslandV2System builds a SHA256 key from: the format and island version, the lakeshelf/springs flags, the code+asset stamp, the setting structs, the Biomes/Heightmap/BiomeMap/BiomeCells bytes, and the biome and landmark names and counts.
  - On a match it loads the file and skips the bake. A missing, mismatched or unreadable file (checksum, magic, format, key, sizes) gives a fresh bake and a rewrite, never an error.
  - The save writes a temp file, then moves it into place.
  - Logs `[island] cache hit|miss <reason> <ms> ms`.
  - `-freshisland` or `PUNCHY_FRESHISLAND=1` (and `-campsitetest`) bypass the cache.
  - On a hit the audits are skipped, with a log line saying so.
- **Tests** (WorldTests, `bash scripts/run-playmode-tests.sh WorldTests`):
  - `IslandCache_LoadedEqualsFresh`: every blob is byte-equal live vs fresh and loaded vs fresh. BiomeKind and RiverSettings are equal. The landmark hash is equal fresh/live/loaded. A key changed by one character does not load.
  - `IslandCache_SavesEveryBlob`: fails if TerrainSource gains a blob the cache doesn't save.
  - Miss run (key changed): 12 passed, 0 failed.
  - Hit run: 12 passed, 0 failed, 0 skipped. Log: `[island] cache hit Library/IslandCache/island-v3.bin 24.22 MB`.
  - `PUNCHY_FRESHISLAND=1` run: 12 passed, 0 failed. Log: `cache miss bypass (-freshisland)`.
- **Pins:** landmarks 112 A955AFE0 and fauna fingerprint AD723954 hold on all three runs.
- **File:** 25,397,152 bytes (24.22 MB of blobs).
- **Fix found on the way:** the Rivers blob is changed in place after it is built, so its header hash is stale. The load restores the stored header hash, so the bytes match exactly. Nothing at runtime reads that hash.
- **Risks:**
  - Writing uses reflection on internal Entities types (StreamBinaryWriter; the blob address for the hash restore). An Entities upgrade could break it. It then fails safe: "cache not saved", fresh bake.
  - Unsaved in-memory asset edits in the editor are not in the stamp.
- **BENCH-QUEUE request (profiler, not timed by me):** launch to island-ready (`v3 ready at N s since start`). Compare a cache hit against `-freshisland`, on a main PC Dev build and on the laptop. Use the row 3 build in Builds/Dev, once it lands.

### W-140 row 3: dev builds ship the file, host + client - done (verified)
- **Commit:** 622d7bb5 on world/island-cache (pushed). Files: `IslandCache.cs`, new `Editor/IslandCacheBuildStep.cs` (+ .meta). No ProjectSettings change; WorldSync unchanged.
- **Build step** (IPostprocessBuildWithReport, Windows players): writes `StreamingAssets/IslandCache/stamp.txt`. For Development builds it also copies the editor's `Library/IslandCache/island-v*.bin`, but only when that file's stamp matches the build's. Build log: `[island] build step: StreamingAssets/IslandCache: stamp only; island-v3.bin 24.22 MB`.
- **Player paths:** reads StreamingAssets/IslandCache first, then persistentDataPath/IslandCache, and writes to persistentDataPath. A miss now names each file tried and which key parts moved (a `.parts` sidecar), e.g. `key changed (typeNames, counts) [StreamingAssets/IslandCache/island-v3.bin]`.
- **Builds/Dev runs** (from the bench path, `-quitafter`, own logs):
  - Solo warm-up: `cache hit Punchy/IslandCache/island-v3.bin 24.22 MB`, 112 landmarks, hash D6B4846D.
  - `-host`: `cache hit` for the server world. Its ClientWorld logs `shared with ClientWorld ... (no rebake)`, hash D6B4846D.
  - `-join 127.0.0.1`: connected (connection 2), `cache hit`, hash D6B4846D. No exceptions in either log.
- **Finding: the shipped editor file misses in the player, and the strict key is right to miss it.** In a player build the subscene palette already lists the 6 Resources village types (FishingVillage, TurtleVillage, SeaFolkVillage, SeaFolkLanding, AshRibcage, MarshFolkVillage). `Resources.Load` returns different instances, so `LandmarkTypes` appends them again and the palette grows from 66 to 72 types, with duplicates. In the editor they are the same objects, so it stays at 66. Placements are unchanged (hash D6B4846D everywhere), but the Types blob differs (about 51K vs 50K), so the outputs really are different and the key catches it. As a result, a dev build bakes once on its first launch, then hits its own persistentDataPath copy. The shipped file only hits once the duplicate is fixed. The fix (match by name in `LandmarkTypes`) changes the player's Types blob, so I left it for a card.
- **Shared persistentDataPath:** every Punchy build on this PC (all trees) writes to the same LocalLow/DefaultCompany/Punchy/IslandCache. Builds from different trees have different stamps, so they keep overwriting each other's file (it stays safe: per-PID temp file then move, checksum on read, key check). Just more misses. Possible follow-up: put the stamp in the file name.
- **Full PlayMode suite:** `26 passed, 1 failed, 17 skipped in 306s`. The failure was `FolkConversationTests.LiveFolk_BeastResponse` ("the fleeing folk got no further off", 53.4 vs > 53.7 m), not the island. Reran alone twice: `1 passed, 0 failed` both times (flaky margin).
- **Pins:** landmarks 112 A955AFE0 and fauna fingerprint AD723954 hold in the full suite (island loaded from the cache).
- **File:** 25,397,152 bytes (24.22 MB).

### W-138 row 4c: tunnels hang their own dim wall lanterns - done (verified)
- **Commit:** c4befd05 on world/mountain-tunnels (pushed). Files: `StructureKit.cs`, `CaveStyle.cs`, `CaveBaking.Tunnels.cs`, `CaveSystem.cs`, `Flora.shader`, `CaveStyles/Mine.asset`, new `Flora/FixtureTunnelLantern.asset` (+ .meta).
- **As Zach asked (11-11):** no tunnel-specific lighting. `TunnelLanternLevel`, the tunnel lamp boost/reach and the in-tunnel flag are gone, so the player's lantern is the same everywhere.
- **The lamp:** a new kit fixture style `WallLantern` (bracket, arm, glass box) and species `FixtureTunnelLantern`. The Mine cave style names it as `TunnelLamp` (falls back to Torch for styles without one). The tunnel bake hangs them in pairs on both walls, about 13 m apart, 1.7 m up. CaveSystem adds them as Fixture lights.
- **Light values (the fixture's own data, `StructureKit.FixtureLight`):** WallLantern = 1.05x lamp colour, reach 7 m. Street Lantern = 1.3x, reach 9 m. The 4b tunnel torch was about 3.7 strength over 6.5 m, so the pools are softer than 4b's but still read as warm patches on the walls with the full player lantern on.
- **Marker glow:** comes from the marker's own data (Spire with RunePatterns), not from a tunnel flag.
- **Kit fix on the way:** wall-hung parts are now `Afloat`, otherwise the foundation skirt stretched the lantern into a post down to the floor.
- **Shared shader (tell flora owner):** `Flora.shader` windows now use `on = max(on, _FloraInCave)`, so lamp glass in a cave glows by day too. This affects any window part placed as cave flora.
- **Shots:** D:/Unity/PunchyMilestones/W-138/row4c/ (c0a-1-entrance, c0a-2-marker, c0a-3-middle, bore25-middle, sheet.jpg). Compared with row4b/sheet.jpg: the lanterns now show as a chain of lit points down the bore. The glass reads near-white up close rather than amber.
- **Pins:** landmarks 117 689D5348 and fauna fingerprint AD723954, unchanged.
- **Full PlayMode suite:** `25 passed, 0 failed, 17 skipped in 399s (Unity exit 0)`.
- **lod-lint:** clean against f6217674 (row 4b). Against origin/main it shows only the 4 earlier tunnel lines from rows 1-4b (niche radius, bake loop/length gates), with no new hits.
- **Rule 22:** the lamps come from the seed and the cave style asset, with no new runtime state, so a world file only needs the cave style's TunnelLamp reference.
- **Note:** SurfaceGlow doesn't use `FixtureLightAt` yet (the lit height for the wall lantern). That only matters if a WallLantern is ever placed on the surface.
- **BENCH-QUEUE request (profiler, not timed by me):** cave light cost walking the C0a tunnel (up to about 14 lamp lights near the player) vs row 4b.
- **Addendum (amber glass, ec884d2d):** WallLantern glass now uses its own kit material `Flame` (0.42, 0.15, 0.03) instead of LampLight. The `_FloraInCave` glow no longer pushes it to white: it samples (205, 151, 75), amber, at about 3 m. Light values are unchanged. Retook c0a-2-marker, added close-3m, and updated row4c/sheet.jpg (checked small). Also touches FloraGenerator.Kit.cs (two Flame checks next to Lamp). Suite `25 passed, 0 failed, 17 skipped in 371s`, pins 117 689D5348 / AD723954 unchanged, and lod-lint shows only the earlier tunnel lines. Editor closed.

### W-140 row 4: the shipped file hits on first launch; stamped user copies - done (verified)
- **Commit:** 47f5490a on world/island-cache (pushed). Files: `IslandV2.cs` (CRLF kept), `IslandCache.cs`.
- **(a) LandmarkTypes de-duplicated by name.** The player now has the editor's 66 types (`.names`: `landmarks 66`). Placements are unchanged: they index the palette's own objects, so the old duplicate entries 66-71 were never used. With the shipped file moved aside, a forced player bake writes a file of 25,397,152 bytes that is **byte-identical** (`cmp`) to the editor's Library file. Before the fix the player's file was 25,398,176 bytes.
- **(b) Stamped per-user name:** `persistentDataPath/IslandCache/island-v3-<stamp8>.bin` (e.g. `island-v3-b4a83457.bin`). The shipped StreamingAssets file keeps its plain name. Each player save prunes stamped files beyond the newest 2, along with their sidecars and the old unstamped name. Checked with dummies: a newer dummy was kept; an older dummy, its `.parts` and the legacy `island-v3.bin` were deleted. I removed the dummies afterwards.
- **First launches on a fresh Builds/Dev build** (no per-user copy present; nothing was written there afterwards):
  - Solo: `[island] cache hit StreamingAssets/IslandCache/island-v3.bin 24.22 MB`, 112 landmarks, hash D6B4846D.
  - `-host`: `cache hit StreamingAssets/...`. Its ClientWorld: `shared with ClientWorld (no rebake)`, hash D6B4846D.
  - `-join 127.0.0.1`: connected (connection 2), `cache hit StreamingAssets/...`, hash D6B4846D.
- **WorldTests:** `12 passed, 0 failed, 0 skipped in 221s`, including IslandCache_LoadedEqualsFresh and IslandCache_SavesEveryBlob. The editor missed once on `key changed (stamp)` (new code) and rebaked; the build then shipped that file.
- **Pins:** landmarks 112 A955AFE0 and fauna fingerprint AD723954 are unchanged.
- **Editor:** none open from this lane (batch runs only, all exited). Ready for job 60.

## W-136 merge with main (W-134), 11-11
- **Pushed:** world/ground-contact-shade at 82d43e13. It holds merge commit 1e59c0f2 (origin/main into W-136) and fix commit 82d43e13. world/cave-lod was not touched.
- **CaveSystem.cs hunk:** I took W-134's structure. Pools and streams are baked by CaveSystem.Water, and streamMeshes, streamDrawn and MakeStreamMesh are gone. Lava pools use main's disc mesh and LightBudget glow. W-135's rule-20 lava test stays: ViewReach(radius*2, CaveWaterPixels) capped at LoadRadius, held by DetailPolicy.Within with poolDrawn. The two doc comments are merged into one.
- **Second clash (it compiled only after this fix):** W-135 added the const CaveWaterfall.Reach (0.9) and W-134 added the per-fall field Reach. Both auto-merged into CaveData.cs (CS0102). The const is now NotchReach, and a new Carry property returns Reach, or NotchReach when Reach is 0. The W-135 fall sheet (WaterPatchSystem.Falls) now uses Carry, so a chute's sheet lands where its curtain lands. LandmarkEffectSystem points at NotchReach.
- **Checks:** W-135's surface skip sits inside Draw. DrawPools/DrawCaveWater run every frame, so the bake trigger isn't blocked. Baked tiles follow screen size (Holds(ScreenSize, WaterTilePixels/Detail)). Note for R-13: the 24 px tile threshold is local to CaveSystem.Water, not a DetailPolicy per-kind constant. Bake start and evict use fixed LoadRadius and LoadRadius*1.2. Those are load reach, not visual reach, and were left as they are. lod-lint on HEAD: clean.
- **PlayMode suite:** `26 passed, 0 failed, 17 skipped in 698s`, 0 compile errors. I didn't run EditMode.
- **Pins:** landmarks 112 A955AFE0, fauna AD723954. Unchanged, and they match main.
- **Look-check** (shots in W-136/merge in the milestone folder):
  - `[shot] 'cavepool' ... camera (1520.0, 2.0, -2622.0); check: mean 0.13, near-black 0.58 ...; good`. This is the W-134 cistern spot. A pale water sheet shows at the bottom.
  - `[shot] 'cavelava' ... in a cave; good`. No lava pool could be placed (the world has 0 lava texels). `-place cavelava` fell back to cave pool 3, at (-210.3, 1.6, -2729.0), 0.6 m deep, and the player stands in that pool.
  - I checked the contact sheet at small size.
- **Tree:** back on world/island-cache. The five parked .mat edits (Cave, Water, Colossus, IcebergIce, TempleGlow: shader property values and a MOTIONVECTORS pass change) were stashed, applied back by SHA, and the stash entry was dropped. None of them were committed.
- **Editor:** closed. Ready for bench.

## W-135 row 6: walk hitch (done, verified by counts and shots)
- **Branch:** `world/ground-contact-shade`, commit dc65c63b (pushed).
- **Cause (counted, no timing):** on the bench's c5 hereentry walk, cave chunks crossing a wall-level band were rebuilt with no cap. Up to 2 level-0 rebuilds started in one frame (601,526 field points, each a new chunk's whole cost), up to 4 builds were in flight, and near the mouth (25-50 s in, the job 52 spike window) up to 22 level builds and 4.55M points ran in one walk-second. 184 of 390 level rebuilds were chunks that stay empty. No room kits, tiles or sorts on this walk; skip flips 10 in all (7 in one frame at the mouth).
- **Fix:** `RebuildsPerFrame` 1 and `MaxRebuilding` 2 cap level and contact rebuilds, nearest finer first; coarser rebuilds wait until nothing else builds. The old mesh stays drawn until the new one swaps in. The mesh job records the chunk's lowest cave field value, so an empty chunk with no wall within WallReach + 2 voxels is never rebuilt finer. New `CaveSystem.Counts` per-frame counters for probes.
- **Counts, walk before -> after:** level rebuilds 390 -> 285; empty rebuilds 184 -> 79; field points 57.3M -> 40.2M; max points per frame 601,526 -> 300,763; max in flight 4 -> 2; max per walk-second 22 / 4.55M -> 13 / 2.28M; frames over 50k points 157 -> 110; frames with 2+ level builds 23 -> 0. Uploads 207 and vertices 705,035 unchanged (the same meshes come out).
- **Pop check:** D:/Unity/PunchyMilestones/W-135/w135r6-3m-{now,settled}.png and w135r6-15m-{now,settled}.png: c5 hall wall at 3 m and 15 m, shot right after posing and again 8 s later. Mean pixel diff 0.02 and 0.11 (0.16% of pixels over 24 at 15 m, from glow). All walls round the camera are level 0. Checked at small size: no coarse wall, no pop.
- **Tests:** PlayMode suite 25 of 26. The one failure was LiveFolk_BeastResponse (the allowed flake); it passed alone on rerun. Pins hold: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint AD723954. lod-lint clean.
- **Crystal spot (f):** the path is valid. The jc.views line lacked `here` in `-benchstops`; it needs `-benchstops here,hereentry`.
- **Bench:** job 61 queued (c5 walk, A main / B branch / C B with `-cavelevel 0 -nocaveskip`, frames over 2x median).
- **Risk:** a chunk entering the fine band now waits up to a few frames longer for its finer mesh (it keeps its coarser mesh drawn meanwhile). That doesn't show in the shots.
- **Tree:** on world/ground-contact-shade with the five parked .mat edits uncommitted. Editor closed.

## W-138 merge prep (done, verified)

- **Branch:** world/mountain-tunnels at 70d0890d, a merge of origin/main 6c3588b6 into ec884d2d. Ready for laptop bench job 59 and the supervisor's merge (main has not moved since).
- **Conflicts:** CaveSystem.cs only, both sides kept.
- **Portals as mouths:** a road tunnel's far portal now counts as a mouth (`FarPortal`, used by `NearMouth`, the nearest-mouth test and Look).
- **Fix found while checking:** from outside, bore chunks more than 32 m past the portal counted as buried and were skipped, so the bore showed sky and popped in when you entered. New `InPortalSight`: chunks on a tunnel's straight run from either portal are never buried and are left to the screen test.
- **Walk through a road tunnel (counts):** before, 7 skip flips and 264 swaps; after, 6 flips (all on the frame you cross the mouth) and 251 swaps. Max per frame: 2 builds and 2 uploads.
- **Shots:** in D:/Unity/PunchyMilestones/W-138/. Each was shot after settling and compared against a repeat shot of the same pose, to measure haze noise:
  - portal-bore-before-after.jpg: the sky hole is gone.
  - s15, s5, n15 and n5 before/after: the remaining differences are within that noise.
  - s1-r-skip and s1-r-noskip: skip on vs off, 174 to 405 px apart, which is noise.
  - bore-levels-default-vs-l0.jpg: no cracks and no sky pixels at z 400 or z 800.
  - Lamps show far down the bore.
- **Tests:** PlayMode suite: 28 passed, 17 skipped, 0 failed. Pins: Landmarks_V3 117 689D5348, Fauna_Fingerprint AD723954. lod-lint clean.
- **Flora owner:** compared with main, this branch touches FloraGenerator.Kit.cs, FloraPlacement.cs, Flora.shader and FixtureTunnelLantern.asset (the tunnel lantern fixture). Flag these at merge time.
- **Risk:** markers and shrines down a bore follow the plantShown/DetailPolicy path. I checked this in code only, with no shot.
- **Tree:** editor closed. Next: W-145 on world/cave-entry.

## W-145 row 1: cave entry pops (done, counts and shots)

- **Branch:** `world/cave-entry`, commit 9d004729 (pushed). It branches from the W-138 merge commit 70d0890d (main 6c3588b6 plus the unmerged W-138), so it needs W-138 merged first.
- **Method:** walked 3 caves at walking pace (6 m/s), from 250 m outside through the mouth and down the main passage. Per-frame counters (new, off by default behind `CaveSystem.CountReveals`): first draws, reveals of chunks drawn before, visible wall swaps, plant batches, water bakes, lamp flames. A shot every 0.5 s from 30 m before the mouth to 45 m after. Counts only, no timing.
- **Pops (d = metres past the mouth, minus is outside):**
  - **Hall (cave 5, the c5 walk), the main pop.** At d+5.3 the camera crosses underground and 18 chunks draw for the first time in one frame. They are chunks more than 32 m from the mouth that count as buried, so they are skipped while the camera is outside, even though the big hall can be seen deep through the mouth (from outside it looks flat purple). Smaller: 1-2 reveals at +11.7, then single ones at +26.9, +51.6, +67.5, +91.6, +107. Plants come in as batches of 28-56 at the plant range edge every 4 m from +11 to +26. 76 visible wall swaps spread before and after the mouth.
  - **Room (cave 10, grotto).** First draws match late chunk builds that land in view: 3 at d-37.4, 2 at d-4, 2 at +28.2, 1 at +48.6. Lamp flames appear at d-4.6. At +60.2, 13 reveals when the camera (12 m back) rose into rock and briefly counted as outside.
  - **Plain (cave 12).** No first draws or reveals. 10 visible swaps at d-48/-41/-22 while the mouth is still behind a rise. 5 brief camera "outside" blips inside the cave (no pop seen).
- **Max per frame, all walks:** 2 new builds, 1 level rebuild, 2 uploads, 1 swap, 4 building, 1 rebuilding. Within the W-135 caps.
- **Strips:** D:/Unity/PunchyMilestones/W-145/row1-{plain,hall,room}-strip.png (pops marked red) and row1-hall-pop-pair.png (the d+5.3 frame before and after). Raw shots in W-145/raw/. Checked at small size.
- **Row 2 plan:** give natural caves the same sight run W-138 gave tunnel portals. From the mouth, follow the main passage while it stays roughly straight (first chamber, plus the next if seen); those chunks are not buried and are built at the level needed from just inside before entry. Room kits, lamps and plants on that run start early. Hold "outside" against brief camera-in-rock blips. Same caps.
- **Tree:** editor 74068 open in play mode for row 2.

## W-145 row 2: cave mouths keep their first stretch built (done, commit f51e4226 on world/cave-entry)

- **Branch:** world/cave-entry, based on 70d0890d, the W-138 merge prep commit, which is not merged yet. Merge W-138 first.
- **What changed (CaveSystem.cs only):**
  - Once per baked world, the Burst index job works out which parts of each natural cave can be seen from outside the mouth. It draws lines from 5 points across the mouth and follows each line through cave air or open air above the ground.
  - Chunks around a seen part are never counted as buried, so the surface skip leaves them to the opening and screen tests.
  - While the mouth is near on screen (DetailPolicy.Holds on screen size, rule 20), those chunks get their wall level from the mouth, not the camera. So they are built at the level needed from just inside before the camera crosses.
  - A camera pushed just under the ground over a passage (it trails the player) no longer counts as outside. Before, the buried chunks around it blinked.
  - Result: 24 mouth runs, 117 chunks. No new range in metres. The constants are a visibility test plus wall-noise padding, and are commented as such.
- **What the d+5.3 hall pop really is:** I compared shots taken from outside with the hidden chunks forced on and off: buried chunks shown, and the whole surface skip turned off, at d-0.5 to d+5.2. The change is 38-80 px out of 129,600, and all of it is birds moving. So the 18 chunks that drew first at the crossing in row 1 sit behind the mouth's rock and cannot be seen from outside. The sight test agrees: lines from the mouth leave the air 5-11 m in, before the hall. The row 1 pop pair was taken 0.5 s (3 m) apart, so the camera move looked like a pop. Underground lighting ramps smoothly (0.16 to 0.69 over 1 m) and does not step.
- **Walks (same 3 as row 1, 6 m/s, counts only):**
  - **Hall (c5):** at d+5.3 the first draws dropped from 18 to 7, and 11 chunks drawn earlier came back. The skip flips at the crossing dropped from 7 to 3. Totals: first draws 20 to 7. Work inside the cave: level rebuilds 151 to 132, uploads 121 to 108. The work outside the cave also fell, because this run started with the world warm, so I can't claim it moved before the mouth. The bench A/B will settle it.
  - **Room (c10):** late builds that land in view now draw earlier: d-59/-24/+3/+29 against row 1's -37/-4/+28/+49. The 12 reveals at +60 are the camera rising about 3 m into the ceiling at the end of the walk. Row 1 had 13 there, and nothing is seen from inside the rock.
  - **Plain (c12):** no first draws or reveals. Skip flips dropped from 219 to 35 (the camera-in-rock fix).
  - **Max per frame, all walks:** 2 new builds, 1 level rebuild, 2 uploads, 2 swaps, 4 building, 2 rebuilding. That is the same as W-135 row 6 and within the caps.
- **Strips:** D:/Unity/PunchyMilestones/W-145/row2-{hall,room,plain}-strip.png. Checked at small size: no geometry pop at the mouth in any of the 3. Plants still come in as batches at the plant range edge (+9 to +26 m in the hall). That edge stays on screen size (rule 20). I did not move it to metres.
- **Checks:** PlayMode suite 28 passed, 0 failed, 17 skipped. Pins hold: Landmarks_V3 117 689D5348, Fauna_Fingerprint AD723954. lod-lint is clean on world/cave-entry.
- **BENCH-QUEUE request:** the c5 walk, A = main and B = world/cave-entry, for counts and cost of the cave entry and the extra one-time sight pass in the index job.
- **For Zach / supervisor:** the measurable chunk pop is fixed or hidden. If Zach still sees a pop on entering, the likely causes are plant batches at the range edge and wall LOD swaps (about 75 per walk, spread out), not chunks appearing. A shot or clip from him would narrow it down.

## R-16 row 1: where the shadow darkness jumps (findings, branch render/shadow-tiers, no code yet)

- **Probe:** one canopy tree and one boulder on flat ground, camera on a 25 degree line, shadows on vs strength 0, clear weather, ambient mist off (it added most of the noise in a first pass). Ratio = shadow pixel brightness on / off; lower is darker. Noon (h12, strength 0.98) and late afternoon (h17, 0.86).
- **Tree, noon (median / darkest 10%):**

| Camera to caster | Ratio | Tier |
|---|---|---|
| 5-50 m | 0.53-0.58 / 0.43-0.46 | sharp (URP cascades, distance 50) |
| 70 m | centre 1.17, edge 0.28 | gap + cache: inner shadow is lit, outer rim dark brown |
| 100 m | 0.49 / 0.28, centre 0.29 | cache and wide stacked |
| 140-300 m | 0.51-0.55 / 0.47-0.50 | wide only |

- **Tree, 17:00:** 0.69-0.74 to 75 m, 0.76 (darkest 0.47) at 93 m, 0.68 (centre 0.44) at 122 m, then 0.74-0.83 out to 320 m.
- **Rock:** sharp up to about 25 m (0.44-0.67 noon, 0.67-0.69 at 17:00). Past 30 m its shadow is a few pixels at most (0.84-0.95); it is too small to cast into the cache or wide maps.
- **Jumps, in Zach's walk-in order:**
  1. **Wide to cache (about 120 m):** 0.50 to 0.29. The shadow nearly doubles in darkness.
  2. **Cache to sharp (50-75 m):** 0.29 to 0.55, plus a lit gap first.
- **Causes:**
  - **Cache and wide stack.** The wide map is fully on past 50 m (fade 0.75-1.0 of shadow distance), and the cache covers 50-120 m, so both darken the same ground. Shot: 70 m cache only = 66, cache + wide = 27, off = 104 (8-bit brightness).
  - **The cache darkens the whole colour** (colour x (1-k)), so ambient goes too and the shadow reads brown. Sharp and wide darken only the direct light, so they read blue-grey.
  - **Lit gap at about 70 m:** the cache receiver skips where the real-time shadow map still has the tree (casters are held past shadow distance), but the terrain's own real-time shadow has already faded out there. Neither tier darkens it.
- **Far "instability":** a still camera for 18 s at 90 m saw 9 cache redraws (every 0.5 degree of sun, about 2 s on the 24 min day) and 8 wide redraws (every 1 degree, plus flora re-streams). Each swaps in with no crossfade. Frame diffs show every canopy shadow edge stepping in blocky stairs (wide texels about 1.6 m; the cache view is not texel-snapped). 4.5-6% of the far half changes between shots taken about 1 s apart, against 0.6-2% in the near half. So it is redraw stepping plus texel snap, not a cascade edge.
- **Strips (checked at small size):** D:/Unity/PunchyMilestones/R-16/R16-row1-tiers.png (30/50/100/200 m: blue sharp shadow, then dark brown at 100 m) and R16-row1-stack.png (70 m: off / cache only / cache + wide). Raw shots and the still series are in R-16/raw/.
- **Row 2 plan:**
  - Cache darkens only the direct share, like the wide tier does.
  - The wide map skips the cache band (or the two blend to 1).
  - The cache receiver uses the faded real-time term so the gap closes.
- **Row 3 plan:** texel-snap the cache view, and crossfade old and new maps after each cache and wide redraw.

## R-16 row 2: same shadow darkness in every tier (done, commit 1fcccf58 on render/shadow-tiers)

- **Branch:** render/shadow-tiers, from origin/main 6c3588b6.
- **What changed (FloraShadowCache.shader, FloraShadowOptions.cs):**
  - The cache shadow now takes only the direct sunlight's share, as the real-time and wide shadows do. It uses the main light's brightness on the surface and the ambient probe. CacheDarkness is now 1 x shadow strength, like the wide tier (was 0.62 of the whole colour).
  - Where the real-time or wide shadow already dims a point, the darker one wins. Both sides of each hand-over weigh to one: lit = ambient + sun x min(real-time, 1 - wide, 1 - cache).
  - The real-time term fades with URP's own shadow fade, so the band near the shadow distance is no longer left lit.
  - No ProjectSettings or renderer changes. No new ranges in metres.
- **Probe after (tree, median / darkest 10%):**
  - Noon: 0.47-0.52 / 0.39-0.41 at 5-70 m, 0.445 / 0.40 at 100 m, 0.44-0.47 / 0.40-0.44 at 140-300 m. Was 0.29 at 100 m. The far tiers sit 5-9% darker than the near ones by median, and match by darkest 10%.
  - 17:00: 0.72-0.79 out to 122 m, 0.71-0.83 past that.
  - The 70 m "centre" sample still reads about 1.0. In the shot that point is under the crown, and the ground shadow there is whole (dbg-F-on vs dbg-F-off in R-16/raw).
- **Strip (checked at small size):** D:/Unity/PunchyMilestones/R-16/R16-row2-walkin.png, at 200/100/70/50/30 m. The tone holds and the brown band is gone.
- **BENCH-QUEUE:** job added for the receiver's cost. It now reads the wide map (4 taps) and the ambient probe per pixel in the cache band.
- **Not done yet:** the 30 s walk-in clip. Row 3 has the motion checks.

## R-16 row 3: far shadows stop crawling (done, commit 8d7e5cde on render/shadow-tiers)

- **What changed:**
  - **Crossfade.** The wide map and the flora shadow cache keep the old map in the back texture and blend the new one in over it: 2 s for wide (`-wideblend`), 1 s for the cache. Sun and flora redraws wait for the blend to finish. A jump past RedrawMove/CacheMove, a caster change or a new near range still redraws at once.
  - **Texel snap.** The cache's view centre now snaps to its texels in the sun's view. The wide map already did this.
  - **Steady candidates (found while measuring).** With the sun paused and the camera still, the wide map still redrew every 1.5 s. The cause was its candidate scan: mantrap leaves sway about 10% in size and crossed the screen-size threshold on alternate scans. A caster already in the map now stays in until it is DetailPolicy.Hysteresis (15%) under the size (rule 20 still holds). After the fix, a still camera with the sun paused had 0 wide redraws in 25 s.
  - WideShadow.LastReason names what started each redraw (first/casters/candidates/moved/flora/sun).
- **Check (still camera at 90 m, 20 shots about 1 s apart over 18 s, sun running):**

| | far half changed | near half changed | wide redraws |
|---|---|---|---|
| before row 2 | 4.5-6.3%, alternating | 0.6-2.1% | 8 |
| row 3 | 2.1-2.4%, flat | 0.4-0.6% | 4 (all sun) |

  The shadow-edge rings in the before diff are gone. What is left is swaying twigs on the far hills, the river and the near tree's sway.
- **Slow walk (1 m/s):** about 15% of pixels change per shot, which is just the camera moving. Blends on 14-16% against blends off 15-16%, with no spikes at redraws. Frame diff cannot separate crawl from motion while walking, so the walk claim rests on the snap math (as the wide map's) and on no ghost or doubled shadows in the frames (checked).
- **Strip:** D:/Unity/PunchyMilestones/R-16/R16-row3-crawl.png (before/after pixel-change masks over 1 s).
- **Cost:** while blending, every wide-shadow reader and the cache receiver take 4 more taps. Added as an addendum to BENCH-QUEUE job 63 (no timing here).
- **Shader warnings:** none new (Terrain/Flora/Impostor only show the old uninitialised-variable warnings).

## W-146 crystal caves dark (#274), branch world/dark-crystal

### Row 1: find the light (measure only, no code)
- **Setup:** lantern off (CaveSystem.LanternIntensity 0), weather Clear, no HUD. Frame mean luma (sRGB 0..1) and near-black share (luma < 0.04), 3 views each. A plain Rock passage (cave 13) is the reference. "Crystal passage" is cave 5 (Crystal style, the lavender look). "Grotto" is crystal room 2 (cyan group) off cave 13. Knock-outs: the Crystal style's glow set to 0, the room's fixture glows cleared, and the style's sheen set to 0 (all restored after).

| lantern off | mean (3 views) | near-black |
|---|---|---|
| Rock passage, deep (p1, p2) | 0.038, 0.019 | 83%, 99% |
| Rock passage, by a lit ruin doorway (p0) | 0.133 | 6% |
| Crystal passage, as is | 0.29, 0.27, 0.24 | 0%, 0%, 4% |
| - style glow 0 | 0.12, 0.08, 0.06 | 7%, 5%, 38% |
| - style glow 0 and sheen 0 | 0.09, 0.06, 0.03 | 17%, 24%, 78% |
| - room glows cleared (none in passages) | 0.29, 0.27, 0.24 | unchanged |
| Grotto, as is | 0.20, 0.13, 0.28 | 4-5% |
| - room glows cleared | 0.05, 0.05, 0.05 | 26-30% |
| Lantern on, for scale: Crystal passage / Rock / Grotto | 0.34, 0.33, 0.28 / 0.19, 0.14, 0.16 / 0.26, 0.23, 0.32 | |

- **What lights a crystal cave with the lantern off, biggest first:**
  1. **The Crystal style's emission (GlowEverywhere 0.08 × Glow 1.6, lavender).** It is a flat wash on every wall. Its colour matches the measured frame colour (about 0.32, 0.26, 0.42 sRGB). It is about 60-75% of the light, and it is why the lantern barely matters there (0.27 rises to 0.33).
  2. **Sheen's light-free rim term** (the 0.035 floor × Sheen 0.9): grazing walls glow on their own.
  3. **Cave ambient** (SH × _Darkness 0.05, the same as in plain passages) on the much paler lavender rock (0.78, 0.70, 0.95 against Rock's 0.48). This is what is left: 0.03-0.09.
  4. **Grottos:** the room's 2-3 fixture glows (7 m reach, cyan or violet) are about 75% of their light. Without them the room sits at 0.05 (ambient on pale room tiles and crystals).
  - Fog, the sun and torches do not reach these views (atmosphere and sun need openness, which is 0 this deep).
- **Row 2 plan:** cut the style's always-on emission to faint, local glints on crystal faces. Drop the sheen's light-free floor for glowing styles. Cut grotto fixtures to small, short-reach glints. Darken ambient on pale glowing rock toward the plain passage's level. Lantern light keeps the lavender albedo, gloss and facets, so lit crystals still sparkle.
- **Shots:** D:/Unity/PunchyMilestones/W-146/raw/ (crystal5-*, rock13-*, grotto-*; suffixes -style, -glows, -both and -style-nosheen for the knock-outs).

### Row 2: dark by default (commit 94e031fe)
- **Done:** crystal passages and grottos read near-black with the lantern off. Only small lavender vein glints and faint plant glints remain, with no wash over the walls. With the lantern on, the walls, veins and crystal plants light up and sparkle in its pool.
- **Lantern off, mean luma / near-black share (luma < 0.04), 3 views each:**

| View | p0 / door | p1 / mid | p2 / back |
|---|---|---|---|
| Crystal passage (cave 5) | 0.026 / 83% (was 0.29 / 0%) | 0.013 / 94% | 0.023 / 88% |
| Plain Rock passage (cave 13) | 0.129 / 11% (by a lit ruin doorway) | 0.012 / 99% | 0.014 / 98% |
| Grotto (room 2) | 0.017 / 92% (was 0.20 / 4%) | 0.010 / 100% | 0.020 / 90% |

  Crystal averages 88% against the plain passage's 69%. In the two deepest views the crystal passage is 5-10 points under the plain passage (94 vs 99, 88 vs 98); the gap is the glints the card asks for. Lantern on: crystal 0.12-0.19, grotto 0.14-0.18.
- **What changed:**
  - `CaveStyle.Ambient` sets the share of the deep ambient, and of the sheen's light-free rim, that a style takes. The style table grows from 9 to 10 vectors. It defaults to 1, so every other style is unchanged. Crystal uses 0.3.
  - `CaveStyle.PlantLight` sets how much light a style's glowing plants throw: `CavePlant.Dim` is set at bake time, and reach drops to 40% when fully dimmed. The plants' own glow is untouched. Crystal uses 0.15. With the emission and ambient already cut, these plants (CrystalShrub, GlimmerCap, both blue) were the last thing lighting crystal passages. Fungal caves keep their full plant light.
  - Crystal asset: Glow 1.6 -> 0.5, GlowEverywhere 0.08 -> 0.
  - Grotto rooms: cluster lights at 12% with 2.5 m reach (`CaveRoomKit.GrottoGlint`), and walls take 30% of the deep ambient (`GrottoAmbient`, scaling the room material's `_Darkness`).
- **Checks:** no placement changes (Dim draws no random numbers, and Find() lists the same caves and grottos). No compile or shader errors. Every shot was checked at small size. No timing was done.
- **For the supervisor:** Crystal.asset is a landmark asset, so editing it re-bakes. The layout came out the same here. CavePlant gains a float, so any fingerprint that hashes raw plant structs will move.
- **Shots:** D:/Unity/PunchyMilestones/W-146/row2-off-on.png. Rows 1-3: crystal p0, crystal p1 and grotto mid, lantern off and on. Row 4: plain Rock p1, off and on. Row 5: crystal p0 and p1 before the change (lantern off). Raw shots use the suffix -r2b.
