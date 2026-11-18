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
