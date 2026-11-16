using System.Collections.Generic;
using System.IO;
using Punchy.Fauna;
using Punchy.Net;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Punchy.Debugging
{
    /// <summary>
    /// "-ridefit [rider|mount]" (P-40, #396): a size change while riding, and jumping off. Alone or hosting: the player
    /// (a cat) gets on a dog put down beside it, switches to a lighter body that still fits (stays on), then to one too
    /// heavy for the dog (gets down: TooHeavy), then back, gets on again and presses jump on a virtual pad (gets down).
    /// "-ridefit rider" / "-ridefit mount" (host and client, either way round; the rider "-body Cat", the mount
    /// "-body Human"): the rider gets on; the mount switches to a heavier body (the rider stays on, on the new body),
    /// then to a cat (the rider gets down), then back; the rider gets on again and jumps off. Each check logged, then
    /// PASS or FAIL. "-ridefitshot dir" saves a shot at each check. Made on its own when the flag is there.
    /// </summary>
    public class RideFitTest : MonoBehaviour
    {
        string role;
        string shotDir;
        float startAt = -1f, last, lastTry = -9f;
        bool synced;
        int shots;
        bool done, pass = true, pressJump;
        int releaseIn;
        Gamepad pad;
        Entity dog, mount;
        string stayBody, dropBody, bigBody;
        int dropped0, refound0, ended0;
        readonly List<string> lines = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            var args = DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-ridefit");
            if (at < 0)
                return;
            var go = new GameObject("RideFitTest");
            DontDestroyOnLoad(go);
            var test = go.AddComponent<RideFitTest>();
            test.role = at + 1 < args.Length && (args[at + 1] == "rider" || args[at + 1] == "mount") ? args[at + 1] : "";
            var shot = System.Array.IndexOf(args, "-ridefitshot");
            if (shot >= 0 && shot + 1 < args.Length)
                test.shotDir = args[shot + 1];
        }

        void OnDestroy()
        {
            if (pad != null)
                InputSystem.RemoveDevice(pad);
        }

        void Update()
        {
            PumpPad();
            if (done || Time.timeSinceLevelLoad < 12f)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            var input = world.GetExistingSystemManaged<InteractionInputSystem>();
            var avatars = FindAnyObjectByType<NetAvatars>();
            using var players = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (input == null || avatars == null || !players.TryGetSingletonEntity<PlayerTag>(out var player))
                return;
            if (role.Length == 0)
                Solo(em, input, avatars, player);
            else
                Pair(em, input, avatars, player);
        }

        /// <summary>Alone or hosting: a dog beside the player; on, a lighter body, a heavier one, on again, jump.</summary>
        void Solo(EntityManager em, InteractionInputSystem input, NetAvatars avatars, Entity player)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.CanSpawn)
                return;
            var sim = director.SimWorld.EntityManager;
            if (startAt < 0f)
            {
                var kind = director.SpeciesIndex("Dog");
                if (kind < 0)
                {
                    Finish("no Dog species", false);
                    return;
                }
                SetBody(avatars, "Cat");
                var p = em.GetComponentData<LocalTransform>(player).Position;
                var at = p.xz + new float2(2.5f, 0f);
                using var terrain = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Terrain.TerrainSource>());
                var y = terrain.TryGetSingleton<Punchy.Terrain.TerrainSource>(out var source) ? source.SampleHeight(at) : p.y;
                dog = director.Spawn(kind, new float3(at.x, y, at.y), 0f);
                var c = sim.GetComponentData<CreatureState>(dog);
                c.Mode = CreatureMode.Puppet;
                c.Speed = c.GaitSpeed = 0f;
                sim.SetComponentData(dog, c);
                startAt = Time.time;
                last = 0f;
                Debug.Log($"RideFit: dog put down, {(em.World == director.SimWorld ? "solo" : "hosting")}");
                return;
            }
            // The dog as drawn here: itself alone, its ghost (the nearest creature to it) in a client world.
            if (!em.Exists(mount) && sim.Exists(dog))
                mount = em.World == director.SimWorld ? dog : Nearest(em, sim.GetComponentData<CreatureState>(dog).Position);
            var t = Time.time - startAt;
            var riding = em.HasComponent<PlayerRide>(player);
            if (At(t, 3f))
            {
                if (!em.Exists(mount))
                {
                    Finish("dog not seen", false);
                    return;
                }
                PickBodies(avatars, MountKg(em));
                Begin();
                input.TestRide(mount);
            }
            if (At(t, 5f))
            {
                Check("on the dog as a cat", riding);
                if (stayBody != null)
                    SetBody(avatars, stayBody);
            }
            if (At(t, 8f))
            {
                if (stayBody != null)
                    Check($"still on as a {stayBody} (fits)", riding && RideFitSystem.Dropped == dropped0);
                Shot("stays-on");
                SetBody(avatars, dropBody);
            }
            if (At(t, 11f))
            {
                Check($"down as a {dropBody} (too heavy: {RideFitSystem.LastReason})",
                      !riding && RideFitSystem.Dropped == dropped0 + 1 && RideFitSystem.LastReason == RideRefusal.TooHeavy);
                Shot("dropped");
                SetBody(avatars, "Cat");
            }
            if (At(t, 14f))
                input.TestRide(mount);
            if (At(t, 16f))
            {
                Check("on again as a cat", riding);
                ended0 = PlayerRideSystem.Ended;
                pressJump = true;
            }
            if (At(t, 18f))
            {
                Check("jump got down", !riding && PlayerRideSystem.Ended == ended0 + 1 && PlayerRideSystem.LastOff == RideOff.None);
                Finish("", pass);
            }
            last = t;
        }

        /// <summary>Two players: the rider gets on; the mount grows (rider stays), shrinks (rider down), back; on, jump.</summary>
        void Pair(EntityManager em, InteractionInputSystem input, NetAvatars avatars, Entity player)
        {
            using var others = em.CreateEntityQuery(ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureState>());
            if (others.IsEmpty)
                return;
            var other = others.ToEntityArray(Allocator.Temp)[0];
            if (startAt < 0f)
            {
                startAt = Time.time;
                last = 0f;
                Begin();
                PickBodies(avatars, 0f);
                Debug.Log($"RideFit {role}: the other player is here");
                return;
            }
            if (!synced)
            {
                // Each side sees the other long before the other has finished loading, so both clocks start at the
                // moment the ride begins (t = 3 s): the rider keeps trying to get on, the mount waits for it on top.
                var since = Time.time - startAt;
                if (since > 90f)
                {
                    Finish("never got on together; ", false);
                    return;
                }
                if (role == "rider")
                {
                    if (em.HasComponent<PlayerRide>(player))
                        synced = true;
                    else if (since - lastTry >= 1.5f)
                    {
                        lastTry = since;
                        var them = em.GetComponentData<CreatureState>(other).Position;
                        var tr = em.GetComponentData<LocalTransform>(player);
                        tr.Position = them + new float3(1.2f, 0.3f, 0f);
                        em.SetComponentData(player, tr);
                        input.TestRide(other);
                    }
                }
                else
                {
                    using var mine = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureState>());
                    if (!mine.IsEmpty)
                    {
                        var me = em.GetComponentData<CreatureState>(mine.GetSingletonEntity()).Position;
                        var them = em.GetComponentData<CreatureState>(other).Position;
                        synced = math.distance(me.xz, them.xz) < 0.8f && them.y > me.y + 0.2f;
                    }
                }
                if (!synced)
                    return;
                startAt = Time.time - 3f;
                last = 3f;
                Debug.Log($"RideFit {role}: on together after {since:F0} s, clocks start");
            }
            var t = Time.time - startAt;
            var riding = em.HasComponent<PlayerRide>(player);
            if (role == "rider")
            {
                if (At(t, 26f))
                    input.TestRide(other);
                if (At(t, 6f))
                    Check("on the other player", riding);
                if (At(t, 13f))
                {
                    Check("still on after its body grew (found again)", riding && RideFitSystem.Refound > refound0 && RideFitSystem.Dropped == dropped0);
                    Shot("rider-stays-on");
                }
                if (At(t, 18f))
                {
                    Check($"down after its body shrank ({RideFitSystem.LastReason})", !riding && RideFitSystem.Dropped == dropped0 + 1);
                    Shot("rider-dropped");
                }
                if (At(t, 29f))
                {
                    Check("on again", riding);
                    ended0 = PlayerRideSystem.Ended;
                    pressJump = true;
                }
                if (At(t, 31f))
                {
                    Check("jump got down", !riding && PlayerRideSystem.Ended == ended0 + 1 && PlayerRideSystem.LastOff == RideOff.None);
                    Finish("", pass);
                }
            }
            else
            {
                if (At(t, 9f))
                    SetBody(avatars, bigBody);
                if (At(t, 15f))
                    SetBody(avatars, "Cat");
                if (At(t, 22f))
                    SetBody(avatars, "Human");
                if (At(t, 4f) || At(t, 12f) || At(t, 17f) || At(t, 25f) || At(t, 30f))
                {
                    var net = em.GetComponentData<RemoteAvatar>(other).Ghost;
                    var look = em.HasComponent<NetPlayerLook>(net) ? em.GetComponentData<NetPlayerLook>(net).Mount : -1;
                    Debug.Log($"RideFit mount {t:F0} s: my body {avatars.Fauna?.PlayerSpecies?.name}, their Mount {look}");
                }
                if (At(t, 13f))
                    Shot("mount-grown");
                if (At(t, 18f))
                    Shot("mount-shrunk");
                if (At(t, 33f))
                    Finish("", true);
            }
            last = t;
        }

        bool At(float t, float s) => t >= s && last < s;

        void Begin()
        {
            dropped0 = RideFitSystem.Dropped;
            refound0 = RideFitSystem.Refound;
        }

        float MountKg(EntityManager em)
        {
            var b = em.GetComponentData<CreatureBody>(mount);
            return b.Blob.Value.WeightAt(em.GetComponentData<CreatureState>(mount).Scale);
        }

        /// <summary>From the playable bodies (by their typical weight): one light enough for the dog, one too heavy, and
        /// for the pair test one heavier than a human and wingless.</summary>
        void PickBodies(NetAvatars avatars, float mountKg)
        {
            CreatureSpecies cat = null, human = null, stay = null, drop = null, big = null;
            var all = new List<string>();
            foreach (var s in avatars.PlayableSpecies)
                if (s != null)
                {
                    all.Add($"{s.name} {s.Weight:F0}");
                    if (s.name == "Cat") cat = s;
                    if (s.name == "Human") human = s;
                }
            foreach (var s in avatars.PlayableSpecies)
            {
                if (s == null || s == cat)
                    continue;
                if (mountKg > 0f && s.Weight * Riding.MassRatio <= 0.8f * mountKg && (stay == null || s.Weight > stay.Weight))
                    stay = s;
                if (mountKg > 0f && s.Weight * Riding.MassRatio > 1.25f * mountKg && (drop == null || s.Weight < drop.Weight))
                    drop = s;
                if (human != null && s.Weight > 1.6f * human.Weight && !Winged(s) && (big == null || s.Weight < big.Weight))
                    big = s;
            }
            stayBody = stay?.name;
            dropBody = human != null && human.Weight * Riding.MassRatio > 1.25f * mountKg ? "Human" : drop?.name;
            bigBody = big?.name;
            Debug.Log($"RideFit: mount {mountKg:F1} kg; playable: {string.Join(", ", all)}; stay {stayBody}, drop {dropBody}, big {bigBody}");
        }

        static bool Winged(CreatureSpecies s)
        {
            foreach (var p in s.Parts)
                if (p.Limb != null && p.Limb.Role == LimbRole.Wing)
                    return true;
            return false;
        }

        static void SetBody(NetAvatars avatars, string name)
        {
            avatars.SetLocalSpecies(avatars.PlayableSpecies.FindIndex(s => s != null && s.name == name));
            Debug.Log($"RideFit: body {name}");
        }

        static Entity Nearest(EntityManager em, float3 at)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<GhostInstance>());
            using var all = q.ToEntityArray(Allocator.Temp);
            var best = 1.5f;
            var found = Entity.Null;
            foreach (var e in all)
            {
                var d = math.distance(em.GetComponentData<CreatureState>(e).Position, at);
                if (d < best && !em.HasComponent<PlayerAvatar>(e) && !em.HasComponent<RemoteAvatar>(e))
                {
                    best = d;
                    found = e;
                }
            }
            return found;
        }

        void Check(string what, bool ok)
        {
            pass &= ok;
            lines.Add($"{what}: {(ok ? "ok" : "NO")}");
            Debug.Log($"RideFit {role}: {what}: {(ok ? "ok" : "NO")}");
        }

        void Finish(string why, bool ok)
        {
            done = true;
            Debug.Log($"RideFit {role}: {why}{string.Join(" | ", lines)} => {(ok ? "PASS" : "FAIL")}");
        }

        void Shot(string name)
        {
            if (shotDir == null)
                return;
            Directory.CreateDirectory(shotDir);
            var path = Path.Combine(shotDir, $"ridefit-{shots++:00}-{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"RideFit: shot {path}");
        }

        /// <summary>A jump press on a virtual pad: queued now, read by everything next frame, let go a few frames on.</summary>
        void PumpPad()
        {
            if (pressJump)
            {
                pad ??= InputSystem.AddDevice<Gamepad>("RideFitPad");
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                pressJump = false;
                releaseIn = 4;
            }
            else if (releaseIn > 0 && --releaseIn == 0)
                InputSystem.QueueStateEvent(pad, new GamepadState());
        }
    }
}
