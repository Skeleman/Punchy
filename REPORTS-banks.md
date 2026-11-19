
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

## #301 row 3 (point bars): banks/point-bars@8359db2c
- RiverData.cs (minimal diff; functions touched: RiverQuery (+Bar), Join (BarBlend 0.5 m), Consider, Query (+barScale), PointBar, BarWidth, BarMask). On inner bends opposite a cut bank, the rough carve lowers the ground into a low bar sloping gently to the water, wider on sharper bends. It fades within a cell of a lake, because the lake surface would otherwise stand as a wall over it.
- TerrainSource.cs: the new RiverAt fades the bar within 24-40 m of a cave's bounding box, because lowering ground under a mouth rim flips its snapped triangles. GroundSample and GroundContext gain a PointBar flag (BiomeData). Tint is left to the materials lane.
- FloraPlacement.cs: RiverFit adds pebble patches across the bar, the odd driftwood pile at its head and small reeds at its tail (row 1 species). UndercutLip now drops lips that aren't on a cut face.
- Shot: river-details/point-bars/pointbar-25m.png shows the cut bank and the bar at -1268,630. Water walls are still 36 (the baseline).
- Pins unchanged: Fauna_Fingerprint 59A4BFD3, plus the landmark and temple pins. WorldTests 21/21; MaterialLibraryTests 13/13.
- Rebased onto main at 730d76e1 (#622 fall-spray; both GroundSample fields kept) and the two comments shortened. Water_TruthCounts now fails at uphillGraph 19 > 10 on the base as well (main regression, not this branch). Setup_SpawnRainAndShot timed out once in the full run but passes alone.

## #301 row 4 (river logs + gravel islands): banks/river-logs@ae15d970
- Pushback: no stream is under 6 m, because rivers start at 9 m (RiverSettings.MinWidth). Logs go on streams 10.5 m wide or less instead, and FallenLog is now 13-15 m long so it reaches both banks. Logs sit at the centreline on about 40% of reaches (ReachDraw), giving 10 logs over about 1300 m of narrow river (roughly 1 per 190 m). Some come as pairs or triples, which read as a small logjam. Both ends rest on dry bank no more than 1.5 m apart in height, with a slight tilt. Code: FloraPlacement.RiverLogs.cs (new); FloraPlacement.cs shrank.
- Rule 33: FloraSpecies.TrunkOf gives a log the bark colour, kind and wood of its biome tree (FloraGenerator, WoodKindFor). New FallenPine, FallenBogwood, FallenSilverTree and FallenCanopyTree replace FallenLog in Mountains, PoisonSwamp, SparklingForest and FoggyMarsh; other biomes keep the generic FallenLog. Also fixed the corrupt `Variants` line in FallenLog.asset, which was already on main.
- Islands (RiverData Isle/IsleMask, TerrainSource RiverAt): about 7% of wide, slow, straight segments get a teardrop gravel dome a fifth of the channel wide, with its crown 0.3-0.4 m above the water. It only lifts the carved bed, never past the terrain, and fades near lakes; the point-bar cave fade covers mouths. The probe found 24 islands. They carry reeds, pebbles and a head snag (point-bar flag); there is no sapling.
- WaterTruth dryRun skips island crowns, which are dry by design (23 hits otherwise). Fauna_Fingerprint repinned to 769A0D77. WorldTests 21/21, uphillGraph included; MaterialLibraryTests 13/13.
- Shots: D:/Unity/PunchyMilestones/river-details/logs-isles/{before,after}/log-a, log-b, isle-a, isle-b (+ contact-sheet.jpg). I viewed after/log-b: two pine logs span the stream, both resting on the banks.

## #301 stepping stones + splash boulders: banks/stepping-stones@9aa8dbde
- Riffles: on narrow fast reaches (gradient 0.015+, wet span 3.5-14.6 m, low banks on both sides) a row of 3-6 flat stones crosses the stream, one reach in five, giving 9 rows. Rows are scaled 0.8-1.3 so the end stones sit near the banks. Wet spans under 11 m are rare, so most rows have 6 stones.
- Splash boulders: in rapids reaches (gradient 0.03+, depth 0.35-2.2 m) one reach in five gets a big boulder, 42 in all. The rapids spray (#608) adds a foam tail downstream and a pillow upstream; LastSplash is 5 at -1396,585.
- Both keep 24 m clear of falls (fall pools) and off river islands. Spots are anchored to segments, so flora and spray agree; 40 of 44 spots placed in the probe. Code: FloraPlacement.Riffles.cs and FloraGenerator.Riffles.cs (new); RiverSpot gains Riffle and Splash. RiffleStone (density 4) and SplashBoulder (density 3) are in every land biome. FloraPlacement.cs is 6 lines shorter.
- Fauna_Fingerprint repinned to 785BB213. WorldTests 20/21 before the repin (the fingerprint only), and Fauna_Fingerprint passes after it; MaterialLibraryTests 13/13.
- Shots: D:/Unity/PunchyMilestones/river-details/stepping-stones/{before,after}: riffle-a and splash-a, plus riffle-b in after only (the before run timed out on it). I viewed after/splash-a: the boulder with its foam tail, and the riffle stones downstream.

## #301 splash-round (PR #638): banks/splash-round@83557709
- The pillar in splash-a was a RiverRock (Boulder standing family). Aquatic boulders now use a low, worn block (boxiness 0.1-0.25) in place of the tall standing slab.
- Splash boulders are wider than tall (height 0.8 of width) and rounder, with a smaller side chunk further out.
- Fauna_Fingerprint repinned to 377B279B. WorldTests: 21/22 before the repin, failing only on the fingerprint.
- Shots: D:/Unity/PunchyMilestones/river-details/splash-round/{before,after}/splash-a.png. I viewed after: the pillar is now a low, round boulder.

## #301 drift-heaps (PR #641): banks/drift-heaps@23ed7db7
- New RiverSpot.Heap: bar heads and sharp outer-bend waterlines in about one reach in four. Driftwood there grows a heap of 4-7 logs plus thin sticks; single driftwood unchanged (shared DriftLog helper).
- New DriftHeap species (density 4, LateAddition) in the 13 biomes with DriftSnag. Rapids checks now name Riffle/Splash.
- Rebased onto main (after #638). Fauna_Fingerprint repinned to 1AC019EC. WorldTests 21/22 before the repin, failing only on the fingerprint; Fauna_Fingerprint passes 1/1 after it.
- Spacing: one outer-bank waterline cell per heap reach, and a heap reach must out-draw 2 reaches each way, so heaps sit about 60 m or more apart. DriftHeap density is now 25. Probe of a 1.2 km box: 17 fit cells, 8 heaps. Builder summaries cut to one reason line.
- Shots: D:/Unity/PunchyMilestones/river-details/drift-heaps/{before,after}/heap-a.png. I viewed after: three pale log heaps on the outer cut bank at the waterline. Also after/heap-b.png (-741,1207): one heap at the waterline of an islet bank.
- Heaps cluster a little (probe: 8 within 300 m of -1396,585, 4 on one bend).

## #365 bay mask (PR #657): banks/bay-mask@03474a9c
- Lake bays are now baked as a flood-filled mask on a 4 m grid near lakes. Each pocket is kept or dropped whole, so deeper ground beside a wet bay is never left dry. IslandCache format is now 7.
- Bake: 34 pockets kept (234 cells), 4 open pockets dropped. Walls 49 -> 11: pool walls 37 -> 3, lake walls 5 -> 1. The WorldTests baseline is re-based.
- Landmarks 120 B32C5306 and fingerprint 1AC019EC are unchanged. WorldTests 21/22: Setup_SpawnRainAndShot timed out in the full run and passes alone.
- Shots: D:/Unity/PunchyMilestones/water/bay-mask/{before,after}/bay.png. I viewed both: the stair-stepped bay tongue east of the pond is gone, leaving one clean edge.
- Gotcha: changing TerrainSource's struct size hangs the open editor on a Burst SharedStatic error. Commit first, then restart through EditorApplication.Exit.

## #301 W-153 fall flanks (PR #659): banks/fall-flanks@00c4fbd1 (redo)
- ScarpDebris now handles falls too. Each side of a fall gets a few big ledged blocks with 1-3 small stones under them, plus single small stones. Most sit near the lip and the foot, with gaps mid-face and sideways scatter, so they never form a column. There is a boulder at the lip and one at the foot. 2 draw groups per fall, LOD by DetailPolicy.ScreenSize, draw only.
- Look: a new ScarpBlock.Own weight gives the blocks the face's own rock (tone and strata, sandstone at the tall fall) with no moss and lighter grime. The gloss came from PunchyFallWet: its band reaches 3 m either side of the sheet. Blocks now sit outside that band except near the foot, so only stones in the spray look wet. No shader change.
- Debris blocks: 377 on main, 4963 in the first pass, 1111 now. Landmarks 120 B32C5306, fingerprint 1AC019EC, walls 11 and dry fall feet 0 are all unchanged. WorldTests 22/22.
- Shots in D:/Unity/PunchyMilestones/water/fall-flanks/{before,after}/. I viewed the tall-wide and small-wide pairs. At the tall fall, sandstone ledges now break the cut with no bands. At the small fall the change is subtle: a few stones near the lip.

## banks/fall-ground (DONE, PR #668, e5302ff2)
- Flank blocks use a face-aligned yaw and a rotated-box check on all 4 edges. Blocks >0.3 m clear: centre 146 -> 0, edge 473 -> 0, worst 21.9 m -> 0.29 m. The tall fall's top-left slab is gone.
- Falls under 6 m get wet stones at the lip and foot, moved to the nearest shallow ground (fall 68 now has 2). Debris blocks 1111 -> 956 (cap 1500).
- WorldTests 22/22 on base 03982fd5: landmarks 120 B32C5306, water truth wall 11, dry fall feet 0. Merge-tree onto main 77c5ed3c is clean; debris doesn't touch the fauna pin.
- Shots before/after in PunchyMilestones/water/fall-ground (tall-wide, small-wide, flanks, contact sheets), all viewed. BENCH line queued.
- Next row: the #663 walls (lip sides at falls 51, 63, 92 x2; feet at 85 and 133).

## banks/fall-walls (DONE, PR #676, a8659b4a)
- No re-pin. Main was already 22/22 at landmarks B32C5306 and walls 7. Fauna A9684863 came from #670, not #668.
- Fixed lips 51 and 92 x2: shoulders now start at the narrower of the channel and sheet edges. Fixed foot 85: the bowl rim is capped inside the water edge. Wall 7 -> 4, step 129 -> 125; baseline tightened.
- Still open: lip 63 (rapids on a bend 23 m upstream), foot 133 (sea-mouth slope, lower cell wet by about 0), a new pool-to-river step at fall 95's foot, and the lake rim.
- WorldTests 22/22, river graph unchanged (140 falls). Merge-tree onto main 4c591507 is clean. Scratch scripts are now lane-named.

## banks/fall-feet (11-19): BLOCKED, no PR
- On main 9356e483 the 3 named walls (95, 63, 133) are all steep water: 1.1-2.0 m surface drop per 4 m sample beside a bank. Wet-ing the low cell only turns each wall into a step of the same height.
- Tried 3 fixes: pool floor following the blended water (95), no point bars on rapids, a water film to the channel edge. Best result was wall 4 -> 4, step 125 -> 130; the worst was wall 5, step 140. All reverted; nothing committed.
- Root cause: reaches below falls 95 and 63, and the 133 sea mouth, drop about 25-50% (main already counts 1.0-1.2 m steps there). A real fix limits water gradient (cascade pools or spreading the drop), or the coordinator decides those count as fall steps.

## banks/water-cascades row 3 (11-19): PR #687, verified
- Water steeper than 0.2 m/m on a reach now steps down as registered falls between flat pools. Bake only (RiverProfile, RiverBaking).
- A step is skipped in a lake, near the sea, right below a full fall, or right above one on a reach under 8 m. Without those skips, 4-5 fall feet went dry.
- Falls 140 -> 151; steep 118 -> 69, pits 22 -> 19; tilt 17 -> 15, step 125 -> 108; walls 4 -> 4. Dry feet 0, landmark hash unchanged, WorldTests 23/23.
- Shots: PunchyMilestones/water/cascades/before and /after (fall95-foot, fall63-rapids, fall133-foot).
- Row 2 (pit fill) is next, as its own PR.

## 11-19 W-181 row 2 (#301): puddle lakes go dry - PR #689 (draft, stacks on #687), banks/water-pits@64019e86
- After the last lake pass, a riverless lake of 1-2 cells under 1 m deep is drained; it stood over the ground round it, so its sheet dived into the ground.
- Steep 69 -> 44, pits 19 -> 0. WaterTruth, falls 151, dry feet 0 and landmark hash unchanged. WorldTests 23/23.
- Trade-off: 18 of 65 lakes go dry (small ponds; -1988,-1091 had moss rocks and cattails). The cheaper alternative is to keep them and fix the rim in BuildWater (materials lane). That is why the PR is a draft.
- Shots: PunchyMilestones/water/pits/before and /after (puddle-1988).

## 11-19 W-181 row 2 (#301): ponds get a dug bowl - PR #693 (replaces #689, now closed), banks/pond-bowls@ae7b2179
- A riverless lake of 1-2 cells under 1 m deep is now flagged as a Pond (kind bit 32). The terrain under it is dug into a smooth bowl about 1 m below its level (RiverSampling.PondBowl, at both carve sites). Only ponds under 0.3 m go dry; those were never drawn anyway.
- Rebased on #691. Sheet steep / pits are unchanged at 66 / 16, since the rim is row 4's job. WaterTruth counts and falls 151 are unchanged. WorldTests 23/23.
- Shots: PunchyMilestones/water/pits/before and /bowl (puddle-1988).

## 11-19 W-154 row 3 (#303): toxic pools detail pass - PR #700, banks/toxic-pools@cf19bd58
- No toxic geyser spawns in the current world, so swamp ponds (green glowing look) now count as toxic pools. They show a sickly yellow-green surface with a darker middle, pale bubble froth and less oil sheen, plus a banded pale crust rim (merging to one band at distance, rule 20). The bank dries and crusts pale (TerrainSource.ShoreWet is now the one shore-band helper).
- New flora DeadReed and BleachedStick on swamp shores and the geyser ring. The fauna fingerprint is re-pinned to 01766C0A, since swamp flora picks move a little (landmarks unchanged).
- WorldTests 23/23 (one earlier run lost to an FXC crash mid-compile), sparkleprobe NaN 0, Water shader shows only its two old warnings.
- Shots: PunchyMilestones/water/toxic/before and /after (swamp-3m, swamp-20m).
