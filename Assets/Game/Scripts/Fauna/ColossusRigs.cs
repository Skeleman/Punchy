using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-72 rows 3-5 (#40): what the walking colossi do with their bodies while <see cref="ColossusWalker"/> moves them.
    /// Only the awake walker (the one nearest the camera) is animated; the others keep the pose StoneColossi built them in.
    /// <para>
    /// StoneColossi builds each one as a single baked mesh (or, for the golem, loose rocks), and nothing here changes how
    /// it is built. The saguaro and the two tortoises get a skinned copy of their body mesh with a few bones, weighted from
    /// where each vertex sits; the copy is shown instead of the baked mesh only while the walker is awake. The golem's
    /// rocks are already separate, so its rig just nudges them on top of <see cref="GolemArcs"/>' bob each frame.
    /// </para>
    /// Cosmetic and local: driven by the walker's pace and stride, which come from the shared clock.
    /// </summary>
    public abstract class ColossusRig
    {
        protected readonly Transform root;
        bool awake;
        /// <summary>A-74: how far (degrees, + to its right) the head turns toward the player, set by ColossusWalker each frame.</summary>
        public float Look;

        protected ColossusRig(Transform root) => this.root = root;

        /// <summary>The rig for this kind, or null if it has none (or its parts aren't where StoneColossi puts them).</summary>
        public static ColossusRig For(StoneColossi.Kind kind, Transform root)
        {
            ColossusRig rig = kind switch
            {
                StoneColossi.Kind.WalkingSaguaro => new SaguaroRig(root),
                StoneColossi.Kind.RockGolem => new GolemRig(root),
                StoneColossi.Kind.GiantTortoise => new TortoiseRig(root),
                StoneColossi.Kind.MesaTortoise => new MesaTortoiseRig(root),
                StoneColossi.Kind.GlassHeron => new HeronRig(root),
                StoneColossi.Kind.LanternStag => new StagRig(root),
                _ => null,
            };
            return rig != null && rig.Ready ? rig : null;
        }

        protected abstract bool Ready { get; }

        /// <summary>
        /// One frame of the awake walker: <paramref name="pace"/> 0 resting to 1 walking, <paramref name="stride"/> footfalls
        /// walked so far, <paramref name="settle"/> 0 to 1 how far it has sunk into its rest, <paramref name="t"/> clock seconds.
        /// </summary>
        public void Animate(float pace, float stride, float settle, float t)
        {
            if (root == null)
                return;
            if (!awake)
            {
                awake = true;
                Show(true);
            }
            Pose(pace, stride, settle, t);
        }

        /// <summary>It is no longer the awake one: back to the baked pose.</summary>
        public void Sleep()
        {
            if (!awake || root == null)
                return;
            awake = false;
            Show(false);
        }

        protected abstract void Show(bool animated);
        protected abstract void Pose(float pace, float stride, float settle, float t);

        protected static Transform Bone(string name, Transform parent, Vector3 local)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = local;
            return bone;
        }

        /// <summary>
        /// A skinned copy of <paramref name="part"/>'s mesh (same material and shadows) on <paramref name="bones"/>, which
        /// must be at rest. Hidden until <see cref="Swap"/> shows it.
        /// </summary>
        protected static SkinnedMeshRenderer Skin(Transform part, Mesh mesh, Transform[] bones, BoneWeight[] weights)
        {
            var original = part.GetComponent<MeshRenderer>();
            var copy = Object.Instantiate(mesh);
            copy.name = mesh.name + "Skinned";
            copy.boneWeights = weights;
            var binds = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                binds[i] = bones[i].worldToLocalMatrix * part.localToWorldMatrix;
            copy.bindposes = binds;
            var go = new GameObject(part.name + "Skinned");
            go.transform.SetParent(part.parent, false);
            go.transform.localPosition = part.localPosition;
            go.transform.localRotation = part.localRotation;
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = copy;
            skin.bones = bones;
            skin.rootBone = bones[0];
            skin.sharedMaterial = original.sharedMaterial;
            skin.shadowCastingMode = original.shadowCastingMode;
            skin.receiveShadows = original.receiveShadows;
            var bounds = mesh.bounds;
            bounds.Expand(12f);
            skin.localBounds = bounds;
            skin.enabled = false;
            return skin;
        }

        protected static void Swap(Renderer baked, Renderer skinned, bool animated)
        {
            skinned.enabled = animated;
            baked.enabled = !animated;
        }

        /// <summary>Two influences, the larger first, summing to one.</summary>
        protected static BoneWeight Weigh(int a, int b, float wb)
        {
            wb = math.saturate(wb);
            return wb > 0.5f ? new BoneWeight { boneIndex0 = b, weight0 = wb, boneIndex1 = a, weight1 = 1f - wb }
                             : new BoneWeight { boneIndex0 = a, weight0 = 1f - wb, boneIndex1 = b, weight1 = wb };
        }

        /// <summary>The horizontal axis that turns a limb hanging from its pivot along <paramref name="reach"/> so its end rises.</summary>
        protected static Vector3 LiftAxis(Vector3 reach)
        {
            var axis = Vector3.Cross(Vector3.up, new Vector3(reach.x, 0f, reach.z)).normalized;
            if (axis == Vector3.zero)
                axis = Vector3.right;
            return (Quaternion.AngleAxis(10f, axis) * reach).y >= reach.y ? axis : -axis;
        }
    }

    /// <summary>
    /// Row 3: the walking saguaro. The trunk sways and leans into the walk, each arm swings on its own like a pendulum, the
    /// three root legs lift one after another (one per footfall), the old footprints baked behind it pull under the sand
    /// while it walks (they would only be dragged along), and its dust rises thicker the faster it goes.
    /// </summary>
    sealed class SaguaroRig : ColossusRig
    {
        const float TrunkBase = 9f;
        static readonly float[] LegAngles = { 180f, -60f, 60f };   // StoneColossi.BuildSaguaro: lifted one trailing behind (A-77), two planted

        readonly MeshRenderer baked;
        readonly SkinnedMeshRenderer skinned;
        readonly Transform trunk, prints;
        readonly Transform[] legs = new Transform[3];
        readonly Vector3[] legAxis = new Vector3[3];
        readonly List<Transform> arms = new List<Transform>();
        readonly List<Vector3> armAxis = new List<Vector3>();
        readonly Vector3 printsAt;
        readonly ParticleSystem dust;
        readonly float dustRate;

        protected override bool Ready => skinned != null;

        public SaguaroRig(Transform root) : base(root)
        {
            var body = root.Find("Body");
            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return;
            baked = body.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;

            // The trunk's bend isn't kept, so fit it: the height and lean that put the most vertices on the trunk's skin.
            float height = 50f, lean = 6f, best = -1f;
            for (var h = 44f; h <= 54.01f; h += 1f)
                for (var l = 4f; l <= 9.01f; l += 0.5f)
                {
                    var score = 0;
                    for (int i = 0; i < v.Length; i += 5)
                    {
                        if (v[i].y < 12f || v[i].y > 40f)
                            continue;
                        var d = math.length(new float2(v[i].x, v[i].z - Axis(v[i].y, h, l)));
                        if (d > 3.4f && d < 5.2f)
                            score++;
                    }
                    if (score > best)
                    {
                        best = score;
                        height = h;
                        lean = l;
                    }
                }

            // Arms: vertices well off the trunk, gathered by their bearing from it (each arm keeps one bearing).
            const int Bins = 72;
            var count = new int[Bins];
            for (int i = 0; i < v.Length; i++)
                if (v[i].y > 14f && Off(v[i], height, lean, out var bearing) > 7f)
                    count[Bin(bearing, Bins)]++;
            var cluster = new int[Bins];
            for (int b = 0; b < Bins; b++)
                cluster[b] = -1;
            var clusters = 0;
            var start = System.Array.IndexOf(count, 0);
            if (start >= 0)
                for (int s = 1; s <= Bins; s++)
                {
                    int b = (start + s) % Bins, prev = (start + s - 1) % Bins;
                    if (count[b] < 20)
                        continue;
                    cluster[b] = count[prev] >= 20 && cluster[prev] >= 0 ? cluster[prev] : clusters++;
                }
            var dir = new Vector3[clusters];
            var shoulderY = new float[clusters];
            var shoulderN = new int[clusters];
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y <= 14f)
                    continue;
                var off = Off(v[i], height, lean, out var bearing);
                var c = off > 6f ? cluster[Bin(bearing, Bins)] : -1;
                if (c < 0)
                    continue;
                dir[c] += new Vector3(math.sin(bearing), 0f, math.cos(bearing));
                if (off < 8f)
                {
                    shoulderY[c] += v[i].y;
                    shoulderN[c]++;
                }
            }

            // Bones: the base (the root itself), the trunk, the footprints and three legs on the base, the arms on the trunk.
            var bones = new List<Transform>();
            var rig = Bone("Rig", root, Vector3.zero);
            bones.Add(rig);
            trunk = Bone("Trunk", rig, new Vector3(0f, TrunkBase, 0f));
            bones.Add(trunk);
            printsAt = new Vector3(0f, -3f, -40f);
            prints = Bone("Footprints", rig, printsAt);
            bones.Add(prints);
            for (int k = 0; k < 3; k++)
            {
                var a = math.radians(LegAngles[k]);
                var d = new Vector3(math.sin(a), 0f, math.cos(a));
                legs[k] = Bone("Leg" + k, rig, new Vector3(0f, TrunkBase, 0f) + d * 2f);
                legAxis[k] = LiftAxis(d * 10f + Vector3.down * 8.5f);
                bones.Add(legs[k]);
            }
            const int FirstArm = 6;
            for (int c = 0; c < clusters; c++)
            {
                var d = dir[c].normalized;
                var y = shoulderN[c] > 0 ? shoulderY[c] / shoulderN[c] : 30f;
                var pivot = new Vector3(0f, y, Axis(y, height, lean)) + d * 4.6f;
                var arm = Bone("Arm" + c, trunk, pivot - trunk.localPosition);
                arms.Add(arm);
                armAxis.Add(Vector3.Cross(Vector3.up, d).normalized);
                bones.Add(arm);
            }

            // Weights: arms by how far off the trunk, legs by height under the trunk, the old footprints whole.
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                if (p.z < -15f && p.y < 1.5f)
                {
                    weights[i] = Weigh(2, 2, 0f);
                    continue;
                }
                if (p.y < 10.5f)
                {
                    var bearing = math.degrees(math.atan2(p.x, p.z));
                    int leg = 0;
                    for (int k = 1; k < 3; k++)
                        if (math.abs(Mathf.DeltaAngle(bearing, LegAngles[k])) < math.abs(Mathf.DeltaAngle(bearing, LegAngles[leg])))
                            leg = k;
                    weights[i] = Weigh(1, 3 + leg, 1f - math.smoothstep(7f, 10.5f, p.y));
                    continue;
                }
                var off = Off(p, height, lean, out var b2);
                var c = p.y > 14f && off > 6f ? cluster[Bin(b2, Bins)] : -1;
                weights[i] = c >= 0 ? Weigh(1, FirstArm + c, math.smoothstep(6f, 8.5f, off)) : Weigh(1, 1, 0f);
            }
            skinned = Skin(body, mesh, bones.ToArray(), weights);

            var puffs = root.Find("Dust");
            dust = puffs != null ? puffs.GetComponent<ParticleSystem>() : null;
            if (dust != null)
                dustRate = dust.emission.rateOverTimeMultiplier;
            Debug.Log($"ColossusRig: saguaro skinned ({v.Length} verts, trunk fit {height:F0} m tall leaning {lean:F1} m, {clusters} arms)");
        }

        /// <summary>BuildSaguaro's trunk: how far forward its centre is at this height.</summary>
        static float Axis(float y, float height, float lean)
        {
            var t = math.saturate((y - TrunkBase) / (height - TrunkBase));
            return lean * t * t;
        }

        static float Off(Vector3 p, float height, float lean, out float bearing)
        {
            var dz = p.z - Axis(p.y, height, lean);
            bearing = math.atan2(p.x, dz);
            return math.length(new float2(p.x, dz));
        }

        static int Bin(float bearing, int bins) => ((int)math.floor((bearing + math.PI) / (2f * math.PI) * bins) % bins + bins) % bins;

        protected override void Show(bool animated)
        {
            Swap(baked, skinned, animated);
            if (dust != null && !animated)
            {
                var e = dust.emission;
                e.rateOverTimeMultiplier = dustRate;
            }
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            // The trunk leans into the walk and rolls with the steps; a slow breeze on top.
            trunk.localRotation = Quaternion.Euler(1.5f * pace + 0.4f * math.sin(t * 0.37f), Look,
                                                   1.5f * pace * math.sin(math.PI * stride * 2f / 3f) + 0.4f * math.sin(t * 0.23f + 1f));
            // Each arm a pendulum of its own, wider while walking.
            for (int k = 0; k < arms.Count; k++)
                arms[k].localRotation = Quaternion.AngleAxis((1.2f + 4f * pace) * math.sin(t * (0.9f + 0.15f * k) + k * 1.7f), armAxis[k]);
            // One leg lifts per footfall, in turn.
            var step = (int)math.floor(stride);
            var u = stride - step;
            for (int k = 0; k < 3; k++)
                legs[k].localRotation = Quaternion.AngleAxis(((step % 3 + 3) % 3 == k ? 16f * pace * math.sin(math.PI * u) : 0f), legAxis[k]);
            // The baked footprints sink away while it walks and come back once it stands.
            prints.localScale = Vector3.one * math.saturate(1f - 2f * pace);
            if (dust != null)
            {
                var e = dust.emission;
                e.rateOverTimeMultiplier = dustRate * (1f + 3f * pace);
            }
        }
    }

    /// <summary>
    /// Row 4: the rock golem. While it walks, every rock drifts further from the body (the arcs stretch to hold it), the
    /// legs swing in step with the footfalls, the feet lifting as they come through, the arms swing against the legs and
    /// the chest rocks side to side. On top of <see cref="GolemArcs"/>' bob, which runs first each frame and puts each
    /// rock back to its own place, so nothing here builds up.
    /// </summary>
    sealed class GolemRig : ColossusRig
    {
        struct Part
        {
            public Transform T;
            /// <summary>-1 left, 1 right, 0 the middle; how far (m) it swings forward and back, and how far it lifts.</summary>
            public float Side, Swing, Lift, Drift, Phase;
            public bool Arm;
        }

        static readonly (string name, float swing, float lift, bool arm)[] Limbs =
        {
            ("Foot", 3.5f, 2f, false), ("Shin", 2.4f, 1.2f, false), ("Thigh", 1.2f, 0.4f, false),
            ("Fist", 2.6f, 0f, true), ("Forearm", 1.8f, 0f, true), ("UpperArm", 1f, 0f, true), ("Shoulder", 0.3f, 0f, true),
        };

        readonly List<Part> parts = new List<Part>();
        readonly Transform head;
        readonly Quaternion headRest;
        static readonly Vector3 Middle = new Vector3(0f, 30f, 0f);

        protected override bool Ready => parts.Count > 0;

        public GolemRig(Transform root) : base(root)
        {
            if (root.GetComponent<GolemArcs>() == null)
                return;
            for (int side = -1; side <= 1; side += 2)
                foreach (var (name, swing, lift, arm) in Limbs)
                    Add((side < 0 ? "Left" : "Right") + name, side, swing, lift, arm, 1.2f);
            Add("Pelvis", 0, 0f, 0f, false, 0.6f);
            Add("Torso", 0, 0f, 0f, false, 0.8f);
            Add("Head", 0, 0f, 0f, false, 1.6f);
            for (int k = 0; k < 7; k++)
                Add("Shard" + k, 0, 0f, 0f, false, 3f);
            head = root.Find("Head");
            if (head != null)
                headRest = head.localRotation;
        }

        void Add(string name, int side, float swing, float lift, bool arm, float drift)
        {
            var t = root.Find(name);
            if (t != null)
                parts.Add(new Part { T = t, Side = side, Swing = swing, Lift = lift, Arm = arm, Drift = drift, Phase = parts.Count * 2.1f });
        }

        protected override void Show(bool animated)
        {
            if (!animated && head != null)
                head.localRotation = headRest;
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            if (head != null)
                head.localRotation = Quaternion.Euler(0f, Look, 0f) * headRest;   // A-74: turned toward the player
            if (pace <= 0f)
                return;
            var phase = math.PI * stride;   // two footfalls a cycle, the left foot first
            foreach (var p in parts)
            {
                var at = p.T.localPosition;
                // Drifting apart: out from the middle of the body, breathing in and out on each rock's own phase.
                var away = (at - Middle).normalized * (pace * p.Drift * (1f + 0.6f * math.sin(t * 0.6f + p.Phase)));
                var gait = Vector3.zero;
                if (p.Side != 0f)
                {
                    var leg = phase + (p.Side > 0f ? math.PI : 0f);
                    var swing = math.sin(leg) * (p.Arm ? -1f : 1f);
                    gait = new Vector3(0f, p.Lift * math.max(0f, math.cos(leg)), p.Swing * swing) * pace;
                }
                else if (p.Drift < 2f)
                    gait = new Vector3(0.8f * math.sin(phase) * pace, 0f, 0f);   // the chest and hips rocking over the planted foot
                p.T.localPosition = at + away + gait;
            }
        }
    }

    /// <summary>
    /// Row 5: the FungalForest tortoise. While it walks, its legs swing in diagonal pairs, lifting as they come forward,
    /// the shell rolls a little and the head nods with each step. When it stops it settles: the shell sinks down over its
    /// legs until the rim nearly rests on the ground, the head lowers, and it breathes slowly until it gets up again.
    /// </summary>
    sealed class TortoiseRig : ColossusRig
    {
        const float Sink = 2.4f;
        static readonly Vector3 ShellAt = new Vector3(0f, 7f, 0f), NeckAt = new Vector3(0f, 8f, 22f);

        readonly MeshRenderer baked;
        readonly SkinnedMeshRenderer skinned;
        readonly Transform shell, head, mushrooms;
        readonly Transform[] legs = new Transform[4];
        readonly Vector3 mushroomsAt;

        protected override bool Ready => skinned != null;

        public TortoiseRig(Transform root) : base(root)
        {
            var body = root.Find("Body");
            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return;
            baked = body.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            var rig = Bone("Rig", root, Vector3.zero);
            shell = Bone("Shell", rig, ShellAt);
            head = Bone("Neck", shell, NeckAt - ShellAt);
            var bones = new List<Transform> { rig, shell, head };
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;   // as BuildTortoise: 0 front left .. 3 back right
                legs[k] = Bone("Leg" + k, rig, new Vector3(sx * 16f, 5f, sz * 16f));
                bones.Add(legs[k]);
            }
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var outside = (p.x / 21f) * (p.x / 21f) + (p.z / 25f) * (p.z / 25f) > 1.1f;
                if (math.abs(p.x) > 24f || (outside && p.y < 3.5f && (math.abs(p.x) < 8f || math.abs(p.z) < 8f)))
                    weights[i] = Weigh(0, 0, 0f);                                                       // the eggs and the plate stay on the ground
                else if (math.abs(p.x) > 8f && math.abs(p.z) > 8f && p.y < 5.5f)
                    weights[i] = Weigh(1, 3 + (p.x < 0f ? 0 : 1) + (p.z > 0f ? 0 : 2), 1f - math.smoothstep(3.5f, 5.5f, p.y));
                else
                    weights[i] = Weigh(1, 2, p.y > 2f ? math.smoothstep(22f, 26f, p.z) : 0f);
            }
            skinned = Skin(body, mesh, bones.ToArray(), weights);
            mushrooms = root.Find("Mushrooms");
            if (mushrooms != null)
                mushroomsAt = mushrooms.localPosition;
            Debug.Log($"ColossusRig: tortoise skinned ({v.Length} verts)");
        }

        protected override void Show(bool animated)
        {
            Swap(baked, skinned, animated);
            if (mushrooms != null && !animated)
            {
                mushrooms.localPosition = mushroomsAt;
                mushrooms.localRotation = Quaternion.identity;
            }
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            var phase = math.PI * stride;
            var breath = 0.12f * settle * math.sin(t * 0.6f);
            shell.localPosition = ShellAt + Vector3.up * (breath - Sink * settle);
            shell.localRotation = Quaternion.Euler(0f, 0f, 1.2f * pace * math.sin(phase));
            // The head nods with each step, sinks to the ground at rest, and looks slowly about either way.
            head.localRotation = Quaternion.Euler(3f * pace * math.sin(2f * phase) + 4f * settle, 6f * math.sin(t * 0.13f) * (1f - math.saturate(math.abs(Look) / 10f)) + Look, 0f);
            // Diagonal pairs: front left with back right, then front right with back left.
            for (int k = 0; k < 4; k++)
            {
                var leg = phase + (k == 0 || k == 3 ? 0f : math.PI);
                legs[k].localRotation = Quaternion.Euler(12f * pace * math.sin(leg), 0f, 0f);
                legs[k].localPosition = new Vector3(k % 2 == 0 ? -16f : 16f, 5f + 0.8f * pace * math.max(0f, math.cos(leg)), k < 2 ? 16f : -16f);
            }
            // The mushrooms ride the shell.
            if (mushrooms != null)
            {
                mushrooms.localPosition = mushroomsAt + Vector3.up * (breath - Sink * settle);
                mushrooms.localRotation = shell.localRotation;
            }
        }
    }

    /// <summary>
    /// A-75 row 2 (#40): the mesa tortoise. Its strata shell rocks slowly side to side, the four stubby legs step in
    /// diagonal pairs, and the head, which rests on the ground under the overhang, lifts a little while it walks and
    /// sinks back down at rest. The talus skirt round its feet would only be dragged along, so it slides under the ground
    /// while the tortoise walks and pushes back up once it has settled (a fresh scree where it stopped). The scrub, cacti
    /// and cairn on top ride the shell.
    /// </summary>
    sealed class MesaTortoiseRig : ColossusRig
    {
        const float Sink = 1.6f, TalusDown = 3f;
        static readonly Vector3 ShellAt = new Vector3(0f, 6.5f, 0f), NeckAt = new Vector3(0f, 6f, 17f);

        readonly MeshRenderer baked;
        readonly SkinnedMeshRenderer skinned;
        readonly Transform shell, head, talus, scrub;
        readonly Transform[] legs = new Transform[4];
        readonly Vector3 scrubAt;

        protected override bool Ready => skinned != null;

        public MesaTortoiseRig(Transform root) : base(root)
        {
            var body = root.Find("Body");
            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return;
            baked = body.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            var rig = Bone("Rig", root, Vector3.zero);
            shell = Bone("Shell", rig, ShellAt);
            head = Bone("Neck", shell, NeckAt - ShellAt);
            talus = Bone("Talus", rig, Vector3.zero);
            var bones = new List<Transform> { rig, shell, head, talus };
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;   // as BuildMesaTortoise: 0 front left .. 3 back right
                legs[k] = Bone("Leg" + k, rig, new Vector3(sx * 11f, 6.5f, sz * 11f));
                bones.Add(legs[k]);
            }
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var ring = (p.x / 18f) * (p.x / 18f) + (p.z / 21f) * (p.z / 21f);
                // The nearest foot (as built: front right a step forward) and how far from it.
                var k = (p.x < 0f ? 0 : 1) + (p.z > 0f ? 0 : 2);
                var footAt = new Vector2(k % 2 == 0 ? -14.5f : 14.5f, k < 2 ? 14f + (k == 1 ? 2.5f : 0f) : -14f);
                var nearFoot = Vector2.Distance(new Vector2(p.x, p.z), footAt) < 7.5f;
                var neck = p.z > 23f || (p.z > 19.5f && p.y < 6.5f && math.abs(p.x) < 7f);
                if (p.x < -17f && p.z < -8f && p.z > -19f && p.y < 7.5f && !nearFoot)
                    weights[i] = Weigh(6, 6, 0f);                                                       // the fallen slab leans on the back left foot
                else if (neck)
                    weights[i] = Weigh(1, 2, p.z > 23f ? 1f : math.smoothstep(19.5f, 23f, p.z));
                else if (nearFoot && p.y < 5.2f)
                    weights[i] = Weigh(1, 4 + k, 1f - math.smoothstep(4f, 5.2f, p.y));
                else if (ring > 1.08f && p.y < 3f)
                    weights[i] = Weigh(3, 3, 0f);                                                       // talus
                else
                    weights[i] = Weigh(1, 1, 0f);
            }
            skinned = Skin(body, mesh, bones.ToArray(), weights);
            scrub = root.Find("Scrub");
            if (scrub != null)
                scrubAt = scrub.localPosition;
            Debug.Log($"ColossusRig: mesa tortoise skinned ({v.Length} verts)");
        }

        protected override void Show(bool animated)
        {
            Swap(baked, skinned, animated);
            if (scrub != null && !animated)
            {
                scrub.localPosition = scrubAt;
                scrub.localRotation = Quaternion.identity;
            }
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            var phase = math.PI * stride;
            var breath = 0.1f * settle * math.sin(t * 0.5f);
            var rise = Vector3.up * (breath - Sink * settle);
            shell.localPosition = ShellAt + rise;
            shell.localRotation = Quaternion.Euler(0.6f * pace * math.sin(2f * phase), 0f, 1.5f * pace * math.sin(phase));
            // The head lifts off the ground to walk, nods with each step, rests at the halt and looks slowly about.
            head.localRotation = Quaternion.Euler(-7f * pace + 2.5f * pace * math.sin(2f * phase) + 2f * settle,
                                                  5f * math.sin(t * 0.11f) * (1f - math.saturate(math.abs(Look) / 10f)) + Look, 0f);
            // The scree slides under while it walks and is back, fresh, once it has settled.
            talus.localPosition = Vector3.down * (TalusDown * (1f - settle));
            for (int k = 0; k < 4; k++)
            {
                var leg = phase + (k == 0 || k == 3 ? 0f : math.PI);
                legs[k].localRotation = Quaternion.Euler(10f * pace * math.sin(leg), 0f, 0f);
                legs[k].localPosition = new Vector3(k % 2 == 0 ? -11f : 11f, 6.5f + 0.7f * pace * math.max(0f, math.cos(leg)), k < 2 ? 11f : -11f);
            }
            if (scrub != null)
            {
                scrub.localPosition = scrubAt + rise;
                scrub.localRotation = shell.localRotation;
            }
        }
    }

    /// <summary>
    /// A-76 row 1 (#40): the glass heron. It wades on its two stilt legs (one thigh swings forward as the other pushes
    /// back, the shin folding at the backward ankle to lift each foot clear), the body rocking over each step and the
    /// S-neck pumping. At rest it stands still, then every few seconds tips its body and neck down and stabs the sand with
    /// its beak, and slowly straightens. The litter round its feet stays lying; the sandpiper on its back rides the body.
    /// The sand drifts round its feet stay in the ground while it walks and are left behind.
    /// "-walkerphase stab" (DebugControls, for shots) holds it at the bottom of a stab.
    /// </summary>
    sealed class HeronRig : ColossusRig
    {
        const float StabEvery = 9f, BodyStab = 45f, NeckStab = 65f;
        static readonly Vector3 HipAt = new Vector3(0f, 16f, 0f), NeckAt = new Vector3(0f, 21f, 5f), HeadAt = new Vector3(0f, 28.5f, 7.8f);

        readonly MeshRenderer baked;
        readonly SkinnedMeshRenderer skinned;
        readonly Transform body, neck, head;
        readonly Transform[] thighs = new Transform[2], shins = new Transform[2];
        readonly Vector3[] thighAt = new Vector3[2];
        readonly List<Transform> bones = new List<Transform>();
        readonly List<Vector3> restAt = new List<Vector3>();
        protected override bool Ready => skinned != null;

        public HeronRig(Transform root) : base(root)
        {
            var part = root.Find("Body");
            var filter = part != null ? part.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return;
            baked = part.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            var rig = Bone("Rig", root, Vector3.zero);
            body = Bone("Body", rig, HipAt);
            neck = Bone("Neck", body, NeckAt - HipAt);
            head = Bone("Head", neck, HeadAt - NeckAt);
            bones.AddRange(new[] { rig, body, neck, head });
            // As BuildGlassHeron: 0 left, 1 right; the right foot a little ahead.
            var feet = new Vector3[2];
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? -1f : 1f, ahead = k == 0 ? -0.6f : 1.6f;
                thighAt[k] = new Vector3(s * 2.2f, 16f, 0f);
                var ankle = new Vector3(s * 2.5f, 8f, -1.6f + ahead * 0.4f);
                feet[k] = new Vector3(s * 2.7f, 0.5f, 0.4f + ahead);
                thighs[k] = Bone("Thigh" + k, rig, thighAt[k]);
                shins[k] = Bone("Shin" + k, thighs[k], ankle - thighAt[k]);
                bones.Add(thighs[k]);
                bones.Add(shins[k]);
            }
            var drift = Bone("Drift", rig, Vector3.zero);
            bones.Add(drift);   // 8
            foreach (var b in bones)
                restAt.Add(b.localPosition);

            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var k = p.x < 0f ? 0 : 1;
                int thigh = 4 + k * 2, shin = thigh + 1;
                var foot = feet[k];
                var fromFoot = new Vector2(p.x - foot.x, p.z - foot.z);
                // The toes run up to 3.6 m forward of the foot and 2.2 m back, thin, along the ground.
                var onToes = p.y < 0.9f && math.abs(fromFoot.x) < 2.6f && fromFoot.y > -2.6f && fromFoot.y < 4f
                             && (math.abs(fromFoot.x) < 0.5f || math.abs(math.abs(fromFoot.x) - 0.5f * fromFoot.y) < 0.6f);
                if (p.y < 0.65f && !onToes)
                    weights[i] = Weigh(8, 8, 0f);                                                        // sand drift
                else if (p.y < 15.4f && math.abs(p.x) > 0.8f)
                    weights[i] = p.y < 8.6f ? Weigh(thigh, shin, 1f - math.smoothstep(7.4f, 8.6f, p.y))
                                            : Weigh(thigh, 1, math.smoothstep(14.2f, 15.4f, p.y));
                else if (p.y > 28.2f)
                    weights[i] = Weigh(3, 3, 0f);                                                        // head, beak and crest
                else if (p.z > 2f && math.abs(p.x) < 1.9f && p.y > 20.5f)
                    weights[i] = p.y > 27f ? Weigh(2, 3, math.smoothstep(27f, 28.2f, p.y))
                                           : Weigh(1, 2, math.smoothstep(20.5f, 22.5f, p.y));
                else
                    weights[i] = Weigh(1, 1, 0f);
            }
            skinned = Skin(part, mesh, bones.ToArray(), weights);
            // The sandpiper rides the body bone.
            var piper = root.Find("Sandpiper");
            if (piper != null)
                piper.SetParent(body, true);
            Debug.Log($"ColossusRig: glass heron skinned ({v.Length} verts)");
        }

        protected override void Show(bool animated)
        {
            Swap(baked, skinned, animated);
            if (animated)
                return;
            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].localPosition = restAt[i];
                bones[i].localRotation = Quaternion.identity;
            }
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            var phase = math.PI * stride;
            // The stab: quick down, a moment's hold, slowly back up; only once it has settled into its rest.
            var c = (float)(t % StabEvery);
            var stab = ColossusWalker.Phase == "stab" ? 1f : settle * math.smoothstep(0f, 0.6f, c) * (1f - math.smoothstep(1.6f, 3.2f, c));
            var watch = 1f - stab;
            body.localPosition = HipAt + Vector3.up * (0.25f * pace * math.abs(math.sin(phase)));
            body.localRotation = Quaternion.Euler(BodyStab * stab + 2f * pace * math.sin(2f * phase), 0f, 2.5f * pace * math.sin(phase));
            // The neck pumps with each step, turns a little toward the player and stabs; the head does most of the looking.
            neck.localRotation = Quaternion.Euler(NeckStab * stab + 6f * pace * math.sin(2f * phase), 0.4f * Look * watch, 0f);
            var glance = 8f * math.sin(t * 0.13f) * settle * (1f - math.saturate(math.abs(Look) / 10f));
            head.localRotation = Quaternion.Euler(-4f * stab, (0.6f * Look + glance) * watch, 0f);
            for (int k = 0; k < 2; k++)
            {
                var leg = phase + (k == 0 ? 0f : math.PI);
                // Swinging forward while cos > 0: the foot lifts, the shin folding back at the ankle.
                var lift = pace * math.max(0f, math.cos(leg));
                thighs[k].localRotation = Quaternion.Euler(-14f * pace * math.sin(leg) - 10f * lift, 0f, 0f);
                thighs[k].localPosition = thighAt[k] + Vector3.up * (0.6f * lift);
                shins[k].localRotation = Quaternion.Euler(32f * lift, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// A-76 row 2 (#40): the lantern stag. It walks on four slim legs in diagonal pairs, the knees folding as each leg
    /// lifts, its barrel rolling a little and its neck nodding with the steps. At rest it lowers its head to graze now and
    /// then and raises it again to look about (toward the player when one is near). The lantern flames
    /// (StoneColossi.GlowLanterns) hang from its antlers, so the rig moves their points onto its head bone.
    /// </summary>
    sealed class StagRig : ColossusRig
    {
        const float GrazeEvery = 14f, Graze = 32f;
        static readonly Vector3 BodyAt = new Vector3(0f, 16.5f, 0f), NeckAt = new Vector3(0f, 18.5f, 9.5f), HeadAt = new Vector3(0f, 24.5f, 13.5f);

        readonly MeshRenderer baked;
        readonly SkinnedMeshRenderer skinned;
        readonly Transform body, neck, head;
        readonly Transform[] uppers = new Transform[4], lowers = new Transform[4];
        readonly Vector3[] hipAt = new Vector3[4];
        readonly List<Transform> bones = new List<Transform>();
        readonly List<Vector3> restAt = new List<Vector3>();

        protected override bool Ready => skinned != null;

        public StagRig(Transform root) : base(root)
        {
            var part = root.Find("Body");
            var filter = part != null ? part.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return;
            baked = part.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var v = mesh.vertices;
            var rig = Bone("Rig", root, Vector3.zero);
            body = Bone("Body", rig, BodyAt);
            neck = Bone("Neck", body, NeckAt - BodyAt);
            head = Bone("Head", neck, HeadAt - NeckAt);
            bones.AddRange(new[] { rig, body, neck, head });
            // As BuildLanternStag: 0 front left, 1 front right (a step ahead), 2 back left (a step back), 3 back right.
            var knees = new Vector3[4];
            var hooves = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var forward = k == 1 ? 2f : k == 2 ? -1.5f : 0f;
                hipAt[k] = new Vector3(sx * 3.4f, 15f, sz * 9f);
                knees[k] = new Vector3(sx * 3.3f, 8f, sz * 9f + (sz < 0f ? -2.2f : 0.6f) + forward * 0.5f);
                hooves[k] = new Vector3(sx * 3.2f, 1.2f, sz * 9f + forward);
                uppers[k] = Bone("Leg" + k, rig, hipAt[k]);
                lowers[k] = Bone("Shank" + k, uppers[k], knees[k] - hipAt[k]);
                bones.Add(uppers[k]);
                bones.Add(lowers[k]);
            }
            foreach (var b in bones)
                restAt.Add(b.localPosition);

            var neckFrom = NeckAt;
            var neckDir = (HeadAt - NeckAt).normalized;
            var neckLength = Vector3.Distance(HeadAt, NeckAt);
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var k = (p.x < 0f ? 0 : 1) + (p.z > 0f ? 0 : 2);
                int upper = 4 + k * 2, lower = upper + 1;
                // Where this leg runs at the vertex's height, and whether the vertex is on it.
                var legAt = p.y > 8f ? Vector3.Lerp(knees[k], hipAt[k], (p.y - 8f) / 7f) : Vector3.Lerp(hooves[k], knees[k], (p.y - 1.2f) / 6.8f);
                var onLeg = p.y < 13.5f && math.abs(p.x - legAt.x) < 2.3f && math.abs(p.z - legAt.z) < 2.3f;
                var u = Vector3.Dot(p - neckFrom, neckDir) / neckLength;
                var offAxis = Vector3.Distance(p, neckFrom + neckDir * (u * neckLength));
                if (onLeg)
                    weights[i] = p.y < 8.6f ? Weigh(upper, lower, 1f - math.smoothstep(7.4f, 8.6f, p.y))
                                            : Weigh(upper, 1, math.smoothstep(12f, 13.5f, p.y));
                else if (p.y > 25.2f || (p.y > 22f && p.z > 9f && math.abs(p.x) > 2.6f))
                    weights[i] = Weigh(3, 3, 0f);                                                        // head, antlers, lanterns, vine, nest
                else if (p.z > 10f && p.y > 23.8f)
                    weights[i] = Weigh(2, 3, math.smoothstep(23.8f, 25.2f, p.y));
                else if (u > 0.1f && (offAxis < 3.6f || u > 0.6f))
                    weights[i] = Weigh(1, 2, math.smoothstep(0.1f, 0.4f, u));
                else
                    weights[i] = Weigh(1, 1, 0f);
            }
            skinned = Skin(part, mesh, bones.ToArray(), weights);
            // The lantern flame points follow the head.
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name.StartsWith("Lantern"))
                {
                    child.SetParent(head, true);
                    i--;
                }
            }
            Debug.Log($"ColossusRig: lantern stag skinned ({v.Length} verts)");
        }

        protected override void Show(bool animated)
        {
            Swap(baked, skinned, animated);
            if (animated)
                return;
            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].localPosition = restAt[i];
                bones[i].localRotation = Quaternion.identity;
            }
        }

        protected override void Pose(float pace, float stride, float settle, float t)
        {
            var phase = math.PI * stride;
            body.localPosition = BodyAt + Vector3.up * (0.3f * pace * math.abs(math.sin(phase)) + 0.08f * settle * math.sin(t * 0.6f));
            body.localRotation = Quaternion.Euler(0.8f * pace * math.sin(2f * phase), 0f, 1.6f * pace * math.sin(phase));
            // Grazing at rest, unless the player is near enough to be looked at.
            var c = (float)(t % GrazeEvery);
            var watching = math.saturate(math.abs(Look) / 6f);
            var graze = settle * (1f - watching) * math.smoothstep(0f, 2.5f, c) * (1f - math.smoothstep(7f, 9.5f, c));
            neck.localRotation = Quaternion.Euler(Graze * graze + 4f * pace * math.sin(2f * phase), 0.35f * Look, 0f);
            var glance = 10f * math.sin(t * 0.12f) * settle * (1f - graze) * (1f - watching);
            head.localRotation = Quaternion.Euler(14f * graze - 3f * pace, 0.65f * Look + glance, 0f);
            for (int k = 0; k < 4; k++)
            {
                var leg = phase + (k == 0 || k == 3 ? 0f : math.PI);
                var lift = pace * math.max(0f, math.cos(leg));
                var hind = k >= 2;
                uppers[k].localRotation = Quaternion.Euler(-16f * pace * math.sin(leg) + (hind ? 8f : -12f) * lift, 0f, 0f);
                uppers[k].localPosition = hipAt[k] + Vector3.up * (0.4f * lift);
                // Front knees fold back, hind hocks fold forward.
                lowers[k].localRotation = Quaternion.Euler((hind ? -30f : 36f) * lift, 0f, 0f);
            }
        }
    }
}
