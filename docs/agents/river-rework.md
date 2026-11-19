# River rework (design, W-153 follow-up)

Rivers keep making water that stands above its ground: walls of water beside dry banks, and raised blocks of water with
falls on every side. Each fix so far patched one case and the next pass broke another. This plan ties water to the
ground that holds it, so the probe can hold the count of those faults at zero.

## Why water escapes its ground

Five steps set heights on their own and nothing checks them against each other:

- **Bed and water per cell**: BedDownhillJob, BaseLevelJob, CutDams.
- **Lake fixes after the fact**: LowerBreachedLakes, ReshapeLoweredLakes, SettleLakeRivers.
- **Pit fills**: FillRiverPits raises beds under backed-up water.
- **Per-sample carve and water** (RiverData.Consider): water comes from whichever segment carves lowest. FallWater
  (and ChannelWater, until it was reverted) then overrides that water with an upper surface.
- **Later ground passes**: GrandFalls overlay, scarps, notches and roads change the ground after the water is set.

Row 4 measurements on v3:

- 72 of the 147 bed steps of FallDrop or more had no fall described at them. They caused 394 walls and 699 steps.
- On rivers wider than half a cell (~16 m), the next segment down reaches back past the lip with its round cap and
  valley. That digs lower ground under the upper pool. FallWater/ChannelWater then hold the upper water over the dug
  ground: that is the mesa.
- The winner switches between segments where two of them overlap, so the water jumps.
- Lake edges cause 305 walls.

## The model

1. **One profile per segment** owns bed, banks and water together:
   - Inputs: bed at each end, water at each end, width, bank height, and the cross-section shape.
   - Carve and water both come from that profile. A sample takes the profile of its nearest segment, blended over a
     short distance at joins and confluences, instead of the lowest carve of every nearby segment.
2. **Water never stands above the lower bank.** The bake clamps each segment's water to at most the lower of its two
   bank tops, at both ends. At sample time, water is drawn only where the ground under it is lower than the water. A
   pit is filled in the bake (the bed rises to the water) or the water drops; it never floats.
3. **Falls only at real ground steps.** A fall is a profile kind: a lip, a drop, and a foot pool dug at the base. The
   lower segment's profile starts at the pool and never carves upstream of the lip. Every bed step of FallDrop or more
   is either a fall or gets smoothed into a ramp. No step goes undescribed.
4. **The water mesh is built from the same test.** WaterPatchSystem emits a water quad only where the ground under it
   is below the water surface. Bank edges come from where the two surfaces cross, not from the channel width.
5. **WaterTruth wall is a gate.** The test fails on any wall > 0 (then step and dryRun) instead of comparing against
   a baseline we re-set every row. Until the rework lands, the baseline only goes down.

## Passes

| Pass | Fate |
|---|---|
| FillRiverPits | merges into the profile bake (the bank clamp) |
| LowerBreachedLakes / ReshapeLoweredLakes / SettleLakeRivers | one lake step before the profiles: each lake gets a level from its outlet once, and river profiles start from it |
| CutDams | stays, runs before the profiles are built |
| GrandFalls overlay | becomes a chain of fall profiles on its path, with no separate water override |
| FallWater override | goes (the fall profile owns its upper water) |
| Notches (#538) | stay, but are bounded by the profile: they may lower ground, and the water test then decides whether water shows there |

## Size

About 6 rows:

1. Profile struct and bake, with bank clamp and pit merge.
2. Sample from the nearest profile with join blending, retiring the lowest-carve winner.
3. Fall profile and foot pool, and every step described (the row 4 WIP on world/river-profiles is a start).
4. Lake step merge.
5. GrandFalls on fall profiles.
6. Water mesh from the ground test, plus the wall gate at 0.

Each row moves the river layout; landmarks may shift.

## Cheaper option (2 rows)

Keep the current passes but add one final clamp:

- **Bake:** after every pass, lower each segment's water to its lower bank top, or raise the bed under it.
- **Sample time** (Query): drop FallWater wherever the ground under it is lower than the lower water by more than
  half a metre, so water is never held over dug ground.
- **Gate:** make wall a hard gate at its new value.

This removes mesas and most walls. It does not stop new passes from adding faults, and the water still jumps where
segments overlap.
