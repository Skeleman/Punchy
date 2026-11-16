using Punchy.Player;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>The kinds of thing the Use key can reach, one bid each. The order breaks a tie: doors first.</summary>
    public enum UseKind { Door, Folk, Item, Board, Room }

    /// <summary>
    /// Which thing the Use key goes to (P-39, #398): one pick for every Use prompt (rule 33). Each system finds its own
    /// best candidate (a door, a folk, an item, chest or container, a notice board, a room's lamp or way out), scores it
    /// with <see cref="DoorPick.Aim"/> (near and in the facing cone of the body or the camera; lower wins) and bids it
    /// here each frame. Only the lowest bid shows its prompt and takes the key. Doors bid at 0.8 of their score, so a
    /// barrel beside a door no longer steals its prompt. Before this each system traded raw distances, and chests,
    /// containers and boards ignored facing. Bids from this frame and the last count, since the systems update in turn.
    /// The creature Interact button (Fauna's InteractionInputSystem) is a separate key and not part of this.
    /// </summary>
    public static class UseTarget
    {
        /// <summary>A door's score is multiplied by this, so doors win near ties.</summary>
        public const float DoorFactor = 0.8f;

        static readonly int Kinds = System.Enum.GetValues(typeof(UseKind)).Length;
        static readonly float[] bids = new float[Kinds];
        static readonly int[] bidFrame = new int[Kinds];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            for (int i = 0; i < Kinds; i++)
                (bids[i], bidFrame[i]) = (float.MaxValue, -10);
        }

        static bool Fresh(int kind) => Time.frameCount - bidFrame[kind] <= 1 && bids[kind] < float.MaxValue;

        /// <summary>
        /// The local player's flat position and the flat ways its body and the camera point, which every candidate is
        /// scored against. False with no player yet.
        /// </summary>
        public static bool View(out float3 position, out float2 forward, out float2 look)
        {
            position = default;
            forward = look = new float2(0f, 1f);
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (!q.TryGetSingleton<LocalTransform>(out var player))
                return false;
            position = player.Position;
            forward = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz);
            var camera = Camera.main;
            look = camera != null ? math.normalizesafe(((float3)camera.transform.forward).xz) : forward;
            return true;
        }

        /// <summary>A candidate's score by the one rule (DoorPick.Aim), or float.MaxValue when it isn't faced.</summary>
        public static float Score(float3 player, float2 forward, float2 look, float3 target)
        {
            var d = math.distance(player.xz, target.xz);
            return DoorPick.Aim(player.xz, forward, look, target.xz, d, out var score) ? score : float.MaxValue;
        }

        /// <summary>
        /// This kind's best candidate this frame (float.MaxValue: none), and whether it wins the Use key: lower than every
        /// other kind's current bid, ties going to the earlier kind. Call once a frame from each system, then show the
        /// prompt and take the key only when it returns true.
        /// </summary>
        public static bool Bid(UseKind kind, float score)
        {
            var k = (int)kind;
            bids[k] = kind == UseKind.Door && score < float.MaxValue ? score * DoorFactor : score;
            bidFrame[k] = Time.frameCount;
            if (bids[k] >= float.MaxValue)
                return false;
            for (int i = 0; i < Kinds; i++)
                if (i != k && Fresh(i) && (bids[i] < bids[k] || bids[i] == bids[k] && i < k))
                    return false;
            return true;
        }

        /// <summary>Whether a kind other than this one has a candidate (a board's "nothing more" note gives way to it).</summary>
        public static bool OtherBids(UseKind kind)
        {
            for (int i = 0; i < Kinds; i++)
                if (i != (int)kind && Fresh(i))
                    return true;
            return false;
        }

        /// <summary>The bids for a log line: "Door 1.20, Item -".</summary>
        public static string Describe()
        {
            var parts = new string[Kinds];
            for (int i = 0; i < Kinds; i++)
                parts[i] = $"{(UseKind)i} {(Fresh(i) ? bids[i].ToString("F2") : "-")}";
            return string.Join(", ", parts);
        }
    }
}
