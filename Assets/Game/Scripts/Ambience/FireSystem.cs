using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>
    /// Flames anyone can light for a frame: a torch carried by a guard, a cave torch on a wall, a campfire. Callers add
    /// each flame every frame (position of its base, height, a stable seed, a palette, and how far its tip trails); the
    /// LandmarkEffectSystem draws them all at once with the Flame shader, which shapes and flickers them on the shared
    /// clock, and the list is cleared. A flame's lean is eased between frames by its seed, so a jerky carrier doesn't
    /// snap it about. Every flame is the standard fire (#131, Resources/StandardFire, a FireEffect): its flame, a spark
    /// now and then (the embers), a soft glow round its base and two puffs of smoke, in one of the effect's colours.
    /// The light it throws is the caller's to add (CaveSystem.MovingLights), coloured by Light(colour).
    /// Refactor 3.2: drawn from a buffer of flame instances (48 bytes each) with a fixed index buffer, two procedural
    /// draws (near flames' five quads, far flames' two); the Flame shader builds each corner from the vertex id. It used
    /// to rebuild and upload a 20-vertex, 30-index mesh piece per flame every frame on the main thread.
    /// </summary>
    public static class FireSystem
    {
        /// <summary>Colours (indices into the standard fire's colours): 0 wood fire, 1 blue, 2 green, 3 violet.</summary>
        public const int Wood = 0, Blue = 1, Green = 2, Violet = 3;

        /// <summary>Debug (F4): every fire burns in this colour of the standard fire; -1 for each its own.</summary>
        public static int ColourOverride = -1;

        /// <summary>Quads per flame: the flame, its spark, its glow and two smoke puffs (Corner.w = colour + 10 x quad).</summary>
        const int Quads = 5;
        /// <summary>A far flame's second quad (W-112): a broad, bright glow card standing in for its light.</summary>
        const int FarGlowQuad = 5;

        static FireEffect effect;
        static readonly Vector4[] colourTable = new Vector4[FireEffect.MaxColours * 3];
        static readonly int ColoursId = Shader.PropertyToID("_PunchyFireColours");
        static readonly int GlowId = Shader.PropertyToID("_PunchyFireGlow");
        static readonly int EmberId = Shader.PropertyToID("_PunchyFireEmbers");
        static readonly int SmokeId = Shader.PropertyToID("_PunchyFireSmoke");
        static readonly int SmokeColourId = Shader.PropertyToID("_PunchyFireSmokeColour");

        /// <summary>The standard fire (Resources/StandardFire), loaded on first use.</summary>
        public static FireEffect Effect => effect != null ? effect : effect = Resources.Load<FireEffect>("StandardFire");

        /// <summary>The colour a flame of <paramref name="colour"/> burns in (after the debug override), in range.</summary>
        static int Colour(int colour)
        {
            var e = Effect;
            var count = e != null ? Mathf.Min(e.Colours.Length, FireEffect.MaxColours) : 1;
            var c = ColourOverride >= 0 ? ColourOverride : colour;
            return c >= 0 && c < count ? c : 0;
        }

        /// <summary>
        /// The light a fire of <paramref name="colour"/> throws, for CaveSystem.MovingLights: rgb x intensity x the
        /// caller's <paramref name="flicker"/>, w its reach (m).
        /// </summary>
        public static Vector4 Light(int colour, float flicker = 1f)
        {
            var e = Effect;
            if (e == null || e.Colours.Length == 0)
                return new Vector4(1.7f * flicker, 0.95f * flicker, 0.4f * flicker, 9f);
            var c = (Vector4)e.Colours[Colour(colour)].Light * (e.LightIntensity * flicker);
            return new Vector4(c.x, c.y, c.z, e.LightReach);
        }

        struct Flame
        {
            public Vector3 Position, Lean;
            public float Size;
            public int Seed, Palette;
            public bool Far;
        }

        /// <summary>One flame for the Flame shader (FlameInstance there): base and height, lean and seed, colour.</summary>
        struct FlameInstance
        {
            public float4 PositionSize;   // xyz base (world), w height (m)
            public float4 LeanSeed;       // xyz lean at the tip (world m), w seed 0..1
            public float4 Palette;        // x colour index

            public const int Stride = 48;
        }

        static readonly List<Flame> flames = new List<Flame>();
        static Dictionary<int, Vector3> eased = new Dictionary<int, Vector3>(), easedNext = new Dictionary<int, Vector3>();
        // This frame's instances: near flames from the front, far ones from the back, uploaded in one SetData.
        static readonly FlameInstance[] instanceData = new FlameInstance[MaxFlames];
        static GraphicsBuffer instances, quadIndices;
        static MaterialPropertyBlock nearProps, farProps;
        static readonly int InstancesId = Shader.PropertyToID("_FlameInstances");
        static readonly int BaseId = Shader.PropertyToID("_FlameBase");
        static readonly int FarId = Shader.PropertyToID("_FlameFar");
        /// <summary>Flames dropped because the cap was full, since the start (refactor 3.2: they used to go silently).</summary>
        public static int Dropped;

        /// <summary>A flame this frame. Main thread only; add it again every frame it burns.</summary>
        /// <param name="position">The base of the flame (a torch head, the logs of a fire).</param>
        /// <param name="size">Its height (m): a torch about 0.35, a campfire 0.8–1.2.</param>
        /// <param name="seed">Anything that stays the same while it burns (an entity index), so its flicker is its own.</param>
        /// <param name="palette">The standard fire's colour: Wood, Blue, Green, Violet or a later one.</param>
        public static void AddFlame(Vector3 position, float size, int seed, int palette = Wood) =>
            AddFlame(position, size, seed, palette, Vector3.zero);

        /// <param name="lean">Where the tip trails, in world metres (added to the wind's lean): e.g. a carrier's
        /// velocity × -0.05, clamped to about 0.3. Eased between frames.</param>
        public static void AddFlame(Vector3 position, float size, int seed, int palette, Vector3 lean)
        {
            if (flames.Count < MaxFlames)
                flames.Add(new Flame { Position = position, Size = size, Seed = seed, Palette = Colour(palette), Lean = lean });
            else
                Dropped++;
        }

        const int MaxFlames = 8192;

        /// <summary>
        /// W-112: a far flame this frame, the level of detail for fire seen from far off (a burning forest across a
        /// valley): only the flame itself, no sparks or smoke, and a broad glow card round it that stands in for the
        /// light it would throw (no real light). Two quads instead of five, and never eased.
        /// </summary>
        public static void AddFarFlame(Vector3 position, float size, int seed, int palette = Wood)
        {
            if (flames.Count < MaxFlames)
                flames.Add(new Flame { Position = position, Size = size, Seed = seed, Palette = Colour(palette), Far = true });
            else
                Dropped++;
        }

        /// <summary>
        /// The colour embers glow in a fire of <paramref name="colour"/> (W-112: the Ember Forest's bark seams and
        /// leaf slivers take its night's colour): the standard wood-fire ember for wood, otherwise the fire's middle
        /// colour. w is 1 so the shaders know it has been set.
        /// </summary>
        public static Vector4 EmberTint(int colour)
        {
            var e = Effect;
            var c = Colour(colour);
            if (c == Wood || e == null || e.Colours.Length == 0)
                return new Vector4(1f, 0.36f, 0.08f, 1f);
            var mid = e.Colours[c].Mid;
            return new Vector4(mid.r, mid.g, mid.b, 1f);
        }

        /// <summary>Draws this frame's flames and clears the list (LandmarkEffectSystem, once a frame).</summary>
        public static void Draw(Material material)
        {
            if (flames.Count == 0 || material == null)
            {
                flames.Clear();
                eased.Clear();
                return;
            }
            Upload();
            Buffers();
            var dt = math.min(Time.deltaTime, 0.1f);
            var follow = 1f - math.exp(-dt / 0.12f);
            easedNext.Clear();
            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            // Near flames from the front of the array, far ones from the back, so each draw reads one run.
            int near = 0, far = flames.Count;
            foreach (var f in flames)
            {
                var lean = Vector3.zero;
                if (!f.Far)
                {
                    lean = eased.TryGetValue(f.Seed, out var was) ? Vector3.Lerp(was, f.Lean, follow) : f.Lean;
                    easedNext[f.Seed] = lean;
                }
                var seed01 = (math.hash(new int2(f.Seed, 0x5EED)) & 0xFFFF) / 65535f;
                var instance = new FlameInstance
                {
                    PositionSize = new float4(f.Position.x, f.Position.y, f.Position.z, f.Size),
                    LeanSeed = new float4(lean.x, lean.y, lean.z, seed01),
                    Palette = new float4(f.Palette, 0f, 0f, 0f)
                };
                instanceData[f.Far ? --far : near++] = instance;
                min = math.min(min, (float3)f.Position);
                max = math.max(max, (float3)f.Position);
            }
            (eased, easedNext) = (easedNext, eased);
            var count = flames.Count;
            flames.Clear();
            instances.SetData(instanceData, 0, 0, count);

            // Bounds round every base, with room for the tallest flame, its sparks and its smoke.
            var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min + 12f));
            if (near > 0)
            {
                nearProps.SetInt(BaseId, 0);
                nearProps.SetFloat(FarId, 0f);
                Graphics.RenderPrimitivesIndexed(Params(material, bounds, nearProps), MeshTopology.Triangles, quadIndices, Quads * 6, 0, near);
            }
            if (far < count)
            {
                farProps.SetInt(BaseId, far);
                farProps.SetFloat(FarId, 1f);
                Graphics.RenderPrimitivesIndexed(Params(material, bounds, farProps), MeshTopology.Triangles, quadIndices, 2 * 6, 0, count - far);
            }
        }

        static RenderParams Params(Material material, Bounds bounds, MaterialPropertyBlock props) =>
            new RenderParams(material) { worldBounds = bounds, matProps = props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };

        /// <summary>The instance buffer (room for every flame) and the fixed index buffer (five quads), made once.</summary>
        static void Buffers()
        {
            if (instances != null && instances.IsValid())
                return;
            instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxFlames, FlameInstance.Stride);
            quadIndices = new GraphicsBuffer(GraphicsBuffer.Target.Index, Quads * 6, sizeof(int));
            var idx = new int[Quads * 6];
            for (int q = 0; q < Quads; q++)
            {
                var first = q * 4;
                idx[q * 6] = first; idx[q * 6 + 1] = first + 1; idx[q * 6 + 2] = first + 2;
                idx[q * 6 + 3] = first; idx[q * 6 + 4] = first + 2; idx[q * 6 + 5] = first + 3;
            }
            quadIndices.SetData(idx);
            nearProps = new MaterialPropertyBlock();
            farProps = new MaterialPropertyBlock();
            nearProps.SetBuffer(InstancesId, instances);
            farProps.SetBuffer(InstancesId, instances);
            Application.quitting += Release;
        }

        static void Release()
        {
            instances?.Release();
            quadIndices?.Release();
            instances = quadIndices = null;
        }

        /// <summary>The standard fire's colours and settings, for the Flame shader (globals: a few floats a frame).</summary>
        static void Upload()
        {
            var e = Effect;
            if (e == null || e.Colours.Length == 0)
                return;
            var count = Mathf.Min(e.Colours.Length, FireEffect.MaxColours);
            for (int i = 0; i < FireEffect.MaxColours; i++)
            {
                var c = e.Colours[i < count ? i : 0];
                colourTable[i * 3] = (Vector4)c.Outer;
                colourTable[i * 3 + 1] = (Vector4)c.Mid;
                colourTable[i * 3 + 2] = (Vector4)c.Core;
            }
            Shader.SetGlobalVectorArray(ColoursId, colourTable);
            Shader.SetGlobalVector(GlowId, new Vector4(e.GlowSize, e.GlowStrength, 0f, 0f));
            Shader.SetGlobalVector(EmberId, new Vector4(e.EmberChance, e.EmberRate, e.EmberRise, 0f));
            Shader.SetGlobalVector(SmokeId, new Vector4(e.SmokeStrength, e.SmokeRise, e.SmokeRate, 0f));
            Shader.SetGlobalVector(SmokeColourId, (Vector4)e.SmokeColour);
        }
    }
}
