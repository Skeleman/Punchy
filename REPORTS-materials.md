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
