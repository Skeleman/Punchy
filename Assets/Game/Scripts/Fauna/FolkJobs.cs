using Unity.Mathematics;
using Punchy.Terrain;

namespace Punchy.Fauna
{
    /// <summary>
    /// A folk's job (A-29): what it does all day and what it talks about. Most come straight from its CreatureTask (a
    /// smith hammers, a fisher fishes); the village jobs (baker, healer, herder, farmer, trader, guard, a village fisher)
    /// are handed by the host's FaunaDirector to free townsfolk, at a post by a matching anchor, and kept in
    /// CreatureState.Job. Local only, never sent: CreatureState.Job is not a ghost field, so a joined client sees the
    /// job its Task implies (a baker tends, so it reads as a well-keeper there). A synced version would need Job as a
    /// ghost field on CreatureGhost (a network table change, rule 11).
    /// </summary>
    public enum FolkJob : byte
    {
        None,
        // From the task alone.
        Smith, Woodcutter, Merchant, WellKeeper, Gatekeeper, Hunter, Guard, Fisher, Miner, Traveller, Hermit,
        // Handed out in villages (A-29).
        Baker, Healer, Herder, Farmer, Trader,
        // Bat folk chamber jobs (per chamber from its props), taken by the chamber folk (the blind cave pygmies, #10).
        RoostKeeper, Tanner, Forager, PoolFisher,
    }

    /// <summary>A folk's temper, for flavouring its lines: from its Individual hash, so every machine agrees.</summary>
    public enum FolkMood : byte { Plain, Cheerful, Grumpy }

    public static class FolkJobs
    {
        /// <summary>The job a task implies (None for an untasked folk).</summary>
        public static FolkJob FromTask(CreatureTask task)
        {
            switch (task)
            {
                case CreatureTask.Hammer: return FolkJob.Smith;
                case CreatureTask.Chop: return FolkJob.Woodcutter;
                case CreatureTask.Gesture: return FolkJob.Merchant;
                case CreatureTask.Tend: return FolkJob.WellKeeper;
                case CreatureTask.Watch: return FolkJob.Gatekeeper;
                case CreatureTask.Hunt: return FolkJob.Hunter;
                case CreatureTask.Patrol: return FolkJob.Guard;
                case CreatureTask.Fish: return FolkJob.Fisher;
                case CreatureTask.Mine: return FolkJob.Miner;
                case CreatureTask.Travel: case CreatureTask.Camper: return FolkJob.Traveller;
                case CreatureTask.Hermit: return FolkJob.Hermit;
                default: return FolkJob.None;
            }
        }

        /// <summary>This folk's job: the one it was handed, else the one its task implies.</summary>
        public static FolkJob Of(in CreatureState s) => s.Job != 0 ? (FolkJob)s.Job : FromTask(s.Task);

        /// <summary>The job's name as dialogue.json tags it ("Baker"), or null for none.</summary>
        public static string Name(FolkJob job) => job == FolkJob.None ? null : job.ToString();

        /// <summary>About a third cheerful, two in five plain, a quarter grumpy.</summary>
        public static FolkMood Mood(uint individual)
        {
            var h = BiomeSampling.Scramble(individual ^ 0x6A09E667u) % 100u;
            return h < 35u ? FolkMood.Cheerful : h < 75u ? FolkMood.Plain : FolkMood.Grumpy;
        }

        /// <summary>
        /// The village posts, in the order they're handed out: an anchor kind near the landmark, the job and the task
        /// whose work loop it uses (go to the post, work there with that pose, breaks, indoors at night). The last two
        /// fields: how many free folk the village needs before it gets this one (bigger villages get more), and how far
        /// past the landmark's edge the anchor may be.
        /// </summary>
        public static readonly (AnchorKind Kind, FolkJob Job, CreatureTask Task, int MinFree, float Beyond)[] VillagePosts =
        {
            (AnchorKind.Hearth, FolkJob.Baker, CreatureTask.Tend, 2, 0f),
            (AnchorKind.Yard, FolkJob.Herder, CreatureTask.Tend, 2, 0f),
            (AnchorKind.Altar, FolkJob.Healer, CreatureTask.Tend, 3, 0f),
            (AnchorKind.Bench, FolkJob.Healer, CreatureTask.Tend, 3, 0f),
            (AnchorKind.Pool, FolkJob.Fisher, CreatureTask.Fish, 3, 60f),
            (AnchorKind.Yard, FolkJob.Farmer, CreatureTask.Chop, 4, 0f),
            (AnchorKind.Stall, FolkJob.Trader, CreatureTask.Gesture, 4, 0f),
            (AnchorKind.Gate, FolkJob.Guard, CreatureTask.Patrol, 5, 0f),
        };

        /// <summary>The job a bat folk would hold by each prop of its chamber (hut, rack, basket), and by its pool.</summary>
        public static FolkJob BatJob(BatFolkPropKind kind) =>
            kind == BatFolkPropKind.Hut ? FolkJob.RoostKeeper : kind == BatFolkPropKind.Rack ? FolkJob.Tanner : FolkJob.Forager;
    }
}
