using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A point on a passage's centre line and how wide the passage is there.</summary>
    public struct CaveNode
    {
        public float3 Position;
        public float Radius;
    }

    /// <summary>What a node is for, so props and anchors can tell a chamber from a passage.</summary>
    public enum CaveNodeKind : byte
    {
        Passage,
        /// <summary>A room: the end of the main passage, one along it, or the end of a side passage.</summary>
        Chamber,
        /// <summary>Part of a shaft rising to the surface.</summary>
        Shaft,
        /// <summary>
        /// W-115: the doorway of a destination chamber (a <see cref="CaveRoom"/>): a chamber that became a tile-built
        /// room. The node keeps the passage's width; the room's own boxes carve the air past it.
        /// </summary>
        Room
    }

    /// <summary>
    /// W-115: a destination chamber, a tile-built room (crystal grotto, underground lake, bat roost or buried ruin room)
    /// at the end of a passage. Its air is a box carved out of the rock and walled off from the cave's own noise, so its
    /// tiled floor, walls and ceiling sit flush on it; a doorway stub joins it to the passage. In a frame at the door:
    /// z runs into the room along F, x across it, y up from the floor F0.
    /// </summary>
    public struct CaveRoom
    {
        public int Cave, Node;
        /// <summary>A <see cref="CaveRoomType"/>.</summary>
        public int Type;
        /// <summary>The doorway's middle on the floor (y = the room's floor), and the horizontal way into the room.</summary>
        public float3 Origin;
        public float2 F;
        /// <summary>Half width (across) and half depth (along F) of the room, and its height.</summary>
        public float HX, HZ, Height;
        public float DoorWidth, DoorHeight;
        public uint Seed;
        /// <summary>The lake room's pool (index into Pools), or -1.</summary>
        public int Pool;
        /// <summary>
        /// The passage floor's height over the room's floor <see cref="Ramp"/> out from the wall's face (measured on
        /// the passages before rooms were added): the floor in front of the doorway is ramped to meet it.
        /// </summary>
        public float Approach;

        /// <summary>Wall thickness: the rock kept round the room's air, which the doorway passes through.</summary>
        public const float Wall = 1.2f;
        /// <summary>Length of the ramp in front of the doorway, from the wall's face out (m).</summary>
        public const float Ramp = 4f;
        /// <summary>Steepest the ramp may be (rise over run), well under the player's 45° slide.</summary>
        public const float RampSlope = 0.7f;
        /// <summary>The ramp's rise per metre out from the wall's face (negative when the passage is lower).</summary>
        public float Slope => math.clamp(Approach / Ramp, -RampSlope, RampSlope);
        /// <summary>The ramped floor's height (over the room's floor) at a local z in front of the wall's face.</summary>
        public float RampFloor(float z) => Slope * math.clamp(-z - Wall, 0f, Ramp);

        public float3 Right => new float3(F.y, 0f, -F.x);
        public float3 Forward => new float3(F.x, 0f, F.y);
        /// <summary>A world point in the room's frame (x across, y up from the floor, z into the room).</summary>
        public float3 Local(float3 p)
        {
            var q = p - Origin;
            return new float3(math.dot(q, Right), q.y, math.dot(q, Forward));
        }
        public float3 World(float3 l) => Origin + Right * l.x + new float3(0f, l.y, 0f) + Forward * l.z;
        public float3 Centre => World(new float3(0f, 0f, HZ));
    }

    /// <summary>W-115: the kinds of destination chamber. Values are stored in <see cref="CaveRoom.Type"/>; never renumber.</summary>
    public enum CaveRoomType { Grotto = 0, Lake = 1, Roost = 2, Ruin = 3 }

    /// <summary>One cave network: everything reached from one landmark's mouth.</summary>
    public struct CaveData
    {
        public int FirstNode, NodeCount;
        public int FirstSegment, SegmentCount;
        /// <summary>World-space box around all of the cave's air (with a margin).</summary>
        public float3 Min, Max;
        /// <summary>The middle of the entrance, where daylight comes in.</summary>
        public float3 Mouth;
        public float Seed;
        /// <summary>Its landmark's Key: the cave's own seeds come from it (not from its index).</summary>
        public uint Key;
        /// <summary>Its CaveStyle's slot in CaveStyleTable order, plus one (0 = no style).</summary>
        public int Style;
        /// <summary>Passage width and height multipliers (the style's Width, and Width × Height).</summary>
        public float2 Profile;
        /// <summary>Wall noise strength and frequency multipliers (the style's Roughness and Grain), its Ribs and RibSpacing.</summary>
        public float4 Wall;
        /// <summary>The style's Jaggedness, FloorFlatness, FloorSteps and Blockiness.</summary>
        public float4 Rock;
        /// <summary>The style's Angularity and FacetSize.</summary>
        public float2 Facet;
        public int FirstPool, PoolCount;
        public int FirstStream, StreamCount;
        /// <summary>W-115: its destination chambers (in Rooms).</summary>
        public int FirstRoom, RoomCount;
        /// <summary>
        /// W-110: 0 for a natural cave; otherwise a road tunnel bored under a crest (its nodes follow the road, its floor
        /// is the road's level, flat): 1 dark, 2 lit by lamps, 3 lamps and timber frames.
        /// </summary>
        public int Tunnel;
        /// <summary>
        /// A-91 row 3: the far end of each of the cave's main paths (1 to 3), as a node index from FirstNode, -1 for none:
        /// x the main passage's end, then the main side passages'. Every main path is graded gently (CaveBaking.MainGrade)
        /// from the mouth to its end, so it can be walked both ways; the other side passages may be steep.
        /// </summary>
        public int3 MainEnds;
    }

    /// <summary>
    /// An underground stream: a chain of points along a passage floor, each with its water surface height (never
    /// rising along the flow) and channel width. A channel is carved under it, and CaveSystem draws it with the
    /// terrain's water material, flowing from the first point to the last.
    /// </summary>
    public struct CaveStream
    {
        public int FirstPoint, PointCount;
        /// <summary>Box round the channel, to skip it cheaply when sampling.</summary>
        public float2 Min, Max;
        /// <summary>
        /// W-134 row 4: 1 for a river chamber's river, whose banks stop at their edge (its chamber's own floor meets
        /// them there) instead of rising on past it into the passages nearby.
        /// </summary>
        public float Chamber;
    }

    /// <summary>
    /// A plant on a cave floor (a glowing mushroom, a crystal, a mossy shrub): placed at bake time from the seed, and
    /// drawn by CaveSystem with the flora system's meshes and material.
    /// </summary>
    /// <summary>
    /// W-147: a ladder a climber can go up (A-101 Climbability grade 1): from its foot to its top (where a climber steps
    /// off), climbed from the Out side (horizontal, unit). On a flora model it's before the plant's own rotation and
    /// scale (FloraModel.Ladders); in CaveSetBlob.Ladders and from FloraPlacement.NearLadders it's in the world.
    /// Baked from the seed with everything else (rule 22).
    /// </summary>
    public struct ClimbLadder
    {
        public float3 Foot, Top, Out;
        public byte Grade;
    }

    public struct CavePlant
    {
        public float3 Position;
        public float Yaw, Scale;
        /// <summary>Index into CaveFloraTable's species.</summary>
        public int Species;
        /// <summary>Which of the species' variants, and a per-plant random for the flora shader.</summary>
        public int Variant;
        public float Random;
    }

    public struct CaveStreamPoint
    {
        /// <summary>The middle of the water surface.</summary>
        public float3 Position;
        public float Width, Depth;
    }

    /// <summary>
    /// A pool sunk into a chamber floor: a basin carved below the floor, filled to Level (lava or water). Drawn with
    /// the terrain's own water material; lava pools glow onto the walls round them and keep the player out.
    /// </summary>
    public struct CavePool
    {
        /// <summary>Centre of the surface (y = the surface level).</summary>
        public float3 Centre;
        public float Radius, Depth;
        /// <summary>1 = molten lava, 0 = water.</summary>
        public float Lava;
        /// <summary>
        /// W-134 row 3: an underground lake's hall, the domed chamber the pool fills (0 = an ordinary pool in a passage
        /// floor): how high the dome rises over the water (m) and how far it reaches across (m, from the centre). Its
        /// floor runs on past the water as a gently rising beach (CaveSampling.HallFloor).
        /// </summary>
        public float Hall, Span;
    }

    /// <summary>
    /// An underground waterfall: water pouring from a lip high on a chamber wall, out and down into a pool. Drawn by
    /// LandmarkEffectSystem as a waterfall curtain and a mist plume, lit as under rock, and, once it looks big enough,
    /// as a fine water sheet by WaterPatchSystem (W-135 row 3).
    /// </summary>
    public struct CaveWaterfall
    {
        /// <summary>How far a notch's water carries out from the lip as it falls (m): the reach when Reach is 0.</summary>
        public const float NotchReach = 0.9f;
        /// <summary>The lip, on the wall.</summary>
        public float3 Lip;
        public float Width;
        /// <summary>Horizontal direction the water leaves the wall.</summary>
        public float2 Out;
        /// <summary>Height from the lip to the pool's surface.</summary>
        public float Drop;
        /// <summary>
        /// W-134 row 4: 1 for a river chamber's chute, its stream pouring from where its flat water ends at the lip down
        /// to where it starts again at the foot; 0 for a waterfall from a notch.
        /// </summary>
        public float Spray;
        /// <summary>W-134 row 4 fix: how far out from the lip the water lands (m); 0 for a notch's usual short reach.</summary>
        public float Reach;
        /// <summary>How far out from the lip the water lands (m): Reach, or NotchReach for a notch.</summary>
        public float Carry => Reach > 0f ? Reach : NotchReach;
    }

    /// <summary>
    /// W-134 row 4: a river chamber, a long vaulted hall beside a passage with a river running its whole length, out of a
    /// hole in one end wall and into a hole in the other, dropping down a chute partway (whitewater, spray). Its floor
    /// runs along the axis from A (upstream) to B, falling gently and then by Drop over a short run at DropAt; its
    /// vault rises Height over the floor and is Width across at the floor. A short gallery joins it to its cave's
    /// passage at Door. Its river is one of its cave's streams (CaveStream), so the water is baked like any cave
    /// water; the hall's air joins its cave's in CaveSampling.Settle, as a lake's hall does.
    /// </summary>
    public struct CaveChamber
    {
        /// <summary>Its cave's index.</summary>
        public int Cave;
        /// <summary>The floor at the two ends of its axis (A upstream and higher).</summary>
        public float3 A, B;
        /// <summary>Half-width of the floor and the vault's height over it (m).</summary>
        public float Width, Height;
        /// <summary>Where along the axis the chute is (0 to 1), and how far the floor drops there (m).</summary>
        public float DropAt, Drop;
        /// <summary>The passage's floor where the gallery leaves it.</summary>
        public float3 Door;
        /// <summary>World-space box round the hall and its gallery (no margin).</summary>
        public float3 Min, Max;
    }

    /// <summary>One capsule of a passage, between two nodes, with the box its influence reaches.</summary>
    public struct CaveSegment
    {
        public int A, B;
        public int Cave;
        public float3 Min, Max;
    }

    /// <summary>
    /// A stalactite, stalagmite or column: a tapered rod of rock standing in a chamber's air, rooted in the floor or
    /// ceiling. It is part of the rock, so it's drawn with the walls (in the cave's own style), collided with and stood
    /// on like them: no meshes or draws of its own.
    /// </summary>
    public struct CaveSpeleothem
    {
        /// <summary>The root (inside the floor or ceiling) and the tip.</summary>
        public float3 Root, Tip;
        public float RootRadius, TipRadius;
        /// <summary>How softly it blends into the rock it stands on (m): 0.5 for stalagmites, less for loose rock.</summary>
        public float Fillet;
        /// <summary>
        /// 0: a tapered rod (root to tip). Otherwise a fallen boulder: a sharp-edged block cut by planes round Root
        /// (RootRadius across), Seed being this value (see <see cref="CaveSampling.Block"/>); with Tip off Root, a slab
        /// |Tip - Root| thick either side of Root across that way (W-134 row 4).
        /// </summary>
        public float Block;
        /// <summary>1: not rock but a hollow cut into the rock (a waterfall's notch): the rod's shape is taken out of it.</summary>
        public float Carve;
        /// <summary>
        /// W-134: above 0, a drip column (a rod grown floor to ceiling): its foot and top flare into the rock, with a waist,
        /// rings of flowstone and lumps, this many metres strong (see <see cref="CaveSampling.Column"/>).
        /// </summary>
        public float Flow;
        /// <summary>World-space box around it (with a margin).</summary>
        public float3 Min, Max;
    }

    /// <summary>
    /// Every cave: networks of nodes joined by segments, whose swept, merged spheres are the cave's air. A coarse
    /// column grid lists the segments reaching each cell (grouped by cave), so a sample only looks at the few
    /// passages nearby however many caves the world has. Speleothems have their own lists on the same grid.
    /// </summary>
    public struct CaveSetBlob
    {
        public BlobArray<CaveSpeleothem> Speleothems;
        public BlobArray<int2> SpeleothemCells;   // (first item, count), same grid as Cells
        public BlobArray<int> SpeleothemItems;
        public BlobArray<CaveData> Caves;
        public BlobArray<CaveNode> Nodes;
        /// <summary>Per node (same order as Nodes).</summary>
        public BlobArray<CaveNodeKind> Kinds;
        public BlobArray<CaveSegment> Segments;
        public BlobArray<CavePool> Pools;
        public BlobArray<CaveWaterfall> Waterfalls;
        public BlobArray<CaveStream> Streams;
        public BlobArray<CaveStreamPoint> StreamPoints;
        public BlobArray<CavePlant> Plants;
        /// <summary>W-115: destination chambers, grouped by cave.</summary>
        public BlobArray<CaveRoom> Rooms;
        /// <summary>W-134 row 4: river chambers (each names its cave; a handful an island, so scanned whole).</summary>
        public BlobArray<CaveChamber> Chambers;
        /// <summary>W-147: mines' ladder climbs (up shafts, between levels), in the world; a handful an island, so scanned whole.</summary>
        public BlobArray<ClimbLadder> Ladders;
        /// <summary>W-144: natural caves' climb routes (CaveClimbs queries them), the pieces that show them, and the shimmy
        /// ledges along the drops' rims; a few dozen an island, so scanned whole.</summary>
        public BlobArray<CaveClimb> Climbs;
        public BlobArray<ClimbPoint> ClimbPoints;
        public BlobArray<CaveLedge> Ledges;
        public float2 GridOrigin;
        public float CellSize;
        public int2 GridSize;
        public BlobArray<int2> Cells;   // (first item, count)
        public BlobArray<int> Items;    // segment indices, grouped by cave
    }

    /// <summary>
    /// The shape of caves as a signed distance: negative inside a cave's air, positive in rock. A cave is a network
    /// of capsules, blended smoothly where they meet and shaped by its style (wide, tall). Each passage's air stops at
    /// a floor: a level plane under its centre with gentle undulation and stepped ledges, so floors are flat enough to
    /// walk. Above it the walls get noise, and towards the ceiling ridged, fractured and blocky noise (jagged rock,
    /// overhangs, collapse hollows) that fades out before the floor. Rock is everywhere that is below the terrain
    /// surface and outside every cave.
    /// </summary>
    public static class CaveSampling
    {
        public const float Blend = 1.5f;
        /// <summary>How softly surface air and cave air merge at a mouth (m): rock thinner than about half this opens.
        /// Shared by the walls (CaveSystem.FieldJob) and the terrain's cut (TerrainSource.MouthCut), so they agree.</summary>
        public const float MouthBlend = 1.2f;
        /// <summary>How far under the terrain surface the cave walls end (m, measured across the slope). W-112: was 10 cm,
        /// which showed as a lip round every mouth where the terrain's cut (wider by up to half a quad) exposed the
        /// walls' skin; at 2 cm the skin sits flush with the surface and still stays under it.</summary>
        public const float SkinGap = 0.02f;
        /// <summary>Most the wall noise moves a wall at Roughness 1 (both octaves at full strength).</summary>
        public const float NoiseReach = 0.75f;
        /// <summary>Most the ceiling's ridges, fractures and blocks move it at Jaggedness 1.</summary>
        public const float JaggedReach = 1.3f;
        /// <summary>Height of one floor ledge (m).</summary>
        const float StepHeight = 0.35f;
        /// <summary>Distances whose wall noise is skipped are kept at least this far from zero (beyond the 1.2 m wall cut-off).</summary>
        const float SkipBeyond = 1.5f;

        /// <summary>Most a cave's wall and ceiling noise can move its walls.</summary>
        public static float WallReach(in CaveData c) => NoiseReach * c.Wall.x + JaggedReach * c.Rock.x + 0.45f * c.Wall.z + 0.64f * c.Facet.y * c.Facet.x;

        /// <summary>
        /// How far beyond a segment's radius its box reaches, before wall noise. Past the blend and the noise, the
        /// distance is exact for a few metres more, so terrain vertices near an entrance still interpolate the
        /// cut-out edge correctly.
        /// </summary>
        public const float SegmentMargin = Blend + 4f;

        /// <summary>
        /// Distance to one capsule of a passage with the cave's width and height, cut off at its floor. Stretching
        /// makes the field a little off true distance; scaling by the smaller stretch keeps it from overstating it, so
        /// nothing that steps by distance overshoots a wall. Out: the floor's height and the capsule's vertical radius
        /// here (for the noise's floor-to-ceiling fade).
        /// </summary>
        static float Capsule(in CaveNode a, in CaveNode b, float3 p, in CaveData c, float floorShape, float channel, out float floorY, out float height)
        {
            var ab = b.Position - a.Position;
            var t = math.saturate(math.dot(p - a.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
            var centre = a.Position + ab * t;
            var q = p - centre;
            var stretch = c.Profile.y / c.Profile.x;
            var flat = c.Rock.y;
            var r = math.lerp(a.Radius, b.Radius, t) * c.Profile.x;
            height = r * stretch;
            // The floor: halfway down at full flatness, sinking below the capsule's bottom (no effect) as flatness goes.
            floorY = centre.y - height * (0.45f + (1f - flat) * 0.65f) + floorShape;
            // A stream's channel cuts the floor down to its own bed wherever it runs, and through the passage's
            // underside too where the floor climbs above the water (otherwise the water ran on hidden in the rock
            // there and came out again further along, F-30): the underside is drawn down by as much as the bed lies
            // under the floor. A-91 row 3: this replaces an air slot cut inside the passage's width, whose sides made
            // 0.6 m steps where a main path crossed them; drawn down, the underside follows the bed smoothly.
            if (channel < floorY && q.y < 0f)
                q.y = math.min(0f, q.y + (floorY - channel));
            floorY = math.min(floorY, channel);
            q.y /= stretch;
            if (q.y < 0f)
                q.y *= math.lerp(1.8f, 1f, flat);   // a flatter underside, until the floor takes over
            var d = (math.length(q) - r) * math.min(1f, stretch);
            return math.max(d, floorY - p.y);
        }

        /// <summary>A stream bank's horizontal run per metre of rise (A-91 row 3: about 31 degrees at its steepest).</summary>
        public const float BankRun = 2.5f;
        /// <summary>How far past the bank's edge the cut keeps rising (m of rise), to meet a floor high above the water.</summary>
        public const float BankReach = 5f;

        /// <summary>
        /// The bed of a cave's streams at a spot (absolute height), or a large number away from them: a rounded channel
        /// under each stretch of each stream, Depth below its surface in the middle, rising past the floor at its banks.
        /// </summary>
        static float StreamBed(ref CaveSetBlob set, in CaveData c, float2 xz)
        {
            var bed = 1e9f;
            for (int s = c.FirstStream; s < c.FirstStream + c.StreamCount; s++)
            {
                ref var stream = ref set.Streams[s];
                if (math.any(xz < stream.Min) || math.any(xz > stream.Max))
                    continue;
                for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount - 1; i++)
                {
                    ref var a = ref set.StreamPoints[i];
                    ref var b = ref set.StreamPoints[i + 1];
                    var ab = b.Position.xz - a.Position.xz;
                    var along = math.dot(xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f);
                    // W-134 row 4: a river chamber's stretches cut only abreast of themselves (round ends only at the
                    // river's two ends), so the pool under its chute doesn't hollow out the run above it.
                    if (stream.Chamber > 0f && ((along < 0f && i > stream.FirstPoint) || (along > 1f && i < stream.FirstPoint + stream.PointCount - 2)))
                        continue;
                    var t = math.saturate(along);
                    var half = math.lerp(a.Width, b.Width, t) * 0.5f;
                    var across = math.distance(xz, a.Position.xz + ab * t);
                    var depth = math.lerp(a.Depth, b.Depth, t);
                    // A-91 row 3: the bank runs out at least BankRun times its rise, so a walk across the stream (a
                    // main path, a rabbit) wades down and up a gentle gully instead of meeting a 1 m slot.
                    var run = math.max(half * 1.5f, (depth + 1.2f) * BankRun);
                    var edge = half * 0.5f + run;
                    var beyond = stream.Chamber > 0f ? 0f : BankReach * BankRun;
                    if (across >= edge + beyond)
                        continue;
                    var surface = math.lerp(a.Position.y, b.Position.y, t);
                    // The bank rises smoothly from the bed to above the surface, then keeps rising at 1 in BankRun for
                    // BankReach more metres: where the floor climbs well above the water, the cut still meets it on a
                    // slope instead of ending in a step at the bank's edge (A-91 row 3: 0.6 m steps on main paths).
                    depth -= (depth + 1.2f) * math.smoothstep(half * 0.5f, edge, across) + math.max(0f, across - edge) / BankRun;
                    bed = math.min(bed, surface - depth + 0.05f);
                }
            }
            return bed;
        }

        /// <summary>
        /// A cave's floor height offset at a spot: gentle undulation plus ledges with short ramps between them, sunk
        /// into a basin under each of its pools.
        /// </summary>
        static float FloorShape(ref CaveSetBlob set, in CaveData c, float2 xz)
        {
            if (c.Tunnel != 0)
                return 0f;   // a road tunnel's floor is its road: no undulation for the road to snag on
            var s = c.Seed;
            var shape = noise.snoise(xz * 0.07f + s) * 0.18f;
            if (c.Rock.z > 0f)
            {
                var v = (noise.snoise(xz * 0.045f - s) * 0.5f + 0.5f) * 4f * c.Rock.z;
                var step = math.floor(v);
                shape += (step + math.smoothstep(0.4f, 0.6f, v - step) - 2f * c.Rock.z) * StepHeight;
            }
            for (int i = c.FirstPool; i < c.FirstPool + c.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Hall > 0f)
                    continue;   // a lake carries its own floor (HallFloor), under its hall
                var d = math.distance(xz, pool.Centre.xz);
                if (d < pool.Radius * 1.2f)
                    shape -= pool.Depth * math.smoothstep(pool.Radius * 1.2f, pool.Radius * 0.7f, d);
            }
            return shape;
        }

        /// <summary>
        /// The walls' noise: gentle near the floor, and towards the ceiling ridges (low and broad), fractures (sharp
        /// creases) and blocky collapse hollows, by the cave's Jaggedness and Blockiness. Ridges and creases push rock in
        /// and hollow it out, so the ceiling gets both hanging ribs and overhung pockets; blocks only hollow (a
        /// collapse leaves a hole, never a slab hanging in the air).
        /// </summary>
        static float WallNoise(in CaveData c, float3 p, float floorY, float height)
        {
            var s = c.Seed;
            var g = c.Wall.y;
            // 0 at the floor (its own floor, ledges and all, so a ledge doesn't sprout ceiling noise), 1 at the top.
            var up = math.saturate((p.y - floorY) / math.max(height * 1.45f, 0.1f));
            // A-91: the relief below fades by height above the floor in metres too, so a low or squashed passage
            // doesn't grow a knee-high fin of rock across its floor (a main path must stay a walk).
            var lift = math.smoothstep(0.35f, 1.1f, p.y - floorY);
            var n = (noise.snoise(p * 0.22f * g + s) * 0.55f + noise.snoise(p * 0.6f * g - s) * 0.2f) * c.Wall.x
                    * math.lerp(0.3f, 1f, math.smoothstep(0f, 0.35f, up));
            if (c.Rock.x > 0f && up > 0.2f)
            {
                var ridge = 1f - math.abs(noise.snoise(p * 0.12f * g + s * 0.7f));
                var crease = math.abs(noise.snoise(p * 0.5f * g - s * 1.3f));
                var jag = (ridge * ridge - 0.45f) * 0.9f + (crease - 0.5f) * 0.35f;
                var blocks = c.Rock.w > 0f ? math.min(0f, noise.snoise(math.floor(p * (1f / 1.7f)) * 0.37f + s)) * 0.6f * c.Rock.w * math.smoothstep(0.45f, 1f, up) : 0f;
                n += (jag + blocks) * c.Rock.x * math.smoothstep(0.2f, 0.75f, up) * lift;
            }
            // Angular faces: three sawtooth waves across directions set per cave add up to a relief that is flat
            // between creases, so the walls break into planes meeting at sharp edges. Continuous (the field stays
            // steady), and off the floor like the ribs.
            if (c.Facet.x > 0f && up > 0.06f)
            {
                var tri = 0f;
                for (int i = 0; i < 3; i++)
                {
                    var a = c.Seed * (2.3f + i * 1.7f) + i * 2.1f;
                    var dir = math.normalize(new float3(math.sin(a), math.cos(a * 1.3f) * 0.8f, math.cos(a)));
                    tri += math.abs(math.frac(math.dot(p, dir) / c.Facet.y + i * 0.37f) - 0.5f);
                }
                n += (tri - 0.75f) * c.Facet.y * 0.85f * c.Facet.x * math.smoothstep(0.06f, 0.3f, up) * lift;
            }
            // Ribs (melt-scalloped ice), with scooped hollows between (about 0.45 m deep at full Ribs). Off the floor, so
            // FloorUnder's march isn't affected.
            if (c.Wall.z > 0f && up > 0.06f)
            {
                var rib = Rib(c, p);
                n += (rib * rib - 0.4f) * 0.75f * c.Wall.z * math.smoothstep(0.06f, 0.3f, up) * lift;
            }
            return n;
        }

        /// <summary>
        /// A cave's rib pattern at a point: 1 on a rib's sharp crest, 0 in the middle of the hollow between two. Ribs run
        /// RibSpacing apart across a slant set per cave, wavering with noise. Shared by the walls' shape and the cave
        /// mesh's shading (hollows darkened, so the ribs read even in flat light).
        /// </summary>
        public static float Rib(in CaveData c, float3 p)
        {
            var s = c.Seed;
            var across = math.normalize(new float3(math.sin(s * 3.1f), 0.35f, math.cos(s * 3.1f)));
            var t = math.dot(p, across) / c.Wall.w + noise.snoise(p * 0.12f + s) * 0.25f;
            return 1f - math.abs(math.sin(t * math.PI));
        }

        /// <summary>
        /// How much further than true distance a cave's field can reach along its stretch (1 for a round passage): it
        /// is scaled by the smaller stretch, so margins measured in it must grow by the larger.
        /// </summary>
        public static float Stretch(float2 profile)
        {
            var s = profile.y / profile.x;
            return math.max(1f, math.max(s, 1f / s));
        }

        /// <summary>The range of grid items (indices into Items) for the column containing a point.</summary>
        static int2 CellRange(ref CaveSetBlob set, float2 xz)
        {
            if (set.Cells.Length == 0)
                return int2.zero;
            var c = (int2)math.floor((xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return int2.zero;
            return set.Cells[c.y * set.GridSize.x + c.x];
        }

        /// <summary>
        /// Distance to the nearest cave's air at a point (a large number away from every cave). Only the segments
        /// whose boxes hold the point count: the ones left out are further than the blend from it, where the smooth
        /// minimum is an exact minimum, so the result matches a sweep over every segment wherever it is near zero.
        /// </summary>
        public static float Distance(ref CaveSetBlob set, float3 p, out int cave)
        {
            cave = -1;
            var best = 1e9f;
            var range = CellRange(ref set, p.xz);
            var current = -1;
            var d = 1e9f;
            float floorShape = 0f, channel = 1e9f, nearest = 1e9f, floorY = 0f, height = 1f;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.any(p < seg.Min) || math.any(p > seg.Max))
                    continue;
                if (seg.Cave != current)
                {
                    // Items are grouped by cave, so the previous cave is done.
                    Settle(ref set, current, d, p, floorY, height, floorShape, channel, ref best, ref cave);
                    current = seg.Cave;
                    d = 1e9f;
                    nearest = 1e9f;
                    floorShape = FloorShape(ref set, set.Caves[current], p.xz);
                    channel = set.Caves[current].StreamCount > 0 ? StreamBed(ref set, set.Caves[current], p.xz) : 1e9f;
                }
                var ds = Capsule(set.Nodes[seg.A], set.Nodes[seg.B], p, set.Caves[current], floorShape, channel, out var fy, out var h);
                // The noise fades from floor to ceiling by the nearest passage's own floor and ceiling.
                if (ds < nearest)
                {
                    nearest = ds;
                    floorY = fy;
                    height = h;
                }
                d = SmoothMin(d, ds, Blend);
            }
            Settle(ref set, current, d, p, floorY, height, floorShape, channel, ref best, ref cave);
            if (best < SpeleothemReach && set.SpeleothemCells.Length > 0)
            {
                best = Speleothems(ref set, p, best);
                // A speleothem never stands in a destination chamber: its walls and air are set again over them (W-115).
                if (cave >= 0)
                    for (int i = set.Caves[cave].FirstRoom; i < set.Caves[cave].FirstRoom + set.Caves[cave].RoomCount; i++)
                        best = Room(set.Rooms[i], p, best);
            }
            return best;
        }

        /// <summary>
        /// A lower bound on the true distance to the cave surface at a point (W-33): the same sign as Distance, and never
        /// more than the real gap from air to rock or from rock to air, so pushing out of rock or stepping a sphere-cast by
        /// it never overshoots. Distance is a field, not a true distance: the flatter underside, the floor's ledges and
        /// the wall noise (ribs, creases, facets) make it read up to 2-4x the real gap. This divides it by the steeper of
        /// the field's slope here and the cave's SlopeLimit. Five field samples. Distance itself must stay as it is:
        /// carving, meshing and placement depend on its exact values (and the cave and landmark hashes with them).
        /// </summary>
        public static float LowerBound(ref CaveSetBlob set, float3 p)
        {
            var d = Distance(ref set, p, out var cave);
            if (cave < 0 || math.abs(d) > 1e8f)
                return d;
            // The slope from four samples on a tetrahedron round the point.
            const float h = 0.04f;
            float3 a = new float3(1f, -1f, -1f), b = new float3(-1f, -1f, 1f), c = new float3(-1f, 1f, -1f), e = new float3(1f, 1f, 1f);
            var slope = a * Distance(ref set, p + a * h, out _) + b * Distance(ref set, p + b * h, out _) +
                        c * Distance(ref set, p + c * h, out _) + e * Distance(ref set, p + e * h, out _);
            var steep = math.length(slope) / (4f * h);
            return d / math.max(SlopeLimit(set.Caves[cave]), steep * 1.2f);
        }

        /// <summary>
        /// How steeply a cave's field can climb near its walls compared with true distance, from its wall noise: ridges
        /// and creases, collapse blocks (which also step), facets and ribs. Fitted with a margin to a probe of every
        /// cave (W-33), not derived: retune it with that probe if WallNoise changes.
        /// </summary>
        public static float SlopeLimit(in CaveData c) =>
            1f + 0.55f * c.Rock.x + 0.35f * c.Wall.x + c.Rock.w * c.Rock.x + 2f * c.Facet.x + 2f * c.Wall.z;

        /// <summary>
        /// Only near the air do speleothems matter: they turn air into rock, and the few carved ones (waterfall notches,
        /// within a metre of the wall) rock into air.
        /// </summary>
        const float SpeleothemReach = 2f;

        /// <summary>Takes the speleothems near a point out of the air: a smooth maximum with each one's rock.</summary>
        static float Speleothems(ref CaveSetBlob set, float3 p, float air)
        {
            var c = (int2)math.floor((p.xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return air;
            var range = set.SpeleothemCells[c.y * set.GridSize.x + c.x];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Speleothems[set.SpeleothemItems[k]];
                if (math.any(p < s.Min) || math.any(p > s.Max))
                    continue;
                air = s.Carve > 0f ? SmoothMin(air, Rod(s, p), math.max(s.Fillet, 0.05f))
                                   : -SmoothMin(-air, Shape(s, p), math.max(s.Fillet, 0.05f));
            }
            return air;
        }

        /// <summary>
        /// W-138 row 4b: distance from a point to the nearest solid speleothem (rod or block, not a carved notch) round
        /// it, a large number with none. For the look only (a road tunnel's fallen stones take their own tone).
        /// </summary>
        public static float StoneDistance(ref CaveSetBlob set, float3 p)
        {
            var best = 1e9f;
            if (set.SpeleothemCells.Length == 0)
                return best;
            var c = (int2)math.floor((p.xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return best;
            var range = set.SpeleothemCells[c.y * set.GridSize.x + c.x];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Speleothems[set.SpeleothemItems[k]];
                if (s.Carve > 0f || math.any(p < s.Min) || math.any(p > s.Max))
                    continue;
                best = math.min(best, s.Block != 0f ? Block(s, p) : Rod(s, p));
            }
            return best;
        }

        /// <summary>
        /// Distance to a fallen boulder: the intersection of ten planes round its middle, each facing its own way (from
        /// the boulder's seed) and set at its own distance, so it's an irregular block with flat faces and sharp
        /// edges. The largest plane distance never overstates the true distance, so stepping never overshoots.
        /// </summary>
        internal static float Block(in CaveSpeleothem s, float3 p)
        {
            var q = p - s.Root;
            var d = -1e9f;
            for (int i = 0; i < 10; i++)
            {
                // Spread round the sphere (golden spiral), turned by the seed, each face nearer or farther.
                var y = 1f - (i + 0.5f) / 5f;
                var ring = math.sqrt(math.saturate(1f - y * y));
                var a = i * 2.39996f + s.Block * 6.283f;
                var n = math.normalize(new float3(math.cos(a) * ring, y * 0.85f, math.sin(a) * ring));
                var h = math.frac(math.sin((i + 1) * 12.9898f + s.Block * 78.233f) * 43758.5453f);
                d = math.max(d, math.dot(q, n) - s.RootRadius * (0.72f + 0.3f * h));
            }
            // W-134 row 4: a slab, when Tip is off Root: cut to |Tip - Root| either side of the plane through Root facing it.
            var across = s.Tip - s.Root;
            var thick = math.length(across);
            if (thick > 1e-4f)
                d = math.max(d, math.abs(math.dot(q, across / thick)) - thick);
            return d;
        }

        /// <summary>Distance to a speleothem's rock, whichever shape it is (a boulder, a drip column or a plain rod).</summary>
        internal static float Shape(in CaveSpeleothem s, float3 p) =>
            s.Block != 0f ? Block(s, p) : s.Flow > 0f ? Column(s, p) : Rod(s, p);

        /// <summary>
        /// W-134: distance to a drip column, a rod from Root (its foot) to Tip (in the ceiling) whose radius swells: a skirt
        /// at the foot and a wider bell where it meets the ceiling, a waist below the middle, uneven rings of flowstone
        /// and lumps (from a seed off its root). Each swell steepens the field, so the distance is divided by the
        /// steepest it can get: stepping never overshoots.
        /// </summary>
        internal static float Column(in CaveSpeleothem s, float3 p)
        {
            var ab = s.Tip - s.Root;
            var len = math.max(math.length(ab), 1e-3f);
            var t = math.saturate(math.dot(p - s.Root, ab) / (len * len));
            var h = t * len;
            var seed = math.frac(math.dot(s.Root, new float3(0.1371f, 0.7113f, 0.2917f))) * 61f;
            var r = math.lerp(s.RootRadius, s.TipRadius, t)
                    + s.Flow * (1.6f * math.exp(-h / 1.1f) + 2f * math.exp(-(len - h) / 1.4f))
                    - s.Flow * 0.45f * math.sin(math.PI * t)
                    + s.Flow * 0.9f * Ring(h * 2.6f + seed + 0.8f * noise.snoise(p * 0.7f + seed))
                    + s.Flow * 0.7f * noise.snoise(p * 0.9f - seed);
            return (math.distance(p, s.Root + ab * t) - r) * 0.9f / (1f + 6.5f * s.Flow);
        }

        /// <summary>A flowstone ring's profile: narrow ridges with wide hollows between them (0..1).</summary>
        static float Ring(float x)
        {
            var w = 0.5f + 0.5f * math.sin(x);
            return w * w * w;
        }

        /// <summary>Distance to a tapered rod (a cone with rounded ends), slightly understated so stepping never overshoots.</summary>
        internal static float Rod(in CaveSpeleothem s, float3 p)
        {
            var ab = s.Tip - s.Root;
            var t = math.saturate(math.dot(p - s.Root, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return (math.distance(p, s.Root + ab * t) - math.lerp(s.RootRadius, s.TipRadius, t)) * 0.9f;
        }

        /// <summary>
        /// Adds a finished cave's wall noise (and its lake halls and river chambers) and keeps it if it's the nearest so
        /// far. floorShape and channel are the cave's FloorShape and StreamBed here.
        /// </summary>
        static void Settle(ref CaveSetBlob set, int current, float d, float3 p, float floorY, float height, float floorShape, float channel,
                           ref float best, ref int cave)
        {
            if (current < 0)
                return;
            ref var c = ref set.Caves[current];
            // Well beyond the noise's reach of the wall, it can't decide rock or air: skip it, and step the distance
            // towards the wall by its reach so anything that steps by distance still never overshoots. The skipped
            // values stay at least SkipBeyond from zero, so everything near a wall (and the meshing's cut-off) is exact.
            var reach = WallReach(c);
            var full = d > reach + SkipBeyond ? d - reach
                     : d < -reach - SkipBeyond ? d + reach
                     : d + WallNoise(c, p, floorY, height);
            // W-115: each destination chamber walls its box off from the cave's own air, then carves its room and
            // doorway in (always, not only near walls, so the field stays continuous).
            // W-134 row 3: each lake's hall joins the cave's air (a smooth union, so the passage opens into it).
            for (int i = c.FirstPool; i < c.FirstPool + c.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Hall <= 0f || math.distance(p.xz, pool.Centre.xz) - pool.Span - reach - Blend - 2f > full)
                    continue;
                var floor = HallFloor(pool, math.distance(p.xz, pool.Centre.xz));
                var hall = math.max(HallDome(pool, p) + WallNoise(c, p, floor, pool.Hall), (floor - p.y) * 0.6f);
                full = SmoothMin(full, hall, Blend);
            }
            // W-134 row 4: each river chamber's vault and its gallery join the cave's air the same way. Its floor is cut
            // down by the cave's streams (its own river among them) like a passage's, and undulates like one.
            for (int i = 0; i < set.Chambers.Length; i++)
            {
                ref var ch = ref set.Chambers[i];
                if (ch.Cave != current || math.cmax(math.max(ch.Min - p, p - ch.Max)) - reach - Blend - 2f > full)
                    continue;
                var vault = ChamberVault(ch, p, out var floorAt, out var scale);
                // Its floor only undulates up (never under its river's surface, which sits just under floorAt).
                var floor = math.min(floorAt + math.max(floorShape, 0f), channel);
                var air = math.max(vault + WallNoise(c, p, floor, ch.Height), (floor - p.y) * 0.6f * scale);
                var gallery = ChamberGallery(ch, p, out var galleryFloor);
                air = SmoothMin(air, gallery + WallNoise(c, p, galleryFloor, GalleryRadius), Blend);
                full = SmoothMin(full, air, Blend);
            }
            for (int i = c.FirstRoom; i < c.FirstRoom + c.RoomCount; i++)
                full = Room(set.Rooms[i], p, full);
            if (full < best)
            {
                best = full;
                cave = current;
            }
        }

        /// <summary>
        /// W-115: a destination chamber's air at a point, given the cave's own: the cave's air is taken out of the
        /// room's clip box (its air plus the wall round it), then the room box, the doorway stub through the wall and
        /// (in a lake room) the pool's basin are added. Boxes are exact distances, so stepping never overshoots.
        /// </summary>
        internal static float Room(in CaveRoom r, float3 p, float air)
        {
            var l = r.Local(p);
            const float t = CaveRoom.Wall;
            var cave = air;
            var clip = Box(l, new float3(0f, (r.Height + t - 3f) * 0.5f, r.HZ), new float3(r.HX + t, (r.Height + t + 3f) * 0.5f, r.HZ + t));
            air = math.max(air, -clip);
            var room = Box(l, new float3(0f, r.Height * 0.5f, r.HZ), new float3(r.HX, r.Height * 0.5f, r.HZ));
            // The doorway stub runs on through the porch and 1.2 m into the room (both wider and taller, so the meshed
            // rock is unchanged): boxes that only touch give the union too short a distance at their seam (0.1 m at
            // the threshold), and the walker's push-out spheres were held back there (W-128).
            var door = Box(l, new float3(0f, r.DoorHeight * 0.5f, (1.2f - t - Porch) * 0.5f), new float3(r.DoorWidth * 0.5f, r.DoorHeight * 0.5f, (t + Porch + 1.2f) * 0.5f));
            // A porch out in front, wider and taller than the doorway, so the passage opens to the whole frame and no
            // rock stands across its jambs.
            // It starts at the wall's face, where the frame stands, and clears the frame's posts and lintel.
            var porch = Box(l, new float3(0f, (r.DoorHeight + 0.6f) * 0.5f, -(t + Porch * 0.5f)),
                            new float3(r.DoorWidth * 0.5f + 0.6f, (r.DoorHeight + 0.6f) * 0.5f, Porch * 0.5f));
            // In front of the porch the floor is ramped to meet the passage's (Approach): the air of the doorway's
            // size above the ramp, and rock below it (filling where the passage dips), so the doorway is a walk up or
            // down a slope rather than a step or a pit. Sheared boxes, scaled so they still never overstate distance.
            var k = r.Slope;
            var sheared = new float3(l.x, l.y - r.RampFloor(l.z), l.z);
            var scale = math.rsqrt(1f + k * k);
            var run = Box(sheared, new float3(0f, (r.DoorHeight + 0.6f) * 0.5f, -(t + CaveRoom.Ramp * 0.5f)),
                          new float3(r.DoorWidth * 0.5f + 0.6f, (r.DoorHeight + 0.6f) * 0.5f, CaveRoom.Ramp * 0.5f)) * scale;
            air = math.min(air, math.min(math.min(room, door), math.min(porch, run)));
            // Where the passage meets the ramp's far end it flares out (a smooth union), so the passage's walls round
            // into it instead of standing as thin sheets across the frame. Kept clear of the wall's face, so the rock
            // still backs the frame's posts and lintel.
            var mouth = math.min(porch, run);
            var h = math.max(Flare - math.abs(cave - mouth), 0f) / Flare;
            var flare = math.min(cave, mouth) - h * h * Flare * 0.25f;
            // Only above the ramp's plane (carried on past its end): the fillet rounds the walls and roof, but under the
            // floor it dug a trench in front of the ramp, a 0.7 m step the walker could not climb (W-128).
            air = math.min(air, math.max(math.max(flare, l.z + t + 0.4f), -sheared.y * scale));
            var fill = Box(sheared, new float3(0f, -1.25f, -(t + CaveRoom.Ramp * 0.5f)),
                           new float3(r.DoorWidth * 0.5f + 1.6f, 1.25f, CaveRoom.Ramp * 0.5f)) * scale;
            air = math.max(air, -fill);
            if (r.Type == (int)CaveRoomType.Lake)
            {
                // A round basin in the middle of the floor. A-91: a bowl, its sides sloping in (1 down in 1.6 across)
                // rather than a sheer 1.6 m drop, so whoever walks in can walk back out (a main path may end here).
                var radius = LakeRadius(r);
                var across = math.length(l.xz - new float2(0f, r.HZ));
                var side = (across - math.max(radius + math.min(l.y, 0f) * 1.6f, 0.5f)) * 0.53f;
                air = math.min(air, math.max(side, math.max(-l.y - LakeDepth, l.y - 0.5f)));
            }
            return air;
        }

        /// <summary>Depth of the porch carved in front of a destination chamber's doorway (m).</summary>
        const float Porch = 1.6f;

        /// <summary>Reach of the smooth union between a porch's mouth and the passage (m).</summary>
        const float Flare = 1.6f;

        /// <summary>
        /// W-115 row 2: surface nets put a wall vertex at the average of its cell's crossings, which rounds a room's
        /// inside corners up to half a voxel out into its air, where the rock would show past the tiles and trims. A
        /// wall vertex inside a room's box or doorway stub is moved onto the nearest face (1 cm behind it), so the
        /// meshed rock keeps to the room's planes and the kit always covers it. Mesh only: the field is unchanged.
        /// </summary>
        public static float3 OutOfRooms(ref CaveSetBlob set, float3 p)
        {
            const float back = 0.01f;
            // Under this far from a second face (less than a voxel), a vertex belongs to the corner, not the face.
            const float Corner = 0.35f;
            const float t = CaveRoom.Wall;
            for (int i = 0; i < set.Rooms.Length; i++)
            {
                var r = set.Rooms[i];
                var l = r.Local(p);
                if (math.abs(l.x) > r.HX + 0.5f || l.z < -t - Porch - 0.5f || l.z > 2f * r.HZ + 0.5f || l.y < -0.5f || l.y > r.Height + 0.5f)
                    continue;
                var halfDoor = r.DoorWidth * 0.5f;
                var inOpening = math.abs(l.x) < halfDoor && l.y < r.DoorHeight;
                // Depth inside each face of the room box (the front face has the doorway cut through it).
                var side = r.HX - math.abs(l.x);
                var floor = l.y;
                var roof = r.Height - l.y;
                // Open across the doorway, but only in the room itself: a vertex back in the stub is the stub's (taken
                // onto a room face, it would stretch a sheet of rock across the opening).
                var front = inOpening && l.z > -back ? float.MaxValue : l.z;
                var rear = 2f * r.HZ - l.z;
                if (math.cmin(new float4(side, floor, roof, rear)) > -back && front > -back)
                {
                    // Onto the nearest face, and onto any other face it is within Corner of too: a vertex in an
                    // inside corner's cell goes onto the edge itself, so no rock face cuts across the corner.
                    var least = math.min(math.cmin(new float4(side, floor, roof, rear)), front);
                    if (side == least || side < Corner) l.x = math.sign(l.x) * (r.HX + back);
                    if (floor == least || floor < Corner) l.y = -back;
                    else if (roof == least || roof < Corner) l.y = r.Height + back;
                    if (rear == least || rear < Corner) l.z = 2f * r.HZ + back;
                    else if (front == least || front < Corner) l.z = -back;
                    return r.World(l);
                }
                // The doorway stub: its sides, soffit and threshold (its ends open into the room and the passage).
                if (l.z < 0.1f && l.z > -t)
                {
                    var stubSide = halfDoor - math.abs(l.x);
                    var stubRoof = r.DoorHeight - l.y;
                    if (stubSide > -back && floor > -back && stubRoof > -back)
                    {
                        var least = math.min(stubSide, math.min(floor, stubRoof));
                        if (stubSide == least || stubSide < Corner) l.x = math.sign(l.x) * (halfDoor + back);
                        if (floor == least || floor < Corner) l.y = -back;
                        else if (stubRoof == least || stubRoof < Corner) l.y = r.DoorHeight + back;
                        return r.World(l);
                    }
                }
                // The porch: its sides, roof, floor and the wall's face round the doorway (its front opens into the
                // passage). Without this, the corner where the wall meets the porch rounds out into a sheet of rock
                // standing in front of the frame's posts.
                if (l.z <= -t + back && l.z > -t - Porch)
                {
                    // The porch's floor follows the ramp (its roof too, so the opening keeps its height).
                    var ramp = r.RampFloor(l.z);
                    floor = l.y - ramp;
                    var porchHalf = halfDoor + 0.6f;
                    var porchSide = porchHalf - math.abs(l.x);
                    var porchRoof = math.max(ramp, 0f) + r.DoorHeight + 0.6f - l.y;
                    var face = inOpening ? float.MaxValue : -t - l.z;
                    if (porchSide > -back && floor > -back && porchRoof > -back && face > -back)
                    {
                        var least = math.min(math.min(porchSide, floor), math.min(porchRoof, face));
                        if (porchSide == least || porchSide < Corner) l.x = math.sign(l.x) * (porchHalf + back);
                        if (floor == least || floor < Corner) l.y = ramp - back;
                        else if (porchRoof == least || porchRoof < Corner) l.y = math.max(ramp, 0f) + r.DoorHeight + 0.6f + back;
                        if (face == least || face < Corner) l.z = -t + back;
                        return r.World(l);
                    }
                }
            }
            return p;
        }

        public const float LakeDepth = 1.6f;
        public static float LakeRadius(in CaveRoom r) => math.min(r.HX, r.HZ) * 0.72f;

        /// <summary>How steeply a lake hall's beach rises from the water's edge to its walls (1 up in 4 across, W-134 row 3).</summary>
        public const float HallBeach = 0.25f;

        /// <summary>
        /// W-134 row 3: a lake hall's floor at a distance across from its centre: the pool's basin (as WaterAt reads it)
        /// out to 1.2 radii, then a beach rising from just over the water.
        /// </summary>
        public static float HallFloor(in CavePool pool, float across)
        {
            var edge = pool.Radius * 1.2f;
            return across < edge ? pool.Centre.y + 0.12f - pool.Depth * math.smoothstep(edge, pool.Radius * 0.7f, across)
                                 : pool.Centre.y + 0.12f + (across - edge) * HallBeach;
        }

        /// <summary>
        /// W-134 row 3: distance to a lake hall's dome, an ellipsoid whose middle sits under the water, so its walls
        /// meet the beach steeply and its roof is Hall over the surface. The usual ellipsoid estimate (close to true
        /// distance near the surface, where it matters).
        /// </summary>
        internal static float HallDome(in CavePool pool, float3 p)
        {
            var drop = pool.Hall * (0.4f / 0.6f);
            var radii = new float3(pool.Span, pool.Hall + drop, pool.Span);
            var q = p - new float3(pool.Centre.x, pool.Centre.y - drop, pool.Centre.z);
            var k0 = math.length(q / radii);
            var k1 = math.length(q / (radii * radii));
            return k1 < 1e-6f ? -math.cmin(radii) : k0 * (k0 - 1f) / k1;
        }

        /// <summary>W-134 row 4: a river chamber's chute runs this many metres per metre it drops (about 35 degrees at its steepest).</summary>
        public const float ChuteRun = 1.4f;
        /// <summary>
        /// W-134 row 4 fix: a river chamber's stream falls faster than this (m per m) only down its chute (its hall falls
        /// far more gently), so a segment this steep is part of the chute: its water pours as a curtain, not a flat sheet.
        /// </summary>
        public const float ChuteSlope = 0.25f;

        /// <summary>W-134 row 4 fix: whether a river chamber's stream segment a-b runs down its chute (see ChuteSlope).</summary>
        public static bool ChuteSegment(float3 a, float3 b) => a.y - b.y > ChuteSlope * math.distance(a.xz, b.xz);
        /// <summary>W-134 row 4: a river chamber's gallery: its round roof's radius, and that roof's middle over the floor (m).</summary>
        public const float GalleryRadius = 2.4f, GalleryRise = 1.2f;   // collision: the carved tunnel's size, not a draw range

        /// <summary>
        /// W-134 row 4: a river chamber's floor (no noise) at a distance along its axis from A, and how steeply it falls
        /// there (m per m): a gentle fall from end to end, then the chute.
        /// </summary>
        public static float ChamberFloor(in CaveChamber ch, float u, out float slope)
        {
            var length = math.max(math.distance(ch.A.xz, ch.B.xz), 1f);
            var fall = (ch.A.y - ch.B.y - ch.Drop) / length;
            var run = ch.Drop * ChuteRun;
            var x = math.saturate((u - (ch.DropAt * length - run * 0.5f)) / math.max(run, 0.01f));
            slope = (u > 0f && u < length ? fall : 0f) + ch.Drop * 6f * x * (1f - x) / math.max(run, 0.01f);
            return ch.A.y - fall * math.clamp(u, 0f, length) - ch.Drop * x * x * (3f - 2f * x);
        }

        /// <summary>W-134 row 4: a point's place by a river chamber's axis: x along it from A, y across it (left positive).</summary>
        public static float2 ChamberLocal(in CaveChamber ch, float2 xz)
        {
            var dir = math.normalizesafe(ch.B.xz - ch.A.xz, new float2(1f, 0f));
            var rel = xz - ch.A.xz;
            return new float2(math.dot(rel, dir), dir.x * rel.y - dir.y * rel.x);
        }

        /// <summary>
        /// W-134 row 4: distance to a river chamber's vault, a long ellipsoid whose middle sinks under the floor (so its
        /// walls meet the floor steeply) and follows the floor down the chute, rounded at both ends. The ellipsoid
        /// estimate, scaled down where the chute shears it so stepping never overshoots. Out: the floor here, and that
        /// scale (for the floor's own cut).
        /// </summary>
        internal static float ChamberVault(in CaveChamber ch, float3 p, out float floor, out float scale)
        {
            var length = math.max(math.distance(ch.A.xz, ch.B.xz), 1f);
            var l = ChamberLocal(ch, p.xz);
            floor = ChamberFloor(ch, l.x, out var slope);
            scale = math.rsqrt(1f + slope * slope);
            var sink = ch.Height * 0.35f;
            var tall = ch.Height + sink;
            var wide = ch.Width * math.rsqrt(1f - sink * sink / (tall * tall));
            var radii = new float3(wide, tall, wide);
            var q = new float3(l.y, p.y - (floor - sink), l.x - math.clamp(l.x, 0f, length));
            var k0 = math.length(q / radii);
            var k1 = math.length(q / (radii * radii));
            return (k1 < 1e-6f ? -math.cmin(radii) : k0 * (k0 - 1f) / k1) * scale;
        }

        /// <summary>W-134 row 4: where a river chamber's gallery comes out on its floor (on the axis, abreast of the door).</summary>
        public static float3 ChamberDoorEnd(in CaveChamber ch)
        {
            var length = math.max(math.distance(ch.A.xz, ch.B.xz), 1f);
            var u = math.clamp(ChamberLocal(ch, ch.Door.xz).x, 0f, length);
            var xz = ch.A.xz + math.normalizesafe(ch.B.xz - ch.A.xz, new float2(1f, 0f)) * u;
            return new float3(xz.x, ChamberFloor(ch, u, out _), xz.y);
        }

        /// <summary>
        /// W-134 row 4: distance to a river chamber's gallery, a round-roofed tunnel from the passage's floor at Door to
        /// the chamber's floor, its own floor sloping evenly between them. Out: that floor here.
        /// </summary>
        internal static float ChamberGallery(in CaveChamber ch, float3 p, out float floor)
        {
            var end = ChamberDoorEnd(ch);
            var ab = end - ch.Door;
            var t = math.saturate(math.dot(p - ch.Door, ab) / math.max(math.lengthsq(ab), 1e-6f));
            floor = math.lerp(ch.Door.y, end.y, t);
            var centre = ch.Door + ab * t + new float3(0f, GalleryRise, 0f);
            return math.max(math.distance(p, centre) - GalleryRadius, floor - p.y);
        }

        /// <summary>How far above a cave water surface a point still counts as over it (m, W-134).</summary>
        // gameplay: tall enough for a swimmer's head and a wading creature, short of the passage stacked above
        public const float WaterReachAbove = 2.5f;
        /// <summary>How far below a cave water bed a point still counts as in it (m, W-134): a body's feet sunk in.</summary>
        public const float WaterReachBelow = 1.5f;

        /// <summary>
        /// W-134: the cave water (a pool's or a stream's, never lava) at a point, if the point is in it or a little over it:
        /// its surface, its bed (estimated from how the basin and channel are carved), the flow and how steep the stream
        /// is. Only the caves whose segments reach the point's column are looked at, and only their pools and streams
        /// whose boxes hold it. Where two waters overlap (a stream running into its pool) the higher surface wins.
        /// </summary>
        public static bool WaterAt(ref CaveSetBlob set, float3 p, out float surface, out float bed, out float2 flow,
            out float rapids, out bool stream)
        {
            surface = -1e6f;
            bed = 0f;
            flow = float2.zero;
            rapids = 0f;
            stream = false;
            var range = CellRange(ref set, p.xz);
            var current = -1;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                var cave = set.Segments[set.Items[k]].Cave;
                // Items are grouped by cave: test each cave's water once.
                if (cave == current)
                    continue;
                current = cave;
                ref var c = ref set.Caves[cave];
                if (math.any(p.xz < c.Min.xz) || math.any(p.xz > c.Max.xz) ||
                    p.y < c.Min.y - LakeDepth - WaterReachBelow || p.y > c.Max.y)
                    continue;
                for (int i = c.FirstPool; i < c.FirstPool + c.PoolCount; i++)
                {
                    ref var pool = ref set.Pools[i];
                    if (pool.Lava > 0.5f || pool.Centre.y <= surface)
                        continue;
                    var d = math.distance(p.xz, pool.Centre.xz);
                    if (d >= pool.Radius)
                        continue;
                    // The floor at the middle is 0.12 m over the surface, sunk by FloorShape's basin.
                    var b = pool.Centre.y + 0.12f - pool.Depth * math.smoothstep(pool.Radius * 1.2f, pool.Radius * 0.7f, d);
                    if (p.y > pool.Centre.y + WaterReachAbove || p.y < b - WaterReachBelow)
                        continue;
                    surface = pool.Centre.y;
                    bed = b;
                    flow = float2.zero;
                    rapids = 0f;
                    stream = false;
                }
                for (int s = c.FirstStream; s < c.FirstStream + c.StreamCount; s++)
                {
                    ref var st = ref set.Streams[s];
                    if (math.any(p.xz < st.Min) || math.any(p.xz > st.Max))
                        continue;
                    for (int i = st.FirstPoint; i < st.FirstPoint + st.PointCount - 1; i++)
                    {
                        ref var a = ref set.StreamPoints[i];
                        ref var e = ref set.StreamPoints[i + 1];
                        var ab = e.Position.xz - a.Position.xz;
                        var length = math.length(ab);
                        var along = math.dot(p.xz - a.Position.xz, ab) / math.max(length * length, 1e-6f);
                        // W-134 row 4: a river chamber's points sit closer than its half width, so (as in StreamBed) its
                        // stretches count only abreast of themselves: an earlier stretch's end would read a step high.
                        // A quarter metre's grace either way closes the wedge outside each bend.
                        var grace = 0.25f / math.max(length, 0.25f);
                        if (st.Chamber > 0f && ((along < -grace && i > st.FirstPoint) || (along > 1f + grace && i < st.FirstPoint + st.PointCount - 2)))
                            continue;
                        var t = math.saturate(along);
                        var half = math.lerp(a.Width, e.Width, t) * 0.5f;
                        var across = math.distance(p.xz, a.Position.xz + ab * t);
                        if (across >= half)
                            continue;
                        var level = math.lerp(a.Position.y, e.Position.y, t);
                        if (level <= surface)
                            continue;
                        // The channel as StreamBed carves it (inside the water's width the bank hasn't started to rise far).
                        var depth = math.lerp(a.Depth, e.Depth, t);
                        var edge = half * 0.5f + math.max(half * 1.5f, (depth + 1.2f) * BankRun);
                        var b = level - (depth - (depth + 1.2f) * math.smoothstep(half * 0.5f, edge, across)) + 0.05f;
                        if (p.y > level + WaterReachAbove || p.y < b - WaterReachBelow)
                            continue;
                        surface = level;
                        bed = b;
                        flow = length > 1e-4f ? ab / length : float2.zero;
                        // gameplay: the same white-water slopes as the surface rivers' rapids read at a glance
                        rapids = math.smoothstep(0.015f, 0.08f, (a.Position.y - e.Position.y) / math.max(length, 0.1f));
                        stream = true;
                    }
                }
            }
            return surface > -1e5f;
        }

        /// <summary>Signed distance to a box (centre, half size).</summary>
        static float Box(float3 p, float3 centre, float3 half)
        {
            var q = math.abs(p - centre) - half;
            return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
        }

        /// <summary>
        /// Whether a point is over a cave's lava pool and low enough to step into it (up to a metre above the surface).
        /// Pools are few, so this just checks them all.
        /// </summary>
        public static bool OverLava(ref CaveSetBlob set, float3 p)
        {
            for (int i = 0; i < set.Pools.Length; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f && math.distancesq(p.xz, pool.Centre.xz) < pool.Radius * pool.Radius &&
                    p.y < pool.Centre.y + 1f && p.y > pool.Centre.y - pool.Depth - 1f)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a point's column passes through any cave segment's box (a cheap first test).</summary>
        public static bool NearColumn(ref CaveSetBlob set, float2 xz)
        {
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.all(xz >= seg.Min.xz) && math.all(xz <= seg.Max.xz))
                    return true;
            }
            return false;
        }

        internal static float SmoothMin(float a, float b, float k)
        {
            var h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
