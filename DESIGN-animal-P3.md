# Animal lane: P3 design notes (Z-50..Z-53)

Animal lane (zache-db), 2026-11-01, written from animal-behaviours c810406 (no Unity). For the supervisor's OK, row
by row. Each row gives what the code does now, the proposal, its cost and risk, and how a slot would check it.
Suggested order: **Z-53, then Z-51, Z-50, Z-52** (cheapest and most visible first).

## Z-53: predator-prey chases are rarely seen
**Now:** chases do exist. A species with `Hunts` (Wolf, RedFox, Coyote, MountainLion, Cat, foxes, bears and others)
picks the nearest prey within 35 m (`FindPrey`). It gets Hunt appeal `Hunger × 2.5 × Urge`, enters
`CreatureMode.Hunt` and stalks at 0.8× walk speed until 14 m, then runs at RunSpeed (`Hunt()`,
CreatureBrainSystem.cs:1617). Prey flees once the hunter is within 30 m × alert (`FindPredator`). Why they're rarely
seen:
1. **There's no catch.** Hunt ends with `Tired()` the moment it reaches body length + 0.5 m, or after ChaseTime
   (10 s), or past 60 m. A chase that works ends as a stop with nothing happening.
2. **Prey is often as fast as the hunter.** RedFox/Coyote run at 6-7 against Rabbit and PrairieDog at 6, so the
   distance stays about the same. The prey starts fleeing at 30 m, before the hunter's 14 m sprint begins, so the
   chase usually opens well past 14 m and stretches off-screen.
3. Only the lone nearest prey is considered, and hunger has to build first (Hunger 0.4 species reach Urge slowly),
   so most predators are wandering when the player passes.

**Proposal (data plus about 60 lines in Hunt):**
- A **pounce/catch** on contact: the hunter does a short lunge (reuse the Startle lunge pose), and the prey gets a
  `Caught` end like the bird-of-prey catch (`FaunaDirector` removes it, then respawns it as it does now), or a
  50% "slipped away" that sends it off on a fresh flee. The hunter then eats: an IdleAction Sniff/HeadDown for
  8-15 s, then `Hunger -= 0.6`.
- **A sprint burst** for the hunter: 1.25× RunSpeed for the first 3-4 s of the run stage, then it tires (this
  stays under the Sprint threshold, so gaits don't change). Prey keeps its RunSpeed but **jinks**: a random
  ±50° heading change every 1-2 s while fleeing a predator. The gap closes, and the chase stays on screen.
- **Stalk closer:** the hunter's stalk speed drops to 0.5× walk speed while prey is still unaware (prey's alert
  range against a stalking hunter goes from 30 m to 12 m × alert). The chase then starts close and short:
  roughly 4-8 s.
- **A bit more often:** Hunt appeal is multiplied by 1.5 when prey is within 20 m.
- **Never** player-owned or held animals, and never inside settlements (pets keep their safety). Domestic chickens
  stay the fox's prey only outside a town's radius, as now.

**Cost:** FindPredator/FindPrey are unchanged. The jink is per-fleeing-prey only. Watch `Brain.Hunter` /
`Brain.Predator` in townwalk (2 ABBA captures).
**Check in a slot:** `-place Dunes` / `-spawn Coyote:1 -spawn Rabbit:3`, then `-watch Coyote`, with 0.5 s shots
for 60 s. Pass: at least one chase that closes in frame, a pounce, and an eat or slip in the log
(`Hunt end: caught|slipped|tired`, a new log line).
**Zach (2026-11-01, via the supervisor):** a caught animal can be eaten. **Z-53 OK to code** on a wip/ branch (no Unity).
Z-50 and Z-51 wait for his OK; Z-52 waits for which races are cool toward each other.

## Z-51: several voices per animal
**Now:** `CreatureSpecies.Calls` is already a list. Each entry has its own sound, interval, pitch range, volume,
range and CallTime, so several voices per species need no new system. But Cat has only Meow, and Dog has only
Bark. Moods add the Purr (cat held) and Pant (dog following or after running) in `CreatureAudioSystem.Moods`. Only
2-4 synth variants are played, and the pitch is fixed per individual (within its range).
**Proposal:**
- **New synth sounds** (appended to SoundId; assets keep sounds by number): Hiss, Yowl (a cat at night), Whine,
  Growl and DogHowl (a dog at night, softer than the wolf's Howl, with the Z-14 low-pass applied). Each gets 3
  variants.
- **Context voices, not just intervals:** Hiss when the cat is startled or fleeing from something within 3 m
  (Startle lunge, a dog nearby). Growl when a dog is threatened but doesn't flee, or meets a stranger dog.
  Whine when the dog is left (its Greet partner walks off). These hook into the existing mode transitions in
  CreatureAudioSystem (where Purr/Pant already read mode).
- **Variation on every play:** a per-play pitch jitter of ±6% and a speed (time-stretch) jitter of ±10%, so
  repeats differ. In the synth this means picking the variant and resampling with `pitch`; GameAudio already
  takes pitch per one-shot, so only the jitter has to be added.
- Apply the same to other species with one call where it's cheap: horse (Snort plus Whinny already), the
  Chicken's model is the template.
**Cost:** synth time at load (each variant is about 0.5-1 s of float maths on a worker), roughly 0.2 MB more
clips. The call budget (`CallsPerSecond`) is unchanged, so no village gets louder.
**Check in a slot:** a sound-log run (`CallsPlayed` by SoundId) around a town for 2 minutes, plus a listen. A
stranger dog for the growl: `-spawn Dog:2`.

## Z-50: real run gaits for quadrupeds (gallop/bound)
**Now:** `Gallops` is set for every 4-legged non-upright body. But `Sprint()` only rises above 1.05-1.6× RunSpeed,
which "only players' bodies go" (CreatureData.cs:145), so NPC animals never gallop. The sprint gives a longer stride
(`SprintStride` 0.6) and one airborne lift per cycle (CreatureAnimator.cs:163). But the legs keep their fixed
per-leg `part.Phase` (a trot/walk pattern), so it's a stretched trot with a hop, not a gallop.
**Proposal:**
- **Phase blend by gait:** a per-leg gallop phase table (rotary gallop: hind L 0, hind R 0.1, fore L 0.5, fore R
  0.6; bound for small bodies such as rabbits, squirrels and weasels: both hinds 0, both fores 0.5). The animator
  and CreatureMotion (planting, `Phase + part.Phase`) use `lerp(part.Phase, gallopPhase, sprint)` via
  shortest-arc on the circle. TraceSystem reads the same so the prints match.
- **Spine flex:** the back arches while the legs are gathered and extends at the push. That's a body pitch on the
  rear half (the hips' segment) of ±0.15 rad × sprint, at the cycle's 2× frequency. Hind feet land ahead of where
  the fore feet were (bigger hind reach in the gallop row).
- **NPCs gallop too:** the threshold drops to 0.85-1.15× RunSpeed for NPCs fleeing or hunting (the Z-53 sprint
  burst reaches it), so wolves, deer and horses in a chase visibly gallop.
- Bound vs. gallop is chosen per species by a new `GallopStyle` field (Rotary, Bound), defaulting from body
  length (short ones bound).
**Cost:** a few more maths per leg only while sprinting. The main risk is foot sliding when the phases blend at
the threshold: the blend runs over the whole smoothstep and planting uses the blended phase.
**Check in a slot:** a fixed `-camlook` side view of player bodies (`-body Horse`, `-body Wolf`, `-body Rabbit`)
running past at full sprint, with 0.1 s shot bursts, plus a deer fleeing (`-spawn Deer:3`, then a startle). Pass:
the hind pair lands ahead of the fore pair at the gather, there's a single suspension, and no foot slides in a
6-frame burst.
**Depends on Z-21** (steep foot angle at full extension): fix that first, because the gallop's reach makes it
worse.

## Z-52: NPCs react to the player's race or species
**Now:** `ownKind` (CreatureBrainSystem.cs:246) already stops animals fleeing from a player of their own species,
and ghosts let a ghost close (Z-25). Folk come to talk (`Greet`, line 456), and FolkTalkSystem already passes
`playerSpecies` to the dialogue (DialogueArchive). There's no general friendlier-or-annoyed stance.
**Proposal:**
- **A stance per NPC, picked once per meeting** from (NPC species, player species): Kin (same species): greets
  sooner (Greet range ×1.5), and a chance of a Wave/Bow emote. Stranger: mostly as now; 20% wary (keeps 3-4 m,
  watches, doesn't come to talk); 10% annoyed (a turn away, an arms-crossed or head-shake pose, and a curt line).
  Animal bodies (the player as a cat or dog in a town): folk either ignore them or come to pet them, which reuses
  the Pet flow reversed. Nobody is hostile: no attacks or chases.
- **Lore table:** a small per-species affinity grid (Elf-Dwarf a bit cool, Human neutral to all, and the like)
  as data on CreatureSpecies (`Affinity` list: species and -1..1), which scales the wary/annoyed odds. Defaults to
  0.
- **Dialogue:** FolkTalkSystem picks from Kin/Stranger/Wary line sets if DialogueArchive has them, falling back
  to the current lines. (Writing the lines is a content task; I'd draft about 10 per set.)
**Cost:** the stance is one byte on CreatureState, rolled at Greet start, so there's no per-frame search.
**Check in a slot:** `-body Elf` walking through a dwarf town and an elf grove, and `-body Human` in the same
places, with emote and line logs. Pass: visibly more greeting from kin, and occasional wary or annoyed reactions
from strangers.
**Zach (2026-11-01, via the supervisor):** goblins and orcs are cool toward each other; humans are cool toward
dwarves and toward elves; dwarves and elves have a playful rivalry. **Z-52 OK to code after Z-53** (wip/ branch,
no Unity). The game has no orcs: read as **Ogre** (GoblinCamp/OgreCamp) unless Zach says otherwise. As affinities:
Goblin-Ogre -0.5 (both ways), Human toward Dwarf and Elf -0.4 (one way, as Zach said it). Dwarf-Elf is its own **Rival** stance (teasing lines,
a mock bow or head-shake, never wary), not a negative affinity. Open: should an annoyed NPC say anything, or just
turn away? (Default: a curt line.)
Also from Zach: **fish folk** are neutral toward all races (affinity 0), and the **blind cave pygmies** are a little
wary of all races (affinity -0.25 toward every other folk, so a higher wary chance and no annoyed reaction). Neither
species exists yet (the `Cultures` flag FishFolk = 64 and REALMS-PLAN list fish folk as "later"; pygmies aren't in
the code at all), so their values go in as data on the species when they're added, plus a per-species default
affinity field (`AffinityToAll`) to cover the "toward every race" cases.
