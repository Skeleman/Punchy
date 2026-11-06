# Design note: hand-editable world override file (audit 7), DRAFT for Zach

Generate the world from the seed, then apply one JSON file per island that you can edit by hand. Missing file = pure seed world.

- **landmarks:** add / move / remove / replace by stable id (type + index), e.g. `{"id":"HumanVillage#2","move":[x,z]}`.
- **rivers:** extra paths as point lists; remove a generated river by id.
- **biomes:** paint patches (polygon + biome name or tag); border nudges.
- **height:** keep HeightmapIO's raw file for sculpting; the JSON can point to it.
- **spawns:** optional fixed spawns (creature/NPC at a spot), separate from the seeded tables.

It's applied after generation and before flora/fauna placement, so everything downstream follows. The landmark hash is measured after overrides. A debug key reloads the file live.

Lane: world. Card after W-116.

Question for Zach: one file per island, or one per area (town, cave…) so each is small?
