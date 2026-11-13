# DESIGN: cliffs that look like cliffs (#281)

Zach 11-13: "The current generator has very uniform faces. There is an effort started to use real geometry to improve
them, but the results are lackluster, not blending well or looking like a real cliff face and instead some bricks
wedged into the ground. I need a solid description of how to make better looking cliffs: mountain edges, canyons, road
and river cuts, washes, and even ledges for stepped terrain in cities."

Status: a description for Zach to read. Nothing here is carded or built yet. It replaces the approach of W-150 (the
cliff kit); R-19's steep-rock shading stays and is reused.

## Why the two attempts so far fall short

- **The height map alone** gives every steep place the same shape: a smooth ramp at whatever angle the noise produced.
  A ramp has no vertical faces, no overhangs, no ledges and no sharp top edge, so no texture can make it read as a cliff.
- **The cliff kit** put separate rock pieces on top of that ramp. The ramp under them did not change, and each piece
  brought its own outline, scale and colour. Real cliffs are one continuous body of rock whose shape comes from its
  layers and cracks; pieces added to a slope read as objects on a slope ("bricks wedged into the ground"), however
  well they are buried or tinted.

The fix is to stop decorating slopes and build the cliff as the ground itself.

## The idea in one paragraph

A cliff is a **scarp**: a line on the map where the ground steps down. The generator decides scarp lines on purpose,
cuts a true step into the height field along each one, and then replaces the stepped strip with a generated rock skin
that is welded to the terrain along its top and bottom edges and drawn with the terrain's own rock material. The
skin's shape comes from a small model of geology (layers, cracks, wear), not from placed pieces. One builder makes
every kind of scarp from one description; the kinds differ only in the numbers fed to it.

## How a rock face gets its shape (the model)

Four things, applied in this order, produce nearly every cliff shape seen in nature:

1. **Layers (strata).** Rock lies in beds of different hardness. Hard beds stand as vertical faces and jut out as
   overhangs; soft beds wear back into slopes and ledges. A cliff's side-on profile is therefore a staircase read from
   a layer stack: face, ledge, face, recess, overhang. Each region gets one layer stack (bed thicknesses, hardness,
   colour, tilt of a few degrees). Every scarp in that region reads its profile from the same stack at the same
   heights, so the two walls of a canyon match, and a band of red rock can be followed around a mountain.
2. **Cracks (joints).** Rock breaks along near-vertical cracks in two or three preferred directions. Cracks split a
   face into columns and blocks, and the face steps in and out along them. In plan (seen from above) a cliff line is
   never a smooth curve: it is buttresses (parts that stand forward) and gullies or alcoves (parts that are set back),
   at three sizes at once: 50-200 m, 8-30 m and 1-4 m. Crack spacing and direction are per rock type: wide and blocky
   for granite, close and columnar for basalt, thin and flaky for shale.
3. **Wear.** What has fallen off lies at the bottom: a scree apron (a ramp of rubble at about 35 degrees) that buries
   the lower part of the face, with large fallen blocks on and below it. Gullies end in rubble fans. Where water runs
   at the foot, the base is undercut instead and there is no apron. The age setting controls this: a fresh cliff is
   tall and clean with little rubble; an old one is half buried and rounded.
4. **The top edge and the life on it.** The rim is the part players stand on and see against the sky. It needs a
   sharp but broken edge: a thin soil and turf cap that overhangs slightly, rim blocks split off by cracks and
   leaning out, bare rock for a metre or two before the grass starts, and a line of bushes or trees set back from the
   edge. Ledges collect soil: grass, a bush, a lone tree, snow in cold lands, nests.

The material follows the shape, not the other way round: bed colours come from the layer stack at that height (so
colour bands line up with the ledges), cracks and recesses are darkened, overhang undersides are darker and drier,
water streaks run down from ledges and the rim, lichen and moss sit on the damp shaded side. It is the same triplanar
rock shading R-19 gave steep terrain, so skin and terrain cannot differ in look.

## How it is built

- **A scarp record** (rule 22, data a world file could carry): the rim line as a polyline, the height of the drop
  along it, which side is down, a kind (below), the region's layer stack, age, and a seed. Scarps are chosen in the
  generator, before rivers, roads and places are finalised, so those can use them (a fall where a river crosses a hard
  bed, a road that climbs a break in the scarp, a town on a bench).
- **A real step in the height field.** Along each scarp the generator terraces the ground: flat-ish above, flat-ish
  below, the drop concentrated into a narrow strip. Today's faces are uniform because the drop is spread over a wide
  ramp. With a true step, even the bare height field (the far view) already shows a cliff.
- **The skin.** The strip is removed from the terrain mesh and replaced by a generated mesh: a grid along the rim line
  and down the face, pushed in and out by the layer profile, the cracks and the wear. Its first and last rows of
  vertices are the terrain's own vertices at the top and bottom of the strip, with the same normals, so there is no
  seam, no gap and nothing to blend. Overhangs and recesses are possible because the skin is a free mesh, not a height.
- **Loose parts on top, sparingly.** Fallen blocks, rim blocks and scree stones are instanced rocks. They are the only
  separate objects, and they are debris, which is what separate objects really are on a cliff.
- **Walking and climbing.** Ledges and overhangs are written as records: ledge tops go into the ground query, overhang
  undersides into the ceiling query, and each face segment carries a climb grade and its shimmy ledges, so climbing and
  shelves-that-hold-places (Zach 11-12) come from the same data as the look.
- **Distance (rule 20, rule 25).** Far: the stepped height field with the layer colours and a baked shade for the
  face in the far ground, so cliffs read from a summit. Middle: the skin at low subdivision. Near: full subdivision,
  fine cracks by displacement, loose blocks. Hand-overs by on-screen size, with the outline (rim and buttresses) kept
  the same at every tier so nothing pops against the sky.

## The kinds, as settings of the one builder

| Kind | Height | Profile | Plan line | Foot | Top | Notes |
|---|---|---|---|---|---|---|
| **Mountain edge** | 30-300 m, in several stacked bands | tall faces split by ledges; steeper and cleaner higher up | large buttresses and deep gullies; ridges narrow to sharp crests | big scree aprons, fallen blocks, snow fans in cold lands | broken, frost-split rim; no soil high up | bands step back up the mountain so the whole reads as tiers, not one wall; gullies are the natural climbing and avalanche lines |
| **Canyon** | 15-120 m, both sides | strong staircase from the layer stack: cliff, slope, cliff | follows the river: outer side of each bend steep and undercut, inner side stepped back | narrow scree, or water against the wall | flat rim (the plateau), sharp edge, side canyons cutting in | the two walls share bed heights and colours; width grows upward; alcoves and arches in soft beds |
| **River cut** | 1-10 m | one face, undercut at the water line | steep bank only on the outside of bends; the inside is a gentle bar of gravel | water, slumped blocks of bank, exposed roots | overhanging turf, leaning trees | earth over rock: soil on top, a rock bed showing below; where a hard bed crosses the river there is a fall (the lip of the waterfall builder is that bed) |
| **Wash (dry gully)** | 0.5-4 m | near-vertical crumbly earth banks, flat sandy floor | winding and braided; cut banks at bends; ends uphill in a small dry step | sand, pebbles, driftwood, cracked mud | sharp crumbling lip, dry grass | no rock layers unless it has cut down to bedrock; fans out where it meets flat ground |
| **Road cut** | 2-20 m | man-made: an even lean, with flat benches every 5-8 m on tall cuts | straight or smoothly curved with the road; both sides alike on a through-cut | a ditch, a little fresh rubble | clean edge, sometimes a fence | shows the same beds as the hill it cuts, with blocky tool-broken faces; near towns the lower part becomes a built wall |
| **City ledge (terrace)** | 1.5-8 m | a built retaining wall, vertical or slightly leaning, on a rock footing | follows streets and plot edges in straight runs and corners | paving, drains, planting | parapet or rail; stairs and ramps cut through at intervals | bedrock shows under and between walls so the town sits on the hill; stone and style come from the town's culture; houses may back onto the wall |

Sea cliffs are a mountain edge or canyon wall with water at the foot: undercut base, wave-cut notch, stacks and
arches where a buttress has been cut off, no scree.

## What makes it fail (the checks every row must pass)

1. **Clay test:** with textures off (flat grey, lit), the shot must still read as a cliff. If it needs the texture, the
   shape is wrong.
2. **Side-on test:** the profile shows faces, ledges and at least one overhang or recess, and the top edge is sharp.
3. **Seam test:** no visible join, colour step or lighting step where skin meets terrain, at top, bottom and ends.
   Scarps end by fading their height into a slope or turning into a gully, never by stopping.
4. **Sameness test:** no shape repeats within one view; three sizes of in-and-out are visible in the plan line.
5. **Far test:** from R-22's fixed vantage points the cliff reads as a cliff, and its outline does not change on approach.
6. **Belonging test:** rubble below, life on ledges, set-back plants at the rim, matching beds across a canyon.
7. Walkable, climbable and standing-under all agree with what is seen.

## What this means for work already done

- R-19 (steep rock shading, PR #285): kept; it becomes the skin's material.
- W-150 (cliff kit): its records for ledges, ground and ceiling queries and shelves carry over; its placed slab
  pieces are dropped, except as fallen and rim blocks.
- Placement: true steps in the height field move ground, so plots, flora and some landmarks near scarps will move and
  the fingerprint will change. That needs Zach's OK before any build (v3 rule: hashes are measures, but he is told first).

## Suggested first step (not started)

One builder, two test scarps at opposite ends of the range (a canyon wall and a river cut), shown to Zach as clay
shots first, then with the material. Only then the other kinds, the generator step, and distance tiers.
