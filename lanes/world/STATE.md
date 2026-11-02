# World lane state

Updated 2026-11-02 after W-26. Branch world-features @ afc068c (pushed). One build (Builds/Dev).

## Done in W-26
- Rows 1-2 (#23, biomes bleeding into mountains).
  - Cause: the v2 map paints Mountains only where the ridge amount passes 0.45, which is just the crests. The
    ridge height (and the 225 m blurred shape) still lifts the ground well past the crests, so the lowland
    surface covered about half of each flank's climb.
  - Fix: `TerrainSource.HighlandSurface` (map mode only). Where a ridged biome with base + relief >= 200 m
    lifts the ground past 40 m (full at 52 m, +-6 m noise wobble), it takes the surface.
  - Island-wide (24 m grid), before -> after:
    - Mountains shape >= 0.5, surface mostly another biome: 2.7% -> 1.3%
    - lift >= 40 m, surface mostly another biome: 13.0% -> 3.4%
    - flank (lift 20-80 m), surface mostly another biome: 47.8% -> 40.0%. Most of this is foothills at
      20-40 m, which stay lowland by design.
  - Edge band (weight 0.9 -> 0.1) at 3 edges:
    - -2128,392: 16 m -> 4 m. The edge stays put, because the ground drops below a 40 m lift right there.
    - -904,224: 24 m -> 8 m, and the edge moves 36-44 m further out (uphill turned mountain).
    - -3592,608: small change.
  - Shots: before (editor) shows green and fungal patches far up rock. After (build): rock up the flanks.
    Pines on the crests and upper flanks are the Mountains biome's own Pine layer, not lowland bleed.

## Open
- Some edges are now sharp (4-8 m) where the ground falls steeply at the map edge. Widening the blend
  (HighlandFull 60+) or lowering HighlandStart (30-35) is a one-line tweak, if Zach wants more mountain.
- Pines along the crests make some ranges look forested from below. Thinning them would be a Mountains
  asset edit (it moves landmarks).

## Hashes
- v2 was 125 / AFA9CC63, caves D531293D622E785A. Now 125 / D709D95C, caves 5D90D9CFC5326976 (editor).
  - Lost: Grotto -1086.4,2981.1 (a surface hollow landmark, not a cave). New: CrystalGrotto -0.1,-406.2.
  - Caves 37/37 (pools 18 -> 17). Settlements unchanged. Bat folk unchanged (cave 1, huts 3).
- v1 101 / E128CB1D: not re-run. The fix only runs in the biome-map branch of SampleBiomes.

## Notes
- Shots: PunchyMilestones/world/w26 (800 px), raw shots in w26/raw.
- Scratch: Punchy-world/Temp/w26_*.cs.
  - w26_m.cs: WorldProbe.MountainStats on the spots in w26_spots.txt.
  - w26_go.cs / w26_shot.cs: flank framing; the camlook lines go to w26_cams.txt.
- The player only logs the hash with a bench run; this shot run didn't print it.
