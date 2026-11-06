# Design note: shared actor stats (audit 3), DRAFT for Zach

One ECS component set for the player, folk and creatures, so combat, taming/riding, skills-by-use and quests all read the same numbers.

- **Vitals:** health, stamina, hunger/thirst (optional per actor), with regen rates.
- **Attributes:** strength, agility, endurance, mind (4 to start; data-defined so more can be added).
- **Skills:** a list of (skill id, level, xp). Skill use adds xp, which levels the skill (Zach: skills improve with use).
- **Disposition:** faction id, plus per-target trust/fear (already partly in folk race reactions, #8), used for taming thresholds and NPC personality.
- **Comfort:** read from `Climate.At` plus traits (cold-adapted etc.), replacing CreatureComfort's own maths.
- **Definitions:** a species/race asset gives base values and growth; individuals vary by a seeded roll (personality).

Lanes: animal builds the components and the creature/folk side; player wires the player and the HUD. The first card would be vitals + skills on the player and one creature, with no combat yet.

Questions for Zach:
1. Is a short attribute list (4) OK, or do you want 5e-style 6 (your friend's repo uses those)?
2. Should hunger/thirst exist at all?
