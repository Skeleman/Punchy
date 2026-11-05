# Playtest build, 2026-11-05 (changes since v0.12.0)

Build: D:\Unity\Punchy-world\Builds\Dev\Punchy.exe (main 763edbc, after PR #163). 44 PRs since v0.12.0. Smoke-tested: island ready in 9 s, 0 exceptions. Most important first.

## Worth a look
- **Fire (#131):** one shared fire effect for torches, campfires and sconces. Ember Forest burns at night, in one of 4 colours.
- **Shadows:** cascades follow the graphics preset. Far trees use cheap, cached shadows (the new default). Watch for pops while walking. Measured: about 7 fps faster at the fishing village.
- **Weather reactions:** animals and folk seek shelter in rain, cold and heat, and complain. Desert and snow folk now work outdoors mornings and evenings, and only shade under the eaves at midday.
- **Walking colossi (#40):** the saguaro, rock golem, fungal tortoise, mesa tortoise, and new: a glass heron (GlassDesert) that stabs at the sand and a lantern stag (SparklingForest) whose antlers glow at night. They walk slow loops, turn to look at you, and kick up dust nearby. You can't walk through them.
- **Fishing villages:** stilt houses over water, piers and doors. Folk use their hut doors and work the piers. Gulls on rails and buoy masts, mackerel leaping. (Known: fishing village 1 has no wharf; being fixed.)
- **Roads and towns (#39):** roads turn to cobblestone at a town's edge. Big towns (HumanVillage, the elf town) have a flagstone square round their pond or fountain.
- **River water:** whitewater, waterfall ribbons, spring pools, rain puddles.
- **Bat folk and pygmies (#35):** bat folk live in egg pods hung from the cave ceiling and shun light (fires and torches too). Blind pygmies live in niches in the same chamber. Fish folk walk their settlements.
- **Quests and containers (#26, #27):** chests, a pad inventory, quest prompts, hand-ins and clear-an-area quests.

## Smaller things
- Race reactions (#8): folk greet, eye or avoid you depending on your race.
- Ruins (#80): broken brick walls, fallen bricks, moss, a wind-shaped sand mound.
- Electric Wastelands (#70): flat rock chunks are solid; you can stand on the low ones.
- Harsh-biome shelters, rune stones, the Frontier house style, the white glow at settlements fixed.
- The salt ball is a lumpy meteoroid with a furrow trail; SaltFlat has a rolling crystal.
- Cave flying works; the camera stays above cave water; the character tab keeps its focus.
- Faster island bake, plus several performance passes for animals and plants.

## Still open
- #69 hermit shelters and work: yours to review.
- #40: Ember/Volcano and FoggyMarsh still have no giant.
- #39 tunnels: next world card.

The per-issue checklist is in PLAYTEST.md. Reopen anything that's wrong.
