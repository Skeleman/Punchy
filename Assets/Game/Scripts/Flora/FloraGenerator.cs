using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>One grown variant of a species, at three levels of detail.</summary>
    /// <summary>A model's walkable tops, built from its mesh (FloraMeshBuilder.SupportGrid; see PropSupport).</summary>
    public class SupportGridData
    {
        public Vector2 Min;
        public float Cell;
        public Vector2Int Res;
        /// <summary>Res.x × Res.y × SupportGrids.Layers heights, highest first per cell, NaN where none.</summary>
        public float[] Values;
        /// <summary>Res.x × Res.y water heights (NaN where none), or null.</summary>
        public float[] Water;
        public float Reach, Error;
    }

    public class FloraModel
    {
        /// <summary>Its walkable tops, for forms people can climb and stand on (rocks, buildings, rims), else null.</summary>
        public SupportGridData Support;
        public FloraSpecies Species;
        public readonly Mesh[] Lods = new Mesh[3];
        /// <summary>Bounding sphere centre height and radius (unscaled).</summary>
        public float CentreY, Radius;
        /// <summary>Radius of the trunk / stem at the ground, for collisions.</summary>
        public float TrunkRadius;
        public float Height;
        /// <summary>
        /// How far the trunk's centre has leaned from its foot (x, z, unscaled, before the plant's rotation) at half its
        /// height and at its height (or the trunk's top, if that's lower), so climbers keep to the trunk as drawn rather
        /// than a straight post at its foot. Zero for plants without a tree trunk.
        /// </summary>
        public Vector2 TrunkLeanMid, TrunkLeanTop;
        /// <summary>
        /// Collision shape for plants that aren't a single trunk (rocks): upright cylinders (x, z offset from the
        /// plant's base, radius, height of the top), before the plant's own rotation and scale. Empty = the trunk.
        /// </summary>
        public readonly List<Vector4> Colliders = new List<Vector4>();
        /// <summary>
        /// W-67 (#79): a kit building's or ruin's solid parts as boxes (model space): the player stands on, bumps into
        /// and jumps under these rather than its cylinders and support grid. Empty for everything else.
        /// </summary>
        public readonly List<KitBox> Boxes = new List<KitBox>();
        /// <summary>
        /// Where each collider starts above the base, by index (missing = 0, the ground). Lets a leaning shape (an
        /// ArcSpire's prongs) be a stack of short slices that follow its tilt instead of one fat upright cylinder.
        /// </summary>
        public readonly List<float> ColliderBase = new List<float>();
        /// <summary>
        /// Basins of open water it holds (a fountain's): centre (x, y, z) and radius (w), before the plant's own rotation
        /// and scale. FloraSystem draws them with the terrain's own water (Z-12).
        /// </summary>
        public readonly List<Vector4> Pools = new List<Vector4>();
        /// <summary>
        /// Places a bird can sit on its top (a roof's peak or ridge ends, a chimney, a post), before the plant's own
        /// rotation and scale. Empty for most plants (see FloraPlacement.NearPerches).
        /// </summary>
        public readonly List<Vector3> Perches = new List<Vector3>();
        /// <summary>Its chimneys' flue tops (W-43c, where smoke rises), before the plant's own rotation and scale.</summary>
        public readonly List<Vector3> Chimneys = new List<Vector3>();
        /// <summary>W-127: its lit window panes (xyz the pane's middle, w the yaw (radians) it faces out along), before
        /// the plant's own rotation and scale, for the light they spill outside at night (WindowSpill).</summary>
        public readonly List<Vector4> Windows = new List<Vector4>();
        /// <summary>Whether it casts shadows. Small props (fences, palisades, signposts, garden rows, woodpiles,
        /// stalls, flowers) don't: towns have hundreds of them, and every shadow-casting draw costs a shadow pass.</summary>
        public bool CastsShadows = true;
        /// <summary>Height of its door at scale 1 (0 if it has none): doors are sized for human folk, about 1.3 times a
        /// person's height, and each settlement scales its buildings by its folk's size (SettlementLayout.BuildingScale).</summary>
        public float DoorHeight;
        /// <summary>Refactor 3.5: nothing in it moves but door leaves (FloraMeshBuilder.Moves), so it draws with the static
        /// shader variant, which skips the wind and the other vertex motions.</summary>
        public bool Static;

        /// <summary>Forms that cast no shadow (see CastsShadows).</summary>
        public static bool Shadowless(FloraForm form) =>
            form is FloraForm.Pen or FloraForm.Palisade or FloraForm.Signpost or FloraForm.Garden or FloraForm.Woodpile
                 or FloraForm.Stall or FloraForm.Flowers or FloraForm.Bedroll or FloraForm.RailFence;

        /// <summary>
        /// Refactor 3.5c: its detail rule's class (DetailPolicy.FloraRule). By form, then for growing things by whether it
        /// moves (3.5a) and how big it is: 4 m and up counts as a tree whichever layer it's on (a giant mushroom, a big dead bush).
        /// </summary>
        public Punchy.Settings.DetailPolicy.FloraClass DetailClass()
        {
            switch (Species.Form)
            {
                case FloraForm.Hut: case FloraForm.StoneHouse: case FloraForm.Tent: case FloraForm.Tower: case FloraForm.Pen:
                case FloraForm.Stall: case FloraForm.Inn: case FloraForm.Smithy: case FloraForm.Forum: case FloraForm.Fountain:
                case FloraForm.Well: case FloraForm.Hall: case FloraForm.Shrine: case FloraForm.Garden: case FloraForm.Lodge:
                case FloraForm.Signpost: case FloraForm.Bridge: case FloraForm.Palisade: case FloraForm.RailFence:
                case FloraForm.MineFrame: case FloraForm.HazardShelter: case FloraForm.KitBuilding: case FloraForm.KitRuin:
                case FloraForm.Ruin:
                    return Punchy.Settings.DetailPolicy.FloraClass.Structure;
                case FloraForm.Boulder: case FloraForm.Spire: case FloraForm.Crystal: case FloraForm.Den: case FloraForm.Columns:
                case FloraForm.IceArch: case FloraForm.Fulgurite: case FloraForm.ArcSpire: case FloraForm.FloatingRock:
                case FloraForm.Bones:
                    return Punchy.Settings.DetailPolicy.FloraClass.Rock;
                case FloraForm.Woodpile: case FloraForm.Eggs: case FloraForm.Bench: case FloraForm.Log: case FloraForm.Stump:
                case FloraForm.FirePit: case FloraForm.Torch: case FloraForm.Debris: case FloraForm.Bedroll: case FloraForm.Driftwood:
                    return Punchy.Settings.DetailPolicy.FloraClass.Debris;
            }
            if (Radius * 2f >= 4f)
                return Punchy.Settings.DetailPolicy.FloraClass.Tree;
            return Static ? Punchy.Settings.DetailPolicy.FloraClass.Debris : Punchy.Settings.DetailPolicy.FloraClass.Plant;
        }

        /// <summary>W-79: each level's geometry until Finish makes its mesh (FloraGenerator grows it on a worker), and their name.</summary>
        internal FloraMeshBuilder[] Pending;
        internal string MeshName;

        /// <summary>Makes the meshes from the grown geometry (main thread).</summary>
        internal void Finish()
        {
            if (Pending == null)
                return;
            Static = true;
            foreach (var b in Pending)
                if (b != null && b.Moves())
                    Static = false;
            for (int lod = 0; lod < Pending.Length; lod++)
                Lods[lod] = Pending[lod].MakeMesh($"{MeshName} LOD{lod}");
            Pending = null;
        }

        public void Destroy()
        {
            foreach (var m in Lods)
                if (m != null)
                    Object.Destroy(m);
        }
    }

    /// <summary>
    /// Grows plants. Each variant is decided by a seeded random sequence (its skeleton: trunk, limbs, clumps, arms,
    /// pads, petals), then emitted at three levels of detail. Every level replays the same sequence, so they share
    /// one silhouette and only differ in how finely it is built.
    /// </summary>
    public static partial class FloraGenerator
    {
        /// <param name="shapeOnly">A-52: only what the animals' tables read (LOD 0's colliders, perches, chimneys, trunk
        /// and height), with no meshes and no support grid: the same sequence, so those match a full grow exactly.</param>
        public static FloraModel Grow(FloraSpecies species, int variant, bool shapeOnly = false)
        {
            // A shape-only grow (the fauna's obstacle pass) skips the pregrown parts: those stay for the flora build.
            var model = shapeOnly ? GrowParts(species, variant, species.name, true)
                                  : TakePregrown(species, variant) ?? GrowParts(species, variant, species.name);
            model.Finish();
            return model;
        }

        /// <summary>
        /// A-55 (#102): a shape-only grow's work, safe on a worker thread (<paramref name="name"/> is the species asset's
        /// name, read on the main thread). The same result as Grow(species, variant, shapeOnly: true).
        /// </summary>
        public static FloraModel GrowShape(FloraSpecies species, int variant, string name) => GrowParts(species, variant, name, true);

        // W-79 (#102): growing every variant took ~8 s of the main thread on the frame the island was ready. It depends
        // only on the palette, so the bake starts it on worker threads (Pregrow) and Grow just turns the result into meshes.
        static readonly Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>> pregrown =
            new Dictionary<(FloraSpecies, int), System.Threading.Tasks.Task<FloraModel>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPregrown()
        {
            lock (pregrown)
                pregrown.Clear();   // Play mode keeps statics: an earlier session's leftovers may be stale
        }

        /// <summary>
        /// Starts growing these species' variants (all but their meshes) on worker threads. A later Grow of the same
        /// species and variant takes the result, waiting for it if it isn't done; anything never asked for is dropped
        /// with the next Play session. Main thread only (it reads the assets' names). Returns how many were started.
        /// </summary>
        public static int Pregrow(IEnumerable<(FloraSpecies species, int variants)> list)
        {
            int started = 0;
            lock (pregrown)
                foreach (var (species, variants) in list)
                {
                    if (species == null)
                        continue;
                    var name = species.name;
                    for (int v = 0; v < variants; v++)
                    {
                        if (pregrown.ContainsKey((species, v)))
                            continue;
                        var variant = v;
                        pregrown[(species, v)] = System.Threading.Tasks.Task.Run(() => GrowParts(species, variant, name));
                        started++;
                    }
                }
            return started;
        }

        /// <summary>The pregrown parts of this variant (waiting for them if needed), or null if none were started or they failed.</summary>
        static FloraModel TakePregrown(FloraSpecies species, int variant)
        {
            System.Threading.Tasks.Task<FloraModel> task;
            lock (pregrown)
            {
                if (!pregrown.TryGetValue((species, variant), out task))
                    return null;
                pregrown.Remove((species, variant));
            }
            try { return task.Result; }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[flora] pregrowing {species.name} {variant} failed, growing it here: {e.GetBaseException().Message}");
                return null;
            }
        }

        /// <summary>
        /// Everything Grow does but making the meshes: pure code over the species' settings, safe on a worker thread
        /// (<paramref name="name"/> is the species asset's name, read on the main thread). FloraModel.Finish makes the meshes.
        /// </summary>
        static FloraModel GrowParts(FloraSpecies species, int variant, string name, bool shapeOnly = false)
        {
            var model = new FloraModel { Species = species, MeshName = $"{name} {variant}", Pending = shapeOnly ? null : new FloraMeshBuilder[3] };
            model.CastsShadows = !FloraModel.Shadowless(species.Form);
            var seed = (int)Hash((uint)species.Seed, (uint)variant, StableHash(name));
            for (int lod = 0; lod < (shapeOnly ? 1 : 3); lod++)
            {
                var builder = new FloraMeshBuilder();
                builder.FoliageAs = species.Finish == FoliageFinish.Metallic ? FloraPart.Metal
                                  : species.Finish == FoliageFinish.Glowing ? FloraPart.Glow
                                  : species.Finish == FoliageFinish.Glass ? FloraPart.Glass : FloraPart.Foliage;
                builder.PetalAs = species.GlowingAccents ? FloraPart.Glow : FloraPart.Petal;
                builder.GlowPulse = species.GlowPulse;
                builder.BarkSeams = species.GlowingSeams;
                builder.BarkVeins = species.GlowingVeins;
                builder.BarkStrata = species.RockStrata;
                var g = new Grower(species, variant, lod, seed, builder);
                switch (species.Form)
                {
                    case FloraForm.BroadleafTree: Broadleaf(g); break;
                    case FloraForm.Conifer: Conifer(g); break;
                    case FloraForm.Palm: Palm(g); break;
                    case FloraForm.Shrub: Shrub(g); break;
                    case FloraForm.DryBush: DryBush(g); break;
                    case FloraForm.Flowers: Flowers(g); break;
                    case FloraForm.Saguaro: Saguaro(g); break;
                    case FloraForm.BarrelCactus: BarrelCactus(g); break;
                    case FloraForm.PricklyPear: PricklyPear(g); break;
                    case FloraForm.Ocotillo: Ocotillo(g); break;
                    case FloraForm.Mushroom: Mushroom(g); break;
                    case FloraForm.Eggs: Eggs(g); break;
                    case FloraForm.MineFrame: MineFrame(g); break;
                    case FloraForm.Torch: Torch(g); break;
                    case FloraForm.Bench: Bench(g); break;
                    case FloraForm.Log: SeatLog(g); break;
                    case FloraForm.Driftwood: Driftwood(g); break;
                    case FloraForm.Stump: Stump(g); break;
                    case FloraForm.FirePit: FirePit(g); break;
                    case FloraForm.HiveTree: Broadleaf(g); Hive(g); break;
                    case FloraForm.IceArch: IceArch(g); break;
                    case FloraForm.Fulgurite: Fulgurite(g); break;
                    case FloraForm.ArcSpire: ArcSpire(g); break;
                    case FloraForm.Debris: Debris(g); break;
                    case FloraForm.Bones: Bones(g); break;
                    case FloraForm.Boulder: Boulder(g); break;
                    case FloraForm.Spire: Spire(g); break;
                    case FloraForm.Crystal: Crystal(g); break;
                    case FloraForm.Den: Den(g); break;
                    case FloraForm.Columns: Columns(g); break;
                    case FloraForm.Hut: Hut(g); break;
                    case FloraForm.StoneHouse: StoneHouse(g); break;
                    case FloraForm.Ruin: Ruin(g); break;
                    case FloraForm.Bedroll: Bedroll(g); break;
                    case FloraForm.RailFence: RailFence(g); break;
                    case FloraForm.FloatingRock: FloatingRock(g); break;
                    case FloraForm.HazardShelter: HazardShelter(g); break;
                    case FloraForm.KitBuilding: KitStructure(g, false); break;
                    case FloraForm.KitRuin: KitStructure(g, true); break;
                    case FloraForm.Tent: Tent(g); break;
                    case FloraForm.Tower: Tower(g); break;
                    case FloraForm.Pen: Pen(g); break;
                    case FloraForm.Stall: Stall(g); break;
                    case FloraForm.Inn: Inn(g); break;
                    case FloraForm.Smithy: Smithy(g); break;
                    case FloraForm.Forum: Forum(g); break;
                    case FloraForm.Fountain: Fountain(g); break;
                    case FloraForm.Woodpile: Woodpile(g); break;
                    case FloraForm.Well: Well(g); break;
                    case FloraForm.LilyPad: LilyPad(g); break;
                    case FloraForm.Reeds: Reeds(g); break;
                    case FloraForm.Roots: Roots(g); break;
                    case FloraForm.Hall: Hall(g); break;
                    case FloraForm.Shrine: Shrine(g); break;
                    case FloraForm.Garden: Garden(g); break;
                    case FloraForm.Lodge: Lodge(g); break;
                    case FloraForm.Signpost: Signpost(g); break;
                    case FloraForm.Bridge: Bridge(g); break;
                    case FloraForm.Palisade: Palisade(g); break;
                }
                if (species.Brackets > 0f && g.TrunkPath != null)
                    Brackets(g);
                if (g.Limbs != null && g.Lod < 2)
                {
                    if (species.Hanging > 0f) Hanging(g);
                    if (species.Cocoons > 0f) Cocoons(g);
                }
                if (species.BarkFace > 0f && g.TrunkPath != null)
                    BarkFace(g);
                if (Founded(species.Form))
                    builder.Skirt(FoundationDepth);
                if (lod == 0)
                {
                    if (Standable(species.Form) && !shapeOnly)
                    {
                        // Clear ice arches stand like rock; a fountain's water (drawn as metal) is for wading.
                        var fountain = species.Form == FloraForm.Fountain;
                        // (A tent has none: its steep hides gave the climb logic a ledge all round it, W-43.)
                        model.Support = species.Form == FloraForm.Tent ? null : builder.SupportGrid(
                            part => part == FloraPart.Bark || part == FloraPart.Cactus || part == FloraPart.Comb || part == FloraPart.Metal && !fountain ||
                                    part == FloraPart.Glass && species.Form == FloraForm.IceArch,
                            part => fountain && part == FloraPart.Metal);
                    }
                    builder.Bounds(out model.CentreY, out model.Radius);
                    model.TrunkRadius = g.TrunkRadius;
                    model.Colliders.AddRange(g.Colliders);
                    model.Boxes.AddRange(g.Boxes);
                    model.ColliderBase.AddRange(g.ColliderBase);
                    model.Perches.AddRange(g.Perches);
                    model.Chimneys.AddRange(g.Chimneys);
                    model.Windows.AddRange(g.Windows);
                    model.Pools.AddRange(g.Pools);
                    model.DoorHeight = g.DoorHeight;
                    model.Height = g.Height;
                    if (g.TrunkAxis != null)
                    {
                        model.TrunkLeanMid = AxisAt(g.TrunkAxis, g.Height * 0.5f);
                        model.TrunkLeanTop = AxisAt(g.TrunkAxis, g.Height);
                    }
                }
                // Built things stand rigid in the wind (benches, halls, palisades swayed like plants, Z-11); a garden's
                // plants still move, and cloth has its own motion (Flora.hlsl).
                if (shapeOnly)
                    break;
                var flexibility = Founded(species.Form) && species.Form != FloraForm.Garden ? 0f : species.Flexibility;
                builder.Sway(g.Height, flexibility);
                model.Pending[lod] = builder;
            }
            return model;
        }

        /// <summary>
        /// A variant's shape without growing it: trees draw their height first from the same seeded sequence Grow
        /// uses, so this matches the model exactly. Anything that isn't a tree is TreeKind.None.
        /// </summary>
        public static FloraShape Shape(FloraSpecies species, int variant)
        {
            var kind = species.Form == FloraForm.BroadleafTree ? TreeKind.Broadleaf
                     : species.Form == FloraForm.Conifer ? TreeKind.Conifer
                     : species.Form == FloraForm.Palm ? TreeKind.Palm : TreeKind.None;
            if (kind == TreeKind.None)
                return default;
            var seed = (int)Hash((uint)species.Seed, (uint)variant, StableHash(species.name));
            var height = Mathf.Lerp(species.Height.x, species.Height.y, (float)new System.Random(seed).NextDouble());
            return new FloraShape
            {
                Kind = kind,
                Height = height,
                TrunkRadius = height * species.Thickness,
                // Palms carry their fronds at the top; the others branch from TrunkFraction up.
                CrownBase = kind == TreeKind.Palm ? height * 0.88f : height * species.TrunkFraction,
                CrownRadius = height * species.Spread * 0.5f
            };
        }

        static uint StableHash(string text)
        {
            var h = 2166136261u;
            foreach (var ch in text)
                h = (h ^ ch) * 16777619u;
            return h;
        }

        static uint Hash(uint a, uint b, uint c)
        {
            var h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ c * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return h;
        }

        /// <summary>Random sequences and shared settings for growing one variant at one level of detail.</summary>
        class Grower
        {
            public readonly FloraSpecies S;
            public readonly int Variant, Lod;
            public readonly FloraMeshBuilder B;
            /// <summary>Shape decisions: consumed identically at every level of detail.</summary>
            readonly System.Random rng;
            /// <summary>Fine detail that only some levels draw (loose leaves, florets).</summary>
            public readonly System.Random Detail;
            public float Height, Width, TrunkRadius, DoorHeight;
            /// <summary>A tree's trunk centreline and radii, bottom to top (Broadleaf), for things that hang on it.</summary>
            public List<Vector3> TrunkPath;
            /// <summary>
            /// A tree's trunk centreline, bottom to top (Broadleaf, Conifer, Palm), for FloraModel's lean only. (Separate from
            /// TrunkPath, which also turns on brackets and bark faces: setting that for more forms would change them.)
            /// </summary>
            public List<Vector3> TrunkAxis;
            public List<float> TrunkRadii;
            /// <summary>A tree's limbs (Broadleaf), each a path from the trunk outwards, for things that hang from them.</summary>
            public List<List<Vector3>> Limbs;
            public readonly List<Vector4> Colliders = new List<Vector4>();
            public readonly List<KitBox> Boxes = new List<KitBox>();
            public readonly List<float> ColliderBase = new List<float>();
            public readonly List<Vector3> Perches = new List<Vector3>();
            public readonly List<Vector3> Chimneys = new List<Vector3>();
            public readonly List<Vector4> Windows = new List<Vector4>();
            public readonly List<Vector4> Pools = new List<Vector4>();

            /// <summary>
            /// Adds upright cylinders covering a rotated ellipsoid's footprint: a row of circles along its longer
            /// horizontal axis, each as wide as the shorter one, as tall as its top.
            /// </summary>
            /// <summary>
            /// AddCollider for a low rounded lump the player meets edge-on: the footprint shrinks to where its surface
            /// rises past the player's step (0.4 m), so they walk over the thin rim and stop at the real face, not at
            /// an invisible wall round it (#70).
            /// </summary>
            public void AddSteppedCollider(Vector3 centre, Vector3 radii, Quaternion rotation)
            {
                const float step = 0.4f;
                if (centre.y + radii.y < step + 0.05f)
                    return;
                var below = Mathf.Clamp01((step - centre.y) / Mathf.Max(radii.y, 0.01f));
                var shrink = centre.y >= step ? 1f : Mathf.Sqrt(1f - below * below);
                AddCollider(centre, new Vector3(radii.x * shrink, radii.y, radii.z * shrink), rotation);
            }

            /// <summary>
            /// W-93 (#70): a low rounded chunk as upright kit boxes (KitCollision), terraced like contour lines: one box per
            /// tier, each as wide as the chunk at half a tier under its top, tiers under a step (0.4 m) apart, so the player
            /// steps up a low chunk and stops at a tall one's face. Chunks too thin to stand on get none (walked over).
            /// Only the turn about y is kept (tops stay flat). Model space, bottoms sunk 0.5 m under the ground.
            /// W-94 (#70): the lowest tier is sunk to a 0.15 m lip (the rest spaced evenly above it), because on a slope the
            /// widest tier's downhill edge stands its own height plus the ground's fall across it: 0.35 m read as a wall.
            /// </summary>
            public void AddTierBoxes(Vector3 centre, Vector3 radii, Quaternion rotation)
            {
                const float tier = 0.35f, bottom = -0.5f, lip = 0.15f;
                var top = centre.y + radii.y;
                if (top < ChunkStep)
                    return;
                var n = Mathf.Clamp(Mathf.CeilToInt((top - lip) / tier) + 1, 2, 7);
                var yaw = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                for (int k = 1; k <= n; k++)
                {
                    var t = lip + (top - lip) * (k - 1) / (n - 1);
                    var u = Mathf.Clamp((t - (k == 1 ? lip : (top - lip) / (n - 1)) * 0.5f - centre.y) / Mathf.Max(radii.y, 0.01f), -1f, 1f);
                    var s = Mathf.Sqrt(1f - u * u) * 0.8f;
                    var half = new Vector3(radii.x * s, (t - bottom) * 0.5f, radii.z * s);
                    if (half.x < 0.15f || half.z < 0.15f)
                        continue;
                    Boxes.Add(new KitBox { Centre = new Vector3(centre.x, (t + bottom) * 0.5f, centre.z), Half = half, Rotation = yaw, Reach = half.magnitude });
                }
            }

            public void AddCollider(Vector3 centre, Vector3 radii, Quaternion rotation)
            {
                var top = centre.y + radii.y;
                if (top < 0.35f)
                    return;   // low enough to step over
                var ax = rotation * new Vector3(radii.x, 0f, 0f);
                var az = rotation * new Vector3(0f, 0f, radii.z);
                var a = new Vector2(ax.x, ax.z);
                var c = new Vector2(az.x, az.z);
                var major = a.magnitude >= c.magnitude ? a : c;
                var minor = Mathf.Max(Mathf.Min(a.magnitude, c.magnitude), 0.1f);
                var count = Mathf.Clamp(Mathf.CeilToInt(major.magnitude / minor), 1, 4);
                for (int k = 0; k < count; k++)
                {
                    var t = count == 1 ? 0f : Mathf.Lerp(-1f, 1f, k / (count - 1f)) * (1f - minor / Mathf.Max(major.magnitude, minor));
                    var at = new Vector2(centre.x, centre.z) + major * t;
                    Colliders.Add(new Vector4(at.x, at.y, minor * 0.95f, top));
                }
            }

            public Grower(FloraSpecies s, int variant, int lod, int seed, FloraMeshBuilder b)
            {
                S = s;
                Variant = variant;
                Lod = lod;
                B = b;
                rng = new System.Random(seed);
                Detail = new System.Random(seed ^ 0x5BD1E995);
            }

            public float R() => (float)rng.NextDouble();
            public float Range(float a, float b) => a + (b - a) * R();
            public float Range(Vector2 v) => Range(v.x, v.y);
            public int Count(Vector2Int v) => rng.Next(Mathf.Min(v.x, v.y), Mathf.Max(v.x, v.y) + 1);
            public Vector3 Direction() => FloraMeshBuilder.RandomDirection(rng);
            public Vector3 Flat(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            public Color Bark => S.Bark.linear;
            public Color FoliageColor() => Color.Lerp(S.Foliage, S.FoliageVariation, R()).linear;
            public bool HasAccent => S.Accents.Count > 0 && S.AccentAmount > 0f;
            public Color Accent => S.Accents.Count > 0 ? S.Accents[Variant % S.Accents.Count].linear : Color.white;
            public int Sides(int lod0, int lod1, int lod2) => Lod == 0 ? lod0 : Lod == 1 ? lod1 : lod2;
        }

        // ------------------------------------------------------------------------------------------- helpers

        /// <summary>Darker deep inside and underneath a crown, lit on top and at the edges.</summary>
        static float CrownOcclusion(Vector3 p, Vector3 centre, Vector3 radii)
        {
            var rel = new Vector3((p.x - centre.x) / radii.x, (p.y - centre.y) / radii.y, (p.z - centre.z) / radii.z);
            return Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(rel.magnitude * 0.75f + rel.y * 0.3f + 0.15f));
        }

        /// <summary>Darker where the plant meets the ground.</summary>
        static float GroundOcclusion(Vector3 p, float height) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p.y / Mathf.Max(height, 0.05f)));

        /// <summary>Distance from a point inside an ellipsoid to its surface along a direction.</summary>
        static float ExitDistance(Vector3 centre, Vector3 radii, Vector3 from, Vector3 dir)
        {
            for (float d = 0.1f; d < 200f; d += 0.1f)
            {
                var p = from + dir * d - centre;
                if (p.x * p.x / (radii.x * radii.x) + p.y * p.y / (radii.y * radii.y) + p.z * p.z / (radii.z * radii.z) > 1f)
                    return d;
            }
            return 200f;
        }

        static Vector3 PointOnPath(List<Vector3> path, float y)
        {
            for (int i = 0; i < path.Count - 1; i++)
                if (y <= path[i + 1].y)
                    return Vector3.Lerp(path[i], path[i + 1], Mathf.InverseLerp(path[i].y, path[i + 1].y, y));
            return path[path.Count - 1];
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        // ------------------------------------------------------------------------------------------- forms

    }
}
