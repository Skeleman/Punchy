# One population, far and near (A-110 row 1, folk lane, #298)
Design note, no code yet. Aim (Zach 11-13, paraphrased): a town's far figures are its real residents, in about
the real number. Scope: houses in towns and camps. Other homes come in a later card (section 8).
## 1. Who computes what
| Thing | Computed by | Sent | Host cost |
|---|---|---|---|
| Households, Whereabouts, far figures, home signs | every machine, from records and the shared clock | nothing | none |
| Creatures (brains) for outdoor members near any player | host only (solo: the local world) | ghosts, as today | section 5 |
| Town exceptions (deaths) | host owns, clients copy | one message per town (section 6) | a few bytes |

Clients share the records, clock and exceptions, so Whereabouts agrees. No ghost field; no client authority wanted.
## 2. Whereabouts(member, day, hour)
- Pure function of the household record, the town's records (posts, streets, well, square) and the clock.
  Returns indoors or outdoors, position, facing, activity (job task, sit, play, carry, walk) and the leg's ends.
- A member's day is at most 8 legs (place, leave hour), built per member and day from its Individual and cached.
  The folk day turns over at 4 h, when all but the watch are indoors, so no leg crosses the turn.
- Every leg ends at a named place (rule 9): own door, job post, well, square, field, a neighbour's door, bench.
  Routes take the shortest path over the town's street records; camps with no streets walk straight. Walking
  speed is fixed, so position is continuous in the hour.
- Jobs: the town's posts and task rows (smith, woodcutter) go to grown members in a fixed order (houses from the
  middle out, head first), with no random draw. The rest keep house, fetch water, tend a field or visit; elders
  sit by the door or in the square; children play in the square or a yard.
- An exception (section 6) overrides the result per member. HomeSign's count of who is in becomes a read of
  Whereabouts, so signs and figures can't disagree.
## 3. What resident rows become
- Rows no longer spawn housed kinds (grown and child folk of a town). Each member spawns as itself, with the
  species its household gives it. Row workers become members with that job. Lodgers (row folk with no bed) stop
  existing: every person in a town has a house.
- Domestics, pens, lake and marsh life keep their rows. Visitors and crossing fishers stay as they are; later a
  visitor can be a member of another town who is away (a displaced exception).
## 4. The two hand-overs
One rule on every machine, replacing A-109's per-town fade: a member is drawn as a far figure when it is
outdoors, not excepted, and its creature is absent or faded out on this machine.
- Far to near: the host spawns the member at its Whereabouts spot, in the leg's activity, when that spot is
  within 120 m of a player (as now). While the creature fades in (140 to 165 m) the far figure stands at the
  creature's own position, so the pair is one person in one place. A joined client keeps the far figure until the
  ghost with that Individual arrives: no gap, no double.
- Near to far: the brain follows the same legs, so a creature stays near its Whereabouts spot unless talking,
  fleeing or fighting. When the host lets it go (170 m from every player), each machine that had it keeps the
  offset between where it stood and the plan, in memory only, and walks it down to zero at walking speed. A
  machine that never had the ghost drew the plan spot all along. A creature going indoors is let go at its door.
- Weak point: a client beyond ghost range (200 m) draws the plan spot while the host's creature may be off it
  (fled or lured). On approach the figure eases to the ghost over about a second.
## 5. Host creature budget
- Far residents cost the host nothing at any distance or count. Indoor members are not entities.
- Spawning is per person, not per town: only outdoor members within 120 m of some player become creatures.
- Housed members get their own tier (proposed cap 80), so domestics and visitors keep MaxSettled 60 as it is.
- Outdoor share of a town: night 1 to 3 people (the watch), dawn and evening up to 30%, peak up to about 55%
  in the afternoon (ruled after row 2: far views full of life come first). Town 73 (38 houses, 101 members):
  row 2 measured a peak of 52 at 16.50 h, 3 at night. If the housed cap of 80 bites at a peak, report it; the
  plan is not tuned down for it.
- Members only go to places within a 90 m street walk of their own door.
- The watch is the heads of the six houses with the shortest street walk to the gate.
- Over the cap (players in two or three big towns at once), the members farthest from any player stay far
  figures. Far figures hide above 24 px (about 75 m), so the cap must keep everyone inside 80 m a creature.
- Row 5 lowers the cost per creature: beyond talk range a housed folk only follows its legs, at a low rate.
## 6. Town exceptions (row 6)
- Record (host; plain data a save or world file could carry): per town, entries of household key (uint), member
  (byte), kind (byte: 1 dead; 2 displaced, later), day (ushort): 8 bytes each.
- Wire: one message per town carrying its whole state, so a repeat does no harm: landmark index plus four ulongs,
  one bit per dead member in the town's fixed member order (256 members, 36 bytes). No per-creature ghost field.
  Displaced needs a place too: its own message, later. Alternative: send the entries (town 73 at most 808 bytes).
- When: to a client as it goes in game, every town with a non-empty list; after that, a town's list to all
  clients when it changes. I prefer this to sending by nearness: far figures show from 1 km and deaths are rare.
- Readers: Whereabouts (a dead member is nowhere: no figure, no spawn), HomeSign (not counted in), family talk
  (Household.Gone). A-106's client-side marking of vanished ghosts is removed. The list lives for the session in
  the host's memory; solo reads it directly. Old builds can't join after row 6.
## 7. Pins and tests that move
- Fauna_Fingerprint (6A9E28E5) changes in row 3, because skipped rows shift later draws; I report old and new and
  re-pin. Landmarks (117, 689D5348) and placement are untouched.
- Town_TwentyFolkDistinct, the LiveFolk, SettledFrame_ForestAndTown and FolkTalk tests need folk standing in a
  town at the test hour: re-checked in row 3. A-105's slot filling and -households log change shape in row 3,
  HomeSign digests in row 2. Row 6 replaces A-106's -kincmd checks with a late-joining client learning of a death.
- New default tests (numbers only): Whereabouts continuity over a day in 3 towns, host and client digests equal,
  far count plus creature count equals outdoor count.
## 8. Rulings (supervisor, 11-13)
1. Own tier, cap 80, for housed members; MaxSettled 60 stays for domestics and visitors. Provisional: row 2 logs
   the real outdoor peak per town, the row 5 BENCH job sets the final number. Over the cap the nearest win.
2. Bit mask, one message per town (landmark index plus four ulongs), to a client at join for every non-empty
   town and to all clients on change. Not by nearness. A town over 256 members is a build-time error, not a clip.
3. Indoor members are not entities on this card. Someone home when a player walks in waits for player-lane
   interiors; keep Whereabouts able to answer who is in which house.
4. Later card (supervisor writes it): igloos, domes, hermit huts, bat folk cave routes, wharf and pier folk.
5. Section 4's weak point is accepted. The near-to-far offset is memory only, never sent or saved.
