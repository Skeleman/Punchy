# Model Lab body builder: what the body data supports (M-06 row 2)

Design note for #413. Nothing here is built yet: rows 3-5 wait for Zach's OK. Modules: CreatureSpecies, LimbSlot,
LimbType and LimbSegment (Fauna data), CreatureBlobBuilder and CreatureBlob (CreatureData.cs), CreatureAnimator,
Creature.shader, CreatureTunables, ModelLab (editor only), the W-166 MaterialLibrary and W-175 composite units.

## What the data expresses today

- **Body plan.** There is no archetype type. A body plan is the species' list of LimbSlots plus `Upright`. Quadrupeds,
  bipeds, birds, serpents, fish and folk all come from different slot lists on the same parts.
- **Trunk (segment 0).** One tapered capsule: `BodyLength`, `BodyRadius`, `BodyTaper` (rear radius = radius x taper)
  and `BodyFlatten`. It can't be subdivided. Upright folk add a torso as a Feature part on the body.
- **Limbs per slot.** A LimbSlot holds one LimbType. `Mirror` makes a left and right pair; there's no count field, so
  four legs are two mirrored slots. `Parent` is -1 for the trunk, or an earlier slot. A child hangs only from the end
  of its parent's last segment, offset by `Socket` in that segment's frame. Each slot also has `Direction` (rest pitch,
  yaw and roll in degrees), `Scale`, `Thickness`, `Phase`, `StyleGroup` and `FurLength`.
- **Limb kinds.** `LimbRole` is Leg, Arm, Tail, Neck, Head, Ear, Horn, Eye, Feature or Wing. A LimbType is a straight
  chain of segments with seven key poses (idle, walk reach/push/lift, run reach/push/lift), one joint angle per segment.
- **Segments.** Each LimbSegment has `Length`, `RadiusStart` and `RadiusEnd` (a straight-line taper only), `Flatten`,
  `Accent` (base to accent coat colour), `UseFixedColor`/`FixedColor`, `Glow`, `Hair`, `Surface` (Coat, Hoof, Claw,
  Pad, Eye, Horn, Skin, Paw) and `FurLength`. The segment count is the length of the list, so subdividing a limb
  means adding segments and adding their angle to all seven poses.
- **Rest rotations.** The neck angle comes from the neck slot's `Direction` and its idle pose angles. A torso lean
  exists only for upright folk: the torso Feature's `Direction` or idle angle, plus a small lean per individual
  (CreatureAnimator). A quadruped's trunk is always level when standing. It only pitches for a sprint and for
  `LiePitch` when lying down.
- **Head shape.** The head is a chain like any other limb, so it can have several segments. Ears, eyes, horns and snouts are child parts on the head. The species also has `CoatKind` (Fur, Feathers,
  Scales, Skin, Chitin) and its coats.
- **Per-species tuning (M-05).** `CreatureTunables` holds multipliers (Size, BodyLength, Thickness, Legs, Head, Neck,
  Tail, Speed, Swing) and `PoseNudges`, and Build applies them. With every value at its default, the blob bytes don't
  change.

**What keeps the fingerprint.** Fauna_Fingerprint hashes species names plus every baked blob byte. A new field on the
blob's structs changes it even at its default, because the layout itself changes. A new field on the asset side that
bakes into fields that already exist (radii, socket, angles) keeps it when left at its default. Segment looks
(`CreatureBlobBuilder.Looks`) are a side table outside the blob, so they aren't hashed.

## Gaps Zach named

| Gap | Smallest data change | Fingerprint safe? | What the animator or renderer must learn |
|---|---|---|---|
| Thighs that taper more | None. Each leg segment already has its own start and end radius, so the builder exposes them per segment. | Yes, and unedited species keep their bytes. | Nothing. |
| A curved thigh (a bulge, not a straight taper) | One `Bulge` float on LimbSegment and CreatureSegment. | No: one re-pin, all species. | Creature.shader and the Lab mesh need a curved radius profile. SegmentInstance has no free float, so its stride grows (renderer and bandwidth cost, so it goes on BENCH-QUEUE). Splitting the thigh into two segments instead would break MeasureLegs, which assumes segment 0 is the thigh and segment 1 the shin. |
| Subdivided head | None for a chain: add head segments, or child Feature parts for the snout, jaw or brow. | Yes. | Nothing for a chain. The limit is that a child hangs only from the parent's last segment. Hanging a jaw from a middle head segment needs a `ParentSegment` index on CreaturePart (not safe: one re-pin) and the animator would read that frame. |
| Torso that leans further (upright) | None: the torso slot's `Direction.x` or its idle angle. | Yes. | Nothing. Walk and run poses lean on top of the rest angle. |
| Torso that leans (quadruped, chest up or down) | A `RestPitch` on CreatureSpecies, baked into a new CreatureBlob field. | No: one re-pin. Rest angles have no existing field to bake into. | Add it to the body pitch the animator already applies for sprints and `LiePitch`. Leg reach must allow front and hind hips at different heights (StandHeight and MeasureLegs per leg pair). |
| Colour per segment | None: `Accent`, or `UseFixedColor`/`FixedColor` (both baked today). | Yes for existing values. | Nothing. Rule 33: colours stay coat-relative or fixed per segment, with no Lab palette. |
| Material per segment | A `MaterialFamily` id per segment on LimbSegment, carried in the looks side table next to `Surface`. | Yes, because looks aren't in the blob. | Creature.shader reads the W-166 library for that kind (W-166 follow-up). SegmentInstance Material.w already carries a look code, which would need room for the kind. |

**Proposals.**
1. Row 3 first builds only what is fingerprint safe: per-segment radii, length, segment count, slot `Direction`,
   adding and removing slots, and saving into the species and limb assets.
2. The two changes that need a re-pin (`RestPitch` and `Bulge`) wait for Zach's call, one card each.
3. Material per segment rides on the looks table.

## Where the kit limits show (row 5)

- The W-166 library (world/material-library, not on main yet) has Wood, Brick, Stone, Thatch, Plaster, Metal, Cloth,
  Leather, Ice and Bone. It has nothing for fur, feathers, scales, chitin, horn or shell. Row 4 needs those rows from
  the materials lane (rule 11 ask). Leather and Bone overlap with creatures and should stay one entry each.
- Blueprints need W-175's composite format (the parts-and-units audit hasn't started). Until then, StructureKit
  buildings are code: each builder returns a list of `KitPart`, a box with a `KitPiece` kind (Wall, Floor, Roof,
  Beam, Door and so on), a `KitMaterial`, a centre, a size and a rotation. There's no part registry for the Lab to
  pick from, and colours come from `CultureTrim` Color fields rather than library kinds. The Lab can't save a
  building until W-175 lands. Saving to a Lab-only format is ruled out.
- The creature builder saves into the existing species and limb assets, so it never needs a Lab-only format either.

## Questions for Zach

1. Is one Fauna_Fingerprint re-pin OK for a quadruped `RestPitch`, and is a later one OK for a curved `Bulge`? Or
   should we stay with straight tapers and upright-only lean?
2. Should a saved Lab variant be a new species asset, or a CreatureTunables entry on an existing species?
