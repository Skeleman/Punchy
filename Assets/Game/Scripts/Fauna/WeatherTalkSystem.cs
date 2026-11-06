using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Folk grumble about the weather (A-71 row 4): a line from WeatherLines over the head of one of the folk near the
    /// player, by chance, as it sets off for shelter or ducks indoors, strikes a weather pose (panting, shivering,
    /// hunching, shielding its eyes, shaking off rain), on reaching shelter (relief), and now and then while it waits
    /// there. Lines go up through FolkTalkSystem.Shown, so they look like any other line. Each speaker waits a while
    /// before it speaks again, and one area (a 32 m cell) has at most two lines up at once and a gap between them, so a
    /// storm in town is a murmur, not a chorus. Animals don't speak; their poses (and sounds) are all they have.
    /// Works on every machine from what's synced (Mode, Stage, Action); a joined client fills the comfort grid itself
    /// (Comfort.Refresh) to know which complaint it is. Each machine picks its own lines; nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(Unity.Entities.PresentationSystemGroup))]
    [UpdateAfter(typeof(FolkTalkSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class WeatherTalkSystem : SystemBase
    {
        /// <summary>How near the player lines are said (m); how often to look (s); how long one speaker waits to speak
        /// again (s); an area's size (m), the most lines up in it at once, and the least time between two of its lines (s).</summary>
        const float Reach = 45f, Every = 0.2f, Again = 40f, Cell = 32f, CellGap = 2f;
        const int PerCell = 2;
        /// <summary>The chance of a line on setting off for shelter or striking a pose; on arriving; per look while waiting.</summary>
        const float ComplainChance = 0.6f, ReliefChance = 0.5f, WaitChance = 0.012f;

        /// <summary>Tests ("-weathertalk"): log each line said, with its area and how many are up there.</summary>
        public static bool LogLines;
        /// <summary>Lines said since start, and the most ever up at once in one area (tests).</summary>
        public static int Said, MostInCell;

        struct Seen { public CreatureMode Mode; public byte Stage; public IdleAction Action; }
        readonly Dictionary<Entity, Seen> seen = new Dictionary<Entity, Seen>();
        readonly Dictionary<Entity, float> nextSpeak = new Dictionary<Entity, float>();
        readonly Dictionary<int2, float> cellLast = new Dictionary<int2, float>();
        readonly List<float3> players = new List<float3>(1);
        EntityQuery query, playerQuery;
        FaunaDirector director;
        float nextLook;
        bool refreshGrid;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0xC1A7u);

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            // (Statics outlive a play with domain reload off.)
            Said = 0;
            MostInCell = 0;
        }

        protected override void OnUpdate()
        {
            var now = UnityEngine.Time.time;
            if (now < nextLook)
                return;
            nextLook = now + Every;
            var lines = WeatherLines.Current;
            if (lines == null || FolkTalkSystem.Quiet || playerQuery.CalculateEntityCount() != 1 ||
                !SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            if (director == null || UnityEngine.Time.frameCount % 120 == 0)
                director = Object.FindAnyObjectByType<FaunaDirector>();
            var player = playerQuery.GetSingleton<LocalTransform>().Position;
            // A joined client has no sim world to fill the comfort grid (FaunaDirector does that on the host): fill it
            // here around the local player (rate-limited inside), once we see nobody else does.
            if (Comfort.Grid.Data.Ready == 0)
                refreshGrid = true;
            if (refreshGrid)
            {
                players.Clear();
                players.Add(player);
                Comfort.Refresh(players, terrain);
            }
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            var hasKit = SystemAPI.TryGetSingletonBuffer<Punchy.Flora.KitBox>(out var kit, true) && kit.Length > 0;

            CreatureSnapshotSystem.Get(World, query, out var states, out var bodies, out var entities);
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                ref var b = ref bodies[i].Blob.Value;
                if (!FolkRaces.Has(b.Comfort.Race, RaceTrait.Folk) || !b.Comfort.Minds || !b.Upright || !b.Hands || b.Haunts)
                    continue;
                if (math.distancesq(s.Position, player) > Reach * Reach)
                {
                    seen.Remove(entities[i]);
                    continue;
                }
                var now_ = new Seen { Mode = s.Mode, Stage = s.Stage, Action = s.Action };
                var had = seen.TryGetValue(entities[i], out var was);
                seen[entities[i]] = now_;
                if (!had)
                    continue;

                // What just happened, if anything worth a line.
                string what = null;
                var chance = 0f;
                var posed = WeatherPose(s.Action);
                if (s.Mode == CreatureMode.Shelter && s.Stage >= 1 && was.Mode == CreatureMode.Shelter && was.Stage == 0)
                {
                    what = "arrived";
                    chance = ReliefChance;
                }
                else if (s.Mode == CreatureMode.Shelter && was.Mode != CreatureMode.Shelter ||
                         s.Mode == CreatureMode.Home && s.Stage < 2 && was.Mode != CreatureMode.Home ||
                         posed != ComfortKind.None && s.Action != was.Action)
                {
                    what = posed != ComfortKind.None && s.Action != was.Action ? s.Action.ToString().ToLowerInvariant() :
                           s.Mode == CreatureMode.Home ? "indoors" : "seeking";
                    chance = ComplainChance;
                }
                else if (s.Mode == CreatureMode.Shelter && s.Stage >= 1)
                {
                    what = "waiting";
                    chance = WaitChance;
                }
                if (what == null || !Speaks(s) || rng.NextFloat() >= chance)
                    continue;
                if (nextSpeak.TryGetValue(entities[i], out var next) && now < next)
                    continue;

                // The complaint: what the weather here does to this one (no cover counted: it's what drove it in), else
                // what its pose says.
                string kind;
                if (what == "arrived")
                    kind = WeatherLines.Relief;
                else
                {
                    var underground = s.Position.y < terrain.SampleHeight(s.Position.xz) - 3f;
                    var w = Comfort.WeatherAt(s.Position.xz);
                    Comfort.Pressure(in b.Comfort, s.Individual, in w, night, underground, ShelterNeed.None, out var felt);
                    if (felt == ComfortKind.None)
                        felt = posed;
                    if (felt == ComfortKind.None)
                        continue;
                    kind = felt.ToString();
                }

                // The area: at most two up at once, and not two in the same moment.
                var cell = (int2)math.floor(s.Position.xz / Cell);
                if (cellLast.TryGetValue(cell, out var last) && now - last < CellGap)
                    continue;
                var up = UpIn(cell, now);
                if (up >= PerCell)
                    continue;
                if (hasKit && FolkTalkSystem.Walled(kit, s, ref b, player))
                    continue;

                var demeanor = WeatherLines.Demeanor(s.Individual);
                var text = lines.Pick(kind, FolkRaces.ById(b.Comfort.Race)?.name, demeanor, ref rng);
                if (string.IsNullOrEmpty(text))
                    continue;
                nextSpeak[entities[i]] = now + Again * rng.NextFloat(0.8f, 1.4f);
                cellLast[cell] = now;
                FolkTalkSystem.Shown.Add(new FolkTalkSystem.Said
                {
                    Who = entities[i], Text = text, Head = s.Position + new float3(0f, b.StandHeight * s.Scale + 0.45f, 0f),
                    Start = now, Until = now + 3.5f + text.Length * 0.05f
                });
                Said++;
                MostInCell = math.max(MostInCell, up + 1);
                if (LogLines)
                    Debug.Log($"WeatherTalk: {director?.SpeciesAt(bodies[i].Species)?.name} ({b.Comfort.Race}, {demeanor}) {what} {kind} " +
                              $"cell ({cell.x},{cell.y}) [{up + 1} up in cell, most {MostInCell}]: {text}");
            }
            CreatureSnapshotSystem.Release(World, states, bodies, entities);
            FolkTalkLabels.Ensure();
            FolkTalkLabels.Show(FolkTalkSystem.Shown.Count > 0 || FolkTalkSystem.Bubbles.Count > 0);
            if (seen.Count > 512)
                seen.Clear();
            if (nextSpeak.Count > 512)
                nextSpeak.Clear();
            if (cellLast.Count > 256)
                cellLast.Clear();
        }

        /// <summary>Lines up now (any, weather or not) over heads in this area.</summary>
        static int UpIn(int2 cell, float now)
        {
            var n = 0;
            foreach (var said in FolkTalkSystem.Shown)
                if (now < said.Until && math.all((int2)math.floor(said.Head.xz / Cell) == cell))
                    n++;
            return n;
        }

        /// <summary>Awake, out and about or heading in: not asleep, fleeing, resting, a player's body or already indoors.</summary>
        static bool Speaks(in CreatureState s) =>
            s.Mode != CreatureMode.Puppet && s.Mode != CreatureMode.Sleep && s.Mode != CreatureMode.Flee && s.Mode != CreatureMode.Rest &&
            !(s.Mode == CreatureMode.Home && s.Stage >= 2);

        /// <summary>The complaint a weather pose shows (A-71 row 3), or None for any other action.</summary>
        static ComfortKind WeatherPose(IdleAction a)
        {
            switch (a)
            {
                case IdleAction.Pant: return ComfortKind.Hot;
                case IdleAction.Shiver: return ComfortKind.Cold;
                case IdleAction.Hunch: return ComfortKind.Storm;
                case IdleAction.ShieldEyes: return ComfortKind.Bright;
                case IdleAction.Shake: return ComfortKind.Rain;
                default: return ComfortKind.None;
            }
        }
    }
}
