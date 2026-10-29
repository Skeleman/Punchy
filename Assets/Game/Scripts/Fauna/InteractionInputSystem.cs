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

        InputAction interact, attack, move, jump;
        /// <summary>Sitting or lying (IdleAction.SitDown or Lie) where they are or on a seat, until they move; or None.</summary>
        IdleAction rest;
        /// <summary>When the player last moved (for offering to sit only once they've stood still a moment).</summary>
        double movedAt;
        /// <summary>The emote the player is doing (an IdleAction, Wave to Dance), and when it ends.</summary>
        IdleAction emote;
        double emoteUntil;
        /// <summary>This press of the button has already done its thing (picked up, put down).</summary>
        bool pressUsed;
        /// <summary>When the lunge pose ends.</summary>
        double lungeUntil;
        /// <summary>The prompt as last built, and what it was built for (species, can carry, device switches).</summary>
        string lastPrompt;
        (int, bool, int) lastPromptKey = (int.MinValue, false, -1);
        EntityQuery creatures;
        FaunaDirector director;

        protected override void OnCreate()
        {
            EntityManager.CreateSingleton<InteractionRequest>("InteractionRequest");
            RequireForUpdate<Punchy.Terrain.TerrainSource>();
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
            jump = InputSystem.actions?.FindAction("Player/Jump");
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
            // (An animal body can't carry: it nuzzles instead, so no "pick up" for it.)
            var folk = Interactions.CanHold(SystemAPI.TryGetSingleton<CarryTable>(out var table) ? table : default,
                                            SystemAPI.TryGetSingleton<Punchy.Net.LocalPlayerSpecies>(out var local) ? local.Index : -1);
            Dependency = new ReachJob
            {
                Me = me,
                Folk = folk,
                Terrain = SystemAPI.GetSingleton<Punchy.Terrain.TerrainSource>(),
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
            // Sitting and lying: up again on moving, jumping, leaving the ground, an emote or an animal in hand.
            var motion = EntityManager.HasComponent<PlayerMotion>(playerEntity) ? EntityManager.GetComponentData<PlayerMotion>(playerEntity) : default;
            var moving = move != null && move.ReadValue<Vector2>().sqrMagnitude > 0.1f;
            var onGround = motion.Grounded && !motion.Flying && motion.Mantle <= 0f;
            if (moving || !onGround)
                movedAt = SystemAPI.Time.ElapsedTime;
            if (rest != IdleAction.None && (moving || !onGround || emote != IdleAction.None || held != Entity.Null || petting ||
                                            jump != null && jump.WasPressedThisFrame()))
                rest = IdleAction.None;
            var terrain = SystemAPI.GetSingleton<Punchy.Terrain.TerrainSource>();
            var nearSeat = terrain.Anchors.IsCreated && Seats.Nearest(ref terrain.Anchors.Value, player.Position, 1.6f, out _, out _);
            // (Offered by a seat, or once they've stood still a moment; the button works anywhere on the ground.)
            var offerRest = onGround && held == Entity.Null && target == Entity.Null &&
                            (rest != IdleAction.None || nearSeat || SystemAPI.Time.ElapsedTime - movedAt > 1.5);
            request.Pose = held != Entity.Null ? IdleAction.Carry : lunging ? IdleAction.Throw : petting ? IdleAction.Pet :
                           emote != IdleAction.None ? emote : rest;
            // Named by the button on whatever was used last (InputGlyphs: E on the keyboard, Y or Triangle on a pad).
            // (The text is only rebuilt when what it says changes, not every frame.)
            var device = InputGlyphs.Version;
            var promptKey = held != Entity.Null ? (-2, false, device) : target != Entity.Null ? (targetSpecies, targetCarry, device) :
                            offerRest ? (rest == IdleAction.SitDown ? -4 : rest == IdleAction.LieBack ? -5 : -3, false, device) : (-1, false, device);
            if (promptKey != lastPromptKey)
            {
                lastPromptKey = promptKey;
                var key = InputGlyphs.Action("Player/Interact") ?? "Interact";
                if (held != Entity.Null)
                    lastPrompt = $"{key}  put down";
                else if (target != Entity.Null)
                {
                    if (director == null)
                        director = Object.FindAnyObjectByType<FaunaDirector>();
                    var name = director != null ? director.SpeciesName(targetSpecies) : "animal";
                    lastPrompt = $"{key}  pet the {name}" + (targetCarry ? $"        hold {key}  pick up" : "");
                }
                else if (offerRest)
                    lastPrompt = rest == IdleAction.SitDown ? $"{key}  lie back" : rest == IdleAction.LieBack ? $"{key}  get up" : $"{key}  sit";
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
                // Nothing to pet or hold: sit down (on the nearest seat, if one's close), then lie back, then get up.
                if (!pressUsed && interact.WasReleasedThisFrame() && target == Entity.Null && held == Entity.Null && onGround)
                {
                    pressUsed = true;
                    rest = rest == IdleAction.None ? IdleAction.SitDown : rest == IdleAction.SitDown ? IdleAction.LieBack : IdleAction.None;
                    if (rest == IdleAction.SitDown && terrain.Anchors.IsCreated &&
                        Seats.Nearest(ref terrain.Anchors.Value, player.Position, 1.6f, out var seat, out var facing))
                    {
                        // Onto the seat: stood at its place (the hips go to its height in the posture), facing out.
                        var t = EntityManager.GetComponentData<LocalTransform>(playerEntity);
                        t.Position = new float3(seat.x, terrain.SampleHeight(seat.xz), seat.z);
                        t.Rotation = quaternion.RotateY(math.atan2(facing.x, facing.y));
                        EntityManager.SetComponentData(playerEntity, t);
                    }
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
            public bool Folk;
            public Punchy.Terrain.TerrainSource Terrain;
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
                        r.TargetCarry = Folk && Interactions.CanCarry(ref b, s, Interactions.OnLand(Terrain, s.Position));
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
