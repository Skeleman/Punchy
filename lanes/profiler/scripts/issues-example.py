import subprocess
H = "From the profiler's ECS review of main 41a9259 (coordination f1998a9, BENCH-REPORTS)."
I = [
("Shared per-frame creature snapshot instead of 10+ array copies", ["lane:animal", "enhancement", "P2"], """
**What:** 10+ main-thread readers each call `ToComponentDataArray<CreatureState>` / `<CreatureBody>` / `ToEntityArray` every frame: CreatureAudioSystem (2), CreatureBreathSystem, CreatureBurrowSystem, CreatureWakeSystem, TraceSystem, FolkTalkSystem (2, item 9), FaunaDirector (3), Mantrap, AmbienceSystem, CreatureRenderSystem. The first one in the presentation group also completes the brain job.

**Suggestion:** build one snapshot per frame after the brain completes (state, body and entity arrays, plus the spatial grid the brain already builds) and keep it on a singleton. All readers use it read-only, and it's disposed or rebuilt the next frame. Later, readers can move into jobs against it.

**Done when:** each array is copied once per frame. Measure the summed main-thread time of the readers above at townwalk cap 60, before and after."""),
("Brain: stagger predator/alarm/chat searches instead of every frame", ["lane:animal", "enhancement", "P2"], """
**What:** in `CreatureBrainSystem`, `FindPredator` and `FindAlarm` run every frame for every hunted, fleeing-capable animal (grid queries), and `StartChat`'s kind-0 `FindChat` runs with probability 0.3 every frame for every ChatFree folk (item 9: BrainJob 0.87 to 0.90 ms).

**Suggestion:** stagger perception. Run each search about every 0.2 s per creature, phased by entity index or Individual, and keep the last result in between. Do it the same way on every machine, from the shared clock, so it stays deterministic. For chats, have the asker flag the asked, or search only when a neighbour's ChatRole changed.

**Done when:** BrainJob worker time at townwalk drops measurably, and flee/alarm reactions still look immediate in play (a 0.2 s delay shouldn't show)."""),
("Split CreatureState into smaller components by reader", ["lane:animal", "enhancement", "P3", "needs-design"], """
**What:** `CreatureState` (`Fauna/CreatureData.cs`) is a single component of about 300 bytes. It holds motion, posture, mode/stage/timers, needs, chat, task/work spot, lair, held item and anchor together. The brain writes all of it each frame, and every reader (render, audio, trace, wake, breath, burrow, talk, director, mantrap, ambience, net) copies all of it, mostly for Position/Yaw/Speed/Phase.

**Suggestion:** split it by reader, for example:
- `CreatureMotion` (Position, Yaw, Speed, GaitSpeed, Phase, YawRate, Scale)
- `CreaturePose` (Lie, HeadDown, Bow, Curl, Sit, Vertical, Fly, Roll, Look*, Action*)
- `CreatureMind` (Mode, Stage, timers, Target, Partner, needs)
- `CreatureSocial` / `CreatureWork` (chat, task, work spot, holder, lair)

Readers then take only what they need, and ghost fields map onto fewer bytes. This is a large refactor touching the brain, animator, replica and ghosts, so do it as its own batch after the shared snapshot issue shows what each reader uses.

**Done when:** CreatureState no longer exists as one blob, behaviour and network replication are unchanged (same shots, host + join clean), and a townwalk profile shows no regression."""),
("Brain LOD: far creatures think every few frames", ["lane:animal", "enhancement", "P3"], """
**What:** every loaded creature runs the full `Think` every frame, wherever it is. FaunaDirector releases far animals, but within the loaded range one 80 m away costs the same as one at your feet.

**Suggestion:** brain LOD. Beyond about 80 m from every player (on a server, from every connected player), run `Think` every 2-4 frames with the accumulated dt, and only ease motion (Advance/EaseGait) in between. Phase the frames by entity index so the load stays even.

**Done when:** BrainJob time drops in a busy town and at glide, with no visible stutter in far animals' motion."""),
("Decide: activity table with explicit priorities instead of early-return chain", ["lane:animal", "enhancement", "P3", "needs-design"], """
**What:** `Decide` in `CreatureBrainSystem` is a needs-weighted roll by species taste, which is a sound core. Before the roll, though, it has a growing chain of early-return special cases: hermit day, camper night, fishing spot, rooted, disguised, hunter task, hive raid, play ring, hearth. Fixed overrides (6, 8, 10, 14) also beat the weights outright. Each new behaviour adds one more, and their order is behaviour nobody wrote down.

**Suggestion (needs design):** describe each activity as (condition, weight, start) in one table or list, with explicit priority tiers (forced, such as night home; then urgent; then weighted). The order then becomes data that can be read and tuned, and new activities are an entry rather than another early return.

**Done when:** the activities and their priorities can be read in one place, and town and wild behaviour shots match before and after."""),
("Split CreatureBrainSystem.cs into partial files by area", ["lane:animal", "enhancement", "P3"], """
**What:** `Fauna/CreatureBrainSystem.cs` is 6,277 lines: one job with 22 modes and 13 tasks. The animal lane and others edit it constantly, so merges collide.

**Suggestion:** split `BrainJob` into `partial` files by area: core and Decide, town folk (chat, work, patrol, hearth, messenger), wild (graze, hunt, flee, play), fliers and swimmers, cave life (roost, web, burrow, crawl), and shared helpers. Burst and runtime are unaffected. A pure move with no code changes, best done right after a merge to main when no lane has brain edits in flight.

**Done when:** no file is over about 1,500 lines, behaviour is unchanged, and both lanes are told before it lands."""),
("MonoBehaviours driving ECS: explicit order and job dependencies", ["lane:animal", "lane:world", "enhancement", "P3", "needs-design"], """
**What:** many per-frame systems are MonoBehaviours that drive entities from `Update`/`LateUpdate` with their own queries: FaunaDirector, CaveSystem, FloraSystem, WeatherSystem, AmbienceSystem, Mantrap, ThirdPersonCamera, GroundDetailSystem and others. Their order against ECS systems is implicit (player loop order), and they can't schedule against ECS job dependencies. So they complete jobs early (`ToComponentDataArray`, `.Complete()`), and where the sync points fall is hard to reason about.

**Suggestion (needs design, not urgent):** for the ones that mainly read or write entities (FaunaDirector's spawning and releasing, Mantrap, the creature-facing parts of AmbienceSystem and CaveSystem), move the ECS-facing work into SystemBase systems with explicit `UpdateInGroup`/`UpdateAfter`, and keep the MonoBehaviour for scene and rendering setup. Start with whichever profile shows the worst sync stall.

**Done when:** the order of the per-frame fauna and world work is written down, and the first moved system shows no main-thread wait on the brain job in the profiler timeline."""),
("Managed TerrainSource sampling on the main thread: audit call sites", ["lane:animal", "lane:world", "enhancement", "P3"], """
**What:** `TerrainSource` sampling from managed code on the main thread costs about 16 us per sample in the Mono player (performance rules). Call sites outside Burst, per file: FaunaDirector 17, TraceSystem 9 (#55), AmbientLife 7, CreatureRenderSystem 6, Mantrap 5, AmbienceSystem 5, plus the camera (#54). Not every one runs per frame or per creature.

**Suggestion:** audit each call site. Where it runs per frame or per creature, batch it into a Burst job (`.Run()` is fine), stagger it by entity, or cache the result. One-off and per-event calls can stay.

**Done when:** each listed file's per-frame, per-creature samples are in Burst or staggered, with a before/after townwalk and cave profile."""),
("Creature needs are invisible to the player", ["lane:animal", "needs-design", "P3"], """
**What:** the brain's needs (Hunger, Thirst, Tired, Lonely, plus Unsettled) drive activity choice well, but the player can't see them. `Lonely` only weights play, and nothing in posture, sound or behaviour says "hungry" or "tired" apart from which activity happens to be picked.

**Suggestion (design):** decide whether the needs should show, for example a tired animal's slower gait and lower head, a hungry one sniffing the ground between grazes, or a lonely one calling more often (CreatureAudioSystem). Or keep them hidden and say so in docs/agents/fauna.md. The needs are simulation-only (not sent), so anything visible must come from synced state or be server-side behaviour.

**Done when:** Zach decides; then one small visible cue per need, or a doc line."""),
]
for t, labels, body in I:
    args = ["gh", "issue", "create", "-R", "Skeleman/Punchy", "-t", t, "-b", H + "\n" + body]
    for l in labels:
        args += ["-l", l]
    r = subprocess.run(args, capture_output=True, text=True)
    print((r.stdout.strip() or r.stderr.strip()).splitlines()[-1], "|", t)
