using Punchy.Player;
using Unity.Entities;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-83 (#62): where the fauna's per-frame work on the simulation world runs, first in its simulation group: ahead
    /// of the player's input and movement and of the brain, which see this frame's clock, spawns and despawns. It used
    /// to run from FaunaDirector's Update, which comes before the simulation group only by the player loop's order.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(PlayerInputSystem))]
    public partial class FaunaSpawnGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A-83 (#62): runs FaunaDirector's population work (<see cref="FaunaDirector.UpdatePopulation(World)"/>) in the
    /// world that simulates the animals. The director's Update still builds, keeps the player's body and the ghosts'
    /// bodies, and says when the work is due; this only gives it a written-down place in the frame. A server group
    /// that ticks twice in a frame does the work once; one that skips a frame does it next time, with both frames' time.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FaunaSpawnGroup))]
    public partial class FaunaPopulationSystem : SystemBase
    {
        FaunaDirector director;
        float nextFind;

        protected override void OnUpdate()
        {
            if (director == null)
            {
                // (Looked for once a second at most: scenes without animals have no director.)
                if (UnityEngine.Time.realtimeSinceStartup < nextFind)
                    return;
                nextFind = UnityEngine.Time.realtimeSinceStartup + 1f;
                director = Object.FindAnyObjectByType<FaunaDirector>();
                if (director == null)
                    return;
            }
            if (director.isActiveAndEnabled)
                director.UpdatePopulation(World);
        }
    }
}
