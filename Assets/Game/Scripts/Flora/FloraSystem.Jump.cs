using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Flora
{
    // W-151 row 1 (#287): after a jump (the first load, or a warp that moves the camera across the island in one frame)
    // the biggest things stream in first. Placement normally runs ground, shrubs, trees from one shared budget, and the
    // ground's many small tiles eat it first, so a fresh view filled with grass before any tree or building showed. In the
    // jump window the layers run in reverse (trees, which hold the settlement and landmark buildings, first) with a
    // boosted budget, most of it for trees. Nothing is placed differently, only sooner or later: placement stays
    // seed-derived and every client places the same plants (multiplayer.md).
    public partial class FloraSystem
    {
        /// <summary>A camera move longer than this in one frame is a jump (the terrain treats shorter moves as heading).</summary>
        const float JumpDistance = 200f; // streaming: detects a jump (warp/teleport), not a visual range
        /// <summary>Big-first ordering lasts at most this long after a jump, in real seconds (or until the trees are in).</summary>
        const float JumpWindow = 5f;
        /// <summary>The share of the boosted budget the first layer (trees) may take in the jump window; the rest flows on.</summary>
        const float JumpTreeShare = 0.75f;
        /// <summary>The ready log gives up on a layer after this long (e.g. a warp straight into a cave).</summary>
        const float JumpLogCap = 30f;
        static readonly string[] LayerNames = { "ground", "shrubs", "trees" };

        float2 jumpEye;
        bool jumpEyeSet;
        float jumpAt = -1f;
        int jumpFrame;
        bool jumpLoad;
        /// <summary>The load's window has run (one per load: the flora can be in well before the terrain says world ready).</summary>
        bool loadSeen;
        readonly float[] layerInAt = new float[3];
        readonly int[] layerInFrame = new int[3];

        /// <summary>
        /// Starts a jump window when the world loads or the camera jumps, notes when each layer's near ring is in (and
        /// logs it, like the terrain's world ready line), and says whether big-first ordering is on this frame.
        /// </summary>
        bool JumpUpdate(float2 camXZ)
        {
            var loading = Punchy.Terrain.TerrainStreamingSystem.Loading;
            var moved = jumpEyeSet && math.distance(camXZ, jumpEye) > JumpDistance;
            jumpEye = camXZ;
            jumpEyeSet = true;
            var now = Time.realtimeSinceStartup;
            if (!loading)
                loadSeen = false;
            if (moved || loading && !loadSeen && jumpAt < 0f)
            {
                loadSeen |= loading;
                jumpAt = now;
                jumpFrame = Time.frameCount;
                jumpLoad = loading;
                for (int i = 0; i < layerInAt.Length; i++)
                    layerInAt[i] = -1f;
            }
            if (jumpAt < 0f)
                return false;

            var allIn = true;
            foreach (var l in layers)
            {
                if (layerInAt[l.Index] >= 0f)
                    continue;
                if (l.ModelCount == 0 || !l.PlacingTiles.IsCreated && MissingNear(l, camXZ) == 0)
                {
                    layerInAt[l.Index] = now;
                    layerInFrame[l.Index] = Time.frameCount;
                }
                else
                    allIn = false;
            }
            if (allIn || now - jumpAt > JumpLogCap)
            {
                var line = new System.Text.StringBuilder($"[flora] after the {(jumpLoad ? "load" : "jump")}:");
                for (int i = layerInAt.Length - 1; i >= 0; i--)
                    line.Append(layerInAt[i] < 0f
                        ? $" {LayerNames[i]} not in after {JumpLogCap:F0} s;"
                        : $" {LayerNames[i]} in at {layerInAt[i]:F1} s since start ({layerInAt[i] - jumpAt:F1} s, {layerInFrame[i] - jumpFrame} frames after);");
                Debug.Log(line.ToString().TrimEnd(';'));
                jumpAt = -1f;
                return false;
            }
            return layerInAt[FloraPlacement.TreeLayer] < 0f && (loading || now - jumpAt < JumpWindow);
        }

        // The colossi are placed a few at a time for several seconds after the load, and each one used to drop every
        // streamed tile on the island (Replant), so the trees round the player vanished and came back after the load,
        // even with no colossus near. Now only the tiles under a circle that changed (where it was and where it is) go.
        FixedList512Bytes<float4> keptOut;
        FixedList512Bytes<float4> changedKeepOuts;
        bool replantAll;

        /// <summary>Notes which keep-out circles changed since the last replant; true if any did.</summary>
        bool ChangedKeepOuts()
        {
            ref var now = ref FloraPlacement.ColossusKeepOut.Data;
            changedKeepOuts.Clear();
            replantAll = false;
            for (int k = 0; k < math.max(now.Length, keptOut.Length); k++)
            {
                var was = k < keptOut.Length ? keptOut[k] : default;
                var c = k < now.Length ? now[k] : default;
                if (was.Equals(c))
                    continue;
                // (More changes than the list holds: drop every tile, as before.)
                replantAll |= changedKeepOuts.Length + 2 > changedKeepOuts.Capacity;
                if (was.w > 0f && !replantAll)
                    changedKeepOuts.Add(was);
                if (c.w > 0f && !replantAll)
                    changedKeepOuts.Add(c);
            }
            keptOut = now;
            return replantAll || changedKeepOuts.Length > 0;
        }

        /// <summary>Whether a tile reaches into a keep-out circle that just changed (plants are kept out by their own position).</summary>
        bool UnderChangedKeepOut(Layer l, int2 tile)
        {
            if (replantAll)
                return true;
            foreach (var c in changedKeepOuts)
                if (TileNearest(l, tile, c.xy) < c.z)
                    return true;
            return false;
        }
    }
}
