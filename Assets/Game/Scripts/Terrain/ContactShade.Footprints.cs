using System.Collections.Generic;
using Punchy.Flora;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-136 row 3: the one footprint fit. A flora model (on the terrain or in a cave) stands on its feet, fitted to its
    /// mesh's cross-section just above its base (<see cref="FitFeet"/>, <see cref="ModelFeet"/>); a kit part (a cave
    /// room's crystal, drum or box) on its foot, its bottom cap or its lowest face, or along its lowest line when it lies
    /// on its side (<see cref="PartFoot"/>). Where it stands (the floor, or a raised top under it) is the caller's.
    /// </summary>
    public static partial class ContactShade
    {
        /// <summary>Where the models are cut for their feet: just above the base, under any plinth's top or step.</summary>
        public const float SliceHeight = 0.08f;
        /// <summary>The most feet one model keeps (its fit coarsens until it fits).</summary>
        const int MaxFeet = 160;
        /// <summary>The coarsest bin (metres) the feet fit merges outline points into: model geometry, not a view range.</summary>
        const float CoarsestFootBin = 50f;

        /// <summary>
        /// A placed model's footprint in model space (before its rotation and scale), into <paramref name="into"/>: a tree
        /// stands on its trunk alone, never its roots or canopy (a disc of the trunk's width); anything else on its feet,
        /// else its ground colliders, else its trunk. With <paramref name="stem"/>, a model with none of those stands on
        /// a stem sized from its spread (a cave plant). Returns how many it added.
        /// </summary>
        public static int ModelFeet(FloraModel m, List<Contact> into, bool stem = false)
        {
            if (m == null || m.Species == null)
                return 0;
            var kind = KindOf(m.DetailClass());
            var before = into.Count;
            var trunkTree = m.TrunkRadius > 0f && m.Colliders.Count == 0 && m.Boxes.Count == 0 && !FloraGenerator.Standable(m.Species.Form);
            if (!trunkTree && m.Feet.Count > 0)
            {
                foreach (var foot in m.Feet)
                {
                    var f = foot;
                    f.Kind = kind;
                    into.Add(f);
                }
            }
            else if (!trunkTree && m.Colliders.Count > 0)
            {
                for (int c = 0; c < m.Colliders.Count; c++)
                    if ((c < m.ColliderBase.Count ? m.ColliderBase[c] : 0f) < 0.05f)
                    {
                        var col = m.Colliders[c];
                        into.Add(Contact.Disc(new float3(col.x, 0f, col.y), col.z, kind));
                    }
            }
            else if (m.TrunkRadius > 0f)
                into.Add(Contact.Disc(float3.zero, m.TrunkRadius, kind));
            if (stem && into.Count == before)
            {
                var radius = math.min(m.Radius * 0.35f, 0.4f);
                if (radius > 0f)
                    into.Add(Contact.Disc(float3.zero, radius, kind));
            }
            return into.Count - before;
        }

        /// <summary>
        /// A kit part's footprint (shape 1 a crystal from its base, 2 a drum, else a box; half its half sizes, a drum's
        /// radius in x, its half length in y and, if set, its second radius in z) and its lowest point, foot: a crystal
        /// by its foot, a drum standing by its bottom cap, a drum on its side (a fallen drum, a bone) along the lowest line
        /// of its round, a box by its lowest corner across the two axes nearest level.
        /// </summary>
        public static Contact PartFoot(int shape, float3 centre, quaternion rotation, float3 half, out float3 foot)
        {
            var up = math.mul(rotation, new float3(0f, 1f, 0f));
            switch (shape)
            {
                case 1:
                    foot = centre;
                    return Contact.Disc(foot, half.x * 0.8f, Kind.Rock);
                case 2 when math.abs(up.y) < 0.5f:
                {
                    var u = math.mul(rotation, new float3(1f, 0f, 0f));
                    var w = math.mul(rotation, new float3(0f, 0f, 1f));
                    var rz = half.z > 0f ? half.z : half.x;
                    var low = math.abs(up.y) * half.y + math.sqrt(math.square(u.y * half.x) + math.square(w.y * rz));
                    foot = centre - new float3(0f, low, 0f);
                    var along = math.length(up.xz);
                    return Contact.Box(foot, along > 1e-4f ? up.xz / along : new float2(1f, 0f),
                        new float2(half.y * along, math.min(half.x, rz)), Kind.Building);
                }
                case 2:
                    foot = centre - up * half.y;
                    return Contact.Disc(foot, half.x, Kind.Building);
                default:
                {
                    var low = float.MaxValue;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new float3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f) * half;
                        low = math.min(low, math.mul(rotation, corner).y);
                    }
                    foot = centre + new float3(0f, low, 0f);
                    var x = math.mul(rotation, new float3(1f, 0f, 0f));
                    var z = math.mul(rotation, new float3(0f, 0f, 1f));
                    float2 h, axis;
                    if (math.abs(x.y) >= math.abs(up.y) && math.abs(x.y) >= math.abs(z.y))
                    {
                        h = half.yz;
                        axis = up.xz;
                    }
                    else if (math.abs(z.y) >= math.abs(up.y))
                    {
                        h = half.xy;
                        axis = x.xz;
                    }
                    else
                    {
                        h = half.xz;
                        axis = x.xz;
                    }
                    return Contact.Box(foot, axis, h, Kind.Building);
                }
            }
        }

        /// <summary>A box standing on its bottom face (a dungeon chest or stand-in): its footprint, by its foot.</summary>
        public static Contact BoxFoot(float3 centre, quaternion rotation, float3 half, Kind kind)
        {
            var foot = centre - math.mul(rotation, new float3(0f, half.y, 0f));
            return Contact.Box(foot, math.mul(rotation, new float3(1f, 0f, 0f)).xz, half.xz, kind);
        }

        /// <summary>
        /// Fits capsules to a model's cross-section (FloraMeshBuilder.Slice segments): the segments join into loops
        /// (a post, a wall, a boulder's outline), each loop's width is 4 × area / perimeter (a disc's diameter, a square's
        /// side, about twice a thin wall's thickness), and its outline is cut into short pieces, each a capsule along it.
        /// Main thread or worker; no Unity objects. The kind is set when the model is placed (ModelFeet).
        /// </summary>
        public static void FitFeet(List<Vector4> segments, List<Contact> into)
        {
            into.Clear();
            if (segments.Count == 0)
                return;
            // Join segments that share an end (quantised to 2 mm) into loops.
            var ids = new Dictionary<long, int>();
            var parent = new List<int>();
            int Id(float x, float z)
            {
                var key = ((long)math.round(x * 500f) << 32) ^ ((long)math.round(z * 500f) & 0xFFFFFFFFL);
                if (!ids.TryGetValue(key, out var id))
                {
                    id = parent.Count;
                    ids[key] = id;
                    parent.Add(id);
                }
                return id;
            }
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }
            var ends = new int[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                int a = Find(Id(s.x, s.y)), b = Find(Id(s.z, s.w));
                if (a != b)
                    parent[b] = a;
                ends[i] = a;
            }
            var area = new Dictionary<int, float>();
            var length = new Dictionary<int, float>();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var loop = Find(ends[i]);
                ends[i] = loop;
                area.TryGetValue(loop, out var a);
                length.TryGetValue(loop, out var l);
                area[loop] = a + (s.x * s.w - s.z * s.y) * 0.5f;
                length[loop] = l + math.distance(new float2(s.x, s.y), new float2(s.z, s.w));
            }

            for (float bin = 0.4f; ; bin *= 1.6f)
            {
                into.Clear();
                var pieces = new Dictionary<(int, int, int), List<float2>>();
                for (int i = 0; i < segments.Count; i++)
                {
                    var s = segments[i];
                    float2 p = new float2(s.x, s.y), q = new float2(s.z, s.w);
                    var steps = math.max(1, (int)math.ceil(math.distance(p, q) / 0.1f));
                    for (int k = 0; k <= steps; k++)
                    {
                        var pt = math.lerp(p, q, k / (float)steps);
                        var c = (int2)math.floor(pt / bin);
                        var key = (ends[i], c.x, c.y);
                        if (!pieces.TryGetValue(key, out var list))
                            pieces[key] = list = new List<float2>();
                        list.Add(pt);
                    }
                }
                if (pieces.Count > MaxFeet && bin < CoarsestFootBin)
                    continue;
                foreach (var piece in pieces)
                {
                    var pts = piece.Value;
                    var loop = piece.Key.Item1;
                    var width = math.clamp(4f * math.abs(area[loop]) / math.max(length[loop], 1e-4f), 0.1f, 3f);
                    var mean = float2.zero;
                    foreach (var pt in pts)
                        mean += pt;
                    mean /= pts.Count;
                    float sxx = 0f, szz = 0f, sxz = 0f;
                    foreach (var pt in pts)
                    {
                        var d = pt - mean;
                        sxx += d.x * d.x;
                        szz += d.y * d.y;
                        sxz += d.x * d.y;
                    }
                    var angle = 0.5f * math.atan2(2f * sxz, sxx - szz);
                    var u = new float2(math.cos(angle), math.sin(angle));
                    var v = new float2(-u.y, u.x);
                    float tMin = float.MaxValue, tMax = float.MinValue, sMin = float.MaxValue, sMax = float.MinValue;
                    foreach (var pt in pts)
                    {
                        var d = pt - mean;
                        var t = math.dot(d, u);
                        var s = math.dot(d, v);
                        tMin = math.min(tMin, t); tMax = math.max(tMax, t);
                        sMin = math.min(sMin, s); sMax = math.max(sMax, s);
                    }
                    var r = (sMax - sMin) * 0.5f;
                    var half = math.max(0f, (tMax - tMin) * 0.5f - r);
                    var mid = (tMax + tMin) * 0.5f;
                    var side = v * (sMax + sMin) * 0.5f;
                    into.Add(Contact.Capsule(mean + u * (mid - half) + side, mean + u * (mid + half) + side,
                        math.max(r, 0.005f), width, Kind.Plant));
                }
                return;
            }
        }
    }
}
