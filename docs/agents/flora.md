# Flora, rocks and props

Read before changing plant species, rocks, props or flora forms.

- Rocks (Boulder form) come in five families by variant (lump, slab, L, standing, monstrous) and carry their own
  collision: FloraModel.Colliders, a few upright cylinders hugging the footprint, turned and scaled with each
  placed rock (other solid plants use one trunk cylinder). FloraSpecies.SizeRange sets each placed plant's
  random size; a landmark prop's Scale multiplies it.
- Flora tubes' caps are cones whose height scales with the radius: don't cap wide tubes (rims, platforms). Slot `Parent` indices count slots in the species' list,
  not the mirrored copies.
- Flora with **Aquatic** grows only where the water depth is within `WaterDepth`. **Floats** puts it on the
  surface (the LilyPad form); otherwise it rises from the bed (Reeds). Floating plants avoid currents.
  `MinRapids` limits a species to white water (RiverRock).
- Player collision with flora is upright cylinders pushed out on xz only (PlayerMovementSystem). A leaning shape
  (ArcSpire prongs) is a stack of short slices, `FloraModel.ColliderBase` giving each slice's start height; those
  plants only count within `ShardCollisionRange` (14 m) of the player.
