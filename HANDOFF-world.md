# Handoff: world lane (laptop, from zache-73), 2026-10-31 (after slot 2)

Supervisor: the laptop session "Unity build analytics supervisor setup" (kind-possum); tasks by message and in
QUEUE-world.md; report per batch in REPORTS-world.md (LF, newest first) plus one message. One Unity at a time on the
laptop: open Unity (or bench builds) only in a slot the supervisor gives you, and say "Unity closed" after.
Worktree: C:\Users\zache\Projects\Punchy-world. Zach's feedback list: FEEDBACK-v0.10.md (W rows are this lane).

## Branches
- **world-features b870008** (pushed): 3976d17 + world-laptop/z30-more: `-place prop:` probe, Z-30 grain redone
  (632f479) and more timber marked (69f3185), `-meteors` and `-caveshards` probes. Built clean (as 8b0a657, same
  tree); 0 exceptions in 7 look runs.
- animal-behaviours 9b7459b + world-features b870008 compile together (tmp/animal-world-check 5f4c917, built clean,
  then deleted; not merged). world-laptop/probe and z30-more are merged and deleted. b/bench-runner db3c1f2 has
  `-benchday`.

## Next slot
1. Z-10 river under fog: still not judged. Needs a clear (no rain) fog view of a river; try `-benchday Overcast` at
   dawn by a FoggyMarsh stream (place FoggyMarsh, -304,817, streams to its east), or a river with fog wisps.
2. Z-11 in heavy rain: light rain showed no screen-space image on the planks; stone legs look glassy dry and wet
   (dither or the wet sheen?). Rain reaches the village square ~90 s into a Partly cloudy, seed 4 run.
3. Cave-mouth shards: run with `-caveshards` and a close `-camlook` at a known shard spot (the item-15 shots were
   from an older island; find one first). Magenta = walls no cave owns.
4. Z-09: no shooting star caught yet with `-meteors` (6 frames); a burst of 20 one second apart would.
5. Z-30: look at the newly marked timber close up (hall walls, inn posts, bridge planks, pen: no Pen placed on this
   seed near the ogre camp, `-place prop:Pen` found none).
6. Z-31/Z-32/Z-34/Z-35 looks (targets now easy with `-place prop:` and the coordinates in REPORTS).

## Open rows
- Z-31 vertical lines in close-up detail on buildings: look first (suspect triplanar detail texture seam).
- Z-32 stone house lit/dark bands: look first (normals or the built double-sided flip).
- Z-34 cave stream meets pool / striping; Z-35 waterfall top gap + faster fall (effect Lifetime) + spray: look first.
- Z-30: grain redone and more timber marked (slot 2); next, a close look at the new timber (Next slot 5).
- Cave-mouth grey shards (above). P3 rows (Z-54, Z-60..64) need a design note first.

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
- `-meteors`: a meteor shower (and aurora) every night. `-caveshards`: ownerless cave walls magenta.
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
- Paw prints can sit on top of a bench a dog walks under (TraceSystem Underfoot, animal lane): not a wetness image.
