# World queue (supervisor kind-possum → world lane, laptop session zache-73)

Pull `coordination` before each task. Report DONE / FAILED / BLOCKED / NEXT in `REPORTS-world.md` (newest first,
one entry per batch) and by message. Branch: `world-features` (yours now; the main-PC world session has stopped).
Work on world-laptop/<item> if you prefer, and merge into world-features after pulling. **Unity: one at a time on
the laptop.** Open an editor only when the supervisor gives you the slot, and say when you close it.

## Queue (top first)
1. **caveapproach hitch (~105 ms worst frame every run).** BENCH-REPORTS item 10: on the spike frames
   BuildChunkMeshJob runs 39 ms on workers while the main thread waits 37.8 ms inside SimulationSystemGroup:
   TerrainStreamingSystem completing a mesh batch on the main thread, most likely the forced Complete after
   MaxBuildFrames (4). Fix: smaller batches while the focus moves fast or nears a cave mouth, or don't force-complete
   (start the next batch late). Read-only prep is fine before your slot. The supervisor benches caveapproach + glide
   after.
2. F-40 stall footprint.
3. G-37 small floating rocks as a biome decoration (no landmark change: keep the hash E128CB1D).
4. Worm ground ripples (terrain side of the animal lane's wip/worms; your wip/world-worms has the marks).
- Not now: G-22 and G-32 (wait on Zach for a spot), the gold-leaf leak (ask Zach which build and where).
- No landmark placement changes before v0.10 ships.
