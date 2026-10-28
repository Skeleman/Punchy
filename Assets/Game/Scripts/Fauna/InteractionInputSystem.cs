using Punchy.Player;
using Unity.Burst;
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
    /// Emotes too (keys 1–4, or the d-pad: wave, bow, cheer, dance): a pose held a moment (dancing, until they move),
    /// which folk and animals near them react to (the brain reads it from the pose).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    public partial class InteractionInputSystem : SystemBase
    {
        /// <summary>What the player can do right now ("E  pet the cat"), or null.</summary>
        public static string Prompt;

        InputAction interact, attack, move;
        /// <summary>The emote the player is doing (an IdleAction, Wave to Dance), and when it ends.</summary>
        IdleAction emote;
        double emoteUntil;
        /// <summary>This press of the button has already done its thing (picked up, put down).</summary>
        bool pressUsed;
        /// <summary>When the lunge pose ends.</summary>
        double lungeUntil;
        /// <summary>The player last used a gamepad (rather than the keyboard and mouse).</summary>
        bool usingGamepad;
        /// <summary>The prompt as last built, and what it was built for (species, can carry, gamepad).</summary>
        string lastPrompt;
        (int, bool, bool) lastPromptKey = (int.MinValue, false, false);
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
            attack = InputSystem.actions?.FindAction("Player/Attack");
            move = InputSystem.actions?.FindAction("Player/Move");
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

            // What we're carrying or petting, and the nearest animal within reach in front of us (a Burst pass over
            // the creatures, no copies).
            var scan = new NativeReference<ReachScan>(new ReachScan
            {
                Best = Interactions.Reach,
                LungeBest = Interactions.LungeReach,
                TargetSpecies = -1
            }, Allocator.TempJob);
            // (Scheduled after whatever writes creature state this frame, the brain's job when alone, and waited for.)
            Dependency = new ReachJob
            {
                Me = me,
                Player = player.Position,
                ReachPoint = player.Position + forward * (Interactions.Reach * 0.45f),
                Result = scan
            }.Schedule(creatures, Dependency);
            Dependency.Complete();
            var found = scan.Value;
            scan.Dispose();
            var held = found.Held;
            var target = found.Target;
            var lungeTarget = found.LungeTarget;
            var petting = found.Petting;
            var targetCarry = found.TargetCarry;
            var targetSpecies = found.TargetSpecies;

            var lunging = SystemAPI.Time.ElapsedTime < lungeUntil;
            UpdateEmote(held != Entity.Null || lunging || petting);
            request.Pose = held != Entity.Null ? IdleAction.Carry : lunging ? IdleAction.Throw : petting ? IdleAction.Pet : emote;
            // Named by the button on whatever was used last: the keyboard's E, or the gamepad's top face button (Y).
            // (By real use: an idle gamepad still sends updates.)
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                usingGamepad = false;
            var pad = Gamepad.current;
            if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.2f || pad.rightStick.ReadValue().sqrMagnitude > 0.2f ||
                                pad.buttonSouth.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                                pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
                usingGamepad = true;
            var gamepad = usingGamepad;
            var key = gamepad ? "Y" : "E";
            // (The text is only rebuilt when what it says changes, not every frame.)
            var promptKey = held != Entity.Null ? (-2, false, gamepad) : target != Entity.Null ? (targetSpecies, targetCarry, gamepad) : (-1, false, gamepad);
            if (promptKey != lastPromptKey)
            {
                lastPromptKey = promptKey;
                if (held != Entity.Null)
                    lastPrompt = $"{key}  put down";
                else if (target != Entity.Null)
                {
                    if (director == null)
                        director = Object.FindAnyObjectByType<FaunaDirector>();
                    var name = director != null ? director.SpeciesName(targetSpecies) : "animal";
                    lastPrompt = $"{key}  pet the {name}" + (targetCarry ? $"        hold {key}  pick up" : "");
                }
                else
                    lastPrompt = null;
            }
            Prompt = lastPrompt;
            InteractionPrompt.Show(Prompt != null);

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
            // Attack: a lunge at the animal in front (it bolts; harmless for now). Empty-handed only.
            if (attack != null && !Punchy.Net.NetMenu.IsOpen && held == Entity.Null && attack.WasPressedThisFrame() && !lunging)
            {
                lungeUntil = SystemAPI.Time.ElapsedTime + 0.6;
                if (lungeTarget != Entity.Null)
                    Send(ref request, InteractAct.Startle, lungeTarget);
            }
            SystemAPI.SetSingleton(request);
        }

        /// <summary>
        /// Starts an emote on its key (or d-pad direction); it ends after a moment (a dance goes on until they move),
        /// when they walk off, or when their hands are busy with an animal.
        /// </summary>
        void UpdateEmote(bool busy)
        {
            var now = SystemAPI.Time.ElapsedTime;
            var moving = move != null && move.ReadValue<Vector2>().sqrMagnitude > 0.1f;
            if (emote != IdleAction.None && (now > emoteUntil || moving || busy))
                emote = IdleAction.None;
            if (busy || Punchy.Net.NetMenu.IsOpen)
                return;
            var keys = Keyboard.current;
            var pad = Gamepad.current;
            var pick = IdleAction.None;
            if (keys != null && keys.digit1Key.wasPressedThisFrame || pad != null && pad.dpad.up.wasPressedThisFrame)
                pick = IdleAction.Wave;
            else if (keys != null && keys.digit2Key.wasPressedThisFrame || pad != null && pad.dpad.down.wasPressedThisFrame)
                pick = IdleAction.Bow;
            else if (keys != null && keys.digit3Key.wasPressedThisFrame || pad != null && pad.dpad.right.wasPressedThisFrame)
                pick = IdleAction.Cheer;
            else if (keys != null && keys.digit4Key.wasPressedThisFrame || pad != null && pad.dpad.left.wasPressedThisFrame)
                pick = IdleAction.Dance;
            if (Requested != IdleAction.None)
            {
                pick = Requested;
                Requested = IdleAction.None;
            }
            if (pick == IdleAction.None)
                return;
            emote = pick;
            emoteUntil = now + EmoteLength(pick);
        }

        /// <summary>How long an emote lasts (a dance, until the player moves off).</summary>
        public static float EmoteLength(IdleAction emote) => emote switch
        {
            IdleAction.Wave => 2.2f,
            IdleAction.Bow => 1.8f,
            IdleAction.Cheer => 1.8f,
            IdleAction.Dance => 30f,
            _ => 0f
        };

        /// <summary>A test's way in ("-emote Wave"): as if its key had been pressed.</summary>
        public static IdleAction Requested;

        /// <summary>What ReachJob found: our held or petted animal, and the nearest within reach and lunge reach.</summary>
        struct ReachScan
        {
            public Entity Held, Target, LungeTarget;
            public bool Petting, TargetCarry;
            public float Best, LungeBest;
            public int TargetSpecies;
        }

        [BurstCompile]
        partial struct ReachJob : IJobEntity
        {
            public int Me;
            public float3 Player, ReachPoint;
            public NativeReference<ReachScan> Result;

            void Execute(Entity entity, in CreatureState s, in CreatureBody body)
            {
                var r = Result.Value;
                ref var b = ref body.Blob.Value;
                if (s.Holder == Me && s.Mode == CreatureMode.Held)
                    r.Held = entity;
                if (s.Holder == Me && s.Mode == CreatureMode.Petted)
                    r.Petting = true;
                if (Interactions.CanPet(ref b, s) && math.abs(s.Position.y - Player.y) <= 1.5f)
                {
                    var d = math.distance(s.Position.xz, ReachPoint.xz) - b.BodyLength * s.Scale * 0.5f;
                    if (d < r.LungeBest)
                    {
                        r.LungeBest = d;
                        r.LungeTarget = entity;
                    }
                    if (d < r.Best)
                    {
                        r.Best = d;
                        r.Target = entity;
                        r.TargetCarry = Interactions.CanCarry(ref b, s);
                        r.TargetSpecies = body.Species;
                    }
                }
                Result.Value = r;
            }
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
            instance.enabled = false;
        }

        /// <summary>On only while there's something to show (so OnGUI doesn't run every frame for nothing).</summary>
        public static void Show(bool show)
        {
            if (instance != null && instance.enabled != show)
                instance.enabled = show;
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
