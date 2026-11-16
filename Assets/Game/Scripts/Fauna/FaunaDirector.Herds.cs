using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Baked herds becoming real animals (A-108 row 2). Herding species (HerdBaking.Herds) are no longer spawned one by
    /// one round a player; instead, when a player comes within reach of a herd's middle, its animals are made where the
    /// shared clock (HerdClock) has them, with the size and coat the far form draws, and when everyone has left they are
    /// taken away again and the herd goes back to being drawn only. Host and solo only: clients see the animals as ghosts,
    /// and work out the far form themselves.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>Each herding fauna entry's species blob, keyed biome * 256 + slot (as HerdRecord names them), for the far
        /// form on every machine; HerdBlobsVersion moves on whenever they're rebuilt or freed.</summary>
        public static readonly Dictionary<int, BlobAssetReference<CreatureBlob>> HerdBlobs = new Dictionary<int, BlobAssetReference<CreatureBlob>>();
        public static int HerdBlobsVersion;

        /// <summary>A herd's animals are made when a player comes this near its middle and taken away beyond the second
        /// (m; the far form crossfades with them by its own fade, CreatureRenderSystem.Herds).
        /// // gameplay: when a herd is simulated, not how it looks.</summary>
        public const float HerdLiveIn = 150f, HerdLiveOut = 175f;

        /// <summary>Which species (by index) live in herds.</summary>
        bool[] herding;
        /// <summary>The herds that are live now (by index in TerrainSource.Herds) and the animals made for them.</summary>
        readonly Dictionary<int, List<Entity>> liveHerds = new Dictionary<int, List<Entity>>();
        readonly List<int> herdsLeaving = new List<int>();
        /// <summary>How many herds have gone live since the start (probes).</summary>
        public static int HerdsWoken;

        void BuildHerdKinds()
        {
            HerdBlobs.Clear();
            herding = new bool[species.Count];
            for (int b = 0; b < biomes.Length; b++)
            {
                var fauna = biomes[b] != null ? biomes[b].Fauna : null;
                if (fauna == null)
                    continue;
                for (int s = 0; s < fauna.Count && s < 255; s++)
                {
                    var f = fauna[s];
                    if (f == null || f.Species == null || f.Density <= 0f || !HerdBaking.Herds(f.Species))
                        continue;
                    var index = SpeciesIndex(f.Species);
                    if (index < 0)
                        continue;
                    HerdBlobs[b * 256 + s] = blobs[index];
                    herding[index] = true;
                }
            }
            HerdBlobsVersion++;
        }

        void TeardownHerds()
        {
            liveHerds.Clear();
            HerdBlobs.Clear();
            herding = null;
            HerdBlobsVersion++;
        }

        /// <summary>A species the baked herds carry (so not spawned one by one), once the island has herds.</summary>
        bool Herding(int index, in TerrainSource source) =>
            source.Herds.IsCreated && herding != null && index >= 0 && index < herding.Length && herding[index];

        /// <summary>Wakes the herds a player has come near and lets go of those everyone has left (with the despawn tally).</summary>
        void UpdateHerds(in TerrainSource source)
        {
            if (!source.Herds.IsCreated || herding == null)
                return;
            ref var set = ref source.Herds.Value;
            var time = HerdTime.From(Cycle, simWorld.Time.ElapsedTime);
            var em = simWorld.EntityManager;
            herdsLeaving.Clear();
            foreach (var kv in liveHerds)
            {
                if (kv.Key >= set.Herds.Length || players.Count == 0)
                {
                    herdsLeaving.Add(kv.Key);
                    continue;
                }
                ref var h = ref set.Herds[kv.Key];
                if (!HerdBlobs.TryGetValue(h.Biome * 256 + h.Slot, out var blob) ||
                    NearestPlayer(HerdClock.Place(ref set, h, HerdShape.Of(ref blob.Value), time).Centre) > HerdLiveOut)
                    herdsLeaving.Add(kv.Key);
            }
            foreach (var i in herdsLeaving)
            {
                foreach (var e in liveHerds[i])
                    if (em.Exists(e) && em.HasComponent<CreatureState>(e) && em.GetComponentData<CreatureState>(e).Mode != CreatureMode.Held)
                        em.DestroyEntity(e);
                liveHerds.Remove(i);
            }
            if (players.Count == 0)
                return;
            for (int i = 0; i < set.Herds.Length; i++)
            {
                ref var h = ref set.Herds[i];
                if (liveHerds.ContainsKey(i) || NearestPlayer(h.Home) > h.Range + HerdLiveIn + 20f)
                    continue;
                var key = h.Biome * 256 + h.Slot;
                if (!HerdBlobs.TryGetValue(key, out var blob))
                    continue;
                var index = SpeciesIndex(biomes[h.Biome].Fauna[h.Slot].Species);
                if (index < 0)
                    continue;
                var shape = HerdShape.Of(ref blob.Value);
                var place = HerdClock.Place(ref set, h, shape, time);
                if (NearestPlayer(place.Centre) > HerdLiveIn)
                    continue;
                WakeHerd(source, i, h, index, shape, place, time);
            }
        }

        /// <summary>Makes a herd's animals where the clock has them, each with the size and coat its far form had.</summary>
        void WakeHerd(in TerrainSource source, int herd, in HerdRecord h, int index, in HerdShape shape, in HerdPlace place, in HerdTime time)
        {
            var members = new List<Entity>(h.Count);
            liveHerds[herd] = members;
            HerdsWoken++;
            var blob = blobs[index];
            for (int k = 0; k < h.Count; k++)
            {
                var xz = place.Centre + HerdClock.Offset(h, shape, k, time.T, place.Walking);
                var ground = source.SampleGround(xz);
                if (ground.HasWater && ground.WaterSurface > ground.Height - 0.3f)
                    continue;
                var scale = HerdClock.Scale(h, shape, k);
                var coat = HerdClock.Coat(ref blob.Value, h, k);
                var yaw = HerdClock.Yaw(h, place, k, time.T);
                Enqueue(index, new float3(xz.x, CreatureMotion.Ground(source, xz, ground.Height), xz.y), SpawnTier.Wild, e =>
                {
                    var em = simWorld.EntityManager;
                    var body = em.GetComponentData<CreatureBody>(e);
                    body.Coat = coat;
                    em.SetComponentData(e, body);
                    var state = em.GetComponentData<CreatureState>(e);
                    state.Scale = scale;
                    em.SetComponentData(e, state);
                    members.Add(e);
                }, yaw);
            }
        }
    }
}
