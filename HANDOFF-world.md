# Handoff: world lane (laptop, from zache-73), 2026-10-31 (after slot 3)

Supervisor: the laptop session "Unity build analytics supervisor setup" (kind-possum); tasks by message and in
QUEUE-world.md; report per batch in REPORTS-world.md (LF, newest first) plus one message. One Unity at a time on the
laptop: open Unity (or bench builds) only in a slot the supervisor gives you, and say "Unity closed" after.
Worktree: C:\Users\zache\Projects\Punchy-world. Zach's feedback list: FEEDBACK-v0.10.md (W rows are this lane).

## Branches
- **world-features b39522b** (pushed): b870008 + world-laptop/slot3 (stone house walls flat, Z-34 cave streams, Z-35
  falls, test flags -weather/-place cavestream/-camrel/-waterprobe). Built clean as c7bb5f3 (same tree); 0
  exceptions in 6 look runs. world-laptop/slot3 is merged and deleted.
- animal-behaviours + world-features compiled together at 9b7459b + b870008 (slot 2); not rechecked against b39522b.
  b/bench-runner db3c1f2 has `-benchday`.

## Next slot
1. Z-34 leftover: the stream ending at (-1454, 37, -1153) has no pool (CaveBaking.PlaceStreams leaves it out when
   RimHolds fails), so it stops mid-floor. **Zach's call (2026-11-01):** a cave stream may end without a pool only where it drops into a hole or crevice. Otherwise give it a pool, or end it at a hole/crevice.
2. Z-30: hall and inn posts close up (only seen at 9 m); Pen still not found on this seed.
3. Z-10: a non-marsh river under fog: MarshFog forced over the River place showed no fog. Try `-weather Rain` at dawn
   or find which preset fogs there.
4. P3 rows (Z-54, Z-60..64) need a design note first.

## Open rows
- Z-31/Z-32: fixed for stone houses (flat walls). Other four-sided smoothed tubes remain (pyramid roofs, chimneys)
  but showed no bands.
- Z-34: lines and pool tongue fixed; one stream without a pool (Next slot 1).
- Z-35: speed from Lifetime (2.5 s), lip spray, no top gap seen. Zach to judge the speed in play.
- Z-09, Z-10 (marsh), Z-11: passed by sight (REPORTS slot 3). Cave-mouth shards: none found anywhere with
  -caveshards; ask Zach for a spot if he still sees them.

## Key files
- Flora models: Scripts/Flora/FloraGenerator.cs (form builders: Stall, Bench, Fountain, Smithy, RailFence, Signpost,
  mushrooms ~1150, gabled roofs ~3150/3220; Founded(); ToMesh flexibility), FloraMeshBuilder.cs (Vertex marks: Bark +
  0.08 strata, 0.25 seams, 0.4 veins, 0.02 wood; Skirt; Sheet/Box/Beam/Tube/Blob; Cloth).
- Placement: Flora/FloraPlacement.cs (Place(..., footprint), Blocked, RockAt, OnBuiltPlot, OnRoad, RockTint, FloraRules).
- Flora draw: Flora/FloraSystem.cs (layers, ScaleRanges, basins = fountain water, bolts); Shaders/Flora.shader
  (Frag: weathering, wood grain, ember slivers, grounding) and Flora.hlsl (wind, cloth).
- Landmarks: Terrain/Landmarks/LandmarkData.cs (Ground, TroddenGround, pools), LandmarkBaking.cs, LandmarkType.cs.
- Settlements/roads: Terrain/Settlements/RoadBaking.cs (ClearPlots), RoadsideBaking.cs, SettlementData.cs.
- Terrain: TerrainSource.cs (SampleSurface, MouthCut), TerrainStreamingSystem.cs (ApplyViewDistance, MaxBuildFrames),
  BuildChunkMeshJob.cs; Caves/CaveSystem.cs (Look(), MakePoolMesh, plants/drips).
- Settings: Scripts/Settings/GraphicsSettings.cs (View/Detail/Shadows, F5 panel, -gfx/-gfxset), Overlay.cs.
- Sky/water: Shaders/Sky.hlsl (cloud march, shooting stars), Water.shader (queue Transparent-10), LandmarkPlume.shader +
  LandmarkEffects.hlsl (bubbles).

## How to test without the editor
- Build: from Punchy-wt/Tools/bench, `B=$(pwd) bash -c ". ./lib.sh; build <commit> Build Bench-<commit>"` (~4 min; builds
  in Projects\Punchy and restores it). Local commits work (shared object store). Grep the build log for
  "Shader error" as well as "error CS": a shader error still says Succeeded.
- **Find a target:** `-place prop:<Species>` (any flora species: Bench, Stall, Smithy, Inn, Hall, StoneHall, Fountain,
  ElfBench, EmberPine, Woodpile, Bridge, AncientTree...), `@x,z` to measure from a point, `#n` for the nth nearest.
  It logs 8 with coordinates and frames one. Coordinates from slot 2 are in REPORTS.
- **Many shots in one session:** `Punchy.exe -benchroute -benchstops here -benchlong here 900 -weatherseed 4 -time 12
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -shotdir <dir> -shotlist <dir>/shots.txt [-benchday
  Stormy] [-caveshards]`. Each list line: `-goto x,z -time h -camlook x,y,z,tx,ty,tz -wait s -shot name` (first line
  `-wait 40` for the world and the route's weather). Kill by PID when the log says "shot list done".
- `-meteors`: a meteor shower (and aurora) every night. `-caveshards`: ownerless cave walls magenta (and logged).
- `-weather <Preset>` (per line): that weather forced over the player (Rain, Storm, MarshFog, Overcast...; Clear
  clears). `-place cavestream[#n]`: end of the nth nearest cave stream. `-camrel dx,dy,dz,tx,ty,tz`: camera relative
  to the player. `-waterprobe 1..4`: water as foam / thickness / alpha / foam parts (shore, churn, streaks).
- Waterfall: landmark ~(627.5, 2526), pool centre (629, 2518): `-place Waterfall:centre -camrel 6,4,-15,-1.5,12,9`.
- OgreCamp is clear (fire ~ -359,-4061; stall -355.6,-4079.4); GoblinCamp sits in fog; elf grove forum at 260.8,3250.9.

## Gotchas learned today
- Git Bash `sed -i` turns CRLF files LF: edit with Python in binary/newline='' and check `git ls-files --eol`.
- Settlement streets/squares repaint ground after landmarks: anything that must show in a town goes after them.
- Probe builds (Debug.Log blob data, exaggerated values) settle "why doesn't it show" faster than tuning.
- Unity fixes a global array's length at first set (an editor needs a restart after a slot-count change).
- `git commit -p` with piped input does commit; don't chain git after a Python assert; check `git log` before push.
- GitHub pushes sometimes fail with 500s: retry. Branch switches can take >2 min (hooks): use long timeouts.
- Benches: smoke can say NOT CLEAN from round order alone; ABBA `ab` decides.
- Window grabs (CopyFromScreen) from a background shell capture whatever window is on top, and keystrokes go
  to it too: use the bench route's in-game `-benchshots` (one shot per stop; `-camlook` holds the camera).
- `line` is an HLSL keyword (geometry shaders): a variable called line fails the Flora shader.
- A static field placed between `[BurstCompile]` and its struct is a compile error (the attribute lands on it).
- The HUD covers the top-left of in-game shots: frame targets right of centre or below, or add `-nohud` per run.
- In a shot list, `-place cavestream#n` counts from where the player is, so put a `-goto` before each one.
- A probe that splits a shader term into colour channels (`-waterprobe 4`) found Z-34's cause after two wrong guesses.
- Paw prints can sit on top of a bench a dog walks under (TraceSystem Underfoot, animal lane): not a wetness image.

## Restart notes (supervisor, 2026-11-01 ~00:00)
- Zach is restarting this lane from this handoff. Read it, then the newest entry in your REPORTS file, then SUMMARY-away.md (what happened while he was away).
- Zach's decisions: rebuild the v0.11 candidate with animal c810406 (the InteractRpc fix). The default graphics preset stays Medium. Z-34: a stream may end without a pool only where it drops into a hole or crevice (Zach). P3 rows still need design notes.
- Unity: the supervisor's v0.11 rebuild and gate holds the slot first (about 4 h). Do code-only work until the supervisor gives you a slot.
- Launch builds only from Builds/Bench. Starting an exe from an archive folder (Builds/bench-<sha>) set off a Windows firewall prompt that stalled every bench on 2026-10-31.
- Token habits that worked: batch shell calls (several greps in one call), one git call per merge step, no ack-only messages, and don't hand back while your own background runs are still going.
