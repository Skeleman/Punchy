using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Rendering.Effects
{
    /// <summary>
    /// R-29 row 3 (#320, rule 30): steam and smoke, two families of rising puffs on the effects kit. Each family is one
    /// asset (PlumeEffect: Resources/StandardSteam, Resources/StandardSmoke) and one draw path: callers hand it their
    /// plumes whenever their own set changes (Set, keyed by the caller) and EffectsDriver draws every family once a
    /// frame in one procedural draw with the Landmark Plume shader, which animates every puff from the shared clock
    /// and the plume's seed (no simulation, the same on every machine). Fade by screen size (rule 20): each plume goes
    /// below its family's GonePixels (LandmarkEffects.hlsl, _PunchyPlumeReach).
    /// Callers: LandmarkEffectSystem (steam: geysers, hot springs, mud pits; smoke: chimneys, fumaroles and other dark
    /// landmark plumes). Still on their own path: FireSystem's smoke over flames, waterfall mist and spray (the water
    /// family), glowing or coloured plumes (embers, sparkles, toxic gas).
    /// </summary>
    public static class PlumeSystem
    {
        /// <summary>One plume. Must match LandmarkEffects.hlsl (LandmarkEmitter); the waterfall fields stay zero.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Plume
        {
            public float3 Position;
            public float Seed;
            public float4 Color;    // rgb (linear), glow
            public float4 Shape;    // rise, width, puff size, lifetime
            public float4 Timing;   // period, duty, opacity, particles
            public float2 Out;
            public float Drop;
            public float UnderRock; // 1 when lit as under rock
            public float4 Fall;
        }

        public static readonly PlumeFamily Steam = new PlumeFamily("StandardSteam");
        public static readonly PlumeFamily Smoke = new PlumeFamily("StandardSmoke");

        /// <summary>
        /// Which family a landmark-style plume of this authored (sRGB) colour and glow belongs to: pale grey-white is
        /// steam, dark grey is smoke, and anything glowing or coloured (embers, sparkles, toxic gas) is neither (null).
        /// </summary>
        public static PlumeFamily FamilyOf(Color authored, float glow)
        {
            if (glow > 0.05f)
                return null;
            var hi = math.max(authored.r, math.max(authored.g, authored.b));
            var lo = math.min(authored.r, math.min(authored.g, authored.b));
            if (hi - lo > 0.12f)
                return null;
            return hi >= 0.6f ? Steam : hi <= 0.55f ? Smoke : null;
        }

        /// <summary>Called by EffectsDriver once a frame.</summary>
        internal static void Draw(Material material)
        {
            Steam.Draw(material);
            Smoke.Draw(material);
        }
    }

    /// <summary>One family of plumes (steam or smoke): its asset, every caller's plumes, one buffer and one draw.</summary>
    public sealed class PlumeFamily
    {
        static readonly int EmittersId = Shader.PropertyToID("_LandmarkEmitters");
        static readonly int MaxParticlesId = Shader.PropertyToID("_MaxParticles");
        static readonly int PlumeReachId = Shader.PropertyToID("_PunchyPlumeReach");

        readonly string assetName;
        PlumeEffect effect;
        readonly Dictionary<object, List<PlumeSystem.Plume>> sets = new Dictionary<object, List<PlumeSystem.Plume>>();
        readonly List<PlumeSystem.Plume> all = new List<PlumeSystem.Plume>();
        GraphicsBuffer buffer;
        MaterialPropertyBlock props;
        Bounds bounds;
        int maxParticles = 1;
        bool dirty;

        internal PlumeFamily(string assetName) { this.assetName = assetName; }

        /// <summary>The family's asset (Resources), loaded on first use.</summary>
        public PlumeEffect Effect => effect != null ? effect : effect = Resources.Load<PlumeEffect>(assetName);

        /// <summary>Plumes drawn now, and how many callers hand them in (for probes and the R-29 counts).</summary>
        public int Count => all.Count;
        public int Callers => sets.Count;

        /// <summary>
        /// Replaces <paramref name="owner"/>'s plumes with <paramref name="plumes"/> (copied; an empty list or null
        /// removes them). Call it when the caller's own set changes, not every frame. Main thread only.
        /// </summary>
        public void Set(object owner, List<PlumeSystem.Plume> plumes)
        {
            if (plumes == null || plumes.Count == 0)
            {
                dirty |= sets.Remove(owner);
                return;
            }
            if (!sets.TryGetValue(owner, out var list))
                sets[owner] = list = new List<PlumeSystem.Plume>();
            list.Clear();
            list.AddRange(plumes);
            dirty = true;
        }

        /// <summary>Re-applies the asset (after an edit in Play mode).</summary>
        public void Refresh() => dirty = true;

        void Rebuild()
        {
            dirty = false;
            all.Clear();
            maxParticles = 1;
            var e = Effect;
            var tint = e != null ? (Vector4)e.Tint.linear : Vector4.one;
            var opacity = e != null ? e.Opacity : 1f;
            var size = e != null ? e.Size : 1f;
            var rise = e != null ? e.Rise : 1f;
            float3 min = float.MaxValue, max = float.MinValue;
            foreach (var list in sets.Values)
                foreach (var source in list)
                {
                    var p = source;
                    p.Color.xyz *= new float3(tint.x, tint.y, tint.z);
                    p.Shape.x *= rise;
                    p.Shape.z *= size;
                    p.Timing.z *= opacity;
                    all.Add(p);
                    maxParticles = math.max(maxParticles, (int)p.Timing.w);
                    // Room for the puffs to spread and drift downwind as they rise.
                    var pad = p.Shape.y + p.Shape.z + 6f;
                    var drift = p.Shape.x + pad;
                    min = math.min(min, p.Position - new float3(drift, pad, drift));
                    max = math.max(max, p.Position + new float3(drift, p.Shape.x + pad, drift));
                }
            if (all.Count == 0)
                return;
            bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));
            if (buffer == null || !buffer.IsValid() || buffer.count < all.Count)
            {
                if (buffer == null)
                    Application.quitting += Release;
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.ceilpow2(all.Count), Marshal.SizeOf<PlumeSystem.Plume>());
            }
            buffer.SetData(all);
            props ??= new MaterialPropertyBlock();
            props.SetBuffer(EmittersId, buffer);
        }

        internal void Draw(Material material)
        {
            if (dirty)
                Rebuild();
            if (all.Count == 0 || material == null)
                return;
            var e = Effect;
            props.SetInteger(MaxParticlesId, maxParticles);
            props.SetFloat(PlumeReachId, Punchy.Settings.DetailPolicy.ExtraReach(1f, e != null ? e.GonePixels : 16.8f));
            var rp = new RenderParams(material) { worldBounds = bounds, matProps = props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, all.Count * maxParticles);
        }

        void Release()
        {
            buffer?.Release();
            buffer = null;
        }
    }
}
