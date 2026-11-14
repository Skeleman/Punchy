# Chemistry bridge (experiment)

A proof of concept: the spell-chemistry core from Spell Chemistry Valley (the `com.anweather.chemistry-core`
package, its rules in `Assets/StreamingAssets/ChemistryRules/`) running inside Punchy's world. Played alone, as a
host or as a joined client, in lockstep through the core's `LockstepSession` (below); nothing is saved.

## What it does

- Once the player has spawned and the terrain has settled (3 s unchanged), `ChemistryBridge` (created by itself at
  scene load, no scene edits) starts the core's **open world** (Spell Chemistry Valley's ADR 0020): 1 m cells on
  Punchy's own grid, cell (x, y) = floor of the absolute x, z (Punchy never shifts its origin), in 64 x 64 chunks keyed
  by layer and place. A chunk exists once it's **filled**: as a player reaches ground never filled, the host (or solo)
  samples it from `TerrainSource` in a Burst job (grass where blades grow, as `GroundDetailSystem` decides; water
  where there's water; rock on steep slopes; dirt elsewhere) and the core stamps the fill on a tick. Chunks round each
  player (the rules' reach, 2) are active; further ones are inactive and packed, and keep what happened to them. There
  is no size and no rebuild: walk away and back, or teleport, and what burned is still there.
- **Layers:** the surface is layer 0; a player more than 2 m under the surface in a cave's (or the sunken temple's)
  air is in layer 1, whose ground is the caves' floors (dirt, cave pools as water, rock where there's no cave). The
  layers never touch.
- It ticks the chemistry at the rules' rate (6 a second), at most 2 ms a frame (a tick can span frames).
- The view is a 256 x 256 window (4 x 4 chunks) round the player, in its layer, moved a chunk at a time as the player
  walks (only the view moves). Each tick it renders the window's filled chunks, active or not, into two 256 x 256 maps (`_PunchyChemMap`: r burning, g burnt, b ice, a frost;
  `_PunchyChemMap2`: r water standing on land, g steam, b smoke; with `_PunchyChemRect`, `_PunchyChemOn`) that the
  shaders read through `Shaders/ChemistryOverlay.hlsl`. The maps are bilinear and `PunchyChemSoft` thresholds them
  near 0.5 with a noise-wandered edge, so ice, scorch, frost and water show soft, organic edges rather than 1 m squares:
  - grass blades (`GroundDetail.hlsl`, `Grass.shader`): burnt grass is short charred stubble, burning grass glows,
    frosted grass whitens;
  - terrain (`TerrainSurface.shader`): scorched where grass burned off, glowing where it burns, white with frost;
    water poured on land is a sheet drawn like a rain puddle (dark film, sky reflection) with damp ground round it,
    and frozen water on land is ice;
  - water (`Water.shader`): frozen water is a sheet of pale ice with a bright, thinner lip at its edge. When fire melts
    it, the map's ice goes and Punchy's water shows again.
- `ChemistryEffects` draws a flame on every burning cell through Punchy's standard fire (`FireSystem`: flame, sparks,
  glow and a little smoke, far flames beyond 70 m, a second lower tongue per cell within 35 m) plus one fire light,
  so the flames follow the fire front and go out with the cells. Gas cells puff soft billboards from two pooled
  ParticleSystems (at most 48 puffs a frame): white steam that rises and thins as the gas does, on the skunk's
  gas-cloud material (`Resources/SkunkGas`, unlit, not faded at the ground), with a burst off each cell the water puts
  out; grey smoke on the breath-puff material (`AmbienceSystem.PuffMaterial`, lit); and a wisp of vapour off ice as it
  melts (cosmetic: the melt itself makes no steam).
- It logs each cast and, once a second while anything has happened, the cell counts (burning, burnt, ice, frost,
  water on land, steam, smoke), the reactions it has seen change the cells since the window was seeded (ice melted,
  fires doused, water boiled to steam, new steam, steam condensed) and its own cost per frame (`[Chemistry]` in the
  console).

## Multiplayer: lockstep (D19 step 3, 2026-11-13)

The cells are shared state, run in lockstep by the core (Spell Chemistry Valley's ADR 0019): one `LockstepSession` a
process (`ChemistryBridge.Lockstep.cs`) steps the world, and every peer runs the same batches, so every peer's cells
are the host's. Solo runs the same session over the core's loopback.

- **The transport** is `NfeLockstepTransport.cs` (about 250 lines): the session's bytes as `ChemMsgRpc` pieces (two
  `FixedBytes510`, 1020 bytes, and a sequence number per link), sent from `NetWorlds.Server` to each connection or from
  the client to its one, at most 24 pieces a link a frame. What arrives is handed to the session from the pump's
  Update, in each link's order. On the host every connection in game but its own client's is a peer (PeerId = its
  NetworkId); its own client takes nothing. The session goes with the worlds (`GameBootstrap.WorldsReplacing`) and a
  new one starts on host, join or solo.
- **The open world, shared** (D20, 2026-11-13; it replaced D19 step 3's one 256 x 256 region). The host (or solo) is
  the core's `IOpenGround` (`PunchyGround`): `Characters()` is every player's layer and cell (each `NetPlayer` on the
  server, or the one player in solo) and `Sample(key)` is a chunk's ground from `TerrainSource`. The core's
  `OpenWorldPlanner` (attached to the session) stamps fills on the `fills` channel and switches on `chunks` as players
  reach new ground, 16 chunks a tick at most, so every peer makes the same chunk from the same bytes on the same tick
  and a client never samples a float for the cells (only heights, for its view). A joiner loads the host's snapshot:
  every filled chunk's ground and its changes. A teleport is a reach that jumps: its chunk is filled and active within
  a tick or two. A cast where nothing is filled yet is refused on the peer that cast it (logged) and not sent.
- **Start-up:** the first fill waits for the terrain to stay the same for 3 s (the first `TerrainSource` is a stand-in).
  A terrain that changes before any cast (and with no peers) starts the session again; after that, filled chunks keep
  the ground they were filled with.
- **Casts and the hands are requests** on the session's `casts` and `combos` channels, never input-component fields:
  the host names the player (`p0` the host, `p` and the NetworkId a client), stamps them, and every peer runs them on
  the same tick. The hands' state is the world's, so its HUD and rumble are the same on every peer for each wizard;
  only the peer whose wizard set a spell off aims it. Effects (flames, steam, ice, craters) follow from the cells.
- **Desync:** the host sends a hash window every 30 ticks; a client that differs logs `desync: ...` (the parts that
  differ) and loads a fresh snapshot. Every peer logs `lockstep <role> tick <t> at <time> sum <hash> ... desync=False`
  every 60 ticks, and each cast as it lands.
- **Tests without keys:** `-chem-script "25:demo; 55:walk 320,0,30; 125:hop 2000,0; 125:cast fire-bolt; 160:quit"`
  (`ChemistryBridge.Script.cs`): timed steps from the session's start (demo, lake, cast, goto, hop, back, walk, quit).
  Every 60 ticks the sum line also gives the chunks filled, active and unpacked, how many were sampled and Unity's
  reserved memory.
- **What differs from solo:** a cast lands on the host's next tick (up to a sixth of a second later, plus the trip to
  the host on a client) instead of at once; a frame behind the batches by more than 6 ticks may tick for 8 ms instead of
  2 to catch up.

## Where the core comes from

`Packages/manifest.json` points at the package by a local `file:` path into a checkout of Spell Chemistry Valley
(`packages/com.anweather.chemistry-core`, with its `dev` up to date). The package declares
`com.unity.nuget.newtonsoft-json` 3.2.2, which Punchy already resolves. Its asmdef also names a
`Prototype.Analyzers` reference (Spell Chemistry Valley's own Roslyn analyzers); Unity drops the missing
reference silently here (no error, no warning).

## First run (2026-11-12, Editor, 1280 x 720)

- Fireball on a lake-side meadow: 19 cells burning at once; after 6 s 130 burning / 370 burnt, after 26 s 405
  burning / 7143 burnt, the ring still spreading through the grass and leaving scorched ground behind it.
- Frost (charged) on the lake: 15 cells of ice; Sleet Storm: 132.
- Cost: about 0.75 ms a frame on average (a tick about 5 ms, sliced to 2 ms a frame; the map about 2 ms, 6
  times a second). No console errors or warnings.

## On a pad: the hands (D16)

Spell Chemistry Valley's one way to cast (its ADR 0016, `docs/design/unified-casting.md`), driven through the core's own
hand logic: `ChemistryBridge.Hands.cs` sends the core's `HandDown` / `HandUp` / `Attune` commands
(`World.ApplyCombo`), the core runs each hand's meter and charge from the rules' hand table (`hold.json` "hands"), and
what it sets off (`FiredCastsHeard`) is aimed at the ground under the screen centre, pulled back to the gesture's range,
and cast into the window. The actions are a map of the bridge's own, built in code; Punchy's `InputSystem_Actions` is
untouched.

| Pad | What it does |
|---|---|
| LT / LB | The left hand's thrust / throw |
| RT / RB | The right hand's thrust / throw |
| A hand's trigger + bumper (inside the window, about 0.7 s) | That hand's sweep |
| Hold a hand's button | It charges (a level every half second, up to 3; the pad hums, and pulses at each level); let go to fire. A tap is charge 1 |
| Both hands on one gesture | They merge into their pair's spell (fire + water thrust: Scalding Steam; throw: Geyser) |
| Hold d-pad up | The element radial: the right stick picks (fire top, water right, earth bottom, air left); let go to attune the hand whose trigger is held, else the left. One element attuned is on both hands. A tap of d-pad up still waves |

The hands start as the rules' table has them: water in the left, fire in the right, so out of the box:

| Hand, gesture | Spell |
|---|---|
| Right (fire): thrust / throw / sweep | Burning Hands / Fire Bolt / Flame Wave |
| Left (water): thrust / throw / sweep | Create Water / Ray of Frost / Sleet Storm |
| Both (fire + water): thrust / throw | Scalding Steam / Geyser |

The HUD (`ChemistryBridge.Hud.cs`, IMGUI through a `GuiHook`) shows two hand icons at the bottom of the screen in
their element's colour, with their buttons (InputGlyphs' icons for the pad in use) and a ring that fills as the gesture
winds up and charges (white, gold, orange by level), greys while the hand cools down and fills in the new element's
colour while it attunes. A merge shows its spell's name between the hands and flashes as it starts and as it goes off.

What Punchy's pad lost to the hands, and where it went:

| Was | Now |
|---|---|
| LB: inventory | D-pad down opens it (B or Tab closes it; inside, d-pad moves between the slots, RB still drops / moves) |
| RB: Use (doors, lamps, talk, pick up, chests) | Y, shared with Interact: a press Use takes (there was something to use) is claimed, and Interact doesn't sit or pet as well (`UseKey.ClaimedPress`) |
| RT: cycle the weather | A long press (half a second) of Select / View; a short press is still Options (the frame cap) |
| D-pad up / down: wave / bow | A tap of d-pad up waves (a hold is the radial); bow is key 2 only |

Menus keep LB / RB for their tabs. The Keys tab of the menu lists all of it (`UseKey.Table`).

## Keys

The keyboard has no hands yet: H, J and L cast straight into the window, as before.

| Key | Spell |
|---|---|
| H | Fireball at the ground under the screen centre (a burst: grass round it catches and the fire spreads) |
| Shift+H | Fire Bolt (one cell) |
| J | Frost, charged: a line of cold through the aimed cell (water freezes, ground frosts over) |
| Shift+J | Sleet Storm (a burst of cold, radius 5: freezes a pond) |
| L | Create Water, charged once (5 x 5): douses fire in a hiss of steam, stands on land as a sheet of water |
| Shift+L | Wave (a line of water rolling out through the aimed cell) |

F, G, R, T and V are the door key choices and K is the weather key, so the bridge stays off them (hence L for water). `-nochemistry` on the command line turns
the bridge off. `-chem-demo` casts a Fireball on the nearest grass (6 m or more from the player) 8 s after
the window is seeded, once, for smoke runs without keys.

## Second round (2026-11-13, Editor, 1280 x 720, a lake shore)

- Sleet Storm twice on the lake: 157 cells of ice, drawn as one wavy, rounded floe (the maps are read at a
  noise-warped point, up to about 1.5 m, then thresholded with a wandering edge).
- Fireball on the meadow above it: a wall of flames along the front, a smoke haze over it; Create Water three times
  on the front: gaps in the flame line, blue-green sheets of water with damp edges, steam rising off them.
- Fireball twice on the ice: a hole melted through the floe each time, open water again.
- Reactions the map saw change cells: 50 melted, 24 doused, 27 boiled to steam, 88 new steam, 12 condensed (Frost
  and Sleet Storm through steam).
- Cost: about 1.05 ms a frame on average (max 1.17) with a 200-cell fire front and 1000 puffs; no console errors.

## Height changes (2026-11-13)

The core's cells have a height in half-metre steps that spells change: Fireball and Meteor blow craters (1 m and 1.5 m
deep in the middle), Molten Boulder and Falling Star small ones, Lower Earth and Earthen Trench dig, Raise Earth, Move
Earth (a tapered hill), Wall of Stone and Stone Ramp raise (the core's `World.Terraform.cs`; the sim's range is -6 to 8
steps). `ChemistryBridge.Terrain.cs` lays them over Punchy's ground:

- The cells keep the steps they're seeded with (0 on land, -2 under water). Seeding Punchy's slopes into the core's
  14-step range would make every cell 1.5 m above the window's lowest a wall (the sim's `blockAt` is 3), so only the
  change is carried over: each map walk compares every cell's height with what was last laid, and a cell's edit is
  (steps now - steps seeded) x 0.5 m. Punchy's own detail stays under it.
- `TerrainEdits` (`Terrain/TerrainEdits.cs`) is a 256 x 256 window of metres, one at each whole metre's centre, sampled
  bilinearly, 0 outside, held in a Burst `SharedStatic` (not a field of `TerrainSource`: systems compare
  `TerrainSource` to find a new world and rebuild everything, and jobs carry copies). `TerrainSource.SampleHeight` and
  `SampleGround` add it last, so every height in the game follows (walking, camera, grass, aim, and `SampleNormal`,
  which is taken from heights). Off, it returns the height untouched: bit-identical (`HeightCheck()`, below).
- The bridge fills one of two buffers and swaps it in, at most every 0.25 s; the one it fills was last laid a swap
  before, so nothing frees memory a job reads, and the rebuild flags below only take effect between batches, after
  any job that read the old ground.
- It flags the changed rectangle (plus a metre) to `TerrainStreamingSystem.FlagRebuild` (the chunks re-mesh, nearest
  first, keeping their old mesh until then), `GroundDetailSystem.FlagRebuild` (the grass tiles scatter again, the old
  blades drawing until then) and, for a change of a metre or more, `FloraSystem.FlagRebuild`: the flora tiles on it
  are dropped and placed again by Punchy's own placement (as a changed keep-out circle replants them), so plants
  re-sit on the new ground, or aren't placed where it's now too steep. Re-placing was the cheaper clean option over
  hiding (no per-plant mask), at the price of the tile's plants popping out for a few frames.
- Recentring the window forgets what moved (the ground goes back, flagged the same way); so does the bridge going off.

Checked (Editor, 1280 x 720): `HeightCheck()` samples 131,072 points (SampleHeight and SampleGround, in Burst) with
the edits on and off: 0 differ with nothing cast; after nine casts 126 differ, all where the edits are non-zero. A
Fireball crater 1 m deep the player walks down into (its y follows the ground 0.75 m below the rim); a Meteor crater
1.5 m deep (the chunk mesh's vertex there 1.12 m down, as the edits say); three Lower Earths on a chunk border
(x = -192): the vertices of both chunks equal `SampleHeight` (no seam), and the grass is scattered again into the pit
(its blades 1.2 m lower); Create Water poured in stays (16 units in each pit cell 25 s later); Move Earth's hill
walked up. Wall of Stone raises 3.5 m of rock, but Punchy's terrain never blocks by slope (only props and cave floors
do), so the player walks up and over it. The ground costs nothing measurable a frame (the compare rides on the map
walk; a swap is a 256 KB copy, a few a second at most); the bridge stays about 0.75 ms a frame.

## Removing it

0. Undo the pad rebinds (each is marked "Spell chemistry"): `UseKey` back to RB in `Interact/Doors.cs`, the inventory
   back to LB in `Items/Items.cs`, the weather back to RT in `Settings/WeatherKey.cs` and Options in
   `Settings/FrameRateCap.cs`, the d-pad emotes in `Fauna/InteractionInputSystem.cs`, the right stick in
   `Camera/ThirdPersonCamera.cs`, and the Y in `Interact/FolkTalk.cs` and `FolkTalk.Test.cs`.

1. Delete this folder (`Assets/Game/Scripts/ChemistryBridge/`) and `Assets/StreamingAssets/ChemistryRules/`.
2. Remove the `com.anweather.chemistry-core` line from `Packages/manifest.json`.
3. Delete `Assets/Game/Shaders/ChemistryOverlay.hlsl` and its hooks (each is marked "Spell chemistry"): the include
   and two lines in `GroundDetail.hlsl`, three lines in `Grass.shader`, the include and the chem lines in
   `TerrainSurface.shader`, the include and the ice block in `Water.shader`.
4. Delete `Assets/Game/Scripts/Terrain/TerrainEdits.cs` and the height hooks (each is marked "Spell chemistry"): the
   `TerrainEditLayer.Add` on SampleHeight's two returns and the line after `Settle` in SampleGround
   (`Terrain/TerrainSource.cs`); `FlagRebuild`, `FlagMoved` and its call in `Terrain/TerrainStreamingSystem.cs`;
   `FlagRebuild`, `stale` and their four uses in `Terrain/Detail/GroundDetailSystem.cs`; `FlagRebuild`,
   `OnMovedGround`, the `|| movedRects...` in `Stream` and its `Clear` in `Flora/FloraSystem.cs`.

With the bridge gone or off, `_PunchyChemOn` is never set (an unset global float is 0), so the overlay reads 0 and
every shader draws exactly as before; and `TerrainEdits` is off, so every height is exactly as before.
