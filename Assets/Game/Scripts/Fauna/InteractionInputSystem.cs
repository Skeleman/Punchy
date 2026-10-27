using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Fauna
{
    /// <summary>
    /// On the player's machine: finds the animal within reach in front of the player and what can be done with it,
    /// shows it (InteractionPrompt), and turns the Interact button into a request (see Interactions): a tap pets, a
    /// hold picks up (small, tame animals), and with an animal in the arms a press puts it down. Also sets the pose the
    /// player's body takes: petting while an animal is being petted by them, carrying while they hold one.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    public partial class InteractionInputSystem : SystemBase
    {
        /// <summary>What the player can do right now ("E  pet the cat"), or null.</summary>
        public static string Prompt;

        InputAction interact;
        /// <summary>This press of the button has already done its thing (picked up, put down).</summary>
        bool pressUsed;
        EntityQuery creatures;
        FaunaDirector director;

        protected override void OnCreate()
        {
            EntityManager.CreateSingleton<InteractionRequest>("InteractionRequest");
            creatures = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>() },
                None = new[] { ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<RemoteAvatar>() }
            });
        }

        protected override void OnStartRunning()
        {
            interact = InputSystem.actions?.FindAction("Player/Interact");
            InteractionPrompt.Ensure();
        }

        protected override void OnDestroy() => Prompt = null;

        protected override void OnUpdate()
        {
            Prompt = null;
            var request = SystemAPI.GetSingleton<InteractionRequest>();
            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity))
                return;
            var player = EntityManager.GetComponentData<LocalTransform>(playerEntity);
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
            var me = Interactions.HolderId(SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0);

            // What we're carrying or petting, and the nearest animal within reach in front of us.
            var reachPoint = player.Position + forward * (Interactions.Reach * 0.45f);
            var entities = creatures.ToEntityArray(Allocator.Temp);
            var states = creatures.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = creatures.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            Entity held = Entity.Null, target = Entity.Null;
            var petting = false;
            var targetCarry = false;
            var best = Interactions.Reach;
            var targetSpecies = -1;
            for (int i = 0; i < entities.Length; i++)
            {
                var s = states[i];
                ref var b = ref bodies[i].Blob.Value;
                if (s.Holder == me && s.Mode == CreatureMode.Held)
                    held = entities[i];
                if (s.Holder == me && s.Mode == CreatureMode.Petted)
                    petting = true;
                if (!Interactions.CanPet(ref b, s) || math.abs(s.Position.y - player.Position.y) > 1.5f)
                    continue;
                var d = math.distance(s.Position.xz, reachPoint.xz) - b.BodyLength * s.Scale * 0.5f;
                if (d < best)
                {
                    best = d;
                    target = entities[i];
                    targetCarry = Interactions.CanCarry(ref b, s);
                    targetSpecies = bodies[i].Species;
                }
            }
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();

            request.Pose = held != Entity.Null ? IdleAction.Carry : petting ? IdleAction.Pet : IdleAction.None;
            if (held != Entity.Null)
                Prompt = "E  put down";
            else if (target != Entity.Null)
            {
                if (director == null)
                    director = Object.FindAnyObjectByType<FaunaDirector>();
                var name = director != null ? director.SpeciesName(targetSpecies) : "animal";
                Prompt = $"E  pet the {name}" + (targetCarry ? "        hold E  pick up" : "");
            }

            // The button: a tap pets, a hold picks up; holding one, a press puts it down.
            if (interact != null && !Punchy.Net.NetMenu.IsOpen)
            {
                if (interact.WasPressedThisFrame())
                {
                    pressUsed = false;
                    if (held != Entity.Null)
                    {
                        Send(ref request, InteractAct.PutDown, held);
                        pressUsed = true;
                    }
                }
                if (!pressUsed && interact.WasPerformedThisFrame() && target != Entity.Null && targetCarry)
                {
                    Send(ref request, InteractAct.PickUp, target);
                    pressUsed = true;
                }
                if (!pressUsed && interact.WasReleasedThisFrame() && target != Entity.Null)
                {
                    Send(ref request, InteractAct.Pet, target);
                    pressUsed = true;
                }
            }
            SystemAPI.SetSingleton(request);
        }

        void Send(ref InteractionRequest request, InteractAct act, Entity target)
        {
            request.Act = act;
            request.Serial++;
            request.Target = target;
            request.TargetGhost = EntityManager.HasComponent<GhostInstance>(target) ? EntityManager.GetComponentData<GhostInstance>(target).ghostId : 0;
        }
    }

    /// <summary>Shows InteractionInputSystem.Prompt at the bottom of the screen.</summary>
    public class InteractionPrompt : MonoBehaviour
    {
        /// <summary>No prompt (for screenshots: "-nohud").</summary>
        public static bool Hidden;
        static InteractionPrompt instance;
        GUIStyle style, shadow;

        public static void Ensure()
        {
            if (instance != null)
                return;
            var go = new GameObject("InteractionPrompt");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<InteractionPrompt>();
        }

        void OnGUI()
        {
            var text = InteractionInputSystem.Prompt;
            if (Hidden || string.IsNullOrEmpty(text))
                return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 40f), alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = Color.white;
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            }
            var rect = new Rect(0f, Screen.height * 0.8f, Screen.width, Screen.height * 0.06f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, shadow);
            GUI.Label(rect, text, style);
        }
    }
}
