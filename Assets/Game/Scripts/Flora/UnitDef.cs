using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// W-175 (#415): a placed unit, such as a market stall or a smithy, as data: the parts it is made of, each a part
    /// library id (FloraGenerator.Parts) set in the unit's frame. A part is defined once, so changing the anvil changes
    /// every unit's anvil. Plain serialisable data (JsonUtility), so a unit the Model Lab saves (M-06 row 5) and a coded
    /// one are the same thing. See docs/agents/parts-and-units.md.
    /// </summary>
    [Serializable]
    public class UnitDef
    {
        /// <summary>Stable id ("smithy"), used by the world file and the Model Lab.</summary>
        public string Id;
        /// <summary>The frame's depth as a share of its width (its width is its height times the species' Spread).</summary>
        public float DepthOfWidth = 1f;
        /// <summary>
        /// W-175 row 4: the frame's width as a share of its height, in place of the species' Spread (0: Spread). A round
        /// unit (the fire pit) sets 1, so a share of its width is a share of its height.
        /// </summary>
        public float WidthOfHeight;
        /// <summary>
        /// W-175 row 4: the species the unit was made on (the Model Lab's host; empty for a coded unit). Its size, colours
        /// and finish are what the unit was made to look right at.
        /// </summary>
        public string Host;
        /// <summary>
        /// W-175 row 4: a fixed frame in metres (width, height, depth), so every share is plain metres; zero (every coded
        /// unit) grows the frame from the host species' Height and Spread instead.
        /// </summary>
        public Vector3 Size;
        /// <summary>
        /// W-175 row 4: each part draws from its own random stream (seeded from the variant, its part id and its Seed), so
        /// adding, removing or moving a part leaves the others as they were. Off for the coded units, whose parts share
        /// the variant's stream in order (their look is pinned).
        /// </summary>
        public bool PartStreams;
        /// <summary>
        /// The parts, drawn in this order. Without PartStreams the order is part of the look: parts share the variant's
        /// random draws, so moving one reshuffles the ones after it.
        /// </summary>
        public UnitPart[] Parts = Array.Empty<UnitPart>();

        /// <summary>
        /// W-175 row 4: the unit as indented JSON with its numbers written short (0.7, not 0.699999988079071): each is the
        /// shortest text that reads back as the same float, so nothing changes on a load.
        /// </summary>
        public string ToJson() => TidyNumbers(JsonUtility.ToJson(this, true));

        /// <summary>
        /// W-175 row 6: snaps the parts' metre offsets together: a point in metres (At or To, with no share of the frame)
        /// within <paramref name="join"/> of an earlier part's point moves onto it, so a wall's end meets the next wall's
        /// start; then every metre value rounds to <paramref name="grid"/>. Shares are left alone. Returns how many
        /// points joined.
        /// </summary>
        public int Snap(float grid = 0.05f, float join = 0.12f)
        {
            var joined = 0;
            var points = new System.Collections.Generic.List<Vector3>();
            Vector3 Join(Vector3 share, Vector3 metres)
            {
                if (share != Vector3.zero)
                    return metres;
                foreach (var q in points)
                    if (q != metres && (q - metres).sqrMagnitude <= join * join)
                    {
                        joined++;
                        return q;
                    }
                points.Add(metres);
                return metres;
            }
            Vector3 Round(Vector3 v) => grid <= 0f ? v : new Vector3(Mathf.Round(v.x / grid) * grid, Mathf.Round(v.y / grid) * grid, Mathf.Round(v.z / grid) * grid);
            for (int i = 0; i < Parts.Length; i++)
            {
                var p = Parts[i];
                p.AtMetres = Round(Join(p.At, p.AtMetres));
                p.ToMetres = Round(Join(p.To, p.ToMetres));
                p.SpanMetres = Round(p.SpanMetres);
                Parts[i] = p;
            }
            return joined;
        }

        /// <summary>Rewrites every decimal number in JSON text as the shortest text for the same float.</summary>
        public static string TidyNumbers(string json) =>
            Regex.Replace(json, @"-?\d+\.\d+(?:[eE][-+]?\d+)?", m =>
            {
                var f = (float)double.Parse(m.Value, CultureInfo.InvariantCulture);
                var text = f.ToString("R", CultureInfo.InvariantCulture);
                return text.Contains(".") || text.Contains("E") ? text : text + ".0";
            });
    }

    /// <summary>
    /// One part in a unit. Points and sizes are in the unit's frame: a share of its width, height and depth (x, y, z)
    /// plus metres, so a unit grown wider or taller carries its parts with it. A part reads the points it needs: At
    /// (where it stands), To (a second point: a post's top, an awning's front edge, the far end of a row) and Span (its
    /// size). What each one means is written on the part (FloraGenerator.Parts).
    /// </summary>
    [Serializable]
    public struct UnitPart
    {
        /// <summary>The part library id ("anvil").</summary>
        public string Part;
        public Vector3 At, AtMetres;
        public Vector3 To, ToMetres;
        /// <summary>Its full size along x, y and z (a round part's diameter is Span.x), whatever the part.</summary>
        public Vector3 Span, SpanMetres;
        /// <summary>W-175 row 4: turns the part about the up axis through At, in degrees (clockwise seen from above).</summary>
        public float Yaw;
        /// <summary>W-175 row 4: mirrors the part left to right (x) about At, before its Yaw.</summary>
        public bool Mirror;
        /// <summary>
        /// W-175 row 4: the part sets the unit's bounds (height, width, trunk) from its own size; otherwise the unit's frame
        /// does. Only parts that know their own bounds (a hide tent, a fire ring) read it.
        /// </summary>
        public bool Bounds;
        /// <summary>W-175 row 4: with UnitDef.PartStreams, picks this part's stream (two crates side by side differ).</summary>
        public int Seed;
        /// <summary>
        /// W-175 row 6: for a "unit" part, the id of the unit drawn here (UnitLibrary): its parts in a frame Span in size
        /// (or the unit's own Size, or this unit's frame), its origin at At, turned and mirrored with the part.
        /// </summary>
        public string Unit;
    }

    /// <summary>A unit variant's frame: its width, height and depth in metres, which UnitPart's shares are of.</summary>
    public readonly struct UnitFrame
    {
        public readonly float Width, Height, Depth;

        public UnitFrame(float width, float height, float depthOfWidth)
        {
            Width = width;
            Height = height;
            Depth = width * depthOfWidth;
        }

        UnitFrame(Vector3 size)
        {
            Width = size.x;
            Height = size.y;
            Depth = size.z;
        }

        /// <summary>W-175 row 4: a fixed frame in metres (UnitDef.Size).</summary>
        public static UnitFrame Sized(Vector3 size) => new UnitFrame(size);

        /// <summary>A point in metres: share of the frame plus metres.</summary>
        public Vector3 Point(Vector3 share, Vector3 metres) =>
            new Vector3(share.x * Width + metres.x, share.y * Height + metres.y, share.z * Depth + metres.z);

        public Vector3 At(in UnitPart p) => Point(p.At, p.AtMetres);
        public Vector3 To(in UnitPart p) => Point(p.To, p.ToMetres);
        public Vector3 Span(in UnitPart p) => Point(p.Span, p.SpanMetres);
    }
}
