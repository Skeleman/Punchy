using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-120 row 1: household pets. Some houses keep an animal, picked by the kind of home and where it stands: a cat or
    /// a dog in a town, a hen or a goat at an outlying farmstead, a lizard by a desert hut, a fox at an igloo, a frog at
    /// a sea dome, a beetle with the dwarves. Which house keeps one, what kind, and the pet's Individual (so its name)
    /// all come from the household's key, so every machine agrees with nothing sent, and talk can name it ("our cat
    /// Moss"). When a house's landmark is peopled the pet is spawned by its bed beside the door, leashed there;
    /// HouseholdPetSystem walks it back to sleep there at night and now and then has it follow one of its folk about.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>How far a household pet strays from its bed by day (m).</summary>
        public const float PetLeash = 6f;

        /// <summary>Of each kind of home, how many in 100 keep a pet.</summary>
        static int PetChance(string kind) => kind switch
        {
            "hut" => 70,
            "stable" => 50,
            "igloo" or "dome" => 40,
            _ => 55
        };

        static bool NameHas(string name, params string[] parts)
        {
            if (name == null)
                return false;
            foreach (var p in parts)
                if (name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Gives each household its pet, if it keeps one, from its key, its kind, its landmark and the biome at its
        /// door. Runs inside EnsureHouses, on every machine that builds the households.</summary>
        void AssignPets(in TerrainSource source, List<Household> list)
        {
            ref var lm = ref source.Landmarks.Value;
            foreach (var h in list)
            {
                h.Pet = h.PetWord = null;
                h.PetIndividual = 0;
                if (BiomeSampling.Scramble(h.Key ^ 0x50455430u) % 100u >= (uint)PetChance(h.Kind))
                    continue;
                var pick = BiomeSampling.Scramble(h.Key ^ 0x50455431u) % 100u;
                var inst = h.Landmark >= 0 && h.Landmark < lm.Instances.Length ? lm.Instances[h.Landmark] : default;
                var type = h.Landmark >= 0 && h.Landmark < lm.Instances.Length && inst.Type < landmarks.Length ? landmarks[inst.Type] : null;
                var typeName = type != null ? type.name : null;
                var b = source.SampleGround(h.Door.xz).Biomes.Dominant();
                var def = biomes != null && b >= 0 && b < biomes.Length ? biomes[b] : null;
                var biome = def != null ? def.name : null;
                var family = def != null ? def.Family : Punchy.Terrain.BiomeFamily.None;
                // (An outlying town house, past two thirds of its landmark's reach: a farmstead.)
                var outlying = h.Kind == "house" && type != null &&
                               math.distance(h.Door.xz, inst.Position) > 0.65f * lm.Types[inst.Type].Radius * inst.Scale;
                string pet, word;
                if (h.Kind == "stable") { pet = "Dog"; word = "dog"; }
                else if (h.Kind == "igloo") { pet = "ArcticFox"; word = "fox"; }
                else if (h.Kind == "dome") { pet = "Frog"; word = "frog"; }
                else if (family == Punchy.Terrain.BiomeFamily.Arid)
                {
                    if (pick < 50) { pet = "Lizard"; word = "lizard"; }
                    else { pet = "CollaredLizard"; word = "lizard"; }
                }
                else if (family == Punchy.Terrain.BiomeFamily.Cold)
                {
                    if (pick < 40) { pet = "ArcticFox"; word = "fox"; }
                    else { pet = "Dog"; word = "dog"; }
                }
                else if (NameHas(biome, "Marsh", "Swamp"))
                {
                    if (pick < 50) { pet = "Frog"; word = "frog"; }
                    else { pet = "MarshDog"; word = "marsh dog"; }
                }
                else if (NameHas(typeName, "Dwarf"))
                {
                    if (pick < 55) { pet = "CapBeetle"; word = "beetle"; }
                    else { pet = "Dog"; word = "dog"; }
                }
                else if (NameHas(typeName, "Goblin", "Ogre"))
                {
                    if (pick < 60) { pet = "Chicken"; word = "hen"; }
                    else { pet = "Dog"; word = "dog"; }
                }
                else if (h.Kind == "hut")
                {
                    if (pick < 60) { pet = "Dog"; word = "dog"; }
                    else { pet = "Chicken"; word = "hen"; }
                }
                else if (outlying)
                {
                    if (pick < 55) { pet = "Chicken"; word = "hen"; }
                    else { pet = "MountainGoat"; word = "goat"; }
                }
                else if (pick < 55) { pet = "Cat"; word = "cat"; }
                else { pet = "Dog"; word = "dog"; }
                h.Pet = pet;
                h.PetWord = word;
                h.PetIndividual = BiomeSampling.Scramble(h.Key ^ 0x50455449u) | 1u;
            }
        }

        /// <summary>Where a household's pet sleeps: beside its door, a step out and a step to the side.</summary>
        internal static float2 PetBed(float4 door)
        {
            math.sincos(door.w, out var sin, out var cos);
            var facing = new float2(sin, cos);
            var side = new float2(cos, -sin);
            return door.xz + facing * 1f + side * 1.2f;
        }

        /// <summary>
        /// A landmark being peopled: each of its households' pets near its bed, leashed there, carrying its household's
        /// Individual and a HouseholdPet. Positions come from the household's key, so no spawn rng is drawn.
        /// </summary>
        void SpawnHouseholdPets(in TerrainSource source, int landmark)
        {
            if (!Households.ByLandmark.TryGetValue(landmark, out var houses))
                return;
            int kept = 0, spawned = 0;
            var kinds = new Dictionary<string, int>();
            foreach (var hi in houses)
            {
                var h = Households.All[hi];
                if (h.Pet == null)
                    continue;
                kept++;
                var species = SpeciesIndex(h.Pet);
                if (species < 0 || settledCount >= MaxSettled)
                    continue;
                var bed = PetBed(h.Door);
                var bits = BiomeSampling.Scramble(h.Key ^ 0x50455432u);
                var angle = (bits & 0xFFFF) / 65535f * 2f * math.PI;
                var reach = 1f + (bits >> 16) / 65535f * 3f;
                math.sincos(angle, out var s, out var c);
                var at = bed + new float2(c, s) * reach;
                var yaw = ((bits >> 8) & 0xFF) / 255f * 2f * math.PI;
                var individual = h.PetIndividual;
                var door = Households.DoorKey(h.Door);
                Enqueue(species, new float3(at.x, source.SampleHeight(at), at.y), SpawnTier.Settled, e =>
                {
                    var em = simWorld.EntityManager;
                    var state = em.GetComponentData<CreatureState>(e);
                    state.Home = bed;
                    state.Leash = PetLeash;
                    state.Individual = individual;
                    em.SetComponentData(e, state);
                    em.AddComponentData(e, new HouseholdPet { Door = door, Bed = bed, Leash = PetLeash });
                }, yaw);
                spawned++;
                kinds.TryGetValue(h.PetWord, out var n);
                kinds[h.PetWord] = n + 1;
            }
            if (kept > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var k in kinds)
                    sb.Append(' ').Append(k.Key).Append(' ').Append(k.Value);
                Debug.Log($"[pets] landmark {landmark}: {kept} of {houses.Count} houses keep a pet, {spawned} spawned:{sb}");
            }
        }
    }
}
