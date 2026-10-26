using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A kind of animal, assembled from parts: a body and limb types attached in slots (legs, neck, head, ears,
    /// tail…), with colours, gait and behaviour. Slots can be mirrored to make left/right pairs, and each has a gait
    /// phase so the same leg type walks, trots or hops depending on how the slots are timed.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Creature Species", fileName = "Creature")]
    public class CreatureSpecies : ScriptableObject
    {
        [Header("Body")]
        [Tooltip("Length of the torso, nose-end to tail-end of the trunk, in metres.")]
        [Min(0.01f)] public float BodyLength = 0.8f;
        [Min(0.005f)] public float BodyRadius = 0.15f;
        [Tooltip("Rear radius relative to the front (below 1 = slimmer hips).")]
        [Range(0.2f, 2f)] public float BodyTaper = 1f;
        [Tooltip("Height of the torso relative to its width.")]
        [Range(0.2f, 3f)] public float BodyFlatten = 1.1f;
        public Color Base = new Color(0.55f, 0.4f, 0.25f);
        public Color Accent = new Color(0.9f, 0.85f, 0.75f);
        [Tooltip("Random size of individuals.")]
        public Vector2 Size = new Vector2(0.9f, 1.1f);

        [Header("Parts")]
        public List<LimbSlot> Parts = new List<LimbSlot>();

        [Header("Gait")]
        public float WalkSpeed = 1.4f;
        public float RunSpeed = 6f;
        [Tooltip("How far the body dips per step, walking and running (metres).")]
        public Vector2 Bob = new Vector2(0.02f, 0.06f);
        [Tooltip("Dips per gait cycle, walking and running (2 for a walk; 1 for a bound or hop).")]
        public Vector2Int BobsPerCycle = new Vector2Int(2, 2);
        [Tooltip("Rocking of the body nose-up / nose-down per cycle when running, in degrees (bounding, hopping).")]
        public float RunRock = 0f;

        [Header("Behaviour")]
        [Tooltip("How far it wanders from where it is in one go.")]
        public float WanderRadius = 25f;
        [Tooltip("Seconds it stands around between wanders.")]
        public Vector2 IdleTime = new Vector2(2f, 8f);
        [Tooltip("Runs away from the player within this distance (0 = unafraid).")]
        public float FleeDistance = 12f;
        [Tooltip("Degrees per second.")]
        public float TurnRate = 140f;
        [Tooltip("Number that appear together.")]
        public Vector2Int GroupSize = new Vector2Int(1, 1);
    }

    [Serializable]
    public class LimbSlot
    {
        public LimbType Limb;
        [Tooltip("-1 = attach to the body; otherwise the index of an earlier part to attach to the end of.")]
        public int Parent = -1;
        [Tooltip("On the body: position in body units (x, y across the radius; z from -1 rear to 1 front).\n" +
                 "On a part: offset in metres from the end of its last segment, in that segment's frame.")]
        public Vector3 Socket;
        [Tooltip("Rest orientation in degrees (x pitch, y yaw, z roll). A part points forward by default: (90, 0, 0) " +
                 "points down, (0, 180, 0) backwards, (0, 90, 0) out to the right.")]
        public Vector3 Direction;
        [Tooltip("Also add a mirror image on the other side (left/right pairs).")]
        public bool Mirror;
        [Tooltip("Where in the gait cycle this part is (0..1).")]
        [Range(0f, 1f)] public float Phase;
        [Tooltip("Phase of the mirror image relative to this one (0.5 = alternating; 0 = together, as in a hop).")]
        [Range(0f, 1f)] public float MirrorPhase = 0.5f;
        [Min(0.01f)] public float Scale = 1f;
    }

    /// <summary>A species living in a biome, and how many.</summary>
    [Serializable]
    public class BiomeFauna
    {
        public CreatureSpecies Species;
        [Tooltip("Animals per hectare (10 000 m²).")]
        [Min(0f)] public float Density = 0.5f;
    }
}
