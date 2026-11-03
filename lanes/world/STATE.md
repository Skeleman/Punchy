# World lane state

Updated 2026-11-02 after W-34. Branch world-features @ 0bdc707 (pushed). One build (Builds/Dev).

## Done in W-34 (bridge posts, cobblestones)
- Bridge railings (FloraGenerator.Bridge): each span used to draw its rail post only at its start, so every span's
  far rail end floated, including the end at the bank. Each side now has posts at both ends, +-3.2 m (RailRun =
  RoadBaking.SpanSpacing 6.4), so neighbouring spans share a post. Rails run post to post. Posts go from 0.6 m
  below the deck to just above the rail. Mesh only: spans, pads and facing are unchanged.
- Cobbles (TerrainSurface.shader): the grid Cobbles() is replaced with CobbleHeight + Cobbles.
  - A domain-warped, size-weighted, jittered Voronoi (0.42 m lattice, weight 0.62-1.38).
  - Smooth-min borders round the corners, and the gap width varies (1.5-4 cm).
  - Domed stones with per-stone height, lean and lumps. The normal comes from finite differences (3 samples).
  - Per-stone hue and shade, a broad patch tint, darkened rims, and dark earth or moss in the gaps.
  - The fade is the same as before (30-80 m).

## Checks
- Rail ends without a post: on v1 (WorldProbe.BridgeRails), 5 bridges and 10 spans; 20 before, 0 after. The largest
  pad step between spans is 0.00 m. v2 was not counted by probe (it needs v2 in play mode). The same code draws every
  span, so v2 also has 0 by construction, and the v2 shot shows posts at all rail ends.
- Build: Succeeded, 0 errors, no "Shader error" in the log.

## Open
- v2 bridges per island not counted by probe (see above). v2 cave hash not re-read: the player log does not print it;
  the change is a mesh and a shader only.
- Cobble cost: 3 CobbleHeight calls (each a 9-cell Voronoi) per paved pixel within 80 m. Not benched.
- From W-28b: a taller dry-dock cave berg; close shots of other big bergs. From W-29: slanted sheet beside the
  main falls, snowy levees, plain gorge walls.

## Hashes (unchanged in W-34)
- v2 126 / 053C63D5 (player log), caves 5D90D9CFC5326976 (not re-read).
- v1 102 / 51EA6463, caves 47C703C0E17F77CB (re-read in the editor).

## Notes
- Shots: PunchyMilestones/world/w34 (800 px), raw shots in w34/raw.
- Scratch: Punchy-world/Temp/w34_*. Bridge probe: WorldProbe.BridgeRails (untracked Assets/Temp/Editor).
- Shots: run `Builds/Dev/Punchy.exe [-island v1] -shotlist <file> -shotdir <dir> -logFile <file> -quit`. v1 and v2
  need separate runs. `-place crossing` does not exist on world-features; use `-place prop:Bridge@x,z` or `-camlook`.
- Lines without `-shot` save stray shot-N.png files; delete them.
- Editor console errors: Temp/w28b_errs.cs (LogEntries via reflection).
