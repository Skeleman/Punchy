# World lane state

Updated 2026-11-02 after W-27b. Branch world-features @ 96a80de (pushed, includes main 0bd91cb). One build (Builds/Dev).

## Done in W-27b (#23, v2 multiplayer)
- Merged origin/main 0bd91cb (PR #52) with merge commit 704229f. One C# conflict in PlayerMovementSystem.cs
  (W-27 `KeepOutOfTower` next to main's #33 wall-probe block). It was trivial and I kept both sides. 0 compile errors.
- `IslandV2System.Shared` (IslandV2.cs): the first world in a process to bake v2 keeps its blobs. Each later
  world (in-process client, solo, F2 server/client) copies them into its own TerrainSource and logs
  "v2 shared with <world> ... (no rebake)" with the landmark hash. The blobs are Persistent and never
  disposed at runtime, so sharing is safe. A SubsystemRegistration hook resets this for domain-reload-off.
  - So the host now does one bake of about 4 s, in ServerWorld before the local client connects. The client
    copies the data, which takes no measurable time.
- Build test, 75 s each, host plus local join:
  - v2: one "v2 ready" per process. Both sides log 126 / 053C63D5. Host shows players 2, nearest player 3 m.
    The joiner sees the host at 3 m. 0 exceptions.
  - v1 (`-island v1`): both sides log 102 / 51EA6463, players 2, 3 m.
- F2 (host from solo) was tested in the editor only, because the shot list has no host command. ServerWorld
  and ClientWorld both shared the solo bake (same hash), the host stayed in game, and nothing timed out.

## W-27 leftovers (checked)
- Caves: v2 37 caves, 5D90D9CFC5326976 (unchanged by the tower). v1 17 caves, 47C703C0E17F77CB.
- Roads near the tower:
  - v2: nearest town street edge 687 m, nearest road edge 5963 m.
  - v1: nearest road edge 1385 m.
  - No road runs into either tower.
- Salt patch long axis:
  - v2: (0.151, 0.988), from -769 to +413 m past the tower.
  - v1: (0.241, -0.970).
- Far shots retaken from the south (3652,-1245) and the north (3776,-431), at noon in Clear weather.

## Open
- F2 hosting is not covered in a build. A `-host`-style shot-list command would let it be scripted.
- The summit is a plain slab, and the collision only roughly matches the drawn blocks at the tunnel mouths.
- No transport keep-alive was added: one bake per process before connecting is enough for now. A bake that
  ever runs past 10 s (DisconnectTimeoutMS) would still drop the local client.

## Hashes
- v2 126 / 053C63D5, caves 5D90D9CFC5326976.
- v1 102 / 51EA6463, caves 47C703C0E17F77CB.

## Notes
- Shots: PunchyMilestones/world/w27b (800 px), raw shots in w27b/raw.
- Scratch: Punchy-world/Temp/w27b_*. Player logs: w27b_v2host.log, w27b_v2join.log, w27b_v1host.log, w27b_v1join.log.
- The world editor logs to Punchy-world/Temp/editor-w04.log. Stale WorldProbe CS7036 lines there are harmless.
- Run `unity command eval` from bash with single quotes; PowerShell mangles the quotes.
