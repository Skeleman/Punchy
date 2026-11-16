using System;
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
        /// The parts, drawn in this order. The order is part of the look for now: parts share the variant's random draws,
        /// so moving one reshuffles the ones after it (per-part seed streams come with a later row and a re-pin).
        /// </summary>
        public UnitPart[] Parts = Array.Empty<UnitPart>();
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
        public Vector3 Span, SpanMetres;
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

        /// <summary>A point in metres: share of the frame plus metres.</summary>
        public Vector3 Point(Vector3 share, Vector3 metres) =>
            new Vector3(share.x * Width + metres.x, share.y * Height + metres.y, share.z * Depth + metres.z);

        public Vector3 At(in UnitPart p) => Point(p.At, p.AtMetres);
        public Vector3 To(in UnitPart p) => Point(p.To, p.ToMetres);
        public Vector3 Span(in UnitPart p) => Point(p.Span, p.SpanMetres);
    }
}
