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
    /// ghost field on CreatureGhost (a network table change, rule 11). The harsh-biome jobs (A-106) are the exception
    /// that needs no new field: each works at its own Stage (WorkStage), and a client reads the job back from that.
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
        // Turtle folk igloo jobs (A-103): fishing through the ice, cutting and carrying ice blocks, tending the
        // village's spirit fire (a conjured flame: no hearth prop yet), and an elder at its porch.
        IceFisher, IceCutter, FireTender, Elder,
        // Sea folk bed keepers (A-103 row 3): one tends a clam bed, one keeps the glow stones round the spire.
        ClamTender, GlowKeeper,
        // Harsh-biome work (A-106 row 1), from the settlement's biome's FolkWorkSet: sweeping ash off paths and doorsteps,
        // shovelling snow from a door, shovelling drifted sand clear, tying a roof down against the storm, mending
        // armour and tools at a bench.
        AshSweeper, SnowShoveller, SandClearer, Roofer, Mender,
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

        /// <summary>A-103: a turtle folk's igloo job from its task, for joined clients (Job is local, not a ghost field):
        /// fishing an ice fisher, chopping an ice cutter, tending a fire tender; None for any other task.</summary>
        public static FolkJob FromTurtleTask(CreatureTask task)
        {
            switch (task)
            {
                case CreatureTask.Fish: return FolkJob.IceFisher;
                case CreatureTask.Chop: return FolkJob.IceCutter;
                case CreatureTask.Tend: return FolkJob.FireTender;
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

        /// <summary>A-106: the task whose work loop a harsh-biome job uses (a mender hammers, the rest tend).</summary>
        public static CreatureTask TaskOf(FolkJob job) => job == FolkJob.Mender ? CreatureTask.Hammer : CreatureTask.Tend;

        /// <summary>
        /// A-106: the Stage a posted worker shows at its post. Job is local, so a harsh-biome job travels in the Stage it
        /// works at (the existing ghost field; W-57's hermit chores already do this): the hermit's tool codes, 3 broom,
        /// 4 shovel, 5 hammer, then 6 rope and 7 sand shovel. Every other job works at 1.
        /// </summary>
        public static byte WorkStage(FolkJob job)
        {
            switch (job)
            {
                case FolkJob.AshSweeper: return 3;
                case FolkJob.SnowShoveller: return 4;
                case FolkJob.Mender: return 5;
                case FolkJob.Roofer: return 6;
                case FolkJob.SandClearer: return 7;
                default: return 1;
            }
        }

        /// <summary>A-106: on a joined client, the harsh-biome job a worker's task and Stage at its post mean (WorkStage
        /// turned back), or None.</summary>
        public static FolkJob FromWorkStage(CreatureTask task, byte stage)
        {
            if (task == CreatureTask.Hammer)
                return stage == 5 ? FolkJob.Mender : FolkJob.None;
            if (task != CreatureTask.Tend)
                return FolkJob.None;
            return stage == 3 ? FolkJob.AshSweeper : stage == 4 ? FolkJob.SnowShoveller : stage == 6 ? FolkJob.Roofer :
                   stage == 7 ? FolkJob.SandClearer : FolkJob.None;
        }

        /// <summary>The job a bat folk would hold by each prop of its chamber (hut, rack, basket), and by its pool.</summary>
        public static FolkJob BatJob(BatFolkPropKind kind) =>
            kind == BatFolkPropKind.Hut ? FolkJob.RoostKeeper : kind == BatFolkPropKind.Rack ? FolkJob.Tanner : FolkJob.Forager;
    }
}
