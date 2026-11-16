# DESIGN: one ground-edit system (W-174, #406; folds #403, #405)

World lane, row 1 audit. Rule 33: one system for every way the ground is reshaped (an *edit*) or handed to a mesh (a
*replacement*), so a fix to the look or to a seam lands everywhere at once.

## 1. Today: every path

Order inside `TerrainSource` (one sample): ShapeHeight -> Archipelago -> RiverSampling.Carve -> LandmarkSampling.Shape
-> GrandFalls.Shape -> ScarpSet.Shape -> Ponds (paddies) -> Settle (streets, plot pads) -> LandmarkBeds.Level.

### Edits (the height changes)

| Path | What it edits | Profile | Blend | How samplers learn |
|---|---|---|---|---|
| LandmarkBeds.Level | a bed under each landmark (incl. roadside camps, which are landmarks) | terraces, Step 0.6, steep last fifth of the edge | hard lerp by a footprint mask; no noise on the edge line | nothing: it is in the height |
| LandmarkData Shape (LandmarkSampling) | landmark ground: Flatten, Profile, Roughness, temple swell, ribcage, pool lips and bowls | per type; pools a smoothstep bowl + 0.6 lip | lerp by Flatten weight | in the height |
| GrandFalls.Shape / Level | basin and bank of each grand fall | bank 8 + 0.4·Δh, linear | lerp | in the height |
| RiverData.Carve | river channels | smooth bed, banks by width and slope (W-43c wobble) | SmoothMin k=2, faded by MaxCarveDistance | in the height; water reads RiverSampling |
| ScarpSet.Shape | scarp step (mesa, canyon, sea cliff) | TopRamp 4, TopFlat 1.5, envelope | envelope weight | in the height; the face itself is a *replacement* (below) |
| Ponds / Terrace | ponds and paddy cells | paddy cells ~PaddySize, banks TerraceBank, rim back to ground | min / lerp per cell | in the height; water via PondMask |
| SettlementData.Height | streets (grade banks), plot pads, PortalCut, PortalLedgeCap | streets banked; pads mixed by w^8 | w^8 mix | in the height |
| Archipelago | islet shapes | own | add | in the height |

Six different blends (hard lerp, SmoothMin, w^8, envelope, min, add) and three profiles that leave straight lines
(bed terraces, falls banks, pad edges). These are the slices of #403.

### Replacements (a mesh or a hole takes over)

| Path | What it replaces | Seam today | Who honours the hole |
|---|---|---|---|
| Cave mouths (TerrainSource.MouthCut) | terrain over a cave entrance | cave field along the normal: 0.35 m in sampling, 0.15 m in the mesh; per-vertex value in BuildChunkMeshJob, shader cuts per pixel | IsCaveHole: creature brain, roads, roadside, settlements. MouthCut directly: CreatureMotion, TraceSystem, GroundDetail, CliffKit. Flora: only CaveClearance + InPortalCut |
| Temple entrances (TempleCut) | ground over a sunken temple door | TempleCut step into the same per-vertex value | as cave mouths (it is in IsCaveHole) |
| Scarp skins (ScarpSkinJob, ScarpSet.Strip) | the steep face of a scarp | strip value (stripY) in the per-vertex cave value; skin mesh built separately | **not in IsCaveHole**: flora and spawns can stand on removed ground |
| Cliff kit pieces (CliffKit) | sea cliff and mesa faces | pieces sit on the ground; no weld; `CliffKit.Covered` tells flora | flora only; TerrainSource.Deck/Ceiling for standing |
| Dungeon/settlement portals (PortalCut, PortalLedgeCap) | ground at a portal | a height cut, not a hole | in the height |

Four seam methods (normal-offset field, step cut, strip, none) and three different ways a sampler hears of a hole
(IsCaveHole, MouthCut, its own clearance test). These are the gaps of #405.

## 2. Proposal

### (a) One ground edit record

```
struct GroundEdit {            // baked (rule 22), stored in one blob, one cell grid
  Shape     shape;             // disc | capsule (a line with width) | polygon outline, in xz; plus its own yaw
  float     target;            // height to reach (beds, pads, paddies) ...
  float     depth;             // ... or depth to dig (rivers, pools, falls basins); one of the two
  Profile   profile;           // falloff out from the shape's edge: width, curve (natural / terrace / sheer)
  float     noise;             // edge break-up (m) and its scale, so no rim is a straight line or a disc
  float2    exposure;          // slope where rock shows, talus amount at the toe (steep cuts)
  byte      priority;          // later wins where edits overlap (river > bed > pad), never blend order by code path
}
```

Applied in **one place**, `GroundEdits.Apply(xz, h)`, called once from `TerrainSource` after the natural height and
the scarp step. Every caller of SampleHeight / SampleGround (mesh, collision, far ring, flora, spawns, water, roads)
reads the edited ground because it is the ground. One blend: a smooth max/min towards target by the profile weight,
the edge distance perturbed by the noise; terraces become a profile option, not a separate path.

Hand edits (M-07 map editor): its sparse painted delta is a *base* input, not an edit record. Places are
chosen from the ground, and every GroundEdit is baked from a place (bed, pad, pool, basin), so all records apply
after places. The delta is added to the natural height before ScarpSet and BuildPlaces, and GroundEdits reads that
sum as its "natural height". One ground stack: natural + painted delta -> scarp -> places -> GroundEdits.Apply.

Moves onto it: LandmarkBeds, LandmarkData pools and Flatten, GrandFalls basins and banks, paddies, plot pads, streets'
banks (rows 2 and 3). River carving keeps its spline sampler but returns its depth through the same profile and
blend, so banks look like every other cut.

### (b) One replacement record

```
struct GroundReplace {         // baked; one blob next to the edits
  Shape   hole;                // where the heightmap is removed (xz outline + a y band)
  Rim     rim;                 // the mesh's rim loop (from the piece: cave mouth, temple door, scarp skin, cliff piece)
}
```

- **One hole mask**: `GroundHoles.Removed(xz, y)` replaces IsCaveHole, MouthCut-as-a-test, CaveClearance,
  InPortalCut and CliffKit.Covered for samplers. Every sampler (flora, ground detail, spawns, creatures, traces,
  roads, settlements) asks it, so nothing stands on removed ground. The mesh and shader keep the per-vertex value,
  now written from this mask only.
- **One seam builder**: walks the rim loop and snaps or welds the terrain vertices it crosses to the rim (skirt down
  where the rim is lower), so the mesh edge and the terrain edge share vertices. Cave mouths, temple doors, scarp
  skins and cliff pieces hand their rim to it instead of solving their own seam.

### Scarps and W-162 (extend, not fork)

- The scarp **step** (ScarpSet.Shape) becomes a GroundEdit with a sheer profile and rock exposure; its envelope is the
  profile. The scarp **skin** is a GroundReplace: its strip is the hole, its skin edge the rim.
- W-162's shared rock wall is the one builder for steep faces; it emits a GroundReplace per wall run, so climbing
  (one method) and the seam come from the same record. CliffKit pieces become decorations on that wall, standing on
  its rim, not a second seam path.

## 3. Rows after review

2 natural cutaways (beds, pads, paddies onto the edit record) - 3 rivers, falls, landmark pools - 4 hole mask + seam
builder - 5 edit-mode guard test. Cost lines to BENCH-QUEUE after rows 2 and 4 (rule 21). Far seam detail by
DetailPolicy.ScreenSize only (rule 20). World layout may move; re-pin and tell the profiler.

## 4. Shots

In the W-174 milestone folder (w174-slice1..3, w174-gap1..2, w174-sheet):

- slice 1: the straight cuts at 1116,42.7,1634 (#403);
- slice 2: a roadside camp bed at 892,-3799 (LandmarkBeds terraces);
- slice 3: a plot pad edge at -1969,258,631 (SettlementData pads, w^8 mix);
- gap 1: the sunken temple entrance at 3520,360 (TempleCut; #405, things stood on removed ground); seen from just below the cut, the temple floor hangs over open sky where ground should be;
- gap 2: cave 0's mouth at -1796,51.2,-199 (MouthCut normal-offset seam).
