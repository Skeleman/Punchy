using System;
using System.Globalization;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// R-30 row 1 (#323): side-by-side leaf looks for trees and bushes, one per play session, chosen by
    /// "-leaftrial a|b|c" (absent: today's look). The cut-outs are marks in the foliage colour's alpha, cut by
    /// Flora.hlsl's FloraLeafClip in every pass (colour, shadows, depth), so shadows match the leaves.
    /// (a) leaf cards over a shrunk core, (b) two cut-out shells, (c) the current crown with a frayed silhouette ring.
    /// All three bend the crown's normals out from its centre so it shades as one soft volume.
    /// Near levels only (0 and 1); the far level and the impostor keep today's crown during the trial.
    /// </summary>
    public static class FloraLeafTrial
    {
        static int mode = -1;

        /// <summary>0 off, 1 cards, 2 shells, 3 rim (read once; safe on the grow workers).</summary>
        public static int Mode
        {
            get
            {
                if (mode < 0)
                {
                    var args = Punchy.Debugging.DebugControls.LaunchArgs;
                    var i = Array.IndexOf(args, "-leaftrial");
                    var v = i >= 0 && i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : "";
                    mode = v == "a" ? 1 : v == "b" ? 2 : v == "c" ? 3 : 0;
                    if (mode > 0)
                        Debug.Log($"[leaftrial] option {v}");
                }
                return mode;
            }
        }

        public static void Apply(FloraForm form, FloraMeshBuilder builder, int seed)
        {
            var m = Mode;
            if (m == 0)
                return;
            if (form != FloraForm.BroadleafTree && form != FloraForm.Conifer && form != FloraForm.Shrub &&
                form != FloraForm.HiveTree && form != FloraForm.Palm && form != FloraForm.DryBush)
                return;
            var tris = builder.FoliageTriangles(out var centre, out var extents);
            if (tris.Count == 0)
                return;
            var crown = extents.magnitude;
            var conifer = form == FloraForm.Conifer;
            builder.LeafSphereNormals(centre, extents, conifer ? 0.45f : 0.75f);
            switch (m)
            {
                case 1:
                    var size = Mathf.Clamp(crown * (conifer ? 0.08f : 0.12f), 0.15f, 0.6f);
                    builder.LeafCore(-size * 0.35f, 0.8f);
                    builder.LeafCards(tris, size, size * size * 0.55f, new System.Random(seed ^ 0x51ed));
                    break;
                case 2:
                    var step = Mathf.Clamp(crown * 0.05f, 0.06f, 0.3f);
                    builder.LeafCore(-step, 0.85f);
                    builder.LeafShell(tris, step * 0.2f, 0);
                    builder.LeafShell(tris, step * 1.1f, 1);
                    break;
                case 3:
                    builder.LeafRim();
                    break;
            }
        }
    }
}
