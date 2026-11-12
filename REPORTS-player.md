# Player lane reports

## 2026-11-08 batch P-21 row 3: interior lights (item 31): PARKED (verified in shots, suite not run, not pushed)
- Uncommitted (staged) in the player tree on lights/round3 (pushed head 884a4673): LightBudget.cs, LightPolicy.cs, CaveSystem.cs (Terrain/Caves), Glow.hlsl. Wrapped up early on the coordinator's word
- Fix chosen: interior lights light only interior surfaces. A lit window's light was a glow at the house's middle (reach ~9 m) in the glow slots and the far glow map's night layer, so it lit the outer walls and the ground round the house through the walls. It is now an indoor Fixture (LightBudget.AddIndoor, LightPolicy.IndoorLightsOutdoors = false): island buildings are solid shells (Interiors builds rooms off-island with their own lamp), so it takes no slot and the windows' far-map layer is off (Glow.hlsl skips that sample: one texture read less per pixel). Panes keep their own glow. Not the door-state option: it would tie a cosmetic light to networked door state, and a centre light would still leak through every wall while a door is open
- Checked: hut at (133,-545), 22:00, before/after from the same cameras: the reddish ground pool round the lit building and soft far-map pools are gone (diff sheet D:/Unity/PunchyMilestones/light/P-21/r3-wide-before-after-diff.png); inside room 2 (door 30) with the lamp lit is unchanged (real light, 1/6). Millfield: 66 -> 61 requests, 15 -> 10-11 glow slots at night; hut area 16/16 slots both (Fire requests fill them). Editor GPU timing is vsync-capped and unreliable, so "GPU no worse" rests on fewer slots and one fewer sample
- Caveat: settlement houses (Millfield) are not flora-placed, so they never had a window light (indoor count 0 there); if Zach's leak was at a settlement house it is something else and not reproduced
- Left: play-mode suite (14/14 + pins) before pushing row 3 (run stopped at wrap-up), then commit and push. merge-tree of lights/round3 with row 3 committed vs origin/sky/cost and origin/water/overhaul: both clean
- Gallery D:/Unity/PunchyMilestones/light/P-21/gallery/ (before-/after-r3-*, r3-inside*)

## 2026-11-08 batch P-21 row 2: clear-day distant fog eased (item 30): VERIFIED (modest)
- lights/round3 884a4673. The fog IS the draw-distance cover: its density is 1.98 / streamed distance (~98% at the edge). So the edge stays at the limit: on fully clear, sunny days (WeatherSystem `_PunchyHazeClear`: sun up, cover under 0.25-0.4, no overcast/rain/dust; not in cold lands via `_PunchyAurora`) the haze grows with distance cubed instead of squared, same ~98% at the edge. Mid-distance haze roughly 0.62 -> 0.40 at half the draw distance, 0.22 -> 0.06 at a quarter
- Checked at Millfield noon, Sunny day type + `-weather Clear` (cover 0.04, clear 1.0), east and north from 60 m: mid-ground band goes from grey-tan (173,153,138) to (168,139,111), mesas sharper; the horizon band is unchanged. Partly cloudy (cover 0.43, clear 0) unchanged. From 250 m the far land still fades out, no edge seen. Gallery D:/Unity/PunchyMilestones/light/P-21/gallery/ (before-/after-r2-*)
- For Zach: the far horizon itself is still the pale sky colour; going further would need a longer draw distance
- NEXT: row 3 (interior lights)

## 2026-11-08 batch P-21 row 1 (lights/sky lane): blue moon deep blue, fog and clouds lit by it (item 35): VERIFIED
- lights/round3 a9ad8fdf (from lights/manager 6f6ae2ad). Lamp (0.1,0.3,1.2) -> (0.017,0.057,1.9) and ambient to the same deep blue; on blue-moon nights the sky zenith/horizon lean to that hue (brightness kept), and Sky.hlsl adds the moon's light scattered in the low air (stronger on its side) to the horizon/haze colour and to the cloud light (new global _PunchyBlueMoonGlow). Sky.hlsl edits are in the twilight/cloud-light functions only
- Checked at Millfield, 21:00, `-bluemoondays 0 -moonphase 0.5` vs the blue moon pinned away: with it, terrain, distant fog and cloud undersides go deep blue (before: light blue land, grey fog, grey clouds); without it the night is the old grey (unchanged). Sheet D:/Unity/PunchyMilestones/light/P-21/r1-sheet.png (before, after, after without blue moon, after looking away from it); gallery D:/Unity/PunchyMilestones/light/P-21/gallery/
- Caveat: the clouds are lit along the big moon's path (no second light march), so their bright side faces the big moon
- NEXT: row 2 (clear-day fog)

## 2026-11-07 P-20 note: underwater fog on terrain and flora: done (verified), PR #191 open
- player-client cd54d1f (Sky.hlsl lent by the stand-in supervisor). PunchyAtmosphere fades to `_PunchyUnderwater` instead of the sky haze while the camera is under water: the water's colour, 0.07 per metre, exponential squared. Underwater.cs sets it with its 0.3 s blend.
- Also fixed: Underwater.cs left RenderSettings fog on with the underwater colour after surfacing. It now saves the scene's fog and puts it back.
- Checked, before/after, same spots (W-120 lake -1624,-488 on v3; sea 4600,0), sheet D:/PunchyMilestones/P-20/fog-compare.png:
  - Sea, looking down: the bed fades to dark teal instead of a clear grey floor.
  - Back above water: before, the surface stayed murky grey because the fog never switched off; after, it is clear blue.
  - Lake: the sea-folk tower a few metres below is clear and fades with distance.
- Caveat: the before lake shot doesn't show that tower at all, probably load timing, so that pair isn't a clean comparison.
- Not merged: the stand-in can't merge its own lane's PR (permission check). Needs review: PR #191 (also carries the ??= fix d1e2ee3).

## 2026-11-07 P-20 note: underwater fog on terrain and flora (W-120 shot): BLOCKED on a shared shader
- Cause: terrain and flora haze through `PunchyAtmosphere` (Shaders/Sky.hlsl), which fades to the sky's horizon colour and never reads RenderSettings fog, so the fog Underwater.cs sets does nothing to them; only my screen tint reaches them.
- Proposed fix (needs your OK, Sky.hlsl is the world lane's): an additive block at the top of `PunchyAtmosphere`. When a new global `_PunchyUnderwater` (rgb fog colour, w density; 0 when dry) is on, return `lerp(color, _PunchyUnderwater.rgb, 1 - exp(-pow(distance * _PunchyUnderwater.w, 2)))` and skip the sky haze. Underwater.cs sets and clears the global with its existing 0.3 s blend. Every shader using PunchyAtmosphere (terrain, flora, the others that include Sky.hlsl) then fogs alike under water. Check: the W-120 sea folk lake bed shot, before and after, plus a sea and a cave pool.
- Not done: no change until you OK the Sky.hlsl edit, or tell the world lane to make it and I do the Underwater.cs side

## 2026-11-06 P-20 note: main merged (Underwater fix, PR #181); the same ??= trap fixed in 5 more places
- Done (player-client d1e2ee3, compiles clean in the editor): main merged into player-client (fast-forward, f03c69a). Thanks for the fix. Searched my lane for ?? / ?. on Unity objects: 5 `x ??= Resources.Load/FindAnyObjectByType` field caches (ItemMaterial in Interiors, Items x2, Quests; FaunaDirector in QuestChecks) are now explicit `if (x == null)` checks. The FaunaDirector one could have kept a destroyed director. Not run in a build (no behaviour change when the objects are alive)

## 2026-11-06 batch P-20 GC row: Items and the player's OnGUI scripts allocate far less (profiler finding 11, A-82 notes)
- VERIFIED (player-client 354b12c). Measured in Development builds with the new `-gclog` (Unity's GC Allocated In Frame counter, 5 s windows), one run per spot: start, Millfield (`-place HumanVillage`) and a meadow, 30 s each, averaging the last 15 s. The baseline was the same tree without the GC changes (pushed P-20 plus today's main), built in the same editor session.

| Spot | Before (KB / allocs a frame) | After, run 1 | After, run 2 |
|---|---|---|---|
| Start | 13.0 / 149 | 11.4 / 101 | 3.9 / 70 |
| Millfield | 14.1 / 147 | 7.2 / 102 | 3.7 / 68 |
| Meadow | 13.4 / 152 | 7.6 / 72 | 3.8 / 72 |

- Runs vary a lot (weather, creatures), so take the allocation counts, which are steadier: about 150 down to 70-100 a frame. One baseline run only; the editor was needed next by animal.
- Items: every entity query (26 call sites across Items, Containers, Quests, QuestChecks) is made once per world and kept. The six Receive handlers are cached delegates, not lambdas and a closure every frame. The temple chest sockets are one reused list a frame, not an iterator plus two lists. Draw's sets are reused. The Use prompt string is rebuilt only when its text changes, and UseKey.Name too. Kept main's temple pick by type (cfd07d0) in ChestSockets/TryChest
- OnGUI: Underwater, Doors, Interiors, Items, NetMenu, FrameRateCap and MainMenu now draw through a GuiHook (new UI/GuiHook.cs and GuiHooks.cs). It is on only while its owner has something to draw, and useGUILayout is off where no GUILayout is used. Checked on the new build: the Keys menu, the underwater tint (sea) and "F open door" (door 90, same as the baseline) still draw
- UI/Prompts.cs: the style cache is checked first, so each style's padding (a new RectOffset per read) is read once
- Still paid: Unity's IMGUI pass runs as long as any enabled script has an OnGUI, and other lanes' scripts keep it alive: DebugControls, FolkTalkSystem, InteractionInputSystem/InteractionPrompt, TempleSockets. GuiHook is in UI/ for them if wanted. Doors.FindFacing still makes an entity query and a prompt string each frame (small; not in the profiler's list)
- Also: my long-running scripts now end by themselves (time limit and parent check), and I sweep my leftover bash and console-helper processes after each task (Zach saw many left running)
- NEXT: nothing for this row

## 2026-11-06 batch P-20: camera under all water and sliding in to look up; Keys table with pad bindings; weather key; interior culling (no measurable gain); house walls let the player walk through
- Row 1 VERIFIED (player-client 07e9fc3): under cave water (new `-place cavepool`, deepest pool, 1.1 m: "Underwater: in the water (cave water, lens -3.98, surface -3.86)", lens to -4.7 at -55°) and under sea water (`-goto 4600,0`, lens -4.6). The cave-water clamp was already gone since P-19; the stream ends are only 0.4-0.5 m deep, too shallow for the lens. Looking up: the open-air pitch limit is -65 (scene MinPitch -20), and the camera slides along the ground toward the character: arm 5.4 m at -20°, 2.7 at -45°, 2.2 at -61°, lens 0.5 m above the ground the whole way. Shots p20-cavepool-under30/-under55, p20-sea-under, p20-ground-up20/45/65
- Row 2 VERIFIED, bindings: the Keys tab lists 16 commands with keyboard and pad bindings in columns (actions read from their own bindings): Move, Look, Jump A, Sprint LS, Walk RS, Roll B, Interact Y, Use (doors, lamps, pick-up, chests, boards) RB, inventory LB, drop A/RB, close LB/B, creature actions d-pad, weather RT, menu Start, tabs LB/RB, options View. Use prompts and the inventory label switch to the pad's buttons when a pad is in use (Y sit with `-forcepad xbox`). A PlayStation pad was plugged in here, so the table names its buttons (Cross, R1); `-forcepad xbox` doesn't override a connected pad's own names (cosmetic). Shot p20-keys2-both.png. Test gotcha: `-menu` and `-perflog` are re-read on every shot line, so put them on the line itself
- Row 2 UNVERIFIED, no entry: a ledge climb or a wall climber's top-out now only lands where the body fits (no rock, no kit wall at the landing, nothing in between, a floor under it). But `-climbhouse` (push into a house wall and jump for 8 s) never once triggered a ledge climb at 5 houses, so that path is untested. What it did find: walking, not climbing, takes the player through some houses. Door 30 (Millfield, 2925,-892): the horse stands at the stone house's door, and 2 s into the push it is 14 m out the far side (shots p20-climbg30-t13/-t15). Door 64 similar (110 m); door 110 (stone tower) ends up inside the wall. Door 0 (hide lodge) and the hall behind door 30 do stop it. That isn't a timing issue (6 s wait before the push). House collision is kit boxes or prop support grids (Flora/**, read-only for me): likely those houses' walls are thinner than the grid, or the open door leaves a gap. Needs the world/flora lane; repro `-climbhouse 30 2.0`
- Row 3 VERIFIED, weather: K / RT (Keys tab "Cycle the weather") forces the next of 13 presets over the player via WeatherSystem.ForceAt, then back to clear. Local: on a joined client the host's sync wins. `-weathercycle 5` shot all 14: p20-weather-sheet.png (sandstorm haze, blizzard flakes, ash, green miasma, fog all visible)
- Row 3 VERIFIED that it culls, no measurable gain: interiors are drawn only within 40 m of a door of their house (0 of 11 drawn in Millfield; `-nointeriorcull` keeps all). Render cost in Millfield with `-perflog`, 4 views, Low, uncapped: 3.94 ms/frame culled vs 4.00 ms not, within noise. There are only 11 interior rooms in total (one per house type, 115 boxes), so there was little to save. This build gives frame time only, no draw-call counts
- Process: my editor was stopped by Claude Code's low-memory reaper three times (it runs inside the background launcher); each time I put my line back in the queue
- Merge (11-06, per the card note; player-client 10fa570, compiles clean in the editor): origin/main merged. P-20's 39 DebugControls lines moved into the partials: the `-place cavepool` flag in DebugControls.Flags.cs, PlaceInCavePool in DebugControls.Places.cs. SunkenTempleSystem.cs conflicted with W-113's temple look: I took main's version (world lane's file, its own block material), which drops P-19's vertex-colour stone pattern and flagstone floor submesh (never on main). Say if you want them back on top of W-113
- NEXT: the walk-through houses (world/flora lane); a real ledge-climb case for the landing check (a wall with a reachable top); else P-20 is done

## 2026-11-05 batch P-19 row 3: camera under water (sea and lake verified; river and cave water not reached); temple contrast; loan handed back
- Row 3 VERIFIED for the sea and a lake (player-client 2c63be3; new Camera/Underwater.cs on the camera rig): with the lens in water, a blue-green squared-exponential fog (density 0.11), a screen tint and a low-pass filter on the listener (700 Hz), blended over 0.3 s, with 4 cm of hysteresis at the waterline. Water read from TerrainSource.SampleGround (bed to surface) and cave streams/pools (ThirdPersonCamera.CaveWaterAbove), only read. The open-air camera clamp now keeps the lens above the ground only, not the water, so it can go under
  - Logs: lake, pitch -25: "Underwater: in the water (a lake or river, lens 36.70, surface 36.93); low-pass on (700 Hz)"; pitch 30: "out of the water (none, lens 43.44, surface -); low-pass off". Sea (-goto 4600,0, the default -20 pitch while swimming): "in the water (the sea, lens -2.44, surface 0.00); low-pass on (700 Hz)"
  - Shots D:/PunchyMilestones/P-19/: p19-lake-above, p19-lake-under, p19-lake-under2, p19-lake-out, p19-sea-above, p19-sea-under (surface seen from below through the fog)
  - Note: a player swimming at sea with the default -20 pitch has the lens under water at once (-2.4 m). That may want a lens kept near the surface while swimming (design call)
- Row 3 UNVERIFIED: river and cave water. My -goto/-place spots put the player on the bank (`-place crossing`: river beside the bank), on a ledge by stream 8's pool, or at `-place cavestream` with the rock probe shortening the arm to ~1 m, so the lens stayed above the surface. A river goes through the same SampleGround path as the lake (kind "a lake or river"); cave water uses the P-18 sampler that found that stream's surface. Needs a spot with the player standing in a river or pool
- Row 2 follow-up VERIFIED: per-stone shade 0.74-1.14 and seams darkened to 0.4: the wall blocks and floor flagstones now read under torchlight (p19-temple-floor.png looks up at the walls' staggered blocks, p19-temple-wall.png shows the flagstones). The torch-lit back wall at the room's end still reads nearly plain (its light washes the shade out). Landmark hash unchanged: "136 landmarks, hash 4AE65CCF"
- File loan: done with SunkenTempleSystem.cs, handed back to the world lane (my changes: materials, submesh 3 floor, vertex-colour stone; no rooms, sockets or collision touched)
- Checks: compile 0 errors; 1 build Succeeded; 4 runs all quit; editor closed (queue followed: opened 23:50 first in line, 18 GB)
- Cost: ~30 calls, 1 build
- NEXT: river and cave-pool underwater shots if wanted (a placement inside the water); otherwise P-19 is done

## 2026-11-04 batch P-19 rows 1-2 (handoff): clear quests gated by player presence; temple floors and walls
- Row 1 VERIFIED in part (player-client 1b7b595): a clear area counts only while its player is within radius + 30 m, and must hold at least one creature before it can clear. First count of 0 logs "(none seen yet: can't be cleared)" and it never finished. Host + client: host counts the client's quest ("1 Dog within 40 m") while the client walks; when the client left, the host stopped counting instead of clearing (P-17's false clear is gone). Walking chase (virtual gamepad left stick, camera-relative, no teleports): solo and client both walk right up to the dog ("walking at a Dog 0.1 m away", 25 steps on the client)
- Row 1 NOT DONE: no clear happened. The only creature near OgreCamp's board (Dog, via the build's copy of quests.json; OgreCamp keeps no chickens) is a domestic dog, which doesn't flee a player even in solo, local or remote. For the animal lane: which species flee a walking player (local and remote)? A clear quest needs one of them near a board
- Row 2 VERIFIED (SunkenTempleSystem, lent): floors on a new submesh 3 with a grey-brown flagstone material, walls in sandstone; vertex-colour stone on a world grid (0.7 m flagstones, 0.45 m courses of staggered 0.9 m blocks), darkened within ~0.4 m of wall/floor seams. Landmark hash unchanged: "136 landmarks, hash 4AE65CCF" (same as the P-18 logs). Shots D:/PunchyMilestones/P-19/temple.png (floor | wall). Walls read too faint under torchlight: stronger block contrast comes with the row 3 build. Seen: rain falls inside the underground temple (weather particles, world lane)
- File loan: still using SunkenTempleSystem.cs for the contrast tweak; I'll say when I'm done with it
- NEXT: row 3 (underwater): the open-air camera clamp keeps the lens above water (it adds the water surface to the ground clamp), so nothing went under in the first run; removing water from that clamp is in the rebuild (queued)

## 2026-11-04 batch P-18: character tab focus, camera above cave water, temple yaw free; row 4 needs the world lane's file
- Row 1 VERIFIED (player-client 079be61): the Character tab opens with the pad focus on the body being played. Rabbit: "CharacterTab: opened on Rabbit (stop 14)"; shot D:/PunchyMilestones/P-18/p18-character.png. (CharacterTab.cs is the animal lane's A-46 file in Net/; the card gave the row to me)
- Row 2 VERIFIED: no underwater effect exists anywhere yet (#22), so the camera keeps above cave water: underground, a lens under a cave stream's surface (segments: each point's level and width) or a water pool's is lifted 0.15 m above it and re-aimed. At the nearest cave stream, pitch -60: "Camera: kept above cave water (surface 39.73, lens 39.63 -> 39.88)". Shots p18-stream-down.png, p18-stream-down2.png. Surface rivers and lakes were already clamped (open-air path)
- Row 3 VERIFIED, no fix needed on this build: nothing outside the camera sets its yaw every frame, and in temple room 9 with `-spin 30` the logged yaw turned freely, "yaw 131 (view 131)" -> 191 -> 251 -> 311 in 2 s steps, lens in the room's air throughout. Most likely the P-9 roof fix (a4994d1): before it the lens could sit in the roof's solid, so the probe returned its 0.2 m minimum and every yaw looked the same from inside the head. Shots p18-temple-a.png, p18-temple-b.png
- Row 4 NOT DONE: the dungeon set pieces are the sunken temple's rooms, drawn by SunkenTempleSystem (Scripts/Terrain/Landmarks, the world lane's; read-only for this lane on every earlier card). What it needs: two materials (floor: worn flagstones; walls: sandstone blocks) chosen per RuinBox face by its normal, a texture each (triplanar or UVs from the box's u/v), and a vertex-colour darkening at wall-floor seams (darken vertices within ~0.4 m of a floor where a wall meets it, multiplied in the shader). I can do it if the file is lent to me
- #99: job 23e confirms main 2dae74b clean on the laptop (30 re-hosts, 0 crashes)
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: the clamp's log line, one-fix allowance); 4 runs all quit; editor closed (queue followed: opened at 18:45 first in line, 9 GB)
- Cost: ~28 calls, 2 builds
- NEXT: row 4 if the world lane lends SunkenTempleSystem

## 2026-11-04 batch P-17: host-checked hand-ins; "clear an area" quests (solo done, multiplayer chase not proven); lead-quest ask
- DONE (player-client 1eda8de; Items/QuestChecks.cs, hooks in Items.cs / Containers.cs / Quests.cs, quests.json)
- #26 #27 row 1: the host keeps a tally per player (NetworkId) of what it carries: + pickups and container takes, - drops and container puts (+ back if the container was full). A client's hand-in is checked against it. Real: host `-spawnstones` (3 Stick by board 0, for "sticks"), client picks them up, takes and hands in: host "player 2's hand-in of sticks checked: carried 3 Stick, 3 handed in", "finished sticks", "reward ... Coin x6". Fake (client `-fakehandin`, no items): host "refused player 2's hand-in of sticks at board 0: it claims 3 Stick, the host saw it carry 0"; client "hand-in refused by the host". (The host's own player is trusted: its inventory is the host's)
- #27 row 2: objective "clear": quest fields `creature` (species name) and `radius` (m), text slot {creature}; the host counts that species within the radius of the board for each player who took it, and finishes it once none have been inside for 10 s. quests.json +3: crows (Crow, 30 m), chickens (Chicken, 20 m), pigeons (Pigeon, 25 m); the _readme lists the type and slots. Adding them changes which quests boards offer (offers are picked modulo the quest count)
  - Solo, start to finish (build copy of quests.json with the chickens quest pointed at Dog 40 m, since OgreCamp keeps no chickens): "took \"The Dogs of OgreCamp are all over the square...\"", "1 Dog within 40 m", `-chase` -> "0 Dog within 40 m", "cleared: no Dog inside for 10 s", "reward: Berry x6"
  - Host + client: the host counted (1 Dog), but the client's `-chase` (stepping its own player up to the dog every 2 s) didn't move it: it stayed 1 until the client left, then 0 (likely the despawn with no player near, which shouldn't count). NOT proven in multiplayer. Question for the animal lane: do creatures flee a remote (client) player's position on the host? If yes, the test's teleport-chase may be what fails (a walking chase would)
  - Fauna spawn round players only, so a clear area far from every player reads 0 (and would finish at once): next step, count only while a player is within the radius + 30 m, or require a non-zero count first
  - Shots: D:/PunchyMilestones/P-17/p17-solo-log.png and p17-clear-log.png (the clear quest in the quest log)
- Row 3, ask for the animal lane ("lead an NPC or animal to a place"): (1) a follow activity: a creature or folk that follows a given player (keep 2-4 m behind, wait when they stop, give up past ~40 m or after N s), started and stopped from outside the brain; (2) a hook to start and stop it, e.g. `Punchy.Fauna.Follow.Start(Entity creature, int playerNetworkId)` / `Stop(...)` on the host, and a way to find a candidate near a place (species + radius -> entity); (3) a read-back: is it following, where is it (I can read CreatureState.Position). With those, a "lead" quest is: the host starts the follow on take, counts it done when the creature is within the target radius, and stops the follow
- Network table change: none (the new checks use P-15's RPCs)
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: first count logged + -clearquest picks the fullest area, one-fix allowance); 8 runs all quit; editor closed; the build's quests.json restored after the tests
- Cost: ~42 calls, 2 builds

## 2026-11-04 batch P-16: quest UI sized, interiors keep their own light, rabbit camera clean, pad inventory input works (item move not shown)
- DONE (player-client 941e41b, on main 21aa5ab)
- #27 row 1: prompts wrap at 60% of the screen and grow to fit; the quest log sits on the left, each row as tall as its wrapped text, scrolling past 75% of the screen. Shots: p16-board-prompt.png (the longest quest: the stones text lengthened to ~200 characters in the build's copy of quests.json for the test, 4 wrapped lines in the box), p16-quest-log.png (4 quests, `-takequests 4`)
- #25 row 2: inside a room the ambient blends over 0.6 s to the room's own dim warm colour (0.22, 0.16, 0.10 with a hearth; +0.10 with the lamp lit), blended back to the sky's at the door (a LateUpdate after WeatherSystem's per-frame Trilight). Hall room at noon and midnight look alike (p16-in-12.png, p16-in-0.png); outside follows the sky (p16-out-12.png bright, p16-out-0.png night blue)
- #85 row 3, rabbit: original ruins (temple room 9): looking up: no clipping, rabbit in frame by the pillars; looking down (60): no clipping, camera stays in the room ("in temple air True"). Hut cave: looking up: no clipping, whole rabbit in frame; looking down (60): no clipping, lens in cave air (cave -2.1), rabbit small below. Shots p16-ruin-rabbit.png, p16-cave-rabbit.png (up | down). No fix needed
- #26 row 4: the virtual pad now drives the real input: it's enabled on add and each queued state is processed at once (InputSystem.Update); LB / B are read from every pad, not only Gamepad.current (the virtual pad wasn't current on its first press). Pad-only log: "presses LeftShoulder" -> "inventory open" -> d-pad right / left -> "inventory slot 0 (Berry x3) ... slot 11 ... back to slot 5" -> "presses A" -> "presses Circle" -> "inventory closed". Shot p16-pad.png (pad focus on a slot). NOT shown: moving an item: each test press is held 1 s, so MenuNav's auto-repeat carried the focus 5-6 slots per press and A landed on an empty slot. A pad press goes through the same Submit path that drops with the keyboard; short taps in the test would show it (next build)
- Seen, not explained: two large white glare blobs over the village in p16-pad.png (door 100, noon) that P-11/P-14 shots of the same spot didn't have; worth a look by whoever owns lights / bloom
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: quest log placement + any-pad LB/B, one-fix allowance); 9 runs all quit; editor closed
- Cost: ~35 calls, 2 builds

## 2026-11-04 batch P-15: quests, first slice: hand-edited archive, notice boards, host-owned progress (#27)
- DONE (player-client 77887d3; Items/Quests.cs, StreamingAssets/quests.json, LF)
- Row 1, quest file format (StreamingAssets/quests.json, outside the build, the dialogue.json way): `{ "_readme": [...], "quests": [ { "id", "type": "fetch"|"find", "item", "count", "text", "reward", "rewardCount" } ] }`; text slots {item} {count} {town} {place} (= "the notice board in {town}"); item names from the item table. Re-read a second after it changes, or on F10. 5 quests to start (sticks, berries, stones fetch; lost-key, shell find). Reload test: the build's copy edited 25 s in -> "Quests: 5 quests loaded (file changed): sticks [fetch] \"EDITED: the smith in {town} wants...\"", and the quest taken at 30 s showed "EDITED: the wall in OgreCamp fell down: bring 4 stones to the notice board in OgreCamp."
- Row 2: a notice board (posts, board, two notes) beside every settlement's centre, 1-2 quests picked by the town's index: "6 notice boards: OgreCamp (2), HumanVillage (1), GoblinCamp (2), DwarfHold (1), ElfGrove (2), DwarfMiningCamp (1)". Use takes the next quest; Use again with what it asks for hands it in. Quest log: a pane under the inventory. Shot: D:/PunchyMilestones/P-15/board-log.png (board by OgreCamp's fire, "take: ..." prompt, the log). Nits: long quest text overflows the prompt box, and the log box is too short for wrapped lines
- Row 3: host-owned progress per player (TakeQuestRpc, TurnInRpc -> QuestStateRpc); find quests: the host hides the item within 40 m of the board (Items.Spawn); reward paid by the board (Items.Spawn). Client start to finish: "took \"A wall in OgreCamp needs mending: bring 4 stones...\"" -> host "player 2 took stones at board 0 (host, for a client)" -> client "board 0 slot 0 taken (from the host)" -> client "handed in 4 Stone" -> host "player 2 finished stones", "reward for stones: Coin x8 by the board" -> client "item 2000004 (Coin x8) appeared (from the host)". (-giveitem Stone:4 gave the client the stones: test only)
- Trust note: the host takes a client's word that it handed the items in (inventories are each player's own); a host-side inventory would be needed to check
- Network table change: 3 new RPC types (TakeQuestRpc, TurnInRpc, QuestStateRpc)
- For the animal lane: when folk give quests, they can reuse the archive (QuestDef) and Items' TakeQuest/TurnIn path; the board is just the giver for now
- Checks: compile 0 errors; 1 build Succeeded 0 errors; 3 runs all quit; editor closed
- Cost: ~20 calls, 1 build

## 2026-11-04 batch P-14: object inventories: containers (chests, room storage), host-owned; drops kept and capped (#26)
- DONE (player-client 8413361, on main da2e0c0; Items/Containers.cs, Items.cs now partial, Interiors.cs storage)
- Row 1: 8-slot containers. Every machine fills one from its seed when registered; the host owns changes (PutRpc / TakeRpc client -> host, ContainerSlotRpc host -> all; late joiners get every changed container). The inventory screen shows the open container's pane beside the player's (MenuNav stops 16-23): click yours = put one in, click its = take the stack. A temple chest, once opened, keeps its seeded contents inside (no more items on the ground). Host/client, chest 17 (seeded Coin x7): client "put Mushroom into container 17" -> host "container 17 slot 1: Mushroom x1 (host, for a client)" -> client "slot 1: Mushroom x1 (from the host)"; host `-takeitem` "container 17 slot 1: empty (host)", "picked up Mushroom" -> client "slot 1: empty (from the host)"
- Row 2: storage per room, seeded: 0-2 of shelf / chest against the side walls by the door, solid (InteriorCells), each a container with 0-3 starter items, now and then a surprise (a key, or a purse of 25 coins). Log: Hall: shelf 100000 Shell x2, shelf 100001 Mushroom x2 Coin x2 Fish x1; StoneHouse: none; Hut: chest 100008 Mushroom x2 Coin x7 Fish x1; MudHut: none; StoneHall: shelf 100016 Coin x2; ElfSpire: chest 100020 Stick x2 Coin x25 (surprise), shelf 100021 empty; ElfTower: none. Shot: D:/PunchyMilestones/P-14/room-shelf.png (Hall, storage by the walls; framing is close)
- Row 3: drops already stayed for everyone and for late joiners (P-11's `added` list); now at most 256 are kept, the oldest cleared for everyone (ItemGoneRpc). Host dropped item 2000004 (Mushroom) at (0.0, 162.6, 1.0) 31 s in; the client joined 9 s later: "item 2000004 (Mushroom x1) appeared (from the host)" (same uid, the host's position)
- Seen: the A-50 spawner now calls Items.Spawn (dungeon items 2000000-2000003 on the host, sent to the client)
- Network table change: 3 new RPC types (PutRpc, TakeRpc, ContainerSlotRpc)
- Also seen: a Punchy.exe from D:/Unity/Punchy/Builds/Dev (the supervisor tree's perf run) was running; not mine, left alone
- Checks: compile 0 errors; 1 build Succeeded 0 errors; 4 runs all quit; editor closed
- Cost: ~22 calls, 1 build

## 2026-11-04 batch P-13: house interiors: 7 seeded rooms, 98 doors lead inside, door teleport, host-owned lamps (#25)
- DONE (player-client f4e050a, Interact/Interiors.cs; Doors.cs and ThirdPersonCamera.cs hooks), on main f97bfc8 with the W-83 hook
- Row 1: one room per house type with an inside (FloraGenerator.HouseInterior), built off the island (x = WorldSize/2 + 500 + 40 per room, floor at sea level + 40) as InteriorCells boxes, drawn as boxes (URP ItemMaterial). Seeded from the world seed and the type: 85-100% of the house's inside, 1-3 of table / bed / hearth against the walls, a lamp and a switch. Log: "7 rooms, 69 boxes; 130 doors, 129 matched to a house, 98 lead inside": Hall 13.3x3.4x7.5 (5710B3F3: bed, hearth, table), StoneHouse 5.1x3.4x4.9 (53EC41A4: bed), Hut 5.1x3.2x4.7 (50C7CF55: table), MudHut 4.4x2.7x4.5 (4DA35D06: bed), StoneHall 11.8x3.4x6.6 (4A7EEAB7: hearth), ElfSpire 4.7x3.4x4.7 (475A7868: all three), ElfTower 5.8x3.4x6.4 (44360619: all three). Same on every machine, nothing sent. Doors are matched to their plot by geometry (on the line out from the plot centre: AnchorBaking puts a house door at its form's DoorFront x BuildingScale)
- Row 2: Use on an open door that leads inside: "F  go inside" -> into its type's room; by the room's door "F  go outside" -> back in front of that house. Each player moves their own body. Host and client (first build, door 11 -> Hall): host "went into room 0 (Hall) by door 11 (host)" ... "came out of room 0 at door 11 (host)"; client "went into room 0 (Hall) by door 11 (client)" ... "came out ... (client)". Solo (final build): door 100 -> room 1 StoneHouse, in and out
- Row 3: lamp per room, Use by its switch, host-owned: host "room 0's lamp on (host)", client "room 0's lamp on (from the host)". Shots: D:/PunchyMilestones/P-13/room.png (StoneHouse, lamp off then on: cool vs warm, bed, "F  go outside")
- Camera: a room counts as underground (InteriorCells.Distance < 6 m), so its probe (SolidDistance includes the boxes) keeps the lens inside: "underground True", arm 0.8 m in a 5 m room
- Network table change: 2 new RPC types (UseLampRpc, LampStateRpc)
- Hook: no change needed. Note for the world lane: rooms are lit by my lamp plus the sky's ambient (they sit in open air past the island, so daylight ambient reaches inside); a per-room ambient override would make "dark" darker
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: door-to-plot matching, 6 -> 129 doors, one-fix allowance); 5 runs all quit; editor closed
- Cost: ~45 calls, 2 builds

## 2026-11-04 batch P-12 row 0: #99 two-frame gap between old and new worlds
- DONE (player-client 46abd95): Host / PlaySolo / Join / HostRelay / JoinRelay dispose the old worlds (P-10's order: jobs first, scene off, dispose) at once, then make the new worlds and reload the scene 2 frames later from a runner kept across scene loads; a second switch during the gap is ignored. `-switchgap N` sets the gap, `-switchgap 0` is the old same-frame switch (for an A/B in one build). Each delayed make logs "GameBootstrap: making the new worlds (2 frames after the old ones went)"
- Build type, correcting job 23c's guess: every desktop build of mine (P-8, P-10 0a, this one) is BuildOptions.None, a release build like BProfileTools.Build (checked: dev=False). So the desktop/laptop difference is the machine, not Dev vs release
- Desktop, release build, `-island v2 -fpscap 0 -netcycle 3`, default graphics, NetCycle (copied in for the build only), stop at the first crash: 5 runs, 15 re-hosts, 30 switches all through the gap, 0 crashes, 0 exceptions
- Ask (profiler): q23 on player-client 46abd95, default vs `-switchgap 0`, v2, stop each at its first crash. If the gap still crashes there, next is `-switchgap 10` (longer), then checking which Burst statics the new worlds' systems initialise (a SharedStatic set by a script the teardown switched off)
- Cost: ~12 calls, 1 build (row 0's own)

## 2026-11-04 batch P-12: chests with lids, MenuNav inventory (pad input unverified), host/solo labels
- Row 0 (#99 fallback): not run: no q23 result on 38a8f4c in BENCH-REPORTS yet (only 23b on main 0ec7a8d). Still waiting on the laptop
- DONE #24 row 1 (player-client 05baa8a): a chest (brown box, metal band, lid on a back hinge, brass latch) at every temple Chest socket, URP ItemMaterial. The lid opens to 105 deg once the chest is in the host-owned openedChests (ChestOpenedRpc, P-11) and stays open. The A-50 "Dungeon ..." placeholder on a chest socket is hidden at runtime (renderer off; their file untouched). Shots: D:/PunchyMilestones/P-12/chests.png (chest 17 closed, then open with Coin x10 in front: "chest 17 opened (seed C98F19AB): Coin x10")
- PARTLY #26 row 2: the panel now draws with GUILayout + MenuNav (arrows / d-pad / stick move, Submit or Use drops; LB opens, B closes on a pad); shot inv-panel.png (Berry x3, Fish, Shell, Mushroom). NOT verified with a pad: `-invpad` adds a virtual Gamepad and queues LB, d-pad right x3, A, B through the Input System (backgroundBehavior IgnoreFocus too), and the log shows each press queued, but the panel never logs "inventory open", so the virtual pad's input isn't read; cause not found. Needs a real pad, or a closer look at virtual devices in a player
- DONE row 3: solo's local world is NetWorlds.Server too, so "hosting" now means IsServer(). Before (P-10 solo): "Doors: door 3 open (host)". After (P-12 solo): "Items: item 405 (Berry) taken" (no host tag); the host still logs "(host)". Doors use the same check (no solo door run this time)
- For the animal lane: please skip chest sockets in DungeonSpawnSystem.MakeProp now that a chest is drawn there (I only hide your primitive's renderer), and give your placeholders a URP material (the magenta)
- Checks: 2 compile errors fixed (GamepadButton using; tuple names in a ternary; each one left the editor at Safe Mode, force-closed: my own editor), 2 builds Succeeded 0 errors (2nd = the IgnoreFocus try, one-fix allowance), 6 runs all quit, editor closed
- Cost: ~42 calls, 2 builds

## 2026-11-04 batch P-10 row 0a: #99 teardown reordered; the crash doesn't reproduce on the desktop
- DONE (player-client 38a8f4c): GameBootstrap.DisposeGameWorlds now (1) completes every doomed world's tracked jobs, (2) switches the scene's roots off (scripts finish their own jobs and free their containers in OnDisable), (3) completes again and disposes. P-8 did (2) first, freeing flora/cave/fauna containers while the worlds' jobs could still read them: the likely reason job 23 crashed sooner. `-hostorder old` keeps P-8's order in the same build for an A/B; each switch logs "GameBootstrap: N worlds disposed (jobs first | P-8 order)"
- Build type: BProfileTools.Build is BuildOptions.None, the same as my Dev builds (plus BenchmarkRoute.cs, PunchyBench identity, batchmode). My P-8 runs differed in conditions, not build type: -gfx Low and the 60 fps cap. This time I matched job 23: `-island v2 -fpscap 0 -netcycle 3`, default graphics, NetCycle copied in for the build only
- Desktop results, one build: P-8 order 3 runs = 9 re-hosts, 0 crashes; new order 5 runs = 15 re-hosts, 0 crashes, all "NETCYCLE done" rc 0. So neither order crashes on the desktop: NOT confirmed fixed where it fails (the laptop: _avx2 Burst code, fewer cores)
- Cause, best reading: lib_burst_generated in a player has few named symbols, so "burst.initialize.statics" is likely just the nearest name to a crashing Burst job, not Burst's static init itself; a job reading freed memory fits both the original crash (scripts' jobs reading freed world blobs) and job 23's (worlds' jobs reading freed script containers)
- Ask (profiler): q23 on player-client 38a8f4c with NetCycle, `-hostorder old` vs default, v2, 15 re-hosts each. If the new order still crashes on the laptop, next step is a one-frame gap (dispose, wait a frame, create) in Host/PlaySolo
- Cost: ~16 calls, 1 build

## 2026-11-04 batch P-11: items, host-decided pickup, inventory, seeded temple chests (#26, #24)
- DONE (player-client 6621249, Items/Items.cs + Items/Resources/ItemMaterial.mat; Doors.cs: Use-key arbitration)
- #26 row 1: "Items: table of 8: 0 Stick (stack 20, 0.3 kg), 1 Stone (20, 1 kg), 2 Berry (30, 0.02), 3 Mushroom (20, 0.05), 4 Shell (20, 0.05), 5 Fish (5, 0.8), 6 Coin (99, 0.01), 7 Key (5, 0.05)". World: 260 starter items (2 seeded per house door, same on every machine, not sent)
- #26 row 2: Use key, nearer of door/item wins; host decides. Host `-pickup 1`: host "item 405 (Berry) taken (host)", client "item 405 gone (from the host)". Client `-pickup 5`: "asked the host for item 400" -> host "item 400 (Fish) taken (host)" -> client "item 400 gone", "picked up Fish" (x5 items)
- #26 row 3: 16-slot session inventory (Tab / I; click or arrows + Use drops one). Client `-drop`: client "dropped Fish", host "item 2000000 (Fish x1) on the ground", client "item 2000000 (Fish x1) appeared (from the host)". Shot inv-client.png: Shell x2, Mushroom x1, Coin x1 + "F  pick up fish" over the dropped fish
- #24 row 4: temple chest sockets open with Use; host lays out 1-3 items from the socket's seed. Host `-openchest`: "chest 17 opened (seed C98F19AB): Coin x10"; client "item 1000068 (Coin x10) appeared (from the host)", "chest 17 opened (from the host)". Solo gives the same chest 17 / C98F19AB / Coin x10
- NOT DONE: a clear open-chest shot: no chest model of mine (no lid to open; the A-50 stand-in at the socket draws magenta), and the coins sat behind a pillar in chest-open.png. A joined client's `-place temple` is overridden by the near-host spawn
- For the animal lane: (1) dungeon item spawns: call `Punchy.Items.Items.Spawn(byte item, int count, float3 pos)` on the host or solo (ignored on a client); it returns the uid and sends the item to everyone; item ids are ItemTable indices (append-only). (2) The magenta boxes: GameObject.CreatePrimitive's default material has no URP shader in a player build; give them a material asset (I used one made from GraphicsSettings.currentRenderPipeline.defaultMaterial, loaded from Resources, colour via MaterialPropertyBlock _BaseColor)
- Network table change: 7 new RPC types (PickupRpc, OpenChestRpc, DropRpc, ItemGoneRpc, ItemSpawnRpc, GrantRpc, ChestOpenedRpc)
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: magenta items -> ItemMaterial, one-fix allowance); 5 player runs all quit; editor closed
- Shots: D:/PunchyMilestones/P-11/inv-client.png, chest-open.png
- Cost: ~27 calls, 2 builds
- NEXT: a chest model with a lid (or ask the animal lane's stand-in for an "opened" state); inventory on a pad (MenuNav)

## 2026-11-04 batch P-10: Use key + host-owned doors done; interiors blocked on collision
- DONE row 0: PlayerOptions master comment (ea26b30)
- DONE #25 row 1 (717e007): a separate Use key (F, cycle F/G/R/T/V in a new Keys tab; RB on a pad) because Interact (E) is the animal lane's pet/pick up/sit and would sit the player at a door. Prompt "F  open door" / "F  close door" facing a Door anchor within 2.5 m (door-prompt.png)
- DONE #25 row 2: 130 house doors (Door anchors, index = door id, same on every machine). Host owns the open set: UseDoorRpc client -> host, DoorStateRpc host -> all, late joiners get every open door; applied through FloraDoors every frame. Host `-usedoor 3`: host "door 3 open (host)", client "door 3 open (from the host)". Client `-usedoor 5`: client "asked the host to use door 5", host "door 5 open (host, for a client)", client "door 5 open (from the host)". Shots: doors2.png (door 60 thatched hut and door 100 stone house, shut then open: leaf swung, dark doorway). Door 3 is a tepee: no visible leaf
- NOT DONE #25 row 3 (interiors): houses aren't hollow (FloraGenerator draws a dark doorway panel behind the leaf), and an interior cell off-island can't be walked: the player only stands where PlayerMovementSystem knows ground (terrain, cave field, TempleAir, flora kit boxes in SupportGrids). What the world lane would need: (a) a hook in SolidDistance/FloorUnder for "interior cell" volumes (a list of boxes: floor, walls, ceiling, in a blob or a static Burst-readable array) that a player-lane InteriorCells system could fill, or (b) let runtime code add KitBox colliders to the flora support grid; plus, per house type, the door's local position and an inside size (the kit already has HumanDoor and the wall sizes). With (a) I can build seeded rooms off-island, a door teleport in/out and a switchable light in one card
- Known nit: solo logs "(host)" instead of "(solo)" (NetWorlds.Server non-null in solo?); cosmetic
- Network table change: new RPC types (UseDoorRpc, DoorStateRpc): builds before 717e007 can't join builds after it
- Checks: compile 0 errors; 1 build Succeeded 0 errors; 5 player runs all quit; editor closed
- Cost: ~37 calls, 1 build
- NEXT: P-11 (items) uses the same Use key and RPC pattern

## 2026-11-04 batch P-9: temple chests 13, small body fully in frame, temple sky = camera in the roof (fixed)
- DONE (all on player-client a4994d1, after merging main 1d8213e)
- #24 chests 8 -> 13: closed halls 6 m+ keep a chest at the end farthest from their doorways, big rooms (40 m2+) a second corner chest. Totals 73: creature 15, chest 13, wall decor 15, interactable 15, clutter 15. Hash 9C11B352 (P-7) -> 0EBAB9D3, same in 4 runs. Socket format unchanged; the A-50 spawner logs "dungeon 78 type 1: 73 sockets ... same"
- #85 small body: tilt capped by the angle the body spans from the lens. Rabbit at -50 in the hut cave: whole body in frame, ears to feet (a2-cave-up-rabbit.png); in that tight spot the arm is 0.68 m, so it keeps the body rather than tilting (looks up only ~10 deg). Human unchanged (b2-cave-up-human.png)
- #85 P-7 b1 sky: the camera, not an open room. Room 9 touches 12 rooms, none open to the sky. -camlog before: lens at the ceiling, "in temple air False, ground +0.5 m vs lens" (in the roof). Cause: in temple rooms SolidDistance is a flat 0.5 m, so the probe only hits once its centre is inside the solid, then backed off 5 cm. Fix: back off by the whole lens then. After: "in temple air True, ground +0.6 m", ceiling drawn, no sky (b1-temple-human.png)
- For the animal lane: bright magenta boxes on the temple walls in both temple shots (missing material): likely the A-50 stand-ins (Fauna/Dungeon/DungeonStandIn)
- Added: `-camlog` (camera spot every 2 s); `-place temple` logs the room's neighbours and open-to-sky ones
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd = the roof fix, one-fix allowance); 4 runs all finished and quit; editor closed. Two Punchy.exe (0.2 MB each, path unreadable: another account's) were listed; not mine, left alone
- Shots: D:/PunchyMilestones/P-9/sheet2.png (rabbit cave up, human cave up, human temple with sockets, rabbit temple)
- Cost: ~27 calls, 2 builds
- NEXT: chests to 15 if wanted (2 more candidates); rabbit look-up in tight caves could raise the pivot while looking up

## 2026-11-04 batch P-8: host-twice crash fixed (#99); Sound and Look settings
- DONE #99 (player-client 84208ff): root cause: GameBootstrap.DisposeGameWorlds freed the worlds (and the subscene's terrain blobs) while scene MonoBehaviours (FloraSystem, CaveSystem/SurfaceGlow, FaunaDirector, AmbienceSystem, WeatherField) still had multi-frame jobs reading them; they complete those only in OnDisable, which came with the scene reload after the dispose. Fix: switch the old scene's roots off first (OnDisable completes the jobs), CompleteAllTrackedJobs per world, then dispose. NetCycle (profiler helper copied in for the build only, not committed) `-island v2 -netcycle 3`: 5 runs, 15 re-hosts, 0 crashes, 0 exceptions, all "NETCYCLE done" rc 0; Total Used flat (solo ~460 MB, hosting ~516 MB). No unfixed baseline on this PC (laptop: 2 of 3 crashed)
- DONE rows 1-3 (f332f30): Sound tab (Master, Music and ambience, Effects) and Look tab (mouse 0.03-0.5, pad 60-400, camera distance 3-20 m, FOV 50-100, Invert Y, Reset), MenuNav sliders/buttons, saved Punchy.Audio.* / Punchy.Look.*, applied at start and after each scene load; `-settings master=0,music=0.4,effects=0.7,mouse=0.25,pad=240,inverty=1,distance=5,fov=75` -> "Settings: master 0.00 music 0.40 effects 0.70; mouse 0.25 pad 240 invertY on distance 5.0 fov 75 (from the command line, saved)"; relaunch without it logs the same values
- HOOK (not my files): master 0 silences GameAudio's voices (they multiply by GameAudio.Master) but not the weather's own AudioSource (WeatherSystem) nor AudioListener.volume (GameAudio sets it to the focus level). One line for the audio owner: AudioListener.volume = focusLevel * Master, then drop Master from the voices. Music: no music exists yet; the slider drives the World and Weather groups
- Learned: since A-46, `-menu Tab` is a shot-list LINE option; a line without it closes the menu, so `-menu` on the launch line no longer shows in shots
- Checks: compile 0 errors; 2 builds Succeeded 0 errors; 10 player runs, all quit, no Punchy.exe left; PlayerPrefs exported before and restored after (no Punchy.Audio/Look keys left); editor closed
- Shots: D:/PunchyMilestones/P-8/tabs.png (Look and Sound tabs with the saved values)
- Cost: ~42 calls, 2 builds
- NEXT: audio owner adds the master hook; P-7 leftovers (chests to ~15, full rabbit body looking up, b1 sky check)

## 2026-11-04 batch P-7: temple placement fixed, small-body camera, 68 temple sockets (#85, #24)
- DONE: merged main 9d34275 (fast-forward), pushed
- DONE #85 row 1: `-place temple[:N]` uses the baked RuinRooms in the temple frame: "in sunken temple room 9/47 (16 x 16 m, ceiling 4.0 m, kind 0) at (3518.9, 19.0, 360.4); the ground is 4.5 m above"
- DONE #85 row 2 (after only): rabbit in that room, camera inside, walls and pillars, no stone see-through (a2-temple-rabbit-side.png). No "before": the pre-P-5 build is gone (overwritten in P-5)
- PARTLY #85 row 3: pivot = 1.6 m x PlayerBody.Size (rabbit 0.25 -> 0.4 m); small bodies (size < 0.75) looking up underground orbit no lower than -10 deg and tilt the view the rest. Rabbit at -50 in the hut cave: head/ears at the frame's foot only (a3-cave-up-rabbit.png); human unchanged, body in frame (b2-cave-up-human.png)
- DONE #24 rows 4-5: TempleSocketSystem -> entity "Dungeon (sunken temple)" with DungeonInfo + DungeonSocket buffer: 68 sockets from 47 rooms: creature 15, chest 8, wall decor 15, interactable 15, clutter 15; hash 9C11B352 in 2 runs (rabbit / human). Rooms classed by shape (hall, dead end, doorway, pedestal, dark); chests = dead-end far walls + far corners of closed rooms. Debug draw: F9 / `-sockets` (b1-temple-sockets.png). Biome = 0 for now. SHA posted in COORDINATION.md (be6d005)
- CHECK NEEDED: in b1 (human, temple room 9) the camera shows sky over the walls: either an open neighbouring room or the camera leaving the room; not resolved
- Checks: 1 compile error fixed (ref param in a local function); 3 builds Succeeded 0 errors (2nd: chest fallback + small-body tilt, under the one-fix allowance; 1st open hit Safe Mode, editor force-closed, it was mine); 4 runs all quit, no Punchy.exe left; editor closed. Went ahead of R-12 in the queue with Zach's OK (23 GB free)
- Shots: D:/PunchyMilestones/P-7/sheet2.png, a2-temple-rabbit-side.png, b1-temple-sockets.png, a3-cave-up-rabbit.png, b2-cave-up-human.png
- Cost: ~46 calls, 3 builds
- NEXT: chest count to ~15 (more closed rooms or Large rooms 2 chests); full rabbit body in frame looking up (raise the pivot only while looking up); check the b1 sky; Biome from the temple's biome

## 2026-11-03 batch P-6: DungeonSocket contract pushed; set pieces BLOCKED on underground collision
- DONE: #24 row 1 `DungeonSocket` exactly per the brief (IBufferElementData: Pos, Rot, SocketKind Kind, byte Size, uint Tags, uint Seed, ushort DungeonType, byte Biome) + SocketKind / SocketSize / SocketTags (NearWater, NearChest, NearAltar, Dark, Ledge, Doorway, Ceiling; append-only) + MakeSeed(world, dungeon, index) (player-client d7ed5eb). SHA posted in COORDINATION.md log. Folder is Assets/Game/Scripts/Dungeon/ (repo convention), not Assets/Scripts/Dungeon/ as the card wrote
- NOT DONE rows 2-3, blocked (decision needed, not mine to make):
  - Walkable space under the heightmap exists only where PlayerMovementSystem.SolidDistance knows it: the cave field and sunken temple rooms (TerrainSource.TempleAir, read from the landmark blob). #79 box collision (W-67) is for surface kit props in flora's SupportGrids. A standalone dungeon needs one of: (a) a dungeon-air hook in SolidDistance / FloorUnder (PlayerMovementSystem is lent to the world lane; TerrainSource is world lane); (b) dungeon rooms baked into LandmarkSetBlob next to the temple's (world lane, touches the world fingerprint); (c) dungeon boxes fed into flora's support grid (flora/world code)
  - RuinKit already has the set pieces (Hall, DeadEnd, Chamber...) and a socket-to-socket layout builder (W-56, also #24); it has no pool room: that's a world-lane tile request
- Option that needs no other lane's code: use the existing sunken temple's kit rooms as the test dungeon (walkable, drawn, collided today) and generate sockets per room from the baked RuinRoom/RuinBox data (hall: Creature/Clutter, dead end: Chest NearChest, room walls: WallDecor...), plus a debug draw. Pool / NearWater would wait for the world lane. Needs a working `-place temple` first (P-5 open)
- Checks: compiled 0 errors in the editor; no build (card: none); editor closed
- Cost: ~16 calls, 0 builds
- NEXT: supervisor picks (a)/(b)/(c) with the world lane, or the temple-rooms option; ask the world lane for a pool room piece

## 2026-11-03 batch P-5: camera underground (#85), partly verified
- DONE (code): underground = cave near the head, sunken temple room (TempleAir), or head under the ground surface; there the rock probe decides and the open-air ground clamp + hill pull-in (to 1.5 m) are skipped. Underground pitch -60..85 (above ground -20..80, eases back); SetView takes the wide range. DebugControls additive: `-place temple` (player-client 0b2309d). Tell-supervisor note: DebugControls is the animal lane's file, change is additive only
- Why: #85 says "inside ruins the camera zooms in and clips most of the character as a rabbit". Ruins = sunken temple rooms (underground, not cave field), so the camera ran the open-air path: ground clamp lifted it through the stone, hill check pulled it to 1.5 m from a pivot 1.6 m above a rabbit
- VERIFIED: hut cave (Granite cave by HermitHutAsh, -3989,37,807), rabbit: default view unchanged (a4-cave.png); `-view 0,-50` now looks up the shaft to the sky hole (a5-cave-up.png; was capped at -20, b5-cave-up.png). Above ground a -50 request eases back into range (t3-temple-up.png)
- NOT VERIFIED: inside a temple room. `-place temple` found no room floor (2 m grid, 40 m round the places-list SunkenTemple spot, 0-20 m down); `-place SunkenTemple` lands on open sand beside it (t1). Before shots of `-place Ruins` (b1/b2) are the surface Ruins landmark, open grass, no walls
- NOT DONE: looking up underground loses a small body from frame: the pivot is a fixed TargetHeight 1.6 m above the feet (rabbit a5). Needs a body-scaled pivot (no body-height field on CreatureSpecies; read it from the player's collider/skeleton)
- Checks: compile 0 errors; 2 builds Succeeded 0 errors (2nd: one-line SetView clamp so -view/warps can aim past -20, under the one-fix allowance); 3 player runs all quit, no Punchy.exe left; editor closed (queue empty, 10 GB at open)
- Shots: D:/PunchyMilestones/P-5/after-sheet.png (a1-a3 failed temple placement, a4 cave, a5 cave looking up), before-sheet.png, temple-sheet.png
- Cost: ~40 calls, 2 builds (1 over card, see above)
- NEXT: fix `-place temple` (check the temple's local frame: SunkenTempleShape.Local / Top 12.5 Bottom -6, maybe search above the cut surface too), shoot temple before/after; body-scaled camera pivot for small bodies

## 2026-11-03 batch P-4: 30 s net timeout fixes the timeouts; uncovers host refusing its own client
- DONE: NetDrivers.DisconnectTimeoutMS 10000 -> 30000 (player-client 18e305d, merged main incl. PR #93 first, no conflicts)
- Timeouts gone: 0 "disconnected (Timeout)" lines in 5 runs
- A (host v3, client v2): client "switching to v3", "sending world check B291FD17"; host "player 1 has the same world (B291FD17); welcome"; client "in game True, players 1", 48-62 animals, 60 fps. PASS for the joiner
- C (host v2, client -worldsalt 7): host "a player built a different world (0613F356, ours CAC3BAE8) ... Disconnecting them." PASS
- NOT DONE row 2 / host half of A: the HOST REFUSES ITS OWN LOCAL CLIENT in every run (host-only v2, A, C): host client "sending world check F5BF2604" (A; C 48648189) vs server B291FD17 / CAC3BAE8 -> "a player built a different world ... Disconnecting them", host ends "in game False, players 0". Was hidden before by the 10 s timeout dropping that client first
- Cause: WorldSync.Signature hashes palette.Landmarks names. FishingVillage (and turtle / sea folk village + landing) are in no scene (FishingVillage.asset guid not referenced by any .unity), so IslandV2System appends them to the palette at bake time, but only in the world that bakes. The host's client world takes the Shared bake path and never appends them -> different palette -> different fingerprint. Joiners bake themselves, so they match. On main since #36/#37/#38, not from P-2
- Proposed fix (IslandV2System, not lent this card): move the palette append (village, turtles, sea folk) into a helper and call it on the Shared path too, before the "shared with" log. ~10 lines, then rerun host-only + A + C
- Checks: compile 0 errors, DisconnectTimeoutMS 30000 read back in the editor; 1 build Succeeded 0 errors; 5 player runs all quit by -quitafter, no Punchy.exe left; editor closed; queue empty, RAM 14.3 GB at open
- Cost: ~17 calls, 1 build
- NEXT: lend IslandV2System for the palette fix above (one build), rerun the P-4 rows

## 2026-11-03 batch P-3: host self-timeout attributed (long bake vs 10 s timeout); -quitafter flag
- DONE: `-quitafter N` (Debugging/QuitAfter.cs, new file, LF): quits N s after start, real time (player-client 2d0b08b)
- FOUND: cause is the island bake blocking the main thread past NetDrivers.DisconnectTimeoutMS (10 s), not the version RPC:
  - mine 99cfd9c, host-only v2, x2: "Net (server): connection 1 disconnected (Timeout)", host "in game False, players 0" all run; bake 5988 ms (places 5425), a46b had 3154 ms (places 2607)
  - same build, host-only `-island v1` (no bake, my RPC still sent "sent island v1 to player 1"), x2: no timeout, "in game True, players 1"
- WORSE, partly mine: P-2 test A rerun (host v3, client v2, -gfx Low, -quitafter) the JOINER also timed out after "sending world check B291FD17" (P-2's run survived). Since P-2 the client bakes after it connects, so its whole bake counts against the server's 10 s. Don't merge P-2 alone
- NOT DONE: main 913d1b2 baseline build (needs a 2nd build; v1 runs attribute it instead). No fix: the fix is shared net code not lent to me
- Proposed fix (needs supervisor OK, NetDrivers.cs / GameBootstrap.cs): raise DisconnectTimeoutMS back toward 30 s at least for the in-process (IPC) link and while a joiner bakes; or bake off the main thread / split the places step across frames (world lane)
- Checks: compile 0 errors; 1 build Succeeded 0 errors; 7 player runs, all quit (QuitAfter "75.0 s, quitting" / "55.0 s, quitting"), no Punchy.exe left; editor closed; editor queue was empty, RAM 8.5 GB at open
- Noted only: DebugControls.ShotList NullReference after a lost connection (animal lane)
- Cost: ~30 calls, 1 build
- NEXT: supervisor decision on the timeout fix; then rerun P-2 A + C with -quitafter

## 2026-11-03 batch P-2: island version from host to client (v3 B14, brief 8B)
- DONE: host sends each new connection its baked island version (IslandVersionRpc); a joined client's IslandV2System waits for it (up to 15 s, then bakes its own with a warning) and bakes the host's version; solo/host keep the launch flag; bake cache keyed by version; fingerprint uses the version actually baked (check waits until baked); `-worldsalt N` test flag sets WorldSync.TestSalt (player-client 99cfd9c, on main 913d1b2)
- NOTE: new RPC type = network table change (RPC hash): builds before 99cfd9c can't join builds after it
- A host `-island v3`, client no flag: client "WorldSync: host runs island v3 (this launch asked for v2)" / "[island] host runs island v3; this launch asked for v2: switching to v3 (ClientWorld)" / "sending world check B291FD17"; host "player 1 has the same world (B291FD17); welcome". Client in game, 50 animals, 60 fps
- B host v2, client `-island v3`: "switching to v2", both CAC3BAE8, welcome
- C host v2, client `-worldsalt 7`: host "a player built a different world (0613F356, ours CAC3BAE8) ... host runs island v2. Disconnecting them."
- OPEN (not proven mine): in all 3 host logs the host's OWN local client timed out at start-up ("Net (server): connection 1 disconnected (Timeout)" right after the server bake) and the host stayed "in game False, players 0"; the joiner was fine. a46b (older main) host had no timeout, but start-up was slower here (island ready 10.1 s vs 7.5 s, scene stream latency 6.5 s vs 3.8 s; other instances were running). Next: one host-only run of a main 913d1b2 build vs this build to attribute it
- Also seen: C client NullReferenceException in DebugControls.ShotList (EntityQuery.TryGetSingleton) after "Lost the connection to the host"; animal lane's file, pre-existing path; shot list still finished and quit
- Checks: compile 0 errors (1 fix: WorldFlags instead of NetCode IsClient in Terrain code), 1 build Succeeded 0 errors, 6 player runs all quit, no Punchy.exe left; editor closed
- Shots: D:/PunchyMilestones/P-2/A-client.png (v2-launched client in game on the v3 island)
- Cost: ~58 calls, 1 build
- NEXT: attribute the host self-timeout (baseline run); a `-quitafter` flag so log-only net tests can run lighter (see RAM notes)

## 2026-11-03 batch P-1: resolution and window mode in the Graphics tab
- DONE: #78 Graphics tab (main menu, also Esc in game) has Resolution (slider over the monitor's sizes, deduped by WxH, 14 on this PC), Window (Full screen / Borderless / Windowed) and Apply display; saved in PlayerPrefs (Punchy.Display.*) and re-applied at start (player-client 1a9ef6c)
- DONE: keyboard / pad nav via MenuNav (slider steps left/right, buttons choose); no new tab, rows sit above Frame cap
- Checks: compile 0 errors; 2 builds (Succeeded, 0 errors; the 2nd fixed a bug found in run 2: a size not in Screen.resolutions, e.g. 1600x900, was saved raw but snapped at the next start, so 1600x900 came back as 1440x900 and the slider showed 640x480; now snapped before saving). 4 launches, 0 exceptions, all quit on their own, no Punchy.exe left
- Numbers: run3 `-display 1280x720,windowed` logged "Display: 1280 x 720 Windowed (from the command line, saved)"; run4 with no flag logged "Display: 1280 x 720 Windowed (saved, applied)", shot 1280x720 (previous saved state 1440x900, shot 1440x900)
- Shots: D:/PunchyMilestones/P-1/p1-run4-kept1280.png (relaunch kept 1280x720 windowed, menu shows Display applied); p1-run2-graphics.png (1440x900 before)
- Test flag: `-display WxH,full|borderless|windowed` applies AND saves (it stands in for the menu). Registry prefs were exported before the runs and restored after (Punchy.Display.* removed, Screenmanager values back)
- Not checked: Exclusive full screen / borderless on screen (kept test windows windowed so they didn't take over the display); editor Game view ignores the rows by design
- Cost: ~38 calls, 2 builds (1 over the card: the one-fix allowance)
- NEXT: Zach to try full screen and borderless by hand; then #85 camera or the next P card

## P-22 row 1: roof undersides (2026-11-09, branch player/p22)
DONE. Cause: three darkenings multiplied on downward faces of built things (albedo x0.6, sky ambient swapped for a dim ground bounce, grounding down term x0.5), about 7% of a shaded wall by the shader's values, which is why raising one 0.18 factor (the earlier try) showed nothing. SSAO is not involved: Flora's forward pass doesn't sample it, and its normals pass already handles back faces.
Fix (Flora.shader, Grounding.hlsl): one underside rule from the ambient a shaded wall gets; the sheet flip no longer tests the normal against the view. It is in the shader, so it covers every kit drawn as a built thing.
Checked at -goto -3277,129.9,1549, noon, High: 8 fixed cameras before and after (undersides now read like the walls below; roof tops unchanged), and an 8-camera arc through the eave's plane and along it (no colour change). No exceptions or shader errors in the log.
Not checked: night and interior ceilings, which are lit by the same rule; other town styles' eaves up close; continuous motion (stills only). The arc was shot only on the new build.

## P-22 rows 2-3 (2026-11-09, branch player/p22)
Row 2 (#85): DONE, pushed (85de3a24). Rabbit in a tight Granite cave: the arm stopped at 0.41 m on the sloping floor and the lens was inside the body. A short arm for a small body now runs from a raised pivot, still aimed at the body (arm 2.41 m, rabbit in frame looking up, level, down and sideways). Temple room views as a rabbit match the old ones. Human and Osprey don't take the new path. Outdoors is untouched by construction (the code runs only underground). No managed allocation (a TempJob array, as before). Stills only, not continuous motion.
Row 3 (#78): DONE, no code change; DisplaySettings already provides it (P-1). In Builds/Dev-player: windowed at 1280x720 and 1600x900 confirmed by the rendered shot sizes; borderless and exclusive read back from Windows as captionless and covering the monitor; a restart kept each mode ("saved, applied"). Display prefs restored after. Not checked: the Graphics tab itself (the -display flag stands in for it), and on-screen proof that exclusive differs from borderless (Windows reported the same window for both).
Suite: 14 passed, 0 failed on 85de3a24 (pin tests included), after an earlier run was stopped for low memory. P-22 is complete on player/p22 (head 85de3a24), not merged.

## Zach's answers to the P-23 and P-24 draft questions (2026-11-09), for the supervisor to fold into the cards
- P-23: gear changes looks only for now. No stat block; stats need their own design first. Which gear comes first was not answered (the draft's torch, staff, hat and pack still stands as a guess). A torch that gives light is a question for that row, since light is more than a look.
- P-24: the tracker is text only. No persistent on-screen guide to where to travel, so the draft's distance and direction readout, and the world marker, are out unless Zach says otherwise.
- P-24: quest progress should eventually be saved. A save file can be explored now, but has to stay flexible because the game is changing fast.

### Save file: what exists today, and a suggested shape (not built, no card yet)
Today only settings and the chosen body and colouring persist, in PlayerPrefs. Inventory, quests and world changes are lost on quit. Quest state is keyed by NetworkId, board index and slot, and none of those is safe to save: NetworkId changes each session, and board index and slot move when settlements or quests.json change. QuestDef already has a stable string `id`.
Suggested shape, so that changes to the game don't break saves:
- One JSON file per world, written by the host, with a format version and the seed at the top.
- Named sections, one per system (quests, inventory, later others). Each system reads and writes only its own. A section the game doesn't recognise is kept and written back; a missing one means defaults.
- Saved keys are stable names, never indices: quest `id`, item name, town name. An entry that no longer resolves is logged and dropped, without failing the load.
- Players need a stable id in place of NetworkId, which each client sends on joining. The `Punchy.PlayerIndividual` pref may be a start; not yet checked that it is unique enough.
- Open choice: what to do when the seed differs (refuse, or keep only what isn't tied to a place). Moved or rebuilt worlds are likely while the island keeps changing.
A first slice could be the file, the section mechanism and quests only.

Zach's follow-up on the same drafts (2026-11-09):
- Save file: no seed checks. A changing seed is a development side effect that won't exist in the final game, so the format carries no conditions for it. This replaces the "open choice" above.
- P-23: the torch gives real light (through the light budget). Lighting in dark places and on dark nights will matter to the game. Other gear stays looks only.
- P-25: taken by the player account, branch player/doors, starting row 1.

## P-25 row 1: door prompt (DONE, c01551c9 on player/doors)
Cause measured: door anchors stand 0.6-0.8 m in front of the door, so at the door the player has passed the point the facing test aimed at. Every old-rule failure was the facing cone.
New rule: reach 3 m to the opening, cone about 75 degrees either side, body or camera, no facing test within 1.2 m, nearest door in front wins, prompt held 0.35 s and drawn at the door.
Scripted approaches (`-doorwalk 40 <mode>`, 10 doors, 5 angles, half run and half stroll, shown within 0.2 s of arriving and still up; old and new scored on the same frames; approaches blocked by obstacles left out):
- Stop 1.4 m out (the card's pass): human new 46/46, old 46/46; rabbit new 45/45, old 45/45. Both rules pass, so this case does not show the problem.
- Walk until the door stops you: human new 47/47, old 12/47; rabbit new 48/48, old 14/48.
- Walk into the wall 0.8 m beside the door: human new 47/47, old 14/47.
- Stop, turn the body 120 degrees, camera on the door: human new 46/46, old 0/46.
- Straight walk-ups: no flicker in any approach.
UNVERIFIED: the prompt's position at the door (the T-1 shot draws the 3D view only, so the prompt isn't in it; Zach to judge in the playtest). Pad and keyboard weren't driven separately (the rule doesn't read the device). Host and client weren't run: the rule is local and the door RPCs are unchanged.
Suite: 15 passed, 0 failed, 7 skipped in 219 s. An earlier run on the same code had 14 passed, 1 failed: Setup_SpawnRainAndShot hit its 180 s limit with three other editors open; it took 11.6 s on the re-run.
Next: row 2.

## P-25 row 2: indoor camera and first person (DONE, 43f32fbf on player/doors)
Indoors: the camera's distance eases to 2.5-3.5 m by room size over 0.6 s, and back on leaving (logged in a StormShelterHouse: wanted 3.48 then 2.50 going in, 7.14 then 8.00 coming out). The room's walls still limit it: standing just inside the door the wall behind holds the arm at 0.83 m, so the pull-in only shows further into a room. Ceilings are respected by the existing arm probe.
First person: the Camera distance setting now runs 0.5-20 m and reads "first person" below 1 m; scrolling reaches it too. The eye is at the height the camera already aimed at. The player's own body isn't drawn at all, so its shadow goes as well (a small additive change in CreatureRenderSystem, the animal lane's file). Pitch runs -85 to 85. Near plane 5 cm. New Look tab option "First person indoors".
Eye heights above the feet: human 1.60, rabbit 0.40, osprey 0.93, horse 2.42 m. Shots ahead and straight down for each, outdoors, and human and rabbit in a room: no body in view in any; the human shows in third person in the same room. T-1 checks all "good". Shots are local, not in the repo.
Other bodies weren't checked beyond those four. The eye sits above the body's centre, not forward at a horse's or bird's actual head.
UNVERIFIED: the Look tab controls were not clicked (test flags -camdist and -fpindoors stand in, and -fpindoors itself wasn't run); host and client weren't run (the camera and body hiding are local).
Seen, not caused here: looking down in that room shows a wedge of blue at the edge of the view, in first and third person. Likely a gap in the room box; for row 3.
Suite: 15 passed, 0 failed, 7 skipped in 230 s.
Next: row 3.

## P-25 row 3: seamless interiors (design note DONE; prototype BLOCKED, question for the supervisor)
**Question: may the prototype change one house kit?** The card says to ask first. A real walk-through needs a real hole in the shell, and today there is none: behind the door leaf is a dark panel in front of a solid wall (FloraGenerator.Door), and kit houses collide as wall circles plus one fat cylinder inside. Without that change the prototype could only fake the doorway. If yes: one type (StormShelterHouse, a kit house), one town, behind a `-seamless` flag, teleport still the default, anchors untouched. Rows 1 and 2 don't depend on this and are ready for review.

### Design
Today: using an open door teleports the player to one room per house type, built off the island out of box GameObjects. The room boxes (InteriorCells) are axis-aligned and global.

- **Shell (world lane).** At the near LOD, an enterable house's front wall has a real opening the size of the door, with wall thickness showing at the jambs and lintel. The leaf swings on its existing hinge. The dark panel stays at far LODs, where nobody can walk in.
- **Collision.** Enterable types lose the fat inner cylinder, and the door bay's wall circle. Walls become boxes in the house's own frame with the door gap (InteriorCells.AddRoom already takes a door width and height). Houses are turned to their plots, so boxes need a yaw: a query rotates into the house's frame first. The player already stands on and bumps into these boxes, and the camera's arm probe already treats them as solid.
- **Contents.** Keep the per-type seeds and furniture, so a type's layout is the same in every town, placed in the house's frame. One baked mesh per type, drawn instanced, in place of a GameObject per box. Rooms would no longer be shared: containers are numbered per type today (so every house of a type shares its chest), and need numbering by door.
- **Lighting.** The room ambient (P-16) blends on being in a room; it becomes a blend on the player's position through the doorway. Lamps stay inside with range sized to the room (P-21). No sun indoors except through the door: the shell casts the shadow.
- **Cost per town.** Only houses near the player build insides, by DetailPolicy, up to about 8 at once. A room is 10-20 boxes, under about 300 triangles, one draw per type. InteriorCells has only one overall bounds check today, and the camera can ask up to 160 times a frame, so it needs a per-house lookup first (which house is this point in, then that house's boxes only).
- **Networking.** Nothing new: layouts are seeded and door state is already host-owned. Each player's own body moves through the door.
- **Camera.** Interiors.IndoorArm (row 2) reads the room the player is in; it would read the room around the player instead. No other camera change.
- **Folk.** Door anchors stay where they are.

**Needed from the world lane:** a wall-with-opening in the house builders and kit, one type first; no inner cylinder on enterable types.
**Order after the prototype:** the per-house lookup, then one type per culture, then retire the teleport.

## P-27: player stats (rows 1 and 2 DONE together, df1bb275 on player/stats, from main; not on top of P-25)
Taken before P-26 because A-93 row 1 isn't merged. Row 2 was small enough to verify with row 1 in one build.
Row 1: StreamingAssets/stats.json (hand-edited, read again a second after it changes) holds the five stats, values for human, small, medium and big bodies, and derived values: carry limit, run bonus, talk bonus. PlayerStats has Value, Derived, Grant(stat, amount, reason) and CanCarry.
Row 2: a Stats tab in the menu (values, descriptions, derived numbers). F2 grants a point to each stat in turn.
Measured in builds, from the log: human strength 10, carry 70 kg; +2 earned gives 12 and 76 kg; after a restart as a rabbit: 5 (3 + 2), carry 55 kg; after a restart as a horse: 20 (18 + 2, held at the range's top), carry 100 kg; back as a human and -2: 10 and 70 kg. Editing the file while running took carry to 71 kg with no restart. The test's earned points were taken back afterwards.
No change to play by default: nothing reads a stat except the carry limit, and enforceCarry is false. Run and talk bonuses are numbers only: the run bonus isn't wired to movement, and the talk bonus waits for A-93.
No save file exists, so earned points are kept per machine with the settings (the card's "saved with the game"). They should move into their own section when a save file is built. Not sent to the host: the game doesn't sync player stats.
No stamina exists, so no hook for it.
UNVERIFIED: the Stats tab itself and the F2 key were not exercised (the shot tool draws only the 3D view; -statgrant goes through the same Grant), and enforceCarry true was not run.
Suite: 15 passed, 0 failed, 7 skipped in 228 s.
Next: P-26 once A-93 row 1 is merged. P-25 row 3's prototype waits for the supervisor's answer on the house kit. P-23 and P-24 are still drafts.

## P-26: talking to folk (rows 1, 2 and 3 DONE together, b5343e03 on player/talk, from main with player/doors merged in)
Branched from main and merged P-25's branch, because the card reuses its prompt rules (DoorPick). P-25 needs to merge first, or goes in with this.

Measured in builds, from the log (Use pressed by the test, not a real key, except the pad row):
- Pass line, 10 folk from the front: window open on 10 of 10, worst 9 ms. Folk stopped and faced the player 10 of 10 (Greet stage 2, speed under 0.2, facing dot 1.00).
- Gesture: sent on 3 of 3 folk, and the folk's action read Wave half a second later. A folk still walking when the gesture arrives drops it (Fidget), so it only plays after the first half second or so.
- Joined client against a host on this PC: opened 3 of 3, worst 11 ms; each folk stopped and took the gesture through FolkTalkRpc.
- Rabbit: no window; the reaction goes up over the folk's head, 6 of 6, worst 10 ms. Reactions and responses came from A-93 (Pet, Wary, BackOff, Approach, Feed).
- Offers: a quest and a gift each accepted. The quest went into the quest list (a board slot); the gift put 3 Berry in the inventory. A gift is once per folk per session.
- Met again: greeting changes, and the first topic moves from "who" to "how".
- Virtual pad: RB opens, d-pad down moves, A picks, B ends with no main menu, left stick walks away. Right stick held 1 s during a talk: camera 0.00 degrees, player 0.000 m.
- Shots (whole screen, window included, new "-talkshot dir"): a human talk (8 topics, the card said 3 as an example), quest offered and accepted, gift accepted, a rabbit meeting a folk. The same individual wasn't guaranteed across the human and animal runs.

Changes outside the talk code:
- FolkTalkSystem: CanTalk and Head made public (animal lane's file, visibility only).
- Overlay.AnyOpen is also true during a talk, so the camera, cursor, emote keys and prompts all hold.
- Quest boards: Offers now holds every quest after the ones the board posts (Posted), so a folk's quest has a slot on the existing host path. Boards still show only the posted ones.

Not what the card said:
- "Greet you by name": the game has no player name, so the second greeting is "Oh, it's you again." Folk names are new: StreamingAssets/folk-names.json, picked by Individual, the same on every machine.
- "Sized by the UI scale option": no such option exists, so text is sized by screen height, as the walk-by lines are. Not looked at on a laptop screen.
- The card said a client sends nothing; A-93's Respond and the stop need the host, so what the folk does is sent (STATE said this). The words stay local. The favour cooldown stays per machine.
- A folk's quest is handed in at the nearest notice board to where it was taken. The window names that town.
- An animal offered a berry (Feed) gets the folk walking up and the line, not the berry.

UNVERIFIED: real mouse clicks, keys 1-9, arrows, Enter, Esc and WASD (only the pad was driven, and only a virtual one); host playing as itself in a talk; laptop screen. On a freshly added virtual pad, the first RB press missed in 2 of 4 runs with the folk in reach and no door or item nearer; the next press worked. I didn't find why. The 10-folk pass line didn't go through the pad.
Suite: 24 passed, 0 failed, 17 skipped in 330 s.
Next: P-25 row 3's prototype waits for the answer on the house kit. P-23 and P-24 are still drafts.

## P-23: item models and gear
### Row 1 (item models) DONE, on player/items from main
All 8 items now have a small procedural mesh in the world and an icon in the inventory and chest panes (ItemMeshes): stick, stone, berries with a leaf, mushroom, shell, fish, coin, key. Item ids and networking are unchanged. ItemDef gains Accent, a second colour for part of a mesh.
The icon is drawn on the CPU from the mesh once per item (64 px), not with a camera, so it doesn't change with the time of day or graphics settings.
Mushroom's colour changed to a red-brown cap (it was the pale colour now on its stem).
Shots: "-itemrow" lays one of each in front of the player at noon and in the inventory; I looked at both, and every item reads as what it is. The key was hidden behind the player in the ground shot, but shows in the icon.
UNVERIFIED: the chest pane's icons weren't shot. It uses the same call as the inventory.
Suite: 25 passed, 0 failed, 17 skipped in 321 s. The run before had 24 passed, 1 failed: FolkConversationTests.LiveFolk_Answer, then it passed unchanged on the rerun.
That test is A-93's and expects a person-shaped player. It takes the body from the settings saved in the shared PlayerPrefs (Punchy.Character / the body key), so it can fail when any lane or Zach last picked an animal. It wants "-body Human" or its own body set. For the animal lane's queue, if you agree.
Next: row 2 (gear).

### Rows 2 and 3 (gear, and where it comes from) DONE together, on player/items
New items, appended: Torch, Staff, Hat, Pack, with Slot and stat numbers in ItemDef. Put on with X or a right click on the inventory slot (X on a pad); the worn row under the slots takes them off.
- Torch: real light, using the guards' torch path. Its reach is ItemDef.Light (11 m). Glow budget, same run before and after putting it on at night: requested 49 to 50, slots 0 to 1, dropped 0.
- Pack: 8 extra slots, 16 to 24 in the log. It won't come off while its rows hold anything.
- Staff (Reach 0.5) and Hat (Cover 0.3) have their numbers and show them, but nothing uses those yet: no reach or stamina rule reads a staff, and the player has no weather comfort.
- Rabbit: refused both, and held nothing.
- Host wearing hat and staff with a client wearing torch and pack: each machine logged the other player's body dressed correctly (held and worn) through GearRpc and GearStateRpc. A later joiner is sent everything worn so far (code only; not run).
- Night shot and worn-row shot looked at: flame in the hand, ground lit round the player, hat on the head, pack on the back.

Changes in the animal lane's files, for P-23: CreatureState gains Worn (a byte, not sent); the renderer gains 3 capsules on bodies with something worn. Nothing changes for a body wearing nothing.

Row 3: 384 of 4000 simulated chests hold gear (397 pieces). DungeonSpawner's LootRows gain the four items at low weights. quests.json: the lost-key quest now pays a Torch; rewards were already by item name.

UNVERIFIED: real mouse, X key and pad on the equip (the test went through Equip directly); the staff drawn in a player's hand wasn't shot; a dungeon chest's gear and the quest paying a Torch weren't run through.
Suite on this code, not green: 24 passed, 1 failed, 17 skipped in 334 s, then 23 passed, 2 failed, 17 skipped in 320 s. Row 1's code had 25 passed, 0 failed.
The failures are FolkConversationTests.LiveFolk_Answer (both runs) and LiveFolk_BeastResponse (second run: "the folk told to flee isn't fleeing"), both A-93's. LiveFolk_Answer's log shows the player was a rabbit in one run; the saved body read Osprey when I checked, and none of my runs used an Osprey. So the shared saved body is being changed while tests run. BeastResponse may be the same cause or the load (6 editors open); I didn't prove which. No test failed outside those two.
**For the supervisor:** the suite takes the player's body from Punchy.Character.Body in the PlayerPrefs every lane and Zach share. A "-body" run doesn't write it (none of my 83 run logs has the "saved for next time" line); a body changed during a session does. So those two tests can fail whenever someone last picked an animal. They need their own body set (animal lane's queue, if you agree).
Also for P-26: Items.cs notes a virtual pad isn't Gamepad.current on its first press (P-16), which fits the first-press miss I reported there. A real pad isn't affected.
Next: P-24.

## P-24: quests
### Row 1 (tracking in the menu, saved progress) DONE, on player/quests (from player/items, with main merged)
Zach's override followed: the tracker is in the quest log only. No overlay and no world marker (I didn't ask about a marker, since none is needed yet).
- The log shows each taken quest's progress ("Stones 0/4") or where to hand it in. Press a quest, or U (Y on a pad), to follow it; the followed quest goes first with a star.
- Saved to a file per machine, by quest id, town (name and position) and a hash of the towns as the world version. New fields can be added without breaking old saves.
- Measured: 3 quests taken, restarted, "3 restored, 0 lost".
- Old save against a changed world (a quest id removed, a town renamed, old world hash): "1 restored, 2 lost to time", with a reason each. They show in the log, and dropping them gave 5 coins each and removed them from the save.
- A done quest comes back shown as done without paying again. A client tells the host again what it had taken (code only, not run).
UNVERIFIED: pressing a row and the U key (no real input was driven; "-droplost" calls the same drop); a client's restore; a save with an item removed from the table.
Coin refund is a flat 5 and goes straight into the inventory, so a joined client's coins aren't on the host's tally. Fine for now, but noted.

**Correction to P-23's report:** suite on this branch, which contains all of P-23: 25 passed, 0 failed, 17 skipped in 391 s, with the saved body reading Osprey. So LiveFolk_Answer and LiveFolk_BeastResponse aren't simply failing on the saved body, as I said. In one failed run the player was a rabbit, and I haven't found why. They failed in 3 of 6 runs on and after P-23 row 1, passed in 3. They look intermittent and unrelated to items, but that's not proven.
Next: rows 2 and 3.

### Rows 2 and 3, part DONE (e2bb0d21 on player/quests); the dungeon type and folk hints are NOT done
Done:
- "deliver": taking it hands you the goods; hand in at the other town's board, and it pays there. Run through the real calls: fish-run, GoblinCamp to ElfGrove, the board offered the hand-in, reward Staff paid.
- "visit": go to the other town (within 15 m of its board), then back to the first board. Run: scout, ElfGrove to DwarfMiningCamp and back, reward Pack paid.
- Six new quests in quests.json (14 in all), some paying Staff, Hat or Pack. A visit's "visited" is saved with the quest.
Not done:
- "dungeon" quests: they need a named dungeon with a stable id and a way to know it's cleared, and A-95 / A-91 are changing that code. Waiting for those. "Escort" wasn't built either (the card said only if cheap).
- Folk lines hinting at nearby quests: P-26 has the talk window, but it isn't merged to this branch. A later small job.
- 3-5 quests per town style: boards still pick 1-2 by the town's index, so style isn't used; I added the six quests overall instead.
Merge note: P-26's change to board Offers (Posted) and this branch's changes to the quest code will meet in Quests.cs; I expect a small conflict.
A visit's hand-in takes the client's word (it can't be checked by carried items), and its "visited" flag is local.
UNVERIFIED: both types as a joined client; pressing Use at the board (the test called TurnIn after reading the board's own answer).
Suite: 24 passed, 1 failed, 17 skipped in 331 s. The failure is LiveFolk_Answer again; no other test failed. That test class sets the body itself (SetBody Human, Rabbit, Wolf, Horse at FolkConversationTests line 176-207, restoring afterwards), which looks a likelier cause than the saved body. Not proven.
Next: nothing open. P-25 row 3 still needs the answer on the house kit.

## P-28 row 0 (11-10): DONE, `player/integrate` at 73b18052
- From origin/main ed0e68ef, merged origin/player/talk (b5343e03), origin/player/quests (e2bb0d21), origin/player/stats
  (df1bb275) in that order, merge commits, no conflicts.
- Suite: 25 passed, 0 failed, 17 skipped in 361s (Unity exit 0).
- Smoke: 60 s Dev run, v3 island, Low: 0 exceptions, no shader errors, quit at 60 s by itself; 14 quests loaded.
- **Main doesn't build as a player (not from these merges):** `Debugging/DebugControls.DungeonWalk.cs` (A-95 rows 1-2,
  daadb490/f31751d5) calls `TestHooks` at lines 146, 158, 418, 419; TestHooks is `#if UNITY_EDITOR`, so a player build stops
  with CS0103. For the smoke build only I wrapped those lines in `#if UNITY_EDITOR` (with `string was = null;` in the #else),
  then reverted: not in the branch. The animal lane needs the same guard (or the supervisor can add it on merge).
- UNVERIFIED: nothing joined (host/client) in this run; each branch's own reports cover that.
- Next: P-28 row 1 (weapons), from player/integrate until it merges.

## P-28 row 1 (11-10): DONE, `player/weapons` at 42847ca9 (from player/integrate)
- Items appended (ids 12-16): Club (dmg 6, reach 0.9 m, 1.1 swings/s), Spear (8, 1.9 m, 0.8/s, two hands), Sword (9, 1.1 m,
  1.4/s), Bow (7, 25 m, 0.6/s, two hands), Arrow (stack 30). Hand slot; ItemDef gains Damage, Speed, TwoHanded, Weapon.
  Procedural meshes and icons in ItemMeshes. The worn row shows e.g. "Bow, dmg 7, reach 25 m, 2 hands".
- **The player has no attack** (the Attack input action is bound but nothing reads it), so per the card: stat numbers and a
  swing pose only, no combat. Left mouse (while it steers the camera) or pad X outside the inventory swings; a swing lasts
  1/Speed s. Club/sword raised over the shoulder and brought down, spear thrust, bow drawn and loosed (an arrow on the string
  while drawn).
- Drawing: CreatureState gains `Outfit` (weapon kind, swing phase; 2 bytes, not sent: each machine sets it, like Worn).
  Pieces are in the worn pass in PoseGear.cs (WeaponParts 7), skipped under 6 reference px (DetailPolicy.ScreenSize).
  Outside PoseGear.cs: WornCount takes the state (CreatureRenderSystem.cs line 98, Passes.cs line 517, the call at
  CreatureRenderSystem.cs line 682), and **an additive arm block in CreatureAnimator.cs** after the staff's (the animal lane's
  file; no swing pose without it). No shader edits.
- Sync: SwingRpc client -> host -> everyone; the weapon goes through the existing GearRpc/GearStateRpc. Host + client: each
  saw the other's weapon (host: "another player's body ... weapon 4" = bow; client: "weapon 3" = sword); swings 26 relayed
  from the client, 36 from the host received; 0 exceptions either side.
- Arms, measured (`-armlog`, hands from the body): no weapon up 0.75 / ahead -0.03; club and sword ready, right hand up 1.04,
  ahead 0.47; spear both hands ahead 0.50/0.58; bow both up 1.25-1.33, ahead 0.65-0.67.
- Shots (D:/Unity/shots-P28 on the main PC, human at noon, -camdist 3): each weapon back and front; side views of a sword
  swing (raised, struck through) and a bow drawn; inventory pane with the three weapon icons and the bow worn.
  `[shot]` lines: 8 of 8 then 6 of 6 ready after 0.8-1.1 s, contact sheets written.
- Suite: 25 passed, 0 failed, 17 skipped in 335s (Unity exit 0).
- Test flags: `-equip A,B;C` (sets 12 s apart, now counted from when the body first stands in the world), `-swing`, `-armlog`.
- Builds still need main's DungeonWalk TestHooks guard (row 0 note); I apply it locally per build and revert it.
- UNVERIFIED: a real mouse click and a real pad X (only the -swing path ran); weapons in chests/rewards not added (not in
  row 1); animals refusing a weapon goes through CanWearGear as before, not re-run.
- Next: P-28 row 2 (armour), on top of player/weapons.

## P-28 row 2 (11-10): DONE, `player/armour` at d6666a82 (on player/weapons)
- GearSlot appends Legs, Feet (Body already existed, unused till now). Items appended (ids 17-22, after Arrow):
  Cap (head, armour 1, 0.5 kg), Helm (head, 3, 2.5 kg), Jerkin (body, 2, 3 kg), Chainmail (body, 5, 9 kg), Greaves (legs,
  2, 2.5 kg), Boots (feet, 1, 1.2 kg). ItemDef gains Armour, Look. Cap and helm keep a small Cover (0.1, 0.2).
- Weight: worn gear now counts with the inventory in CarriedWeight (the carry limit's input; P-23 gear wasn't counted
  before). Measured: Helm+Chainmail+Greaves+Boots worn 15.2 kg, armour total 11. Armour is a number only (nothing fights).
- Drawn as shells a little larger than the body's own segment under them (head over the crown with a flat rim, the torso's
  last two segments, each shin, each foot), so the size follows the body kind; in the worn pass in PoseGear.cs
  (ArmourParts 8), skipped under 8 reference px. Linear colours (the first try read near-white). The straw-hat bit is now
  the Hat item's only, so a cap or helm doesn't also draw the hat.
- **Sync finding:** GearRpc/GearStateRpc grown from 3 to 6 bytes failed on both host and client in the Dev build:
  "RpcSystem failed to deserialize RPC ... bits read (56 [7B]) did not match expected (80 [10B])", connection then closed.
  Not proven why (stale generated or Burst serializer is my guess). Fixed by leaving those RPCs at their known shape and
  sending armour in new ArmourRpc / ArmourStateRpc. After: host saw the client's cap+jerkin, the client saw the host's
  helm+chainmail+greaves+boots, 0 deserialize errors either side. Worth knowing for anyone growing an existing RPC.
- Shots (D:/Unity/shots-P28-armour, human at noon, front/side/back): set 1 cap+jerkin+boots, set 2 helm+chainmail+greaves
  (+boots), set 3 straw hat+jerkin (+greaves, boots). 9 of 9 `[shot]` lines, contact sheet written.
- Suite: 25 passed, 0 failed, 17 skipped in 325s (Unity exit 0). The run before it: 24/1 (FolkConversationTests
  .Beast_ReactsInCharacter, "SetUp : world not up after 240 s", 584 s total with CPU ~50% busy from other work); unchanged
  code passed on the re-run.
- UNVERIFIED: real clicks on the six-button worn row (only -equip ran); a non-human upright body in armour (sizes follow
  the segments, but only a human was shot).
- Next: P-28 row 3 (clothing).
- Correction (row 2): the armour went in before Arrow in the table, moving Arrow from id 16 to 22. Fixed in 746ea888
  on player/armour: Arrow back at 16, armour 17-22. A one-line data move, not yet covered by a suite run; row 3's
  suite runs will include it.

## P-28 row 3 (11-10): DONE, `player/clothing` at 9a0455ea (on player/armour; P-28 complete)
- Items appended (ids 23-29): Tunic, Robe (body), Cloak (back), Scarf (new GearSlot.Neck, appended), Hood, Woolcap,
  Brimhat (head). No armour; light (0.1-1.2 kg); Cover: brimhat 0.35, hood 0.25, cloak 0.25, woolcap 0.15, robe 0.1,
  scarf 0.05 (a number, as the hat's is).
- Folk colours: cloth is dyed by the wearer's GuardKit (human madder red, dwarf moss, elf teal, goblin mustard, ogre
  plum, each with a trim). One item per garment; the colour comes from who wears it.
- Drawn in the worn pass (PoseGear.cs, ArmourParts now 13): hats over the crown (brimhat wide brim, woolcap bobble, hood
  hanging behind), tunic/robe shells on the torso, a robe's skirt to the shins, the cloak from the shoulders in two pieces
  swept back with speed (up to 4 m/s) and stirring with the clock, the scarf round the base of the head with an end down
  the chest. The cloak first hung about 0.3 m behind the back; fixed (the torso's flattened depth) and reshot.
- Sync: the Neck place goes in new SlotRpc/SlotStateRpc (one place, one item), so places added later need no new message
  and no existing RPC changes shape (row 2's finding). Host + client: host saw robe+woolcap+scarf, client saw
  tunic+cloak+scarf; 0 deserialize errors.
- Where it comes from: chests (the existing 1-in-20 gear roll per slot now includes clothing: about 7 in 22 of those),
  quest rewards (letter-sticks pays Scarf, see-town pays Cloak; were coins), and three folk gift lines appended to
  dialogue.json (scarf 0.2, cloak 0.1, tunic 0.2 for traders/tanners). A folk gift of something to wear is 1, not 3.
  **dialogue.json is the animal lane's data file (A-93)**: three lines appended at the end of the gift list, nothing else
  touched.
- Shots (D:/Unity/shots-P28-cloth): outfit 1 human tunic+woolcap+scarf+boots, outfit 2 human robe+hood+cloak, outfit 3
  elf robe+brimhat+scarf (teal, silver trim), outfit 4 dwarf tunic+hood+cloak+boots (moss); a cloak standing and while
  running (swept back). 4+3+3+3 `[shot]` lines, all ready.
- Suite: 25 passed, 0 failed, 17 skipped in 350s (Unity exit 0). Includes row 2's item-id fix (746ea888).
- Test flag: `-sprint` (a test walk runs).
- UNVERIFIED: clothing actually dropping from a chest, paid by those two quests, or given by a folk in play (the data is
  in, not run); goblin and ogre colours not shot; the cloak is two stiff pieces (it tilts, it doesn't fold).
- Branches to merge, in order: player/integrate (row 0) <- player/weapons <- player/armour <- player/clothing (each on
  the one before). Builds still need main's DungeonWalk guard.
- Next: P-29 (hairstyles) after P-28 merges per its card; P-30 may run first if P-28 waits on review.

## P-30 (11-10): DONE as a prototype, `player/seamless` at 0c5e6b89 (from player/integrate, not main: it needs P-25's doors)
Branched from player/integrate rather than origin/main as the card said: the prototype changes P-25's door and interior
code, which isn't on main yet. Merge after player/integrate.
- Behind `-seamless` (read once from the command line; off by default, teleport unchanged): the storm house
  (StormShelterHouse, KitShape.StormHouse; 12 on v3) is walked into. World-lane files touched, behind the flag only:
  StructureKit (the flag, WalkIn), FloraGenerator.Kit/FloraGenerator (door leaf hinged and its box kept apart in
  DoorBoxes; no centre cylinder, no wall circles, no trunk circle for that type; plinth floor and step solid, step raised
  0.16 -> 0.25 m), FloraSystem (door leaf box added only while FloraDoors says the door is shut), FloraDoors (IsOpen).
  No shader edits, anchors and placement untouched; Landmarks_V3 and Fauna_Fingerprint pass (suite runs without the flag).
- Interiors: that type's doors have no room (so Use opens/shuts them, no teleport); a per-house lookup (walk-in houses in
  32 m cells: which house a point is in, then that house only) gives "inside, and how far past the door"; the ambient
  follows that depth (sky's at the sill, the room's 1.5 m in) and IndoorArm gives the camera 2.6 m inside.
- Measured (door 9, v3, noon, `-atdoor 9 -usedoor 9 -walkin 9`): door shut, the walk stops at the leaf; open, the player
  climbs the step and the 0.51 m floor, reaches 2.8 m past the door marker (house 0, depth 0.98), is held by the side wall
  when pushed at it (stops at 2.47 m across, still inside), and walks back out ("out of the walk-in house", back on the
  ground). Light 0.02 -> 1.00 -> 0.74 -> out; ambient equator 0.53 (sky) -> 0.17 (room). Frame time at the same door,
  standing, last 25 s: 3.75-3.89 ms plain, 3.77-4.35 ms with -seamless (noise; the first seamless lines include loading).
- Shots (D:/Unity/shots-P30): outside (door open, dark room behind it), in the doorway (on the step, leaf swung in),
  inside (floor, walls, roof's underside, the open leaf, dim warm light). `[shot]` lines ready after 0.3 s each.
- Suite: 25 passed, 0 failed, 17 skipped in 335s (Unity exit 0).
- Findings for the next step: (1) the door anchor isn't on the doorway's line for this kit house: the real doorway (a
  bay to one side) is 1.66 m from the anchor, so the "F open door" prompt and folk door use aim 1.5 m off; the test walks
  to the door's step first. (2) Without circles, creatures path through this type's walls (not checked, but nothing
  stops them). (3) No furniture: the room is the kit shell. (4) Not run as a joined client (door state is host-owned and
  FloraDoors is set on every machine, so the leaf box should follow).

## Doors.IsOpen for A-98 (11-10, STATE "small row"): DONE, `player/doors-open` at f5123250 (from player/integrate)
- `Doors.IsOpen(int door)` (index in the island's Door anchors) and `Doors.IsOpen(Vector3 doorPos, float within = 2f)`
  (the nearest door anchor within that distance; false if none). Read-only, this machine's view (the host's word, or solo).
  Only Interact/Doors.cs changed; the animal lane wires GameAudioOcclusion to it.
- Checked in a build: after `-usedoor 9`, "door 9 IsOpen True, by position True; door 10 IsOpen False".
- Suite: 25 passed, 0 failed, 17 skipped in 320s (Unity exit 0). The run before: 24/1, LyingTests
  .Lying_LegsStayAboveGround_FlatAndSlope (BoneStag -0.078 m on the slope and others), animal posing that this change
  can't reach; it passed unchanged on the re-run.
- Windows as sound openings: not done (the card says they can come with P-30; P-30's prototype didn't add any).

## Tools row (11-10, standing order "never idle": tools like a pickaxe, fishing rod, lantern): DONE, `player/tools`
(on player/clothing)
- Items appended (ids 30-32): Lantern (hand, Light 8 m, 1.1 kg), Pickaxe (hand, two hands, 2.6 kg), Rod (hand, two
  hands, line 3 m, 0.7 kg). Meshes and icons in ItemMeshes. Pickaxe and rod are numbers only (no mining or fishing for the
  player yet); the worn row says "mining" / "fishing".
- Drawn through the folk's own held items (HeldItem.Lantern, Pickaxe, Rod; no renderer change beyond the light's reach):
  the lantern lit in the off hand, the pick across the back, the rod in both hands with its line out. The rod also brings
  the folk fisher's basket at the feet, wherever the player stands: worth its own pose later.
- Light: the player's lantern reach is ItemDef.Light, as the torch's (one additive line in CreatureRenderSystem.Passes.cs).
  Measured at dusk: light budget requested 51 -> 52, slots 0 -> 1, dropped 0. Shot: the body lit warm round the lantern.
- Shots (D:/Unity/shots-tools, human, 20:30, front and side): 6 of 6 `[shot]` lines ready.
- Suite: 25 passed, 0 failed, 17 skipped in 350s (Unity exit 0).
- UNVERIFIED: host/client for these three (they ride the existing Hand slot in GearRpc, which row 1 checked).
- Next: P-29 once P-28 merges (its card says from origin/main after that); more item rows meanwhile.

## Shots moved (11-10)
DONE: per the supervisor's 11-10 rule, every player-lane shot folder is now under D:/Unity/PunchyMilestones/P-<nn>/ on the main PC
(190 files, nothing left in D:/Unity/shots-*). Old report paths map as: shots-P9/P22/P23/P24/P25/P26/P28/P30 -> P-9 ... P-30;
shots-P28-armour -> P-28/armour; shots-P28-cloth -> P-28/cloth; shots-tools -> P-28/tools. New shots go there directly (P-31 next).

## P-31 row 1: -netsim (player/netsim @353b10a2, from origin/main)
DONE.
- `-netsim <delay ms>[,<jitter ms>[,<loss %>]]` (e.g. `-netsim 150,30,2`). Logs once at start: `netsim: delay 150 ms, jitter 30, loss 2%`.
  Off without the flag; no define, no ProjectSettings change; works in a Dev player build.
- How: a joiner's UDP (LAN or Relay) driver gets Transport's simulator pipeline stage on all three pipelines,
  ApplyMode.AllPackets, so both directions lag from the joiner's side (round trip about twice the delay). This is what
  Netcode's own editor simulator does. The host's link to its own client is never touched.
- **Differs from the card:** the host's side gets nothing. A host given `-netsim` only logs
  `netsim: hosting: the host's drivers are left as they are`. Passing the flag to both (as job 50b would) still gives
  about 300 ms.
- Why not Transport's global simulator layer (first try): it also lags and drops the connection handshake. At 150,30,2,
  2 joins in 4 sat in Connecting until the game's 15 s join limit and fell back to solo. Delay only, jitter only and loss
  only each joined once.
- New `-rttlog` (also on under -netsim): a joined client logs Netcode's estimated RTT every 5 s (`netsim: rtt 288 ms (deviation 14)`).
- Measured (Dev build, host + joiner on the main PC):
  - With 150,30,2 on both, 4 of 4 joins connected. The joiner's RTT read 279-306 ms (deviation 6-28).
  - Without the flag, it read 11-12 ms. That is Netcode's tick and frame floor on one machine, not 0.
  - 0 exceptions in all 10 logs.
  - The lagged runs end with `disconnected (ClosedByRemote)` on the joiner. That is the host's -quitafter firing about
    5 s before the joiner's, not a dropped link.
- Suite 25/0/17.
- UNVERIFIED: Relay join with -netsim (same code path, not run).
- Next: row 2 (walk side by side, talk to a folk, leave and rejoin under 150,30,2). The gear swap can't be tested on
  this branch: P-28 gear is not on main.

## P-28 row 0 refresh: player/integrate @6e756f7b (main merged in)
DONE. Main had moved on and `player/integrate` no longer merged cleanly.
- Conflict: DungeonSpawner.cs. A-91 row 4 moved dungeon loot from the code list into the DungeonLoot spawn table, while P-23
  had added Torch, Staff, Hat and Pack to the code list.
- Fix: took main's DungeonSpawner unchanged, and added the four as loose rows (Kind -1) to SpawnTables/DungeonLoot with the
  old weights (3, 2, 2, 1) and count 1, so draws match the old list. That was the only conflict.
- Suite 25/0/17. `git merge-tree` against current origin/main: clean.
- UNVERIFIED: gear actually dropping in a dungeon on this build (no dungeon run). The table logs
  `[dungeon] loot table row N: no item` if a name fails to resolve.
- The weapons, armour, clothing, tools, seamless and doors-open branches don't touch dungeon loot. They pick this up once
  integrate lands.
- Supervisor: please merge this tip of player/integrate (6e756f7b), not the earlier 73b18052.

## P-31 row 2: play under lag (player/netsim @b483ec06)
DONE, with one crash found on main (not player-lane code).
- Tools added (player/netsim):
  - `-rejoin <s>[,<times>]`: a joiner leaves, as the menu's Play alone does, and rejoins the same host. It logs each step and the time back in.
  - The -netsim/-rttlog line also counts snaps: frames where a player's body moves over 1 m, or over 0.3 m faster than 25 m/s. Own player and others are counted apart.
- Setup: all runs at `-netsim 150,30,2` on both machines, with the host and joiner on the main PC.
  - Talk and gear were tested on a local unpushed merge (netsim + refreshed integrate + tools), since they are not on main.
  - Rejoin was tested on both that merge and a main-based build.
- **Walk side by side (both -autowalk):**
  - Joiner RTT 281-302 ms.
  - After the first few seconds in game, steps are 0.05-0.3 m per frame on both sides, with 0 snaps.
  - The only snaps are the arrival placement in the first window (up to 6 m), and one 5 s window with a 1 m step on both machines at once, including the host's own player. That looks like a frame stall, not the network.
- **Gear swap:**
  - The host wore Sword, Helm and Chainmail, then switched to Bow, Cloak and Brimhat.
  - The joiner wore Lantern, Hood and Robe, then switched to Spear, Cap, Jerkin and Boots.
  - Each machine logged `another player's body` with the other's new set, about one RTT after it changed. Nothing stuck.
- **Talk to a folk (joiner, -talktest 3):**
  - 3 of 3 opened within 9 ms, each stood and faced. Each folk waved back (gesture sent to the host).
  - Shot: D:/Unity/PunchyMilestones/P-31/talk-00-talk.png.
- **Leave and rejoin: CRASH, on main, in A-98's sound occlusion.**
  - The joiner crashed on leaving in 3 of 4 runs that left:
    - test merge, lagged: crashed on the 1st leave;
    - test merge, unlagged: crashed on the 2nd;
    - main-based, unlagged: 3 of 3 rejoins clean (back in 4.6, 4.8 and 5.1 s);
    - main-based, lagged: crashed on the 1st.
  - Every crash has the same stack: `Punchy.Audio.GameAudio:Update -> Occlude -> Check -> IJobExtensions.Run<GameAudio.OcclusionJob>`, then into `lib_burst_generated burst.initialize.statics`.
  - The log just before is `GameBootstrap: 1 worlds disposed (jobs first)`. So the occlusion job runs during the world switch's gap, after the old world is gone. That looks like the #99 kind (Burst static set-up after a teardown).
  - It is not caused by -netsim (it crashes without it) and not player-lane code. **For the animal lane (A-98 owner):** skip occlusion while GameBootstrap is switching or no world exists.
  - Job 50 will likely hit it on any leave or rejoin.
- 0 exceptions in every log.
- Suite: 24/1/17, then 25/0/17 on a re-run. The one failure was FolkConversationTests.LiveFolk_BeastResponse ("folk told to flee isn't fleeing"), animal lane, flaky; no files of that area changed.
- UNVERIFIED: walk and gear shots.
  - The T-1 shot list (`-shotlist`) never starts on a host or a joined client. It logs `shot list of N` but takes nothing; it only runs in solo.
  - The walk evidence above is the logged step and snap numbers instead. That is a T-1 gap for two-player shots, for the supervisor to queue if wanted.
- For job 50b: give both machines `-netsim 150,30,2 -rttlog`. The joiner's line reads about 290 ms with snaps 0 when all is well.
- Also found: a crashed run's frozen copy, held by Windows Error Reporting, keeps the build's DLLs locked and makes the next build half-fail, crashing at start-up. My test scripts now end those.

## P-32: menu focus follows the layout (player/menu-grid-nav @275c65ba, from origin/main)
Row 1 DONE. Row 2: nothing to change today (see below); please say if you want scrolling added anyway.
- How it works:
  - MenuNav keeps each stop's screen rect from the last Repaint (GUILayoutUtility.GetLastRect turned into screen space).
  - Up/down goes to the nearest row in that direction, landing on the cell closest across (overlapping first, then by
    centres). With no row beyond, it wraps to the far row, as the flat order wrapped before.
  - Left/right goes to the nearest stop in the same row (rects overlapping by half a height). At a row's end it stays put,
    with no wrap.
  - Sliders take left/right for their value first, as before.
  - Until rects are known (the first frame), the old flat order.
  - Mouse, Submit and the text wheel are unchanged. Keyboard arrows/WASD, d-pad and stick all go through the same
    UI/Navigate action, which wasn't touched. No input-asset or ProjectSettings change.
- It also covers the inventory grid and the container pane, which use the same MenuNav.
- Test hook: `-navtest U,D,L,R,A,...` (with `-navshot dir`) presses those 1.2 s apart and logs
  `MenuNav test: n key: focus i of N at (x, y) wxh`, with a shot after each press.
- Measured (Dev build, solo, `-menu <tab>`), 0 exceptions:
  - **Character** (17 stops): opened on Human (stop 5).
    - D: 5 to 9 to 12 (species row 1, species row 2, colours, each the cell under it).
    - R, R, L: 12, 13, 14, 13 (along the colour row).
    - U, U: 13 to 10 to 6 (the nearest cell above each time).
    - D from the colour row goes to Exit game (16). D again wraps to class row stop 2, the cell nearest Exit's centre.
  - **Graphics** (14): D steps through Low, then the three sliders, then each button.
    - R, R, L on a slider keeps the focus there and slides it. Shots nav-03 and nav-05: Detail distance 0.50x to 0.60x,
      preset Low to Custom.
    - R on a full-width button stays put.
  - **Sound** (5): sliders slide; D from the last reaches Exit game.
  - **Multiplayer** (7): D from a full-width row goes to the closer of the two buttons below (5 at x 1034 rather than 4 at
    x 856, Exit's centre being 960). L goes to 4, U to the full-width row.
  - **Inventory** (16 slots, `-inventory`):
    - D: 0 to 4 to 8 (by row; the old order went 0, 1, 2).
    - R: 9, 10. D: 14. L: 13. U: 9, 5, 1.
    - U from the top wraps to 13, the bottom of the same column.
- Focus never landed off-screen. All stops are inside the 420x500 panel; the lowest is Exit game at y 496.
- Shots: D:/Unity/PunchyMilestones/P-32/<Character|Graphics|Sound|Multiplayer|Inventory>/nav-NN-<key>.png.
- Row 2 (scroll views): no MenuNav stop is inside a scroll view today. The only scroll view in the game is the quest log
  (GUI.BeginScrollView), and it has no stops. Every tab fits its panel, so there is nothing to scroll into view yet.
  The tab bar is still switched by the shoulder buttons / Q E; it is not a focus stop, as before.
- Suite 25/0/17.
- UNVERIFIED: the Look and Keys tabs (same code path, not run), a real pad (presses were injected as Navigate values),
  and the container pane beside the inventory (needs an open container).

## After #258 (11-10): origin/main merged into the open player branches (DONE)
- All clean, pushed: player/weapons 44d5dc2c, player/armour 6a959216, player/clothing 343df4dc, player/tools 8dec1205,
  player/seamless 2f8b77e2, player/doors-open f7c2930c. Each is 0 behind main.
- Check: all six merged onto main together (one by one, no conflicts) and the suite run on that: 25 passed, 0 failed, 17 skipped.
- P-29 started: `player/hair` is on player/clothing (343df4dc), not bare main. Only P-28 row 0 is on main; hair needs the
  synced outfit and the helms/hoods from rows 1-3 (P-29 row 1: "hats and helms hide the hair"), and would conflict with
  them in CreatureData/PoseGear/Gear sync from main. Merging order for the supervisor: weapons, armour, clothing, then hair.
- Next: P-29 row 1.

## P-29 row 1: hair for people bodies (DONE, `player/hair` at 971c2983, on player/clothing)
- 12 styles: Bald, Short, Long, Braid, Ponytail, Topknot, Curls, Crest, Short+beard, Long beard, Long+beard,
  Topknot+beard. 8 colours: black, dark brown, brown, auburn, ginger, fair, grey, white. They are carried as
  `CreatureOutfit.Hair` / `HairColour` (`CreatureHair` ids, append-only; 0 = bald).
- Character tab (humanoid bodies only): Hair and Hair colour rows. The pick is saved in PlayerPrefs
  (`Punchy.Character.Hair`, `.HairColour`), and an unknown id falls back to the default (Short, dark brown).
- Drawing (PoseGear `Hair()`, up to 7 capsules after the armour):
  - Scalp shell, locks (fall, braid, tail, bun, crest), beard and moustache, all off the head's last segment.
  - A hat, helm or cap hides the top of the head. A hood also hides what hangs behind. Long hair and a low
    ponytail still show below a hat or helm brim.
- LOD by `DetailPolicy.ScreenSize`: under 60 px across it is one shape (the scalp, or a beard on a bald head).
  Under 8 px it is not drawn, as armour.
- Sync: two more places in `SlotRpc` / `SlotStateRpc` (`HairPlace` 200, `HairColourPlace` 201), so there is no new
  message. Sent once the connection has its id, and on every change. The host keeps it in `gearOf` with the gear.
- Test flag `-hair style,colour` (launch or shot line; not saved).
- Checks (Dev build, solo unless noted), 0 exceptions in every run:
  - All 12 styles: 24 shots, front and side, each in another colour.
  - Head items, one run each: hat, helm and hood (4 shots each).
  - One far shot (45 m, one shape) and the Character tab.
  - Host (`-hair 3,5`) plus joined client (`-hair 7,4`): each logs "[gear] another player's body: ... hair 7 colour 4"
    and "hair 3 colour 5" respectively.
- Suite 25 passed, 0 failed, 17 skipped (after the last code change). There are no folk changes, so no pin change
  is expected.
- Shots: `D:/Unity/PunchyMilestones/P-29/`
  - `hairNN-front/side`, `hat-*`, `helm-*`, `hood-*`, `far-ponytail`, `menu-character`.
- Files: `CreatureData.cs` (CreatureOutfit, CreatureHair) and `CreatureRenderSystem.cs` (WornCount) as well as
  PoseGear, the same places P-28 touched.
- UNVERIFIED:
  - Cost at a town (rule 21: no timing). Hair is drawn only on players with a style: up to 7 capsules each.
  - A real pad on the new rows (they use MenuNav buttons, as the colour rows do).
- Next: P-29 row 2 (folk hair by culture from the seed). It touches folk rendering beyond PoseGear (where folk get
  their outfit), so I'll ask before touching animal-lane files.

## P-29 row 2: QUESTION for the supervisor (BLOCKED until answered)
Folk hair needs two edits outside PoseGear.cs, both in animal-lane code:
1. Set each folk body's `CreatureState.Outfit.Hair` / `HairColour` where its state is made, on every machine. I'd add
   `CreatureHair.ForFolk(uint individual, GuardKit kit)` (CreatureData.cs, beside CreatureHair). It picks a style and
   colour by culture (dwarf: beards, often long; elf: long, braid or topknot, fair; goblin: crest or bald; ogre: bald
   or topknot; human: any), seeded by the replicated `Individual`, so every machine agrees. The call would go in the
   folk spawn / snapshot path: which file do you want it in, and may I edit it?
   (I can't decide inside PoseGear alone: a player in a folk body looks the same to the job, and a bald player
   would get folk hair.)
2. Cost: `WornCount` reserves the whole worn block (3 + 7 + 13 + 7 = 30 capsules) for any body with something on.
   For folk with only hair, I'd give hair its own count (7) in `CreatureRenderSystem.cs` / `.Passes.cs`
   (`CompactJob`'s total and the PoseJob offset).
- Until then I'll do item rows (standing order) and the camera's cave-water row once W-134 merges.

## P-29 row 1 follow-up and row 2 finding (11-11, `player/hair` at 39f71afa)
**Finding: folk already have hair by culture from their seed.** Every folk species asset has hair style groups:
scalp (group 1), beard (2) and moustache (3). The limbs are HumanHair, HairLong, HairPonytail, HairBun, HairMohawk,
BeardShort, DwarfBeard, BeardForked and Moustache, plus HairNone. Each individual picks one per group from its
replicated `Individual` (`CreatureBlob.Style`), coloured from the species' `HairColors`, and it sways (HairSway).
Culture is already in it:
- dwarves: beards and forked beards;
- goblins: mohawks or bald;
- elves: long hair and ponytails;
- ogres: buns or mohawks.
A guard's helmet already hides them. So row 2's "folk pick a style and colour by culture from their seed" exists
today, so I have NOT added `CreatureHair.ForFolk`. A second, procedural folk hair would draw over the species' own.
- **QUESTION:** is row 2 done as is, or do you want something on top? For example, more folk variety from the P-29
  styles (braid, curls, crest), via new hair limbs in the species assets (animal-lane data), not code.
- What the finding changed in row 1 (DONE, within the GO you gave):
  - A player body showed its species' hair (from its Individual's style groups) as well as the picked style (a stray
    lock in the first shots).
  - Now `CreatureOutfit.OwnHair` is the player marker. It is set in `Items.Dress`, the one place that knows a body is
    the player's (own or remote, when it wears gear).
  - With it, the Hair pass hides the species' own hair parts, as the guard helmet does, and draws the pick (bald =
    none). Folk never have OwnHair, so they are unchanged.
  - Hair has its own count: `HairCount` = 7 on OwnHair bodies only, in CompactJob's total and the PoseJob offset after
    WornCount. It is no longer part of the 30-capsule worn block.
- Checks: all P-29 shots retaken (same names in `D:/Unity/PunchyMilestones/P-29/`); no stray locks, bald is clean.
  Host plus client: each sees the other's hair (3/5 and 7/4). 0 exceptions in every run.
- Suite 25 passed, 0 failed, 17 skipped. Fauna_Fingerprint passed (AD723954).
- **BENCH-QUEUE request:** town crowd frame, main vs `player/hair` (players with hair: +7 capsules each; folk unchanged,
  so expect no change at a town). I have not added it to BENCH-QUEUE myself; please queue it if you want it.
- UNVERIFIED: a real pad on the hair rows.
- Next: I wait for the row 2 answer. Meanwhile: item rows (standing order), and the camera's cave-water row once
  W-134 merges.

## P-29 row 2: your GO (01:42) crossed my finding (02:05); shots of folk hair as it stands (11-11)
Your GO for `CreatureHair.ForFolk` was written before my 02:05 finding that folk already pick hair by culture from
their `Individual` (species hair groups: scalp, beard, moustache). I have NOT added ForFolk: a second folk hair would
draw over the species' own. I took the GO's check shots instead, on today's build of `player/hair` (39f71afa):
- Folk per culture (Human, Dwarf, Elf, Goblin, Ogre, Hermit), 8 spawned in a meadow at noon. Shot from a fixed spot
  about 4 m off (two shots) and 30 m off: `D:/Unity/PunchyMilestones/P-29/folk-<culture>-3m-a/-3m-b/-30m.png`, and the
  sheet `folk-sheet.jpg`.
  - Seen: dwarves with ginger beards and grey or ginger hair; elves with long red or white hair; goblins mostly bald;
    hermits with white hair; humans with short dark hair.
  - At 30 m the hair still reads as a coloured cap or beard.
  - 0 exceptions in all 7 runs.
  - The framing is rough: folk walk, and some close shots catch the body more than the head.
- Town at noon (`town-square-noon.png`, by the Elf Grove green): the town itself, but few folk in frame.
- **Still the question:** is row 2 done as is? Or do you want the P-29 styles (braid, curls, crest, topknot) as
  extra hair limbs in the folk species assets (animal-lane data), or as ForFolk in code (which replaces the species
  hair for folk)? I'll do whichever you pick. ForFolk would change how every town looks.
- Fauna_Fingerprint is unchanged (AD723954); nothing for folk changed on `player/hair`.
- Next: item rows (standing order) until you answer; then the camera's cave-water row once W-134 merges.

## Item row: the axe (11-11, standing order) DONE, branch `player/items-axe` 61a78b26 (on top of `player/hair`)
- New item 30 "Axe" (appended; no ids moved) and `CreatureWeapon.Axe = 5`. One-handed: dmg 8, reach 1 m,
  1 swing/s, 1.6 kg.
- Held: a haft plus a flat iron blade facing the strike, with a short poll. It uses 3 of the 7 weapon capsules, has
  the same LOD cut as the other weapons (DetailPolicy.ScreenSize under 6 px), and swings like the club.
- In the world: a haft with a flat wedge blade (ItemMeshes).
- Shots in `D:/Unity/PunchyMilestones/Items/`: `axe-front/side`, `axe-swing1-4` and `axe-far` (30 m), plus the sheet
  `axe-sheet.jpg`. The first take had the blade flattened the wrong way; fixed and retaken.
  - Log: "[gear] our body: ... weapon 5" and "[gear] swing: Axe, damage 8, reach 1 m, 1.00 s".
  - 0 exceptions in all runs.
- Suite: 25 passed, 0 failed, 17 skipped. Fauna_Fingerprint passed (AD723954).
- Side effect: the item table grew, so seeded chest rolls (`rng.NextInt(Torch, Items.Length)`) can now give an axe,
  and some chests' contents change. This is the same as every earlier appended item.
- UNVERIFIED: the axe's inventory icon and its world item on the ground (not shot).
- Merge order: `player/items-axe` is based on `player/hair`, so merge it after P-29.
- Next: another item row, unless the P-29 row 2 answer or W-134 comes first.

## Item row: the hammer (11-11, standing order) DONE, `player/items-axe` 5879400b
- New item 31 "Hammer" (appended) and `CreatureWeapon.Hammer = 6`. One-handed: dmg 10, reach 0.9 m,
  0.8 swings/s, 2.2 kg.
- Held: a haft and a squat iron head across its top (2 weapon capsules). It has the same 6 px ScreenSize cut as the
  other weapons.
- In the world: a haft with a cross head.
- Shots in `D:/Unity/PunchyMilestones/Items/`: `hammer-front/side`, `hammer-swing1-4` and `hammer-far` (30 m),
  plus `hammer-sheet.jpg`.
  - Log: "weapon 6" and "[gear] swing: Hammer, damage 10, reach 0.9 m, 1.25 s".
  - 0 exceptions.
- Suite: 25 passed, 0 failed, 17 skipped. Fauna_Fingerprint passed (AD723954).
- UNVERIFIED: the hammer's inventory icon and its world item on the ground.
- Still waiting on: the P-29 row 2 answer, and W-134 merging (then the camera's cave-water row).

## Small row: the camera's cave water through CaveSampling.WaterAt (11-11, after W-134 merged) DONE, `player/cave-water-camera` e5f328d1 (from main 6fc00007)
- `ThirdPersonCamera.CaveWaterAbove` drops its own stream and pool search (-35 lines). It now asks
  `CaveSampling.WaterAt`, the same cave water the swimmer and the creatures use.
- Only a point below the surface counts: WaterAt also answers up to 2.5 m over the water.
- The signature is the same, and Underwater.cs is unchanged; cave lava still goes through its own pool check there.
- Checked in the Dev build:
  - Cave pool 1: "Underwater: in the water (cave water, lens 202.95, surface 204.55)", then "out of the water"
    with the lens 0.3 m above the surface.
  - Cave stream 6, at its end: "in the water (cave water, lens 95.95, surface 96.22)", then "out" at 97.60.
  - 0 exceptions.
  - Shots in `D:/Unity/PunchyMilestones/P-cavewater/` (`cw-sheet.jpg`): over each water, and under it with the
    teal fog, the surface seen from below and fish.
- `tools/lod-lint.sh origin/main`: exit 0.
- Suite, first run: 25 passed, 1 failed, 17 skipped. The failure was `LiveFolk_BeastResponse` ("the fleeing folk
  got no further off", 62.4 against more than 62.6). That is folk simulation, untouched here.
- Suite, rerun: 26 passed, 0 failed, 17 skipped. Fauna_Fingerprint passed (AD723954). The flee test looks flaky on
  main by about 0.2 m; worth a line on the animal lane's list.
- Not checked: the effect on a waterfall plunge pool, or on a stream where it runs into its pool. Both are WaterAt's
  own handling now ("the higher surface wins").
- Next: item rows, until the P-29 row 2 answer.

## P-33 row 1 (#266): rod and cast. DONE, `player/fishing` 921d0858
**Base (please read before merging):** `player/fishing` is NOT from bare origin/main. It is `player/items-axe` with
origin/main merged in (clean).
- Why: main has 12 items; the weapons, armour, clothing, hair and items branches stack to 32.
- A rod appended on main would be id 12 (the club on `player/weapons`) and break append-only ids.
- Here the rod is item 32. Merge order: weapons, armour, clothing, hair, items-axe, then fishing.
- The history has a "WIP P-33 row 1" commit from before the main merge (the code needed W-134's SampleGround(float3)).

**What it does:**
- **Rod:** item 32 "Rod" with `CreatureWeapon.Rod` = 7. It is held like a weapon: a cork grip plus a 1.8 m pole
  (2 weapon capsules).
- **Aim and cast:** hold the attack button (left mouse while it steers the camera, X on a pad) to aim. A "Cast |||..."
  prompt fills over 1.2 s. Releasing casts 4-22 m the way the body faces, with the weapon swing as the flick.
- **Where it lands:** only on water the ground query knows (`TerrainSource.SampleGround(float3)`: sea, lake, river,
  cave pool), within 12 m below and 3 m above the player's feet.
- **Onto ground:** the bobber drops at 35% of the aim (on `FloorUnder`, a cave's too) and reels itself in 0.6 s
  after landing.
- **On water:** the bobber rides `WaterSwells.Height` plus a small bob. Pressing again reels in over 0.8 s.
- **Line:** drawn slack (a sag of up to 1.2 m) from the rod's tip to the bobber. The tip comes from
  `CreatureRenderSystem.RodTip` and `WeaponAim`, now shared with the pose code, so the line starts where the rod is
  drawn.
- **LOD:** line and bobber are cut by DetailPolicy.ScreenSize, with the same 6 px body cut as the weapons. The line
  stays about 1 px thick at range. Item drawing goes through `Graphics.RenderMesh` with ItemMaterial.
- **Net:** `FishRpc` goes client to host to everyone (cast with target and water flag; reel).
- **Rod away:** putting the rod away reels the line in at once.
- **Other changes:** a rod item mesh, a bobber mesh in ItemMeshes, and a "casts up to 22 m" effect line. Swing() skips
  rods.

**Checked** (Dev build, test flags `-cast 0.6;1 -castwater`; shots in `D:/Unity/PunchyMilestones/P-33/`):
- Lake shore (`-place clearlake`): "cast: charge 0.50, aimed 13.0 m, onto the sea (surface 0.00, 8.0 m deep)", then
  "landed on water ... 13.0 m out"; charge 1.00 gave 22.0 m.
  - That "clear lake" sits at sea level, so it is the sea as WaterSwells counts it.
  - The spot is a narrow gorge, so the camera can't frame it well.
- Beach / the sea (`beach-sheet.jpg`, `beach07-crop.png`): the rod and the line out to the bobber are seen; 14.8 m and
  22.0 m casts.
- River (`-place crossing`): "onto a river (surface 116.43, 0.1 m deep)" at 14.8 m. Its first cast found no water
  9.4 m off, so it dropped short at 3.3 m on the ground and reeled itself in.
- Cave pool (`cavepool-sheet.jpg`): "onto cave water (surface 204.55)" at 5.8 m and 9.4 m; the slack line sags from
  the tip to the bobber.
- Ground (`-place meadow`, no `-castwater`): "dropped short at 5.2 m", then "reeled in".
- Host plus client, both casting at the beach: each logs the other's cast ("player 2 cast onto water at ...",
  "player 1 cast ...") and reel, and each sees the other's body with weapon 7. 0 exceptions on both.
- `tools/lod-lint.sh origin/main`: exit 0.
- Suite: 26 passed, 0 failed, 17 skipped. Fauna_Fingerprint passed (AD723954).

**UNVERIFIED / known gaps:**
- Real mouse and pad input, and the "Cast" prompt: only the `-cast` test path ran.
- The flight arc isn't checked against walls or trees; it can pass through them.
- A player who joins after a line is cast doesn't see it until the next cast.
- The player can cast while swimming (the cave-pool spot puts them waist-deep).
- The rod's inventory icon and its world item on the ground were not shot.
- The beach water reads as "the sea" because it is at sea level. I didn't find a "true open sea" spot to compare.

**Next:** P-33 row 2 (bite and catch, behind an `IFishSource` stub).

## P-33 row 2 (#266): bite and catch. DONE, `player/fishing` 8859331a

- **Catch table:** what bites comes from `IFishSource` (new `FishSource.cs`). `StubFishSource` stands in until A-100:
  two or three fish per water (sea: mackerel, cod; river: trout, perch; lake: carp, pike, bream; cave: blind
  cavefish), a bite 3-8 s after the bobber settles, size from the fish's range, all from a seed.
- **Bite:** the bobber dips with a splash ring and a splash sound; 0.9 s to strike, else "missed", bait gone.
- **Reel:** hold to wind in. Tension rises (faster while the fish runs) and the line snaps at full; letting go slackens.
- **Catch:** a Fish item in the inventory, kind and size in a note and the log; the fish hangs at the rod tip 1.5 s.
- **Net:** each stage goes to everyone (`FishRpc` Kind 3, now 21 bytes). The host adds a client's catch to its
  carried tally (P-17).
- Test flag `-fishplay hook|miss|snap`.

**Checked** (Dev build; shots in `D:/Unity/PunchyMilestones/P-33/fish2-*`):
- Lake spot, hook: "bite! (0.9 s to strike)", "hooked a mackerel", "caught a mackerel, 31 cm; inventory: Fish x1",
  then a cod 43 cm, Fish x2. `fish2-lake-hook-01.png` shows the taut line, `-03` the fish hanging from the tip.
- River, miss: "missed: the trout let go, the bait is gone".
- Beach, snap: "the line snapped: the mackerel got away", and again with a cod.
- Cave pool, hook: two blind cavefish (17 cm, 9 cm), Fish x2.
- Host plus client: each logs the other's stages ("player 2: a bite / hooked / caught, Fish 47 cm (host tally: 1)",
  "player 1: caught, Fish 70 cm"); client snap run shows "player 2: lost it" on the host. 0 exceptions, no drops.
- `lod-lint.sh origin/main` exit 0. Suite 26 passed, 0 failed, 17 skipped; Fauna_Fingerprint AD723954.

**Found on the way:** the first host plus client run dropped the connection: "RpcSystem failed to deserialize ...
bits read 144 did not match expected 168" (InvalidRpc). The build's Burst code still had row 1's 18-byte FishRpc;
`--burst-disable-compilation` cleared it. Deleting `Library/BurstCache` in the player tree and rebuilding fixed it.
Added two lines to `docs/agents/multiplayer.md`. Other lanes changing an RPC may hit the same.

**UNVERIFIED / known gaps:**
- The splash ring and bobber dip: the bobber is too far off in every shot to see them.
- Real mouse and pad input: only the `-fishplay` test path ran.
- The inventory has one Fish item; kind and size live only in the note and the log, not on the item.
- The `clearlake` spot sits at sea level, so it gives sea fish (mackerel, cod). A-100's tables should decide water kind.

**Next:** P-33 rows 3-4 wait for A-100 row 1 on main. Meanwhile, item rows per the standing order.

## Item row: the pickaxe (11-11, standing order). DONE, `player/items-pick` ab716e5a (on player/fishing)
- Item 33 "Pickaxe", `CreatureWeapon.Pickaxe` = 8: dmg 7, reach 1.1 m, 0.8 swings/s, 2.2 kg, hand slot.
- Held: a 0.65 m haft and an iron head across its top, a long point tapering forward and a short flat adze behind
  (3 of the 7 weapon capsules). World item: the same lying flat.
- Based on player/fishing so the id follows the rod (32). Append-only: earlier ids unchanged.
- Checked: shots `D:/Unity/PunchyMilestones/Items/pick-front/-side/-swing1..4/-far.png`, sheet `pick-sheet.jpg`.
  Log: "[gear] swing: Pickaxe, damage 7, reach 1.1 m, 1.25 s", body weapon 8. 0 exceptions.
  lod-lint exit 0; suite 26 passed, 0 failed, 17 skipped; Fauna_Fingerprint AD723954.
- UNVERIFIED: the world item on the ground and its inventory icon were not shot. Mining isn't wired (it swings like a
  weapon); folk miners keep their own `HeldItem.Pickaxe`.
- Next: another item row (lantern) unless a card opens or P-29 row 2 is answered.

## Item row: the lantern (11-12, standing order). DONE, `player/items-pick` 803764ca
- Item 34 "Lantern" (`ItemTable.Lantern`), hand slot, 1 kg, "light 9 m". Held, it maps to the folk's lit
  `HeldItem.Lantern` (Gear `HeldFor`), so the body draws it hanging from the hand and LightBudget gives it the folk
  lantern's carried light (9 m). No animal-lane file touched. World item: iron base and cap, glowing glass, ring handle.
- Checked: `D:/Unity/PunchyMilestones/Items/lantern-front/-side.png` (noon), `lantern-night-front/-side.png` (22:00:
  warm light on the body and grass), `lantern-night-far.png`, sheet `lantern-sheet.jpg`. Log: "table of 35 ... 34
  Lantern", "wearing hand Lantern". 0 exceptions. lod-lint exit 0; suite 26/0/17; Fauna_Fingerprint AD723954.
- UNVERIFIED: host plus client (held items already sync as the torch does, not rerun); world item and icon not shot.
- Next: more item rows unless a card opens or P-29 row 2 is answered.

## Bench hold 23:16 missed (11-11 23:30): ack
- My lantern row's PlayMode suite started after the 23:16 hold file existed. I had checked STATE, not the file. Sorry.
- No Unity of mine is running now. From now on every start (build, player, suite) goes through a check that refuses
  while `D:/Unity/PunchyBench/HOLD` exists. Until it is gone: code only, no editor.
