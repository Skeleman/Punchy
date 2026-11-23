# Punchy v0.18.0

## New since v0.17.0

### Performance
- **Fewer hitches while travelling**: river waterfall shapes are measured once per terrain, and the search for water seeps runs on a worker thread, instead of both stalling the frame when new ground comes in.
- **Terrain meshes faster after spawn and while moving**: far terrain chunks no longer shade contact shadows for plants too small to see at their distance, and town ground skips grid cells with no plots or streets.
- **Less work standing still**: terrain and plant streaming stop rescanning their tiles while nothing in view can change. At a town, that frees about 3 ms of main thread a frame.
- **Faster shader compiling**: the sky haze has a simpler clear-air form that compiles quickly.

### Look
- **Snow and ice**: snow ground has wind ripples, sastrugi, hollows and glitter; glacier slopes have rime grain, and frost shares the same glint.
- **Water seeps**: wet, mossy streaks run down the rock where water seeps out.
- **Volcanic caves**: basalt columns, sulphur crust and ember cracks that keep their glow at a distance; cave moss grows only near light or damp.
- **Vents and caves**: soft rising wisps from vents, cave-foliage leaves, and cave skins that take shadows properly.
- **Lava and toxic pools**: coarser crust normals and a far form for lava; toxic pools get the same detail pass.
- **Detail fades by screen size** on creatures, plants, terrain, water and cloud edges, so small things fade out evenly at any view distance.
- **Waterfalls**: the fall's clip box now stops at the foot of its face.

## Known
- The first few seconds of loading have one long frame (about half a second) while the caves set up; it doesn't happen in play.
- The first start bakes the island (about a minute on a laptop) because this build doesn't ship a pre-baked island file; later starts load it in a second or two.
