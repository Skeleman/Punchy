using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Audio
{
    /// <summary>
    /// The creatures' sounds, and the countryside's, played through GameAudio. Every one comes from what each machine
    /// already knows (synced creature state, the clock, the anchors), so everyone hears the same things with nothing sent:
    /// <list type="bullet">
    /// <item>footsteps: each leg's footfall (the gait phase passing that leg's phase), for folk, players and big animals
    /// near the listener; a splash in water, a clop for hooves;</item>
    /// <item>actions as they start: a wolf's howl (heard far), a guard drawing its blade;</item>
    /// <item>a messenger bird's wingbeats; now and then a hen's cluck;</item>
    /// <item>bees droning at the hives near the listener;</item>
    /// <item>by day birdsong in the air round the listener, at night crickets and now and then an owl (outdoors only).</item>
    /// </list>
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CreatureAudioSystem : SystemBase
    {
        const float StepReach = 18f, HowlReach = 160f, CluckReach = 20f, WingReach = 25f, HiveReach = 18f;

        EntityQuery query;
        /// <summary>What each creature was doing last frame (its gait phase, action, flap count).</summary>
        readonly Dictionary<Entity, (float phase, IdleAction action, int flap, int cluck)> last = new Dictionary<Entity, (float, IdleAction, int, int)>();
        readonly List<Entity> stale = new List<Entity>();
        readonly Dictionary<int, int> hiveLoops = new Dictionary<int, int>();
        readonly List<int> hiveGone = new List<int>();
        int crickets;
        float nextBird, nextOwl;
        int chicken = -2;
        NativeList<int> near;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnDestroy()
        {
            if (near.IsCreated)
                near.Dispose();
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var ear = (float3)cam.transform.position;
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            var outdoors = 1f - CaveSystem.Underground;
            if (chicken == -2)
            {
                var director = Object.FindAnyObjectByType<FaunaDirector>();
                chicken = director != null ? director.SpeciesIndex("Chicken") : -1;
            }

            if (!SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            Creatures(ear, terrain);
            Hives(ear, terrain, night);
            Countryside(ear, night, outdoors);
        }

        void Creatures(float3 ear, TerrainSource terrain)
        {
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);
            var seen = new HashSet<Entity>();
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                var d = math.distance(s.Position, ear);
                if (d > HowlReach)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                var e = entities[i];
                seen.Add(e);
                var had = last.TryGetValue(e, out var was);
                var flap = (int)math.floor(s.Clock * math.max(b.FlapRate, 0.1f));
                var cluck = (int)math.floor((s.Clock + (s.Individual % 97u)) / 9f);
                if (had)
                {
                    // An action just begun.
                    if (s.Action != was.action)
                    {
                        if (s.Action == IdleAction.Howl)
                            GameAudio.PlayOneShot(SoundId.Howl, s.Position, 0.9f + (s.Individual % 13u) * 0.015f, 1f, HowlReach);
                        else if (s.Action == IdleAction.Draw && d < StepReach * 1.5f)
                            GameAudio.PlayOneShot(SoundId.Draw, s.Position, 1f, 0.7f, StepReach * 1.5f);
                    }
                    // Footfalls: a leg's phase wrapping round, while moving on the ground.
                    if (d < StepReach && s.Speed > 0.2f && s.Fly < 0.5f && (b.Upright || b.BodyLength * s.Scale > 0.8f))
                        for (int p = 0; p < b.Parts.Length; p++)
                        {
                            ref var part = ref b.Parts[p];
                            if (part.Role != LimbRole.Leg || part.Parent >= 0)
                                continue;
                            var now = math.frac(s.Phase + part.Phase);
                            var then = math.frac(was.phase + part.Phase);
                            if (now < then && then - now > 0.5f)
                                Step(s, ref b, part.Side, terrain);
                        }
                    // A messenger's wingbeats while it flies.
                    if (s.Task == CreatureTask.Messenger && s.Fly > 0.5f && flap != was.flap && d < WingReach)
                        GameAudio.PlayOneShot(SoundId.Wingbeat, s.Position, 1.1f, 0.5f, WingReach);
                    // A hen clucks now and then (every nine seconds or so of her own clock, not every time).
                    if (bodies[i].Species == chicken && cluck != was.cluck && d < CluckReach && (s.Individual + (uint)cluck) % 3u == 0u)
                        GameAudio.PlayOneShot(SoundId.Cluck, s.Position + new float3(0f, 0.3f, 0f), 0.95f + (s.Individual % 7u) * 0.02f, 0.6f, CluckReach);
                }
                last[e] = (s.Phase, s.Action, flap, cluck);
            }
            // (Forget those gone or out of hearing.)
            stale.Clear();
            foreach (var e in last.Keys)
                if (!seen.Contains(e))
                    stale.Add(e);
            foreach (var e in stale)
                last.Remove(e);
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }

        void Step(in CreatureState s, ref CreatureBlob b, float side, TerrainSource terrain)
        {
            var heavy = !b.Upright;
            var scale = math.max(s.Scale, 0.3f);
            var at = s.Position + new float3(math.cos(s.Yaw) * side * 0.12f * scale, 0.02f, -math.sin(s.Yaw) * side * 0.12f * scale);
            var ground = terrain.SampleGround(at.xz);
            var wet = ground.HasWater && ground.WaterSurface > s.Position.y + 0.02f;   // (wading)
            var id = wet ? SoundId.StepWet : heavy ? SoundId.Clop : SoundId.Step;
            var volume = (s.Mode == CreatureMode.Puppet ? 0.55f : 0.35f) * math.lerp(0.6f, 1.2f, math.saturate(s.Speed / 4f));
            GameAudio.PlayOneShot(id, at, math.lerp(1.15f, 0.85f, math.saturate(scale - 0.5f)), volume, StepReach,
                                  s.Mode == CreatureMode.Puppet ? SoundGroup.Player : SoundGroup.Creatures);
        }

        void Hives(float3 ear, TerrainSource terrain, float night)
        {
            if (!terrain.Anchors.IsCreated)
                return;
            if (!near.IsCreated)
                near = new NativeList<int>(4, Allocator.Persistent);
            near.Clear();
            Anchors.Near(ref terrain.Anchors.Value, ear, HiveReach, AnchorKind.Hive, ref near);
            hiveGone.Clear();
            foreach (var h in hiveLoops.Keys)
                if (!near.Contains(h))
                    hiveGone.Add(h);
            foreach (var h in hiveGone)
            {
                GameAudio.StopLoop(hiveLoops[h]);
                hiveLoops.Remove(h);
            }
            // (Quieter at night, when they're in on the comb.)
            var volume = math.lerp(0.55f, 0.12f, night);
            for (int i = 0; i < near.Length; i++)
            {
                var hive = terrain.Anchors.Value.Anchors[near[i]];
                var trunk = math.max(hive.Size - 1f, 0.1f);
                var comb = hive.Position + new float3(hive.Facing.x, 0f, hive.Facing.y) * (trunk + 0.3f);
                if (!hiveLoops.TryGetValue(near[i], out var handle) || handle == 0)
                    hiveLoops[near[i]] = GameAudio.StartLoop(SoundId.BeeBuzz, comb, volume, HiveReach, SoundGroup.Creatures);
                else
                    GameAudio.MoveLoop(handle, comb, volume);
            }
        }

        void Countryside(float3 ear, float night, float outdoors)
        {
            var now = (float)SystemAPI.Time.ElapsedTime;
            // Crickets at night, outdoors: one field all round, a little off to one side so it isn't inside the head.
            var cricketVolume = math.saturate((night - 0.4f) * 2f) * outdoors * 0.35f;
            var at = ear + new float3(3f, -1.5f, 2f);
            if (cricketVolume > 0.01f && crickets == 0)
                crickets = GameAudio.StartLoop(SoundId.Crickets, at, cricketVolume, 40f, SoundGroup.World);
            else if (cricketVolume <= 0.01f && crickets != 0)
            {
                GameAudio.StopLoop(crickets);
                crickets = 0;
            }
            else if (crickets != 0)
                GameAudio.MoveLoop(crickets, at, cricketVolume);
            if (outdoors < 0.5f)
                return;
            // Birdsong by day: a call or a song now and then in the air round the listener, up in the trees' height.
            if (night < 0.4f && now >= nextBird)
            {
                nextBird = now + UnityEngine.Random.Range(1.2f, 4f);
                var dir = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(8f, 28f);
                var spot = ear + new float3(dir.x, UnityEngine.Random.Range(3f, 9f), dir.y);
                GameAudio.PlayOneShot(UnityEngine.Random.value < 0.25f ? SoundId.Song : SoundId.Chirp, spot,
                                      UnityEngine.Random.Range(0.85f, 1.2f), UnityEngine.Random.Range(0.25f, 0.5f), 35f, SoundGroup.World);
            }
            // An owl now and then at night.
            if (night > 0.6f && now >= nextOwl)
            {
                nextOwl = now + UnityEngine.Random.Range(12f, 30f);
                var dir = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(15f, 35f);
                GameAudio.PlayOneShot(SoundId.Owl, ear + new float3(dir.x, UnityEngine.Random.Range(4f, 8f), dir.y),
                                      UnityEngine.Random.Range(0.9f, 1.1f), 0.5f, 45f, SoundGroup.World);
            }
        }
    }
}
