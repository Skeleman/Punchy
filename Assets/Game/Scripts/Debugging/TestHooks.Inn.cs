using Punchy.Fauna;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Debugging
{
    /// <summary>Test hooks for the innkeeper (A-132): a live innkeeper at its town's inn Counter, and what it says to "work".</summary>
    public static partial class TestHooks
    {
        /// <summary>What InnkeeperAtCounter last saw: the innkeeper's talk dump and its answer to "work".</summary>
        public static string InnTrail;

        /// <summary>
        /// The nearest Counter anchor to the player and the nearest live innkeeper to it: [1 if a counter, 1 if an
        /// innkeeper, the counter's x, y, z, its facing x, z, the innkeeper's x, y, z, its distance from the counter,
        /// its mode, 1 if FolkConversation took it as a talker, 1 if its "work" answer is the innkeeper line, its stage
        /// (FolkJobs.Serving once at its post), its distance from its work spot]. The talk itself goes to InnTrail.
        /// </summary>
        public static float[] InnkeeperAtCounter()
        {
            var r = new float[16];
            InnTrail = "";
            if (!Source(out var source) || !source.Anchors.IsCreated)
                return r;
            ref var set = ref source.Anchors.Value;
            var at = (float3)PlayerPosition();
            var counter = -1;
            for (int a = 0; a < set.Anchors.Length; a++)
                if (set.Anchors[a].Kind == AnchorKind.Counter &&
                    (counter < 0 || math.distancesq(set.Anchors[a].Position, at) < math.distancesq(set.Anchors[counter].Position, at)))
                    counter = a;
            if (counter < 0)
                return r;
            var c = set.Anchors[counter];
            r[0] = 1f;
            r[2] = c.Position.x; r[3] = c.Position.y; r[4] = c.Position.z;
            r[5] = c.Facing.x; r[6] = c.Facing.y;
            var world = World.DefaultGameObjectInjectionWorld;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var best = Entity.Null;
            var bestD = float.MaxValue;
            foreach (var e in entities)
            {
                var s = EM.GetComponentData<CreatureState>(e);
                var d = math.distance(s.Position.xz, c.Position.xz);
                if (FolkJobs.Of(s) == FolkJob.Innkeeper && d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }
            if (best == Entity.Null)
                return r;
            var st = EM.GetComponentData<CreatureState>(best);
            r[1] = 1f;
            r[7] = st.Position.x; r[8] = st.Position.y; r[9] = st.Position.z;
            r[10] = bestD;
            r[11] = (float)st.Mode;
            r[14] = st.Stage;
            r[15] = math.distance(st.Position.xz, st.WorkSpot);
            if (FolkConversation.TryTalker(world, best, null, out var who))
            {
                r[12] = 1f;
                // (A third of answers may be a line any folk could say: ask a few times for the innkeeper's own.)
                InnTrail = $"{who.Species} #{who.Individual:X8} job {who.Job ?? who.Trade ?? "-"} town {who.HomeTown ?? "-"} mode {st.Mode} " +
                           $"{bestD:F1} m from the counter";
                // (The test's player may wear an animal body, which only gets to "meet": ask as a handed speaker would.)
                var asker = who;
                asker.Beast = false;
                for (int k = 0; k < 6 && r[13] == 0f; k++)
                {
                    var work = FolkConversation.Ask(asker, "work");
                    r[13] = work.Text != null && work.Text.StartsWith("Beds upstairs") ? 1f : 0f;
                    InnTrail += "\n  " + work;
                }
                FolkConversation.ForgetAsks();
            }
            return r;
        }
    }
}
