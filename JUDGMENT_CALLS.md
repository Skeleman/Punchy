# Judgment calls

Every decision the supervisor asked Zach for. **Before you ask Zach anything, look here first.** If a past
decision covers the question, follow it, and add 1 to its *Revisited* count. Ask again only if something has
changed, and say what.

Agents send judgment calls to the supervisor (claude-41 [21fe18]), not to Zach. The supervisor checks this log,
asks Zach if needed, and records the answer here.

## Open

(none)

## Decided

### J-021: Leaf-card tree canopies?

- **Asked:** 2026-10-27, by the supervisor, after the world session's experiment (113c608, 86951d0).
- **Also comes up when:** any change to how tree crowns are built.
- **Answer (2026-10-27):** drop it. Revert the cards; reshape the great tree's crown with spheroids instead.
  Follow-up the same day: try a second experiment, a solid spheroid core plus a fringe of alpha-cut leaf-cluster
  cards (inspired by Benedict Targett's Abundant Skies trees).
- **Why:** Zach's pick after the compare sheet (cards read see-through and spiky).
- **Revisited:** 0.

### J-018: In what order does play-test feedback get worked?

- **Asked:** 2026-10-27, by the supervisor, after Zach's ~40-item play-test list.
- **Also comes up when:** any batch of feedback, or a mix of bugs and new content.
- **Answer (2026-10-27):** bugs first, then look polish, then new content.
- **Why:** (recommendation).
- **Revisited:** 0.

### J-019: How do we get realistic buildings?

- **Asked:** 2026-10-27, by the supervisor. The friend's repo has no licence.
- **Also comes up when:** borrowing anything from anweather/5e-compatible-game.
- **Answer (2026-10-27):** write Punchy's own modular buildings, inspired by the friend's ideas; no copied code.
- **Why:** (recommendation) no licence needed.
- **Revisited:** 0.

### J-020: Where do ghosts appear?

- **Asked:** 2026-10-27, by the supervisor.
- **Also comes up when:** other night-only or spooky creatures; spooky landmarks.
- **Answer (2026-10-27):** at night in the foggy marsh, the poison swamp, and near ruins and graveyards (new
  landmarks).
- **Why:** Zach's pick.
- **Revisited:** 0.

### J-017: What does the animal session do after cave drinkers?

- **Asked:** 2026-10-27, by the animal session (via the supervisor).
- **Also comes up when:** ordering the animal roadmap: small cave-fauna items vs Interactions vs Imps.
- **Answer (2026-10-27):** Interactions next (pet, carry, hunt), then swallows and glow moths, then Imps.
- **Why:** (recommendation) biggest change to play, and nothing blocks it.
- **Revisited:** 0.

### J-013: Should bats be redesigned to look like bats?

- **Asked:** 2026-10-27, by the supervisor, from the bat screenshots (62eb225).
- **Also comes up when:** any flying creature built from bird parts: giant birds, flying serpents.
- **Answer (2026-10-27):** yes: scalloped skin wings on finger struts, big ears, a short snout, tucked legs,
  reusing shared parts. After the dog fix.
- **Why:** (recommendation) in flight they read as birds.
- **Revisited:** 0.

### J-014: Where do COORDINATION.md and JUDGMENT_CALLS.md live?

- **Asked:** 2026-10-27, by the supervisor.
- **Also comes up when:** any shared doc that every session, including the other PC, must read.
- **Answer (2026-10-27):** on a `coordination` branch the supervisor pushes from its own worktree, holding only
  these files. Sessions fetch it and read `origin/coordination`.
- **Why:** (recommendation) no one's working tree or history is touched.
- **Revisited:** 0.

### J-015: Do the two local sessions share one Unity editor slot?

- **Asked:** 2026-10-27, by the supervisor: two editors open with 3.1 GB of RAM free.
- **Also comes up when:** a session opens another editor, a build, or a player next to a running editor.
- **Answer (2026-10-27):** no. Both keep their editors. The supervisor watches free memory and steps in only if
  it drops under about 1.5 GB or something crashes.
- **Why:** (recommendation) keeps both sessions moving.
- **Revisited:** 0.

### J-016: How much coordination process?

- **Asked:** 2026-10-27, by the supervisor, after Zach shared the coordination skills.
- **Also comes up when:** merge order, branch rules, review before merge.
- **Answer (2026-10-27):** light: lanes and file ownership, this log, one-screen updates and idle tracking.
  Sessions keep their branches and merge each other as now. No merge turns.
- **Why:** (recommendation) little disruption.
- **Revisited:** 0.

### J-001: Where does the Glacier biome go?

- **Asked:** 2026-10-27, by the world session. Adding a biome re-bakes and can reshuffle the island.
- **Also comes up when:** adding any biome; any change to the climate ranges.
- **Answer (2026-10-27):** high altitude only: a narrow cold, high climate range, so the rest of the island
  mostly stays put.
- **Why:** Zach: "Glacier should be high altitude."
- **Revisited:** 0.

### J-002: Which biome is the "mystical forest"?

- **Asked:** 2026-10-27, by the world session.
- **Also comes up when:** crystal and metallic caves; flying serpents.
- **Answer:** SparklingForest.
- **Why:** (recommendation, not contradicted).
- **Revisited:** 0.

### J-003: Which session is the profiler?

- **Asked:** 2026-10-27, by the supervisor, after two sessions claimed the role.
- **Also comes up when:** routing performance or code-quality findings.
- **Answer (2026-10-27):** "windows-7l945c9-kind-possum" reviews both branches. "Unity multiplayer debugging
  setup" may be a second session on that PC.
- **Why:** Zach: "I suppose that's right... they might both be running on the computer."
- **Revisited:** 0.

### J-004: May CLAUDE.md point at GLOSSARY.md?

- **Asked:** 2026-10-27, by the supervisor.
- **Also comes up when:** any CLAUDE.md edit.
- **Answer (2026-10-27):** yes. The line is written in D:\Unity\Punchy but not committed: the animal session
  commits it only on Zach's direct word.
- **Why:** Zach: "Do it!"
- **Revisited:** 0.

### J-005: What goes on the milestone page?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** every report from a session.
- **Answer (2026-10-27):** only complete features (a new creature, building type, interaction, cave style), with
  screenshots. Interim steps get short chat summaries.
- **Why:** Zach suggested it to cut overhead.
- **Revisited:** 0.

### J-006: Is 30 FPS on the profiling PC a hard gate?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** every benchmark round.
- **Answer (2026-10-27):** a target, not a gate. Occasional frames over 33 ms are notes. Only a commit with a
  big, repeatable impact is held for a fix.
- **Why:** Zach: "I'd rather not stall the whole pipeline for a marginal failure."
- **Revisited:** 0.

### J-007: How much should assets be reused?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** new species, props, buildings, cave styles, materials.
- **Answer (2026-10-27):** as much as is reasonable: shared meshes, materials and textures, with differences as
  parameters. Say so when reuse would make something much harder or slower.
- **Why:** Zach: lower asset footprint and better batching.
- **Revisited:** 0.

### J-008: Do fast-travel hitches matter?

- **Asked:** 2026-10-27, from the profiler's dash benchmark.
- **Also comes up when:** teleports, debug jumps, any future fast travel.
- **Answer (2026-10-27):** no. They'll be hidden behind a loading screen later. Streaming hitches at normal play
  speeds still matter.
- **Why:** Zach.
- **Revisited:** 0.

### J-009: Can the player walk into lava?

- **Asked:** 2026-10-27, by the world session.
- **Also comes up when:** lava pools, volcano craters, eruptions, any hazard before a health system exists.
- **Answer (2026-10-27):** yes, always. Lava is never forbidden; it will hurt once there's a health system.
- **Why:** Zach: "There's no health system in place yet."
- **Revisited:** 0.

### J-010: What may the profiler change?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** any code-quality or performance finding.
- **Answer (2026-10-27):** review performance, code quality and streaming readiness; report to owners. Don't
  recommend refactors that cost performance or carry high risk.
- **Why:** Zach.
- **Revisited:** 0.

### J-011: Should creature traces and populations be bounded?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** footprints, feathers, nests, bat colonies, any new spawner.
- **Answer (2026-10-27):** yes. Traces are temporary; creatures despawn by distance with seeded regeneration.
- **Why:** Zach: free up memory.
- **Revisited:** 0.

### J-012: Should sessions invest in faster testing?

- **Asked:** 2026-10-27, by Zach.
- **Also comes up when:** any slow test loop.
- **Answer (2026-10-27):** yes: time speed-up, spawning next to the player, sandboxes, scripted checks.
- **Why:** Zach: "If gameplay speedup or temporarily dropping elements into a blank space can increase
  interesting time, that should be explored."
- **Revisited:** 0.
