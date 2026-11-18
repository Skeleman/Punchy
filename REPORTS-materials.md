# REPORTS-materials

Materials lane reports, newest last. Designs and handoffs for W-166 (the one material library) go here.

## W-166 rock surface r2 plan (#336), 11-17: scarp walls read the Stone table

Scope (Zach 11-17): one rule set per stone for every bare rock wall: cave walls, cliff scarps, river notches.
Row 1 makes `MaterialLibrary.Stone` (by `StoneKind`) general; caves are its first reader. Row 2 waits for the
coordinator's OK after the world lane's river rows land (it is in the scarp and TerrainSource files now).

- **Choosing the stone:** the ground material under the scarp, falling back to its biome. A `StoneKind` field on
  `BiomeDefinition` (default Plain) is the fallback; where the ground layer names a rock surface, that wins. Picked
  once per scarp at bake, from the rim's midpoint, so a scarp is one stone along its length (beds still vary).
- **Where it is stored:** `Scarp` (ScarpSet.cs) gains a `Stone` byte, set in `ScarpBake.Style` beside the kind's
  proportions.
- **Shape (TerrainSource shape pass, `ScarpSet.Shape`):** the layout-safe columns first: Grain scales crack size,
  Blockiness the bed push and setback steps (jointed blocks vs rounded beds), RibSpacing the gully spacing.
  Roughness and Jaggedness may scale Push and Crack later; they move the face, so that step re-pins the hashes and
  tells the profiler.
- **Surface (ScarpPaint, W-162 rock walls):** Detail and DetailScale drive the face's fine relief, the same columns
  the cave shader reads, so a granite cliff and a granite cave match up close. Climbing (bare rock grades) may read
  Blockiness for hold density in a later row.
- **Notches:** river notches are RiverCut scarps, so they follow once scarps read the table.

## W-166 row 1: cave walls read MaterialLibrary.Stone (#336), done 11-17
- Branch materials/cave-surface: cf7acd9c (table, untuned: cave sdf 3D2D78E06B03B800 and style table equal to before), 9cf9ca10 (tuning).
- `StoneKind` + general `MaterialLibrary.StoneRow`/`Stone` table; CaveStyle names a Stone; Cave.shader fine detail fades by ScreenSize ppm.
- Tuned granite, sandstone, tuff (layout-safe columns only); Limestone has no caves on this island, so tuff (Fungal) was tuned instead. Only those caves' sdf changed; node counts equal, NaN 0.
- 968da515: tuff softened (Grain 1.5, Detail 0.4 at 1.8); the rainy shot read as smeared.
- Retake 11-17: same seed and frames, weather pinned Clear, time 12:00, ground wetness zeroed, 25 s settle. Skies clear in all 6. Lum before/after: Granite 73.8/69.2, Sandstone 51.4/58.9, Fungal 82.6/84.7.
- Landmarks_V3 and the 3 cave tests pass on 968da515. Shots: PunchyMilestones/W-166/cave/before and after (Granite, Sandstone, Fungal).
- Row 2 (scarps, notches) plan above; waits for the OK.

## W-166 wood row: per-kind board scale, knot spacing, figure, wear (#336), done 11-17
- Branch materials/wood-kinds: c585ce62 (4 new WoodRow columns -> _PunchyWoodFormKinds; every row keeps the old shader constants, so the math is identical), 4db070ed (tuning).
- Tuned pine (narrow straight boards, close knots), driftwood (wide, twisted, worn), dark (clean, sparse knots), bleached (more figure and wear); oak unchanged. Laid shingle/siding courses keep their geometry height; only width varies.
- Untuned check: main vs untuned shots differ no more than two runs of the same code (fog, water, NPCs move between runs), so pixel diffs can't prove identity; the shader math is the proof.
- Shots (Clear, 12:00, dry ground, 25 s settle): PunchyMilestones/W-166/wood/before and after (Oak town house, Pine snow shelter, Driftwood stilt house and log, Dark ramada and marsh hut, Bleached palisade). The change is subtle at these distances.
- Landmarks_V3 passes. MaterialLibraryTests: 2 of 4 fail on things this branch doesn't touch (Cookhouse stall bark has no family; Cave/TerrainSurface Stone include check). Likely already failing on main.
- Wood needs no edits to Cave.shader or TerrainSurface.hlsl.

## materials/lib-tests (8d9a25bc) - DONE
- Both failures reproduced on main. MaterialLibraryTests 4/4 now, edit-mode suite 42/42.
- Stone: the test now follows #include chains. Cave and TerrainSurface reach Materials/Stone.hlsl through Materials/RockWall.hlsl (#327). No shader edits.
- Cookhouse: its blueprint's parts take their form's family, and Stall has none. The pyramid roof falls back to plaster (like the smithy's), the door mat is cloth, the fire ring is stone (so camp fire pit stones now draw as stone too).

## materials/coverings (a6218b6d) - DONE (W-166 #336, roadmap line 127)
- A-133 had only moved gear wood and metal; coverings were still inline in Creature.shader.
- MaterialLibrary.Coat: one row per Fauna.CoatKind (fur, feathers, scales, skin, chitin), pushed as globals. New Materials/Coat.hlsl, PunchyCoat(), carries the A-97/A-123 patterns unchanged; default rows match the inline values.
- Fauna hand-off: switch Creature.shader's coat block (surf 0-4) to PunchyCoat(), then drop ("Coat.hlsl","Creature.shader") from Pending in MaterialLibraryTests and add Creature.shader as Coat's user.
- Tests: MaterialLibraryTests 5/5 (new EveryCoatKindHasARow), edit-mode suite 43/43. A probe shader compiled Coat.hlsl clean (no warnings). No look change, no image reads.

## 11-17 coat switch (fauna hand-off), materials/coat-switch f22f5f49
- Creature.shader surfaces 0-4 now call PunchyCoat() from Materials/Coat.hlsl (CRLF kept, 739 -> 640 lines). MaterialLibraryTests: Pending emptied, Creature.shader listed as Coat.hlsl's user.
- Edit-mode 43/43. Shader compile: 3 warnings in Atmosphere/Material/RealLights, none in Coat code.
- Same-pose shots (RedFox, Chicken, GilaMonster, Frog, Scorpion; 11:00 Clear, spin off), pixel diff vs two before runs: fur/scales/skin/chitin within noise. Feathers was above noise; viewed one pair: only the cast shadow and mountain haze differ, the plumage matches.


## 11-17 #550 pier planks, b/fix-pier-planks 6bfde9c9
- Cause: the darker-footing recolour (Kit x3, Village x1) multiplied the whole vertex colour below 0.5 m, alpha too. Wood alpha holds grain axis + look, so low decks decoded to stray looks (-2, -5, 6) that Wood.hlsl falls through to shingles. Not the roof surface: deck planks were look 0 all along.
- Fix: FloraMeshBuilder.DarkenBelow darkens rgb only; the four footing passes use it. Mesh probe after: dock, wharf, boardwalk, shore, stilt house, sea-folk pier decode only looks 0/1/3. Boardwalk railings sit above 0.5 m, so they were never affected.
- Beach-glare pose before/after (viewed after): dock planks plain grain, no shingle rows. Edit-mode 43/43.
- Stairs NOT done: StructureKit.Stair builds solid Stone blocks to the ground; open treads need geometry in world's file. Proposal waiting for OK (see handoff).


## 11-17 #550 pier stairs, b/pier-stairs 2d8e0828
- Stair moved to StructureKit.Stairs.cs (StructureKit.cs 16 lines smaller) with an optional material, default Stone. Wood: 0.05 m plank treads overlapping 4 cm in plan, on two stringers at +-0.45 width. Stilt house and marsh walk pass Wood. Houses pick stone when the culture has quoins (dwarf, human) and wood otherwise (elf, the 0.2 culture). Cave code untouched.
- The landing started 15 cm past the top tread, which left a hole in the walk support. It now starts at the tread's edge (stone stairs too).
- New StairSupportTests: every tread holds a capsule (middle and +-0.25 m) for stilt house and marsh walk, variants 0-2. It also checks the bottom tread is in step reach and the support reaches the landing. Support cells are 0.35-0.53 m, wider than a tread, so a cell reads the neighbouring tread: the tolerance is one rise. Edit-mode 45/45.
- Found, not fixed (outside the card): Platform deck planks have 8% gaps (W-58), so some support cells on stilt walkways read empty.
- Beach-glare same-pose after shot viewed: open treads and stringers on the stilt house stair. Editor reached via --project-path; --pid and --port still fail discovery.

## 11-17 #550 pier props, b/pier-props d8b47d4b
- Ropes and nets: Cloth on bark carries a rope mark; Flora.shader draws PunchyRope (Cloth.hlsl, 4 cm twist) and skips the built-stone surface.
- Hung fish: new KitMaterial.Fish drawn by FloraGenerator.HungFish from FloraSpecies.HungFish (real fish species proportions and coats; fauna files only read). Hung on cords by StructureKit.HangFish in the StructureKit.Fish.cs partial, so stalls can reuse it. Sea set Mackerel/SilverFish/Trout, marsh set Trout/Carp.
- Root cause of "draws as stone": the shader's stone flag covers every non-wood built part. Still hit: Trim/Roof accents and buoy floats (Plaster), plaster/brick/ice walls (stone on top of their own include), and the moored boat hull. The fix is a small shader flag change and needs an OK.
- Same-pose after shot viewed: fish read as fish, rope strands visible on the mooring knot. Edit-mode 43/43. Editor reached via --project-path.

## 11-17 Stone fallback fix, b/fix-stone-fallback 9fa4278c
- Flora.shader: the stone flag now skips plaster, brick, ice and the new paint, so each keeps its own look. Plaster and brick keep 0.3 of the rock relief; paint, ice and rope keep none.
- New MaterialFamily.Paint (Materials/Paint.hlsl, mark 0.041): KitMaterial.Trim and buoy floats (Trim piece on a plaster Roof) get a smooth, slightly chipped coat. Cloth's mark moves from 0.05 to 0.053, and the rope decode moves with it.
- Afloat Trim (boat strakes) is now Wood, so the hulls come out as wood.
- Check: matched before/after shots with MatchedShot.Hold(xz,12,1000) of the pier with buoys, a plaster house and a turtle igloo. Mean diffs 1.9/2.0/2.0, with 3-4% of pixels changed. I viewed the pier pair: buoys are smooth and painted, walls lose the stone mottling. EditMode tests pass, 45/45.
- Follow-up for cloth-metal: Copper and Lamp (Metal) still have mark 0, so they fall back to stone.
- Unity commands still use --project-path D:/Unity/Punchy-materials.

## 11-17 Cloth and metal rows (W-166 #336 phase A), materials/cloth-metal cc879e5b
- MaterialLibrary: ClothKind (Linen, Hide) and MetalKind (Iron) rows, pushed as _PunchyClothKinds, _PunchyMetalLightKinds and _PunchyMetalGlintKinds. Cloth.hlsl and Metal.hlsl read their sizes, contrast, highlight and glints from them. The values match today's, so nothing changes on screen. The old PunchyMetalShade signature stays as an Iron overload.
- Test EveryClothAndMetalKindHasARow. EditMode tests pass, 47/47.
- Check: matched shots (pier, house, igloo) against main's: mean diffs 0.8/0.9/1.5, under the run-to-run noise, so no visible change, as intended.
- It merges cleanly with b/fix-stone-fallback (merge-tree).
- Phase B needs the OK of the lanes that own Flora.shader, Creature.shader and Colossus.shader. It would pass a kind (copper/bronze/gold, wool/felt), use the Wrap/Sheen/Diffuse columns, and give Copper and Lamp kit parts a Metal mark so they leave the stone fallback.
- Unity commands still use --project-path D:/Unity/Punchy-materials.

## 11-17 Phase B copper + felt, materials/cloth-metal-b ea14440c
- Added MetalKind.Copper and ClothKind.Felt rows and shader paths (Metal.hlsl, Cloth.hlsl, Flora.shader). Storm-house Copper Columns (rod, stake) are copper metal now. Bedrolls are felt now.
- Copper Trim stays paint: it is shared with gull bills, marsh paint and float weights. I skipped wool because no wool part exists yet; felt covers the soft-cloth case.
- Creature.shader and Colossus.shader are untouched: neither had a PunchyCoat/material call this change needed. Iron is the same as before because its diffuse row stays 0.45.
- Matched shots (main vs branch): rod diff 16.6 on the rod, 0.6 elsewhere. Bedroll weave stripes become mottled felt (viewed). Goblin 0.08% and colossus at noise level, so unchanged.
- EditMode tests 47/47. EOL unchanged on all 6 files.

## 11-17 Lamp check + copper trim fallback, b/fix-copper-trim-fallback 791c0fe1
- Lamps are already clean: LanternHead caps and trays are Trim (Paint), the glass is a Window part, and pier lamp posts are Wood. None of them takes the stone fallback, so no lamp change.
- The real fallback was Copper Trim (storm-house rod fins, stake wire, ridge strap, gull bills, marsh paint, float weights): it is Metal family with mark 0. It now takes the Paint family (FloraGenerator.Kit.cs, +4/-2).
- Iron lantern caps would need a new KitMaterial in StructureKit.cs, which is over 2500 lines, so I skipped them.
- Matched rod shots (main vs branch): diff 0.5, 0.96% of pixels changed. I viewed the crop: only the fins and wire change, from stone mottle to a flat coat. EditMode tests 47/47, CRLF kept.

## 11-17 W-166 row 2: scarp stone from the biome, materials/scarp-stone e21bddb8
- Each biome names a MaterialLibrary.Stone row (BiomeDefinition.Stone, baked into BiomeBlobData). ScarpBake takes the stone under each scarp's middle and folds it into the shape (Crack by grain, Push and Setback by blockiness) and into the paint (tint, bed contrast, joint grime, polish via ScarpPaint; debris follows). Plain bakes exactly as before.
- Stones set: Basalt for Ashlands and Volcano, Sandstone for Dunes, Mesa and Sonoran, Shale for the wastes and swamps, Tuff for EmberForest, Limestone for Fungal, Grassland and SaltFlat, Ice for Glacier, Crystal for GlassDesert, Granite for Mountains, Sparkling and Tundra, Plain for Temperate. 18 biome assets edited (world files?).
- Current world: 5 scarps (canyon plus 3 notch banks are Granite, cut bank is Basalt). Shape changes, so ground heights near them move: possible hash change, profiler should re-pin.
- Matched shots (main vs branch): canyon 16.4% of pixels changed (blockier granite face, bolder beds), notch 2.8%, bank 4.2%. Tests 49/49 (2 new).
- river-profiles touches only RiverData/WorldTests, not scarp or TerrainSource files, so nothing is held back. Deferred: RibSpacing, Roughness, Jaggedness per stone; a per-vertex stone channel (SurfaceProps has no spare slot).

## 11-17 W-166 row 2b: stone sets scarp gully spacing and chimney odds, materials/scarp-stone-2b 39c2f18e
- ScarpSet gets Gully and Chimneys. ScarpBake.Stone sets Gully from the stone's RibSpacing against Plain's (gully period 22 m times 1 + Gully) and Chimneys from its Jaggedness (the chimney threshold drops by up to 0.25). Plain bakes exactly as before, and the test's zero check covers both new fields.
- Matched shots (row 2 vs 2b): canyon 70.4% of pixels changed, mean 17.5 (granite gullies respaced, more chimneys); notch 0.09%; bank 1.0%. Shots are in PunchyMilestones/W-166/row2b/before and after.
- Edit-mode tests 49/49. Scarp shape changes, so heights near scarps move: profiler should re-pin.

## 11-17 Cave mouth floor matches the ground beside it, b/fix-mouth-floor a7c77cde
- Cause: round a mouth, flat floor drawn by the cave walls' terrain share was plain vertex colour with the bare shape normal. Only steep faces took the shared stone, so the floor turned smooth at the mouth line.
- Fix, inside Cave.shader's terrain-share branch only: flat ground now takes what TerrainSurface gives the ground next to it. That is the W-112 mouth grain read top-down, the albedo Grain (3 and 7 cm cells) and the fine relief octaves (MicroHeight: 3, 0.7 and 0.21 m) in the normal and the colour. Each fades by on-screen size. 0.8 stands in for the terrain's per-vertex bump weight. No colour seam remained, so the collar dim was left alone.
- Matched shots at cave 6 (before vs after): 5 m pose 9.0% of pixels changed, 20 m pose 8.1%. The floor now has the same mottling as the ground beside it. Shots are in PunchyMilestones/mouth-floor/before and after. Shader compiles (only an existing warning in PunchyAtmosphere).

## 11-17 Iron KitMaterial for metal fittings, materials/iron-kit c391365d
- New KitMaterial.Iron, added last in the enum (no asset stores it). It maps to the Metal family and draws as FloraPart.Metal with MetalKind.Iron, the iron row Metal.hlsl already has. Its colour (0.20, 0.20, 0.22) comes from a ColourOther helper in StructureKit.Parts.cs, so StructureKit.cs only gained the enum line.
- Moved onto Iron: lantern caps and drip trays (LanternHead, so every street, door, pier, tunnel and wall lantern), the lantern part's hanger, sign chains, brazier legs and bowl, the candelabra, the sconce arm, the wall lantern bracket, the beacon fire bowl and the barrel hoop. Wooden lamp arms, chest lids and painted floats stay as they were.
- The smithy's anvil was already drawn as metal (FloraGenerator.Parts Anvil), and the smithy has no kit trim, so it is unchanged: 0.00% of pixels changed. Street lamp: 0.97% changed, the caps now dark iron. Brazier: 3.4% changed.
- Model Lab matched shots (FixtureLantern, Smithy, FixtureBrazier) are in PunchyMilestones/iron-kit/before and after. Edit-mode tests 49/49.
- Profiler: the kit fingerprint hash mixes in each part's material, so fixture and house hashes change. Re-pin.

## W-163 row 1 (#330) - shared wood grain (2026-11-17)

Branch materials/wood-grain, commit fd145111.
- #544/W-166 already had rings, pores, knots, the 3-tone ramp and fades by on-screen size in Wood.hlsl. This row filled only the gap, in the same file through MaterialLibrary (rule 33, no second wood system):
  - a true domain warp of the grain coordinates;
  - dirt as soft darker bands that average out over a piece (the old term was symmetric +/-);
  - a per-piece seed for along-axis timber, which always used seed 0 before (shingle courses reuse it).
- Our own code from the techniques; nothing copied. Far tone unchanged (rule 25). Elf pieces untouched.
- Matched shots in D:/Unity/PunchyMilestones/W-163/before and /after (one session, clear noon). Mean luminance before -> after: pier close 125.7->124.9, house close 139.1->144.7, crate close 133.2->134.6, pier 20 m 160.7->159.3, house 20 m 157.0->154.1, crate 20 m 113.2->112.4. NaN 0. Viewed the close pair: the pier ramp shows lengthwise grime and grain; crate faces change little.
- Tests: 2 new edit-mode tests (layers pinned; Flora/Cave/Creature compile with no errors). MaterialLibraryTests 9/9, full EditMode 49/49.
- Cost: about 2 extra value-noise calls per wood pixel. Suggest a BENCH-QUEUE check; I did no timing.

## W-163 row 2 (#330) - static strangler-fig roots (2026-11-17)

Branch materials/strangler-roots, commit 791c9a5c. Our own code from the technique; nothing copied. No animation.
- Trunks: FloraSpecies.Strangler (0-1). Bogwood is set to 0.7, so about 7 in 10 variants grow twisted strands around the trunk. They come from their own seed stream, so the tree's other draws stay put. There are none at LOD2; LOD1 skips the thin strands.
- Triangles per Bogwood variant, LOD0/1/2, before -> after: v0 16040/1730/112 -> 17188/2006/112; v1 6872/1730/112 -> 7856/2006/112; v2 6872/1730/112 -> 8020/2006/112; v3 5735/1451/112, no roots. Net about +1000-1150 at LOD0, +276 at LOD1, 0 at LOD2. NaN 0 across all variants and LODs.
- Caves: a new shared include, Materials/Roots.hlsl. The Cave.shader diff is 3 lines (the include, plus one call on natural walls only: no dressed stone, no tunnels). Patches follow noise, damp and moss, times bare. Strands run on world axes blended by the normal; a per-facet axis broke them into chevrons at triangle edges. The relief fades by on-screen size.
- Shots are in D:/Unity/PunchyMilestones/W-163/roots-before and roots-after (tree close, tree 20 m, 2 cave walls). Final caves are in roots-after3. The cave shots use a probe torch light, because those caves are pitch dark. Viewed the tree-close and cave-rock pairs: pale twisted roots on the trunk, braided strands on the wall.
- Tests: new StranglerRootsAreStableAndLeaveTheFarLevelAlone (deterministic, LOD2 unchanged, roots grow). Full EditMode 52/52. Cave shader compiles with only the existing PunchyAtmosphere warning.
- BENCH request (via supervisor): Bogwood trunk geometry. LOD0 is +7-17% triangles per tree in poison-swamp forest views. Optionally also cave-wall pixel cost: up to 2x3 root columns per natural wall pixel in patches. I did no timing.

## W-163 row 3 (#330) - cracked bark per species (2026-11-17)

Branch materials/cracked-bark, commit d99dd918. Our own code from the technique; nothing copied. TerrainSurface.shader untouched.
- One new include, Materials/Bark.hlsl. Every living trunk, branch and stem draws through it (rule 33). It replaces Flora.shader's inline furrow block. Kinds come from MaterialLibrary.BarkKind and are set per species:
  - Plated: Oak, Pine, AncientTree, FungalOak, CanopyTree, WhitePine. Deep plates that get deeper with instance scale, so older trees crack more.
  - Checked: Birch, SilverTree. Fine checks and lenticel dashes, fissured and darker low on the trunk.
  - Smooth: Beech, PaloVerde.
  - Furrowed (the old look, plus sparse cracks): every other species. Ember/veined/strata bark keep their own marks.
- The kind rides on the bark mark at 0.002, 0.004 and 0.006, under the wood band; only living plants with no family read it. The asset diffs are one line each. Every detail fades by on-screen size, and the far tone stays the vertex colour (rule 25).
- Shots: D:/Unity/PunchyMilestones/W-163/bark-before and bark-after (oak and birch, close and 20 m; before is origin/main code). Viewed the close pair: the oak shows dark-edged plates, the birch shows checks. At 20 m the change is subtle.
- Tests: new BarkKindReachesTheMark (the birch mark reaches the mesh and clears when reset, the band stays under wood, and Bark.hlsl defines match the enum). Full EditMode 53/53, up from 52. Flora compiles with no errors and no Bark warnings.
- BENCH request (via supervisor): per-pixel bark cost is up. That is a 3x3 jittered-cell search (9 hashes x3) plus 2-3 value-noise calls per near bark pixel. Past the crack size on screen it is skipped by a branch. Forest close-up views are the case to time. I did no timing.
- For you to decide: PunchyBarkCells (wrapping F2-F1 cell edges plus a per-cell tone) could serve as a shared crack-cell helper for the R-33 salt-flat cracks. It currently lives in Bark.hlsl.


## W-165 row 1: farm fields plan (materials/farm-fields)

Note: the card names world (REPORTS-world, world/farm-fields). The coordinator gave it to materials, so it is here. Placement touches SettlementBaking/LandmarkBeds, which is world's lane. **I ask world and you to confirm before row 2.**

- **Which towns get fields:** HumanVillage (radius 82), DwarfHold (70), ElfGrove (90), plus the fishing, sea-folk and turtle villages (small strips only). Mining, goblin and ogre camps get none, or at most one scrappy patch. Fields go in a ring from the town radius (the palisade or the last plot) out to about 1.8x radius, along the roads out. Rules: ground slope under 25 % (steeper ground gets terraces, as in W-164); not on water, roads or other landmark footprints. Count is about radius/12, so roughly 6-8 for a village, each 15-40 m by 10-25 m.
- **Shapes:** Fields are long strips with their long side on the contour, so they read as patchwork from above. Each gets a random size and angle jitter and keeps a 2-3 m gap for its boundary.
  - Boundaries by culture: human hedgerows and rail fences (reuse the RoadFence rail); dwarf dry-stone walls (stone mark 0.142); elf stone-edged beds with no wood (W-161); sea folk wattle.
  - A worn track runs from the town's gate or road to each field's gate. The track is a Road-0 SettlementStreet stretch, so it gets surface paint and folk paths for free.
  - About one field hut per 3 fields, using an existing small House kit piece at farm scale. Each field also gets a scarecrow or tool rack and a few failed patches (bare soil cells).
- **One system (rule 33):** a new founded form, FloraForm.Field, sibling to Garden. It is one instance per field, with the rows baked in and the field's size passed in. Each crop is a FloraSpecies entry (leaf colour, height, clump shape, frost or glow) carrying a crop-kind value that picks the clump builder. The biome's species list chooses which crop a town plants. The code goes in a new partial file, FloraGenerator.Fields.cs, so no large file grows.
- **Crop looks:**
  - temperate: swaying grain rows (gold-green, wind-bent cards), cabbage rows, bean poles, an orchard row along one edge;
  - cold: snow potatoes (pale leaves on banked mounds, frosted tops), dark hardy kale, a glass cold frame;
  - desert/volcanic: blue-grey agave rosettes on terraces with stone channels, or black ash mulch;
  - marsh: rice in paddy cells. This reuses W-153's PaddySize cells: the field claims cells and plants green tufts in the water. No new water.
  - salt flats: low fleshy succulents in raised plank or stone beds;
  - elf: stone-edged beds of glowing herbs (an emission mark, soft blue-violet).
- **Cost class (proposed; BENCH to confirm):**
  - Each field is 1 flora instance plus 1 boundary instance, plus a hut or scarecrow on about one field in three. A village adds about 15-25 instances.
  - LOD by ScreenSize (rule 20): LOD0 has every clump; LOD1 merges rows into ribbons; past that an impostor.
  - The far tone comes from the ground: each field paints a tilled or crop colour stripe into the surface blend, as streets already do. So from 500 m the patchwork is ground colour that never pops off, with no hard end line (rule 25).
  - Levelling: one LandmarkBed per field, plus terrace steps on slopes, using the GroundEdits profile.
- **Anchors (row 3):** each field adds AnchorKind.Garden-style spots (hoe, harvest, water, scare birds) along its rows. The Home stays the farm house. I'll send the folk-lane note to you.

### W-165 row 1 DONE: farm fields, human and dwarf towns (materials/farm-fields 968fdc56)
- **Counts (seed 8121, v3):** 16 fields. Human village: 9 (6 agave, 3 beans). Dwarf hold: 7 (6 kale, 1 snow potato). Dwarf mining camp: 0. Grain, cabbage and barley are defined; this seed's slots did not roll them.
- **Vertices per field (LOD0/LOD1/LOD2):** grain 20448/11616/1224, cabbage 18198/4776/1440, beans 12048/4956/552, agave 20952/14232/696, snow potato 10104/7632/1176, kale 15138/5724/744, barley 15288/10344/1368.
- **LOD:** new DetailPolicy.FloraClass.Field rule: Geometry 1150/200/0 px. LOD0 hands off to LOD1 at 1150 px, about one field width away (about 13-27 m at Detail 1, depending on field size). LOD1 hands off to LOD2 at 200 px (about 127 m for agave). Before this, fields used the Structure rule (295/75), which kept LOD0 out to about 91 m. ContactShade treats Field like Structure.
- **Hashes:** landmarks 114 DE33C724 unmoved. Fauna fingerprint 3B4B93C6 -> 69053A54: field plots join the settlement plot list and the flora blob gains 7 species, so fauna spawn spots near the towns shift. Re-pinned in WorldTests.
- **Tests:** PlayMode 66/66, including TempleSockets_Pinned (73 sockets, C41A5BA2). EditMode 53/53.
- **Shots (PNG):** D:/Unity/PunchyMilestones/W-165: human-village-near/far, dwarf-hold-near/far, agave-3m/15m.
- **BENCH request (via supervisor):** frame time and flora draw/vertex count at the human village and the dwarf hold, uncapped, NaN 0. Compare against main 80d02c94.
- **Row 2:** rice paddies, elf beds, sea village, huts/scarecrows and tracks.


## W-165 row 2: paused for the cheap-fields question (materials/farm-fields-2, WIP 9fec7dc5)
- **Done (WIP, compiles, not shot or tested):** rule 35 trim (a3eba169). Biome borders: adobe wall, dry brush fence, stone kerb. Field hut on prop 2. Glow-herb crop. Worn track from the gate toward town. Species assets are not wired yet.
- **Cost now (verts per field mesh, LOD0/1/2):** grain 20-21k/12k/1k, cabbage 18k/5k/1.4k, beans 12k/5k/0.5-1k, agave 21k/14k/0.7k, dwarf 10-16k/5-10k/0.7-1.4k.
- **Already shared:** fields are 3 variant meshes per crop, drawn instanced, so there is no unique mesh per field. The soil boxes are about 2% of the verts (17 rows x 24 verts).
- **Open:** (1) furrows in the shader need a mask plus a furrow coordinate per vertex, and no channel is free. (2) Per-crop instancing goes through FloraPlacement, which is 2957 lines (over the 2500-line limit). Questions sent to the supervisor.


## W-165 row 2: cheaper crops + wired species (materials/farm-fields-2, f6330187, DONE)
- **Verts per field LOD0 (3 variants), before -> after:** grain 20.4-21.2k -> 4.8-5.0k, cabbage 17.8-18.7k -> 5.4-5.6k, beans 11.8-12.5k -> 4.1-4.3k, agave 20.4-21.0k -> 4.0-4.1k, dwarf barley 14.7-15.3k -> 4.1-4.2k, kale 14.6-15.9k -> 4.7-5.0k, snow potato 9.9-10.1k -> 4.9-5.0k. New: desert beans 5.0-5.1k, elf glow herb 4.0-4.2k. LOD2 (far read) is unchanged.
- **How:** grain and rice as 2 crossed cards; cabbage, kale and potato as one blob; agave as 4 tapered blades; beans as pole + blob; glow herb as blob + 2 glow diamonds. Hedge, wattle and dry-stone edges are thinned. LOD1 is not pulled in: it is now only 25-40% cheaper than LOD0, which doesn't justify the pop risk.
- **Wired:** HumanDesertBeans (dusty, brush fence) in Sonoran/Mesa villages. HumanBeans is now Grassland/Temperate only. HumanAgave gets an adobe wall. ElfGlowHerb stone-kerb beds at elf groves (10 at the test grove). Borders, hut and track come from WIP 9fec7dc5.
- **Pushback, rice on paddies:** placing rice needs either FloraPlacement reading the terrain paddy blob (2957 lines, terrain code is outside the lane) or biome asset edits (outside the lane). CropKind.Rice is built. Cheaper option for an OK: an aquatic rice-tuft species (water depth about 0.05-0.45) in the Grassland/Temperate flora lists, so it lands in the paddy pools.
- **Pushback, sea villages:** these are landmark-prop villages with no layout, so FieldBaking can't reach them. A small prop plot could come later.
- **Shots (PNG):** D:/Unity/PunchyMilestones/W-165/r2: human-village-near/far, dwarf-hold-near/far, agave-3m/15m, desert-beans-near, elf-herb-near, elf-grove-far. Before shots: D:/Unity/PunchyMilestones/W-165. Agave luminance is lower (80 -> 58) because of the darker adobe wall and the sparser blades, not a lighting fault.
- **Tests:** EditMode 53/53. PlayMode 66/66 (65/66 on the full run, with only Fauna_Fingerprint failing; re-pinned 69053A54 -> 4EE0D127 for the new plots, and that test passes on rerun). Commit f6330187.
- **BENCH (via supervisor):** draw calls and flora verts at the human village, the Sonoran village and the dwarf hold, uncapped. Compare against main 80d02c94.

## W-165 row 3 rice tuft (#335) - DONE, branch materials/rice-tuft 122b1367
- RiceTuft (Reeds form, Floats) in Grassland + Temperate flora lists, depth 0.05-0.45, still water only.
- River check: 0 rice in 13 river shallows (reach -761,59,1930); still shallows: 240 rice at 59 points (ponds too).
- No Grassland/Temperate paddies on this seed (all 124 paddy cells are PoisonSwamp/Ashlands/EmberForest), so shots show a rice pond: PunchyMilestones/W-165/r3 near/far/river.png.
- Fauna fingerprint re-pinned 4EE0D127 -> 8E24FECC (144 species). EditMode 53/53, WorldTests 20/20, full PlayMode 65/66 before re-pin.

## W-161 elf towns without wood (#326) - DONE, branch materials/elf-no-wood a458023d
- FloraMeshBuilder.TimberFamily (Wood; Stone for elf species): b.Wood and Timber(g) both use it, so the shared sign, bridge, fence, stable and prop builders go stone for elves. No forked code (rule 33).
- Elf copies of Signpost, Bridge, Garden, HitchRail and Stable (pale stone, KitCulture Elf) are swapped into ElfGroveLayout. ElfRamada, ElfShrine, ElfSpire and ElfTower are marked Elf. Bark wood count is 0 on all of them (ElfBench too).
- Still wood: KitHouse roof shingles (StructureKit.Surfaces gives elves Shingles, and MaterialLibrary maps that to Wood) and the shared hazard shelter houses. Making these stone needs a world-lane Surfaces change or a stone-shingle look.
- Shots: PunchyMilestones/W-161 elf-street/mid/far (lum 141/124/105). EditMode 53/53, WorldTests 20/20. Fauna fingerprint 4EE0D127 -> 9430B213, which conflicts with rice-tuft's 8E24FECC: re-pin after the second merge.

## W-161 elf-no-wood fixes (2026-11-18) - materials/elf-no-wood ec3d9144, pushed
- Speckle: the cause was the Flora.shader metal-kind decode. frac() on an interpolated 3.99999 gave kind 10 (out of range), so every flora metal (elf canopies, tower roof, porch roof, signpost fittings) went hot. The kind is now read off the part and clamped to 0-4. DepthNormals now uses SafeNormalize (it was producing NaN). Sparkle probe on the street: other bad 0, hot 365k -> 5.4k over 20 frames.
- Door and shingles: added MaterialLibrary.FamilyOf(material, look, timber). Kit doors and wall/roof families now use b.TimberFamily, so elf doors are stone. Stone timber also drops the plank/shingle wood mark, so elf roofs are stone with slate courses (RoofCourses). StructureKit.Surfaces is unchanged.
- Merged origin/main and re-pinned the fauna fingerprint once, to 9EA6387D. WorldTests 20/20. Shot: PunchyMilestones/W-161/elf-street.png (viewed: clean).
- Tails: hazard shelters stay wood. The elf door keeps its tan trim colour (stone family, no grain). The metal decode and NaN fixes change all flora metal, not just elves.

## W-166 row 2 (#336) - 2026-11-18 - branch materials/w166-scarp-stone 80b5fb0a
- Scarps and river notches already took the biome's MaterialLibrary.Stone (#564/#567). Caves took their style's stone instead, so a cave and a cliff in one biome could use different stone rows. CaveBaking now gives a cave's wall relief from ScarpBake.StoneAt at the mouth (CaveStyle.ReliefIn). Themed styles keep their own stone: ice, crystal, ore.
- Changed: Mesa cave plain->sandstone, Tundra mine shale->granite, plus the fungal, electric, ember and glass caves. Mountains and Ashlands (the only biomes with scarps, 5 in all) already matched, so the shots there are unchanged. The lighting differs between runs.
- Not done: the cave colour deep inside still comes from the style. The country-rock fade is in CaveSystem.RockLook (RockReach 30 m), which is World's mouth area. Matching the colour deep inside needs that file, or a per-cave stone channel, so I stopped there. Elf hazard shelters are biome species with no culture, so making them stone means passing the culture per settlement. Not cheap, skipped.
- Shots: D:/Unity/PunchyMilestones/W-166/row2/ (scarp, notch, cave, mesacave, far; before/after). Sparkle NaN 0 (mesacave and scarp, 20 frames each). WorldTests 20/20.

## b/fix-cave-veins: Mesa cave white squiggles (no change, as ordered)
- Cause found: the bright squiggles are Cave.shader's mineral veins (abs(noise-0.5) contour lines, pale calcite 0.88 albedo plus gloss 0.55, added in A-91 row 2 3cf43ad0), not #583's cracks. With `vein = 0` as a temporary edit they all vanish: D:/Unity/PunchyMilestones/cave-veins/mesa-noveins.png vs W-166/row2/mesacave-before.png.
- They're meant to be there (W-138 row 4b already turned them off in road tunnels for reading as white squiggles), so per the order nothing is changed. The edit is reverted, the branch is not pushed.
- Proposed fix if wanted (shader only): drop calcite to a darker tone near the rock colour, cut the 0.85 blend to about 0.4 and the gloss, or gate veins to ore and crystal styles.

## b/fix-cave-veins: veins toned down (acdd2690, pushed)
- In plain stone caves the veins are now sparser, thinner seams tinted towards the rock, with blend 0.4 and gloss 0.3. Ore, crystal and ice styles (metallic, facets or translucency of at least 0.5) keep the bright veins. Only Cave.shader changed.
- Shots in D:/Unity/PunchyMilestones/cave-veins/: in mesa-before/after (matched, both viewed) the netting is gone and a few faint seams remain. crystal-before/after is the Crystal style cave at -2754,-643 and is unchanged (mean diff 0.86/255, 0.25% of pixels over 16, all sparkle particles).
- Gotcha: the first shot after AssetDatabase.Refresh can catch the cave mid-reimport (dark, with the mouth openings missing). Retake it.

## 2026-11-18 W-165 row 4 (#335 tail): sea-village plots - DONE, materials/sea-plots a193ecd0
- FishingVillage gets 2 SeaBedBox (kale in a wattle bed, ring 0.28) and 2 SeaKelpRack (new CropKind.Kelp: pole racks with hanging strips in a stone kerb, ring 0.45), all on the W-165 Field form. WetPlot (PlotKit) keeps them off water and slopes over 1.5 m. Fields under 8 m skip the scarecrow, tool rack and hut.
- Verts per plot (LOD0/1/2): bed box about 1000/960/220, kelp rack about 610/584/420.
- Pushback: racks sit on the shore next to the wharf, not on the piers (props stand on terrain). Rice skipped: these villages sit at river mouths with no still shallows.
- Shots: PunchyMilestones/sea-plots v104 and v103 before/after. In the far view the ground is unchanged; only sky and mist differ.
- WorldTests 19/20. Fauna_Fingerprint re-pinned 9EA6387D -> 29237322, and it passes on the rerun.
