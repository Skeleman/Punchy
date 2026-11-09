using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// One rule for how much detail anything gets (refactor roadmap 3.7, Zach 11-08; ChatGPT's audit of the first pass):
    /// how big it looks on screen, not how far away it is. A 30 m colossus at 600 m looks as big as a 1.5 m deer at
    /// 30 m, so it gets the same treatment.
    ///
    /// The measure is <see cref="ScreenSize"/>: an object's size over its distance, in reference pixels (a 1080-line
    /// screen at the camera's real field of view). Reference pixels rather than real ones, so a higher resolution
    /// sharpens the picture without moving every detail boundary out and raising the cost with it.
    ///
    /// Each kind of object has a <see cref="Rule"/>, and the rule gives four separate answers, so one never drags
    /// another along: a big still rock wants a far shadow and no animation, a small fast critter frequent posing and
    /// no shadow, a far giant serpent smooth motion on a cheap mesh.
    /// - Geometry: which mesh detail (0 full, 1 reduced, 2 low, 3 very far).
    /// - Animation: how often it's posed or simulated (0 every frame, 1 every 2nd, 2 every 4th, 3 held).
    /// - Shadow: whether it casts one in the real-time cascades.
    /// - Light: for light sources, see <see cref="LightPolicy"/>.
    ///
    /// Tiers have hysteresis (<see cref="Hysteresis"/>): a boundary is crossed one way at the threshold and back only
    /// some way past it, so nothing flickers between tiers while the camera hovers at a boundary.
    ///
    /// Graphics settings move the thresholds: Detail scales geometry and animation, Shadows scales shadows (Medium,
    /// 1, is the game as tuned). Only what's drawn changes, never what's simulated or sent, except
    /// <see cref="SimulationReach"/>, which is fixed so every machine agrees.
    /// </summary>
    public static class DetailPolicy
    {
        /// <summary>Reference screen height (pixels) the thresholds are tuned in.</summary>
        public const float ReferenceLines = 1080f;
        /// <summary>The field of view (degrees, vertical) <see cref="SimulationReach"/> assumes, as the game camera's
        /// default: simulation runs on the host, which has no say in anyone's camera.</summary>
        public const float ReferenceFov = 60f;
        /// <summary>How far past a boundary (as a fraction of it) a tier has to go before it switches back.</summary>
        public const float Hysteresis = 0.15f;

        /// <summary>The current view, set once a frame by <see cref="SetView"/> (main thread), read anywhere (Burst too).</summary>
        public struct View
        {
            public float3 Camera;
            /// <summary>Reference pixels per metre at 1 m: ReferenceLines / (2 tan(fov / 2)).</summary>
            public float PixelsPerMetre;
            /// <summary>Graphics Detail and Shadows (1 = as tuned).</summary>
            public float Detail, Shadows;
        }

        public static readonly SharedStatic<View> Current = SharedStatic<View>.GetOrCreate<ViewKey>();
        struct ViewKey { }
        static int viewFrame = -1;

        /// <summary>Takes this frame's camera and graphics settings (once a frame; any system may call it first).</summary>
        public static void SetView(UnityEngine.Camera cam)
        {
            if (cam == null || viewFrame == Time.frameCount)
                return;
            viewFrame = Time.frameCount;
            Current.Data = new View
            {
                Camera = cam.transform.position,
                PixelsPerMetre = PixelsPerMetreAt(cam.fieldOfView),
                Detail = GraphicsSettings.Detail,
                Shadows = GraphicsSettings.Shadows
            };
        }

        public static float PixelsPerMetreAt(float fovDegrees)
            => ReferenceLines / (2f * math.tan(math.radians(math.clamp(fovDegrees, 5f, 170f)) * 0.5f));

        /// <summary>How big something of this size (m: its largest extent) looks at this distance, in reference pixels.</summary>
        public static float ScreenSize(float size, float distance, float pixelsPerMetre)
            => size / math.max(distance, 0.01f) * pixelsPerMetre;

        /// <summary>The distance at which something of this size looks this many reference pixels big (the same rule
        /// turned round, for code that still thinks in ranges).</summary>
        public static float Reach(float size, float pixels, float pixelsPerMetre)
            => size * pixelsPerMetre / math.max(pixels, 1e-3f);

        /// <summary>A simulation range by size, the same on every machine (the host's brain LOD): the distance at which
        /// something this big looks this many pixels at the reference view.</summary>
        public static float SimulationReach(float size, float pixels) => Reach(size, pixels, PixelsPerMetreAt(ReferenceFov));

        /// <summary>
        /// Three boundaries between four tiers, in reference pixels, biggest first: bigger on screen than Near is tier 0,
        /// than Mid tier 1, than Far tier 2, else tier 3. Use float.MaxValue / 0 to leave a tier out.
        /// </summary>
        public struct Thresholds
        {
            public float Near, Mid, Far;
            public Thresholds(float near, float mid, float far) { Near = near; Mid = mid; Far = far; }
            public Thresholds Scaled(float quality) => new Thresholds(Near / quality, Mid / quality, Far / quality);
        }

        /// <summary>
        /// The tier for this many pixels, from the tier it was in (-1 if none: first seen). Moving to a finer tier
        /// happens at the boundary; moving back to a coarser one only once it's Hysteresis below it.
        /// </summary>
        public static int Tier(float pixels, in Thresholds t, int previous)
        {
            var plain = pixels > t.Near ? 0 : pixels > t.Mid ? 1 : pixels > t.Far ? 2 : 3;
            if (previous < 0 || plain <= previous)
                return plain;
            // Coarser than last frame: only as far as the shrunk boundaries allow.
            var k = 1f - Hysteresis;
            var held = pixels > t.Near * k ? 0 : pixels > t.Mid * k ? 1 : pixels > t.Far * k ? 2 : 3;
            return math.max(held, previous);
        }

        // ------------------------------------------------------------------------------- the shared rules (3.7 addendum)
        // (1) on-screen size picks the tier: ScreenSize / Tier above. (2) hysteresis, (3) a capped tier's hand-off and
        // (4) a shared crossfade: below, for code that picks by reach or fades between tiers. All plain maths: no state,
        // no allocation, safe in Burst.

        /// <summary>Rule 2 for one boundary: above it counts as in, and something that was in stays in until it's
        /// Hysteresis below it.</summary>
        public static bool Holds(float value, float boundary, bool was)
            => value > boundary || was && value > boundary * (1f - Hysteresis);

        /// <summary>A step towards a target, the same whatever the frame rate.</summary>
        public static float Ease(float current, float target, float dt, float perSecond)
            => math.lerp(current, target, 1f - math.exp(-dt * perSecond));

        /// <summary>
        /// Rule 2 for a reach (a boundary that moves): a target within half of Hysteresis is ignored, so the answer
        /// doesn't swim as what it's worked out from wobbles, and past that it eases: quickly inwards, slowly outwards.
        /// Quick inwards because the reach is where the next tier takes over, and it mustn't stay past what this tier
        /// actually covers (see <see cref="CappedReach"/>).
        /// </summary>
        public static float EasedReach(float current, float target, float dt)
        {
            if (current <= 0f || current >= float.MaxValue || math.abs(target - current) <= current * Hysteresis * 0.5f)
                return current <= 0f || current >= float.MaxValue ? target : current;
            return Ease(current, target, dt, target < current ? 6f : 1.5f);
        }

        /// <summary>
        /// Rule 3, the hand-off for every capped tier (playtest 11-09): a near tier with a cap ends at the nearest thing
        /// the cap cut, not at a fixed distance, so the tier after it takes over from there and nothing falls between the
        /// two. (Lights seen from afar went out as you came in and came back up close.) With nothing cut, the normal reach
        /// stands.
        /// </summary>
        public static float CappedReach(float normalReach, float nearestCut) => math.min(normalReach, math.max(nearestCut, 10f));

        /// <summary>Rule 4, the one crossfade between tiers: 0 at <paramref name="from"/>, 1 at <paramref name="to"/>,
        /// eased. The tier leaving takes 1 minus it, so the two add up to one over the same band, measured the same way.</summary>
        public static float Crossfade(float value, float from, float to) => math.smoothstep(from, to, value);

        /// <summary>A kind of object's rule: the boundaries for each answer.</summary>
        public struct Rule
        {
            public Thresholds Geometry, Animation;
            /// <summary>Casts a real-time shadow while bigger than this (reference pixels).</summary>
            public float Shadow;
            /// <summary>How much a fast mover counts as bigger for Animation only (its motion shows before its shape
            /// does): pixels x (1 + Activity x how active, 0..1).</summary>
            public float Activity;
        }

        /// <summary>The four answers for one object, packed in a byte (geometry 2 bits, animation 2, shadow 1).</summary>
        public struct Decision
        {
            public byte Packed;
            public int Geometry => Packed & 3;
            public int Animation => (Packed >> 2) & 3;
            public bool Shadow => (Packed & 16) != 0;
            /// <summary>No decision yet (first seen): tiers start from scratch.</summary>
            public bool None => Packed == 255;
            public static Decision First => new Decision { Packed = 255 };
            public static Decision Of(int geometry, int animation, bool shadow)
                => new Decision { Packed = (byte)(geometry | animation << 2 | (shadow ? 16 : 0)) };
        }

        /// <summary>
        /// The rule applied: this object (size m, at distance m, activity 0..1) under this view, from last frame's
        /// decision (Decision.First when new).
        /// </summary>
        public static Decision Decide(in Rule rule, float size, float distance, float activity, in View view, Decision previous)
        {
            var pixels = ScreenSize(size, distance, view.PixelsPerMetre);
            var detail = math.max(view.Detail, 0.1f);
            var first = previous.None;
            var geometry = Tier(pixels, rule.Geometry.Scaled(detail), first ? -1 : previous.Geometry);
            var animation = Tier(pixels * (1f + rule.Activity * math.saturate(activity)), rule.Animation.Scaled(detail),
                                 first ? -1 : previous.Animation);
            var shadowAt = rule.Shadow / math.max(view.Shadows, 0.1f);
            var shadow = Holds(pixels, shadowAt, !first && previous.Shadow);
            return Decision.Of(geometry, animation, shadow);
        }

        // ------------------------------------------------------------------------------- rules by kind

        /// <summary>
        /// Creatures. Tuned so a deer (1.5 m) changes where it always has at 1080 lines and a 60 degree view: full
        /// capsule and every-frame pose inside 40 m, reduced to 100 m, low beyond; shadow to 100 m (A-70 row 4, A-82
        /// row 3). Bigger animals reach out in proportion; small critters come in. A creature crossing a body length
        /// or more a second counts up to half again as big for its pose rate.
        /// </summary>
        public static readonly Rule Creature = new Rule
        {
            Geometry = new Thresholds(35f, 14f, 0f),
            Animation = new Thresholds(35f, 14f, 0f),
            Shadow = 14f,
            Activity = 0.5f
        };

        /// <summary>The simulation's (brain LOD) boundary for creatures, reference pixels: a deer thinks every frame
        /// inside 80 m of a player (A-54), something 10x its size inside 800 m.</summary>
        public const float CreatureThinkPixels = 17.5f;

        /// <summary>What a flora model is, for its rule (refactor 3.5c, from FloraModel.Static and the form).</summary>
        public enum FloraClass : byte { Tree, Plant, Rock, Debris, Structure }

        /// <summary>
        /// Flora by class (refactor 3.5c / 3.3), the size being the bounding sphere's diameter times the plant's scale.
        /// Geometry picks the mesh (0, 1, 2) and tier 3 means not drawn at all (Far 0: always drawn); FloraCull.compute
        /// turns these into distances per plant. Tuned at the High preset (Detail 1.5, Shadows 1.4), against the old layer
        /// distances there (x View 1.3), on what fills a view: a 20 m conifer changes at 71 m and 286 m as it did and a
        /// 1.3 m shrub at 39 m and 143 m; bigger and smaller ones move out and in with their size. A first pass tuned on
        /// a 15 m tree ran about 0.5 ms slower on the GPU in a forest. See <see cref="FloraFloor"/> for small trees.
        /// </summary>
        /// <summary>
        /// The nearest a class may change mesh (m at the reference view and Detail 1: x off LOD 0, y off LOD 1), whatever
        /// its size. Trees only: a 7 m tree's reduced and far meshes are made for a distance, and by size alone it reached
        /// them at 25 m and 100 m, where its crown read as angular blobs and then flat discs.
        /// </summary>
        public static float2 FloraFloor(FloraClass c) => c == FloraClass.Tree ? new float2(47f, 190f) : float2.zero;

        public static Rule FloraRule(FloraClass c)
        {
            switch (c)
            {
                case FloraClass.Tree: return new Rule { Geometry = new Thresholds(395f, 98f, 0f), Shadow = 14f };
                case FloraClass.Plant: return new Rule { Geometry = new Thresholds(46f, 13f, 0f), Shadow = 45f };
                // Rocks only change mesh, and stay to the layer's edge.
                case FloraClass.Rock: return new Rule { Geometry = new Thresholds(120f, 32f, 0f), Shadow = 45f };
                // Logs, stumps, benches, eggs: gone once they're a few pixels.
                case FloraClass.Debris: return new Rule { Geometry = new Thresholds(70f, 18f, 12f), Shadow = 55f };
                // Built things hold their reduced mesh far out: their outline is what shows.
                default: return new Rule { Geometry = new Thresholds(295f, 75f, 0f), Shadow = 14f };
            }
        }
    }
}
