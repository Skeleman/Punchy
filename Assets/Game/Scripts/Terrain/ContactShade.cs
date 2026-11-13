using Punchy.Settings;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The seam darkening where a placed thing meets the ground, rock or tile under it (W-135 row 4, W-136): not a
    /// shadow, but the ground's side of the darkening Grounding.hlsl gives the thing's own base, so both faces of the seam
    /// are darkened alike and the hard edge between them reads softer. Darkest right at the contact (Seam: Grounding's
    /// tight contact times its broad rise, what a base gets at height 0), back to 1 within a short reach sized from the
    /// contact's width (not the thing's height or spread). Baked into vertex colours when a mesh builds: no per-frame cost.
    /// W-136 row 3: the one path for every surface. Terrain chunks (GroundContacts), cave chunks and room tiles
    /// (CaveSystem.Contacts, CaveRoomKit) all take their footprints from ContactShade.Footprints, shade through
    /// <see cref="Weigh"/> and <see cref="Darken"/>, and share one per-kind Strength and Reach set.
    /// </summary>
    public static partial class ContactShade
    {
        /// <summary>Which strength and reach a contact takes.</summary>
        public enum Kind : byte { Plant, Rock, Building, Prop }

        /// <summary>The darkest it gets, at the seam: PunchyGroundContact (0.72) x PunchyGroundBroad (0.75), as a base.</summary>
        public const float Seam = 0.54f;
        /// <summary>The shortest and longest fall-off (m): a stem's seam still blends, a big plinth's stays a seam.</summary>
        public const float MinReach = 0.15f, MaxReach = 1.5f;
        /// <summary>Darkening fainter than this at its seam (after spreading over a surface's vertex spacing) is skipped.</summary>
        public const float Faint = 0.04f;
        /// <summary>The most a tunable reach can grow a contact (Extent and the terrain's chunk margins allow for this much).</summary>
        public const float MaxReachScale = 2f;

        /// <summary>The tunables, per Kind (x plants and trees, y rocks, z buildings, w props): how dark the seam gets
        /// (1 = Seam) and how far it reaches (1 = about its width). FloraSystem's inspector sets them (<see cref="Set"/>);
        /// jobs get copies when they're scheduled.</summary>
        public static float4 Strength = new float4(1f, 1f, 1f, 1f), ReachScale = new float4(1f, 1f, 1f, 1f);
        /// <summary>Bumped when a tunable changes: every surface re-meshes what it has built once.</summary>
        public static int Version;

        /// <summary>Sets every kind's tunables; a change bumps Version.</summary>
        public static void Set(float4 strength, float4 reachScale)
        {
            if (math.all(strength == Strength) && math.all(reachScale == ReachScale))
                return;
            Strength = strength;
            ReachScale = reachScale;
            Version++;
        }

        public static Kind KindOf(DetailPolicy.FloraClass c) => c switch
        {
            DetailPolicy.FloraClass.Tree or DetailPolicy.FloraClass.Plant => Kind.Plant,
            DetailPolicy.FloraClass.Rock => Kind.Rock,
            DetailPolicy.FloraClass.Structure => Kind.Building,
            _ => Kind.Prop
        };

        /// <summary>How far the darkening reaches out from a contact this wide (m): about its own width.</summary>
        public static float Reach(float width) => math.clamp(width, MinReach, MaxReach);

        /// <summary>The shade (Seam..1) at distance d (m) from a contact whose darkening reaches this far.</summary>
        public static float Shade(float d, float reach) => math.lerp(Seam, 1f, math.smoothstep(0f, reach, d));

        /// <summary>
        /// How a contact of this width and kind darkens a surface whose vertices are minReach apart (0: as fine as it
        /// likes): its reach after the tunables, spread over at least minReach and faded in proportion when narrower
        /// (keeping its overall darkness: a 6 cm post on a 1 m grid gives a faint patch, not a missed one or a vertex-sized
        /// blot). False when there's nothing to see.
        /// </summary>
        public static bool Weigh(float width, Kind kind, float4 strength, float4 reachScale, float minReach,
                                 out float spread, out float weight)
        {
            var k = (int)kind;
            var reach = Reach(width) * math.clamp(reachScale[k], 0f, MaxReachScale);
            spread = math.max(reach, minReach);
            weight = (minReach > 0f ? math.min(1f, reach / minReach) : 1f) * strength[k];
            return reach > 0f && (1f - Seam) * weight >= Faint;
        }

        /// <summary>The shade (0..1, 1 untouched) at distance d from a contact Weigh gave this spread and weight.</summary>
        public static float Darken(float d, float spread, float weight) =>
            math.max(0f, 1f - (1f - Shade(math.max(d, 0f), spread)) * weight);

        /// <summary>
        /// Where something touches down: a footprint at its base, a rectangle (turned about y: Axis is its local x in the
        /// xz plane) grown by Radius, so a disc is a zero rectangle with a radius and a capsule a zero-wide one. Distance
        /// is to the footprint itself, in 3D, so a wall rising beside a thing darkens up from the floor as far as the
        /// floor does out from it. Width sets how far it reaches (Reach), Kind which tunables it takes.
        /// </summary>
        public struct Contact
        {
            public float3 Base;
            public float2 Axis, Half;
            public float Radius, Width;
            public Kind Kind;

            /// <summary>A round footprint (a stem, a crystal's foot, a drum) of this radius.</summary>
            public static Contact Disc(float3 at, float radius, Kind kind) => new Contact
            {
                Base = at, Axis = new float2(1f, 0f), Radius = radius, Width = radius * 2f, Kind = kind
            };

            /// <summary>A rectangular footprint (a chest, a plinth) with these half sizes along axis and across it.</summary>
            public static Contact Box(float3 at, float2 axis, float2 half, Kind kind) => new Contact
            {
                Base = at, Axis = math.normalizesafe(axis, new float2(1f, 0f)), Half = math.abs(half),
                Width = math.cmin(math.abs(half)) * 2f, Kind = kind
            };

            /// <summary>A capsule from a to b (on the ground plane) of this radius, belonging to a part this wide.</summary>
            public static Contact Capsule(float2 a, float2 b, float radius, float width, Kind kind)
            {
                var mid = (a + b) * 0.5f;
                return new Contact
                {
                    Base = new float3(mid.x, 0f, mid.y), Axis = math.normalizesafe(b - a, new float2(1f, 0f)),
                    Half = new float2(math.distance(a, b) * 0.5f, 0f), Radius = radius, Width = width, Kind = kind
                };
            }

            /// <summary>How far its darkening reaches at reach scale 1 (m).</summary>
            public float Reach => ContactShade.Reach(Width);

            /// <summary>The farthest from Base any point it darkens can be, at any reach scale (a quick reject).</summary>
            public float Extent => math.length(Half) + Radius + Reach * MaxReachScale;

            /// <summary>A model-space contact placed: turned by yaw about y, scaled, then moved to at.</summary>
            public Contact Placed(float3 at, float yaw, float scale)
            {
                float c = math.cos(yaw), s = math.sin(yaw);
                float2 Turn(float2 v) => new float2(v.x * c - v.y * s, v.x * s + v.y * c);
                var xz = Turn(Base.xz) * scale;
                return new Contact
                {
                    Base = at + new float3(xz.x, Base.y * scale, xz.y), Axis = Turn(Axis), Half = Half * scale,
                    Radius = Radius * scale, Width = Width * scale, Kind = Kind
                };
            }

            /// <summary>Distance (m) from p to the footprint.</summary>
            public float Distance(float3 p)
            {
                var q = p - Base;
                var local = new float2(math.dot(q.xz, Axis), math.dot(q.xz, new float2(-Axis.y, Axis.x)));
                var e = math.max(math.abs(local) - Half, 0f);
                return math.max(math.length(new float3(e.x, q.y, e.y)) - Radius, 0f);
            }

            /// <summary>Distance (m) from p to the footprint across the ground plane (height ignored).</summary>
            public float Distance(float2 p) => Distance(new float3(p.x, Base.y, p.y));

            /// <summary>Its shade at p (0..1) on a surface whose vertices are minReach apart and sit up to pad off the
            /// true surface (Weigh, Darken).</summary>
            public float ShadeAt(float3 p, float minReach, float pad, float4 strength, float4 reachScale) =>
                Weigh(Width, Kind, strength, reachScale, minReach, out var spread, out var weight)
                    ? Darken(Distance(p) - pad, spread, weight) : 1f;

            /// <summary>Whether it darkens anywhere within radius of p (at these reach scales).</summary>
            public bool Near(float3 p, float radius, float4 reachScale) =>
                Distance(p) - radius < Reach * math.clamp(reachScale[(int)Kind], 0f, MaxReachScale);
        }

        /// <summary>A hash of a list of contacts and the tunables (0: none), which a build records to tell when it's out
        /// of date.</summary>
        public static uint Hash(System.Collections.Generic.List<Contact> list)
        {
            if (list == null || list.Count == 0)
                return 0u;
            var h = 2166136261u ^ (uint)Version;
            foreach (var c in list)
                h = math.hash(new float4(c.Base, c.Radius + c.Half.x * 3f + c.Half.y * 7f + c.Width * 11f + (int)c.Kind * 13f)) ^ (h * 16777619u);
            return h == 0u ? 1u : h;
        }
    }
}
