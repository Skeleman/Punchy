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
    /// <item>actions as they start: a wolf's howl (heard far), a guard drawing its blade, a player in an animal's body
    /// emoting (its call);</item>
    /// <item>a messenger bird's wingbeats;</item>
    /// <item>each species' calls (CreatureSpecies.Calls: a hen's cluck, a duck's quack, a dog's bark), every individual on
    /// its own clock, at the time of day the call is for, not asleep or indoors;</item>
    /// <item>a cat purring while it's held and content (not squirming to get down), a dog panting while it follows
    /// someone or after a run;</item>
    /// <item>bees droning at the hives near the listener;</item>
    /// <item>the countryside's small life (AmbientLife), each call from where it is: songbirds, a woodpecker, owls, crickets,
    /// grasshoppers, frogs, cicadas. No sound without something there.</item>
    /// </list>
    /// Calls and the countryside's sounds share a budget (CallsPerSecond, a few saved up), so a village or a wood full of
    /// creatures doesn't call over itself: past it, a call is let go unless it's right by the listener. Footsteps, howls
    /// and the like are outside it.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CreatureAudioSystem : SystemBase
    {
        const float StepReach = 18f, HowlReach = 160f, WingReach = 25f, HiveReach = 18f, FarthestCall = 260f;
        /// <summary>The calls' budget: this many a second on average, up to CallBurst saved up; always heard within CloseCall m.</summary>
        const float CallsPerSecond = 1.5f, CallBurst = 3f, CloseCall = 6f;
        float callBudget = CallBurst;
        /// <summary>Calls played, and let go over the budget, since start (for tests); and whether the budget applies.</summary>
        public static int CallsPlayed, CallsThinned;
        public static bool ThinCalls = true;

        EntityQuery query;
        /// <summary>What each creature was doing last frame (its gait phase, action, flap count).</summary>
        readonly Dictionary<Entity, (float phase, IdleAction action, int flap, int4 calls)> last = new Dictionary<Entity, (float, IdleAction, int, int4)>();
        /// <summary>Each species' calls (its own, or the species it's based on), by species index; and the cats and dogs.</summary>
        List<CreatureCall>[] calls;
        bool[] cats, dogs;
        /// <summary>A purring cat's loop; each dog's exertion (seconds of running, easing off) and when it last panted.</summary>
        readonly Dictionary<Entity, int> purrs = new Dictionary<Entity, int>();
        readonly Dictionary<Entity, (float exertion, float panted)> dogBreath = new Dictionary<Entity, (float, float)>();
        float hour;
        readonly List<Entity> stale = new List<Entity>();
        readonly Dictionary<int, int> hiveLoops = new Dictionary<int, int>();
        readonly List<int> hiveGone = new List<int>();
        /// <summary>Each of AmbientLife's small creatures' call count and whether it was hidden, last frame.</summary>
        readonly Dictionary<uint, (int calls, bool hidden)> ambientCalls = new Dictionary<uint, (int, bool)>();
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
            hour = clock.Hour;
            // (The species' calls, read once the director has its species, and again if it rebuilds them.)
            var director = calls == null || UnityEngine.Time.frameCount % 120 == 0 ? Object.FindAnyObjectByType<FaunaDirector>() : null;
            if (calls == null && (director == null || director.SpeciesCount == 0))
                return;
            if (director != null && director.SpeciesCount > 0 && (calls == null || calls.Length != director.SpeciesCount))
            {
                var n = director.SpeciesCount;
                calls = new List<CreatureCall>[n];
                cats = new bool[n];
                dogs = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    var sp = director.SpeciesAt(i);
                    if (sp == null)
                        continue;
                    calls[i] = sp.Calls.Count > 0 ? sp.Calls : sp.Body.Calls;
                    var voice = sp.Voice != VoiceFamily.None ? sp.Voice : sp.Body.Voice;
                    cats[i] = voice == VoiceFamily.Cat;
                    dogs[i] = voice == VoiceFamily.Dog;
                }
            }

            if (!SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
            {
                GameAudio.HasTerrain = false;
                return;
            }
            GameAudio.Terrain = terrain;
            GameAudio.HasTerrain = true;
            callBudget = math.min(callBudget + SystemAPI.Time.DeltaTime * CallsPerSecond, CallBurst);
            Creatures(ear, terrain);
            Hives(ear, terrain, night);
            Countryside(ear, night);
            Moods(ear);
        }

        void Creatures(float3 ear, TerrainSource terrain)
        {
            var clock = (float)SystemAPI.Time.ElapsedTime;
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);
            var seen = new HashSet<Entity>();
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                var d = math.distance(s.Position, ear);
                if (d > FarthestCall)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                var e = entities[i];
                seen.Add(e);
                var had = last.TryGetValue(e, out var was);
                var flap = (int)math.floor(s.Clock * math.max(b.FlapRate, 0.1f));
                var species = bodies[i].Species;
                var mine = species >= 0 && species < calls.Length ? calls[species] : null;
                var slots = int4.zero;
                if (mine != null)
                    for (int c = 0; c < mine.Count && c < 4; c++)
                    {
                        // (Each individual calls every so often, somewhere in the call's range, from its own clock.)
                        var every = math.lerp(mine[c].Interval.x, mine[c].Interval.y, CreatureBlob.Trait(s.Individual, 50 + c) * 0.5f + 0.5f);
                        slots[c] = (int)math.floor((s.Clock + (s.Individual % 101u)) / math.max(every, 0.5f));
                    }
                if (had)
                {
                    // An action just begun.
                    if (s.Action != was.action)
                    {
                        if (s.Action == IdleAction.Howl)
                            GameAudio.PlayOneShot(SoundId.Howl, s.Position, 0.9f + (s.Individual % 13u) * 0.015f, 0.5f, HowlReach);
                        else if (s.Action == IdleAction.Draw && d < StepReach * 1.5f)
                            GameAudio.PlayOneShot(SoundId.Draw, s.Position, 1f, 0.7f, StepReach * 1.5f);
                        // A player in an animal's body emoting: the animal's own call (each emote one of its calls, if
                        // it has several), heard as the player's own sound. Bodies with hands gesture instead.
                        else if (s.Mode == CreatureMode.Puppet && s.Action >= IdleAction.Wave && s.Action <= IdleAction.Dance &&
                                 !b.Hands && mine != null && mine.Count > 0)
                        {
                            var call = mine[(s.Action - IdleAction.Wave) % mine.Count];
                            GameAudio.PlayOneShot(call.Sound, s.Position + new float3(0f, b.StandHeight * s.Scale, 0f),
                                                  (call.Pitch.x + call.Pitch.y) * 0.5f, call.Volume, call.Reach, SoundGroup.Player);
                        }
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
                    // Its calls, each when its time comes round (awake and out, at the call's time of day). Not a player's
                    // body: a player makes its animal's sounds only by emoting (above).
                    if (mine != null && s.Mode != CreatureMode.Puppet && Awake(s))
                        for (int c = 0; c < mine.Count && c < 4; c++)
                        {
                            var call = mine[c];
                            if (slots[c] == was.calls[c] || d > call.Reach || !Now(call.When))
                                continue;
                            var pitch = math.lerp(call.Pitch.x, call.Pitch.y, CreatureBlob.Trait(s.Individual, 60 + c) * 0.5f + 0.5f);
                            // (A herd doesn't call over itself: one moo, bleat or grunt, then that sound waits a while
                            // near there before another of the herd takes it up.)
                            if (Herd(call.Sound))
                            {
                                if (herdCalls.TryGetValue(call.Sound, out var lastCall) && clock - lastCall.time < 8f &&
                                    math.distance(lastCall.at, s.Position) < 30f)
                                    continue;
                                herdCalls[call.Sound] = (clock, s.Position);
                            }
                            if (!Spend(d))
                                continue;
                            if (GameAudio.PlayOneShot(call.Sound, s.Position + new float3(0f, b.StandHeight * s.Scale, 0f), pitch, call.Volume, call.Reach))
                                CallsPlayed++;
                        }
                }
                last[e] = (s.Phase, s.Action, flap, slots);
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

        /// <summary>Whether a call this far off fits the budget (taking its share if so).</summary>
        bool Spend(float distance)
        {
            if (!ThinCalls || distance < CloseCall)
                return true;
            if (callBudget >= 1f)
            {
                callBudget -= 1f;
                return true;
            }
            CallsThinned++;
            return false;
        }

        /// <summary>When and where each herd call was last made.</summary>
        readonly Dictionary<SoundId, (float time, float3 at)> herdCalls = new Dictionary<SoundId, (float, float3)>();

        static bool Herd(SoundId id) => id == SoundId.Moo || id == SoundId.Bleat || id == SoundId.Grunt;

        /// <summary>Up and about (not asleep, hanging asleep, down its burrow or indoors).</summary>
        static bool Awake(in CreatureState s) =>
            s.Mode != CreatureMode.Sleep && !(s.Mode == CreatureMode.Roost && s.Stage == 200) && !(s.Mode == CreatureMode.Burrow && s.Stage >= 1) &&
            !(s.Mode == CreatureMode.Home && s.Stage >= 1);

        bool Now(CallTime when)
        {
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            return when switch
            {
                CallTime.Day => night < 0.5f,
                CallTime.Night => night >= 0.5f,
                CallTime.Dawn => hour >= 5f && hour < 8f,
                _ => true
            };
        }

        /// <summary>
        /// A held cat that isn't squirming to get down purrs (only heard close); a dog pants while it follows someone (a
        /// friendly one's greeting) or after a run, the breaths quicker the harder it's been running.
        /// </summary>
        void Moods(float3 ear)
        {
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);
            var now = (float)SystemAPI.Time.ElapsedTime;
            var dt = SystemAPI.Time.DeltaTime;
            var purring = new HashSet<Entity>();
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                var species = bodies[i].Species;
                if (species < 0 || species >= cats.Length)
                    continue;
                var d = math.distance(s.Position, ear);
                if (cats[species] && s.Mode == CreatureMode.Held && s.Action != IdleAction.Shake && d < 6f)
                {
                    purring.Add(entities[i]);
                    if (!purrs.TryGetValue(entities[i], out var handle) || handle == 0)
                        purrs[entities[i]] = GameAudio.StartLoop(SoundId.Purr, s.Position, 0.5f, 6f, SoundGroup.Creatures);
                    else
                        GameAudio.MoveLoop(handle, s.Position, 0.5f);
                }
                if (dogs[species] && d < 12f)
                {
                    dogBreath.TryGetValue(entities[i], out var breath);
                    // (Running builds it up; at rest it eases off.)
                    breath.exertion = math.clamp(breath.exertion + (s.Speed > 2.5f ? dt : -dt * 0.25f), 0f, 8f);
                    var panting = s.Mode == CreatureMode.Greet && s.Speed > 0.2f || breath.exertion > 1f;
                    var every = math.lerp(0.6f, 0.22f, math.saturate(breath.exertion / 5f));
                    if (panting && now - breath.panted > every)
                    {
                        breath.panted = now;
                        var mouth = s.Position + new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw)) * bodies[i].Blob.Value.BodyLength * s.Scale * 0.6f +
                                    new float3(0f, bodies[i].Blob.Value.StandHeight * s.Scale, 0f);
                        GameAudio.PlayOneShot(SoundId.Pant, mouth, 0.9f + (s.Individual % 9u) * 0.03f, 0.35f, 12f);
                    }
                    dogBreath[entities[i]] = breath;
                }
            }
            // (Stop the purrs of cats put down or out of hearing.)
            stale.Clear();
            foreach (var e in purrs.Keys)
                if (!purring.Contains(e))
                    stale.Add(e);
            foreach (var e in stale)
            {
                GameAudio.StopLoop(purrs[e]);
                purrs.Remove(e);
            }
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
            // (Or in a fountain's basin: PropSupport knows its water.)
            if (!wet && SystemAPI.TryGetSingleton<Punchy.Flora.SupportGrids>(out var grids) && grids.Set.IsCreated &&
                SystemAPI.TryGetSingletonBuffer<Punchy.Flora.FloraSupport>(out var props, true) &&
                Punchy.Flora.PropSupport.Sample(ref grids.Set.Value, props, s.Position, 0.1f, 0.3f, out var hit) && hit.Wading)
                wet = hit.WaterHeight > s.Position.y + 0.02f;
            // What's underfoot: snow crunches, grass rustles, sand hisses, rock taps; hooves clop on anything hard. Padded
            // paws (a wolf's, a dog's) only thump softly, quieter than feet.
            var id = SoundId.Step;
            var paw = heavy && !b.Hooves;
            if (wet)
                id = SoundId.StepWet;
            else
            {
                var surface = terrain.SampleSurface(at.xz, ground, terrain.SampleNormal(at.xz, 0.5f));
                var sand = surface.Grass < 0.2f && surface.Snow < 0.2f && surface.Albedo.x > 0.3f && surface.Albedo.x > surface.Albedo.z * 1.35f && surface.Pebbles < 0.4f;
                var rock = surface.Strata > 0.4f || surface.Pebbles > 0.5f;
                id = surface.Snow > 0.4f ? SoundId.StepSnow
                   : surface.Grass > 0.4f ? (b.Hooves ? SoundId.Step : SoundId.StepGrass)
                   : b.Hooves ? SoundId.Clop
                   : sand ? SoundId.StepSand
                   : paw ? SoundId.StepPaw
                   : rock ? SoundId.StepRock : SoundId.Step;
            }
            var volume = (s.Mode == CreatureMode.Puppet ? 0.55f : paw ? 0.18f : 0.35f) * math.lerp(0.6f, 1.2f, math.saturate(s.Speed / 4f));
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

        /// <summary>
        /// The countryside's sounds, each from something there to find (AmbientLife): songbirds singing in the trees,
        /// a woodpecker drumming, owls hooting, crickets chirping in the grass, grasshoppers buzzing as they jump, frogs
        /// croaking at the water's edge (and a plop as one goes in), cicadas on hot days. Each call as its count goes up.
        /// </summary>
        void Countryside(float3 ear, float night)
        {
            var life = AmbientLife.Near(World, ear);
            // (Frogs near the water at night: their chorus held on under the single croaks, from among them, as loud as
            // there are many of them.)
            var frogs = 0;
            var frogsAt = float3.zero;
            foreach (var a in life)
            {
                if (a.Kind == AmbientKind.Frog && !a.Hidden && math.distance(a.Position, ear) < 40f)
                {
                    frogs++;
                    frogsAt += a.Position;
                }
                if (!ambientCalls.TryGetValue(a.Id, out var was))
                {
                    ambientCalls[a.Id] = (a.Calls, a.Hidden);
                    continue;
                }
                var pitch = 0.9f + (a.Id % 23u) * 0.01f;
                // (Songbirds only every other time: there are a great many of them, and the trees ring with chirps.)
                var thinned = a.Kind == AmbientKind.Songbird && ThinCalls && (((uint)a.Calls + a.Id) & 1u) != 0u;
                if (a.Hidden && !was.hidden && a.Kind == AmbientKind.Frog)
                    GameAudio.PlayOneShot(SoundId.Splash, a.Position, 1.7f, 0.35f, 15f);
                else if (!a.Hidden && a.Calls != was.calls && !thinned && Spend(math.distance(a.Position, ear)) && ++CallsPlayed > 0)
                    switch (a.Kind)
                    {
                        case AmbientKind.Songbird:
                            GameAudio.PlayOneShot(((uint)a.Calls + a.Id) % 5u == 0u ? SoundId.Song : SoundId.Chirp, a.Position, pitch + 0.1f, 0.4f, 30f);
                            break;
                        case AmbientKind.Woodpecker: GameAudio.PlayOneShot(SoundId.Drum, a.Position, pitch, 0.6f, 50f); break;
                        case AmbientKind.Owl: GameAudio.PlayOneShot(SoundId.Owl, a.Position, pitch, 0.5f, 45f); break;
                        case AmbientKind.Cricket: GameAudio.PlayOneShot(SoundId.CricketChirp, a.Position, pitch + 0.1f, 0.3f, 12f); break;
                        case AmbientKind.Grasshopper: GameAudio.PlayOneShot(SoundId.Zip, a.Position, pitch, 0.25f, 10f); break;
                        case AmbientKind.Frog: GameAudio.PlayOneShot(SoundId.Croak, a.Position, pitch, 0.5f, 30f); break;
                        case AmbientKind.Cicada: GameAudio.PlayOneShot(SoundId.Cicada, a.Position, pitch, 0.45f, 40f); break;
                    }
                ambientCalls[a.Id] = (a.Calls, a.Hidden);
            }
            if (ambientCalls.Count > 600)
                ambientCalls.Clear();
            if (frogs > 0 && night > 0.5f)
            {
                var volume = math.min(0.08f * frogs, 0.35f);
                if (chorus == 0)
                    chorus = GameAudio.StartLoop(SoundId.FrogChorus, frogsAt / frogs, volume, 45f, SoundGroup.Creatures);
                else
                    GameAudio.MoveLoop(chorus, frogsAt / frogs, volume);
            }
            else if (chorus != 0)
            {
                GameAudio.StopLoop(chorus);
                chorus = 0;
            }
        }

        /// <summary>The frogs' chorus loop while there are frogs near (its handle; 0 none).</summary>
        int chorus;
    }
}
