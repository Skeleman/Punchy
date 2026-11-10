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
            /// <summary>Casts a shadow while bigger than this (reference pixels); below it, none.</summary>
            public float Shadow;
            /// <summary>
            /// Shadow tiers (3.7 step 3): where a cached sun-shadow map stands in past the real-time cascades (the trees
            /// layer's far cache), the real-time tier holds only while bigger than this; between it and Shadow the cached
            /// map. float.MaxValue: never past the cascades' own floor (FloraShadowOptions.NearRange). Without a cached
            /// map (creatures, small flora), Shadow alone decides.
            /// </summary>
            public float SharpShadow;
            /// <summary>
            /// R-5: past the real-time cascades and the cached map, the wide sun-shadow map (Rendering.WideShadow, about
            /// 1.6 km round the player) keeps a caster's shadow while it's bigger than this (reference pixels, at most
            /// Shadow); below it, none. 0: no wide tier (Shadow alone ends it). Anything still big on screen from afar
            /// qualifies by its size alone: a 50 m colossus to about 1.5 km at 30, a 10 m house to about 300 m.
            /// </summary>
            public float WideShadow;
            /// <summary>How much a fast mover counts as bigger for Animation only (its motion shows before its shape
            /// does): pixels x (1 + Activity x how active, 0..1).</summary>
            public float Activity;
            /// <summary>
            /// R-7: past its coarsest mesh, an object with an impostor (FloraSystem.Impostors.cs) is drawn as one while
            /// smaller than this (reference pixels), with hysteresis. 0: no impostor tier.
            /// </summary>
            public float Impostor;
        }

        /// <summary>
        /// The one shadow tier decision (3.7 step 3, R-5): 0 the real-time cascades, 1 the cached map, 2 the wide map,
        /// 3 none, with hysteresis (previous -1 when first seen). Pixels as <see cref="ScreenSize"/>; shadows is the
        /// Shadows setting. FloraCull.compute does the same per plant through <see cref="SharpReach"/> and
        /// <see cref="WideReach"/>.
        /// </summary>
        public static int ShadowTier(float pixels, in Rule rule, float shadows, int previous)
        {
            var q = math.max(shadows, 0.1f);
            // (Without a wide tier Far = Mid, so tier 2 can't happen and 3 is "none" as before.)
            var wide = rule.WideShadow > 0f ? math.min(rule.WideShadow, rule.Shadow) : rule.Shadow;
            return Tier(pixels, new Thresholds(rule.SharpShadow, rule.Shadow, wide).Scaled(q), previous);
        }

        /// <summary>R-5: a size's wide-map shadow reach per reference pixel-per-metre (0: the rule has no wide tier).</summary>
        public static float WideReach(in Rule rule, float size) => rule.WideShadow > 0f ? size / rule.WideShadow : 0f;

        /// <summary>A size's real-time shadow reach per reference pixel-per-metre (0: the rule has no size-based one, so
        /// only the cascades' floor applies).</summary>
        public static float SharpReach(in Rule rule, float size) => rule.SharpShadow >= float.MaxValue ? 0f : size / rule.SharpShadow;

        /// <summary>R-7: where a size hands over to its impostor, per reference pixel-per-metre (0: the rule has none).</summary>
        public static float ImpostorReach(in Rule rule, float size) => rule.Impostor > 0f ? size / rule.Impostor : 0f;

        /// <summary>R-7: whether the rule has an impostor tier at all (only those models are baked one).</summary>
        public static bool HasImpostor(in Rule rule) => rule.Impostor > 0f;

        /// <summary>The four answers for one object, packed in a byte (geometry 2 bits, animation 2, shadow 1).</summary>
        public struct Decision
        {
            public byte Packed;
            public int Geometry => Packed & 3;
            public int Animation => (Packed >> 2) & 3;
            public bool Shadow => (Packed & 16) != 0;
            /// <summary>A-97, creatures: the fur tier (0 long fur shown, 1 medium at most, 2 short only and no shells)
            /// in bits 5-6, and whether the feet are fitted to the ground in bit 7. (A real decision tops out at 223, so
            /// never reads as None.)</summary>
            public int Fur => (Packed >> 5) & 3;
            public bool Feet => (Packed & 128) != 0;
            public Decision With(int fur, bool feet)
                => new Decision { Packed = (byte)((Packed & 31) | math.min(fur, 2) << 5 | (feet ? 128 : 0)) };
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
            SharpShadow = 14f,
            Activity = 0.5f
        };

        /// <summary>
        /// A-97: a creature's fur by how big the fur itself looks (tiers as Decision.Fur). Its strands and shells are the
        /// same few millimetres on a squirrel as on a bear, so the size given is 1 m (CreatureFurSize), not the body:
        /// long fur within 5 m and shells within 7 m (A-92's fixed range) at the reference view, nearer at a lower
        /// Detail, further through a zoom.
        /// </summary>
        public static readonly Thresholds CreatureFur = new Thresholds(187.1f, 133.6f, 0f);
        public const float CreatureFurSize = 1f;

        /// <summary>A-97: a creature's feet fitted to the ground (each foot reaching for it, tipped with it) while it looks
        /// at least this big, reference pixels: a wolf (0.92 m) inside 40 m at the reference view, as the old fixed range.</summary>
        public const float CreatureFeetPixels = 21.5f;

        /// <summary>
        /// R-5: any shadow-casting mesh in the scene that isn't flora or a creature the fauna system draws (the colossi,
        /// the big landmarks' meshes, props): only its shadow tier is read, by Rendering.WideShadow, which lays every
        /// caster in tiers 0-2 into the wide map. These have no cached map of their own, so Shadow only has to sit above
        /// WideShadow for the wide tier to exist; WideShadow is the one number that decides how far from the player a
        /// huge thing keeps a shadow (trees and built flora share it, through WideReach on the GPU).
        /// </summary>
        public static readonly Rule Huge = new Rule
        {
            Geometry = new Thresholds(295f, 75f, 0f),
            Animation = new Thresholds(295f, 75f, 0f),
            Shadow = 60f,
            SharpShadow = 400f,
            WideShadow = 30f
        };

        /// <summary>
        /// R-6 row 1: a big single mesh's level (0-2: the landmarks' own meshes) by the Huge rule's geometry boundaries,
        /// from last frame's (-1 when first seen), at the current view (<see cref="SetView"/> first). Size is the whole
        /// mesh's bounding diameter (m) and distance from the camera to its nearest point, so the ribcage, the basalt
        /// tower and the sunken temple change where anything else their size would.
        /// </summary>
        public static int LandmarkLod(float size, float distance, int previous)
        {
            var view = Current.Data;
            var pixels = ScreenSize(size, distance, view.PixelsPerMetre);
            return math.min(2, Tier(pixels, Huge.Geometry.Scaled(math.max(view.Detail, 0.1f)), previous));
        }

        /// <summary>A landmark mesh's switch distances (m: off LOD 0, off LOD 1) at the reference view and Detail 1, for logs.</summary>
        public static float2 LandmarkReach(float size)
        {
            var perMetre = PixelsPerMetreAt(ReferenceFov);
            return new float2(Reach(size, Huge.Geometry.Near, perMetre), Reach(size, Huge.Geometry.Mid, perMetre));
        }

        /// <summary>The simulation's (brain LOD) boundary for creatures, reference pixels: a deer thinks every frame
        /// inside 80 m of a player (A-54), something 10x its size inside 800 m.</summary>
        public const float CreatureThinkPixels = 17.5f;

        /// <summary>
        /// Ground detail (R-2, GroundDetailSystem): how far grass and pebbles draw and where grass starts thinning, by
        /// how big one looks rather than by fixed distances. Sizes in m, boundaries in reference pixels at Detail 1, tuned
        /// so the 60 degree view gets the old ranges (grass 60 m, thinning from 20 m, pebbles 45 m, x Detail).
        /// </summary>
        public const float GrassSize = 0.6f, PebbleSize = 0.2f, GrassEndPixels = 9.353f, GrassFullPixels = 28.06f, PebbleEndPixels = 4.157f;
        /// <summary>The most a narrow view may stretch ground detail: tiles are built round the camera, so a zoom
        /// mustn't ask for a much bigger area.</summary>
        const float GroundMaxStretch = 1.5f;

        /// <summary>How far something this small keeps this many reference pixels at the current view (Detail included),
        /// for detail that fades continuously, with no tiers to hold (<see cref="SetView"/> first).</summary>
        public static float GroundReach(float size, float pixels)
        {
            var view = Current.Data;
            var perMetre = math.min(view.PixelsPerMetre, PixelsPerMetreAt(ReferenceFov) * GroundMaxStretch);
            return Reach(size, pixels / math.max(view.Detail, 0.1f), perMetre);
        }

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

        /// <summary>
        /// R-6 row 4: a class drawn always (Far 0) is drawn past its layer's radius while it is at least this many pixels
        /// across, out to the terrain edge: a layer's fixed radius no longer cuts a tall tree or a big building off before
        /// its size says so (FloraSystem streams those far plants on their own). R-6 row 6: the tree layer only.
        /// </summary>
        public const float FloraEdgePixels = 4f;

        public static Rule FloraRule(FloraClass c)
        {
            switch (c)
            {
                // (SharpShadow, 3.7 step 3: a 20 m tree keeps the old 25 m real-time range at High, so a forest casts no
                // more; only giants reach past it. Built things and big rocks reach out with their size: at High a 12 m
                // hall casts sharp to about 40 m, past where a bush stops, and a 20 m one to the cascades' end.)
                case FloraClass.Tree: return new Rule { Geometry = new Thresholds(395f, 98f, 0f), Shadow = 14f, SharpShadow = 1050f, WideShadow = Huge.WideShadow, Impostor = 30f };
                case FloraClass.Plant: return new Rule { Geometry = new Thresholds(46f, 13f, 0f), Shadow = 45f, SharpShadow = float.MaxValue, Impostor = 9f };
                // Rocks only change mesh, and stay to the layer's edge. No impostor: a rock's coarsest mesh is already
                // cheaper than the impostor quad (R-11 measured +0.1-0.25 ms at High in a mesa, for 110 MB more atlas).
                case FloraClass.Rock: return new Rule { Geometry = new Thresholds(120f, 32f, 0f), Shadow = 45f, SharpShadow = 400f };
                // Logs, stumps, benches, eggs: gone once they're a few pixels.
                case FloraClass.Debris: return new Rule { Geometry = new Thresholds(70f, 18f, 12f), Shadow = 55f, SharpShadow = float.MaxValue };
                // Built things hold their reduced mesh far out: their outline is what shows.
                default: return new Rule { Geometry = new Thresholds(295f, 75f, 0f), Shadow = 14f, SharpShadow = 400f, WideShadow = Huge.WideShadow };
            }
        }
    }
}
