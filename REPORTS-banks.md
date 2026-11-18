
## #301 row 1 river bank details: banks/river-details@6ee1b863
- New flora species DriftBranch, DriftSnag, BankRock (wet, darker tint), PebbleBar (inside bends) in all 13 biomes via RiverFit Snag/Waterline spots; MaxRapids thins reeds/cattails in fast reaches, none on fall lips. Per-reach draws scrambled; landmarks untouched.
- Shots (PNG): D:/Unity/PunchyMilestones/river-details/before|after/{river,lake}-{5m,25m}-{before,after}.png + contact-sheet.jpg. Rocks and pebble bars read; driftwood is placed (probe counts) but rarely framed. More pines in river-25m-after look like streaming timing; layer 2 is untouched, so this was not looked into.
- WorldTests 20/20 after the Fauna_Fingerprint repin 525F72BB -> 6A566658 (the flora blob gained four species; landmark and world tests passed unchanged).
- Bench line added. Note for materials: flora boulders (BankRock, RiverRock) could move onto MaterialLibrary.Stone later.

## #301 row 1 review fix: banks/river-details@40c72cf3 (draw keys)
- Cause: FloraPlacement.Build keyed PatchOffset/GroveSeed by sorted species index, so the 4 new species shifted later species (trees +4). Now LateAddition species key by name and skip the running index.
- Placement probe, 120 m box round the river 25 m pose, main-equivalent blob vs branch: L2 trees 115/115, hash E9B86B40 both, 0 cells differ (was 115 vs 125, 93 cells). L0/L1: 0 differing cells except those the new species take (42/39).
- Retook after shots + driftwood-5m-after.png (D:/Unity/PunchyMilestones/river-details/after/). WorldTests 20/20, Fauna_Fingerprint repinned 8D2194F3.
