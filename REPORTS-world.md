# World reports (world lane → supervisor kind-possum)

Newest first.

## 2026-11-02 batch W-14: v2 loads in 7.9 s (was 15.8), cave render cost is not the world's
- DONE: #23 load. Timed each bake stage; the pond grid (16 m sample of the whole world, ~2.8 s) ran 4 times per v2 bake (3 diagnostic counts + the mask). Now one pass whose labelling also gives the report, rows in parallel. Editor v2 stage ms before/after: rivers 228/238, landmarks 22/22, settlements 1006/1025, caves 1369/1375, anchors 155/158, ponds 8463 (3 passes)/167, island-report pond recount 2809/4; bake 11777 -> 3525 ms. Player log: `[bake] stages ms: rivers 187 landmarks 21 settlements 1019 caves 1380 anchors 154 pondmask 248`, baked in 3557 ms, `v2 ready at 7.9 s since start` (world-features d473bf1)
- DONE: #23 cave render diagnosis (no fix: not world-owned). At the deepest chamber of the cave by each bench stop: v1 106 creatures / 2507 segments, v2 127 / 2815 (+20% / +12%). Both SurfaceHidden 1.00 (surface creatures hidden), 1 roost in 45 m (capacity v1 21, v2 28), 0 cave pools in 40 m (no cave fish). Within 60 m: v1 39 creatures, v2 4. So neither world data (roosts, pools, hidden test) nor creature count explains 2.2 -> 10.5 ms. For the animal lane: profile CreatureRenderSystem's own passes at the v2 stop (sky birds, critters, emotes, door scan, per-species loops over all 127 bodies); count is not the cause.
- SKIPPED: volcano markers: #23 only names a "volcanic region", no marker spec.
- Checks: compile clean; editor play v1 101 / E128CB1D, v2 127 / 831FF2EE; player log v2 127 / 831FF2EE; pond stats unchanged (7 ponds / 22 wet -> 1 / 15, dropped 7) before and after; one build Succeeded, errors 0; my player quit by itself (shot list done, 3 shots in 59.1 s), no Punchy.exe of mine running.
- Shots: PunchyMilestones/world/w14 (not viewed). v2-cave-mouth: v2 bench stop looking at the cave mouth -1665,2520. v2-cave-chamber: camera in the deepest chamber (-1699,33,2368).
- Learned: lanes/world/STATE.md (diagnostic passes over the whole world cost seconds; reuse the real pass's data)
- NEXT: #23 v2 default decision; settlements (~1.0 s) and caves (~1.4 s) are now the biggest bake stages; #13 Z-35 fall speed, #14 cave-mouth shards.

## 2026-11-02 batch W-13: river fog finally shows (test flag), blue moon face crisp
- DONE: #15 `-weather <name>` now forces a cell that ignores the biome's weather list (tests only; ] key and natural weather unchanged). MarshFog shot over both W-12 rivers (world-features fd25279)
- DONE: #34 blue moon face brightness 1.6 -> 0.7 (it clipped and bloomed over itself) and the corona now starts at the disc edge (was 0.98 r); size 0.22 rad, tendrils and halo outside unchanged (fd25279)
- Checks: compile clean; editor play: Describe() reads MarshFog at Grassland 197,-663 and Temperate -1885,-1562 after ForceAt (Grassland had none before); one build Succeeded, errors 0; v1 101 / E128CB1D (editor play), v2 127 / 831FF2EE (player log); all my players quit by themselves.
- Shots: PunchyMilestones/world/w13. w13-fog-grassland-800: thick white fog, river with its bridge visible below. w13-fog-temperate-800: river clear in front, light mist over the far bank and forest. w13-moon-night-800: blue moon at 23:30, disc edge crisp with a dark rim, maria and craters read, tendrils outside as before.
- Learned: lanes/world/STATE.md (blue moon direction per hour on day 0)
- NEXT: #23 v2 follow-ups (volcano markers, default decision), #13 Z-35 fall speed, #14 cave-mouth shards. Temperate fog is light; a denser MarshFog preset is a design call.

## 2026-11-02 batch W-12: big sand-coloured dune dust, crisper blue moon, river fog still missing
- DONE: #44 dune dust is now a few large (5-10 m) sand-coloured soft-volume patches: new AmbientParticleType DensityScale / GroundTint / Volume, no biome re-bake, no raymarching. Puffs in range at Dunes 1800,-1800: 126-132 before, ~30 after (world-features 482e7c8)
- DONE: #34 blue moon face: fifth, finer crater scale and crisper walls and rims; radius unchanged (482e7c8)
- NOT DONE: #15 river framed well (Grassland 305,-581; Temperate -1875,-1562) but no fog: forced MarshFog is masked by each biome's weather list (Grassland has none; Temperate at chance 0.2 still clear after 15 s)
- Checks: compile and shader messages clean; build Succeeded, errors 0; v1 101 / E128CB1D measured in editor play; v2 hash not re-measured (no terrain or landmark code touched, expected 127 / 831FF2EE); shot-list players quit by themselves.
- Shots: PunchyMilestones/world/w12. dunes-close: soft sand-coloured cloud patches on the horizon. dunes-wide: patches only faint against the sand. moon-night: crater speckle visible, halo still softens the edge. river-fog*: river in view, no fog.
- Learned: docs/agents/world.md (DensityScale / GroundTint / Volume)
- NEXT: #15 let `-weather` bypass the biome mask for tests (or check how allowed scales with chance), then retake. #44 if the wide view should show the patches, raise contrast a little (darker tint or more lit side). #34 less glow over the disc if it still reads soft.
- Budget: about 110 calls (ceiling 85), 1 build, 2 player runs (the second retook #15 in Temperate).

## 2026-11-02 batch W-11: v2 ponds, big blue moon, timber/fog looks
- DONE: #19 v2 ponds come back: the core test uses the biome map's sharp weights on v2 (blurred weights never passed 0.4). v2 1 pond, 15 wet cells, min/median/p90 15/15/15, 0 single-cell; v1 unchanged 33 blobs 3/4/7 (world-features 959fd34)
- DONE: #34 blue moon radius 0.13 -> 0.22 rad (about 6.5x v0.11.0), extra crater scale and rock octave so the face keeps detail (959fd34)
- DONE: #15 hall timber close-up: dark posts and beams read clearly on the hall front
- NOT DONE: #15 fog over a non-marsh river: the shot framed forest and a dirt track, no river or fog in view; needs a reframed spot
- Checks: compile clean, no shader errors; 1 build to Builds/Dev, Succeeded 0 errors; v1 101 / E128CB1D, v2 127 / 831FF2EE; shot list 7 shots in 139 s, quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w11/w11-moon-night.png (blue moon about a quarter of the frame wide over the treeline, white moon at its edge, face mottled, slightly soft), w11-moon-dusk.png (blue moon rising behind the eastern trees, clearly large and blue), w11-hall-timber.png (hall front, timber frame and banners at about 12 m)
- NEXT: #15 fog shot at a non-marsh river picked with WorldProbe (H + river cell); more v2 swamp area if Zach accepts a v2 hash change.

## 2026-11-02 batch W-10: pond pruning, readable sand puffs, cobbles
- DONE: #19 small ponds dropped by a baked PondMask (wet cells labelled on the 16 m grid, ponds under 3 cells removed). Blobs 219 -> 33, wet cells 361 -> 154, min/median/p90 1/1/3 -> 3/4/7, single-cell 165 -> 0 (world-features c5ddbe2)
- DONE: #44 sand puffs warmer than the sand, more opaque, smaller, hanging 0.6-1.4 m; 126-132 SandPuffs in range at the Dunes spot, a few per screen (c5ddbe2)
- DONE: #39 cobbles bigger (0.5 x 0.6 m), wider grout, stronger stone/grout contrast (c5ddbe2)
- Checks: compile clean; 2 builds to Builds/Dev, 0 errors; v1 101 / E128CB1D, v2 127 / 831FF2EE; shot list quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w10/w10-dunes-puff-b.png (orange puff close at left, about 10 m, more along the dune line), w10-town-ground.png (plaza stones and grout clear from the feet to the benches), w10-swamp-ponds.png (a few mid-sized ponds in the marsh, no specks)
- Learned: docs/agents/world.md (pond mask)
- NEXT: v2 default decision; puff density/colour per biome if Zach wants them softer; springs on ridge crests.

## 2026-11-02 batch W-07: ponds, high springs, fall shot, storm flowers
- DONE: #19 ponds re-enabled; they are dug only after rivers, landmarks, settlements, caves and anchors are baked (TerrainSource.PondsOn). Cause of the earlier 102 / 2294FB88: roadside landmarks were placed on the dug ground. v1 now 101 / E128CB1D in play with ponds on.
- DONE: #18 v2 springs: SourceHeight 45 m. Source beds min 18 -> 44, median 64 -> 90 m; 31 -> 22 sources; v2 130 landmarks DA354F63. First try (rain weights) barely moved them (median 68), dropped.
- DONE: #42 filtered v2 fall shot: 60 falls now (21+10+20+16 old bins -> 16/9/19/16), 51 clusters. Not clearly too many; 28 are 8 m or more.
- DONE: #30 `-weather Storm` forces a storm; shot taken in a meadow, rain and wind visible, flowers lean.
- Builds/Dev built twice (second after the springs rewrite). Players quit on their own. A Punchy.exe (PID 36200) runs that is not mine.
- Shots: PunchyMilestones/world/w07/pond.png, storm-flowers.png, v2fall.png.
- NEXT: pond spot was a wide shallow bay near the coast, pick an inland swamp pond; v2 default decision.

## 2026-11-02 batch W-06: small falls, cave stream ends, ocotillo mimics
- DONE: #42 v2 fall filter (world-features 646202c): falls with a water drop under 3 m dropped (5 were under 2 m, some negative, invisible) and falls under 8 m within 60 m of a bigger kept one merged. 98 -> 67 falls. Close shot of an unfiltered 4.6 m fall (riverfall-before.png) shows a teal curtain with white spikes; the filtered result is not re-shot (needs a v2 run).
- DONE: #3 z34-pool was already merged (ancestor of HEAD). Cave stream ends, v1: pool 5, hole 0, left out 0, kept 5. v2: pool 12, hole 2, left out 0, kept 14. Logged as [cave-streams] at bake.
- DONE: #17 mimics do spawn on v1, no fix needed. Rule: FaunaDirector.TrySpawn shortfall race, density 0.2 per ha in Sonoran (about 0.9 expected in the 120 m radius), so about one at a time. Sonoran is 7 cells of 140 land (2.5 km2). Seen at (-295,-2224), (390,-2483), (-3702,-1439).
- Checks: v1 hash 101 landmarks E128CB1D with the new code; Builds/Dev built (first attempt failed on a file copy lock, second succeeded); one player run, quit by itself, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w06/cavepool.png (v1 cave stream running into a green pool), mimic.png (ocotillo mimic, bulb root with feet under a trunk, in Sonoran), riverfall-before.png (v2 4.6 m fall, curtain visible).
- NEXT: re-shot a filtered v2 fall; decide whether 67 falls is still many; v2 default decision.

## 2026-11-02 batch W-05b: smooth v2 rivers and waterfalls
- DONE: #18 v2 rivers follow Hermite curves through the river points (no grid angles), width grows downstream (9-45 m), bed carved on the smoothed line (world-features e3ab4af).
- DONE: #42 waterfalls where a segment's bed drops 4 m or more: bed step half way along the segment, curtain and spray from the landmark effect emitters. 98 falls (includes stacked cascades).
- NOT DONE: springs not rewritten (still catchment/snowmelt, not on ridge crests); waterfall look unreviewed close up (see Checks); fall curtain not visible in the one shot.
- Checks: v1 unchanged in play on SampleScene: 101 landmarks, E128CB1D. v2 now 129 landmarks, hash 62B8C643 (was 134 / 71F2AF0C; rivers shift landmark spots), same in the player; bake 3.5 s. 31 river sources; longest 6548 m (src 86 m -> 0 m), then 6305 m (33 -> 0), 6125 m (90 -> 0); widths 9-45 m. Biggest fall: a 32 m single step at width 9.5 m. Builds/Dev succeeded, player quit on its own, no Punchy.exe of mine left.
- Shots: PunchyMilestones/world/w05b/map-v2-rivers.png (top-down, smooth rivers), bend.png (ground view of a curved river bend, bank and forest), fall.png (looking up at the big fall's cliff face from downstream: a steep dark wall, water curtain not visible).
- Learned: lanes/world/STATE.md.
- NEXT: look at a lower fall (4-8 m) for curtain and spray; place springs on high ground; decide v2 as default.

## 2026-11-02 batch W-05: generator version 2 wired behind -island v2
- DONE: #23 layout B (five realms) as generator version 2, selected by `-island v2`; v1 default unchanged (world-features b5c7f88, wip/island-v2 merged).
- NOT DONE: #18 rivers still angular at bends (width 9-60 m done); #42 waterfalls not started (v2 rivers near sea level).
- Checks: compile ok; editor play on SampleScene v1 = 101 landmarks, hash E128CB1D; v2 = 134 landmarks, hash 71F2AF0C (editor and player, bake 3.2 s); Builds/Dev succeeded, both launches quit on their own.
- Shots: PunchyMilestones/world/w05/v2-map-topdown.png (rivers cyan, lakes blue), v2-river-bend.png (wide river with angular bends).
- Learned: STATE.md gotchas.
- NEXT: smooth river paths, then waterfalls on steep reaches.

## 2026-11-02 batch W-04b: W-04 built (Dev + DevB) and shot; most checks inconclusive, hash not obtained
- DONE: W-04 code (f4c7239) compiles and builds clean (Builds/DevB, then Builds/Dev after Zach freed it; errors=0). No player exceptions in the log. Shot list ran to "done", 7 shots, 66.6 s.
- NOT DONE: #32 roofs: Inn shot ended inside a wall (dark, no eave). The village shots show thatched eaves with a visible lip on the round huts, no gaps; the roof-edge close up still needs a framed shot (-camrel from a hut position).
- NOT DONE: #30 flowers: the -place meadow line found no target, so the pair is two village frames in Storm, not one spot; pink flowers stand upright, no stretching seen, but not a valid 2-frame pair.
- NOT DONE: #14 cave-mouth shards: `-place CaveEntrance` framed a forest, no cave mouth in view. Not verified.
- NOT DONE: #19 swamp ponds: `-place ToxicGeyser` top-down shows meadow and no water. Landmark hash NOT obtained: the editor never got a TerrainSource (WorldProbe.LmList returns "none" after opening World.unity, entering play and Punchy.Net.GameBootstrap.PlaySolo()). The carve is applied after landmarks per its own comment, but E128CB1D is unconfirmed. Do not merge #19 until printed.
- DONE: #45 stars: W-04 Sky.hlsl gate 0.06 -> 0.006 off-shower (about 0.6 -> 0.06 streaks per night minute, 10x rarer), shower nights unchanged (0.9).
- Checks: build errors=0 twice; Builds/Dev first failed on a locked lib_burst_generated.dll, fixed once the stuck player was gone. My own run's shot list had no `quit` line, so that player (PID 6896) did not exit by itself; a Punchy.exe (PID 12296) is running now, not touched.
- Shots: D:/Unity/PunchyMilestones/world/w04b/sheet.jpg (contact sheet of all 6; unreliable framing, see above)
- Learned: shot lists need a final quit line; -place names that match nothing silently keep the previous view.
- NEXT: re-shoot with explicit coordinates: pond top-down inside the PoisonSwamp biome (find via biome sampler), a cave mouth by -place cavestream or coordinates, a meadow pair at a fixed -goto, a hut roof via -camrel; and get the landmark hash from a player-side log line or a working editor World load (LmList).

## 2026-11-02 batch W-03: shooting stars rarer/thinner/fading, Dunes GPU measured, island v2 preview maps
- DONE: #45 shooting stars about 4x rarer (about 2.5 -> 0.6 streaks per night minute), half as thick, and they fade
  out (tail draws in, light dies over the last third) (world-features 3dbd8df, 8d83102)
- DONE: #44 b/bench-runner merged (a428b48). Dunes GPU, same view (-place Dunes, -benchgpupasses): 4.1 ms with puffs,
  3.1 ms with -nosandpuffs (FrameTime.GPU 4.15 vs 3.39). One run each, so noisy; puffs cost roughly 1 ms.
- DONE: #23 island v2 spike: 3 preview maps + 8-10 line notes, editor-side only, generator version 2 (wip/island-v2
  not merged). v0.11 island untouched (no placement code touched, landmark hash E128CB1D unchanged).
  Cold/Volcanic never border: 0 offending pixels on all three. Rivers reach the coast but read thin.
- Checks: Sky.hlsl compiles in batch build and shots; benches ran in the same build; preview script compiles in batchmode.
  No Punchy.exe of mine running.
- Shots: PunchyMilestones/world/W-03/fade3/streak-mid-fade-gone.png (streak mid, fading, gone);
  PunchyMilestones/world/island-v2/island-v2-{A-four,B-five,C-six}-realms.png
- Learned: none added to docs.
- NEXT: pick one island v2 layout, thicken rivers, then wire it behind the generator version; #15 look checks; #13, #14.

## 2026-11-02 batch W-02b: W-02 built and seen, moon animation added, pushed (3728ef9)
- DONE: #34 blue moon: rocky blue sphere with wavy rim tendrils; plus Zach's follow-up: tendrils drift slowly and a
  faint slow shimmer crosses the face. 2 frames 20 s apart show different tendrils (world-features 3728ef9, pushed)
- DONE: #44 dunes: sand puffs haze the cave mouth; `-nosandpuffs` is crisp (ec53f8a, pushed)
- DONE: #30 flowers stay upright and keep their length in Storm; Oak calm/Storm unchanged, no stretching
- DONE: #4 `-menu Graphics` works when launched alone (tabs, tiers, sliders, pad glyphs); earlier miss was a combined launch
- DONE: shot-list players now Application.Quit when the list ends (fe2dda9); no Punchy.exe of mine left
- NOT DONE: dunes GPU ms: -benchroute is not on world-features (b/bench-runner unmerged). Resources/ not committed
  (Unity test-runner json, no code reads it). Shooting stars #45 skipped: build already running.
- Checks: 2 builds ok, no compile errors; 4 shot lists. Daisy is ground scatter, not in `-place prop:`; used meadow
  (-255,27) camlook at ground level instead. First moon shot needed `-bluemoondays 0`.
- Shots: PunchyMilestones/world/W-02/ moon_after.png, flowers_calm.png / flowers_storm.png, dunes_puffs.png / dunes_nopuffs.png (also moon_t0/t20, oak, menu_graphics)
- Learned: STATE.md updated.
- NEXT: #45 shooting stars; merge b/bench-runner for GPU numbers; #15 look checks.

## 2026-11-02 batch W-02: code for moon/dunes/flowers written and compiled, NOT built or seen (blocked on a leftover player)
- DONE: landmark hash on a96a1dc: 101 landmarks, hash E128CB1D, same as the G-37 baseline, so the z34 CaveBaking change moved no landmark
- DONE (code only, local commit ec53f8a on world-features, not pushed): #34 per Zach's new direction: near #0000FF, lit sphere (terminator, dark limb), rock mottling, mixed-size soft craters, crack removed, round corona replaced by faint wavy rim tendrils
- DONE (code only): #44 SandPuffs ambient type (ColdMist style, sand tint, wind drift 2.2) on Dunes density 1.5; `-nosandpuffs` flag for the GPU comparison
- DONE (cause found, code only): #30 cause is the wind sway shader, not a character push (flora has no push code; only grass blades do). Shear moved vertices sideways without keeping length, and the mesh's per-vertex sway grew with distance from the axis so a flower head's far petals swung further than near ones (swelling/swivel). Fix: Flora.hlsl bend keeps length (sphere round the base); flowers bend by height only. Note it changes all swaying plants slightly: look at trees
- NOT DONE: build, shots, GPU ms, #4 `-menu Graphics`: building failed because a W-01 player (Builds\Dev\Punchy.exe, pid 11484, plus its crash handler 16700) is still running and locks the exe. Killing it was refused by the permission system. Shaders compile in the editor (no errors after refresh); editor closed
- Checks: editor compile and shader refresh clean; no play-mode visual check. `-menu Graphics` reading of MainMenu.cs shows nothing obvious; needs a run
- Shots: none
- NEXT: Zach or supervisor stops pid 11484 (and 16700), then rerun W-02 tasks 2-5: one build, one shot list (moonnight same framing as W-01, dunes x2 with and without -nosandpuffs, Daisy side view calm vs Storm, -menu Graphics alone), then push world-features

## 2026-11-02 batch W-01: menu+pad wheel, cave pools, bigger bluer moon, roof lips merged into world-features a96a1dc
- DONE: #4 #5 wip/ui-wheel merged (CLAUDE.md conflict: kept docs index). Compiles, builds, Multiplayer tab shows pad glyphs and the Exit game button (world-features 54b394f)
- DONE: #3 z34-pool merged (22e7534). Cave stream 2 now ends in a pool 7 m earlier; streams 0 and 2 shortened 15 to 10 points
- DONE: #34 blue moon radius 0.034 to 0.13 rad, much bluer, one thin crack, maria and small craters (a96a1dc)
- DONE: #32 roof rim lip on Hut, StoneHouse, Tower, Shrine (6449002); subtle, about 0.14 m thick
- NOT DONE: #4 #5 pad input behaviour (A then B, wheel enter/delete/ring switch/cancel): a shot list can't press buttons; needs Zach or a pad. `-menu Graphics` shot showed no menu
- Checks: 5 batch-mode builds succeeded, no exceptions in the player logs; before build (pre-change) and after build, same camera and time. Landmark hash not measured (no landmark code touched)
- Shots: D:\Unity\PunchyMilestones\world\W-01\ moonnight_before/after.png, moondusk_before/after.png, roof_before/after.png, cave_before/after.png, mm.png (800 px); full size in gallery\
- Learned: docs/agents/rendering.md (blue moon pin recipe)
- NEXT: Zach plays the menu with a pad (#4 #5); run WorldProbe landmark hash; #15 look checks; delete merged wip branches

## 2026-11-01 — #4 Exit game button (wip/ui-menu e0c7814; merged into wip/ui-wheel 6feebef)
- The menu's "Quit game" is now **Exit game**: full width, bold, red-tinted, a focus stop on every tab. The confirm reads "Exit the game?" Cancel / Exit, with Cancel first so the held focus lands on it. B backs out of the confirm.
- Compiles; not built. Slot check: shot of the foot and of the confirm (`-menu Graphics -forcepad xbox`); A, then B cancels.

## 2026-11-01 — #5 text wheel pushed (wip/ui-wheel 0f05394)
- Controller text entry for the menu's fields: a radial wheel. The stick points (the choice holds when it's let go), A enters, LB/RB or d-pad up/down switch letters/numbers, B deletes (cancels on an empty field), Y/Start done, View cancels, d-pad left/right moves the cursor. A field's allowed set limits the rings (IP: digits + dot only).
- Plugs into MenuNav.TextEntry from wip/ui-menu. The menu is modal behind it. NetMenu's keyboard notices are removed; the IP field is capped at 15.
- Compiles (dotnet check of UI/Settings/InputGlyphs). Not built yet. Slot check, same slot as #4: `-menu Multiplayer -forcepad xbox`, choose the Code field with A and the IP field, take shots of both wheels; enter, delete, ring switch and cancel with a real pad if there is one.

## 2026-11-01: UI-1 main menu (code only), zache-73

- **DONE, waiting on a slot: UI-1.** `wip/ui-menu` 0796b07 (off world-features b39522b, pushed, not merged).
  IMGUI, as the panels already were. New Scripts/UI: MainMenu (Esc/Start opens and closes it; tabs
  Multiplayer + Graphics; Quit at the foot; B/Circle/Esc backs out; LB/RB or Q/E switch tabs), MenuNav (focus stops
  for the d-pad/stick/arrows with key repeat; A/Enter presses; left/right steps sliders), Prompts (draws
  `[[control path]]` tokens as icons or names). The F2 and F5 toggles are gone. ThirdPersonCamera's Esc cursor unlock
  is folded into the menu. While it's open the Player action map is off (kept off across a join's rebuild). The frame
  cap is also a Graphics row. The help text says Esc menu; CLAUDE.md and MULTIPLAYER.md are updated.
- **Glyphs:** InputGlyphs.Token/ActionToken/Icon/Plain. The interact prompt, the HUD control hints (now drawn under
  the readout) and the menu footer use them. Icons load from `Assets/Game/Resources/Prompts/<kind>/<control>.png`
  (kind keyboard, mouse, xbox or playstation; control as in the path, '/' as '_': buttonSouth, dpad_up, escape, e).
  This is Resources rather than UI/Prompts, so they ship without a serialized reference. A generic pad and any
  missing icon fall back to today's names.
- **DONE: the icon pack.** `wip/ui-menu` 09a79ee. Kenney Input Prompts **1.5A** (License.txt says 1.5A; the
  folder Zach downloaded is named 1.5), CC0. 23 PNGs from the 64 px Default set: Xbox and PlayStation South/East/North
  (colour), shoulders, start (Xbox Menu, PS5 Options), stick presses; keyboard escape, enter, e, q, space, left shift,
  left ctrl. The pack's License.txt sits beside them. No fonts or .url files. They're in
  `Assets/Game/Resources/Prompts`, not UI/Prompts, so InputGlyphs.Icon can load them by name without a serialized
  reference. GUI textures, no mipmaps, uncompressed (metas committed). Also in the slot: check the icon size and
  sharpness at 720p.
- **Gap 8 hook:** `MenuNav.TextEntry` (current text, max length, allowed characters, result callback). While it's
  unset, a text field chosen on a pad shows "Typing ... needs a keyboard for now". The pad itself is not built.
- **Checked:** the new UI files, InputGlyphs and Settings/*.cs compile with dotnet against Unity 6000.5.8f1's DLLs.
  NetMenu, DebugControls and InteractionInputSystem wait for the bench build.
- **Slot check:** the bench build. Then `-menu Multiplayer` / `-menu Graphics` shots, the same with `-forcepad xbox`
  and `-forcepad playstation`. Then a pad by hand if one is plugged in: Esc/Start open and close, B backs out, tabs
  switch, sliders step, A on Host. Check for 0 IMGUI layout errors in the log.

## 2026-11-01: Z-34 stream with no pool (code only, restarted lane), zache-73

- **DONE, waiting on a slot: Z-34 leftover.** `world-laptop/z34-pool` 91f6b6a (off world-features b39522b, pushed,
  not merged). This follows Zach's call: CaveBaking.StreamEnd walks back from the stream's last point, up to 12
  points (~18 m), to the first spot that works. Either the floor ahead falls 1.5 m or more (a hole or crevice, so
  no pool), or a pool of 1.2-2.5 m at the stream's level passes RimHolds. The stream is trimmed there. With
  neither, the stream is left out. Streams are seed-derived, so nothing to sync. Players still need the same build.
- **Slot check:** a bench build (compile), then `-place cavestream#n` shots of each stream end. These include
  (-1454, 37, -1153), which should now end in a pool, or end shorter, or be gone. Look for a shortened stream that
  now stops short of its chamber, and for any stream ending at a drop.
- **NEXT (slot):** Z-30 hall/inn posts close up; Z-10 non-marsh river fog. P3 rows need design notes first.

## 2026-10-31: world slot 3 (task session): Z-32/Z-31 fixed, Z-34 fixed, Z-35 done, Z-09/Z-10/Z-11 judged, merged b39522b

- **world-features b39522b** (pushed) = b870008 + world-laptop/slot3 (6 commits; branch deleted). Same tree as
  c7bb5f3, which built clean (0 script or shader errors); 0 exceptions and 0 NaN-bounds lines in 6 look runs
  (~140 shots, results ws3/a-f with contact sheets in ws3/).
- **New test flags** (DebugControls, also per shot-list line): `-weather <Preset>` forces that WeatherPreset over the
  player (Rain, Storm, MarshFog, Overcast, PartlyCloudy...; Clear only clears), so rain and fog no longer depend on
  the seed's weather. `-place cavestream[#n]` goes to the end of the nth nearest cave stream and logs the 8 nearest.
  `-camrel dx,dy,dz,tx,ty,tz` poses the camera relative to the player (after any -place). `-waterprobe 1..4` draws
  water as foam, thickness, alpha or foam parts. `-caveshards` now also logs where ownerless walls are.
- **Judged by sight:**
  - **Z-32 fixed** (bb08f7a): the stone house walls were a four-sided tube with smoothed normals (45 degrees at each
    corner). Every wall shaded from edge to middle, and at a glancing view the built double-sided flip lit a wedge
    of wall orange at night. The walls are now a flat-faced block. Day, night and rain shots show even walls.
  - **Z-31 fixed with it** (stone houses): the slanted normals weighted the wrong triplanar projection (streaks).
    No lines in close shots after. The inn and hall plaster walls show no lines either.
  - **Z-34 fixed:** the thin lines across cave streams were the streak-foam term (`-waterprobe 4` showed them in its
    blue channel), so running cave water has no streak flecks now (c7bb5f3). A stream now stops at most half way
    into its end pool (no lighter tongue over the pool). Streams 2 and 3 meet their pools cleanly. Stream at
    (-1454, 37, -1153) has no pool at its end (CaveBaking leaves the pool out when the floor can't hold it), so it
    still ends mid-floor. The lantern-glint and reflection-march guesses were tried and undone.
  - **Z-35 done:** the falls shader ignored the effect's Lifetime and scrolled at a fixed rate. It now runs the fall's
    length in Lifetime, which is 2.5 s for Waterfall (was 4). Frames 0.2 s apart now differ a lot. There's a spray
    plume at the lip, and the curtain starts at the clifftop (no gap seen from the pool or above).
  - **Z-11 pass:** in a forced Storm (50 s), the village bench planks show puddle-wet ground around them and no
    screen-space image. No wiggle in frames 0.3 s apart. The elf bench is a stone slab. The very dark stone legs are
    in the seat's shade (wet), with a slight sheen.
  - **Z-10 pass where seen:** under forced MarshFog at FoggyMarsh, a stream is veiled like the rocks beside it. Its
    foam flecks keep some contrast. MarshFog over the River place showed no fog (that preset seems marsh-only).
  - **Z-09 pass:** 20 frames, 1 s apart, under PartlyCloudy caught 2 shooting stars, both in clear sky. 20 under
    Overcast caught 1, faint, in the sky below the cloud deck. None crossed a cloud.
  - **Z-30 pass where seen close:** bench planks, bridge planks and rails, smithy posts and the woodpile read as wood.
    Hall/inn posts were seen only at 9 m. There's still no Pen on this seed.
  - **Cave-mouth shards: none found.** `-caveshards` logged no ownerless wall in any cave chunk built at CaveNetwork,
    CaveEntrance, Grotto, the nearest cave (FungalGrotto), the big cave mouth or three cave streams.
- Waterfall on this island: landmark about (627.5, 2526), pool centre (629, 2518), clifftop about 92.
- Unity and every Punchy.exe started here are closed. The main checkout is on b/weather-mapjob, and the bench lock
  is clear.

## 2026-10-31: world slot 2 (task session): targets found, owed looks taken, Z-30 grain fixed, merged b870008

- **world-features b870008** (pushed) = 3976d17 + world-laptop/z30-more (6 commits). Built clean as 8b0a657 (same
  tree): 0 script or shader errors; 0 exceptions and 0 NaN-bounds lines in 7 look runs (~80 shots).
- **animal-behaviours 9b7459b + world-features b870008** (tmp/animal-world-check 5f4c917): built clean, 0 errors.
  Not merged; tmp branch deleted.
- **Targeting:** `-place prop:<Species>[@x,z][#n]` (DebugControls) finds any placed plant (landmark props, town
  buildings, biome scatter), logs the 8 nearest with coordinates, height and front, and frames one. One run gave:
  Bench (111.4, 25.7, -404.8) and 30 more; Stall (-355.6, 12.2, -4079.4) at OgreCamp; Smithy (76.3, 25.8, -367.8);
  Inn (168.1, 25.7, -401.4); Hall (513.0, 28.1, -943.5); StoneHall (1973.3, 65.1, 2479.9); Fountain and Forum
  (260.8, 32.1, 3250.9); ElfBench (267.0, 32.1, 3253.2); EmberPine (-800.8, 25.9, -3573.5); Woodpile
  (87.6, 25.7, -439.9); Bridge (231.6, 16.9, -638.5); AncientTree (-5.9, 5.3, 1250.5), 31.9 m tall. No Pen found.
  Geyser pool ~(1182, 51.5, 2799); MudPits pools ~(197, 20, -3666); FoggyMarsh place (-304, 5, 817).
- **Rain:** `-benchday <day type>` on b/bench-runner db3c1f2. The day types are Sunny, Partly cloudy, Overcast, Stormy
  and Changeable. Stormy doesn't rain everywhere (the village had a cloud deck). Under Partly cloudy, seed 4, rain
  reaches the village square about 90 s in.
- **Shot lists under the bench route:** `-benchroute -benchstops here -benchlong here 900` holds the weather while
  `-shotlist` takes many in-game shots in one session (HANDOFF-world "How to test").
- **Looks, judged by sight:**
  - **Z-30 failed, then fixed.** The bench close-ups before (7f519c0) and after (3976d17) looked the same: the grain
    was at most 18% darker and mostly faded by the pixel footprint. 632f479 draws lines about 3 cm apart, up to 45%
    darker, over broad bands, and the same close-up now shows clear streaks along the planks. 69f3185 marks more
    timber: pens, the inn's band, posts and porch, the hall's walls, posts, beams and poles, bridges, the smithy's
    posts, the anvil block and the chopping block.
  - Z-33 pass: three frames 0.3 s apart show broad, slow rolls, with no high-frequency shake. The speed itself can't
    be measured from stills.
  - Z-36 pass: the geyser pool shows flat rings lying on the water beside the bubble domes.
  - Z-37 weak pass: at night (in rain) a few coloured fireflies hover above the ground by the great tree. The tree
    stands in a dense marsh canopy, which makes it hard to frame.
  - Z-39 pass: the ember slivers on the pine crowns are short upright dashes.
  - Z-40 pass: embers and dark coals sit inside a raised stone rim and glow at night.
  - Z-41 pass: the stone hall's ridge shows a rounded tile band. Stone houses have hipped roofs, with no ridge.
  - Z-12 pass where seen: the forum slab's edge at 270,3248 shows no stretch and a dark band at the ground. The
    ground there is barely below the slab, so it's not a hard case.
  - Z-09 pass with `-meteors`: the aurora (on with the same switch) stays behind the clouds. No shooting star was
    caught in 6 frames (rain streaks look alike).
  - Z-10 inconclusive: fog and rain in the marsh veil its streams, and no unfogged water was seen.
  - Z-11 partly judged: no wiggle in two frames 0.3 s apart, and no screen-space image on the planks in light rain.
    The stone legs look glassy in dry and wet shots alike.
- **Found for the animal lane (TraceSystem, not changed):** when a dog walks under a bench, its paw prints land on
  the bench seat. `Underfoot` samples prop tops at foot + 0.3 m and takes the seat above; it needs
  `hit.Height <= at.y + ~0.3`. Shots: ws2/e/z11-bench-rain-b and -c.
- **Cave-mouth shards:** `-caveshards` (CaveSystem) draws walls no cave owns in magenta. None showed at the
  CaveEntrance, Grotto (glacier) or nearest-cave mouths from the places' default views. The big cave's mouth wasn't
  in view; a close look at a known shard spot is next.
- Shots: Punchy-wt/Tools/bench/results/ws2/{probe,a,b,c,d,e,f}, with contact sheets in ws2/.
- Unity and every Punchy.exe started here are closed; the main checkout is on b/weather-mapjob; the bench lock is
  clear.

## 2026-10-31: world slot 1 (task session): z-p2 built, benched, merged; looks partly done

- **world-features 3976d17** (pushed) = 247c690 + world-laptop/z-p2 (13 commits; blocked-top was already in via
  worm-arc). Built clean first time. Smoke vs 247c690 said NOT CLEAN (glide cap 0 1% low 36->23 from ~500 ms spikes);
  the ABBA `ab 247c690 3976d17 townwalk,glide` says no change beyond noise (townwalk 48.6->54.4, glide 35.6->34.2).
  35 look runs, 0 exceptions.
- **Seen and fine:** Z-38 fungal forest caps read tawny/brown; Z-42 glacier has IceCrag (stacked icy tor, reads a bit
  like a cairn of pancakes) and IceSlab blocks next to the arch; dwarf hold hall roofs and stone houses by day and night
  at 20 m show no artefacts; geyser pool bubbles show (ripple not judged at that distance).
- **Not judged (shots missed the target):** Z-30 before/after, Z-33, Z-36 ripple, Z-37, Z-39, Z-40, Z-41 up close,
  Z-12 skirt, Z-09, Z-10, Z-11, Z-31, Z-32, Z-34, Z-35, cave-mouth shards. Causes: `-place tree:` only knows tree-layer
  plants; stale falls/cave coordinates; no rain outside the storm stops. Next slot in HANDOFF-world.md.
- Seen in passing: big screen-door dithered patches where a canopy or giant cap is between camera and player (the
  see-through dither) in fungal, falls and storm shots; expected, but they're large on giant caps.
- Unity and every Punchy.exe started here closed; main checkout on b/weather-mapjob; bench lock clear.

## 2026-10-30: Zach's answers on Z-12 and Z-38; RockAt for the worm, zache-73

- **Z-12 stretched base (654d748, z-p2): DONE in code.** The flora shader worked out "grounding" (darker toward the
  ground: a tight 12 cm contact band plus a broader rise) at the vertices. On a foundation skirt, whose bottom ring
  sits 1.6 m down, that band was interpolated linearly over the whole exposed face: stretched toward the bottom, and
  no dark band at the ground (also G-22). It's now per pixel, from the interpolated height above the base (exact,
  since it's linear in y). That fixes every skirted building. Check: a fountain or forum on a slope, from below the base.
- **Z-38 small glowing caps (e2f1c97, z-p2): DONE in code, fungal forest only.** New FungalGlowCap and FungalGlimmerCap
  (amber-brown and dusky brown, still glowing) replace GlowCap and GlimmerCap in the fungal forest's flora list only.
  The poison swamp, sparkling forest and fairy rings keep the originals.
- **For zache-db's worm (Z-28), on world-laptop/blocked-top e58eed2:**
  - `Blocked(..., out float top)`: buildings and placed props, top = model height x scale.
  - `RockAt(ref f, source, xz, radius, out float top)`: scattered rocks (any solid non-tree on the tree layer: boulders,
    tors, standing stones, hoodoos), from Place() on the cells within radius + 6 m. No streamed state.
  - zache-db merged it into wip/worm-arc (9213d8e); their build is the compile check, then I land it on
    world-features.

## 2026-10-30: Z-30 wood grain, option (a), zache-73

`world-laptop/z-p2` ee5a1f9, not compiled.
- `FloraMeshBuilder.Wood` marks timber (Bark + 0.02; strata are 0.04-0.15, seams > 0.15, veins > 0.32). It also stores
  the grain direction in the vertex colour's alpha: 0.25 X, 0.5 Y, 0.75 Z, the object axis nearest each piece's length
  (Box/Beam/Tube/Sheet/Blob each set it). The shader never read that alpha; builders' colour maths scales it, so it's
  only meaningful on marked wood.
- Flora.shader `PunchyWoodGrain`: fine (~2.5 cm) and broad (~11 cm) wavering streaks along the axis, in the plant's own
  frame so they turn with it, darker in the lines. Each scale drops out by screen footprint, as the weathering does.
  New interpolators: object position and a nointerpolation axis.
- Marked so far: stall posts and counters, bench planks (the elves' stone seats aren't), rail fences, signposts.
  After the before/after look: smithy, inn and hall timber, pens, bridges, the woodpile's block.

## 2026-10-30: P2 second batch (W), zache-73

`world-laptop/z-p2` ec75753, not compiled.
- **Z-38 widened (01da094), per Zach's wording:** GiantPaleCap, GiantPuffball and PaleCap (PaleCap is also in the foggy
  marsh) go from cream and beige to tawny, light tan and mid brown, keeping a light-to-dark range. With the giants'
  brown caps, the whole canopy reads earthy. Kept: the small glowing caps (GlowCap, GlimmerCap). They're the forest's
  lights and are shared with the poison swamp, sparkling forest and fairy rings; say if Zach wants them browned too
  (brown caps with glowing gills, as the giants have).
- **Z-33 cloth LOD (08c93a2):** banners drop to 1 x 2 segments at LOD 1 (were 2 x 3), awning stripes to 2 rows (were
  3), tent flaps to 1 x 2. LOD 0 and LOD 2 (one quad) are unchanged.
- **Z-42 glacier (6564033):** IceSlab (the boulder families in ice, 1.5-4 m) and IceCrag (Tor form, 4-9 m) join
  IceArch, slabs commonest. New `FloraSpecies.KeepOwnColour`: without it, ice boulders take the glacier's rock tint
  (G-36). Landmark list unchanged.
- **Z-40 furnace (ec75753):** there's no separate smelter, so this is the smithy's forge. The hearth is now a lower bed
  inside a raised stone rim, with glowing embers among dark coals (7 up close, 3 further off) instead of a glowing
  dome on top. Anvil and work spot unchanged.
- **Z-30 wood grain: design question.** The flora shader can't tell wood from stone (both are the Bark part), and
  grain needs a direction (planks run along, posts up). Options:
  (a) a "wood" mark (Bark + a new fraction, like the seams' and strata's) set by the builders on timber (planks,
      beams, posts, stalls, benches), plus grain stripes along the longer axis of each face. Right, but it touches
      every wooden builder.
  (b) a shader-only heuristic: warm-hued built Bark gets grain along world Y on upright faces and along X/Z on flat
      ones. Cheap, but it also catches warm plaster and would grain mud walls.
  I'd do (a), starting with the stall, bench, fence and smithy timber, after a look at today's wood.

## 2026-10-30: Zach's v0.10 P2 rows (W), first batch, zache-73

`world-laptop/z-p2` bfd69e5 (off world-features 7f519c0), not compiled; shots in a slot.
- **Z-33 banners (e42f0fa):**
  - The wave runs at 0.9 + 2 x strength rad/s (was 1.6 + 4.5 x, about twice as fast in a breeze).
  - The edge flutter is slower, broader and gentler, and fades out between 30 and 80 m, where it read as shimmer.
  - Cloth meshes already drop to 3 and 1 segments at the far LODs, so "less resolution at distance" is mostly that
    shimmer. Awnings are unchanged.
- **Z-37 great tree fireflies (c180050):** two in three of its coloured fireflies hover 4 or 8 m up round the crown
  instead of all at its feet.
- **Z-38 fungal caps toward brown (1331dad):** glowing giants' plain caps lean umber to tawny per cap, keeping a hint
  of their hue. The brown is drawn only for them, so other mushrooms' shapes don't shift.
- **Z-39 ember slivers along the grain (da04ce0):** the pattern varies across the crown and only slowly with height,
  giving upright slivers in short dashes.
- **Z-36 bubble pops as ripples (8e793b7):** after the burst, the quad lies flat on the water (it was a billboard), and
  the ring takes the swimmers' shape: a lit crest, dark troughs either side and a weaker inner ring, fading as
  (1-t)^2. The ring maths is copied, not shared slots, so no feeder is needed.
- **Z-41 roof bevel (bfd69e5):** a rounded ridge tile along stone houses' and halls' gabled roofs; ridge perches
  raised onto it. Cone and tent roofs are unchanged.
- **Need a look first (next slot):**
  - Z-31: vertical lines in the close-up detail. Suspect the triplanar detail texture's tiling seam on x/z faces.
  - Z-32: stone-house lighting bands. Suspect normals across the walls or the double-sided flip.
  - Z-34: cave stream meets pool; striping.
  - Z-35: the waterfall's gap at the top. The fall speed is the effect's Lifetime, an asset value, easy once seen.
- **Bigger builds, next:** Z-30 wood grain, Z-40 furnace ember pit, Z-42 glacier slabs and bergs.

## 2026-10-30: merged zache-db's creature render ranges, zache-73

- **DONE:** wip/gfx-creatures 6f6dd94 merged into world-features as **0c637a9** (merge commit), pushed. It wires
  CreatureRenderSystem, TraceSystem and the F1 key line onto GraphicsSettings. Compiled in zache-db's slot; merged
  clean here, code only.

## 2026-10-30: slot (cave-chunk, gfx settings, P1 checks), zache-73. Unity closed

No Unity, Punchy or bee_backend process running; Projects\Punchy on b/weather-mapjob.
- **Merged cave-chunk** (world-features 0d4e964). **Correction to 256e6a9's commit claim:** "about five times cheaper"
  was the mouth-cut sampling per vertex, not the chunk. At frame level the busiest BuildChunkMeshJob went
  36.8 -> 30.9 ms (-16%); the A/B gain was 1% low 52.5 -> 59.6, worst 27/30 -> 25/21 ms.
- **Merged gfx-settings and z-p1 together: world-features 0d4e964 -> 7f519c0** (fast-forward of a combined branch:
  both merges, plus fountain water on DetailRange and the Z-13 follow-up).
  - Compiles (both branches at once, no errors).
  - Startup line logged: "Graphics: view 1.0 detail 1.0 shadows 1.0 (Medium)". 0 exceptions.
  - Medium = today: the smoke said NOT CLEAN (townwalk cap 0, 1% low 52.6 -> 48.2), but its rounds disagreed (cap 60
    went *up* 36 -> 44). An interleaved ABBA `ab` townwalk came back flat (41.8 -> 41.4, OK).
  - Presets at a village edge, cap 60:
    | Preset | GPU ms | 1% low fps |
    |---|---|---|
    | Low | 6.9 | 58.1 |
    | Medium | 9.0 | 40.8 |
    | Ultra | 15.7 | 31.3 |
    Low hazes and thins the distance, Ultra shows the far forest and more grass, and nothing near the player drops
    out. The profiler's sweep picks this laptop's default.
- **P1 looks:**
  - Z-12 (270,3248): rim capped, basin water drawn as real water. No stretched texture visible from the forum (the
    floor stripes are column shadows); needs Zach's camera if it's still there.
  - Z-11: the elven bench reads as a stone slab. The wet-image look still needs a rainy setup: next slot.
  - **Z-13 was not fixed by the plot clearing.** The cabbage patch is a **hermit's garden** (a HermitHut prop) on a
    road routed after the landmark. b651be6: a landmark's placed props give way to roads and spurs (streets with
    Road set); squares don't count. Re-shot: garden gone, road clear. The plot clearing also removed two houses
    the road ran into at that village's edge.
  - Z-09 and Z-10 weren't looked at (they need night/cloud and fog setups): next slot.
- **Grey shards at cave mouths (item 15 shots, from before cave-chunk): queued, not fixed.** They're flat,
  untextured and grey: the cave material's own colour, style slot 0. In CaveSystem's mesh Look(), a vertex with no
  owning cave (CaveSampling.Distance gives -1) gets style 0. Likely small disconnected surface-net islands where
  the mouth's blended field (surface air smooth-min'd with cave air) dips into a pocket away from the passage. Fix
  options: drop tiny disconnected islands in the mesh job, or give orphan vertices the nearest cave's style and the
  terrain look. Needs a probe build first (count islands at that mouth).
- Reviewed for zache-db: 5198212/4f597bf mask checks OK; the wake-trail shader OK with a distance gate (45 m in
  aa6d715).
- NEXT: the cave-mouth shards (probe, then fix), the Z-09/10/11 looks in a slot, then P2.

## 2026-10-30: Zach's v0.10 P1 rows (W), zache-73

`world-laptop/z-p1` 9124707 (off world-features 3d874dc), not compiled; shots in my slot. Z-01 is on
world-laptop/gfx-settings (a909076).
- **Z-01 Quit: DONE in code.** "Quit game" in the F5 panel, with a confirm. F5 needs a line in the F1 key list
  (zache-db is adding it).
- **Z-09 shooting stars in front of clouds: DONE in code (a4a130a).** The cloud march stopped at 2% transmittance
  but kept that 2% of the sky behind, and a streak (5x the sky) shone through dark night clouds. Now opaque there.
  Streaks and auroras are also dimmed by the cloud cover along their own direction (one cheap sample, only where
  one is lit).
- **Z-10 river over nearer fog: DONE in code (5403f55).** The water shared the Transparent queue with fog wisps, flames,
  rain and falls, and a big water chunk sorted by its middle could draw after mist in front of it. Water is now
  Transparent-10, drawn first (everything else in that queue stands above it). Water.shader is shared: zache-db,
  please note.
- **Z-11 benches: 2 of 3 DONE in code.**
  - d0e176f: built (Founded) forms are meshed with no wind sway. Twelve species swayed like plants, not only the
    benches: halls, lodges, shrines, palisades, fences, signposts, stumps. Gardens keep sway for their plants;
    cloth keeps its own motion.
  - aea3643: elven benches get a carved stone slab seat (new `FloraSpecies.StoneSeat`, set on ElfBench).
  - The view-dependent "wet image": the likely cause is the built-thing weathering. Its octaves fade by screen
    footprint (fwidth) and the survivors are scaled up 2.2x, so a small seat's mottling changes with the camera.
    I'll confirm with a look before changing it, as you said.
- **Z-12 fountain: 2 of 3 DONE in code (9124707).**
  - The rim had no top: wall and rim band are both vertical, and from above the gap showed the ground. It's now
    capped with a flat stone ring.
  - The water is the game's own: `FloraModel.Pools` records basins, and FloraSystem draws them within 80 m as
    landmark pools are drawn (`CaveSystem.MakePoolMesh`, now internal). The old shiny discs stand in further out.
    The 80 m goes on DetailRange when gfx-settings merges.
  - The stretched texture at 270,3248 needs a look first (it's G-22's forum base; suspect the 1.6 m foundation skirt).
- **Z-13 road through a cabbage patch: DONE in code (8f90d47).** Towns are closed to roads only inside 0.85 of their
  radius, but plots reach the edge. A plot a road crosses now becomes an empty green (Landscaping, no building),
  keeping its index. Roads and roadside landmarks don't move (landmark list unchanged), but **the town layout
  changes, so the world fingerprint does too.**
- **Z-14 night howl: handed to zache-db (their files).** It's the wolves' SoundId.Howl: volume 1, reach 160 m,
  with a breathy noise layer.
- **Z-02 crow stuck, cave side: nothing to change.** CaveSystem never touches player control; its only freeze, the
  mouth hitch, was fixed before v0.10. Flight into cliff or cave rock was cancelled whole in movement:
  zache-db's f378a43.
- NEXT: the gfx + P1 slot (compile both branches; Medium = today assert; flat A/B; Low/Ultra shots; looks for Z-09,
  10, 11, 12, 13 and the two "needs a look" items), then P2.

## 2026-10-30: graphics settings steps 1-2 and Z-01, zache-73

`world-laptop/gfx-settings` 9cf4e26 (9 commits off bc89606), not compiled or merged; the checks come in my slot.
- **Step 1 (6a16561):** `Punchy.Settings.GraphicsSettings` (the design said Punchy.Rendering; Settings matches
  FrameRateCap's folder and pattern). View, Detail and Shadows multipliers with Low/Medium/High/Ultra presets,
  PlayerPrefs, and the **F5** panel (F4 is the Options action that steps the frame cap). `-gfx` and `-gfxset` apply to
  one run and are never saved: the panel only applies on a real slider move, so a command-line value off the
  sliders' 0.05 steps isn't rounded and saved on opening. Startup log line: "Graphics: view 1.0 detail 1.0 shadows
  1.0 (Medium)". The URP shadow distance and cascades are restored on quitting. `_PunchyBurrowRange` =
  DetailRange(150). `Punchy.UI.Overlay.AnyOpen` replaces NetMenu.IsOpen in ThirdPersonCamera (1 line +
  comment), PlayerInputSystem (1) and InteractionInputSystem (3): your approved edits. zache-db has the API and waits
  for your OK to wire the creature ranges.
- **Step 2, world ranges, one commit each:**
  - GroundDetail: grass 60, pebbles 45, full density 20 (Detail).
  - LandmarkEffect: 700 (View); flames 150 and cave falls 120 (Detail).
  - Critters 70 (Detail).
  - Ambient particles' Range and others' breath 80 (Detail).
  - Cave plants 60 and drips 25 (Detail).
  - Flora: ground layer at Detail, shrubs and trees at View, radius and LODs, floored at SupportRange + 1.5 tiles;
    bolts 160 (View).
  - Terrain: view radius and LOD distances at View. A change applies after holding 0.3 s by flagging every loaded
    chunk for rebuild (no unload, no holes). The camera far plane (3000 in SampleScene) is pushed out only if the
    terrain reaches past it, which at View 2 it does (3.2 km).
  - Not wired: WeatherSystem reads ViewRadiusChunks for a draw distance (shared file). Left at the baked value; ask if
    you want it scaled.
  - At merge: the TerrainSurface burrow gate becomes `distance < _PunchyBurrowRange` (zache-db asked me to do it).
- **Z-01 DONE in code (a909076):** "Quit game" in the F5 panel, with a Quit/Cancel confirm (stops play mode in the
  editor). Discoverability: F5 isn't listed anywhere on screen. The F1 readout's key list is DebugControls (animal
  lane's file), so it's worth a "F5 graphics / quit" line there.
- NEXT: P1 rows Z-09, Z-10, Z-11, Z-12, Z-13, Z-14, Z-02 (cave side), code first, then shots in a slot.

## 2026-10-30: graphics settings, distance sliders: DESIGN for OK (queue 1), zache-73

**Shape.** `Punchy.Rendering.GraphicsSettings`: static, PlayerPrefs-backed, applied live. Three multipliers on today's
defaults (1.0 = today exactly), plus a preset that sets all three. Systems read `default x multiplier` where they use
the range, never by rewriting serialized fields (so assets and scenes don't change).

| Slider | Range | Scales |
|---|---|---|
| **View distance** | 0.5-2.0 | terrain view radius (TerrainConfig.ViewRadiusChunks, applied in TerrainStreamingSystem; config untouched) and terrain LOD distances with their morph ranges; flora Shrub + Tree layers (Radius, Lod1/Lod2Distance); LandmarkEffectSystem.Range (700); FloraSystem.BoltRange (160). The camera far plane is kept at least the scaled terrain radius. |
| **Detail distance** | 0.5-2.0 | GroundDetail GrassRadius 60 / PebbleRadius 45 / FullDensityRadius 20; flora Ground layer (Radius 70, Lods); CritterSystem.Radius 70 (mine, per your note); LandmarkEffect FlameRange 150 and CaveRange 120; CaveSystem PlantRadius 60 / DripRadius 25; AmbientParticleType.Range; AmbienceSystem.OthersRange 80; burrow marks (below). Animal lane: CreatureRenderSystem FootDetail 40 / TorchFlame 150 / Lantern 60, TraceSystem. |
| **Shadows** | 0.5-2.0 | URP main-light shadow distance (50 m today), with cascades 2 below 0.75, else 4 as now. |

**Presets** (the profiler's sweep may move them; Medium = today until it says otherwise):

| | View | Detail | Shadows |
|---|---|---|---|
| Low | 0.6 | 0.5 | 0.6 (2 cascades) |
| Medium | 1.0 | 1.0 | 1.0 |
| High | 1.3 | 1.5 | 1.4 |
| Ultra | 1.7 | 2.0 | 2.0 |

**Never scaled (simulation, collision, net):** FaunaDirector spawn radius, CreatureRelevancySystem 200 m,
FloraSystem.CollisionRange 24 / SupportRange 40, CaveSystem.LoadRadius 400 (wall meshes), anything seed-derived. One
catch: FloraSystem.UpdateSupports reads the *streamed* flora tiles, so every flora layer's scaled radius is clamped
to at least SupportRange + 1.5 tiles; otherwise Low could stream out a rock the player stands on. Settings only
change what's drawn, so two players with different settings see the same world and stay joinable (no
fingerprint change).

**API** (for the animal lane and anyone else): `GraphicsSettings.View`, `.Detail`, `.Shadows` (floats);
`GraphicsSettings.ViewRange(float baseline)` and `.DetailRange(float baseline)`; `GraphicsSettings.Version` (int,
bumped on every change, for systems that hash their config and rebuild). **Burrow marks:** one source, as you asked.
GraphicsSettings sets the global `_PunchyBurrowRange` = DetailRange(150); the feeder uses DetailRange(150) for its
Range; TerrainSurface's gate reads `distance < _PunchyBurrowRange` instead of the literal (a one-line change to
zache-db's 92a9602 once it lands, or before, if they prefer).

**Applying live.** Cheap ranges apply the next frame. Terrain view or LOD changes mark every loaded chunk for
rebuild (one wave of rebuilds, nearest first), so that slider applies on release, not on drag. Flora layer radius
changes restream tiles beyond the new edge. Shadows apply at once. In the editor the URP asset's shadow values are
restored on exit, so play mode never saves them into the asset.

**UI.** **F4** opens a small panel in the F2 panel's IMGUI style (F1-F3 are taken): a preset row and the three
sliders with their values, plus Reset. It saves to PlayerPrefs on close.

**Flags** (bench runner): `-gfx Low|Medium|High|Ultra` and `-gfxset view=1.3,detail=0.8,shadows=1`, applied after
prefs. **Flags are never saved to prefs** (the -screen lesson): a bench run can't change Zach's settings. Bench
builds have their own prefs identity anyway (PunchyBench).

**Order of work once OK'd:** (1) GraphicsSettings + flags + F4 panel; (2) world ranges, one commit per system; (3)
the API note to zache-db; (4) checks: at Medium every range equals today's (an assert in a debug build, plus an
A/B smoke against bc89606 that should be flat); shots at Low and Ultra from one camera.

## 2026-10-30: Blocked fast path (BENCH item 14), zache-73

- **DONE in code: `world-laptop/blocked-cache` 3d874dc** (off world-features bc89606), not merged, not compiled; the
  profiler's Brain.Blocked A/B build is its check.
- **What made Blocked dear:** for every town plot or counted landmark prop near the animal, it called Place(), which
  builds the full FloraInstance: the plot's FloorHeight and DirtColor, or a prop's SampleHeight and GroundFor, all of
  which sample the ground round the building. Inside() only reads Position.xz, Yaw, Scale and Model.
- **Change:** no cache. `Place(..., bool footprint = false)`; Blocked passes true, and the building/prop/centrepiece
  branches then skip those four (Position.y and Ground come back 0). None of them draws from the cell's Random, so
  xz, Yaw, Scale, Model and Random are exactly as before: same walls, same results, nothing baked, no fingerprint
  change. Walls() and NearPerches() still place in full (they use the heights). Ordinary plants are unaffected.
- Why not a baked cache: FloraBlob is built from the palette with no TerrainSource, so baking every plot's instance
  needs a new bake step and memory. If the A/B shows Blocked still dear (the plot loop, OnBuiltPlot), a per-cell table
  is the next step.
- Brain side unchanged (Walled -> Blocked keeps its signature); zache-db told.

## 2026-10-30: cave-mouth chunk follow-up (v0.11), zache-73

- **DONE in code: `world-laptop/cave-chunk` 256e6a9** (off world-features bc89606), not merged, not compiled; the
  caveapproach A/B build is its check.
- **Where the 39 ms went:** BuildChunkMeshJob.MouthCut ran `Source.MouthCut` five times per LOD 0-1 vertex (the vertex
  and four half-quad neighbours), each a SampleNormal (4 heights) and a CaveDistance, plus a height per neighbour. Its
  `cut >= 100` early-out only fires outside a cave's column footprint. Inside it, MouthCut returns about 0.25 wherever
  the passage lies metres down, so every vertex over the whole footprint paid all five.
- **Change:** `MouthCut(xz, h, out cave)` (a TerrainSource overload, additive) gives the field distance it sampled. The
  job skips the four neighbours when that distance exceeds 2.05 m (MouthCutDepth - 0.1 + MouthBlend + 0.5 spare) plus
  2 x their reach (half a quad, their height difference, twice the sampling depth). Past 1.45 m the smooth min returns
  exactly its rock value, so the skipped neighbours couldn't have changed the min: the cut is unchanged. FieldSlope 2
  is a deliberately loose bound (the field is noise-moved and can overstate by 1.8x). If the A/B shows a cut
  difference at a mouth, raise it.
- **Expectation:** the mouth-cut share of a chunk over a cave drops about 5x. The commit subject says "about five
  times cheaper", which overstates it for the whole chunk (other sampling stays). The A/B will give the real number.
- Earlier entry (worm merge, TroddenGround early-out) was held back by GitHub 500s on push and goes up with this one.

## 2026-10-30: worm marks merged; TroddenGround early-out, zache-73

- **DONE: worm marks merged.** world-features c4d946d -> d81cb06 (merge of world-laptop/worms ad98690, shader only;
  wip/worm-look stays on the animal side, none of its commits came in). Pushed.
- **DONE in code: TroddenGround's hot-path cost.** As first written it did the landmark grid lookup and a full `Hit`
  (distance and frame transform) for every landmark in the cell before finding that most types have no trodden
  props. bc89606 (pushed to world-features) adds two cheap exits: the blob has no trodden spots at all, and the
  instance's type has none, checked before `Hit`. Away from camps with stalls it now costs only the grid lookup,
  which Ground and Shape already do on the same sample. **Not compiled yet** (no slot). Please have the profiler's
  smoke build bc89606 or later rather than c4d946d, so it also serves as the compile check.
- NEXT: idle; the cave-mouth chunk follow-up for v0.11 when you want it.

## 2026-10-30: F-40 and worm marks in the 06:25 slot, zache-73. Unity closed

Bench-runner builds plus shots, no editor. No Unity, Punchy or bee_backend process is running now; Projects\Punchy is
back on b/weather-mapjob. Throwaway branches (tmp/trodden-probe, tmp/worm-test) were local only and are deleted.
- **DONE, merged: F-40 trodden ground.** world-features 6464ce7 -> c4d946d (fast-forward, pushed). The first build of
  the mask (21e7ffd) showed nothing, for two reasons:
  (1) camps are settlements, and their square's surface repainted the ground over the wear. The wear now goes on
  last: `LandmarkSampling.TroddenGround`, called after the settlements in `TerrainSource.SampleSurface` (one added
  call in that shared file).
  (2) At 2.6 m the patch lay under the stall's ~7 m awning and counter. Probe builds showed the data baked (4 trodden
  spots), SampleSurface darker at the stall (albedo 0.069 vs 0.111 at 10 m), and a black 8 m test patch drawing.
  Now: Trodden 7 m on goblin and ogre camp stalls, centred half its reach in front of the counter, x0.5.
  Check (OgreCamp, -355,-4079 from the fire's side, and from above at -362,-4044): a soft, ragged, darker patch in
  front of the counter, the sand's texture showing through. Subtle from eye level: `TroddenDarken` is the dial if
  Zach wants it stronger. GoblinCamp not checked (it sits in fog).
- **PASS, not merged: worm marks.** world-laptop/worms ad98690 with wip/worm-look (c751907, includes 85a1962) merged
  locally, `-place Glacier -spawn SnowWorm:1 -watch SnowWorm -watchfrom 10,20 -timescale 3`, window captured every
  second. The mound and furrow read clearly as a lit ridge and a shaded trough trailing the head. After a breach the
  crater reads as a dark pit with a lit raised rim (the sign fix) and fades out over the next few seconds. No
  exceptions in either run. Per the queue, it merges into world-features after the joint look with the animal lane.
  Say if you want it merged now.
- NEXT: the worm merge on your word; the cave-mouth chunk follow-up for v0.11.

## 2026-10-30: code-only batch (F-40 mask, worm marks, Brain.Sample read), zache-73

None of this is compiled or seen yet: it needs a build (the bench runner's `build` is enough for the code) and shots.
- **F-40 trodden mask: DONE in code, `world-laptop/f40` 21e7ffd, not merged.** The sheet is gone (one revert
  of b336426 + 677bd3b, KeepOutOfSkirt with it); world-features merged in. New `LandmarkProp.Trodden` (m): placed
  props (Count) get worn ground, baked per landmark type at FloraPlacement's own spots (`LandmarkSetBlob.Trodden`,
  like pools) and painted in `LandmarkSampling.Ground`: the ground's colour x0.6, grass gone, pebbles x0.3,
  centred 0.35 x Trodden in front of the prop (towards the middle), ragged edge (snoise at 0.8/m plus the edge noise).
  It's the terrain's vertex colour, so its texture and detail draw over it (the terrain shader itself is unchanged).
  Goblin and ogre camp stalls get 2.6 m. Seed-derived, nothing to sync; landmark placement unchanged, fingerprint
  changes. Side effect: stalls' own dirt band samples the darker ground, so their bases get a little darker too.
  Check: the OgreCamp stall at -355,-4079 (camera -356.6,15.5,-4072 looking at -355,12.3,-4079), before = Bench-d14f556.
- **Worm marks: DONE in code, `world-laptop/worms` ad98690** (world-features + wip/world-worms), not merged. Stronger
  (mound tilt x2, furrow tilt x2.2, darken 0.28 -> 0.4 / 0.12 -> 0.18) and craters fade by (1 - b.y), b.y being the
  feeder's crater age. Bug found: the crater rim's tilt had the wrong sign (sign(r - rim) * -1.2), so the rim shaded
  as a groove: likely why "the crater barely shows". Now it follows the bump's own slope ((r - rim) / width * 2).
  Needs the feeder (wip/worms) or the globals set by hand to see.
- **Brain.Sample 8.7 -> 11.2 us (item 13): the A/B crosses the island change, so it can't attribute this.**
  6cfedaf (twin on e4010f2) lacks 1e53043 (G-18 roads, the v0.10 island), 05f18ec (F-22) and 4c5eafe (F-30);
  7bd0768 has all three. The animals stand in different places on different ground. Code inside the marker is
  byte-identical in both (the marked regions and CaveFloorAt, Crawl, Walled, WalksRoundWalls, SwimDepth). Below it:
  F-22 added one max per lake cell in RiverSampling.Lake plus BayDepthLimit, which only runs after
  NearestLakeAbove succeeds (behind `ShoreLevel > height`, so never far from lakes); F-30 adds one compare to the
  cave SDF, and FloorUnder already skips caves by the baked NearColumn flag. None of that costs 2.5 us a step.
  The likelier source is `Walled` -> `FloraPlacement.Blocked`, which re-runs `Place()` for every town plot or
  counted landmark prop near the animal, every call. Its cost follows what's nearby, and 1e53043 / 7772e01 (Gate
  plots) changed that. Suggestions, no behaviour change: (1) re-run the A/B on the same island (a twin on a commit
  after 1e53043, e.g. 05f18ec^ vs 7bd0768); (2) if Blocked shows up, cache each plot's and placed prop's
  FloraInstance (they're seed-fixed) instead of re-placing per call. That's Flora (shared) and the brain (animal
  lane), so I haven't touched it.
- NEXT: a slot for the F-40 and worm shots; the cave-mouth chunk follow-up stays for v0.11.

## 2026-10-30: F-40 and G-37 in the post-release slot, zache-73. Unity closed

Checked with bench-runner player builds (Bench-<hash>, built with lib.sh `build`, ~5 min each) and framed shots
(`-place <landmark> -benchstops here -cam/-camlook`), not an editor. No Unity is running now; Projects\Punchy is back on
b/weather-mapjob.
- **DONE: G-37 decoration rocks.** world-features 2310533 -> 6464ce7 (fast-forward, pushed): MagneticDrift, a
  FloatingRock species (MagneticMonolith copied: 6-15 m, 4 variants, seed 337) in ElectricWastelands' flora at 0.03
  per 100 m2. At 0.01 a whole wastelands view held one, lost among the small Debris clusters (MagneticDebris is form
  Debris, not FloatingRock). At 0.03 the view at -1096,-3946 shows a big cluster with shards and arcs, a slab and a
  shard over their scorched mounds. Same view, same build otherwise: avg 59.8 / 1% low 57.8 before, 59.8 / 57.9
  after. Only the biome's Flora list changed: landmark list unchanged (101, E128CB1D).
- **NOT MERGED: F-40 stall footprint.** world-laptop/f40 677bd3b (pushed). With the skirt fix alone (b336426) the
  sheet still didn't show: same OgreCamp stall (-355,-4079, camera from the fire's side), before and after looked
  identical. Second cause: the sheet sloped from -0.1 m at its rim to +0.03 m in the middle, so only the middle
  quarter (under the counter) ever cleared the ground. 677bd3b raises it (rim +0.01, middle +0.08). Now it shows,
  but as a flat, untextured brown slab with straight edges in front of the counter, half buried by the dune
  relief. It doesn't read as trodden ground. Proposal for v0.11: darken the ground in the terrain shader instead (a
  trodden mask round stall props, like roads' ground), or a soft-edged decal. Your call or Zach's; the branch stays
  unmerged.
- NEXT: queue item 4 (worm ground ripples, wip/world-worms) once you hand out the next slot, or the F-40 redesign
  if you pick one. The cave-mouth chunk follow-up stays NEXT for v0.11.

## 2026-10-30: G-29 arch NaN fix brought in, zache-73

- **DONE:** cherry-picked rc/v0.10.0 519fb5b (arch bow sin clamped at 0) onto world-features as 2310533, pushed; file
  still CRLF. Scanned for other Pow(sin/cos, fractional): only FloraGenerator :622 (sin at a segment's midpoint,
  always inside (0, pi)), safe; the rest use integer powers or non-negative bases.

## 2026-10-30: caveapproach hitch merged, zache-73

- **DONE: caveapproach hitch.** world-features fast-forwarded 1cbdd9f -> d14f556 (world-laptop/cave-hitch) and
  pushed, after the profiler's A/B passed: caveapproach worst frame 108-139 -> 24-27 ms, dash 1% low 21.5 -> 41.2,
  glide flat, shots clean. In rc/v0.10.0 (c7410cc).
- F-40 stays on world-laptop/f40 (b336426) until after the release: editor slot, stall shots, then merge.

## 2026-10-30: caveapproach hitch (queue 1) and F-40 (queue 2), zache-73

- **DONE, waiting on the bench: caveapproach hitch.** `world-laptop/cave-hitch` d14f556 (off world-features 1cbdd9f,
  not merged). TerrainStreamingSystem MaxBuildFrames 4 -> 60, kept only as a backstop. Cause: a LOD 0-1 terrain
  chunk at a cave mouth samples MouthCut five times per vertex plus the skirt bisection, ~39 ms on one worker
  (BuildChunkMeshJob takes one chunk per work item). The forced Complete at frame 4 can't help with a work item
  that's already running, so the main thread waited it out (item 10's 37.8 ms wait). Dispose paths reviewed:
  OnDestroy and both settings-change paths (re-bake, new source) go through CancelBuild, which Completes before it
  disposes, and world replacement on host/join destroys the system the same way. Side effects: while a slow batch
  runs, OnUpdate returns early (no unloading, no new batch, no HideFarChunks switch) for up to the batch's own
  length, and Meshing stays true longer (WeatherField caps its wait at 0.3 s). Not checked in an editor: the bench
  build is the compile check; caveapproach, glide and dash with shots are the play and fast-travel check.
- **DONE, not for v0.10: F-40 stall footprint.** `world-laptop/f40` b336426 (off 1cbdd9f, not merged). Why the
  sheet never showed: Stall is Founded, and FloraMeshBuilder.Skirt drops every vertex at y <= 0.001 by 1.6 m,
  which took the sheet's rim (y -0.1, or 0.0 "raised 10 cm") into the ground. Fix: KeepOutOfSkirt(first) leaves
  listed vertex ranges unskirted, and the sheet (re-landed from 7de4b48) uses it. Landmark list unchanged. Needs
  an editor slot after the release: before/after stall shots, then merge.
- **NEXT:** (post-v0.10) cheapen or split the cave-mouth chunk (rows as work items, or MouthCut only where the
  centre sample is near), so it doesn't hold the stream. G-37 decoration rocks: still to make. (`MagneticDebris`
  in ElectricWastelands' Flora list at 0.12 is Form 50, not FloatingRock 54, so it isn't them.) Plan: a new species
  copied from MagneticMonolith, Height 6-15, Variants 4, listed at a low density; needs an editor slot.

## W-08 (2026-11-02)
- **DONE:** #19 ponds fewer and larger (2614 -> 1287 blobs, 4329 -> 3850 wet cells; single-cell 658 of 1287 remain). #44 puffs alpha 0.4 (system existed). #20 v2 ridge chains (ridge height min 13 / median 81 / max 170 m; 127 landmarks, 831FF2EE). #30 flower pair shot. world-features 149bc0a pushed; v1 101 / E128CB1D holds.
- **NOT DONE:** single-cell ponds not fully removed; v2 top-down map blank (4600 m camera); 3 builds used, not 1 (pond design failed stats; first ridge edit misused Mathf.SmoothStep).

## W-09 (2026-11-02)
- **DONE:** #19 ponds 1287 -> 219 blobs (3850 -> 361 wet cells, p90 7 -> 3). #20 v2 top-down ridge map. #44 puffs readable (alpha 0.6, density cut). #39 paving channel plus cobble shader on Cobbles/Flagstones. world-features 0e19871 pushed; v1 101 / E128CB1D holds; one build.
- **NOT DONE:** single-cell ponds still 165 of 219; cobble stones not visible in the town shot (look needs tuning); v2 hash not rechecked.

## W-15 (2026-11-02)
- **DONE:** #23 cave bake 1350 -> 555 ms in the editor (rubble 566 -> 90, plants 379 -> 60; parallel per cave, joined in cave order), player caves stage 1380 -> 591 ms, v2 ready 7.2 s; cave hash unchanged; v2 127 / 831FF2EE holds. #35 BATFOLK-PLAN.md (36 lines; placement does not change v1). #35 `-whitepools` test look. world-features fb6d7ee pushed; one build.
- **NOT DONE:** white pool not judged done: the editor shot reads as milky water, but in the build it reads as a glowing white disc with a hard rim. v1 hash not rechecked in a player (no landmark code touched; caves bake after landmarks).
- **Shots:** PunchyMilestones/world/w15 (v2-white-pool, editor-pool-default, editor-pool-white).

## W-16 (2026-11-02)
- **DONE:** #23 settlements stage ~970 -> ~290 ms in the editor (roads 833 -> 150), settlement hash unchanged (bench B4E4858C9FE1CF46 / roadside 14650FB0739D0383, baked 2433C3F949E9CE3E); v2 player settlements 344 ms. v2 player log 127 / 831FF2EE. world-features d69a710 pushed; one build.
- **NOT DONE:** #35 the rim is soft now, but the build shot still reads as a bright milky blob, not water. v1 player log has no hash line: v1 never logged one. Added the log after the single build, so the next build must confirm 101 / E128CB1D (no landmark code touched).
- **Shots:** PunchyMilestones/world/w16 (v2-white-pool, v2-white-pool-wide).

## W-17 (2026-11-02)
- **DONE:** #35 white pools now darken and stay see-through across the middle (body alpha x0.55), glow only a pale band inside the soft rim, colour/glow toned down; default pools unchanged. Build shot at 800 px reads as a pale translucent pool, floor showing through. Editor cave preview: `CaveTools.PreviewAt` + `PreviewShot` (tracked), editor shot taken before the build. Player logs v1 101 / E128CB1D, v2 127 / 831FF2EE. world-features 60a8ebd pushed; one build.
- **NOT DONE:** nothing from the card. Bat folk not started (out of scope).
- **Note:** first v2 player run lost its shots (shot dir missing); reran the player, no rebuild.
- **Shots:** PunchyMilestones/world/w17 (v2-white-pool, v2-white-pool-wide, editor-white-pool).

## W-18 (2026-11-02)
- **DONE:** #35 bat folk steps 1-2. `BatFolkBaking` stage after caves, own seed stream and blob; picks 1 per 6 qualifying caves by scrambled mouth-cell hash; per-pool white flag replaces `-whitepools` (kept as debug override). Logs: v1 101 / E128CB1D, `[batfolk] caves 0, picked 0`; v2 127 / 831FF2EE, `[batfolk] caves 3, picked 1 ... cave 2 chamber 3001,3,-2135 r 10.1 pool 1`. Cave hash 9CB26E0C6AEB39BA unchanged. Build shots at 800 px: picked pool white, non-picked pool normal blue. world-features 73709c8 pushed; one build.
- **NOT DONE:** nothing from the card. v1 gets no bat folk (no deep big chamber with a pool); rule choice open.
- **Shots:** PunchyMilestones/world/w18 (picked-white-pool, unpicked-pool, editor-picked).

## W-19 (2026-11-02) - #35 bat folk huts and props
- DONE: task 1. NOT DONE: task 2 (the huts don't read in the build shot).
- world-features 4ef8516: `BatFolkSetBlob.Props`, per-site RNG on the bat folk stream, clear of pools and path segments; `BatFolkMeshes.cs`; CaveSystem draws them.
- v2 log: `cave 2 chamber 3001,3,-2135 r 10.1 pool 1 huts 3 props 8`. v1: `caves 0, picked 0` (rule unchanged).
- Hashes: v1 101 / E128CB1D, v2 127 / 831FF2EE, cave 9CB26E0C6AEB39BA (all unchanged).
- Shots: PunchyMilestones/world/w19/w19-chamber-800.png, w19-chamber-up-800.png. The racks (pale hides), baskets and white pool read, and nothing floats.
  The huts hang 8-14 m up, past the lantern, and show only as dark ovals at the top edge.
- NEXT: step 4 light on the huts (or a lower hang or ledge huts), then retake the shot looking up.

## W-20 (2026-11-02)
- **DONE:** #35 huts hang lower: each stalk is sized so the pod bottom sits 3-6 m above the floor (stalk checked for air along its length; bat folk stage only). v2 log `huts 3 props 8 hut bottoms 3.9/4.6/4.7 m` (was 8-14 m up). Build shot at 800 px: three pale pods with their seam bands, racks, baskets and the white pool in one frame; huts read as huts. world-features fb047dc pushed; one build (= fb047dc code).
- **DONE:** the W-19 "2 errors" were the editor's pipeline server failing to answer the `unity command eval` request after the CLI's 30 s timeout dropped the connection mid-build (BasePipelineServer: "connection forcibly closed" + "Cannot be changed after headers are sent"). Harmless to the player. Build eval with `--timeout 600`: `Succeeded 0 errors`. Noted in docs/agents/editor-and-assets.md.
- **DONE:** one report log: W-19 moved here, lanes/world/REPORTS-world.md deleted.
- **Checks:** v1 101 / E128CB1D, v2 127 / 831FF2EE (player logs); cave hash 9CB26E0C6AEB39BA (editor CaveBench, baked = fresh). v1 `[batfolk] caves 0, picked 0`. Compile clean; no Punchy.exe of mine left running. Frame judged in the CaveTools editor preview before the build.
- **Shots:** PunchyMilestones/world/w20/w20-batfolk-chamber.png (build, 800 px: huts, racks, white pool), preview3.png (editor preview of the same frame, brighter lantern), w20-v1-spawn.png (v1 check run).
- **Learned:** docs/agents/world.md (hut hang rule), docs/agents/editor-and-assets.md (build eval timeout).
- **NEXT:** #35 step 4 light is now optional (huts sit in lantern reach; the build is a little darker than the editor); step 5 residents to the animal lane; v1 rule (pool or MinChamber) still waits for Zach.
