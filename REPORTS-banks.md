
## #301 row 1 river bank details: banks/river-details@6ee1b863
- New flora species DriftBranch, DriftSnag, BankRock (wet, darker tint), PebbleBar (inside bends) in all 13 biomes via RiverFit Snag/Waterline spots; MaxRapids thins reeds/cattails in fast reaches, none on fall lips. Per-reach draws scrambled; landmarks untouched.
- Shots (PNG): D:/Unity/PunchyMilestones/river-details/before|after/{river,lake}-{5m,25m}-{before,after}.png + contact-sheet.jpg. Rocks and pebble bars read; driftwood is placed (probe counts) but rarely framed. More pines in river-25m-after look like streaming timing; layer 2 is untouched, so this was not looked into.
- WorldTests 20/20 after the Fauna_Fingerprint repin 525F72BB -> 6A566658 (the flora blob gained four species; landmark and world tests passed unchanged).
- Bench line added. Note for materials: flora boulders (BankRock, RiverRock) could move onto MaterialLibrary.Stone later.

## #301 row 1 review fix: banks/river-details@40c72cf3 (draw keys)
- Cause: FloraPlacement.Build keyed PatchOffset/GroveSeed by sorted species index, so the 4 new species shifted later species (trees +4). Now LateAddition species key by name and skip the running index.
- Placement probe, 120 m box round the river 25 m pose, main-equivalent blob vs branch: L2 trees 115/115, hash E9B86B40 both, 0 cells differ (was 115 vs 125, 93 cells). L0/L1: 0 differing cells except those the new species take (42/39).
- Retook after shots + driftwood-5m-after.png (D:/Unity/PunchyMilestones/river-details/after/). WorldTests 20/20, Fauna_Fingerprint repinned 8D2194F3.

## #301 row 1 main merge + grain fix: banks/river-details@50a84903
- Merged origin/main (#605 gatehouse). Fauna_Fingerprint repinned F00119AC; landmarks 5B47CDAE and temple pins unchanged. WorldTests 20/21: Village_CreatureDensity read 54 against a cap of 50, then passed alone. The count varies run to run (30/44/54 seen), so it's a flaky cap, not this change.
- Grain across the logs: static Log/Driftwood forms had FormFamily None, so they drew through the stone surface (a fan pattern round the log). Now they're MaterialFamily.Wood, so the shared GrainAlong path runs the grain along each tube, root stubs and snag branches included. MaterialLibraryTests 13/13.
- Shots: after/driftwood-5m-after.png (retaken) and after/driftwood-1m-after.png (new close-up; fine grain fades by 5 m by design).

## #301 row 2 bank overhangs: banks/overhangs@4c9a1510
- New RiverSpot.Undercut and a BankLip Boulder species, at density 10 in six biomes. Each lip is plaster-earth blocks with a turf (moss) top, a dark underside and hanging roots. The roots use a shared HangingRoot helper that the cave roots now use too (rule 33). Placement: dry outer bends only. The lip moves out to the bank edge and turns to face the water (331/331 face downhill). No river geometry files touched.
- Shots: river-details/overhangs/overhang-5m.png and overhang-25m.png, at the steepest outer bend (-1266.9,68,641.6). The lips read clearly, but they sit on a gentle sandy bank, not in a real cut bank. A true undercut needs a bank-profile hook from world.
- Fauna_Fingerprint repinned 696024B5; landmark and temple pins unchanged. WorldTests 21/21 after the repin; MaterialLibraryTests 13/13.

## #301 row 2 rework (cut banks): banks/overhangs@58d1a8d2
- RiverData.cs (minimal diff; functions touched: RiverQuery (+Cut), Bank/Bank.At (+Cut), Query, Consider, Profile). On outer bends the rough carve now makes a 0.5-1.5 m earth face, sized by bend strength. It has a waterline notch and a flatter grass terrace above, and it fades out by 14 m. Bakes are non-rough, so they are unchanged.
- Lips find the face's top edge, so they line up along it. Each lip is a set of overlapping sheets, 0.6-1.6 m wide, with uneven gaps and lengths. 15% fall to the foot as half-sunk chunks. Density is now 30. The side-root comment is dropped (rule 35).
- Shots: same 5 m and 25 m views. At 25 m the bare face shows, with turf strips along its grassy top.
- Fauna_Fingerprint repinned 59A4BFD3; landmark and temple pins unchanged. WorldTests all pass except the fingerprint (repinned and re-run: pass); MaterialLibraryTests 13/13.

## #301 row 3 (point bars): banks/point-bars@6266ff45
- RiverData.cs (minimal diff; functions touched: RiverQuery (+Bar), Join (BarBlend 0.5 m), Consider, Query (+barScale), PointBar, BarWidth, BarMask). On inner bends opposite a cut bank, the rough carve lowers the ground into a low bar sloping gently to the water, wider on sharper bends. It fades within a cell of a lake, because the lake surface would otherwise stand as a wall over it.
- TerrainSource.cs: the new RiverAt fades the bar within 24-40 m of a cave's bounding box, because lowering ground under a mouth rim flips its snapped triangles. GroundSample and GroundContext gain a PointBar flag (BiomeData). Tint is left to the materials lane.
- FloraPlacement.cs: RiverFit adds pebble patches across the bar, the odd driftwood pile at its head and small reeds at its tail (row 1 species). UndercutLip now drops lips that aren't on a cut face.
- Shot: river-details/point-bars/pointbar-25m.png shows the cut bank and the bar at -1268,630. Water walls are still 36 (the baseline).
- Pins unchanged: Fauna_Fingerprint 59A4BFD3, plus the landmark and temple pins. WorldTests 21/21; MaterialLibraryTests 13/13.
