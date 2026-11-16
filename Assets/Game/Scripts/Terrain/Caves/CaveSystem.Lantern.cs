using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// The cave lantern (moved out of CaveSystem.cs for P-37 row 2): a warm light at someone's head while they're in a
    /// cave, now only while they hold a light item (a torch or lantern; Zach 11-14: characters don't glow in the dark).
    /// Other players' held items come from the gear the host already sends (Items.HeldLight by network id), so
    /// nothing new is synced.
    /// </summary>
    public partial class CaveSystem
    {
        [Header("Lantern")]
        public Color LanternColor = new Color(1f, 0.78f, 0.5f);
        public float LanternIntensity = 6f;
        public float LanternRange = 15f;

        Light lantern;
        float lanternLevel;

        // Other players carry lanterns too (lit from where their bodies are, while the gear the host sent puts a light in
        // their hands).
        class OtherLantern { public Light Light; public float Level; public bool Seen; }
        readonly Dictionary<Entity, OtherLantern> otherLanterns = new Dictionary<Entity, OtherLantern>();
        readonly List<Entity> goneLanterns = new List<Entity>();
        EntityQuery othersQuery;

        void UpdateLantern(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingleton<Unity.Transforms.LocalTransform>(out var player))
                return;
            var wanted = Punchy.Items.Items.OwnHeldLight > 0f ? LanternWanted(source, player.Position) : 0f;
            lanternLevel = Mathf.MoveTowards(lanternLevel, wanted, Time.deltaTime * 1.2f);
            if (lantern == null)
                lantern = CreateLantern("Lantern");
            PlaceLantern(lantern, player, lanternLevel, 0.5f, LocalLanternId);
            // The flora shader has no light loop (it would cost every plant on the surface), so cave plants get the
            // lantern as one cheap term from these.
            var at = lantern.transform.position;
            Shader.SetGlobalVector(LanternId, new Vector4(at.x, at.y, at.z, lantern.enabled ? lantern.intensity * 0.2f : 0f));
            Shader.SetGlobalVector(LanternColorId, new Vector4(LanternColor.r, LanternColor.g, LanternColor.b, LanternRange));
        }

        static readonly int LanternId = Shader.PropertyToID("_PunchyLantern");
        /// <summary>The local player's lantern's id in LightBudget (other players' are their entity's index).</summary>
        const int LocalLanternId = -1;
        static readonly int LanternColorId = Shader.PropertyToID("_PunchyLanternColor");

        /// <summary>How lit someone's lantern should be: in a cave and away from the daylight at the entrance (the callers
        /// also check that a light item is held).</summary>
        static float LanternWanted(in TerrainSource source, float3 feet)
        {
            source.FloorUnder(feet, out var inCave);
            var depth = source.SampleHeight(feet.xz) - feet.y;
            if (!inCave)
                return 0f;
            return math.saturate((depth - 1f) / 3f);
        }

        Light CreateLantern(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>
        /// Places someone's lantern and asks LightBudget for its real light (refactor 3.1): it's lit as far as the budget
        /// grants (fading), and when it loses the budget the same light goes in a glow slot instead (about as bright,
        /// a little shorter: the glow's fall-off is squared).
        /// </summary>
        void PlaceLantern(Light light, in Unity.Transforms.LocalTransform holder, float level, float flickerSeed, int id)
        {
            light.color = LanternColor;
            light.range = LanternRange;
            var flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, flickerSeed);
            var forward = math.mul(holder.Rotation, new float3(0f, 0f, 1f));
            var at = holder.Position + new float3(0f, 1.6f, 0f) + forward * 0.4f;
            light.transform.position = at;
            if (level > 0.01f)
            {
                var glow = (Vector4)LanternColor * (LanternIntensity * 0.27f * level * flicker);
                glow.w = LanternRange * 0.7f;
                Punchy.Settings.LightBudget.AddReal(Punchy.Settings.LightKind.Carried, id, at, glow);
            }
            var real = level * Punchy.Settings.LightBudget.RealLevel(Punchy.Settings.LightKind.Carried, id);
            light.enabled = real > 0.01f;
            light.intensity = LanternIntensity * real * flicker;
        }

        void UpdateOtherLanterns(in TerrainSource source, Vector3 camera)
        {
            if (othersQuery.IsEmpty && otherLanterns.Count == 0)
                return;
            foreach (var l in otherLanterns.Values)
                l.Seen = false;
            var em = queryWorld.EntityManager;
            var entities = othersQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                if (em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e))
                    continue;
                var t = em.GetComponentData<Unity.Transforms.LocalTransform>(e);
                // (No range of its own: LightBudget caps how many are real lights; the rest glow or go out.)
                if (math.lengthsq(t.Position) < 1e-6f)
                    continue;
                if (!otherLanterns.TryGetValue(e, out var l))
                    otherLanterns[e] = l = new OtherLantern { Light = CreateLantern("Lantern (other player)") };
                l.Seen = true;
                var held = em.HasComponent<GhostOwner>(e) ? Punchy.Items.Items.HeldLight(em.GetComponentData<GhostOwner>(e).NetworkId) : 0f;
                l.Level = Mathf.MoveTowards(l.Level, held > 0f ? LanternWanted(source, t.Position) : 0f, Time.deltaTime * 1.2f);
                PlaceLantern(l.Light, t, l.Level, 1.5f + (e.Index % 7), e.Index);
            }
            entities.Dispose();
            goneLanterns.Clear();
            foreach (var pair in otherLanterns)
                if (!pair.Value.Seen)
                    goneLanterns.Add(pair.Key);
            foreach (var e in goneLanterns)
            {
                if (otherLanterns[e].Light != null) Destroy(otherLanterns[e].Light.gameObject);
                otherLanterns.Remove(e);
            }
        }
    }
}
