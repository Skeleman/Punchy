# Animal queue (supervisor kind-possum → animal lane, laptop session zache-db)

Pull `coordination` before each task. Report DONE / FAILED / BLOCKED / NEXT in `REPORTS-animal.md` (newest first,
one entry per batch) and by message. Branch: `animal-behaviours` (yours now; the main-PC animal session has stopped).
**Unity: one at a time on the laptop.** Open an editor only when the supervisor gives you the slot, and say when
you close it.

## Queue (top first)

0. **Zach's v0.10 playtest: FEEDBACK-v0.10.md.** Your rows are lane A (and shared ones). Order: P1 first (bugs), then P2, alongside the current item 1; P3 items need a design note and the supervisor's OK. Report by Z-ID.
Done since v0.10: test hooks (3025dbf), brain-sample markers (item 13/14 captured), worms (149e4b0), trunk-hug
(2272733), trunk-lean (fade08c), worm-look (d83b1ee).
1. **Worm trail of bumps (Zach):** wip/worm-trail 18f1b3b + world-shader wip/worm-trail-shader 92a9602 (zache-73 OK'd).
   Merge both together once the profiler's cost check (slot2 "worm") passes.
2. **Graphics settings (Zach):** after the supervisor OKs the world lane's settings API (QUEUE-world item 1), wire the
   creature render ranges as default × multiplier: CreatureRenderSystem FootDetail/TorchFlame/Lantern/Colony/CaveFish
   (Swarm: draw use only, not anger/chase; leave BehindCull), TraceSystem, and Burrow Range from the same setting
   as the shader gate. Rendering only: never FaunaDirector's spawn radius or relevancy.
3. Later: NPC climbers following trunk lean (FloraShape has no mesh data); decide from the trunk-lean shots.
- wip/brain-sample: merge animal-behaviours in, or retire it now that item 14 is captured. Never merge the profiler's
  wip/brain-blocked-* branches.
- F-02 held.
- Supervisor 11-12: #279 (W-138 tunnels) merged and touches shared flora files, additively: FloraGenerator.Kit.cs (Flame beside Lamp, 2 places), FloraPlacement.cs (OnRoad, 3 lines: props with PierReach exempt from the road cull), Flora.shader (`_FloraInCave` line), new FixtureTunnelLantern.asset. Rebase open flora work on main. Also: job 59b shows TraceSystem spikes (1.7-3.4 ms) in the first 3 s after leaving a tunnel bore; this joins the profiler's row 5 (TraceSystem to a Burst job) when that gets carded.

- **Rule 24 (11-12, #282):** NaN-safe shader/mesh maths; every shader, material or generated-mesh row adds `-sparkleprobe` NaN count 0 (with `-stopnan 0` once R-18 lands) to its done-when. R-18 (world agent, rendering) turns on Stop NaN for the main camera and hunts the horizon/mountain-edge NaN left over from A-96.
- Supervisor 11-13 (#298): fauna lane, after A-107 comes **A-108** (life in the distance: herds, flocks, town life; drawn only, no sim or network change).
- Supervisor 11-13: new rules 25-28 in COORDINATION.md (far view first; one lighting path; no new item/save stand-ins until P-35; refactors have right of way). Read "What we are building" before your next card.
- Supervisor 11-13: #305 merged (A-106, closes #69). Folk lane next: **A-109** (towns alive from afar: far figures, home lights and smoke from the household records; takes A-108 row 3). Fauna lane's A-108 keeps rows 1, 2, 4.
- Supervisor 11-15: folk lane next: **A-128 (#392)**, worn cloth follows the legs. Row 1: bring the PoseGear apron and the ogre guard cape onto the FollowsLeg path from A-127, so no legs poke through while walking. Shots before and after: a walk at cycle 0/0.25/0.5/0.75, front and side. Fingerprint re-pinned if limb data changes. Check solo, host and client. Row 2 (only if row 1 is cheap): the A-120 parallel, PoseGear carried items drawn by their own path vs HeldPoseSystem; a design note only, no code. Branch folk/cloth-legs from main 37750588.
- Supervisor 11-15: folk lane after A-128: **A-129 (#393)**, dialogue archive. Row 1: live reload of dialogue.json (file watch or debug key), errors named and old lines kept, works in a build. Row 2: move hard-coded folk lines into the archive. Row 3: a design note for dialogue options (data shape, UI owner = player lane, wire needs as a rule 11 ask). Branch folk/dialogue-archive from main. Don't touch the player talk UI files.
- Supervisor 11-15 (v0.16 playtest): **A-130 (#404)**, fauna/flora: dune palms in groves with sparse stretches and clearings (low-frequency density field), not everywhere. Check the same field serves other biome tree densities (rule 33). Aerial before/after over the dunes; flora fingerprint re-pinned.
