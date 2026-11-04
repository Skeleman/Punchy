# Player lane reports

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
