# Zach's v0.10 playtest feedback (2026-10-30)

Triage by the supervisor. IDs Z-01... Lanes: **W** world (zache-73), **A** animal (zache-db), **P** profiler (zache-11).
P1 = bugs and quick wins, do first; P2 = look and feel polish; P3 = bigger features (design note first, supervisor OK).
Each lane reports progress by ID in its REPORTS file. Anything that changes terrain, rivers, placement or a
network table changes the fingerprint: fine for v0.11, but say so in the report.

## P1: bugs and quick wins

| ID | Lane | Item |
|---|---|---|
| Z-01 | W | **No way to exit the game.** Add Quit (with confirm) to the F5 graphics panel / an Esc menu. |
| Z-02 | A+W | **Crow got stuck mid-flight flying toward a cave.** Find out if cave loading (CaveSystem underground switch, streaming, or a behaviour/LOD cull) froze player control. Reproduce with `-body Crow` flying at a cave mouth. A investigates the player/bird side, W the cave side. |
| Z-03 | A | Speed drops when *jumping over* a river; it should only change in the water (check airborne state in the river slow-down). |
| Z-04 | A | Can't jump out of water as a fish (fish leaps). |
| Z-05 | A | The player's animal makes random noises; only wild animals should. The player makes noise only through emotes. |
| Z-06 | A | Drink emote still lifts the rear legs (the v0.10 notes said it was fixed: find out why it isn't). Horses sitting looks very odd. |
| Z-07 | A | NPCs and a squirrel sit in place spinning. |
| Z-08 | A | Cold breath on the chicken comes out well above its head. Anchor breath to each creature's mouth/head socket. |
| Z-09 | W | Shooting stars render in front of clouds. |
| Z-10 | W | River renders on top of nearer fog (fog doesn't occlude the river): fog in the water shader / queue order. |
| Z-11 | W | Benches wiggle (wind/rain flutter on a rigid prop?). Bench wetness shows a screen-space image (changes with camera angle). Elven stone benches use the plank model. |
| Z-12 | W | Fountain: the lower basin rim is incomplete (floor visible from above); stretched texture at `-goto 270,3248`; its water should use the game's water shader. |
| Z-13 | W | A road runs through a cabbage patch at `-goto 147,-542`. |
| Z-14 | A | Night noise (howl or ghost?) is too in-your-face, too frequent and a bit staticky: identify it, make it rarer, softer and distant (low-pass, reverb). Traced (W) to the wolves' SoundId.Howl (CreatureAudioSystem.cs:148): animal lane. |

## P2: polish

| ID | Lane | Item |
|---|---|---|
| Z-20 | A | Held animals don't follow the arms; arms don't adjust to what's carried. |
| Z-21 | A | Long-stride run: the foot bends very steeply against the shin with the leg fully back. |
| Z-22 | A | NPC chopping/hammering: put the body into it; the axe gets a wind-up step. |
| Z-23 | A | Different jump heights per creature. |
| Z-24 | A | Rattlesnake rattle emote. |
| Z-25 | A | Ghost player: can talk to ghosts, and they don't flee. |
| Z-26 | A | Bison moo sounds like a groan: a moo rises in pitch. Frog croaks sound like burps and are staticky; add the continuous night hum. |
| Z-27 | A | Swimmer wake is a fixed shape tied to heading and slides as it turns: make it a trail left in the water (world-space ripples, as the bubble pops should use). |
| Z-30 | W | Wood: more striped and grainy (now reads as leather or painted plaster). |
| Z-31 | W | Vertical lines in the close-up detail texture on buildings. |
| Z-32 | W | Stone house lighting: at night lit edges with a dark centre band; wet daytime dark edges, bright centre (normals or the wetness term). |
| Z-33 | W | Banner wind is far too high frequency. Cloth LOD: less resolution on building cloth at distance. |
| Z-34 | W | Cave streams don't meet the pool at the end cleanly; striping across the flow. |
| Z-35 | W | Waterfall: faster fall, whitewater spray at top and bottom; it nearly meets the top (close the gap). |
| Z-36 | W | Bubble pop: use the fish-style surface ripple. |
| Z-37 | W | Great tree fireflies fly higher too (another layer or two). |
| Z-38 | W | Fungal forest cap colours converge toward brown (Morrowind feel). |
| Z-39 | W | Ember slivers in pine needles aligned vertically with the grain. |
| Z-40 | W | Smelter furnace: glowing embers, recessed like a pit. |
| Z-41 | W | Roof edges get a slight bevel instead of a hard point. |
| Z-42 | W | Glacier tops: not every formation an arch; add slabs and iceberg-like forms. |

## P3: bigger features (design note first)

| ID | Lane | Item |
|---|---|---|
| Z-50 | A | Real run gaits for quadrupeds: a gallop/bound (hind legs push, front legs catch, hind legs come forward past them, leap again) above a speed. |
| Z-51 | A | Several voices per animal (cat: meow, purr, hiss; dog: pant, bark, howl; like the chicken), with pitch and speed variation so repeats differ. |
| Z-52 | A | NPCs sometimes react to the player's race or species: friendlier to their own, sometimes annoyed by strangers. |
| Z-53 | A | Predator-prey chases are rarely seen: check they still happen (Brain.Hunter/Chaser), and tune so they're visible. |
| Z-54 | W | Couldn't find any ocotillo mimics: check spawn rules and density; list where they are. |
| Z-60 | W | Rivers: natural curves instead of straight segments; occasional width changes. |
| Z-61 | W | Poison swamp: frequent small, organically joined pools (not circles) for a marshy look. |
| Z-62 | W | Mountain ranges dividing greater biome regions; roads through them either wind up and down or tunnel through (cave rules). |
| Z-63 | W | Plant fidelity: experiments to raise plant detail to match the rest (the foggy-marsh hanging vines are the model). Shots of each experiment. |
| Z-64 | W+A | Underwater camera, visuals and sound (later; jump-to-rise is fine for now). |
