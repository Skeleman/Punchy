# Playtest list (for Zach)

The supervisor closes issues once they're merged and checked by the agents. This list collects what's worth a look
when you play. Reopen any issue that's wrong; otherwise just delete its line. Newest at the top.

| Issue | What to check | Where | Shipped |
|---|---|---|---|
| Marsh wildlife, colossi | Frogs on lily pads, a heron in the shallows, dragonflies darting low at marsh folk villages; magma, ember and spore colossi glow at night; fewer hitches when the plant, salt and magma colossi appear. New pins Landmarks_V3 112 A955AFE0, Fauna 83902658 | Foggy marsh villages; colossi at night | PR #193, 2026-11-07 |
| Marsh folk | Stilt villages over the water: reed huts, plank walks, a smoke hut; drying frames, eel traps, totems; a nest raft of eggs with a hatchling behind the smoke hut | Foggy marsh villages | PR #192, 2026-11-07 |
| #39 tunnels | Roads go through tunnels under steep crests, with a stone portal at each end | Hilly roads between towns | PR #178, 2026-11-06 |
| Camps, igloos, temple | Leather tents and ramadas in camps; igloos sit into slopes; the sunken temple has sandstone blocks and torch glow, with no plane through the rooms | Camps, igloo villages, sunken temple | PR #170, 2026-11-06 |
| #40 mire walker | A mangrove-rooted giant with fog eyes and a lantern looms out of the marsh mist and walks a slow loop | FoggyMarsh, v3 near -1804,145 | PR #169, 2026-11-06 |
| #40 snail | A huge cinder snail creeps a slow loop through the Ember Forest; its eyes and shell seams glow from far off | EmberForest | PR #168, 2026-11-05 |
| Cave folk | `-incave` lands you in the bat folk chamber on v3; pygmies and bat folk live in their caves | v3 caves, `-incave` | PR #168, 2026-11-05 |
| Cave mouths | No rim where a cave mouth meets the hillside; the ground round it has the cave's rock grain | Any cave mouth | PR #167, 2026-11-05 |
| Far shadows | Tree shadows reach 120 m and are darker. If still too light, launch with `-shadowcachedark 0.8` | Forest from a hill | PR #167, 2026-11-05 |
| Far fires | Burning forests glow from far away at night; embers match the fire's colour | Forest fire at night | PR #167, 2026-11-05 |
| Colossi | The poison plant is twice as tall and sways; the heron's crystals stay on the ground; the saguaro stands on roots; the golem is dark basalt | v3: plant at -goto -53,34,516 | PR #166, 2026-11-05 |
| Colossus paths | Animals walk round colossi instead of into their legs | Golem ring, v3 -goto 729,-335 | PR #166, 2026-11-05 |
| v3 default | New builds start on the v3 island with no flags (`-island v2` still works) | Any launch | PR #165, 2026-11-05 |
| #39 roads | Roads run unbroken up steep slopes now, cut into or banked up on the hillside | Hilly roads | PR #165, 2026-11-05 |
| Waterfalls | Falls show falling white and grey sheets, not flat glowing white. Try fall 74: -goto -2167,3,3691 | v3 at noon | PR #165, 2026-11-05 |
| Wharf | Fishing village 1 now has its wharf by the river mouth (not in the 11-05 playtest build) | Fishing village 1 | PR #164, 2026-11-05 |
| #40 heron | A glass heron wades the dunes, rests, and stabs at the sand; it turns to look at you | GlassDesert | PR #163, 2026-11-05 |
| #40 stag | A huge stag grazes between the trees; its antler lanterns glow brighter at night | SparklingForest | PR #163, 2026-11-05 |
| #39 roads | Roads turn from dirt to cobblestone at a town's edge; ruts, gaps with grass, the odd missing stone | Any HumanVillage road | PR #162, 2026-11-05 |
| #39 squares | Flagstone square with a kerb and worn paths round the town's pond or fountain | HumanVillage, elf town | PR #162, 2026-11-05 |
| Buoys | Buoys off the wharf end float clear of the stilt-house piles | Fishing village 2 | PR #162, 2026-11-05 |
| Folk heat | Desert and snow folk are out working at 8 h; at midday most stand in the shade of the eaves | Desert HumanVillage | PR #161, 2026-11-05 |
| #40 tortoise | The mesa tortoise walks a slow loop and turns to look at you | MesaCanyonlands | PR #161, 2026-11-05 |
| Gulls | Some gulls sit on the buoy masts off the wharf end | Fishing village 1 | PR #161, 2026-11-05 |
| Shadows | Far trees still look right with the new default (cheap + cached far shadows); no shadow pops while walking | Forest, fishing village 1 | PR #158, 2026-11-05 |
| #80 | Ruin brick walls: broken courses, fallen bricks, moss and sand | Any ruin | PR #158, 2026-11-05 |
| #70 | You can't run through the flat rock chunks, and you can stand on the low ones | Electric Wastelands | PR #158, 2026-11-05 |
| #8 | Folk greet, eye or avoid you depending on your race | Any village, try 2 races | PR #157, 2026-11-05 |
| #131 | Fire look (you plan to tweak it later) | Ember Forest at night | PRs #152, #153 |
| #62 | Mantrap bite still shoves you back; colossi and mantraps appear as before after world load | Dunes, Stone colossus area | PR #177, 2026-11-06 |
| #40 | New colossi: Tundra snow worm, Temperate walking oak (rope swing, owl, saplings in its footprints); mire walker fog visible by day | Tundra, Temperate, Foggy marsh | PR #180, 2026-11-06 |
| #40 | New colossi: fungal puffball (glowing spores), charred treant (ember trail), ice mammoth (sheds ice chunks) | Fungal forest, Ember forest, Glacier | PR #183, 2026-11-07 |
| #40 #69 #70 | Round dented salt crystal ball; biome hazard houses (ash, snow, storm, sand) in harsh-biome towns; magnetic drift mounds no longer hang over steep slopes | Salt flats, Ashlands OgreCamp, Tundra dwarf towns, Electric wastelands | PR #184, 2026-11-07 |
| #40 | Ice mammoth now walks Glacier slopes; new sky whale floats over the Mountains (spouts on updrafts, sings) | Glacier, Mountains | PR #185, 2026-11-07 |
| #36 | Fishing villages: stilt houses and docks on the water, boardwalk pier networks, per-village trades (crab pots, nets, drying racks), rope coils, gulls | River mouth villages | PR #186, 2026-11-07 |
| #40 | Mire walker now wades its marsh loop with splashes; new hermit crab colossus with an islet and lighthouse on its shell (lamp lit at night) | Foggy marsh, Ocean shore | PR #187, 2026-11-07 |
| #37 | Sea folk lake village: central spire, glowing rune stones, snaking kelp, sunken boats, giant clams with pearls, bubble vents; boardwalks now ramp on steep banks | Sea folk lake (dive), fishing villages | PR #188, 2026-11-07 |
| #21 #37 | No haze edge from peaks near snow; sea folk buoys, moored raft, bigger wrecks; leafier shrubs and richer flowers (daisy, buttercup, poppy) up close | Peaks in snow weather, sea folk lake, meadows | PR #189, 2026-11-07 |
| #40 #37 | Magma serpent colossus in its own crater (Ashlands); fish schools round the sea folk domes that scatter from you, sturgeon on the lake bed | Ashlands, sea folk lake | PR #190, 2026-11-07 |
- 2026-11-09: closed #60 (Decide priority table, A-82 de458d0). Retitled and updated #22 (underwater sound only), #24 (dungeon polish), #26 (item models, equipment), #27 (quest tracking, dungeon quests); #28 stays on hold; #85/#78 close when P-22 merges.
- 2026-11-09: P-22 merged (#229): roof undersides lit like shaded walls; #85 small-body cave camera closed; #78 display modes closed (already in DisplaySettings).
- 2026-11-10: R-7 merged (#233): far shrubs as octahedral impostors, shrub reach 260 -> 520 m (+0.0-0.34 ms); shrubs now visible from 300 m altitude. Atlas trim (140 -> ~39 MB) follow-up running.
- 2026-11-10: R-7 row 5 merged (#234): impostors baked only for the 60 models that can become one; atlas 140 -> 39 MB, bake 14.9 -> 2.1 ms, no look or ms change.
- 2026-11-10: R-8 merged (#235): cheaper sky. Skybox -54% at noon, -39% at night; storm clouds -7.5% (missed the 15% target, kept the clean version). No visible change.
- 2026-11-10: W-130 merged (#236): far shrub impostors drew as magenta slabs on D3D11 (shader compile error since #233); fixed, impostors now match the meshes.
- 2026-11-10: A-92 merged (#237): lying animals keep legs above ground and stand before moving (#231 closed); fur, near fur outline and smoother joints on by default, `-nofur`/`-noshells`/`-noseams` to compare (#230 closed). Check: a wolf or rabbit up close; a horse lying on a slope.
- 2026-11-10: A-93 merged (#238): folk have seeded personalities and answer who/how/work/town/news/favour; as an animal they pet, fear, shoo or marvel at you. No talk window yet (P-26, player account); `dialogue.json` `conversation` is hand-editable (see its `_readme`).
- 2026-11-10: R-11 merged (#239): far trees draw as impostors past 30 px (-0.1 to -0.26 ms, no look change). Rocks stay on meshes (impostors cost more). Longer tree reach waits for a bigger far terrain ring. Check: a forest from a hill and from 300 m, walk toward it for pops.
- 2026-11-10: A-94 merged (#240): flying birds leave one faint breath dot, no trail; the ice mammoth and snow worm breathe big slow puffs only in the cold. Check: a condor flying in Tundra snow; the mammoth up close (puff may look bright; easy to soften).
- 2026-11-10: Zach OK'd fur, near fur outline and smoother joints on by default (A-92) and the mammoth breath as is (A-94).
- 2026-11-10: W-131 merged (#242): at High the far ground reaches ~3 km (was ~2 km), so flying high shows much more land before the haze (+0.1-0.35 ms). Check: fly at 300 m on High and look at the horizon for seams or pops; switch presets a few times mid-flight.
