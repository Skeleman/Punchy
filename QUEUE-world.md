# World queue (supervisor kind-possum → world lane, laptop session zache-73)

Pull `coordination` before each task. Report DONE / FAILED / BLOCKED / NEXT in `REPORTS-world.md` (newest first,
one entry per batch) and by message. Branch: `world-features` (yours now; the main-PC world session has stopped).
Work on world-laptop/<item> if you prefer, and merge into world-features after pulling. **Unity: one at a time on
the laptop.** Open an editor only when the supervisor gives you the slot, and say when you close it.

## Queue (top first)

0. **Zach's v0.10 playtest: FEEDBACK-v0.10.md.** Your rows are lane W (and shared ones). Order: P1 first (bugs), then P2, alongside the current item 1; P3 items need a design note and the supervisor's OK. Report by Z-ID.
Done since v0.10: cave hitch (d14f556), F-40 trodden mask (c4d946d), G-37 rocks (6464ce7), worm marks (d81cb06).
Waiting on the profiler: cave-chunk 256e6a9 (item 15), blocked-cache 3d874dc (item 16), worm-trail shader (with zache-db).
1. **Graphics settings: distance sliders (Zach, 2026-10-30).** Today every draw/detail range is a hardcoded const or
   component default (FloraSystem layer Radius 260, GroundDetail Grass 60 / Pebble 45, LandmarkEffect Range 700 /
   Flame 150, CritterSystem 70, CreatureRenderSystem FootDetail 40 / TorchFlame 150 / Lantern 60, BurrowMarks 150,
   terrain LOD ranges, shadow distance...). Zach wants them parameterised, with good defaults for this laptop, and
   sliders in a graphics settings panel so a stronger PC can push them out and a weaker one pull them in.
   - **Design first** (a short note in REPORTS-world, then the supervisor's OK): a `GraphicsSettings` (static,
     PlayerPrefs-backed, applied live) with a few grouped sliders, each a multiplier on the existing defaults (for example
     View distance: terrain LODs + flora layers + landmark effects; Detail distance: grass, pebbles, foot detail,
     burrow marks, critters, flames/lanterns; Shadows: URP shadow distance/cascades), plus Low/Medium/High/Ultra
     presets. Each range reads `default × multiplier`, so behaviour at 1.0 is unchanged.
   - **Rendering only.** Never scale anything that the simulation or netcode depends on: FaunaDirector spawn Radius,
     CreatureRelevancySystem's 200 m, collision ranges (FloraSystem.CollisionRange, CaveSystem.LoadRadius for
     walls), anything seed-derived. Two players with different settings must see the same world and stay joinable
     (no fingerprint change).
   - **UI:** a panel on a key (or in the F2 panel's style), sliders with live apply; a `-gfx <preset>` and
     `-gfxset name=value` flag for the bench runner.
   - **Split:** the world lane owns the settings class, UI and the terrain/flora/ground/landmark/weather ranges;
     the animal lane wires the creature render ranges (CreatureRenderSystem, TraceSystem, CritterSystem) to it.
   - **Defaults:** the profiler sweeps the presets on this laptop (townwalk, glide, caveapproach, a forest view) and
     picks the default that holds J-006 (1% low at cap 60) with headroom; Medium = today's values unless the sweep says
     otherwise.
- Not now: G-22 and G-32 (wait on Zach for a spot), the gold-leaf leak (ask Zach which build and where).
- No landmark placement changes before v0.10 ships.
