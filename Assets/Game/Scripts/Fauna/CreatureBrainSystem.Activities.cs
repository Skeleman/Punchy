using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: needs, Decide, play, hunting. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            // ------------------------------------------------------------------------------- needs

            /// <summary>
            /// Needs rise over time (by the species' tastes: a big grazer gets hungry faster) and fall while the
            /// activity that meets them goes on: grazing or a hunt for hunger, drinking for thirst, rest and sleep for
            /// tiredness, play, a flock or company for loneliness.
            /// </summary>
            void Needs(ref CreatureState s, ref CreatureBlob b, float dt)
            {
                using var profiled = NeedsMarker.Auto();
                M.Unsettled = math.max(M.Unsettled - dt, 0f);
                M.Hunger += dt * (0.002f + (b.Grazing + b.Hunger) * 0.003f);
                M.Thirst += dt * b.Thirst * 0.004f;
                M.Tired += dt * 0.0015f;
                M.Lonely += dt * b.Playfulness * 0.003f;
                switch (s.Mode)
                {
                    case CreatureMode.Graze: M.Hunger -= dt * 0.04f; break;
                    case CreatureMode.Hunt: M.Hunger -= dt * 0.01f; break;
                    case CreatureMode.Drink: if (s.Stage == 1) M.Thirst -= dt * 0.12f; break;
                    case CreatureMode.Rest: M.Tired -= dt * 0.02f; break;
                    case CreatureMode.Sleep: M.Tired -= dt * 0.04f; break;
                    case CreatureMode.Burrow: if (s.Stage == 2) M.Tired -= dt * 0.03f; break;
                    case CreatureMode.Home: if (s.Stage == 2) M.Tired -= dt * 0.04f; break;
                    case CreatureMode.Roost: if (s.Stage == 200) M.Tired -= dt * 0.04f; break;
                    case CreatureMode.Climb: if (s.Stage == 3) M.Tired -= dt * 0.02f; break;
                    case CreatureMode.Play: M.Lonely -= dt * 0.05f; break;
                    case CreatureMode.Greet: M.Lonely -= dt * 0.03f; break;
                    case CreatureMode.Petted: M.Lonely -= dt * 0.08f; break;
                }
                M.Hunger = math.saturate(M.Hunger);
                M.Thirst = math.saturate(M.Thirst);
                M.Tired = math.saturate(M.Tired);
                M.Lonely = math.saturate(M.Lonely);
                // The cue others see (#64): the strongest need past 0.6, none asleep or while held.
                var cue = NeedCue.None;
                if (s.Mode != CreatureMode.Sleep && s.Holder == 0)
                {
                    var strongest = 0.6f;
                    if (M.Tired > strongest) { strongest = M.Tired; cue = NeedCue.Tired; }
                    if (M.Hunger > strongest) { strongest = M.Hunger; cue = NeedCue.Hungry; }
                    if (M.Thirst > strongest) { strongest = M.Thirst; cue = NeedCue.Thirsty; }
                    if (M.Lonely > strongest) cue = NeedCue.Lonely;
                }
                s.Cue = (byte)cue;
                ComfortCheck(ref s, ref b);
            }

            /// <summary>
            /// A-71: the weather's pressure on it, checked every 1-3 s (its own period and phase, so checks spread over
            /// the frames): its discomfort builds while the pressure lasts (about half a minute to full at its worst) and
            /// fades out of it. In a cave or under rock the sky can't reach it.
            /// </summary>
            void ComfortCheck(ref CreatureState s, ref CreatureBlob b)
            {
                if (!b.Comfort.Minds || s.Holder != 0 || Comfort.Off.Data != 0)
                {
                    M.Discomfort = 0f;
                    M.ComfortPressure = 0f;
                    return;
                }
                var h = math.hash(new uint2(s.Individual, 0xC0F0u));
                var period = 1f + 2f * ((h & 0xFFFFu) / 65536f);
                var phase = (h >> 16) / 65536f * period;
                if (math.floor((Now + phase) / period) == math.floor((Now - Step + phase) / period) && SearchEveryFrame.Data == 0)
                    return;
                var dt = SearchEveryFrame.Data != 0 ? Step : period;
                var w = Comfort.WeatherAt(s.Position.xz);
                var underground = underRock || s.Position.y < Terrain.SampleHeight(s.Position.xz) - 2f;
                var cover = CoverNow(in s, ref b);
                M.Cover = cover;
                var pressure = Comfort.Pressure(in b.Comfort, s.Individual, in w, Night, underground, cover, out var kind);
                M.ComfortPressure = pressure;
                M.ShelterRetry -= dt;
                // Row 2: sheltering, it goes on its way once the weather out in the open lets up.
                if (s.Mode == CreatureMode.Shelter && s.Stage >= 1 && s.StageTime > 8f &&
                    Comfort.Pressure(in b.Comfort, s.Individual, in w, Night, underground, ShelterNeed.None, out _) < 0.05f)
                {
                    var rested = new Random(math.hash(new uint2(s.Individual, (uint)(Now * 10f))) | 1u);
                    Pause(ref s, ref b, ref rested);
                }
                // Out in it and bothered enough, it looks for shelter (a few searches a frame, over everyone).
                else if (M.Discomfort > 0.3f && pressure > 0.02f && M.ShelterRetry <= 0f && MaySeekShelter(in s, ref b) &&
                         System.Threading.Interlocked.Increment(ref Shelters.Data.ThisFrame) <= ShelterSearchesPerFrame)
                {
                    var seek = new Random(math.hash(new uint2(s.Individual, (uint)(Now * 10f))) | 1u);
                    // (A-75 row 1: in the midday heat only some folk give in; the hardy half fan themselves and carry on.)
                    var hardy = new Random(math.hash(new uint2(s.Individual, 0x4EA7u)) | 1u);
                    if (FolkRaces.Has(b.Comfort.Race, RaceTrait.Folk) && M.DiscomfortKind is ComfortKind.Hot or ComfortKind.Dry or ComfortKind.Bright &&
                        hardy.NextFloat() < 0.5f)
                    {
                        M.ShelterRetry = seek.NextFloat(40f, 80f);
                        DecideWeatherPose(ref s, ref b, ref seek);
                    }
                    else if (!StartShelter(ref s, ref b, ref seek))
                    {
                        System.Threading.Interlocked.Increment(ref Shelters.Data.NotFound);
                        M.ShelterRetry = seek.NextFloat(20f, 40f);
                        // Row 3: nowhere to go, it puts up with it where it stands.
                        DecideWeatherPose(ref s, ref b, ref seek);
                    }
                }
                // Row 3: waiting out the retry in the open, now and then it pants, shivers or hunches again.
                else if (M.Discomfort > 0.4f && pressure > 0.02f && M.ShelterRetry > 0f && s.Action == IdleAction.None &&
                         s.Mode != CreatureMode.Shelter && MaySeekShelter(in s, ref b))
                {
                    var again = new Random(math.hash(new uint2(s.Individual, (uint)(Now * 10f) ^ 0x5E5Eu)) | 1u);
                    if (again.NextFloat() < 0.2f)
                        DecideWeatherPose(ref s, ref b, ref again);
                }
                // Row 3: out of the rain at last, it shakes the water off (once: the meter drops below the line).
                else if (pressure <= 0.02f && M.DiscomfortKind == ComfortKind.Rain && M.Discomfort > 0.3f && s.Action == IdleAction.None &&
                         MaySeekShelter(in s, ref b) && s.Mode != CreatureMode.Rest && !Flying(in s))
                {
                    SetMode(ref s, CreatureMode.Idle);
                    s.Action = IdleAction.Shake;
                    s.ActionLength = 1.2f;
                    s.Timer = 1.4f;
                    M.Discomfort = 0.29f;
                    System.Threading.Interlocked.Increment(ref Shelters.Data.Posed);
                }
                if (pressure > 0.02f)
                {
                    M.Discomfort = math.min(M.Discomfort + pressure * dt / 30f, 1f);
                    M.DiscomfortKind = kind;
                }
                else
                {
                    M.Discomfort = math.max(M.Discomfort - dt / 20f, 0f);
                    if (M.Discomfort <= 0f)
                        M.DiscomfortKind = ComfortKind.None;
                }
            }

            /// <summary>
            /// A-71 row 3: with no shelter in reach, it stands and shows the weather it's bearing: panting in the heat or
            /// the dry, shivering with arms hugged in the cold or snow, hunched against a storm or the wind (or the rain,
            /// shaking it off now and then), and folk shading their eyes from a glare (animals pant). A few seconds, then
            /// on with its business. Not lying down, nor a flier in the air.
            /// </summary>
            void DecideWeatherPose(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (s.Mode == CreatureMode.Rest || Flying(in s) || !Free(s.Mode) && s.Mode != CreatureMode.Work && s.Mode != CreatureMode.Sit)
                    return;
                var folk = FolkRaces.Has(b.Comfort.Race, RaceTrait.Folk) && b.Upright;
                var pose = M.DiscomfortKind switch
                {
                    ComfortKind.Hot or ComfortKind.Dry => IdleAction.Pant,
                    ComfortKind.Cold or ComfortKind.Snow => rng.NextFloat() < 0.7f ? IdleAction.Shiver : IdleAction.Hunch,
                    ComfortKind.Bright => folk ? IdleAction.ShieldEyes : IdleAction.Pant,
                    ComfortKind.Rain => rng.NextFloat() < 0.4f ? IdleAction.Shake : IdleAction.Hunch,
                    ComfortKind.Storm or ComfortKind.Wind => IdleAction.Hunch,
                    _ => IdleAction.None
                };
                if (pose == IdleAction.None)
                    return;
                SetMode(ref s, CreatureMode.Idle);
                s.Action = pose;
                s.ActionLength = pose == IdleAction.Shake ? 1.2f : rng.NextFloat(3f, 6f);
                s.Timer = s.ActionLength + 0.3f;
                System.Threading.Interlocked.Increment(ref Shelters.Data.Posed);
            }

            /// <summary>How much a need pushes an activity: a little even when met, a lot when pressing.</summary>
            static float Urge(float need) => 0.25f + need * need * 3f;

            // ------------------------------------------------------------------------------- choosing what to do

            /// <summary>How much this is its time to sleep (0..1).</summary>
            float SleepTime(ref CreatureBlob b) =>
                b.Active == ActiveTime.Day ? Night : b.Active == ActiveTime.Night ? 1f - Night : 0f;

            /// <summary>
            /// The weighted activities, in the order the roll is spent on them. Those marked * can fail to start (no
            /// water, tree, web or flight found); then the roll, already spent, falls to the next * one that has any
            /// weight, and at last to a wander.
            /// </summary>
            enum Weighted : byte { Work, Home, Hunt, Play, Sleep, Rest, Graze, Drink, Climb, Swim, Lounge, Web, Fly, Surface, Count }

            /// <summary>
            /// Chooses what to do next (#60). Every activity, its condition, and what decides it, in priority order: the
            /// first of the forced, then the urgent, that applies starts and ends the choice; otherwise the weighted roll.
            /// <code>
            /// tier     activity     condition                                                  weight / chance      start
            /// forced   Shield       light shunner, no goggles, a light near (LightNear)        always               Idle + ShieldEyes
            /// forced   Flit        drawn to light                                             always               Flit
            /// forced   Breach       breaches, awake                                            30%                  soar (Wander 2)
            /// forced   Patrol       guard on shift / night guard by day                        always               StartPatrol / Home, Sleep
            /// forced   Hermit       hermit, its day at the shelter                             HermitDay            HermitDay
            /// forced   Camper       traveller, its night at camp                               CamperNight          CamperNight
            ///          (a fisher with no spot looks for one; none, and it's townsfolk)
            /// forced   Rooted       rooted                                                     always               Sleep / Idle
            /// forced   Disguised    passes for a plant                                         asleep, or 85%       Sleep / Rest
            ///          (senses: kin, prey, nest; then the weights below)
            /// forced   Nest         walker away from its nest in its sleeping time             always               Wander to the nest
            /// urgent   HuntGame     hunter by day, game within 100 m                           60%                  Hunt (stalk)
            /// urgent   Raid         hive raider by day, cooldown over                          8%                   StartRaid
            /// urgent   PlayRing     child by day, homed                                        30%                  StartPlayRing
            /// urgent   Hearth       townsfolk at dusk, room at the fire                        80%                  StartHearth
            ///          (the roll is drawn here)
            /// urgent   Greet        friendly, a person near its range, awake                   35% + lonely 60%     Greet
            /// urgent   Roost        roosting species                                           always               StartRoost / NightLoop
            /// urgent   Burrow       burrower in its sleeping time with sleep weight            has or digs one      Burrow
            /// urgent   Chores       pygmy under the rock / bat folk on the surface, awake      45% / 60%            pool or forage Work / carry goods home
            /// weighted Work         posted task                                               6 day, 10 night*     Work   (*0 for day work)
            /// weighted Home         townsfolk with a door (not the watch)                      14 night, 0.15 day   Home
            /// weighted Hunt         prey found (fierce enough)                                 hunger x2.5 (x1.5 near) Hunt
            /// weighted Play         kin to play with                                           play x2 x lonely     Play
            /// weighted Sleep        its sleeping time / always-active and lazy                 6 / lazy x0.4        Sleep
            /// weighted Rest         -                                                          lazy x0.7 x tired    Rest
            /// weighted Graze        on the ground                                              graze x2.5 x hunger  Graze
            /// weighted Drink*       on the ground, not a flier                                 thirst x1.2 x thirst FindWater / FindCaveWater
            /// weighted Climb*       climber, flora                                             climb x2.5 (8 asleep) StartClimb
            /// weighted Swim*        homed townsfolk at midday                                  0.12 + wades x1.2    StartSwim
            /// weighted Lounge*      homed townsfolk by day                                     0.2                  StartLounge
            /// weighted Web*         web spinner                                                6                    GoToWeb
            /// weighted Fly*         flier                                                      1.2 (2 perched, 8 to roost) StartFlight
            /// weighted Surface*     light shunner under the rock, awake                        surface trips x0.5   StartSurface (goggles by day)
            /// fallback Wander       on the ground                                              1 (in the roll)      Wander (cave crawl, sunny rock, far shore)
            /// </code>
            /// Night overrides zero some weights (posted folk and townsfolk going in, birds going to roost). The roll is
            /// drawn after the hearth and before the greet, as it always was, so every creature's random draws (and so its
            /// choices) are the same as before the table.
            /// </summary>
            void Decide(Entity self, ref CreatureState s, ref CreatureBlob b, int species, ref Random rng, bool perched, float3 player)
            {
                using var profiled = DecideMarker.Auto();
                var sleepy = SleepTime(ref b);
                var awake = 1f - sleepy * 0.8f;
                var flier = b.Habitat == Habitat.Flies;

                // ---- forced (before the senses)
                if (DecideShield(ref s, ref b, ref rng))
                    return;
                if (DecideFlit(ref s, ref b, ref rng) || DecideBreach(ref s, ref b, ref rng, sleepy) || DecidePatrol(ref s, ref rng))
                    return;
                if (s.Task == CreatureTask.Hermit && HermitDay(ref s, ref b, ref rng, sleepy))
                    return;
                if (s.Task == CreatureTask.Camper && CamperNight(ref s, ref rng))
                    return;
                // (A fisher finds its spot the first time: none within reach, and it's just one of the townsfolk.)
                if (s.Task == CreatureTask.Fish && sleepy < 0.5f && math.all(s.WorkSpot == float2.zero) && !FindFishingSpot(ref s))
                    s.Task = CreatureTask.None;
                if (DecideRooted(ref s, ref b, ref rng, sleepy) || DecideDisguised(ref s, ref b, ref rng, sleepy))
                    return;

                // ---- senses, and the weights
                var playmate = Entity.Null;
                // (A critter runs light: no company to look for.)
                if (b.Playfulness > 0f && sleepy < 0.5f && !perched && s.Task != CreatureTask.Critter)
                    FindKin(self, ref s, species, 15f, out playmate);
                var prey = Entity.Null;
                // (Only the fierce ones of a species with a hunting threshold: a dog chases hares if it's aggressive, #50.)
                if (b.Prey.Length > 0 && b.Hunger > 0f && sleepy < 0.5f && CreaturePersonality.WillHunt(ref b, s.Individual))
                    FindPrey(self, ref s, ref b, flier ? 60f : 35f, out prey);
                // (A pygmy's chore is one stint, not a post: done with it, it isn't drawn back to the spot or kept up at night.)
                if (b.Blind && b.CaveDweller && b.Hands && s.Mode != CreatureMode.Work)
                    s.Task = CreatureTask.None;
                var w = new FixedList64Bytes<float>();
                w.Length = (int)Weighted.Count;
                var wander = Weights(ref s, ref b, ref w, sleepy, awake, flier, perched, playmate, prey, out var awayFromNest);

                // ---- forced (after the senses)
                if (!flier && awayFromNest && sleepy > 0.5f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = s.Anchor.xz;
                    return;
                }

                // ---- urgent
                if (s.Task == CreatureTask.Hunt && sleepy < 0.5f && rng.NextFloat() < 0.6f && FindGame(self, ref s, 100f, out var game))
                {
                    SetMode(ref s, CreatureMode.Hunt);
                    s.Partner = game;
                    s.Stage = FolkHunting.Stalk;
                    s.Timer = rng.NextFloat(60f, 100f);
                    return;
                }
                // A bear by day, now and then, goes after a wild hive's honey (rarely: after a raid it leaves hives be a
                // long while).
                if (b.RaidsHives && sleepy < 0.3f && M.ChaseCooldown <= 0f && rng.NextFloat() < 0.08f && StartRaid(ref s, ref rng))
                    return;
                // Children by day now and then sit down in a ring on a green to play with their sticks and stones.
                if (b.Child && sleepy < 0.3f && Night < 0.4f && s.Leash > 0f && rng.NextFloat() < 0.3f && StartPlayRing(self, ref s, ref rng))
                    return;
                // Townsfolk gather round the town's fire at dusk, as many as it has room for (the rest go in as usual).
                if (Gathering(in s, ref b) && rng.NextFloat() < 0.8f && StartHearth(self, ref s, ref rng))
                    return;
                // (The sum in its old order, so the roll lands on the same activity for the same draw.)
                var roll = rng.NextFloat(wander + w[(int)Weighted.Graze] + w[(int)Weighted.Rest] + w[(int)Weighted.Sleep] + w[(int)Weighted.Drink] +
                                         w[(int)Weighted.Play] + w[(int)Weighted.Hunt] + w[(int)Weighted.Work] + w[(int)Weighted.Climb] +
                                         w[(int)Weighted.Web] + w[(int)Weighted.Fly] + w[(int)Weighted.Home] + w[(int)Weighted.Swim] +
                                         w[(int)Weighted.Lounge] + w[(int)Weighted.Surface]);
                if (DecideGreet(ref s, ref b, ref rng, sleepy, player) || DecideRoost(ref s, ref b, ref rng, sleepy) ||
                    DecideBurrow(ref s, ref b, ref rng, sleepy, w[(int)Weighted.Sleep]) || DecideChamberChores(ref s, ref b, ref rng, sleepy))
                    return;

                // ---- weighted
                for (int i = 0; i < (int)Weighted.Count; i++)
                {
                    if (w[i] <= 0f || (roll -= w[i]) >= 0f)
                        continue;
                    if (StartWeighted((Weighted)i, self, ref s, ref b, ref rng, sleepy, flier, playmate, prey))
                        return;
                }
                WanderOff(ref s, ref b, ref rng, flier, !perched);
            }

            /// <summary>
            /// The weighted activities' appeal (the species' taste for each, pressed by the need it meets), into w by
            /// Weighted; returns the wander's. Picks a nester's nest the first time, and says whether it's away from it.
            /// </summary>
            float Weights(ref CreatureState s, ref CreatureBlob b, ref FixedList64Bytes<float> w, float sleepy, float awake, bool flier,
                          bool perched, Entity playmate, Entity prey, out bool awayFromNest)
            {
                // On a branch, a bird can only rest, sleep or fly off.
                var ground = perched ? 0f : 1f;
                var wander = 1f * awake * ground;
                var graze = b.Grazing * 2.5f * awake * ground * Urge(M.Hunger);
                var rest = b.Laziness * 0.7f * Urge(M.Tired);
                var sleep = sleepy > 0.5f ? 6f : b.Active == ActiveTime.Always ? b.Laziness * 0.4f * Urge(M.Tired) : 0f;
                var drink = b.Thirst * 1.2f * awake * (!perched && !flier ? 1f : 0f) * Urge(M.Thirst);
                var play = playmate != Entity.Null ? b.Playfulness * 2f * Urge(M.Lonely) : 0f;
                var hunt = prey != Entity.Null ? b.Hunger * 2.5f * Urge(M.Hunger) : 0f;
                // (Keener with prey close: Z-53, so more chases start where they can be seen.)
                if (prey != Entity.Null && Lookup.TryGetValue(prey, out var preyAt) && math.distance(States[preyAt].Position.xz, s.Position.xz) < 20f)
                    hunt *= 1.5f;
                // Climbers go up trees to sit, and to sleep (they sleep up there, not on the ground).
                var climb = b.Climbing > 0f && HasFlora && !flier ? b.Climbing * (sleepy > 0.5f ? 8f : 2.5f) : 0f;
                // Townsfolk sit a while by the well, or lie back on the ground near it, by day.
                var lounge = b.Upright && b.Hands && s.Leash > 0f && sleepy < 0.3f && Night < 0.3f && s.Task != CreatureTask.Patrol ? 0.2f * awake : 0f;
                // Townsfolk now and then go for a swim, in the warm middle of the day.
                // (Water folk wade and swim in the shallows near home far more often: #10.)
                var swim = b.Upright && b.Hands && s.Leash > 0f && sleepy < 0.2f && Night < 0.2f && s.Task != CreatureTask.Patrol ? (0.12f + b.Wades * 1.2f) * awake : 0f;
                if (climb > 0f && sleepy > 0.5f)
                    sleep *= 0.2f;
                // Web spinners spend most of their time in their web.
                var web = b.SpinsWebs ? 6f : 0f;
                // Birds fly off somewhere new more often from a branch; to sleep, they go up into a tree first.
                var fly = flier ? (perched ? 2f : 1.2f) * awake : 0f;
                // Nesters pick their nest the first time it's needed, and go home to it to sleep.
                if (b.Nests != NestSite.None && s.AnchorSize == 0f)
                    FindNest(ref s, ref b);
                var nest = b.Nests != NestSite.None && s.AnchorSize > 0f;
                awayFromNest = nest && math.distance(s.Position, s.Anchor) > 0.5f;
                if (flier && sleepy > 0.5f && HasFlora && (!perched || awayFromNest))
                {
                    fly = 8f;
                    sleep = 0f;
                }
                else if (nest && sleepy > 0.5f && !awayFromNest)
                    fly = 0f;   // home in the nest: stays put till morning
                // (Going home to the nest is forced: stop before FindDoor, which writes to the state, as before.)
                if (!flier && awayFromNest && sleepy > 0.5f)
                    return wander;
                // Workers mostly work by day, taking a short wander or rest between stints; at night they doze at
                // their post.
                // (Hunters have no post: by day they go out after game, at night they sleep at home.)
                var posted = s.Task != CreatureTask.None && s.Task != CreatureTask.Hunt && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Critter;
                // (A fisher fishes by day only: at night it sleeps at home like anyone.)
                var dayWork = s.Task == CreatureTask.Fish || s.Task == CreatureTask.Mine;
                var work = posted ? sleepy > 0.5f ? (dayWork ? 0f : 10f) : 6f : 0f;
                if (posted && sleepy > 0.5f && !dayWork)
                    sleep = 0f;
                // Townsfolk go indoors through their door: for the night (all but the watch), and now and then by day.
                var home = 0f;
                if (b.Upright && s.Leash > 0f && s.Task != CreatureTask.Watch && FindDoor(ref s))
                {
                    home = sleepy > 0.5f ? 14f : 0.15f * awake;
                    if (sleepy > 0.5f)
                        work = sleep = 0f;
                }
                w[(int)Weighted.Work] = work;
                w[(int)Weighted.Home] = home;
                w[(int)Weighted.Hunt] = hunt;
                w[(int)Weighted.Play] = play;
                w[(int)Weighted.Sleep] = sleep;
                w[(int)Weighted.Rest] = rest;
                w[(int)Weighted.Graze] = graze;
                w[(int)Weighted.Drink] = drink;
                w[(int)Weighted.Climb] = climb;
                w[(int)Weighted.Swim] = swim;
                w[(int)Weighted.Lounge] = lounge;
                w[(int)Weighted.Web] = web;
                w[(int)Weighted.Fly] = fly;
                // Bat folk now and then walk up out of their cave (#35): goggles on by day (UpdateHeld), bare at night.
                // (Last in the roll's sum and 0 for everyone else, so their draws are unchanged.)
                w[(int)Weighted.Surface] = b.SurfaceTrips > 0f && Below(s, ref b) && sleepy < 0.5f ? b.SurfaceTrips * 0.5f * awake : 0f;
                return wander;
            }

            /// <summary>
            /// A light shunner's forced entry (#35): with no goggles on and a light near (LightNear), it stops and covers
            /// its face with an arm for a few seconds, then looks again (a short wait, so it keeps covering while the
            /// light stays).
            /// </summary>
            bool DecideShield(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (!b.ShunsLight || s.Held == HeldItem.Goggles || !LightNear(in s, ref b))
                    return false;
                SetMode(ref s, CreatureMode.Idle);
                s.Action = IdleAction.ShieldEyes;
                s.ActionTime = 0f;
                s.ActionLength = rng.NextFloat(3f, 6f);
                s.Timer = 0.5f;
                return true;
            }

            /// <summary>Wander's stage for a bat folk carrying surface goods home (DecideChamberChores; a sack in UpdateHeld).</summary>
            public const byte CarryGoods = 110;

            /// <summary>
            /// The chamber folk's shared round (#35): bat folk and blind pygmies live in one chamber. A pygmy awake under the
            /// rock now and then tends the chamber's pool (a fisher at its rim, from FindCaveWater) or forages the floor near
            /// its home; a bat folk up on the surface heads home with goods (a sack, once out of the sun: UpdateHeld). Only
            /// cave dwellers with hands draw here, so every other creature's choices are unchanged.
            /// </summary>
            bool DecideChamberChores(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!b.Hands || !b.CaveDweller || sleepy >= 0.5f || s.Leash <= 0f)
                    return false;
                var below = Below(s, ref b);
                if (b.Blind && below)
                {
                    var chore = rng.NextFloat();
                    if (chore < 0.25f && FolkRaces.Has(b.Comfort.Race, RaceTrait.Fishing) && FindCaveWater(ref s, ref b, ref rng, out var shore, out var water))
                        return StartChore(ref s, ref rng, CreatureTask.Fish, FolkJob.PoolFisher, shore, water);
                    if (chore < 0.45f)
                    {
                        var spot = s.Home + rng.NextFloat2Direction() * rng.NextFloat(1f, math.clamp(s.Leash * 0.6f, 1.5f, 8f));
                        if (CaveFloor(spot, s.Position.y))
                            return StartChore(ref s, ref rng, CreatureTask.Tend, FolkJob.Forager, spot, spot + rng.NextFloat2Direction());
                    }
                    return false;
                }
                if (b.ShunsLight && !below && rng.NextFloat() < 0.6f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = s.Home;
                    s.Stage = CarryGoods;
                    s.Timer = rng.NextFloat(90f, 150f);
                    return true;
                }
                return false;
            }

            /// <summary>A chore as a short stint of work at a spot (its task and job for the pose), done when the timer runs out.</summary>
            static bool StartChore(ref CreatureState s, ref Random rng, CreatureTask task, FolkJob job, float2 spot, float2 face)
            {
                SetMode(ref s, CreatureMode.Work);
                s.Task = task;
                s.Job = (byte)job;
                s.WorkSpot = spot;
                s.WorkFace = face;
                s.Timer = rng.NextFloat(30f, 80f);
                return true;
            }

            /// <summary>
            /// Weighted Surface (#35): off up its cave to the mouth (the cave whose passage is nearest), for a look
            /// outside; false if it isn't in a cave. (Steer takes it along the passage node by node: PassageWay, A-69.)
            /// </summary>
            bool StartSurface(ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Caves.IsCreated)
                    return false;
                ref var set = ref Terrain.Caves.Value;
                var best = 900f;
                var mouth = float3.zero;
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    ref var cave = ref set.Caves[c];
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                    {
                        var d = math.distancesq(set.Nodes[n].Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            mouth = cave.Mouth;
                        }
                    }
                }
                if (best >= 900f)
                    return false;
                SetMode(ref s, CreatureMode.Wander);
                s.Target = mouth.xz;
                s.Timer = rng.NextFloat(60f, 120f);
                return true;
            }

            /// <summary>Starts a weighted activity the roll landed on; false if it found nowhere to do it (the * ones).</summary>
            bool StartWeighted(Weighted a, Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy, bool flier, Entity playmate, Entity prey)
            {
                switch (a)
                {
                    case Weighted.Work:
                        SetMode(ref s, CreatureMode.Work);
                        s.Timer = s.Task == CreatureTask.Fish ? rng.NextFloat(120f, 300f) : rng.NextFloat(40f, 120f);
                        // (A-73: a village fisher works on its wharf when it has one, else on its own bit of shore. A-81: the
                        // wharf is for a fishing race, the trait, not any one race.)
                        s.PierSpot = 0;
                        if (s.Task == CreatureTask.Fish && FolkRaces.Has(b.Comfort.Race, RaceTrait.Fishing))
                            FindWharf(ref s, ref rng);
                        return true;
                    case Weighted.Home:
                        SetMode(ref s, CreatureMode.Home);
                        // (At night it stays in till morning: the time only runs by day.)
                        s.Timer = rng.NextFloat(15f, 45f);
                        return true;
                    case Weighted.Hunt:
                        SetMode(ref s, CreatureMode.Hunt);
                        s.Partner = prey;
                        s.Timer = b.ChaseTime * rng.NextFloat(0.7f, 1.3f);
                        if (flier)
                            s.Stage = 1;
                        return true;
                    case Weighted.Play:
                        SetMode(ref s, CreatureMode.Play);
                        s.Partner = playmate;
                        s.Timer = rng.NextFloat(12f, 25f);
                        s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
                        return true;
                    case Weighted.Sleep:
                        SetMode(ref s, CreatureMode.Sleep);
                        s.Timer = sleepy > 0.5f ? rng.NextFloat(90f, 300f) : rng.NextFloat(20f, 60f);
                        return true;
                    case Weighted.Rest:
                        SetMode(ref s, CreatureMode.Rest);
                        s.Timer = rng.NextFloat(12f, 40f);
                        return true;
                    case Weighted.Graze:
                        SetMode(ref s, CreatureMode.Graze);
                        s.Timer = rng.NextFloat(8f, 25f);
                        return true;
                    case Weighted.Drink:
                        if (!(CaveWalker(s, ref b) ? FindCaveWater(ref s, ref b, ref rng, out var shore, out var water)
                                                   : FindWater(ref s, ref b, ref rng, out shore, out water)))
                            return false;
                        SetMode(ref s, CreatureMode.Drink);
                        s.Target = shore;
                        s.Focus = water;
                        return true;
                    case Weighted.Climb: return StartClimb(ref s, ref b, ref rng, sleepy > 0.5f);
                    case Weighted.Swim: return StartSwim(ref s, ref b, ref rng);
                    case Weighted.Lounge: return StartLounge(self, ref s, ref rng);
                    case Weighted.Web: return GoToWeb(ref s, ref b, ref rng);
                    case Weighted.Fly: return StartFlight(ref s, ref b, ref rng, sleepy > 0.5f);
                    case Weighted.Surface: return StartSurface(ref s, ref rng);
                }
                return false;
            }

            /// <summary>The fallback: off somewhere new nearby (a cave crawler over the rock, a lizard to sunny rock, a
            /// swimmer to the far shore), within its home range; a bird on a branch just waits.</summary>
            void WanderOff(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool flier, bool onGround)
            {
                if (!onGround)
                {
                    s.Timer = 1f;
                    return;
                }
                var candidate = s.Position.xz + rng.NextFloat2Direction() * b.WanderRadius * rng.NextFloat(0.3f, 1f) * (flier ? 0.15f : 1f);
                // Animals that swim now and then set off for the far side of water in front of them.
                if (b.CrossesWater && s.Leash <= 0f && rng.NextFloat() < 0.3f && FarShore(s.Position.xz, ref rng, out var farShore))
                    candidate = farShore;
                // Animals with a home stay near it.
                if (s.Leash > 0f && math.distance(candidate, s.Home) > s.Leash)
                    candidate = s.Home + rng.NextFloat2Direction() * s.Leash * rng.NextFloat(0.1f, 0.8f);
                // A crawler in a cave: off over the rock, onto a wall or the ceiling as likely as the floor.
                if (b.Crawls && CaveWalker(s, ref b))
                {
                    if (CrawlTarget(s, ref b, ref rng, out var spot))
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        s.Target = spot.xz;
                        s.TargetHeight = spot.y;
                    }
                    else
                        s.Timer = 0.5f;
                }
                // A lizard outside makes for steep, sunny rock (of a few places, the steepest).
                else if (b.Crawls && b.Habitat == Habitat.Land && CaveWalkerNot(s, ref b) && SteepestOf(s.Position.xz, b.WanderRadius, ref rng, out var face))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = face;
                }
                else if ((CaveWalker(s, ref b) ? CaveFloor(candidate, s.Position.y) : b.Habitat == Habitat.Land ? OpenGround(candidate) : Walkable(candidate, b.Habitat)) &&
                    (!b.LavaDweller || InLava(candidate)))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = candidate;
                }
                else
                    s.Timer = 0.5f;
            }

            // ---- the forced and urgent entries: each starts its activity and returns true if it applies.

            bool DecideFlit(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (!b.DrawnToLight)
                    return false;
                Flit(ref s, ref rng);
                return true;
            }

            /// <summary>A sky serpent, awake under the water, now and then bursts up and out to soar a while.</summary>
            bool DecideBreach(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!(b.Breaches && sleepy < 0.5f && rng.NextFloat() < 0.3f))
                    return false;
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 2;
                s.Focus = s.Position.xz;
                s.TargetHeight = Cruise(s, ref b, s.Focus);
                s.Timer = rng.NextFloat(180f, 480f);   // (long spells: it spends much of its day in the air)
                s.Target = s.Focus;
                return true;
            }

            /// <summary>A guard on shift goes (back) on its rounds; a night guard off shift sleeps the day away indoors (a
            /// day guard off shift has its evening and goes home to bed like anyone).</summary>
            bool DecidePatrol(ref CreatureState s, ref Random rng)
            {
                if (s.Task != CreatureTask.Patrol)
                    return false;
                if (OnShift(s) && StartPatrol(ref s))
                    return true;
                if (!(NightShift(s) && Night < 0.45f))
                    return false;
                if (FindDoor(ref s))
                {
                    SetMode(ref s, CreatureMode.Home);
                    s.Timer = rng.NextFloat(300f, 600f);
                }
                else
                {
                    SetMode(ref s, CreatureMode.Sleep);
                    s.Timer = rng.NextFloat(120f, 300f);
                }
                return true;
            }

            /// <summary>Rooted to the spot: it only waits, awake or (in its sleeping hours) closed up asleep.</summary>
            bool DecideRooted(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!b.Rooted)
                    return false;
                SetMode(ref s, sleepy > 0.5f ? CreatureMode.Sleep : CreatureMode.Idle);
                s.Timer = sleepy > 0.5f ? rng.NextFloat(90f, 300f) : rng.NextFloat(5f, 15f);
                return true;
            }

            /// <summary>Passing for a plant: it mostly sits still, and only now and then (in its waking hours) creeps off
            /// somewhere new.</summary>
            bool DecideDisguised(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!(b.Disguised && (sleepy > 0.5f || rng.NextFloat() < 0.85f)))
                    return false;
                SetMode(ref s, sleepy > 0.5f ? CreatureMode.Sleep : CreatureMode.Rest);
                s.Timer = rng.NextFloat(30f, 90f);
                return true;
            }

            /// <summary>Friendly animals go over to a person nearby (within their home range) more often than not.
            /// (Someone arriving at the edge of its home range counts: it goes as far as its range lets it.)</summary>
            bool DecideGreet(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy, float3 player)
            {
                var nearPerson = math.distance(player.xz, s.Position.xz) < 35f &&
                                 (s.Leash <= 0f || math.distance(player.xz, s.Home) < s.Leash + 15f);
                if (!(b.Friendly && nearPerson && sleepy < 0.5f && rng.NextFloat() < 0.35f + M.Lonely * 0.6f))
                    return false;
                SetMode(ref s, CreatureMode.Greet);
                s.Timer = rng.NextFloat(20f, 45f);
                return true;
            }

            /// <summary>Roosting species: hang up to sleep through their sleeping time, and otherwise stay on the wing.
            /// (Startled off its roost, it flies about a while before it settles again.)</summary>
            bool DecideRoost(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!b.Roosts)
                    return false;
                if (sleepy > 0.5f && M.Unsettled <= 0f)
                    StartRoost(ref s, ref b, ref rng);
                else
                    NightLoop(ref s, ref b, ref rng);
                return true;
            }

            /// <summary>Burrowers sleep down their burrow (digging one where they are if they have none yet).</summary>
            bool DecideBurrow(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy, float sleep)
            {
                if (!b.Burrows)
                    return false;
                if (s.AnchorSize < 0f && s.Leash <= 0f &&
                    math.any((float2)s.Anchor.xz != (math.floor(s.Position.xz / 16f) + 0.5f) * 16f))
                    s.AnchorSize = 0f;   // somewhere new since it last found no room: look again
                if (sleep > 0f && sleepy > 0.5f && (s.AnchorSize > 0f || s.AnchorSize == 0f && DigBurrow(ref s, ref b, ref rng)))
                {
                    SetMode(ref s, CreatureMode.Burrow);
                    s.Timer = rng.NextFloat(120f, 400f);
                    return true;
                }
                if (s.AnchorSize == 0f)
                    DigBurrow(ref s, ref b, ref rng);
                return false;
            }

            static bool Free(CreatureMode mode) =>
                mode == CreatureMode.Idle || mode == CreatureMode.Wander || mode == CreatureMode.Graze || mode == CreatureMode.Rest;

            static void SetMode(ref CreatureState s, CreatureMode mode)
            {
                s.Mode = mode;
                s.ModeTime = 0f;
                s.Stage = 0;
                s.StageTime = 0f;
                s.Partner = Entity.Null;
                s.Action = IdleAction.None;
                s.ActionTime = 0f;
            }

            static void NextStage(ref CreatureState s)
            {
                s.Stage++;
                s.StageTime = 0f;
            }

            static void Pause(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Idle);
                s.Timer = rng.NextFloat(b.IdleTime.x, math.max(b.IdleTime.x, b.IdleTime.y));
            }

            // ------------------------------------------------------------------------------- idle actions

            /// <summary>
            /// While standing, lying or sitting about (on the ground, on a branch), now and then starts one of its
            /// species' little actions, carries it through, and takes up the posture it needs.
            /// </summary>
            void Fidget(Entity self, int species, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool perched,
                        ref float sit, ref float vertical, ref float bow, ref float desiredLook)
            {
                using var profiled = FidgetMarker.Auto();
                var lying = s.Mode == CreatureMode.Rest && s.Lie > 0.5f;
                // (Talking with someone: hands and head are busy with that, A-26.)
                var calm = s.Mode == CreatureMode.Idle && !InChat(s) || s.Mode == CreatureMode.Rest ||
                           s.Mode == CreatureMode.Climb && s.Stage == 3 ||
                           s.Mode == CreatureMode.Greet && s.Speed < 0.2f;
                // (Being petted or held, it carries on with what it was given to do: a wag, a wriggle; nothing new. A
                // guard drawing its weapon or putting it away finishes, whatever it's doing.)
                var keeps = calm || s.Mode == CreatureMode.Petted || s.Mode == CreatureMode.Held ||
                            s.Action == IdleAction.Draw || s.Action == IdleAction.Sheathe ||
                            s.Mode == CreatureMode.Sit && s.Stage == CreatureAnimator.PlayRing;
                if (s.Action != IdleAction.None)
                {
                    s.ActionTime += dt / math.max(s.ActionLength, 0.1f);
                    if (s.ActionTime >= 1f || !keeps)
                    {
                        s.Action = IdleAction.None;
                        s.ActionTime = 0f;
                    }
                }
                else if (calm && b.Idles != IdleActions.None && s.Speed < 0.1f && !(b.Disguised && s.Mode == CreatureMode.Rest) &&
                         rng.NextFloat() < dt * b.Fidgets * 0.15f)
                    StartAction(ref s, ref b, ref rng, lying, perched || s.Mode == CreatureMode.Climb, Night, HowlTime(in s, Hour));
                // A howl is taken up by the rest of the pack, one after another (in the pack's own time for it).
                else if (calm && !lying && (b.Idles & IdleActions.Howl) != 0 && s.Speed < 0.1f && Night > 0.5f && HowlTime(in s, Hour) &&
                         rng.NextFloat() < dt * 0.8f && Howling(self, ref s, species))
                {
                    s.Action = IdleAction.Howl;
                    s.ActionTime = 0f;
                    s.ActionLength = rng.NextFloat(4f, 7f);
                }

                var t = s.ActionTime;
                switch (s.Action)
                {
                    case IdleAction.Sit:
                        sit = 1f;
                        break;
                    case IdleAction.SitUp:
                        sit = 1f;
                        vertical = 0.5f;
                        desiredLook = math.sin(s.ModeTime * 1.3f) * 1.1f;
                        break;
                    case IdleAction.Scratch:
                        sit = math.max(sit, 0.8f);
                        break;
                    case IdleAction.Howl:
                        sit = 1f;
                        desiredLook = 0f;
                        break;
                    case IdleAction.Stretch:
                        bow = t < 0.5f ? 1f : 0f;
                        break;
                    case IdleAction.LookAround:
                        desiredLook = math.sin(s.ModeTime * 1.7f + s.Individual % 5) * 1.15f;
                        break;
                }
            }

            /// <summary>Picks one of the species' idle actions that suits how it is now (lying, perched or standing).</summary>
            /// <summary>
            /// Whether it's a pack's time to howl: half an hour once a night, somewhere between 9 at night and 3 in the
            /// morning, the same for every wolf from one home (a lone one's from its own individual), so a night's
            /// howling is one chorus now and then rather than all night long.
            /// </summary>
            static bool HowlTime(in CreatureState s, float hour)
            {
                var key = math.any(s.Home != float2.zero) ? math.hash((int2)math.round(s.Home)) : s.Individual;
                var start = 21f + key % 60u / 10f;
                var h = hour < 12f ? hour + 24f : hour;
                return h >= start && h < start + 0.5f;
            }

            static void StartAction(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool lying, bool aloft, float night, bool howlTime)
            {
                var options = new FixedList64Bytes<byte>();
                for (int a = 1; a <= (int)IdleAction.Howl; a++)
                {
                    var flag = (IdleActions)(1 << (a - 1));
                    if ((b.Idles & flag) == 0)
                        continue;
                    var action = (IdleAction)a;
                    // Lying down, only what can be done lying down; up a tree or on a branch, nothing that needs the
                    // ground.
                    if (lying && action != IdleAction.Groom && action != IdleAction.TailFlick && action != IdleAction.LookAround &&
                        action != IdleAction.Preen)
                        continue;
                    if (aloft && (action == IdleAction.PawGround || action == IdleAction.Sniff || action == IdleAction.Stretch ||
                                  action == IdleAction.SitUp || action == IdleAction.HeadBob || action == IdleAction.Howl))
                        continue;
                    // Howling is for the night (and not lying down), in the pack's time for it.
                    if (action == IdleAction.Howl && (night < 0.5f || lying || !howlTime))
                        continue;
                    options.Add((byte)a);
                }
                if (options.Length == 0)
                    return;
                s.Action = (IdleAction)options[rng.NextInt(options.Length)];
                s.ActionTime = 0f;
                s.ActionLength = s.Action switch
                {
                    IdleAction.PawGround => 2.4f,
                    IdleAction.Groom => rng.NextFloat(3f, 6f),
                    IdleAction.Scratch => 2.2f,
                    IdleAction.Shake => 1.1f,
                    IdleAction.Stretch => 3f,
                    IdleAction.Sniff => 2.5f,
                    IdleAction.Sit => rng.NextFloat(6f, 15f),
                    IdleAction.SitUp => rng.NextFloat(4f, 9f),
                    IdleAction.TailFlick => 1.6f,
                    IdleAction.Preen => rng.NextFloat(2.5f, 5f),
                    IdleAction.HeadBob => 1.4f,
                    IdleAction.Howl => rng.NextFloat(4f, 7f),
                    _ => 3f
                };
            }

            // ------------------------------------------------------------------------------- two-animal activities

            /// <summary>
            /// Chase in fun. The chaser play-bows, then runs at its partner; catching up swaps the roles (the runner
            /// that has been running longest turns chaser, so both agree). Ends when the time is up or the partner
            /// stops playing.
            /// </summary>
            void Play(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng,
                      ref float targetSpeed, ref float desiredLook, ref float bow, ref bool moving)
            {
                s.Timer -= Dt;
                if (!Lookup.TryGetValue(s.Partner, out var p) || s.Timer <= 0f ||
                    s.ModeTime > 1.5f && (States[p].Mode != CreatureMode.Play || States[p].Partner != self))
                {
                    if (rng.NextFloat() < 0.4f)
                    {
                        SetMode(ref s, CreatureMode.Rest);
                        s.Timer = rng.NextFloat(10f, 25f);
                    }
                    else
                        Pause(ref s, ref b, ref rng);
                    return;
                }

                var other = States[p];
                var to = other.Position.xz - s.Position.xz;
                var distance = math.length(to);
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                moving = true;
                var walk = b.WalkSpeed * s.Scale;
                var run = b.RunSpeed * s.Scale;

                if (s.Stage == 0)
                {
                    s.Target = other.Position.xz;
                    if (s.StageTime < 1f)
                    {
                        // Play bow, then off. It holds its heading while it bows (the head follows the partner): a
                        // partner close by and running round it turned it on the spot (Z-07).
                        bow = 1f;
                        s.Target = s.Position.xz + new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    }
                    else
                    {
                        targetSpeed = math.lerp(walk, run, 0.75f);
                        var reach = (b.BodyLength * s.Scale + Bodies[p].Blob.Value.BodyLength * other.Scale) * 0.6f + 0.3f;
                        // (A near miss counts too: a partner that's run close past it, beside or behind, within a few
                        // reaches. Otherwise one running round it turned it on the spot, Z-07.)
                        var bearing = math.abs(WrapAngle(math.atan2(to.x, to.y) - s.Yaw));
                        if (distance < reach || distance < reach * 3f + 0.5f && bearing > 1.2f && s.StageTime > 2f)
                        {
                            s.Stage = 1;
                            s.StageTime = 0f;
                        }
                    }
                }
                else
                {
                    // Both running: the one that's run longest turns round to chase.
                    if (other.Stage == 1 && other.Partner == self && s.StageTime > other.StageTime && s.StageTime > 0.3f)
                    {
                        s.Stage = 0;
                        s.StageTime = 0f;
                        return;
                    }
                    var flee = math.normalizesafe(-to, new float2(1f, 0f));
                    var side = new float2(-flee.y, flee.x) * math.sin(s.Clock * 1.3f + s.Seed % 11) * 0.6f;
                    // Don't run off: past a dozen metres from the chaser, circle back round; and keep the game near
                    // where it started (home, for animals that have one), turning back towards it further out.
                    var dir = distance > 12f ? new float2(-flee.y, flee.x) : flee + side;
                    var toCentre = s.Focus - s.Position.xz;
                    var wander = math.length(toCentre);
                    var range = s.Leash > 0f ? s.Leash * 0.6f : 12f;
                    // Out there the run is bent round towards the middle: its angle from the way in is narrowed, keeping
                    // the side it leans to (dead away from the middle, its own side). Not a blend of the two ways: with
                    // the chaser between it and the middle they point opposite, and a few metres past the range they
                    // cancelled, leaving the way to go to noise; it turned on the spot at full rate (Z-07).
                    if (wander > range)
                    {
                        var inward = toCentre / wander;
                        var pull = math.saturate((wander - range) / 6f);
                        var fled = math.normalizesafe(dir, flee);
                        var theta = math.atan2(inward.x * fled.y - inward.y * fled.x, math.dot(inward, fled));
                        var lean = math.abs(theta) > math.PI - 0.35f ? (s.Seed & 1u) == 0u ? 1f : -1f : math.sign(theta);
                        var angle = lean * math.min(math.abs(theta), math.lerp(math.PI, math.PI * 0.25f, pull));
                        math.sincos(angle, out var sn, out var cs);
                        dir = new float2(inward.x * cs - inward.y * sn, inward.x * sn + inward.y * cs);
                    }
                    // A run aimed at something in the way (water, a steep bank) turns off along it, its own side
                    // first: left to the steering, it was stopped, sent aside, then aimed back in again each frame
                    // (Z-07).
                    var aim = math.normalizesafe(dir, flee);
                    // The way to run swings round as fast as the chaser circles it: a chaser close by and running round
                    // it (or the weave across a stalled run) turned it on the spot at full rate (#12: songbird, marmot,
                    // goat 270 deg in 1-3 s at 0.1 m/s). So the run's aim turns at most 100 deg/s from last frame's.
                    var last = s.Target - s.Position.xz;
                    if (OldPlay.Data == 0 && s.StageTime > 0.1f && math.lengthsq(last) > 1f)
                    {
                        last = math.normalize(last);
                        var turn = math.atan2(last.x * aim.y - last.y * aim.x, math.dot(last, aim));
                        var most = math.radians(100f) * Dt;
                        if (math.abs(turn) > most)
                        {
                            math.sincos(math.sign(turn) * most, out var ls, out var lc);
                            aim = new float2(last.x * lc - last.y * ls, last.x * ls + last.y * lc);
                        }
                    }
                    var probe = 1.5f * s.Scale + 0.5f;
                    var land = b.Habitat == Habitat.Land;
                    if (!CaveWalker(s, ref b) && !Open(s.Position.xz, s.Position.xz + aim * probe, b.Habitat, land))
                    {
                        var own = (s.Seed & 1u) == 0u ? 1f : -1f;
                        for (int k = 1; k <= 6; k++)
                        {
                            math.sincos(own * (k % 2 == 1 ? 1f : -1f) * 0.8f * ((k + 1) / 2), out var ts, out var tc);
                            var tried = new float2(aim.x * tc - aim.y * ts, aim.x * ts + aim.y * tc);
                            if (Open(s.Position.xz, s.Position.xz + tried * probe, b.Habitat, land))
                            {
                                aim = tried;
                                break;
                            }
                        }
                    }
                    s.Target = s.Position.xz + aim * 8f;
                    targetSpeed = math.lerp(walk, run, distance > 12f ? 0.3f : 0.6f);
                }
            }

            /// <summary>Stalks low towards its prey, then sprints; gives up when it's caught up or run out of puff.</summary>
            void Hunt(ref CreatureState s, ref CreatureBlob b, ref Random rng,
                      ref float targetSpeed, ref float desiredLook, ref float headDown, ref bool moving)
            {
                s.Timer -= Dt;
                var found = Lookup.TryGetValue(s.Partner, out var p);
                // Eating what it caught: head down over the spot a while, then a long rest, its hunger met.
                if (s.Stage == HuntEat)
                {
                    headDown = 1f;
                    if (s.Timer <= 0f)
                    {
                        M.Hunger = math.max(0f, M.Hunger - 0.6f);
                        SetMode(ref s, CreatureMode.Rest);
                        s.Timer = rng.NextFloat(30f, 60f);
                    }
                    return;
                }
                // The pounce: a short lunge at it. On a hit the prey, seeing it (Pounced), is caught and gone; a miss,
                // or a hit it got clear of, and the hunter gives up, winded, as the prey runs on.
                if (s.Stage == HuntPounceHit || s.Stage == HuntPounceMiss)
                {
                    if (s.Stage == HuntPounceHit && (!found || States[p].Gone))
                    {
                        NextStage(ref s, HuntEat);
                        s.Timer = rng.NextFloat(8f, 15f);
                        s.Partner = Entity.Null;
                        return;
                    }
                    if (s.StageTime > 0.6f || !found)
                    {
                        Tired(ref s, ref rng);
                        return;
                    }
                    var at = States[p].Position.xz - s.Position.xz;
                    s.Target = States[p].Position.xz;
                    desiredLook = math.clamp(WrapAngle(math.atan2(at.x, at.y) - s.Yaw), -1f, 1f);
                    moving = true;
                    targetSpeed = b.RunSpeed * s.Scale * (s.StageTime < 0.35f ? 1.4f : 0.5f);
                    headDown = 0.4f;
                    return;
                }
                if (!found || s.Timer <= 0f)
                {
                    Tired(ref s, ref rng);
                    return;
                }
                var prey = States[p];
                var to = prey.Position.xz - s.Position.xz;
                var distance = math.length(to);
                if (distance > 60f || prey.Gone || prey.Mode == CreatureMode.Held)
                {
                    Tired(ref s, ref rng);
                    return;
                }
                // Within a leap of it: the pounce. Half of them land (none in a settlement, where the folk's animals
                // are safe: there it's always a miss).
                if (distance < b.BodyLength * s.Scale + 1.1f)
                {
                    var hit = rng.NextFloat() < 0.5f && SettlementOf(prey.Position.xz) < 0;
                    NextStage(ref s, hit ? HuntPounceHit : HuntPounceMiss);
                    return;
                }
                s.Target = prey.Position.xz;
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1f, 1f);
                moving = true;
                // Creeping up slow and low (prey notices a stalker only close: FindPredator), then the run, with a burst
                // of speed for its first few seconds (Z-53: so a chase closes in sight rather than trailing off).
                if (distance > 14f && s.Stage == 0)
                {
                    targetSpeed = b.WalkSpeed * s.Scale * 0.5f;
                    headDown = 0.35f;
                }
                else
                {
                    if (s.Stage == 0)
                        NextStage(ref s, 1);
                    targetSpeed = b.RunSpeed * s.Scale * (s.StageTime < 3.5f ? 1.25f : 1f);
                }
            }

            /// <summary>A land hunter's stages (CreatureMode.Hunt): 0 stalking, 1 running, then the pounce, landed or
            /// missed, and eating what it caught. (Fliers diving use 1-3 in Dive; folk hunters FolkHunting's.)</summary>
            const byte HuntPounceHit = 2, HuntEat = 3, HuntPounceMiss = 4;

            /// <summary>Whether a land hunter's pounce has landed on this animal (it is caught).</summary>
            bool Pounced(Entity self, in CreatureState s)
            {
                if (s.Mode == CreatureMode.Held || s.Holder != 0)
                    return false;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        ref var ob = ref Bodies[j].Blob.Value;
                        if (other.Mode == CreatureMode.Hunt && other.Partner == self && other.Stage == HuntPounceHit &&
                            ob.Habitat != Habitat.Flies && math.distance(other.Position.xz, s.Position.xz) < ob.BodyLength * other.Scale + 1.3f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>
            /// A rooted creature: the nearest thing within its reach (Flee Distance; half that asleep), the player or
            /// another animal, and now and then (every third of a second or so while it stays) it whips round and
            /// snaps at it, waking if it was asleep.
            /// </summary>
            void Snap(Entity self, ref CreatureState s, ref CreatureBlob b, float3 player, ref Random rng)
            {
                if (s.Action == IdleAction.Snap || b.FleeDistance <= 0f || rng.NextFloat() > Dt * 3f)
                    return;
                var reach = b.FleeDistance * s.Scale * (s.Mode == CreatureMode.Sleep ? 0.5f : 1f);
                var best = reach;
                var at = float2.zero;
                var found = false;
                var d = math.distance(player, s.Position);
                if (d < best)
                {
                    best = d;
                    at = player.xz;
                    found = true;
                }
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(reach / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || Bodies[j].Blob.Value.Rooted)
                            continue;
                        var e = math.distance(States[j].Position, s.Position);
                        if (e < best)
                        {
                            best = e;
                            at = States[j].Position.xz;
                            found = true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                if (!found)
                    return;
                if (s.Mode != CreatureMode.Idle)
                {
                    SetMode(ref s, CreatureMode.Idle);
                    s.Timer = rng.NextFloat(5f, 15f);
                }
                var to = at - s.Position.xz;
                if (math.lengthsq(to) > 1e-4f)
                    s.Yaw = math.atan2(to.x, to.y);
                s.Action = IdleAction.Snap;
                s.ActionTime = 0f;
                s.ActionLength = 0.7f;
            }

            void Tired(ref CreatureState s, ref Random rng)
            {
                M.Hunger = math.max(0f, M.Hunger - 0.4f);
                SetMode(ref s, CreatureMode.Rest);
                s.Timer = rng.NextFloat(15f, 40f);
            }
        }
    }
}
