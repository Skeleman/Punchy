# Punchy v0.11.0 (DRAFT, not released)

Draft for Zach to review. Candidate 2: branch `cand/v0.11` 4eea658 (world-features b39522b + animal-behaviours c810406).
Gate results: GATE-v0.11-candidate2.md (first candidate 21f1193: GATE-v0.11-candidate.md).

## New since v0.10.0

### Settings
- **Graphics settings**: press **F5** for a panel with View distance, Detail and Shadows sliders and Low, Medium,
  High and Ultra presets. Your choice is saved.
- **Quit game** is in the F5 panel, with a confirm (there was no way to exit before).

### Playing as animals
- Each animal jumps its own height.
- Your own animal no longer makes random noises; it only calls when you emote.
- Jumping over a river no longer slows you down or carries you with the current; only swimming does.
- Fish can leap out of the water again.
- Birds no longer hang in mid-air when flying into a cave wall or cliff; they slide along or sink down.
- Climbers hug the trunk and lean into it.
- The bow emote no longer lifts an animal's back legs.
- Rattlesnakes have a rattle emote.
- As a ghost you can talk to other ghosts, and they don't flee from you.
- Picking up a cat or a dog: the arms now open under the animal and hold it.
- The long sprint stride no longer folds the foot back against the shin.

### Creatures and folk
- **Giant worms** leap out of the ground in one clean arc and dive back in, steer around rocks underground, and leave
  a trail of churned ground behind them.
- Swimmers leave real wakes: rings spread out in the water from where they passed, instead of a fixed shape that slid
  as they turned.
- Woodcutters, smiths and dwarf smiths put their body into the swing, with a wind-up at the top.
- Animals and folk no longer stand in place spinning (it was mice, elves at their fountains, and animals at play).
- Cold breath comes from each creature's mouth (the chicken's floated above its head).
- The wolves' night howl is softer, sounds far away, and comes once a night per pack.
- The bison's moo rises like a moo instead of groaning; frogs croak "rib-bit" and hum together at night.
- Paw prints stay on the ground and no longer land on bench seats.

### World
- **Wood looks like wood**: planks, posts and beams have clear grain and streaks (they read as leather or plaster
  before).
- Stone houses have flat walls: no more dark or bright bands down the middle at night or in the rain.
- **Waterfalls** fall faster, with spray at the lip.
- Cave streams no longer have thin lines across the flow and run cleanly into their pools.
- Fountains have a complete stone rim and real water in the basin.
- Benches and other built things (halls, fences, signposts...) no longer sway in the wind; elven benches have carved
  stone seats.
- Roads no longer run through a hermit's garden or into houses at a village's edge.
- Shooting stars stay behind the clouds; rivers are hidden by fog in front of them.
- Banners ripple slowly instead of shaking.
- Bubbling pools spread ripples like a fish surfacing.
- More fireflies higher up in the great tree.
- Fungal forest caps are browner and earthier.
- Ember pine slivers run along the needles.
- Smelter furnaces glow with embers in a stone pit.
- Roof ridges are rounded instead of sharp.
- Glaciers have ice slabs and craggy ice tors, not only arches.
- Trodden ground: the earth is worn dark in front of goblin and ogre camp stalls.
- More floating magnetic rocks drift over the electric wastelands.
- Smoother streaming on the way into caves.

## Multiplayer
- **Petting, picking up and putting down animals now work for players who joined a game.** Before, when the host's
  game was running slowly, a joined player's pick-up could arrive seconds late, together with the put-down, so the
  animal never seemed to leave the ground. Now the host sees it straight away, and everyone sees you carrying it.
- **Builds**: everyone needs v0.11.0 (the world changed, so v0.10.0 and v0.11.0 players can't join each other).
- **Tested** by hosting and joining on one PC (see the gate file).

## Download
Unzip `Punchy-v0.11.0-windows.zip` anywhere and run `Punchy.exe`. Windows 64-bit only.

---
Notes for Zach (delete before publishing):
- Default graphics preset: Medium, your call of 2026-11-01 (as v0.10 looked). On the laptop, Medium glide sits ~31 fps
  1% low; a "Balanced" in-between (view 0.8, detail 0.75, shadows 0.8) gives ~35; Low ~57.
- Known open: Z-20 carrying a large animal (most dogs are over the 0.65 m limit) or another player; Z-07
  songbird/dormouse wander spins; one cave stream ends without a pool. Z-22 miners passed by sight on seed 3 (slot 8).
- Check the F5 / Quit line: F5 isn't listed in the F1 key list yet.
